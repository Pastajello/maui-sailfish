import QtQuick 2.6
import Sailfish.Silica 1.0
import QtGraphicalEffects 1.0

// The look shared by the dialog adapters: Sailfish's system dialogs (Sailfish.Lipstick SystemDialog, the permission
// and USB mode prompts). A panel across the top of the screen with a centred highlight-coloured title and message,
// the dialog's own content, then text buttons (cancel left, accept right); the page stays visible, dimmed, below it,
// and a tap there dismisses like cancel. Opened over the current model page by MauiModelPage.__pushDialog, not
// pushed on the pageStack: the page does not move and back navigation is held while the panel is up.
// Subclasses put their content in the default property and handle accepted()/rejected(); each fires once.
// Contract: mauiId / mauiProbe / mauiEvent — see controls/Label.qml.
Item {
    id: root

    property string mauiId: ""
    property string mauiProbe: ""
    signal mauiEvent(string name, string payload)

    // Adapter events are suppressed while managed pushes are applied (no echo loop).
    property bool mauiApplying: false

    property string mauiTitle: ""
    property string mauiMessage: ""

    // Button texts; an empty text hides its button.
    property string acceptText: ""
    property string cancelText: ""

    // Item handles for diagnostics (injected taps) and subclasses.
    readonly property Item __acceptItem: acceptButton
    readonly property Item __cancelItem: cancelButton
    readonly property Item __panel: panel

    default property alias content: body.data
    // The page content to show blurred behind the panel (set by MauiModelPage.__pushDialog); null = no blur.
    property Item __blurSource: null
    property bool closing: false

    signal accepted()
    signal rejected()

    function accept() {
        if (closing)
            return
        closing = true
        accepted()
    }

    function reject() {
        if (closing)
            return
        closing = true
        rejected()
    }

    anchors.fill: parent
    z: 10000
    opacity: 0
    Component.onCompleted: opacity = 1
    Behavior on opacity { FadeAnimation {} }
    onClosingChanged: if (closing) opacity = 0
    onOpacityChanged: if (closing && opacity === 0) root.destroy()

    // The page under the panel: visible, dimmed, a tap dismisses.
    Rectangle {
        anchors.fill: parent
        color: Theme.rgba(Theme.overlayBackgroundColor, Theme.opacityLow)
    }
    MouseArea {
        anchors.fill: parent
        onClicked: root.reject()
    }

    Item {
        id: panel
        width: parent.width
        height: scroller.height + buttons.height

        // Swallows taps on the panel's empty parts so they do not dismiss.
        MouseArea { anchors.fill: parent }

        // Lipstick paints its dialogs on a blurred copy of the screen under the palette's overlay colour. Inside
        // the app window the wallpaper is out of reach (its own window), so: the overlay colour nearly opaque (the
        // sharp page under it must not read through), the page content blurred once on top, Silica's panel tint.
        Rectangle {
            anchors.fill: parent
            color: Theme.rgba(Theme.overlayBackgroundColor, 0.95)
        }
        ShaderEffectSource {
            id: blurCopy
            sourceItem: root.__blurSource
            sourceRect: root.__blurSource
                        ? Qt.rect(-root.__blurSource.x, -root.__blurSource.y, panel.width, panel.height)
                        : Qt.rect(0, 0, 0, 0)
            live: false
            visible: false
        }
        FastBlur {
            anchors.fill: parent
            visible: root.__blurSource !== null
            source: blurCopy
            radius: 64
            cached: true
            opacity: 0.5
        }
        Rectangle {
            anchors.fill: parent
            gradient: Gradient {
                GradientStop { position: 0.0; color: Theme.rgba(Theme.highlightBackgroundColor, 0.1) }
                GradientStop { position: 1.0; color: Theme.rgba(Theme.highlightBackgroundColor, 0.2) }
            }
        }

        // Header and content scroll when they do not fit; the buttons stay.
        SilicaFlickable {
            id: scroller
            width: parent.width
            height: Math.min(inner.height, root.height - Theme.itemSizeLarge - buttons.height)
            contentHeight: inner.height
            clip: contentHeight > height

            Column {
                id: inner
                width: parent.width

                // SystemDialogHeader: generous top padding, title then message, both centred.
                Item {
                    width: parent.width
                    height: header.height + 2 * Theme.paddingLarge + Theme.paddingLarge
                    visible: root.mauiTitle.length > 0 || root.mauiMessage.length > 0

                    Column {
                        id: header
                        x: Theme.horizontalPageMargin
                        y: 2 * Theme.paddingLarge
                        width: parent.width - 2 * x
                        spacing: Theme.paddingLarge

                        Label {
                            width: parent.width
                            visible: text.length > 0
                            text: root.mauiTitle
                            wrapMode: Text.Wrap
                            horizontalAlignment: Text.AlignHCenter
                            color: Theme.highlightColor
                            font.pixelSize: Theme.fontSizeLarge
                        }
                        Label {
                            width: parent.width
                            visible: text.length > 0
                            text: root.mauiMessage
                            wrapMode: Text.Wrap
                            horizontalAlignment: Text.AlignHCenter
                            color: Theme.highlightColor
                            font.pixelSize: Theme.fontSizeMedium
                        }
                    }
                }

                Column {
                    id: body
                    width: parent.width
                }
            }
            VerticalScrollDecorator {}
        }

        Row {
            id: buttons
            property real buttonWidth: Math.min(panel.width / 2, Theme.itemSizeHuge * 1.5)
            anchors.horizontalCenter: parent.horizontalCenter
            y: scroller.height

            DialogButton {
                id: cancelButton
                width: buttons.buttonWidth
                text: root.cancelText
                onClicked: root.reject()
            }
            DialogButton {
                id: acceptButton
                width: buttons.buttonWidth
                text: root.acceptText
                onClicked: root.accept()
            }
        }
    }
}
