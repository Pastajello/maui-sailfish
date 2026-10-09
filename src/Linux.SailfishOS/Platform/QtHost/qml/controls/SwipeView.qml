import QtQuick 2.6
import Sailfish.Silica 1.0
import QtGraphicalEffects 1.0

// Adapter: MAUI SwipeView -> horizontal Flickable with [left items | content | right items].
// The flickable only takes the drag past its threshold, so taps inside the content still work;
// release snaps open or closed. Items are JSON [{text, icon, bg, fg, iconColor}]; "execute" mode invokes the
// first item past mauiThreshold (Qt units, 0 = half the panel). mauiCommand {"name":"open","side"} is managed
// Open/Close. Events: "swipe-item-invoked", "swipe-state", "swipe-changing" {id,offset} while a finger drags.
Flickable {
    id: root

    property string mauiId: ""
    property string mauiProbe: "open=" + __openSide
    signal mauiEvent(string name, string payload)

    // Managed property pushes set this flag; events fired meanwhile are suppressed (no echo).
    property bool mauiApplying: false

    // The MAUI content lives in the middle cell.
    property Item mauiChildHost: contentCell

    property string mauiLeftItems: "[]"
    property string mauiRightItems: "[]"
    property string mauiLeftMode: "reveal"
    property string mauiRightMode: "reveal"
    property real mauiThreshold: 0
    // "reveal" (MAUI default: item rows pinned to the viewport edges, content slides over them)
    // or "drag" (rows travel with the content).
    // BackgroundColor/Background (ContainerProps): painted under the content, moving with it like a container's
    // background. Undeclared, the shim rejected every SwipeView batch that carried it.
    property color mauiBackground: "transparent"
    property string mauiTransition: "reveal"
    readonly property bool __reveal: mauiTransition !== "drag"

    property var __left: []
    property var __right: []
    property string __openSide: ""
    // A finger drag reports its offset (Qt units, > 0 revealing the left items) as swipe-changing; the drag then
    // ends with a swipe-state even when it settles where it started.
    property real __lastOffset: 0
    property bool __swiped: false
    onDraggingChanged: if (dragging) __lastOffset = 0
    onContentXChanged: {
        if (!dragging || mauiApplying)
            return;
        var offset = __closedX - contentX;
        if (Math.abs(offset - __lastOffset) < 4)
            return;
        __lastOffset = offset;
        __swiped = true;
        mauiEvent("swipe-changing", JSON.stringify({ id: mauiId, offset: offset }));
    }

    function __parse(json) { try { return JSON.parse(json); } catch (e) { return []; } }
    onMauiLeftItemsChanged: __left = __parse(mauiLeftItems)
    onMauiRightItemsChanged: __right = __parse(mauiRightItems)

    clip: true
    flickableDirection: Flickable.HorizontalFlick
    boundsBehavior: Flickable.StopAtBounds
    pixelAligned: true
    interactive: enabled && (__left.length > 0 || __right.length > 0)
    contentWidth: leftPanel.width + width + rightPanel.width
    contentHeight: height

    readonly property real __closedX: leftPanel.width
    function __xFor(side) { return side === "left" ? 0 : side === "right" ? leftPanel.width + rightPanel.width : __closedX; }

    // Keep the closed row aligned when the panels or the width change.
    onWidthChanged: if (!moving && !snap.running) contentX = __xFor(__openSide)
    on__ClosedXChanged: if (!moving && !snap.running) contentX = __xFor(__openSide)
    Component.onCompleted: contentX = __closedX

    NumberAnimation { id: snap; target: root; property: "contentX"; duration: 180; easing.type: Easing.OutQuad }

    function __setState(side) {
        snap.stop();
        snap.to = __xFor(side);
        snap.start();
        var swiped = __swiped;
        __swiped = false;
        if (side === __openSide) {
            if (swiped && !mauiApplying)
                mauiEvent("swipe-state", JSON.stringify({ id: mauiId, side: side, open: side !== "" }));
            return;
        }
        __openSide = side;
        if (!mauiApplying)
            mauiEvent("swipe-state", JSON.stringify({ id: mauiId, side: side, open: side !== "" }));
    }

    function __invoke(side, index) {
        mauiEvent("swipe-item-invoked", JSON.stringify({ id: mauiId, side: side, index: index }));
        __setState("");
    }

    // Past the threshold opens (or in execute mode invokes) the revealed side; otherwise closes.
    onMovementEnded: __settle()
    function __settle() {
        if (snap.running)
            return;
        var leftShown = __closedX - contentX;           // > 0 while revealing the left items
        var rightShown = contentX - __closedX;          // > 0 while revealing the right items
        if (leftShown > 0 && leftPanel.width > 0) {
            var lt = mauiThreshold > 0 ? mauiThreshold : leftPanel.width / 2;
            if (leftShown >= lt) {
                if (mauiLeftMode === "execute") { __invoke("left", 0); return; }
                __setState("left"); return;
            }
        } else if (rightShown > 0 && rightPanel.width > 0) {
            var rt = mauiThreshold > 0 ? mauiThreshold : rightPanel.width / 2;
            if (rightShown >= rt) {
                if (mauiRightMode === "execute") { __invoke("right", 0); return; }
                __setState("right"); return;
            }
        }
        __setState("");
    }

    // Managed commands (QtHostRuntime.Invoke "mauiCommand"): {"name":"open","side":"left"|"right"|""}.
    function mauiCommand(json) {
        var c = JSON.parse(json);
        if (c.name === "open")
            __setState(c.side);
    }

    // Diag: scene center "x,y" of swipe item `index` on `side`.
    function mauiItemPoint(side, index) {
        var row = side === "left" ? leftRow : rightRow;
        var n = 0;
        for (var i = 0; i < row.children.length; ++i) {
            var b = row.children[i];
            if (b.side === undefined)
                continue;   // the Repeater itself
            if (n++ === index) {
                var p = b.mapToItem(null, b.width / 2, b.height / 2);
                return p.x + "," + p.y;
            }
        }
        return "";
    }

    Row {
        Item {
            id: leftPanel
            width: leftRow.width
            height: root.height
            // Reveal: pinned to the viewport's left edge; hidden while closed so a transparent MAUI
            // content does not show it.
            Row {
                id: leftRow
                x: root.__reveal ? root.contentX : 0
                visible: root.contentX < root.__closedX - 0.5
                Repeater { model: root.__left; delegate: swipeButton }
            }
            property string side: "left"
        }
        Item {
            id: contentCell
            width: root.width
            height: root.height
            z: 1   // above the pinned rows in reveal mode
            Rectangle {
                anchors.fill: parent
                z: -1   // under the MAUI content hosts (mauiChildHost)
                color: root.mauiBackground
                visible: root.mauiBackground.a > 0
            }
        }
        Item {
            id: rightPanel
            width: rightRow.width
            height: root.height
            // Reveal: pinned to the viewport's right edge.
            Row {
                id: rightRow
                x: root.__reveal ? root.contentX + root.width - rightRow.width - rightPanel.x : 0
                visible: root.contentX > root.__closedX + 0.5
                Repeater { model: root.__right; delegate: swipeButton }
            }
            property string side: "right"
        }
    }

    Component {
        id: swipeButton
        MouseArea {
            // The Repeater's parent Row sits in a panel carrying its side.
            readonly property string side: parent && parent.parent ? parent.parent.side : ""
            width: Math.max(Theme.itemSizeLarge, label.implicitWidth + 2 * Theme.paddingLarge)
            height: root.height
            onClicked: root.__invoke(side, index)

            Rectangle {
                anchors.fill: parent
                color: modelData.bg ? modelData.bg : Theme.rgba(Theme.highlightBackgroundColor, Theme.highlightBackgroundOpacity)
                opacity: parent.pressed ? 0.7 : 1
            }
            Column {
                anchors.centerIn: parent
                spacing: Theme.paddingSmall
                Image {
                    anchors.horizontalCenter: parent.horizontalCenter
                    source: modelData.icon ? modelData.icon : ""
                    visible: source != ""
                    width: Theme.iconSizeMedium
                    height: Theme.iconSizeMedium
                    sourceSize.width: width
                    sourceSize.height: height
                    // SwipeItem.IconColor (MAUI 11): the icon tinted, else drawn in its own colours.
                    layer.enabled: !!modelData.iconColor
                    layer.effect: ColorOverlay { color: modelData.iconColor || "transparent" }
                }
                // QtQuick Text, not Label: the sibling Label.qml adapter shadows the Silica type (as RadioButton.qml).
                Text {
                    id: label
                    anchors.horizontalCenter: parent.horizontalCenter
                    text: modelData.text || ""
                    color: modelData.fg ? modelData.fg : Theme.primaryColor
                    font.pixelSize: Theme.fontSizeSmall
                }
            }
        }
    }
}
