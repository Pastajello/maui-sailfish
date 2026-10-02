// sailfish_host.cpp — implementation of the Qt Quick/Silica host C ABI (see sailfish_host.h).
//
// Cross-built with zig for aarch64 Sailfish OS (tools/sf native-build, sysroot from
// tools/sf sysroot). No moc: the shim overrides eventFilter/event and connects lambdas
// to existing Qt metaobjects instead of declaring Q_OBJECT.

#include "sailfish_host.h"

#include <sailfishapp.h>

#include <QAccessible>
#include <QColor>
#include <QDateTime>
#include <QElapsedTimer>
#include <QFile>
#include <QFont>
#include <QFontDatabase>
#include <QFontMetrics>
#include <QTextLayout>
#include <QImage>
#include <QPainter>
#include <QtMath>
#include <QGuiApplication>
#include <QClipboard>
#include <csignal>
#include <execinfo.h>
#include <fcntl.h>
#include <unistd.h>
#include <QDesktopServices>
#include <QDir>
#include <QOpenGLContext>
#include <QOpenGLFunctions>
#include <QRunnable>
#include <QThreadPool>
#include <functional>
#include <QHash>
#include <QJsonArray>
#include <QJsonDocument>
#include <QJsonObject>
#include <QJsonValue>
#include <QKeyEvent>
#include <QMouseEvent>
#include <QMetaMethod>
#include <QPointer>
#include <QQmlComponent>
#include <QQmlContext>
#include <QQmlEngine>
#include <QtQml/qqmldebug.h>
#include <QQmlNetworkAccessManagerFactory>
#include <QNetworkAccessManager>
#include <QNetworkDiskCache>
#include <QNetworkRequest>
#include <QStandardPaths>
#include <QQmlError>
#include <QQmlExpression>
#include <QQmlProperty>
#include <QQuickItem>
#include <QSGNode>
#include <QSGSimpleTextureNode>
#include <QSGTexture>
#include <QQuickView>
#include <QQuickWindow>
#include <QScreen>
#include <QSet>
#include <QStringList>
#include <QThread>
#include <QTimer>
#include <QTouchEvent>
#include <QUrl>
#include <QWheelEvent>
#include <QWindow>

#include <qpa/qwindowsysteminterface.h>   // private QPA, used to simulate background/resume

// Private QtQuick 5.6.3 headers (pinned by the sysroot), used to name the render loop.
#include <private/qquickwindow_p.h>
#include <private/qsgrenderloop_p.h>

#include <atomic>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <ctime>
#include <execinfo.h>   // backtrace in message_handler
#include <string>
#include <unistd.h>

