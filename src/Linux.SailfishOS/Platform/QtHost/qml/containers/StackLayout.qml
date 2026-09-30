// Adapter: MAUI StackLayout -> plain container. Contract: see controls/Label.qml.
// Stacking is computed by the managed layout engine; children are positioned parent-relative
// by the geometry batch, so this must be a plain Item (a Column/Row would re-lay them out).
// mauiSpacing/mauiOrientation are diagnostic mirrors. No MouseArea: input falls through.
import QtQuick 2.6
import Sailfish.Silica 1.0

Item {
    id: root
    property string mauiId: ""
    property string mauiProbe: ""
    signal mauiEvent(string name, string payload)
    property bool mauiApplying: false

    property real mauiSpacing: Theme.paddingSmall
    property string mauiOrientation: "vertical"
    property color mauiBackground: "transparent"

    Rectangle {
        anchors.fill: parent
        color: root.mauiBackground
        visible: color.a > 0
    }
}
