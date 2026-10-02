# SkiaSharp on Sailfish OS — execution and test plan

Status: **plan, not started** (2026-10-01). Answers the open question in
[`app-test-campaign.md`](app-test-campaign.md#open-questions): yes, SkiaSharp support is built, as
`Microsoft.Maui.SailfishOS.SkiaSharp`, with full feature parity with MAUI Android. Implementation follows this plan
phase by phase (S0–S7); every phase ends with its tests green on the device.

## 1. Goal and what "100% parity" means

**Reference platform: MAUI Android.** Sailfish is a touch phone, like Android. For every public API of
`SkiaSharp.Views.Maui.Controls` / `.Core` (2.88.x and 3.x), the observable behaviour on Sailfish must equal the
behaviour on Android. Observable means:

- the values the app receives (`SKPaintSurfaceEventArgs.Info`/`RawInfo`, `CanvasSize`, `GRContext`,
  every `SKTouchEventArgs` field, event order and count),
- the pixels on screen (position, size, colour, alpha, z-order, clip, transforms),
- layout (measured and arranged sizes),
- lifecycle (when `PaintSurface` runs and when it does not: hidden, detached, navigation, rotation, background),
- input routing (what the canvas gets, what the parent gets: `Handled`, scroll-view interception, cancel).

**Allowed differences.** These are the only ones, each with a reason. Nothing else counts as parity:

| # | Difference | Why |
|---|---|---|
| AD1 | Default typeface (`SKTypeface.Default`, fontconfig → Sailfish font instead of Roboto) and so text metrics | Platform font, as for `Label` |
| AD2 | `SKGLView` pixels may differ by GPU rasterization (antialiasing, gradients) within the tolerance in §6.4 | Different GPU and driver; Android GPU pixels differ between devices too |
| AD3 | Frame cadence (vsync period, exact frame in which a paint lands) | Different compositor; tested as "next frame", not as a timestamp |
| AD4 | `Pressure` values | Hardware-reported (Sailfish touchscreens often report a constant) |

Mouse, hover and wheel are a decision, not a difference: see D2 in §8.

## 2. Current state (facts, 2026-10-01)

- The native library already works. With `SkiaSharp.NativeAssets.Linux` (glibc, linux-arm64) in the version the
  app resolves, `SKTypeface`/`SKBitmap`/`SKSurface` work on the device (MoneyFox, Profitocracy:
  [`porting-existing-apps.md`](porting-existing-apps.md)). Sailfish OS 5.2 has glibc 2.34; libSkiaSharp needs ≤ 2.27.
- `libSkiaSharp.so` (2.88.6 and 3.119.4) has `libGL.so.1`/`glXGetProcAddress` only for its *native* GL interface
  (GLX). Sailfish is EGL/GLES, so `GRGlInterface.Create()` cannot work there. A GPU path must build the interface
  with `GRGlInterface.CreateGles(getProcAddress)` on Qt's context (spike S0.2).
- The views do not work. The plain-`net` assets of `SkiaSharp.Views.Maui.Core` contain
  `SKCanvasViewHandler : ViewHandler<ISKCanvasView, object>` (3.x also `SKGLViewHandler`), whose
  `CreatePlatformView` throws `NotImplementedException` and whose mappers are empty. On Sailfish they fall back to
  an empty container: white area.
- `UseSkiaSharp()` on plain `net` registers:
  - 2.88.x: the `SKCanvasView` handler, plus `SKImageSourceService` for `ISKImageImageSource`, `ISKBitmapImageSource`,
    `ISKPixmapImageSource` and `ISKPictureImageSource`. There is **no `SKGLView` handler in 2.88 plain `net`**.
  - 3.x: as 2.88, plus the `SKGLView` handler.

  The plain-`net` `SKImageSourceService` is an empty `ImageSourceService`. On Sailfish, images are resolved by
  `QtHostImages.Resolve` through a type switch over the MAUI sources, so every `SK*ImageSource` resolves to
  nothing: an empty image.
- The Sailfish input router forwards only the first touch point, ignores the wheel and has no pinch
  (`QtHostInput.cs`). Test injection (`sailfish_host_inject_pointer`) is single-pointer mouse.
- Adapter events (`mauiEvent`) reach C# **asynchronously** (queued, drained by `mauiEventSeq`). Android's
  `SKTouchEventArgs.Handled` decides synchronously whether the view keeps the gesture. Touch therefore needs a
  synchronous native → managed path on the Qt thread.
- Apps in the campaign that draw through SkiaSharp views (they serve only as real-world consumers in S7):

  | App | Draws through | SkiaSharp | Uses |
  |---|---|---|---|
  | MoneyFox | LiveCharts 2.0.0-rc2 | 2.88.6, `UseSkiaSharp(true)` (Compatibility overload) | 4 statistics pages |
  | Profitocracy | LiveCharts 2.0.0-rc5.4 | 3.116.1, `UseSkiaSharp()` | Overview cards |
  | WeightTracker | Microcharts.Maui 1.0.0 (conditioned out; 2.0.0.3 has `net10.0`) | 2.88.7 / 3.119.4 | Graph page |
  | GitTrends | Syncfusion.Maui.Charts (not SkiaSharp-based); `SkiaSharp.Views.Maui.Controls` 3.119.1 | 3.119.1 | SkiaSharp use re-checked in S7 |

## 3. Parity inventory

Every row is a requirement with its test IDs (§6). A row is done when its tests pass on the device and, where
marked **Δ**, the differential test against the Android emulator shows no difference beyond §1.

### 3.1 Registration and resolution

| ID | Requirement (Android behaviour) | Sailfish implementation | Tests |
|---|---|---|---|
| R1 | `UseSkiaSharp()` (2.88 and 3.x), `UseSkiaSharp(bool, bool)` (2.88 Compatibility) and `UseSkiaSharpHandlers()` (obsolete 3.x) make `SKCanvasView`/`SKGLView` render | Sailfish handler replaces SkiaSharp's plain-`net` handler wherever it was registered, independent of call order | U-R1..R4 |
| R2 | No app code beyond the package reference | Package's `buildTransitive` adds `[assembly: AssemblyMetadata("Microsoft.Maui.SailfishOS.Extension", "<registrar type>")]` to the app. The backend reads it at startup (the existing `AssemblyMetadata` pattern of `SailfishMauiApplication`) | U-R5, B-3 |
| R3 | A subclass of `SKCanvasView` (Microcharts `ChartView`, LiveCharts `CPURenderMode`) or of its handler (`MyHandler : SKCanvasViewHandler`) works | Override table maps SkiaSharp handler types (and subclasses registered by libraries) onto Sailfish handlers; type-hierarchy resolution as today | U-R6, U-R7 |
| R4 | `SKCanvasViewHandler.SKCanvasViewMapper` / `SKGLViewMapper` / command mappers: an app's `AppendToMapping`/`ModifyMapping` on them still runs | Sailfish handlers run their own mapper, then the keys an app added to SkiaSharp's static mappers (same model as MAUI stock mappers) | U-R8 |
| R5 | Mapper keys = SkiaSharp's: `EnableTouchEvents`, `IgnorePixelScaling` (+`HasRenderLoop` for GL), command `InvalidateSurface`, plus `ViewHandler.ViewMapper` | Automated key-set comparison, added to [`handler-parity.md`](handler-parity.md) | U-R9 |

### 3.2 `SKCanvasView` (raster)

| ID | Requirement (Android behaviour, from `SkiaSharp.Views.Android.SKCanvasView` + `SurfaceFactory`) | Tests |
|---|---|---|
| C1 | `Info` = view size in device pixels (`Width × Height`), `ColorType = Rgba8888`, `AlphaType = Premul`; `RawInfo` = the same | U-C1, D-C1, P-C1 Δ |
| C2 | `IgnorePixelScaling = true`: `Info` = `((int)(w/density), (int)(h/density))`, `RawInfo` = px, canvas pre-scaled by density and `Save()`d once | U-C2, D-C2, P-C2 Δ |
| C3 | `CanvasSize` = `Info.Size` of the last paint; `OnCanvasSizeChanged` fires only when it changes | U-C3, P-C3 Δ |
| C4 | Pixel edges of the view follow MAUI Android's rounding of the arranged rect (each edge to px), so sizes at fractional densities match | U-C4, P-C4 Δ |
| C5 | Width or height 0 → no paint, buffer freed, `CanvasSize` empty | U-C5, D-C5 |
| C6 | The buffer persists between paints (same bitmap reused at the same size; content from the previous paint stays unless the app clears it). A new size starts zeroed | D-C6, P-C6 Δ |
| C7 | `InvalidateSurface()` → exactly one paint in the next frame, however many calls came before it (coalesced) | U-C7, D-C7, P-C7 Δ |
| C8 | Paint triggers: first attach, size change, `IgnorePixelScaling` change, invalidate, re-attach after detach. Nothing else: parent scroll, sibling change or opacity change do not repaint | U-C8, D-C8, P-C8 Δ |
| C9 | Not painted while `IsVisible = false` (buffer freed). Becoming visible paints | D-C9, P-C9 Δ |
| C10 | `PaintSurface` runs on the UI thread (Qt thread = MAUI main thread), synchronously inside the frame | U-C10, D-C10 |
| C11 | Pixels on screen are 1:1 with the buffer: exact colour, premultiplied alpha blended over what is behind (`BackgroundColor`, siblings), no blur from filtering (item snapped to device pixels) | D-C11 (pixel compare) |
| C12 | Generic view state works on the canvas: `Opacity`, `BackgroundColor`/`Background` (painted under the bitmap), `Clip`, `Shadow`, `Rotation*`/`Scale*`/`Translation*`/`Anchor*`, `ZIndex`, `IsVisible`, `InputTransparent`, `IsEnabled`, `FlowDirection` (no mirroring of content, as Android) | D-C12, P-C12 Δ |
| C13 | Measure: an unconstrained `SKCanvasView` measures as on Android (no intrinsic size; fills a finite constraint, 0 in an infinite one; `WidthRequest`/`HeightRequest` honoured). 2.88's `OnMeasure` (40×40) is inert on MAUI's measure path; checked, not assumed | U-C13, P-C13 Δ |
| C14 | Large canvases: a canvas bigger than `GL_MAX_TEXTURE_SIZE` renders (tiled textures). Android draws bitmaps up to ~100 MB | D-C14 |
| C15 | Orientation change and window resize → resize + one paint | D-C15, P-C15 Δ |
| C16 | Inside `CollectionView`/`ListView` templates: handler reconnects on recycling, no stale pixels from another item, buffers released on disconnect | D-C16 |
| C17 | Navigation away and back (Shell, NavigationPage, modal, back cache): no leak, paint on return as Android's re-attach | D-C17, F-3 |
| C18 | App to background/cover and back: no crash; repaint if the scene graph was released | D-C18 |
| C19 | Exceptions thrown in `PaintSurface` behave as on Android (propagate to the unhandled-exception path; the app does not keep a half-drawn frame silently) | U-C19, D-C19 |
| C20 | Hot path cost: no allocation per paint except what the app does. Buffer reused; texture updated in place (`glTexSubImage2D`), not re-created | F-1, F-2 |

### 3.3 Touch (`EnableTouchEvents`, `Touch`, `SKTouchEventArgs`)

| ID | Requirement (Android `SKTouchHandler`) | Tests |
|---|---|---|
| T1 | `EnableTouchEvents = false` (default): no `Touch` events; input goes to the parent as if the canvas were not there | U-T1, D-T1, P-T1 Δ |
| T2 | Down → `Pressed`, move → `Moved`, up → `Released`, cancel → `Cancelled`. `InContact` = true for Pressed/Moved, false for Released/Cancelled | U-T2, D-T2, P-T2 Δ |
| T3 | Multi-touch: every pointer has its own `Id`; a second finger down → `Pressed` with its id, a move → one `Moved` **per active pointer**, a finger up → `Released` for that id. Ids are small integers reused like Android pointer ids (0, 1, …), not raw Qt ids | U-T3, D-T3, P-T3 Δ |
| T4 | `Location` in device px relative to the canvas; with `IgnorePixelScaling` in dp. Outside the bounds while captured, values go negative or past the size (no clamping) | U-T4, D-T4, P-T4 Δ |
| T5 | `Handled`: if the first `Pressed` is not handled, the canvas does not get the rest of that gesture; the parent (ScrollView, Silica page) gets it | U-T5, D-T5, P-T5 Δ |
| T6 | Parent interception: in a vertical `ScrollView`/`CollectionView`, a vertical drag past the slop is taken by the parent and the canvas gets `Cancelled`; a horizontal drag stays with the canvas. Horizontal scroller: the same, transposed | D-T6, P-T6 Δ |
| T7 | Silica system gestures keep working over a canvas: the edge back-swipe navigates back, pulley menu from the top over a canvas inside a flickable | D-T7 |
| T8 | `IsEnabled = false` → no touch events (Android's touch listener is not called on a disabled view). `InputTransparent = true` → input passes through | D-T8, P-T8 Δ |
| T9 | `DeviceType = Touch`, `MouseButton = Left` for touch (Android `GetButton` default), `WheelDelta = 0`, `Pressure` from the device (AD4) | U-T9, P-T9 Δ |
| T10 | Tap gesture recognizers on the canvas or its parents behave as on Android when the canvas handles/doesn't handle the touch | D-T10, P-T10 Δ |
| T11 | Events arrive on the UI thread, synchronously in input order; `InvalidateSurface` from inside `Touch` paints in the next frame | U-T11, D-T11 |
| T12 | Mouse/hover/wheel: see decision D2 (§8) | U-T12 |

### 3.4 `SKGLView` (GPU)

| ID | Requirement (Android `SKGLViewHandler` + `SKGLTextureView`) | Tests |
|---|---|---|
| G1 | Renders through a real `GRContext` (Skia GL backend on Qt's GLES context); `PaintSurface` gets `Surface` (GPU), `BackendRenderTarget`, `Origin`, `ColorType`, `Info`/`RawInfo` | D-G1, P-G1 Δ |
| G2 | `GRContext` property non-null after the first paint; `OnGRContextChanged` when the context is created or replaced (scene graph re-created after background) | D-G2 |
| G3 | `IgnorePixelScaling` as Android's `MauiSKGLTextureView` (dp `Info`, canvas scaled by density, `RawInfo` px) | D-G3, P-G3 Δ |
| G4 | `HasRenderLoop = true` and the view is in a window → paint every frame; false → paint on invalidate only (Android `RenderMode` continuous / when-dirty). Leaving the window stops the loop (`ISKGLView.HasRenderLoop` is false without a `Window`) | D-G4, P-G4 Δ |
| G5 | Paints run on the GL render thread, as Android's GL thread (`PaintSurface` is not on the UI thread when Qt's loop is threaded; recorded which loop the device uses) | D-G5 |
| G6 | Transparent (Android `SetOpaque(false)`): what is behind shows through undrawn areas | D-G6 |
| G7 | C8/C9/C12/C15/C17/C18 also hold for `SKGLView` | D-G7 |
| G8 | Touch: T1–T12 hold for `SKGLView` | D-G8 |
| G9 | 2.88 has no plain-`net` `SKGLView` handler. On 2.88 the Sailfish package still renders `SKGLView` (Android does on 2.88) | U-G9, V-2 |
| G10 | If GL init fails on a device (driver), fall back to the raster path with `GRContext = null`, log one `[QT_HOST][WARN]`, and never show an empty view | D-G10 (forced by env) |

### 3.5 Image sources

| ID | Requirement (Android `SKImageSourceService`) | Tests |
|---|---|---|
| I1 | `SKImageImageSource`, `SKBitmapImageSource`, `SKPixmapImageSource`, `SKPictureImageSource` (rasterized at `Dimensions`) display in `Image` | U-I1, D-I1, P-I1 Δ |
| I2 | …and wherever MAUI takes an `ImageSource`: `ImageButton`, `Button.ImageSource`, `ToolbarItem`/`MenuItem` icons, `Page.IconImageSource`, Shell/Tab icons, `Image` in templates | D-I2 |
| I3 | Intrinsic size = Android's (bitmap pixels shown at px / density dp) | U-I3, P-I3 Δ |
| I4 | Changing `Bitmap`/`Image`/`Pixmap`/`Picture`/`Dimensions` reloads the image | U-I4, D-I4 |
| I5 | Null/empty source → empty image, no error (Android returns null result) | U-I5 |
| I6 | Generated files are cached by content generation and cleaned up; no unbounded growth | U-I6, F-4 |

### 3.6 Pure managed API

| ID | Requirement | Tests |
|---|---|---|
| M1 | `Extensions` (`ToSKColor`, `ToMauiColor`, points, rects, sizes) | U-M1 (already managed; asserted) |
| M2 | `SKTouchEventArgs.ToString`, constructors, `SKPaintSurfaceEventArgs`/`SKPaintGLSurfaceEventArgs` constructors (apps build them in tests) | U-M2 |

### 3.7 Native library and versions

| ID | Requirement | Tests |
|---|---|---|
| N1 | Supported SkiaSharp lines: 2.88.x (2.88.6 … 2.88.9) and 3.x (3.116.1 … 3.119.4), managed and native in the same version | V-1..V-4 |
| N2 | A build that resolves SkiaSharp without a matching `libSkiaSharp.so` for the Sailfish RID fails with an exact fix (`SF-SKIA001: add <PackageReference Include="SkiaSharp.NativeAssets.Linux" Version="X" />`), instead of a `DllNotFoundException` on the phone | B-1 |
| N3 | A managed/native version mismatch is reported at build time (`SF-SKIA002`), not at runtime | B-2 |
| N4 | armv7hl: the package builds and the `linux-arm` native asset is picked; runtime is tested only if an armv7hl device is available | B-4 (V-5 if a device exists) |
| N5 | Trimmed and AOT builds work (`docs/aot-and-trimming.md`): no trimmed-away handler or registrar | B-5, A-4 |

### 3.8 Out of scope

Only SkiaSharp itself is delivered. Libraries built on it (LiveCharts, Microcharts, SkiaSharp.Extended, Svg.Skia,
…) bring their own Sailfish support if they need it.

The known case: LiveCharts on plain `net` has no input in any version (2.0.4 `PointerController` and rc
`ChartBehaviour` are empty stubs), so its charts draw but do not react to touch. That is LiveCharts' work, not
ours.

Also not in scope: MAUI `PinchGestureRecognizer` (the Sailfish router has no pinch), Syncfusion charts,
`Microsoft.Maui.Graphics.Skia` views.

## 4. Architecture

```text
SKCanvasView / SKGLView (SkiaSharp.Views.Maui.Controls, unchanged)
    │  resolved to the Sailfish handler (override table, any registration order)
    ▼
SailfishSKCanvasViewHandler / SailfishSKGLViewHandler  (Microsoft.Maui.SailfishOS.SkiaSharp)
    │  raster: SKSurface over a reused RGBA8888-premul buffer → PaintSurface → commit
    │  GL: GRContext on Qt's GLES context, PaintSurface on the render thread into the item's FBO
    │  touch: synchronous callback → SKTouchEventArgs → Handled returned to the item
    ▼
QtHostSurface (Microsoft.Maui.SailfishOS, public, generic: "a natively hosted drawing surface")
    │  generic ABI: surface create/commit/request-frame/touch-callback/gl-proc-address
    ▼
libsailfishhost.so: MauiSurfaceItem (QQuickItem, raster) / MauiGlSurfaceItem (QQuickFramebufferObject)
    │  texture updated in place, tiled past GL_MAX_TEXTURE_SIZE, snapped to device pixels
    ▼
Qt Quick scene graph → Wayland → lipstick
```

**Principle exception, to be recorded in [`architecture.md`](architecture.md).** "No per-frame CPU copy" stays
the rule for the renderer. A raster `SKCanvasView` is CPU drawing by definition (Android's is too: a `Bitmap` drawn by
`onDraw`), so its buffer is uploaded once per paint, and only for that view's area. The upload is a
`glTexSubImage2D` into a persistent texture; there is no PNG, no readback and no second copy in C#.

**Generic ABI, not a Skia ABI.** The shim gets a drawing-surface host and a touch callback. Neither knows
SkiaSharp: any library that draws pixels (a video frame, a custom renderer) can use them. That keeps the
"generic native ABI" principle.

**New native functions (`sailfish_host.h`):**

- `sailfish_host_surface_commit(long item, const void* pixels, int w, int h, int stride)`: copies into the
  item's staging image and schedules `update()`. The copy lets C# reuse its buffer at once.
- `sailfish_host_surface_set_touch(long item, int enabled, sfhost_touch_fn fn, void* user)`. The callback is
  `int fn(user, int action, int pointerId, double x, double y, float pressure, int device, int button, int wheel)`
  and returns `handled`. It is called synchronously from `touchEvent`/`mouse*Event`/`touchUngrabEvent` on the Qt
  thread.
- `sailfish_host_request_frame(long item)`: a per-frame callback (`QQuickWindow::afterAnimating`) for coalesced
  invalidation (C7) and the GL render loop (G4).
- `sailfish_host_gl_get_proc_address(const char*)` (for `GRGlInterface.CreateGles`) and
  `sailfish_host_gl_surface_*` (create, set paint callback that runs in `QQuickFramebufferObject::Renderer::render`,
  continuous mode).
- `sailfish_host_inject_touch(int count, const int* ids, const double* xy, const int* states)`: multi-touch test
  injection through `QWindowSystemInterface::handleTouchEvent`, so Qt's real dispatch (Flickable stealing)
  applies.

**Packaging (decision D1).** One package, two assemblies:

- `Microsoft.Maui.SailfishOS.SkiaSharp.dll` is compiled against 2.88.6 and runs on 2.88 and 3.x by assembly
  roll-forward. U-V tests prove it on both lines.
- `Microsoft.Maui.SailfishOS.SkiaSharp.V3.dll` is compiled against 3.116.1 for `ISKGLView`/`SKGLViewHandler`.
  It is loaded only when the app's SkiaSharp is 3.x.
- On 2.88, `SKGLView` (Controls type, no Core interface) gets a handler built on `ISKGLViewController` (G9).

The package's dependency is `SkiaSharp.Views.Maui.Controls >= 2.88.6`, so it never upgrades an app's SkiaSharp.

## 5. Phases

Each phase lists its tasks, what it delivers and its exit criterion. Estimates are working days for one
person, a planning aid only.

### S0 — spikes (de-risk), ~3 d

1. **Raster surface prototype.** `MauiSurfaceItem` in the shim; commit a 1080×1000 RGBA buffer per frame from
   C#; measure commit + upload p50/p95 on the device (`tools/sf trace`); grab a frame and compare it with the
   buffer.
   **Exit:** pixel-exact, and the upload p95 is known. Target: ≤ 4 ms for 1080×1000.
2. **Skia GL on Qt.** `QQuickFramebufferObject` renderer; `GRGlInterface.CreateGles` with Qt's
   `getProcAddress`; `GRContext.CreateGl`; draw a gradient; restore Qt state (`resetOpenGLState`, Skia
   `ResetContext`). Record the device's render loop (threaded/basic) and GLES version.
   **Exit:** a GPU-drawn frame on screen, no scene-graph corruption across 1000 frames, or a documented no-go
   (then G10's raster fallback becomes the `SKGLView` path, and §1 gets an allowed difference for `GRContext`).
3. **Native library matrix.** Load 2.88.6, 2.88.9, 3.116.1 and 3.119.4 on the device (`SKSurface` + `SKTypeface`
   + PNG encode); check that `libGL.so.1` is not needed at load.
   **Exit:** all four load.
4. **Android reference harness.** `SkiaParity` app (§6.3) builds and runs on the Pixel_8_37 emulator and writes a
   trace.
   **Exit:** one scenario trace from Android.

Deliverable: measured numbers written into §7. Go/no-go for GL.

### S1 — framework pieces in `Microsoft.Maui.SailfishOS`, ~5 d

- Native ABI from §4 + P/Invokes; public `QtHostSurface` managed wrapper (create, commit, touch, frame request,
  GL mode).
- **Handler override table:** `SailfishHandlers.Replace(Type libraryHandler, Type sailfishHandler)`, consulted by
  `SailfishHandlersFactory` before a library registration is used. It makes R1 order-independent and is reusable
  for other libraries.
- **Extension auto-registration:** `AssemblyMetadata("Microsoft.Maui.SailfishOS.Extension", …)` read at startup;
  trim-safe (`DynamicDependency` emitted by the package's targets).
- **Image resolver extension point** in `QtHostImages`: `RegisterResolver(Func<ImageSource, string?>)` + async
  generation and `WhenReady`, like `StreamImageSource`.
- **Test injection** of multi-touch; `DiagPng` region compare helper for surfaces.

Exit: U-R1..R8, unit tests for the factory override and resolver; matrix 25/25 still PASS (no regression).

### S2 — `SKCanvasView` render path, ~5 d

`SailfishSKCanvasViewHandler`: buffer management (C1, C2, C5, C6), coalesced invalidation via frame request (C7),
paint triggers (C8, C9), pixel snapping (C4, C11), measure (C13), tiling (C14), lifecycle (C15–C18), errors (C19),
no per-paint allocation (C20).

Exit: U-C*, D-C* and P-C* green; Kitchen "SkiaSharp" page shows every scene.

### S3 — touch, ~4 d

Item touch in the shim (touch + non-synthesized mouse, ungrab → cancel, `keepTouchGrab` only for what Android
keeps). Managed mapping to `SKTouchEventArgs` with Android-style id allocation, `Handled` returned synchronously
(T1–T11). Decision D2 applied (T12).

Exit: U-T*, D-T* and P-T* green, including ScrollView/CollectionView interception and the Silica back gesture.

### S4 — image sources, ~2 d

Resolver for the four `SK*ImageSource` types (PNG into the image cache keyed by source + generation; `SKPicture`
rasterized at `Dimensions`), reload on property change (I1–I6).

Exit: U-I*, D-I* and P-I* green.

### S5 — `SKGLView`, ~5 d (depends on S0.2)

`SailfishSKGLViewHandler` (3.x, `ISKGLView`) and the 2.88 controller-based handler: FBO item, `GRContext`
lifetime and `OnGRContextChanged`, render loop gated on `Window`, `IgnorePixelScaling`, transparency, render-thread
painting, fallback (G1–G10). LiveCharts' `UseGPU = true` gives an `SKGLView` but no other support from us.

Exit: D-G* and P-G* green; 1000-frame soak with no GL error and no leak.

### S6 — packaging, versions, tooling, ~3 d

Package layout (two assemblies, `buildTransitive` targets, registrar metadata), build checks `SF-SKIA001/002`,
`tools/sf doctor` check, local feed (`tools/sf pack-local`, fixed 0.1.0), template note, trimming/AOT (N1–N5).

Exit: V-1..V-4 and B-1..B-5 green.

### S7 — real apps, docs, matrix, ~4 d

- Campaign walkthroughs A-1..A-4 per [`app-test-campaign.md`](app-test-campaign.md), recorded and analysed frame by
  frame; every SkiaSharp bug fixed in the same round.
- New matrix legs `skia`, `skiainput` and `skiagl` in `tools/cmd/matrix.sh`.
- Docs (§9). `BUG_LIST.md` cleaned.

Exit: §10 definition of done.

Total ≈ 30 working days, plus fix rounds found by S7.

## 6. Test plan

### 6.1 Layers

| Prefix | Layer | Where | Runs |
|---|---|---|---|
| U- | Host unit tests: handler, mapper, coordinate and size math, id allocation, coalescing, resolver, registration order; with `FakeShim` (surface/touch/frame-request fakes added) | `tests/Linux.SailfishOS.SkiaSharp.Tests` (new), references both SkiaSharp lines via two test projects or `VersionOverride` | every build, `dotnet test` |
| D- | On-device self-verifying diag legs: build pages, drive input by injection, read back pixels with `grab_png`, print `ACCEPTANCE` | `src/Linux.SailfishOS.SkiaSharp.Diagnostics` (new, opt-in like `Linux.SailfishOS.Diagnostics`), registered in `SailfishKitchen` | `tools/sf matrix skia skiainput skiagl` |
| P- | Differential parity: the same scenario script on the Android emulator and on the phone, traces compared | `tests/SkiaParity` app (Android + Sailfish heads) + comparer | per phase exit, before S7 |
| A- | Real-app walkthroughs, recorded | campaign procedure | S7, and after every fix |
| F- | Performance and memory | device, `tools/sf trace`, `/proc/<pid>/status` | S2, S5, S7 |
| V- | Version matrix | device | S6 |
| B- | Build/tooling | host | S6, CI |

### 6.2 Unit tests (U-)

- **U-R1..R4:** registration in every order. `UseSkiaSharp()` before/after the Sailfish registrar, 2.88
  `UseSkiaSharp(true, …)`, 3.x `UseSkiaSharpHandlers()`. The resolved handler for `SKCanvasView`, a subclass of
  it, and `SKGLView` is always Sailfish's.
- **U-R5:** a registrar named by `AssemblyMetadata` is called once; a missing type logs and continues.
- **U-R6/R7:** library subclasses (`class ChartView : SKCanvasView`, `class H : SKCanvasViewHandler` registered by
  the app) resolve correctly.
- **U-R8:** `SKCanvasViewHandler.SKCanvasViewMapper.AppendToMapping("X", …)` added by an app runs on the Sailfish
  handler.
- **U-R9:** mapper/command key sets equal SkiaSharp's (reflection over `SKCanvasViewMapper`, `SKGLViewMapper` and
  the command mappers), the same way `HandlerParityTests` checks MAUI's mappers.
- **U-C1/C2:** `Info`/`RawInfo`/`ColorType`/`AlphaType` for sizes {1×1, 37×19, 360×200, 1080×2160} dp ×
  densities {1, 1.5, 1.75, 2, 2.5, 3, 4} × `IgnorePixelScaling` {false, true}; canvas matrix = scale(density) when
  ignoring.
- **U-C3/C5/C7/C8:** event sequences of `OnCanvasSizeChanged`/`PaintSurface` for scripted size, visibility and
  invalidate sequences (golden lists taken from the Android trace).
- **U-C4/C13:** pixel rects and measured sizes against MAUI Android's rounding of the same rects.
- **U-C10/T11:** thread affinity asserted.
- **U-C19:** exception in `PaintSurface` reaches the unhandled path.
- **U-T1..T12:** synthetic native touch sequences (single, two fingers with interleaved down/up, cancel mid-move,
  pressed-not-handled, capture outside bounds) → expected `SKTouchEventArgs` lists. The golden lists come from
  Android's `SKTouchHandler` logic with the same input.
- **U-I1..I6:** each source type → PNG with the expected pixels and size; reload on change; null source; cache
  cleanup.
- **U-M1/M2:** conversions and constructors.

### 6.3 Differential parity (P-)

`tests/SkiaParity` is one MAUI app with an Android head and a Sailfish head. Each scenario:

1. builds a page (canvas configuration, parent containers, visibility, density-dependent sizes),
2. runs a script of steps: resize, invalidate ×N, show/hide, navigate away/back, rotate, inject touch,
3. writes a JSON-lines trace:
   - every `PaintSurface` (`Info`, `RawInfo`, canvas matrix, thread),
   - `CanvasSize` changes and every `Touch` (all fields, `Location` also normalized to dp),
   - measured/arranged sizes,
   - a PNG of the managed buffer after each paint.

Input injection happens inside the app on both heads, so platform dispatch applies:

- Android: `MotionEvent.obtain` with pointer properties, dispatched to the activity's window. `ScrollView`
  interception is real.
- Sailfish: `sailfish_host_inject_touch`. Flickable stealing is real.

`tools/analyze/sf-skia-parity.py` compares the two traces after normalization (density, AD1–AD4). A difference
fails the scenario and names the field.

Scenarios: P-C1…C15, P-T1…T10, P-G1/G3/G4 and P-I1/I3. They run on Android densities 2.625 (Pixel 8) and
1.0/3.0 (`wm density`), and on the phone's own density.

### 6.4 Device legs (D-)

- **`skia`** (render + lifecycle; C*, I*). Scenes with known pixels:
  - colour bars with alpha ramps (premul check), 1-px lines at canvas edges (snapping and off-by-one),
  - a canvas over a coloured background with opacity 0.5, rotated/scaled canvases, a clip,
  - a 5000-px-tall canvas in a ScrollView (tiling), a 200-row CollectionView of canvases (recycling),
  - navigate away/back ×20, background/foreground.

  Pixel compare of the screenshot region against the managed buffer: exact for raster, in the axis-aligned
  unscaled cases (C11). For `SKGLView`: mean absolute error ≤ 2/255 and no pixel off by > 32/255 (AD2).
- **`skiainput`** (T*). Injected sequences: single tap, drag, two-finger pinch, pressed-not-handled in a
  ScrollView (the scroll view must scroll), vertical drag in a vertical ScrollView (canvas gets `Cancelled`),
  horizontal drag (canvas keeps it), disabled canvas, InputTransparent canvas, edge back-swipe over a canvas (page
  pops).
- **`skiagl`** (G*). GRContext present, render-loop frame count over 2 s (≥ 50 fps when idle), loop stops off
  window, context loss/restore via app background, forced fallback (`MAUI_SAILFISH_SKIA_GL=0`).

Each leg prints `Qt skia diag: ACCEPTANCE … => OK|FAIL` and gets its marker in `matrix.sh`.

### 6.5 Real apps (A-)

These apps are real-world consumers of `SKCanvasView`. Only what SkiaSharp is responsible for is checked: the
canvas is drawn, sized, repainted and released correctly. Chart interaction is the libraries' business (§3.8).

| ID | App | Walkthrough adds |
|---|---|---|
| A-1 | MoneyFox (draws through LiveCharts rc2, SkiaSharp 2.88.6) | each of the 4 statistics pages: chart drawn and matching the Android screenshot; a period change repaints it |
| A-2 | Profitocracy (draws through LiveCharts rc5.4, SkiaSharp 3.116.1) | Overview cards: charts drawn; switching period/profile repaints |
| A-3 | WeightTracker (draws through Microcharts 2.0.0.3) | graph page: chart drawn and animates; add an entry, graph repaints |
| A-4 | A-1 built trimmed/AOT | the same walkthrough |
| A-5 | GitTrends | re-check whether any SkiaSharp view is used beyond Syncfusion; walk it if unblocked |

Every app also gets an Android-emulator screenshot of the same screens for a side-by-side check (frames cut by
the campaign procedure).

### 6.6 Performance and memory (F-)

- **F-1:** raster paint pipeline (commit + upload) for 360×200, 1080×1000 and 1080×2160 px. Targets: p95 ≤ 1, 4
  and 8 ms. The app's own draw time is excluded and reported separately.
- **F-2:** continuous invalidate loop (an animating chart) on a 1080×800 canvas: ≥ 55 fps, no dropped
  frame from the pipeline when the app draws ≤ 8 ms.
- **F-3:** navigate to a chart page and back ×50. RSS growth ≤ 2 MB after GC; no live `SKSurface`/texture
  above the baseline (diag census).
- **F-4:** image-source cache stays bounded over 500 source changes.
- **F-5:** `SKGLView` loop at 60 Hz for 5 min: no GL error, steady RSS.

Every number is recorded in [`profiling.md`](profiling.md) with the build it came from.

### 6.7 Versions (V-) and build (B-)

- **V-1..V-4:** the `skia` leg on 2.88.6, 2.88.9, 3.116.1 and 3.119.4 (managed = native).
- **V-5:** armv7hl if a device exists.
- **B-1/B-2:** a missing native asset and a version mismatch each give the documented error code.
- **B-3:** a fresh `dotnet new maui-sailfish` + `SkiaSharp.Views.Maui.Controls` + the package renders with no code
  change.
- **B-4:** armv7hl build.
- **B-5:** trimmed/AOT build has no IL2026/IL3050 from the package and no runtime miss.

## 7. Risks and spikes

| Risk | Impact | Mitigation |
|---|---|---|
| Skia GL on Qt 5.6/libhybris GLES fails or corrupts scene-graph state | G1–G5 | S0.2 first; state reset on both sides; G10 raster fallback; if no-go, §1 gets one recorded allowed difference (`GRContext` null) |
| Upload too slow for full-screen animation | F-2 | persistent texture + `glTexSubImage2D`; dirty-rect upload if needed (Skia gives no dirty rect, so a row-hash diff is a later option) |
| Fractional item positions blur the texture | C11 | snap to device pixels in the item; test at fractional densities |
| Flickable steals differ from Android's interception | T6 | differential tests decide; tune `keepTouchGrab`/filtering per direction, not per app |
| Synchronous managed callback re-enters the renderer during Qt event delivery | T11 | callback only creates args and runs app code; host-tree changes requested from it go through the normal loop-turn path |
| 2.88/3.x assembly roll-forward breaks on an API the package uses | N1 | U-tests run against both lines; the package uses only members present in both |

Measured S0 numbers go here once taken.

## 8. Decisions

- **D1 — packaging:** one package with a 2.88-compiled main assembly and a 3.x light-up assembly (§4).
  Alternative: two packages, one per SkiaSharp line. Recommended: one package, since apps never choose.
- **D2 — mouse, hover, wheel (T12):** Android's `SKTouchHandler` sends no `Entered`/`Exited`/`WheelChanged`. Only
  needs a choice if a mouse is connected to the phone. Recommended: follow Android exactly (mouse press/move/release
  as `DeviceType = Mouse`, no hover/wheel events). Alternative: also send hover/wheel as WinUI does. **Open:
  confirm.**
- **D3 — `SKGLView` thread:** paint on the render thread, as Android's GL thread (G5). Apps written for Android
  already cope with it.

## 9. Documentation to update

- [`porting-existing-apps.md`](porting-existing-apps.md): replace "SkiaSharp views — not supported" and the native
  asset note with the package instructions and error codes.
- [`architecture.md`](architecture.md): the raster-upload exception and the generic surface ABI.
- [`custom-controls.md`](custom-controls.md): `QtHostSurface` for library authors; the handler override table.
- [`handler-parity.md`](handler-parity.md): SkiaSharp mapper keys.
- [`app-test-campaign.md`](app-test-campaign.md): close the SkiaSharp open question; update known gaps of
  apps 1–3.
- [`parity-plan.md`](parity-plan.md): link this plan under "What is left".
- New `docs/skiasharp.md` (user guide: install, supported versions, `SKGLView`, what libraries on SkiaSharp must do themselves, troubleshooting).

## 10. Definition of done

- Every row in §3 has its tests green; P- traces show no difference beyond AD1–AD4.
- `tools/sf matrix` all legs PASS including `skia`, `skiainput` and `skiagl`.
- A-1..A-4 recorded clean (A-5 per its re-check).
- F- targets met and recorded; V- and B- green.
- No open SkiaSharp entry in `BUG_LIST.md`; docs in §9 updated.
