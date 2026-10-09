import QtQuick 2.6
import Sailfish.Silica 1.0
import "../lib/adapter.js" as Adapter

// Adapter: MAUI DisplayPromptAsync -> a system-dialog panel (DialogPanel.qml) with a Silica TextField under the
// message (QtHostPageRenderer.PushPromptAsync). The field takes focus when the panel opens (Maliit follows); the
// keyboard's Enter key accepts. The panel sits at the top, so the keyboard never covers it.
// Events: "prompt-accepted" {id, text} / "prompt-rejected" (payload = mauiId).
DialogPanel {
    id: root

    // Diag: "<activeFocus>|<text>", proving native field focus and injected key taps.
    mauiProbe: field.activeFocus + "|" + field.text

    property string mauiPlaceholder: ""
    property string mauiAccept: "OK"
    property string mauiCancel: "Cancel"
    property string mauiInitial: ""
    property int mauiMaxLength: -1
    property bool mauiNumeric: false
    // Keyboard.Email / Keyboard.Url: Qt input method hints (Maliit's address layouts).
    property int mauiHints: 0

    acceptText: mauiAccept
    cancelText: mauiCancel

    TextField {
        id: field
        width: parent.width
        placeholderText: root.mauiPlaceholder
        // The label repeats the placeholder; in landscape the line it takes is the title's above the keyboard.
        label: root.__tight ? "" : root.mauiPlaceholder
        EnterKey.iconSource: "image://theme/icon-m-enter-accept"
        EnterKey.onClicked: root.accept()
        Component.onCompleted: {
            text = root.mauiInitial
            if (root.mauiMaxLength >= 0)
                maximumLength = root.mauiMaxLength
            if (root.mauiNumeric)
                inputMethodHints = Qt.ImhDigitsOnly
            else if (root.mauiHints !== 0)
                inputMethodHints = root.mauiHints
            forceActiveFocus()
        }
    }

    onAccepted: {
        field.focus = false
        mauiEvent("prompt-accepted", JSON.stringify({ id: mauiId, text: field.text }))
    }
    onRejected: {
        field.focus = false
        Adapter.emit(root, "prompt-rejected")
    }
}
