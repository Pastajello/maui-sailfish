import QtQuick 2.6
import Sailfish.Silica 1.0
import "../lib/pullrefresh.js" as PullRefresh

// Adapter: MAUI ScrollView -> SilicaFlickable.
// Contract: mauiId / mauiProbe / mauiEvent — see controls/Label.qml.
// Content hosts are created in mauiChildHost (contentItem) so they scroll natively.
// All sizes and offsets are Qt units. Events: scroll-changed {id,x,y},
// refresh-requested {id} (armed when a RefreshView wraps it and the page has no pulley).
SilicaFlickable {
    id: root

    property string mauiId: ""
    property string mauiProbe: ""
    signal mauiEvent(string name, string payload)

    // Adapter events are suppressed while managed pushes are applied (no echo loop).
    property bool mauiApplying: false
    property int mauiSuppressedCount: 0

    // Nested hosts go into the scrolled content, not the viewport.
    property Item mauiChildHost: contentItem

    property string mauiOrientation: "vertical"
    property real mauiContentWidth: 0
    property real mauiContentHeight: 0
    property real mauiScrollX: 0
    property real mauiScrollY: 0
    property color mauiBackground: "transparent"
    property string mauiRefreshId: ""
    property bool mauiRefreshing: false
    property color mauiRefreshColor: "transparent"
    // ScrollBarVisibility per axis: 0 Default (while moving), 1 Always, 2 Never.
    property int mauiHBar: 0
    property int mauiVBar: 0

    contentWidth: mauiContentWidth
    contentHeight: mauiContentHeight
    interactive: mauiOrientation !== "neither"
    flickableDirection: mauiOrientation === "horizontal" ? Flickable.HorizontalFlick
                      : mauiOrientation === "both" ? Flickable.HorizontalAndVerticalFlick
                      : Flickable.VerticalFlick
    clip: true

    // Managed scrolls are clamped to the range as on other MAUI platforms (Flickable
    // would show empty space). A target clamped short because the content size has not
    // arrived yet stays pending until it grows; a native drag drops it.
    function __clampX(v) { return Math.max(0, Math.min(v, Math.max(0, contentWidth - width))); }
    function __clampY(v) { return Math.max(0, Math.min(v, Math.max(0, contentHeight - height))); }
    property real __pendingX: -1
    property real __pendingY: -1
    function __scrollToX(v) { var x = __clampX(v); __pendingX = x < v - 0.5 ? v : -1; if (Math.abs(contentX - x) > 0.5) contentX = x; if (Math.abs(x - v) > 0.5) __clampReport.restart(); }
    function __scrollToY(v) { var y = __clampY(v); __pendingY = y < v - 0.5 ? v : -1; if (Math.abs(contentY - y) > 0.5) contentY = y; if (Math.abs(y - v) > 0.5) __clampReport.restart(); }
    // A clamped managed scroll (a ScrollTo past the end) reports where the content stopped once the push is applied,
    // as the native views write the clamped position back on Android: ScrollX/ScrollY and the content's hit rects
    // then match what is drawn. During the push the report would be taken for an echo.
    Timer { id: __clampReport; interval: 0; onTriggered: root.__report() }
    onMauiScrollXChanged: __scrollToX(mauiScrollX)
    onMauiScrollYChanged: __scrollToY(mauiScrollY)
    onContentWidthChanged: if (__pendingX >= 0) __scrollToX(__pendingX)
    onContentHeightChanged: if (__pendingY >= 0) __scrollToY(__pendingY)
    onMovementStarted: { __pendingX = -1; __pendingY = -1; }

    function __report() {
        if (mauiApplying) { mauiSuppressedCount++; return; }
        mauiEvent("scroll-changed", JSON.stringify({ id: mauiId, x: contentX, y: contentY }));
    }
    onContentXChanged: __report()
    onContentYChanged: {
        __report();
        PullRefresh.track(root);
    }

    // Pull-to-refresh uses the same gesture as containers/ListView.qml.
    // This replaces SilicaFlickable's own boundsBehavior binding, so the pulley
    // conditions are repeated; refresh and a pulley never arm together.
    boundsBehavior: mauiRefreshId.length > 0
                    || (pullDownMenu && pullDownMenu._activationPermitted)
                    || (pushUpMenu && pushUpMenu._activationPermitted)
                    ? Flickable.DragOverBounds : Flickable.StopAtBounds
    property real __pullDepth: 0
    property bool __pullDecided: false
    onDragEnded: PullRefresh.release(root, Theme.itemSizeMedium)
    onMovementEnded: PullRefresh.release(root, Theme.itemSizeMedium)

    // The fill sits behind the viewport (a plain child of a Flickable would
    // land in contentItem and scroll away).
    Rectangle {
        parent: root
        anchors.fill: parent
        z: -1
        color: root.mauiBackground
        visible: color.a > 0
    }

    // The spinner rides the viewport (a plain child would scroll away).
    BusyIndicator {
        parent: root
        size: BusyIndicatorSize.Medium
        running: root.mauiRefreshing
        visible: root.mauiRefreshing
        color: root.mauiRefreshColor.a > 0 ? root.mauiRefreshColor : Theme.highlightColor
        width: Theme.itemSizeMedium
        height: Theme.itemSizeMedium
        x: (root.width - width) / 2
        y: Theme.paddingMedium
        z: 1
    }

    HorizontalScrollDecorator {
        id: hDecorator
        flickable: root
        visible: root.mauiOrientation !== "vertical" && root.mauiHBar !== 2
    }
    VerticalScrollDecorator {
        id: vDecorator
        flickable: root
        visible: (root.mauiOrientation === "vertical" || root.mauiOrientation === "both") && root.mauiVBar !== 2
    }
    Binding { target: hDecorator; property: "opacity"; value: 1.0; when: root.mauiHBar === 1 }
    Binding { target: vDecorator; property: "opacity"; value: 1.0; when: root.mauiVBar === 1 }
    // Diagnostics readback of the decorators.
    readonly property string mauiBars: "h=" + (hDecorator.visible ? hDecorator.opacity : "hidden") +
                                       " v=" + (vDecorator.visible ? vDecorator.opacity : "hidden")
}
