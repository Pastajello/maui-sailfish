import QtQuick 2.6
import Sailfish.Silica 1.0

// Adapter: MAUI CheckBox -> Silica-styled check box (rounded frame + acknowledge mark).
// Contract: see controls/Label.qml. mauiColor = CheckBox.Color (transparent = Silica highlight).
// Event: "toggled" {id, checked}.
MouseArea {
    id: root

    property string mauiId: ""
    property string mauiProbe: "checked=" + checked
    signal mauiEvent(string name, string payload)

    // Managed property pushes set this flag; events fired meanwhile are suppressed (no echo).
    property bool mauiApplying: false
    property int mauiSuppressedCount: 0

    property bool checked: false
    property color mauiColor: "transparent"

    readonly property color __accent: mauiColor.a > 0 ? mauiColor : Theme.highlightColor
    readonly property real __box: Math.min(width, height, Theme.iconSizeMedium)

    onClicked: checked = !checked
    onCheckedChanged: {
        if (mauiApplying) { mauiSuppressedCount++; return; }
        mauiEvent("toggled", JSON.stringify({ id: mauiId, checked: checked }));
    }

    Rectangle {
        id: box
        anchors.centerIn: parent
        width: root.__box * 0.75
        height: width
        radius: Theme.paddingSmall
        color: root.checked ? Theme.rgba(root.__accent, Theme.opacityHigh) : "transparent"
        border.width: Math.max(2, Math.round(Theme.paddingSmall / 2))
        border.color: root.pressed ? Theme.highlightColor
                     : root.checked ? root.__accent
                     : Theme.rgba(Theme.primaryColor, root.enabled ? Theme.opacityHigh : Theme.opacityLow)

        Image {
            anchors.centerIn: parent
            width: parent.width
            height: parent.height
            source: "image://theme/icon-m-acknowledge?" + Theme.primaryColor
            visible: root.checked
            sourceSize.width: width
            sourceSize.height: height
        }
    }
}
