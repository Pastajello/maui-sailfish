# SkiaSharp on Sailfish OS — execution and test plan

Status: **in progress** (2026-10-02). Answers the open question in
[`app-test-campaign.md`](app-test-campaign.md#open-questions): SkiaSharp views get Sailfish support as thin platform
handlers. Nothing of Skia itself is written here.

## 1. Scope and responsibility

**Who owns what.** `SkiaSharp.Views.Maui.Controls`/`.Core` are published by the SkiaSharp project (`mono/SkiaSharp`),
not by MAUI. For every platform it supports, SkiaSharp ships the platform handler in its own package, as one asset per
TFM: Android, iOS, MacCatalyst, Windows, and in 2.88 also Tizen, which Samsung contributed upstream. Plain `net` gets a
stub handler that throws, meaning "unknown platform". Sailfish is a TFM of our own workload (`net11.0-sailfish`), so
SkiaSharp cannot ship an asset for it today.

The work is split along that line:

| Layer | Owner | Android counterpart |
|---|---|---|
| Drawing (Skia, `libSkiaSharp.so`) | SkiaSharp; it already works on the device | the same |
| Platform primitives: a surface that shows managed pixels in the Qt scene, touch delivery with a synchronous `Handled`, a frame callback | **us**, in `Microsoft.Maui.SailfishOS` (generic, no Skia in it) | `View`, `Bitmap`, `MotionEvent`, `Choreographer` |
| Thin glue: `ISKCanvasView`/`ISKGLView` handlers, `SKImageSourceService` on those primitives | SkiaSharp by convention; **written by us for now** as `Microsoft.Maui.SailfishOS.SkiaSharp`, shaped like SkiaSharp's own platform views so it can go upstream | `SkiaSharp.Views.Android` + its MAUI handlers |

**Behaviour reference.** SkiaSharp's Android handlers and views (`SKCanvasViewHandler`, `SKGLViewHandler`,
`SKTouchHandler`, `SkiaSharp.Views.Android.SKCanvasView`/`SurfaceFactory`, `SKImageSourceService`). They serve as the
spec, read from the 3.119 packages. The glue does what they do, no more.

**Not in scope.**
- Skia itself.
- Libraries built on SkiaSharp (LiveCharts, Microcharts, SkiaSharp.Extended, Svg.Skia, …): they bring their own
  platform support. LiveCharts' input is a known case, since its plain-`net` `PointerController`/`ChartBehaviour` are
  stubs.
- SkiaSharp 2.88. It is the MAUI 6–8 line, and its plain-`net` asset has no `SKGLView` handler. MAUI 10/11 apps use
  3.x. A 2.88 app (MoneyFox via LiveCharts rc2) keeps the empty-view fallback.
- Build-time checks of native assets, a Sailfish `GRContext` (see 3.4), armv7hl runtime tests, and a differential
  Android test harness.

## 2. Current state (2026-10-02)

- `libSkiaSharp.so` from `SkiaSharp.NativeAssets.Linux` (glibc, linux-arm64) loads and draws on the device in the
  version the app resolves (MoneyFox, Profitocracy: [`porting-existing-apps.md`](porting-existing-apps.md)). Its GL
  interface is GLX-only (`libGL.so.1`/`glXGetProcAddress`), which Sailfish (EGL/GLES) does not have.
- The plain-`net` `SKCanvasViewHandler`/`SKGLViewHandler` throw in `CreatePlatformView` and have empty mappers.
  Sailfish falls back to an empty container, so the area stays white. `UseSkiaSharp()` (3.x) registers both
  handlers and an empty `SKImageSourceService` for the four `SK*ImageSource` types. `QtHostImages.Resolve` does not
  know those types, so they show nothing.
- Adapter events reach C# asynchronously; the input router forwards the first touch point only. Android's `Handled`
  needs a synchronous path, hence the touch primitive.
