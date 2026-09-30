import QtQuick 2.6
import Sailfish.Silica 1.0

// Adapter: MAUI edge drawer (FlyoutPage flyout) -> Silica Drawer. Page-level (mauiDetached):
// reparents to the page; its default children are the foreground and `background` stays empty
// so the page shows through. Event: "drawer-open-changed".
Drawer {
    id: root

    property string mauiId: ""
    property string mauiProbe: ""
    signal mauiEvent(string name, string payload)

    // Managed property pushes set this flag; events fired meanwhile are suppressed (no echo).
    property bool mauiApplying: false

    // Page-level host: MauiModelPage skips canvas parenting and order moves for it.
    property bool mauiDetached: true
    property var mauiPage: null

    property string mauiDock: "left"
    property bool mauiOpen: false
    property real mauiSize: 0
    property string mauiText: ""

    dock: mauiDock === "top" ? Dock.Top
        : mauiDock === "right" ? Dock.Right
        : mauiDock === "bottom" ? Dock.Bottom
        : Dock.Left

    width: dock === Dock.Left || dock === Dock.Right
           ? (mauiSize > 0 ? mauiSize : (parent ? parent.width * 0.8 : 0))
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
    // native -> managed (show()/hide() or animation)
    onOpenChanged: {
        if (open !== mauiOpen)
            mauiOpen = open;
        if (!mauiApplying)
            mauiEvent("drawer-open-changed",
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
