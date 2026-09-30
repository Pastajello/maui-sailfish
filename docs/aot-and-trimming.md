# Runtime and performance: CoreCLR payload, trimming, ReadyToRun, NativeAOT

> **Status (refreshed 2026-09-28).** Decisions from 2026-09-15 stand:
> **`trimr2r` is the default Release profile** (`--jit` / `--trim` as an escape hatch, §2.4, item 1.7), and
> **NativeAOT is deferred** (gate G2, Phase 4 frozen). Open: 2.6/2.8/2.9 (return with Phase 4), B5,
> 2.14 and Phase 5. Measurements: macOS arm64 host, SDK `11.0.100-rc.1.26425.128`, MAUI `11.0.0-rc.1.26451.6`, RID
> `linux-arm64`; those marked "preview.7" predate switching the SDK pin to RC1. The "current state" description
> in §1 is the starting point from 2026-09-15 (when `jit` was the default).

## 1. Current state

The app is published as **self-contained CoreCLR**
(`tools/sf deploy`, `dotnet publish -c Release -r linux-arm64
-p:SelfContained=true`). There is no Mono, no NativeAOT; trimming and ReadyToRun are
available as opt-in (§2.2), but disabled by default. The whole runtime ships in the RPM:

| File | Size | Role |
|---|---|---|
| `libcoreclr.so` | 5 236 616 B | CoreCLR |
| `libclrjit.so` | 3 763 272 B | JIT (tiered) |
| `libclrgc.so` / `libclrgcexp.so` | 666 584 / 713 608 B | GC |
| `libhostfxr.so` / `libhostpolicy.so` | 312 768 / 306 016 B | hosting |
| `libmscordaccore.so` | 1 671 824 B | **DAC** — basis of SOS/vsdbg |
| `libmscordbi.so` | 1 257 592 B | **DBI** — basis of managed debug |
| `libsailfishhost.so` | 3 130 672 B | our Qt/Silica shim |

`Linux.SailfishOS.Sample.runtimeconfig.json`:
`"includedFrameworks": [{ "name": "Microsoft.NETCore.App", "version":
"11.0.0-rc.1.26425.128" }]` — self-contained, not framework-dependent.

Apphost: `ELF 64-bit LSB pie executable, ARM aarch64, interpreter
/lib/ld-linux-aarch64.so.1` → **glibc**, so RID `linux-arm64` (not
`linux-musl-arm64`) is correct; Sailfish OS 5.2 has glibc 2.34
([`architecture.md`](architecture.md)).

`jit` profile payload: **94 MB / 283 files / 225 assemblies**; RPM ~28.6 MB
(measured on a Debug build from the preview.7 era).

To fix along the way: `src/Linux.SailfishOS/Linux.SailfishOS.csproj:9`
(`Description`) claims "Sailfish OS (musl-based Linux)" — this is false and
misleading when choosing the RID.

## 2. Measurement matrix (`-r linux-arm64`, net11.0, SDK RC1, macOS host)

Files and assemblies counted recursively over the `publish/` directory (`find -type f`,
`find -name '*.dll'`) — not via `ls`, which skips subdirectories.

| Profile | Payload | Files | DLL | `qml/` | Result | IL warnings |
|---|---|---|---|---|---|---|
| `jit` (default, = state from Q22A) | 94 MB | 283 | 225 | 36 | exit 0 | 0 |
| `jit` + `PublishReadyToRun` | 99 MB | 283 | 225 | 36 | exit 0 | 0 |
| `trim` (`TrimMode=partial`) | **29 MB** | 149 | 91 | 36 | exit 0 | 0 |
| `trim` + `TrimMode=full` | **27 MB** | 134 | 76 | 36 | exit 0 | **29** |
| `trimr2r` | 44 MB | 149 | 91 | 36 | exit 0 | 0 |
| `PublishAot` (NativeAOT) | — | — | — | — | **exit 1** | 37 *(preview.7)* |

Warning breakdown:

- `TrimMode=full` (RC1): 19× IL2026, 6× IL2072, 3× IL2070, 1× IL2075 = **29**.
  The same variant on preview.7 gave 55 — there are fewer on RC1.
- `PublishAot` (preview.7): 19× IL2026, **11× IL3050**, 3× IL2072, 3× IL2070,
  1× IL2075 = 37. Deliberately not repeated on RC1: §2.2 added a guard that does not
  let `PublishAot` through without passing gate G2.

**The `qml/` column is the most important one here:** 36 files in every profile, likewise
`images/`, `libsailfishhost.so` and the apphost — trimming does **not** lose the `Content`
payload. Risk B10 from §2.1 is therefore closed for `trim`/`trimr2r`; for AOT
it stays open (a different publish mechanism).

Trimming really cuts MAUI: `Microsoft.Maui.Controls.dll`
1 664 296 B (NuGet `11.0.0-rc.1.26451.6`) → 1 013 248 B after partial trim
*(measured on preview.7)*.

**Honesty about warnings:** all measured warnings point to our
assembly (`Linux.SailfishOS.csproj`), none to `Microsoft.Maui.*`. This does **not**
prove MAUI's AOT compatibility — the absence of warnings from assemblies the trimmer does not
analyze deeply is not proof of cleanliness. The only decisive test
is a run of `tools/sf matrix` (18 legs) on the device.

Places ILLink actually flagged (`/tmp/pub-full.log`,
`/tmp/pub-aot.log`):

