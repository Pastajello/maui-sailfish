import QtQuick 2.6
import Sailfish.Silica 1.0

// Adapter: MAUI Image -> QtQuick Image. The root is an Item because Qt 5.6 fillMode cannot
// express Aspect.Center (no Image.Pad), and the element bounds belong to the geometry pass.
// Sources arrive as absolute file:// or http(s):// URLs (QtHostImages); Qt decodes them.
// Events: "image-failed" {id, source}.
// The GIF player, the corner caps and the tap surface sit behind Loaders that are active only when used, so a
// plain list thumbnail creates none of them (~45% cheaper; adapterbench keeps it pixel-identical to
// Linux.SailfishOS.Diagnostics/qml/diag/reference/ImageEager.qml).
Item {
    id: root

    property string mauiId: ""
    // Diag: aspect value plus the corner-clip bitmask when clipping.
    property string mauiProbe: "aspect=" + mauiAspect +
        (mauiClipRadius > 0 ? " clip=" + mauiClipCorners : "")
    signal mauiEvent(string name, string payload)

    // Managed property pushes set this flag; events fired meanwhile are suppressed (no echo).
    property bool mauiApplying: false

    /* --- image state (QtHostImages.Props) --- */
    property string mauiSource: ""
    // Aspect contract owned by QtHostImages (mapped by enum member name): 0 Fill, 1 AspectFit,
    // 2 AspectFill, 3 Center. Don't use MAUI enum ordinals; MAUI 11 reordered them.
    property int mauiAspect: 1
    property color mauiBackground: "transparent"
    // ImageButton uses this adapter: arms the MouseArea so taps cross as "tap". A plain Image
    // lets presses fall through to the page flickable.
    property bool mauiTappable: false
    // IsAnimationPlaying: a GIF source plays in an AnimatedImage instead of Image.
    property bool mauiPlaying: false
    // Arranged size in Qt units, the decode size (0 = not arranged yet: natural size).
    property int mauiDecodeW: 0
    property int mauiDecodeH: 0
    // The decode request; frozen once a load starts, since a new sourceSize refetches the image.
    // AspectFill/Fill constrain the longer box axis only: Qt fits a request into both axes, which
    // would leave a crop short on the other one (exact for square thumbnails into wide tiles).
    function __decodeFor() {
        if (mauiDecodeW <= 0 || mauiDecodeH <= 0 || mauiAspect === 3)
            return Qt.size(0, 0);
        if (mauiAspect === 1)
            return Qt.size(mauiDecodeW, mauiDecodeH);
        return mauiDecodeW >= mauiDecodeH ? Qt.size(mauiDecodeW, 0) : Qt.size(0, mauiDecodeH);
    }
    property size __decode: __decodeFor()
    // A query or a fragment (the cache policy, QtHostImages) may follow the extension.
    readonly property bool __gif: /\.gif([?#].*)?$/i.test(String(mauiSource))
    readonly property int mauiFrame: anim.item ? anim.item.currentFrame : 0
    property int mauiFrameCount: 0   // captured on Ready (frameCount has no NOTIFY on Qt 5.6)
    // ImageButton frame (device px): plate and outline share the radius, Padding insets the bitmap.
    property real mauiCornerRadius: 0
    property color mauiStrokeColor: "transparent"
    property real mauiStrokeWidth: 0
    property real mauiPadL: 0
    property real mauiPadT: 0
    property real mauiPadR: 0
    property real mauiPadB: 0

    /* --- corner clip (QtHostClip) ---
     * Border children are sibling hosts on the flat canvas, so the Border cannot clip them.
     * Managed pushes which Border corners this element covers (1=TL 2=TR 4=BR 8=BL), the radius
     * and the surround color; each corner gets a concave cap in that color. Qt 5.6 has no rounded
     * clip and OpacityMask mis-renders here. Exact only for flat surrounds.
     * The caps are declared, not created: init props fire no change handlers and collection rows
     * get no later push, so only bindings see the initial values. */
    property real mauiClipRadius: 0
    property int mauiClipCorners: 0
    property color mauiClipColor: "transparent"

    /* --- diagnostics (read back through the native handle) --- */
    property bool mauiLoaded: false
    property int mauiNaturalWidth: 0
    property int mauiNaturalHeight: 0
    property string mauiLoadError: ""

    clip: true                       // AspectFill crops to the element bounds
    onMauiSourceChanged: {
        mauiLoadError = "";
        __loadStart = Date.now();
        __startedOnScreen = __onScreen();
        __decode = Qt.binding(__decodeFor);   // a new source takes the current size
    }

    /* --- load trace (MAUI_SAILFISH_IMAGE_TRACE=1): ms from source to Ready; on screen when the source was set and
     * when the bitmap arrived (late=1: the user saw the empty tile) --- */
    property real __loadStart: Date.now()
    property bool __startedOnScreen: false
    function __onScreen() {
        if (typeof window === "undefined" || !window.mauiImageTrace)
            return false;
        var p = root.mapToItem(null, 0, 0);
        return root.visible && p.y + root.height > 0 && p.y < window.height &&
               p.x + root.width > 0 && p.x < window.width;
    }
    function __traceLoad() {
        if (typeof window === "undefined" || !window.mauiImageTrace)
            return;
        var src = String(root.mauiSource);
        // warn: console.log only prints with MAUI_SAILFISH_QT_HOST_DIAG=1
        console.warn("MAUI-IMG ms=" + (Date.now() - __loadStart) + " late=" + (__onScreen() ? 1 : 0) +
                     " started=" + (__startedOnScreen ? 1 : 0) + " px=" + image.sourceSize.width + "x" +
                     image.sourceSize.height + " box=" + Math.round(width) + "x" + Math.round(height) +
                     " src=" + src.substring(src.lastIndexOf("/") + 1));
    }

    Rectangle {
        anchors.fill: parent
        color: root.mauiBackground
        radius: root.mauiCornerRadius
        visible: root.mauiBackground.a > 0
    }

    Image {
        id: image
        // Centered in the padded box (ImageButton.Padding; 0 for an Image).
        x: root.mauiPadL + (root.width - root.mauiPadL - root.mauiPadR - width) / 2
        y: root.mauiPadT + (root.height - root.mauiPadT - root.mauiPadB - height) / 2
        // Center draws the bitmap at natural size; other modes fill the bounds and let fillMode scale.
        width: root.mauiAspect === 3 ? sourceSize.width : Math.max(0, root.width - root.mauiPadL - root.mauiPadR)
        height: root.mauiAspect === 3 ? sourceSize.height : Math.max(0, root.height - root.mauiPadT - root.mauiPadB)
        fillMode: root.mauiAspect === 0 ? Image.Stretch
                  : root.mauiAspect === 2 ? Image.PreserveAspectCrop
                  : Image.PreserveAspectFit    // AspectFit (and Center, which sizes itself)
        source: root.__gif ? "" : root.mauiSource
        visible: !root.__gif
        asynchronous: true
        smooth: true
        cache: true
        sourceSize: root.__decode

        onStatusChanged: {
            if (status === Image.Loading)
                root.__decode = root.__decode;   // freeze: a relayout must not refetch
            if (status === Image.Error) {
                root.mauiLoaded = false;
                root.mauiLoadError = "QtQuick Image error for " + String(root.mauiSource);
                if (!root.mauiApplying)
                    root.mauiEvent("image-failed",
                                   JSON.stringify({ id: root.mauiId, source: String(root.mauiSource) }));
            } else if (status === Image.Ready) {
                root.__traceLoad();
                root.mauiLoaded = true;
                root.mauiNaturalWidth = sourceSize.width;
                root.mauiNaturalHeight = sourceSize.height;
                root.mauiLoadError = "";
            }
        }
    }

    Loader {
        id: anim
        active: root.__gif
        sourceComponent: Component {
            AnimatedImage {
                x: image.x
                y: image.y
                width: root.mauiAspect === 3 ? implicitWidth : image.width
                height: root.mauiAspect === 3 ? implicitHeight : image.height
                fillMode: image.fillMode
                source: root.__gif ? root.mauiSource : ""
                visible: root.__gif
                playing: root.__gif && root.mauiPlaying
                smooth: true
                onStatusChanged: {
                    if (status === AnimatedImage.Ready) {
                        root.mauiFrameCount = frameCount;
                        root.mauiLoaded = true;
                        root.mauiNaturalWidth = implicitWidth;
                        root.mauiNaturalHeight = implicitHeight;
                        root.mauiLoadError = "";
                    } else if (status === AnimatedImage.Error) {
                        root.mauiLoaded = false;
                        root.mauiLoadError = "QtQuick AnimatedImage error for " + String(root.mauiSource);
                    }
                }
            }
        }
    }

    /* --- the four corner caps (after the Image: they stack above), only while a clip is pushed --- */
    Loader {
        id: caps
        anchors.fill: parent
        active: root.mauiClipRadius > 0 && root.mauiClipColor.a > 0 && root.mauiClipCorners !== 0
        sourceComponent: Component {
            Item {
                function repaint() { capTL.requestPaint(); capTR.requestPaint(); capBR.requestPaint(); capBL.requestPaint(); }
                Canvas {
                    id: capTL
                    anchors.left: parent.left; anchors.top: parent.top
                    width: root.mauiClipRadius; height: root.mauiClipRadius
                    visible: root.mauiClipRadius > 0 && root.mauiClipColor.a > 0 &&
                             (root.mauiClipCorners & 1) !== 0
                    onPaint: root.__drawCap(this, 1)
                    onVisibleChanged: if (visible) requestPaint()
                }
                Canvas {
                    id: capTR
                    anchors.right: parent.right; anchors.top: parent.top
                    width: root.mauiClipRadius; height: root.mauiClipRadius
                    visible: root.mauiClipRadius > 0 && root.mauiClipColor.a > 0 &&
                             (root.mauiClipCorners & 2) !== 0
                    onPaint: root.__drawCap(this, 2)
                    onVisibleChanged: if (visible) requestPaint()
                }
                Canvas {
                    id: capBR
                    anchors.right: parent.right; anchors.bottom: parent.bottom
                    width: root.mauiClipRadius; height: root.mauiClipRadius
                    visible: root.mauiClipRadius > 0 && root.mauiClipColor.a > 0 &&
                             (root.mauiClipCorners & 4) !== 0
                    onPaint: root.__drawCap(this, 4)
                    onVisibleChanged: if (visible) requestPaint()
                }
                Canvas {
                    id: capBL
                    anchors.left: parent.left; anchors.bottom: parent.bottom
                    width: root.mauiClipRadius; height: root.mauiClipRadius
                    visible: root.mauiClipRadius > 0 && root.mauiClipColor.a > 0 &&
                             (root.mauiClipCorners & 8) !== 0
                    onPaint: root.__drawCap(this, 8)
                    onVisibleChanged: if (visible) requestPaint()
                }
            }
        }
    }

    // A surround-color change needs an explicit repaint; visibility/size repaint via bindings.
    onMauiClipColorChanged: if (caps.item) caps.item.repaint()

    // Concave corner: the R×R square minus the quarter disc, mirrored into the requested corner.
    function __drawCap(cap, corner) {
        var ctx = cap.getContext("2d");
        ctx.reset();
        var r = root.mauiClipRadius;
        if (r <= 0)
            return;
        var c = root.mauiClipColor;
        ctx.fillStyle = "rgba(" + Math.round(c.r * 255) + "," + Math.round(c.g * 255) +
                        "," + Math.round(c.b * 255) + "," + c.a + ")";
        ctx.save();
        if (corner === 2 || corner === 4) {
            ctx.translate(cap.width, 0);
            ctx.scale(-1, 1);
        }
        if (corner === 4 || corner === 8) {
            ctx.translate(0, cap.height);
            ctx.scale(1, -1);
        }
        ctx.beginPath();
        ctx.moveTo(0, 0);
        ctx.lineTo(r, 0);
        ctx.arc(r, r, r, -Math.PI / 2, Math.PI, true);   // (r,0) -> (0,r)
        ctx.closePath();
        ctx.fill();
        ctx.restore();
    }

    // Tap surface: last child so it wins the press over the bitmap and caps.
    Loader {
        anchors.fill: parent
        active: root.mauiTappable
        sourceComponent: Component {
            MouseArea {
                onClicked: root.mauiEvent("tap", JSON.stringify({ id: root.mauiId }))
            }
        }
    }

    // ImageButton outline, above the bitmap and the caps.
    Rectangle {
        id: frame
        anchors.fill: parent
        color: "transparent"
        radius: root.mauiCornerRadius
        border.color: root.mauiStrokeColor
        border.width: root.mauiStrokeWidth
        visible: root.mauiStrokeColor.a > 0 && root.mauiStrokeWidth > 0
        z: 10
    }
}
