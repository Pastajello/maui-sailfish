# Tools and the device loop

Everything that builds, deploys, runs, measures and records on the phone is one command: `tools/sf <command>`.
Setting up the phone itself is in [connecting-your-phone.md](connecting-your-phone.md).

## The loop from a clone

```bash
# 1. host build (managed + solution)
dotnet build Linux.Sailfish.slnx -c Release

# 2. publish, build the RPM, upload, install, verify (the shim comes from
#    artifacts/native: ./tools/sf native-build after changing src/…/Native)
./tools/sf deploy            # add --run to launch the app afterwards

# 3. prove the device runs exactly this build (payload digests, no stale proc)
./tools/sf verify

# 4. run with diagnostics / extra env, tail the device log
./tools/sf run --env MAUI_SAILFISH_TRACE=1

# 5. capture what the user sees
./tools/sf screenshot /tmp/sf_now.png

# 6. full unattended acceptance matrix (31 legs, verdicts per leg)
./tools/sf matrix

# 7. packaging cycle: RPM build → install → launcher spawn → remove → restore
./tools/sf package-test
```

`tools/sf deploy --run --screenshot` chains steps 2, 4 and 5 into one command for the daily loop.

## Commands

Every device and build tool is one command: `tools/sf <command>` (`tools/sf help` lists them,
`tools/sf <command> --help` shows the options). It also works through a symlink, e.g.
`ln -s "$PWD/tools/sf" ~/.local/bin/sf`.

| Command | Purpose |
|---|---|
| `sf setup` | interactive phone setup (address, user, password → validated, key installed, `connect.info` written); `--if-needed` is SailfishRun's pre-flight, `--force` changes the phone |
| `sf pair` | SSH key pairing only (`ssh-copy-id`, asks the password once) |
| `sf detect` | finds a reachable phone and proves it runs Sailfish OS before any deploy targets it |
| `sf forget-device` | un-pins a phone's host key (after reflashing) |
| `sf doctor` | blocking pre-flight for the deploy loop: sysroot/shim present, SSH paired, endpoint is a Sailfish device (`--local`, `--fix`, `--for-debug`) |
| `sf deploy` | publish + RPM build + upload + install + verify (`--run`, `--screenshot`, `--clean`, `--release N`, `--jit`\|`--trim`\|`--trimr2r`; `SF_PUBLISH_PROPS` adds `-p:` arguments for A/B builds) |
| `sf verify` | proves the installed RPM is byte-for-byte the local build; detects stale processes |
| `sf run` | launches the installed app with a launcher-like Wayland env; `--env NAME=VALUE` injects variables; tails logs; `--wait S` waits up to S s for the app to exit on its own (0 = exited, 124 = still running) |
| `sf kill` | kills every instance (launcher/invoker/firejail and direct-binary cmdlines differ); `--list` only lists them |
| `sf debug-attach` | prepares managed debugging: pushes the linux-arm64 vsdbg to `/tmp/vsdbg`, checks it runs, relaxes ptrace |
| `sf screenshot` | compositor screenshot → local PNG (lipstick `saveScreenshot`, elevated) |
| `sf shots` | runs the app and takes a compositor screenshot for every `SF-SHOT <name>` marker it logs |
| `sf rec` | records the screen while you use the phone: `rec start [-f out.mp4]`, `rec stop [--gif]`, `rec status` |
| `sf record` | runs the app (the sample's showcase tour by default) and records it → MP4; `--manual`, `--audio`, `--keep DIR`, `--app` (app's own window, no password) |
| `sf trace` | EventPipe profile of the installed app from its start (file session, nothing installed on the phone) → `.nettrace`, speedscope file, device log and an analyzer report ([docs/profiling.md](profiling.md)) |
| `sf trace-analyze` | analyzer for those traces: UI-thread split (Qt loop / shim / managed), timeline, per-window phases, caller chains, GC pauses |
| `sf qml-profile` | QML profiler client for Qt 5.6 (`record` over `ssh -L` from an app started with `MAUI_SAILFISH_QML_PROFILER=<port>`, `report` = self/inclusive ms per JS function, binding, signal handler) |
| `sf page-load` | page-load timing from a `record --keep` recording: first frame, settled, GUI stalls per tap + the app's NAV-TIMELINE/PAINT-SLOW lines |
| `sf matrix` | the acceptance matrix: one self-verifying diag leg per feature area (all legs, or the named ones) |
| `sf package-test` | packaging cycle: build → install → launcher spawn → remove → restore (`--jit`\|`--trim`\|`--trimr2r`) |
| `sf native-build` | `zig c++` cross-build of `libsailfishhost.so`, the secrets bridge and the Harbour launcher against the device sysroot → `artifacts/native/<arch>/` (`SF_ARCH=aarch64` default, `armv7hl`) |
| `sf sysroot` | assembles the aarch64 sysroot (Qt 5.6.3 headers + runtime libs) from public RPMs |
| `sf screenrec-build` | `zig cc` cross-build of the on-phone recorder (lipstick recorder protocol → V4L2 H.264 → GStreamer MP4) |
| `sf pack-local` | packs this checkout into the local NuGet feed (`Microsoft.Maui.SailfishOS`, the workload manifest and its `sailfish-workload` tool, the template), registers the source, clears the cached 0.1.0 — what SailfishKitchen and template apps restore from |
| `sf workload-install` | teaches the local SDK the `net11.0-sailfish` TFM (without a clone: `dnx Microsoft.Maui.SailfishOS.Workload install`) |