| Place | Construct | Warning |
|---|---|---|
| `Platform/QtHost/QtHostBridge.cs:32,33,39,49,139` | `JsonSerializer.Serialize<T>` (reflection) | IL2026 / IL3050 |
| `Platform/SailfishMauiApplication.cs:1268,1271` | `Assembly.GetType(string)` / `Assembly.GetTypes()` | IL2026 |
| `Platform/SailfishMauiApplication.cs:1306` | `Activator.CreateInstance(Type)` | IL2072 |
| `RunQt{Page,Shapes,Visual}Diagnostics` | three copies of the same pattern (below: A6) | IL2026/IL2072 |

### 2.1 Inventory of constructs in our code (audit 2026-09-15)

Scope: `src/Linux.SailfishOS` (~13.9k LOC C#), both samples, project files and
targets. Baseline facts (grep over the whole repo): **zero** hits for `dynamic`,
`System.Linq.Expressions`, `DynamicMethod`, `Reflection.Emit`, `XmlSerializer`,
`BinaryFormatter`, `.resx`/`ResourceManager`, `EventSource`/`DiagnosticListener`,
`AppContext.SetSwitch`, `AssemblyLoadContext`, `ActivatorUtilities`,
`DependencyService`, `JsonSerializerContext`,
`NativeLibrary`/`SetDllImportResolver`/`LibraryImport`. All real exposure
sits in three files: `QtHostPageRenderer.cs`, `QtHostInput.cs`,
`SailfishMauiApplication.cs`.

**(a) Hard NativeAOT blockers**

| # | Place | Construct | Shape of the fix |
|---|---|---|---|
| A1 | `QtHost/QtHostInput.cs:135-137` (call `:428`) | `typeof(TapGestureRecognizer).GetMethod("SendTapped", Instance\|NonPublic\|Public, …)` + `MethodInfo.Invoke` — the only way to fire `Tapped`/`Command`; when the method is missing, the code **silently drops the tap** (`:421-425`) | Cheapest possible fix: in MAUI 11 `SendTapped` is **public** (`[EditorBrowsable(Never)]`) — a comment in the same place documents this, and the internal→public change came with the migration to MAUI 11. A direct call is enough |
| A2 | `QtHost/QtHostPageRenderer.cs:2102, 2273, 2960` | `JsonSerializer.Serialize(ops)`, `ops` = `List<Dictionary<string, object?>>` → serialization by **runtime** type | Manual JSON builder (patterns exist: `QtHostBridge.cs:120-138`, `QtHostCollectionBridge.cs:742-743,784`, `QtHostDiag.cs:170-190`) or explicit DTOs + `JsonSerializerContext` |
| A3 | `QtHost/QtHostPageRenderer.cs:2563` (arguments `:2500-2545`) | `JsonSerializer.Serialize(props)` on **anonymous types** (Alert/Prompt/ActionSheet) — anonymous types cannot be registered in a source-gen context | Explicit `record`s + context or a manual builder |
| A4 | `QtHost/QtHostPageRenderer.cs:2396, 2409` | `JsonSerializer.Serialize(new { text, enabled })` — the same problem | Manual builder (2 fields: string + bool) |
| A5 | `QtHost/QtHostPageRenderer.cs:1838-1840` (path from `:1824`) | `item.GetType().GetProperty(path!)` + `GetValue` over **arbitrary app model types** (Picker `ItemDisplayBinding`) | Compiled binding / `TypedBinding` or an explicit `Func<object,string>` contract; otherwise the app must carry rd.xml |
| A6 | `SailfishMauiApplication.cs:1267-1273`+`1306`, `1320-1326`+`1352`, `1478-1484`+`1509` | Three copies: `AppDomain.CurrentDomain.GetAssemblies()` → `GetType(name)` (name from env `MAUI_SAILFISH_QT_HOST_*_TYPE`) → fallback `GetTypes()` + `GetConstructor(Type.EmptyTypes)` → `Activator.CreateInstance` | An explicit factory table `Dictionary<string, Func<Page>>` or diag legs outside Release. Mitigating: the sample also constructs these types statically (`Pages/FeaturesPage.xaml.cs:34,40,43`), so metadata survives; the blocker is only the "arbitrary type name from env" variant |

**(b) Requires rework for trimming or a configuration decision**

| # | Place | Construct | Shape of the fix |
|---|---|---|---|
| B1 | `QtHost/QtHostBridge.cs:32,33,39,49,139` | `JsonSerializer.Serialize` on statically known `string`/`char`/`Enum.ToString()` — goes through the reflection resolver | Own escaping (already exists: `QtHostTextMetrics.JsonString:87-105`, `QtHostPageRenderer.ToJsString:3494`) |
| B2 | `SailfishMauiApplication.cs:4049-4066` | `GetType().Assembly` / `Assembly.GetEntryAssembly()` / `GetCustomAttributes<AssemblyMetadataAttribute>()` → reading `MauiApplicationId` for the Qt/Wayland app_id | A constant generated from MSBuild (`$(ApplicationId)`) or rd.xml; a fallback already exists: env `MAUI_SAILFISH_APP_ID` (`QtHostRuntime.cs:516-518`) |
| B3 | `SailfishMauiApplication.cs:3662, 4405` | `label.SetBinding(Label.TextProperty, new Binding("."))` in the `DataTemplate` of diag pages — an uncompiled binding created at runtime | The dataset is `ObservableCollection<string>` → `label.Text = item?.ToString()` in the factory is enough |
| B4 | `SailfishMauiApplication.cs:1610, 1618` | `VisualStateManager.GoToState(…, CommonStates.Disabled/Normal)` — static on our side, the risk lives in MAUI | rd.xml / `TrimmerRootAssembly` for `Microsoft.Maui.Controls` or the diag leg outside Release |
| B5 | `QtHost/QtHostPageRenderer.cs:1869-1870` | `TimeZoneInfo.Local.GetUtcOffset(midnight)` (DatePicker/TimePicker → epoch ms) | `TimeZoneInfo` on Linux requires tzdata and is sensitive to `InvariantGlobalization` → a guard with a fallback or a documented tzdata requirement in the RPM |
| B6 | the sample does **not** have `InvariantGlobalization` (the former `SailfishQtProbe` had `true`); the only culture-sensitive format is `{0:P0}` in `Pages/TaskDetailPage.xaml:30` | Decision: `InvariantGlobalization`/`HybridGlobalization` in the sample and in `.targets` — otherwise trim/AOT pulls in ICU, which is not on the device |
| B7 | `QtHost/QtHostNative.cs:37-158` — **30× `[DllImport]`**, 0× `LibraryImport` | The whole P/Invoke surface (48 sites, one library `sailfishhost`) | `[LibraryImport]` + `partial` + `[UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])]` + `StringMarshalling.Utf8` (removes SYSLIB1054, source-gen marshalling). Reverse-P/Invoke delegates can stay — they are pinned to static fields (`QtHostRuntime.cs:68-72`) |
| B8 | `QtHost/QtHostRuntime.cs:256,318,373,438,459,488` (`Marshal.AllocHGlobal/FreeHGlobal`), `:182,184,474` (`GCHandle.Alloc/ToIntPtr/FromIntPtr`) | eval/get_property/screen_info/diag/perf/last_error buffers + the `sailfish_host_post` trampoline | `NativeMemory.Alloc/Free` or `ArrayPool`+`Span` — silences interop analyzer warnings |
| B9 | Locating `libsailfishhost.so`: **no** `NativeLibrary.SetDllImportResolver`. Relies on CoreCLR app-local probing — `.so` next to the binary, the launcher is a symlink `/usr/bin/<pkg> → ../share/<pkg>/<AssemblyName>`, the shim linked **without rpath** (`tools/sf native-build:38-47`), scripts do not export `LD_LIBRARY_PATH` | In AOT there is no apphost or `deps.json` → a resolver pointing at `Path.Combine(AppContext.BaseDirectory, "libsailfishhost.so")` or linking with `-Wl,-rpath,'$ORIGIN'` |
| B10 | Resources from `AppContext.BaseDirectory`: `QtHostAdapters.cs:68` (`qml/adapters.json`), `SailfishMauiApplication.cs:319` (`qml/MauiShell.qml`), `QtHostImages.cs:74-97` (probing `images/`, `Resources/Images/`, system icons) | The code is trim-safe (plain `File.Exists`/`JsonDocument`) — the risk sits in MSBuild: the `Content` payload must physically reach publish in new configurations. A test exists: `tools/sf package-test:109,246` |

**(c) Neutral — confirmed, do not touch**

25× `JsonDocument.Parse` (plain DOM, no reflection); ~40 formatting calls with
`CultureInfo.InvariantCulture` (the backend is consistently invariant — the exceptions are
B5/B6); DI in `Hosting/AppHostBuilderExtensions.cs:22,34-70` has correct
`DynamicallyAccessedMembers` annotations and **zero** assembly scanning; handlers created
with explicit `new ScrollViewHandler()` / `new NullViewHandler()` /
`new NullElementHandler()` (`QtHostLayout.cs:46-70`) — they bypass MAUI's reflection-based
factory; the sample XAML has `MauiXamlInflator=SourceGen`, all 20 bindings with
`x:DataType`, zero `Style`/`ResourceDictionary`/`DynamicResource`/`Effects`/
`Shell`/`Routing.RegisterRoute`; `DataTemplate(() => …)` (factories, not
`DataTemplate(Type)`); selecting diagnostic legs is exclusively
`string.Equals(GetEnvironmentVariable(...), "1", Ordinal)` — **no** dynamic
dispatch (the only exception: A6); `GC.GetTotalAllocatedBytes(true)` in the Q18 perf
legs works both in CoreCLR and in AOT.

**Framework-side risks** (pulled in by the MAUI APIs we call):
`ConfigureMauiHandlers`/`AddHandler<TView,THandler>` → `MauiHandlersFactory`
instantiates the handler via reflection (our types have public parameterless ctors,
so preserving them is enough); `MauiApp.CreateBuilder()` + `UseMauiApp<TApp>()`;
`Window.AlertManager`/`IAlertManagerSubscription` (resolution via
`GetService(Type)`); `VisualStateManager` (states by name); MAUI's reflection-based binding
engine for `new Binding(".")` and `ItemDisplayBinding`;
`TapGestureRecognizer.SendTapped` (a surface without a stable contract);
`DataTemplate.CreateContent()` (risk only for third-party `DataTemplate(Type)`);
`Microsoft.Maui.Controls.Internals` (`QtHostAlertSubscription.cs:2` — a versioning
risk, not trim); MAUI Essentials in the sample (`Pages/EssentialsPage.xaml.cs`, on
Linux mostly `Unsupported*` implementations).

### 2.2 What is already in the code (Phase 1)

**MSBuild** — `src/Linux.SailfishOS/buildTransitive/Microsoft.Maui.SailfishOS.targets`:

- `SailfishTrim` (→ `PublishTrimmed`), `SailfishTrimMode` (→ `TrimMode`, default
  `partial`), `SailfishReadyToRun` (→ `PublishReadyToRun`). All default to
  `false`, so a publish without flags gives a payload identical to the one accepted in Q22A.
- Target `_ValidateSailfishReleaseProfile` with four errors and a profile
  message. `BeforeTargets` must include **`Build`**: ILC compilation is a
  dependency of `Publish`, so a hook only on `Publish` fires *after* it and instead of
  our error you see `llvm-objcopy not found` — measured, not predicted.

Paths verified by running them (not copied from documentation):

| Case | Result |
|---|---|
| `-p:SailfishTrim=true -r linux-arm64 -p:SelfContained=true` | exit 0, `Sailfish: payload profile trim=true trimMode=partial readyToRun=false`, 149 files / 91 dll / 29 MB |
| same + `-p:SailfishReadyToRun=true` | exit 0, 44 MB |
| `-p:PublishAot=true` | **error**: "PublishAot is not supported by this backend yet (gate G2 …)" |
| `-p:SailfishTrim=true` **without `-r`** | **error**: "only support Linux RuntimeIdentifiers … got `osx-arm64`" |
| `-p:SailfishTrim=true -p:SelfContained=false` | **error**: "require a self-contained publish" |

The fourth case closes a trap found only during verification: without `-r`
the SDK **infers the host RID** (`obj/Release/net11.0/osx-arm64/…` is created) and
a trimmed publish on macOS succeeds, producing a payload that cannot
be installed on the device. A `RuntimeIdentifier == ''` check does not catch this.

**Tools** — `tools/lib/sf-lib.sh`: `SF_PROFILE` (default `jit`) and
`sf_set_profile <jit|trim|trimr2r>` setting `SF_PROFILE_PROPS`, expanded unquoted
into the `dotnet publish` line; an unknown profile ends with `sf_die`
(verified: `SF_PROFILE=bogus` → exit 1). `tools/sf deploy` and
`tools/sf package-test` got `--jit|--trim|--trimr2r` flags and log the profile
before publishing. `bash -n` clean on all three. Along the way fixed the
`--help` ranges (`sed -n '2,35p'`): in `sf package-test` the old range `2,36p`
printed `set -euo pipefail`, i.e. it was broken **before** this change.

**Packaging test** — `payload_audit()` in `sf package-test` had a hard threshold
`n_dll >= 100`, which would FAIL on a healthy trimmed payload (91 assemblies).
The threshold is per profile: `jit` → 100, `trim`/`trimr2r` → 60 (measured 225 / 91).
QML threshold (`>= 30`) unchanged — 36 in every profile.

**Still open in Phase 1:** `ILLink.Descriptors.xml` (1.2) and the device
gates 1.5/1.6/1.7.

### 2.3 Root cause of the empty rendering and the bridge fix (2026-09-15)

After switching the SDK to RC1 every matrix leg gave NO-VERDICT, and the screen
showed only the ambient background with the title "MAUI". Elimination showed it was
**not** trimming, **not** the RC1 runtime and **not** the RC1 SDK (builds with the preview.7
runtime and preview.7 SDK behaved identically), and the device's Qt/Silica stack
is healthy (the `sf-qtrun.sh` probe, removed 2026-09-28, passed). A diagnostic trace showed
`Walk` producing `desired=11` hosts and **zero** `applyMauiOps` batches.

Two exceptions found with probes in `ReconcileCore`:

1. `InvalidOperationException: Reflection-based serialization has been disabled
   for this application…` from `JsonSerializer.Serialize(ops)` — op batches
   were serialized with reflection-based STJ, and the published `System.Text.Json.dll`
   (438 272 B vs 2 782 032 B in the runtime pack) has the reflection feature switch
   **substituted at compile time**, so `runtimeconfig` does not override it
   (verified: the switch in runtimeconfig does not help). Because `_current.Add(host)`
   happens before serialization, the first thrown exception permanently bricked the state
   ("hosts exist" on the managed side, zero on the QML side).
2. After switching to the manual serializer: `TypeLoadException: Could not load type
   'System.Collections.IDictionary' from assembly 'System.Runtime'` — a type pattern
   on an interface requires a forwarder from the `System.Runtime` facade, which is not in
   the published payload. Fix: match the **concrete** type
   (`Dictionary<string, object?>`), not the interface.

The fix (items A2/A3/A4/B1 from §2.1, closed): `BridgeValue` got a manual
`Quote` (escaping identical to `QtHostTextMetrics.JsonString`) and a
`Dictionary<string, object?> → JSON object` branch before the `IEnumerable` branch;
all `JsonSerializer.Serialize` sites in `src/` are gone (op batches,
destroy batches, `ApplyOps`, `MenuItemsJson` ×2, dialog payloads moved
from anonymous types to dictionaries). JSON contract unchanged.

The sample's `runtimeconfig.template.json` sets
`System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault=true` as a
**temporary safety net** for reflection-based STJ paths inside MAUI,
which we do not audit; the bridge no longer needs it. To be verified
with a separate run: if the matrix is green without it, the template goes away.

Acceptance state after the fix: `jit` **18/18 PASS** (full sweep). The first full
`trimr2r` sweep gave 16/18: `collection10` (`virtualization: all 9 rows live ==
10 total`) and `input` (`focus=0>=1`) — both assertions read state that can
settle a tick later than the measurement window (on rerun both legs PASS), i.e. a
timing race in the harness, not the payload's fault. Fix: bounded settle windows
before the assertion — `FinishQtCollectionDiagnostics` waits up to 10×200 ms for
`LiveRows == TotalRows` at a scale that fits in the viewport, and
`VerifyQtInputDiagnostics` up to 10×300 ms for `FocusTransitions >= 1`; assertion
semantics unchanged (no settle within the window is still FAIL). The second full
`trimr2r` sweep is gate G1.

At G1b a second, independent tooling defect came up: `payload_audit` in
`sf package-test` read the payload list via `sf-rpm2cpio.py | cpio -it`,
which stopped working when brew rpm 6.1 switched payload compression to zstd
(macOS `cpio`: "Unrecognized archive format"). Listing now goes through
`rpm -qpl` (the rpm reader) with the previous pipeline as a fallback and
path normalization (`/usr/…` and `./usr/…` → `usr/…`); the QML asset counter
also matches nested files (`/qml/.+\.(qml|js|json)$`), because directories in
the rpm list have no trailing slash.

### 2.4 Measurement 1.7: `jit` vs `trimr2r` (`perf` leg, same device, RC1)

| Metric (QT PERF SUMMARY) | `jit` | `trimr2r` |
|---|---|---|
| payload / files / RPM | 94 MB / 283 / 20 MB | 44 MB / 149 / 14 MB |
| firstFrameMs | 66 (second run 85) | 78 (second run 79) |
| rssKb / peakRssKb | 305 460 / 305 460 | 273 964 / 278 508 |
| cpuMs | 10 039 | 9 653 |
| reconcile avg | 5.64 ms ×108 | 5.18 ms ×108 |
| nav avg | 57 ms ×9 | 50 ms ×9 |

Conclusion: the gain is in memory (−10% RSS), CPU (−4%) and size (RPM −30%);
first-frame is inconclusive, because the run bands overlap between profiles.
**Decision 2026-09-15: `trimr2r` is the default Release profile** (`--jit`
stays as an escape hatch). Changing the default requires another full sweep
and G1b on the default path — recorded below after running.

## 3. NativeAOT: two hard blockers

### 3.1 Toolchain — **a Linux build host is required**

ILCompiler generates native code, then the build fails on macOS twice:

1. `error : Symbol stripping tool ('llvm-objcopy' or 'objcopy') not found in PATH`
2. `clang : error : invalid linker name in argument '-fuse-ld=bfd'`

The link line ILC assembles for `linux-arm64` requires an ELF linker and
a Linux sysroot: `--target=aarch64-linux-gnu`, `-Wl,--version-script`,
`-Wl,--export-dynamic`, `-lrt`, `-pie`, plus static libraries from
`microsoft.netcore.app.runtime.nativeaot.linux-arm64` (`libSystem.Native.a`,
`libRuntime.WorkstationGC.a`, `libstdc++compat.a`, `libz.a`, `libzstd.a`, …).
Apple clang will not link this.

**Consequence for this repo:** NativeAOT cannot be built from the current host.
A Linux builder is needed (container, CI or a remote machine) with .NET 11 +
clang/llvm (with `llvm-objcopy`) and an aarch64 glibc sysroot. This **breaks the
README principle** ("There is no on-device build and no SDK container in the loop"):
`zig` was enough for the C++ shim, because `tools/sf native-build` calls `zig c++`
directly — but ILC calls `clang` with hard-coded flags, so zig will not replace it
without patching the ILCompiler targets.

### 3.2 No diagnostics — AOT is not debuggable

The AOT link line has `libeventpipe-disabled.a` and `libstandalonegc-disabled.a`,
and **does not have** `libmscordaccore.so` or `libmscordbi.so`. So in an AOT binary
EventPipe, SOS, vsdbg, `dotnet-trace` and hot reload
(`MetadataUpdater`) do not work. The requirement "Release should be as fast as possible but still
debuggable" cannot be met within **a single** AOT configuration.

### 3.3 Why "like on iOS/Android" does not carry over 1:1

On iOS/Android AOT is done by **Mono** (LLVM) with an interpreter and a soft debugger — a
different runtime than CoreCLR/NativeAOT, with its own DAC. On Sailfish JIT is not
blocked (there is no enforced W^X as on iOS), so full AOT is **not a
prerequisite** for performance: R2R gives precompilation of hot methods while keeping
the JIT and full diagnostics.

## 4. Proposal: three configurations

| Configuration | Runtime | Goal | Debugging |
|---|---|---|---|
| `Debug` | JIT CoreCLR, without trim/R2R | development work, hot reload | DAC/DBI present → vsdbg/SOS, `MetadataUpdater` |
| `Release` | JIT CoreCLR + `PublishTrimmed` + `PublishReadyToRun` | production, 38 MB | **still attachable** |
| `ReleaseAot` (optional) | NativeAOT | max startup/RSS | **no** managed debug — trace log only |

`ReleaseAot` makes sense only if on-device measurement shows that
`Release` does not meet the startup/memory budget.

## 5. Checklist

### Phase 0 — baseline and decisions
- [x] 0.1 — measured 2026-09-15 with the `perf` leg (§2.4): firstFrameMs 66–85 (jit) / 78–79 (trimr2r), RSS 305 460 / 273 964 kB, RPM 20 / 14 MB. A first-frame marker in the trace was not needed — the `perf` leg reads `firstFrameMs` from the shim report.
- [x] 0.2 — decided **YES**: DAC/DBI stay in the payload, and the diagnostic port is conditional (3.1); that is why NativeAOT could not become the default Release profile (Phase 4).
- [x] 0.3 — the budget was not formalized numerically; the G2 decision rests on the §2.4 measurements (RSS −10%, CPU −4%, RPM −30%, cold start within the noise band between profiles). If a concrete budget is ever set, it comes back as a new Phase 4 gate.
- [x] 0.4 — fixed 2026-09-15: `Description` in `Linux.SailfishOS.csproj` says glibc (SFOS 5.2 = glibc 2.34), not musl.

### Phase 1 — Release = trim + R2R (works from macOS, exit 0)

> The default profile stays `jit` until G1/G1b pass. Switching the
> default to `trimr2r` is a separate, deliberate step after on-device acceptance.

- [x] 1.1 Payload flags — as `SailfishTrim` / `SailfishTrimMode` / `SailfishReadyToRun` in `Microsoft.Maui.SailfishOS.targets`, **not** in `Directory.Build.props` (there they would hit the `src/Linux.SailfishOS` library). Default `false` = the Q22A payload. `DebugType` left for the decision at 1.7. Details: §2.2.
- [x] 1.2 — closed 2026-09-15 as a no-action on the backend side: the backend's reflection roots eliminated (page registry 2.3, `Quote` 2.5, `SelfTextLabel` 2.7); the only remaining roots are owned by the app (`ItemDisplayBinding` models, see the suppression in 2.4) and do not require `ILLink.Descriptors.xml` in the backend.
- [x] 1.3 Validation in `.targets`: `_ValidateSailfishReleaseProfile` (PublishAot, missing RID, non-Linux RID, missing `SelfContained`) + profile message. All five paths run — table in §2.2.
- [x] 1.4 `tools/lib/sf-lib.sh` (`SF_PROFILE`, `sf_set_profile`) + `--jit|--trim|--trimr2r` flags in `sf deploy` and `sf package-test`; the `n_dll` threshold in `payload_audit()` made per profile (100 / 60), because the old hard threshold of 100 would fail a healthy trimmed payload.
- [x] 1.5 **GATE G1:** full sweep `./tools/sf matrix` on `trimr2r` = **18/18 PASS** (2026-09-15 20:23, after the settle windows; the first sweep gave 16/18 due to a harness race, §2.3). Full sweep on `jit` also 18/18. **Repeated 2026-09-15 ~21:45 on the default path after the Phase 2 batch: 18/18 PASS.**
- [x] 1.6 **GATE G1b:** `./tools/sf package-test --trimr2r` = **ALL PASS** (payload audit A/B, pkcon fresh/update/uninstall/restore, desktop-file-validate, modes, direct-exec topmost + appid; `windowmodel`/`sailjail` are informational probes per the comment in the script, `direct` is what is graded). **Repeated on the default path: ALL PASS.**
- [x] 1.7 Measurement after: `perf` leg on both profiles, same device, RC1 — table in §2.4. Conclusion: `trimr2r` wins on memory, CPU and size (RSS −10%, RPM −30%); first-frame sits within the noise band (jit 66/85 ms, trimr2r 78/79 ms), so it is inconclusive. **Decision 2026-09-15: `trimr2r` becomes the default Release profile** — the targets set `SailfishTrim`/`SailfishReadyToRun=true` for Release by default, `sf-lib.sh` has `SF_PROFILE` defaulting to `trimr2r`, and `--jit` is the escape hatch; another full sweep and G1b on the default path recorded below.

### Phase 2 — code for trimming/AOT (items from §2.1)
- [x] 2.1 **A1** — closed 2026-09-15: `QtHostInput.SendTapped` calls `TapGestureRecognizer.SendTapped(view, getPosition)` directly (public in MAUI 11); removed the only `MethodInfo.Invoke` and the silent tap-dropping path. `input` leg 4/4 PASS after the drain window (below).
- [x] 2.2 **A2/A3/A4** — closed 2026-09-15 with a manual serializer (§2.3): op/destroy batches and `ApplyOps` go through `BridgeValue.Serialize`, `MenuItemsJson` ×2 and dialog payloads assembled manually/via dictionaries. The cause was deeper than the IL warnings: reflection-based STJ is **disabled by a substituted feature switch** in the published payload and threw at runtime.
- [x] 2.3 **A6** — closed 2026-09-15: registry `SailfishMauiApplication.RegisterDiagnosticPage(leg, factory)`; the sample registers `TextPage`/`ShapesImagesPage`/`VisualPage` in `MauiProgram`, and legs no longer scan `AppDomain` (an unregistered leg fails loudly instead of sweeping reflection under the rug).
- [x] 2.4 **A5** — 2026-09-15: `DisplayText` stays with reflection, but with `[UnconditionalSuppressMessage("Trimming","IL2075")]` and a justification: `ItemDisplayBinding` paths address properties of app models, which the app must root (item 1.2); the sample never sets `ItemDisplayBinding`, so acceptance does not go through this path.
- [x] 2.5 **B1** — closed 2026-09-15: `BridgeValue.Quote` (escaping like `QtHostTextMetrics.JsonString`) replaces `JsonSerializer.Serialize` for string/char/enum/fallback and for keys in `AppendEntry`. The `InvariantGlobalization` decision (B6) stays open.
- [ ] 2.6 **DEFERRED 2026-09-15 (G2 decision):** app-id via `$(ApplicationId)` from MSBuild returns together with Phase 4; with 0 trim warnings nothing blocks today (the env fallback exists).
- [x] 2.7 **B3** — closed 2026-09-15: diag templates do not use `new Binding(".")`; `SelfTextLabel()` mirrors the immutable string from `BindingContextChanged` (trim-clean by construction, and the Q14 C2 leg asserts the text update after INCC Replace, so a regression would be caught).
- [ ] 2.8 **DEFERRED 2026-09-15 (G2 decision):** `[DllImport]` → `[LibraryImport]` (`QtHostNative.cs` 30×) is AOT-readiness hygiene; it returns if a new gate reopens Phase 4.
- [ ] 2.9 **DEFERRED 2026-09-15 (G2 decision):** `NativeMemory`/`ArrayPool` instead of `Marshal.AllocHGlobal` + `GCHandle` casts — same as above.
- [x] 2.10 — decision 2026-09-15: `InvariantGlobalization=true` in the sample (the probe already runs that way; the sample formats and parses exclusively in `InvariantCulture`), so the payload carries no ICU dependency. Full sweep on the invariant binary: **18/18 PASS**. **B5 stays OPEN:** no matrix leg covers pickers, so the `TimeZoneInfo.Local` epoch semantics under invariant wait for a pickers leg.
- [x] 2.11 — closed 2026-09-15 as a **NO-ACTION**: VSM works on the device with trimming (visual leg 12/12 in sweeps), full trim gives no warnings from our code; the risk lives in MAUI and does not materialize in practice.
- [x] 2.12 — closed 2026-09-15: `Directory.Build.props` sets `EnableTrimAnalyzer`/`EnableAotAnalyzer` for the Release configuration, so trim-safety regressions show up as build warnings, not only on a `TrimMode=full` publish. Release build = 0 IL warnings (existing CS0414/CS0618 unchanged).
- [x] 2.13 After the fixes repeat the `TrimMode=full` and `PublishAot` publish — goal: **0 IL warnings from our code**. Achieved 2026-09-15: full trim = **0** warnings from our code (path: 55 preview.7 → 29 RC1 → 18 after the bridge → 3 after the page registry → 0 after `SelfTextLabel`; the only deliberate exception is the IL2075 suppression in `DisplayText`, item 2.4).
- [ ] 2.14 Audit of reflection-based STJ paths **inside MAUI** (not our code). *(2026-09-28: the `IsReflectionEnabledByDefault` switch is no longer in the repo — to recheck whether the audit is still needed.)* Until it is clean, the sample's `runtimeconfig.template.json` keeps `System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault=true` as a deliberate opt-in — it protects against exactly the class of bug that bricked the bridge (§2.3). After the audit: remove the switch and repeat the full sweep.

### Phase 3 — debuggability
- [x] 3.1 — closed 2026-09-15: `sf run --diagnostics` forwards `SF_DIAGNOSTICS=1`, and `sf-run-remote.sh` sets `DOTNET_EnableDiagnostics` conditionally (default 0, so unattended sweeps leave no socket). Verified on the device per PID of the current process: with the flag exactly 1 socket `/tmp/dotnet-diagnostic-<pid>-*`, without the flag 0.
- [x] 3.2 — verified 2026-09-15: the Debug publish **does not contain** the `System.Reflection.Metadata.MetadataUpdater.IsSupported` key (i.e. runtime default `true`), and Release has an explicit `false`; the runtime hot reload knobs (`DOTNET_MODIFIABLE_ASSEMBLIES=debug`, `DOTNET_STARTUP_HOOKS`) go through `sf run --env`.
- [x] 3.3 vsdbg on SFOS (glibc/libstdc++/ptrace) — **GO 2026-09-20** (the NO-GO from 2026-09-16 invalidated: it was measured with our own DAP client, which vsdbg rejects via a licensing gate; empty breakpoints were SHA384/SHA512 checksums, stripped by `tools/py/sf-debug-dap-filter.py`). Details: [`architecture.md`](architecture.md) (managed debugging).
- [x] 3.4 — recorded: §3.2 and §2.3 of this document and the `_ValidateSailfishAotPolicy` guard message state explicitly that the AOT payload has no DAC/DBI (no vsdbg/SOS/`dotnet-trace`/hot reload), so it is not for debugging.

### Phase 4 — NativeAOT (only behind gate G2)

**Decision 2026-09-15: G2 resolved in favor of `trimr2r` — NativeAOT
DEFERRED.** trim+R2R delivers RSS −10%, CPU −4% and RPM −30% without a Linux
builder and without losing DAC/DBI (vsdbg/SOS/hot reload stay). The phase returns only
if a future budget (cold start/RSS) demands it — then items 2.6/2.8/2.9
and 4.x return with it.
- [x] 4.1 **GATE G2 resolved 2026-09-15:** `trimr2r` delivers (RSS −10%, CPU −4%, RPM −30%; the budget from 0.3 was not formalized, so the decision rests on the §2.4 measurements) → NativeAOT deferred, items 4.2–4.9 frozen until Phase 4 possibly returns.
- [ ] 4.2 Linux builder: .NET 11 + clang/llvm (with `llvm-objcopy`) + aarch64 glibc sysroot; verify that `PublishAot=true` produces an ELF.
- [ ] 4.3 Verify that `tools/sf native-build` (zig) works in the same environment — or move the shim build to the builder.
- [ ] 4.4 **11× IL3050** (`RequiresDynamicCode`) = items A1–A6 from §2.1; without closing them the AOT binary will not start — these are not cosmetic warnings.
- [ ] 4.5 **B9** — in AOT there is no apphost or `deps.json`, and `libsailfishhost.so` is today found only via CoreCLR app-local probing (no `SetDllImportResolver`, shim linked without rpath). Add a resolver on `AppContext.BaseDirectory` or `-Wl,-rpath,'$ORIGIN'` in `tools/sf native-build`.
- [ ] 4.6 **B10** — `qml/`, `qml/adapters.json`, `images/` read from `AppContext.BaseDirectory`: the code is trim-safe, but the flow of `Content` items through the AOT publish must be confirmed (`tools/sf package-test:109,246`).
- [ ] 4.7 Crash reporting without DAC: base it on the existing trace log, not on mini-dumps.
- [ ] 4.8 Tuning: `IlcOptimizationPreference=Speed`, `IlcFoldIdenticalMethodBodies`, `DebuggerSupport=false`, `UseSystemResourceKeys=true`.
- [ ] 4.9 Matrix 18/18 + startup/RSS measurement on the AOT binary vs `Release`-R2R.

### Phase 5 — documentation and CI
- [ ] 5.1 README: configuration table + build host requirements (Linux for `ReleaseAot`).
- [ ] 5.2 Entry in [`parity-plan.md`](parity-plan.md) with evidence from the device.
- [ ] 5.3 CI: `Debug` + `Release` build on macOS; `ReleaseAot` only on a Linux runner.

## 6. Risks

- **Trimming without an on-device run = silent loss of features.** MAUI reaches for
  types via reflection; `sf matrix` is the only proof. G1 is mandatory.
- **.NET 11 before GA** — trimmer/R2R behavior may change; the numbers from §2
  are dated 2026-09-15.
- **R2R costs +12 MB** for a startup gain. If measurement 0.1/1.7 shows that
  startup is dominated by Qt/QML, R2R may not pay off — trim alone gives
  93 → 26 MB.

## 7. Build host: SDK RC1 (aligned 2026-09-15)

**State before:** the machine had **two** .NET installations — user root
`~/Library/Application Support/dotnet` (18 GB, managed by `dotnetup`;
SDK 9/10 + `11.0.100-preview.7.26381.103`; of which 14 GB are mobile workload
packs: `Microsoft.Android.Sdk.Darwin`, `Runtime.AOT.osx-arm64.Cross.*`,
`Runtime.Mono.android-*`) and system root `/usr/local/share/dotnet` (548 MB,
`root:wheel`, only `11.0.100-rc.1.26425.128`). `DOTNET_ROOT` and PATH pointed
to the user root, so the repo built with **preview.7**, even though `Directory.Build.props`
had MAUI `11.0.0-rc.1.26451.6`, and `dotnet workload list` showed the band
`11.0.100-rc.1.26458.5`. The preview.7 runtime pack went to the device.

Officially (Microsoft release metadata): `11.0.0-rc.1` released **2026-09-08**,
SDK `11.0.100-rc.1.26425.128`; there is no GA yet.

- [x] 7.1 SDK `11.0.100-rc.1.26425.128` + runtime `11.0.0-rc.1.26425.128`
      installed into the user root (`dotnetup install 11.0.100-rc.1.26425.128`).
      Provenance: `sha256(sdk/11.0.100-rc.1.26425.128/dotnet.dll)` =
      `0449ca54bc14a9616ae62c917d8a9fc0a9bc7573e571c1f563790c77bb726718` —
      identical to the official system installation, so the `dotnetup` warning
      "daily builds are not code-signed" was cosmetic.
- [x] 7.2 `global.json` → `"version": "11.0.100-rc.1.26425.128"`
      (`rollForward: latestFeature` stays). Roll-forward already picked RC1 with the
      old pin (preview.7 + rc.1 in the same `11.0.1xx` band), but the pin
      is now explicit.
- [x] 7.3 Verified: `dotnet --version` in the repo = `11.0.100-rc.1.26425.128`;
      `dotnet build Linux.Sailfish.slnx -c Release` → 0 errors (4 warnings
      CS0618/CS0414, pre-existing); `dotnet publish -r linux-arm64
      -p:SelfContained=true` → exit 0, `runtimeconfig.json` =
      `Microsoft.NETCore.App 11.0.0-rc.1.26425.128`, `libcoreclr.so`
      5 236 616 B, `libclrjit.so` 3 763 272 B, payload 94 MB / 248 files.
- [x] 7.4 *(closed: subsequent sweeps on RC1, most recently 25/25 legs 2026-09-25)* **On-device re-acceptance** (`tools/sf matrix` 18/18 +
      `tools/sf package-test`) — the runtime under the app changed
      (preview.7 → rc.1). Until this passes, evidence from Q4–Q22A applies to the
      preview.7 runtime.
- [x] 7.5 Updated descriptions: `README.md` (Prerequisites) and the plans of that time
      (`PLAN.md`, `PLAN-checklist.md`, removed 2026-09-28).
- [ ] 7.6 Consolidation into one installation: `/usr/local/share/dotnet` (548 MB)
      to be removed — `sudo rm -rf /usr/local/share/dotnet` (directory
      `root:wheel`, requires a password). To fix in `.zshrc`: remove
      `/usr/local/share/dotnet` from PATH (line 1) and fix the order —
      line 31 `export PATH="$DOTNET_ROOT:$PATH"` runs **before** line
      32, which only then sets `DOTNET_ROOT`; it works by accident, because
      `dotnetup env script` (line 28) substitutes the same path.
