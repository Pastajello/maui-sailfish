# MAUI.Sailfish → parity with MAUI.Android / MAUI.iOS — roadmap

The only current work plan (refreshed 2026-09-28). Phases F0–F5 below record what has been done since
the 2026-09-24 audit; what is left is in the [What is left](#what-is-left) section. Defects:
[`../BUG_LIST.md`](../BUG_LIST.md). Architecture principles: [`architecture.md`](architecture.md). The move to a
handler model aligned with MAUI net11 (stages A0–A7) is done except for a single-window renderer (A6).

## Status (2026-09-28)

- **On-device matrix:** `tools/sf matrix` 25/25 legs PASS on 2026-09-25 (31 legs today, `tools/cmd/matrix.sh`),
  every verdict from the leg's own marker.
- **Handler parity:** 1073/1073 keys of the official MAUI mappers (100%, since 2026-09-28,
  [`handler-parity.md`](handler-parity.md), measured on remapped mappers). `Border` (obsolete
  `IBorder`), `ContainerView` and `ToolTip` do not apply to Sailfish.
- **Works natively:** Shell, TabbedPage, FlyoutPage, CarouselView, SwipeView, IndicatorView, Stepper,
  CheckBox, WebView (Gecko), app fonts; Essentials on Sailfish services (F4); cover, Harbour,
  `dotnet new maui-sailfish` = MAUI template with a Sailfish head (F5).
- **Tools:** `tools/sf-*.sh` scripts (deploy, verify, run, screenshot, matrix, F5 DEBUG in `.vscode/`)
  and the VS Code extension **MAUI Sailfish Tools** in a separate repo:
  <https://github.com/Pastajello-Organization/sailfishos_maui_tools> (devices, SSH pairing, F5/Ctrl+F5
  with debugger, Debug | Release switch).

## Plan

### F0 — honest baseline — DONE 2026-09-24 (matrix 15/18 from own verdicts; since 2026-09-25 25/25)
- [x] Matrix verdict only from the given leg's marker; no marker = NO-VERDICT.
- [x] Legs `text`/`input`/`geometry` reach their own verdict (geometry
  exposed a scroll mapping bug in the shim — fixed).
- [x] "Leak" 24→35 (stress/perf) — false alarm (back cache), census fixed.
- [x] `devel-su` password kept out of argv and logs (remote command via ssh stdin).
- [x] README: real matrix status and description of `connect.info`.
- [x] Diagnostic legs moved from `SailfishMauiApplication` to a separate assembly
  (`src/Linux.SailfishOS.Diagnostics`; core 5.3k → ~390 lines).

### F1 — architecture (4–6 wks) — DONE 2026-09-25 (F1.1–F1.5, last F1.3b: main ScrollView on its own flickable)
- Hierarchical QML host tree (child inside parent) instead of a flat
  canvas: clip, nested/horizontal ScrollView, subtree transforms;
  removal of `MaxItems`.
- Handlers with a per-property mapper + parity test: keys of our mappers
  vs `ButtonHandler.Mapper` etc. from MAUI.
- Unsupported control → warning in the log instead of disappearing.

### F2 — navigation (3–4 wks) — DONE 2026-09-24
- Shell: current section stack → native pageStack, `GoToAsync` routing,
  flyout as the pulley menu, tabs as a bar under the header + swipe.
- TabbedPage (bar tabs), FlyoutPage (flyout as a pushed page).
- Open: animation/preview on tab swipe (SlideshowView with several
  pages at once).

### F3 — controls (4–6 wks) — DONE 2026-09-25 (F3.1–F3.9, leg `f3`)
- [x] CarouselView: ListView in page mode (snap, peek, two-way Position/CurrentItem);
  `Loop=true` (the MAUI default) on a looping PathView
  (`containers/CarouselView.qml`, same bridge contract).
- [x] IndicatorView (dots, tap → page), horizontal CollectionView.
- [x] Stepper (minus/plus), a real CheckBox (frame + checkmark).
- [x] SwipeView (Left/Right, Invoked, Open/Close; Top/Bottom and SwipeItemView
  → warning).
- [x] WebView on Sailfish.WebView (Gecko): HTML/URL, Navigating/Navigated,
  EvaluateJavaScriptAsync, GoBack/GoForward/Reload.
