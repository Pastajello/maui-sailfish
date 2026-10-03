import QtQuick 2.6
import Sailfish.Silica 1.0
import "../lib/adapter.js" as Adapter

// Adapter: MAUI DisplayAlert -> a system-dialog panel over the page (DialogPanel.qml), opened by the renderer.
// Contract: mauiId / mauiProbe / mauiEvent — see controls/Label.qml.
// Events: alert-accepted / alert-rejected (payload = mauiId); a tap on the dimmed page rejects.
// A single-button alert (MAUI passes its one text as cancel) shows that button as the acknowledgement: it accepts.
DialogPanel {
    id: root

    property string mauiAccept: "OK"
    property string mauiCancel: "Cancel"

    acceptText: mauiAccept.length > 0 ? mauiAccept : mauiCancel
    cancelText: mauiAccept.length > 0 ? mauiCancel : ""

    onAccepted: Adapter.emit(root, "alert-accepted")
    onRejected: Adapter.emit(root, "alert-rejected")
}
