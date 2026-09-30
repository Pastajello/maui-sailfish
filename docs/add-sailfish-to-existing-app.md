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

`MauiProgram.cs` stays as it is. `UseMaui=true` can stay too; the template turns it off on the Sailfish head
only so that head builds on machines without the MAUI workloads (then pin `MauiVersion`, or restore fails
with NU1015).

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

MAUI's `Window.Activated/Deactivated/Resumed/Stopped` fire as on the other platforms. The overrides are the
native Sailfish/Qt events, with the platform's own values:

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
`Platforms/SailfishOS/Program.cs` present, generation turns off by itself.

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

## Caveats

- Third-party MAUI libraries without a Sailfish build restore their `net11.0` assets. They build (CA1416
  warnings name the calls), but platform-specific parts may not work on the phone.
- Every machine that builds the project, CI included, needs the workload manifest from step 1 (on CI:
  `dnx Microsoft.Maui.SailfishOS.Workload install --yes` before the build).
  To keep the head opt-in, guard the TFM line: `Condition="'$(EnableSailfish)' == 'true'"`.
