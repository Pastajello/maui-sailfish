import QtQuick 2.6
import Sailfish.Silica 1.0
import "../lib/silica.js" as SilicaWalk

// Adapter: MAUI DatePicker -> Silica ValueButton + DatePickerDialog. Contract: see controls/Label.qml.
// mauiDateMs/mauiMinMs/mauiMaxMs are epoch ms of local midnight (0 = default), so the JS Date
// reads back the same wall-clock day. "date-selected" {id, y, m, d} fires only on a user accept.
ValueButton {
    id: root

    property string mauiId: ""
    property string mauiProbe: ""
    signal mauiEvent(string name, string payload)

    // Managed property pushes set this flag; events fired meanwhile are suppressed (no echo).
    property bool mauiApplying: false
    property int mauiSuppressedCount: 0

    property real mauiDateMs: 0
    property real mauiMinMs: 0
    property real mauiMaxMs: 0
    // Value text formatted by MAUI (DatePicker.Format); "" before the first push → ISO fallback.
    property string mauiValueText: ""

    // Text style overrides apply only when set. ValueButton keeps its labels internal (a Flow of
    // [title, value] under contentItem), found once after completion; the shim applies
    // mauiLetterSpacing on mauiTextItem.
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

    // The dialog exists only while open: a Silica Dialog declared as a plain child paints its
    // DialogHeader ("Cancel / Accept") over the page.
    property var __date: new Date()
    value: mauiValueText.length > 0 ? mauiValueText : Qt.formatDate(__date, "yyyy-MM-dd")

    Component {
        id: dialogComponent
        DatePickerDialog {}
    }

    Component.onCompleted: { __findLabels(); __syncDate(); }
    onMauiDateMsChanged: __syncDate()

    function __syncDate() {
        if (mauiDateMs > 0)
            __date = new Date(mauiDateMs);
    }

    function __applyBounds(dialog) {
        // This Silica build has maximumDate but no minimumDate on DatePickerDialog; write only what
        // exists. MAUI clamps out-of-range dates anyway.
        if ("minimumDate" in dialog)
            dialog.minimumDate = mauiMinMs > 0 ? new Date(mauiMinMs) : new Date(1900, 0, 1);
        if ("maximumDate" in dialog)
            dialog.maximumDate = mauiMaxMs > 0 ? new Date(mauiMaxMs) : new Date(2100, 11, 31);
    }


    // IsOpen both ways: mauiOpen pushes/pops the dialog; native open/close reports "picker-open".
    property bool mauiOpen: false
    property var __dialog: null
    readonly property bool mauiDialogOpen: __dialog !== null
    onMauiOpenChanged: {
        if (mauiOpen && !__dialog)
            __open();
        else if (!mauiOpen && __dialog && pageStack.currentPage === __dialog)
            pageStack.pop();
    }
    function __reportOpen(open) {
        if (mauiApplying || open === mauiOpen)
            return;
        mauiEvent("picker-open", JSON.stringify({ id: mauiId, open: open }));
    }
    function __track(dialog) {
        __dialog = dialog;
        var seenActive = false;
        dialog.statusChanged.connect(function() {
            if (dialog.status === PageStatus.Active)
                seenActive = true;
            else if (dialog.status === PageStatus.Inactive && seenActive && __dialog === dialog) {
                __dialog = null;
                __reportOpen(false);
            }
        });
        __reportOpen(true);
    }

    onClicked: __open()
    function __open() {
        var dialog = pageStack.push(dialogComponent, { date: __date });
        __applyBounds(dialog);
        __track(dialog);
        dialog.accepted.connect(function() {
            __date = dialog.date;
            if (mauiApplying) { mauiSuppressedCount++; return; }
            mauiEvent("date-selected",
                      JSON.stringify({ id: mauiId, y: dialog.date.getFullYear(),
                                       m: dialog.date.getMonth() + 1,
                                       d: dialog.date.getDate() }));
        });
    }
}
