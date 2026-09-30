import QtQuick 2.6
import Sailfish.Silica 1.0
import "../lib/textinput.js" as InputJs

// Adapter: MAUI SearchBar -> Silica SearchField.
// Contract: mauiId / mauiProbe / mauiEvent — see controls/Label.qml.
// Focus follows the same contract as controls/Entry.qml.
// Events: text-changed {id,text}, focus-changed {id,focused}, cursor-changed,
// completed {id} (editor accepted -> SearchButtonPressed).
// inputMethodHints come from MAUI, replacing SearchField's no-prediction default.
SearchField {
    id: root

    property string mauiId: ""
    property string mauiProbe: ""
    signal mauiEvent(string name, string payload)

    // Adapter events are suppressed while managed pushes are applied (no echo loop).
    property bool mauiApplying: false

    // Text styling overrides apply only when the app set them; unset keeps the Silica
    // look. mauiLetterSpacing is written on the QFont by the shim (px).
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
    onMauiVAlignChanged: InputJs.applyVAlign(root, true)
    onHeightChanged: if (mauiVAlign !== "") InputJs.applyVAlign(root, true)
    onImplicitHeightChanged: if (mauiVAlign !== "") InputJs.applyVAlign(root, true)
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

    // Managed focus push: true opens the VKB, false commits and hides the input method.
    property bool mauiFocus: false

    // Entry parity: VKB hints, enter key icon, search icon tint, caret/selection.
    property int mauiHints: 0
    inputMethodHints: mauiHints
    property string mauiEnterIcon: ""
    EnterKey.iconSource: mauiEnterIcon
    property color mauiSearchIconColor: "transparent"
    Binding { target: root.leftItem; property: "color"; value: root.mauiSearchIconColor; when: root.leftItem !== null && root.mauiSearchIconColor.a > 0 }
    property int mauiCursor: -1
    property int mauiSelLen: -1
    onMauiCursorChanged: InputJs.applyCursorSel(root)
    onMauiSelLenChanged: InputJs.applyCursorSel(root)
    onCursorPositionChanged: InputJs.reportCursor(root)
    onSelectionStartChanged: InputJs.reportCursor(root)
    onSelectionEndChanged: InputJs.reportCursor(root)

    // CancelButtonColor re-tints the rightItem theme icon; unset keeps the palette tint.
    property color mauiCancelColor: "transparent"
    onMauiCancelColorChanged: __applyCancelColor()
    function __applyCancelColor() {
        if (mauiCancelColor.a > 0 && root.rightItem && root.rightItem.icon)
            root.rightItem.icon.color = mauiCancelColor;
    }

    // x/y/width/height come from the managed geometry pass.

    onMauiFocusChanged: InputJs.applyManagedFocus(root, Qt.inputMethod)

    onTextChanged: {
        if (mauiApplying) { mauiSuppressedCount++; return; }
        mauiEvent("text-changed",
                  JSON.stringify({ id: mauiId, text: text }))
    }

    onActiveFocusChanged: {
        if (mauiApplying) { mauiSuppressedCount++; return; }
        mauiEvent("focus-changed",
                  JSON.stringify({ id: mauiId, focused: activeFocus }))
    }

    Component.onCompleted: {
        // createObject(init) does not fire change handlers, so apply the initial tint here.
        __applyCancelColor();
        if (root.editor) {
            root.editor.accepted.connect(function() {
                if (mauiApplying) { mauiSuppressedCount++; return; }
                mauiEvent("completed", JSON.stringify({ id: mauiId }))
            })
        } else {
            console.warn("maui SearchBar adapter: internal editor unavailable — 'completed' not wired")
        }
    }
}
