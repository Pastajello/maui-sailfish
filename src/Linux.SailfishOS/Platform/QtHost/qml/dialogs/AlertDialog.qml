import QtQuick 2.6
import Sailfish.Silica 1.0

// Adapter: MAUI DisplayAlert -> Silica Dialog, pushed on the pageStack by the renderer.
// Contract: mauiId / mauiProbe / mauiEvent — see controls/Label.qml.
// Events: alert-accepted / alert-rejected (payload = mauiId).
Dialog {
    id: root

    property string mauiId: ""
    property string mauiProbe: ""
    signal mauiEvent(string name, string payload)

    // Adapter events are suppressed while managed pushes are applied (no echo loop).
    property bool mauiApplying: false

    property string mauiTitle: ""
    property string mauiMessage: ""
    property string mauiAccept: "OK"
    property string mauiCancel: "Cancel"

    // A dialog page has no managed background; without this the dark-ambience
    // (white) text would be white-on-white.
    Rectangle {
        anchors.fill: parent
        z: -1
        color: "#000000"
    }

    DialogHeader {
        title: root.mauiTitle
        acceptText: root.mauiAccept
        cancelText: root.mauiCancel
    }
    Label {
        anchors { left: parent.left; right: parent.right; verticalCenter: parent.verticalCenter
                  margins: Theme.paddingLarge }
        text: root.mauiMessage
        wrapMode: Text.Wrap
        color: Theme.primaryColor
    }

    onAccepted: mauiEvent("alert-accepted", mauiId)
    onRejected: mauiEvent("alert-rejected", mauiId)
}
