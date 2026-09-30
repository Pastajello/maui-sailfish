import QtQuick 2.6
import Sailfish.Silica 1.0
import "../lib/silica.js" as SilicaWalk

// Adapter: MAUI Slider -> Silica Slider. Events: value-changed {id,value}.
// Contract: mauiId / mauiProbe / mauiEvent — see controls/Label.qml.
Slider {
    id: root

    property string mauiId: ""
    property string mauiProbe: ""
    signal mauiEvent(string name, string payload)

    // Adapter events are suppressed while managed pushes are applied (no echo loop).
    property bool mauiApplying: false

    // `color` paints the filled track + handle, `backgroundColor` the empty track;
    // the handle is the third GlassItem.
    property color mauiMinTrackColor: "transparent"
    property color mauiMaxTrackColor: "transparent"
    property color mauiThumbColor: "transparent"

    // Silica keeps its light/track items as unnamed GlassItems; collect them in tree order.
    property var __glass: []
    Component.onCompleted: __glass = SilicaWalk.collectGlass(root)

    Binding { target: root; property: "color"; value: root.mauiMinTrackColor; when: root.mauiMinTrackColor.a > 0 }
    Binding { target: root; property: "backgroundColor"; value: root.mauiMaxTrackColor; when: root.mauiMaxTrackColor.a > 0 }
    Binding {
        target: root.__glass.length > 2 ? root.__glass[2] : null; property: "color"
        value: root.mauiThumbColor
        when: root.__glass.length > 2 && root.mauiThumbColor.a > 0 && !root.highlighted
    }
    property int mauiSuppressedCount: 0

    // ThumbImageSource replaces the Silica handle, drawn at the image's size in dp (mauiThumbScale = density).
    property string mauiThumbImage: ""
    property real mauiThumbScale: 1
    handleVisible: mauiThumbImage.length === 0
    Image {
        id: thumbImage
        objectName: "mauiThumbImage"
        source: root.mauiThumbImage
        visible: root.mauiThumbImage.length > 0
        width: implicitWidth * root.mauiThumbScale
        height: implicitHeight * root.mauiThumbScale
        x: root._highlightX + root._highlightItem.width / 2 - width / 2
        y: root._backgroundItem.y + root._backgroundItem.height / 2 - height / 2
        smooth: true
        // Undo the RTL flip below: the image itself is not mirrored.
        transform: Scale { origin.x: thumbImage.width / 2; xScale: root.LayoutMirroring.enabled ? -1 : 1 }
    }

    // FlowDirection RTL (LayoutMirroring, set by the shim): the value grows from the right, as on Android.
    // Silica positions the groove and handle by x, which LayoutMirroring leaves alone.
    transform: Scale { origin.x: root.width / 2; xScale: root.LayoutMirroring.enabled ? -1 : 1 }

    // x/y/width/height come from the managed geometry pass.
    // No valueText: Silica adds top padding for the value label, pushing the groove
    // out of the managed 44dp slot.

    onValueChanged: {
        if (mauiApplying) { mauiSuppressedCount++; return; }
        mauiEvent("value-changed",
                  JSON.stringify({ id: mauiId, value: value }))
    }
}
