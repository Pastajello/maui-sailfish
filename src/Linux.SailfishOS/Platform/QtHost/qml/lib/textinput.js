.pragma library
// Shared behaviour of the Entry/Editor/SearchBar adapters (Silica TextField, TextArea, SearchField).

// Managed focus push: true forces active focus (Maliit opens as on a tap); false commits, clears focus and
// hides the input method.
function applyManagedFocus(input, inputMethod) {
    if (input.mauiFocus) {
        input.forceActiveFocus();
    } else if (input.activeFocus) {
        inputMethod.commit();
        input.focus = false;
        // The internal editor holds the scoped focus; clear it too if activeFocus survived.
        if (input.activeFocus && input.editor)
            input.editor.focus = false;
        inputMethod.hide();
    }
}

// Caret/selection from the mauiCursor/mauiSelLen mirrors; runs on either half so batch order does not matter.
function applyCursorSel(input) {
    if (input.mauiSelLen > 0 && input.mauiCursor >= 0)
        input.select(input.mauiCursor, input.mauiCursor + input.mauiSelLen);
    else if (input.mauiCursor >= 0) {
        input.deselect();
        input.cursorPosition = input.mauiCursor;
    }
}

// Native caret/selection → "cursor-changed"; suppressed while a managed push is applied.
function reportCursor(input) {
    if (input.mauiApplying) {
        input.mauiSuppressedCount++;
        return;
    }
    input.mauiEvent("cursor-changed",
                    JSON.stringify({ id: input.mauiId, cursor: input.cursorPosition,
                                     selStart: input.selectionStart, selEnd: input.selectionEnd }));
}

// VerticalTextAlignment ("top" | "center" | "bottom"; "" = the Silica layout) inside a field taller than its
// natural height, through textTopMargin. Imperative with a dead band: a TextField/TextArea implicitHeight
// includes textTopMargin, so a binding would loop. SearchField centres itself and its implicitHeight does not
// depend on the margin; its own binding comes back when the app clears the alignment.
function applyVAlign(input, searchField) {
    var mode = input.mauiVAlign;
    var editorH = input._editor ? input._editor.implicitHeight : 0;
    if (mode === "") {
        if (!input.__mauiVAlignApplied)
            return;
        input.__mauiVAlignApplied = false;
        if (searchField)
            input.textTopMargin = Qt.binding(function() {
                return input.height / 2 - (input._editor ? input._editor.implicitHeight : 0) / 2;
            });
        else
            input.textTopMargin = input.__mauiTopMargin0;
        return;
    }
    var f = mode === "center" ? 0.5 : mode === "bottom" ? 1 : 0;
    var margin;
    if (searchField) {
        var natural = input.implicitHeight;
        margin = (Math.min(input.height, natural) - editorH) / 2 + Math.max(0, input.height - natural) * f;
    } else {
        var base = input.__mauiTopMargin0;
        var natural0 = input.implicitHeight - (input.textTopMargin - base);
        margin = base + Math.max(0, input.height - natural0) * f;
    }
    input.__mauiVAlignApplied = true;
    if (Math.abs(margin - input.textTopMargin) > 0.5)
        input.textTopMargin = margin;
}
