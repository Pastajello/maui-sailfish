// host_core.cpp — the host state and the helpers every export uses: errors and logging, the window and engine,
// handles, JSON ↔ QVariant, JS evaluation and the QML event drain, the HTTP cache, loading and teardown.

#include "host_internal.h"

namespace sfhost {

HostState g;

void set_error(std::string text)
{
    std::lock_guard<std::mutex> lock(g.error_lock);
    g.error = std::move(text);
}

std::string error_text()
{
    std::lock_guard<std::mutex> lock(g.error_lock);
    return g.error;
}

// Common exit for bad arguments/preconditions: sets last_error with the API name.
int fail_args(const char *api)
{
    set_error(std::string(api) + ": invalid arguments, or host not ready / in teardown");
    log_line(2, QStringLiteral("fail_args: %1 (g=%2 app=%3)")
                  .arg(QString::fromLatin1(api))
                  .arg((quintptr)&g)
                  .arg((quintptr)g.app.load()));
    return SFHOST_E_ARGS;
}

void log_line(int level, const QString &text)
{
    // Level 0 (incl. QML console.log) only with MAUI_SAILFISH_QT_HOST_DIAG=1; errors always print.
    if (level == 0 && !g.diag)
        return;
    const QByteArray utf = text.toUtf8();
    if (g.log)
        g.log(level, utf.constData(), g.log_user);
    std::fprintf(stderr, "[sfhost:%d] %s\n", level, utf.constData());
    std::fflush(stderr);
}

void message_handler(QtMsgType type, const QMessageLogContext &ctx, const QString &msg)
{
    // Property pushes must happen on the Qt thread; if this warning ever appears, log the
    // thread ids and a backtrace to name the offending caller (GUITHREAD-DIAG).
    if (type == QtWarningMsg
        && msg.startsWith(QLatin1String("Updates can only be scheduled"))) {
        void *frames[48];
        const int n = backtrace(frames, 48);
        char **syms = backtrace_symbols(frames, n);
        QString bt;
        for (int i = 0; i < n; ++i) {
            bt += QStringLiteral(" #");
            bt += QString::number(i);
            bt += ' ';
            bt += QLatin1String(syms ? syms[i] : "?");
        }
        free(syms);
        const quintptr cur = reinterpret_cast<quintptr>(QThread::currentThreadId());
        const quintptr rend = g.render_tid.load();
        log_line(1, QStringLiteral("GUITHREAD-DIAG sameAsGui=%1 sameAsRender=%2 cur=%3 render=%4 ctx=%5:%6 bt=%7")
                 .arg(QThread::currentThread() == QCoreApplication::instance()->thread())
                 .arg(rend != 0 && cur == rend)
                 .arg(cur)
                 .arg(rend)
                 .arg(QLatin1String(ctx.file ? ctx.file : "-"))
                 .arg(ctx.line)
                 .arg(bt));
    }
    switch (type) {
    case QtDebugMsg:    log_line(0, msg); break;
    case QtWarningMsg:  log_line(1, msg); break;
    case QtCriticalMsg: log_line(2, msg); break;
    case QtFatalMsg:    log_line(3, msg); std::abort();
    default:            log_line(1, msg); break;
    }
}

// Counts and logs callback paths reached after teardown; must stay 0.
void note_late_callback(const char *what)
{
    const long long n = ++g.late_callbacks;
    if (n <= 8)   // avoid log floods
        log_line(1, QStringLiteral("LATE callback path '%1' reached after teardown (n=%2)")
                        .arg(QString::fromUtf8(what)).arg(n));
}

QQmlEngine *current_engine()
{
    return g.view ? g.view->engine() : g.engine;
}

// --- perf ---
// CPU is the process clock (all threads); RSS is /proc/self/statm field 2 (resident pages).
void sample_cpu_rss()
{
    struct timespec ts;
    if (clock_gettime(CLOCK_PROCESS_CPUTIME_ID, &ts) == 0)
        g.cpu_ms.store(static_cast<long long>(ts.tv_sec) * 1000LL +
                       static_cast<long long>(ts.tv_nsec) / 1000000LL);
    QFile f(QStringLiteral("/proc/self/statm"));
    if (f.open(QIODevice::ReadOnly)) {
        const QByteArray line = f.readLine();
        f.close();
        const QList<QByteArray> parts = line.split(' ');
        if (parts.size() >= 2) {
            const long pages = sysconf(_SC_PAGESIZE);
            const long long kb = parts.at(1).toLongLong() * (pages > 0 ? pages : 4096) / 1024;
            g.rss_kb.store(kb);
            long long peak = g.peak_rss_kb.load();
            while (kb > peak && !g.peak_rss_kb.compare_exchange_weak(peak, kb)) { }
        }
    }
}

// Counts QObjects and visual childItems (Qt 5.6 ListView delegates keep their QObject
// parent), deduplicated.
void count_tree(QObject *o, QSet<QObject *> &seen, long long &objects, long long &items)
{
    if (!o || seen.contains(o))
        return;
    seen.insert(o);
    ++objects;
    if (QQuickItem *item = qobject_cast<QQuickItem *>(o)) {
        ++items;
        const QList<QQuickItem *> kids = item->childItems();
        for (int i = 0; i < kids.size(); ++i)
            count_tree(kids.at(i), seen, objects, items);
    }
    const QObjectList kids = o->children();
    for (int i = 0; i < kids.size(); ++i)
        count_tree(kids.at(i), seen, objects, items);
}

// Render loop class name, e.g. QSGThreadedRenderLoop; windowManager is a QObject, so no moc.
const char *render_loop_name()
{
    QQuickWindow *qw = qobject_cast<QQuickWindow *>(g.window);
    if (!qw)
        qw = qobject_cast<QQuickWindow *>(g.root);
    if (!qw)
        return "none";
    QQuickWindowPrivate *priv = QQuickWindowPrivate::get(qw);
    if (!priv || !priv->windowManager)
        return "unknown";
    return priv->windowManager->metaObject()->className();
}

// Hooks frame timing on the main window once. These signals may fire on the render thread,
// so only atomics and the read-only QElapsedTimer are touched.
void hook_perf_signals(QWindow *w)
{
    if (g.perf_hooked)
        return;
    QQuickWindow *qw = qobject_cast<QQuickWindow *>(w);
    if (!qw)
        return;
    g.perf_hooked = true;
    QObject::connect(qw, &QQuickWindow::frameSwapped, []() {
        g.render_tid.store(reinterpret_cast<quintptr>(QThread::currentThreadId()));
        const long long now_ns = g.perf_clock.isValid() ? g.perf_clock.nsecsElapsed() : 0;
        const long long prev = g.last_frame_ns.exchange(now_ns);
        const long long n = ++g.frames;
        if (n == 1) {
            g.first_frame_ms.store(now_ns / 1000000LL);
        } else if (prev > 0 && now_ns >= prev) {
            const long long us = (now_ns - prev) / 1000LL;
            g.frame_sum_us += us;
            if (us > 32000)
                ++g.slow_frames;
            long long mx = g.frame_max_us.load();
            while (us > mx && !g.frame_max_us.compare_exchange_weak(mx, us)) { }
        }
    });
    // The sync phase is where property writes from apply_props/apply_geometry reach the scene graph.
    QObject::connect(qw, &QQuickWindow::beforeSynchronizing, []() {
        g.sync_start_ns.store(g.perf_clock.isValid() ? g.perf_clock.nsecsElapsed() : 0);
    });
    QObject::connect(qw, &QQuickWindow::afterSynchronizing, []() {
        const long long start = g.sync_start_ns.exchange(0);
        if (start <= 0 || !g.perf_clock.isValid())
            return;
        const long long us = (g.perf_clock.nsecsElapsed() - start) / 1000LL;
        ++g.sync_count;
        g.sync_sum_us += us;
        long long mx = g.sync_max_us.load();
        while (us > mx && !g.sync_max_us.compare_exchange_weak(mx, us)) { }
    });
    log_line(0, QStringLiteral("perf: frame/sync instrumentation hooked (renderLoop=%1)")
                    .arg(QString::fromLatin1(render_loop_name())));
}

// Drawing surfaces paint in afterAnimating: on the GUI thread, once per frame, before the scene graph syncs, so a
// surface committed there shows in the same frame (Android draws a View in its frame's onDraw the same way).
// Per window: Silica creates its windows late, so the main window can change after the first attach, and a signal
// connected on the earlier one would never fire for the frames requested on the current one.
void hook_frame_signal(QWindow *w)
{
    QQuickWindow *qw = qobject_cast<QQuickWindow *>(w);
    if (!qw || g.frame_window == qw)
        return;
    g.frame_window = qw;
    log_line(0, QStringLiteral("frame callback hooked on %1").arg(QString::fromUtf8(qw->metaObject()->className())));
    QObject::connect(qw, &QQuickWindow::afterAnimating, qw, [qw]() {
        if (g.frame_window != qw)
            return;
        if (!g.frame_requested)
            return;
        g.frame_requested = false;
        ++g.frame_callbacks;
        if (g.frame_fn)
            g.frame_fn(g.frame_user);
        else if (g.shutdown)
            note_late_callback("frame");
    });
    if (g.frame_requested)
        qw->update();   // requested before the window existed
}

// --- input ---
// Events are only observed (eventFilter returns false), so Silica/QML still receives them.
void emit_pointer(int kind, double x, double y, double delta, int extra)
{
    ++g.pointer_events;
    if (g.pointer)
        g.pointer(kind, x, y, delta, extra, g.input_user);
    else if (g.shutdown)
        note_late_callback("pointer");
    if (kind != 2 && kind != 5 && kind != 7) // skip move / touch update / second finger to avoid log floods
        log_line(0, QStringLiteral("input kind=%1 x=%2 y=%3 delta=%4 extra=%5 total=%6")
                        .arg(kind)
                        .arg(x, 0, 'f', 1)
                        .arg(y, 0, 'f', 1)
                        .arg(delta, 0, 'f', 1)
                        .arg(extra)
                        .arg(g.pointer_events));
}

class InputFilter : public QObject
{
public:
    bool eventFilter(QObject *, QEvent *event) override
    {
        switch (event->type()) {
        case QEvent::MouseButtonPress:
        case QEvent::MouseButtonRelease:
        case QEvent::MouseMove: {
            QMouseEvent *me = static_cast<QMouseEvent *>(event);
            const int kind = event->type() == QEvent::MouseButtonPress     ? 0
                             : event->type() == QEvent::MouseButtonRelease ? 1
                                                                           : 2;
            emit_pointer(kind, me->x(), me->y(), 0.0, static_cast<int>(me->button()));
            break;
        }
        case QEvent::Wheel: {
            QWheelEvent *we = static_cast<QWheelEvent *>(event);
            emit_pointer(3, we->x(), we->y(), we->angleDelta().y(), 0);
            break;
        }
        case QEvent::TouchBegin:
        case QEvent::TouchUpdate:
        case QEvent::TouchEnd: {
            QTouchEvent *te = static_cast<QTouchEvent *>(event);
            const int kind = event->type() == QEvent::TouchBegin ? 4
                             : event->type() == QEvent::TouchEnd ? 6
                                                                 : 5;
            const QList<QTouchEvent::TouchPoint> points = te->touchPoints();
            if (points.isEmpty())
                emit_pointer(kind, 0.0, 0.0, 0.0, 0);
            else
                emit_pointer(kind, points.first().pos().x(), points.first().pos().y(),
                             0.0, points.size());
            // Kind 7, right after: the second finger (pinch), with the number of fingers still down.
            if (points.size() >= 2) {
                int down = 0;
                for (const QTouchEvent::TouchPoint &p : points)
                    if (p.state() != Qt::TouchPointReleased)
                        ++down;
                emit_pointer(7, points.at(1).pos().x(), points.at(1).pos().y(), 0.0, down);
            }
            break;
        }
        case QEvent::KeyPress:
        case QEvent::KeyRelease: {
            QKeyEvent *ke = static_cast<QKeyEvent *>(event);
            const int kind = event->type() == QEvent::KeyPress ? 0 : 1;
            const QByteArray text = ke->text().toUtf8();
            if (g.key)
                g.key(kind, ke->key(), static_cast<int>(ke->modifiers()),
                      text.constData(), g.input_user);
            else if (g.shutdown)
                note_late_callback("key");
            log_line(0, QStringLiteral("key kind=%1 code=%2 text='%3'")
                            .arg(kind).arg(ke->key()).arg(QString::fromUtf8(text)));
            break;
        }
        default:
            break;
        }
        return false;
    }
};

// Arms the single-shot tick in ms (the earlier deadline wins).
void wake_on_qt(int ms)
{
    if (!g.exec_timer) {
        if (g.pending_wake < 0 || ms < g.pending_wake)
            g.pending_wake = ms;
        return;
    }
    if (!g.event_driven)
        return;
    ++g.wakes;
    if (!g.exec_timer->isActive() || g.exec_timer->remainingTime() > ms)
        g.exec_timer->start(ms);
}

void wake_cb(void *data) { wake_on_qt(static_cast<int>(reinterpret_cast<intptr_t>(data))); }

// --- props / JS ---
// Set on rootContext before create(), so QML sees them in Component.onCompleted (e.g. cover: null).
void apply_context_props(QQmlEngine *engine, const char *props_json)
{
    if (!engine || !props_json || !props_json[0])
        return;
    QJsonParseError perr;
    const QJsonDocument doc = QJsonDocument::fromJson(QByteArray(props_json), &perr);
    if (perr.error != QJsonParseError::NoError || !doc.isObject()) {
        log_line(1, QStringLiteral("props_json ignored: %1")
                        .arg(QString::fromUtf8(perr.errorString().toUtf8())));
        return;
    }
    QQmlContext *ctx = engine->rootContext();
    const QJsonObject obj = doc.object();
    for (QJsonObject::const_iterator it = obj.constBegin(); it != obj.constEnd(); ++it) {
        const QJsonValue v = it.value();
        if (v.isString())
            ctx->setContextProperty(it.key(), v.toString());
        else if (v.isBool())
            ctx->setContextProperty(it.key(), v.toBool());
        else if (v.isDouble())
            ctx->setContextProperty(it.key(), v.toDouble());
        else
            ctx->setContextProperty(it.key(), v.toVariant());
        log_line(0, QStringLiteral("context property: %1").arg(it.key()));
    }
}

// JSON is a subset of JS object literal syntax, so compact JSON drops straight into QQmlExpression.
QString json_to_js_literal(const char *json)
{
    if (!json || !json[0])
        return QStringLiteral("{}");
    QJsonParseError perr;
    const QJsonDocument doc = QJsonDocument::fromJson(QByteArray(json), &perr);
    if (perr.error != QJsonParseError::NoError || !doc.isObject()) {
        log_line(1, QStringLiteral("props json invalid (%1) — using {}")
                        .arg(QString::fromUtf8(perr.errorString().toUtf8())));
        return QStringLiteral("{}");
    }
    return QString::fromUtf8(QJsonDocument(doc.object()).toJson(QJsonDocument::Compact));
}

// Must be absolute: PageStack._getPage() resolves relative paths against a JS stack frame
// that does not exist when called from C++.
QString path_to_js_url(const char *path)
{
    QString url = QUrl::fromLocalFile(QString::fromUtf8(path ? path : "")).toString();
    url.replace(QLatin1Char('\\'), QLatin1String("\\\\"));
    url.replace(QLatin1Char('"'), QLatin1String("\\\""));
    return QLatin1Char('"') + url + QLatin1Char('"');
}

// Metaobject class chain; shows Silica ApplicationWindow is a QQuickItem, not a QQuickWindow.
QString class_chain(const QObject *obj)
{
    QString out;
    const QMetaObject *mo = obj ? obj->metaObject() : nullptr;
    for (int i = 0; mo && i < 8; ++i) {
        if (!out.isEmpty())
            out += QStringLiteral(" <- ");
        out += QString::fromUtf8(mo->className());
        mo = mo->superClass();
    }
    return out;
}

// The root's context carries the component imports (pageStack, PageStackAction, Theme) and
// the root is the scope, since ApplicationWindow aliases pageStack.
bool eval_js(const QString &js, QString *out)
{
    QObject *obj = g.root;
    if (!obj) {
        set_error("no QML root object (call load/load_window first)");
        return false;
    }
    QQmlContext *ctx = QQmlEngine::contextForObject(obj);
    if (!ctx && current_engine())
        ctx = current_engine()->rootContext();
    if (!ctx) {
        set_error("no QML context for the root object");
        return false;
    }
    QQmlExpression expr(ctx, obj, js);
    expr.setNotifyOnValueChanged(false);
    bool isUndefined = false;
    const QVariant result = expr.evaluate(&isUndefined);
    if (expr.hasError()) {
        set_error(expr.error().toString().toUtf8().constData());
        log_line(2, QStringLiteral("eval failed: %1 | expr: %2")
                        .arg(QString::fromStdString(error_text()), js));
        return false;
    }
    if (out) {
        if (isUndefined || !result.isValid())
            *out = QStringLiteral("undefined");
        else if (result.canConvert<QString>())
            *out = result.toString();
        else
            *out = QStringLiteral("<%1>").arg(
                QString::fromUtf8(result.typeName() ? result.typeName() : "?"));
    }
    return true;
}

// Drains the QML-to-C queue (current page's __mauiQueue plus the app queue) with one eval.
// It runs on every tick, so the expression is compiled once and the length guard skips
// JSON.stringify when the queues are empty.
void drain_qml_events()
{
    if (!g.event_fn) {
        if (g.shutdown)
            note_late_callback("qml-event");
        return;
    }
    if (!g.window_mode || !g.root)
        return;
    if (!g.drain_expr) {
        QQmlContext *ctx = QQmlEngine::contextForObject(g.root);
        if (!ctx && current_engine())
            ctx = current_engine()->rootContext();
        if (!ctx)
            return;
        // MauiShell.__mauiDrainAll reads every model page; the inline form (top page + app queue) is the fallback.
        g.drain_expr = new QQmlExpression(ctx, g.root, QStringLiteral(
            "(function(){if(typeof __mauiDrainAll==='function')return __mauiDrainAll();"
            "var p=pageStack&&pageStack.currentPage;var out=null;"
            "if(p&&p.__mauiQueue&&p.__mauiQueue.length)out=p.__mauiDrain();"
            // app service queue (MauiShell), independent of the page
            "if(typeof __mauiAppQueue!=='undefined'&&__mauiAppQueue.length)out=(out||[]).concat(__mauiAppDrain());"
            "return out?JSON.stringify(out):'[]';})()"));
        g.drain_expr->setNotifyOnValueChanged(false);
    }
    ++g.drains;
    bool isUndefined = false;
    const QVariant value = g.drain_expr->evaluate(&isUndefined);
    if (g.drain_expr->hasError()) {
        log_line(1, QStringLiteral("drain eval failed: %1")
                        .arg(g.drain_expr->error().toString()));
        g.drain_expr->deleteLater();   // the context may have died; recreate on the next tick
        g.drain_expr = nullptr;
        return;
    }
    const QString result = (isUndefined || !value.isValid())
        ? QStringLiteral("undefined")
        : value.toString();
    if (result == QLatin1String("[]") || result == QLatin1String("undefined"))
        return;
    QJsonParseError perr;
    const QJsonDocument doc = QJsonDocument::fromJson(result.toUtf8(), &perr);
    if (perr.error != QJsonParseError::NoError || !doc.isArray())
        return;
    const QJsonArray arr = doc.array();
    for (const QJsonValue &v : arr) {
        if (!v.isObject())
            continue;
        const QJsonObject o = v.toObject();
        const QByteArray name = o.value(QStringLiteral("name")).toString().toUtf8();
        const QByteArray payload = o.value(QStringLiteral("payload")).toString().toUtf8();
        log_line(0, QStringLiteral("qml event name='%1' payload=%2")
                        .arg(QString::fromUtf8(name), QString::fromUtf8(payload)));
        g.event_fn(name.constData(), payload.constData(), g.event_user);
    }
}

// Tells managed code the app is quitting, once. Synchronous: the loop is ending, so a queued QML event would
// never be drained.
void notify_quit()
{
    if (g.quit_notified)
        return;
    g.quit_notified = true;
    if (g.event_fn)
        g.event_fn("svc-app-quit", "{}", g.event_user);
}

// --- native objects (handles) ---
// nullptr for unknown handles and for dead ones (the QPointer was cleared). Qt thread only.
QObject *resolve_handle(long long handle)
{
    auto it = g.objects.constFind(reinterpret_cast<void *>(static_cast<qintptr>(handle)));
    if (it == g.objects.constEnd())
        return nullptr;
    return it.value().data();
}

// resolve_handle that records the "dead or unknown" error; callers return SFHOST_E_DEAD_HANDLE on null.
QObject *require_handle(long long handle)
{
    QObject *obj = resolve_handle(handle);
    if (!obj)
        set_error(QStringLiteral("dead or unknown object handle %1").arg(handle).toUtf8().constData());
    return obj;
}

// Copies utf into a caller buffer, truncated to cap-1 bytes and always NUL-terminated; returns the copied length.
int copy_out(const QByteArray &utf, char *buf, int cap)
{
    if (!buf || cap <= 0)
        return 0;
    const int n = qMin(utf.size(), cap - 1);
    std::memcpy(buf, utf.constData(), static_cast<size_t>(n));
    buf[n] = '\0';
    return n;
}

// A result longer than the caller's buffer stays for sailfish_host_last_result.
void keep_overflow(const QByteArray &utf, int cap)
{
    if (utf.size() >= cap)
        g.overflow = utf;
}

// Unregisters automatically on destroyed, so objects QML deletes (e.g. on page pop) do not
// leave dead entries and the registry count returns to baseline.
long long register_handle(QObject *obj)
{
    if (!g.objects.contains(obj)) {
        g.objects.insert(obj, QPointer<QObject>(obj));
        void *key = obj;
        QObject::connect(obj, &QObject::destroyed, [key]() {
            g.objects.remove(key);
        });
    }
    return static_cast<long long>(reinterpret_cast<qintptr>(obj));
}

// Converts JSON to a QVariant typed by the target property; see sailfish_host_set_property
// for the accepted forms. A bare number also works as an object handle.
// "#AARRGGBB" (the bridge's color form) is parsed by hand: QColor(QString) returned invalid for it on the
// device's Qt 5.6 build. Every other form goes through QColor.
QColor parse_color(const QString &text)
{
    if (text.size() == 9 && text.startsWith(QLatin1Char('#'))) {
        bool ok = false;
        const uint argb = text.midRef(1).toUInt(&ok, 16);
        if (ok)
            return QColor::fromRgba(argb);
    }
    return QColor(text);
}

bool json_value_to_variant(const QJsonValue &v, QObject *obj, const char *name, QVariant *out)
{
    const QMetaObject *mo = obj->metaObject();
    const int idx = mo->indexOfProperty(name);
    const QMetaProperty prop = idx >= 0 ? mo->property(idx) : QMetaProperty();

    if (v.isNull()) {
        // Default-constructed value of the property type (invalid for dynamic properties).
        *out = prop.isValid() ? QVariant(static_cast<int>(prop.type()), nullptr) : QVariant();
        return true;
    }

    if (prop.isValid() && prop.isEnumType()) {
        if (v.isString()) {
            const QMetaEnum me = prop.enumerator();
            const int key = me.keyToValue(v.toString().toUtf8().constData());
            if (key < 0) {
                set_error(std::string("unknown enum key '")
                        + v.toString().toUtf8().constData() + "' for " + name);
                return false;
            }
            *out = key;
            return true;
        }
        *out = qRound(v.toDouble());
        return true;
    }

    switch (prop.isValid() ? static_cast<int>(prop.type()) : -1) {
    case QMetaType::Bool:
        *out = v.toBool();
        return true;
    case QMetaType::Int:
    case QMetaType::UInt:
        *out = qRound(v.toDouble());
        return true;
    case QMetaType::LongLong:
    case QMetaType::ULongLong:
        *out = static_cast<qlonglong>(v.toDouble());
        return true;
    case QMetaType::Double:
        *out = v.toDouble();
        return true;
    case QMetaType::QString:
        *out = v.toString();
        return true;
    case QMetaType::QColor: {
        const QColor color = parse_color(v.toString());
        if (!color.isValid()) {
            set_error(std::string("invalid color for ") + name);
            return false;
        }
        *out = color;
        return true;
    }
    case QMetaType::QRectF: {
        const QJsonObject o = v.toObject();
        *out = QRectF(o.value(QStringLiteral("x")).toDouble(),
                      o.value(QStringLiteral("y")).toDouble(),
                      o.value(QStringLiteral("width")).toDouble(),
                      o.value(QStringLiteral("height")).toDouble());
        return true;
    }
    case QMetaType::QRect: {
        const QJsonObject o = v.toObject();
        *out = QRect(qRound(o.value(QStringLiteral("x")).toDouble()),
                     qRound(o.value(QStringLiteral("y")).toDouble()),
                     qRound(o.value(QStringLiteral("width")).toDouble()),
                     qRound(o.value(QStringLiteral("height")).toDouble()));
        return true;
    }
    case QMetaType::QPointF: {
        const QJsonObject o = v.toObject();
        *out = QPointF(o.value(QStringLiteral("x")).toDouble(),
                       o.value(QStringLiteral("y")).toDouble());
        return true;
    }
    case QMetaType::QPoint: {
        const QJsonObject o = v.toObject();
        *out = QPoint(qRound(o.value(QStringLiteral("x")).toDouble()),
                      qRound(o.value(QStringLiteral("y")).toDouble()));
        return true;
    }
    case QMetaType::QSizeF: {
        const QJsonObject o = v.toObject();
        *out = QSizeF(o.value(QStringLiteral("width")).toDouble(),
                      o.value(QStringLiteral("height")).toDouble());
        return true;
    }
    case QMetaType::QSize: {
        const QJsonObject o = v.toObject();
        *out = QSize(qRound(o.value(QStringLiteral("width")).toDouble()),
                     qRound(o.value(QStringLiteral("height")).toDouble()));
        return true;
    }
    default:
        break;
    }

    if (prop.isValid() && (QMetaType::typeFlags(prop.userType()) & QMetaType::PointerToQObject)) {
        // Object identity: {"$handle": N} or a bare number, resolved through the registry.
        const qlonglong ref_handle = v.isObject()
                ? static_cast<qlonglong>(v.toObject().value(QStringLiteral("$handle")).toDouble())
                : static_cast<qlonglong>(v.toDouble());
        QObject *ref = ref_handle ? resolve_handle(ref_handle) : nullptr;
        if (!ref) {
            set_error(std::string("unknown object handle in identity value for ") + name);
            return false;
        }
        *out = QVariant::fromValue(ref);
        return true;
    }

        // Dynamic properties (QML var) and other types: generic conversion.
    *out = v.toVariant();
    if (!out->isValid())
        set_error(std::string("cannot convert value for ") + name);
    return out->isValid();
}

// Wraps the value in an array because Qt 5.6 QJsonDocument cannot parse a top-level scalar.
bool json_parse_value(const char *json, QJsonValue *out)
{
    if (!json)
        return false;
    const QByteArray wrapped = QByteArray("[") + json + "]";
    QJsonParseError perr;
    const QJsonDocument doc = QJsonDocument::fromJson(wrapped, &perr);
    if (perr.error != QJsonParseError::NoError || !doc.isArray() || doc.array().size() != 1)
        return false;
    *out = doc.array().at(0);
    return !out->isUndefined();
}

// --- windows ---
// Silica ApplicationWindow is an Item with its own QQuickWindow, plus a wallpaper window and,
// with a cover set, "_CoverWindow". Fallback when QQuickItem::window() is unavailable: the
// largest visible top-level window that is not the cover.
QWindow *find_main_window()
{
    QWindow *best = nullptr;
    qint64 bestScore = -1;
    const QWindowList wins = QGuiApplication::topLevelWindows();
    for (int i = 0; i < wins.size(); ++i) {
        QWindow *w = wins.at(i);
        if (!w)
            continue;
        const QString name = w->objectName().isEmpty() ? w->title() : w->objectName();
        if (name == QLatin1String("_CoverWindow"))
            continue;
        qint64 score = qint64(w->width()) * qint64(w->height());
        if (w->isVisible())
            score += (qint64(1) << 40);
        if (score > bestScore) {
            bestScore = score;
            best = w;
        }
    }
    return best;
}

// Idempotent and also called deferred, since Silica creates windows in componentComplete.
// Qt does not deduplicate event filters, so the filter is installed only on a window change.
void attach_window(const char *why)
{
    QWindow *w = g.window;
    if (!w && g.root) {
        if (QQuickWindow *qw = qobject_cast<QQuickWindow *>(g.root))
            w = qw;
        else if (QQuickItem *item = qobject_cast<QQuickItem *>(g.root))
            w = item->window();
    }
    if (!w)
        w = find_main_window();
    if (!w || w == g.window)
        return;

    g.window = w;
    if (!g.input_filter)
        g.input_filter = new InputFilter();
    w->installEventFilter(g.input_filter);
    hook_perf_signals(w);   // idempotent
    hook_frame_signal(w);   // idempotent
    log_line(0, QStringLiteral("attached main window (%1): %2 name='%3' title='%4' geom=%5,%6 %7x%8")
                    .arg(QString::fromUtf8(why))
                    .arg(QString::fromUtf8(w->metaObject()->className()))
                    .arg(w->objectName())
                    .arg(w->title())
                    .arg(w->x()).arg(w->y()).arg(w->width()).arg(w->height()));
}

QString describe_windows()
{
    const QWindowList wins = QGuiApplication::topLevelWindows();
    QString out = QStringLiteral("mode=%1 root=%2 main=%3 topLevel=%4\n")
                      .arg(g.window_mode ? QStringLiteral("window")
                                         : (g.root ? QStringLiteral("view")
                                                   : QStringLiteral("none")))
                      .arg(g.root ? QString::fromUtf8(g.root->metaObject()->className())
                                  : QStringLiteral("null"))
                      .arg(g.window ? QString::fromUtf8(g.window->metaObject()->className())
                                    : QStringLiteral("null"))
                      .arg(wins.size());
    bool cover = false;
    for (int i = 0; i < wins.size(); ++i) {
        const QWindow *w = wins.at(i);
        if (!w)
            continue;
        const QString name = w->objectName().isEmpty() ? w->title() : w->objectName();
        if (name == QLatin1String("_CoverWindow"))
            cover = true;
        out += QStringLiteral("#%1 %2 name='%3' title='%4' visible=%5 visibility=%6\n")
                   .arg(i)
                   .arg(QString::fromUtf8(w->metaObject()->className()))
                   .arg(w->objectName())
                   .arg(w->title())
                   .arg(w->isVisible() ? QStringLiteral("yes") : QStringLiteral("no"))
                   .arg(static_cast<int>(w->visibility()));
        out += QStringLiteral("   geom=%1,%2 %3x%4 trackedAsMain=%5\n")
                   .arg(w->x()).arg(w->y()).arg(w->width()).arg(w->height())
                   .arg(w == g.window ? QStringLiteral("yes") : QStringLiteral("no"));
    }
    out += QStringLiteral("coverWindow=%1 pointerEvents=%2 ticks=%3\n")
               .arg(cover ? QStringLiteral("yes") : QStringLiteral("no"))
               .arg(g.pointer_events)
               .arg(g.ticks);
    return out;
}

// Appends one line to /tmp/maui_trace.log with raw syscalls.
void trace_file_line(const char *line)
{
    const int fd = open("/tmp/maui_trace.log", O_WRONLY | O_CREAT | O_APPEND, 0644);
    if (fd < 0)
        return;
    const size_t len = strlen(line);
    write(fd, line, len);
    write(fd, "\n", 1);
    close(fd);
}

// http(s) sources QML loads itself (image thumbnails) go through a disk cache: the engine's default manager
// has none, and Qt 5.6 keeps only ~2 MB of decoded pixmaps, so a tile scrolled back into view would download
// again. MAUI_SAILFISH_HTTP_CACHE_MB sizes it (default 64, 0 disables).
// Each entry is stamped with its store time, so UriImageSource.CacheValidity can age it out.
static const char kCachedAtHeader[] = "X-Maui-Cached-At";

class StampedDiskCache : public QNetworkDiskCache
{
public:
    using QNetworkDiskCache::QNetworkDiskCache;