- **Done (2026-10-02, uncommitted):** P1–P5. Matrix 29/29 PASS after P1; the new legs `skia` (11 checks) and
  `skiainput` (5) PASS on `samples/SkiaSharpProbe`. A-1 Profitocracy: the LiveCharts charts draw, the pie charts
  with data. A-2 WeightTracker: the Microcharts graph draws. All device runs used the default trimmed + R2R payload,
  so the registrar survives trimming. Docs from §6 are updated.
  - Shim: `MauiSurfaceItem`, the surface ABI (`surface_commit`, `surface_set_touch` + touch callback,
    `request_frame`/frame callback in `afterAnimating`, `inject_touch`), and surface counters in `perf_stats`.
  - Core: `QtHostSurface`, the generic `surface` adapter, `NativeElementHost.Attached`,
    `SailfishHandlersFactory.ReplaceLibraryHandler`, `SailfishExtensions` (registrar metadata) and
    `QtHostImageSources`.
  - Package: `SailfishSKCanvasViewHandler` + `SkiaSurfaceRenderer` + `SkiaTouch`, the registrar, and
    buildTransitive targets; `tools/sf pack-local` packs it.
  - P4: `SailfishSKGLViewHandler` (raster, `GRContext` null, render loop) and `SkiaImageSources` (PNG per source,
    rewritten when the source object changes; pictures are drawn onto a raster surface, since
    `SKImage.FromPicture` segfaults in SkiaSharp 3.116).
  - Tests: 244 host tests green. The device probe (`SkiaProbe`, scratch app, prototype of D-1/D-2) passes 15/15 in
    portrait. A run with the phone in landscape relaid the page for the landscape width and the canvases repainted
    at that size, so the portrait pixel checks need the probe locked to portrait.
- **Measured on the device** (Xperia, density 1.911, `QSGThreadedRenderLoop`, `GL_MAX_TEXTURE_SIZE` 16383):
  - pixels on screen equal the bitmap exactly (0 of 131 790 differ);
  - a 993×764 px canvas invalidated every frame paints at about 88 fps;
  - commit (staging copy) averages 1.1 ms and upload (`glTexSubImage2D`) 2.1 ms, with a 20 ms peak on the first
    upload of a size.
- **Learned on the device:**
  - `afterAnimating` must be connected on the current `QQuickView`. Silica's window changes after the first
    attach, so the frame hook follows it.
  - A texture needs its GL id before the first `bind()`. The scene-graph renderer batches materials that compare
    equal by `textureId()`, so surfaces first committed in the same frame drew one texture.
  - The surface shows the bitmap 1:1 from the host's corner instead of stretching it. The host item's own pixel
    rounding can differ by one from MAUI Android's, and stretching doubled a column.
  - Qt 5.6 sends no ungrab when a Flickable ancestor takes a drag. The item watches the ancestors' `dragging`
    NOTIFY and cancels, as Android's interception does.

## 3. Requirements

Each row names its tests (§5).

### 3.1 Registration

| ID | Requirement | Tests |
|---|---|---|
| R1 | With the package referenced, `UseSkiaSharp()` alone makes `SKCanvasView`/`SKGLView` render, whatever order the app registers things in. The Sailfish handler replaces SkiaSharp's plain-`net` one | U-R1 |
| R2 | Subclasses of `SKCanvasView` (library chart views) resolve to the Sailfish handler | U-R2 |
| R3 | Mapper and command keys cover SkiaSharp's (`EnableTouchEvents`, `IgnorePixelScaling`, `HasRenderLoop` for GL, `InvalidateSurface`, + `ViewMapper`). Known limit: an `AppendToMapping` on SkiaSharp's own `SKCanvasViewMapper` targets its handler type and does not run on Sailfish; apps extend `SailfishSKCanvasViewHandler.Mapper` instead | U-R3 |

Mechanism: the package's `buildTransitive` targets add an `AssemblyMetadata` naming its registrar, which the backend
calls at startup (the pattern `SailfishMauiApplication` already uses for the application id). It registers the
handlers after the app's builder configuration. If that cannot be made order-independent with plain
`ConfigureMauiHandlers`, a minimal "replace library handler" hook goes into `SailfishHandlersFactory`.

### 3.2 `SKCanvasView`

