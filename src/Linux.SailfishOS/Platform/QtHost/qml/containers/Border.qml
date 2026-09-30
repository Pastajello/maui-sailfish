import QtQuick 2.6
import QtGraphicalEffects 1.0
import Sailfish.Silica 1.0
import "../shapes/pathops.js" as PathOps

// Adapter: MAUI Border -> styled container. All lengths are device px.
// Contract: mauiId / mauiProbe / mauiEvent — see controls/Label.qml.
// Content is a child of the box (mauiChildHost) so the box paints below it; there
// is no MouseArea, so presses fall through. Strokes a Rectangle cannot draw (dashes,
// per-corner radii, other StrokeShapes, gradients) arrive as mauiShapeOps and are
// painted by a Canvas. The root is unclipped so the content clip never cuts the shadow.
Item {
    id: root

    property string mauiId: ""
    property string mauiProbe: ""
    signal mauiEvent(string name, string payload)

    // Adapter events are suppressed while managed pushes are applied (no echo loop).
    property bool mauiApplying: false

    property color mauiBorderColor: Theme.secondaryColor
    property real mauiBorderWidth: 1
    property real mauiCornerRadius: 4
    property color mauiBackground: "transparent"
    property bool mauiClip: false
    property Item mauiChildHost: box

    property var mauiShapeOps: []
    property var mauiFillSpec: []
    property var mauiStrokeSpec: []
    property int mauiCap: 0
    property int mauiJoin: 0
    property real mauiMiter: 10
    property var mauiDash: []
    readonly property bool mauiCanvas: mauiShapeOps !== undefined && mauiShapeOps !== null && mauiShapeOps.length > 0
    property int mauiDashes: 0       // diagnostics: dashes cut in the last paint
    property int mauiCanvasPaints: 0

    property color mauiShadowColor: "transparent"
    property real mauiShadowBlur: 0
    property real mauiShadowX: 0
    property real mauiShadowY: 0
    readonly property bool mauiShadow: mauiShadowColor.a > 0
    property int mauiShadowPaints: 0

    // Diagnostics readback.
    function mauiDiag() {
        return JSON.stringify({
            canvas: mauiCanvas, dashes: mauiDashes, paints: mauiCanvasPaints,
            ops: mauiShapeOps ? mauiShapeOps.length : 0,
            kinds: mauiShapeOps ? mauiShapeOps.map(function(o) { return o[0]; }).join("") : "",
            bw: box.border.width, radius: box.radius, clip: box.clip,
            shadow: mauiShadow, shadowPaints: mauiShadowPaints
        });
    }

    // Shadows are drawn on the GPU; Context2D shadowBlur is a slow software blur on
    // the GUI thread. A (rounded) rectangle uses RectangularGlow.
    Loader {
        id: rectShadow
        active: root.mauiShadow && !root.mauiCanvas
        x: root.mauiShadowX
        y: root.mauiShadowY
        width: root.width
        height: root.height
        sourceComponent: RectangularGlow {
            glowRadius: root.mauiShadowBlur
            spread: 0
            color: root.mauiShadowColor
            cornerRadius: Math.min(root.mauiCornerRadius, width / 2, height / 2) + glowRadius
            cached: true
            Component.onCompleted: root.mauiShadowPaints++
            onColorChanged: root.mauiShadowPaints++
            onGlowRadiusChanged: root.mauiShadowPaints++
        }
    }
    // Other StrokeShapes: the silhouette is filled on a Canvas and blurred by FastBlur.
    Loader {
        id: pathShadow
        readonly property real pad: Math.ceil(root.mauiShadowBlur * 2 + Math.max(Math.abs(root.mauiShadowX), Math.abs(root.mauiShadowY)) + 2)
        x: -pad
        y: -pad
        width: root.width + 2 * pad
        height: root.height + 2 * pad
        active: root.mauiShadow && root.mauiCanvas
        sourceComponent: Item {
            Canvas {
                id: silhouette
                anchors.fill: parent
                visible: false
                renderStrategy: Canvas.Immediate
                renderTarget: Canvas.FramebufferObject
                onWidthChanged: requestPaint()
                onHeightChanged: requestPaint()
                Connections {
                    target: root
                    onMauiShadowColorChanged: silhouette.requestPaint()
                    onMauiShadowXChanged: silhouette.requestPaint()
                    onMauiShadowYChanged: silhouette.requestPaint()
                    onMauiShapeOpsChanged: silhouette.requestPaint()
                }
                property double __paintT0: 0   // logs PAINT-SLOW for paints over 15 ms
                onPainted: { var slow = PathOps.slowPaintMessage("border-shadow", root.objectName, width, height, __paintT0); if (slow) console.log(slow); __paintT0 = 0; }
                onPaint: {
                    __paintT0 = Date.now();
                    var ctx = getContext("2d");
                    PathOps.resetContext(ctx);
                    ctx.clearRect(0, 0, width, height);
                    root.mauiShadowPaints++;
                    ctx.save();
                    ctx.translate(pathShadow.pad + root.mauiShadowX, pathShadow.pad + root.mauiShadowY);
                    PathOps.buildPath(ctx, root.mauiShapeOps);
                    ctx.fillStyle = root.mauiShadowColor;
                    ctx.fill();
                    ctx.restore();
                }
            }
            FastBlur {
                anchors.fill: parent
                source: silhouette
                radius: Math.min(64, root.mauiShadowBlur)
                transparentBorder: true
            }
        }
    }

    Rectangle {
        id: box
        anchors.fill: parent
        clip: root.mauiClip
        color: root.mauiCanvas ? "transparent" : root.mauiBackground
        border.color: root.mauiBorderColor
        border.width: root.mauiCanvas ? 0 : root.mauiBorderWidth
        radius: root.mauiCanvas ? 0 : root.mauiCornerRadius

        Loader {
            anchors.fill: parent
            z: -1
            active: root.mauiCanvas
            sourceComponent: Canvas {
                id: canvas
                renderStrategy: Canvas.Immediate
                renderTarget: Canvas.FramebufferObject
                onWidthChanged: requestPaint()
                onHeightChanged: requestPaint()
                Connections {
                    target: root
                    onMauiShapeOpsChanged: canvas.requestPaint()
                    onMauiFillSpecChanged: canvas.requestPaint()
                    onMauiStrokeSpecChanged: canvas.requestPaint()
                    onMauiBorderWidthChanged: canvas.requestPaint()
                    onMauiCapChanged: canvas.requestPaint()
                    onMauiJoinChanged: canvas.requestPaint()
                    onMauiMiterChanged: canvas.requestPaint()
                    onMauiDashChanged: canvas.requestPaint()
                }
                property double __paintT0: 0   // logs PAINT-SLOW for paints over 15 ms
                onPainted: { var slow = PathOps.slowPaintMessage("border-canvas", root.objectName, width, height, __paintT0); if (slow) console.log(slow); }
                onPaint: {
                    __paintT0 = Date.now();
                    var ctx = getContext("2d");
                    PathOps.resetContext(ctx);
                    ctx.clearRect(0, 0, width, height);
                    root.mauiCanvasPaints++;
                    var ops = root.mauiShapeOps;
                    if (!ops || ops.length === 0)
                        return;
                    PathOps.buildPath(ctx, ops);
                    var fill = PathOps.paintStyle(ctx, root.mauiFillSpec, null);
                    if (fill) {
                        ctx.fillStyle = fill;
                        ctx.fill();
                    }
                    var stroke = root.mauiBorderWidth > 0 ? PathOps.paintStyle(ctx, root.mauiStrokeSpec, null) : null;
                    if (!stroke)
                        return;
                    ctx.lineWidth = root.mauiBorderWidth;
                    ctx.lineCap = ["butt", "square", "round"][root.mauiCap] || "butt";
                    ctx.lineJoin = ["miter", "bevel", "round"][root.mauiJoin] || "miter";
                    ctx.miterLimit = root.mauiMiter;
                    ctx.strokeStyle = stroke;
                    if (!PathOps.applyDash(ctx, root.mauiDash))
                        root.mauiDashes = PathOps.dashPath(ctx, ops, root.mauiDash, null);
                    else
                        root.mauiDashes = 0;
                    ctx.stroke();
                }
            }
        }
    }
}
