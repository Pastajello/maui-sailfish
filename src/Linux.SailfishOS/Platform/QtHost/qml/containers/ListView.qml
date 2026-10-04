import QtQuick 2.6
import Sailfish.Silica 1.0
import "../lib/pullrefresh.js" as PullRefresh

// Adapter: MAUI CollectionView/ItemsView -> Silica ListView.
// Contract: mauiId / mauiProbe / mauiEvent — see controls/Label.qml.
//
// The ListView owns virtualization, flick physics and row placeholders; MAUI
// owns row content, which is measured and positioned by the managed layout pass
// and materialized as children of the delegate (the delegate never lays it out).
//
// Events: list-item-attached/rebind/detached {id,row,dg}, list-item-released {id,dg,to},
// list-item-tapped
// {id,row,cell,x,y} (x/y delegate-relative, Qt units), list-scroll {id,y,first,last,count} (throttled, along the scroll
// axis), carousel-position {id,index}, refresh-requested {id}.
// mauiRowsJson: [{k,h,t}] where h is the extent along the scroll axis (Qt units) and t is 0 (inert), 1 (selectable,
// highlights on press) or 2 (tap recognizers in the template only, no highlight).
// mauiSelectedRows is highlight only ("0,3,5", or "row:cell" in grids); MAUI stays the selection authority.
// Grid rows hold mauiSpan cells mauiCellStride apart (Qt units, across the scroll axis: x in a vertical list, y in a
// horizontal one); taps report the touched cell.
// mauiCommand {"name":"scrollTo","row":N,"pos":P}: P 0=Start 1=Center 2=End 3=MakeVisible.
SilicaListView {
    id: root

    property string mauiId: ""
    property string mauiProbe: count + " rows, y=" + contentY

    signal mauiEvent(string name, string payload)

    // Adapter events are suppressed while managed pushes are applied (no echo loop).
    // Delegate lifecycle events bypass it: they are materialization requests, not echoes.
    property bool mauiApplying: false

    property string mauiRowsJson: "[]"
    property string mauiSelectedRows: ""
    property real mauiSpacing: 0
    property int mauiSpan: 1
    property real mauiCellWidth: 0
    property real mauiCellStride: 0


    property real mauiHeaderH: 0
    property real mauiFooterH: 0
    property real mauiEmptyH: 0

    property string mauiOrientation: "vertical"
    readonly property bool __horizontal: mauiOrientation === "horizontal"
    property bool mauiCarousel: false
    property real mauiPeekStart: 0
    property real mauiPeekEnd: 0
    property int mauiPosition: 0
    property bool mauiSwipeEnabled: true
    property bool mauiBounce: true

    orientation: __horizontal ? ListView.Horizontal : ListView.Vertical
    interactive: mauiSwipeEnabled
    // Carousel: one item per flick, the current page kept inside the peek window.
    snapMode: mauiCarousel ? ListView.SnapOneItem : ListView.NoSnap
    highlightRangeMode: mauiCarousel ? ListView.StrictlyEnforceRange : ListView.NoHighlightRange
    preferredHighlightBegin: mauiCarousel ? mauiPeekStart : 0
    preferredHighlightEnd: mauiCarousel ? (__horizontal ? width : height) - mauiPeekEnd : 0
    highlightMoveDuration: 250

    onMauiPositionChanged: __applyPosition()
    function __applyPosition() {
        if (!mauiCarousel || mauiPosition < 0 || mauiPosition >= count || currentIndex === mauiPosition)
            return;
        currentIndex = mauiPosition;
    }
    // A swipe: mauiPosition follows the native page before it is reported, so it always holds what is shown and the
    // managed side pushes a position only when MAUI's differs (QtHostListAdapter records the reported one as applied).
    onCurrentIndexChanged: if (mauiCarousel && !__rebuilding && currentIndex >= 0 && currentIndex !== mauiPosition) {
        mauiPosition = currentIndex;
        mauiEvent("carousel-position", JSON.stringify({ id: mauiId, index: currentIndex }));
    }

    // Set when a RefreshView wraps this list and the page has no pulley
    // (Silica's pull-down menu owns the top overscroll when present).
    property string mauiRefreshId: ""
    property bool mauiRefreshing: false
    property color mauiRefreshColor: "transparent"

    clip: true
    spacing: mauiSpacing
    // Pull-to-refresh needs the top overscroll; otherwise keep the hard stop.
    boundsBehavior: mauiRefreshId.length > 0 ? Flickable.DragOverBounds
                  : mauiCarousel && mauiBounce ? Flickable.DragAndOvershootBounds
                  : Flickable.StopAtBounds
    // Bounded cache so off-screen delegates are destroyed and their MAUI content
    // released; the Silica default keeps every delegate alive. Once the first rows
    // have settled it grows to two viewports each way (measured on the Kitchen catalog:
    // half the tiles showing empty on a cold cache vs one viewport), so rows scrolling in
    // and their images are built before they show; at open it stays small so the page
    // appears fast. Short rows are bounded by count instead (__prefetchRows each way), so
    // a list of one-line rows does not hold a hundred delegates the managed side must fill.
    property bool __prefetch: false
    readonly property real __prefetchViewports:
        typeof window !== "undefined" && window.mauiListPrefetch > 0 ? window.mauiListPrefetch : 2
    readonly property int __prefetchRows: 24
    property real __avgRowH: 0   // mean row extent incl. spacing, from the row model
    readonly property real __prefetchPx: {
        var px = __prefetchViewports * (__horizontal ? width : height);
        return __avgRowH > 0 ? Math.min(px, __prefetchRows * __avgRowH) : px;
    }
    cacheBuffer: __prefetch ? Math.max(256, __prefetchPx) : 256
    Timer {
        id: prefetchTimer
        interval: 600
        onTriggered: root.__prefetch = true
    }
    // Rows can arrive as creation props, which fire no change handler.
    Component.onCompleted: if (mauiRowsJson.length > 2) prefetchTimer.start()
    model: ListModel { id: rowModel }

    onMauiRowsJsonChanged: { __rebuildRows(); if (!__prefetch && !prefetchTimer.running) prefetchTimer.start(); }
    onMauiSelectedRowsChanged: __applySelection()
    // Throttle, not debounce: a restart() would delay the report until a fling stops.
    onContentXChanged: if (__horizontal && !scrollReportTimer.running) scrollReportTimer.start()
    onContentYChanged: {
        PullRefresh.track(root);
        if (!scrollReportTimer.running)
            scrollReportTimer.start();
    }
    // Releasing a top overscroll deeper than itemSizeMedium requests a refresh.
    onDragEnded: PullRefresh.release(root, Theme.itemSizeMedium)
    onMovementEnded: {
        PullRefresh.release(root, Theme.itemSizeMedium);
        __restingAtTop = !__horizontal && atYBeginning;
    }

    // Qt keeps the first row in place when the header's height changes, which pushes a grown header out of view
    // (a rotation, a section appearing); a list resting at its top stays there, as a MAUI header does elsewhere.
    property bool __restingAtTop: true
    onMauiHeaderHChanged: if (!__horizontal && __restingAtTop) headerTopTimer.restart()
    Timer {
        id: headerTopTimer
        interval: 0   // after the header item took its new height
        onTriggered: root.positionViewAtBeginning()
    }

    // Pull state for lib/pullrefresh.js.
    property real __pullDepth: 0
    property bool __pullDecided: false

    // A model reset moves currentIndex (-1, then 0); the carousel must not report
    // it and returns to the managed page without animation.
    property bool __rebuilding: false

    // Edits animate Silica-style: others slide aside, new rows fade in (hiding
    // the frame before their MAUI content lands), removed rows fade out.
    property bool __animateEdits: false
    add: __animateEdits ? addTransition : null
    remove: __animateEdits ? removeTransition : null
    displaced: __animateEdits ? displacedTransition : null
    Transition {
        id: addTransition
        // appears once the others have made room (no text over text)
        SequentialAnimation {
            PropertyAction { property: "opacity"; value: 0 }
            PauseAnimation { duration: 170 }
            NumberAnimation { property: "opacity"; to: 1; duration: 180; easing.type: Easing.InOutQuad }
        }
    }
    Transition {
        id: removeTransition
        NumberAnimation { property: "opacity"; to: 0; duration: 150; easing.type: Easing.InOutQuad }
    }
    Transition {
        id: displacedTransition
        NumberAnimation { properties: "x,y"; duration: 200; easing.type: Easing.InOutQuad }
    }
    // mauiRowsJson rows (QtHostListAdapter.RowJson): k stable key, r index, h height (Qt units), t tap kind
    // (0 none, 1 selectable, 2 a tap gesture), n cells in a grid row; s is the selected role mauiSelectedRows flips.
    function __rebuildRows() {
        var rows = [];
        try { rows = JSON.parse(mauiRowsJson); } catch (e) { rows = []; }
        __rebuilding = true;
        __syncRows(rows);
        if (mauiCarousel && mauiPosition >= 0 && mauiPosition < count) {
            var move = highlightMoveDuration;
            highlightMoveDuration = 0;
            currentIndex = mauiPosition;
            positionViewAtIndex(mauiPosition, ListView.SnapPosition);
            highlightMoveDuration = move;
        }
        __rebuilding = false;
        __applySelection();
        __reportScroll();
    }

    // Keyed diff instead of clear+refill, so Add/Remove touches only that row and
    // other delegates keep their MAUI content. Shifted rows get a new `r`, which the
    // delegate reports as a rebind of the same row.
    function __syncRows(rows) {
        var i, j;
        var keyed = rows.length === 0 || rows[0].k !== undefined;
        var wanted = {};
        for (i = 0; keyed && i < rows.length; ++i)
            wanted[rows[i].k] = true;
        var kept = 0;
        for (i = 0; keyed && i < rowModel.count; ++i)
            if (wanted[rowModel.get(i).k])
                ++kept;
        // animate edits of a filled list only: not the first fill, not a carousel page reset, and not a new
        // ItemsSource (no row kept), which Android and iOS redraw at once; animating every row out and in
        // blinked the list, and an add transition that stalled left it blank until the next touch
        __animateEdits = keyed && kept > 0 && !mauiCarousel;
        if (!keyed) {
            rowModel.clear();
        } else {
            for (i = rowModel.count - 1; i >= 0; --i)
                if (!wanted[rowModel.get(i).k])
                    rowModel.remove(i);
        }
        for (i = 0; i < rows.length; ++i) {
            var n = rows[i];
            var h = n.h || 0, t = n.t || 0, c = n.n === undefined ? 1 : n.n;
            if (keyed && i < rowModel.count && rowModel.get(i).k !== n.k) {
                for (j = i + 1; j < rowModel.count; ++j)
                    if (rowModel.get(j).k === n.k) {
                        rowModel.move(j, i, 1);
                        break;
                    }
            }
            if (keyed && i < rowModel.count && rowModel.get(i).k === n.k) {
                var cur = rowModel.get(i);
                if (cur.h !== h) rowModel.setProperty(i, "h", h);
                if (cur.t !== t) rowModel.setProperty(i, "t", t);
                if (cur.n !== c) rowModel.setProperty(i, "n", c);
            } else {
                rowModel.insert(i, { k: keyed ? n.k : i, r: i, h: h, n: c,
                                     s: "",   // selected cells ",0,2,"; set by __applySelection
                                     t: t });
            }
        }
        // indices last: every delegate is at its final position, so its
        // rebind (row + name) is unambiguous
        var total = 0;
        for (i = 0; i < rowModel.count; ++i) {
            if (rowModel.get(i).r !== i)
                rowModel.setProperty(i, "r", i);
            total += rowModel.get(i).h;
        }
        __avgRowH = rowModel.count > 0 ? total / rowModel.count + mauiSpacing : 0;
    }

    function __applySelection() {
        var cells = {};
        var parts = mauiSelectedRows.split(",");
        for (var i = 0; i < parts.length; ++i) {
            if (parts[i].length === 0)
                continue;
            var rc = parts[i].split(":");
            var row = parseInt(rc[0], 10);
            cells[row] = (cells[row] || ",") + (rc.length > 1 ? parseInt(rc[1], 10) : 0) + ",";
        }
        for (var r = 0; r < rowModel.count; ++r) {
            var want = cells[r] || "";
            if (rowModel.get(r).s !== want)
                rowModel.setProperty(r, "s", want);
        }
    }

    function __reportScroll() {
        // Arithmetic over the row model, not indexAt(): indexAt() on an unfilled
        // list instantiates delegates synchronously (startup stall) and returns -1
        // over the header or past short content. A header can sit at negative
        // x/y, so the offset is measured from the origin.
        var offset = __horizontal ? contentX - originX : contentY - originY;
        var top = offset;
        var bottom = offset + (__horizontal ? width : height);
        var y = mauiHeaderH;
        var first = -1;
        var last = -1;
        for (var i = 0; i < rowModel.count; ++i) {
            var rowTop = y;
            var rowBottom = y + rowModel.get(i).h;
            if (first < 0 && rowBottom > top)
                first = i;
            if (rowTop < bottom)
                last = i;
            y = rowBottom + mauiSpacing;
            if (rowTop >= bottom)
                break;
        }
        mauiEvent("list-scroll", JSON.stringify({
            id: mauiId, y: offset, first: first, last: last, count: count }));
    }

    // Managed commands (QtHostRuntime.Invoke "mauiCommand", docs/custom-controls.md): one call per request.
    function mauiCommand(json) {
        var c = JSON.parse(json);
        if (c.name === "scrollTo")
            __scrollToRow(c.row, c.pos);
    }

    function __scrollToRow(row, where) {
        if (row < 0 || row >= count)
            return;
        // Qt 5.6 positionViewAtIndex is immediate, so managed ScrollTo never animates.
        var pos = where === 0 ? ListView.Beginning
                : where === 1 ? ListView.Center
                : where === 2 ? ListView.End
                : ListView.Contain;
        positionViewAtIndex(row, pos);
        __restingAtTop = !__horizontal && atYBeginning;
        __reportScroll();
    }

    Timer {
        id: scrollReportTimer
        interval: 60
        repeat: false
        onTriggered: root.__reportScroll()
    }

    // Serial for released delegate names (see the delegate's ListView.onRemove).
    property int __releaseSerial: 0

    delegate: Item {
        id: dg
        objectName: __releasedName.length > 0 ? __releasedName : "maui_" + root.mauiId + "__r" + r

        // A row removed from the model stays alive through the remove transition while its replacement
        // (a rebuild after an ItemsLayout change, a refill) is created under the same "__r<n>" name. It
        // hands the name over at once, so the replacement's lookups and the later detach of this one
        // cannot hit each other; managed re-keys this delegate's content to the released name.
        property string __releasedName: ""
        ListView.onRemove: {
            var was = objectName;
            __releasedName = "maui_" + root.mauiId + "__x" + (++root.__releaseSerial);
            root.mauiEvent("list-item-released",
                JSON.stringify({ id: root.mauiId, dg: was, to: __releasedName }));
        }
        width: root.__horizontal ? h : root.width
        height: root.__horizontal ? root.height : h

        property int mauiRow: r
        readonly property bool __grid: root.mauiSpan > 1 && root.mauiCellStride > 0
        property int __pressCell: -1

        // The grid cell under the point across the scroll axis (-1 in the spacing or past this row's cells); a
        // plain row is one cell.
        function __cellAt(x, y) {
            if (!__grid)
                return 0;
            var p = root.__horizontal ? y : x;
            var cell = Math.floor(p / root.mauiCellStride);
            if (cell < 0 || cell >= n || p - cell * root.mauiCellStride > root.mauiCellWidth)
                return -1;
            return cell;
        }

        // Selection/press highlight per cell; MAUI children are created later and stack above it.
        Repeater {
            model: dg.__grid ? root.mauiSpan : 1
            Rectangle {
                readonly property bool pressed: t === 1 && tapArea.pressed && dg.__pressCell === index
                x: dg.__grid && !root.__horizontal ? index * root.mauiCellStride : 0
                y: dg.__grid && root.__horizontal ? index * root.mauiCellStride : 0
                width: dg.__grid && !root.__horizontal ? root.mauiCellWidth : dg.width
                height: dg.__grid && root.__horizontal ? root.mauiCellWidth : dg.height
                radius: 8
                color: pressed ? Theme.rgba(Theme.highlightColor, 0.45)
                               : Theme.rgba(Theme.highlightColor, 0.22)
                visible: pressed || s.indexOf("," + index + ",") >= 0
            }
        }

        MouseArea {
            id: tapArea
            anchors.fill: parent
            enabled: t !== 0
            onPressed: dg.__pressCell = dg.__cellAt(mouse.x, mouse.y)
            onClicked: {
                if (dg.__pressCell >= 0)
                    root.mauiEvent("list-item-tapped",
                        JSON.stringify({ id: root.mauiId, row: r, cell: dg.__pressCell, x: mouse.x, y: mouse.y }))
            }
        }

        // the name spelled out: objectName's binding may not have re-run yet
        onMauiRowChanged: if (__releasedName.length === 0) root.mauiEvent("list-item-rebind",
            JSON.stringify({ id: root.mauiId, row: r, dg: "maui_" + root.mauiId + "__r" + r }))
        Component.onCompleted: root.mauiEvent("list-item-attached",
            JSON.stringify({ id: root.mauiId, row: r, dg: objectName }))
        // The whole list may be going away with its page: root is gone then.
        Component.onDestruction: if (root) root.mauiEvent("list-item-detached",
            JSON.stringify({ id: root.mauiId, dg: objectName }))
    }

    // Header/footer/empty slots are found by objectName and filled by managed code;
    // their extent runs along the scroll axis.
    // A horizontal header's width arrives after the first layout and Qt stays on
    // column 0, so until the user scrolls the list follows originX (vertical lists do this themselves).
    property bool __userScrolled: false
    onMovementStarted: __userScrolled = true
    onOriginXChanged: if (__horizontal && !__userScrolled && !moving && contentX !== originX) contentX = originX

    header: Item {
        objectName: "maui_" + root.mauiId + "__header"
        width: root.__horizontal ? root.mauiHeaderH : root.width
        height: root.__horizontal ? root.height : root.mauiHeaderH
        visible: root.mauiHeaderH > 0
    }

    footer: Item {
        objectName: "maui_" + root.mauiId + "__footer"
        width: root.__horizontal ? root.mauiFooterH : root.width
        height: root.__horizontal ? root.height : root.mauiFooterH
        visible: root.mauiFooterH > 0
    }

    // Empty view: pinned to the viewport, visible only while there are no rows.
    Item {
        objectName: "maui_" + root.mauiId + "__empty"
        width: root.width
        height: root.mauiEmptyH
        x: root.__horizontal ? root.contentX : 0
        y: root.__horizontal ? 0 : root.contentY
        visible: root.count === 0 && height > 0
    }

    // A plain-text EmptyView: the native empty-state text, shown while the list has no rows.
    property string mauiPlaceholderText: ""
    ViewPlaceholder {
        objectName: "maui_" + root.mauiId + "__placeholder"
        flickable: root
        enabled: root.count === 0 && root.mauiPlaceholderText.length > 0
        text: root.mauiPlaceholderText
    }

    // ScrollBarVisibility per axis: 0 Default (while moving), 1 Always, 2 Never. A carousel pages, it has none.
    property int mauiVBar: 0
    property int mauiHBar: 0
    VerticalScrollDecorator {
        id: vDecorator
        flickable: root
        visible: !root.__horizontal && !root.mauiCarousel && root.mauiVBar !== 2
    }
    HorizontalScrollDecorator {
        id: hDecorator
        flickable: root
        visible: root.__horizontal && !root.mauiCarousel && root.mauiHBar !== 2
    }
    Binding { target: vDecorator; property: "opacity"; value: 1.0; when: root.mauiVBar === 1 }
    Binding { target: hDecorator; property: "opacity"; value: 1.0; when: root.mauiHBar === 1 }
    // Diagnostics readback of the decorators, as on ScrollView.qml.
    readonly property string mauiBars: "h=" + (hDecorator.visible ? hDecorator.opacity : "hidden") +
                                       " v=" + (vDecorator.visible ? vDecorator.opacity : "hidden")

    // RefreshView spinner, pinned to the viewport like the empty slot.
    BusyIndicator {
        size: BusyIndicatorSize.Medium
        running: root.mauiRefreshing
        visible: root.mauiRefreshing
        color: root.mauiRefreshColor.a > 0 ? root.mauiRefreshColor : Theme.highlightColor
        width: Theme.itemSizeMedium
        height: Theme.itemSizeMedium
        x: (root.width - width) / 2
        y: root.contentY + Theme.paddingMedium
    }
}
