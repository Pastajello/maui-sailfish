# Profiling MAUI apps on Sailfish OS

**Status: 2026-09-29, verified on the device** (Jolla, SFOS 5.2, kernel 6.12.38,
aarch64 Cortex-A55). These methods drove the CollectionView and page-load work of 2026-09-26..30.

Starting point: [Profiling .NET MAUI Apps](https://github.com/dotnet/maui/wiki/Profiling-.NET-MAUI-Apps).

## 1. Conclusion

**It works, and it is simpler than on Android/iOS.** The wiki mostly covers Mono (Android,
iOS), where the diagnostic port goes over TCP and `dotnet-dsrouter` is needed. We
run **CoreCLR with JIT + ReadyToRun on linux-arm64**, i.e. the same runtime as on
desktop Linux:

- EventPipe is in `libcoreclr.so` and the RPM does not strip it;
- the diagnostic port is a Unix socket in `/tmp`;
- `dotnet-trace`, `dotnet-counters` and `dotnet-gcdump` on the Mac talk to it over
  an SSH tunnel.

Method names resolve in a trim + R2R build too.

**The Qt/QML side of the GUI thread** is seen by EventPipe only as "time in `sailfish_host_exec`".
Synchronous QML calls from managed code (`sailfish_host_eval`) it measures directly. The rest
comes from the QML Profiler: JS time per function, bindings, signals and object creation, via
a switch in the shim and `tools/sf qml-profile` (§4.4). Frame times come from
`QSG_RENDER_TIMING`. A native sampler still requires installing tools (§4.5).

| Method | Status | Notes |
|---|---|---|
| EventPipe to file (§4.1, `tools/sf trace`) | **works** | from app start, no tools on the phone |
| `dotnet-trace` / `dotnet-counters` / `dotnet-gcdump` via `ssh -L` (§4.2) | **works** | attach to a running app; the `-R` variant (trace from start) was not tested |
| `dotnet-trace` on the phone (§4.3) | not needed | the tunnel works |
| `QSG_RENDER_TIMING` (§4.4) | **works** | only with `MAUI_SAILFISH_QT_HOST_DIAG=1` |
| QML Profiler (§4.4, `tools/sf qml-profile`) | **works** | `MAUI_SAILFISH_QML_PROFILER=<port>` + `qt5-qtdeclarative-plugin-qmlinspector`; `qmlprofiler` 5.6 on the phone does not work |
| native sampling: sysprof / operf / perf (§4.5) | unavailable | no tools, `perf_event_paranoid=3` (root needed) |
| `dotnet-trace collect-linux` | **ruled out** | kernel without `CONFIG_USER_EVENTS` |

## 2. MAUI wiki vs Sailfish

| Wiki (platform) | Mechanism | Sailfish |
|---|---|---|
| Android: `AndroidEnableProfiler`, `adb reverse` | Mono, TCP port | not applicable (no Mono). The equivalent is a Unix socket + `ssh -L` |
| iOS/Mac: `dotnet-dsrouter`, `DOTNET_DiagnosticPorts=127.0.0.1:9000` | Mono, TCP | CoreCLR on Linux accepts only a Unix socket path in `DOTNET_DiagnosticPorts`; dsrouter is unnecessary |
| Windows: PerfView, ETW | ETW | not applicable; a `.nettrace` from the phone opens in PerfView on Windows |
| `dotnet-trace collect --format speedscope` | EventPipe | **works** (§4.1, §4.2) |
| `dotnet-gcdump` | EventPipe | **works** through the tunnel (§4.2) |
| "Measure in Release, because Debug has `UseInterpreter=true`" | Mono | for us Debug is JIT without R2R and trim, no interpreter. Same conclusion: **profile Release** (trim + R2R) |
| `dotnet-trace collect-linux` | perf_events + user_events | ruled out: `CONFIG_USER_EVENTS` is not set |

## 3. Facts

### App and tools in the repo

| Fact | Source |
|---|---|
| CoreCLR `11.0.0-rc.1.26425.128`, self-contained, `linux-arm64`, JIT + R2R + trim `partial`; NativeAOT blocked | `*.runtimeconfig.json`, `docs/aot-and-trimming.md` |
| Trimmed Release does not disable EventSource (no `EventSource.IsSupported=false`) or Meter (no `System.Diagnostics.Metrics.Meter.IsSupported`). Effect: MAUI layout instrumentation works too | `SailfishKitchen.runtimeconfig.json` (Release) |
| The RPM strips apphost, `createdump`, `libcoreclrtraceptprovider.so` (LTTng) and the launcher. EventPipe stays, `perfcollect` is ruled out | `Microsoft.Maui.SailfishOS.targets:323-326` |
| `sf-run-remote.sh` sets **`DOTNET_EnableDiagnostics=0`** by default, which also disables EventPipe. `--env DOTNET_EnableDiagnostics=1` gets overridden, pass **`--diagnostics`** | `tools/remote/sf-run-remote.sh:176-183` |
| `sf-run` launches `/usr/bin/<pkg>` from SSH, without booster and sailjail. So startup differs from launching from the icon | `tools/remote/sf-run-remote.sh` |
| Shim without symbols; `SF_NATIVE_KEEP_SYMBOLS=1` keeps them (for a native sampler) | `tools/sf native-build:42-45` |
| The shim passes only `argv[0]` to Qt, so `-qmljsdebugger=` will not work. Instead there is `MAUI_SAILFISH_QML_PROFILER=<port>[,block]`: `QQmlDebuggingEnabler` + `startTcpDebugServer` on 127.0.0.1, off by default | `Native/sailfish_host.cpp` (`sailfish_host_init`) |
| `qCDebug` from Qt (incl. frame times) reaches the log only with `MAUI_SAILFISH_QT_HOST_DIAG=1` | `Native/sailfish_host.cpp` (`message_handler` → `log_line(0, …)`) |

### Phone (verified 2026-09-29)

| What | Result | Meaning |
|---|---|---|
| kernel | `6.12.38-4k`, `CONFIG_PERF_EVENTS=y`, `CONFIG_HW_PERF_EVENTS=y`, `CONFIG_UPROBES=y` | native sampling is possible |
| `CONFIG_USER_EVENTS` | not set | `collect-linux` is ruled out |
| `kernel.perf_event_paranoid` | `3` | perf_events for root only (`devel-su`) |
| installed tools | 2026-09-29 installed (with consent): `qt5-qtdeclarative-plugin-qmlinspector` (`qmltooling/libqmldbg_*`) and `qt5-qtdeclarative-devel-tools`. No `sysprof-cli`, `oprofile`, `valgrind`, `strace`, `gdb` | a native sampler requires installation |
| `sshd` | Unix socket forwarding (`ssh -L sock:sock`) works | §4.2 |
| diagnostic sockets | a `dotnet-diagnostic-<pid>-*-socket` is left in `/tmp` after every run with `--diagnostics` (8 dead ones seen) | when looking for the socket, take the one whose PID is alive |

The Jolla 5.2.0.17 (aarch64) repository provides: `qt5-qtdeclarative-plugin-qmlinspector`
(`libqmldbg_{profiler,server,tcp,…}.so`), `qt5-qtdeclarative-devel-tools` (`qmlprofiler`),
`sysprof-cli` 3.36, `oprofile`, `valgrind`, `strace`, `gdb`, `sp-smaps` and
`*-debuginfo`. **There is no `perf`**.

### On the Mac

`dotnet tool install -g dotnet-trace dotnet-counters dotnet-gcdump` (version used
10.0.745401). The analyzer `tools/analyze/sf-trace-analyze.cs` downloads the
`Microsoft.Diagnostics.Tracing.TraceEvent` package from NuGet on first run.

## 4. Methods

We profile the Release build (the default `sf deploy` profile) and always with `--diagnostics`.

### 4.1 EventPipe to file: `tools/sf trace`

The runtime opens a session at startup and writes a `.nettrace`. Nothing needs to be
installed on the phone. The scenario should end the app itself (a tour with `Application.Quit`),
because a clean exit writes the rundown with method names.

```sh
SF_SAMPLE_DIR=$PWD/samples/SailfishKitchen \
  tools/sf trace /tmp/prof-kitchen --env KITCHEN_TOUR=beef --env KITCHEN_OFFLINE=1
# → trace.nettrace, trace.speedscope.json (speedscope.app), device.log, analysis.txt
dotnet run tools/analyze/sf-trace-analyze.cs -- /tmp/prof-kitchen/trace.nettrace anchor=DemoTour \
  window=6500:11100 phase=QtHostListAdapter.ResyncDelegates focus=sailfish_host_eval
```

The script does the same as the manual steps:

```sh
./tools/sf run --diagnostics \
  --env DOTNET_EnableEventPipe=1 \
  --env DOTNET_EventPipeOutputPath=/tmp/sf-trace.nettrace \
  --env DOTNET_EventPipeOutputStreaming=1 \
  --env 'DOTNET_EventPipeConfig=Microsoft-DotNETCore-SampleProfiler:0:5,Microsoft-Windows-DotNETRuntime:0x100003801D:4'
# … the app exits on its own, or tools/sf kill (SIGTERM) …
scp defaultuser@<ip>:/tmp/sf-trace.nettrace .
dotnet-trace convert sf-trace.nettrace --format Speedscope   # or Chromium → Perfetto
```

- `0x100003801D` is the runtime part of the `dotnet-common` profile: GC, loader, JIT,
  exceptions, threads.
- The sampler from `DOTNET_EventPipeConfig` samples about every **1.46 ms**. This is not the ~100 Hz
  of the `dotnet-sampled-thread-time` profile from `dotnet-trace`. Overhead is described in §5.
- The Kitchen tour (`KITCHEN_TOUR=beef`) gives about 34 s of recording and a ~8 MB trace.

### 4.2 Attach from the Mac through an SSH tunnel

```sh
SOCK=$(ssh defaultuser@<ip> 'for s in /tmp/dotnet-diagnostic-*-socket; do
  p=$(echo $s | cut -d- -f3); [ -d /proc/$p ] && echo $s; done | tail -1')
ssh -N -L /tmp/sf-diag.sock:$SOCK defaultuser@<ip> &
dotnet-counters collect --diagnostic-port /tmp/sf-diag.sock,connect --duration 00:00:00:10 --format csv -o counters.csv
dotnet-trace   collect --diagnostic-port /tmp/sf-diag.sock,connect --duration 00:00:00:08 -o attach.nettrace
dotnet-gcdump  collect --diagnostic-port /tmp/sf-diag.sock,connect -o heap.gcdump
dotnet-gcdump  report heap.gcdump | head -30
```

- All three tools work through the same tunnel.
- **Local socket in a short path** (`/tmp/…`): on macOS a Unix socket path has a
  limit of ~104 characters, and a deep temp directory exceeds it.
- Sockets of dead processes stay in the phone's `/tmp`, which is why we look for a live PID (§3).
- The from-start variant (the runtime connects to `dotnet-trace` via `ssh -R`, `suspend`) was not
  tested. For a trace from start §4.1 is enough.

### 4.3 `dotnet-trace` on the phone

Not needed, because the tunnel works. If needed: the single-file
`https://aka.ms/dotnet-trace/linux-arm64` into `/tmp` (never into the RPM), run as
`defaultuser`.

### 4.4 Qt/QML side

**Frame times (works, no code changes):**

```sh
./tools/sf run --env QSG_RENDER_TIMING=1 --env 'QT_LOGGING_RULES=qt.scenegraph.time.*=true' \
  --env MAUI_SAILFISH_QT_HOST_DIAG=1 --env MAUI_SAILFISH_QT_HOST_PERF_DIAG=1 …
```

- The render loop is `threaded`.
- Each frame gives two lines. `Frame rendered … in N ms, sync, render, swap` comes from
  the render thread (swap includes waiting for vsync). `Frame prepared … polish, lock,
  blockedForSync, animations` comes from the GUI thread.
- `PERF_DIAG` appends a summary on exit `shutdown: perf frames=… avgFrameUs …
  evals=… rssKb=…`.
- DIAG adds logging by itself, so we do not combine this run with EventPipe.

**QML Profiler (works):**

```sh
# once on the phone: devel-su pkcon install qt5-qtdeclarative-plugin-qmlinspector
./tools/sf run --env MAUI_SAILFISH_QML_PROFILER=3768 --env KITCHEN_TOUR_DELAY_MS=6000 … &
# wait until the port listens: ssh … 'netstat -ltn | grep 127.0.0.1:3768'
ssh -N -L 13768:127.0.0.1:3768 defaultuser@<ip> &
tools/sf qml-profile record 127.0.0.1 13768 55 qml.json   # until the app exits or 55 s
tools/sf qml-profile report qml.json 30
```

- The QML debug server starts in `sailfish_host_init`, before the first `QQmlEngine`,
  and listens only on 127.0.0.1, so access is only through `ssh -L`.
- `,block` is supposed to wait for a client, but on the phone the app starts without one. So
  we delay the tour, e.g. `KITCHEN_TOUR_DELAY_MS=6000`.
- The report gives self and inclusive time for each JS function (file:line),
  binding, signal handler and created object type. Code from `sailfish_host_eval`
  shows up as `<eval>:0`, and the functions in it have their names (e.g. `N` in the old
  `ResyncDelegates`).
- **`qmlprofiler` 5.6 from the `qt5-qtdeclarative-devel-tools` package does not work on the phone:**
  in `--attach` mode it does not open a connection, prints nothing and does not respond to
  commands on stdin. Hence our own client: `QDeclarativeDebugServer` hello + the
  `CanvasFrameRate` service. Qt Creator from the Sailfish SDK should work too, but was not
  checked.
- Qt 5.6 accepts one client. A hanging client blocks the port until it is
  killed. `pkill -f` with a path pattern also kills your own SSH session, so kill by PID.
- With the Kitchen tour the recording has ~1–2.4 million messages, and the JSON ~50–125 MB.

### 4.5 Native sampling (unavailable)

- The kernel allows it (`CONFIG_PERF_EVENTS=y`), but with `perf_event_paranoid=3` root
  is needed, and there is no sampler on the phone.
- Options: `sysprof-cli` or `operf` from the Jolla repo (devel-su install), a self-built
  `perf`.
- Plus `DOTNET_PerfMapEnabled=3` (JIT/R2R method names in `/tmp/perf-<pid>.map`),
  `SF_NATIVE_KEEP_SYMBOLS=1` and `*-debuginfo` for Qt.
- Installing packages and lowering `perf_event_paranoid` change the phone's state, so
  we do it only with the owner's consent.

### 4.6 Memory

- `dotnet-gcdump` (§4.2) gives the managed heap.
- `dotnet-counters` gives the trend: `dotnet.gc.*`, `dotnet.process.memory.working_set`.
- Native RSS comes from `PERF_DIAG` (`rssKb`), and eventually `sp-smaps`.
- In Kitchen the managed heap is ~13 MB and the working set ~400 MB, so look on
  the native side.

### 4.7 Existing instrumentation (the primary metric)

`sf record` + `sf-page-load.py`, `NAV-TIMELINE` / `PAINT-SLOW`
(`MAUI_SAILFISH_QT_HOST_DIAG=1`), the `perf` leg in `sf-matrix`, and in Kitchen the lines
`TOUR +ms … | ui-stall max N ms` (probe: a 16 ms timer on the UI thread). The profiler breaks these
numbers down into causes, but does not replace them.

## 5. How to read EventPipe results

- **The UI thread** is the one with `sailfish_host_exec` on the stack. `tools/analyze/sf-trace-analyze.cs`
  splits its samples into three groups:
  - `qt` — the leaf is `sailfish_host_exec`: the Qt event loop, **work or idle**
    (EventPipe cannot tell them apart);
  - `shim` — a `QtHostNative.*` P/Invoke called from managed code. `sailfish_host_eval` is
    synchronous JS/QML execution in the Qt engine, i.e. the real QML-side cost
    requested from .NET;
  - `managed` — .NET code.
- **Leaves shift to `PollGC` points.** Every sample suspends the runtime, and R2R code
  stops at the nearest safepoint. The leaves then show
  `Thread.PollGC`, `Array.Copy` in `Dictionary.Resize` and `Buffer.BulkMoveWithWriteBarrier`.
  These are stop locations, not cost. What is reliable are the **inclusive times of callers**
  (`focus=… depth=…`, `phase=…`), not the exclusive ranking.
- **Sampler overhead.**
  - About 23.4k suspensions of ~0.23 ms over a 34 s recording, i.e. ~15% of wall time.
  - On the Kitchen tour the stall when scrolling without loading more does not change.
  - Heavier steps (opening the catalog, details, going back) have a stall 0–40% longer.
  - Numbers from profiling sessions do not go into performance tables, they serve only for proportions.
- **EE suspensions** have reason `SuspendOther` (sampler) or `SuspendForGC` (GC).
  We count GC pauses only from the latter.
- **JIT:** `Method/JittingStarted` requires level 5. At level 4 the analyzer counts
  only background tier-up batches (`BackgroundJitStart`). JIT rate comes from `dotnet-counters`.
- **Stale log.** `sf run` clears `/tmp/sf_run.log` only right before launch, ~10 s
  after invocation. A script that searches the log for a tour step during that time will find lines
  from the previous run. That is how the "screenshots without the app" in the first version of the results came about:
  a screenshot taken before the new instance started. `sf trace` clears the log and waits for
  `LAUNCHED_PID`.
- **A screenshot disturbs the measurement.** `sf screenshot` (lipstick, a few seconds and the
  "Screenshot captured" banner) during the tour gave a 600–700 ms stall in the next step, whereas without
  a screenshot it was 70–90 ms. We check visibility in a separate run, and measure stall and frames
  without screenshots.

## 6. Findings for app code (Kitchen and similar)

The backend cannot speed these up:
- **Register pages and ViewModels with a factory** (`AddTransient<CatalogPage>(sp => new CatalogPage(…))`)
  instead of activation via reflection. It matters most for the first resolve in a trimmed build;
  the alternative is warming up DI at startup.
- **No synchronous work before the first `await`** in `OnAppearing` or in thumbnail loading:
  `await Task.Yield()` at the start lets the page appear first and the data arrive in the next turn.
- Start entrance animations after the first frame.
- `SailfishMetrics=false` (the Release default) removes MAUI's per-Measure/Arrange layout instrumentation;
  `SailfishMetrics=true` brings `System.Diagnostics.Metrics` back when you need them in `dotnet-counters`.

## 6a. Measured numbers

**SkiaSharp raster surface** (2026-10-02, Xperia, density 1.911, `QSGThreadedRenderLoop`, from `perf_stats`
`surfaceCommitUs`/`surfaceUploadUs`, [`skiasharp-plan.md`](skiasharp-plan.md)):

| What | Value |
|---|---|
| Pixels on screen vs the managed bitmap | identical (0 of 131 790 differ) |
| A 993×764 px canvas invalidated every frame | ~88 fps |
| Commit (staging copy), average | 1.1 ms |
| Upload (`glTexSubImage2D`), average | 2.1 ms; 20 ms peak on the first upload of a size |

**Kitchen detail push** (2026-10-02, `KITCHEN_TOUR=beef KITCHEN_OFFLINE=1`, `TOUR … ui-stall max`, 3 runs each):

| Build | detail 6 | detail 12 |
|---|---|---|
| Offline seed without recipe lookups (the page showed "HTTP 503") | 162–174 ms | 101–118 ms |
| Lookups seeded, ingredient row Grid + BoxView + Label | 182–191 ms | 126–145 ms |
| Lookups seeded, ingredient row one Label with spans | 193–200 ms | 123–155 ms |
| Lookups seeded, original row, the detail's load yields first (`await Task.Yield()`) | 160–167 ms | 112–123 ms |

The ingredient rows are not where the stall goes. An EventPipe trace of the push block (~200 ms with the sampler):
QML host creation (`sailfish_host_eval`) ~40%, `NavigationPage.PushAsync` ~23%, and the view model's load run
synchronously from `SendAppearing` ~22% (the yield moved that part to the next turn). Until the seed had lookups, ~27 ms of it was the app logging the 503's
stack trace.

**Where host creation goes** (2026-10-02, Kitchen tour, `MAUI_SAILFISH_OPS_TIMING=1` logs `OPS-TIMING` per op batch):
920 hosts, 942 ms in `applyMauiOps`, of which `createObject` 568 ms (60%), the delegate lookup by name 39 ms (4%) and
the rest (JSON, init copy, connect, registry) ~335 ms. A first instance of an adapter kind costs several times a later
one (first Statistics push: QML ops ~110 ms, the second 17–22 ms), which the adapter warm-up takes off later pages.

| Change (2026-10-02, Kitchen tour unless noted) | Before | After |
|---|---|---|
| Row pool (detached rows' hosts reused) | 920 hosts, QML ops ~920 ms | 690 hosts, ~740 ms |
| Adapter warm-up, first Controls push (leg `navback`) | 54–61 ms to Appearing | 44–45 ms |
| Adapter warm-up, detail stall | 183–203 ms | 166–170 ms |
| Threaded canvases (shapes/visual/tree legs, dropped) | 6 slow paints, 219 ms | 11 slow paints, 1598 ms |
| Composite QML component per card (leg `adapterbench`, dropped) | 1667 µs per card | 1583 µs (−5%) |
| Animation ticker (leg `visual` G, RotateTo 600 ms) | thread-pool timer, 16 ms | 55 ticks in 644 ms, one per frame, 0 layout passes |
| Long list's first rows after the page's first frame (Kitchen Beef catalog push, 2026-10-03, 3 runs, `ready` → `catalog opened`) | 164–179 ms | 136–158 ms |

The catalog push after that change (`MAUI_SAILFISH_SLOW_WORK_MS=15`): the app's push work item 75 ms (page
construction, `PushAsync`), then the poll that creates the page 67 ms (navigation 18, reconcile 46, rows 2), back to
back; the row model (~33 ms) and the row delegates (~60 ms) follow after frames. The stall that is left is those two
items in one block. Polling after a frame instead (every poll request held until the navigation's frame callback)
put a frame between them but measured the same, 134–144 ms: the kicked poll is a posted event and runs before the
probe's timer gets a turn. Dropped 2026-10-03.

## 7. Next steps

1. `tools/sf qml-profile` together with the tunnel and waiting for the port as a single script (today
   these are three steps from §4.4).
2. A native sampler (sysprof/operf or a self-built `perf`) with `DOTNET_PerfMapEnabled=3`, if
   the QML Profiler is not enough.
3. A backend `EventSource` (e.g. `Microsoft-Maui-Sailfish`) with `NAV-TIMELINE` steps and
   tour markers, so that trace windows do not have to rely on the `DemoTour` anchor.
