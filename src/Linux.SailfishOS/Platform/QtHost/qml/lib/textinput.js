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
    if (input.__mauiCompact)
        return;   // applyCompact owns the margin
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

// A TextField arranged shorter than its natural height (a HeightRequest below Silica's) clips its editor to the
// height minus the margins, hiding the text. Compact mode drops the label row and centres the editor line in the
// given height instead. The natural height is taken outside compact mode (hiding the label shrinks implicitHeight);
// imperative with a dead band for the same binding loop as applyVAlign.
function applyCompact(input) {
    if (!input.__mauiCompact)
        input.__mauiNatural = input.implicitHeight - (input.textTopMargin - input.__mauiTopMargin0);
    var compact = input.height > 0 && input.height < input.__mauiNatural - 0.5;
    if (compact !== input.__mauiCompact) {
        input.__mauiCompact = compact;
        input.labelVisible = !compact && !input.mauiBare;
        if (!compact) {
            input.textTopMargin = input.__mauiTopMargin0;
            input.__mauiVAlignApplied = false;
            applyVAlign(input, false);
            return;
        }
    }
    if (!compact)
        return;
    var editorH = input._editor ? input._editor.implicitHeight : 0;
    var margin = Math.max(0, (input.height - editorH) / 2);
    if (Math.abs(margin - input.textTopMargin) > 0.5)
        input.textTopMargin = margin;
}