    QIODevice *prepare(const QNetworkCacheMetaData &metaData) override
    {
        QNetworkCacheMetaData stamped(metaData);
        auto headers = stamped.rawHeaders();
        headers.append({ QByteArray(kCachedAtHeader), QByteArray::number(QDateTime::currentMSecsSinceEpoch()) });
        stamped.setRawHeaders(headers);
        return QNetworkDiskCache::prepare(stamped);
    }
};

// Qt 5.6 has no transfer timeout (QNetworkRequest::setTransferTimeout came in 5.15): a request on a dead keep-alive
// connection (after a network change or a suspend) hangs for minutes, and with six connections per host the next
// thumbnails queue behind it, so images stop loading. A reply that receives nothing for MAUI_SAILFISH_HTTP_STALL_S
// seconds (default 20, 0 = off) is aborted; the Image adapter then loads it again (qml/controls/Image.qml).
// Runs on the thread that issued the request (QML's pixmap reader): stderr, not log_line's managed callback.
void arm_stall_watchdog(QNetworkReply *reply)
{
    static const int seconds = [] {
        bool ok = false;
        const int value = qEnvironmentVariableIntValue("MAUI_SAILFISH_HTTP_STALL_S", &ok);
        return ok ? value : 20;
    }();
    if (seconds <= 0 || !reply || reply->isFinished())
        return;
    auto *timer = new QTimer(reply);
    timer->setSingleShot(true);
    timer->setInterval(seconds * 1000);
    QObject::connect(timer, &QTimer::timeout, reply, [reply]() {
        if (reply->isFinished())
            return;
        std::fprintf(stderr, "[sfhost] http stall: no data for %d s, aborting %s\n", seconds,
                     reply->url().toString(QUrl::RemoveFragment).toUtf8().constData());
        reply->abort();
    });
    QObject::connect(reply, &QNetworkReply::downloadProgress, timer, [timer](qint64, qint64) { timer->start(); });
    QObject::connect(reply, &QNetworkReply::finished, timer, &QTimer::stop);
    timer->start();
}

// The per-request policy comes from UriImageSource (QtHostImages: "#maui-cache=N" in the URL, never sent):
// 0 = CachingEnabled false (network, store nothing), N = a copy younger than N seconds (else refresh). Without
// it GETs prefer the cached copy (QML only fetches images; app data goes through .NET's HttpClient).
class CachingNetworkAccessManager : public QNetworkAccessManager
{
public:
    using QNetworkAccessManager::QNetworkAccessManager;

protected:
    QNetworkReply *createRequest(Operation op, const QNetworkRequest &request, QIODevice *data) override
    {
        if (op != GetOperation)
            return QNetworkAccessManager::createRequest(op, request, data);
        QNetworkRequest shaped(request);
        const QString fragment = request.url().fragment();
        if (fragment.startsWith(QLatin1String("maui-cache="))) {
            const qint64 validitySeconds = fragment.mid(11).toLongLong();
            if (validitySeconds <= 0) {
                shaped.setAttribute(QNetworkRequest::CacheLoadControlAttribute, QNetworkRequest::AlwaysNetwork);
                shaped.setAttribute(QNetworkRequest::CacheSaveControlAttribute, false);
            } else {
                shaped.setAttribute(QNetworkRequest::CacheLoadControlAttribute,
                                    isFresh(request.url(), validitySeconds) ? QNetworkRequest::PreferCache
                                                                            : QNetworkRequest::AlwaysNetwork);
            }
        } else {
            shaped.setAttribute(QNetworkRequest::CacheLoadControlAttribute, QNetworkRequest::PreferCache);
        }
        QNetworkReply *reply = QNetworkAccessManager::createRequest(op, shaped, data);
        arm_stall_watchdog(reply);
        return reply;
    }

private:
    bool isFresh(const QUrl &url, qint64 validitySeconds) const
    {
        if (!cache())
            return false;
        const QNetworkCacheMetaData meta = cache()->metaData(url);
        if (!meta.isValid())
            return false;
        for (const auto &header : meta.rawHeaders())
            if (header.first == kCachedAtHeader)
                return QDateTime::currentMSecsSinceEpoch() - header.second.toLongLong() < validitySeconds * 1000;
        return false;   // stored before stamping: refresh once
    }
};

class CachingNetworkFactory : public QQmlNetworkAccessManagerFactory
{
public:
    CachingNetworkFactory(const QString &dir, qint64 bytes) : m_dir(dir), m_bytes(bytes) {}