The glue follows SkiaSharp's Android code line by line:

| ID | Requirement (Android) | Tests |
|---|---|---|
| C1 | `Info` = view size in px, `Rgba8888`/`Premul`; `RawInfo` the same | U-C1, D-1 |
| C2 | `IgnorePixelScaling`: `Info` = `((int)(w/density), (int)(h/density))`, `RawInfo` px, canvas `Scale(density)` + `Save()` | U-C1, D-1 |
| C3 | `OnCanvasSizeChanged` only when the size changes | U-C2 |
| C4 | 0 width/height: no paint, pixels freed | U-C2 |
| C5 | Buffer reused between paints at the same size (old content stays) | U-C2 |
| C6 | `InvalidateSurface` → one paint in the next frame, however many calls | U-C3, D-1 |
| C7 | Paints on: attach, resize, `IgnorePixelScaling` change, invalidate. Not while `IsVisible = false` (pixels freed) | U-C3, D-1 |
| C8 | `PaintSurface` on the UI thread; an exception is logged as `[ERROR]` and the loop goes on (backend policy, `SailfishDispatcher.DrainQueue`) | U-C4 |
| C9 | Pixels on screen 1:1 with the buffer (colour, premultiplied alpha over what is behind) | D-1 |
| C10 | Generic view state works (opacity, background under the canvas, clip, transforms, z-order, `IsEnabled`, `InputTransparent`), via the generic host | D-1 |
| C11 | Canvas larger than the GPU texture limit renders (tiles) | D-1 |
| C12 | Navigation away/back and collection recycling: the new QML object is painted again, nothing leaks | D-1 |

### 3.3 Touch (`EnableTouchEvents`)

Per Android's `SKTouchHandler`:

| ID | Requirement | Tests |
|---|---|---|
| T1 | Off by default; when off, input goes to the parent | U-T1, D-2 |
| T2 | `Pressed`/`Moved`/`Released`/`Cancelled`, `InContact`, per-pointer `Id` (small ints like Android), one `Moved` per active pointer | U-T1, D-2 |
| T3 | `Location` in px, in dp with `IgnorePixelScaling` | U-T1, D-2 |
| T4 | First `Pressed` not handled → the rest of the gesture goes to the parent (ScrollView scrolls) | U-T1, D-2 |
| T5 | A parent flickable taking the drag → `Cancelled` | D-2 |
| T6 | `IsEnabled = false` → no events | D-2 |
| T7 | `DeviceType = Touch`, `MouseButton = Left`; no hover/wheel events (Android sends none) | U-T1 |

### 3.4 `SKGLView`

The thin variant uses the raster surface:
- `PaintSurface` gets a raster `SKSurface`, `GRContext = null` and an empty `BackendRenderTarget`.
- `HasRenderLoop` repaints every frame while the view is in a window; `IgnorePixelScaling` works as in C2; touch as
  in 3.3.

