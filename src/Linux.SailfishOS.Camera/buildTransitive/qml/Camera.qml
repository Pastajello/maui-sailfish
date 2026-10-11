import QtQuick 2.6
import QtMultimedia 5.6
import Sailfish.Silica 1.0

// Adapter: Microsoft.Maui.SailfishOS.Camera.SailfishCameraView (SailfishCameraViewHandler), on QtMultimedia.
// Contract: mauiId / mauiProbe / mauiEvent / mauiApplying / mauiCommand (docs/custom-controls.md).
// Snapshot: position ("back"/"front"), flash ("off"/"on"/"auto"), torch, zoom, previewing.
// Command: capture {path}. Events: camera-state {available, maxZoom}, camera-captured {path}, camera-failed {reason}.
Item {
    id: root

    property string mauiId: ""
    property string mauiProbe: "state=" + camera.cameraState + " status=" + camera.cameraStatus + " availability=" + camera.availability +
                               " position=" + camera.position + " zoom=" + camera.digitalZoom.toFixed(2) + "/" + camera.maximumDigitalZoom.toFixed(2) +
                               " error='" + camera.errorString + "'"
    signal mauiEvent(string name, string payload)
    property bool mauiApplying: false

    property string position: "back"
    property string flash: "auto"
    property bool torch: false
    property real zoom: 1
    property bool previewing: true

    clip: true

    Camera {
        id: camera
        captureMode: Camera.CaptureStillImage
        position: root.position === "front" ? Camera.FrontFace : Camera.BackFace
        cameraState: root.previewing && Qt.application.active ? Camera.ActiveState : Camera.UnloadedState
        digitalZoom: Math.min(root.zoom, Math.max(1, maximumDigitalZoom))
        flash.mode: root.torch ? Camera.FlashVideoLight
                  : root.flash === "on" ? Camera.FlashOn
                  : root.flash === "off" ? Camera.FlashOff : Camera.FlashAuto
        focus.focusMode: Camera.FocusContinuous

        imageCapture {
            onImageSaved: root.mauiEvent("camera-captured", JSON.stringify({ id: root.mauiId, path: path }))
            onCaptureFailed: root.mauiEvent("camera-failed", JSON.stringify({ id: root.mauiId, reason: message }))
        }

        onCameraStatusChanged: root.reportState()
        onMaximumDigitalZoomChanged: root.reportState()
        onErrorStringChanged: if (errorString.length > 0) console.warn("[Sailfish] camera: " + errorString)
    }

    function reportState() {
        if (mauiId.length === 0)
            return;
        mauiEvent("camera-state", JSON.stringify({
            id: mauiId, available: camera.cameraStatus === Camera.ActiveStatus, maxZoom: camera.maximumDigitalZoom }));
    }
    onMauiIdChanged: reportState()

    VideoOutput {
        anchors.fill: parent
        source: camera
        fillMode: VideoOutput.PreserveAspectCrop
        autoOrientation: true
    }

    // Tap to focus at the point (where the camera supports it).
    MouseArea {
        anchors.fill: parent
        onClicked: camera.focus.customFocusPoint = Qt.point(mouse.x / width, mouse.y / height)
    }

    function mauiCommand(json) {
        var c = JSON.parse(json);
        if (c.name === "capture") {
            if (camera.imageCapture.ready)
                camera.imageCapture.captureToLocation(c.path);
            else
                root.mauiEvent("camera-failed", JSON.stringify({ id: root.mauiId, reason: "The camera is not ready" }));
        }
    }
}
