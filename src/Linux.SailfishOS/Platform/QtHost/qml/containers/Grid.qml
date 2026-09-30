import QtQuick 2.6

// Adapter: MAUI Grid -> the plain ContentView container; the managed layout engine computes the grid (a QtQuick
// positioner would re-lay out the children). Contract: mauiId / mauiProbe / mauiEvent — see controls/Label.qml.
ContentView {
    property int mauiColumns: 2   // diagnostics mirror
}
