import QtQuick 2.6
import Sailfish.Silica 1.0

// Adapter: MAUI ActivityIndicator -> Silica BusyIndicator. Contract: see controls/Label.qml.
// running maps IsRunning; no events.
BusyIndicator {
    id: root

    property string mauiId: ""
    property string mauiProbe: ""
    signal mauiEvent(string name, string payload)

    // Managed property pushes set this flag; events fired meanwhile are suppressed (no echo).
    property bool mauiApplying: false

    // Centers itself inside the managed geometry bounds.
    size: BusyIndicatorSize.Medium

    // Silica's spinner only runs while Qt.application.active, which the compositor does not grant
    // in some launch modes (static dot). So Silica's image is hidden and the same theme graphic is
    // spun with a GUI-thread RotationAnimation, observable via mauiSpinRotation.
    _forceAnimation: false

    // MAUI Color tints the spinner (transparent = Silica highlight).
    property color mauiColor: "transparent"
    readonly property alias mauiSpinRotation: spin.rotation
    readonly property alias mauiSpinColor: spin.color

    HighlightImage {
        id: spin
        objectName: "mauiSpinner"
        anchors.centerIn: parent
        source: "image://theme/graphic-busyindicator-medium"
        color: root.mauiColor.a > 0 ? root.mauiColor : Theme.highlightColor
        smooth: true
        transformOrigin: Item.Center
        RotationAnimation on rotation {
            from: 0; to: 360; duration: 2000
            loops: Animation.Infinite
            running: root.running && root.visible
        }
    }

    Component.onCompleted: {
        for (var i = 0; i < children.length; ++i)
            if (children[i] !== spin)
                children[i].visible = false;
    }
}
