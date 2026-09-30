// Adapter: MAUI Grid -> plain Item; the managed layout engine computes the grid.
// A QtQuick positioner would re-lay out the children. No MouseArea, so input falls through.
// Contract: mauiId / mauiProbe / mauiEvent — see controls/Label.qml.
import QtQuick 2.6
import Sailfish.Silica 1.0

Item {
    id: root
    property string mauiId: ""
    property string mauiProbe: ""
    signal mauiEvent(string name, string payload)
    property bool mauiApplying: false

    property int mauiColumns: 2   // diagnostics mirror
    property color mauiBackground: "transparent"

    Rectangle {
        anchors.fill: parent
        color: root.mauiBackground
        visible: color.a > 0
    }
}
