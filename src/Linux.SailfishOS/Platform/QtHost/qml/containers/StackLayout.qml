import QtQuick 2.6
import Sailfish.Silica 1.0

// Adapter: MAUI StackLayout -> the plain ContentView container. Stacking is computed by the managed layout engine and
// the geometry batch positions the children parent-relative, so this must not be a Column/Row (it would re-lay them
// out). Contract: mauiId / mauiProbe / mauiEvent — see controls/Label.qml.
ContentView {
    // Diagnostics mirrors.
    property real mauiSpacing: Theme.paddingSmall
    property string mauiOrientation: "vertical"
}
