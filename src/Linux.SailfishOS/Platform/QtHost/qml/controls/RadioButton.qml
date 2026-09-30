import QtQuick 2.6
import Sailfish.Silica 1.0

// Adapter: MAUI RadioButton -> Silica composite (BackgroundItem + drawn circle + text).
// Silica has no inline radio button, so it is composed; the root must not be named
// `RadioButton` or same-directory resolution would instantiate this file recursively.
// Contract: mauiId / mauiProbe / mauiEvent — see controls/Label.qml.
// Events: toggled {id,checked}. Group exclusivity stays in MAUI.
BackgroundItem {
    id: root

    property string mauiId: ""
    property string mauiProbe: ""
    signal mauiEvent(string name, string payload)

    // Adapter events are suppressed while managed pushes are applied (no echo loop).
    property bool mauiApplying: false
    property int mauiSuppressedCount: 0

    property string text: ""
    property bool checked: false

    // Styling applies only what the app set. mauiLetterSpacing is applied by the shim
    // on mauiTextItem; the border (px) shows only when a color is set.
    property color mauiTextColor: "transparent"
    property real mauiPixelSize: 0
    property string mauiFamily: ""
    property bool mauiBold: false
    property bool mauiItalic: false
    property real mauiLetterSpacing: 0
    property color mauiStrokeColor: "transparent"
    property real mauiStrokeWidth: -1
    property real mauiCornerRadius: -1
    readonly property Item mauiTextItem: contentText

    Rectangle {
        id: frame
        anchors.fill: parent
        color: "transparent"
        visible: root.mauiStrokeColor.a > 0
        border.color: root.mauiStrokeColor
        border.width: root.mauiStrokeWidth >= 0 ? root.mauiStrokeWidth : 1
        radius: root.mauiCornerRadius >= 0 ? root.mauiCornerRadius : 0
    }

    // Diagnostics readback.
    function mauiDiag() {
        return JSON.stringify({
            color: contentText.color.toString(), pixel: contentText.font.pixelSize,
            family: contentText.font.family, bold: contentText.font.bold,
            spacing: contentText.font.letterSpacing, lines: contentText.lineCount,
            truncated: contentText.truncated,
            frame: frame.visible, frameWidth: frame.border.width, frameRadius: frame.radius,
            frameColor: frame.border.color.toString()
        });
    }

    // x/y/width/height come from the managed geometry pass.

    Row {
        id: row
        anchors {
            left: parent.left; right: parent.right; verticalCenter: parent.verticalCenter
            leftMargin: Theme.horizontalPageMargin; rightMargin: Theme.horizontalPageMargin
        }
        spacing: Theme.paddingMedium

        // Ring + filled dot when checked; follows the press highlight.
        Item {
            id: circle
            anchors.verticalCenter: parent.verticalCenter
            width: Theme.iconSizeSmall
            height: width

            Rectangle {
                anchors.fill: parent
                radius: width / 2
                color: "transparent"
                border.width: 2
                border.color: root.down || root.checked ? Theme.highlightColor : Theme.secondaryColor
            }
            Rectangle {
                anchors.centerIn: parent
                width: parent.width * 0.5
                height: width
                radius: width / 2
                color: Theme.highlightColor
                visible: root.checked
            }
        }

        // QtQuick Text, not Label: the sibling Label.qml adapter shadows the Silica type.
        Text {
            id: contentText
            anchors.verticalCenter: parent.verticalCenter
            width: root.width - 2 * Theme.horizontalPageMargin - circle.width - row.spacing
            font.pixelSize: root.mauiPixelSize > 0 ? root.mauiPixelSize : Theme.fontSizeMedium
            font.family: root.mauiFamily.length > 0 ? root.mauiFamily : Theme.fontFamily
            font.bold: root.mauiBold
            font.italic: root.mauiItalic
            color: root.mauiTextColor.a > 0 ? root.mauiTextColor
                 : root.down ? Theme.highlightColor : Theme.primaryColor
            text: root.text
            wrapMode: Text.NoWrap
            elide: Text.ElideRight
        }
    }

    // A tap can only check, never uncheck (MAUI radio semantics).
    onClicked: {
        if (!checked)
            checked = true;
    }

    onCheckedChanged: {
        if (mauiApplying) { mauiSuppressedCount++; return; }
        mauiEvent("toggled",
                  JSON.stringify({ id: mauiId, checked: checked }))
    }
}

