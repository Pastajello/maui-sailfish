<!-- Static audit of commit af3d782 (branch feature/fixes) against .NET MAUI 11.0.0-rc.1.26451.6, 2026-10-04. Produced by a read-only code review with ilspycmd decompiles; nothing was run on the device. Items marked unverified were not checked on the phone. Summary and work packages: docs/maui11-alignment-plan.md -->

# MAUI Essentials coverage audit — Sailfish OS backend vs .NET MAUI 11.0.0-rc.1.26451.6

Static analysis only (branch `feature/fixes`, 2026-10-04). MAUI members come from decompiling
`Microsoft.Maui.Essentials.dll`, `Microsoft.Maui.dll` and `Microsoft.Maui.Controls.dll` of the pinned RC1 build.
Paths below are relative to `/Users/mer/Projects/maui-sailfish/src/Linux.SailfishOS/`. Already-decided items (camera
capture, Geocoding, TextToSpeech, BlazorWebView, MauiSplashScreen out; Contacts non-privileged store only) are listed as
"decided" and not re-argued.

## Summary

- **Install mechanism is sound for RC1.** All 36 registry rows name the installer the RC1 facade really has
  (`SetCurrent` for FileSystem, AppInfo, DeviceInfo, DeviceDisplay, Connectivity, Permissions, Geocoding, AppActions;
  `SetDefault` for the rest), verified facade by facade; `MoreEssentialsTests.Every_row_names_an_installer_the_facade_has`
  guards it. MAUI 11's own `EssentialsInitializer` (Microsoft.Maui.Hosting.EssentialsExtensions) bridges every DI-registered
  Essentials interface to its facade during `Build()`, so under `UseMauiAppSailfish` the facades are already Sailfish
  before `SailfishEssentials.Install` re-sets the same instances (harmless). MAUI registers **no** Essentials interface in
  DI itself, so the Sailfish `TryAddSingleton` never loses to MAUI; an app registration wins in either order.
- **`IVersionTracking` is missing from the registry (confirmed, `SailfishEssentialsRegistry.cs:38-73`).** The static
  still works: MAUI's `VersionTrackingImplementation` is platform-neutral (`Preferences.Default` + `AppInfo.Current`), and
  the RC1 bridge installs a `LazyVersionTracking` whenever `IPreferences`/`IAppInfo` are in DI. What is broken upstream is
  `AppInfo`: `VersionString` is the entry **assembly** version (`1.0.0.0`), `BuildString` is empty and `PackageName` is
  the assembly name, so `VersionTracking` never notices an RPM version bump and `IsFirstLaunchForCurrentBuild` is
  meaningless. `IVersionTracking` is not resolvable from DI/overlay (same as on Android, where MAUI registers none).
- **`IViewScreenshot` (new in MAUI 11) is not implemented.** `view.CaptureAsync()` / `window.CaptureAsync()` resolve
  `IScreenshot` from `handler.MauiContext.Services` and need it to also implement `IViewScreenshot.CaptureViewAsync(object)`;
  `SailfishScreenshot` only has `CaptureAsync()`, so per-view capture silently returns `null`.
- **MainThread is hooked.** `MainThread.SetCustomImplementation(Func<bool>, Action<Action>)` exists in RC1 and
  `SailfishMainThread.Install` binds it with `UnsafeAccessor` (`SailfishMainThread.cs:16-18`, called at
  `SailfishMauiApplication.Boot.cs:95`). MAUI's own `BridgeMainThreadFromDispatcher` also sets it during `Build()` from the
  application dispatcher (the Sailfish provider). The plain-net `PlatformIsMainThread`/`PlatformBeginInvokeOnMainThread`
  throw `NotImplementedInReferenceAssemblyException` only when no custom implementation is set.
- **Permissions:** the generic static form works through `IPermissions`; the **instance form**
  (`new Permissions.Camera().CheckStatusAsync()`) throws `NotImplementedInReferenceAssemblyException` on plain net
  (`Permissions.BasePlatformPermission`, not hookable). 20 of 27 permission types map to Sailjail names; `LaunchApp →
  "AppLaunch"` is an unverified Sailjail name; `Permissions.Flashlight` reports Granted while `Flashlight` throws in a
  sandbox; the pickers never demand `Pictures`/`Documents`.
- **Functional defects found:** `Launcher.TryOpenAsync` never opens anything (returns `CanOpenAsync`);
  `Map.TryOpenAsync` always `true`; `Share.RequestAsync(ShareTextRequest)` drops `Text` when `Uri` is set;
  `Clipboard.ClipboardContentChanged` never fires for changes made by other apps; `IScreenshotResult.OpenReadAsync(Jpeg)`
  throws; `EmailMessage.Attachments`/`BodyFormat` and `MapLaunchOptions.NavigationMode` are silently ignored;
  `GeolocationListeningRequest.MinimumDistance`/`DesiredAccuracy` and `GeolocationRequest.DesiredAccuracy` are ignored;
  `DisplayInfo.RefreshRate` is a constant 60; `AppInfo.RequestedLayoutDirection` is always LTR.
- **Stale assumption:** `ISemanticScreenReader` lives in `Microsoft.Maui.Essentials.dll` in RC1 (not `Microsoft.Maui.dll`)
  and nothing in MAUI registers a default in DI any more, so the "MAUI registers its reference-assembly reader" branch in
  `AddSailfishEssentials` (`SailfishEssentials.cs:32-39`) and the overlay comment (`SailfishServiceOverlay.cs:77-81`) are
  dead code — harmless.
