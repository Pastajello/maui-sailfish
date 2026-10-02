# Porting existing MAUI apps: what breaks and how to fix it

[add-sailfish-to-existing-app.md](add-sailfish-to-existing-app.md) covers the steps. This page collects what
happened when real open-source apps got a `net11.0-sailfish` head on a Jolla phone (Sailfish OS 5.2, aarch64):
the build and startup errors, what the app had to change, and what does not work yet.

| App | Stack | App changes | Result |
|---|---|---|---|
| dotnet/maui-samples `10.0/Apps`: Calculator, Weather, TipCalc, RpnCalculator, SolitaireEncryption, GameOfLife | net10, plain MAUI | the TFM and the MAUI 11 pin only | run |
| WhatToEat, EmployeeDirectory, BugSweeper, WordPuzzle, WeatherTwentyOne (maui-samples) | net10, plain MAUI | a few lines each | run |
| DeveloperBalance (maui-samples) | net10, Syncfusion Toolkit | the TFM and the MAUI 11 pin only | runs; Syncfusion text inputs draw no outline, the chart is empty |
| MoneyFox | net8, MSAL, Sharpnado tabs, LiveCharts, CommunityToolkit | csproj, MSAL in `SailfishApplication`, a tab effect | runs; charts empty |
| Profitocracy | net9, Shell, LiveCharts, Plugin.LocalNotification, CommunityToolkit 12 | csproj, notifications skipped, `OnPlatform` defaults | runs; charts empty, no notifications |
| WeightTracker | net8, UraniumUI Material, Microcharts, AiForms.SettingsView, CommunityToolkit 7 | csproj, one picker reset | onboarding and home run; chart, settings page and the add-weight popup do not |
| GameSpur | net10, Firebase, private API config | csproj, stand-ins for three Android/iOS-only libraries, 13 converters | builds; needs the authors' private configuration to start |
| GitTrends | net9, C# Markup, central package management, Shiny, Sentry, Syncfusion Charts | csproj, Shiny and StoreReview stand-ins | starts, then stops at the first page: CommunityToolkit.Maui.Markup typed bindings do not work on MAUI 11 rc1 (see below) |