Layout: `tools/cmd/` holds the commands, `tools/lib/sf-lib.sh` the shared helpers (connection, transports,
timeouts, app identity; `SF_SSH_MUX=1` makes every call of a run share one ssh connection per phone,
`~/.ssh/sf-mux-<user>@<host>`, about 3× faster per call, off by default because long sessions over it
occasionally drop), `tools/remote/` the helpers that run on
the phone, `tools/py/` the SDK-free RPM build/query/extract and sysroot/ELF fixes, `tools/analyze/` the
trace, QML and page-load analyzers, `tools/screenrec/` the recorder's source. `tools/sf-debug-pipe.sh` is
VS Code's `pipeTransport` for the `coreclr` attach (with `py/sf-debug-dap-filter.py`, which drops the
SHA384/SHA512 breakpoint checksums vsdbg rejects). `tools/sf-{setup,deploy,run,preflight,debug-attach}.sh`
are the former names, kept for the `.vscode` files of apps generated before `tools/sf`.
The NuGet package ships the device-loop subset (setup, pair, detect, doctor, deploy, verify, run, kill, screenshot,
debug-attach); the other commands need a checkout.

## VS Code

The **MAUI Sailfish Tools** extension deploys and debugs a `net11.0-sailfish`
project on the phone from VS Code, the way the .NET MAUI extension does for
Android and iOS: <https://github.com/Pastajello-Organization/sailfishos_maui_tools>.
- A **Devices** view and a status bar picker: add a phone, pair an SSH key
  (host key pinned on first use), store the developer-mode password in the
  system keychain, push the debugger.
- **F5** builds the harbour RPM, installs it, starts the app with the
  diagnostics port and attaches the `coreclr` debugger over SSH; **Ctrl+F5**
  runs without the debugger. The app log goes to the Debug Console.
- A **Debug | Release** switch in the status bar; Debug installs as
  `<package>-debug` next to Release.

It runs everything over SSH itself, so the build machine needs neither this
repo's `tools/` nor `expect`/`ssh-copy-id`; it needs the C# extension
(`ms-dotnettools.csharp`) for the debugger.

