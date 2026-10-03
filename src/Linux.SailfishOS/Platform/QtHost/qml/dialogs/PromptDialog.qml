import QtQuick 2.6
import Sailfish.Silica 1.0
import "../lib/adapter.js" as Adapter

// Adapter: MAUI DisplayPromptAsync -> Silica Dialog + TextField (QtHostPageRenderer.PushPromptAsync).
// Events: "prompt-accepted" {id, text} / "prompt-rejected" (payload = mauiId).
Dialog {
    id: root

    property string mauiId: ""
    // Diag: "<activeFocus>|<text>", proving native field focus and injected key taps.
    property string mauiProbe: field.activeFocus + "|" + field.text
    signal mauiEvent(string name, string payload)

    // Managed property pushes set this flag; events fired meanwhile are suppressed (no echo).
    property bool mauiApplying: false

    property string mauiTitle: ""
    property string mauiMessage: ""
    property string mauiPlaceholder: ""
    property string mauiAccept: "OK"
    property string mauiCancel: "Cancel"
    property string mauiInitial: ""
    property int mauiMaxLength: -1
    property bool mauiNumeric: false

    // A pushed dialog page has no managed background and the palette is dark-ambience (white text),
    // so paint black to avoid white-on-white over the pageStack's light default.
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
    Column {
        anchors { left: parent.left; right: parent.right; verticalCenter: parent.verticalCenter
                  margins: Theme.paddingLarge }
        spacing: Theme.paddingSmall
        Label {
            width: parent.width
            text: root.mauiMessage
            wrapMode: Text.Wrap
            color: Theme.primaryColor
            visible: text.length > 0
        }
        TextField {
            id: field
            width: parent.width
            placeholderText: root.mauiPlaceholder
            label: root.mauiPlaceholder
            Component.onCompleted: {
                text = root.mauiInitial
                if (root.mauiMaxLength >= 0)
                    maximumLength = root.mauiMaxLength
                if (root.mauiNumeric)
                    inputMethodHints = Qt.ImhDigitsOnly
            }
        }
    }

    // Focus once active; native focus opens the Maliit keyboard.
    onStatusChanged: if (status === PageStatus.Active) field.forceActiveFocus()

    onAccepted: mauiEvent("prompt-accepted",
                          JSON.stringify({ id: mauiId, text: field.text }))
    onRejected: Adapter.emit(root, "prompt-rejected")
}
