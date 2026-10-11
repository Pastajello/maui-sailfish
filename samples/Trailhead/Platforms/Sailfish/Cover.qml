import QtQuick 2.6
import Sailfish.Silica 1.0

// Trailhead's cover (<SailfishCoverQml>): a ridge line behind the app's title and up to three lines from
// SailfishCover.SetContent (trip count and distance this year, or the live recording: activity, distance, time,
// pace). The actions come from SailfishCover.SetActions.
Item {
    id: cover
    anchors.fill: parent

    property var mauiCoverData: ({ title: "Trailhead", lines: [] })

    // The ridge: a few peaks in the ambience's highlight colour, faint, along the bottom.
    Canvas {
        id: ridge
        anchors { left: parent.left; right: parent.right; bottom: parent.bottom }
        height: parent.height * 0.45
        opacity: 0.35
        onPaint: {
            var ctx = getContext("2d");
            ctx.reset();
            ctx.fillStyle = Theme.highlightColor;
            ctx.beginPath();
            ctx.moveTo(0, height);
            var peaks = [0.55, 0.2, 0.45, 0.05, 0.4, 0.25, 0.6];
            for (var i = 0; i < peaks.length; i++)
                ctx.lineTo(width * i / (peaks.length - 1), height * peaks[i]);
            ctx.lineTo(width, height);
            ctx.closePath();
            ctx.fill();
        }
        Connections {
            target: Theme
            onHighlightColorChanged: ridge.requestPaint()
        }
    }

    Column {
        anchors { left: parent.left; right: parent.right; top: parent.top; margins: Theme.paddingLarge }
        spacing: Theme.paddingSmall

        Label {
            width: parent.width
            text: cover.mauiCoverData && cover.mauiCoverData.title ? cover.mauiCoverData.title : "Trailhead"
            font.pixelSize: Theme.fontSizeMedium
            color: Theme.highlightColor
            truncationMode: TruncationMode.Fade
        }

        Repeater {
            model: cover.mauiCoverData && cover.mauiCoverData.lines ? cover.mauiCoverData.lines : []
            Label {
                width: parent.width
                text: modelData
                font.pixelSize: index === 0 ? Theme.fontSizeLarge : Theme.fontSizeSmall
                color: index === 0 ? Theme.primaryColor : Theme.secondaryColor
                truncationMode: TruncationMode.Fade
            }
        }
    }
}
