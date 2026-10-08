import QtQuick 2.6
import QtGraphicalEffects 1.0
import "../shapes/pathops.js" as PathOps

/* MAUI Shadow + Clip for any adapter: the shim (apply_layer_effect) installs this as the item's
 * layer.effect with the spec baked in (one component per distinct spec; nothing live-updates).
 * Clip ops (pathops.js encoding, device px) become an alpha mask, since Qt 5.6 items only clip
 * rectangularly. The shadow is a DropShadow of the clipped content when both are set, like MAUI.
 */
Item {
    id: root
    objectName: "mauiLayerEffect"

    property variant source                 // the item's layer (QQuickItemLayer)
    property color shadowColor: "transparent"
    property real shadowRadius: 0
    property real shadowX: 0
    property real shadowY: 0
    property var clipOps: []
    property bool clipEvenOdd: true

    readonly property bool clipping: clipOps !== undefined && clipOps !== null && clipOps.length > 0
    readonly property bool shadowing: shadowColor.a > 0

    Canvas {
        id: mask
        anchors.fill: parent
        visible: false
        onWidthChanged: requestPaint()
        onHeightChanged: requestPaint()
        onPaint: {
            var ctx = getContext("2d");
            PathOps.resetContext(ctx);
            ctx.clearRect(0, 0, width, height);
            if (!root.clipping)
                return;
            ctx.fillStyle = "#ffffff";
            ctx.beginPath();
            PathOps.buildPath(ctx, root.clipOps);
            ctx.fillRule = root.clipEvenOdd ? Qt.OddEvenFill : Qt.WindingFill;   /* fill()'s argument is ignored */
            ctx.fill();
        }
    }

    OpacityMask {
        id: clipped
        anchors.fill: parent
        source: root.source
        maskSource: mask
        // shown directly without a shadow; otherwise the shadow's source
        visible: root.clipping && !root.shadowing
    }

    DropShadow {
        anchors.fill: parent
        visible: root.shadowing
        source: root.clipping ? clipped : root.source
        color: root.shadowColor
        radius: root.shadowRadius
        samples: Math.min(32, Math.max(1, Math.ceil(root.shadowRadius * 2 + 1)))
        horizontalOffset: root.shadowX
        verticalOffset: root.shadowY
        transparentBorder: true
    }

    // Neither set: the shim disables the layer then; kept so a stale instance still shows content.
    ShaderEffect {
        anchors.fill: parent
        visible: !root.clipping && !root.shadowing
        property variant source: root.source
    }
}
