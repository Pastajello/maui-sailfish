import QtQuick 2.6

// Adapter: a drawing surface (QtHostSurface). The shim adds the child item that shows the pixels managed code drew
// and takes its touch; the adapter itself only carries the contract, and the generic host paints the background
// under the pixels. Contract: mauiId / mauiProbe / mauiEvent — see controls/Label.qml.
Item {
    id: root

    property string mauiId: ""
    property string mauiProbe: ""
    signal mauiEvent(string name, string payload)

    property bool mauiApplying: false
}
