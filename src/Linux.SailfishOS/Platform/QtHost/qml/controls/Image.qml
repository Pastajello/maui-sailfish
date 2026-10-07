import QtQuick 2.6
import Sailfish.Silica 1.0

// Adapter: MAUI Image -> QtQuick Image. The root is an Item because Qt 5.6 fillMode cannot
// express Aspect.Center (no Image.Pad), and the element bounds belong to the geometry pass.
// Sources arrive as absolute file:// or http(s):// URLs (QtHostImages); Qt decodes them.
// Events: "image-failed" {id, source}; "image-natural" {id, source, width, height} once a remote image loads at its
// own size (local files are measured from their headers), so an Image without a size request can size to it.
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
    // Unsized and Center images decode at most at this (the screen's long side, Qt units; 0 = no cap): a 4000 px
    // photo no longer decodes at full size. Qt scales rasters down only, so smaller images keep their pixels; an SVG
    // would be scaled to the request, so it is left alone.
    property int mauiDecodeCap: 0
    // The decode request; frozen once a load starts, since a new sourceSize refetches the image.
    // AspectFill/Fill constrain the longer box axis only: Qt fits a request into both axes, which
    // would leave a crop short on the other one (exact for square thumbnails into wide tiles).
    function __decodeFor() {
        if (mauiDecodeW <= 0 || mauiDecodeH <= 0 || mauiAspect === 3)
            return mauiDecodeCap > 0 && !/\.svgz?([?#].*)?$/i.test(String(mauiSource))
                ? Qt.size(mauiDecodeCap, mauiDecodeCap) : Qt.size(0, 0);
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
    // The Border's solid stroke, which runs over the caps' outer edge: they cover it, so they draw it again.
    property color mauiClipStroke: "transparent"
    property real mauiClipStrokeWidth: 0

    /* --- diagnostics (read back through the native handle) --- */
    property bool mauiLoaded: false
    property int mauiNaturalWidth: 0
    property int mauiNaturalHeight: 0
    property string mauiLoadError: ""
    // The natural-size decode: none, or the cap (an image above the cap reports its capped size).
    readonly property bool __naturalDecode: __decode.width === __decode.height &&
                                            (__decode.width <= 0 || __decode.width === mauiDecodeCap)
    function __reportNatural(w, h) {
        if (w > 0 && h > 0 && !mauiApplying && /^https?:/i.test(String(mauiSource)) && (__gif || __naturalDecode))
            mauiEvent("image-natural", JSON.stringify({ id: mauiId, source: String(mauiSource), width: w, height: h }));
    }
    // Image.IsLoading goes false (managed matches the source against the one it is waiting for). Sent on the next
    // turn: a local GIF's AnimatedImage loads synchronously while this object is still being created, before managed
    // knows its id, and an event then is dropped.
    function __reportLoaded() {
        loadedReport.restart();
    }
    Timer {
        id: loadedReport
        interval: 0
        onTriggered: root.mauiEvent("image-loaded", JSON.stringify({ id: root.mauiId, source: String(root.mauiSource) }))
    }

    // A remote load that failed (a dropped connection, a request the shim's stall watchdog aborted) loads again twice,
    // 1 s and then 3 s later, before it counts as failed; Qt keeps no failed pixmap, so a reload fetches anew.
    property int __retries: 0
    property bool __reloading: false
    Timer {
        id: retryTimer
        onTriggered: {
            root.__reloading = true;    // source "" then back: a new request
            root.__reloading = false;
        }
    }

    clip: true                       // AspectFill crops to the element bounds
    onMauiSourceChanged: {
        __retries = 0;
        retryTimer.stop();
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
        source: root.__gif || root.__reloading ? "" : root.mauiSource
        visible: !root.__gif
        asynchronous: true
        smooth: true
        cache: true
        sourceSize: root.__decode

        onStatusChanged: {
            if (status === Image.Loading)
                root.__decode = root.__decode;   // freeze: a relayout must not refetch
            if (status === Image.Error && root.__retries < 2 && /^https?:/i.test(String(root.mauiSource))) {
                root.__retries++;
                console.warn("MAUI-IMG retry " + root.__retries + " " + String(root.mauiSource));
                retryTimer.interval = root.__retries === 1 ? 1000 : 3000;
                retryTimer.restart();
            } else if (status === Image.Error) {
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
                root.__reportNatural(sourceSize.width, sourceSize.height);
                root.__reportLoaded();
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
                        root.__reportNatural(implicitWidth, implicitHeight);
                        root.__reportLoaded();
                    } else if (status === AnimatedImage.Error) {
                        root.mauiLoaded = false;
                        root.mauiLoadError = "QtQuick AnimatedImage error for " + String(root.mauiSource);
                        root.mauiEvent("image-failed", JSON.stringify({ id: root.mauiId, source: String(root.mauiSource) }));
                    }
                }
            }
        }
    }

    /* --- the corner caps (after the Image: they stack above), only while a clip is pushed ---
     * A rounded Rectangle frame in the surround colour, grown past the image so only its inner radius shows in
     * the covered corners; sides with no covered corner are pushed out of view (the image clips). A second frame
     * draws the Border's stroke ring over the caps. Scene-graph rectangles paint with the first frame; the
     * Canvas caps painted asynchronously and showed square photos for a moment after a list rebuild. */
    Item {
        id: caps
        anchors.fill: parent
        visible: root.mauiClipRadius > 0 && root.mauiClipColor.a > 0 && root.mauiClipCorners !== 0
        readonly property real r: root.mauiClipRadius
        readonly property int c: root.mauiClipCorners
        readonly property real eL: (c & 9) !== 0 ? 0 : 2 * r    // TL|BL
        readonly property real eR: (c & 6) !== 0 ? 0 : 2 * r    // TR|BR
        readonly property real eT: (c & 3) !== 0 ? 0 : 2 * r    // TL|TR
        readonly property real eB: (c & 12) !== 0 ? 0 : 2 * r   // BR|BL
        Rectangle {
            x: -caps.r - caps.eL
            y: -caps.r - caps.eT
            width: caps.width + 2 * caps.r + caps.eL + caps.eR
            height: caps.height + 2 * caps.r + caps.eT + caps.eB
            radius: 2 * caps.r
            color: "transparent"
            border.width: caps.r
            border.color: root.mauiClipColor
            antialiasing: true
        }
        Rectangle {
            readonly property real w: root.mauiClipStrokeWidth
            visible: w > 0 && root.mauiClipStroke.a > 0
            x: -w - caps.eL
            y: -w - caps.eT
            width: caps.width + 2 * w + caps.eL + caps.eR
            height: caps.height + 2 * w + caps.eT + caps.eB
            radius: caps.r + w
            color: "transparent"
            border.width: w
            border.color: root.mauiClipStroke
            antialiasing: true
        }
    }

    // Tap surface: last child so it wins the press over the bitmap and caps.
    Loader {
        anchors.fill: parent
        active: root.mauiTappable
        sourceComponent: Component {
            MouseArea {
                onClicked: root.mauiEvent("tap", JSON.stringify({ id: root.mauiId }))
                // ImageButton Pressed/Released.
                onPressedChanged: root.mauiEvent("pressed-changed", JSON.stringify({ id: root.mauiId, pressed: pressed }))
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
