import QtQuick 2.6
import Sailfish.Silica 1.0

// Diagnostics adapter for a library control (docs/custom-controls.md, f3 part O): registered at runtime with
// QtHostAdapters.Register, loaded from the app's qml/ directory. Events: rating-changed {id,value}.
Row {
    id: root

    property string mauiId: ""
    property string mauiProbe: ""
    signal mauiEvent(string name, string payload)
    property bool mauiApplying: false

    // Witness that this file, not the fallback label, was instantiated.
    property string diagAdapter: "rating"
    property int value: 0
    property int maximum: 5

    spacing: Theme.paddingSmall

    // What a tap on a star would do; the leg calls it to raise the event deterministically.
    function diagRate(v) {
        if (!mauiApplying)
            mauiEvent("rating-changed", JSON.stringify({ id: mauiId, value: v }))
    }

    Repeater {
        model: root.maximum
        Rectangle {
            width: Theme.itemSizeExtraSmall
            height: width
            radius: width / 2
            color: index < root.value ? Theme.highlightColor : "transparent"
            border.color: Theme.highlightColor
            border.width: 2
        }
    }
}
