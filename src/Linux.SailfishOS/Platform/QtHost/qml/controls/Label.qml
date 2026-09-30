import QtQuick 2.6
import Sailfish.Silica 1.0

// Adapter: MAUI Label -> QtQuick Text with the Silica theme (colors, font family and sizes from Theme).
// Not Silica's Label: its SilicaText palette/highlight handling and fade-truncation layer go unused here
// (color, font, elide and format are set explicitly, truncationMode never), and a plain Text creates ~30%
// faster. The adapterbench leg keeps it pixel-identical to the Silica-rooted reference
// (Linux.SailfishOS.Diagnostics/qml/diag/reference/SilicaLabel.qml).
//
// Adapter contract: mauiId is the host id set on create, mauiProbe is a diagnostics witness
// that survives in-place updates only if the object is not recreated, and
// mauiEvent(name, payload) carries semantic events to managed (page.mauiNotify).
//
// Sizes are device px; MAUI enums cross as plain Qt enum ints so QML needs no MAUI knowledge.
// text is plain, or escaped HTML when mauiTextFormat is RichText (FormattedText spans).
// Alpha-0 colors and 0 sizes mean "use the Silica theme default".
Text {
    id: root

    property string mauiId: ""
    property string mauiProbe: ""
    signal mauiEvent(string name, string payload)

    // Managed property pushes set this flag; events fired meanwhile are suppressed (no echo).
    property bool mauiApplying: false

    property string mauiEmphasis: "normal"
    property color mauiColor: "transparent"

    property real mauiPixelSize: 0
    property string mauiFamily: ""
    property bool mauiBold: false
    property bool mauiItalic: false
    property bool mauiUnderline: false
    property bool mauiStrike: false
    property real mauiLetterSpacing: 0
    property real mauiLineHeight: 1.0
    property int mauiMaxLines: 0
    property int mauiWrap: Text.Wrap
    property int mauiElide: Text.ElideNone
    property int mauiHAlign: Text.AlignLeft
    property int mauiVAlign: Text.AlignTop
    property int mauiTextFormat: Text.AutoText
    property color mauiBackground: "transparent"
    // Label.Padding (device px).
    property real mauiPadL: 0
    property real mauiPadT: 0
    property real mauiPadR: 0
    property real mauiPadB: 0
    leftPadding: mauiPadL
    topPadding: mauiPadT
    rightPadding: mauiPadR
    bottomPadding: mauiPadB

    // Child rect, so the root stays a Label and the bridge keeps setting `text` directly.
    Rectangle {
        anchors.fill: parent
        color: root.mauiBackground
        visible: color.a > 0
        z: -1
    }

    textFormat: mauiTextFormat
    wrapMode: mauiWrap
    elide: mauiElide
    horizontalAlignment: mauiHAlign
    verticalAlignment: mauiVAlign
    maximumLineCount: mauiMaxLines > 0 ? mauiMaxLines : 1000000
    lineHeight: mauiLineHeight
    lineHeightMode: Text.ProportionalHeight
    color: mauiColor.a > 0 ? mauiColor
         : mauiEmphasis === "header" ? Theme.highlightColor
         : mauiEmphasis === "secondary" ? Theme.secondaryColor
         : Theme.primaryColor
    font.family: mauiFamily.length > 0 ? mauiFamily : Theme.fontFamily
    font.pixelSize: mauiPixelSize > 0 ? mauiPixelSize
                  : mauiEmphasis === "header" ? Theme.fontSizeLarge
                  : mauiEmphasis === "small" ? Theme.fontSizeSmall
                  : Theme.fontSizeMedium
    font.bold: mauiBold
    font.italic: mauiItalic
    font.underline: mauiUnderline
    font.strikeout: mauiStrike
    // Qt 5.6: font.letterSpacingMode / Font.AbsoluteSpacing only exist from 5.10 and assigning them
    // fails the whole component load. The shim applies mauiLetterSpacing natively instead
    // (QFont::setLetterSpacing(AbsoluteSpacing, px)).
}
