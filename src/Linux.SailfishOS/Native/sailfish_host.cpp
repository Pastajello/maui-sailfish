// sailfish_host.cpp — implementation of the Qt Quick/Silica host C ABI (see sailfish_host.h): the lifecycle
// (init, load, show, exec, quit), the loop (wake, post), JS (eval, invoke), page pushes, clipboard and URLs, and the
// crash trap. The other exports live by family in host_handles.cpp, host_text.cpp, host_surface.cpp and
// host_diag.cpp; what they share is host_internal.h (defined in host_core.cpp).
//
// Cross-built with zig for aarch64 Sailfish OS (tools/sf native-build, sysroot from
// tools/sf sysroot). No moc: the shim overrides eventFilter/event and connects lambdas
// to existing Qt metaobjects instead of declaring Q_OBJECT.

#include "host_internal.h"

using namespace sfhost;

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
        set_error("SailfishApp::application() returned null");
        log_line(2, QString::fromStdString(error_text()));
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
    // Inactive stalls pushes, so trace every transition. Managed hears the state from MauiShell.qml
    // (svc-app-state, its only source), not from here.
    QObject::connect(g.app, &QGuiApplication::applicationStateChanged,
                     [](Qt::ApplicationState st) {
                         char buf[64];
                         snprintf(buf, sizeof buf,
                                  "[Sailfish] TRACE appState=%d", int(st));
                         trace_file_line(buf);
                     });
    log_line(0, QStringLiteral("init ok qt=%1 app=%2 pid=%3")
                    .arg(QLatin1String(qVersion()))
                    .arg(QString::fromUtf8(g.argv0))
                    .arg(QGuiApplication::applicationPid()));
    return 0;
}

int sailfish_host_abi_version(void)
{
    return SFHOST_ABI_VERSION;
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
        set_error("no window to show (call load or load_window first)");
        return -1;
    }
    g.window = w;
    // Window mode goes through g.view and never calls attach_window, so hook perf and frames here too.
    hook_perf_signals(w);
    hook_frame_signal(w);

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
        set_error("init/load must be called before exec");
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
    const int rc = g.app.load()->exec();
    log_line(0, QStringLiteral("event loop exited rc=%1 ticks=%2 pointerEvents=%3")
                    .arg(rc).arg(g.ticks).arg(g.pointer_events));
    return rc;
}

void sailfish_host_quit(void)
{
    // Any thread: each shared field is loaded once; shutdown_teardown runs on the Qt thread via PostEvent.
    QObject *receiver = g.receiver.load(std::memory_order_acquire);
    if (!g.app.load(std::memory_order_acquire) || !receiver)
        return;
    QCoreApplication::postEvent(receiver, new PostEvent(shutdown_teardown, nullptr));
}

int sailfish_host_wake(int delay_ms)
{
    // Any thread: each shared field is loaded once.
    QGuiApplication *app = g.app.load(std::memory_order_acquire);
    if (g.shutdown.load(std::memory_order_acquire) || !app)
        return 0;
    if (delay_ms < 0)
        delay_ms = 0;
    if (QThread::currentThread() == app->thread())
        wake_on_qt(delay_ms);
    else if (QObject *receiver = g.receiver.load(std::memory_order_acquire))
        QCoreApplication::postEvent(receiver, new PostEvent(&wake_cb, reinterpret_cast<void *>(static_cast<intptr_t>(delay_ms))));
    return 0;
}

int sailfish_host_post(sfhost_void_fn fn, void *user_data)
{
    // Any thread: each shared field is loaded once.
    QObject *receiver = g.receiver.load(std::memory_order_acquire);
    if (!g.app.load(std::memory_order_acquire) || !receiver || !fn || g.shutdown.load(std::memory_order_acquire)) {
        ++g.posts_rejected;   // no new work after teardown
        return fail_args("sailfish_host_post");   // managed frees its GCHandle trampoline
    }
    QCoreApplication::postEvent(receiver, new PostEvent(fn, user_data));
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
        set_error("empty qml_path");
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
        return SFHOST_E_JS;
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
        return SFHOST_E_JS;
    log_line(0, QStringLiteral("pop immediate=%1 -> %2")
                    .arg(immediate ? 1 : 0).arg(result));
    return 0;
}

int sailfish_host_eval(const char *expression, char *out, int cap)
{
    g.overflow.clear();
    if (out && cap > 0)
        out[0] = '\0';
    if (!expression || !expression[0]) {
        set_error("empty expression");
        return -1;
    }
    ++g.evals;
    if (std::strstr(expression, "applyMauiOps"))
        ++g.ops_evals;
    QString result;
    if (!eval_js(QString::fromUtf8(expression), &result))
        return SFHOST_E_JS;
    const QByteArray utf = result.toUtf8();
    copy_out(utf, out, cap);
    keep_overflow(utf, cap);
    return utf.size();
}

int sailfish_host_invoke(long long handle, const char *method, const char *arg, char *out, int cap)
{
    g.overflow.clear();
    if (out && cap > 0)
        out[0] = '\0';
    if (!method || !method[0] || g.shutdown)
        return fail_args("sailfish_host_invoke");
    QObject *obj = require_handle(handle);
    if (!obj)
        return SFHOST_E_DEAD_HANDLE;
    ++g.invokes;
    if (std::strcmp(method, "applyMauiOps") == 0)
        ++g.ops_evals;
    // A QML function is a QVariant f(QVariant...) slot of the object's meta object.
    QVariant ret;
    const bool ok = arg
        ? QMetaObject::invokeMethod(obj, method, Qt::DirectConnection, Q_RETURN_ARG(QVariant, ret),
                                    Q_ARG(QVariant, QVariant(QString::fromUtf8(arg))))
        : QMetaObject::invokeMethod(obj, method, Qt::DirectConnection, Q_RETURN_ARG(QVariant, ret));
    if (!ok) {
        set_error(std::string("invoke: no method ") + method + (arg ? "(arg)" : "()") + " on "
                  + obj->metaObject()->className());
        return SFHOST_E_PROPERTY;
    }
    const QByteArray utf = ret.toString().toUtf8();
    copy_out(utf, out, cap);
    keep_overflow(utf, cap);
    return utf.size();
}

int sailfish_host_last_result(char *out, int cap)
{
    if (out && cap > 0)
        out[0] = '\0';
    copy_out(g.overflow, out, cap);
    return g.overflow.size();
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
        set_error("screen_info: host is shutting down");
        return -1;
    }
    attach_window("screen_info"); // idempotent; the window may not exist yet
    QWindow *w = g.window;
    QScreen *screen = w ? w->screen() : QGuiApplication::primaryScreen();
    if (!w && !screen) {
        set_error("screen_info: no window and no screen");
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
    // The state at startup reaches managed from MauiShell.qml (Component.onCompleted queues svc-app-state, drained
    // once this callback exists).
}

long long sailfish_host_tick_count(void)
{
    return g.ticks;
}

int sailfish_host_last_error(char *buf, int cap)
{
    if (!buf || cap <= 0)
        return -1;
    const std::string text = error_text();
    copy_out(QByteArray::fromStdString(text), buf, cap);
    return static_cast<int>(text.size());
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

} // extern "C"
