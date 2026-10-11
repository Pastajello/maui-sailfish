import QtQuick 2.6
import Sailfish.Silica 1.0

// Adapter: Trailhead.Controls.CompassView (CompassViewHandler). Contract: mauiId / mauiProbe / mauiEvent / mauiApplying
// (docs/custom-controls.md). Snapshot: heading (degrees the phone points to), targetBearing (-1 = none), calibrated.
// Event: compass-tapped. The dial is drawn in the ambience's colours; the rose turns the shortest way.
Item {
    id: root

    property string mauiId: ""
    property string mauiProbe: "heading=" + heading.toFixed(1) + " rose=" + rose.rotation.toFixed(1) + " target=" + targetBearing.toFixed(0)
    signal mauiEvent(string name, string payload)
    property bool mauiApplying: false

    property real heading: 0
    property real targetBearing: -1
    property bool calibrated: true

    // Shortest-way rotation: the rose's angle follows -heading, unwrapped so it never spins the long way round.
    property real __roseAngle: 0
    onHeadingChanged: {
        var target = -heading;
        var delta = ((target - __roseAngle) % 360 + 540) % 360 - 180;
        __roseAngle += delta;
    }

    Rectangle {
        anchors.fill: parent
        radius: width / 2
        color: Theme.rgba(Theme.overlayBackgroundColor, 0.55)
        border.color: Theme.rgba(Theme.primaryColor, 0.4)
        border.width: Math.max(1, Theme.paddingSmall / 4)
    }

    Item {
        id: rose
        anchors.fill: parent
        rotation: root.__roseAngle
        Behavior on rotation { NumberAnimation { duration: 300; easing.type: Easing.OutCubic } }

        Repeater {
            model: [ "N", "E", "S", "W" ]
            Label {
                text: modelData
                font.pixelSize: root.width * 0.16
                font.bold: index === 0
                color: index === 0 ? Theme.highlightColor : Theme.secondaryColor
                x: root.width / 2 - width / 2 + Math.sin(index * Math.PI / 2) * root.width * 0.34
                y: root.height / 2 - height / 2 - Math.cos(index * Math.PI / 2) * root.height * 0.34
                rotation: -rose.rotation   // letters stay upright
            }
        }

        // The way back to the start, relative to north on the rose.
        Rectangle {
            visible: root.targetBearing >= 0
            width: root.width * 0.06
            height: root.height * 0.36
            radius: width / 2
            color: "#E4572E"
            x: root.width / 2 - width / 2
            y: root.height / 2 - height
            transformOrigin: Item.Bottom
            rotation: root.targetBearing
        }
    }

    // The phone's own direction: a fixed needle up.
    Rectangle {
        width: root.width * 0.04
        height: root.height * 0.22
        radius: width / 2
        color: Theme.highlightColor
        anchors.horizontalCenter: parent.horizontalCenter
        y: root.height * 0.08
    }

    Label {
        anchors.centerIn: parent
        text: root.calibrated ? Math.round(((root.heading % 360) + 360) % 360) + "°" : "calibrate"
        font.pixelSize: root.width * (root.calibrated ? 0.17 : 0.11)
        color: root.calibrated ? Theme.primaryColor : Theme.errorColor
    }

    MouseArea {
        anchors.fill: parent
        onClicked: root.mauiEvent("compass-tapped", JSON.stringify({ id: root.mauiId }))
    }
}
