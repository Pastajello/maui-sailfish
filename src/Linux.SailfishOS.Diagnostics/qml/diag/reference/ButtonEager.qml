import QtQuick 2.6
import Sailfish.Silica 1.0
import "../../lib/silica.js" as SilicaWalk

// REFERENCE ONLY (adapterbench): the Button adapter before P4, with its eight style Bindings created per
// instance. controls/Button.qml must keep painting pixel-identical to it; diagnostics sample only.
// Adapter: MAUI Button -> Silica Button. Events: tap (payload = mauiId).
// Contract: mauiId / mauiProbe / mauiEvent — see controls/Label.qml.
// Styling applies only what the app set (px; <0 or transparent = Silica default).
// The label and plate are Silica internals ("label", contentRow's parent), found once.
Button {
    id: root

    property string mauiId: ""
    property string mauiProbe: ""
    signal mauiEvent(string name, string payload)

    // Adapter events are suppressed while managed pushes are applied (no echo loop).
    property bool mauiApplying: false

    property real mauiPixelSize: 0
    property string mauiFamily: ""
    property bool mauiBold: false
    property bool mauiItalic: false
    property real mauiCornerRadius: -1
    property color mauiStrokeColor: "transparent"
    property real mauiStrokeWidth: -1
    property string mauiIconSource: ""
    // CharacterSpacing (device px); the shim sets it on the QFont since Qt 5.6 QML
    // has no absolute letter spacing.
    property real mauiLetterSpacing: 0
    readonly property Item mauiTextItem: __label

    property Item __label: null
    property Item __plate: null

    Component.onCompleted: {
        __label = SilicaWalk.findByObjectName(root, "label");
        var row = SilicaWalk.findByObjectName(root, "contentRow");
        __plate = row ? row.parent : null;
    }

    Binding { target: root.__label; property: "font.pixelSize"; value: root.mauiPixelSize; when: root.__label !== null && root.mauiPixelSize > 0 }
    Binding { target: root.__label; property: "font.family"; value: root.mauiFamily; when: root.__label !== null && root.mauiFamily.length > 0 }
    Binding { target: root.__label; property: "font.bold"; value: true; when: root.__label !== null && root.mauiBold }
    Binding { target: root.__label; property: "font.italic"; value: true; when: root.__label !== null && root.mauiItalic }
    Binding { target: root.__plate; property: "radius"; value: root.mauiCornerRadius; when: root.__plate !== null && root.mauiCornerRadius >= 0 }
    Binding { target: root.border; property: "color"; value: root.mauiStrokeColor; when: root.mauiStrokeColor.a > 0 }
    Binding { target: root.__plate; property: "border.width"; value: root.mauiStrokeWidth; when: root.__plate !== null && root.mauiStrokeColor.a > 0 && root.mauiStrokeWidth >= 0 }
    Binding { target: root.icon; property: "source"; value: root.mauiIconSource; when: root.mauiIconSource.length > 0 }

    // Diagnostics readback.
    function mauiDiag() {
        return JSON.stringify({
            pixel: __label ? __label.font.pixelSize : -1,
            bold: __label ? __label.font.bold : false,
            radius: __plate ? __plate.radius : -1,
            strokeWidth: __plate ? __plate.border.width : -1,
            stroke: __plate ? __plate.border.color.toString() : "",
            icon: root.icon ? root.icon.source.toString() : ""
        });
    }

    // Diagnostics: the label pixel size of the first painted frame, read one event-loop
    // turn after becoming visible (a frame only renders between managed ticks).
    property real mauiFirstVisiblePx: -1
    Timer {
        id: firstFrameProbe
        interval: 0
        onTriggered: if (root.__label) root.mauiFirstVisiblePx = root.__label.font.pixelSize
    }
    onVisibleChanged: if (visible && mauiFirstVisiblePx < 0) firstFrameProbe.start()

    // x/y/width/height come from the managed geometry pass.
    onClicked: mauiEvent("tap", mauiId)
}
