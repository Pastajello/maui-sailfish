import QtQuick 2.6
import Sailfish.Silica 1.0

// Adapter: MAUI navigation root -> Silica ApplicationWindow. Contract: see controls/Label.qml.
// Not used yet: the boot shell is MauiShell.qml; registered so the URI contract is complete.
ApplicationWindow {
    id: root

    property string mauiId: ""
    property string mauiProbe: ""
    signal mauiEvent(string name, string payload)

    // Managed property pushes set this flag; events fired meanwhile are suppressed (no echo).
    property bool mauiApplying: false

    visible: true
    cover: null
    allowedOrientations: Orientation.Portrait
}