    QNetworkAccessManager *create(QObject *parent) override
    {
        auto *manager = new CachingNetworkAccessManager(parent);
        auto *cache = new StampedDiskCache(manager);
        cache->setCacheDirectory(m_dir);
        cache->setMaximumCacheSize(m_bytes);
        manager->setCache(cache);
        return manager;
    }

private:
    QString m_dir;
    qint64 m_bytes;
};

void install_http_cache(QQmlEngine *engine)
{
    bool ok = false;
    const int mb = qEnvironmentVariableIntValue("MAUI_SAILFISH_HTTP_CACHE_MB", &ok);
    const qint64 bytes = qint64(ok ? mb : 64) * 1024 * 1024;
    if (bytes <= 0)
        return;
    // ~/.cache/<org>/<app>: inside the app's Sailjail directories.
    const QString dir = QStandardPaths::writableLocation(QStandardPaths::CacheLocation) + QStringLiteral("/qml-http");
    static CachingNetworkFactory *factory = nullptr;   // outlives the engine; one per process
    if (!factory)
        factory = new CachingNetworkFactory(dir, bytes);
    engine->setNetworkAccessManagerFactory(factory);
    log_line(1, QStringLiteral("http cache: %1 (%2 MB)").arg(dir).arg(bytes / (1024 * 1024)));
}

// Both modes host the QML root in a QQuickView. Silica ApplicationWindow is an Item, not a
// window, so created loose via QQmlComponent it has no QQuickWindow; window mode therefore
// uses the canonical SailfishApp::createView() + setSource bootstrap.
int load_common(const char *qml_path, const char *props_json, bool silica_window)
{
    if (!g.app) {
        set_error("sailfish_host_init() was not called");
        return -1;
    }
    if (g.view || g.root) {
        set_error("QML already loaded");
        return -1;
    }
    if (!qml_path || !qml_path[0]) {
        set_error("empty qml_path");
        return -1;
    }

    QQuickView *view = silica_window ? SailfishApp::createView() : new QQuickView();
    if (!view)
        view = new QQuickView(); // createView() may return null without app metadata
    g.view = view;
    g.window_mode = silica_window;

    QQmlEngine *engine = view->engine();
    install_http_cache(engine);   // before any QML loads a remote source
    // Explicit Silica/QtQuick import paths on Sailfish OS, independent of the environment.
    engine->addImportPath(QStringLiteral("/usr/lib64/qt5/qml"));
    engine->addImportPath(QStringLiteral("/usr/share/qt5/qml"));
    // Must be set before setSource: ApplicationWindow reads them in Component.onCompleted.
    apply_context_props(engine, props_json);
    view->setResizeMode(QQuickView::SizeRootObjectToView);

    const QUrl url = QUrl::fromLocalFile(QString::fromUtf8(qml_path));
    view->setSource(url);
    if (!view->rootObject()) {
        QString errs;
        const QList<QQmlError> list = view->errors();
        for (const QQmlError &e : list)
            errs += e.toString() + QLatin1Char('\n');
        set_error(errs.toUtf8().constData());
        log_line(2, QStringLiteral("qml errors:\n%1").arg(errs));
        return SFHOST_E_LOAD;
    }

    g.root = view->rootObject();
    g.window = view;
    // A frame asked for before the window existed (an animation started while the app was still being built) is
    // delivered now; without this the request waited for the next one, and MAUI's animation ticker never got one.
    hook_frame_signal(view);
    if (!g.input_filter)
        g.input_filter = new InputFilter();
    view->installEventFilter(g.input_filter);
    // Closing the Silica window mid-session quits the app (lastWindowClosed), so trace
    // visibility/closing to see whether the stack pop or the window moved first.
    QObject::connect(view, &QQuickWindow::visibilityChanged, [](QWindow::Visibility v) {
        char buf[64];
        snprintf(buf, sizeof buf, "[Sailfish] TRACE windowVisibility=%d", int(v));
        trace_file_line(buf);
    });
    QObject::connect(view, &QQuickWindow::closing, [] {
        trace_file_line("[Sailfish] TRACE window closing");
    });
    log_line(0, QStringLiteral("loaded mode=%1 url=%2 root=%3 chain=%4")
                    .arg(silica_window ? QStringLiteral("window") : QStringLiteral("view"))
                    .arg(url.toString())
                    .arg(QString::fromUtf8(g.root->metaObject()->className()))
                    .arg(class_chain(g.root)));

    // Silica creates the cover/wallpaper windows only after the loop starts.
    QTimer::singleShot(400, []() {
        attach_window("deferred-400");
        log_line(0, QStringLiteral("windows after 400ms:\n%1").arg(describe_windows()));
    });
    return 0;
}

// Full teardown on the Qt thread, sent by sailfish_host_quit as a PostEvent (Qt 5.6 has no
// invokeMethod with a functor). g.shutdown is only touched on the Qt thread.
void shutdown_teardown(void *)
{
    if (g.shutdown)
        return;
    g.shutdown = true;
    log_line(0, QStringLiteral("shutdown: teardown begin "
                               "(timer/receiver -> callbacks -> filter -> view/engine -> quit)"));
    // Counters as of entering teardown (registry still alive; lateCallbacks must be 0).
    log_line(0, QStringLiteral("shutdown: diag stats registry=%1 postsQueued=%2 postsRun=%3 "
                               "postsRejected=%4 lateCallbacks=%5 ticks=%6 pointerEvents=%7")
                    .arg(g.objects.size())
                    .arg(g.posts_queued.load()).arg(g.posts_run.load())
                    .arg(g.posts_rejected.load()).arg(g.late_callbacks.load())
                    .arg(g.ticks).arg(g.pointer_events));
    // deleteLater so DeferredDelete runs before the queued quit.
    if (g.exec_timer) { g.exec_timer->stop(); g.exec_timer->deleteLater(); g.exec_timer = nullptr; }
    if (g.drain_timer) { g.drain_timer->stop(); g.drain_timer->deleteLater(); g.drain_timer = nullptr; }
    if (QObject *receiver = g.receiver.exchange(nullptr, std::memory_order_acq_rel)) receiver->deleteLater();   // unpublished first: posts from other threads stop
    // The compiled drain dies with its engine context; log the final perf report.
    sample_cpu_rss();
    if (g.perf_sampler) { g.perf_sampler->stop(); g.perf_sampler->deleteLater(); g.perf_sampler = nullptr; }
    if (g.drain_expr)   { g.drain_expr->deleteLater(); g.drain_expr = nullptr; }
    {
        const long long fr = g.frames.load();
        const long long avg_us = fr > 1 ? g.frame_sum_us.load() / (fr - 1) : 0;
        const long long sy = g.sync_count.load();
        const long long avg_sync_us = sy > 0 ? g.sync_sum_us.load() / sy : 0;
        log_line(0, QStringLiteral("shutdown: perf frames=%1 firstFrameMs=%2 avgFrameUs=%3 maxFrameUs=%4 "
                                   "slowFrames=%5 sync=%6 avgSyncUs=%7 maxSyncUs=%8 cpuMs=%9 rssKb=%10 peakRssKb=%11 "
                                   "evals=%12 opsEvals=%13 drains=%14 propertySets=%15 propsBatches=%16 geometryBatches=%17 "
                                   "textMeasures=%18 grabs=%19")
                        .arg(fr).arg(g.first_frame_ms.load()).arg(avg_us)
                        .arg(g.frame_max_us.load()).arg(g.slow_frames.load())
                        .arg(sy).arg(avg_sync_us).arg(g.sync_max_us.load())
                        .arg(g.cpu_ms.load()).arg(g.rss_kb.load()).arg(g.peak_rss_kb.load())
                        .arg(g.evals.load()).arg(g.ops_evals.load()).arg(g.drains.load())
                        .arg(g.property_sets.load()).arg(g.props_batches.load())
                        .arg(g.geometry_batches.load()).arg(g.text_measures.load())
                        .arg(g.grabs.load()));
    }
    notify_quit();   // before the callbacks go: aboutToQuit comes too late for managed code
    // Borrowed managed callbacks: cleared, never freed, so nothing calls into .NET afterwards.
    g.tick = nullptr; g.tick_user = nullptr;
    g.pointer = nullptr; g.key = nullptr; g.input_user = nullptr;
    g.event_fn = nullptr; g.event_user = nullptr;
    g.frame_fn = nullptr; g.frame_user = nullptr;
    g.surface_touch_fn = nullptr; g.surface_touch_user = nullptr;
    // Input filter: off the window and the view it was installed on (attach_window, load_common), then deleted.
    if (g.input_filter) {
        if (g.window) g.window->removeEventFilter(g.input_filter);
        if (g.view)   g.view->removeEventFilter(g.input_filter);
        g.input_filter->deleteLater();
        g.input_filter = nullptr;
    }
    // The view owns the engine, which owns root and its window, so deleteLater cascades.
    // g.root/g.window are borrowed and cleared first; the handle registry goes with the QML layer.
    g.root = nullptr; g.window = nullptr;
    g.objects.clear();
    if (g.view)        { g.view->hide(); g.view->deleteLater(); g.view = nullptr; }
    else if (g.engine) { g.engine->deleteLater(); g.engine = nullptr; }
    // QGuiApplication is kept (QPA/Wayland statics). quit is queued after deleteLater, so
    // the loop destroys QML before it ends.
    log_line(0, QStringLiteral("shutdown: QML layer scheduled for destruction, quitting event loop"));
    QMetaObject::invokeMethod(g.app, "quit", Qt::QueuedConnection);
}

} // namespace sfhost
