import QtQuick 2.6
import Sailfish.Silica 1.0
import "../lib/silica.js" as SilicaWalk

// Adapter: MAUI Picker -> Silica ComboBox + ContextMenu.
// Contract: mauiId / mauiProbe / mauiEvent — see controls/Label.qml.
// mauiItems is an array of display strings; mauiSelectedIndex -1 means none.
// Events: selected {id,index}, picker-open {id,open}.
ComboBox {
    id: root

    property string mauiId: ""
    property string mauiProbe: ""
    signal mauiEvent(string name, string payload)

    // Adapter events are suppressed while managed pushes are applied (no echo loop).
    property bool mauiApplying: false
    property int mauiSuppressedCount: 0

    property string mauiTitle: ""
    property var mauiItems: []
    property int mauiSelectedIndex: -1
    // TitleColor; transparent keeps the palette.
    property color mauiTitleColor: "transparent"
    Binding { target: root; property: "labelColor"; value: root.mauiTitleColor; when: root.mauiTitleColor.a > 0 }

    // Text styling applies only what the app set. ValueButton keeps its labels
    // internal (a Flow of [title, value] under contentItem), found once after completion.
    property color mauiTextColor: "transparent"
    property real mauiPixelSize: 0
    property string mauiFamily: ""
    property bool mauiBold: false
    property bool mauiItalic: false
    property real mauiLetterSpacing: 0
    property Item __titleLabel: null
    property Item __valueLabel: null
    readonly property Item mauiTextItem: __valueLabel

    function __findLabels() {
        var labels = SilicaWalk.valueLabels(root);
        if (labels) {
            __titleLabel = labels[0];
            __valueLabel = labels[1];
        }
    }

    Binding { target: root; property: "valueColor"; value: root.mauiTextColor; when: root.mauiTextColor.a > 0 }
    Binding { target: root.__valueLabel; property: "font.pixelSize"; value: root.mauiPixelSize; when: root.__valueLabel !== null && root.mauiPixelSize > 0 }
    Binding { target: root.__valueLabel; property: "font.family"; value: root.mauiFamily; when: root.__valueLabel !== null && root.mauiFamily.length > 0 }
    Binding { target: root.__valueLabel; property: "font.bold"; value: true; when: root.__valueLabel !== null && root.mauiBold }
    Binding { target: root.__valueLabel; property: "font.italic"; value: true; when: root.__valueLabel !== null && root.mauiItalic }
    // The Title label beside the value takes the app's size and family too: with only the value at the app's 14 dp,
    // the two sat at different sizes and heights (MoneyFox: "Selected Account" over a raised "All Accounts").
    Binding { target: root.__titleLabel; property: "font.pixelSize"; value: root.mauiPixelSize; when: root.__titleLabel !== null && root.mauiPixelSize > 0 }
    Binding { target: root.__titleLabel; property: "font.family"; value: root.mauiFamily; when: root.__titleLabel !== null && root.mauiFamily.length > 0 }

    // Diagnostics readback.
    function mauiDiag() {
        return JSON.stringify({
            value: root.value,
            valueColor: root.valueColor.toString(),
            labelColor: root.labelColor.toString(),
            pixel: __valueLabel ? __valueLabel.font.pixelSize : -1,
            family: __valueLabel ? __valueLabel.font.family : "",
            bold: __valueLabel ? __valueLabel.font.bold : false,
            spacing: __valueLabel ? __valueLabel.font.letterSpacing : -1
        });
    }

    // HorizontalTextAlignment / VerticalTextAlignment of the [title, value] row; "" keeps the Silica layout.
    // Start/End are logical: under RTL, LayoutMirroring mirrors the row's anchors, margins and Flow.
    // The content width comes from implicit widths: the laid-out label widths clamp to the row, which
    // this margin narrows, so they would feed back.
    property string mauiHAlign: ""
    property string mauiVAlign: ""
    property bool __mauiVApplied: false
    onMauiHAlignChanged: __align()
    onMauiVAlignChanged: __align()
    onWidthChanged: __align()
    onHeightChanged: __align()
    onValueChanged: __align()
    onLabelChanged: __align()
    function __align() {
        if (!__titleLabel || !__valueLabel)
            return;
        var fh = mauiHAlign === "center" ? 0.5 : mauiHAlign === "end" ? 1 : 0;
        // ValueButton sizes the title implicitWidth + paddingMedium, the value implicitWidth.
        var content = __titleLabel.implicitWidth + Theme.paddingMedium + __valueLabel.implicitWidth;
        var margin = leftMargin + Math.max(0, width - leftMargin - rightMargin - content) * fh;
        if (Math.abs(labelMargin - margin) > 0.5)
            labelMargin = margin;
        var column = __titleLabel.parent ? __titleLabel.parent.parent : null;   // Flow → Column
        if (!column)
            return;
        var natural = Math.max(column.height + 2 * Theme.paddingMedium, minimumContentHeight);
        if (mauiVAlign === "") {
            if (__mauiVApplied) {
                __mauiVApplied = false;
                column.anchors.verticalCenterOffset = 0;
                contentHeight = Qt.binding(function() {
                    return visible ? Math.max(column.height + 2 * Theme.paddingMedium, minimumContentHeight) : 0;
                });
            }
            return;
        }
        __mauiVApplied = true;
        var box = Math.max(natural, height);
        if (Math.abs(contentHeight - box) > 0.5)
            contentHeight = box;
        var fv = mauiVAlign === "top" ? -0.5 : mauiVAlign === "bottom" ? 0.5 : 0;
        column.anchors.verticalCenterOffset = fv * (box - natural);
    }
    Connections { target: root.__valueLabel; onImplicitWidthChanged: root.__align() }
    Connections { target: root.__titleLabel; onImplicitWidthChanged: root.__align() }

    // x/y/width/height come from the managed geometry pass.
    label: mauiTitle

    // Imperative sync, not a binding: a user pick would break a currentIndex binding.
    onMauiSelectedIndexChanged: __syncIndex()
    onMauiItemsChanged: __syncIndex()
    Component.onCompleted: { __findLabels(); __syncIndex(); __align(); }

    function __syncIndex() {
        var count = mauiItems ? mauiItems.length : 0;
        var idx = (mauiSelectedIndex >= 0 && mauiSelectedIndex < count) ? mauiSelectedIndex : -1;
        if (currentIndex !== idx)
            currentIndex = idx;
    }

    onCurrentIndexChanged: {
        if (mauiApplying) { mauiSuppressedCount++; return; }
        if (currentIndex === mauiSelectedIndex) return;   // echo of __syncIndex
        mauiEvent("selected",
                  JSON.stringify({ id: mauiId, index: currentIndex }))
    }

    // Picker.IsOpen, synced both ways.
    property bool mauiOpen: false
    onMauiOpenChanged: __syncOpen()
    function __syncOpen() {
        if (mauiOpen && !_menuOpen)
            root._controller.openMenu();
        else if (!mauiOpen && _menuOpen && root.menu)
            root.menu.close();
    }

    // The inline menu grows the ComboBox inside its MAUI ancestors: under one that clips (an outlined field's
    // rounded Border) it is cut off while Silica dims the page. It opens as Silica's selection page then (what
    // ComboBox does past five items), as Android's Picker opens a dialog. Checked once open() returns: it sets the
    // menu's parent (opening it for _menuOpen) before activating it, so an immediate close() would be undone.
    Timer { id: __cutCheck; interval: 0; onTriggered: root.__avoidCutMenu() }
    function __avoidCutMenu() {
        if (!_menuOpen || !root.menu || !root.menu._contentColumn)
            return;
        // The column has no height yet while the menu starts expanding: a MenuItem is Theme.itemSizeSmall tall.
        var menuHeight = Math.max(root.menu._contentColumn.height, (mauiItems ? mauiItems.length : 0) * Theme.itemSizeSmall);
        var bottom = root.mapToItem(null, 0, root.contentItem.height).y + menuHeight;
        for (var p = root.parent; p && p.objectName !== "mauiCanvas"; p = p.parent) {
            if ((p.clip || (p.layer && p.layer.enabled)) && bottom > p.mapToItem(null, 0, p.height).y + 1) {
                root.menu.close();
                // Closed before it grew: no height animation runs to call _reset, which would leave the menu
                // parented (_menuOpen true) and the next click without effect.
                root.menu._reset();
                root._controller._openSeparateDialog();
                return;
            }
        }
    }
    on_MenuOpenChanged: {
        if (_menuOpen)
            __cutCheck.restart();
        if (mauiApplying || _menuOpen === mauiOpen)
            return;
        mauiEvent("picker-open", JSON.stringify({ id: mauiId, open: _menuOpen }));
    }

    menu: ContextMenu {
        Repeater {
            model: root.mauiItems
            MenuItem { text: modelData }
        }
    }
}
