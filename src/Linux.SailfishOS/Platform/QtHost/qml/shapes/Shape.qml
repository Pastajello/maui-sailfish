import QtQuick 2.6
import QtGraphicalEffects 1.0
import Sailfish.Silica 1.0
import "pathops.js" as PathOps

// Adapter: MAUI Shapes and BoxView -> QtQuick Canvas vector painting.
// Contract: mauiId / mauiProbe / mauiEvent — see controls/Label.qml.
//
// A Canvas because Qt 5.6 has no QtQuick.Shapes and the zig build runs no moc,
// so no C++ QQuickPaintedItem can be registered. Managed code reduces every shape
// to a PathF op list plus natural bounds; the Stretch transform maps it into the
// item rect. All lengths are device px, converted once on the managed side.
Canvas {
    id: root

    property string mauiId: ""
    property string mauiProbe: ""
    signal mauiEvent(string name, string payload)

    // Adapter events are suppressed while managed pushes are applied (no echo loop).
    property bool mauiApplying: false

    property string mauiKind: "shape"      // shape | box | path (diagnostics)
    property var mauiPathOps: []           // PathF op list (pathops.js encoding)
    property var mauiNatural: [0, 0, 0, 0] // natural bounds, device px
    property int mauiAspect: 1             // MAUI Stretch: 0 None 1 Fill 2 Uniform 3 UniformToFill
    property var mauiFillSpec: []          // pathops.js paint spec ["solid"|"linear"|"radial", ...]
    property var mauiStrokeSpec: []
    property var mauiClipOps: []           // VisualElement.Clip, element space (device px)
    property real mauiStrokeWidth: 0
    property int mauiCap: 0                // PenLineCap: 0 Flat 1 Square 2 Round
    property int mauiJoin: 0               // PenLineJoin: 0 Miter 1 Bevel 2 Round
    property real mauiMiter: 10
    property var mauiDash: []              // [offset, d0, d1, ...] device px
    property int mauiWinding: 0            // FillRule: 0 EvenOdd 1 Nonzero (MAUI order)
    property color mauiBackground: "transparent"
    // Fast path: a solid (rounded) rect [fill, radius, stroke, strokeWidth] is drawn
    // by the Rectangle child so the Canvas never creates a context; mauiWantHash
    // still forces the Canvas.
    property var mauiRect: []
    // Gradient spec (item px) for the fast path, drawn by a QtGraphicalEffects shader.
    property var mauiRectGradient: []
    readonly property bool mauiFast: mauiRect !== undefined && mauiRect !== null && mauiRect.length === 4 && !mauiWantHash
    property bool __painted: false

    // --- Diagnostics, read back through the native handle ---
    property int mauiPaints: 0
    property int mauiPaintedOps: 0
    property int mauiSkippedOps: 0
    property int mauiDashes: 0
    property string mauiPixelHash: "0"
    property bool mauiWantHash: false

    // Qt 5.6 cannot change strategy/target once the context exists, and pixel
    // readback (mauiWantHash) is unsupported in Cooperative mode, hence Immediate + FBO.
    renderStrategy: Canvas.Immediate
    renderTarget: Canvas.FramebufferObject

    onMauiWantHashChanged: __repaint()
    onMauiPathOpsChanged: __repaint()
    onMauiNaturalChanged: __repaint()
    onMauiAspectChanged: __repaint()
    onMauiFillSpecChanged: __repaint()
    onMauiStrokeSpecChanged: __repaint()
    onMauiClipOpsChanged: __repaint()
    onMauiStrokeWidthChanged: __repaint()
    onMauiCapChanged: __repaint()
    onMauiJoinChanged: __repaint()
    onMauiMiterChanged: __repaint()
    onMauiDashChanged: __repaint()
    onMauiWindingChanged: __repaint()
    onMauiBackgroundChanged: __repaint()
    onWidthChanged: __repaint()
    onHeightChanged: __repaint()
    onMauiFastChanged: if (!mauiFast || __painted) requestPaint()   // switch paths / clear stale pixels
    function __repaint() { if (!mauiFast) requestPaint(); }

    // LinearGradient projects in normalized item space, MAUI in pixels. This end
    // point makes them match on non-square boxes: with d = end - start and
    // k = (W dx, H dy) / |d|², the shader needs l = k / |k|².
    function __normalizedEnd(g, w, h) {
        var dx = g[3] - g[1], dy = g[4] - g[2];
        var d2 = dx * dx + dy * dy;
        if (w <= 0 || h <= 0 || d2 <= 0)
            return Qt.point(g[3], g[4]);
        var kx = w * dx / d2, ky = h * dy / d2;
        var k2 = kx * kx + ky * ky;
        return Qt.point(g[1] + kx / k2 * w, g[2] + ky / k2 * h);
    }

    function __gradient(stops, owner) {
        var q = "import QtQuick 2.6; Gradient {";
        for (var i = 0; i < stops.length; ++i)
            q += " GradientStop { position: " + stops[i][0] + "; color: \"" + stops[i][1] + "\" }";
        return Qt.createQmlObject(q + " }", owner);
    }
    Loader {
        anchors.fill: parent
        readonly property var spec: root.mauiRectGradient
        active: root.mauiFast && spec !== undefined && spec !== null && spec.length > 0
        sourceComponent: active && spec[0] === "radial" ? radialFill : linearFill
        Component {
            id: linearFill
            LinearGradient {
                readonly property var g: root.mauiRectGradient
                start: Qt.point(g[1], g[2])
                end: root.__normalizedEnd(g, width, height)
                gradient: root.__gradient(g[5], this)
            }
        }
        Component {
            id: radialFill
            RadialGradient {
                readonly property var g: root.mauiRectGradient
                horizontalOffset: g[1] - width / 2
                verticalOffset: g[2] - height / 2
                horizontalRadius: g[3]
                verticalRadius: g[3]
                gradient: root.__gradient(g[4], this)
            }
        }
    }

    Rectangle {
        anchors.fill: parent
        visible: root.mauiFast
        color: root.mauiFast && root.mauiRect[0] !== "" ? root.mauiRect[0] : "transparent"
        radius: root.mauiFast ? root.mauiRect[1] : 0
        border.color: root.mauiFast && root.mauiRect[2] !== "" ? root.mauiRect[2] : "transparent"
        border.width: root.mauiFast ? root.mauiRect[3] : 0
    }

    property double __paintT0: 0   // logs PAINT-SLOW for paints over 15 ms
    onPainted: { var slow = PathOps.slowPaintMessage("shape", objectName, width, height, __paintT0); if (slow) console.log(slow); __paintT0 = 0; }
    onPaint: {
        if (mauiFast) {
            if (__painted) {   // was a Canvas shape before: clear its pixels once
                getContext("2d").clearRect(0, 0, width, height);
                __painted = false;
            }
            return;
        }
        __painted = true;
        __paintT0 = Date.now();
        var ctx = getContext("2d");
        PathOps.resetContext(ctx);
        ctx.clearRect(0, 0, width, height);
        mauiPaints++;
        if (mauiBackground.a > 0) {
            ctx.fillStyle = PathOps.paintStyle(ctx, [], mauiBackground);
            ctx.fillRect(0, 0, width, height);
        }
        var ops = mauiPathOps;
        if (!ops || ops.length === 0 || width <= 0 || height <= 0) {
            mauiPaintedOps = 0;
            report(0, 0, 0);
            return;
        }
        var t = PathOps.stretchTransform(mauiNatural, width, height, mauiAspect);
        var skipped = 0;
        /* Clip is in element space, so it is applied before the Stretch transform. */
        var clipped = mauiClipOps && mauiClipOps.length > 0;
        if (clipped) {
            ctx.save();
            PathOps.buildPath(ctx, mauiClipOps);
            ctx.clip();
        }
        ctx.save();
        ctx.translate(t.tx, t.ty);
        ctx.scale(t.sx, t.sy);
        /* StrokeThickness is view-space: undo the Stretch scale so the stroke keeps
         * its device px width. */
        var avg = (Math.abs(t.sx) + Math.abs(t.sy)) / 2;
        ctx.lineWidth = mauiStrokeWidth / (avg > 0 ? avg : 1);
        ctx.lineCap = ["butt", "square", "round"][mauiCap] || "butt";
        ctx.lineJoin = ["miter", "bevel", "round"][mauiJoin] || "miter";
        ctx.miterLimit = mauiMiter;
        /* Without setLineDash, dashes are cut in JS as a separate stroke path after the fill. */
        var manualDash = !PathOps.applyDash(ctx, mauiDash);
        var n = PathOps.buildPath(ctx, ops);
        var fill = PathOps.paintStyle(ctx, mauiFillSpec, null);
        if (fill) {
            ctx.fillStyle = fill;
            try { ctx.fill(mauiWinding === 0 ? "evenodd" : "nonzero"); }
            catch (e) { ctx.fill(); }
        }
        var stroke = mauiStrokeWidth > 0 ? PathOps.paintStyle(ctx, mauiStrokeSpec, null) : null;
        if (stroke) {
            ctx.strokeStyle = stroke;
            if (manualDash)
                mauiDashes = PathOps.dashPath(ctx, ops, mauiDash, t);
            ctx.stroke();
        }
        ctx.restore();
        if (clipped)
            ctx.restore();
        mauiPaintedOps = n;
        mauiSkippedOps = skipped;
        if (mauiWantHash)
            mauiPixelHash = PathOps.pixelHash(ctx, width, height);
        report(n, fill ? 1 : 0, stroke ? 1 : 0);
    }

    // Diagnostics only (mauiWantHash); normal repaints send no event.
    function report(ops, filled, stroked) {
        if (!mauiWantHash)
            return;
        mauiEvent("shape-painted", JSON.stringify({
            id: mauiId, kind: mauiKind, ops: ops, filled: filled, stroked: stroked,
            skipped: mauiSkippedOps, paints: mauiPaints, hash: mauiPixelHash
        }));
    }
}
