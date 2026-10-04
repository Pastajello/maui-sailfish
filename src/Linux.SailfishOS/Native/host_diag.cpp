// host_diag.cpp — diagnostics: input injection, screenshots, the showcase recorder, lifecycle and perf counters.

#include "host_internal.h"

using namespace sfhost;

extern "C" {

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
        set_error("no QQuickWindow to grab or empty path");
        return -1;
    }
    ++g.grabs;   // diagnostics only; must stay 0 in production
    const QImage img = qw->grabWindow();
    if (img.isNull()) {
        set_error("grabWindow() returned a null image");
        return -1;
    }
    if (!img.save(QString::fromUtf8(path), "PNG")) {
        set_error(std::string("could not save grab to ") + path);
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
        set_error("record: no QQuickWindow or empty directory");
        return -1;
    }
    if (g_rec.conn)
        QObject::disconnect(g_rec.conn);
    const QString path = QString::fromUtf8(dir);
    if (!QDir().mkpath(path)) {
        set_error("record: cannot create the directory");
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

/* --- Lifecycle diagnostics --- */

int sailfish_host_diag_app_state(int state, int activate)
{
    if (g.shutdown || !g.app) {
        set_error("diag_app_state: host is down (teardown)");
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
        "\"frameCallbacks\":%43,\"surfaceMaxTexture\":%44,\"invokes\":%45}")
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
        .arg(g.surface_max_texture.load())
        .arg(g.invokes.load());
    const QByteArray utf = json.toUtf8();
    return copy_out(utf, buf, cap);
}

} // extern "C"
