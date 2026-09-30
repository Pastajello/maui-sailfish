import QtQuick 2.6
import Sailfish.Silica 1.0
import "../lib/silica.js" as SilicaWalk

// Adapter: MAUI ProgressBar -> Silica ProgressBar (value 0..1).
// Contract: mauiId / mauiProbe / mauiEvent — see controls/Label.qml.
ProgressBar {
    id: root

    property string mauiId: ""
    property string mauiProbe: ""
    signal mauiEvent(string name, string payload)

    // Adapter events are suppressed while managed pushes are applied (no echo loop).
    property bool mauiApplying: false

    // ProgressColor paints the filled part, the second GlassItem of the groove.
    property color mauiProgressColor: "transparent"

    // Silica keeps its light/track items as unnamed GlassItems; collect them in tree order.
    property var __glass: []
    Component.onCompleted: __glass = SilicaWalk.collectGlass(root)

    Binding {
        target: root.__glass.length > 1 ? root.__glass[1] : null; property: "color"
        value: root.mauiProgressColor
        when: root.__glass.length > 1 && root.mauiProgressColor.a > 0
    }

    // Silica defaults to indeterminate and then ignores `value`; MAUI's bar is always determinate.
    indeterminate: false

    // FlowDirection RTL (LayoutMirroring, set by the shim): the bar fills from the right. The fill sits at x 0
    // of the groove, which LayoutMirroring leaves alone.
    transform: Scale { origin.x: root.width / 2; xScale: root.LayoutMirroring.enabled ? -1 : 1 }

    // x/y/width/height come from the managed geometry pass.
}