- [x] App fonts (IFontRegistrar → QFontDatabase), FontImageSource
  (glyph rendered to PNG in the shim), StreamImageSource (file in cache).
- [x] Header/Footer in horizontal lists (along the axis, width from measure).
- [x] Text mappers (F3.7): Button CharacterSpacing, Label Padding,
  RadioButton (font/color/spacing/stroke), Picker/DatePicker/TimePicker
  (TextColor, TitleColor, font, CharacterSpacing, Format).
- [x] Strokes (F3.8): Border — dashed, per-corner, any StrokeShape,
  gradient, cap/join/miter on the adapter's Canvas; dashes cut in JS (Qt 5.6
  has no `setLineDash`) — also works for Shape and GraphicsView; ImageButton
  (stroke, radius, Padding, Background).
- [x] F3.9 mappers (parity 89% then, today counted differently — 86%, [`handler-parity.md`](handler-parity.md)):
  generic Background for Silica controls (rectangle in the shim), Border Shadow,
  SemanticProperties → Accessible.*, AutomationId; ReturnType (Silica
  EnterKey), ClearButtonVisibility, Editor MaxLength, IsSpellCheckEnabled,
  SearchBar = parity with Entry; picker IsOpen, ScrollBarVisibility,
  IsRefreshEnabled/RefreshColor, WebView UserAgent, SwipeTransitionMode,
  animated GIFs (IsAnimationPlaying).
- [x] Animated ActivityIndicator (was: custom RotationAnimation, leg `tree`).
- [x] Shadow and Clip with arbitrary geometry on EVERY control (2026-09-26): the host's `layer.effect` = `qml/effects/MauiLayerEffect.qml`
  (OpacityMask with a mask drawn by Canvas from `GeometryOps` + DropShadow from the content's
  alpha, shadow of the clipped content as in MAUI); the effect follows geometry,
  transform, z and parent by itself. Leg f3 part M: layer, window pixels
  (ellipse cut out of a square, shadow next to the box), layer release.
- [x] Last mapper keys (F3.10, 2026-09-28, parity 86% → 100%, leg f3 part N):
  HeadingLevel → `Accessible.role` Heading, IsInAccessibleTree/ExcludedWithChildren → `Accessible.ignored`
  (exclusion computed from ancestors); Label TextType=Html (RichText) and TextTransform; VerticalTextAlignment
  of Entry/Editor/SearchBar (text margin in a taller field) and Picker Horizontal/VerticalTextAlignment — only
  when the app sets them, the default Silica layout stays; Slider.ThumbImageSource (image in place of the handle);
  Page.BackgroundImageSource (image under the page content, cropped to fill); FlowDirection RTL on
  leaf controls — the shim sets `LayoutMirroring` explicitly on every host (leaves mirror, the rest
  do not, so inheritance does not pass through MAUI hosts), Slider and ProgressBar additionally reverse the value
  axis.

### F4 — platform and Essentials — DONE 2026-09-25 (leg `f4`)
- [x] Static Essentials APIs (`Preferences.Default`, `DeviceInfo.Current`,
  `Clipboard.Default`, …) hit the Sailfish implementations through internal
  SetDefault/SetCurrent hooks — without attaching an Application handler (that broke
  navigation) and without portable exceptions; stores on JSON with source-gen.
- [x] Platform services channel (QML objects on the window + an app queue
  drained by the shim together with the page queue).
- [x] AppTheme from `Theme.colorScheme` + ambience change → `ThemeChanged`.
- [x] DeviceInfo (/etc/hw-release, /etc/sailfish-release), DeviceDisplay
  (+ KeepScreenOn via Nemo.KeepAlive), Battery (Nemo.Mce), Connectivity
  (Connman), Vibration/HapticFeedback (QtFeedback).
- [x] Sensors (QtSensors/sensorfw, MAUI units), Geolocation (QtPositioning).
- [x] Share (Sailfish.Share), MediaPicker/FilePicker (Sailfish.Pickers),
  Permissions per Sailjail, PhoneDialer/Email/Sms/Map (URI), Screenshot,
  `SailfishNotifications` (Nemo.Notifications — MAUI has no API of its own).
- [x] Packaging: `SailfishPermissions` lost everything after the first
  permission; the sandbox gets OrganizationName/ApplicationName.