Every change is listed with its code in [What each port changed](#what-each-port-changed).

The first screen of each app on the phone (2026-10-02). Each app was installed from the port's RPM, started by
`tools/sf run`, and shot after it settled. Profitocracy shows Serbian, the language left from a language-switch
test. Its progress bars glow because they are Silica's glass bars. DeveloperBalance's empty "Task Categories" box
is the Syncfusion chart, which does not draw yet. GitTrends and GameSpur are not shown: neither gets past startup.

<table>
<tr>
<td align="center"><img src="screenshots/apps/calculator.jpg" width="150" alt="Calculator"><br>Calculator</td>
<td align="center"><img src="screenshots/apps/weather.jpg" width="150" alt="Weather"><br>Weather</td>
<td align="center"><img src="screenshots/apps/tipcalc.jpg" width="150" alt="TipCalc"><br>TipCalc</td>
<td align="center"><img src="screenshots/apps/rpncalculator.jpg" width="150" alt="RpnCalculator"><br>RpnCalculator</td>
<td align="center"><img src="screenshots/apps/solitaireencryption.jpg" width="150" alt="SolitaireEncryption"><br>SolitaireEncryption</td>
</tr>
<tr>
<td align="center"><img src="screenshots/apps/gameoflife.jpg" width="150" alt="GameOfLife"><br>GameOfLife</td>
<td align="center"><img src="screenshots/apps/bugsweeper.jpg" width="150" alt="BugSweeper"><br>BugSweeper</td>
<td align="center"><img src="screenshots/apps/wordpuzzle.jpg" width="150" alt="WordPuzzle"><br>WordPuzzle</td>
<td align="center"><img src="screenshots/apps/weathertwentyone.jpg" width="150" alt="WeatherTwentyOne"><br>WeatherTwentyOne</td>
<td align="center"><img src="screenshots/apps/employeedirectory.jpg" width="150" alt="EmployeeDirectory"><br>EmployeeDirectory</td>
</tr>
<tr>
<td align="center"><img src="screenshots/apps/whattoeat.jpg" width="150" alt="WhatToEat"><br>WhatToEat</td>
<td align="center"><img src="screenshots/apps/developerbalance.jpg" width="150" alt="DeveloperBalance"><br>DeveloperBalance</td>
<td align="center"><img src="screenshots/apps/moneyfox.jpg" width="150" alt="MoneyFox"><br>MoneyFox</td>
<td align="center"><img src="screenshots/apps/profitocracy.jpg" width="150" alt="Profitocracy"><br>Profitocracy</td>
<td align="center"><img src="screenshots/apps/weighttracker.jpg" width="150" alt="WeightTracker"><br>WeightTracker</td>
</tr>
</table>

## Build and restore

**The SDK.** The Sailfish head needs the .NET 11 SDK, so `global.json` must select it:

```json
{ "sdk": { "version": "11.0.100-rc.1.26425.128", "rollForward": "latestFeature" } }
```

**Out-of-support mobile heads (NETSDK1202).** SDK 11 refuses the net8 and net9 Android/iOS workloads. Gate them
with a property instead of overriding `TargetFrameworks` on the command line: a global `-p:TargetFrameworks=…`
also reaches project references and breaks them (NETSDK1005).

```xml
<!-- SailfishOnly=true builds the Sailfish head alone -->
<TargetFrameworks Condition="'$(SailfishOnly)' != 'true'">net8.0-android;net8.0-ios</TargetFrameworks>
<TargetFrameworks>$(TargetFrameworks);net11.0-sailfish</TargetFrameworks>
```

```bash
dotnet build App.csproj -f net11.0-sailfish -p:SailfishOnly=true -t:SailfishRun
```

With `tools/sf`, pass it to the inner publish too: `SF_PUBLISH_PROPS="-p:SailfishOnly=true"`.

**MAUI 11 for the Sailfish head only.** Pin `MauiVersion` and update the app's own `Microsoft.Maui.Controls`
reference in a Sailfish-only group. Leave the other heads alone:

```xml
<PropertyGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'sailfish'">
	<MauiVersion>11.0.0-rc.1.26451.6</MauiVersion>
</PropertyGroup>
<ItemGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'sailfish'">
	<PackageReference Update="Microsoft.Maui.Controls" Version="$(MauiVersion)" />
</ItemGroup>
```

**Central package management** (`Directory.Packages.props`) rejects a `Version` on a `PackageReference` (NU1008):
override with `VersionOverride` instead. With transitive pinning, MAUI packages that only a class library
references (`Microsoft.Maui.Essentials`) stay pinned to the central 9.x version and restore fails with NU1109.
Give them an override in the Sailfish group as well:

```xml
<ItemGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'sailfish'">
	<PackageReference Update="Microsoft.Maui.Controls" VersionOverride="$(MauiVersion)" />
	<PackageReference Include="Microsoft.Maui.Essentials" VersionOverride="$(MauiVersion)" />
</ItemGroup>
```

**Warnings as errors.** MAUI 10/11 marks `DisplayAlert`, `FadeTo`, `TranslateTo`, `SetUseSafeArea`, … obsolete
(CS0618). A repo that lists CS0618 in `WarningsAsErrors` fails on the Sailfish head only. `WarningsNotAsErrors` does
not override an explicit list, so use `<NoWarn>$(NoWarn);CS0618</NoWarn>` in the Sailfish property group. A NuGet
audit run as errors (`NuGetAuditMode=all`) also fails on advisories published after the app's last release, on
every head. `<NuGetAuditSuppress Include="<advisory URL>" />` names the one to accept.

`Microsoft.Maui.Controls.Compatibility` has no MAUI 11 counterpart. Condition it out of the Sailfish head
(`… != 'sailfish'`).

**Libraries that cap the MAUI version (NU1107).** CommunityToolkit.Maui 12.1 requires MAUI `[9.0.80, 10.0.0)`. A
later release of the same major has an open bound and the same API (12.3.0). Use `PackageReference Update` in
the Sailfish group, as for MAUI itself. CommunityToolkit 7.x–9.x, with lower bounds only, restore as they are.

**Libraries with platform targets only (NU1202).** Microcharts.Maui 1.x ships Android/iOS/Mac/Windows assets and
nothing for plain `net`. Its 2.0 release adds `net10.0`, which the Sailfish head can use (2.0.0.3, with SkiaSharp
3.119.4). Without such a release, condition the package out and supply the types the XAML names. The XAML
compiler keeps `OnPlatform` branches for Sailfish, so every type in the file must exist.

**SkiaSharp.** SkiaSharp 3.x views (`SKCanvasView`, `SKGLView`, the `SK*ImageSource` types) work through
`Microsoft.Maui.SailfishOS.SkiaSharp`. It holds the Sailfish handlers, the counterpart of the per-platform assets
SkiaSharp ships for Android and iOS, and registers itself, so `UseSkiaSharp()` stays as it is. Restore brings no
`libSkiaSharp.so` for linux-arm64, so add the glibc build in the version the app resolves. Without it, any code
that touches SkiaSharp (even an `SKTypeface` in a view-model constructor: MoneyFox, Profitocracy) throws
`DllNotFoundException`:

```xml
<ItemGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'sailfish'">
  <PackageReference Include="Microsoft.Maui.SailfishOS.SkiaSharp" Version="0.1.0" />
  <PackageReference Include="SkiaSharp.NativeAssets.Linux" Version="3.119.4" />  <!-- = the app's SkiaSharp -->
</ItemGroup>
```

Canvases behave as on Android: the same `Info`/`RawInfo` sizes, `IgnorePixelScaling`, one paint per frame however
often `InvalidateSurface` is called, and touch with `Handled` and parent-scroll interception. Limits:
- `SKGLView` draws through the same raster path, so `GRContext` is null.
- SkiaSharp 2.88 (the MAUI 6–8 line) is not supported.
- Libraries built on SkiaSharp draw, but their own platform code is theirs to port. LiveCharts' plain-`net` input
  is a stub, so its charts show but do not react to touch.

## Platform `#if` blocks

Shared code often enables a feature per platform (`#if ANDROID || IOS || MACCATALYST`). The Sailfish head defines
`SAILFISH`; add it where the feature works here too. BugSweeper's double-tap recognizer was compiled for the mobile
platforms only, so tiles never revealed until `|| SAILFISH` joined the condition.

## Startup

**"Bait and switch" plugins.** The portable assembly of Plugin.StoreReview (and plugins built the same way)
throws `NotImplementedException` from `CrossStoreReview.Current`. Register a Sailfish implementation of the
interface under `#if SAILFISH`, e.g. one that does nothing for store reviews.

**Libraries whose plain-`net` asset registers stubs.** `ConfigureSyncfusionCore()` registers handlers that are
plain classes in Syncfusion.Maui.Core's `net9.0` asset. MAUI rejects them (`Unable to add handler mapping for …`),
and the app's whole handler collection fails to build. Skip the call on Sailfish; the Syncfusion views then
render empty.

**Platform-only service registrations.** Apps register Shiny's notification and job managers only under
`#if ANDROID || IOS`, but resolve services that depend on them on every head. Register Sailfish implementations
for those interfaces. Notifications can post through `SailfishNotifications.Show`.

**Plugins whose platform singleton is null on plain `net`.** `UseLocalNotification()` registers
`LocalNotificationCenter.Current`, which is null without a platform implementation, and the app dies with
`ArgumentNullException (implementationInstance)`. Skip the registration on Sailfish and treat the missing service
as unsupported:

```csharp
#if !SAILFISH
	.UseLocalNotification()
#endif
```

**Platform services from `Platforms/<os>`.** Apps that register services in `MainApplication`/`AppDelegate`
(MoneyFox's MSAL client) need the same in `Platforms/SailfishOS/SailfishApplication.cs`:
`dotnet new maui-sailfish-platform`. Match the other heads' call when `MauiProgram.CreateMauiApp` takes
arguments (GitTrends: `CreateMauiApp(AppInfo.Current)`; Essentials such as `AppInfo`, `Preferences` and `FileSystem`
are available before it runs). The generated files follow the default code style: a repo that enforces IDE0040
"accessibility modifiers unnecessary" in the build wants `static void Main` without `private`. Code that reads `MauiApplication.Current.Services` or
`MauiUIApplicationDelegate.Current.Services` can read `IPlatformApplication.Current.Services` on every head.

**`{OnPlatform}` without `Default`.** `DeviceInfo.Platform` is `SailfishOS`, which the markup extension has no
named argument for. `{OnPlatform iOS=Ionicons, Android=Ionicons.ttf#}` therefore yields null on Sailfish, and the
FontImageSource glyph renders in the wrong font. Add `Default=…`. The element form can name the platform:
`<On Platform="SailfishOS" Value="…" />`.

**Effects.** Routing effects whose platform effect lives in each platform folder (Sharpnado's tab touch effects,
XamEffects) need a Sailfish `PlatformEffect` registered with `ConfigureEffects`. Without one, the effect does
nothing; for Sharpnado tabs that means taps never switch tabs.

## What maui-sailfish does with foreign handlers

A library without a Sailfish build restores its plain-`net` assets. Its handlers then derive from MAUI's
plain-`net` handlers, whose `CreatePlatformView` throws `NotImplementedException`. maui-sailfish catches the
failure once per handler type, logs `[QT_HOST][WARN] <handler> failed for <view> — …`, and continues:

- **A handler built on a stock one falls back to the Sailfish handler of that control.** UraniumUI registers
  `StatefulButtonHandler : ButtonHandler` for every `Button`, and Plainer (under UraniumUI Material) does the same
  for its Entry and Picker views. They render and work as plain Silica controls, without the library's
  platform tweaks: `… — falling back to SailfishButtonHandler`.
- **A view that draws itself (`IDrawable`) renders its drawing** under its MAUI children: Syncfusion Toolkit's
  `SfView` controls (`… — rendered from its own drawing (IDrawable) with its children`). The drawing is recorded
  again on every render pass, not on the library's own invalidate, so animations show their end state. A drawing
  that throws (Syncfusion's text measurer on plain .NET) logs `IDrawable.Draw (…) failed` once and draws what it
  got to.
- **Anything else renders as an empty container** whose MAUI children still paint: AiForms.SettingsView, and
  SkiaSharp's views without `Microsoft.Maui.SailfishOS.SkiaSharp` (see above).
- Collection item views are the `CollectionView`'s logical children, so `{RelativeSource AncestorType=…}`
  bindings in item templates (a page model's command) resolve as on Android.

## Platform behaviour that differs from Android/iOS

These follow Silica conventions. Port authors should expect them; none needs app changes.

- `ToolbarItems` become the page's pull-down menu. The line at the top of the page is its indicator.
- Shell and TabbedPage tabs are a row under the page header. Long titles shrink before they fade.
- A page without a `Title` shows the app's name (`ApplicationTitle`) in its header.
- `Shell.BackButtonBehavior` `IsVisible`/`IsEnabled` false and `NavigationPage.HasBackButton` false turn off the
  back gesture and indicator. A first-run modal page cannot be swiped away.
- A `Picker` opens Silica's inline menu. With more than five items, or inside a container that would clip the
  menu (an outlined field's rounded `Border`), it opens Silica's selection page instead.
- An `Entry`/`Editor` with a set `BackgroundColor` or `Background` has no Silica underline, as a set background
  replaces the native one on Android. The "borderless entry" idiom (`BackgroundColor="Transparent"` inside the
  app's own frame) needs no platform mapper.
- A `TapGestureRecognizer` in a `CollectionView` item template fires on tap, and the tap then does not select the
  row. A `SwipeItemView` shows as a Silica swipe action: the first background colour, image and label in its
  content, with `Invoked`/`Command` as usual.
- A page's size (`Width`/`Height`, `OnSizeAllocated`) is the area below the page header and tab row, as on Android
  and iOS, so apps that size views from it fit the screen.
- `Loaded` fires when the page joins the window. Pages a Shell route push builds (`GoToAsync("detail")`) have their
  handlers by then, so `Loaded` handlers can call `SetSemanticFocus()` and similar. Any other page (a ShellContent
  template, a page the app constructs and pushes) gets its handlers on the next render; touch `Handler` there from
  `Appearing` or later.
- `SemanticScreenReader.Announce` does nothing (Sailfish OS has no screen reader), as on Android with TalkBack off.
- An exception from an `async void` handler (a command, an event) is logged as
  `[Sailfish][QT_HOST][ERROR] unhandled exception in dispatched work` with its stack, and the app keeps running.
  Android would crash; look for that line when an action silently does nothing.
- Pulling the pull-down menu all the way and releasing past its items leaves it open; tap an item then (Silica).
- The page header is always there. A page whose own `BackgroundColor` is unset shows the theme behind the header
  even when its root layout has a colour; set the page's `BackgroundColor` to colour the whole screen.
- A missing image file shows nothing, as on Android, and logs
  `[Sailfish][QML_OBJECT][WARN] image source not found, nothing shown: File: …` once per file.
- An `Image` filling a `Border` with a `RoundRectangle` or `Ellipse` `StrokeShape` (round avatars) is masked to the
  shape in the colour behind the Border, with the Border's solid stroke on top. The mask is exact on a solid
  background only.
- Flicking a list against its end dims the list content for a moment: Silica's end-of-list feedback.
- A `ProgressBar` is Silica's glass bar: the filled part glows in `ProgressColor`, which stands out on a light page.
- `Flashlight` drives the system torch (the Top menu's toggle). Sailjail has no permission for it, so in a sandboxed
  app (the default) it throws `FeatureNotSupportedException`; it works with `<SailfishSandboxing>false</SailfishSandboxing>`.
- `Contacts` cannot reach the user's address book: a platform limit, not a missing feature. Sailfish OS keeps it in
  privileged data that only system apps open (the `Privileged` permission plus a `mapplauncherd` `privileges.d`
  entry, as Jolla's People app has). Sailjail's `Contacts` permission does not open it. A third-party app, sandboxed or not,
  reads qtcontacts-sqlite's non-privileged store, which does not hold the user's contacts.
  `GetAllAsync` therefore returns an empty list, and `PickContactAsync` opens Silica's contact selection page
  showing "No people" (back returns `null`). In a sandbox both need `Contacts` in `SailfishPermissions`, else
  `PermissionException`. This is not worked around: an app that needs contacts has to say it is unavailable on
  Sailfish OS.
- `Geolocation` needs `Location` in `SailfishPermissions`, else `PermissionException` (as on Android without the
  manifest entry). `Permissions.RequestAsync` never shows a dialog: Sailjail asks once, at the first launch.
- `AppActions` become the buttons of the app's home-screen cover: the first two actions (a Silica cover has two).
  Tapping one raises `AppActions.OnAppAction`. The icon is a theme cover icon name (`icon-cover-search`), an
  `image://` or a file path; without one the button shows `icon-cover-next`.
- `WebAuthenticator` opens the sign-in page in the system browser and completes when the browser hands the callback
  URL back. The callback scheme must be declared (`<SailfishUrlSchemes>myapp</SailfishUrlSchemes>`), or the call
  throws `InvalidOperationException`. That URL goes to the waiting call, not to `OnAppLinkRequestReceived`. A
  sign-in the user abandons stays pending until its cancellation token fires or a new one starts.
- `SecureStorage` keeps secrets in Sailfish Secrets when the daemon can open the app's device-lock collection. On a
  phone without Jolla's device-lock integration the daemon never gets the lock code and the collection stays locked
  (unlocking the screen does not help); the app then uses the file store (obfuscation only, the key lives next to
  the data) and logs `SecureStorage: Sailfish Secrets unavailable (… collection locked …)`. Entries move into
  Secrets once it opens.
- A borderless `Entry` (`BackgroundColor` set) without a `Placeholder` has no Silica label line, and an Entry taller
  than its natural height centres its text (MAUI's default `VerticalTextAlignment`), as on Android.

## Not supported yet

- **SkiaSharp 2.88 views**, a GPU `GRContext` in `SKGLView`, and input of SkiaSharp-based libraries whose
  plain-`net` platform code is a stub (LiveCharts: charts draw, touch does nothing).
- **CommunityToolkit.Maui platform features.** The v1 `Popup` (CommunityToolkit ≤ 9, `ShowPopupAsync`) never
  opens. `Toast`, `Snackbar` and `Badge` have no Sailfish implementation; their plain-`net` services throw or do
  nothing. (CommunityToolkit 12+ shows popups as modal pages; that path is not verified yet.)
- **Native-only controls** with no cross-platform part, such as AiForms.SettingsView.
- **Plugins** with Android/iOS implementations only: local notifications, biometrics, in-app rating.
- **Essentials the platform lacks:** `TextToSpeech` (no speech engine), `Geocoding` (no geocoder) and `Passkeys` (no
  WebAuthn authenticator) throw `FeatureNotSupportedException`, as MAUI does on a device without the feature;
  `TextToSpeech.GetLocalesAsync` returns no locales. `MediaPicker.CapturePhotoAsync`/`CaptureVideoAsync` likewise.
- **`FileResult.OpenReadAsync()`** throws: MAUI's plain-`net` `FileBase` has no platform reader and the method is
  internal to MAUI. Read `File.OpenRead(result.FullPath)` instead. `ContentType` and `FileName` work.

## MAUI 10/11 changes that break older libraries everywhere

These show up on the Sailfish head first because it is the first MAUI 11 head an older app gets. They happen on
Android/iOS with the same MAUI version too.

- **UraniumUI `PickerField` loops forever** (100 % CPU, "not responding") when its `SelectedItem` is set to a value
  not in its items, e.g. resetting it to `""`: `""` and `null` alternate between the field and its inner `Picker`.
  It reproduces in a console app with no platform at all: UraniumUI 2.7.4 on MAUI 10.0.60 and 11 rc1, and 2.16
  and 3.0 on MAUI 11 rc1. On MAUI 8.0.6 it does not happen. Reset with `null` instead.
- **`Shell.Current` is null until a window exists.** Code that runs in `App`'s constructor or `CreateWindow` and
  navigates through `Shell.Current` throws.
- **TwoWay bindings call `ConvertBack` while the binding context propagates**, so converters that assume a user
  edit run at startup.
- **MAUI 11 rc1 dropped the `InternalsVisibleTo` grants for CommunityToolkit** that MAUI 10 has
  (`CommunityToolkit.Maui.Core`, `.Markup`, …). CommunityToolkit.Maui.Markup's typed bindings
  (`.Bind(Label.TextProperty, static (VM vm) => vm.Name)`) derive from MAUI's internal `TypedBindingBase` and throw
  `MethodAccessException` when a page is built, with Markup 6.0.1 and 7.0.1 alike. An app built on C# Markup
  (GitTrends) cannot open its first page on MAUI 11 rc1 until the toolkit and MAUI agree again. Bindings by path
  (`.Bind(Label.TextProperty, nameof(VM.Name))`) do not go through that type.

## What each port changed

Every port adds `net11.0-sailfish` to `TargetFrameworks` and pins `MauiVersion` 11 for that head (see
[add-sailfish-to-existing-app.md](add-sailfish-to-existing-app.md#2-add-the-target-framework)). The net8/net9 apps also
gate their mobile heads behind `SailfishOnly`, as shown there. Everything beyond that is listed below. Apps not named
needed nothing more: Calculator, Weather, TipCalc, RpnCalculator, SolitaireEncryption, GameOfLife and DeveloperBalance.

### maui-samples (net10)

- **WhatToEat:** `Microsoft.Maui.Controls.Compatibility` conditioned out (`… != 'sailfish'`).
- **EmployeeDirectory:** its class library `EmployeeDirectory.Core` (`net10.0`, MAUI 10) gets a `net11.0` target.
  Without it, the head's project reference brings MAUI 10 back (NU1605):

  ```xml
  <TargetFrameworks>net10.0;net11.0</TargetFrameworks>
  <PropertyGroup Condition="'$(TargetFramework)' == 'net11.0'">
      <MauiVersion>11.0.0-rc.1.26451.6</MauiVersion>
  </PropertyGroup>
  ```

- **BugSweeper** (`Tile.cs`): the double-tap recognizer that reveals a tile was compiled for the mobile platforms only.

  ```csharp
  #if ANDROID || IOS || MACCATALYST || SAILFISH
      TapGestureRecognizer doubleTap = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
  ```

- **WordPuzzle:** two `#if ANDROID || IOS` blocks leave a variable unassigned on any other head, so the shared code
  does not compile. `GameSquare.cs` sets the font size and `MainPage.xaml.cs` the layout multiplier. Both get
  `|| SAILFISH`.
- **WeatherTwentyOne:** the `App` constructor navigates through `Shell.Current`, which MAUI 11 leaves null until a
  window exists. The service locator also had no branch for this head:

  ```csharp
  // App.xaml.cs
  if (DeviceInfo.Idiom == DeviceIdiom.Phone)
      ((Shell)MainPage).CurrentItem = PhoneTabs;     // was Shell.Current.CurrentItem

  // Services/ServiceExtensions.cs
  #elif SAILFISH
      IPlatformApplication.Current!.Services;
  ```

### Profitocracy (net9)

- csproj: the `SailfishOnly` gate, Compatibility conditioned out, and in the Sailfish item group:

  ```xml
  <PackageReference Update="Microsoft.Maui.Controls" Version="$(MauiVersion)" />
  <PackageReference Update="CommunityToolkit.Maui" Version="12.3.0" />          <!-- 12.1 caps MAUI below 10 -->
  <PackageReference Include="SkiaSharp.NativeAssets.Linux" Version="3.116.1" /> <!-- LiveCharts -->
  ```

- `MauiProgram.cs`: `UseLocalNotification()` under `#if !SAILFISH`. The plugin's `Current` is null on plain `net`.
  `NotificationService` reads it through a null-safe property and reports `NotificationResult.NotSupported`:

  ```csharp
  private static INotificationService? Center => LocalNotificationCenter.Current;

  public static async Task<bool> AreNotificationsEnabled() =>
      Center is { IsSupported: true } center && await center.AreNotificationsEnabled();
  ```

- XAML: `Default=Ionicons` added to all seven `{OnPlatform iOS=Ionicons, Android=Ionicons.ttf#}` font families.
- `Platforms/SailfishOS/` from `dotnet new maui-sailfish-platform`, unchanged.

### MoneyFox (net8)

- csproj: the gate, Compatibility conditioned out, `Microsoft.Maui.Controls` updated to `$(MauiVersion)`, and
  `SkiaSharp.NativeAssets.Linux` 2.88.6 (LiveCharts).
- `Platforms/SailfishOS/SailfishApplication.cs` registers the OneDrive backup's MSAL client, as `MainApplication`
  and `AppDelegate` do. The redirect is the desktop loopback, which the system browser returns to:

  ```csharp
  protected override MauiApp CreateMauiApp()
  {
      MauiProgram.AddPlatformServicesAction = services =>
          services.AddSingleton(PublicClientApplicationBuilder.Create(MSAL_APPLICATION_ID)
              .WithRedirectUri("http://localhost").Build());
      return MauiProgram.CreateMauiApp();
  }
  ```

- Sharpnado.Tabs taps its tabs through a routing effect whose plain-`net` asset has no platform effect, so tabs
  never switched. A Sailfish `PlatformEffect` turns the tap into a gesture recognizer:

  ```csharp
  // MauiProgram.cs
  #if SAILFISH
      .ConfigureEffects(effects => effects
          .Add<Sharpnado.Tabs.Effects.CommandsRoutingEffect, SharpnadoTapEffect>()
          .Add<Sharpnado.Tabs.Effects.TouchRoutingEffect, SharpnadoTouchEffect>())   // ripple only: empty
  #endif

  // Platforms/SailfishOS/SharpnadoTapEffect.cs
  internal sealed class SharpnadoTapEffect : PlatformEffect
  {
      private TapGestureRecognizer? _tap;

      protected override void OnAttached()
      {
          if (Element is not View view)
              return;
          _tap = new TapGestureRecognizer();
          _tap.Tapped += (_, _) =>
          {
              var command = Commands.GetTap(view);
              var parameter = Commands.GetTapParameter(view);
              if (command?.CanExecute(parameter) == true)
                  command.Execute(parameter);
          };
          view.GestureRecognizers.Add(_tap);
      }

      protected override void OnDetached()
      {
          if (Element is View view && _tap is not null)
              view.GestureRecognizers.Remove(_tap);
          _tap = null;
      }
  }
  ```

### WeightTracker (net8)

- csproj: the gate, Compatibility conditioned out, and in the Sailfish item group:

  ```xml
  <PackageReference Update="Microsoft.Maui.Controls" Version="$(MauiVersion)" />
  <PackageReference Update="Microcharts.Maui" Version="2.0.0.3" />   <!-- 1.x: platform TFMs only (NU1202) -->
  <PackageReference Update="SkiaSharp" Version="3.119.4" />
  <PackageReference Include="SkiaSharp.NativeAssets.Linux" Version="3.119.4" />
  ```

- `WelcomeModelView.cs`: `SelectedItem = null!;` instead of `""`. A UraniumUI `PickerField` loops forever on a value
  that is not in its items (see below).

### GitTrends (net9, central package management)

- csproj, Sailfish groups (`VersionOverride`, since `Directory.Packages.props` owns the versions):

  ```xml
  <NoWarn>$(NoWarn);CS0618</NoWarn>   <!-- CS0618 is in the repo's WarningsAsErrors -->

  <PackageReference Update="Microsoft.Maui.Controls" VersionOverride="$(MauiVersion)" />
  <PackageReference Include="Microsoft.Maui.Essentials" VersionOverride="$(MauiVersion)" />   <!-- NU1109 -->
  <PackageReference Update="CommunityToolkit.Maui.Markup" VersionOverride="7.0.1" />
  <PackageReference Include="SkiaSharp.NativeAssets.Linux" VersionOverride="3.119.1" />
  ```

  and, for every head, `<NuGetAuditSuppress Include="https://github.com/advisories/GHSA-2m69-gcr7-jv3q" />`.
- `MauiProgram.cs`: `ConfigureSyncfusionCore()` under `#if !SAILFISH`. The app registers Shiny's managers under
  `#if ANDROID || IOS` only, so the Sailfish head registers its own, and Plugin.StoreReview's `Current` throws:

  ```csharp
  #if SAILFISH
      builder.Services.AddSingleton<Shiny.Notifications.INotificationManager, SailfishNotificationManager>();
      builder.Services.AddSingleton<Shiny.Jobs.IJobManager, SailfishJobManager>();
  #endif
  …
  #if SAILFISH
      services.AddSingleton<IStoreReview>(new SailfishStoreReview());   // no-op: no in-app review here
  #else
      services.AddSingleton<IStoreReview>(CrossStoreReview.Current);
  #endif
  ```

  `SailfishNotificationManager` posts at once through `SailfishNotifications.Show`/`Close`, and keeps channels,
  badges and schedules in memory. `SailfishJobManager` keeps jobs and runs them only on request (the app's unit-test
  mock), since there is no background scheduler.
- `SailfishApplication.CreateMauiApp() => MauiProgram.CreateMauiApp(AppInfo.Current)`, as on the other heads.
- `Program.Main` without `private`: the repo enforces IDE0040.

### GameSpur (net10)

- csproj: `Microsoft.Maui.Controls`, `.Controls.Core`, `.Core` and `.Essentials` updated to `$(MauiVersion)` (the app
  references them explicitly). `Sharpnado.Maui.Nuke` and `Vapolia.StrokedLabel` are conditioned out: they ship
  Android/iOS/Windows assets only.
- `Platforms/SailfishOS/LibraryShims.cs` keeps the shared code and XAML compiling without them. It provides a no-op
  `UseNuke()` and `UseStrokedLabelBehavior()`, and `StrokedLabel`'s attached properties under the library's XAML
  namespace (`[assembly: XmlnsDefinition("https://vapolia.eu/Vapolia.StrokedLabel", …)]`). It also defines the
  Android-only `HtmlLabel` type, because the XAML compiler keeps every `OnPlatform` branch for this head. Images then
  load uncached, and labels draw without a stroke.
- `MauiProgram.cs`: `AddHandler<Shell, TabbarBadgeRenderer>()` under `#if ANDROID || IOS`. Two stand-ins, registered
  under `#if SAILFISH`: Firebase push (the plugin's plain-`net` asset is reference-only, and its `Current` throws) and
  CommunityToolkit's `IBadge` (its plain-`net` default throws):

  ```csharp
  #if SAILFISH
      NoPushNotification.Install(builder);   // IFirebasePushNotification + permissions: denied, no token
      NoBadge.Install();                     // IBadge.SetCount does nothing; set through Badge's internal SetDefault
  #endif
  ```

  `AppShell.OnAppearing` skips `RegisterNotificationCategories` under `#if !SAILFISH`.
- 13 converters threw `NotImplementedException` from `ConvertBack`. MAUI 11 calls it on TwoWay bindings while the
  binding context propagates, so they return `Binding.DoNothing`.
- `Services/Fetcher.cs`: the instance id is an Android id or an iOS vendor id. The Sailfish head stores a random one:

  ```csharp
  #elif SAILFISH
      Preferences.Default.Get(AppConstant.InstanceIdKey, string.Empty) is { Length: > 0 } saved
          ? saved : Guid.NewGuid().ToString("N")[..30];
      Preferences.Default.Set(AppConstant.InstanceIdKey, InstanceID);
      return InstanceID;
  ```

- `ArticlePage.xaml`: `<On Platform="iOS,SailfishOS">` reuses the iOS branch (a plain `Label`).

## Driving a port on the phone

- `MAUI_SAILFISH_TAPS` scripts input from launch, in screenshot pixels. Each entry starts with its time in ms
  after launch: `6000:x,y` taps, `6000:x,y>x2,y2` drags (swipes; `…>x2,y2@2500` takes 2.5 s and holds at the end,
  as a pulley needs), `6000:"text"` types into the focused field. Example:
  `tools/sf run --env MAUI_SAILFISH_TAPS='6000:903,227;8500:516,441;11000:900,240>300,240'`. The `dev tap #N` log
  lines need `MAUI_SAILFISH_QT_HOST_DIAG=1`. A tap in the top-left corner of a pushed page hits Silica's back
  indicator and goes back; a harmless filler tap is one on the page title.
- `SF_PKG`/`SF_BIN` point `tools/sf run`, `kill` and `screenshot` at the ported app
  (`SF_PKG=harbour-moneyfox SF_BIN=MoneyFox.Ui`). The defaults come from the current directory.
- `MAUI_SAILFISH_QT_HOST_DIAG=1` logs handler pushes, navigation and QML events. Add
  `MAUI_SAILFISH_QT_HOST_INPUT_TRACE=1` to see which element each touch hit and who consumed it, and
  `MAUI_SAILFISH_FIRST_CHANCE=N` to print the first N first-chance exceptions with their stacks. Use the last one for
  async startup code that swallows exceptions.
- `tools/sf run` stops printing the log after a short while, but the app keeps running. A late `dev tap` line
  missing from the output does not mean the app hung; take a screenshot.
