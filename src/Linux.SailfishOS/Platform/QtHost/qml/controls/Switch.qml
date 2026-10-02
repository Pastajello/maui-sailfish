import QtQuick 2.6
import Sailfish.Silica 1.0
import "../lib/silica.js" as SilicaWalk

// Adapter: MAUI Switch -> Silica Switch (CheckBox has its own adapter). Events: toggled {id,checked}.
// Contract: mauiId / mauiProbe / mauiEvent — see controls/Label.qml.
Switch {
    id: root

    property string mauiId: ""
    property string mauiProbe: ""
    signal mauiEvent(string name, string payload)

    // Adapter events are suppressed while managed pushes are applied (no echo loop).
    property bool mauiApplying: false

    // Silica's switch has no track: its indicator light takes TrackColor while
    // checked, ThumbColor otherwise. Transparent keeps the palette.
    property color mauiThumbColor: "transparent"
    property color mauiTrackColor: "transparent"

    // Silica keeps its light/track items as unnamed GlassItems; collect them in tree order.
    property var __glass: []
    Component.onCompleted: __glass = SilicaWalk.collectGlass(root)

    Binding {
        target: root.__glass.length > 0 ? root.__glass[0] : null; property: "color"
        value: root.checked && root.mauiTrackColor.a > 0 ? root.mauiTrackColor : root.mauiThumbColor
        when: root.__glass.length > 0 && !root.highlighted &&
              ((root.checked && root.mauiTrackColor.a > 0) || root.mauiThumbColor.a > 0)
    }
    property int mauiSuppressedCount: 0

    // Silica's Switch binds width/height to its content, which would overwrite the
    // managed geometry; plain values drop those bindings so the shim's writes stick.
    width: 0
    height: 0

    onCheckedChanged: {
        if (mauiApplying) { mauiSuppressedCount++; return; }
        mauiEvent("toggled",
                  JSON.stringify({ id: mauiId, checked: checked }))
    }
}