- **Platform-specific API shape:** `SailfishLifecycle` follows `AddAndroid`/`AddiOS` exactly. `SailfishPage.AllowedOrientations`
  is the one API whose idiomatic home is `Microsoft.Maui.Controls.PlatformConfiguration.SailfishOSSpecific.Page` with
  `On<SailfishOS>()`; MAUI's `On<T>()` is open to any third-party `IConfigPlatform` marker, so no MAUI change is needed.
  Cover/Remorse/BottomSheet/Notifications are imperative services with no MAUI counterpart; the Essentials shape (interface
  + `Default` facade + DI row) would make them mockable, but the static classes are not un-idiomatic.

## Coverage table

Legend for "Sailfish state": **impl** = implemented, **partial** = implemented with options/semantics dropped,
**FNS** = throws `FeatureNotSupportedException`, **no-op** = silently does nothing / returns a constant, **missing** = not in
registry, **n/a** = no Sailfish counterpart (reason given), **decided** = owner decision, keep as-is.

### Install-time facades and app identity

| Service | Member | MAUI 11 signature | Sailfish state | Evidence (file:line) | Severity | Fix sketch |
|---|---|---|---|---|---|---|
| IAppInfo | PackageName | `string PackageName { get; }` | partial: entry assembly name, not `ApplicationId`/RPM name | `Essentials.cs:435` | degrades | bake `ApplicationId` (and `SailfishPackageName`) into `qml/maui-appmeta.json` (`buildTransitive/Microsoft.Maui.SailfishOS.targets:106-107`), read via `SailfishAppMeta` |
| IAppInfo | Name | `string Name { get; }` | impl (appmeta title, else assembly name) | `Essentials.cs:427-437` | — | — |
| IAppInfo | VersionString | `string VersionString { get; }` | partial: `Assembly.GetEntryAssembly().GetName().Version` ("1.0.0.0"), not `ApplicationDisplayVersion` | `Essentials.cs:430-432,439` | degrades (About pages, VersionTracking history) | bake `ApplicationDisplayVersion` (targets `:237-244` already compute it) into appmeta |
| IAppInfo | Version | `Version Version { get; }` | impl (parses VersionString; `Version.Parse` throws on a non-numeric display version once the fix above lands) | `Essentials.cs:441` | cosmetic | `Version.TryParse` with fallback |
| IAppInfo | BuildString | `string BuildString { get; }` | no-op: `string.Empty` (Android: versionCode = `ApplicationVersion`) | `Essentials.cs:443` | degrades (`VersionTracking.IsFirstLaunchForCurrentBuild` always compares "") | bake `ApplicationVersion` into appmeta |
| IAppInfo | RequestedTheme | `AppTheme RequestedTheme { get; }` | impl (Silica ambience, live via `SailfishTheme`) | `Essentials.cs:446`, `SailfishEssentials.cs:168-211` | — | — |
| IAppInfo | PackagingModel | `AppPackagingModel PackagingModel { get; }` | partial: always `Unpackaged`; an RPM-installed app is packaged in MAUI's sense | `Essentials.cs:448` | cosmetic | return `Packaged` when `SailfishAppMeta.Current.Application` is set |
| IAppInfo | RequestedLayoutDirection | `LayoutDirection RequestedLayoutDirection { get; }` | no-op: always `LeftToRight` | `Essentials.cs:450` | degrades (RTL locales) | read `QGuiApplication::isRightToLeft()` through the shim, or `CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft` |
| IAppInfo | ShowSettingsUI | `void ShowSettingsUI()` | throws `NotSupportedException` (MAUI convention: `FeatureNotSupportedException`) | `Essentials.cs:452-453` | cosmetic | throw `FeatureNotSupportedException`; optionally open Settings → Apps page over D-Bus (unverified API) |
| IDeviceInfo | Model / Manufacturer / Name | `string …` | impl (`/etc/hw-release`, `/etc/hostname`) | `Essentials.cs:244-273` | — | — |
| IDeviceInfo | VersionString / Version | `string` / `Version` | impl (`/etc/sailfish-release VERSION_ID`) | `Essentials.cs:275-291` | — | — |
| IDeviceInfo | Platform | `DevicePlatform Platform { get; }` | impl: `DevicePlatform.Create("SailfishOS")`; `OnPlatform<T>` matches `On.Platform.Contains(DeviceInfo.Platform.ToString())` so `<On Platform="SailfishOS">` works; `{OnPlatform}` markup extension has only fixed properties → `Default` (documented, `docs/porting-existing-apps.md:176-177`) | `Essentials.cs:293`, `SailfishPlatform.cs:10`; MAUI `OnPlatform<T>` decompile | — | — |
| IDeviceInfo | Idiom | `DeviceIdiom Idiom { get; }` | impl (≥600 dp short side = Tablet) | `Essentials.cs:296-297` | — | — |
| IDeviceInfo | DeviceType | `DeviceType DeviceType { get; }` | impl (emulator heuristics) | `Essentials.cs:300-309` | — | — |
| IFileSystem | CacheDirectory / AppDataDirectory | `string …` | impl (`~/.cache/<seg>`, `~/.local/share/<seg>`; Sailjail `org/app` segment) | `Essentials.cs:355-357,456-492` | — | — |
| IFileSystem | AppPackageFileExistsAsync | `Task<bool>(string)` | impl: `AppContext.BaseDirectory/filename`; MauiAsset lands at `LogicalName` (or file name) under the app root, backslashes normalised → paths match | `Essentials.cs:359-360`; targets `:135-146` | — | — |
| IFileSystem | OpenAppPackageFileAsync | `Task<Stream>(string)` | impl; a missing file throws synchronously (`File.OpenRead`) instead of a faulted task | `Essentials.cs:362-366` | cosmetic | `Task.FromException` or `async` |
| IPreferences | ContainsKey / Remove / Clear / Set<T> / Get<T> (all with `sharedName`) | as in RC1 interface | impl: one JSON store, shared containers namespaced `"<shared>::key"`, atomic rename, all 8 `Preferences.SupportedTypes` | `Essentials.cs:114-219` | — | — |
| IVersionTracking | all 14 members (`IsFirstLaunchEver`, `CurrentVersion`, `Track()`, `IsFirstLaunchForVersion(string)`, …) | RC1 interface (14 members) | **missing from registry**; the static works via MAUI's neutral `VersionTrackingImplementation(Preferences.Default, AppInfo.Current)` and the RC1 `BridgeLazyVersionTrackingFromDI`; **DI/overlay resolve `IVersionTracking` as null**; values are wrong because of `AppInfo.VersionString/BuildString` above | `SailfishEssentialsRegistry.cs:38-73` (no row); `SailfishServiceOverlay.cs:75-76` | degrades (via AppInfo); cosmetic (DI) | add `services.TryAddSingleton<IVersionTracking>(_ => VersionTracking.Default)` in `AddSailfishEssentials` (the impl type is internal to MAUI, so not a `Create` row) + fix AppInfo |
| ISemanticScreenReader | Announce | `void Announce(string)` | impl (trace only; no screen reader on Sailfish) | `SailfishSemanticScreenReader.cs:13-14` | — | — |
| MainThread (static) | IsMainThread / BeginInvokeOnMainThread / InvokeOnMainThreadAsync / GetMainThreadSynchronizationContextAsync | plain-net `PlatformIsMainThread`/`PlatformBeginInvokeOnMainThread` throw unless `s_mainThreadImplementation` is set | impl: `SetCustomImplementation(Func<bool>, Action<Action>)` bound by `UnsafeAccessor`; loop thread id captured on the thread that becomes the Qt loop (`BindLoopThread` at `Boot.cs:83`); `SailfishSynchronizationContext` set on it, so `GetMainThreadSynchronizationContextAsync` returns it | `SailfishMainThread.cs:16-18,24-37`; `SailfishMauiApplication.Boot.cs:83,93-95` | — | the `if (Dispatcher.GetForCurrentThread() is { } …)` guard at `Boot.cs:93` skips the hook silently; log when it does |

