import QtQuick 2.6
import Sailfish.Silica 1.0

// Adapter: MAUI Stepper -> minus/plus Silica IconButtons (Silica has no stepper).
// Contract: mauiId / mauiProbe / mauiEvent — see controls/Label.qml.
// Events: value-changed {id,value} for user steps, clamped to the range.
Row {
    id: root

    property string mauiId: ""
    property string mauiProbe: "value=" + value
    signal mauiEvent(string name, string payload)

    // Adapter events are suppressed while managed pushes are applied (no echo loop).
    property bool mauiApplying: false
    property int mauiSuppressedCount: 0

    property real value: 0
    property real mauiMinimum: 0
    property real mauiMaximum: 100
    property real mauiIncrement: 1

    spacing: Theme.paddingSmall

    function __step(direction) {
        // Round to the increment's precision so 0.1 + 0.2 does not become 0.30000000000000004.
        var digits = Math.max(0, Math.min(10, -Math.floor(Math.log(Math.abs(mauiIncrement) || 1) / Math.LN10) + 1));
        var next = Number((value + direction * mauiIncrement).toFixed(digits));
        next = Math.max(mauiMinimum, Math.min(mauiMaximum, next));
        if (next === value)
            return;
        value = next;
        if (mauiApplying) { mauiSuppressedCount++; return; }
        mauiEvent("value-changed", JSON.stringify({ id: mauiId, value: next }));
    }

    IconButton {
        objectName: "minus"
        width: (root.width - root.spacing) / 2
        height: root.height
        icon.source: "image://theme/icon-m-remove"
        enabled: root.enabled && root.value > root.mauiMinimum
        onClicked: root.__step(-1)
    }

    IconButton {
        objectName: "plus"
        width: (root.width - root.spacing) / 2
        height: root.height
        icon.source: "image://theme/icon-m-add"
        enabled: root.enabled && root.value < root.mauiMaximum
        onClicked: root.__step(1)
    }
}
