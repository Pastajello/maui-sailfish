// host_surface.cpp — drawn surfaces (SkiaSharp canvases): the texture node, touch, the frame callback.

#include "host_internal.h"

using namespace sfhost;

extern "C" {

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
