import QtQuick 2.6
import Sailfish.Silica 1.0

// Adapter: MAUI ContentPage -> Silica Page. Contract: see controls/Label.qml.
// Not used yet: the renderer hosts pages in MauiModelPage; registered so the URI contract is complete.
Page {
    id: root

    property string mauiId: ""
    property string mauiProbe: ""
    signal mauiEvent(string name, string payload)

    // Managed property pushes set this flag; events fired meanwhile are suppressed (no echo).
    property bool mauiApplying: false

    property string pageTitle: ""

    PageHeader { title: root.pageTitle }
}
