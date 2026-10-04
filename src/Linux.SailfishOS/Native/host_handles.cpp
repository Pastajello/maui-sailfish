// host_handles.cpp — object handles: find, properties (set/apply/get, the generic keys the shim applies itself), geometry, parent, destroy.

#include "host_internal.h"

using namespace sfhost;

extern "C" {

long long sailfish_host_find_object(const char *object_name)
{
    if (!object_name || !object_name[0] || g.shutdown)
        return 0;
    ++g.find_objects;
    if (!g.root) {
        set_error("no QML root object (call load/load_window first)");
        return 0;
    }
    QObject *obj = g.root->findChild<QObject *>(QString::fromUtf8(object_name));
    if (!obj) {
        set_error(std::string("object not found: ") + object_name);
        return 0;
    }
    return register_handle(obj);
}

// Qt 5.6 reparents ListView delegates visually only, so findChild misses them; BFS the
// childItems instead, like __mauiFindByName on the QML side.
long long sailfish_host_find_visual(long long parent, const char *object_name)
{
    if (!object_name || !object_name[0] || g.shutdown)
        return 0;
    ++g.find_objects;
    QObject *base = parent ? resolve_handle(parent) : static_cast<QObject *>(g.root);
    if (!base) {
        set_error(parent ? "find_visual: dead or unknown parent handle"
                         : "no QML root object (call load/load_window first)");
        return 0;
    }
    QQuickItem *rootItem = qobject_cast<QQuickItem *>(base);
    if (!rootItem) {
        set_error("find_visual: parent is not a QQuickItem");
        return 0;
    }
    const QString name = QString::fromUtf8(object_name);
    QList<QQuickItem *> queue;
    queue.append(rootItem);
    while (!queue.isEmpty()) {
        QQuickItem *cur = queue.takeFirst();
        if (cur->objectName() == name)
            return register_handle(cur);
        queue.append(cur->childItems());
    }
    set_error(std::string("object not found visually: ") + object_name);
    return 0;
}

// Qt 5.6 QML font has no absolute letter spacing (setLetterSpacing hardcodes
// PercentageSpacing), but MAUI CharacterSpacing is absolute device px as measured by
// measure_text. So "mauiLetterSpacing" edits the QFont natively through QVariant.
static void apply_letter_spacing_px(QObject *obj, qreal px)
{
    // Adapters without their own "font" (Silica Button, ValueButton, RadioButton) point
    // at the label via mauiTextItem.
    if (!obj->property("font").isValid()) {
        QObject *target = qvariant_cast<QObject *>(obj->property("mauiTextItem"));
        if (!target)
            return;
        obj = target;
    }
    const QVariant fv = obj->property("font");
    if (!fv.isValid() || !fv.canConvert<QFont>())
        return;
    QFont f = qvariant_cast<QFont>(fv);
    f.setLetterSpacing(QFont::AbsoluteSpacing, px);
    obj->setProperty("font", QVariant::fromValue(f));
    // Log only when tracking is requested or the value did not survive QQuickText::setFont.
    const QFont after = qvariant_cast<QFont>(obj->property("font"));
    if (px > 0 || qAbs(after.letterSpacing() - px) > 0.01)
        log_line(0, QStringLiteral("letter-spacing: wrote px=%1 -> font.letterSpacing=%2 type=%3")
                        .arg(px, 0, 'f', 2)
                        .arg(after.letterSpacing(), 0, 'f', 2)
                        .arg(static_cast<int>(after.letterSpacingType())));
}

static bool apply_generic_prop(QObject *obj, const QByteArray &name, const QJsonValue &value);

int sailfish_host_set_property(long long handle, const char *name, const char *value_json)
{
    if (!name || !name[0] || g.shutdown)
        return fail_args("sailfish_host_set_property");
    ++g.property_sets;
    QObject *obj = require_handle(handle);
    if (!obj)
        return SFHOST_E_DEAD_HANDLE;
    QJsonValue json;
    if (!json_parse_value(value_json, &json)) {
        set_error("property value must be valid JSON");
        return -1;
    }
    if (apply_generic_prop(obj, QByteArray(name), json))
        return 0;
    QVariant value;
    if (!json_value_to_variant(json, obj, name, &value))
        return -1;
    if (!obj->setProperty(name, value)) {
        set_error(std::string("no such property: ") + name);
        return -2;
    }
    if (qstrcmp(name, "mauiLetterSpacing") == 0)
        apply_letter_spacing_px(obj, value.toDouble());
    return 0;
}

// Generic MAUI view properties for adapters that do not declare them: mauiBackgroundFill
// (lazy child Rectangle), mauiAccessibleName/Description (attached Accessible) and
// mauiAutomationId (dynamic property). Adapters that declare them get a plain setProperty.
static void apply_background_fill(QObject *obj, const QColor &color)
{
    QQuickItem *item = qobject_cast<QQuickItem *>(obj);
    if (!item)
        return;
    QQuickItem *fill = nullptr;
    const QList<QQuickItem *> kids = item->childItems();
    for (QQuickItem *k : kids)
        if (k->objectName() == QLatin1String("mauiBackgroundFill")) {
            fill = k;
            break;
        }
    if (!fill) {
        if (color.alpha() == 0)
            return;
        QQmlEngine *engine = qmlEngine(obj);
        if (!engine)
            return;
        // Engine-owned and weakly held: a component of a destroyed engine reads as null and is made again.
        static QHash<QQmlEngine *, QPointer<QQmlComponent>> components;
        QQmlComponent *component = components.value(engine);
        if (!component) {
            component = new QQmlComponent(engine, engine);
            component->setData("import QtQuick 2.6\nRectangle { objectName: \"mauiBackgroundFill\"; anchors.fill: parent; z: -1000 }\n", QUrl());
            components.insert(engine, component);
        }
        QObject *created = component->beginCreate(qmlContext(obj) ? qmlContext(obj) : engine->rootContext());
        fill = qobject_cast<QQuickItem *>(created);
        if (!fill) {
            delete created;
            log_line(1, QStringLiteral("background fill: create failed: %1").arg(component->errorString()));
            return;
        }
        fill->setParent(item);
        fill->setParentItem(item);
        component->completeCreate();
    }
    fill->setProperty("color", color);
    fill->setVisible(color.alpha() > 0);
}

// Generic MAUI Shadow + Clip: the item's layer.effect is qml/effects/MauiLayerEffect.qml with
// the spec baked in, so it follows the item's geometry and visibility. The layer is only ours
// while a spec is set.
//   mauiLayerShadow: "" | "#AARRGGBB|radiusPx|offsetXPx|offsetYPx"
//   mauiLayerClip:   "" | {"ops":[pathops.js ops, element space px],"eo":0|1}
static QString qml_root_url(QObject *obj)
{
    QQmlContext *ctx = qmlContext(obj);
    const QString base = ctx ? ctx->baseUrl().toString() : QString();
    const int at = base.lastIndexOf(QLatin1String("/qml/"));
    return at >= 0 ? base.left(at + 5) : QString();
}

static void apply_layer_effect(QObject *obj)
{
    QQuickItem *item = qobject_cast<QQuickItem *>(obj);
    QQmlEngine *engine = qmlEngine(obj);
    if (!item || !engine)
        return;
    const QString shadow = item->property("__mauiShadowSpec").toString();
    const QString clip = item->property("__mauiClipSpec").toString();
    // Created by MauiShell.mauiApplyLayerEffect (Qt.createQmlObject): QQuickItemLayer needs
    // the component's creationContext(), which a C++-built QQmlComponent lacks (SIGSEGV).
    auto setter = [item](const QString &text, const QString &url) -> QObject * {
        QVariant ret;
        if (!g.root || !QMetaObject::invokeMethod(g.root, "mauiApplyLayerEffect", Q_RETURN_ARG(QVariant, ret),
                                                  Q_ARG(QVariant, QVariant::fromValue<QObject *>(item)),
                                                  Q_ARG(QVariant, text), Q_ARG(QVariant, url))) {
            log_line(1, QStringLiteral("layer effect: the shell has no mauiApplyLayerEffect"));
            return nullptr;
        }
        return ret.value<QObject *>();
    };
    auto dropPrevious = [item]() {
        if (QObject *old = item->property("__mauiLayerComponent").value<QObject *>())
            old->deleteLater();
        item->setProperty("__mauiLayerComponent", QVariant());
    };
    if (shadow.isEmpty() && clip.isEmpty()) {
        if (item->property("__mauiLayerOwned").toBool()) {
            setter(QString(), QString());
            item->setProperty("__mauiLayerOwned", false);
            dropPrevious();
        }
        return;
    }
    QString body;
    const QStringList sh = shadow.split(QLatin1Char('|'));
    if (sh.size() == 4 && sh.at(0).size() == 9 && sh.at(0).startsWith(QLatin1Char('#'))) {
        const QColor c = parse_color(sh.at(0));
        if (c.isValid() && c.alpha() > 0)
            body += QStringLiteral("shadowColor: Qt.rgba(%1,%2,%3,%4); shadowRadius: %5; shadowX: %6; shadowY: %7; ")
                        .arg(c.redF()).arg(c.greenF()).arg(c.blueF()).arg(c.alphaF())
                        .arg(sh.at(1).toDouble()).arg(sh.at(2).toDouble()).arg(sh.at(3).toDouble());
    }
    if (!clip.isEmpty()) {
        const QJsonObject spec = QJsonDocument::fromJson(clip.toUtf8()).object();
        const QJsonArray ops = spec.value(QStringLiteral("ops")).toArray();
        if (!ops.isEmpty())
            body += QStringLiteral("clipOps: %1; clipEvenOdd: %2; ")
                        .arg(QString::fromUtf8(QJsonDocument(ops).toJson(QJsonDocument::Compact)))
                        .arg(spec.value(QStringLiteral("eo")).toInt(1) != 0 ? QStringLiteral("true") : QStringLiteral("false"));
    }
    if (body.isEmpty()) {
        item->setProperty("__mauiShadowSpec", QString());
        item->setProperty("__mauiClipSpec", QString());
        apply_layer_effect(obj);
        return;
    }
    const QString root = qml_root_url(obj);
    if (root.isEmpty()) {
        log_line(1, QStringLiteral("layer effect: no qml root for %1").arg(obj->objectName()));
        return;
    }
    const QString text = QStringLiteral("import QtQuick 2.6\nComponent { MauiLayerEffect { %1} }\n").arg(body);
    // A URL inside effects/ makes MauiLayerEffect.qml an implicit import.
    QObject *created = setter(text, root + QStringLiteral("effects/MauiLayerEffectSpec.qml"));
    if (!created) {
        log_line(1, QStringLiteral("layer effect: component creation failed for %1").arg(obj->objectName()));
        return;
    }
    dropPrevious();
    item->setProperty("__mauiLayerComponent", QVariant::fromValue(created));
    item->setProperty("__mauiLayerOwned", true);
}

// mauiMatrix: the host's 3D / non-uniform transform (QtHostVisualState.HostMatrix), 16 row-major numbers; anything else
// is the identity. One Matrix4x4 per item, appended after the adapter's own transforms (ProgressBar/Slider flip for RTL),
// and kept at the identity once created, since Qt 5.6 cannot remove one entry of the transform list.
static void apply_item_matrix(QObject *obj, const QJsonValue &value)
{
    QQuickItem *item = qobject_cast<QQuickItem *>(obj);
    if (!item)
        return;
    const QJsonArray a = value.toArray();
    const bool identity = a.size() != 16;
    QObject *transform = item->property("__mauiMatrix").value<QObject *>();
    if (!transform) {
        if (identity)
            return;
        QQmlEngine *engine = qmlEngine(item) ? qmlEngine(item) : current_engine();
        if (!engine)
            return;
        static QHash<QQmlEngine *, QPointer<QQmlComponent>> components;   // engine-owned, weakly held
        QQmlComponent *component = components.value(engine);
        if (!component) {
            component = new QQmlComponent(engine, engine);
            component->setData("import QtQuick 2.6\nMatrix4x4 {}\n", QUrl());
            components.insert(engine, component);
        }
        transform = component->create();
        if (!transform) {
            log_line(1, QStringLiteral("matrix: Matrix4x4 creation failed: %1").arg(component->errorString()));
            return;
        }
        transform->setParent(item);
        QQmlListReference list(item, "transform", engine);
        if (!list.canAppend() || !list.append(transform)) {
            log_line(1, QStringLiteral("matrix: cannot append to %1.transform").arg(item->objectName()));
            delete transform;
            return;
        }
        item->setProperty("__mauiMatrix", QVariant::fromValue(transform));
    }
    QMatrix4x4 m;
    if (!identity) {
        float v[16];
        for (int i = 0; i < 16; ++i)
            v[i] = static_cast<float>(a.at(i).toDouble());
        m = QMatrix4x4(v);
    }
    transform->setProperty("matrix", QVariant::fromValue(m));
}

static bool apply_generic_prop(QObject *obj, const QByteArray &name, const QJsonValue &value)
{
    if (name == "mauiMatrix" && obj->metaObject()->indexOfProperty("mauiMatrix") < 0) {
        apply_item_matrix(obj, value);
        return true;
    }
    const bool generic = name == "mauiBackgroundFill" || name == "mauiAccessibleName" ||
                         name == "mauiAccessibleDescription" || name == "mauiAutomationId" ||
                         name == "mauiLayerShadow" || name == "mauiLayerClip" ||
                         name == "mauiAccessibleRole" || name == "mauiAccessibleIgnored" ||
                         name == "mauiMirrored";
    if (!generic || obj->metaObject()->indexOfProperty(name.constData()) >= 0)
        return false;
    const QString text = value.toString();
    if (name == "mauiBackgroundFill") {
        const QColor color = parse_color(text);
        if (!color.isValid() && !value.isNull())
            log_line(1, QStringLiteral("background fill: unparsable color '%1' (json type %2) on %3")
                            .arg(text).arg(static_cast<int>(value.type())).arg(obj->objectName()));
        apply_background_fill(obj, color.isValid() ? color : QColor(Qt::transparent));
    } else if (name == "mauiLayerShadow" || name == "mauiLayerClip") {
        const QByteArray slot = name == "mauiLayerShadow" ? QByteArrayLiteral("__mauiShadowSpec") : QByteArrayLiteral("__mauiClipSpec");
        if (obj->property(slot.constData()).toString() != text) {
            obj->setProperty(slot.constData(), text);
            apply_layer_effect(obj);
        }
    } else if (name == "mauiAutomationId") {
        obj->setProperty("mauiAutomationId", text);   // dynamic property
    } else if (name == "mauiAccessibleRole") {
        // "heading" (SemanticProperties.HeadingLevel; Qt has no levels) or "" = the item's own role, kept
        // aside on the first override.
        const QQmlProperty prop(obj, QStringLiteral("Accessible.role"), qmlContext(obj));
        if (!prop.isValid()) {
            log_line(1, QStringLiteral("accessible: role not resolvable on %1").arg(obj->metaObject()->className()));
        } else {
            if (!obj->property("__mauiAccessibleRole0").isValid())
                obj->setProperty("__mauiAccessibleRole0", prop.read().toInt());
            prop.write(text == QLatin1String("heading") ? int(QAccessible::Heading)
                                                        : obj->property("__mauiAccessibleRole0").toInt());
        }
    } else if (name == "mauiAccessibleIgnored") {
        // AutomationProperties.IsInAccessibleTree=false / ExcludedWithChildren (resolved per host managed-side).
        const QQmlProperty prop(obj, QStringLiteral("Accessible.ignored"), qmlContext(obj));
        if (prop.isValid())
            prop.write(value.toBool());
        else
            log_line(1, QStringLiteral("accessible: ignored not resolvable on %1").arg(obj->metaObject()->className()));
    } else if (name == "mauiMirrored") {
        // FlowDirection RTL on a leaf control: "on" mirrors the Silica internals (anchors, positioners, text
        // alignment); "off"/"" is explicit so a mirrored ancestor's inheritance stops at every MAUI host.
        const QQmlProperty enabled(obj, QStringLiteral("LayoutMirroring.enabled"), qmlContext(obj));
        const QQmlProperty inherit(obj, QStringLiteral("LayoutMirroring.childrenInherit"), qmlContext(obj));
        if (enabled.isValid() && inherit.isValid()) {
            enabled.write(text == QLatin1String("on"));
            inherit.write(true);
        } else {
            log_line(1, QStringLiteral("mirroring: LayoutMirroring not resolvable on %1").arg(obj->metaObject()->className()));
        }
    } else {
        const QQmlProperty prop(obj, name == "mauiAccessibleName" ? QStringLiteral("Accessible.name")
                                                                 : QStringLiteral("Accessible.description"),
                                qmlContext(obj));
        if (prop.isValid())
            prop.write(text);
        else
            log_line(1, QStringLiteral("accessible: %1 not resolvable on %2").arg(QString::fromUtf8(name), obj->metaObject()->className()));
    }
    return true;
}

int sailfish_host_apply_props(long long handle, const char *props_json)
{
    if (!props_json || !props_json[0] || g.shutdown)
        return fail_args("sailfish_host_apply_props");
    ++g.props_batches;
    QObject *obj = require_handle(handle);
    if (!obj)
        return SFHOST_E_DEAD_HANDLE;
    QJsonParseError perr;
    const QJsonDocument doc = QJsonDocument::fromJson(QByteArray(props_json), &perr);
    if (perr.error != QJsonParseError::NoError || !doc.isArray()) {
        set_error("apply_props: expected an ordered JSON array of {name,value}");
        return -1;
    }
    // An array keeps order (unlike QJsonObject): the mauiApplying true...false envelope must
    // be applied in sequence.
    int failed = 0;
    std::string details;
    bool hasSpacing = false;
    qreal spacingPx = 0;
    const QJsonArray arr = doc.array();
    for (int i = 0; i < arr.size(); ++i) {
        const QJsonObject o = arr.at(i).toObject();
        const QByteArray name = o.value(QStringLiteral("name")).toString().toUtf8();
        if (name.isEmpty()) {
            ++failed;
            details += "<empty>; ";
            continue;
        }
        if (apply_generic_prop(obj, name, o.value(QStringLiteral("value"))))
            continue;
        QVariant value;
        if (!json_value_to_variant(o.value(QStringLiteral("value")), obj, name.constData(), &value)) {
            ++failed;
            details += std::string(name.constData()) + "(convert); ";
            continue;
        }
        if (!obj->setProperty(name.constData(), value)) {
            ++failed;
            details += std::string(name.constData()) + "(no such property); ";
        } else if (name == QByteArrayLiteral("mauiLetterSpacing")) {
            apply_letter_spacing_px(obj, value.toDouble());
            spacingPx = value.toDouble();
            hasSpacing = true;
        }
    }
    // A later font.* write in the same batch can clobber the native letter spacing, so
    // re-assert it once at the end.
    if (hasSpacing) {
        const QVariant cv = obj->property("font");
        if (cv.isValid() && cv.canConvert<QFont>()) {
            const qreal got = qvariant_cast<QFont>(cv).letterSpacing();
            if (qAbs(got - spacingPx) > 0.01) {
                log_line(0, QStringLiteral("letter-spacing: clobbered mid-batch (%1 != %2) — re-applying")
                                .arg(got, 0, 'f', 2)
                                .arg(spacingPx, 0, 'f', 2));
                apply_letter_spacing_px(obj, spacingPx);
            }
        }
    }
    if (failed)
        set_error("apply_props failures: " + details);
    g.props_applied += arr.size() - failed;
    return failed;
}

int sailfish_host_get_property(long long handle, const char *name, char *out, int cap)
{
    if (out && cap > 0)
        out[0] = '\0';
    if (!name || !name[0] || g.shutdown)
        return fail_args("sailfish_host_get_property");
    QObject *obj = require_handle(handle);
    if (!obj)
        return SFHOST_E_DEAD_HANDLE;
    // Dotted names ("font.bold") are QML group sub-properties that only QQmlProperty reads.
    QVariant value;
    if (strchr(name, '.') != nullptr) {
        // With the object's QML context it also resolves attached ones ("Accessible.name").
        const QQmlProperty prop(obj, QString::fromUtf8(name), qmlContext(obj));
        if (prop.isValid() && prop.isProperty()) {
            value = prop.read();
        } else {
            // Attached types outside the context imports (Silica EnterKey): the attached
            // object is a QObject child whose class name ends with the type name.
            const QString full = QString::fromUtf8(name);
            const int dot = full.indexOf(QLatin1Char('.'));
            const QString type = full.left(dot);
            const QByteArray member = full.mid(dot + 1).toUtf8();
            const QList<QObject *> all = obj->findChildren<QObject *>();
            for (QObject *child : all) {
                if (QString::fromLatin1(child->metaObject()->className()).endsWith(type)) {
                    value = child->property(member.constData());
                    break;
                }
            }
            if (!value.isValid()) {
                set_error(std::string("no such property: ") + name);
                return -2;
            }
        }
    } else {
        value = obj->property(name);
    }
    if (!value.isValid()) {
        set_error(std::string("no such property: ") + name);
        return -2;
    }
    QString text;
    switch (static_cast<int>(value.type())) {
    case QVariant::Bool:    text = value.toBool() ? QStringLiteral("true") : QStringLiteral("false"); break;
    case QVariant::Int:     text = QString::number(value.toInt()); break;
    case QVariant::LongLong: text = QString::number(value.toLongLong()); break;
    case QVariant::Double:  text = QString::number(value.toDouble(), 'g', 17); break;
    /* Readable values for diagnostics. */
    case QVariant::Color:   text = value.value<QColor>().name(QColor::HexArgb); break;
    case QVariant::RectF: {
        const QRectF r = value.toRectF();
        text = QString::number(r.x(), 'g', 17) + QLatin1Char(',') + QString::number(r.y(), 'g', 17)
             + QLatin1Char(',') + QString::number(r.width(), 'g', 17)
             + QLatin1Char(',') + QString::number(r.height(), 'g', 17);
        break;
    }
    case QVariant::PointF: {
        const QPointF p = value.toPointF();
        text = QString::number(p.x(), 'g', 17) + QLatin1Char(',') + QString::number(p.y(), 'g', 17);
        break;
    }
    case QVariant::SizeF: {
        const QSizeF s = value.toSizeF();
        text = QString::number(s.width(), 'g', 17) + QLatin1Char(',') + QString::number(s.height(), 'g', 17);
        break;
    }
    default:                text = value.toString(); break;
    }
    const QByteArray utf = text.toUtf8();
    copy_out(utf, out, cap);
    return utf.size();
}

int sailfish_host_item_geometry(long long handle, double *x, double *y, double *w, double *h)
{
    if (!x || !y || !w || !h || g.shutdown)
        return fail_args("sailfish_host_item_geometry");
    ++g.geometry_reads;
    QObject *obj = require_handle(handle);
    if (!obj)
        return SFHOST_E_DEAD_HANDLE;
    QQuickItem *item = qobject_cast<QQuickItem *>(obj);
    if (!item) {
        set_error("handle is not a QQuickItem");
        return -2;
    }
    const QPointF scene = item->mapToScene(QPointF(0, 0));
    *x = scene.x();
    *y = scene.y();
    *w = item->width();
    *h = item->height();
    return 0;
}

int sailfish_host_set_parent_item(long long handle, long long parent)
{
    if (g.shutdown)
        return fail_args("sailfish_host_set_parent_item");
    QObject *obj = resolve_handle(handle);
    QObject *parentObj = resolve_handle(parent);
    if (!obj || !parentObj) {
        set_error(QStringLiteral("dead or unknown object handle %1/%2").arg(handle).arg(parent).toUtf8().constData());
        return -3;
    }
    QQuickItem *item = qobject_cast<QQuickItem *>(obj);
    QQuickItem *parentItem = qobject_cast<QQuickItem *>(parentObj);
    if (!item || !parentItem) {
        set_error("handle is not a QQuickItem");
        return -2;
    }
    if (item->parentItem() != parentItem)
        item->setParentItem(parentItem);
    return 0;
}

// Values are already in Qt scene units; nothing is scaled here. handle may be a number or a
// string (a 64-bit pointer does not survive a double). Entry order does not matter.
int sailfish_host_apply_geometry(const char *geo_json)
{
    if (!geo_json || !geo_json[0] || g.shutdown)
        return fail_args("sailfish_host_apply_geometry");
    QJsonParseError perr;
    const QJsonDocument doc = QJsonDocument::fromJson(QByteArray(geo_json), &perr);
    if (perr.error != QJsonParseError::NoError || !doc.isArray()) {
        set_error("apply_geometry: expected a JSON array of {handle,x,y,w,h,vis}");
        return -1;
    }
    int failed = 0;
    std::string details;
    const QJsonArray arr = doc.array();
    ++g.geometry_batches;
    g.geometry_entries += arr.size();
    for (int i = 0; i < arr.size(); ++i) {
        const QJsonObject o = arr.at(i).toObject();
        const QJsonValue hv = o.value(QStringLiteral("handle"));
        const long long handle = hv.isString()
            ? hv.toString().toLongLong()
            : static_cast<long long>(hv.toDouble());
        QObject *obj = resolve_handle(handle);
        QQuickItem *item = obj ? qobject_cast<QQuickItem *>(obj) : nullptr;
        if (!item) {
            ++failed;
            details += (obj ? std::string("not an item: ") : std::string("dead handle: "))
                     + std::to_string(handle) + "; ";
            continue;
        }
        // Children of the page canvas ("mauiCanvas") carry canvas coordinates, which exclude
        // the page scroll, and apply as-is: mapping them from the scene would bake in the
        // flickable's contentY. Delegate/slot children carry scene coordinates and are mapped
        // through the parent, so they keep their offset while it scrolls.
        const QPointF given(o.value(QStringLiteral("x")).toDouble(),
                            o.value(QStringLiteral("y")).toDouble());
        // Entries flagged "local" are already relative to the parent host (nested page hosts).
        QQuickItem *parent = item->parentItem();
        const bool local = o.value(QStringLiteral("local")).toInt(0) != 0;
        const QPointF pos = !parent || local
            ? given
            : parent->objectName() == QLatin1String("mauiCanvas")
                ? given
                : parent->mapFromScene(given);
        item->setX(pos.x());
        item->setY(pos.y());
        item->setWidth(o.value(QStringLiteral("w")).toDouble());
        item->setHeight(o.value(QStringLiteral("h")).toDouble());
        item->setVisible(o.value(QStringLiteral("vis")).toInt(1) != 0);
    }
    if (failed)
        set_error("apply_geometry failures: " + details);
    return failed;
}

void sailfish_host_destroy_object(long long handle)
{
    if (g.shutdown)
        return;
    ++g.destroys;
    auto it = g.objects.find(reinterpret_cast<void *>(static_cast<qintptr>(handle)));
    if (it == g.objects.end())
        return; // unknown handle
    QObject *obj = it.value().data();
    g.objects.erase(it);
    // QML may already have destroyed it (null QPointer); then only unregister.
    if (obj) {
        // deleteLater only runs on the next loop pass, while a create in the same tick may
        // reuse the objectName (collection rows recreate host ids), so hide the dying object
        // from name lookups right away.
        obj->setObjectName(QString());
        if (auto *item = qobject_cast<QQuickItem *>(obj)) {
            item->setVisible(false);
            item->setParentItem(nullptr);
        }
        obj->deleteLater();
    }
}

} // extern "C"