namespace {

struct HostState {
    QGuiApplication *app = nullptr;
    QQuickView *view = nullptr;       // view mode
    QQmlEngine *engine = nullptr;     // fallback for current_engine()
    QObject *root = nullptr;          // QML root (ApplicationWindow / view root)
    QWindow *window = nullptr;        // main Wayland window (not cover, not wallpaper)
    QObject *receiver = nullptr;      // target of sailfish_host_post (QEvent::User)
    QObject *input_filter = nullptr;
    sfhost_log_fn log = nullptr;
    void *log_user = nullptr;
    bool diag = false;                   // MAUI_SAILFISH_QT_HOST_DIAG=1 enables level 0 logs
    sfhost_tick_fn tick = nullptr;
    void *tick_user = nullptr;
    sfhost_pointer_fn pointer = nullptr;
    sfhost_key_fn key = nullptr;
    void *input_user = nullptr;
    sfhost_event_fn event_fn = nullptr;  // QML semantic event channel
    void *event_user = nullptr;
    bool window_mode = false;         // root is a Silica ApplicationWindow
    QTimer *exec_timer = nullptr;     // tick pump (parent: app)
    // Event-driven loop: exec_timer is single-shot, armed by managed code via
    // sailfish_host_wake; QML event queues wake drain_timer through mauiEventSeq.
    // MAUI_SAILFISH_TICK_POLL=1 restores a fixed tick every tick_ms.
    QTimer *drain_timer = nullptr;
    bool event_driven = true;
    int pending_wake = -1;            // wake requested before the loop started
    long long wakes = 0;
    bool shutdown = false;            // teardown started (Qt thread only)
    // Handle registry: QPointer turns objects destroyed by QML into dead handles (-3).
    QHash<void *, QPointer<QObject>> objects;
    std::string error;
    std::string clipboardMirror;   // in-app clipboard round-trip mirror
    bool quit_notified = false;    // svc-app-quit sent (teardown or aboutToQuit, whichever is first)
    long long ticks = 0;
    long long pointer_events = 0;
    // posts_queued/rejected change on any thread, hence atomics.
    std::atomic<long long> posts_queued{0};
    std::atomic<long long> posts_run{0};
    std::atomic<long long> posts_rejected{0};
    std::atomic<long long> late_callbacks{0};
    // Perf counters: frame/sync signals fire on the render thread, so everything is atomic.
    QElapsedTimer perf_clock;               // started in exec (uptime/firstFrame)
    QTimer *perf_sampler = nullptr;         // 1 Hz CPU/RSS (parent: app)
    QQmlExpression *drain_expr = nullptr;   // drain expression, compiled once
    bool perf_hooked = false;               // window signals connected once
    std::atomic<long long> frames{0};
    std::atomic<long long> frame_sum_us{0};
    std::atomic<long long> frame_max_us{0};
    std::atomic<long long> slow_frames{0};  // interval > 32 ms
    std::atomic<long long> first_frame_ms{-1};
    std::atomic<long long> last_frame_ns{0};
    std::atomic<long long> sync_count{0};
    std::atomic<long long> sync_sum_us{0};
    std::atomic<long long> sync_max_us{0};
    std::atomic<long long> sync_start_ns{0};
    // Render thread id, so message_handler can tell whether a "can only be scheduled
    // from GUI thread" warning comes from the render loop or a foreign managed thread.
    std::atomic<quintptr> render_tid{0};
    std::atomic<long long> cpu_ms{0};       // CLOCK_PROCESS_CPUTIME_ID (all threads)
    std::atomic<long long> rss_kb{0};
    std::atomic<long long> peak_rss_kb{0};
    // Managed-to-shim boundary crossings, compared per phase.
    std::atomic<long long> evals{0};        // all sailfish_host_eval calls
    std::atomic<long long> ops_evals{0};    // ...of which applyMauiOps batches
    std::atomic<long long> drains{0};       // QML-to-C drain per tick (counted separately)
    std::atomic<long long> property_sets{0};
    std::atomic<long long> props_batches{0};
    std::atomic<long long> props_applied{0};
    std::atomic<long long> geometry_batches{0};
    std::atomic<long long> geometry_entries{0};
    std::atomic<long long> text_measures{0};
    std::atomic<long long> find_objects{0};
    std::atomic<long long> pushes{0};
    std::atomic<long long> pops{0};
    std::atomic<long long> grabs{0};        // RGBA readback, must be 0 in production
    std::atomic<long long> injects{0};
    std::atomic<long long> destroys{0};
    std::atomic<long long> geometry_reads{0};   // item_geometry reads
    // Drawing surfaces: the frame callback runs once per requested frame, in afterAnimating (GUI thread, before sync).
    sfhost_frame_fn frame_fn = nullptr;
    void *frame_user = nullptr;
    sfhost_surface_touch_fn surface_touch_fn = nullptr;
    void *surface_touch_user = nullptr;
    long long surface_touches = 0;
    bool frame_requested = false;
    QPointer<QQuickWindow> frame_window;   // the window afterAnimating is connected on
    long long frame_requests = 0;           // Qt thread
    long long frame_callbacks = 0;
    long long surface_commits = 0;
    long long surface_commit_us = 0;
    std::atomic<long long> surface_uploads{0};      // render thread
    std::atomic<long long> surface_upload_us{0};
    std::atomic<long long> surface_upload_max_us{0};
    std::atomic<long long> surface_tiles{0};        // textures of the last multi-tile upload
    std::atomic<long long> surface_max_texture{0};  // GL_MAX_TEXTURE_SIZE, once a surface rendered
    int argc = 1;
    char *argv0 = nullptr;
    char *argv[2] = {nullptr, nullptr};
};

HostState g;

// Common exit for bad arguments/preconditions: sets last_error with the API name.
void log_line(int level, const QString &text);

int fail_args(const char *api)
{
    g.error = std::string(api) + ": invalid arguments, or host not ready / in teardown";
    log_line(2, QStringLiteral("fail_args: %1 (g=%2 app=%3)")
                  .arg(QString::fromLatin1(api))
                  .arg((quintptr)&g)
                  .arg((quintptr)g.app));
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
    if (kind != 2 && kind != 5) // skip move / touch update to avoid log floods
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

// --- post ---
// postEvent is thread-safe, and a custom QEvent::User handled in event() needs no moc.
class PostEvent : public QEvent
{
public:
    PostEvent(sfhost_void_fn f, void *u) : QEvent(QEvent::User), fn(f), user(u) {}
    sfhost_void_fn fn;
    void *user;
};

// Not counted in postsRun: the teardown event is sent by sailfish_host_quit, bypassing postsQueued.
void shutdown_teardown(void *);
// Arms the single-shot tick in ms (the earlier deadline wins).
static void wake_on_qt(int ms)
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

static void wake_cb(void *data) { wake_on_qt(static_cast<int>(reinterpret_cast<intptr_t>(data))); }

class PostReceiver : public QObject
{
public:
    bool event(QEvent *e) override
    {
        if (e->type() == QEvent::User) {
            PostEvent *pe = static_cast<PostEvent *>(e);
            // Cross-thread wake: not managed work, so not counted; dropped after teardown.
            if (pe->fn == &wake_cb) {
                if (!g.shutdown)
                    pe->fn(pe->user);
                return true;
            }
            if (pe->fn && pe->fn != &shutdown_teardown)
                ++g.posts_run;
            // A post after teardown is a race (expected 0), but fn still runs so the
            // managed trampoline can free its GCHandle.
            if (g.shutdown)
                note_late_callback("post");
            if (pe->fn)
                pe->fn(pe->user);
            return true;
        }
        return QObject::event(e);
    }
};


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
        g.error = "no QML root object (call load/load_window first)";
        return false;
    }
    QQmlContext *ctx = QQmlEngine::contextForObject(obj);
    if (!ctx && current_engine())
        ctx = current_engine()->rootContext();
    if (!ctx) {
        g.error = "no QML context for the root object";
        return false;
    }
    QQmlExpression expr(ctx, obj, js);
    expr.setNotifyOnValueChanged(false);
    bool isUndefined = false;
    const QVariant result = expr.evaluate(&isUndefined);
    if (expr.hasError()) {
        g.error = expr.error().toString().toUtf8().constData();
        log_line(2, QStringLiteral("eval failed: %1 | expr: %2")
                        .arg(QString::fromStdString(g.error), js));
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
        g.drain_expr = new QQmlExpression(ctx, g.root, QStringLiteral(
            "(function(){var p=pageStack&&pageStack.currentPage;var out=null;"
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
static void notify_quit()
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
static QObject *require_handle(long long handle)
{
    QObject *obj = resolve_handle(handle);
    if (!obj)
        g.error = QStringLiteral("dead or unknown object handle %1").arg(handle).toUtf8().constData();
    return obj;
}

// Copies utf into a caller buffer, truncated to cap-1 bytes and always NUL-terminated; returns the copied length.
static int copy_out(const QByteArray &utf, char *buf, int cap)
{
    if (!buf || cap <= 0)
        return 0;
    const int n = qMin(utf.size(), cap - 1);
    std::memcpy(buf, utf.constData(), static_cast<size_t>(n));
    buf[n] = '\0';
    return n;
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
static QColor parse_color(const QString &text)
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
                g.error = std::string("unknown enum key '")
                        + v.toString().toUtf8().constData() + "' for " + name;
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
            g.error = std::string("invalid color for ") + name;
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
            g.error = std::string("unknown object handle in identity value for ") + name;
            return false;
        }
        *out = QVariant::fromValue(ref);
        return true;
    }

        // Dynamic properties (QML var) and other types: generic conversion.
    *out = v.toVariant();
    if (!out->isValid())
        g.error = std::string("cannot convert value for ") + name;
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
static void trace_file_line(const char *line)
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
        return QNetworkAccessManager::createRequest(op, shaped, data);
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
        g.error = "sailfish_host_init() was not called";
        return -1;
    }
    if (g.view || g.root) {
        g.error = "QML already loaded";
        return -1;
    }
    if (!qml_path || !qml_path[0]) {
        g.error = "empty qml_path";
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
        g.error = errs.toUtf8().constData();
        log_line(2, QStringLiteral("qml errors:\n%1").arg(errs));
        return -2;
    }

    g.root = view->rootObject();
    g.window = view;
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
    if (g.receiver)   { g.receiver->deleteLater(); g.receiver = nullptr; }
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
    // Input filter.
    if (g.input_filter) { g.app->removeEventFilter(g.input_filter); g.input_filter->deleteLater(); g.input_filter = nullptr; }
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


} // namespace



// Native crash trap: logs the signal and a raw backtrace (symbolize offline against the .so)
// to stderr and /tmp/maui_trace.log. The previous handler runs first: on ARM64 .NET's PAL turns a
// managed SIGSEGV into NullReferenceException by rewriting the faulting context, so it must get the
// fault's own siginfo/ucontext. (Re-raising handed it a fresh SI_TKILL signal it cannot map, and
// every managed null-ref died as "ExecutionEngineException: Illegal instruction".)
static struct sigaction g_prev_sa[3];

static const struct sigaction *prev_sa(int sig)
{
    return sig == SIGSEGV ? &g_prev_sa[0]
         : sig == SIGABRT ? &g_prev_sa[1]
                          : &g_prev_sa[2];
}

static void sailfish_crash_log(int sig);

static void sailfish_crash_handler(int sig, siginfo_t *info, void *uctx)
{
    const struct sigaction *prev = prev_sa(sig);
    const bool prev_custom = (prev->sa_flags & SA_SIGINFO)
        ? prev->sa_sigaction != nullptr
        : prev->sa_handler != SIG_DFL && prev->sa_handler != SIG_IGN;
    if (prev_custom) {
        if (prev->sa_flags & SA_SIGINFO)
            prev->sa_sigaction(sig, info, uctx);
        else
            prev->sa_handler(sig);
        // Still installed: the previous handler dealt with it (a managed null-ref now unwinds as
        // NullReferenceException). Otherwise it restored a default action for a fatal fault, which
        // the faulting instruction re-triggers once this returns; log it on the way out.
        struct sigaction cur;
        if (sigaction(sig, nullptr, &cur) == 0 && (cur.sa_flags & SA_SIGINFO) &&
            cur.sa_sigaction == sailfish_crash_handler)
            return;
        sailfish_crash_log(sig);
        return;
    }
    sailfish_crash_log(sig);
    // No handler before ours: restore the previous disposition and re-raise (default core dump).
    sigaction(sig, prev, nullptr);
    raise(sig);
}

static void sailfish_crash_log(int sig)
{
    void *frames[64];
    const int n = backtrace(frames, 64);
    char header[160];
    const int len = snprintf(header, sizeof header,
                             "[Sailfish] CRASH signal=%d frames=%d\n", sig, n);
    const int fd = open("/tmp/maui_trace.log", O_WRONLY | O_CREAT | O_APPEND, 0644);
    if (len > 0)
        write(2, header, static_cast<size_t>(len));
    if (fd >= 0 && len > 0)
        write(fd, header, static_cast<size_t>(len));
    char **syms = backtrace_symbols(frames, n);
    for (int i = 0; i < n; ++i) {
        const char *line = syms ? syms[i] : "?";
        const size_t slen = strlen(line);
        write(2, line, slen);
        write(2, "\n", 1);
        if (fd >= 0) {
            write(fd, line, slen);
            write(fd, "\n", 1);
        }
    }
    if (fd >= 0)
        close(fd);
}

extern "C" {

int sailfish_host_init(const char *app_name, sfhost_log_fn log, void *log_user)
{
    if (g.app)
        return 0;
    g.log = log;
    g.log_user = log_user;
    {
        // Each signal keeps the previous handler's flags and mask: PAL's SA_ONSTACK runs a stack-overflow
        // SIGSEGV on its alternate stack, which ours must not leave.
        const int sigs[3] = { SIGSEGV, SIGABRT, SIGBUS };
        for (int i = 0; i < 3; ++i) {
            sigaction(sigs[i], nullptr, &g_prev_sa[i]);
            struct sigaction sa;
            memset(&sa, 0, sizeof sa);
            sa.sa_sigaction = sailfish_crash_handler;
            sa.sa_mask = g_prev_sa[i].sa_mask;
            sa.sa_flags = SA_SIGINFO | (g_prev_sa[i].sa_flags & (SA_ONSTACK | SA_NODEFER | SA_RESTART));
            sigaction(sigs[i], &sa, nullptr);
        }
    }
    // MAUI_SAILFISH_QT_HOST_DIAG=1 enables level 0 (debug / QML console.log) output.
    const char *diag_env = std::getenv("MAUI_SAILFISH_QT_HOST_DIAG");
    g.diag = diag_env && diag_env[0] == '1';
    qInstallMessageHandler(message_handler);

    g.argv0 = strdup(app_name && app_name[0] ? app_name : "sailfishhost");
    g.argv[0] = g.argv0;
    g.app = SailfishApp::application(g.argc, g.argv);
    if (!g.app) {
        g.error = "SailfishApp::application() returned null";
        log_line(2, QString::fromStdString(g.error));
        return -1;
    }
    QGuiApplication::setApplicationName(QString::fromUtf8(g.argv0));
    // MAUI_SAILFISH_QML_PROFILER=<port>[,block]: QML debug server on 127.0.0.1 for qmlprofiler / Qt Creator
    // (docs/profiling.md; needs qt5-qtdeclarative-plugin-qmlinspector). Must run before the first QQmlEngine.
    // Off by default: it opens a port, and "block" holds the first engine until a client attaches.
    if (const char *qp = std::getenv("MAUI_SAILFISH_QML_PROFILER")) {
        const QString spec = QString::fromLatin1(qp);
        const int port = spec.section(QLatin1Char(','), 0, 0).toInt();
        if (port > 0) {
            static QQmlDebuggingEnabler enabler(false);
            const bool block = spec.section(QLatin1Char(','), 1, 1) == QLatin1String("block");
            const bool ok = QQmlDebuggingEnabler::startTcpDebugServer(
                port, block ? QQmlDebuggingEnabler::WaitForClient : QQmlDebuggingEnabler::DoNotWaitForClient,
                QStringLiteral("127.0.0.1"));
            log_line(ok ? 1 : 2, QStringLiteral("QML debug server %1 on 127.0.0.1:%2%3")
                                     .arg(ok ? QStringLiteral("listening") : QStringLiteral("FAILED"))
                                     .arg(port)
                                     .arg(block ? QStringLiteral(" (waiting for a client)") : QString()));
        }
    }
    // The post receiver must be created on the main thread (posts come from any thread).
    g.receiver = new PostReceiver();
    // A clean Qt quit and an external kill look the same from outside; stamp the Qt side.
    QObject::connect(g.app, &QGuiApplication::aboutToQuit, [] {
        trace_file_line("[Sailfish] EXIT trap: QGuiApplication::aboutToQuit");
        notify_quit();
    });
    QObject::connect(g.app, &QGuiApplication::lastWindowClosed, [] {
        trace_file_line("[Sailfish] EXIT trap: lastWindowClosed");
    });
    // Host creation waits for Qt.application.state == Active, and a mid-session drop to
    // Inactive stalls pushes, so trace every transition.
    QObject::connect(g.app, &QGuiApplication::applicationStateChanged,
                     [](Qt::ApplicationState st) {
                         char buf[64];
                         snprintf(buf, sizeof buf,
                                  "[Sailfish] TRACE appState=%d", int(st));
                         trace_file_line(buf);
                         snprintf(buf, sizeof buf, "{\"state\":%d}", int(st));
                         if (g.event_fn)
                             g.event_fn("svc-app-state", buf, g.event_user);
                     });
    log_line(0, QStringLiteral("init ok qt=%1 app=%2 pid=%3")
                    .arg(QLatin1String(qVersion()))
                    .arg(QString::fromUtf8(g.argv0))
                    .arg(QGuiApplication::applicationPid()));
    return 0;
}

int sailfish_host_load(const char *qml_path)
{
    return load_common(qml_path, nullptr, false);
}

int sailfish_host_load_window(const char *qml_path, const char *props_json)
{
    return load_common(qml_path, props_json, true);
}


int sailfish_host_show(void)
{
    QWindow *w = nullptr;
    if (g.view) {
        w = g.view;
    } else {
        attach_window("show");
        w = g.window;
    }
    if (!w) {
        g.error = "no window to show (call load or load_window first)";
        return -1;
    }
    g.window = w;
    // Window mode goes through g.view and never calls attach_window, so hook perf here too.
    hook_perf_signals(w);

    // Silica already shows the window via "visible: true"; view mode shows it itself.
    if (g.view || !w->isVisible())
        w->showFullScreen();
    w->requestActivate();
    log_line(0, QStringLiteral("shown mode=%1 class=%2 geometry=%3,%4 %5x%6 visible=%7")
                    .arg(g.window_mode ? QStringLiteral("window") : QStringLiteral("view"))
                    .arg(QString::fromUtf8(w->metaObject()->className()))
                    .arg(w->x()).arg(w->y()).arg(w->width()).arg(w->height())
                    .arg(w->isVisible() ? QStringLiteral("yes") : QStringLiteral("no")));

    // showFullScreen is async, so log geometry later. Re-raise because after taking over a
    // previous instance's surface the compositor may leave home/switcher on top.
    QTimer::singleShot(500, []() {
        if (!g.window)
            return;
        log_line(0, QStringLiteral("geometry after 500ms: %1,%2 %3x%4")
                        .arg(g.window->x()).arg(g.window->y())
                        .arg(g.window->width()).arg(g.window->height()));
        g.window->raise();
        g.window->requestActivate();
    });
    QTimer::singleShot(1500, []() {
        if (!g.window)
            return;
        g.window->raise();
        g.window->requestActivate();
        log_line(0, QStringLiteral("re-activated at 1500ms geometry=%1,%2 %3x%4\n%5")
                        .arg(g.window->x()).arg(g.window->y())
                        .arg(g.window->width()).arg(g.window->height())
                        .arg(describe_windows()));
    });
    return 0;
}

int sailfish_host_exec(sfhost_tick_fn tick, void *tick_user, int tick_ms)
{
    if (g.shutdown)
        return 0;   // the loop never restarts after teardown
    if (!g.app || (!g.view && !g.root)) {
        g.error = "init/load must be called before exec";
        return -1;
    }
    g.tick = tick;
    g.tick_user = tick_user;
    // Perf clock plus 1 Hz CPU/RSS sampling; the first sample is the baseline.
    g.perf_clock.start();
    sample_cpu_rss();
    if (!g.perf_sampler) {
        g.perf_sampler = new QTimer(g.app);
        QObject::connect(g.perf_sampler, &QTimer::timeout, []() { sample_cpu_rss(); });
        g.perf_sampler->start(1000);
    }
    if (tick && tick_ms > 0) {
        g.event_driven = qgetenv("MAUI_SAILFISH_TICK_POLL") != "1";
        QTimer *timer = new QTimer(g.app);
        g.exec_timer = timer;
        timer->setSingleShot(g.event_driven);
        QObject::connect(timer, &QTimer::timeout, []() {
            ++g.ticks;
            drain_qml_events(); // QML events before the MAUI dispatcher
            if (g.tick)
                g.tick(g.tick_user);
            else if (g.shutdown)
                note_late_callback("tick");   // should be impossible: teardown stops the timer first
        });
        if (g.event_driven) {
            // First tick right away (work queued before the loop); managed arms the rest.
            timer->start(0);
            g.pending_wake = -1;
            g.drain_timer = new QTimer(g.app);
            g.drain_timer->setSingleShot(true);
            g.drain_timer->setInterval(0);
            QObject::connect(g.drain_timer, &QTimer::timeout, []() { drain_qml_events(); });
            // Window's mauiEventSeq NOTIFY -> drain_timer.start(), connected via QMetaMethod (no moc).
            if (g.root) {
                const QMetaObject *mo = g.root->metaObject();
                const int pi = mo->indexOfProperty("mauiEventSeq");
                const QMetaObject *tmo = g.drain_timer->metaObject();
                const int si = tmo->indexOfSlot("start()");
                if (pi >= 0 && mo->property(pi).hasNotifySignal() && si >= 0)
                    QObject::connect(g.root, mo->property(pi).notifySignal(), g.drain_timer, tmo->method(si));
                else
                    log_line(1, QStringLiteral("event-driven drain: mauiEventSeq not found on the root — drains ride the ticks"));
            }
        } else {
            timer->start(tick_ms);
        }
    }
    log_line(0, QStringLiteral("entering event loop (tick_ms=%1)\n%2")
                    .arg(tick_ms).arg(describe_windows()));
    const int rc = g.app->exec();
    log_line(0, QStringLiteral("event loop exited rc=%1 ticks=%2 pointerEvents=%3")
                    .arg(rc).arg(g.ticks).arg(g.pointer_events));
    return rc;
}

void sailfish_host_quit(void)
{
    if (!g.app || !g.receiver)
        return;
    // Thread-safe; shutdown_teardown runs on the Qt thread via PostEvent.
    QCoreApplication::postEvent(g.receiver, new PostEvent(shutdown_teardown, nullptr));
}

int sailfish_host_wake(int delay_ms)
{
    if (g.shutdown || !g.app)
        return 0;
    if (delay_ms < 0)
        delay_ms = 0;
    if (QThread::currentThread() == g.app->thread())
        wake_on_qt(delay_ms);
    else if (g.receiver)
        QCoreApplication::postEvent(g.receiver, new PostEvent(&wake_cb, reinterpret_cast<void *>(static_cast<intptr_t>(delay_ms))));
    return 0;
}

int sailfish_host_post(sfhost_void_fn fn, void *user_data)
{
    if (!g.app || !g.receiver || !fn || g.shutdown) {   // no new work after teardown
        ++g.posts_rejected;
        return fail_args("sailfish_host_post");   // managed frees its GCHandle trampoline
    }
    QCoreApplication::postEvent(g.receiver, new PostEvent(fn, user_data));
    ++g.posts_queued;
    return 0;
}

int sailfish_host_set_context_int(const char *name, long long value)
{
    QQmlEngine *engine = current_engine();
    if (!engine || !name)
        return fail_args("sailfish_host_set_context_int");
    engine->rootContext()->setContextProperty(QString::fromUtf8(name),
                                              static_cast<qint64>(value));
    return 0;
}

int sailfish_host_set_context_string(const char *name, const char *value)
{
    QQmlEngine *engine = current_engine();
    if (!engine || !name)
        return fail_args("sailfish_host_set_context_string");
    engine->rootContext()->setContextProperty(QString::fromUtf8(name),
                                              QString::fromUtf8(value ? value : ""));
    return 0;
}


int sailfish_host_push_page(const char *qml_path, const char *props_json, int immediate)
{
    if (!qml_path || !qml_path[0]) {
        g.error = "empty qml_path";
        return -1;
    }
    ++g.pushes;
    // PageStackAction: Animated = 0, Immediate = 1. ApplicationWindow's initialPage uses
    // animatorPush, which does not progress outside the launcher flow, hence explicit Immediate.
    const QString js = QStringLiteral("pageStack.push(%1, %2, PageStackAction.%3)")
                           .arg(path_to_js_url(qml_path),
                                json_to_js_literal(props_json),
                                immediate ? QStringLiteral("Immediate")
                                          : QStringLiteral("Animated"));
    QString result;
    if (!eval_js(js, &result))
        return -2;
    log_line(0, QStringLiteral("push %1 immediate=%2 -> %3")
                    .arg(QString::fromUtf8(qml_path))
                    .arg(immediate ? 1 : 0)
                    .arg(result));
    return 0;
}

int sailfish_host_pop_page(int immediate)
{
    // page=undefined pops the top page; page=null would unwind to the first page.
    ++g.pops;
    const QString js = immediate
        ? QStringLiteral("pageStack.pop(undefined, PageStackAction.Immediate)")
        : QStringLiteral("pageStack.pop()");
    QString result;
    if (!eval_js(js, &result))
        return -2;
    log_line(0, QStringLiteral("pop immediate=%1 -> %2")
                    .arg(immediate ? 1 : 0).arg(result));
    return 0;
}

int sailfish_host_eval(const char *expression, char *out, int cap)
{
    if (out && cap > 0)
        out[0] = '\0';
    if (!expression || !expression[0]) {
        g.error = "empty expression";
        return -1;
    }
    ++g.evals;
    if (std::strstr(expression, "applyMauiOps"))
        ++g.ops_evals;
    QString result;
    if (!eval_js(QString::fromUtf8(expression), &result))
        return -2;
    const QByteArray utf = result.toUtf8();
    copy_out(utf, out, cap);
    return utf.size();
}

int sailfish_host_window_info(char *buf, int cap)
{
    const QByteArray utf = describe_windows().toUtf8();
    copy_out(utf, buf, cap);
    return utf.size();
}

// Readable screen orientation for rotation traces.
QString orientation_name(Qt::ScreenOrientation o)
{
    switch (o) {
    case Qt::PortraitOrientation:          return QStringLiteral("portrait");
    case Qt::LandscapeOrientation:         return QStringLiteral("landscape");
    case Qt::InvertedPortraitOrientation:  return QStringLiteral("inverted-portrait");
    case Qt::InvertedLandscapeOrientation: return QStringLiteral("inverted-landscape");
    default:                               return QStringLiteral("unknown");
    }
}

// Window/screen geometry in device pixels with devicePixelRatio and orientation; the input
// for managed unit conversion and rotation handling.
int sailfish_host_screen_info(char *buf, int cap)
{
    if (buf && cap > 0)
        buf[0] = '\0';
    if (g.shutdown) {
        g.error = "screen_info: host is shutting down";
        return -1;
    }
    attach_window("screen_info"); // idempotent; the window may not exist yet
    QWindow *w = g.window;
    QScreen *screen = w ? w->screen() : QGuiApplication::primaryScreen();
    if (!w && !screen) {
        g.error = "screen_info: no window and no screen";
        return -1;
    }
    QJsonObject root;
    if (w) {
        QJsonObject win;
        win.insert(QStringLiteral("x"), w->x());
        win.insert(QStringLiteral("y"), w->y());
        win.insert(QStringLiteral("width"), w->width());
        win.insert(QStringLiteral("height"), w->height());
        win.insert(QStringLiteral("dpr"), w->devicePixelRatio());
        root.insert(QStringLiteral("window"), win);
    }
    if (screen) {
        const QRect geom = screen->geometry();
        QJsonObject scr;
        scr.insert(QStringLiteral("name"), screen->name());
        scr.insert(QStringLiteral("x"), geom.x());
        scr.insert(QStringLiteral("y"), geom.y());
        scr.insert(QStringLiteral("width"), geom.width());
        scr.insert(QStringLiteral("height"), geom.height());
        scr.insert(QStringLiteral("dpr"), screen->devicePixelRatio());
        scr.insert(QStringLiteral("orientation"), orientation_name(screen->orientation()));
        scr.insert(QStringLiteral("nativeOrientation"), orientation_name(screen->nativeOrientation()));
        root.insert(QStringLiteral("screen"), scr);
    }
    const QByteArray utf = QJsonDocument(root).toJson(QJsonDocument::Compact);
    copy_out(utf, buf, cap);
    return utf.size();
}

void sailfish_host_set_input_callbacks(sfhost_pointer_fn pointer, sfhost_key_fn key,
                                       void *user_data)
{
    g.pointer = pointer;
    g.key = key;
    g.input_user = user_data;
    log_line(0, QStringLiteral("input callbacks: pointer=%1 key=%2")
                    .arg(pointer ? QStringLiteral("yes") : QStringLiteral("no"))
                    .arg(key ? QStringLiteral("yes") : QStringLiteral("no")));
}

void sailfish_host_set_event_callback(sfhost_event_fn fn, void *user_data)
{
    g.event_fn = fn;
    g.event_user = user_data;
    log_line(0, QStringLiteral("event callback: %1")
                    .arg(fn ? QStringLiteral("yes") : QStringLiteral("no")));
    // The state settles while the window loads and shows, before this callback exists: report where it is now.
    if (fn && g.app) {
        char buf[32];
        snprintf(buf, sizeof buf, "{\"state\":%d}", int(QGuiApplication::applicationState()));
        fn("svc-app-state", buf, user_data);
    }
}

void sailfish_host_inject_pointer(int kind, double x, double y)
{
    if (!g.window)
        return;
    ++g.injects;
    const QPointF local(x, y);
    const QPointF global(x + g.window->x(), y + g.window->y());
    const QEvent::Type type = kind == 0 ? QEvent::MouseButtonPress
                            : kind == 1 ? QEvent::MouseButtonRelease
                                        : QEvent::MouseMove;
    const Qt::MouseButtons buttons = kind == 1 ? Qt::MouseButtons()
                                               : Qt::MouseButtons(Qt::LeftButton);
    // Goes through the QPA window-system interface like real Wayland input, so the button
    // state and the mouse grab survive a drag (a posted QMouseEvent made SilicaFlickable
    // drop its grab). Real timestamps keep flick velocity sane.
    Q_UNUSED(type);
    QWindowSystemInterface::handleMouseEvent(g.window,
                                             static_cast<ulong>(QDateTime::currentMSecsSinceEpoch()),
                                             local, global, buttons);
}

// Posted to the window, so QQuickWindow delivers it to its focusObject (Silica TextField)
// exactly like a hardware key; the InputFilter observes it too.
void sailfish_host_inject_key(int kind, int key, int modifiers, const char *text)
{
    if (!g.window)
        return;
    ++g.injects;
    const QEvent::Type type = kind == 0 ? QEvent::KeyPress : QEvent::KeyRelease;
    const QString textStr = text ? QString::fromUtf8(text) : QString();
    QCoreApplication::postEvent(g.window, new QKeyEvent(type, static_cast<int>(key),
                                                        static_cast<Qt::KeyboardModifiers>(modifiers),
                                                        textStr));
}

int sailfish_host_grab_png(const char *path)
{
    QQuickWindow *qw = qobject_cast<QQuickWindow *>(g.window);
    if (!qw || !path || !path[0]) {
        g.error = "no QQuickWindow to grab or empty path";
        return -1;
    }
    ++g.grabs;   // diagnostics only; must stay 0 in production
    const QImage img = qw->grabWindow();
    if (img.isNull()) {
        g.error = "grabWindow() returned a null image";
        return -1;
    }
    if (!img.save(QString::fromUtf8(path), "PNG")) {
        g.error = std::string("could not save grab to ") + path;
        return -1;
    }
    log_line(0, QStringLiteral("grabbed scene %1x%2 -> %3")
                    .arg(img.width()).arg(img.height()).arg(QString::fromUtf8(path)));
    return 0;
}

// Showcase recorder (tools/sf record): on the render thread after each frame, at most fps
// times a second, glReadPixels the frame and let a pool thread write <dir>/<ms>.png with alpha.
// Frames only render on change, so file names carry the timing. Diagnostics only.
namespace {
class SfLambdaRunnable : public QRunnable {
public:
    explicit SfLambdaRunnable(std::function<void()> f) : m_f(std::move(f)) {}
    void run() override { m_f(); }
private:
    std::function<void()> m_f;
};
struct SfRecorder {
    QMetaObject::Connection conn;
    QString dir;
    qint64 intervalMs = 66;
    int scalePct = 70;
    QElapsedTimer clock;
    qint64 lastMs = -1000000;
    std::atomic<int> frames{0};
    std::atomic<int> pending{0};
};
SfRecorder g_rec;
}

int sailfish_host_record_start(const char *dir, int fps, int scale_pct)
{
    QQuickWindow *qw = qobject_cast<QQuickWindow *>(g.window);
    if (!qw || !dir || !dir[0]) {
        g.error = "record: no QQuickWindow or empty directory";
        return -1;
    }
    if (g_rec.conn)
        QObject::disconnect(g_rec.conn);
    const QString path = QString::fromUtf8(dir);
    if (!QDir().mkpath(path)) {
        g.error = "record: cannot create the directory";
        return -1;
    }
    g_rec.dir = path;
    g_rec.intervalMs = fps > 0 ? 1000 / fps : 66;
    g_rec.scalePct = scale_pct < 10 ? 10 : (scale_pct > 100 ? 100 : scale_pct);
    g_rec.frames = 0;
    g_rec.pending = 0;
    g_rec.lastMs = -1000000;
    g_rec.clock.start();
    g_rec.conn = QObject::connect(qw, &QQuickWindow::afterRendering, qw, [qw]() {
        const qint64 now = g_rec.clock.elapsed();
        if (now - g_rec.lastMs < g_rec.intervalMs || g_rec.pending > 6)
            return;
        QOpenGLContext *ctx = QOpenGLContext::currentContext();
        if (!ctx)
            return;
        g_rec.lastMs = now;
        const qreal dpr = qw->effectiveDevicePixelRatio();
        const QSize size(qRound(qw->width() * dpr), qRound(qw->height() * dpr));
        // The window is translucent (the compositor draws the ambience), so keep the alpha.
        QImage img(size, QImage::Format_RGBA8888_Premultiplied);
        ctx->functions()->glReadPixels(0, 0, size.width(), size.height(), GL_RGBA, GL_UNSIGNED_BYTE, img.bits());
        ++g_rec.pending;
        const QString file = QStringLiteral("%1/%2.png").arg(g_rec.dir).arg(now, 8, 10, QLatin1Char('0'));
        const int pct = g_rec.scalePct;
        QThreadPool::globalInstance()->start(new SfLambdaRunnable([img, file, pct]() {
            QImage out = img.mirrored();   // GL rows run bottom-up
            if (pct < 100)
                out = out.scaledToWidth(out.width() * pct / 100, Qt::SmoothTransformation);
            if (out.save(file, "PNG", 30))
                ++g_rec.frames;
            else
                log_line(1, QStringLiteral("record: cannot write %1").arg(file));
            --g_rec.pending;
        }));
    }, Qt::DirectConnection);
    log_line(0, QStringLiteral("record: %1 every %2 ms at %3%").arg(path).arg(g_rec.intervalMs).arg(g_rec.scalePct));
    return 0;
}

int sailfish_host_record_stop(void)
{
    if (g_rec.conn)
        QObject::disconnect(g_rec.conn);
    g_rec.conn = QMetaObject::Connection();
    QThreadPool::globalInstance()->waitForDone(5000);
    log_line(0, QStringLiteral("record: stopped, %1 frames").arg(g_rec.frames.load()));
    return g_rec.frames.load();
}

int sailfish_host_register_font(const char *path, char *out, int cap)
{
    if (out && cap > 0)
        out[0] = '\0';
    if (!path || !path[0]) {
        g.error = "empty font path";
        return -1;
    }
    const int id = QFontDatabase::addApplicationFont(QString::fromUtf8(path));
    if (id < 0) {
        g.error = std::string("QFontDatabase rejected font ") + path;
        return -1;
    }
    const QStringList families = QFontDatabase::applicationFontFamilies(id);
    const QByteArray utf = families.isEmpty() ? QByteArray() : families.first().toUtf8();
    copy_out(utf, out, cap);
    log_line(0, QStringLiteral("font registered %1 -> %2").arg(QString::fromUtf8(path), QString::fromUtf8(utf)));
    return utf.size();
}

int sailfish_host_render_glyph(const char *family, const char *text, double px,
                               const char *color, const char *out_path)
{
    if (!text || !text[0] || !out_path || !out_path[0] || px <= 0) {
        g.error = "render_glyph: empty text/path or non-positive size";
        return -1;
    }
    QFont font = (family && family[0]) ? QFont(QString::fromUtf8(family)) : QFont();
    font.setPixelSize(qMax(1, qRound(px)));
    const QString glyph = QString::fromUtf8(text);
    const QFontMetricsF fm(font);
        // Square-ish box at least px (icons are square), wide enough for the glyph advance.
    const int w = qMax(qRound(px), qCeil(fm.width(glyph)));
    const int h = qMax(qRound(px), qCeil(fm.height()));
    QImage img(w, h, QImage::Format_ARGB32_Premultiplied);
    img.fill(Qt::transparent);
    QPainter painter(&img);
    painter.setRenderHint(QPainter::Antialiasing);
    painter.setRenderHint(QPainter::TextAntialiasing);
    painter.setFont(font);
    painter.setPen(parse_color(QString::fromUtf8(color && color[0] ? color : "#ffffffff")));
    painter.drawText(QRectF(0, 0, w, h), Qt::AlignCenter, glyph);
    painter.end();
    if (!img.save(QString::fromUtf8(out_path), "PNG")) {
        g.error = std::string("render_glyph: could not save ") + out_path;
        return -1;
    }
    return 0;
}

long long sailfish_host_find_object(const char *object_name)
{
    if (!object_name || !object_name[0] || g.shutdown)
        return 0;
    ++g.find_objects;
    if (!g.root) {
        g.error = "no QML root object (call load/load_window first)";
        return 0;
    }
    QObject *obj = g.root->findChild<QObject *>(QString::fromUtf8(object_name));
    if (!obj) {
        g.error = std::string("object not found: ") + object_name;
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
        g.error = parent ? "find_visual: dead or unknown parent handle"
                         : "no QML root object (call load/load_window first)";
        return 0;
    }
    QQuickItem *rootItem = qobject_cast<QQuickItem *>(base);
    if (!rootItem) {
        g.error = "find_visual: parent is not a QQuickItem";
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
    g.error = std::string("object not found visually: ") + object_name;
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
        g.error = "property value must be valid JSON";
        return -1;
    }
    if (apply_generic_prop(obj, QByteArray(name), json))
        return 0;
    QVariant value;
    if (!json_value_to_variant(json, obj, name, &value))
        return -1;
    if (!obj->setProperty(name, value)) {
        g.error = std::string("no such property: ") + name;
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
        static QHash<QQmlEngine *, QQmlComponent *> components;
        QQmlComponent *component = components.value(engine);
        if (!component) {
            component = new QQmlComponent(engine);
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

static bool apply_generic_prop(QObject *obj, const QByteArray &name, const QJsonValue &value)
{
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
        g.error = "apply_props: expected an ordered JSON array of {name,value}";
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
        g.error = "apply_props failures: " + details;
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
                g.error = std::string("no such property: ") + name;
                return -2;
            }
        }
    } else {
        value = obj->property(name);
    }
    if (!value.isValid()) {
        g.error = std::string("no such property: ") + name;
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
        g.error = "handle is not a QQuickItem";
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
        g.error = QStringLiteral("dead or unknown object handle %1/%2").arg(handle).arg(parent).toUtf8().constData();
        return -3;
    }
    QQuickItem *item = qobject_cast<QQuickItem *>(obj);
    QQuickItem *parentItem = qobject_cast<QQuickItem *>(parentObj);
    if (!item || !parentItem) {
        g.error = "handle is not a QQuickItem";
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
        g.error = "apply_geometry: expected a JSON array of {handle,x,y,w,h,vis}";
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
        g.error = "apply_geometry failures: " + details;
    return failed;
}

// One line's natural width as QQuickText lays it out: QTextLayout with design metrics (Text.QtRendering, the
// default). QFontMetricsF sums hinted advances, a few px short on a long bold title, so a label sized to its own
// text (a centered card, an Auto column) wrapped its last word onto a line the layout never reserved.
static double text_line_width(const QFont &font, const QString &s)
{
    QTextLayout layout(s, font);
    QTextOption option;
    option.setUseDesignMetrics(true);
    option.setWrapMode(QTextOption::NoWrap);
    layout.setTextOption(option);
    layout.beginLayout();
    double width = 0;
    QTextLine line = layout.createLine();
    if (line.isValid()) {
        line.setLineWidth(1e7);
        width = line.naturalTextWidth();
    }
    layout.endLayout();
    return width;
}

// Layout measurement and QML Text share Qt metrics. Greedy wrapping mimics Text.WordWrap /
// Text.WrapAnywhere; lh multiplies the line height like Text.ProportionalHeight. Qt thread.
int sailfish_host_measure_text(const char *json, double *out_w, double *out_h)
{
    if (!json || !out_w || !out_h || g.shutdown || !g.app)
        return fail_args("sailfish_host_measure_text");
    ++g.text_measures;
    QJsonParseError perr;
    const QJsonDocument doc = QJsonDocument::fromJson(QByteArray(json), &perr);
    if (perr.error != QJsonParseError::NoError || !doc.isObject()) {
        g.error = "measure_text: expected a JSON object";
        return -1;
    }
    const QJsonObject o = doc.object();
    const QString text = o.value(QStringLiteral("text")).toString();
    *out_w = 0;
    *out_h = 0;
    if (text.isEmpty())
        return 0;

    QFont font;
    const QString family = o.value(QStringLiteral("family")).toString();
    if (!family.isEmpty())
        font.setFamily(family);
    font.setPixelSize(qMax(1, qRound(o.value(QStringLiteral("px")).toDouble(0))));
    font.setBold(o.value(QStringLiteral("bold")).toInt(0) != 0);
    font.setItalic(o.value(QStringLiteral("italic")).toInt(0) != 0);
    const double ls = o.value(QStringLiteral("ls")).toDouble(0);
    if (ls != 0.0)
        font.setLetterSpacing(QFont::AbsoluteSpacing, ls);
    const double lh = qMax(0.1, o.value(QStringLiteral("lh")).toDouble(1.0));
    const int maxLines = o.value(QStringLiteral("maxLines")).toInt(0);
    const int wrap = o.value(QStringLiteral("wrap")).toInt(1);
    const double maxW = o.value(QStringLiteral("maxW")).toDouble(0);

    // Widths are whole-line design-metric widths, rounded up: QQuickText wraps when the
    // fractional natural width exceeds the item, so summed integer word widths broke lines
    // one pixel early. Line height stays integral (fm.height()).
    const QFontMetrics fm(font);
    const double lineH = fm.height() * lh;

    double widest = 0;
    int totalLines = 0;
    bool capped = false;
    const QStringList paragraphs = text.split(QLatin1Char('\n'));
    for (int p = 0; p < paragraphs.size() && !capped; ++p) {
        const QString &para = paragraphs.at(p);
        if (para.isEmpty() || wrap == 0 || maxW <= 0) {
            // Empty paragraph, NoWrap or unbounded width: one line.
            if (!para.isEmpty())
                widest = qMax(widest, text_line_width(font, para));
            ++totalLines;
            if (maxLines > 0 && totalLines >= maxLines)
                capped = true;
            continue;
        }
        QString line;
        if (wrap == 2) {
            // WrapAnywhere: break between characters.
            for (int i = 0; i < para.size(); ++i) {
                const QString candidate = line + para.at(i);
                const double cw = text_line_width(font, candidate);
                if (!line.isEmpty() && cw > maxW) {
                    ++totalLines;
                    line = QString(para.at(i));
                    widest = qMax(widest, text_line_width(font, line));
                } else {
                    line = candidate;
                    widest = qMax(widest, cw);
                }
                if (maxLines > 0 && totalLines >= maxLines) {
                    capped = true;
                    break;
                }
            }
        } else {
            // WordWrap: greedy break between words.
            const QStringList words = para.split(QLatin1Char(' '), QString::SkipEmptyParts);
            for (int i = 0; i < words.size(); ++i) {
                const QString candidate = line.isEmpty() ? words.at(i) : line + QLatin1Char(' ') + words.at(i);
                const double cw = text_line_width(font, candidate);
                if (!line.isEmpty() && cw > maxW) {
                    ++totalLines;
                    line = words.at(i);
                    widest = qMax(widest, text_line_width(font, line));
                    if (maxLines > 0 && totalLines >= maxLines) {
                        capped = true;
                        break;
                    }
                } else {
                    line = candidate;
                    widest = qMax(widest, cw);
                }
            }
        }
        if (!capped)
            ++totalLines;   // last line of the paragraph
        if (maxLines > 0 && totalLines >= maxLines)
            capped = true;
    }
    widest = std::ceil(widest - 0.001);
    *out_w = widest;
    *out_h = totalLines * lineH;
    return 0;
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

long long sailfish_host_tick_count(void)
{
    return g.ticks;
}

/* --- Lifecycle diagnostics --- */

int sailfish_host_diag_app_state(int state, int activate)
{
    if (g.shutdown || !g.app) {
        g.error = "diag_app_state: host is down (teardown)";
        return -1;
    }
    // Same QPA path lipstick/Wayland use to drive app state and window activation.
    if (activate >= 0)
        QWindowSystemInterface::handleWindowActivated(activate ? g.window : nullptr);
    if (state >= 0)
        QWindowSystemInterface::handleApplicationStateChanged(
            static_cast<Qt::ApplicationState>(state), /*forcePropagate=*/true);
    log_line(0, QStringLiteral("diag: app state -> %1, activate -> %2 (QPA window-system events)")
                    .arg(state).arg(activate));
    return 0;
}

int sailfish_host_diag_stats(char *buf, int cap)
{
    if (!buf || cap <= 0)
        return -1;
    const QString json = QStringLiteral(
        "{\"registry\":%1,\"postsQueued\":%2,\"postsRun\":%3,\"postsRejected\":%4,"
        "\"lateCallbacks\":%5,\"ticks\":%6,\"pointerEvents\":%7,\"shutdown\":%8}")
        .arg(g.objects.size())
        .arg(g.posts_queued.load())
        .arg(g.posts_run.load())
        .arg(g.posts_rejected.load())
        .arg(g.late_callbacks.load())
        .arg(g.ticks)
        .arg(g.pointer_events)
        .arg(g.shutdown ? 1 : 0);
    const QByteArray utf = json.toUtf8();
    return copy_out(utf, buf, cap);
}

/* --- Perf diagnostics --- */

// Takes a fresh CPU/RSS sample and counts the QML tree. avgFrameUs is over frameSwapped
// intervals (n-1). Diagnostics only (MAUI_SAILFISH_QT_HOST_PERF_DIAG).
int sailfish_host_perf_stats(char *buf, int cap)
{
    if (!buf || cap <= 0)
        return -1;
    sample_cpu_rss();
    long long objects = 0;
    long long items = 0;
    if (g.root && !g.shutdown) {
        QSet<QObject *> seen;
        count_tree(g.root, seen, objects, items);
    }
    const long long fr = g.frames.load();
    const long long avg_frame_us = fr > 1 ? g.frame_sum_us.load() / (fr - 1) : 0;
    const long long sy = g.sync_count.load();
    const long long avg_sync_us = sy > 0 ? g.sync_sum_us.load() / sy : 0;
    const long long uptime_ms = g.perf_clock.isValid() ? g.perf_clock.elapsed() : 0;
    const QString json = QStringLiteral(
        "{\"uptimeMs\":%1,\"firstFrameMs\":%2,\"frames\":%3,\"avgFrameUs\":%4,"
        "\"maxFrameUs\":%5,\"slowFrames\":%6,\"syncCount\":%7,\"avgSyncUs\":%8,"
        "\"maxSyncUs\":%9,\"cpuMs\":%10,\"rssKb\":%11,\"peakRssKb\":%12,"
        "\"qmlObjects\":%13,\"qmlItems\":%14,\"renderLoop\":\"%15\","
        "\"registry\":%16,\"ticks\":%17,\"evals\":%18,\"opsEvals\":%19,"
        "\"drains\":%20,\"propertySets\":%21,\"propsBatches\":%22,\"propsApplied\":%23,"
        "\"geometryBatches\":%24,\"geometryEntries\":%25,\"textMeasures\":%26,"
        "\"findObjects\":%27,\"pushes\":%28,\"pops\":%29,\"grabs\":%30,"
        "\"injects\":%31,\"destroys\":%32,\"shutdown\":%33,\"geometryReads\":%34,"
        "\"surfaceCommits\":%35,\"surfaceCommitUs\":%36,\"surfaceUploads\":%37,\"surfaceUploadUs\":%38,"
        "\"surfaceUploadMaxUs\":%39,\"surfaceTiles\":%40,\"surfaceTouches\":%41,\"frameRequests\":%42,"
        "\"frameCallbacks\":%43,\"surfaceMaxTexture\":%44}")
        .arg(uptime_ms)
        .arg(g.first_frame_ms.load())
        .arg(fr)
        .arg(avg_frame_us)
        .arg(g.frame_max_us.load())
        .arg(g.slow_frames.load())
        .arg(sy)
        .arg(avg_sync_us)
        .arg(g.sync_max_us.load())
        .arg(g.cpu_ms.load())
        .arg(g.rss_kb.load())
        .arg(g.peak_rss_kb.load())
        .arg(objects)
        .arg(items)
        .arg(QString::fromLatin1(render_loop_name()))
        .arg(g.objects.size())
        .arg(g.ticks)
        .arg(g.evals.load())
        .arg(g.ops_evals.load())
        .arg(g.drains.load())
        .arg(g.property_sets.load())
        .arg(g.props_batches.load())
        .arg(g.props_applied.load())
        .arg(g.geometry_batches.load())
        .arg(g.geometry_entries.load())
        .arg(g.text_measures.load())
        .arg(g.find_objects.load())
        .arg(g.pushes.load())
        .arg(g.pops.load())
        .arg(g.grabs.load())
        .arg(g.injects.load())
        .arg(g.destroys.load())
        .arg(g.shutdown ? 1 : 0)
        .arg(g.geometry_reads.load())
        .arg(g.surface_commits)
        .arg(g.surface_commit_us)
        .arg(g.surface_uploads.load())
        .arg(g.surface_upload_us.load())
        .arg(g.surface_upload_max_us.load())
        .arg(g.surface_tiles.load())
        .arg(g.surface_touches)
        .arg(g.frame_requests)
        .arg(g.frame_callbacks)
        .arg(g.surface_max_texture.load());
    const QByteArray utf = json.toUtf8();
    return copy_out(utf, buf, cap);
}

int sailfish_host_last_error(char *buf, int cap)
{
    if (!buf || cap <= 0)
        return -1;
    copy_out(QByteArray::fromStdString(g.error), buf, cap);
    return static_cast<int>(g.error.size());
}

// Clipboard and URL opening for managed services. Called on the MAUI main thread, which is
// the Qt GUI thread, so direct QClipboard/QDesktopServices calls are safe.
int sailfish_host_clipboard_set(const char *text)
{
    if (!text)
        return fail_args("sailfish_host_clipboard_set");
    const QString s = QString::fromUtf8(text);
    // Can run before sailfish_host_init, and QtWayland registers the selection
    // asynchronously, so keep an in-process mirror for in-app round trips; the real
    // clipboard is best-effort cross-app.
    g.clipboardMirror = s.toUtf8().toStdString();
    if (QGuiApplication::instance())
        QGuiApplication::clipboard()->setText(s);
    return 0;
}

int sailfish_host_clipboard_get(char *buf, int cap)
{
    if (!buf || cap <= 0)
        return fail_args("sailfish_host_clipboard_get");
    QString s;
    if (QGuiApplication::instance())
        s = QGuiApplication::clipboard()->text();
    if (s.isEmpty() && !g.clipboardMirror.empty())
        s = QString::fromUtf8(g.clipboardMirror.c_str());
    const QByteArray utf = s.toUtf8();
    return copy_out(utf, buf, cap);
}

int sailfish_host_open_url(const char *url)
{
    if (!g.app || !url || !url[0])
        return fail_args("sailfish_host_open_url");
    return QDesktopServices::openUrl(QUrl(QString::fromUtf8(url))) ? 0 : SFHOST_E_ARGS;
}

// --- Drawing surfaces ---
// A surface is a child item of a host that fills it and shows RGBA8888-premultiplied pixels drawn by managed code
// (SkiaSharp's raster canvas). The pixels are copied once into a staging image on commit, so the caller can reuse its
// buffer at once, and uploaded on the render thread into persistent textures with glTexSubImage2D; nothing is read
// back. A canvas larger than GL_MAX_TEXTURE_SIZE is split into tiles. Sampling is nearest, as Android draws the
// canvas bitmap without a filtering paint, so a surface at 1:1 stays pixel-exact at fractional positions.
namespace {

class SurfaceTexture : public QSGTexture
{
public:
    ~SurfaceTexture() override
    {
        if (m_id)
            if (QOpenGLContext *ctx = QOpenGLContext::currentContext())
                ctx->functions()->glDeleteTextures(1, &m_id);
    }

    int textureId() const override { return static_cast<int>(m_id); }
    QSize textureSize() const override { return m_size; }
    bool hasAlphaChannel() const override { return true; }
    bool hasMipmaps() const override { return false; }

    // Render thread (updatePaintNode, context current); the image shares the staging data until bind() uploads it.
    // The id exists from here on: the renderer batches nodes whose materials compare equal by textureId(), so
    // surfaces first committed in the same frame would otherwise all draw the first one's texture.
    void setPixels(const QImage &image)
    {
        m_pending = image;
        if (!m_id)
            QOpenGLContext::currentContext()->functions()->glGenTextures(1, &m_id);
    }

    void bind() override
    {
        QOpenGLFunctions *f = QOpenGLContext::currentContext()->functions();
        if (!m_id)
            f->glGenTextures(1, &m_id);
        const bool fresh = !m_size.isValid();
        f->glBindTexture(GL_TEXTURE_2D, m_id);
        if (!m_pending.isNull()) {
            QElapsedTimer t;
            t.start();
            const int w = m_pending.width();
            const int h = m_pending.height();
            // QImage rows of 32-bit pixels are tightly packed, which GLES2 needs (no GL_UNPACK_ROW_LENGTH).
            if (m_size != m_pending.size()) {
                f->glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA, w, h, 0, GL_RGBA, GL_UNSIGNED_BYTE, m_pending.constBits());
                m_size = m_pending.size();
            } else {
                f->glTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, w, h, GL_RGBA, GL_UNSIGNED_BYTE, m_pending.constBits());
            }
            m_pending = QImage();
            const long long us = t.nsecsElapsed() / 1000LL;
            ++g.surface_uploads;
            g.surface_upload_us += us;
            long long mx = g.surface_upload_max_us.load();
            while (us > mx && !g.surface_upload_max_us.compare_exchange_weak(mx, us)) { }
        }
        updateBindOptions(fresh);
    }

private:
    GLuint m_id = 0;
    QSize m_size;
    QImage m_pending;
};

class SurfaceItem : public QQuickItem
{
public:
    explicit SurfaceItem(QQuickItem *host) : QQuickItem(host)
    {
        setObjectName(QStringLiteral("mauiSurface"));
        setFlag(ItemHasContents, true);
        setSize(QSizeF(host->width(), host->height()));
        // Without pixels the surface fills its host (it takes touch there); with pixels it has their size, 1:1 from
        // the host's top-left corner. The managed side sizes the bitmap from MAUI Android's pixel rounding of the
        // view, which can differ by a pixel from the host item's own rounding; stretching would double a column.
        QObject::connect(host, &QQuickItem::widthChanged, this, [this, host]() {
            if (m_staging.isNull())
                setWidth(host->width());
        });
        QObject::connect(host, &QQuickItem::heightChanged, this, [this, host]() {
            if (m_staging.isNull())
                setHeight(host->height());
        });
    }

    // Qt thread. width/height 0 frees the pixels (a hidden or empty canvas holds no memory, as on Android).
    void commit(const uchar *pixels, int width, int height, int stride)
    {
        if (width <= 0 || height <= 0 || !pixels) {
            m_staging = QImage();
            if (QQuickItem *host = parentItem())
                setSize(QSizeF(host->width(), host->height()));
        } else {
            setSize(QSizeF(width, height));
            const QSize size(width, height);
            // Still shared with the render thread (the previous frame is not uploaded yet): a fresh image instead of
            // the copy bits() would make, since every pixel is overwritten anyway.
            if (m_staging.size() != size || !m_staging.isDetached())
                m_staging = QImage(size, QImage::Format_RGBA8888_Premultiplied);
            const int row = width * 4;
            uchar *dst = m_staging.bits();
            const int dstStride = m_staging.bytesPerLine();
            if (stride == row && dstStride == row) {
                std::memcpy(dst, pixels, static_cast<size_t>(row) * height);
            } else {
                for (int y = 0; y < height; ++y)
                    std::memcpy(dst + y * dstStride, pixels + y * stride, static_cast<size_t>(row));
            }
        }
        m_dirty = true;
        update();
    }

    // Touch follows SkiaSharp's Android SKTouchHandler: the first press decides whether the item keeps the gesture
    // (handled) or Qt passes it on to the items below and the parents, as an unhandled ACTION_DOWN does on Android.
    void setTouchEnabled(bool enabled)
    {
        if (m_touch == enabled)
            return;
        m_touch = enabled;
        setAcceptedMouseButtons(enabled ? Qt::LeftButton | Qt::RightButton | Qt::MiddleButton : Qt::NoButton);
        if (!enabled)
            cancelAll();
    }

protected:
    void touchEvent(QTouchEvent *e) override
    {
        if (!m_touch) {
            e->ignore();
            return;
        }
        const QList<QTouchEvent::TouchPoint> points = e->touchPoints();
        if (e->type() == QEvent::TouchCancel) {
            cancelAll();
            e->accept();
            return;
        }
        if (e->type() == QEvent::TouchBegin) {
            m_points.clear();
            bool handled = false;
            for (const QTouchEvent::TouchPoint &p : points)
                if (p.state() == Qt::TouchPointPressed) {
                    m_points.insert(p.id(), p.pos());
                    handled = send(0, p.id(), p.pos(), p.pressure(), 0, 0) || handled;
                }
            if (!handled) {
                m_points.clear();
                e->ignore();
                return;
            }
            watchAncestors();
            e->accept();
            return;
        }
        if (m_points.isEmpty()) {
            e->ignore();
            return;
        }
        // Qt 5.6 lets a Flickable ancestor take the gesture through its child mouse filter while this item still
        // holds the touch points; on Android the parent's interception cancels the child. Do the same.
        if (ancestorTookOver()) {
            cancelAll();
            ungrabTouchPoints();
            e->ignore();
            return;
        }
        // A Qt update can carry presses, moves and releases at once; Android sends them as separate events, in this
        // order, and a move reports every pointer still down.
        bool moved = false;
        for (const QTouchEvent::TouchPoint &p : points) {
            if (p.state() == Qt::TouchPointPressed && !m_points.contains(p.id())) {
                m_points.insert(p.id(), p.pos());
                send(0, p.id(), p.pos(), p.pressure(), 0, 0);
            } else if (p.state() == Qt::TouchPointMoved) {
                moved = true;
            }
        }
        if (moved)
            for (const QTouchEvent::TouchPoint &p : points)
                if (m_points.contains(p.id()) && (p.state() == Qt::TouchPointMoved || p.state() == Qt::TouchPointStationary)) {
                    m_points[p.id()] = p.pos();
                    send(1, p.id(), p.pos(), p.pressure(), 0, 0);
                }
        for (const QTouchEvent::TouchPoint &p : points)
            if (p.state() == Qt::TouchPointReleased && m_points.remove(p.id()))
                send(2, p.id(), p.pos(), p.pressure(), 0, 0);
        if (e->type() == QEvent::TouchEnd)
            m_points.clear();
        if (m_points.isEmpty())
            unwatchAncestors();
        e->accept();
    }

    // A parent (a Flickable) took the gesture over.
    void touchUngrabEvent() override { cancelAll(); }

    // A real mouse; Qt's mouse synthesized from touch is left alone, since touch already reported it.
    void mousePressEvent(QMouseEvent *e) override
    {
        if (!m_touch || e->source() != Qt::MouseEventNotSynthesized || m_mouseDown) {
            e->ignore();
            return;
        }
        m_mouseButton = e->button() == Qt::RightButton ? 2 : e->button() == Qt::MiddleButton ? 1 : 0;
        if (!send(0, 0, e->localPos(), 1.0, 1, m_mouseButton)) {
            e->ignore();
            return;
        }
        m_mouseDown = true;
        e->accept();
    }

    void mouseMoveEvent(QMouseEvent *e) override
    {
        if (!m_mouseDown || e->source() != Qt::MouseEventNotSynthesized) {
            e->ignore();
            return;
        }
        send(1, 0, e->localPos(), 1.0, 1, m_mouseButton);
        e->accept();
    }

    void mouseReleaseEvent(QMouseEvent *e) override
    {
        if (!m_mouseDown || e->source() != Qt::MouseEventNotSynthesized) {
            e->ignore();
            return;
        }
        m_mouseDown = false;
        send(2, 0, e->localPos(), 0.0, 1, m_mouseButton);
        e->accept();
    }

    void mouseUngrabEvent() override
    {
        if (!m_mouseDown)
            return;
        m_mouseDown = false;
        send(3, 0, m_lastMouse, 0.0, 1, m_mouseButton);
    }

    void geometryChanged(const QRectF &newGeometry, const QRectF &oldGeometry) override
    {
        QQuickItem::geometryChanged(newGeometry, oldGeometry);
        if (newGeometry.size() != oldGeometry.size())
            update();   // tile rects follow the item size
    }

    QSGNode *updatePaintNode(QSGNode *old, UpdatePaintNodeData *) override
    {
        if (m_staging.isNull() || width() <= 0 || height() <= 0) {
            delete old;
            m_tileCols = m_tileRows = 0;
            return nullptr;
        }
        if (s_maxTexture == 0) {
            GLint max = 0;
            QOpenGLContext::currentContext()->functions()->glGetIntegerv(GL_MAX_TEXTURE_SIZE, &max);
            s_maxTexture = max > 0 ? max : 2048;
            g.surface_max_texture.store(s_maxTexture);
        }
        const int w = m_staging.width();
        const int h = m_staging.height();
        const int cols = (w + s_maxTexture - 1) / s_maxTexture;
        const int rows = (h + s_maxTexture - 1) / s_maxTexture;
        QSGNode *root = old;
        if (!root || cols != m_tileCols || rows != m_tileRows) {
            delete old;
            root = new QSGNode();
            for (int i = 0; i < cols * rows; ++i) {
                auto *tile = new QSGSimpleTextureNode();
                tile->setTexture(new SurfaceTexture());
                tile->setOwnsTexture(true);
                tile->setFiltering(QSGTexture::Nearest);
                root->appendChildNode(tile);
            }
            m_tileCols = cols;
            m_tileRows = rows;
            m_dirty = true;
        }
        // Pixels map onto the item's size; they match it 1:1 when managed code sized the canvas from the same rect.
        const qreal sx = width() / w;
        const qreal sy = height() / h;
        int i = 0;
        for (QSGNode *n = root->firstChild(); n; n = n->nextSibling(), ++i) {
            auto *tile = static_cast<QSGSimpleTextureNode *>(n);
            const int tx = (i % cols) * s_maxTexture;
            const int ty = (i / cols) * s_maxTexture;
            const QRect px(tx, ty, qMin(s_maxTexture, w - tx), qMin(s_maxTexture, h - ty));
            if (m_dirty) {
                auto *texture = static_cast<SurfaceTexture *>(tile->texture());
                texture->setPixels(cols * rows == 1 ? m_staging : m_staging.copy(px));
                tile->markDirty(QSGNode::DirtyMaterial);
            }
            tile->setRect(QRectF(px.x() * sx, px.y() * sy, px.width() * sx, px.height() * sy));
        }
        if (m_dirty && cols * rows > 1)
            g.surface_tiles.store(cols * rows);
        m_dirty = false;
        return root;
    }

private:
    bool send(int action, int pointer, const QPointF &pos, qreal pressure, int device, int button)
    {
        if (device == 1)
            m_lastMouse = pos;
        ++g.surface_touches;
        QQuickItem *host = parentItem();
        if (!g.surface_touch_fn || !host)
            return false;
        const long long handle = static_cast<long long>(reinterpret_cast<qintptr>(host));
        return g.surface_touch_fn(handle, action, pointer, pos.x(), pos.y(), pressure, device, button,
                                  g.surface_touch_user) != 0;
    }

    // An ancestor grabbed the mouse Qt synthesizes for its child filter, or an ancestor Flickable started dragging.
    bool ancestorTookOver() const
    {
        QQuickItem *grabber = window() ? window()->mouseGrabberItem() : nullptr;
        for (QQuickItem *p = parentItem(); p; p = p->parentItem()) {   // QQuickItem::isAncestorOf is Qt 5.7+
            if (p == grabber)
                return true;
            const QVariant dragging = p->property("dragging");
            if (dragging.isValid() && dragging.toBool())
                return true;
        }
        return false;
    }

    // Once a Flickable ancestor starts dragging, Qt stops sending this item the gesture without an ungrab; its
    // dragging NOTIFY starts a zero-delay check (connected via QMetaMethod, no moc) that cancels the touch points.
    void watchAncestors()
    {
        unwatchAncestors();
        if (!m_stealCheck) {
            m_stealCheck = new QTimer(this);
            m_stealCheck->setSingleShot(true);
            m_stealCheck->setInterval(0);
            QObject::connect(m_stealCheck, &QTimer::timeout, this, [this]() {
                if (!m_points.isEmpty() && ancestorTookOver()) {
                    cancelAll();
                    ungrabTouchPoints();
                }
            });
        }
        const QMetaObject *tmo = m_stealCheck->metaObject();
        const QMetaMethod start = tmo->method(tmo->indexOfSlot("start()"));
        for (QQuickItem *p = parentItem(); p; p = p->parentItem()) {
            const QMetaObject *mo = p->metaObject();
            const int pi = mo->indexOfProperty("dragging");
            if (pi >= 0 && mo->property(pi).hasNotifySignal())
                m_watched.append(QObject::connect(p, mo->property(pi).notifySignal(), m_stealCheck, start));
        }
    }

    void unwatchAncestors()
    {
        for (const QMetaObject::Connection &c : m_watched)
            QObject::disconnect(c);
        m_watched.clear();
    }

    void cancelAll()
    {
        unwatchAncestors();
        const QHash<int, QPointF> points = m_points;
        m_points.clear();
        for (auto it = points.constBegin(); it != points.constEnd(); ++it)
            send(3, it.key(), it.value(), 0.0, 0, 0);
        mouseUngrabEvent();
    }

    bool m_touch = false;
    QHash<int, QPointF> m_points;   // touch points this item holds, by Qt id
    QTimer *m_stealCheck = nullptr;
    QList<QMetaObject::Connection> m_watched;
    bool m_mouseDown = false;
    int m_mouseButton = 0;
    QPointF m_lastMouse;
    QImage m_staging;
    bool m_dirty = false;
    int m_tileCols = 0;
    int m_tileRows = 0;
    static int s_maxTexture;   // render thread; read once from the first GL context
};

int SurfaceItem::s_maxTexture = 0;

// Host item -> its surface; entries go with either object.
QHash<QObject *, QPointer<SurfaceItem>> g_surfaces;

SurfaceItem *surface_for(QQuickItem *host, bool create)
{
    if (SurfaceItem *existing = g_surfaces.value(host).data())
        return existing;
    if (!create)
        return nullptr;
    auto *surface = new SurfaceItem(host);
    g_surfaces.insert(host, QPointer<SurfaceItem>(surface));
    QObject::connect(host, &QObject::destroyed, [host]() { g_surfaces.remove(host); });
    return surface;
}

} // namespace

int sailfish_host_surface_commit(long long handle, const void *pixels, int width, int height, int stride)
{
    QQuickItem *host = qobject_cast<QQuickItem *>(require_handle(handle));
    if (!host)
        return SFHOST_E_DEAD_HANDLE;
    const bool empty = width <= 0 || height <= 0 || !pixels;
    if (!empty && stride < width * 4)
        return fail_args("sailfish_host_surface_commit");
    SurfaceItem *surface = surface_for(host, !empty);
    if (!surface)
        return SFHOST_OK;   // nothing drawn yet, nothing to free
    QElapsedTimer t;
    t.start();
    surface->commit(static_cast<const uchar *>(pixels), width, height, stride);
    ++g.surface_commits;
    g.surface_commit_us += t.nsecsElapsed() / 1000LL;
    return SFHOST_OK;
}

void sailfish_host_set_surface_touch_callback(sfhost_surface_touch_fn fn, void *user_data)
{
    g.surface_touch_fn = fn;
    g.surface_touch_user = user_data;
}

int sailfish_host_surface_set_touch(long long handle, int enabled)
{
    QQuickItem *host = qobject_cast<QQuickItem *>(require_handle(handle));
    if (!host)
        return SFHOST_E_DEAD_HANDLE;
    SurfaceItem *surface = surface_for(host, enabled != 0);
    if (surface)
        surface->setTouchEnabled(enabled != 0);
    return SFHOST_OK;
}

void sailfish_host_inject_touch(int count, const int *ids, const double *xy, const int *states)
{
    if (!g.window || count <= 0 || !ids || !xy || !states)
        return;
    ++g.injects;
    static QTouchDevice *device = nullptr;
    if (!device) {
        device = new QTouchDevice();
        device->setName(QStringLiteral("maui-inject"));
        device->setType(QTouchDevice::TouchScreen);
        device->setCapabilities(QTouchDevice::Position | QTouchDevice::Area | QTouchDevice::Pressure
                                | QTouchDevice::NormalizedPosition);
        QWindowSystemInterface::registerTouchDevice(device);
    }
    const QRect screen = g.window->screen() ? g.window->screen()->geometry() : QRect(0, 0, 1, 1);
    QList<QWindowSystemInterface::TouchPoint> points;
    for (int i = 0; i < count; ++i) {
        QWindowSystemInterface::TouchPoint p;
        p.id = ids[i];
        p.state = static_cast<Qt::TouchPointState>(states[i]);
        const QPointF global(xy[2 * i] + g.window->x(), xy[2 * i + 1] + g.window->y());
        p.area = QRectF(global.x() - 2, global.y() - 2, 4, 4);
        p.normalPosition = QPointF(global.x() / qMax(1, screen.width()), global.y() / qMax(1, screen.height()));
        p.pressure = p.state == Qt::TouchPointReleased ? 0.0 : 1.0;
        points.append(p);
    }
    QWindowSystemInterface::handleTouchEvent(g.window, static_cast<ulong>(QDateTime::currentMSecsSinceEpoch()),
                                             device, points);
}

void sailfish_host_set_frame_callback(sfhost_frame_fn fn, void *user_data)
{
    g.frame_fn = fn;
    g.frame_user = user_data;
}

int sailfish_host_request_frame(void)
{
    if (g.shutdown)
        return SFHOST_E_ARGS;
    g.frame_requested = true;
    ++g.frame_requests;
    // Every request schedules a frame (cheap: Qt coalesces them), so a frame that never came cannot leave the
    // request flag set with nothing scheduled.
    hook_frame_signal(g.window);
    if (QQuickWindow *qw = qobject_cast<QQuickWindow *>(g.window))
        qw->update();
    return SFHOST_OK;
}

} // extern "C"