Apps from the `maui-sailfish` template also get a `.vscode/` with
terminal-based configurations on top of `tools/sf-*.sh` (*Sailfish: F5 Deploy &
Run*, *Sailfish: F5 DEBUG (deploy + attach)*, a two-step Debug deploy + attach);
the attach entries need `SF_TOOLS_DIR` pointing at a checkout's `tools/`.
How managed debugging works on the phone and what it needs:
[`architecture.md`](architecture.md#platform-facts-measured-on-the-device).

## Environment switching

**Host vs device.** All compilation happens on the host (`dotnet` + `zig`);
the device only ever runs installed artifacts (`/usr/share/harbour-sample/…`,
launcher symlink `/usr/bin/harbour-sample`). There is no on-device build and no
SDK container in the loop — `tools/sf sysroot` + `zig` replace it.

**Rendering backend.** The device uses GPU Qt Quick by default; force software
rasterization with `./tools/sf run --env QT_QUICK_BACKEND=software` (used on
GPU-less/stalled sessions).

**Payload profile.** `--trimr2r` (default), `--trim` and `--jit` on
`sf deploy` / `sf package-test` choose the managed payload: trimmed plus
`PublishReadyToRun` (44 MB, precompiled hot code), `PublishTrimmed` only
(29 MB — 91 assemblies instead of 225), or plain self-contained CoreCLR
(94 MB, escape hatch). `trimr2r` became the default on 2026-09-15 after passing
the full device matrix and the packaging cycle; `PublishAot` is deliberately
rejected by the packaging targets. Measurements and rationale:
[`aot-and-trimming.md`](aot-and-trimming.md).

**Diagnostics.** The sample app ships self-verifying cycles, switched entirely
by environment (all prefixed `MAUI_SAILFISH_QT_HOST`):

| Variable | Effect |
|---|---|
| `…_DIAG=1` | master diag switch (traces, acceptance report) |
| `…_PAGE_DIAG=1` / `…_CONTROLS_DIAG=1` / `…_NAV_DIAG=1` / `…_POPUP_DIAG=1` | page / controls / navigation / popup acceptance legs |
| `…_COLLECTION_DIAG=1` (+ `…_COLLECTION_ROWS=N`) | virtualized CollectionView legs (default 40 rows; matrix uses 10/100/500) |
| `…_SHAPES_DIAG=1` / `…_VISUAL_DIAG=1` | shapes/images and visual-state legs |
| `…_RECONCILE_DIAG=1` (+ `…_BRIDGE/…_TEXT/…_INPUT/…_GEOMETRY_DIAG=1`) | reconcile/bridge/text/input/geometry contract legs |
| `…_STRESS_DIAG=1` / `…_PERF_DIAG=1` / `…_ERROR_DIAG=1` | lifecycle stress, performance, error-path legs |
| `…_F3_DIAG=1` | F3 controls leg (carousel, indicator, stepper, check box, swipe view, images/fonts, web view); with `MAUI_SAILFISH_DIAG_STALL_URL=<url of a server that accepts and never answers>` and `MAUI_SAILFISH_HTTP_STALL_S=3` it also checks the hung-image abort and retries |
| `…_F4_DIAG=1` | F4 platform-services leg (Essentials statics, device, theme, battery, sensors, pickers, share) |
| `…_AUTO_SHUTDOWN=1` | quit when the cycle finishes (unattended runs) |
| `MAUI_SAILFISH_TRACE=1` | verbose trace log (`/tmp/maui_trace.log`) |

Runtime knobs and probes for any app (full names):

| Variable | Effect |
|---|---|
| `MAUI_SAILFISH_LIST_PREFETCH=N` | CollectionView rows (and their images) are built N viewports ahead once a list settles (default 2) |
| `MAUI_SAILFISH_HTTP_CACHE_MB=N` | disk cache for http(s) sources QML loads itself, e.g. image thumbnails (default 64, `0` disables) |
| `MAUI_SAILFISH_HTTP_STALL_S=N` | aborts such a request after N seconds without data (default 20, `0` = off); the image loads again twice (1 s, 3 s later) before it counts as failed |
| `MAUI_SAILFISH_LIST_FIRST_FRAME=0` | A/B switch: a long list builds its first rows in the page's own turn (default: after the page's first frame; header and footer paint at once) |
| `MAUI_SAILFISH_PUSH_LAYOUT=0` | A/B switch: a pushed page waits for the end of its slide-in before it lays out and reconciles (old behaviour) |
| `MAUI_SAILFISH_IMAGE_TRACE=1` | logs every image load: ms to Ready, decode size, whether the tile was already visible (`MAUI-IMG`) |
| `MAUI_SAILFISH_SLOW_WORK_MS=N` | logs every UI-thread work item (dispatch, timer, renderer poll breakdown) that ran N ms or longer (`[SLOW]`) |
| `MAUI_SAILFISH_NAV_IDLE_KICK=0` | A/B switch: leave the end of a pageStack transition and native depth events to the next poll (old behaviour) |
| `MAUI_SAILFISH_PAGE_CACHE=N` | pages whose QML objects are kept while not shown (under a push, or a tab/section/detail switched away from; default 4, least recently used rebuilt) |
| `MAUI_SAILFISH_POLL_MS=N` | heartbeat poll interval (default 2000; `250` = the former safety-net poll, `0` = off). Every change arrives as an event; work the heartbeat finds is logged as a warning |
| `MAUI_SAILFISH_TEXT_CACHE=0` | A/B switch: measure every text through the shim on each layout pass (no cache of QFontMetrics answers) |
| `MAUI_SAILFISH_HANDLER_TREE=0` | A/B switch: layout child changes wait for the full page reconcile instead of the container's subtree pass |
| `MAUI_SAILFISH_APP_HANDLER=0` | do not attach the Application handler (then `Application.Quit()` does nothing) |

