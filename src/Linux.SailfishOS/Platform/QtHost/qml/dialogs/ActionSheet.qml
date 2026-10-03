import QtQuick 2.6
import Sailfish.Silica 1.0
import "../lib/adapter.js" as Adapter

// Adapter: MAUI DisplayActionSheetAsync -> a system-dialog panel (DialogPanel.qml) listing the choices, as the
// system's option dialogs (USB mode selector): one full-width row per choice, centred text, highlighted while
// pressed; the cancel text as the panel's button.
// Contract: mauiId / mauiProbe / mauiEvent — see controls/Label.qml.
// Events: action-selected {id,index,text} (index -1 = destructive entry),
// action-cancelled (payload = mauiId) on the cancel button or a tap on the dimmed page.
DialogPanel {
    id: root

    mauiProbe: "sheet:" + root.mauiActions.length

    property string mauiCancel: ""
    property string mauiDestruction: ""
    property var mauiActions: []

    // Entry items (each has `label`), for mapping injected diagnostic taps.
    property var __items: []

    function __select(index, text) {
        if (root.closing)
            return
        root.closing = true
        mauiEvent("action-selected", JSON.stringify({ id: root.mauiId, index: index, text: text }))
    }

    cancelText: mauiCancel

    // Destructive entry first, in the palette's error colour (MAUI's red destruction button).
    BackgroundItem {
        id: destructive
        property string label: root.mauiDestruction
        visible: root.mauiDestruction.length > 0
        width: parent.width
        height: Theme.itemSizeSmall
        onClicked: root.__select(-1, root.mauiDestruction)
        Label {
            x: Theme.horizontalPageMargin
            width: parent.width - 2 * x
            anchors.verticalCenter: parent.verticalCenter
            // The label sits in the BackgroundItem's contentItem: address the row by id, not parent.
            text: destructive.label
            horizontalAlignment: Text.AlignHCenter
            truncationMode: TruncationMode.Fade
            color: destructive.highlighted ? Theme.highlightColor : palette.errorColor
        }
        Component.onCompleted: root.__items.push(this)
    }
    Repeater {
        model: root.mauiActions
        BackgroundItem {
            id: entry
            property string label: modelData
            width: parent.width
            height: Theme.itemSizeSmall
            onClicked: root.__select(index, modelData)
            Label {
                x: Theme.horizontalPageMargin
                width: parent.width - 2 * x
                anchors.verticalCenter: parent.verticalCenter
                text: entry.label
                horizontalAlignment: Text.AlignHCenter
                truncationMode: TruncationMode.Fade
                color: entry.highlighted ? Theme.highlightColor : Theme.primaryColor
            }
            Component.onCompleted: root.__items.push(this)
        }
    }
    // Room under the last entry when no cancel button follows.
    Item {
        width: parent.width
        height: root.mauiCancel.length > 0 ? 0 : Theme.paddingLarge
    }

    onRejected: Adapter.emit(root, "action-cancelled")
}