- [x] SecureStorage on Sailfish Secrets (2026-09-25): a separate native bridge
  `libsailfishsecretsbridge.so` (Secrets C++ API, loaded dynamically —
  the shim does not depend on Secrets, which are not preinstalled); an app
  collection DeviceLock/KeepUnlocked/OwnerOnly in sqlcipher, the key is held by the
  daemon. `<SailfishSecureStorage>Secrets</SailfishSecureStorage>` adds
  Requires on the daemon + the Secrets permission (Harbour validator: PASSED). Without
  the daemon — a file (obfuscation only) with a warning in the log; once
  Secrets appears, entries from the file migrate, and the file and key disappear. On the device:
  f4 with the `secrets` backend (dev and Harbour via launcher) and `file`.
- [x] Flashlight, Contacts, AppActions (cover actions), WebAuthenticator (browser + URL scheme), and
  FeatureNotSupportedException for TextToSpeech, Geocoding and Passkeys (2026-10-02; unit tests, checked by hand
  on the phone).
- Platform limit: Contacts sees only qtcontacts-sqlite's non-privileged store. The user's address book is privileged
  data, which Sailfish OS opens only to system apps (`Privileged` + `mapplauncherd` `privileges.d`); Sailjail's
  `Contacts` permission is not enough. Documented, not worked around.
- Deliberately open: camera photos (Sailfish has no in-app capture API — IsCaptureSupported false); no speech
  engine or geocoder on the platform.

### F5 — quality and release (ongoing) — IN PROGRESS
- [x] Renderer tests on the host: `QtHostRuntime.TestShim` + `FakeShim`
  (objects from applyMauiOps, property batches, geometry, page stack) —
  the real renderer/handlers/layout without Qt (`tests/…/Renderer`).
- [ ] Self-hosted CI runner with a phone: not in the repo (no `.github/` is tracked); the matrix runs from a
  workstation with `tools/sf matrix`.
- [x] armv7hl shim (`SF_ARCH=armv7hl`): sysroot from the Jolla repo, zig build,
  `.armv7hl` RPM with `-r linux-arm` — built, NOT tested (no device).
  x86: the SFOS 5 emulator is i486, and .NET does not run on 32-bit x86 Linux — no point.
- [x] Harbour validator compliance — `-p:SailfishHarbour=true`: native
  launcher (hostfxr, `main()` export, rpath `$ORIGIN/../share/PKG/lib`),
  payload in `lib/`, no createdump/lttng, Icon by name, silica-qt5,
  sandbox + Sailjail directories, no Vendor, root:root/0644. Validator: exit 0
  (warnings: icons 86/128/172, unstripped shim). On the device the legs
  page/f3/f4 green via launcher. Launch from the app grid (tap,
  2026-09-26): invoker + Sailjail/firejail (template org.maui/harbour-sample,
  NoNewPrivs, own mount namespace), cover on the home screen
  ([`sailfishos-packaging.md`](sailfishos-packaging.md)).
- [x] Icons 86/108/128/172 from `MauiIcon` (MSBuild task in pure C#:
  PNG decoder/encoder + tent filter on premultiplied alpha; source
  square, upscaling with a warning) and shim strip (3.3 MB → 387 KB,
  `SF_NATIVE_KEEP_SYMBOLS=1` keeps symbols): Harbour validator PASSED without
  warnings.
