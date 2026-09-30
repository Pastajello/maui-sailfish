import QtQuick 2.6
import Sailfish.Silica 1.0

// BENCH FLOOR ONLY (adapterbench): Silica's Button with the adapter contract and nothing else, to show how
// much of controls/Button.qml's creation cost is the adapter's own bindings, timer and tree walk.
Button {
    property string mauiId: ""
    property string mauiProbe: ""
    signal mauiEvent(string name, string payload)
    property bool mauiApplying: false
    onClicked: mauiEvent("tap", mauiId)
}
