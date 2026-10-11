# Microsoft.Maui.Platforms.SailfishOS.Camera

A camera view for Sailfish OS on QtMultimedia (the camera Sailfish's own apps use): a live preview that takes photos.

```csharp
builder.UseSailfishCamera();
```

```csharp
var camera = new SailfishCameraView { CameraPosition = SailfishCameraPosition.Back, FlashMode = SailfishFlashMode.Auto };
string path = await camera.CaptureImageAsync();            // a JPEG in the app's cache (or the path given)
```

It is shaped like CommunityToolkit.Maui's `CameraView` (`CaptureImage`, `MediaCaptured`, `MediaCaptureFailed`,
flash, torch, zoom, front/back), which cannot run on Sailfish: the toolkit's view drives its own internal platform
manager, and a platform outside the toolkit cannot provide one. `IsPreviewing = false` releases the camera (set it when
the page goes away); the preview also stops while the app is in the background.

The app needs the `Camera` Sailjail permission (`<SailfishPermissions>Camera</SailfishPermissions>`); both the
QtMultimedia import and the permission are allowed in the Jolla Store.