Apps that only draw through `e.Surface.Canvas` (the common case, e.g. LiveCharts' `UseGPU`) work unchanged. A real
`GRContext` would need Skia's GL interface built on Qt's GLES context (`GRGlInterface.CreateGles`) and a
`QQuickFramebufferObject` primitive. That is a separate decision, made only when an app needs it. Tests: U-G1, D-1.

### 3.5 Image sources

`SKImageImageSource`, `SKBitmapImageSource`, `SKPixmapImageSource` and `SKPictureImageSource` (rasterized at
`Dimensions`) display wherever MAUI takes an `ImageSource`. They are encoded once to PNG in the image cache and
reloaded when their property changes. The core gets a small resolver hook in `QtHostImages`. Tests: U-I1, D-1.

## 4. Phases

| Phase | Work | Exit |
|---|---|---|
| **P1 — primitives** (~3 d) | Surface + frame callback (done natively, managed `QtHostSurface` left); touch primitive in the shim (item touch/mouse events → synchronous callback with `handled`, ungrab → cancel) + multi-touch test injection; image resolver hook; registrar metadata hook | host tests for the primitives; matrix 25/25 still PASS |
| **P2 — SKCanvasView** (~3 d) | `SailfishSKCanvasViewHandler` (C1–C12) in a new `src/Linux.SailfishOS.SkiaSharp`, compiled against SkiaSharp 3.116.1 (lowest 3.x the apps use), dependency `SkiaSharp.Views.Maui.Controls >= 3.116.1` | U-R*, U-C*, D-1 |
| **P3 — touch** (~2 d) | Touch mapping (T1–T7) | U-T1, D-2 |
| **P4 — SKGLView + image sources** (~2 d) | 3.4, 3.5 | U-G1, U-I1, D-1 |
| **P5 — apps + docs** (~2 d) | Profitocracy (3.116.1) and WeightTracker (Microcharts 2.0.0.3 on 3.119.4): charts drawn; docs; matrix leg | A-1, A-2; §6 |

Total ≈ 12 working days.

## 5. Tests

- **Host unit tests (U-),** in `tests/Linux.SailfishOS.SkiaSharp.Tests` with `FakeShim` (surface commits and frames
  recorded; touch callbacks driven):
  - U-R1..R3: registration in every order, subclass resolution, mapper keys vs SkiaSharp's by reflection.
  - U-C1: `Info`/`RawInfo`/matrix for sizes × densities {1, 1.5, 1.75, 2, 2.5, 3} × `IgnorePixelScaling`, expected
    values computed with Android's formulas.
  - U-C2..C4: size-change, empty-size, buffer-reuse, coalescing, visibility and exception sequences.
  - U-T1: scripted native touch sequences (one and two fingers, not handled, cancel) → expected `SKTouchEventArgs`.
  - U-G1: render loop on/off with and without a window.
  - U-I1: each image source → PNG pixels/size; reload on change; null source.
- **Device leg D-1 `skia`** (render). Known-pixel scenes:
  - colour/alpha bars, 1-px edge lines, a canvas over a background with opacity, a rotated canvas,
  - a 5000-px-tall canvas in a ScrollView, a CollectionView of canvases, navigate away/back ×20,
  - an `SKGLView` with the render loop, image sources in `Image`.

  Each scene compares the screenshot region with the managed buffer (exact for unscaled raster).
- **Device leg D-2 `skiainput`** (touch). Injected:
  - tap and drag,
  - two fingers,
  - not-handled in a ScrollView (it scrolls),
  - vertical drag in a vertical ScrollView (`Cancelled`),
  - a disabled canvas.
- Both legs print `Qt skia diag: ACCEPTANCE … => OK|FAIL` and run from `tools/sf matrix skia skiainput`. Their code
  lives in an opt-in `Linux.SailfishOS.SkiaSharp.Diagnostics`, registered in `SailfishKitchen`, so the core
  diagnostics stay free of SkiaSharp.
- **Apps (A-):** campaign walkthroughs, render only; chart interaction is the libraries' business.
  - A-1 Profitocracy Overview: charts drawn, repaint on period change.
  - A-2 WeightTracker graph: drawn and animating, repaint after adding an entry.
- **Measured once** and recorded in [`profiling.md`](profiling.md): commit + upload time for 1080×1000 px (from
  `perf_stats` `surfaceCommitUs`/`surfaceUploadUs`) and frame rate of a continuously invalidated canvas.

## 6. Documentation

- [`porting-existing-apps.md`](porting-existing-apps.md): SkiaSharp views supported (3.x) through the package, the
  `SkiaSharp.NativeAssets.Linux` requirement, `SKGLView` without `GRContext`, 2.88 not supported.
- [`architecture.md`](architecture.md): the raster surface as the one place a CPU-drawn buffer is uploaded (only
  that view's area, once per paint, never read back), and the ownership split from §1.
- [`custom-controls.md`](custom-controls.md): `QtHostSurface` and the touch primitive for library authors.
- [`app-test-campaign.md`](app-test-campaign.md): close the SkiaSharp open question.

## 7. Definition of done

U- and D- tests green; `tools/sf matrix` all legs PASS including `skia` and `skiainput`; A-1 and A-2 clean; §6
updated.
