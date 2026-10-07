# Adding Sailfish OS to an existing .NET MAUI app

An ordinary MAUI app (`dotnet new maui`, or any existing one) gets a Sailfish OS head the same way it has
Android or iOS heads: one more target framework and, optionally, a `Platforms/SailfishOS/` folder.

## 1. One-time machine setup

```bash
dotnet nuget add source <feed> --name maui-sailfish   # where Microsoft.Maui.SailfishOS is published
dnx Microsoft.Maui.SailfishOS.Workload install        # teaches the SDK the net11.0-sailfish TFM
```

`dnx` runs the `sailfish-workload` tool without installing it (`dotnet tool install -g Microsoft.Maui.SailfishOS.Workload`
keeps it). It copies the workload manifest into the SDK that `dotnet --version` selects in the current directory, so
run it where `global.json` applies; the tool refuses an SDK band it has no manifest for. `status` shows what is
installed, `uninstall` removes it. For an SDK you cannot write to (`/usr/share/dotnet`), use `sudo`, or
`install --manifest-root ~/.dotnet-sailfish` and set `DOTNETSDK_WORKLOAD_MANIFEST_ROOTS=~/.dotnet-sailfish` for builds.
In a clone, `./tools/sf workload-install` does the same from the sources.

Optionally, once the manifest is installed, the SDK's own workload command works too:

```bash
dotnet workload install sailfish --source <feed> --source https://api.nuget.org/v3/index.json
```