### Devices

| Service | Member | MAUI 11 signature | Sailfish state | Evidence | Severity | Fix sketch |
|---|---|---|---|---|---|---|
| IBattery | ChargeLevel / State / PowerSource / EnergySaverStatus | properties | impl (Nemo.Mce, kernel `power_supply` fallback) | `SailfishDevices.cs:149-155,49-120` | — | — |
| IBattery | BatteryInfoChanged / EnergySaverStatusChanged | events | impl | `SailfishDevices.cs:144-146,157-159` | — | — |
| IDeviceDisplay | KeepScreenOn | `bool { get; set; }` | impl (Nemo.KeepAlive `DisplayBlanking`) | `SailfishEssentials.cs:321-339` | — | — |
| IDeviceDisplay | MainDisplayInfo | `DisplayInfo` (6-arg ctor incl. `float rate`) | partial: `RefreshRate` hard-coded `60`; width/height/density/orientation/rotation real | `SailfishEssentials.cs:341-357` (`:355`) | cosmetic | expose `QScreen::refreshRate()` from the shim (nothing in `Native/` or `MauiShell.qml` reports it today) |
| IDeviceDisplay | MainDisplayInfoChanged | event | impl (surface size / orientation) | `SailfishEssentials.cs:318-319,359`; `SailfishDisplay.cs:30-58` | — | — |
| IFlashlight | IsSupportedAsync | `Task<bool>()` | impl (D-Bus torch provider) | `SailfishDeviceExtras.cs:42-43` | — | — |
| IFlashlight | TurnOnAsync / TurnOffAsync | `Task` | impl outside Sailjail; FNS inside (no Sailjail permission for the torch) — documented | `SailfishDeviceExtras.cs:45-57` | n/a (platform) | make `Permissions.Flashlight` report `Denied` when sandboxed (see Permissions) |
| IHapticFeedback | IsSupported / Perform | `bool` / `void Perform(HapticFeedbackType)` | impl (QtFeedback ThemeEffect) | `SailfishDevices.cs:328-337` | — | — |
| IVibration | IsSupported / Vibrate() / Vibrate(TimeSpan) / Cancel | as RC1 | impl (clamped 1–5000 ms as MAUI does) | `SailfishDevices.cs:295-311` | — | — |

### Sensors and location

