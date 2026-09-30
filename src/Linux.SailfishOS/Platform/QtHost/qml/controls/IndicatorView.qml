import QtQuick 2.6
import Sailfish.Silica 1.0

// Adapter: MAUI IndicatorView (no IndicatorTemplate) -> row of page dots. Contract: see
// controls/Label.qml. Transparent colors = Silica palette; mauiDotSize in Qt units.
// "indicator-tapped" {id, index}: managed sets Position, which moves the linked carousel.
Item {
    id: root

    property string mauiId: ""
    property string mauiProbe: "dots=" + dots.count + " at " + mauiPosition
    signal mauiEvent(string name, string payload)

    // Managed property pushes set this flag; events fired meanwhile are suppressed (no echo).
    property bool mauiApplying: false

    property int mauiCount: 0
    property int mauiPosition: 0
    property color mauiDotColor: "transparent"
    property color mauiSelectedColor: "transparent"
    property real mauiDotSize: Theme.paddingMedium
    property bool mauiSquare: false
    property int mauiMaxVisible: 2147483647
    property bool mauiHideSingle: true

    readonly property int __visibleCount: Math.max(0, Math.min(mauiCount, mauiMaxVisible))
    visible: !(mauiHideSingle && mauiCount <= 1)

    Row {
        anchors.centerIn: parent
        spacing: root.mauiDotSize

        Repeater {
            id: dots
            model: root.__visibleCount

            Rectangle {
                readonly property bool current: index === root.mauiPosition
                width: root.mauiDotSize
                height: root.mauiDotSize
                radius: root.mauiSquare ? 0 : width / 2
                color: current
                       ? (root.mauiSelectedColor.a > 0 ? root.mauiSelectedColor : Theme.highlightColor)
                       : (root.mauiDotColor.a > 0 ? root.mauiDotColor : Theme.rgba(Theme.secondaryColor, Theme.opacityHigh))

                // Tap target spans the dot plus half the gap on each side, full strip height.
                MouseArea {
                    x: -root.mauiDotSize / 2
                    y: -(root.height - parent.height) / 2
                    width: parent.width + root.mauiDotSize
                    height: root.height
                    onClicked: if (!root.mauiApplying)
                        root.mauiEvent("indicator-tapped", JSON.stringify({ id: root.mauiId, index: index }))
                }
            }
        }
    }
}
