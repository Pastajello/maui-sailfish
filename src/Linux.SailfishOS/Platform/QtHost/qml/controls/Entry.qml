import QtQuick 2.6
import Sailfish.Silica 1.0
import "../lib/textinput.js" as InputJs

// Adapter: MAUI Entry -> Silica TextField. Contract: see controls/Label.qml.
// Events: "text-changed", "focus-changed" (Qt is the focus source), "cursor-changed",
// "completed". All are suppressed under mauiApplying (counted in mauiSuppressedCount).
TextField {
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
    onHeightChanged: if (mauiVAlign !== "") InputJs.applyVAlign(root, false)
    onImplicitHeightChanged: if (mauiVAlign !== "") InputJs.applyVAlign(root, false)
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


    // Managed focus push: true forces active focus (opens the Maliit keyboard the same way a tap
    // does); false commits, clears focus and hides the input method.
    property bool mauiFocus: false

    // Selection spans [CursorPosition, CursorPosition + SelectionLength); -1 = not driven.
    property int mauiCursor: -1
    property int mauiSelLen: -1

    // Password hints (ImhHiddenText|ImhSensitiveData|ImhNoAutoUppercase|ImhNoPredictiveText = 0x47,
    // as Silica PasswordField) are composed here from echoMode, so IsPassword needs no re-push.
    property int mauiHints: 0
    inputMethodHints: mauiHints | (root.echoMode === TextInput.Password ? 0x47 : 0)

    // MAUI ReturnType -> VKB enter key icon ("" = Silica default).
    property string mauiEnterIcon: ""
    EnterKey.iconSource: mauiEnterIcon

    // ClearButtonVisibility.WhileEditing: shown while focused with text. Held by a property so it
    // has no visual parent until shown.
    property bool mauiClearButton: false
    property Item __clearButton: IconButton {
        icon.source: "image://theme/icon-m-input-clear"
        onClicked: {
            root.text = "";
            root.forceActiveFocus();
        }
    }
    rightItem: mauiClearButton && root.activeFocus && root.text.length > 0 ? __clearButton : null

    onMauiFocusChanged: InputJs.applyManagedFocus(root, Qt.inputMethod)

    onMauiCursorChanged: InputJs.applyCursorSel(root)
    onMauiSelLenChanged: InputJs.applyCursorSel(root)

    onTextChanged: {
        if (mauiApplying) { mauiSuppressedCount++; return; }
        mauiEvent("text-changed",
                  JSON.stringify({ id: mauiId, text: text }))
    }

    // Suppressed under mauiApplying so a managed focus push does not echo back.
    onActiveFocusChanged: {
        if (mauiApplying) { mauiSuppressedCount++; return; }
        mauiEvent("focus-changed",
                  JSON.stringify({ id: mauiId, focused: activeFocus }))
    }

    // Native -> managed write-back into Entry.CursorPosition/SelectionLength.
    onCursorPositionChanged: InputJs.reportCursor(root)
    onSelectionStartChanged: InputJs.reportCursor(root)
    onSelectionEndChanged: InputJs.reportCursor(root)

    // The internal TextInput emits accepted on Return from both the hardware keyboard and the
    // Maliit enter key. No EnterKey action is declared, so there is exactly one completion path.
    Component.onCompleted: {
        if (root.editor) {
            root.editor.accepted.connect(function() {
                if (mauiApplying) { mauiSuppressedCount++; return; }
                mauiEvent("completed", JSON.stringify({ id: mauiId }))
            })
        } else {
            console.warn("maui Entry adapter: internal editor unavailable — 'completed' not wired")
        }
    }
}
