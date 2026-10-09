# MAUI 11 alignment: gap analysis and work plan

Written for: the agent (or person) who continues the backend after commit `af3d782` ("remove not needed files from
package", branch `feature/fixes`, 2026-10-04), without having taken part in the earlier reviews. It assumes you
have read [`architecture.md`](architecture.md) (the design rules) and know that
[`architecture-plan.md`](architecture-plan.md) and [`architecture-handoff.md`](architecture-handoff.md) (W1–W10)
describe internal refactors that are, with a few listed leftovers, done. This document is about something else:
**what a MAUI 11 app expects from a platform backend and where this one does not provide it.**

How it was produced: a static review of the working tree at `af3d782` against the MAUI assemblies the repo pins
(`11.0.0-rc.1.26451.6`, the plain `lib/net11.0` build this backend consumes, plus the Android/iOS builds as the
reference behaviour), decompiled with `ilspycmd`, and against the "What's new in .NET MAUI for .NET 11" page
(learn.microsoft.com, dated 2026-09-10). Eight area audits were run; their full reports with file:line evidence are
in [`audits/maui11/`](audits/maui11/). Nothing was run on the phone for this review: every "unverified" mark means
exactly that, and the first step of several packages is a device check.

Line numbers are from `af3d782`. They drift; grep the symbol when one no longer matches.

**Progress is tracked in [`maui11-tracker.md`](maui11-tracker.md)**: the packages below split into 58 sessions
(S01–S58) with checkboxes, notes and owner answers to D1–D17. This file stays the reference; do not tick here.

## 1. State at `af3d782`

| Measure | Value |
| --- | --- |
| Core source (`src/Linux.SailfishOS`, .cs) | 24 747 lines; QML + JS 6 766; native shim 4 082 (7 files, ABI 4); diagnostics 10 310 |
| Host tests | 326 `[Fact]`/`[Theory]` (+80 `InlineData` rows) in 41 files; `dotnet test tests/Linux.SailfishOS.Tests` |
| Device matrix | 31 legs (`tools/sf matrix`), last full run 2026-10-04: 31/31 PASS (README) |
| Handler key parity | 1073/1073 official property-mapper keys for 27 controls ([`handler-parity.md`](handler-parity.md)); CollectionView, CarouselView and the page containers are **not** in that measurement |
| Public surface | 89 types (`tests/Linux.SailfishOS.Tests/PublicSurface.txt`) |
| MSBuild | `buildTransitive/Microsoft.Maui.SailfishOS.targets` 717 lines, no automated test |
| Package ids | all five start with `Microsoft.Maui.` (see M7) |

What works, in one paragraph: every stock control, Shell/TabbedPage/FlyoutPage/NavigationPage on the Silica
pageStack, CollectionView/CarouselView on a virtualized Silica ListView, dialogs as Silica panels, most Essentials
on Sailfish services, fonts, images (file, URI with cache policy, stream, glyph), shapes, brushes on shapes,
shadows and clips on every view, SkiaSharp raster views, Release trim+R2R RPMs, vsdbg debugging from VS Code. The
gaps below are what a MAUI developer meets after that.

## 2. MAUI 11 "what's new" against the backend

The MAUI 11 feature list (RC1) and where each item stands here. Controls-level features that need nothing from a
platform are marked "inherited".

| MAUI 11 item | Backend state | Where / what to do |
| --- | --- | --- |
| CoreCLR is the default runtime | done (always was CoreCLR) | — |
| Test project templates (`androidtest`, `iostest`, …) on Microsoft.Testing.Platform, `dotnet test` runs in-app | **missing**; RC1 ships no device runner the backend could reuse | the 31-leg matrix is the stand-in; a `sailfishtest` runner is M21 (deferred unless the owner wants it) |
| `HybridWebView` JS→.NET with a source-generated `JsonSerializerContext` | n/a: `HybridWebView` has no Sailfish handler at all (falls to the container fallback, renders nothing) | M15 (decide: Gecko-backed handler or documented "not supported") |
| Material 3 on Android | n/a (Android) | — |
| `BoxView.Fill` brush | done (`QtHostShapes.cs:251`, MAUI's Fill → Background → Color order) | — |
| `LongPressGestureRecognizer` (Duration, movement threshold, `GestureState`, Command) | implemented (`QtHostInput.cs:81,120-122`) | M17 verifies the semantics against the RC1 type |
| `Map` enhancements | n/a: `Microsoft.Maui.Controls.Maps` has no Sailfish implementation | document; a Mapbox/QtLocation adapter is out of scope |
| Windows CollectionView2, Android Shell handler, iOS Navigation/Tabbed/Flyout handlers | n/a (their platforms); note the **event-timing change**: on iOS `Appearing` now fires before the push, as it already does here | — |
| `Microsoft.Maui.Controls.Compatibility` package removed | fine: not referenced. Consequence: legacy `ListView`/`TableView`/cells have **no renderer anywhere**, so a platform must ship its own (M4) | M4 |
| `TabbedPage.BadgeText/BadgeColor/BadgeTextColor` attached properties | **missing** (no "Badge" in `src/`) | M15 |
| `SwipeItem.IconColor/TextColor` | **missing**: `SwipeItemsJson` emits text/icon/background only; the QML reads a `fg` field that is never emitted and never tints the icon; `Font`/`CharacterSpacing` ignored; Top/Bottom items not rendered (warned) | M15 |
| `BlazorWebView` static content caching | n/a (BlazorWebView deliberately out) | — |
| Shell route templates (`Routing.RegisterRoute("trip/{tripId}")`) | inherited (`ShellUriHandler`); the overlay's route-page construction (`SailfishServiceOverlay.cs:48-52`) must be checked with a template route | M16 |
| `ViewExtensions` `*ToAsync(CancellationToken)`, non-Async obsolete | inherited (animations tick on `SailfishFrameTicker`) | — |
| `NavigationPage.BackButtonAccessibilityLabel`, `BackButtonBehavior.AccessibilityLabel` | n/a: Silica has no toolbar back button (the page-stack indicator is the back affordance) | document |
| `Passkeys` | `FeatureNotSupportedException` (decided) | — |
| `MediaPickerOptions.SaveToGallery` | n/a (no in-app capture, decided) | — |
| `Window.StatusBarTheme` | n/a: a Sailfish app window has no status bar; today the key is simply unmapped (`SailfishWindowHandler.Mapper` has only `Content`) | M15 maps `Title` → cover title and leaves `StatusBarTheme` as an explicit no-op |
| `MauiAndroidSystemBarsUseMauiChrome`, Windows `AppInstance` activation, Android MediaPicker recovery | n/a | — |
| `GeolocationListeningRequest.MinimumDistance`, `AltitudeReferenceSystem.Geoid` | **`MinimumDistance` ignored** (only `MinimumTime` → `updateInterval`; every fix raises `LocationChanged`); altitude reference as QtPositioning reports it | M14 |
| `MauiIcon MonochromeFile`, `MauiImage ResizeQuality`, themed `MauiSplashScreen` | `ResizeQuality` flows through MAUI's own `ResizetizeImages` task; `MonochromeFile` is irrelevant to the Sailfish icon set; splash is ignored (decided) | M19 moves the images onto MAUI's external-backend hook |
| `Permissions.PostNotifications` on iOS; `Permissions.Current` (`IPermissions`) | `IPermissions` is in the registry and the generic static form works (`PostNotifications` always Granted); the **instance form** of any `Permissions.X` throws on plain net and cannot be hooked | M14 |
| Trimmable CSS | inherited | — |
| `VisualElement.InvalidateStyle()`, `VisualStateManager.InvalidateVisualStates()` | inherited | — |
| XAML: `x:Code`, compiled bindings in DataTemplates, implicit `xmlns`, lazy `ResourceDictionary`, AOT-safe `RelativeSource` bindings | inherited: the head builds with `MauiXamlInflator=SourceGen` (`targets:75`) | — |
| XAML Incremental Hot Reload (source generator + `MetadataUpdateHandler`, on in Debug, applied by `dotnet watch`) | **no path to the phone**: Debug builds carry the generator output but nothing applies deltas | M21 |
| `dotnet run` prompts for TFM/device, streams output, Ctrl+C stops the app | **missing**: no `RunCommand` hook; `dotnet run -f net11.0-sailfish` would execute the linux-arm64 binary on the host. Only the `SailfishRun` target exists (`targets:688`) | M21 |
| `dotnet watch` for Android/iOS (deploy + hot reload) | **missing** | M21 |
| Breaking: `Color` is sealed | fine (no subclass in `src/`) | — |

## 3. Gap catalog by area

Each area: the few findings that matter, with evidence; the full tables are in the audit files. Severity:
**blocks** = an app using the feature does not work; **degrades** = works with a visible difference; cosmetic;
n/a = not applicable on the platform (documented, not fixed).

### 3.1 Application, window, lifecycle, threading ([audit](audits/maui11/lifecycle.md))

| # | Finding | Evidence | Severity |
| --- | --- | --- | --- |
| L1 | `IWindow.Stopped` fires only on `Qt.ApplicationSuspended`; minimizing to the cover is `Inactive` and raises `Deactivated` only, so **`Application.OnSleep` never runs on minimize** while `OnResume` does run on return (`Resumed` on 2→4). The porting guide claims parity. | `ActivationGate.cs:65-72`; `SailfishPlatformTypes.cs:4-14`; `docs/add-sailfish-to-existing-app.md:110,116` | **blocks** (apps flush/pause in `OnSleep`) |
| L2 | Quit never raises `IWindow.Stopped`/`Destroying`: `OnSleep`, `Window.Destroying`, `AlertManager.Unsubscribe`, `Application.RemoveWindow`, the window handler's `DisconnectHandler` do not run on exit. `Resumed` is also raised once at startup (Android raises none). `Created` is sent before the window handler exists, so `OnStart` sees `Windows[0].Handler == null`. `ActivateWindow` command unmapped. | `Boot.cs:124-146,169-176`; `SailfishMauiApplication.cs:113-121`; `ActivationGate.cs:48-54`; `SailfishApplicationHandlers.cs:15-45` | degrades |
| L3 | Hardware Back/Escape goes straight to `renderer.TryPop()`; `IWindow.BackButtonClicked()` → `Page.OnBackButtonPressed` / `Shell.OnBackButtonPressed` (`BackButtonBehavior.Command`) / `NavigationPage.OnBackButtonPressed` are never consulted. Silica swipe-back pops natively and MAUI follows; no veto. | `Boot.cs:188-195`; `QtHostPageRenderer.cs:606-623`; `NativeStackCoordinator.cs:208-230`; `grep SendBackButtonPressed src` → none | **blocks** (confirm-exit, unsaved-changes prompts) |
| L4 | `SoftInputExtensions.Hide/Show/IsSoftInputShowing` throw `NotSupportedException` once a platform view exists (plain-net Core has no seam); the Maliit keyboard rectangle reaches `OnInputMethodChanged` only, nothing resizes or scrolls layout. | decompile `Microsoft.Maui.SoftInputExtensions`; `MauiShell.qml:142-151`; `grep keyboard MauiModelPage.qml QtHostLayout.cs` → none | degrades (forms under the keyboard; a crash at the call site) |
| L5 | Theme starts `Unspecified`: `Application`'s constructor snapshots `AppInfo.RequestedTheme` before the theme service runs on the first Qt tick, so the first page is built with `AppThemeBinding.Light` values under a dark ambience and re-themed one tick later. Silica's `Theme.highlightColor` is not exposed anywhere; `Application.AccentColor` is MAUI's constant. `AppInfo.RequestedLayoutDirection` is hard-coded `LeftToRight`. | `SailfishEssentials.cs:166-210`; `Boot.cs:210-213`; `Essentials.cs:446,450` | cosmetic / degrades (RTL locales) |
| L6 | `Loaded` on plain-net Controls is `Window != null`: it fires before any Sailfish handler exists for every page the app constructs itself (Shell route pages are the exception, hooked in the overlay). A `Loaded` handler touching `Handler.PlatformView` fails. | `SailfishServiceOverlay.cs:49-57,99-122`; decompile `Controls.VisualElement` (`IsLoaded`, `HandlePlatformUnloadedLoaded`) | degrades (common pattern in ported apps) |
| L7 | Dispatcher/MainThread/animation/dialog wiring are sound. Cosmetic: a "Replaced an existing DispatcherProvider" warning (two provider instances), a dispatcher timer whose `Tick` throws re-fires every pump tick, no `ActivationGate` test. | `SailfishMauiApplication.cs:43-47` + `Hosting/AppHostBuilderExtensions.cs:33`; `SailfishDispatcherProvider.cs:205-222`; `grep ActivationGate tests` → none | cosmetic |
| L8 | Hot reload: nothing references `MetadataUpdateHandler`/`MauiHotReloadHelper` in `src/` or `tools/`; Debug builds enable MAUI 11's incremental XAML hot reload generator with no agent to apply deltas. | audit rows 27; `docs/parity-plan.md:258-261` | degrades (DX) |

### 3.2 Collections ([audit](audits/maui11/collections.md))

| # | Finding | Evidence | Severity |
| --- | --- | --- | --- |
| C1 | **`DataTemplateSelector` is unsupported on the whole list path**: `CreateFromTemplate` calls `template.CreateContent()`, which MAUI 11 throws for a selector; the exception is swallowed, the row is null and 0 dp, the list renders empty. Same for group header/footer, header/footer and empty-view templates. | `QtHostCollectionBridge.cs:565-580`; `grep SelectDataTemplate src` → none | **blocks** |
| C2 | **Legacy `ListView`, `TableView` and every `Cell` type have no handler** and render blank: `ListView : ItemsView<Cell>` does not derive from `ItemsView`, so `Row<ItemsView, SailfishListViewHandler>` does not match and resolution falls to `SailfishContainerHandler` (background only). Cells are never realized. Not mentioned in the porting guide. | `SailfishHandlersFactory.cs:198-201,246`; `SailfishLayoutHandlers.cs:174-197`; `SailfishViewHandler.cs:239` | **blocks** (Xamarin-era apps) |
| C3 | `RefreshView` arms only on pages without a pulley: any `ToolbarItems`, a Shell flyout or a FlyoutPage disarm it silently and no "Refresh" pulley entry is added, so in a Shell app with a flyout the pull gesture never refreshes. | `QtHostPageRenderer.Layout.cs:638-650`; `QtHostCollectionBridge.cs:251` | **blocks** (refresh in Shell apps) |
| C4 | Missing MAUI behaviours: `ItemsUpdatingScrollMode` (chat UIs: `KeepLastItemInView` set in the sample, ignored), `ItemSizingStrategy` (always measure-all), `SnapPointsType/Alignment` on CollectionView, `CanReorderItems`, VSM `Selected` state on item roots (highlight is a QML rectangle *under* the content, invisible with opaque templates), carousel `VisibleViews/IsDragging/IsScrolling/IsScrollAnimated` and item states, `ScrollTo(animate:)`, `Scrolled` deltas (always 0), runtime `Span`/`ItemSpacing` change on the same layout object, vertical `Loop`. | audit tables | degrades |
| C5 | Recycling semantics: one MAUI view per item for its lifetime (never rebound, O(N) views); the pool recycles QML hosts only, and only for rows whose adapters are all in `PoolableUris` (an Entry, SwipeView or nested list in a row is recreated on every scroll-in). A CollectionView inside a ScrollView measures to full extent and hits the 192-delegate cap: rows past it stay blank. | `Rows.cs:57-66,201`; `RowPool.cs:16-26`; `QtHostCollectionBridge.cs:27`; `Delegates.cs:197-201` | degrades → blocks for long nested lists |
| C6 | Parity tracking excludes the items handlers: `HandlerParityTests` lists no `CollectionViewHandler`/`CarouselViewHandler`, so their mapper keys are not counted. | `HandlerParityTests.cs:25-53` | cosmetic |

### 3.3 Graphics, images, text ([audit](audits/maui11/graphics.md))

| # | Finding | Evidence | Severity |
| --- | --- | --- | --- |
| G1 | Image resolution bypasses MAUI's `IImageSourceService` DI entirely: `QtHostImages.Resolve` switches on the four concrete Controls types and the Sailfish-only synchronous `QtHostImageSources.Register(Func<ImageSource,string?>)`. A library's `AddService<GravatarImageSource, …>` is ignored, a custom source implementing `IStreamImageSource` is not recognised, there is no cancellation of in-flight loads and `Image.IsLoading` is never set. | `QtHostImages.cs:192-201`; `QtHostImageSources.cs:27-52`; `grep IImageSourceService src` → none | degrades (architecture gap; blocks libraries with custom sources) |
| G2 | Image leaks and losses: `StreamImageSource` cache files are never deleted; a GIF from a stream never animates (`.img` extension); unsized or `Aspect.Center` images decode at full size (memory risk on the phone). | `QtHostImages.cs:272-313`; `Image.qml:41-50,151-158` | degrades |
| G3 | `GraphicsView` interaction callbacks (`StartInteraction/DragInteraction/EndInteraction/CancelInteraction`) are not wired: chart and gauge libraries on `GraphicsView` render but take no touch. | `grep StartInteraction src` → none; `GraphicsView.qml` has no touch item | degrades (blocks interactive charts) |
| G4 | Canvas text defects: `DrawString(value,x,y,ha)` anchors the text *top* at `y` (Android anchors `y − fontSize`, so text sits one line low); `ICanvas.Font` is passed raw (a `ConfigureFonts` alias is not a Qt family) while `GetStringSize` resolves it. `FillPath`/`ClipPath` pass `"evenodd"` as an argument Qt 5.6 ignores (fill rule is the `fillRule` property). | `GraphicsView.qml:362-370,198-205`; `QtHostCanvasRecorder.cs:83-87`; `QtHostTextMetrics.cs:39` | degrades |
| G5 | Every drawable is re-recorded on every reconcile pass (plus `Invalidate` and property changes); a 4096-command cap silently truncates dense charts; `DrawImage`, `SubtractFromClip`, `IAttributedText`, `ImagePaint`/`PatternPaint`, `Antialias` and 12 blend modes are dropped. | `SailfishSnapshotHandler.cs:123` → `Walk.cs:266`; `QtHostCanvasRecorder.cs:15,218-233` | degrades |
| G6 | Gradient brushes on `Background` of anything but Shape/BoxView/Border reduce to nothing (`QtHostPaint.Solid` returns null → `BackgroundColor` fallback): a gradient page or stack background paints flat. `Shadow.Brush` must be solid. | `QtHostPaint.cs:11-14`; `QtHostVisualState.cs:169,233` | degrades |
| G7 | `FormattedText` spans: no per-span `BackgroundColor`, `CharacterSpacing`, `LineHeight`, `TextTransform` or span gestures; a multi-font FormattedText is *measured* with the first span's font. `FontAutoScalingEnabled` is not read anywhere (large-text accessibility). | `AdapterSnapshots.Label.cs:95-125`; `SailfishMeasure.cs:111-135`; `grep FontAutoScaling src` → none | degrades |
| G8 | No Sailfish `IImage`: the netstandard `PlatformImage` only parses headers and throws on `Draw/Downsize/Resize`; `Screenshot` returns a PNG file with no `IImage` bridge; `IViewScreenshot` (MAUI 11: "third-party platform backends should implement this interface on their `IScreenshot`") is not implemented, so `View.CaptureAsync()` is unsupported. | `SailfishShareAndPickers.cs:421-460`; decompile `Microsoft.Maui.Media.IViewScreenshot` | degrades |
| G9 | No host unit tests cover the recorder, shape serialisation, span HTML, glyph/stream URLs or the cache policy; coverage is device-leg only. | audit Q10 | cosmetic |

### 3.4 Build, SDK, workload, packaging, DX ([audit](audits/maui11/build-sdk.md))

| # | Finding | Evidence | Severity |
| --- | --- | --- | --- |
| B1 | **All five package ids use the reserved `Microsoft.Maui.` prefix** (`Microsoft.Maui.SailfishOS`, `.SkiaSharp`, `.Workload`, `microsoft.maui.sailfishos.Manifest-<band>`, `Microsoft.Maui.Platforms.SailfishOS.Templates`); nuget.org rejects a push from a non-Microsoft account. The ids are hard-coded in the workload tool, pack-local, docs and template. | `Linux.SailfishOS.csproj:7`; `Linux.SailfishOS.Workload/Program.cs:16`; `tools/cmd/pack-local.sh:37-60` | **blocks release** |
| B2 | `_SailfishDefuseMauiFrameworkConversion` empties `TargetPlatformIdentifier` before `ProcessFrameworkReferences` (needed for MAUI's neutral-TFM backend hook) and thereby loses everything else keyed on it: `[SupportedOSPlatform("sailfish1.0")]`/`[TargetPlatform]` attributes, the `SAILFISH1_0`/`SAILFISH1_0_OR_GREATER` defines (only `SAILFISH` is re-added), and CA1416 treats the head as cross-platform. The manifest targets also force RID + `SelfContained` on **every** project with the TFM, class libraries included. | `targets:60,193-199`; `WorkloadManifest.targets.in:10-13` | degrades (DX) |
| B3 | Resizetizer is driven through a private side door (`ResizetizeImages PlatformType="wpf"` called directly, fonts copied raw, assets resolved by hand) while MAUI 11 RC1 has an explicit external-backend contract (`ResizetizerPlatformType`, `_PrepareExternalMaui*`, `ResizetizerAfterAssetProcessingTargets`, `@(MauiProcessedImage/Asset/Font)`). No incremental stamps or stale-output cleanup. | `targets:127-183`; `Microsoft.Maui.Resizetizer.After.targets:138-141,414-478` | degrades (DX) |
| B4 | Trimming: the backend assembly carries no `IsTrimmable`, so under the default `TrimMode=partial` it ships untrimmed; `scaffold/trimmer.xml` roots `*MauiProgram` in every assembly; no MAUI feature switch is set by the leg (the Controls defaults for partial trim stay on). | `targets:206-211,281-294`; `strings` on the dll | cosmetic |
| B5 | DX vs an in-box head: no `dotnet run`, no `dotnet watch`/hot reload path, no device `dotnet test` (RC1 ships no runner either), 86 `MAUI_SAILFISH_*` env switches, 26 shell/python files shipped inside the NuGet package under `buildTransitive/net11.0/tools` (Windows hosts cannot run them), `UseMaui=false` on the Sailfish head of the template. | audit rows; `Linux.SailfishOS.csproj:50-64`; `templates/maui-sailfish-app/MauiSailfishApp.csproj:27,58` | degrades (DX) |
| B6 | Packaging quirks: `sailfish-launcher` lands at `runtimes/linux-arm64/native/sailfish-launcher/sailfish-launcher` (extension-less `PackagePath` read as a folder, works by accident); no `snupkg`/SourceLink properties; version pinned `0.1.0` with a cache-clearing workaround in pack-local. | `Linux.SailfishOS.csproj:73`; `Directory.Build.props:15` | cosmetic |
| B7 | No automated coverage for the targets (717 lines), the manifest targets, the template output, the QML behaviour (6.8k lines) or the shim behaviour (4.1k lines); no CI, no `.editorconfig`. | audit "Test & CI coverage map" | degrades |
| B8 | Template: faithful copy of the official `maui` template's non-sample variant, minus the official symbols (`applicationId`, `Framework`, `IncludeSampleContent`, `$guid9$`): `ApplicationId` is always `com.companyname.mauisailfishapp` and every generated app has the same Windows `PhoneProductId`. The template's `Styles.xaml` overrides Silica theming with explicit colours. | `templates/maui-sailfish-app/.template.config/template.json`; `Package.appxmanifest:11` | cosmetic |

### 3.5 Essentials and platform services ([audit](audits/maui11/essentials.md))

The install mechanism is sound: all 36 registry rows name the installer the RC1 facade really has (guarded by
`MoreEssentialsTests.Every_row_names_an_installer_the_facade_has`), MAUI's own `EssentialsInitializer` bridges every
DI-registered interface during `Build()`, MAUI registers no Essentials interface itself so the Sailfish `TryAdd`
never loses, and `MainThread` is hooked through the real internal `SetCustomImplementation`. The gaps are at member
level.

| # | Finding | Evidence | Severity |
| --- | --- | --- | --- |
| E1 | `AppInfo` identity is wrong: `PackageName` = entry assembly name, `VersionString` = assembly version ("1.0.0.0"), `BuildString` = "", `PackagingModel` always `Unpackaged`, `RequestedLayoutDirection` always LTR. `IVersionTracking` has no registry row; the static still works through MAUI's neutral implementation and the RC1 bridge, but with this `AppInfo` it never sees an RPM version bump. | `Essentials.cs:430-450`; `SailfishEssentialsRegistry.cs:38-73` | degrades (version tracking, RTL) |
| E2 | `IViewScreenshot` (new in 11) not implemented: `view.CaptureAsync()` resolves `IScreenshot` and requires `is IViewScreenshot`, so per-view capture silently returns null. `IScreenshotResult.OpenReadAsync(Jpeg)` throws. | `SailfishShareAndPickers.cs:422-458`; decompile `ScreenshotDispatch` | degrades |
| E3 | `Permissions` instance form (`new Permissions.Camera().CheckStatusAsync()`) throws `NotImplementedInReferenceAssemblyException` on plain net (`BasePlatformPermission` virtuals, not hookable); only the generic static form reaches `IPermissions`. Mapping: 20/27 types → Sailjail names; `LaunchApp → "AppLaunch"` unverified; `Permissions.Flashlight` reports Granted while `Flashlight` throws in a sandbox; the pickers never demand `Pictures`/`Documents`. | `SailfishShareAndPickers.cs:295-346`; `SailfishDeviceExtras.cs:53-55` | **blocks** (instance form) / degrades |
| E4 | Functional defects: `Launcher.TryOpenAsync` returns `CanOpenAsync` and never opens; `Map.TryOpenAsync` always true; `Share.RequestAsync(ShareTextRequest)` drops `Text` when `Uri` is set; `Clipboard.ClipboardContentChanged` fires only after the app's own `SetTextAsync` (no `QClipboard::dataChanged` hook in the shim); `EmailMessage.Attachments`/`BodyFormat` and `MapLaunchOptions.NavigationMode` dropped silently; `GeolocationListeningRequest.MinimumDistance` (new in 11) and `DesiredAccuracy` ignored (every fix raises `LocationChanged`); every `MediaPickerOptions` member ignored, including `SelectionLimit`; `DisplayInfo.RefreshRate` constant 60; `AppInfo.ShowSettingsUI` throws `NotSupportedException` instead of `FeatureNotSupportedException`. | `Essentials.cs:407-409`; `SailfishShareAndPickers.cs:69-91,214-218,365-380,390-412`; `Essentials.cs:19,39` + `sailfish_host.cpp:550-577`; `SailfishSensors.cs:285-318`; `SailfishEssentials.cs:355` | degrades |
| E5 | `Early` set covers FileSystem, Preferences, AppInfo, DeviceInfo only; `SecureStorage` and `DeviceDisplay` read in a `MauiProgram` before `Build()` still hit MAUI's throwing defaults although neither needs Qt to construct. | `SailfishEssentialsRegistry.cs:38-43` | cosmetic |
| E6 | Dead code: the "MAUI registers its reference-assembly screen reader" branch; in RC1 `ISemanticScreenReader` lives in `Microsoft.Maui.Essentials.dll` and nothing registers it. `IAppleSignInAuthenticator` has no row (MAUI's default throws). | `SailfishEssentials.cs:32-39`; `SailfishServiceOverlay.cs:77-81` | cosmetic |
| E7 | API shape: `SailfishLifecycle`/`AddSailfish` follow `AddAndroid`/`AddiOS` exactly. `SailfishPage.AllowedOrientations` is the one API whose idiomatic home is `Microsoft.Maui.Controls.PlatformConfiguration.SailfishOSSpecific.Page` + `page.On<SailfishOS>()` (MAUI's `On<T>()` accepts any third-party `IConfigPlatform`; no MAUI change needed). Cover/Remorse/BottomSheet/Notifications have no MAUI counterpart; imperative statics are acceptable, an interface + `Default` would add mockability. | `SailfishPage.cs:28-38`; audit "Platform-specific API shape" | cosmetic |

### 3.6 Handlers and elements ([audit](audits/maui11/handlers.md))

Every view type MAUI 11 registers by default (47 pairs in `AddControlsHandlers`) has a Sailfish row or an adequate
fallback, with the exceptions below. Command mappers: 30 of 35 meaningful keys answered. All 31 public handlers
expose `Mapper`/`CommandMapper` and the `(IPropertyMapper?, CommandMapper?)` constructor (W5.5 verified).

| # | Finding | Evidence | Severity |
| --- | --- | --- | --- |
| H1 | `HybridWebView` falls to `SailfishContainerHandler`: blank, warning once, its three commands unanswered so `InvokeJavaScriptAsync` never completes. `Controls.Maps.Map` and the toolkit `MediaElement` are also undocumented "not supported". | `SailfishHandlersFactory.cs:198-201`; `grep HybridWebView src` → none | degrades (blocks apps on it) |
| H2 | `SwipeItem` (MAUI 11 `IconColor`/`TextColor`): no handler; `SwipeItemsJson` drops text colour, icon tint, `Font`, `CharacterSpacing`; `SwipeView.qml` reads a `fg` field never emitted; runtime `SwipeItem` property changes do not re-push; Top/Bottom items not rendered; `SwipeItemView` content approximated. | `AdapterSnapshots.Containers.cs:239-241,269-290`; `qml/controls/SwipeView.qml:176,196` | degrades |
| H3 | `Window` mapper maps `Content` only: `Window.Title` never reaches the cover/task switcher (the .desktop title shows); `Window.FlowDirection` is not mapped and the RTL root resolution skips the window (`e is VisualElement`), so RTL set on the window yields LTR. | `SailfishApplicationHandlers.cs:51-80`; `QtHostVisualState.cs:220-228`; `Layout.cs:334-336` | degrades (RTL root) |
| H4 | Renderer-owned chrome bypasses handlers: `ToolbarItem` icon-only items become blank pulley entries (`Text`/`IsEnabled` only), `MenuFlyoutSubItem` is flattened to one entry (children lost), `MenuFlyoutSeparator` becomes an empty tappable row. | `QtHostPageRenderer.Interactions.cs:36,220-245`; `qml/interactions/ContextMenu.qml:44-45` | degrades |
| H5 | Extensibility traps: `SailfishNavigationViewHandler` publishes `NavigationCommandMapper` while the inherited static `CommandMapper` (from `SailfishPageHandler`) is visible on the type and unused, so `SailfishNavigationViewHandler.CommandMapper.AppendToMapping("RequestNavigation", …)` compiles and never fires; `SailfishApplicationHandler`/`SailfishWindowHandler` lack the mapper constructors; a view with `Clip`/`Shadow` reports `HasContainer == true` with `ContainerView == null` (MAUI's `NeedsContainer`), misleading app code and `PlatformEffect.Container`. | `SailfishNavigationViewHandler.cs:15-28`; `SailfishPageHandler.cs:34`; `SailfishApplicationHandlers.cs:22,66` | cosmetic |
| H6 | Change notifications not mapped: `TabbedPage.PagesChanged` (children/`ItemsSource` changes reach the tabs only at the next poll), `Shell.Items`/`FlyoutItems`/`FlyoutBehavior` (`UpdateValue` to the `ShellItem` handler, which is `NullElementHandler`), `ContentPage.HideSoftInputOnTapped` (plain-net manager is a no-op, router does nothing), `Compatibility.Layout` children added at runtime (no layout command raised). | `SailfishPageContainerHandlers.cs:84-152,233-424`; `grep HideSoftInputOnTapped src` → none | low |
| H7 | Legacy `ListView` is the only element with **no route at all** (C2); `docs/silica-parity.md:46` claims it is "native", contradicting the code; the six Controls shape handlers, `CollectionView`/`CarouselView` are missing from `HandlerParityTests.Pairs`. | `docs/silica-parity.md:46`; `HandlerParityTests.cs:25-53` | blocks (C2) / cosmetic |

### 3.7 Navigation ([audit](audits/maui11/navigation.md))

Push/pop/pop-to-root, modals (including a modal `NavigationPage`), TabbedPage, FlyoutPage (flyout as a pushed page),
Shell sections/tabs/routes/flyout-as-pulley and the three dialogs work and are device-proven. `NavigationFinished`
and the modal platform seam complete when the pageStack settles, with a 3 s "completing anyway" cap. The division
of labour (container handlers describe the stack, the renderer executes it on the one pageStack) violates no
stack-sync contract. The gaps are in the chrome and Back paths.

| # | Finding | Evidence | Severity |
| --- | --- | --- | --- |
| N1 | Page chrome bypasses MAUI 11's `Toolbar` abstraction. MAUI builds a `NavigationPageToolbar`/`ShellToolbar` with Title, `IsVisible`, `BackButtonVisible`, `BackButtonTitle`, `TitleView`, Priority-sorted `ToolbarItems` (page + Shell level); the renderer reads `Page.Title`, `Page.ToolbarItems`, `HasBackButton`, `BackButtonBehavior` directly. Silently ignored: `HasNavigationBar=false`, `NavBarIsVisible=false`, `TitleView`, `Shell.ToolbarItems`, `ToolbarItem.Priority`, `IconImageSource`; `NavigationFinished`'s `UpdateValue("BackButtonVisible")` goes nowhere. | `QtHostPageRenderer.cs:930-998,1208-1231`; `Interactions.cs:21-79`; decompile `NavigationPageToolbar`, `ShellToolbar`, `ToolbarTracker` | degrades |
| N2 | `Shell.SearchHandler` is not rendered at all: the whole search feature of a Shell app disappears, silently. | `grep SearchHandler src` → none | **blocks** (search-centred Shell apps) |
| N3 | Shell flyout features ignored silently: `FlyoutIsPresented` set from code (a hamburger/"menu" button does nothing), `FlyoutHeader/Footer/Content(+Template)`, `ItemTemplate`, `FlyoutBackdrop`, `FlyoutWidth`, `TabBarIsVisible=false` (tabs shown on login/onboarding content inside a `TabBar`), `NavBarIsVisible=false`, tab/flyout icons (an icon-only tab has an empty label), `BackButtonBehavior.Command/TextOverride/IconOverride`, `PresentationMode.NotAnimated`, `FlyoutBehavior.Locked` treated as `Flyout`. | `SailfishPageContainerHandlers.cs:319-385`; `SailfishPageHandler.cs:16-32` | degrades |
| N4 | A veto after a Silica swipe-back (cancelled `Shell.Navigating`, `ModalPopping.Cancel`, `PopAsync` returning null) leaves the native stack one page short until the coordinator's 3 s timeout re-pushes it. | `NativeStackCoordinator.cs:112-135,176-178`; `Navigation.cs:182-222` | degrades |
| N5 | Dialogs: one at a time (a second concurrent dialog completes at once with the negative result); any navigation requested while a dialog is open is held until it closes (a fire-and-forget alert followed by a push completes via the 3 s path); a dialog from a non-UI thread does not hop and completes with the fallback result; `FlowDirection` ignored; prompt `Keyboard` mapped for Numeric/Telephone only. | `MauiModelPage.qml:786-791`; `Interactions.cs:380-400`; `Navigation.cs:295-304`; `QtHostAlertSubscription.cs:84-104` | degrades |
| N6 | `InsertPageBefore`/`RemovePage` are modelled as a push/pop of the top model page (the unchanged top page re-renders, animated); the `animated:false` flag of push/pop/pop-to-root/modal is ignored (single-level ops always animate, multi-level are always `Immediate`). | `Navigation.cs:421-543`; `SailfishNavigationViewHandler.cs:52-56`; `SailfishModalNavigation.cs:26-28` | degrades / cosmetic |
| N7 | `TabbedPage` badges (new in 11) are not read; tab icons are ignored; more than ~4 tabs split the width with shrinking text and no scroll. A replaced `FlyoutPage.Detail` keeps its old handler subscribed. | `MauiModelPage.qml:980-994`; `SailfishPageContainerHandlers.cs:47-52,207` | degrades / cosmetic |
| N8 | Page cache (LRU 4): beyond four pages back the QML of a page is rebuilt on return (native transient state such as caret lost; MAUI state kept). Lifecycle counts match MAUI; the renderer's comment "MAUI core never fires appearing" is wrong for MAUI 11. | `QtHostPageRenderer.PageCache.cs:13`; `QtHostPageRenderer.cs:887` | cosmetic |

### 3.8 Input ([audit](audits/maui11/input.md))

All gesture families except Drag & Drop reach MAUI, including the new `LongPressGestureRecognizer`. The router is
observe-only at the Qt level (the shim's event filter returns false), so Silica always processes a touch first.

| # | Finding | Evidence | Severity |
| --- | --- | --- | --- |
| I1 | **Drag & Drop is entirely missing** (`DragGestureRecognizer`/`DropGestureRecognizer` never referenced); the MAUI 11 `SendDragStarting/SendDragOver/SendDragLeave/SendDrop/SendDropCompleted` surface is public, so a router-driven implementation is possible. | `QtHostInput.cs:717-760`; `grep DragGestureRecognizer src` → none | degrades (blocks DnD apps) |
| I2 | Router bugs: `SwipeGestureRecognizer.Threshold` ignored and the direction test is equality, so `Left\|Right` recognizers never fire; an `InputTransparent` hit ends the sequence instead of falling through to the view below and `CascadeInputTransparent` is not evaluated; hit rects under `Rotation`/`Scale` are the unscaled w×h at the transformed origin (a `Scale=2` view is tappable only in its top-left quarter), only translation is exact. | `QtHostInput.cs:275-282,590-608`; `Layout.cs:19-52,347-348` | degrades |
| I3 | A recognizer on a QML-consumed adapter (`Button`, `Entry`, `Switch`, `Slider`, `ScrollView`, lists) **or on any of its ancestors** never fires (the press returns before recognizer lookup); inside CollectionView rows only `TapGestureRecognizer` is forwarded (Pan/Swipe/Pinch/LongPress/Pointer in a DataTemplate never fire); `Span.GestureRecognizers` are never seen (router reads `View.GestureRecognizers`, not `CompositeGestureRecognizers`). | `QtHostInput.cs:30-36,266-273,733`; `ListView.qml:370-380` | degrades |
| I4 | `TappedEventArgs.GetPosition(relativeTo)`/`PointerEventArgs.GetPosition` ignore `relativeTo` (always root-space dp) while list-row taps are cell-relative; `Buttons` mask not checked. | `QtHostInput.cs:526-535,555,772`; `QtHostListAdapter.Input.cs:80-83` | degrades |
| I5 | LongPress: `AllowableMovement` not read (fixed 10 dp slop), `GestureStatus.Canceled` never sent (state goes stale on early release/movement), no `Running`. | `QtHostInput.cs:39,436-439,800-830` | cosmetic |
| I6 | Events never raised: `Button`/`ImageButton` `Pressed`/`Released`, `Slider.DragStarted/DragCompleted`, `SwipeView.SwipeChanging`, `Editor.Completed` (Android/iOS raise it on focus loss). | `grep SendPressed src` → none; `Slider.qml`; `AdapterEventRouter.cs:395-398,637-657` | degrades |
| I7 | Text input: `KeyboardFlags.Capitalize*`/`Keyboard.Create(None)` do not set `ImhNoAutoUppercase`; `ReturnType.Next` does not move focus; `ContentPage.HideSoftInputOnTapped` unimplemented; `SoftInputExtensions` throw (L4). | `AdapterSnapshots.TextInput.cs:127-153`; `AdapterEventRouter.cs:399-427` | cosmetic / degrades |
| I8 | Silica gesture conflicts: a MAUI horizontal Pan/Swipe also drives the page back-swipe, a vertical Pan at scroll-top also pulls the pulley, a Pan inside a `ScrollView` scrolls it natively too (gesture hosts have no `MouseArea`, nothing captures at the Qt level). | `host_core.cpp:224-225,245-302`; `MauiModelPage.qml:266,890-893`; `ContentView.qml:6`; `Border.qml:9` | degrades |
| I9 | Multi-touch: only the second finger is emitted (kind 7) and only for pinch; a pan while another finger holds elsewhere is lost when the holder is the first point; two Silica sliders under two fingers do not work (Qt mouse synthesis). | `host_core.cpp:277-283`; `QtHostInput.cs:615-654` | degrades (unverified on device) |
| I10 | No host tests for LongPress, Pointer, Pan or Swipe (only multi-tap and pinch). | `SampleAppRegressionTests.cs:641-714` | cosmetic |

## 4. Work packages

Ordered by the harm they remove. Each package names the files, the steps, the host tests and the device legs that
close it. Sizes: S ≤ half a day, M 1–2 days, L 3–5 days, XL more (an agent-day = one focused session with the
phone). Rules that apply to all of them are in §6.

### M1. Window lifecycle parity (L1, L2, L7) — M

Goal: `Application.OnStart/OnSleep/OnResume` and the `Window` events fire when and as often as on Android.

Files: `Platform/QtHost/ActivationGate.cs`, `Platform/SailfishMauiApplication.Boot.cs` (`CreateWindow`, `RunLoop`),
`Platform/SailfishMauiApplication.cs` (`RaiseQuitting`), `Handlers/SailfishApplicationHandlers.cs`,
`Platform/SailfishDispatcherProvider.cs`, `Hosting/AppHostBuilderExtensions.cs`.

Steps:
1. First read the shim's `TRACE appState=` lines on the device (`sailfish_host.cpp:151-157`) for: minimize to cover,
   cover → app, screen off/lock, system dialog in front, home-screen close. Record the `Qt.application.state` values
   in `docs/add-sailfish-to-existing-app.md` (lifecycle section) before changing anything.
2. `ActivationGate`: raise `Stopped()` on Active → {Inactive, Hidden, Suspended}; raise `Resumed()` on → Active only
   when a `Stopped` preceded it (`_stopped` flag); drop the startup `Resumed`; treat the first snapshot's `-1` as
   unknown and still raise on the first real value.
3. Quit: in `RaiseQuitting` (still on the Qt thread with callbacks alive) send `Deactivated()` if activated,
   `Stopped()`, `Destroying()` before the app's `OnQuitting`; let `Destroying` dispose the window scope (today
   `RunLoop` does).
4. `CreateWindow`: send `window.Created()` after `SetWindowHandler` + `AttachRootHandler` (Android order).
5. `SailfishApplicationHandler.CommandMapper["ActivateWindow"]`: single window → raise the Silica window
   (`QtThread.Post(() => …window.activate())`), with a trace.
6. Cosmetics from L7: register the one provider instance (`AddSingleton<IDispatcherProvider>(_ =>
   (SailfishDispatcherProvider)DispatcherProvider.Current)`) to silence MAUI's "Replaced an existing
   DispatcherProvider"; in `SailfishRuntime.TickDueTimers` catch per timer and advance `_nextTick` before `Tick`.

Tests: new `ActivationGateTests` (Active→Inactive→Active raises Stopped then Resumed; Active→Suspended; startup with
`-1`; quit sequence Deactivated/Stopped/Destroying once; no startup Resumed); `LifecycleTests` keep passing.
Legs: `page nav features f4` plus a hand check: minimize the sample to its cover and back, read `OnSleep`/`OnResume`
in the log (`SailfishLifecycle` counts are in `NativeEventCounts`).
Acceptance: the lifecycle table in `add-sailfish-to-existing-app.md` matches the log; `Window.Destroying` observed on
home-screen close.

### M2. Back button through MAUI's veto chain (L3) — S/M

Goal: `Page.OnBackButtonPressed`, `Shell.BackButtonBehavior.Command`, `NavigationPage.OnBackButtonPressed` and the
modal default all run before anything pops.

Files: `Platform/SailfishMauiApplication.Boot.cs:188-195` (`RouteHostEvents` key handler),
`Platform/QtHost/QtHostPageRenderer.cs:606-623` (`TryPop`), `Platform/QtHost/QtHostPageRenderer.Navigation.cs:99-115`,
`Platform/SailfishPage.cs` (new attached property), `Platform/QtHost/qml/MauiModelPage.qml` (`backNavigation`).

Steps:
1. Key handler: `if (((IWindow)window).BackButtonClicked()) return;` on the dispatcher, then `TryPop()` only when
   MAUI did not handle it (root-level fallback). MAUI's defaults already pop modals and NavigationPage/Shell stacks,
   so the native pageStack follows through the existing request path (`NoteNavigationRequest`).
2. Swipe-back: it cannot be vetoed after the fact (Silica pops first, like the iOS edge swipe). Add
   `SailfishPage.BackNavigation` (attached bool, default true) → model page `backNavigation: false`, so an app that
   needs a confirm flow turns the gesture off and shows its own control. Document in `sailfish-apis.md`.
3. Make sure `BackButtonBehavior.IsEnabled=false`/`HasBackButton=false` still disable the gesture (today honoured).

Tests: harness test "a page whose OnBackButtonPressed returns true is not popped by Back"; "Shell BackButtonBehavior
Command runs instead of the pop"; "a modal is popped by Back through MAUI, not TryPop".
Legs: `nav navback shell tabpulley` + injected Escape key in the `navback` leg (the Diagnostics runner has the key
injection; extend `QtHostDiagnosticsRunner.NavBack.cs` with a vetoing page).
Acceptance: the veto test page stays on screen after Back; `TryPop` runs only when `BackButtonClicked()` returned
false (count it).

### M3. `DataTemplateSelector` on the list path (C1) — S

Files: `Platform/QtHost/QtHostCollectionBridge.cs:565-580` (`CreateFromTemplate`),
`Platform/QtHost/QtHostListAdapter.Rows.cs` (`BuildSignature`, `Reusable`, `:43-66`), `Slots.cs` (header/footer/empty
templates), `Rows.cs:337-365` (group templates).

Steps: resolve `template.SelectDataTemplate(item, owner)` before `CreateContent()` for every template site; include
the selected template (reference) in the row signature so a selector that changes its answer rebuilds that row;
the pool already keys on host shape, so mixed templates pool separately. Log once per list when `CreateContent`
still throws (a selector returning null).
Tests: `CollectionBridgeTests`: a selector with two templates creates both shapes; the empty-view and group-header
templates through a selector; an item whose selected template changes after an INCC `Replace`.
Legs: `collection collection100 containers f3`; add a selector list to the sample's `CollectionAdvancedPage` and a
check in the `collection` leg.
Acceptance: the selector list renders every row; `RowsPooled`/`RowsAdopted` unchanged on the Kitchen tour.

### M4. Legacy `ListView`, `TableView` and cells (C2) — XL

Goal: Xamarin.Forms-era apps that still use `ListView`/`TableView` show their rows; MAUI 11 removed the
Compatibility renderers, so a platform must own this.

Files: new `Handlers/SailfishLegacyListViewHandler.cs` (row `Row<ListView, …>` and `Row<TableView, …>` before
`ItemsView` in `SailfishHandlersFactory.ViewHandlers`), `Platform/QtHost/QtHostListAdapter*.cs` (a second item
source: `TemplatedItemsList`), `Platform/QtHost/QtHostCollectionBridge.cs`, `Handlers/Snapshots/AdapterSnapshots.Containers.cs`,
`qml/containers/ListView.qml` (separators), `qml/interactions/ContextMenu.qml`, `docs/porting-existing-apps.md`.

Steps:
1. Owner decision first (§5 D2): full legacy support or the minimal set (TextCell/ImageCell/ViewCell, tap, grouping,
   pull-to-refresh, ContextActions); the rest documented as unsupported.
2. Handler: adapt `ListView.TemplatedItems` onto the list adapter: cells from `TemplatedItems.GetOrCreateContent`,
   `ViewCell.View` as the row content, built-in row templates for `TextCell` (Text/Detail/colors), `ImageCell`,
   `SwitchCell`, `EntryCell`; `NotifyRowTapped` → `ItemTapped`/`ItemSelected`; `ScrollToRequested` → `scrollTo`;
   `IsPullToRefreshEnabled/IsRefreshing/RefreshCommand` → the refresh surface; `SendCellAppearing/Disappearing` on
   (un)materialize; `HasUnevenRows/RowHeight`, `SeparatorVisibility/Color` (a thin `Rectangle` per delegate),
   `Header/Footer` (+templates), `IsGroupingEnabled/GroupDisplayBinding/GroupHeaderTemplate`.
3. `Cell.ContextActions` → the existing context menu (`__openContextMenu`) on delegate long press; `MenuItem.IsDestructive`
   → remorse (Silica idiom) is optional.
4. `TableView`: flatten `Root` into adapter rows, `TableSection.Title` as group headers, `Intent` ignored.
5. `CachingStrategy`: `RecycleElement` maps to the row pool, `RetainElement` to one-view-per-item (today's model).
Tests: harness tests for each cell type, grouping, tap → `ItemSelected`, `ContextActions`, `TableView` sections.
Legs: new `legacylist` leg in the Diagnostics runner (a ListView with the four cell kinds, a grouped one, a
TableView), `collection`, `containers`.
Acceptance: the ported apps that use `ListView` (find them with `grep -l "<ListView" ~/Projects/*/**/*.xaml`
across the app-test checkouts, `docs/app-test-campaign.md`) show rows; porting guide updated.

### M5. `RefreshView` on pages with a pulley (C3) — S

Files: `Platform/QtHost/QtHostPageRenderer.Layout.cs:638-650` (`RefreshAncestorOf`),
`Platform/QtHost/QtHostPageRenderer.Interactions.cs` (pulley building), `qml/interactions/PullDownMenu.qml`,
`qml/lib/pulley.js`.
Steps: when a `RefreshView` exists on the page but the pulley owns the overscroll, add a "Refresh" `MenuItem` at the
top of the pull-down menu (localizable through the app's resources, default text from `Microsoft.Maui.Controls`'
resource if any, else "Refresh") that sets `IRefreshView.IsRefreshing = true`; show the busy indicator as today.
Tests: harness test "a page with ToolbarItems and a RefreshView gets a Refresh pulley entry that sets IsRefreshing".
Legs: `pulley tabpulley shell` + a RefreshView in the sample's Shell page.
Acceptance: Kitchen (Shell with flyout) refreshes from the pulley.

### M6. Image sources through MAUI's `IImageSourceService` (G1, G2) — L

Goal: a library's `IImageSourceService<T>` registration works; `Image.IsLoading` is real; in-flight loads cancel;
stream cache files are cleaned; GIF streams animate.

Files: `Platform/QtHost/QtHostImages.cs:192-313`, `Platform/QtHost/QtHostImageSources.cs`,
`Hosting/AppHostBuilderExtensions.cs` (`ConfigureImageSources`), `Handlers/SailfishDrawingHandlers.cs:36-57`,
`Handlers/Snapshots/AdapterSnapshots.Button.cs:43`, `AdapterSnapshots.Values.cs:43`, `QtHostPageRenderer.cs:990`,
`qml/controls/Image.qml:41-50`, `src/Linux.SailfishOS.SkiaSharp/SkiaImageSources.cs`.

Steps:
1. Define `ISailfishImageSourceService : IImageSourceService` with
   `Task<IImageSourceServiceResult<string>?> GetUrlAsync(IImageSource source, CancellationToken ct)` (a `file://` or
   `http(s)://` URL Qt loads; the result's dispose deletes a temp file).
2. Four defaults (`IFileImageSource`, `IFontImageSource`, `IStreamImageSource`, `IUriImageSource`) registered in
   `SetupSailfishDefaults` through `builder.ConfigureImageSources(s => s.AddService<…>())`, so MAUI's
   `ImageSourceServiceProvider` type mapping knows them; match on the **Core interfaces**, not the Controls classes.
3. `QtHostImages.Resolve` → `MauiContext.Services.GetRequiredService<IImageSourceServiceProvider>()
   .GetImageSourceService(source.GetType())`; run a Sailfish-shaped service through the existing pending/`WhenReady`
   machinery; raise `IImageSourcePartEvents.UpdateIsLoading(true/false)`; cancel the previous load on a source change
   (mirror Core's internal `ImageSourceServiceResultManager`).
4. Keep `QtHostImageSources.Register(Func…)` as a thin adapter wrapping the func into a service (SkiaSharp keeps
   working); document the new extension point in `custom-controls.md`.
5. Stream cache: write `.gif` when the header says GIF (`ImageHeader`), delete files on result dispose and sweep
   `cache/streams` at startup; respect the cancellation token.
6. Downsampling: give unsized/`Center` images a `sourceSize` cap (the display size) and document.
Tests: `ImageMeasureTests` + new tests: a custom `ImageSource` with its own service resolves; `IsLoading` toggles;
a swapped source cancels the first load; the stream file is deleted on dispose.
Legs: `f3` (images part), `collection100` (remote images), `skia`.
Acceptance: CommunityToolkit `GravatarImageSource` (or a test source) renders; `cache/streams` does not grow across a
Kitchen tour.

### M7. Package identity and release hygiene (B1, B6) — S + owner decision

Steps: pick the new prefix (§5 D1), rename the five ids (`PackageId` only; namespaces/assembly names can stay),
update `Linux.SailfishOS.Workload/Program.cs:16`, `tools/cmd/pack-local.sh`, the manifest `packs` key, template.json,
README and `add-sailfish-to-existing-app.md`; fix `PackagePath="runtimes/linux-arm64/native/"` for the launcher; add
`IncludeSymbols`+`SymbolPackageFormat=snupkg`, `PublishRepositoryUrl`, `EmbedUntrackedSources`; decide a prerelease
versioning scheme (MinVer or `0.1.0-ci.<n>`) so pack-local stops clearing the cache.
Tests: `ToolsPackagingTests`, `WorkloadToolTests` updated; a pack-layout test that opens the nupkg and asserts the
launcher path.
Acceptance: `tools/sf pack-local` → template app restores and runs on the phone (`SailfishRun`).

### M8. Collections behaviour gaps (C4, C5, C6) — L

Files: `Platform/QtHost/QtHostListAdapter.cs`, `.Rows.cs`, `.Scroll.cs`, `.Input.cs`, `QtHostCollectionBridge.cs`,
`qml/containers/ListView.qml`, `qml/containers/CarouselView.qml`, `tests/…/HandlerParityTests.cs`.
Steps (each its own commit):
1. `ItemsUpdatingScrollMode`: after `PushRows` when the count grew, `KeepLastItemInView` → `scrollTo(last, End)`,
   `KeepScrollOffset` → pin `contentY`.
2. `ItemsLayout.PropertyChanged` subscription (span/spacing on the same object) → `Invalidate`.
3. VSM: `VisualStateManager.GoToState(root, "Selected"/"Normal")` in `RecomputeSelection`; carousel
   `CurrentItem/NextItem/PreviousItem/DefaultItem` in `OnCarouselPosition`; raise the native highlight above the
   content or make it optional (device screenshot decides).
4. `ScrollTo(animate:)` → animate `contentY`; `Scrolled` deltas from the last reported offset; remove the dead
   `IScrollViewController` cast.
5. `SnapPointsType/Alignment` → `snapMode` + `preferredHighlightBegin/End`.
6. Carousel: `IsScrollAnimated` → `highlightMoveDuration`; `VisibleViews`/`IsDragging`/`IsScrolling` from
   `movementStarted/Ended` and the materialized delegates; vertical `Loop` (Path) or a documented limit.
7. `ItemSizingStrategy.MeasureFirstItem` honoured; template off-screen rows lazily in `RequestMaterialize`.
8. CollectionView inside ScrollView: `interactive: false` and lift the 192 cap when the list is unbounded along its
   axis, or warn once; document the pattern in the porting guide.
9. Add `CollectionView→CollectionViewHandler` and `CarouselView→CarouselViewHandler` to `HandlerParityTests.Pairs`
   with a pinned `KnownGaps` list, then burn the list down.
Tests: `CollectionBridgeTests` per step. Legs: `collection collection10 collection100 collection500 containers f3`.
Acceptance: parity doc includes the two items handlers; `collection500` timings recorded in `profiling.md`.

### M9. Canvas and `GraphicsView` correctness (G3, G4, G5, G9) — M

Files: `Platform/QtHost/QtHostCanvasRecorder.cs`, `QtHostGraphics.cs`, `qml/shapes/GraphicsView.qml`,
`qml/shapes/Shape.qml`, `qml/effects/MauiLayerEffect.qml`, `Platform/QtHost/QtHostInput.cs`,
`Handlers/SailfishDrawingHandlers.cs`, `Handlers/SailfishSnapshotHandler.cs`.
Steps:
1. Device check first: `DrawString("x",0,0,Left)` next to a Label, an EvenOdd star through `FillPath`, and
   `Antialias=false` — screenshots settle the three "unverified" items.
2. `DrawString` vertical anchor as Android (`y − fontSize` top); `Font` through `QtHostFonts.Resolve`;
   `ctx.fillRule = Qt.OddEvenFill` around EvenOdd fills/clips in the three QML files; apply `antialias`.
3. Interaction callbacks: route press/move/release for `GraphicsView` hosts through the input router to
   `IGraphicsView.StartInteraction/DragInteraction/EndInteraction/CancelInteraction` (PointF[] in dp; cancel on
   flickable steal).
4. Re-record policy: record only on `Invalidate`, drawable/property change and size change, not on every reconcile
   (`SailfishSnapshotHandler.cs:123` path); chunk the command list instead of truncating at 4096; fix the stale
   comment in `QtHostGraphics.cs:8-9`.
5. Host tests for the recorder (every member recorded as expected), `QtHostShapes.ShapeProps/PathOps`, span HTML,
   glyph/stream URLs, cache policy.
Legs: `shapes visual f3` + a new interactive-GraphicsView check in the `input` leg.
Acceptance: a GraphicsView chart sample receives drag; screenshots match Android anchoring within 1 px.

### M10. Gradient backgrounds and shadow brushes everywhere (G6) — M

Files: `Platform/QtHost/QtHostPaint.cs`, `QtHostVisualState.cs:160-190,231-240`, `host_handles.cpp:114-240` (the
generic background rectangle), `qml/effects/MauiLayerEffect.qml`, a new `qml/effects/GradientFill.qml`.
Steps: when `Background` is a gradient brush on a view without its own paint path, create a QtGraphicalEffects
`LinearGradient`/`RadialGradient` item under the host (the BoxView square path already does this) sized by the
geometry pass; `Shadow.Brush` gradient → use its average colour (as iOS does) rather than dropping the shadow.
Tests: `AdapterSnapshotsTests`/visual-state tests for the new props. Legs: `visual shapes controls`.
Acceptance: the sample's gradient page background renders (add one).

### M11. Text: spans, auto-scaling, mixed-font measure (G7, L7-17) — M

Files: `Handlers/Snapshots/AdapterSnapshots.Label.cs:95-125`, `Platform/Text/LabelTextMapper.cs`,
`Handlers/SailfishMeasure.cs:111-135`, `Handlers/SailfishFontRules.cs`, `qml/controls/Label.qml`,
`Platform/QtHost/QtHostTextMetrics.cs`, `Native/host_text.cpp`.
Steps: per-span `BackgroundColor` (`<span style="background-color">`), `CharacterSpacing` (letter-spacing in the
HTML), `LineHeight`, `TextTransform`; span `GestureRecognizers` through `Text.linkActivated` with one `href` per
span; measure FormattedText per run (one `QTextLayout` with formats, `host_text.cpp` gains a runs argument);
`FontAutoScalingEnabled`: probe Silica's text-size ratio (`Theme.fontSizeMedium` against its default) once in the
`theme` service and multiply explicit sizes when the flag is true, in `PaintFontSizeDp` and in `SailfishMeasure`.
Tests: span HTML tests; measure tests with two fonts. Legs: `text controls`.
Acceptance: a two-size FormattedText row does not clip; a span tap fires.

### M12. Soft input and keyboard avoidance (L4) — M

Files: `Platform/SailfishMauiApplication.cs:188-193`, `qml/MauiShell.qml:142-151`, `qml/MauiModelPage.qml`,
`Platform/QtHost/QtHostPageRenderer.Layout.cs`, new `Platform/SailfishKeyboard.cs`, `qml/lib/textinput.js`,
`docs/sailfish-apis.md`, `docs/porting-existing-apps.md`.
Steps:
1. Device check: does the Silica flickable scroll a focused Entry above the Maliit panel for these adapters? One
   screenshot with a long form.
2. Feed the keyboard height into the model page as a bottom inset (shrink the content height while `visible`), request
   a layout pass; scroll the focused host into view when the page itself is not a flickable.
3. `SoftInputExtensions` cannot be intercepted (static, no seam): provide `SailfishKeyboard.Hide()`/`Show(view)`
   (Qt `inputMethod.hide()/show()` + focus) and document that the MAUI 8+ API throws on this TFM; open an upstream
   issue asking for an `ISoftInputService` seam (link it in the doc).
Tests: harness test for the inset → layout pass. Legs: `input text`.
Acceptance: the last Entry of a long form stays visible with the keyboard open.

### M13. Theme and locale seams (L5) — S

Files: `Platform/SailfishEssentials.cs` (`SailfishTheme`), `Platform/Essentials.cs:440-452` (`SailfishAppInfo`),
`Platform/SailfishMauiApplication.Boot.cs`, `qml/MauiShell.qml` (theme service payload), `docs/sailfish-apis.md`.
Steps: read the ambience's colour scheme synchronously as the first thing the shell does and hand it to
`SailfishTheme` before the first render (or defer the first render until the theme service answered; it already
runs first in `OnHostReady`), so `Application`'s first `RequestedTheme` is right; expose
`SailfishTheme.HighlightColor`/`PrimaryColor` (probed, updated on `svc-theme-changed`); report
`Qt.application.layoutDirection` into `SailfishAppInfo.RequestedLayoutDirection`; document the `LANG`/ICU
dependency in `sailfishos-packaging.md`.
Tests: `LifecycleTests`/`EssentialsTests` for the initial theme and RTL direction. Legs: `silica features`.
Acceptance: a dark-ambience start shows no light frame (recording).

### M14. Essentials member-level gaps (E1–E7) — M

Files: `Platform/Essentials.cs` (`SailfishAppInfo`, `SailfishLauncher`), `Platform/SailfishAppMeta.cs`,
`buildTransitive/Microsoft.Maui.SailfishOS.targets:98-134` (`_SailfishWriteAppMeta`), `Platform/SailfishEssentialsRegistry.cs`,
`Platform/SailfishShareAndPickers.cs` (Share, pickers, Map, Email, Screenshot, Permissions), `Platform/SailfishSensors.cs`
(`SailfishGeolocation.OnFix`), `Platform/SailfishEssentials.cs` (`SailfishDeviceDisplay`), `Native/sailfish_host.cpp`
(clipboard, JPEG save, `QScreen::refreshRate`), `Platform/SailfishPage.cs`, new
`Platform/PlatformConfiguration/SailfishOSSpecific/Page.cs`, `docs/porting-existing-apps.md`, `docs/sailfish-apis.md`.

Steps, each its own commit with a host test:
1. App identity: bake `ApplicationId`, `SailfishPackageName`, `ApplicationDisplayVersion` and `ApplicationVersion`
   into `qml/maui-appmeta.json`; `SailfishAppInfo` reads them (`PackageName`, `VersionString`, `BuildString`,
   `PackagingModel = Packaged`), `Version` through `TryParse`. Add the registry row
   `TryAddSingleton<IVersionTracking>(_ => VersionTracking.Default)` so DI and the overlay resolve it. Test: a
   `VersionTracking` instance over the Sailfish `Preferences` sees a version change between two `Track()` calls.
2. `IViewScreenshot` on `SailfishScreenshot`: `CaptureViewAsync(object platformView)` takes the `NativeElementHost`,
   grabs the host (`QQuickItem::grabToImage` through `sailfish_host_invoke`, or crops the window PNG to the host's
   geometry as the fallback) and returns an `IScreenshotResult`; JPEG with quality in the shim; delete the PNG on
   dispose. Leg `f4` gains a view capture check.
3. Defects (one commit): `Launcher.TryOpenAsync => OpenAsync`; `Map.TryOpenAsync` returns the real `openUrl` result;
   `Share` sends text and URI together (Android concatenates); `Email` with attachments → `FeatureNotSupportedException`
   (as Windows) or Sailfish.Share; `ShowSettingsUI` → FNS; `MediaPickerOptions.SelectionLimit`/`Title` honoured;
   `MinimumDistance` filtered in `OnFix` with `Location.CalculateDistance` against the last raised fix,
   `DesiredAccuracy` → `preferredPositioningMethods`; `RefreshRate` from `QScreen::refreshRate()`;
   `ClipboardContentChanged` from a shim `QClipboard::dataChanged` → `svc-clipboard-changed` event.
4. Permissions: verify the Sailjail names on the phone (`/etc/sailjail/permissions/`, `AppLaunch` in particular),
   make `Permissions.Flashlight` Denied when sandboxed, let the pickers `Demand` `Pictures`/`Videos`/`Documents` when
   sandboxed; document that the instance form of `Permissions.X` throws on this TFM and the generic form must be used
   (porting guide "Not supported yet").
5. `Early: true` for `SecureStorage` and `DeviceDisplay`; remove the dead screen-reader branch (a registry row);
   optional `IAppleSignInAuthenticator` FNS row; log when the `Boot.cs:93` guard skips the MainThread hook.
6. Platform configuration: add `Microsoft.Maui.Controls.PlatformConfiguration.SailfishOS` (marker) and
   `SailfishOSSpecific.Page` with `AllowedOrientationsProperty` + `SetAllowedOrientations`/`GetAllowedOrientations`
   extensions on `IPlatformElementConfiguration<SailfishOS, Page>`; keep `SailfishPage.AllowedOrientations` as an
   `[Obsolete]` forwarder for one release; document `page.On<SailfishOS>()` in `sailfish-apis.md`.
Tests: `EssentialsTests`/`MoreEssentialsTests` per step; `PublicSurface.txt` updated (new namespace).
Legs: `f4 features`.
Acceptance: `VersionTracking.CurrentVersion` equals the RPM version on the phone; `view.CaptureAsync()` returns a PNG
of the right size; the matrix `f4` leg green.

### M15. Handlers and elements (H1–H7, N7 badges) — M

Files: `Handlers/SailfishHandlersFactory.cs`, `Handlers/SailfishApplicationHandlers.cs`,
`Handlers/SailfishNavigationViewHandler.cs`, `Handlers/SailfishPageContainerHandlers.cs`,
`Handlers/Snapshots/AdapterSnapshots.Containers.cs` (`SwipeItemsJson`), `qml/controls/SwipeView.qml`,
`Platform/QtHost/QtHostVisualState.cs:220-228` (RTL root), `Platform/QtHost/QtHostPageRenderer.Interactions.cs:220-245`,
`qml/interactions/ContextMenu.qml`, `qml/MauiModelPage.qml` (tab row), `tests/…/HandlerParityTests.cs`,
`docs/silica-parity.md:46`, `docs/porting-existing-apps.md`.

Steps (each its own commit):
1. `SwipeItem`: emit `fg` (TextColor), `iconColor` (QML `ColorOverlay`), font keys; watch each `SwipeItem`'s
   `PropertyChanged` in the SwipeView handler so runtime changes re-push; keep Top/Bottom as a warned limit.
2. `TabbedPage.BadgeText/BadgeColor/BadgeTextColor`: `SailfishTabbedPageHandler.Tabs` carries badge text and colours
   per tab (listen to each child's `PropertyChanged` for the three attached keys); the tab row draws a small badge
   (text pill, or `"Inbox (3)"` as the cheap first step). Tab icons: fall back to `Route`/type name when `Title` is
   empty. Tab row in a horizontal `SilicaFlickable` once `count > 4`.
3. `Window`: map `Title` → cover/window title; map `FlowDirection` → treat `IWindow.FlowDirection` as the RTL root in
   `IsRightToLeft` and request a layout pass; `StatusBarTheme` an explicit traced no-op. Add the mapper constructors to
   `SailfishApplicationHandler`/`SailfishWindowHandler`; `ActivateWindow` (M1.5).
4. `SailfishNavigationViewHandler`: `public static new readonly CommandMapper<IStackNavigationView, …> CommandMapper`
   as the one the handler uses; keep `NavigationCommandMapper` as an alias. Override `NeedsContainer => false` on the
   Sailfish view handlers so `HasContainer` is honest.
5. Change notifications: `MultiPage.PagesChanged` → `RequestPoll`; Shell `Items`/`FlyoutItems`/`FlyoutBehavior` keys →
   `MapModelPage`; `Compatibility.Layout.LayoutChanged` → `RequestSubtree`; `ContentPage.HideSoftInputOnTapped` in the
   router (`OnPress` outside a text adapter → `Unfocus()` the focused `InputView`).
6. Context menus: `MenuFlyoutSubItem` children rendered inline under a `SectionHeader`; separators skipped or emitted
   as `{separator:true}`; icon-only `ToolbarItem` falls back to `AutomationId`/`SemanticProperties.Description`, else
   warns once (Silica pulleys are text-only).
7. `HybridWebView`: owner decision D5; (a) `SailfishHybridWebViewHandler` on the Gecko adapter (serve `HybridRoot`
   over a file URL, inject `HybridWebView.js`, route `window.external.sendMessage` → `RawMessageReceived`, answer the
   three commands), or (b) warn once and document next to BlazorWebView and `Maps.Map`/`MediaElement`.
8. Parity test: add `CollectionView`, `CarouselView` and the six Controls shape handlers to `HandlerParityTests.Pairs`;
   fix `silica-parity.md:46` (ListView is not native until M4).
Tests: `HandlerFixTests` per step; `HandlerParityTests` regenerated. Legs: `controls containers shell tabpulley visual`.
Acceptance: a SwipeItem with `TextColor`/`IconColor` renders in colour (screenshot); a TabbedPage badge shows.

### M16. `Toolbar` handler and Shell chrome (N1, N2, N3) — L

Goal: the page chrome is driven by MAUI 11's `IToolbar` as on the other platforms, which closes `HasNavigationBar`,
`NavBarIsVisible`, `TitleView`, `Shell.ToolbarItems`, `Priority` ordering and `BackButtonVisible` in one place, and
gives `SearchHandler` a home.

Files: new `Handlers/SailfishToolbarHandler.cs` (`ElementHandler<IToolbar, object>`), `Handlers/SailfishApplicationHandlers.cs`
(`MapContent` creates it from `(window as IToolbarElement).Toolbar?.ToHandler(context)`),
`Platform/QtHost/QtHostPageRenderer.cs:930-998,1208-1231` (`PageChromeOps`), `QtHostPageRenderer.Interactions.cs:21-111`
(`AddSyntheticHosts`, `WatchToolbarItems`), `qml/MauiModelPage.qml` (header, tab rows), new `qml/controls/ShellSearch.qml`,
`Handlers/SailfishPageContainerHandlers.cs` (Shell), `docs/porting-existing-apps.md`, `docs/silica-parity.md`.

Steps:
1. Device check first: `HasNavigationBar=false` cannot simply hide the `PageHeader` (`QtHostPageRenderer.cs:1221-1223`
   records Canvas shapes not reaching the screen on a page without a rendered header). Try a 0-height header that
   keeps the item alive; screenshot.
2. `SailfishToolbarHandler` with mapper keys `Title`, `IsVisible`, `BackButtonVisible`, `BackButtonTitle` (n/a, traced),
   `TitleView`, `ToolbarItems`; it feeds `PageChromeOps` and replaces the hand-rolled `WatchToolbarItems` and the direct
   `Page.ToolbarItems` reads; `ToolbarItems` come Priority-sorted and include Shell-level items.
3. `TitleView` → a host inside the chrome under (or instead of) the `PageHeader` title.
4. `Shell.SearchHandler` → a Silica `SearchField` under the header (the `SearchBar` adapter exists): `Query` two-way,
   `SearchBoxVisibility`, `Placeholder`, `ShowsResults` + `ItemsSource`/`ItemTemplate` as a dropdown list on the
   list adapter, `SelectedItem`, `Command`. Warn once until built.
5. `Shell.TabBarIsVisible` (per page) → hide the tab row; `NavBarIsVisible=false` → the header path from step 1.
6. `FlyoutIsPresented=true` from code → open a Silica `ContextMenu`/`Menu` with the flyout entries from the header
   (owner decision D15), or warn once; warn once per Shell when `FlyoutHeader/Footer/Content/ItemTemplate` is set.
   Document "flyout = pulley, text only".
Tests: harness tests for `IsVisible`, `TitleView`, Shell-level items, Priority order, `SearchHandler` query write-back.
Legs: `page nav shell tabpulley pulley silica`.
Acceptance: Kitchen's search (if any) or a sample `SearchHandler` filters a list on the phone; an onboarding page with
`HasNavigationBar=false` shows no header (screenshot).

### M17. Input router correctness and gesture coverage (I2–I10) — L

Files: `Platform/QtHost/QtHostInput.cs`, `Platform/QtHost/QtHostPageRenderer.Layout.cs:19-52,347-348` (hit-test),
`Platform/QtHost/QtHostVisualState.cs:57` (`TryInvert`), `Platform/QtHost/AdapterEventRouter.cs`,
`Platform/QtHost/QtHostListAdapter.Input.cs`, `qml/containers/ListView.qml:370-380`, `qml/controls/Button.qml`,
`Image.qml`, `Slider.qml`, `Editor.qml`, `qml/containers/ContentView.qml`, `Border.qml`,
`Handlers/Snapshots/AdapterSnapshots.TextInput.cs:127-153`, `Handlers/Snapshots/AdapterSnapshots.Label.cs`.

Steps (each with a FakeShim router test in the style of `SampleAppRegressionTests.cs:641-714`):
1. Swipe: `(swipe.Direction & direction) != 0` and `Math.Abs(total) >= Math.Max(swipe.Threshold, SwipeMinDp)`, or
   call `ISwipeGestureController.SendSwipe` + `DetectSwipe`.
2. Hit-testing: skip `InputTransparent` hosts and `Layout { InputTransparent, CascadeInputTransparent }` subtrees so the
   next candidate wins; inverse-map the point through the host's `toRoot` affine into local space (bounding box as the
   first filter) so `Scale`/`Rotation`/anchors hit correctly.
3. Positions: one `getPosition(relativeTo)` shared by Tap/Pointer/LongPress using `MauiLogicalBounds`; honour the
   `Buttons` mask.
4. Consumed adapters: after `NativeConsumed`, still run `TryFindRecognizers` on the parent chain for Tap/LongPress and
   dispatch on release (no capture), so a `TapGestureRecognizer` on a `Button`'s ancestor fires as on Android.
5. Rows: delegate `MouseArea.onPressAndHold` → `list-item-held` → `TryFindRowLongPress`; `list-item-pressed` hands a
   row press to the router so Pan/Swipe/Pointer owners inside templates capture (`preventStealing` while captured).
6. Spans: `Label.CompositeGestureRecognizers` (`ChildGestureRecognizer`) → spans with gestures rendered as
   `<a href="span:N">`, `linkAt(x,y)` in `Label.qml` reports an adapter event (shared with M11).
7. LongPress: `AllowableMovement`, `Canceled` on early release/movement/pinch, optional `Running`.
8. Events: `Button`/`ImageButton` `Pressed`/`Released` (`pressedChanged`), `Slider.DragStarted/DragCompleted`,
   `SwipeView.SwipeChanging`, `Editor.Completed` on focus loss (as Android/iOS).
9. Text: `ImhNoAutoUppercase` for `KeyboardFlags` without a Capitalize flag; `ReturnType.Next` focuses the next
   `InputView` in visual order.
10. Silica conflicts: gesture-owner hosts get a `MouseArea { preventStealing: true }` toggled by a `mauiCaptures` prop
    pushed when `GestureRecognizers.Count > 0`, or `backNavigation=false`/`interactive=false` on the enclosing
    flickable for the duration of a captured drag. Device check: a horizontal Pan no longer drags the page.
11. Optional: register an `IGesturePlatformManagerFactory` (public, DI) so the router gets a per-view hook when
    recognizers change instead of walking `GestureRecognizers` at press time (MAUI's designed seam for backends
    without `IPlatformViewHandler`); verify it is consulted on the plain TFM first.
Legs: `input visual containers collection`. Acceptance: the `input` leg gains a swipe-flags check, an
`InputTransparent` fall-through check, a scaled-view tap and a row long-press.

### M18. `IImage`, `DrawImage`, `View.CaptureAsync` (G8) — L + owner decision

Owner decision §5 D6 first: a QImage-backed `IImage` through the shim (`sailfish_host_invoke` to decode/encode/
resize, pixels never cross to managed) or a Skia-backed one in the SkiaSharp package (only apps that reference it).
Then: `ICanvas.DrawImage` draws it (the Canvas adapter takes a file URL or texture id), `SetFillPaint(ImagePaint)`,
`IScreenshotResult → IImage`, `IViewScreenshot.CaptureViewAsync(platformView)` with `NativeElementHost` → the host's
`grabToImage`. Legs: `shapes f4`.

### M19. Build integration on MAUI's public hooks (B2, B3) — M

Files: `buildTransitive/Microsoft.Maui.SailfishOS.targets:59-67,125-199`, `WorkloadManifest.targets.in`,
`docs/add-sailfish-to-existing-app.md`.
Steps:
1. Resizetizer: set `ResizetizerPlatformType=wpf`, hook `ResizetizerAfterAssetProcessingTargets`, consume
   `@(MauiProcessedImage/Asset/Font)`; keep the `maui-resized.txt` writer and the icon-set task; delete the private
   `ResizetizeImages` call and the manual `MauiAsset`/`MauiFont` items. Check where `Resources/Raw` lands against
   `QtHostImages.ResolveFilePath` and `FileSystem.OpenAppPackageFileAsync`.
2. TPI side effects: add `SAILFISH1_0`/`SAILFISH1_0_OR_GREATER` defines, `<SupportedPlatform Include="sailfish" />`
   and an `AssemblyAttribute` `SupportedOSPlatform("sailfish1.0")` on the head; try whether `UseMauiEssentials`/
   `UseMauiCore`-style properties avoid NETSDK1186 without clearing the TPI (binlog), and document the result.
3. Manifest: condition RID/`SelfContained` on `'$(OutputType)' != 'Library'`; turn `_SailfishRefreshRestoreForRid`'s
   nested restore into an error with the command to run.
4. Document the clean-machine limitation of `dotnet workload install sailfish` (`KnownWorkloadManifests.txt` has
   Tizen's id, not ours; option (a) stands).
Tests: an MSBuild evaluation test project (`tests/Linux.SailfishOS.Tests/BuildTargetsTests.cs`: evaluate a temp csproj
with the targets and assert properties/items) — the targets have no test today.
Acceptance: `dotnet build -f net11.0-sailfish` of the template with `-bl` shows the resizetizer external targets ran;
class library with the TFM builds without a RID.

### M20. Trimming profile and feature switches (B4) — S

`IsTrimmable=true` on the backend (the AOT doc reports 0 IL warnings from repo code); narrow `scaffold/trimmer.xml`
to the app assembly (a generated descriptor from `SailfishGenerateMain`) or replace reflection discovery with a
generated attribute; write the feature-switch table from the audit into `aot-and-trimming.md`; evaluate partial
composite R2R as Android does (`Controls.targets:71-78`), measure with `tools/sf package-test --trimr2r`.

### M21. `dotnet run`, `dotnet watch`, hot reload, device tests (B5, L8) — L + owner decision

1. `dotnet run -f net11.0-sailfish`: set `RunCommand`/`RunArguments` on the head to the packaged `tools/sf deploy
   --run` path (log streaming, Ctrl+C → `sf kill`), so the MAUI 11 `dotnet run` flow works; `dotnet watch` then
   restarts on edits even without hot reload.
2. Hot reload (owner decision §5 D7): a startup hook in the Debug RPM (`DOTNET_MODIFIABLE_ASSEMBLIES=debug`,
   `DOTNET_STARTUP_HOOKS`) listening on a port forwarded over SSH, receiving deltas from a host-side agent built on
   `dotnet watch`'s `Microsoft.DotNet.HotReload` client (or `dotnet-watch`'s browser-refresh protocol) and applying
   `MetadataUpdater.ApplyUpdate` + `MauiHotReloadHelper.UpdateApplication`; MAUI 11's XAML incremental hot reload
   generator then works through the same `MetadataUpdateHandler`. Until built: set
   `EnableMauiIncrementalHotReload=false` on the head to keep Debug builds lean.
3. Device `dotnet test`: RC1 ships no runner; a `sailfishtest` template needs a Microsoft.Testing.Platform runner
   inside the RPM plus `tools/sf` streaming the result file. Defer unless the owner wants it; the matrix stays.
4. Env switches: generate the `MAUI_SAILFISH_*` table in `tools.md` from `SailfishEnv` by a test.

### M22. Tools out of the package, Windows hosts (B5) — M + owner decision

The 26 shell/python files in `buildTransitive/net11.0/tools` make the package Unix-only. Options: a separate
`*.Tools` package, or a dotnet tool (`sailfish-tools`) that hosts the device loop in C# (the Python RPM builder
already exists). Owner decision §5 D8.

### M23. Tests and CI (B7) — M

Host-only CI (build the slnx, `dotnet test`, `dotnet pack -p:SailfishAllowMissingShim=true`, template `dotnet new` +
build) needs no phone and no runner; add `.editorconfig` + `dotnet format --verify-no-changes`; MSBuild evaluation
tests (M19); a template smoke test; `ActivationGateTests` (M1). Device matrix stays manual until a self-hosted
runner exists (decided).

### M24. Template alignment (B8) — S

Add the official symbols the Sailfish template dropped (`applicationId`→`ApplicationId`, `Framework`, a fresh
`PhoneProductId` guid), restore `UseMaui=true` on the head when the MAUI SDK is present, and decide (§5 D9) whether
`Styles.xaml` keeps explicit colours or `OnPlatform`-wraps them so Silica theming shows through.

### M25. `Loaded` semantics (L6) — S now, upstream later

Mitigation now: register an `IWindowCreator` wrapper (or hook `Window.PropertyChanging("Page")`) that attaches the
root page's handler before `IApplication.CreateWindow` parents it; run `QtHostLayout.AttachHandlers` synchronously
from the container handlers' `ChildAdded` so the gap shrinks to one call stack; keep the porting-guide note.
Upstream: propose `IsLoaded => Window != null && IsPlatformEnabled` for the Standard partial (`OnHandlerChangedCore`
already re-runs the wiring), file the issue and link it here.

### M26. Docs that the gaps above contradict — S, do with each package

`add-sailfish-to-existing-app.md:110-116` (lifecycle claims, M1), `porting-existing-apps.md` "Not supported yet"
(legacy ListView/TableView until M4, `DataTemplateSelector` until M3, `SoftInputExtensions`, `View.CaptureAsync`,
`VersionTracking`, Drag & Drop, Map, HybridWebView), `custom-controls.md` (image source services after M6),
`sailfish-apis.md` (`SailfishKeyboard`, `SailfishTheme.HighlightColor`, `SailfishPage.BackNavigation`),
`handler-parity.md` (items handlers after M8), `QtHostGraphics.cs:8-9` stale comment.

### M27. Navigation behaviours and dialogs (N4–N8) — M

Files: `Platform/QtHost/NativeStackCoordinator.cs:112-135,176-178`, `QtHostPageRenderer.Navigation.cs:295-304,421-543`,
`Handlers/SailfishNavigationViewHandler.cs:52-56`, `Platform/QtHost/SailfishModalNavigation.cs:26-28`,
`QtHostPageRenderer.Interactions.cs:380-400`, `qml/MauiModelPage.qml:786-791`, `Platform/QtHost/QtHostAlertSubscription.cs`,
`qml/dialogs/*.qml`, `Handlers/SailfishPageContainerHandlers.cs:47-52,207`, `QtHostPageRenderer.cs:887`.
Steps:
1. Swipe-back veto: when the follow pop did not change the MAUI depth (`MauiDone && Expected > mirror`), re-push at
   once instead of waiting for the 3 s deadline.
2. `animated`: thread `NavigationRequest.Animated` and the modal `animated` flag into `NavOperation` and pick the
   `PageStackAction` from it; animate the last level of a multi-level pop.
3. `InsertPageBefore`/`RemovePage`: operate on the model page *below* the top (`pageStack.pushAttached`/insert, `pop(page)`
   of the removed one) instead of re-rendering the unchanged top.
4. Dialogs: queue them (`Queue<TaskCompletionSource>`, open the next on `CompleteDialog`); let `Step` run with a dialog
   open (dialogs are in-page panels; W1.7 notes the gate is obsolete); hop to the Qt thread in `QtHostAlertSubscription`
   (`QtThread.Run`); pass `FlowDirection` as `mauiMirrored` to `DialogPanel`; map `Keyboard.Email/Url` hints.
5. `FlyoutPage.Detail` swap: disconnect the replaced Detail's handlers in `SailfishFlyoutPageHandler`.
6. Fix the "MAUI core never fires appearing" comment; document the page cache (LRU 4) difference.
Tests: `NativeStackSyncTests`/`NativeStackCoordinatorTests` for 1–3 (the harness needs the follow pop to complete
outside the poll for 1, see the handoff's W1.2 note); dialog queue test. Legs: `nav navback popup shell containers`.
Acceptance: two chained `DisplayAlertAsync` calls show both; `PushAsync(page, animated:false)` shows no slide.

### M28. Drag & Drop (I1) — L

Design in the input audit ("Drag & Drop on top of the router"). Files: `QtHostInput.cs` (`TryFindRecognizers`,
`OnPress/OnMove/OnRelease`), `QtHostPageRenderer.Walk.cs:219-233` (collect `DropGestureRecognizer` owners), new
`qml/interactions/DragGhost.qml`, `qml/MauiModelPage.qml` (ghost layer, `backNavigation`/flickables off while
dragging), `qml/containers/ListView.qml` (row drag source via `list-item-held`), new `SailfishDragEventArgs`/
`SailfishDropEventArgs` subclasses supplying positions. Steps: start on the LongPress timer (Android idiom) →
`SendDragStarting` (honour `Cancel`/`Handled`); ghost from `grabToImage` of the source host; `SendDragOver`/`SendDragLeave`
on target change with `AcceptedOperation` feedback; `await SendDrop` then `SendDropCompleted` (also on cancel). Tests:
router tests asserting the event order and the default text transfer into a `Label`. Legs: `input` + a new DnD check.
Depends on M17.5 for drag sources inside rows.

## 5. Decisions for the owner

The seventeen decisions (D1–D17) are kept in one place, the [Decisions table of the tracker](maui11-tracker.md#decisions):
each is a question with its options (a, b, c, …), the suggested option, the sessions it blocks, and an `Answer` column
for the owner. The packages above name the decision they wait for (for example "owner decision D2"). All seventeen
were answered on 2026-10-07; D11 and D17 got sessions of their own (S59, S60), and D1 chose the maui-labs naming
(`Microsoft.Maui.Platforms.SailfishOS*`), not a new prefix.

## 6. Ground rules and verification (unchanged from the handoff)

- Never commit; the owner commits. Back up `session.patch` + `untracked.tar` in the scratchpad after each milestone.
- No `tools/*.sh` or `src/` edits while a detached device run is in progress; `tools/sf native-build` after any
  `Native/*` edit; `tools/sf pack-local` after any change the template, the SkiaSharp probe or the Kitchen must see.
- A device-visible change is closed by its legs **and** a compositor screenshot (`tools/sf screenshot`), not by a
  QML state readback. Host tests first (`dotnet test tests/Linux.SailfishOS.Tests`, about 1 s), then legs.
- Full matrix (`tools/sf matrix`, detached, about 25 min; wait for `LAUNCHED_PID` before reading the device log) for
  packages that touch every path: M1, M2, M6, M8, M19.
- Counters that must stay 0 in the device logs: `treeFixups`, `timerWithWork`, `NavResyncs`, `BridgeFailed`.
- The fake shim can agree with a bug: after a bridge change read the device log for the counters the change should
  move, not only the PASS line.
- Every package updates the docs it contradicts (M26) in the same change.

Suggested order: M7 (one decision, unblocks publishing) → M1, M2, M3, M5 (small, remove the "blocks" rows) → M14
(Essentials defects), M15 (handlers), M17 (router bugs) → M16 (Toolbar handler + SearchHandler) → M6, M8, M9 → M4
(large, after D2) → M27, M28 → M10–M13, M18 → M19–M25 as the quiet-hour work. M26 rides along with every package.
