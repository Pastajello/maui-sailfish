import QtQuick 2.6
import Sailfish.Silica 1.0

// Adapter: a library control that draws itself through IDrawable (Syncfusion Toolkit's SfView: a text input's
// outline and hint, a shimmer) and lays out MAUI children. The drawing is the recorded IDrawable stream, replayed by
// the GraphicsView adapter below the children; the Canvas is created only while there is something to paint, since
// each painted Canvas holds a GL context and a page can carry dozens of these containers.
// Contract: mauiId / mauiProbe / mauiEvent — see controls/Label.qml.
Item {
    id: root

    property string mauiId: ""
    property string mauiProbe: ""
    signal mauiEvent(string name, string payload)

    // Adapter events are suppressed while managed pushes are applied (no echo loop).
    property bool mauiApplying: false

    property var mauiCommands: []
    property real mauiScale: 1
    property color mauiBackground: "transparent"
    property string mauiFontFamily: ""

    Loader {
        anchors.fill: parent
        active: (root.mauiCommands && root.mauiCommands.length > 0) || root.mauiBackground.a > 0
        source: "../shapes/GraphicsView.qml"
        onLoaded: {
            item.mauiScale = Qt.binding(function() { return root.mauiScale; });
            item.mauiBackground = Qt.binding(function() { return root.mauiBackground; });
            item.mauiFontFamily = Qt.binding(function() { return root.mauiFontFamily; });
            item.mauiCommands = Qt.binding(function() { return root.mauiCommands; });
        }
    }
}
