import QtQuick 2.6
import Sailfish.Silica 1.0
import "../lib/textinput.js" as InputJs

// Adapter: MAUI Editor -> Silica TextArea. Contract: see controls/Label.qml; events as in
// controls/Entry.qml. Editor.Completed has no native trigger: Qt 5.6 TextEdit has no accepted
// signal and the Sailfish VKB shows a newline key, so Return inserts a newline.
TextArea {
    id: root

    property string mauiId: ""
    property string mauiProbe: ""
    signal mauiEvent(string name, string payload)

    // Managed property pushes set this flag; events fired meanwhile are suppressed (no echo).
    property bool mauiApplying: false

    // Text style overrides (QtHostPageRenderer.TextStyleProps) apply only when set; otherwise the
    // Silica look stays. mauiLetterSpacing is applied natively by the shim (absolute px).
    property color mauiColor: "transparent"
    property color mauiPlaceholderColor: "transparent"
    property real mauiPixelSize: 0
    property string mauiFamily: ""
    property bool mauiBold: false
    property bool mauiItalic: false
    property string mauiHAlign: ""
    // VerticalTextAlignment (InputJs.applyVAlign); "" keeps the Silica layout.
    property string mauiVAlign: ""
    property bool __mauiVAlignApplied: false
    readonly property real __mauiTopMargin0: Theme.paddingSmall
    onMauiVAlignChanged: InputJs.applyVAlign(root, false)
    // Compact mode (InputJs.applyCompact): a HeightRequest below the natural height keeps the text visible.
    property bool __mauiCompact: false
    property real __mauiNatural: 0
    onHeightChanged: { InputJs.applyCompact(root); if (mauiVAlign !== "") InputJs.applyVAlign(root, false) }
    onImplicitHeightChanged: { InputJs.applyCompact(root); if (mauiVAlign !== "") InputJs.applyVAlign(root, false) }
    property real mauiLetterSpacing: 0

    Binding { target: root; property: "color"; value: root.mauiColor; when: root.mauiColor.a > 0 }
    Binding { target: root; property: "placeholderColor"; value: root.mauiPlaceholderColor; when: root.mauiPlaceholderColor.a > 0 }
    Binding { target: root; property: "font.pixelSize"; value: root.mauiPixelSize; when: root.mauiPixelSize > 0 }
    Binding { target: root; property: "font.family"; value: root.mauiFamily; when: root.mauiFamily.length > 0 }
    Binding { target: root; property: "font.bold"; value: true; when: root.mauiBold }
    Binding { target: root; property: "font.italic"; value: true; when: root.mauiItalic }
    Binding {
        target: root; property: "horizontalAlignment"; when: root.mauiHAlign.length > 0
        value: root.mauiHAlign === "center" ? Text.AlignHCenter
             : root.mauiHAlign === "right" ? Text.AlignRight : Text.AlignLeft
    }
    property int mauiSuppressedCount: 0


    // Managed focus push (forceActiveFocus opens the Maliit keyboard).
    property bool mauiFocus: false

    // Selection spans [CursorPosition, CursorPosition + SelectionLength); -1 = not driven.
    property int mauiCursor: -1
    property int mauiSelLen: -1

    // Editor.MaxLength (-1 = unlimited): TextArea has no maximumLength, so longer text is cut back.
    property int mauiMaxLength: -1
    onMauiMaxLengthChanged: __clampLength()
    function __clampLength() {
        if (mauiMaxLength >= 0 && text.length > mauiMaxLength) {
            text = text.substring(0, mauiMaxLength);
            cursorPosition = text.length;
        }
    }

    property int mauiHints: 0
    inputMethodHints: mauiHints

    onMauiFocusChanged: InputJs.applyManagedFocus(root, Qt.inputMethod)

    onMauiCursorChanged: InputJs.applyCursorSel(root)
    onMauiSelLenChanged: InputJs.applyCursorSel(root)

    // A clamp during a managed push must still report the cut text, one event-loop turn later.
    Timer { id: clampReport; interval: 0; onTriggered: root.mauiEvent("text-changed", JSON.stringify({ id: root.mauiId, text: root.text })) }
    onTextChanged: {
        if (mauiMaxLength >= 0 && text.length > mauiMaxLength) {
            var applying = mauiApplying;
            __clampLength();   // re-enters with the clamped text
            if (applying)
                clampReport.restart();
            return;
        }
        if (mauiApplying) { mauiSuppressedCount++; return; }
        mauiEvent("text-changed",
                  JSON.stringify({ id: mauiId, text: text }))
    }

    // Qt is the focus source; suppressed under mauiApplying so a managed focus push does not echo.
    onActiveFocusChanged: {
        if (mauiApplying) { mauiSuppressedCount++; return; }
        mauiEvent("focus-changed",
                  JSON.stringify({ id: mauiId, focused: activeFocus }))
    }

    // Native -> managed write-back into Editor.CursorPosition/SelectionLength.
    onCursorPositionChanged: InputJs.reportCursor(root)
    onSelectionStartChanged: InputJs.reportCursor(root)
    onSelectionEndChanged: InputJs.reportCursor(root)
}
