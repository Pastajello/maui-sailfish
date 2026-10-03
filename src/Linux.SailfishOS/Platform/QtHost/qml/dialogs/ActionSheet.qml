import QtQuick 2.6
import Sailfish.Silica 1.0
import "../lib/adapter.js" as Adapter

// Adapter: MAUI DisplayActionSheetAsync -> Silica Dialog with a value-button list.
// Contract: mauiId / mauiProbe / mauiEvent — see controls/Label.qml.
// Events: action-selected {id,index,text} (index -1 = destructive entry),
// action-cancelled (payload = mauiId) on any dismiss.
Dialog {
    id: root

    property string mauiId: ""
    property string mauiProbe: "sheet:" + root.mauiActions.length
    signal mauiEvent(string name, string payload)

    // Adapter events are suppressed while managed pushes are applied (no echo loop).
    property bool mauiApplying: false

    property string mauiTitle: ""
    property string mauiCancel: ""
    property string mauiDestruction: ""
    property var mauiActions: []

    // Entry items, for mapping injected diagnostic taps.
    property var __items: []
    // Set before accept() so a selection is not also reported as a dismiss.
    property bool __selected: false

    // A dialog page has no managed background; without this the dark-ambience
    // (white) labels would be white-on-white.
    Rectangle {
        anchors.fill: parent
        z: -1
        color: "#000000"
    }

    DialogHeader {
        id: header
        title: root.mauiTitle
        acceptText: ""      // entries select — no accept decoration
        cancelText: root.mauiCancel.length > 0 ? root.mauiCancel : "Cancel"
    }
    SilicaFlickable {
        anchors { left: parent.left; right: parent.right; bottom: parent.bottom
                  top: header.bottom; topMargin: Theme.paddingMedium }
        contentHeight: list.height + Theme.paddingLarge * 2
        Column {
            id: list
            width: parent.width
            spacing: Theme.paddingSmall
            // Destructive entry first, in red (Jolla convention; Silica has no public
            // destructive color token).
            ValueButton {
                visible: root.mauiDestruction.length > 0
                label: root.mauiDestruction
                labelColor: "#ff4d4d"
                onClicked: {
                    root.__selected = true
                    mauiEvent("action-selected",
                              JSON.stringify({ id: root.mauiId, index: -1,
                                               text: root.mauiDestruction }))
                    root.accept()
                }
                Component.onCompleted: root.__items.push(this)
            }
            Repeater {
                model: root.mauiActions
                ValueButton {
                    label: modelData
                    onClicked: {
                        root.__selected = true
                        mauiEvent("action-selected",
                                  JSON.stringify({ id: root.mauiId, index: index,
                                                   text: modelData }))
                        root.accept()
                    }
                    Component.onCompleted: root.__items.push(this)
                }
            }
        }
    }

    // Header cancel / swipe-down / hardware back → the cancel text result.
    onRejected: Adapter.emit(root, "action-cancelled")
    // A bare swipe-up accept is also a dismiss, so the managed task never hangs.
    onAccepted: if (!__selected) Adapter.emit(root, "action-cancelled")
}
