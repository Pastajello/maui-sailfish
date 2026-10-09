# Sailfish OS Packaging (harbour RPM)

This document describes how to build and package a .NET MAUI app for
Sailfish OS as a harbour-compliant RPM, using the `Microsoft.Maui.Platforms.SailfishOS` package (project
`src/Linux.SailfishOS`) and its MSBuild packaging targets.

## Overview

Sailfish OS apps distributed through the Jolla Store / OpenRepos must follow
the [harbour validation rules](https://harbour.jolla.com/faq). The packaging
targets in `src/Linux.SailfishOS/buildTransitive/Microsoft.Maui.Platforms.SailfishOS.targets`
produce an RPM that follows those conventions:

| Item | Location |
|------|----------|
| Package name | `harbour-<name>` (validated) |
| App binaries | `/usr/share/harbour-<name>/` |
| Launcher entry | `/usr/bin/harbour-<name>` — **symlink to the ELF** in `/usr/share/harbour-<name>/` |
| Desktop entry | `/usr/share/applications/harbour-<name>.desktop` |
| Icons (86/108/128/172) | `/usr/share/icons/hicolor/<size>x<size>/apps/harbour-<name>.png` |
| Dependencies | none (`AutoReqProv: no`, self-contained publish) |

The launcher is a symlink, not a shell script: Sailfish OS launches grid apps
through `invoker`/`sailjail`, which reject non-ELF `Exec` targets
("is not elf binary", exit 1). A symlink keeps `/usr/bin/<pkg>` an ELF while
the real binary (and `libsailfishhost.so`, `qml/`) live in `/usr/share/<pkg>/`.

### Sandbox and permissions

Every build (Debug and Release) runs the app in the Sailjail sandbox, like a
Jolla Store app, so Debug already sees the permissions and data directories a
Harbour build gets. Declare what the app needs in the project file, as in the
Android manifest:

```xml
<SailfishPermissions>Location;Pictures</SailfishPermissions>
```

The desktop entry then carries an `[X-Sailjail]` section with
`Permissions=Internet;Location;Pictures`, `OrganizationName` and
`ApplicationName`. Sailjail asks the user once, at the first launch.
`Internet` is always added: the sandbox has no network otherwise, and every
other MAUI head has it without asking. `SailfishSecureStorage=Secrets` adds
`Secrets`. The names are the files in `/etc/sailjail/permissions/` on the
phone (`Location`, `Camera`, `Contacts`, `Pictures`, `WebView`, …).

At runtime `Permissions.CheckStatusAsync`/`RequestAsync` report `Granted` for
a declared permission and `Denied` otherwise. Request never shows a dialog,
since Sailjail only asks at launch. `Geolocation` and `Contacts` throw
`PermissionException` when their permission is missing, as on Android/iOS.
`Contacts` grants the contacts D-Bus names and directories but not the user's
address book, which is privileged data that only system apps open (see
[porting-existing-apps.md](porting-existing-apps.md)). A
`WebView` without the `WebView` permission logs a warning, because Gecko cannot
start in the sandbox without it.

`<SailfishSandboxing>false</SailfishSandboxing>` opts out with
`[X-Sailjail] Sandboxing=Disabled`, the pattern used by system and sideloaded
apps on Sailfish OS 4.4+ (e.g. fingerterm, jolla-settings). An unsandboxed app
keeps its data under `~/.config|.local/share|.cache/<Assembly>` instead of
`<org>/<pkg>`, reaches services Sailjail has no permission for (the
flashlight), and cannot go to Harbour. (`X-Nemo-Sandboxing-Disabled=` and
`X-Nemo-Application-Type=no-invoker` are parsed by nothing on Sailfish OS 5.2.)

`tools/sf run` starts `/usr/bin/<pkg>` directly over SSH, outside the jail
(sailjaild refuses launches from an SSH session), so the Permissions policy
and data directories match the sandbox there, but the firejail filtering does
not. To check a feature under the real sandbox, launch the app from the app grid.

Because the app is published **self-contained**, the whole .NET runtime is
bundled inside the package and no runtime dependencies are declared.

## Prerequisites

- .NET SDK (net11.0). Nothing else builds the package: the `SailfishRpmPack` MSBuild task writes the RPM itself
  (lead, signature and main header, gzip-compressed cpio payload), so `dotnet publish -p:CreateSailfishRpm=true`
  works on macOS, Linux and Windows without `rpmbuild`, python3 or a shell. File modes do not come from the build
  host: directories and ELF or `#!` files are 0755, everything else 0644. `/usr/bin/<package>` is a symlink declared
  to the task, not created on disk.
- `-p:SailfishRpmBuilder=rpmbuild` uses a host `rpmbuild` with the generated spec instead (Unix only; macOS
  `brew install rpm`, Linux `rpm-build`). The payload is then xz, about a quarter smaller (the sample: 14.1 MB
  instead of 18.2 MB). `SailfishRpmSignKey` (`rpm --addsign`) needs host `rpm` with either builder.

> Note: the official Sailfish SDK (`sfdk`) is not required to *build* the RPM.
> It is only needed if you want to build inside the Sailfish build engine or
> test in the Sailfish OS emulator.

## Build and package

From the repository root:

```bash
# Emulator / x86_64 device
dotnet publish samples/Linux.SailfishOS.Sample/Linux.SailfishOS.Sample.csproj \
  -r linux-x64 -p:SelfContained=true -p:CreateSailfishRpm=true

# 64-bit ARM device
dotnet publish samples/Linux.SailfishOS.Sample/Linux.SailfishOS.Sample.csproj \
  -r linux-arm64 -p:SelfContained=true -p:CreateSailfishRpm=true

# 32-bit ARM (armv7hl) device: builds, never run on a device yet
dotnet publish samples/Linux.SailfishOS.Sample/Linux.SailfishOS.Sample.csproj \
  -r linux-arm -p:SelfContained=true -p:CreateSailfishRpm=true
```

The RPM is written to:

```
samples/Linux.SailfishOS.Sample/bin/SailfishRpm/harbour-sample-<version>-1.<arch>.rpm
```

Example verified output (macOS host, linux-x64):

```
Name         : harbour-sample
Version      : 0.1.0
Release      : 1
Architecture : x86_64
Size         : 86304638
...
/usr/bin/harbour-sample
/usr/share/applications/harbour-sample.desktop
/usr/share/harbour-sample/...
```

## MSBuild properties

| Property | Default | Description |
|----------|---------|-------------|
| `CreateSailfishRpm` | `true` on `net11.0-sailfish`, else `false` | Enables RPM packaging during `dotnet publish` |
| `SailfishPackageName` | `harbour-<ApplicationId last segment>` | RPM package name, must start with `harbour-` |
| `SailfishVersion` | `$(ApplicationDisplayVersion)` | RPM version |
| `SailfishRelease` | `1` | RPM release number |
| `SailfishSummary` | `$(ApplicationTitle)` | RPM summary |
| `SailfishDescription` | `$(Description)` or app title | RPM description |
| `SailfishVendor` | `$(Authors)` | RPM vendor |
| `SailfishLicense` | `$(PackageLicenseExpression)` or `MIT` | RPM license |
| `SailfishRuntimeIdentifier` | *(empty)* → `linux-arm64` | RID of the `net11.0-sailfish` head only; `sailfish deploy` and `tools/sf deploy` pass it instead of a global `-r`, which would also reach the project's other heads |
| `SailfishRpmArch` | derived from RID | `x86_64` / `aarch64` / `armv7hl` |
| `SailfishRpmOutputDir` | `bin/SailfishRpm` | Output directory |
| `SailfishRpmFileName` | `<pkg>-<ver>-<rel>.<arch>.rpm` | Output file name |
| `SailfishRpmDebugPayload` | `true` in Debug, else `false` | Keeps the debugger files in the RPM: PDBs, the NativeAOT `.dbg`, `libmscordaccore.so` / `libmscordbi.so` (vsdbg, SOS) and `createdump`. Set it to `true` to attach to a Release build (see [Payload](#payload)) |
| `SailfishSandboxing` | `true` | `false` opts out of Sailjail (`Sandboxing=Disabled`); Harbour requires the sandbox |
| `SailfishPermissions` | *(empty)* | Semicolon-separated Sailjail permissions on top of `Internet` (see [Sandbox and permissions](#sandbox-and-permissions)) |
| `SailfishSecureStorage` | *(empty)* | `Secrets`: `SecureStorage` requires the Sailfish Secrets daemon (see below) |
| `SailfishOrientation` | `Any` | `Any`, `Portrait` or `Landscape`; the window follows the phone like MAUI on iOS/Android |
| `SailfishCover` | `false` | Generic cover with the app title |
| `SailfishCoverQml` | *(empty)* | Your QML item as the cover content (see below) |
| `SailfishIconSizes` | `86;108;128;172` | Launcher icon sizes generated from `MauiIcon` |
| `SailfishUrlSchemes` | *(empty)* | URL schemes the app opens (`myapp;geo`): `.desktop` `MimeType=x-scheme-handler/…`, `Exec … %U`, the D-Bus `openUrl` method and its activation file; the URL reaches `Application.OnAppLinkRequestReceived` |
| `SailfishMimeTypes` | *(empty)* | File types the app opens (`text/plain;image/png`), the same way; files arrive as `file://` URIs |

### Payload

The RPM carries the publish output minus what never runs on the phone:

| Left out | Why | Kept with |
|---|---|---|
| `libcoreclrtraceptprovider.so` | LTTng provider; Sailfish OS has no `liblttng-ust` (and Harbour disallows it) | — |
| `libclrgc.so`, `libclrgcexp.so` | standalone GCs, loaded only with `DOTNET_GCName` | — |
| `sailfish-launcher` in the payload | the Harbour profile copies it to `/usr/bin/<pkg>`; the default profile starts the apphost | — |
| `*.pdb`, `*.dbg` (NativeAOT symbols) | debugger only | `SailfishRpmDebugPayload=true` |
| `libmscordaccore.so`, `libmscordbi.so` | DAC/DBI for vsdbg and SOS | `SailfishRpmDebugPayload=true` |
| `createdump` | crash dumps; the phone's `core_pattern` is `/bin/false` | `SailfishRpmDebugPayload=true` |
| satellite assemblies (`<culture>/*.resources.dll`) | with `InvariantGlobalization=true` the UI culture is invariant, so none is ever loaded (`SatelliteResourceLanguages=en`) | set `SatelliteResourceLanguages` |

Debug builds keep the debugger files. SailfishKitchen Release: 213 → 171 files, 51.6 → 45.8 MB installed, RPM
15.4 → 13.8 MB.

### Cover

The home-screen cover is off unless the app asks for it:

- `<SailfishCover>true</SailfishCover>` shows the app title on a generic cover;
- `SailfishCover.SetContent(title, lines…)` and `SailfishCover.SetActions(new
  SailfishCoverAction("image://theme/icon-cover-new", () => …))` fill the default
  cover with up to three lines and two actions, and enable it; `IsActive` /
  `ActiveChanged` report when it is visible;
- `<SailfishCoverQml>Cover.qml</SailfishCoverQml>` replaces the content with your
  own QML item (it receives `{title, lines}` in a declared
  `property var mauiCoverData`); actions still come from `SetActions`.

### SecureStorage and Sailfish Secrets

`SecureStorage` keeps values in the Sailfish Secrets daemon: one collection per
app in the encrypted (sqlcipher) storage plugin, locked by the device lock
(readable after the first unlock since boot) and accessible only to the app.
Sailfish Secrets is **not preinstalled**, so an app that relies on it opts in:

```xml
<SailfishSecureStorage>Secrets</SailfishSecureStorage>
```

This adds `Requires: sailfishsecretsdaemon` and
`sailfishsecretsdaemon-secretsplugins-default` to the RPM (both allowed in
Harbour), and adds the `Secrets` permission. At runtime the
store uses Secrets whenever the device has it. Without Secrets it falls back to
an AES file under `~/.config/<app>/`, which is obfuscation only because the key
sits next to the data, and logs a warning. When Secrets later appears, entries
from the file move into it and the file and its key are deleted.
`MAUI_SAILFISH_SECURESTORAGE=file` forces the file store.

RID to RPM architecture mapping:

| RuntimeIdentifier | RPM arch |
|-------------------|----------|
| `linux-x64` | `x86_64` |
| `linux-arm64` | `aarch64` |
| `linux-arm` | `armv7hl` |

### Locale and right to left

.NET takes the culture from `LANG`, which an app started from the app grid gets from the user's language setting, and
formats through the phone's ICU (`libicu` 73 on SFOS 5.2), as on Linux desktops. With `InvariantGlobalization=true`
(smaller, no ICU) every culture is the invariant one: dates and numbers format as in English and
`CultureInfo.CurrentUICulture` says nothing about the user's language. `AppInfo.RequestedLayoutDirection` then still
follows the language through Qt (`Qt.application.layoutDirection`, right to left for Arabic or Hebrew), read once the
host runs; before that it comes from the current UI culture. An app started over SSH (`tools/sf run`) runs with the
SSH session's `LANG`, which may be empty.

## Installing on a device

From an app (template or package consumer): `dotnet run -f net11.0-sailfish`
publishes, installs and launches, then streams the app log until it exits (Ctrl+C
stops the app); `dotnet build -f net11.0-sailfish -t:SailfishRun` returns after
the launch window instead. The first run asks for the
phone (address, SSH user, developer-mode password — Settings > Developer tools >
Remote connection) through the `sailfish` tool's `setup` (the package carries it), stores it in
`~/.config/maui-sailfish/connect.info` (0600; `known_hosts` next to it) and
installs your SSH key; `-t:SailfishSetup` runs just that
(`-p:SailfishSetupForce=true` to change the phone). Lookup order for the
settings: `$SF_CONNECT_INFO`, `connect.info` in the project directory, a
checkout's root `connect.info`, the per-user file. There is no built-in device
address. The questions need a terminal: the .NET 11 CLI runs targets in the
background MSBuild server, so from a shell use
`DOTNET_CLI_USE_MSBUILD_SERVER=0 dotnet build -f net11.0-sailfish -t:SailfishSetup`
(the template's VS Code F5 and tasks set it); otherwise, and in CI, the missing
setup fails with the exact `dotnet "…/sailfish.dll" setup` command to run in a terminal (on Windows always: run it,
or `sailfish setup`, once).


Sailfish OS ships no `zypper` CLI; the system package-manager front-end is
PackageKit with the **zypp** backend (`pkcon backend-details` → `Name: zypp`),
so `pkcon install-local` is the zypper path on-device. Copy the RPM to the
device and install as root:

```bash
scp bin/SailfishRpm/harbour-sample-0.1.0-1.aarch64.rpm defaultuser@<device-ip>:
ssh defaultuser@<device-ip>
devel-su pkcon install-local -y harbour-sample-0.1.0-1.aarch64.rpm
```

Updates are the same command with a higher `Release`; `pkcon remove -y
harbour-sample` uninstalls cleanly (no payload leftovers). The end-to-end
acceptance test for the whole flow (payload audit → pkcon install/update/
remove → desktop/icon/permission compliance → launcher-path launches) is

```bash
./tools/sf package-test
```

For day-to-day iteration use `dotnet run -f net11.0-sailfish` or `sailfish deploy --run` (publish → upload with a
sha256 check → `rpm -Uvh --force` → run); in a checkout `tools/sf deploy --run --screenshot` adds the full
verify (RPM vs device file digests, `rpm -V`). Both are faster than the PackageKit round-trip but bypass the zypp stack.

## harbour compliance notes

- The package declares **no dependencies** (everything the app needs is bundled)
  unless `SailfishSecureStorage=Secrets` adds the Sailfish Secrets daemon —
  both allowed in Harbour.
- `/usr/bin/<pkg>` is a symlink to the bundled ELF; the desktop entry declares
  the Sailjail sandbox (`Permissions=`, `OrganizationName`, `ApplicationName`)
  unless `SailfishSandboxing=false` opts out, which Harbour rejects.
- The app reports its Wayland app_id as the package name
  (`QtHostRuntime.ResolveAppId`), so lipstick associates the window with the
  desktop entry (launcher icon, events view) like for any normal app.
- If your app uses WebView, note that Sailfish's WebKit is not bundled; the
  current SailfishOS platform project does not ship a BlazorWebView handler.

## Harbour (Jolla Store) profile

`-p:SailfishHarbour=true` builds an RPM that passes Jolla's
[sdk-harbour-rpmvalidator](https://github.com/sailfishos/sdk-harbour-rpmvalidator)
(verified 2026-09-26 on the sample: PASSED with no warnings):

```bash
dotnet publish -c Release -r linux-arm64 -p:SelfContained=true -p:CreateSailfishRpm=true \
  -p:SailfishHarbour=true -p:SailfishPermissions=Pictures \
  -p:SailfishOrganizationName=org.example
```

What changes against the default (sideloading) layout:

| | default | Harbour |
|---|---|---|
| `/usr/bin/<pkg>` | symlink to the .NET apphost | **native launcher** (`sailfish-launcher`, 7 KB): PIE, exports `main()` for the `silica-qt5` booster, links `__libc_start_main`, loads `libhostfxr.so` and runs `<app>.dll` through the .NET hosting API |
| payload | `/usr/share/<pkg>/` | `/usr/share/<pkg>/lib/` (no apphost, no `createdump`) |
| launcher rpath | — | `$ORIGIN/../share/<pkg>/lib` (written into the prebuilt launcher's placeholder at packaging) |
| desktop | absolute icon path | `Icon=<pkg>`, `X-Nemo-Application-Type=silica-qt5`; `SailfishSandboxing=false` is an error |
| RPM | `Vendor:` set | no `Vendor:` (not allowed); files root:root, non-ELF 0644 |

Verified on the device (Jolla Phone, SFOS 5.2): the Harbour RPM installs, and
the `page`, `f3` and `f4` acceptance legs pass launched through the native
launcher (`/usr/bin/harbour-sample`), with the Sailjail data directories and
the Permissions policy (undeclared Camera/Location → Denied). A tap in the
app grid (2026-09-26) launches it through invoker + Sailjail: firejail with the
`org.maui`/`harbour-sample` template, NoNewPrivs, its own mount namespace,
data under `~/.config|.local/share/org.maui/harbour-sample`, the four icon
sizes and the app's cover on the home screen. (sailjaild refuses launch prompts
from an SSH session — `invalid uid -1` — so this path needs the tap.) The launcher (`tools/sf native-build`, also `SF_ARCH=armv7hl`)
ships in the package next to the shim (`runtimes/<rid>/native/`).

## Known limitations

- `MauiImage` and `MauiIcon` go through MAUI's resizetizer on the Sailfish head
  (as on Android/iOS): SVGs become PNGs under the name XAML uses, `BaseSize`,
  `TintColor` and `Resize` apply, and the app icon is composed from `MauiIcon`,
  `ForegroundFile` and `Color`. `MauiSplashScreen` is ignored (Sailfish apps
  have no splash screen; the window shows the first page directly). The head
  uses the resizetizer's contract for external backends
  (`ResizetizerPlatformType=wpf` and the `ResizetizerAfter…ProcessingTargets`
  hooks), so `MauiImage`, `MauiFont` and `MauiAsset` of referenced projects are
  included, as on the in-box heads. They land in `images/`, `fonts/` and, for
  assets, under their `LogicalName` at the app root (`Resources/Raw` keeps its
  subfolders; `FileSystem.OpenAppPackageFileAsync("sub/file.txt")` reads them there).
- A PNG `MauiIcon` must be square (8-bit, non-interlaced); it is
  resampled to 86, 108, 128 and 172 px (`SailfishIconSizes`). Ship at least
  172x172 — smaller sources are upscaled with a build warning.
  `-p:SailfishSkipIconCheck=true` copies the file unchanged as the 108 px icon.
- No `sfdk`/Sailfish SDK integration; the RPM is built by the `SailfishRpmPack` task
  (or a host rpmbuild with `-p:SailfishRpmBuilder=rpmbuild`).