- [x] Cover (2026-09-26): `SailfishCover.SetContent/SetActions` (title, up to
  3 lines, up to 2 actions with callbacks, `IsActive`/`ActiveChanged`) and
  `<SailfishCoverQml>` (the author's own QML element gets `mauiCoverData`);
  the sample shows task progress + an "add" action. Leg f4 part H.
- [x] DX: `dotnet new maui-sailfish` = the official MAUI 11 template (XAML
  App/AppShell/MainPage, `Resources/`, `Platforms/` Android/iOS/MacCatalyst/
  Windows) + a `net11.0-sailfish` head with `Platforms/SailfishOS/Program.cs`;
  `--sailfish-only` without MAUI workloads. Under the hood: `Platforms/SailfishOS/`
  registered via the MAUI hook for external backends
  (`MauiPlatformSpecificFolder`), MauiImage/MauiIcon via the MAUI resizetizer
  (SVG → PNG, icon with foreground and color → Harbour set), fonts from
  `ConfigureFonts` with plain `UseMauiApp`, Shell page title from
  `ShellContent`, `SailfishRun` with `-f`, no nested restore with RID in
  multi-TFM projects. On the device: generated project via
  `SailfishRun` (screenshot `docs/screenshots/template-maui-sailfish.png`); the full
  variant builds Android and Sailfish from one project.
- Subset of MAUI DeviceTests: the matrix legs (25, ~600 checks) fill this role
  on the device; porting the DeviceTests runners (Android/iOS) — not worth it.

### Performance — 2026-09-25
- [x] Event-driven loop (tick only after wake: work, timer, QML
  event, 1 s heartbeat) instead of 62 Hz; QML drain via a counter NOTIFY.
- [x] Dead geometry readback (66 calls/s) removed; list row refresh
  only after layout / row change.
- [x] TieredPGO off by default: idle CPU 18% → 11% (A/B on the device, no
  startup cost); `SailfishTieredCompilation=false` → 3–6% (+150–250 ms startup).

## What is left

As of 2026-09-28, checked against the code and git history; Navigation, Platform and Performance updated 2026-10-02. Smaller defects and platform gaps
(`OpenUrl`, MCE, `OpenFileRequest`) are in
[`../BUG_LIST.md`](../BUG_LIST.md).

**Navigation**
- Tab swipe: since 2026-10-02 the page follows the finger with the next tab's title in the uncovered strip, and a
  committed swipe slides the page out and the new tab in (leg `shell` F3, screenshot `shell-tab-drag`). Not done: the
  next tab's real content during the drag (Silica's SlideshowView with several pages at once). Qt 5.6 `grabToImage`
  cannot crop to the viewport, so not even a snapshot of it.
- One window per app, as the platform has it (lipstick shows one Silica ApplicationWindow). Decided 2026-10-02:
  `Application.OpenWindow` is dropped with a warning (iOS without multiple scenes does the same), and `CloseWindow`
  on the app's window quits.

**Performance** (page-load acceptance: push animation starts ≤ 150 ms after the tap with complete content,
no empty frames, pop reveals a ready page even 2+ levels back; check with `tools/sf record … --keep DIR` +
`tools/sf page-load DIR --max-settled 700`)
- Going back 2+ levels: re-measured 2026-10-02 with the page cache (A5): a two-level pop shows the cached page in
  37 ms to Appearing, nothing created (before the cache: 847 ms).
- Adapter warm-up: done 2026-10-02. One shared component cache in the window (each page used to keep its own), and
  a second after the first page the shell loads every visual adapter in the background and makes one throwaway
  instance of each (`MauiShell.adapterPreload`; `MAUI_SAILFISH_ADAPTER_PRELOAD=0` turns it off). The first instance of
  a kind is the cost, not the compile: a first Controls push 54–61 → 44–45 ms, the Kitchen detail 183–203 →
  166–170 ms. A page pushed within the first second (the matrix's startup tap) still pays it.
- Canvases off the GUI thread: measured 2026-10-02 and dropped. `renderStrategy: Canvas.Threaded` on Shape, Border and
  GraphicsView made paints slower (shapes/visual/tree legs: 11 paints over 15 ms, 1598 ms in all, against 6 and
  219 ms with `Immediate`): the paint is JavaScript on the GUI thread either way, and the hand-over costs more.
- Page-load guard: leg `navback` fails when a push takes longer to Appearing than twice today's time (first
  Statistics 300 ms, Controls 150 ms, Statistics again 120 ms). Every hub page of leg `features` reaches Appearing in
  29–104 ms, Statistics (pushed in the first second) 170 ms.
- MAUI animations: since 2026-10-03 they tick on Qt's frame clock (`SailfishFrameTicker`, MAUI's
  `IAnimationManager` answered by the service overlay): one tick per frame the panel shows (~85–90 Hz on the
  Jolla phone, leg `visual` G), on the Qt thread, none when nothing animates; a transform outside a list row takes the
  geometry pass only (`SailfishViewMapper.MapTransform`). The values are still computed by MAUI in managed code each
  frame, since app code may observe them; a QML NumberAnimation path is not planned.
