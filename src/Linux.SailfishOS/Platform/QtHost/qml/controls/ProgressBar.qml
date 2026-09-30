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

    // Silica insets the groove by Screen.width/8 per side and draws it paddingMedium plus half a
    // 2*paddingLarge glass below the item top, so in a MAUI rect (12 dp, or 4 dp in Kitchen) the bar landed
    // below its box, under the next sibling. The groove spans the MAUI width and centres on the rect: the
    // inner column (the groove's parent) moves, not the root, whose rect must stay the MAUI one.
    leftMargin: 0
    rightMargin: 0
    // Silica derives implicitHeight from the column's y, which follows height here: a binding loop. MAUI measures
    // the bar itself (SailfishMeasure.Progress), so the implicit size is unused.
    implicitHeight: 0
    Binding {
        target: root.__glass.length > 0 ? root.__glass[0].parent : null; property: "y"
        value: Math.round(root.height / 2 - root.__glass[0].y - root.__glass[0].height / 2)
        when: root.__glass.length > 0
    }

    // FlowDirection RTL (LayoutMirroring, set by the shim): the bar fills from the right. The fill sits at x 0
    // of the groove, which LayoutMirroring leaves alone.
    transform: Scale { origin.x: root.width / 2; xScale: root.LayoutMirroring.enabled ? -1 : 1 }

    // x/y/width/height come from the managed geometry pass.
}
