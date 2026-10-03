import QtQuick 2.6
import Sailfish.Silica 1.0

// A dialog text button, as Sailfish.Lipstick's SystemDialogTextButton: centred medium text in the primary colour,
// highlighted while pressed, tall bottom padding. Hidden while its text is empty.
BackgroundItem {
    id: button

    property alias text: label.text

    visible: text.length > 0
    height: label.height

    Label {
        id: label
        x: Theme.paddingMedium
        width: parent.width - 2 * x
        topPadding: Theme.paddingLarge
        bottomPadding: 2 * Theme.paddingLarge
        horizontalAlignment: Text.AlignHCenter
        wrapMode: Text.Wrap
        color: button.highlighted ? Theme.highlightColor : Theme.primaryColor
        font.pixelSize: Theme.fontSizeMedium
    }
}