- CollectionView row pool: done 2026-10-02 (F4b as recycling, RecyclerView's view holders). Qt 5.6 destroys a
  delegate that scrolls out; its row subtree now outlives it (canvas-owned), waits in a per-list pool, and the next
  row of the same shape takes it over (new ids, only the differences pushed; `MAUI_SAILFISH_ROW_POOL=0` turns it
  off). Kitchen tour: 920 → 690 hosts created, QML ops 920 → 740 ms. A first screen of rows still creates every host;
  one composite QML component per DataTemplate was measured for that and dropped (leg `adapterbench`: a Kitchen card as
  one composite 1583 µs against five createObject calls 1667 µs, −5%): the cost is the adapters' own instantiation.
- Opening a page with a list: done 2026-10-03 (owner decision 3c). A list estimated taller than its viewport (item
  count, span, a grid cell as tall as wide, at least 40 dp a row) builds its first rows after the page's first frame,
  at most 200 ms later (`QtHostListAdapter.FirstFrame.cs`, `MAUI_SAILFISH_LIST_FIRST_FRAME=0` turns it off). Kitchen
  Beef catalog push stall 164–179 → 136–158 ms. What is left is the app's push work and the page creation in one
  block (75 + 67 ms).

**Platform**
- Per-page orientation: done 2026-10-02, `SailfishPage.AllowedOrientations` (Silica `Page.allowedOrientations`,
  [`sailfish-apis.md`](sailfish-apis.md#sailfishpage)); host test only, not turned on the phone yet.
- Touch: since 2026-10-02 the second finger reaches the router (shim pointer kind 7) and drives
  `PinchGestureRecognizer` (leg `visual` B2, two injected fingers, screenshot `visual-pinch`). Still first-point only
  for pan/swipe/tap. Wheel is not routed: Silica's flickables take it natively and the phone has none.
- 3D transforms and non-uniform scale: since 2026-10-02 a QML Matrix4x4 on the host (`QtHostVisualState.HostMatrix`,
  Android's camera distance), exact for shear too (leg `visual` F, screenshot `visual-3d`). Left: hit-testing uses
  the 2D footprint. Button colours set back to `null` return to Silica's (fixed 2026-10-02, leg `visual` D2).
- Full `dotnet workload install` (requires the `microsoft.net.workloads.<band>` aggregate, which the repo does not pack;
  today the `sailfish-workload` tool, `dnx Microsoft.Maui.SailfishOS.Workload install`, or `tools/sf workload-install`).
  Tried 2026-10-03 in a private SDK (W10 13a): once the tool has installed the manifest, `dotnet workload install
  sailfish` works in both modes and adds the backend to the SDK's `library-packs` (no NuGet source needed for it),
  `workload list` and `workload uninstall`. Without the tool it fails ("Workload ID sailfish is not recognized"):
  the SDK only knows IDs from manifests on disk or from a workload set. Two manifest defects found there are fixed:
  the manifest version was the number 1 (workload-set mode uninstalled the manifest right after installing it), and
  restore on a clean machine failed with NETSDK1112 (`SelfContained` now defaults to true in the manifest targets).
  A workload set of our own would replace Microsoft's for the band and has to be re-cut with each of theirs.

**Deliberately open** (not touched without a new decision; confirmed by the owner 2026-10-03)
- Camera photos (Sailfish has no in-app capture API); Geocoding and TextToSpeech (no provider on the platform).
- BlazorWebView.
- `MauiSplashScreen` ignored (Sailfish apps have no splash, `sailfishos-packaging.md`); the ugly
  cold start is a separate defect in `BUG_LIST.md` (S3-1).

**Quality and release**
- armv7hl on a real device (built, untested; labelled so in the README until a 32-bit device is at hand).
- NativeAOT: phase 4 frozen by decision G2; `LibraryImport` instead of `DllImport`, `NativeMemory`, B5 (TimeZone
  with InvariantGlobalization), audit 2.14 — [`aot-and-trimming.md`](aot-and-trimming.md).
- No sfdk integration (RPM from host rpmbuild or `sf-rpmbuild.py`) — known limitation.
- VS Code extension (repo `sailfishos_maui_tools`): a confirmed breakpoint in an F5 session from the extension,
  Hot Reload (gate: whether the `net11.0-sailfish` head build works under `dotnet watch`), shim/sysroot preflight and checking which
  build is on the phone (equivalents of `sf doctor` / `sf verify`).
