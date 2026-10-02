# MAUI.Sailfish → parity with MAUI.Android / MAUI.iOS — roadmap

The only current work plan (refreshed 2026-09-28). Phases F0–F5 below record what has been done since
the 2026-09-24 audit; what is left is in the [What is left](#what-is-left) section. Defects:
[`../BUG_LIST.md`](../BUG_LIST.md). Architecture principles: [`architecture.md`](architecture.md). The move to a
handler model aligned with MAUI net11 (stages A0–A7) is done except for a single-window renderer (A6).

## Status (2026-09-28)

- **On-device matrix:** `tools/sf matrix` 25/25 legs PASS (2026-09-25), every verdict from the leg's own
  marker.
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
- [x] Self-hosted CI runner with a phone: job `device` in `ci.yml`
  (workflow_dispatch + nightly, label `sailfish-device`, full matrix, logs).
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

As of 2026-09-28, checked against the code and git history. Smaller defects and platform gaps
(`OpenUrl`, MCE, `OpenFileRequest`) are in
[`../BUG_LIST.md`](../BUG_LIST.md).

**Navigation**
- Animation and preview of the next page on tab swipe (SlideshowView with several pages at once).
- One window only: the renderer is a static `Current`; the window DI scope exists (A6), multi-window does not.

**Performance** (page-load acceptance: push animation starts ≤ 150 ms after the tap with complete content,
no empty frames, pop reveals a ready page even 2+ levels back; check with `tools/sf record … --keep DIR` +
`tools/sf page-load DIR --max-settled 700`)
- Going back 2+ levels: re-measure with the per-page cache (A5, LRU of 4); before it, 847 ms for two levels.
- Idle preload of adapter QML components after the first page (`Qt.createComponent` over `adapters.json`),
  so the first visit is as fast as the second (−50 to −300 ms).
- Remaining Canvases (Shape with gradient/path, GraphicsView) off the GUI thread (`renderStrategy: Threaded`),
  after checking Qt 5.6 with Mali.
- Enter animation start ≤ 150 ms for hub rows (measured 85–304 ms); a matrix leg guarding against page-load
  regressions.
- MAUI animations (`FadeTo`/`TranslateTo`/`ScaleTo`) work, but every frame is a reconcile pass — no Qt-side
  animation path.
- CollectionView: one QML component per `DataTemplate` (F4b) instead of ~12 hosts per card (today F4a: empty
  layout containers in rows get no host). Largest remaining gain for opening a catalog and loading more.
- Opening a page with a list (~200 ms stall in Kitchen): split into two turns (page + header first, visible rows
  in the next frame)? Saves ~80–120 ms but the list is empty for 1–2 frames of the enter animation. Needs a decision.

**Platform**
- Per-page orientation (today only for the whole app, 769f2db).
- Touch: first point only — no pinch/multi-touch or wheel.
- 3D transforms (`RotationX/Y`) and non-uniform `ScaleX≠ScaleY` — warning only
  (`QtHostVisualState.TransformLimit`); to check: hit-test of rotated/scaled hosts and Button colors
  reverting to Silica defaults after setting `null`.
- Full `dotnet workload install` (requires the `microsoft.net.workloads.<band>` aggregate, which the repo does not pack;
  today the `sailfish-workload` tool, `dnx Microsoft.Maui.SailfishOS.Workload install`, or `tools/sf workload-install`).

**Deliberately open** (not touched without a new decision)
- Camera photos (Sailfish has no in-app capture API); Geocoding and TextToSpeech (no provider on the platform).
- BlazorWebView.
- `MauiSplashScreen` ignored (Sailfish apps have no splash, `sailfishos-packaging.md`); the ugly
  cold start is a separate defect in `BUG_LIST.md` (S3-1).

**Quality and release**
- armv7hl on a real device (built, untested).
- NativeAOT: phase 4 frozen by decision G2; `LibraryImport` instead of `DllImport`, `NativeMemory`, B5 (TimeZone
  with InvariantGlobalization), audit 2.14 — [`aot-and-trimming.md`](aot-and-trimming.md).
- No sfdk integration (RPM from host rpmbuild or `sf-rpmbuild.py`) — known limitation.
- VS Code extension (repo `sailfishos_maui_tools`): a confirmed breakpoint in an F5 session from the extension,
  Hot Reload (gate: whether the `net11.0-sailfish` head build works under `dotnet watch`), shim/sysroot preflight and checking which
  build is on the phone (equivalents of `sf doctor` / `sf verify`).
