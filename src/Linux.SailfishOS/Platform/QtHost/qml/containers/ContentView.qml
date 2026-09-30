import QtQuick 2.6
import Sailfish.Silica 1.0

// Adapter: generic container for ContentView, content hosts, custom layouts and
// controls without an adapter. Children are positioned by the managed geometry pass,
// so it paints only the background; no MouseArea, so input falls through.
// Contract: mauiId / mauiProbe / mauiEvent — see controls/Label.qml.
Item {
    id: root

    property string mauiId: ""
    property string mauiProbe: ""
    signal mauiEvent(string name, string payload)

    // Adapter events are suppressed while managed pushes are applied (no echo loop).
    property bool mauiApplying: false

    property color mauiBackground: "transparent"

    Rectangle {
        anchors.fill: parent
        color: root.mauiBackground
        visible: color.a > 0
    }
}
