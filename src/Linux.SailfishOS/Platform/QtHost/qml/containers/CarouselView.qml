import QtQuick 2.6
import Sailfish.Silica 1.0

// Adapter: MAUI CarouselView with Loop=true -> looping PathView (a ListView cannot wrap; the
// non-looping carousel uses containers/ListView.qml). It implements the same collection bridge
// contract: delegates are "maui_<id>__r<row>" placeholders, and it accepts the unused list-view
// properties so shared pushes land. Only previous/current/next pages stay alive on the path.
PathView {
    id: root

    property string mauiId: ""
    property string mauiProbe: count + " pages, at " + currentIndex
    signal mauiEvent(string name, string payload)
    property bool mauiApplying: false

    property string mauiRowsJson: "[]"
    property string mauiSelectedRows: ""
    // Grid cell layout the bridge pushes to every list adapter; a carousel page is one cell.
    property int mauiSpan: 1
    property real mauiCellWidth: 0
    property real mauiCellStride: 0
    property real mauiSpacing: 0
    property string mauiOrientation: "horizontal"
    property bool mauiCarousel: true
    property real mauiPeekStart: 0
    property real mauiPeekEnd: 0
    property int mauiPosition: 0
    property bool mauiSwipeEnabled: true
    property bool mauiBounce: true
    property int mauiScrollRow: -1
    property int mauiScrollPos: 3
    property int mauiScrollTick: 0
    property real mauiHeaderH: 0
    property real mauiFooterH: 0
    property real mauiEmptyH: 0

    readonly property real __pageW: Math.max(1, width - mauiPeekStart - mauiPeekEnd)
    readonly property real __step: __pageW + mauiSpacing

    clip: true
    model: ListModel { id: rowModel }
    interactive: mauiSwipeEnabled && count > 1
    pathItemCount: Math.min(count, 3)
    snapMode: PathView.SnapOneItem
    highlightRangeMode: PathView.StrictlyEnforceRange
    preferredHighlightBegin: 0.5
    preferredHighlightEnd: 0.5
    highlightMoveDuration: 250
    // The path spans three pages, centered on the page box between the peeks.
    path: Path {
        startX: root.mauiPeekStart + root.__pageW / 2 - 1.5 * root.__step
        startY: root.height / 2
        PathLine {
            x: root.mauiPeekStart + root.__pageW / 2 + 1.5 * root.__step
            y: root.height / 2
        }
    }

    property bool __rebuilding: false
    onMauiRowsJsonChanged: {
        var rows = [];
        try { rows = JSON.parse(mauiRowsJson); } catch (e) { rows = []; }
        __rebuilding = true;
        rowModel.clear();
        for (var i = 0; i < rows.length; ++i)
            rowModel.append({ r: rows[i].r | 0, h: rows[i].h || 0 });
        if (mauiPosition >= 0 && mauiPosition < count) {
            var move = highlightMoveDuration;
            highlightMoveDuration = 0;
            currentIndex = mauiPosition;
            positionViewAtIndex(mauiPosition, PathView.Center);
            highlightMoveDuration = move;
        }
        __rebuilding = false;
    }
    onMauiPositionChanged: if (mauiPosition >= 0 && mauiPosition < count && currentIndex !== mauiPosition) currentIndex = mauiPosition
    onCurrentIndexChanged: if (!__rebuilding && currentIndex >= 0 && currentIndex !== mauiPosition)
        mauiEvent("carousel-position", JSON.stringify({ id: mauiId, index: currentIndex }))

    delegate: Item {
        objectName: "maui_" + root.mauiId + "__r" + r
        width: root.__pageW
        height: root.height
        property int mauiRow: r

        onMauiRowChanged: root.mauiEvent("list-item-rebind",
            JSON.stringify({ id: root.mauiId, row: r, dg: objectName }))
        Component.onCompleted: root.mauiEvent("list-item-attached",
            JSON.stringify({ id: root.mauiId, row: r, dg: objectName }))
        Component.onDestruction: if (root) root.mauiEvent("list-item-detached",
            JSON.stringify({ id: root.mauiId, dg: objectName }))
    }
}
