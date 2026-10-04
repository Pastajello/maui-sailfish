// host_internal.h — what the shim's source files share: the Qt includes, the host state and the helpers
// (namespace sfhost, defined in host_core.cpp). The C ABI itself is sailfish_host.h.

#pragma once

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
#include <QNetworkReply>
#include <QTimer>
#include <QStandardPaths>
#include <QQmlError>
#include <QQmlExpression>
#include <QQmlProperty>
#include <QQmlListReference>
#include <QMatrix4x4>
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
#include <mutex>
#include <string>
#include <unistd.h>

namespace sfhost {

struct HostState {
    // app, receiver and shutdown are read by sailfish_host_post/wake/quit on any thread: atomics, loaded once there.
    std::atomic<QGuiApplication *> app{nullptr};
    QQuickView *view = nullptr;       // view mode
    QQmlEngine *engine = nullptr;     // fallback for current_engine()
    QObject *root = nullptr;          // QML root (ApplicationWindow / view root)
    QWindow *window = nullptr;        // main Wayland window (not cover, not wallpaper)
    std::atomic<QObject *> receiver{nullptr};   // target of sailfish_host_post (QEvent::User)
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
    std::atomic<bool> shutdown{false};   // teardown started (set on the Qt thread, read anywhere)
    // Handle registry: QPointer turns objects destroyed by QML into dead handles (-3).
    QHash<void *, QPointer<QObject>> objects;
    std::string error;                 // last error text: only through set_error/error_text (any thread)
    std::mutex error_lock;
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
    std::atomic<long long> ops_evals{0};    // applyMauiOps batches (through eval or invoke)
    std::atomic<long long> invokes{0};      // sailfish_host_invoke calls
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
    QByteArray overflow;              // the last eval/invoke result that did not fit (sailfish_host_last_result)
    int argc = 1;
    char *argv0 = nullptr;
    char *argv[2] = {nullptr, nullptr};
};

extern HostState g;

// The helpers (host_core.cpp).
void set_error(std::string text);
std::string error_text();
int fail_args(const char *api);
void log_line(int level, const QString &text);
void message_handler(QtMsgType type, const QMessageLogContext &ctx, const QString &msg);
void note_late_callback(const char *what);
QQmlEngine *current_engine();
void sample_cpu_rss();
void count_tree(QObject *o, QSet<QObject *> &seen, long long &objects, long long &items);
const char *render_loop_name();
void hook_perf_signals(QWindow *w);
void hook_frame_signal(QWindow *w);
void emit_pointer(int kind, double x, double y, double delta, int extra);
void wake_on_qt(int ms);
void wake_cb(void *data);
void apply_context_props(QQmlEngine *engine, const char *props_json);
QString json_to_js_literal(const char *json);
QString path_to_js_url(const char *path);
QString class_chain(const QObject *obj);
bool eval_js(const QString &js, QString *out);
void drain_qml_events();
void notify_quit();
QObject *resolve_handle(long long handle);
QObject *require_handle(long long handle);
int copy_out(const QByteArray &utf, char *buf, int cap);
void keep_overflow(const QByteArray &utf, int cap);
long long register_handle(QObject *obj);
QColor parse_color(const QString &text);
bool json_value_to_variant(const QJsonValue &v, QObject *obj, const char *name, QVariant *out);
bool json_parse_value(const char *json, QJsonValue *out);
QWindow *find_main_window();
void attach_window(const char *why);
QString describe_windows();
void trace_file_line(const char *line);
void arm_stall_watchdog(QNetworkReply *reply);
void install_http_cache(QQmlEngine *engine);
int load_common(const char *qml_path, const char *props_json, bool silica_window);
void shutdown_teardown(void *);

// --- post ---
// postEvent is thread-safe, and a custom QEvent::User handled in event() needs no moc.
class PostEvent : public QEvent
{
public:
    PostEvent(sfhost_void_fn f, void *u) : QEvent(QEvent::User), fn(f), user(u) {}
    sfhost_void_fn fn;
    void *user;
};

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
            // Not counted in postsRun: the teardown event is sent by sailfish_host_quit, bypassing postsQueued.
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

} // namespace sfhost
