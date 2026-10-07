import QtQuick 2.6
import Sailfish.Silica 1.0
import "lib/adapter.js" as Adapter

// One tab row of the page chrome (MauiModelPage: the Shell/TabbedPage tabs, a Shell section's contents). Text tabs,
// the selected one underlined; a tab may carry a badge (MAUI 11 BadgeText: a pill with the text, a dot for "").
// Up to `fits` tabs share the width; more flick sideways, about three and a half in view so the cut one hints at the
// rest, and the selected tab is kept in view.
Flickable {
    id: row

    property Item modelPage
    property var titles: []
    property var badges: []
    property int index: 0
    property int level: 0
    property int fits: 4
    property string namePrefix: "mauiTab_"
    property int fontSize: Theme.fontSizeMedium
    property int minimumFontSize: Theme.fontSizeSmall
    property real markerOpacity: 1

    readonly property real tabWidth: titles.length > fits ? width / (fits - 0.5) : width / Math.max(1, titles.length)

    contentWidth: tabWidth * titles.length
    contentHeight: height
    flickableDirection: Flickable.HorizontalFlick
    interactive: contentWidth > width + 1
    boundsBehavior: Flickable.StopAtBounds
    clip: interactive

    function __reveal() {
        if (!interactive) {
            contentX = 0;
            return;
        }
        var x = index * tabWidth - (width - tabWidth) / 2;
        contentX = Math.max(0, Math.min(contentWidth - width, x));
    }
    // A tap tells managed to switch the MAUI tab (the row's level: 0 tabs, 1 a Shell section's contents).
    function __select(i) {
        var page = modelPage;
        Adapter.pageEmit(page, "tab-selected", level === 0 ? { index: i } : { index: i, level: level });
    }

    onIndexChanged: __reveal()
    onTitlesChanged: __reveal()
    onWidthChanged: __reveal()

    Row {
        Repeater {
            model: row.titles
            BackgroundItem {
                id: tab
                width: row.tabWidth
                height: row.height
                objectName: row.namePrefix + index
                readonly property var badge: index < row.badges.length ? row.badges[index] : null

                Label {
                    id: label
                    anchors.centerIn: parent
                    width: parent.width - 2 * Theme.paddingSmall
                    horizontalAlignment: Text.AlignHCenter
                    truncationMode: TruncationMode.Fade
                    text: modelData
                    color: index === row.index ? palette.highlightColor : palette.secondaryColor
                    font.pixelSize: row.fontSize
                    // Four tabs share the width: a long title ("Transactions") shrinks before it fades.
                    fontSizeMode: Text.HorizontalFit
                    minimumPixelSize: row.minimumFontSize
                }
                Rectangle {
                    anchors.bottom: parent.bottom
                    width: parent.width
                    height: Theme.paddingSmall / 2
                    color: palette.highlightColor
                    opacity: row.markerOpacity
                    visible: index === row.index
                }
                // The badge sits on the title's top-right corner, as the other platforms draw it on the tab icon.
                Rectangle {
                    id: badgeShape
                    objectName: row.namePrefix + "badge_" + index
                    readonly property string badgeText: tab.badge && tab.badge.text !== undefined ? String(tab.badge.text) : ""
                    readonly property bool dot: badgeText.length === 0
                    visible: !!tab.badge
                    height: dot ? Theme.paddingMedium : Math.round(badgeLabel.implicitHeight + Theme.paddingSmall / 2)
                    width: dot ? height : Math.min(tab.width - Theme.paddingSmall, Math.max(height, badgeLabel.implicitWidth + Theme.paddingSmall * 2))
                    radius: height / 2
                    x: Math.max(0, Math.min(tab.width - width - Theme.paddingSmall / 2,
                                            (tab.width + Math.min(label.paintedWidth, label.width)) / 2 - Theme.paddingSmall / 2))
                    y: Theme.paddingSmall / 2
                    color: tab.badge && tab.badge.bg ? tab.badge.bg : palette.highlightColor
                    Text {
                        id: badgeLabel
                        anchors.centerIn: parent
                        width: Math.min(implicitWidth, parent.width - Theme.paddingSmall)
                        visible: !badgeShape.dot
                        text: badgeShape.badgeText
                        elide: Text.ElideRight
                        font.pixelSize: Theme.fontSizeTiny
                        font.bold: true
                        color: tab.badge && tab.badge.fg ? tab.badge.fg : palette.highlightDimmerColor
                    }
                }
                onClicked: row.__select(index)
            }
        }
    }

    HorizontalScrollDecorator {}
}
