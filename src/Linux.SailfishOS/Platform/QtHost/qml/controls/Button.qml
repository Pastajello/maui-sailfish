import QtQuick 2.6
import Sailfish.Silica 1.0
import "../lib/silica.js" as SilicaWalk

// Adapter: MAUI Button -> Silica Button. Events: tap (payload = mauiId).
// Contract: mauiId / mauiProbe / mauiEvent — see controls/Label.qml.
// Styling applies only what the app set (px; <0 or transparent = Silica default).
// The label and plate are Silica internals ("label", contentRow's parent), found once. The style Bindings and
// the first-frame probe are created on demand (~20% cheaper per button; adapterbench keeps it pixel-identical
// to Linux.SailfishOS.Diagnostics/qml/diag/reference/ButtonEager.qml).
Button {
    id: root

    property string mauiId: ""
    property string mauiProbe: ""
    signal mauiEvent(string name, string payload)

    // Adapter events are suppressed while managed pushes are applied (no echo loop).
    property bool mauiApplying: false

    // TextColor and the plate (Background/BackgroundColor) as the app set them; unset gives the Silica binding back
    // (Silica Button: color = palette.primaryColor, backgroundColor = a faint tint of color), as clearing a colour
    // returns the platform's own on Android and iOS.
    property color mauiTextColor: "transparent"
    property bool mauiTextColorSet: false
    property color mauiPlateColor: "transparent"
    property bool mauiPlateSet: false
    onMauiTextColorChanged: __applyColors()
    onMauiTextColorSetChanged: __applyColors()
    onMauiPlateColorChanged: __applyColors()
    onMauiPlateSetChanged: __applyColors()
    function __applyColors() {
        color = mauiTextColorSet ? mauiTextColor : Qt.binding(function() { return palette.primaryColor; });
        backgroundColor = mauiPlateSet ? mauiPlateColor
                                       : Qt.binding(function() { return Theme.rgba(root.color, Theme.opacityFaint); });
    }

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
    // Unset FontSize: Silica's size, shrunk to fit a narrower button down to this (px, MAUI's 14 dp); 0 = fixed.
    // Set imperatively on the Silica label, which binds neither property (no Binding for the common case).
    property real mauiFitPixelSize: 0
    onMauiFitPixelSizeChanged: __applyFit()
    function __applyFit() {
        if (!__label)
            return;
        __label.fontSizeMode = mauiFitPixelSize > 0 ? Text.HorizontalFit : Text.FixedSize;
        __label.minimumPixelSize = mauiFitPixelSize > 0 ? mauiFitPixelSize : 12;
    }

    property Item __label: null
    property Item __plate: null
    property Item __row: null

    // Button.ContentLayout and the plate fill (QtHostPageRenderer.ButtonProps). Silica lays the icon and label out in a
    // Row on a plate kept itemSizeExtraSmall tall: "top"/"bottom" lift the icon out of the Row onto the plate, "right"
    // mirrors the Row, and mauiFillPlate lets the plate (MAUI's background) cover a frame the app made taller.
    property string mauiIconPosition: "left"
    property real mauiIconSpacing: 0
    property bool mauiFillPlate: false
    property bool __iconLifted: false
    onMauiIconPositionChanged: __applyLayout()
    onMauiIconSpacingChanged: __applyLayout()
    onMauiFillPlateChanged: __applyLayout()
    onMauiIconSourceChanged: __applyLayout()
    function __applyLayout() {
        if (!__plate || !__row)
            return;
        if (mauiFillPlate) {
            __plate.anchors.topMargin = 0;
            __plate.anchors.bottomMargin = 0;
        }
        __row.layoutDirection = mauiIconPosition === "right" ? Qt.RightToLeft : Qt.LeftToRight;
        var icon = root.icon;
        var lift = (mauiIconPosition === "top" || mauiIconPosition === "bottom") && mauiIconSource.length > 0;
        if (lift && !__iconLifted) {
            icon.anchors.verticalCenter = undefined;
            icon.parent = __plate;
            icon.anchors.horizontalCenter = __plate.horizontalCenter;
            __iconLifted = true;
        } else if (!lift && __iconLifted) {
            icon.anchors.horizontalCenter = undefined;
            icon.parent = __row;
            if (__label) {   // back in front of the label, Row order
                __label.parent = null;
                __label.parent = __row;
            }
            icon.anchors.verticalCenter = __row.verticalCenter;
            __iconLifted = false;
        }
        if (__iconLifted) {
            var top = mauiIconPosition === "top";
            var gap = __label && __label.text.length > 0 ? mauiIconSpacing : 0;
            icon.y = Qt.binding(function() {
                var start = (__plate.height - (icon.height + gap + __row.height)) / 2;
                return top ? start : start + __row.height + gap;
            });
            __row.anchors.verticalCenterOffset = Qt.binding(function() { return (top ? 1 : -1) * (icon.height + gap) / 2; });
        } else {
            __row.anchors.verticalCenterOffset = 0;
        }
    }

    Component.onCompleted: {
        __label = SilicaWalk.findByObjectName(root, "label");
        __applyFit();
        var row = SilicaWalk.findByObjectName(root, "contentRow");
        __row = row;
        __plate = row ? row.parent : null;
        if (__styled)
            __style = styleComponent.createObject(root);
        __applyLayout();
    }

    // The style Bindings exist only once a style value is set: an unstyled button (most list cells) creates none.
    // They keep Binding semantics, so a value going back to "unset" restores Silica's own binding.
    property QtObject __style: null
    readonly property bool __styled: mauiPixelSize > 0 || mauiFamily.length > 0 || mauiBold || mauiItalic ||
                                     mauiCornerRadius >= 0 || mauiStrokeColor.a > 0 || mauiIconSource.length > 0
    on__StyledChanged: if (__styled && !__style && __label !== null) __style = styleComponent.createObject(root)
    Component {
        id: styleComponent
        QtObject {
            property list<QtObject> bindings: [
                Binding { target: root.__label; property: "font.pixelSize"; value: root.mauiPixelSize; when: root.__label !== null && root.mauiPixelSize > 0 },
                Binding { target: root.__label; property: "font.family"; value: root.mauiFamily; when: root.__label !== null && root.mauiFamily.length > 0 },
                Binding { target: root.__label; property: "font.bold"; value: true; when: root.__label !== null && root.mauiBold },
                Binding { target: root.__label; property: "font.italic"; value: true; when: root.__label !== null && root.mauiItalic },
                Binding { target: root.__plate; property: "radius"; value: root.mauiCornerRadius; when: root.__plate !== null && root.mauiCornerRadius >= 0 },
                Binding { target: root.border; property: "color"; value: root.mauiStrokeColor; when: root.mauiStrokeColor.a > 0 },
                Binding { target: root.__plate; property: "border.width"; value: root.mauiStrokeWidth; when: root.__plate !== null && root.mauiStrokeColor.a > 0 && root.mauiStrokeWidth >= 0 },
                Binding { target: root.icon; property: "source"; value: root.mauiIconSource; when: root.mauiIconSource.length > 0 }
            ]
        }
    }

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
    Component {
        id: firstFrameProbe
        Timer {
            interval: 0
            running: true
            onTriggered: { if (root.__label) root.mauiFirstVisiblePx = root.__label.font.pixelSize; destroy(); }
        }
    }
    onVisibleChanged: if (visible && mauiFirstVisiblePx < 0) firstFrameProbe.createObject(root)

    // x/y/width/height come from the managed geometry pass.
    onClicked: mauiEvent("tap", mauiId)
}