`tools/sf matrix` wraps these into named legs:
`page controls nav popup collection collection10 collection100 collection500
shapes visual text input geometry reconcile bridge stress perf error tree
shell containers pulley tabpulley silica navback features f3 f4 adapterbench skia skiainput`
(`ALL_LEGS` in `tools/cmd/matrix.sh`; the skia legs run `samples/SkiaSharpProbe`).

## Recording the phone's screen

Sailfish OS ships no usable screen recorder, so the repo has its own:
`tools/screenrec/sf_screenrec.c` runs on the phone.
- **Capture.** It takes every repainted frame straight from the compositor
  through lipstick's Wayland recorder extension. There is no VNC and no
  "Screenshot captured" banner.
- **Encoding.** It feeds the frames to the SoC's hardware H.264 encoder
  (V4L2 mem2mem, `mtk-vcodec-enc`), with real compositor timestamps and
  optional PulseAudio sound.
- **Measured on the Jolla phone (90 Hz panel):** 70–80 fps while the screen
  moves, nothing dropped, the recorded app keeps its 90 fps.

```bash
tools/sf rec start -f demo.mp4                 # record while you use the phone ...
tools/sf rec stop --gif                        # ... then pull it (+ a README-sized GIF)
tools/sf record demo.mp4                       # the sample's showcase tour (full)
tools/sf record demo.mp4 --manual --seconds 20 # you use the phone, it records
tools/sf record demo.mp4 --audio --env MAUI_SAILFISH_QT_HOST=1   # your own run, with sound
```

Details:
- It needs the developer-mode password (`sf setup`). The encoder node is
  root-only, and lipstick only serves the `privileged` group.
- The output is upright, in the phone's aspect ratio (1032:2272), resampled to
  60 fps.
- `--keep DIR` keeps the raw recording together with per-frame timestamps, and
  `tools/sf page-load DIR` measures page loads from it (first frame,
  settled, GUI stalls per tap).
- `--app` is the password-free fallback: the app records its own window at
  15 fps or less.

### How the README clips were made

Both are scripted tours with real touch input on a Jolla phone (Sailfish OS 5.2), recorded
2026-09-30. The whole screen was captured from the compositor with hardware encoding. The GIFs are
12 fps; the originals are [`media/sailfish-showcase.mp4`](media/sailfish-showcase.mp4) and
[`media/sailfish-kitchen.mp4`](media/sailfish-kitchen.mp4). To record them again:

```bash
# sample app (installed with tools/sf deploy)
tools/sf record showcase.mp4 --env MAUI_SAILFISH_QT_HOST=1 --env MAUI_SAILFISH_QT_HOST_DIAG=1 \
  --env MAUI_SAILFISH_QT_HOST_AUTO_SHUTDOWN=1 --env MAUI_SAILFISH_QT_HOST_SHOWCASE=1 \
  --env MAUI_SAILFISH_QT_HOST_SHOWCASE_TOUR=short --env MAUI_SAILFISH_ORIENTATION=Portrait

# Kitchen (installed with SF_SAMPLE_DIR=samples/SailfishKitchen tools/sf deploy); the recording
# stops when the tour quits the app. The README clip keeps the grid scroll and the first detail.
SF_SAMPLE_DIR=samples/SailfishKitchen tools/sf record kitchen.mp4 --max-seconds 90 \
  --env KITCHEN_TOUR=beef --env MAUI_SAILFISH_ORIENTATION=Portrait
```

The GIFs are made from the MP4s with
`ffmpeg -vf "fps=12,scale=360:-2:flags=lanczos,split[a][b];[a]palettegen=max_colors=192:stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle"`
(Kitchen: `fps=10,scale=320`) and `gifsicle -O3 --lossy=30` (Kitchen: `--lossy=60`).