It puts the `Microsoft.Maui.SailfishOS` package into the SDK's `library-packs`, so restore finds it without the
NuGet source from above, and `dotnet workload list` shows `sailfish` (`dotnet workload uninstall sailfish` removes
the package; the tool's `uninstall` removes the manifest). On an SDK in workload-set mode (the default) the command
also brings the SDK's other workloads up to Microsoft's latest workload set for the band, as any
`dotnet workload install` does. Without the first step it fails with "Workload ID sailfish is not recognized": the
SDK only knows workload IDs from manifests it already has or from a workload set, and the project ships no workload
set of its own (it would replace Microsoft's for the whole band).

## 2. Add the target framework

In the `.csproj`, after the other `TargetFrameworks` lines:

```xml
<TargetFrameworks>$(TargetFrameworks);net11.0-sailfish</TargetFrameworks>
```

That is all a build needs. The TFM brings the rest, the way an in-box workload brings its packs:

- the `Microsoft.Maui.SailfishOS` package (version `$(SailfishPackageVersion)`, default `0.1.0`; an explicit
  `PackageReference` wins, `SailfishImplicitPackageReference=false` turns it off),
- `RuntimeIdentifier=linux-arm64` (set `linux-arm` for 32-bit phones),
- a self-contained, trimmed ReadyToRun payload and the harbour RPM on `dotnet publish -f net11.0-sailfish`,
- the `SAILFISH` compilation symbol for `#if SAILFISH` in shared code.

`MauiProgram.cs` stays as it is, unless it registers platform-only plugins (step 5). `UseMaui=true` can stay too; the template turns it off on the Sailfish head
only so that head builds on machines without the MAUI workloads (then pin `MauiVersion`, or restore fails
with NU1015).

An app still on .NET 10 (`net10.0-android;net10.0-ios;…`) keeps its heads; the Sailfish head needs the .NET 11 SDK
and MAUI 11:

- `global.json` must select the .NET 11 SDK the workload manifest was installed into; an SDK 10 pin does not know
  the `net11.0-sailfish` TFM,
- pin MAUI 11 for that head only, since the Sailfish package depends on it (NU1605 otherwise):

  ```xml
  <PropertyGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'sailfish'">
  	<MauiVersion>11.0.0-rc.1.26451.6</MauiVersion>
  </PropertyGroup>
  ```

- a shared class library with its own `Microsoft.Maui.Controls` reference pinned to 10.x hits the same NU1605
  through the project reference; give it the same pin (or a `net11.0` target).

An app on .NET 8 or 9 needs more, because SDK 11 refuses the out-of-support net8/net9 Android and iOS workloads
(NETSDK1202). This block worked for MoneyFox (net8), WeightTracker (net8) and Profitocracy (net9):

```xml
<!-- SailfishOnly=true builds the Sailfish head alone; the other heads still build with their own SDK -->
<TargetFrameworks Condition="'$(SailfishOnly)' != 'true'">net8.0-android;net8.0-ios</TargetFrameworks>
<TargetFrameworks>$(TargetFrameworks);net11.0-sailfish</TargetFrameworks>

<PropertyGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'sailfish'">
	<MauiVersion>11.0.0-rc.1.26451.6</MauiVersion>
</PropertyGroup>
<ItemGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'sailfish'">
	<!-- the app's own pinned MAUI reference (8.0.14, 9.0.90, …) moves to MAUI 11 on this head only -->
	<PackageReference Update="Microsoft.Maui.Controls" Version="$(MauiVersion)" />
</ItemGroup>

<!-- no MAUI 11 counterpart: keep it on the other heads -->
<PackageReference Include="Microsoft.Maui.Controls.Compatibility" Version="8.0.14"
                  Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) != 'sailfish'" />
```

Build with `dotnet build -f net11.0-sailfish -p:SailfishOnly=true`. Do not override `TargetFrameworks` on the
command line: a global property also reaches project references and breaks them (NETSDK1005). Library version caps,
central package management, SkiaSharp's native library and warnings-as-errors are in
[porting-existing-apps.md](porting-existing-apps.md#build-and-restore).

## 3. Platforms/SailfishOS (optional, recommended)

```bash
dotnet new maui-sailfish-platform       # in the project directory
```

adds the counterpart of `Platforms/iOS/AppDelegate.cs` / `Platforms/Android/MainActivity.cs`:

```
Platforms/SailfishOS/
  Program.cs              Main: new SailfishApplication().Run(args)
  SailfishApplication.cs  : SailfishMauiApplication — CreateMauiApp() and the native event overrides
```

MAUI's window lifecycle (and with it `Application.OnStart/OnSleep/OnResume`) follows Android's order, driven by
`Qt.application.state` and the window's focus:

| What happens on the phone | `Qt.application.state` | MAUI events | `Application` |
|---|---|---|---|
| App starts | Active | `Created` (MAUI), `Activated` | `OnStart` |
| Minimized to its cover, or a system dialog in front | Active → Inactive | `Deactivated`, `Stopped` | `OnSleep` |
| Further to Hidden/Suspended while stopped | Inactive → Hidden/Suspended | none (already stopped) | — |
| Back from the cover | → Active | `Resumed`, `Activated` | `OnResume` |

Sailfish reports the cover and a system dialog both as Inactive, so a system dialog (e.g. "USB cable connected")
also raises `OnSleep`/`OnResume`, where Android raises only `onPause`. An app that must tell them apart reads
`OnCoverStatusChanged` (only when it has a cover) or `OnApplicationStateChanged`.

The overrides are the native Sailfish/Qt events, with the platform's own values:

| Override | Native source | iOS / Android counterpart |
|---|---|---|
| `OnLaunched(string[] arguments)` | window created, before the Qt loop | `FinishedLaunching` / `OnCreate` |
| `OnApplicationStateChanged(SailfishApplicationState)` | `QGuiApplication::applicationStateChanged` (Active, Inactive = cover or system dialog, Hidden, Suspended) | `OnActivated`/`OnResignActivation` / `OnResume`/`OnPause` |
| `OnOrientationChanged(SailfishOrientation)` | Silica window orientation | `ViewWillTransitionToSize` / `OnConfigurationChanged` |
| `OnCoverStatusChanged(SailfishCoverStatus)` | Silica cover on the home screen | — |
| `OnCoverActionTriggered(int index)` | cover action tapped (`SailfishCover.SetActions`) | `PerformActionForShortcutItem` |
| `OnColorSchemeChanged(SailfishColorScheme)` | ambience light/dark (`Theme.colorScheme`) | `TraitCollectionDidChange` / `OnConfigurationChanged` |
| `OnInputMethodChanged(bool visible, Rect keyboard)` | `Qt.inputMethod` visibility and rectangle | keyboard notifications / `OnApplyWindowInsets` |
| `OnQuitting()` | `QGuiApplication::aboutToQuit` (closed from the home screen, `Application.Quit`) | `WillTerminate` / `OnDestroy` |

Shared code can subscribe without the folder, like `AddiOS`/`AddAndroid`:

```csharp
using Microsoft.Maui.LifecycleEvents;

builder.ConfigureLifecycleEvents(events =>
{
#if SAILFISH
	events.AddSailfish(sf => sf
		.OnApplicationStateChanged((app, state) => Console.WriteLine($"state {state}"))
		.OnQuitting(app => SaveState()));
#endif
});
```

Not there yet: opening a URL or file in the app (`OpenUrl` / `OnNewIntent`; needs `.desktop` MIME types and a
D-Bus service for a running instance), display blank/lock and memory pressure (MCE over D-Bus).

Without the folder the build generates a `Main` that finds `MauiProgram.CreateMauiApp()`. With
`Platforms/SailfishOS/Program.cs` present, generation turns off by itself. The generated `Main` finds the method by
reflection, and a trimmed publish keeps it only on a class whose name ends in `MauiProgram`; an app that builds its
`MauiApp` elsewhere writes its own `Program.cs`.

## 4. Build, package, run

```bash
dotnet build   -f net11.0-sailfish
dotnet publish -f net11.0-sailfish                 # bin/SailfishRpm/harbour-<app>-<version>.aarch64.rpm
dotnet build   -f net11.0-sailfish -t:SailfishRun  # deploy + launch on the phone
```

Packaging and store properties (`SailfishPermissions`, `SailfishHarbour`, `SailfishCover`, …) are listed in
[sailfishos-packaging.md](sailfishos-packaging.md). For F5 in VS Code install the
[MAUI Sailfish Tools](https://github.com/Pastajello-Organization/sailfishos_maui_tools) extension (it finds
the `net11.0-sailfish` project by itself), or copy `.vscode/launch.json` and `tasks.json` from
`dotnet new maui-sailfish` (their debug-attach entries need `SF_TOOLS_DIR` pointing at the repo's `tools/`).

## 5. Check the shared code

Shared code written for Android and iOS assumes one of them in a few places. On the Sailfish head these places fail
to compile, throw at startup, or turn a feature off without any message. The checklist below comes from porting
real apps (dotnet/maui-samples, MoneyFox, Profitocracy, WeightTracker, GitTrends, GameSpur). Each row names an app
that hit the problem. [porting-existing-apps.md](porting-existing-apps.md#what-each-port-changed) has each port's
code.

| Look for | Seen in | Change |
|---|---|---|
| `#if ANDROID \|\| IOS` around a feature or a value | BugSweeper (double-tap recognizer: tiles never opened), WordPuzzle (font size, layout multiplier: did not compile) | add `\|\| SAILFISH` where the feature works here |
| `MauiApplication.Current.Services`, `MauiUIApplicationDelegate.Current.Services` | WeatherTwentyOne | `IPlatformApplication.Current.Services` (any head) |
| Services set up in `Platforms/Android/MainApplication.cs` or `Platforms/iOS/AppDelegate.cs` | MoneyFox (MSAL client), GitTrends (`CreateMauiApp(AppInfo.Current)`) | the same in `Platforms/SailfishOS/SailfishApplication.cs` (step 3) |
| `{OnPlatform iOS=…, Android=…}` without `Default` | Profitocracy (icon font: glyphs in the wrong font) | add `Default=…`; in the element form, `<On Platform="iOS,SailfishOS">` (GameSpur) |
| `Shell.Current` in `App`'s constructor | WeatherTwentyOne (NullReferenceException at start) | use the Shell instance itself (`(Shell)MainPage`); MAUI 11 sets `Shell.Current` once the window exists |
| Converters whose `ConvertBack` throws `NotImplementedException` | GameSpur (13 converters) | return `Binding.DoNothing`; MAUI 11 calls `ConvertBack` on TwoWay bindings while the context propagates |
| Plugins with Android/iOS implementations only (`*.Current`, `Use…()` registrations) | Profitocracy (`UseLocalNotification`), GitTrends (Plugin.StoreReview, Shiny notifications and jobs), GameSpur (Firebase push) | skip the registration under `#if !SAILFISH`, or register a Sailfish implementation of the plugin's interface |
| Handlers or effects registered per platform | GameSpur (`AddHandler<Shell, TabbarBadgeRenderer>`), GitTrends (`ConfigureSyncfusionCore()`), MoneyFox (Sharpnado tab effects) | keep platform handlers under `#if ANDROID \|\| IOS`; skip `ConfigureSyncfusionCore` on Sailfish; give routing effects a Sailfish `PlatformEffect` |
| Packages with Android/iOS assets only (NU1202) | WeightTracker (Microcharts.Maui 1.x), GameSpur (Sharpnado.Maui.Nuke, Vapolia.StrokedLabel) | a release with a plain `net` asset (Microcharts 2.0), or condition the package out and define the types the code and XAML use |
| SkiaSharp anywhere in the app, charts included | MoneyFox, Profitocracy, WeightTracker, GitTrends (`DllNotFoundException: libSkiaSharp`; empty chart areas) | `SkiaSharp.NativeAssets.Linux` in the app's SkiaSharp version, and `Microsoft.Maui.SailfishOS.SkiaSharp` for the views (SkiaSharp 3.x) |
| A UraniumUI `PickerField` reset with `SelectedItem = ""` | WeightTracker (100 % CPU, app hangs) | reset with `null`; this happens on every MAUI 10+ head |

Android and iOS show the same MAUI 10/11 problems (`Shell.Current`, `ConvertBack`, UraniumUI) once the app moves
to that MAUI version. The Sailfish head is just the first MAUI 11 head an older app gets.

## Caveats

- Third-party MAUI libraries without a Sailfish build restore their `net11.0` assets. They build (CA1416
  warnings name the calls), but platform-specific parts may not work on the phone. A library handler built on a
  stock one (UraniumUI's `Button`) falls back to the Sailfish handler of that control. A control whose
  plain-`net` handler throws when it creates its platform view renders its own drawing if it has one (Syncfusion's
  `SfView` controls), otherwise an empty container. Each case logs one `[QT_HOST][WARN] … failed for …` line per
  handler type.
- Some Essentials are limited by the platform: the user's address book is closed to third-party apps (`Contacts`
  returns nothing), and there is no speech engine, geocoder or passkey authenticator. See
  [porting-existing-apps.md](porting-existing-apps.md#platform-behaviour-that-differs-from-androidios).
- [porting-existing-apps.md](porting-existing-apps.md) lists what broke in real apps (net8/net9 heads, version
  caps, SkiaSharp, plugins, MAUI 10/11 changes) and how each was fixed.
- Every machine that builds the project, CI included, needs the workload manifest from step 1 (on CI:
  `dnx Microsoft.Maui.SailfishOS.Workload install --yes` before the build).
  To keep the head opt-in, guard the TFM line: `Condition="'$(EnableSailfish)' == 'true'"`.
