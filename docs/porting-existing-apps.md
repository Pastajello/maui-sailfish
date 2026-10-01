# Porting existing MAUI apps: what breaks and how to fix it

[add-sailfish-to-existing-app.md](add-sailfish-to-existing-app.md) covers the steps. This page collects what
happened when real open-source apps got a `net11.0-sailfish` head on a Jolla phone (Sailfish OS 5.2, aarch64):
the build and startup errors, what the app had to change, and what does not work yet.

| App | Stack | Result |
|---|---|---|
| dotnet/maui-samples `10.0/Apps` (Calculator, Weather, TipCalc, RpnCalculator, SolitaireEncryption, GameOfLife, BugSweeper, WordPuzzle, WeatherTwentyOne, EmployeeDirectory, WhatToEat) | net10, plain MAUI | run without app changes |
| DeveloperBalance (maui-samples) | net10, Syncfusion Toolkit | runs; the Syncfusion views are empty |
| MoneyFox | net8, MSAL, Sharpnado tabs, LiveCharts, CommunityToolkit | runs; charts empty |
| Profitocracy | net9, Shell, LiveCharts, Plugin.LocalNotification, CommunityToolkit 12 | runs; charts empty, no notifications |
| WeightTracker | net8, UraniumUI Material, Microcharts, AiForms.SettingsView, CommunityToolkit 7 | onboarding and home run; chart, settings page and the add-weight popup do not |
| GameSpur | net8, Firebase, private API config | builds; needs the authors' private configuration to start |
| GitTrends | net9, C# Markup, central package management, Shiny, Sentry, Syncfusion Charts | starts with app changes, then stops at the first page: CommunityToolkit.Maui.Markup typed bindings do not work on MAUI 11 rc1 (see below) |

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

**SkiaSharp's native library.** Restore brings no `libSkiaSharp.so` for linux-arm64. Any library that touches
SkiaSharp, even only through `SKTypeface` in a view-model constructor (MoneyFox, Profitocracy), throws
`DllNotFoundException`, and its pages fail to open. Add the glibc build in the version the app resolves:

```xml
<PackageReference Include="SkiaSharp.NativeAssets.Linux" Version="2.88.6" />  <!-- = the app's SkiaSharp -->
```

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
- **Anything else renders as an empty container** whose MAUI children still paint: SkiaSharp's
  `SKCanvasViewHandler` (LiveCharts, Microcharts), Syncfusion's `SfView` controls, AiForms.SettingsView.

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

## Not supported yet

- **SkiaSharp views** (`SKCanvasView`, `SKGLView`), and with them LiveCharts and Microcharts: the area stays empty.
- **CommunityToolkit.Maui platform features.** The v1 `Popup` (CommunityToolkit ≤ 9, `ShowPopupAsync`) never
  opens. `Toast`, `Snackbar` and `Badge` have no Sailfish implementation; their plain-`net` services throw or do
  nothing. (CommunityToolkit 12+ shows popups as modal pages; that path is not verified yet.)
- **Native-only controls** with no cross-platform part, such as AiForms.SettingsView.
- **Plugins** with Android/iOS implementations only: local notifications, biometrics, in-app rating.

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

## Driving a port on the phone

- `MAUI_SAILFISH_TAPS` scripts input from launch, in screenshot pixels: `ms:x,y` taps, `ms:x,y>x2,y2` drags
  (swipes), `ms:"text"` types into the focused field. Example:
  `tools/sf run --env MAUI_SAILFISH_TAPS='6000:903,227;8500:516,441;11000:900,240>300,240'`.
- `SF_PKG`/`SF_BIN` point `tools/sf run`, `kill` and `screenshot` at the ported app
  (`SF_PKG=harbour-moneyfox SF_BIN=MoneyFox.Ui`). The defaults come from the current directory.
- `MAUI_SAILFISH_QT_HOST_DIAG=1` logs handler pushes, navigation and QML events. Add
  `MAUI_SAILFISH_QT_HOST_INPUT_TRACE=1` to see which element each touch hit and who consumed it, and
  `MAUI_SAILFISH_FIRST_CHANCE=N` to print the first N first-chance exceptions with their stacks. Use the last one for
  async startup code that swallows exceptions.
- `tools/sf run` stops printing the log after a short while, but the app keeps running. A late `dev tap` line
  missing from the output does not mean the app hung; take a screenshot.
