import QtQuick 2.6
import Sailfish.Silica 1.0

// Adapter: MAUI docked sheet -> Silica DockedPanel. Page-level (mauiDetached): reparents to the
// page because a DockedPanel docks against its parent's edges. mauiSize is the extent along the
// dock axis (Qt units). "panel-open-changed" mirrors user drags back into mauiOpen.
DockedPanel {
    id: root

    property string mauiId: ""
    property string mauiProbe: ""
    signal mauiEvent(string name, string payload)

    // Managed property pushes set this flag; events fired meanwhile are suppressed (no echo).
    property bool mauiApplying: false

    // Page-level host: MauiModelPage skips canvas parenting and order moves for it.
    property bool mauiDetached: true
    property var mauiPage: null

    property string mauiDock: "bottom"
    property bool mauiOpen: false
    property real mauiSize: 0
    property string mauiText: ""

    dock: mauiDock === "top" ? Dock.Top
        : mauiDock === "left" ? Dock.Left
        : mauiDock === "right" ? Dock.Right
        : Dock.Bottom

    // DockedPanel does no layout: mauiSize along the dock axis (default a third), full page across.
    width: dock === Dock.Left || dock === Dock.Right
           ? (mauiSize > 0 ? mauiSize : (parent ? parent.width / 2 : 0))
           : (parent ? parent.width : 0)
    height: dock === Dock.Left || dock === Dock.Right
            ? (parent ? parent.height : 0)
            : (mauiSize > 0 ? mauiSize : (parent ? parent.height / 3 : 0))

    onMauiPageChanged: __attach()
    Component.onCompleted: __attach()

    function __attach() {
        if (mauiPage && parent !== mauiPage)
            parent = mauiPage;
    }

    // managed -> native
    onMauiOpenChanged: {
        if (open !== mauiOpen)
            open = mauiOpen;
    }
    // native -> managed (user drag / modal outside press)
    onOpenChanged: {
        if (open !== mauiOpen)
            mauiOpen = open;
        if (!mauiApplying)
            mauiEvent("panel-open-changed",
                      JSON.stringify({ id: mauiId, open: open }))
    }

    Label {
        anchors { left: parent.left; right: parent.right; top: parent.top
                  margins: Theme.paddingLarge }
        text: root.mauiText
        color: Theme.primaryColor
        visible: text.length > 0
    }
}