| Service | Member | MAUI 11 signature | Sailfish state | Evidence | Severity | Fix sketch |
|---|---|---|---|---|---|---|
| IAccelerometer | IsSupported / IsMonitoring / Start / Stop / ReadingChanged / ShakeDetected | as RC1 | impl (m/s² → G, shake heuristic) | `SailfishSensors.cs:38-74,93-118` | — | — |
| IGyroscope | same set | as RC1 | impl (°/s → rad/s) | `SailfishSensors.cs:120-134` | — | — |
| IMagnetometer | same set | as RC1 | impl (T → µT) | `SailfishSensors.cs:136-150` | — | — |
| ICompass | Start(SensorSpeed, bool applyLowPassFilter) | overload | partial: `applyLowPassFilter` ignored | `SailfishSensors.cs:160` | cosmetic | — (Android-only semantic) |
| IBarometer | same set | as RC1 | impl (Pa → hPa) | `SailfishSensors.cs:166-177` | — | — |
| IOrientationSensor | same set | as RC1 | impl (Euler → quaternion) | `SailfishSensors.cs:179-194` | — | — |
| all sensors | Start when unsupported / already monitoring | `FeatureNotSupportedException` / `InvalidOperationException` per MAUI docs | impl | `SailfishSensors.cs:53-56` | — | — |
| IGeolocation | GetLastKnownLocationAsync | `Task<Location?>()` | impl; `PermissionException` without `Location` Sailjail permission | `SailfishSensors.cs:240-247` | — | — |
| IGeolocation | GetLocationAsync(GeolocationRequest, CancellationToken) | request has `Timeout`, `DesiredAccuracy`, `RequestFullAccuracy` | partial: `Timeout` honoured (default 30 s, null on timeout); `DesiredAccuracy` ignored (always full GPS/`PositionSource` default); `RequestFullAccuracy`/`Location.ReducedAccuracy` n/a (no reduced-accuracy mode on Sailfish) | `SailfishSensors.cs:249-275` | cosmetic | map `DesiredAccuracy` to `PositionSource.preferredPositioningMethods` (Lowest/Low → NonSatellite, else All) |
| IGeolocation | StartListeningForegroundAsync(GeolocationListeningRequest) | request has `MinimumTime`, `MinimumDistance`, `DesiredAccuracy` | partial: only `MinimumTime` → `updateInterval`; **`MinimumDistance` ignored** (every fix raises `LocationChanged`); `DesiredAccuracy` ignored | `SailfishSensors.cs:285-295,307-318` | degrades (battery/noise for apps that rely on distance filtering) | keep the last raised `Location` and skip fixes closer than `MinimumDistance` (`Location.CalculateDistance`) in `OnFix` |
| IGeolocation | StopListeningForeground / IsListeningForeground / IsEnabled / LocationChanged / ListeningFailed | as RC1 | impl (`IsEnabled` = a valid `PositionSource`, not the Settings toggle) | `SailfishSensors.cs:277-283,297-328` | cosmetic | — |
| IGeocoding | GetPlacemarksAsync / GetLocationsAsync | as RC1 | FNS — decided | `SailfishDeviceExtras.cs:72-79` | decided | — |

### Communication, sharing, pickers, media

