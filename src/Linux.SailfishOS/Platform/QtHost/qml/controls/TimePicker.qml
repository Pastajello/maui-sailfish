import QtQuick 2.6
import Sailfish.Silica 1.0
import "../lib/silica.js" as SilicaWalk

// Adapter: MAUI TimePicker -> Silica ValueButton + TimePickerDialog. Contract: see controls/Label.qml.
// mauiHour/mauiMinute form one state (pushed together). "time-selected" {id, h, mi} fires only
// on a user accept. 24h/12h follows the device locale.
ValueButton {
    id: root

    property string mauiId: ""
    property string mauiProbe: ""
    signal mauiEvent(string name, string payload)

    // Managed property pushes set this flag; events fired meanwhile are suppressed (no echo).
    property bool mauiApplying: false
    property int mauiSuppressedCount: 0

    property int mauiHour: 0
    property int mauiMinute: 0
    // Value text formatted by MAUI (TimePicker.Format); "" before the first push → H:MM fallback.
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


    // The dialog exists only while open (see DatePicker.qml: a plain-child Dialog paints its header).
    property int __hour: 0
    property int __minute: 0
    value: mauiValueText.length > 0 ? mauiValueText : __hour + ":" + (__minute < 10 ? "0" : "") + __minute

    // Assigned, not bound: an accepted pick overwrites them and a later managed push must still land.
    Component.onCompleted: { __findLabels(); __syncTime(); }
    onMauiHourChanged: __syncTime()
    onMauiMinuteChanged: __syncTime()
    function __syncTime() {
        __hour = mauiHour;
        __minute = mauiMinute;
    }

    Component {
        id: dialogComponent
        TimePickerDialog {}
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
        var dialog = pageStack.push(dialogComponent, { hour: __hour, minute: __minute });
        __track(dialog);
        dialog.accepted.connect(function() {
            __hour = dialog.hour;
            __minute = dialog.minute;
            if (mauiApplying) { mauiSuppressedCount++; return; }
            mauiEvent("time-selected",
                      JSON.stringify({ id: mauiId, h: dialog.hour, mi: dialog.minute }));
        });
    }
}