| Service | Member | MAUI 11 signature | Sailfish state | Evidence | Severity | Fix sketch |
|---|---|---|---|---|---|---|
| IBrowser | OpenAsync(Uri, BrowserLaunchOptions) | options: `LaunchMode`, `TitleMode`, `PreferredToolbarColor`, `PreferredControlColor`, `Flags` | partial: every option ignored (no in-app browser tab on Sailfish; always external) | `Essentials.cs:380-381` | n/a | — |
| ILauncher | CanOpenAsync(Uri) | `Task<bool>` | no-op: always `true` | `Essentials.cs:399-401` | cosmetic | query `xdg-mime query default x-scheme-handler/<scheme>` or the D-Bus activation files |
| ILauncher | OpenAsync(Uri) | `Task<bool>` | impl (`QDesktopServices::openUrl`) | `Essentials.cs:403-405`; `Native/sailfish_host.cpp:579-584` | — | — |
| ILauncher | **TryOpenAsync(Uri)** | `Task<bool>`: open if supported, return whether it opened | **defect: returns `CanOpenAsync(uri)` and never opens** | `Essentials.cs:407-409` | degrades (apps using `TryOpenAsync` get `true` and nothing happens) | `=> OpenAsync(uri)` |
| ILauncher | OpenAsync(OpenFileRequest) | `Task<bool>` | impl (file:// through the default handler; Sailjail visibility documented) | `Essentials.cs:414-421` | — | — |
| IPhoneDialer | IsSupported / Open(string) | as RC1 | impl (`tel:`), `ArgumentNullException` on blank | `SailfishShareAndPickers.cs:352-361` | — | — |
| IEmail | IsComposeSupported | `bool` | no-op: always `true` | `SailfishShareAndPickers.cs:363` | cosmetic | — |
| IEmail | ComposeAsync(EmailMessage) | message has `Subject`, `Body`, `BodyFormat`, `To`, `Cc`, `Bcc`, `Attachments` | partial: `mailto:` with to/cc/bcc/subject/body; **`Attachments` and `BodyFormat.Html` silently dropped** | `SailfishShareAndPickers.cs:365-380` | degrades | throw `FeatureNotSupportedException` when `Attachments.Count > 0` (MAUI Windows does this), or hand attachments to `Sailfish.Share` with the email target |
| ISms | IsComposeSupported / ComposeAsync(SmsMessage) | as RC1 | impl (`sms:` URI; handler is Jolla Messages — unverified on device) | `SailfishShareAndPickers.cs:382-388` | — | — |
| IMap | OpenAsync(lat, lon, MapLaunchOptions) | options: `Name`, `NavigationMode` | partial: `geo:` URI with name; `NavigationMode` ignored; result of `openUrl` discarded (no maps app → silent) | `SailfishShareAndPickers.cs:390-391,414-418` | cosmetic | — |
| IMap | OpenAsync(Placemark, …) | as RC1 | impl (`geo:0,0?q=` address fallback) | `SailfishShareAndPickers.cs:393-400` | — | — |
| IMap | TryOpenAsync (both) | `Task<bool>`: whether a maps app opened | no-op: always `true` | `SailfishShareAndPickers.cs:402-412` | cosmetic | return the `OpenUrl` bool |
| IShare | RequestAsync(ShareTextRequest) | request: `Title`, `Text`, `Subject`, `Uri` | partial: **when `Uri` is set, `Text` is dropped** (Android concatenates); `Subject` becomes the resource name | `SailfishShareAndPickers.cs:69-83,85-91` | degrades | share two resources (text/plain + text/x-url) or concatenate as Android does |
| IShare | RequestAsync(ShareFileRequest) / RequestAsync(ShareMultipleFilesRequest) | as RC1 | impl (Sailfish.Share, MIME from extension) | `SailfishShareAndPickers.cs:93-105` | — | in a sandbox the `Sharing` Sailjail permission may be needed — unverified, see open questions |
| IClipboard | HasText / GetTextAsync / SetTextAsync | as RC1 | impl (QClipboard on the Qt thread) | `Essentials.cs:17-40` | — | — |
| IClipboard | ClipboardContentChanged | event; fires for any clipboard change on the platforms that support it | partial: **fires only after this app's own `SetTextAsync`**; the shim has no `QClipboard::dataChanged` hook | `Essentials.cs:19,39`; `Native/sailfish_host.cpp:550-577` | degrades (clipboard-watching apps) | connect `QGuiApplication::clipboard()->dataChanged` in the shim → `svc-clipboard-changed` shell event, add to `ShellEvents.cs` |
| IMediaPicker | IsCaptureSupported | `bool` | `false` — decided | `SailfishShareAndPickers.cs:210` | decided | — |
| IMediaPicker | PickPhotoAsync / PickVideoAsync (obsolete in RC1) | `Task<FileResult?>(MediaPickerOptions?)` | impl (Sailfish.Pickers single page) | `SailfishShareAndPickers.cs:212,216` | — | — |
| IMediaPicker | PickPhotosAsync / PickVideosAsync | `Task<List<FileResult>>(MediaPickerOptions?)` | partial: multi dialogs work; **`MediaPickerOptions` never read** (`SelectionLimit`, `Title`, `MaximumWidth/Height`, `RotateImage`, `PreserveMetaData`, `SaveToGallery` ignored) | `SailfishShareAndPickers.cs:214,218` | degrades (`SelectionLimit`) | truncate the result to `SelectionLimit` at least; `Title` → the picker page title |
| IMediaPicker | CapturePhotoAsync / CaptureVideoAsync | as RC1 | FNS — decided | `SailfishShareAndPickers.cs:220-224` | decided | — |
| IMediaPicker / IFilePicker | returned `FileResult.OpenReadAsync()` | `FileBase.PlatformOpenReadAsync` is `internal virtual` and throws on plain net | n/a (MAUI reference-assembly limitation, documented `docs/porting-existing-apps.md:314-315`); `ContentType` supplied from extension | `SailfishShareAndPickers.cs:205-208`, MAUI `FileBase` decompile | n/a | — |
| IFilePicker | PickAsync(PickOptions) | options: `PickerTitle`, `FileTypes` | partial: extension filters only (MIME filters dropped), `PickerTitle` ignored; no `Documents`/`UserDirs` permission demand in a sandbox (picker shows nothing instead of `PermissionException`) | `SailfishShareAndPickers.cs:226-236` | degrades | `SailfishPermissions.Demand(typeof(Permissions.StorageRead), "FilePicker")` |
| IFilePicker | PickMultipleAsync | `Task<IEnumerable<FileResult>?>` | impl (nullable annotation differs: `IEnumerable<FileResult?>`) | `SailfishShareAndPickers.cs:238-239` | cosmetic | match the RC1 annotation |
| IScreenshot | IsCaptureSupported / CaptureAsync | as RC1 | impl (`grabWindow` → PNG in cache dir, never deleted) | `SailfishShareAndPickers.cs:424-432` | cosmetic | delete the file when the result is disposed/read |
| IScreenshot (+IViewScreenshot) | **`IViewScreenshot.CaptureViewAsync(object platformView)`** | new RC1 interface; `IView.CaptureAsync()`/`IWindow.CaptureAsync()` → `ScreenshotDispatch` resolves `IScreenshot` from `handler.MauiContext.Services` and requires `is IViewScreenshot`, else returns `null` | **missing**: `SailfishScreenshot` is not `IViewScreenshot`, so every `view.CaptureAsync()` is `null` | `SailfishShareAndPickers.cs:422` (class decl); MAUI `ScreenshotDispatch.cs` decompile | degrades (MAUI 11 feature silently absent) | implement `IViewScreenshot` on `SailfishScreenshot`: `platformView` is a `NativeElementHost`; grab its QQuickItem (`QQuickItem::grabToImage` via a shim call keyed by host id) or crop the window PNG to the host's geometry |
| IScreenshotResult | Width / Height / CopyToAsync | as RC1 | impl | `SailfishShareAndPickers.cs:449-451,460-464` | — | — |
| IScreenshotResult | OpenReadAsync(ScreenshotFormat format, int quality) | Png or Jpeg with quality | partial: **Jpeg throws `NotSupportedException`** | `SailfishShareAndPickers.cs:453-458` | degrades | have the shim save `QImage` as JPEG with quality, or re-encode the PNG with SkiaSharp if present |
| ITextToSpeech | GetLocalesAsync / SpeakAsync | as RC1 | empty / FNS — decided | `SailfishDeviceExtras.cs:62-68` | decided | — |

### Storage, auth, app services

| Service | Member | MAUI 11 signature | Sailfish state | Evidence | Severity | Fix sketch |
|---|---|---|---|---|---|---|
| ISecureStorage | GetAsync / SetAsync | `Task<string?>` / `Task` | impl (Sailfish Secrets, file fallback + migration, Qt-thread hop with host wait) | `SailfishSecureStorage.cs:28-50,75-116,150-170` | — | — |
| ISecureStorage | Remove / RemoveAll | `bool Remove(string)` / `void RemoveAll()` (synchronous) | impl; blocks on `.GetAwaiter().GetResult()`; safe on the Qt thread because `QtThread.RunAsync` runs inline there (`QtThread.cs:27-35`); before the first tick on the Qt thread it throws `InvalidOperationException` (documented) | `SailfishSecureStorage.cs:52-73` | — | — |
| ISecureStorage | `SecureStorage.Default` plain-net getter | builds `SecureStorageImplementation(GetDefaultPackageName())` lazily | hooked before first use (`SetDefault`); `Install` is not `Early`, so a MauiProgram that touches `SecureStorage` *before* `Build()` hits the throwing default | `SailfishEssentialsRegistry.cs:43` (no `Early`) | cosmetic | mark `Early: true` if apps read it in `CreateMauiApp` (needs no Qt until first call) |
| IPermissions | CheckStatusAsync<T> | `Task<PermissionStatus>` | impl: `Granted` unsandboxed or when any mapped Sailjail permission is declared, else `Denied`; unmapped types always `Granted` | `SailfishShareAndPickers.cs:312-320,340-341` | — | — |
| IPermissions | RequestAsync<T> | `Task<PermissionStatus>` | impl = Check (Sailjail asks at launch) — documented | `SailfishShareAndPickers.cs:343-344` | — | — |
| IPermissions | ShouldShowRationale<T> | `bool` | no-op `false` | `SailfishShareAndPickers.cs:346` | — | — |
| Permissions (static) | instance form `new Permissions.X().CheckStatusAsync()/RequestAsync()/EnsureDeclared()/ShouldShowRationale()` | `BasePlatformPermission` virtuals | **throw `NotImplementedInReferenceAssemblyException`** on plain net; no hook exists | MAUI `Permissions.BasePlatformPermission` decompile | blocks apps that use the instance form (e.g. permission lists `List<BasePermission>`) | cannot be fixed in the backend; document (porting guide) and recommend the generic form |
| Permissions mapping | `SailjailFor(Type)` | 27 RC1 types: Battery, Bluetooth, CalendarRead, CalendarWrite, Camera, ContactsRead, ContactsWrite, Flashlight, LaunchApp, LocationWhenInUse, LocationAlways, Maps, Media, Microphone, NearbyWifiDevices, NetworkState, Phone, Photos, PhotosAddOnly, PostNotifications, Reminders, Sensors, Sms, Speech, StorageRead, StorageWrite, Vibrate | mapped: 20 (Calendar*, Reminders→Calendar; Camera; Contacts*; Location*, Maps→Location; Microphone, Speech→Microphone; Phone; Sms→Messages; Photos*→Pictures; Media→Music/Videos; Storage*→UserDirs/Documents/Pictures; Bluetooth, NearbyWifiDevices→Bluetooth; LaunchApp→**AppLaunch**). Always-Granted: Battery, Flashlight, NetworkState, PostNotifications, Sensors, Vibrate. Issues: (a) `AppLaunch` is not a Sailjail permission name I could verify (not in `docs/sailfishos-packaging.md`, tools or templates); (b) `Flashlight` Granted but the feature throws in a sandbox; (c) `NearbyWifiDevices→Bluetooth` is a guess | `SailfishShareAndPickers.cs:295-310`; `SailfishDeviceExtras.cs:53-55` | degrades (b), unverified (a) | (a) verify against `/etc/sailjail/permissions/` on the phone; (b) `"Flashlight" => IsSandboxed ? Denied : Granted` special case; (c) map to `Internet`/none |
| IWebAuthenticator | AuthenticateAsync(options) / AuthenticateAsync(options, ct) | options: `Url`, `CallbackUrl`, `PrefersEphemeralWebBrowserSession`, `ResponseDecoder` | impl (system browser + D-Bus `openUrl` callback via `SailfishUrlSchemes`); `PrefersEphemeralWebBrowserSession` n/a (no ephemeral browser session API); abandoned sign-in stays pending (documented) | `SailfishAppServices.cs:66-101` | — | — |
| IPasskeys | IsSupported / CreateAsync / AssertAsync | as RC1 | `false` / FNS — decided | `SailfishDeviceExtras.cs:82-91` | decided | — |
| IAppleSignInAuthenticator | AuthenticateAsync | RC1 interface exists; facade default throws | **missing** from registry (MAUI's default throws `NotImplementedInReferenceAssemblyException`) | registry has no row | cosmetic (iOS-only API) | optional FNS row for consistency with Passkeys |
| IAppActions | IsSupported / GetAsync / SetAsync / AppActionActivated | as RC1 | impl (first two actions → cover actions; `AppAction.Icon` read by `UnsafeAccessor`); `ConfigureEssentials(e => e.AddAppAction(..))` reaches it because the RC1 bridge installs the DI instance before applying `AppActions` (`MoreEssentialsTests.cs:25-39` asserts `AppActions.Current` is Sailfish after `Build`) | `SailfishAppServices.cs:16-50` | — | — |
| IContacts | PickContactAsync / GetAllAsync | as RC1 | impl; non-privileged store only — decided; `PermissionException` without `Contacts` in a sandbox | `SailfishAppServices.cs:180-230` | decided | — |
| IConnectivity | NetworkAccess / ConnectionProfiles / ConnectivityChanged | as RC1 | impl (Connman + kernel-route fallback) | `SailfishDevices.cs:165-282` | — | — |

## Install mechanism findings

1. **Hook names vs RC1 facades — all correct.** Verified by decompiling each facade (`internal static void SetDefault/SetCurrent`):

   | Facade | Registry hook (`SailfishEssentialsRegistry.cs`) | RC1 installer | OK |
   |---|---|---|---|
   | FileSystem, AppInfo, DeviceInfo, DeviceDisplay, Connectivity, Permissions, AppActions | `SetCurrent` (`:38,40,41,46,48,61,71`) | `SetCurrent` | ✓ |
   | Geocoding | `SetCurrent` (`:69`) | `SetCurrent` (although its property is `Default`) | ✓ |
   | Preferences, Clipboard, SecureStorage, Browser, Launcher, Battery, Vibration, HapticFeedback, Accelerometer, Gyroscope, Magnetometer, Compass, Barometer, OrientationSensor, Geolocation, Share, MediaPicker, FilePicker, PhoneDialer, Email, Sms, Map, Screenshot, Flashlight, TextToSpeech, Passkeys, WebAuthenticator, Contacts | `SetDefault` | `SetDefault` | ✓ |
   | SemanticScreenReader (`SailfishEssentials.cs:69-70`) | `SetDefault` | `SetDefault` | ✓ |
   | VersionTracking | — (no row) | `SetDefault` / `GetDefault` exist | n/a |
   | MainThread | `SetCustomImplementation(Func<bool>, Action<Action>)` via `UnsafeAccessor` (`SailfishMainThread.cs:16-18`) | exists, internal | ✓ |

   The `[DynamicDependency]` list on `Hook` (`SailfishEssentials.cs:74-110`) covers every row plus `SemanticScreenReader`
   (the handoff's W6.5 note that they attached to `InstallEarly` is no longer true). `Hook` tolerates a missing method or
   a signature change (logs, continues) — robust.

2. **MAUI's own bridge does the heavy lifting under `UseMauiAppSailfish`.** `MauiAppBuilder` → `UseEssentials()` →
   `EssentialsInitializer` (an `IMauiInitializeService`) calls `BridgeIfRegistered` for every interface: `services.GetService<T>()`,
   and if non-null `SetDefault/SetCurrent`. Because `AddSailfishEssentials` `TryAddSingleton`s every row
   (`SailfishEssentials.cs:30-31`), the facades are Sailfish at the end of `Build()`; `EssentialsTests.Build_installs_the_sailfish_services_and_app_registrations_win`
   asserts exactly this. `SailfishEssentials.Install` (`Boot.cs:99`) then sets the same instances again — harmless. Side
   effect worth knowing: MAUI tracks facade ownership (`FacadeBridgeState`) and `EssentialsCleanup` restores the previous
   (throwing) defaults on `MauiApp.Dispose()`; only tests/teardown see it.

3. **Registration order: Sailfish never loses to MAUI.** `ConfigureEssentials`/`UseEssentials` register only
   `EssentialsRegistration`, `EssentialsCleanup` and the initializer — no `IDeviceInfo`, `IPreferences`, … (grep over the
   full decompile of Microsoft.Maui.dll and Microsoft.Maui.Controls.dll found none). So `TryAddSingleton` loses only to an
   app registration made *before* `UseMauiAppSailfish`, and an app registration made *after* wins by last-registration
   semantics (`GetService<T>` returns the last) — app wins both ways, as intended.

4. **`ISemanticScreenReader` handling is stale but harmless.** In RC1 the interface, facade and
   `SemanticScreenReaderImplementation` live in `Microsoft.Maui.Essentials.dll` (the task brief said Microsoft.Maui.dll —
   not in this build), and nothing in MAUI registers it in DI. `AddSailfishEssentials` (`SailfishEssentials.cs:32-39`) and
   `SailfishServiceOverlay.Resolve` (`:77-81`) special-case a MAUI-registered reader that no longer exists; the
   `reader is null` path registers the Sailfish one, so behaviour is correct. Simplify to a registry row
   (`new(typeof(ISemanticScreenReader), typeof(SemanticScreenReader), "SetDefault", …)`).

5. **MainThread, plain `UseMauiApp` path.** `InstallDispatcherProvider()` (`SailfishMauiApplication.cs:43-47`) sets
   `DispatcherProvider.Current` before `CreateMauiApp`, so MAUI's `TryAddSingleton(svc => DispatcherProvider.Current)` and
   `BridgeMainThreadFromDispatcher` pick the Sailfish provider even without `UseMauiAppSailfish`; `Boot.cs:95` re-hooks it.
   The only silent failure mode is `Dispatcher.GetForCurrentThread()` returning null at `Boot.cs:93` (no log).

6. **`Early` set.** Only FileSystem, Preferences, AppInfo, DeviceInfo are installed before `CreateMauiApp`
   (`SailfishEssentialsRegistry.cs:38-41`). `SecureStorage`, `Connectivity`, `DeviceDisplay`, `VersionTracking`
   (via `Preferences`+`AppInfo`, fine) read in a `MauiProgram` before `Build()` still hit MAUI's throwing defaults;
   `SecureStorage` and `DeviceDisplay.MainDisplayInfo` need no Qt to construct and could be `Early`.

## Platform-specific API shape findings

MAUI's convention (verified in the RC1 Controls decompile): a marker `sealed class <Platform> : IConfigPlatform` in
`Microsoft.Maui.Controls.PlatformConfiguration`, static classes in `…PlatformConfiguration.<Platform>Specific` holding
attached `BindableProperty`s with `Get/Set` statics plus extension methods on
`IPlatformElementConfiguration<TPlatform, TElement>`; `element.On<T>()` is `IElementConfiguration<TElement>.On<T>() where T : IConfigPlatform`
and `PlatformConfigurationRegistry<TElement>` creates the configuration for **any** `T`, so a third-party marker works
without MAUI changes.

| Sailfish API | Shape today | MAUI-idiomatic shape | Verdict |
|---|---|---|---|
| `SailfishPage.AllowedOrientations` (attached BP, `Microsoft.Maui.SailfishOS.Platform`, `SailfishPage.cs:28-38`) | static class + attached BP, XAML `sf:SailfishPage.AllowedOrientations` | `Microsoft.Maui.Controls.PlatformConfiguration.SailfishOSSpecific.Page.AllowedOrientationsProperty` + `page.On<SailfishOS>().SetAllowedOrientations(...)`/`.AllowedOrientations()` (cf. iOSSpecific `Page.SetPrefersStatusBarHidden`, AndroidSpecific `Application.UseWindowSoftInputModeAdjust`) | **should move** — the one API that is exactly a per-element platform-specific; keep the attached BP (XAML) and add the `On<SailfishOS>()` extensions; keep a `[Obsolete]` forwarder on `SailfishPage` |
| `SailfishLifecycle` + `AddSailfish(...)` (`LifecycleEvents/SailfishLifecycle.cs`) | `ILifecycleBuilder` extension with delegate types, in `Microsoft.Maui.LifecycleEvents` | identical to `AddAndroid`/`AddiOS`/`AddWindows` | **idiomatic as-is** |
| `SailfishCover` (static: `SetContent`, `SetActions`, `IsActive`, `ActiveChanged`) | imperative static service | no MAUI counterpart (closest: `AppActions`, an Essentials-style `Default` facade behind an interface). An app-level attached property (`Application.On<SailfishOS>().SetCoverTitle`) would fit static content but not actions/events | keep static; optional `ISailfishCover` + `SailfishCover.Default` for mocking (Essentials shape). Not a `PlatformConfiguration` candidate |
| `SailfishRemorse` (static `ExecuteAsync`, `CancelAll`) | imperative static | no MAUI counterpart; `CommunityToolkit.Maui` ships such services as `IX`/`X.Default` pairs | keep; optional interface for testability |
| `SailfishBottomSheet` (class, `Show/Hide/Update/Close`) | imperative object outside the view tree | no MAUI counterpart (CommunityToolkit has `Popup`/`BottomSheet` as views) | acceptable; a `View`-derived `BottomSheet` with a handler would be more MAUI-like but is a larger change and the architecture note says it must stay outside the MAUI tree |
| `SailfishNotifications` (static `Show`/`Close`) | imperative static | no MAUI counterpart | keep; optional interface |
| `SailfishPlatform.DevicePlatform`, `SailfishTheme.Current`, `SailfishDisplay.*` | read-only statics | `DevicePlatform.Create("SailfishOS")` is the sanctioned extension point; the others duplicate `AppInfo.RequestedTheme`/`DeviceDisplay` (docs say prefer the MAUI ones) | fine |

Namespace note: a `Microsoft.Maui.Controls.PlatformConfiguration.SailfishOSSpecific` namespace in a non-Microsoft assembly
is namespace-squatting, but the repo already ships `Microsoft.Maui.SailfishOS.*` and `Microsoft.Maui.LifecycleEvents`
extensions, so it is consistent with the project's existing choice (MAUI's GTK/WPF/Tizen backends did the same).

## Open questions / unverified

- **Sailjail permission names**: `AppLaunch` (`SailfishShareAndPickers.cs:308`) does not appear anywhere else in the repo
  (docs, tools, templates); whether it exists in `/etc/sailjail/permissions/` is unverified — check on the phone. Same for
  whether `Sailfish.Share` needs the `Sharing` permission and the pickers need `Pictures`/`Videos`/`Documents`/`MediaIndexing`
  inside a sandbox; if so, `Share.RequestAsync`/the pickers fail silently today and should `Demand(...)`.
- **`sms:` and `geo:` handlers** on Sailfish OS 5.2: whether Jolla Messages registers `x-scheme-handler/sms` and which
  maps app (if any) handles `geo:` is not verifiable statically; `Map.TryOpenAsync` returning the real `openUrl` result would
  surface it.
- **`IViewScreenshot` platform view**: the fix sketch assumes `handler.PlatformView` is a `NativeElementHost` with a QML
  host id the shim can grab (`QQuickItem::grabToImage`, Qt 5.6 has it); not verified against `QtHostRuntime`'s current ABI.
- **`Permissions` instance form**: how many ported apps use `new Permissions.X()` vs the generic statics is unknown; the
  porting guide (`docs/porting-existing-apps.md`) does not mention the instance form today.
- **`SecureStorage` `Early`**: whether any audited app reads `SecureStorage` in `CreateMauiApp` (as MoneyFox did for
  `FileSystem`) is unknown; marking it `Early` is cheap.
- `DeviceDisplay.MainDisplayInfo.Density` is `pixelWidth / 540` by design (`SailfishDisplay.cs:4-10,38`), not a DPI scale;
  apps that treat `Density` as DPI/160 get a different number than on Android for the same panel — a documented design
  choice, listed here only because it is Essentials-visible.
