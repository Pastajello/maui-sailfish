# Architecture alignment plan

Executable plan for bringing the Sailfish OS backend's structure in line with the MAUI 11 handler model without
changing device-visible behaviour. Written for an agent (or a person) who did not take part in the review: every
step names the files, the types, the tests and the acceptance check. The review itself (findings, bug table,
diagrams) is the Claude Doc "Przegląd architektury maui-sailfish" (2026-10-02/03, HEAD `1cfd712`); this file is
the part that gets executed. Keep it current: tick a step when its acceptance check passed and note the commit.

Line numbers below are from HEAD `1cfd712` plus the fixes of 2026-10-03 (see "Already fixed"), all committed in
`ab808cd`; they drift as steps land, so grep for the named symbol when a number no longer matches.

## Status at the end of 2026-10-03 (committed as `ab808cd`)

The second review of the code after this plan was executed, and the work that remains (defects, structure,
docs, native, tools, owner questions), is in [`architecture-handoff.md`](architecture-handoff.md). Start there;
this file is the record of what was done and why.

| Step | State | What is left |
| --- | --- | --- |
| A1–A4 | done | — |
| A5 commands | done | open/closed booleans stay state on purpose |
| B1–B4 | done | B3/B2 small leftovers noted in their sections; `QtQmlService` dropped |
| B5 handler fixes | done | Label keeps its font rule (owner question in B5) |
| C1–C3 | done | — |
| C4 navigation | partly | coordinator is a class; page cache and push/pop stay renderer partials (coupling measured) |
| C5 scheduler, C5b renderer steps | done | — |
| C6, C7 | done | cap/late-key tests not writable in the harness |
| C8 | covered | the rebuild already reuses rows; tests pin it |
| C9 | partly | naming helpers, `mauiPosition` push, `Loop`, PathView events |
| C10 list adapter | partly | partials + pure lookups; separate classes and bridge intent methods open |
| D1, D4 | done | — |
| D2 | partly | per-type `BridgeValue` round trips, `CollectionBridgeTests` |
| D3 build tasks | partly | `sf-lib.sh` asking MSBuild (needs a lazy cached query), small targets items |
| D5 diagnostics package | deferred | owner: "some day" |
| D6 | docs done | repo/sample hygiene needs the owner or the phone |
| E1 ABI version | done | — |
| E2 typed ops | covered | ops already centralised; op contract test added |
| E3 invoke | partly | non-string-argument page calls stay on eval |
| E4 native | partly | atomics, error mutex, weak caches done; the file split open |
| E5 events | partly | drain covers every page; `LibraryImport` rewrite has no warning to fix |

Host tests: 352. Closing full device matrix with every change above: 31/31 PASS at 15:12.

## Ground rules for whoever executes this

- **Never commit, add or push.** The owner commits. Back up uncommitted work (`git diff > session.patch`,
  untracked files to a tar) after every milestone.
- **Never restart lipstick or reboot the phone.** If the window does not foreground, ask the owner to unlock it.
- **No edits to `tools/*.sh` or `src/` while a detached deploy or matrix run is in progress.** Check
  `ps aux | grep -E 'sf-deploy|sf-run|matrix'` first.
- **Host tests first, device second.** `dotnet test tests/Linux.SailfishOS.Tests` (352 tests, a few s) after every
  step. A step that touches the renderer, the shim or QML is closed only by the named `tools/sf matrix <leg>` legs
  on the phone; the full matrix (31 legs) takes about 20 min and runs detached (`nohup`, wait for `LAUNCHED_PID`
  before reading the log).
- **Native build before deploy** after any edit to `src/Linux.SailfishOS/Native/*`: `tools/sf native-build`.
- **Package flow.** `tools/sf pack-local` repacks the fixed 0.1.0 into `~/.local/share/maui-sailfish/feed`
  (clears the NuGet cache, reinstalls the template). SailfishKitchen and the templates restore from that feed.
- **Behaviour-preserving.** Every step keeps the device output identical. If a step turns out to need a
  behaviour change, stop and write it into `BUG_LIST.md` or `docs/parity-plan.md` instead.
- **Counters are the regression alarm.** `treeFixups`, `timerWithWork`, `NavResyncs`, `BridgeFailed` stay 0 on
  the matrix; `RowsPooled`/`RowsAdopted` must not drop on the Kitchen tour; `reconcileDiffPushes` must not grow.
- **One step per branch of work.** Finish a step, run its acceptance check, back up, then start the next. Do not
  interleave steps from different phases.

## Decisions already taken (owner, 2026-10-02)

| Decision | Choice | Consequence for the steps |
| --- | --- | --- |
| Package id | stays `Microsoft.Maui.SailfishOS`; no NuGet.org | no renames, no namespace moves |
| Distribution | workload + local feed, fixed 0.1.0 | manifest stays; the version is still derived from one file (D4) |
| Essentials threading | **HOP**: an off-thread call is marshalled onto the Qt loop and waits; inline on the Qt thread | one base class, no silent no-ops, no throws (B2) |
| Collections | row pool landed first (`1cfd712`) | list steps build on `QtHostListAdapter.RowPool.cs` (C6+) |
| Diagnostics | separate package later | core hooks go `internal` + InternalsVisibleTo now (D5) |
| Dialogs (2026-10-03) | Sailfish system-dialog look: a top panel over the page, not a pageStack page | `dialogs/DialogPanel.qml`; legs find the panel as `pageStack.currentPage.__dialog` |
| Label default size (2026-10-03) | unset `FontSize` paints `Theme.fontSizeMedium` | `SailfishFontRules.LabelFontSize` follows `AppFontSize` |
| Add-on packages (2026-10-03) | none: no CommunityToolkit or Syncfusion add-ons, only `Microsoft.Maui.SailfishOS` | gaps documented in `porting-existing-apps.md` |
| Other W10 answers (2026-10-03) | see `architecture-handoff.md` W10 | D5 deferred, AOT frozen, `Invoke` internal, encodings only when touched |

## Verification commands

```
dotnet build src/Linux.SailfishOS/Linux.SailfishOS.csproj -c Debug --nologo -v q
dotnet test tests/Linux.SailfishOS.Tests/Linux.SailfishOS.Tests.csproj --nologo -v q
tools/sf native-build                         # after Native/* edits
tools/sf deploy && tools/sf verify            # phone unlocked, display on
tools/sf matrix <leg ...>                     # legs: page controls nav popup collection collection10
                                              # collection100 collection500 shapes visual text input geometry
                                              # reconcile bridge stress perf error tree shell containers pulley
                                              # tabpulley silica navback features f3 f4 adapterbench skia skiainput
tools/sf pack-local                           # local feed + template
```

Feature flags that gate paths touched below (all `MAUI_SAILFISH_*`): `ROW_POOL`, `LIST_FIRST_FRAME`, `ADAPTER_PRELOAD`,
`HANDLER_TREE`, `PAGE_CACHE`, `CREATE_CHUNK`, `CREATE_FIRST_CHUNK`, `OPS_TIMING`, `QT_HOST_DIAG`, `SLOW_WORK_MS`.

## Already fixed during the review (2026-10-03, committed in `ab808cd`)

- `QtHostRuntime.cs`: the `QmlEvent` callback catches exceptions like the pointer/key/post/timer callbacks (a
  throwing subscriber used to fail fast inside the shim's drain timer).
- `qml/controls/Button.qml`: `Component.onCompleted` applies `mauiTextColor`/`mauiPlateColor` when set
  (createObject init props fire no change handlers; a colour set in XAML before creation was never applied).
- `qml/containers/CarouselView.qml`: declares `mauiPlaceholderText` (pushed to every list adapter; the looping
  carousel rejected the batch).
- `SailfishEssentials.cs`: `InstallEarly` and DI share one `SailfishPreferences` instance (two caches overwrote
  each other's file).
- `QtHostPageRenderer.PageCache.cs` `Drop`: lists are torn down while their hosts still read as parked, so the row
  pool's destroys address the parked page.
- Earlier (in `ade2485`): `svc-app-state` payload, docked panel after push, interaction-host ids, `PushBatch`
  applied state, `BridgeValue` non-finite numbers, callback guards, `ProgressBar`/`SwipeView`/pickers/`CarouselView`
  QML fixes.

## Device evidence log

| When | Build | Legs | Result |
| --- | --- | --- | --- |
| 2026-10-04 10:13 | + W7, W8 (shim split into host_*.cpp, QTextLayout measure, shell invoke for the nav poll, ABI 4 whole results), W9 (run exit code) | full matrix: all 31 legs | 31/31 PASS (`/tmp/sf-matrix-summary-20261004-101347.txt`); deploy D6: installed shim matches the local build |
| 2026-10-04 09:41 | + W6 (service start pattern, one thread hop, counters gone) + W2.2 | f4 features silica popup stress | 5/5 PASS (`/tmp/sf-matrix-summary-20261004-094135.txt`) |
| 2026-10-04 09:05 | + W5 (handlers split by family, `SailfishKeys`, mapper constructors) | controls text input visual geometry page popup features | 8/8 PASS (`/tmp/sf-matrix-summary-20261004-090536.txt`); screenshot `controls-gallery` (scratchpad `shots-w5/`) has the B5 layout, Labels at the theme size (decision 1b) |
| 2026-10-04 08:51 | + W4 (bridge ↔ adapter intent methods, mapping scope, attach retries in the bridge) | collection collection10 collection100 collection500 containers stress features f3 | 8/8 PASS (`/tmp/sf-matrix-summary-20261004-085156.txt`) |
| 2026-10-04 08:40 | + W3 page cache split (`PageCache`, `ActivationGate`) | 10 legs incl. containers nav navback shell | 10/10 PASS (`/tmp/sf-matrix-summary-20261004-084029.txt`) |
| 2026-10-04 08:16 | + W3 renderer items | 17 legs | 17/17 PASS (`/tmp/sf-matrix-summary-20261004-081646.txt`) |
| 2026-10-04 07:50 | + W1.9 silica leg M (scheme switch and back) | silica | 1/1 PASS (`/tmp/sf-matrix-summary-20261004-075043.txt`); with the full run before it (30/31, silica M the one failure) the full matrix is green |
| 2026-10-03 08:29 | A1–A4, D4, D6 + review fixes | popup input pulley controls navback features collection containers visual | 9/9 PASS (`/tmp/sf-matrix-summary-20261003-082945.txt`) |
| 2026-10-03 08:35 | + visual leg D2 "XAML colours at create" check | `tools/sf shots` visual | CHECK OK; screenshot `visual-xaml-colours.png`: the XAML-coloured Button shows #2E6BB0 / white on first paint |
| 2026-10-03 08:45 | + C1 HostTreeDiff, C2 AdapterEventRouter, image-failed | tree reconcile input popup controls shell | 6/6 PASS (`/tmp/sf-matrix-summary-20261003-084509.txt`) |
| 2026-10-03 15:12 | everything above (closing run; pack-local, Sample + SkiaSharpProbe) | full matrix: all 29 Sample legs; skia skiainput | 29/29 + 2/2 PASS (`/tmp/sf-matrix-summary-20261003-151004.txt`, `…-151217.txt`) |
| 2026-10-03 14:47 | + E5 drain of every model page (native rebuilt), D3 build tasks, targets fixes | popup controls input text nav navback shell tabpulley collection features silica | 11/11 PASS (`/tmp/sf-matrix-summary-20261003-144723.txt`); no drain errors |
| 2026-10-03 14:30 | + D3 build tasks (pack-local; Sample from src, SkiaSharpProbe from the package) | page features; skia skiainput | 2/2 + 2/2 PASS (`…-143009.txt`, `…-143223.txt`) |
| 2026-10-03 14:20 | + renderer partials, `ReconcileCore` in nine steps | page controls nav navback reconcile tree stress perf containers collection visual shell features geometry | 14/14 PASS (`/tmp/sf-matrix-summary-20261003-142048.txt`) |
| 2026-10-03 14:01 | + C10 list adapter partials, pure row lookups | collection collection10 collection100 collection500 containers stress features | 7/7 PASS (`/tmp/sf-matrix-summary-20261003-140157.txt`) |
| 2026-10-03 13:52 | + A5 adapter commands (`mauiCommand` through invoke) | controls input popup collection collection100 containers features silica stress; then f3 pulley tabpulley navback | 9/9 + 4/4 PASS (`/tmp/sf-matrix-summary-20261003-134816.txt`, `…-135203.txt`); f3 WebView script (UserAgent) through the `js` command |
| 2026-10-03 13:35 | + E3 page calls through `sailfish_host_invoke` (ABI 3, native rebuilt) | perf bridge page nav navback stress controls collection containers popup tabpulley pulley shell features | 14/14 PASS (`/tmp/sf-matrix-summary-20261003-133546.txt`); perf startup: `pageInvokes=20 pageCallFallbacks=0` |
| 2026-10-03 13:20 | + E4 atomics, error mutex, weak component caches (native rebuilt), D2 tests; pack-local | full matrix: all 29 Sample legs; skia skiainput | 29/29 PASS (`/tmp/sf-matrix-summary-20261003-131640.txt`) + skia (see run log) |
| 2026-10-03 12:48 | + E1 ABI version, error codes -4/-5, verify D6 (native rebuilt) | bridge page nav popup controls features; broken-QML start; ABI-3 build | 6/6 PASS (`/tmp/sf-matrix-summary-20261003-124849.txt`); D6 OK; load error `native code -5`; ABI mismatch refused at start |
| 2026-10-03 12:40 | + C9 (template signature, item equality, grouped ScrollTo, replaced header, rebuild re-entrancy) | collection collection10 collection100 collection500 containers stress features page | 8/8 PASS (`/tmp/sf-matrix-summary-20261003-124033.txt`); 0 refused rekeys |
| 2026-10-03 12:27 | + C6 wrapper fix (page calls return their value), C7 per-row remeasure | collection collection10 collection100 collection500 navback stress features containers popup | 9/9 PASS (`/tmp/sf-matrix-summary-20261003-122754.txt`); 0 refused rekeys, collection100 shows 35 rekey-only op batches (rows adopted from the pool) |
| 2026-10-03 12:18 | + C6 row pool hardening (before the wrapper fix) | collection collection10 collection100 collection500 navback stress features containers | 8/8 PASS (`/tmp/sf-matrix-summary-20261003-121807.txt`) but every rekey read as refused (-1): pooling was off, found in the log |
| 2026-10-03 12:10 | + C4 coordinator class, reconcile gate, observed pops | nav navback shell tabpulley containers geometry page popup | 8/8 PASS (`/tmp/sf-matrix-summary-20261003-121038.txt`) |
| 2026-10-03 11:58 | + C5 RenderScheduler (no static kick, no `Current`), template version fix | page perf stress nav navback shell tabpulley controls collection reconcile tree containers features | 13/13 PASS (`/tmp/sf-matrix-summary-20261003-115851.txt`) |
| 2026-10-03 11:42 | + B2 strict thread check (off-thread shim call throws); everything above | full matrix: all 29 Sample legs; skia skiainput | 29/29 + 2/2 PASS (`/tmp/sf-matrix-summary-20261003-113742.txt`, `…-114244.txt`) |
| 2026-10-03 11:13 | + B4 public surface (pack-local, SkiaSharpProbe from the feed) | popup f4 features page controls; skia skiainput | 5/5 + 2/2 PASS (`/tmp/sf-matrix-summary-20261003-111049.txt`, `…-111258.txt`) |
| 2026-10-03 11:01 | + B3 `SailfishSystemService` | silica f4 features page | 4/4 PASS (`/tmp/sf-matrix-summary-20261003-110100.txt`); silica J: MCE display On, no lock, memory level answered |
| 2026-10-03 10:53 | + B5 (handler fixes; Label keeps its font rule; controls page Border witness moved up) | controls input text page collection containers features visual | 8/8 PASS (`/tmp/sf-matrix-summary-20261003-105324.txt`); screenshot `controls-gallery` (scratchpad `evidence-controls-gallery-b5.png`): Label boxes fit their 18 dp text |
| 2026-10-03 10:25 | B5 with never-sized Labels measured at the theme size | controls input text page collection containers features | 6/7; controls FAIL: Border button centre at y=2297, below the 2272 px page (rerun alone: same). Bisected 10:30: old Label measure → PASS at y=2225. Cause: Labels paint 18 dp, were measured at 25 dp |
| 2026-10-03 09:48 | + SailfishAppMeta, SailfishCrashTrace, `Run` split into steps | page nav popup shell f4 features; broken-QML start | 6/6 PASS; load error logged, clean exit |
| 2026-10-03 09:38 | + Preferences slice, B3 registry, `Run` returns int | f4 features page controls; broken-QML start | 4/4 PASS; `/usr/bin/harbour-sample` with `MAUI_SAILFISH_QT_HOST_QML=/etc/hostname` → `EXIT=2` |
| 2026-10-03 09:27 | + B2 QtThread / HOP in QtHostServices | f4 features popup silica controls page | 6/6 PASS (`/tmp/sf-matrix-summary-20261003-092735.txt`) |
| 2026-10-03 09:16 | + B1 SailfishRenderSession | popup shell nav navback containers controls features silica | 8/8 PASS (`/tmp/sf-matrix-summary-20261003-091604.txt`) |
| 2026-10-03 09:02 | + C3 AdapterSnapshots, ScenePerDp | controls text adapterbench visual collection input shapes | 6/7 PASS; `collection` NO-VERDICT because its log held the previous (visual) instance's output; rerun 09:04 alone: PASS 22/22 |

## Phase map and order

```
A  contract C#↔QML        (A1 → A5)      ~1–2 weeks   host tests + legs: popup input pulley bridge reconcile
B  global state, services (B1 → B5)      ~2 weeks     host tests + legs: features f4 lifecycle(navback) controls
C  renderer + collections (C1 → C10)     ~3–4 weeks   host tests + legs: page reconcile nav navback collection* tree
D  build, tests, diag     (D1 → D6)      parallel     dotnet test, pack-local, legs: page controls f3 f4 skia
E  native bridge          (E1 → E5)      ~2–3 weeks   native-build + legs: bridge nav lifecycle(navback) perf
```

A before B before C: C's extracted modules must not reach for `QtHostPageRenderer.Current` (B1) and need the
key/event contract (A2) to be testable. D1 (harness hardening) is a prerequisite of every C step and should be
done first. E goes last because it changes the ABI; E1 (version symbol) can be pulled forward at any time.

---

## Phase A: contract C#↔QML

### A1. `lib/adapter.js` and one event payload shape — DONE 2026-10-03 (device: 9/9 legs PASS 08:29)
- Done as planned: `qml/lib/adapter.js` (`emit`, `guarded`, `pageEmit`, ES5), Button tap, the three dialogs,
  ContextMenu, `lib/pulley.js` and every MauiModelPage page event moved to it; managed parses events with
  `HostIdOf` (no bare id, no `Contains("\"id\"")`); `ContractTests.Every_adapter_event_payload_is_an_object_with_id`
  and `Page_events_go_through_page_emit`. Deviation: `mauiSuppressedCount` is not added to every adapter; `guarded()`
  counts only where an adapter declares it. Device: the 08:04 run could not judge it (phone held Inactive by the
  "USB cable connected" dialog); QML loaded and `window-geometry` arrived with `"page":"mp1"`.
- Add `src/Linux.SailfishOS/Platform/QtHost/qml/lib/adapter.js` (`.pragma library`, functions only):
  `emit(item, name, extra)` = `item.mauiEvent(name, JSON.stringify(Object.assign({id: item.mauiId}, extra||{})))`;
  `guarded(item, name, extra)` = `if (item.mauiApplying) { item.mauiSuppressedCount++; return; } emit(...)`;
  `pageEmit(page, name, extra)` adds `{page: page.mauiPageId}`.
- Replace every bare-id or id-less emit: `Button.qml:173` (`tap`), `AlertDialog.qml:43-44`, `ActionSheet.qml:83,85`,
  `PromptDialog.qml:70`, `ContextMenu.qml:31`, `lib/pulley.js:67,81,87` (add `id: menu.mauiId`),
  `MauiModelPage.qml:80,894,987,1030`.
- Declare `property int mauiSuppressedCount: 0` in every adapter (12 have it today).
- C#: `QtHostPageRenderer.Events.cs:16-30` drop the bare-id branch of `HandleTap`; `:97` replace the
  `Contains("\"id\"")` sniff with `TryResolveHost`; `Interactions.cs` `ApplyContextActivated`/`ApplyToolbarActivated`
  unchanged (they ignore the id).
- Test: `ContractTests.Every_adapter_event_payload_is_an_object_with_id` (regex over `mauiEvent(` call sites: the
  second argument is `JSON.stringify(` or an `Adapter.emit`/`guarded` call).
- Accept: `dotnet test` green; `tools/sf matrix popup input pulley`.

### A2. `adapters.json` as the schema; ContractTests cross-check — DONE (deviation) 2026-10-03
- Done differently: the schema is the adapters' own `property … maui*` declarations, not a copy in `adapters.json`
  (a third list to keep in step). `Renderer/AdapterKeyContractTests` renders a page with every control kind and
  fails on any pushed `maui*` key its adapter does not declare and the shim does not own; it found
  `swipe-view.mauiBackground` (fixed: declared and painted in `SwipeView.qml`). `The_shim_owned_key_lists_agree`
  checks the C# / MauiModelPage / sailfish_host.cpp copies of the shim-owned keys.
  `ContractTests.Every_emitted_event_has_a_consumer_and_every_handled_event_an_emitter` checks both directions with
  an explicit allow-list (`UnconsumedOnPurpose`: diagnostics events and the `image-failed` gap for C2). Loader and
  `adapters.json` format unchanged; the original bullets below are kept for reference.
- Change entries to `{"src": "...", "keys": [...], "events": [...], "extends": "content-view"?}`; keep
  `QtHostAdapters.Load` (`QtHostAdapters.cs:77-106`) reading `src` (accept string or object); add
  `QtHostAdapters.Keys(uri)` and `Events(uri)`. Generate the initial lists from each file's `property ... maui*`
  declarations (the regex of `ContractTests.cs:49-55`), inheriting through `extends`.
- Shim-owned generic keys (`mauiBackgroundFill`, `mauiAccessible*`, `mauiAutomationId`, `mauiLayerShadow`,
  `mauiLayerClip`, `mauiMirrored`, `mauiMatrix`, `mauiLetterSpacing`) go into a top-level `"generic"` array; the
  skip list at `MauiModelPage.qml:438-442` reads it (the shell loads the file with `XMLHttpRequest`, Qt 5.6
  supports it for `file://`) and `sailfish_host.cpp:2217-2221` stays the native mirror until E1 adds a test.
- Tests: (i) every declared key in a QML file is in `keys` and vice versa; (ii) every `"maui*"` literal in
  `QtHostPageRenderer.Props.cs`, `QtHostImages.cs`, `QtHostClip.cs`, `QtHostShapes.cs`, `QtHostGraphics.cs`,
  `QtHostListAdapter.cs`, `Handlers/SailfishControlHandlers.cs`, `SailfishBottomSheet.cs` maps to a uri whose `keys`
  contain it (this would have caught `mauiVBar` and `mauiPlaceholderText`); (iii) every `case "…"` in
  `Events.cs:63-205` appears in some adapter's `events`, and every `mauiEvent("…")` site is listed.
- Accept: host tests green; `tools/sf matrix bridge reconcile`.

### A3. Constants instead of literals — DONE 2026-10-03 (shim rebuilt; device: 9/9 legs PASS 08:29, svc-app-state only from QML)
- Done as: `Platform/QtHost/ShellEvents.cs` (constants + payload records `AppStatePayload`, `AppOrientationPayload`,
  `CoverStatusPayload`, `CoverActionPayload`, `ThemeChangedPayload`, `DisplayPayload`, `ScreenLockPayload`,
  `MemoryLevelPayload`, `InputMethodPayload`); every C# `Subscribe` and `== "svc-…"` goes through it;
  `SailfishMauiApplication.SubscribeNativeEvents` parses with the records and raises a state transition once
  (`_appStateRaised`). `svc-app-state` has one source, `MauiShell.qml` (`mauiReportAppState`, also from
  `Component.onCompleted`); the two shim emitters are gone. Tests: `ContractTests.Every_shell_event_has_a_constant_and_is_subscribed_through_it`,
  `LifecycleTests.An_application_state_transition_is_raised_once`. Not done: `SailfishKeys` for `maui*` names
  (A2's tests cover the key contract; a constants class adds no check) and `Subscribe<T>` (the records are parsed
  at the subscriber, one line each). Device check: `tools/sf matrix navback features` (lifecycle in the log).
- `Handlers/SailfishKeys.cs` (generated or hand-kept from `adapters.json`): `public static class SailfishKeys`
  with one `const string` per `maui*` key; `Platform/QtHost/ShellEvents.cs`: `const string` per `svc-*` event plus
  payload records `AppStatePayload(int State, bool? Active)`, `CoverStatusPayload`, `InputMethodPayload(bool
  Visible, Rect Keyboard)`, `DisplayPayload`, `ScreenLockPayload`, `MemoryLevelPayload`, `OpenUrlPayload(string[]
  Urls)`, each with `static Parse(JsonElement)`; `QtHostServices.Subscribe<T>(string, Func<JsonElement,T>,
  Action<T>)`.
- Replace every `"svc-…"` literal in `.cs` (`SailfishMauiApplication.cs:416-492`, `SailfishCover.cs`,
  `SailfishEssentials.cs` Theme, `SailfishOpenUrl.cs`, `SailfishDevices.cs`, `SailfishSensors.cs`,
  `SailfishShareAndPickers.cs`, `QtHostPageRenderer.cs:415`).
- Single `svc-app-state` source: keep `MauiShell.qml:197-203` (it carries `active`, which the activation bridge in
  `QtHostPageRenderer.Navigation.cs:284-312` needs), add the initial report in the shell's `Component.onCompleted`,
  delete the shim emitters `sailfish_host.cpp:1395-1404` and `:1729-1733` (then `tools/sf native-build`); de-dup on
  `state` in `SailfishMauiApplication`.
- Tests: `Every_shell_event_in_qml_and_shim_has_a_constant` (regex `"svc-[a-z-]+"` over `qml/*.qml` and
  `sailfish_host.cpp` vs the constants), `App_state_is_raised_once_per_transition`.
- Accept: `grep -rn '"svc-' src/Linux.SailfishOS --include='*.cs' | grep -v ShellEvents.cs` empty; `tools/sf
  matrix navback features`.

### A4. Delete dead adapters and properties; guard `window` — DONE 2026-10-03 (device: 9/9 legs PASS 08:29)
- Done: `navigation/MauiPage.qml` + `MauiNavigationRoot.qml` deleted (copies in the session scratchpad) with their
  `adapters.json`/fallback entries; `IsGenericViewProperty`, `IsTransformProperty`, `IsLabelVisualProperty`,
  `CountItems`, `TransformedBottom`, the `ApplicationId` family and the bypassed scoped `IDispatcher` factory deleted;
  `MauiModelPage.__opsTiming()` reads the flag through `__shell`; `rekey` renames pending re-parents; stale plan
  markers and "250 ms" wording rewritten (log prefixes `Q14 row` → `list row`, `README #5` → `activation:` /
  `page reset:`); `docs/architecture.md:23`; test `ContractTests.Adapter_map_has_no_unused_uris`.
- Not done, on purpose: the per-page `__comps` cache stays as the path for a page loaded outside MauiShell; a failed
  adapter load stays pinned to the fallback (the QML file on disk does not change, a retry only repeats the error);
  the preload `first`/`warm` lists are left as measured on the device. `ArmAdapterPreload` in `Render` → C5.
- Delete `qml/navigation/MauiPage.qml`, `qml/navigation/MauiNavigationRoot.qml`, `adapters.json:47-48`,
  `QtHostAdapters.cs:51-52`; update the threshold in `ContractTests.cs:44` (≥ 37).
- Delete the per-page `__comps` cache in `MauiModelPage.qml:193,225-235` (dead since the shell cache; keep
  `fallbackComp`). Replace `window.mauiOpsTiming` at `MauiModelPage.qml:245,449,463` with
  `page.__shell && page.__shell.mauiOpsTiming`.
- `rekey` (`MauiModelPage.qml:314-329`): also rename `__pendingReparents` entries whose `id === o.from`.
- `MauiShell.qml:228-232`: do not pin a failed adapter load to `null` forever (retry on next request);
  `MauiShell.qml:269` honour the `first`/`warm` split from `QtHostPageRenderer.cs:1310-1317` (no throwaway instance
  for list-view, scroll-view, shape).
- Delete in C#: `QtHostVisualState.IsGenericViewProperty`/`IsTransformProperty` (`QtHostVisualState.cs:389-403`,
  zero callers), `IsLabelVisualProperty` (`Props.cs:818-825`), `CountItems` (`Props.cs:929-935`),
  `TransformedBottom` (`Layout.cs:431`), `ApplicationId`/`ResolveApplicationId`/`TryGetApplicationId`
  (`SailfishMauiApplication.cs:19-20,39,589-621`), the scoped `IDispatcher` factory
  (`AppHostBuilderExtensions.cs:42-49`, bypassed by the overlay).
- Stale markers: replace "250 ms" with the heartbeat wording (`QtHostPageRenderer.cs:181,184,327,396,536,539`,
  `Navigation.cs:145`, `Architecture.cs:12`, `docs/architecture.md:23`); drop A0..A7/F4a/F5/README #5 references
  (`Architecture.cs:4-19`, `HandlerTree.cs:5`, `R.cs:188,586,658,934,946,1123,1352,1823,1846,1851`,
  `Layout.cs:323`, `Navigation.cs:368`) in favour of the leg or test that proves the rule.
- Test: `Adapter_map_has_no_unused_uris` (every uri in `adapters.json` appears as a C# literal or in a
  `QtHostAdapters.Register` test).
- Accept: host tests; `tools/sf matrix controls collection visual`.

### A5. Managed→native commands, one idiom — DONE 2026-10-03 (host: 346 tests; device: 13/13 legs PASS 13:48–13:52)
- Done as: `mauiCommand(json)` in `ListView.qml` (`scrollTo {row,pos}`), `SwipeView.qml` (`open {side}`) and
  `MauiWebView.qml` (`nav {action}`, `js {req,script}`), called through `sailfish_host_invoke` by
  `Platform/QtHost/AdapterCommands.Send` (the handler base exposes `SendCommand`); the counters
  `mauiScrollTick/mauiOpenTick/mauiNavTick/mauiJsTick` and their row/side/command/script properties are gone, the
  carousel's three dead scroll properties too. A script for a WebView that does not exist yet completes with null
  at once. `mauiSourceTick` was state, not a command (the source's identity, which reloads an equal URL): renamed
  `mauiSourceId`. `FakeShim.Commands` records commands. The idiom is in `docs/custom-controls.md`. Tests
  `AdapterCommandTests` (WebView script round trip, SwipeView open), the grouped ScrollTo test reads the command,
  `AdapterQmlTests` (no `maui*Tick` counter; every `onMaui…Changed` has its property: it caught the WebView handler
  my rename had missed, which would have failed the adapter's load on the device).
- Not changed: the open/closed booleans of Picker, DatePicker, DockedPanel and Drawer are state with a native
  write-back (`IsOpen`), not commands.
- Add an invokable `function mauiCommand(name, json)` to the adapters that today use `*Tick` counters
  (`MauiWebView.qml:33-38` nav/js/source, `SwipeView.qml:32` open/close, `ListView.qml:43` scrollTo) and bool
  round-trips (`Picker.qml:139-146`, `DatePicker.qml:94-107`, `DockedPanel.qml:48-59`, `Drawer.qml:47-58` open);
  C# calls it through `QtHostRuntime.Invoke` once E2 exists, or through `QmlPage.Call` until then.
- Remove `mauiOpenTick/mauiNavTick/mauiJsTick/mauiScrollTick/mauiSourceTick` and the echo guards; C# side:
  `Props.cs:633`, `QtHostListAdapter.cs:1545`, `SailfishControlHandlers.cs:588,605,657`, `Events.cs:430`.
- Document the idiom in `docs/custom-controls.md`.
- Accept: `tools/sf matrix input popup collection containers`; `grep -rn 'Tick' qml/` empty.

---

## Phase B: global state and services

### B1. Renderer session instead of `QtHostPageRenderer.Current` — DONE 2026-10-03 (device: 8/8 legs PASS 09:16)
- Done as: `Platform/QtHost/SailfishRenderSession.cs`, one per app (Sailfish has one window), served by
  `SailfishServiceOverlay` (so the window scope and every MauiContext resolve it). It exists before the renderer and
  owns the one `NativeHostCache`; the renderer attaches itself (`session.Renderer`) and uses that cache, so a handler
  connected before the renderer holds the same host the reconcile uses (the old `Detached` dual identity is gone for
  every overlay-backed context; `Detached` remains only for bare providers). Handlers reach it through
  `SailfishHandlerCore.SessionOf(handler)` (their MauiContext); `SailfishViewHandler.Renderer`; the overlay hands it to
  `QtHostAlertSubscription` and the modal factory (DI-built instances look it up lazily through
  `SailfishRenderSession.OfApp`); `RoutePageNavigation` is a session property set by the Shell handler. App-level
  static APIs (BottomSheet, Remorse, images, fonts) use `SailfishRenderSession.OfApp` (MAUI's own static APIs use
  `IPlatformApplication.Current.Services` the same way). The core no longer reads `QtHostPageRenderer.Current`; the
  property stays for the diagnostics. Test: `RenderSessionTests`.
- Left for C5: the static `RequestPoll`/`RequestSubtree`/`NoteNavigationRequest` (the scheduler's statics).
- New `internal interface ISailfishRenderSession` (host cache, `SuppressPush()`, `RequestLayout()`,
  `RequestScrollGeometry()`, `RequestSubtree(Element)`, `RequestPoll()`, `NoteNavigationRequest()`,
  `PushHostProps`, `PushTransient`, `IsInListRow(Element)`), implemented by `QtHostPageRenderer`, registered in the
  window scope's services (`SailfishWindowScope.cs`) and in `SailfishServiceOverlay.Resolve`.
- Handlers resolve it in `SetMauiContext` (`MauiContext.Services.GetRequiredService<ISailfishRenderSession>()`)
  and store it on `NativeElementHost` at `HostFor` time; drop the static `Detached` cache
  (`NullElementHandler.cs:73-81`) and `SailfishServiceOverlay.RoutePageNavigation` (becomes a session property).
- Call sites to re-point (19 today): `Handlers/SailfishViewHandler.cs:70,119,139,146,213,220,223,225`,
  `ScrollViewHandler.cs:42`, `NullElementHandler.cs:81,102`, `SailfishNavigationViewHandler.cs:49`,
  `SailfishPageHandler.cs:43`, `SailfishPageContainerHandlers.cs:80,128,183`, `SailfishApplicationHandlers.cs:79`,
  `SailfishBottomSheet.cs:35`, `SailfishRemorse.cs:65`, `SailfishServiceOverlay.cs:67`, `QtHostImages.cs`,
  `QtHostFonts.cs`, `SailfishModalNavigation.cs`, `QtHostAlertSubscription.cs`, `SailfishPage.cs:32`.
- `QtHostPageRenderer.Current` stays one release as `[Obsolete]`; the static `RequestPoll`/`RequestSubtree`/
  `NoteNavigationRequest` forwarders too.
- Tests: `ArchitectureAlignmentTests` (handler registration, subtree reconcile), `RendererTests`; add
  `Two_renderers_in_one_process_do_not_share_state` (two `RendererHarness` instances, a push on one never reaches
  the other's shim).
- Accept: `grep -rn 'QtHostPageRenderer.Current' src/Linux.SailfishOS --include='*.cs' | grep -v Obsolete` empty;
  tests green.

### B2. `QtThread` + `QtQmlService` with the HOP policy — DONE 2026-10-03 (device: 6/6 legs PASS 09:27; strict thread check: full matrix 31/31 PASS 11:42); `QtQmlService` base dropped
- Done as: `Platform/QtHost/QtThread.cs` (`Run`, `RunAsync`, `Post`; inline on the Qt thread, before the host and
  under the test shim). The hop sits in `QtHostServices` itself (`Ensure`, `Eval`, `Subscribe` run through
  `QtThread`), so every inline-QML service is callable from any thread and the nine `IsQtThread` guards (Battery,
  Connectivity, Vibration, Haptics, Notifications ×2, Sensor, Geolocation) are gone: off-thread calls now work
  instead of silently returning defaults or throwing FeatureNotSupported. Clipboard, `OpenUrl` (Browser/Launcher),
  Screenshot and BottomSheet go through `QtThread` too; `SailfishMainThreadCall` is deleted (Flashlight uses
  `QtThread.RunAsync`); `SailfishSecureStorage.OnQt` waits for HostReady and then hops through `QtThread`, and fails at
  once on the Qt thread before the host is ready (it used to stall the loop 15 s). Folded fixes: Battery falls back
  to the kernel once when Nemo.Mce is missing (`QtHostServices.IsUnavailable`); `Ensure` warns once per service;
  `OnHostReady` is dispatched without a renderer too; Geolocation's pending list is locked. Tests:
  `PlatformServiceThreadingTests`; `TestStatics` rolls back `QtHostServices` and the renderer's shell subscription.
- `CheckThread` throws DONE 2026-10-03: an off-thread shim call throws `InvalidOperationException` after the log
  and the `OffThreadCalls` count; `MAUI_SAILFISH_STRICT_THREAD=0` only logs (one release). Test
  `StrictShimThreadTests.A_shim_call_off_the_qt_thread_throws`; `docs/native-interop.md` says so. Device: full
  matrix 31/31 PASS 11:42.
- Dropped: the `QtQmlService` base class. The hop and the once-per-service warning live in `QtHostServices`, so what
  is left per service is an `Ensure` + `Subscribe` pair (about three lines in nine files), and the sensors already
  share `SailfishSensor`; a second base would couple them for no behaviour. Reopen if a service needs shared
  lifecycle (stop/restart).
- Preferences slice DONE 2026-10-03 (host): `SailfishPreferences` takes one lock, writes through a temp file renamed
  over the store, logs an unreadable file instead of silently starting empty, supports `DateTimeOffset`, and
  `Clear(sharedName)` clears that container only. Behaviour change, deliberate: `Clear()` now clears the default
  container and keeps shared ones (MAUI's semantics on every platform; it used to wipe everything). Tests:
  `PreferencesStoreTests` (scratch file, never `~/.config`).
- New `Platform/QtHost/QtThread.cs`: `internal static class QtThread { static T Run<T>(Func<T>); static Task<T>
  RunAsync<T>(Func<T>); static void Post(Action); }`. On the Qt thread: inline. Off-thread: `Post` + TCS (`Run`
  blocks, `RunAsync` awaits); before `HostReady` an off-thread caller waits for it (15 s bound, from
  `SailfishSecureStorage.OnQt`); on the loop thread before `HostReady`, `Run` throws `InvalidOperationException`
  at once (never block the loop on itself).
- New `Platform/QtHost/QtQmlService.cs`: `internal abstract class QtQmlService(string name, string qml) {
  protected bool Ensure(); protected string Eval(string js); protected void Subscribe(string, Action<JsonElement>);
  protected T OnQt<T>(Func<T>); protected Task<T> OnQtAsync<T>; protected void PostQt(Action); }`. Derive:
  Battery, Connectivity, Vibration, Haptics, Notifications, SailfishSensor, Geolocation, Share, Pickers, Contacts,
  Flashlight, Theme, DeviceDisplay, Cover, OpenUrl, SystemService (B3). Wrap Clipboard (`Essentials.cs:28,44`),
  Browser (`:343`), Screenshot (`SailfishEssentials.cs:419`) and BottomSheet (`SailfishBottomSheet.cs:42-50`) in
  `QtThread.Run`.
- Delete `SailfishMainThreadCall` (`SailfishDeviceExtras.cs:93-110`), `SailfishSecureStorage.OnQt` (`:147-178`),
  every `IsQtThread &&`/`||` guard in `Platform/Sailfish*.cs`; `QtHostRuntime.RunOnQtThread` becomes an alias of
  `QtThread.Post`; `QtHostRuntime.CheckThread` throws `InvalidOperationException` instead of logging (keep
  `MAUI_SAILFISH_STRICT_THREAD=0` as an escape hatch for one release).
- Folded fixes: `SecureStorage.Remove/RemoveAll` (`:56-72`) through `QtThread.Run`; move
  `dispatcher.Dispatch(SailfishEssentials.OnHostReady)` out of `if (renderer is not null)`
  (`SailfishMauiApplication.cs:228-231`); Battery sets `_started = true` and calls `ReadKernel()` when `Ensure`
  fails (`SailfishDevices.cs:53-54`); `QtHostServices.Ensure` warns once per service (`QtHostServices.cs:24-27`);
  Geolocation continuations marshalled (`SailfishSensors.cs:287-290`); `SailfishPickers._pending` instance, not
  static (`SailfishShareAndPickers.cs:106`); `SailfishPreferences` gets a lock and an atomic write
  (temp file + rename, `Essentials.cs:88-134`); `Preferences.Clear(sharedName)` clears only that shared store;
  `DateTimeOffset` case in `Value`/`Convert` (`:142-173`).
- Tests: `EssentialsTests` Vibrate/Cancel theories, `MoreEssentialsTests` flashlight, `DispatcherTests`,
  `SynchronizationContextTests`; add `Off_thread_calls_hop_onto_the_loop` (`await Task.Run(() =>
  Vibration.Default.Vibrate())`, assert the EvalHook ran on the harness thread), `Battery_reads_the_kernel_once_when_mce_is_missing`,
  `SecureStorage_remove_on_the_loop_thread_before_host_ready_fails_fast` (< 1 s),
  `Host_ready_completes_without_a_renderer`.
- Accept: `grep -rn "IsQtThread" src/Linux.SailfishOS/Platform/Sailfish*.cs` empty; `SailfishMainThreadCall` gone;
  tests green; `OffThreadCalls` 0 on `tools/sf matrix f4 features`.

### B3. One Essentials registry; application split; `Run` returns int — DONE 2026-10-03 (device: 11:01)
- Registry done as: `Platform/SailfishEssentialsRegistry.cs` (36 rows: service, facade, installer, factory, shared
  group, early flag) with one lazily created default instance per row/group. `AddSailfishEssentials`,
  `InstallEarly`, `Install` and `SailfishServiceOverlay` iterate it; the overlay's 13 instance fields, `CreateF4` and
  `Shared<T>` are gone. Fixed with it: under plain `UseMauiApp` the overlay used to create its own Preferences next
  to the early-installed one (two caches again). Tests: `EssentialsRegistryTests` (every installer exists; container,
  overlay and registry hand out one instance; one pickers instance). Hazard found: a test that builds a `MauiApp`
  remaps MAUI's official mappers process-wide and races `HandlerParityTests` (kept out of the registry test).
- `Run` returns int DONE 2026-10-03 (device: a QML that fails to load exits with 2, ssh-verified 09:40; it exited 0 before): `SailfishMauiApplication.Run` returns the loop's code,
  `ExitHostFailed` (2, a QtHostException) or `ExitStartupFailed` (1, logs the whole exception); the generated entry
  point, the template, the Sample and SkiaSharpProbe use `static int Main`; the Sample's hand-rolled trace Main is
  gone. Test: `EntryPointExitCodeTests`. Breaking for apps with their own `Main` calling `Run` as a statement: none
  (it still compiles); apps that want the code return it.
- Extractions DONE 2026-10-03 (device: 6/6 legs PASS 09:48 + broken-QML start): `Platform/SailfishAppMeta.cs` (one parser of
  `qml/maui-appmeta.json`, used by the shell, `SailfishAppInfo` and `SailfishAppPaths`; test `AppMetaTests`);
  `Platform/SailfishCrashTrace.cs` (crash/unobserved/first-chance/exit hooks and the trace file, also behind
  `SailfishRuntime.Trace`); `Run` split into named steps in `SailfishMauiApplication.Boot.cs` (`EnsureOnDevice`,
  `BuildApplication`, `CreateWindow`, `ResolveShellQml`, `RouteHostEvents`, `StartRendering`, `AttachDiagnostics`,
  `RunLoop`) instead of a separate `SailfishBootstrapper` class: the steps share the application's fields, so a
  partial keeps them without a context object. `SailfishMauiApplication.cs` 624 → 303 lines.
- `SailfishSystemService` DONE 2026-10-03 (device: 4/4 legs PASS 11:01, silica J reads the MCE states): `Platform/SailfishSystemService.cs` holds the
  MCE QML, `Start`, `Subscribe` and the last display/lock/memory state with de-duplicated typed events; the
  application owns one, forwards its events to the overrides and lifecycle handlers, and its public `DisplayState`/
  `ScreenLocked`/`MemoryLevel` read through (API unchanged). `SailfishMauiApplication.cs` 303 → 244 lines. Test:
  `LifecycleTests.Mce_states_are_kept_and_each_change_is_raised_once` (mutation-checked).
- `MapOpenWindow` cleanup: not needed, plan assumption wrong. `Application.OpenWindow` never adds the window to
  `Application.Windows`; MAUI parks it in its private `_requestedWindows` until a platform `CreateWindow` claims it,
  which never happens here (one stale entry per dropped call, unreachable without reflection). Probed on the host.
- New `Platform/SailfishEssentialsRegistry.cs`: `internal sealed record EssentialsEntry(Type Service,
  [DynamicallyAccessedMembers(NonPublicMethods)] Type Facade, string Hook, Func<object> Create, bool Early, Type?
  Impl = null)` with `Lazy<object> Default`; `SailfishEssentialsRegistry.Entries` + `Find(Type)`. One row per
  facade; shared implementations (`SailfishPickers`, `SailfishCommunication`) share one `Lazy` via `Impl`.
  `AddSailfishEssentials` loops `TryAddSingleton(e.Service, _ => e.Default.Value)` (keep the
  `ISemanticScreenReader` special case); `InstallEarly` hooks the `Early` rows; `Install` hooks from the container;
  the `[DynamicDependency]` block moves onto `Install`; `SailfishServiceOverlay.Resolve` becomes `existing ??
  Find(serviceType)?.Default.Value` (delete `_clipboard…_haptics`, `_f4`, `CreateF4`, `Shared<T>`,
  `SailfishServiceOverlay.cs:18-32,85-118,130-163,198-204`).
- `public int Run(string[] args)`: return the exec rc; `QtHostException` → 2; other → log the full exception,
  return 1. Scaffold `SailfishGeneratedEntryPoint.cs:4-7` and `templates/maui-sailfish-app/Platforms/SailfishOS/
  Program.cs:7` become `static int Main`.
- Extract from `SailfishMauiApplication.cs`: `SailfishCrashTrace` (`:71-86,509-536`; merge with
  `SailfishRuntime.Trace`), `SailfishAppMeta` record (`Parse(string)`, `Current` lazy over `qml/maui-appmeta.json`,
  `OrientationMask`; replaces `:543-587`, `Essentials.cs:382-402,445-468`), `SailfishSystemService : QtQmlService`
  (`:336-386,458-491`, typed events the application forwards), `SailfishBootstrapper` (`Boot`, `ResolveShellQml`,
  `RunLoop`; `:89-157,165-175,177-290`). The application keeps `Current/Services/Application/Run/virtuals/Raise*/
  Invoke/SubscribeNativeEvents`.
- `MapOpenWindow` (`SailfishApplicationHandlers.cs:35-37`): remove the unopened window from `Application.Windows`
  after the warning.
- Tests: `EssentialsTests`, `EarlyEssentialsTests`, `MoreEssentialsTests`, `WindowScopeTests`, `LifecycleTests`
  (extend: dispatch `svc-display {state:0}` and assert `DisplayState == Off`); add
  `Registry_hooks_resolve_on_every_facade`, `Early_and_built_preferences_are_one_instance`,
  `App_meta_parses_once_from_json`, `Boot_failure_maps_to_a_nonzero_exit_code`, template `int Main` in
  `ToolsPackagingTests`.
- Accept: `SailfishMauiApplication.cs` < 300 lines; `grep -c "TryAddSingleton<\|Hook(typeof"
  SailfishEssentials.cs` = 0; `grep -c "maui-appmeta.json" Platform/*.cs` = 1; on the device
  `MAUI_SAILFISH_QT_HOST_QML=/nonexistent tools/sf run …; echo $?` ≠ 0.

### B4. Public surface shrink — DONE 2026-10-03 (host: 312 tests; device: 7/7 legs PASS 11:13, skia included)
- Done as planned for: every Essentials implementation, `SailfishEssentials`, `SailfishMauiContext`, the dispatcher
  family, `SailfishRuntime`, `SailfishFontManager/Registrar`, `QtHostDiag(Channel)`, `QtHostBridge`, `BridgeValue`,
  `QtHostInputRouter`, `QtHostLayout`, `QtHostUnits`, `QtHostTextMetrics`, `NativeHostCache`, the text model
  (`LabelTextMapper`, `TextSpan`, `TextParagraphStyle`), `QtHostArchitectureCounters`, and `QtHostPageRenderer`
  itself; `QtHostRuntime.Run` (it takes the dispatcher). The native delegates were already internal
  (`QtHostNative` is internal). New: `SailfishPlatform.DevicePlatform` (the device-info class that held the
  constant is internal now), `SailfishDockEdge` for `SailfishBottomSheet.Dock` (was a string; breaking, documented in
  `docs/sailfish-apis.md`). Guard: `PublicSurfaceTests.Public_types_match_the_snapshot` against
  `tests/Linux.SailfishOS.Tests/PublicSurface.txt` (87 types, 45 outside `Handlers`; regenerate with
  `SF_WRITE_PUBLIC_SURFACE=1`); `PublicApiGuard` uses the enum and the platform constant.
- Deviations, kept public: `QtHostRuntime` (`docs/native-interop.md` documents it for apps, the samples use it),
  `QtHostSurface`, `SurfaceTouch`/`SurfaceTouchAction` and `QtHostImageSources` (the SkiaSharp package builds on them
  without InternalsVisibleTo, as any third-party library would), `SailfishExtensions` (the SkiaSharp
  `buildTransitive` targets register through it), `SailfishMauiApplicationHost` (the generated entry point),
  `NativeGeometry` (returned by `NativeElementHost`).
- Go `internal`: every Essentials implementation (`SailfishClipboard`, `SailfishPreferences`, …, `SailfishContacts`),
  `SailfishEssentials`, `SailfishMauiContext`, `SailfishDispatcherProvider/Dispatcher/DispatcherTimer/
  SynchronizationContext`, `SailfishRuntime`, `SailfishFontManager/Registrar`, `SailfishExtensions` (keep
  `MetadataKey` documented), diagnostic counters (`SailfishTheme.Changes`, `SailfishDisplay.Update`,
  `SailfishVibration.Vibrations/NativeState`, `SailfishHapticFeedback.Performed`, `SailfishGeolocation.NativeState`,
  `SailfishSensor.Readings`, `SailfishDeviceDisplay.NativePreventBlanking`), `QtHostRuntime`, `QtHostDiag`,
  `QtHostDiagChannel`, `QtHostBridge`, `BridgeValue`, `QtHostInputRouter`, `QtHostLayout`, `QtHostUnits`,
  `QtHostSurface`, `QtHostTextMetrics`, `NativeHostCache`, the six delegate types in `QtHostNative.cs:14-38`.
  Diagnostics and tests keep access through the existing `InternalsVisibleTo` (`Linux.SailfishOS.csproj:18,20`).
- Stay public: Hosting, `SailfishMauiApplication` (Current, Services, Application, Run, virtuals,
  DisplayState/ScreenLocked/MemoryLevel), `SailfishLifecycle` + builder, `SailfishPlatformTypes` enums,
  `SailfishPage`/`SailfishOrientations`, `SailfishCover(Action)`, `SailfishRemorse`, `SailfishBottomSheet`
  (`Dock` as an enum), `SailfishNotifications`, `SailfishTheme.Current`, `SailfishDisplay` reads,
  `QtHostException`, handler types, `NativeElementHost`, `QtHostAdapters.Register`, `SailfishSnapshotHandler`,
  `SailfishDiagnostics.Register`; add `SailfishPlatform.DevicePlatform`.
- `tests/Linux.SailfishOS.PublicApiGuard` gains every public type above; add
  `PublicSurfaceTests.Public_types_match_the_snapshot` (reflection over the assembly vs a checked-in list).
- Accept: PublicApiGuard builds; snapshot test green; public type count in Platform/Hosting/LifecycleEvents drops
  from 64 to the snapshot (about 25).

### B5. Handler-layer fixes that need B1 — DONE 2026-10-03 (host: 309 tests; device: 8/8 legs PASS 10:53 + screenshot)
- Done as planned, each with a host test that fails without the fix (`Renderer/HandlerFixTests.cs` unless noted):
  - Entry/Editor background: `SnapshotMapper` maps `MapSnapshotAndViewState` for keys `SailfishViewMapper.Keys`
    also covers (`A_runtime_entry_background_reaches_native_without_a_reconcile`).
  - WebView: `DisconnectHandler` completes every pending script with null; `_pendingJs` locked
    (`A_script_in_flight_completes_when_the_handler_disconnects`).
  - Root page through the factory: `AttachRootHandler` asks `context.Handlers.GetHandler`, the Sailfish rows only
    serve a context without a factory (`An_app_registration_serves_the_window_root_page`).
  - Focus before attach: `FocusHost` keeps `NativeElementHost.PendingFocus` when the object is missing;
    `AttachNative` replays it and takes IsFocused back when Qt refuses
    (`Focus_before_the_object_exists_is_replayed_when_it_attaches`, driven through `HealIfDead`). `FakeShim` now
    models focus for a single `SetProperty` too, as QML does.
  - Commands: `SailfishViewMapper.CommandMapper` (chained from `ViewHandler.ViewCommandMapper`) holds Focus, Unfocus
    and InvalidateMeasure; every handler (SkiaSharp ones too) chains from it, `NullViewHandler`/`SailfishViewHandler`
    default to it; the `Invoke` overrides and `SailfishHandlerCore.TryInvoke` are gone. Native focus goes through
    the internal `ISailfishNativeFocus`. Guard: `Every_sailfish_handler_answers_focus_through_its_command_mapper`.
    `docs/architecture.md` says custom handlers chain from it.
  - Rebind on adapter change: `QtHostPageRenderer.HostingOf` → `NeedsRebind`/`RebindAdapter`: when the handler's
    chosen adapter differs from the bound host's (or it chooses none and the host is not a generic container or
    placeholder), `NativeHostCache.Forget` + `DisconnectHandler` + re-attach; the diff destroys the old object;
    counter `AdapterRebinds`. Deviation: no `ReplaceHost` on the handler (`PlatformView` has a `private protected`
    setter in MAUI); the element gets a new handler, as a platform view is recreated elsewhere. Tests
    `An_image_whose_source_resolves_later_gets_the_image_adapter` (missing file → existing file),
    `An_indicator_view_switches_between_dots_and_its_template`.
  - Font size: `SailfishFontRules.AppFontSize` / `PaintFontSizeDp` (18 = unset, the theme size paints) for Button,
    RadioButton, value boxes and text inputs, snapshot and measure alike; `AdapterSnapshots.HasAppFontSize` deleted;
    `LabelTextMapper`/`TextSpan` defaults read `SailfishMeasure.DefaultFontSize`. Deviation: Label keeps its own,
    device-visible rule in `SailfishFontRules.LabelFontSize` (any positive size paints, MAUI's default 18 included), now
    shared by the Label snapshot and measure. On the device a never-sized Label reads FontSize 18 (set) and paints
    18 dp; moving it to the theme size (25 dp there) measured it larger than it painted and pushed the controls
    page's Border button off screen (controls leg FAIL 10:25, bisected 10:30). Test
    `A_label_is_measured_at_the_size_it_paints` (references `QtHostTextMetrics` directly, mutation-checked).
    Owner question: should a never-sized Label paint at `Theme.fontSizeMedium` like the other controls? It would
    enlarge every unstyled Label in every app. The 18 trap for the other controls is in
    `docs/porting-existing-apps.md` (handler-parity.md is generated).
  - Cycle: `TransientInputProperties` → `SailfishViewKeys.TransientInput`; `IsRightToLeft` →
    `QtHostVisualState.IsRightToLeft` (`AdapterSnapshots` forwards); the surface test reads the host
    (`PlatformView is NativeElementHost { QmlUri: QtHostSurface.AdapterUri }`). `GenericBackgroundTypes` stays in
    `QtHostVisualState`: which adapters paint no background is adapter knowledge, and `SailfishViewKeys` reading it
    is the allowed Handlers → QtHost direction.
  - Scroll view: deviation, `ScrollViewHandler.AdapterUri` stays null. A handler-declared adapter binds first (first
    URI wins), and the parent walk cannot tell a CollectionView header ScrollView (not a row, scrolls) from a row
    one, so it could lock the wrong adapter. The literal is `QtHostAdapters.ScrollView` everywhere instead;
    `InListRow` also matches `ItemsView<Cell>` (legacy ListView).
- Accept checks: no `override void Invoke` in `Handlers/`, `grep -n "Handlers\." QtHostVisualState.cs` empty, no
  `"scroll-view"` literal in `Handlers/`.
- Entry/Editor background (`SailfishControlHandlers.cs:37-44,231,267`): in `SnapshotMapper` register
  `MapSnapshotAndViewState` for keys that `SailfishViewMapper` also covers, so `mauiBackgroundFill` is pushed with
  the snapshot; remove the `GenericBackgroundTypes` block from `SailfishViewKeys.BuildRendererOwned`
  (`SailfishViewKeys.cs:52-59`). Test: Entry on a page, set `BackgroundColor` after connect, assert
  `mauiBackgroundFill` on the shim object without `h.Poll()`.
- Rebind on adapter change (`NativeHostCache.cs:16-23`, `QtHostPageRenderer.cs:1840,1889-1898`): when
  `host.IsBound && host.QmlUri != uri`, destroy the host through the `HandlerTree.cs:178-186` path and create a
  fresh `NativeElementHost`; `NativeHostCache.Replace(Element, NativeElementHost)`;
  `SailfishViewHandler.ReplaceHost(NativeElementHost)` (disconnect/connect without touching `VirtualView`).
  Covers Image with late `Source` (`:742-743`), RadioButton (`:530`), IndicatorView (`:708-709`), RefreshView
  (`:875`). Test: `new Image()` on a page, poll, set `Source`, poll, assert one `"image"` host and no
  `"content-view"` host for that element.
- One font-size convention: `SailfishFontRules.AppFontSize(BindableObject, BindableProperty, double) → double?`
  (null = unset) with today's `HasAppFontSize` rule and `PaintFontSizeDp(...)`; replace `Props.cs:603`,
  `SailfishMeasure.cs:137,440`, `LabelTextMapper.cs:70-73`; `TextModel.FontSize` default reads
  `SailfishMeasure.DefaultFontSize`. Do not change the 18 sentinel (device-visible); record the explicit-18 trap
  in `docs/handler-parity.md`. Test: extend `An_unset_size_label_is_measured_at_the_theme_size_it_paints_with`
  with a `Label` whose FontSize is never assigned.
- WebView pending JS (`SailfishControlHandlers.cs:551,599-614`): override `DisconnectHandler` to complete every
  `_pendingJs` with `null`, clear, then `base`; lock around `RunJs`/`CompleteJs`. Test: evaluate JS, remove the
  page, assert the task completes.
- Root page through the factory (`SailfishHandlersFactory.cs:161-167`): `context.Handlers.GetHandler(root.GetType())`
  + `SetMauiContext` + `SetVirtualView`; keep `ResolveViewHandlerType` for `HandlerParityTests`. Test: register an
  app handler for `NavigationPage` and assert `window.Content.Handler` is that type after `MapContent`.
- Focus before attach (`QtHostPageRenderer.cs:685-688,719-721`, `NullElementHandler.cs:104-109`): remember a
  pending focus request on the host and replay it in `AttachNative`.
- Commands into the `CommandMapper`: `SailfishViewHandler.ViewCommands = new CommandMapper<IView,
  IViewHandler>(ViewHandler.ViewCommandMapper)` with `InvalidateMeasure`, `Focus`, `Unfocus`; chain every
  handler's `CommandMapper` from it (`SailfishControlHandlers.cs:176,206,236,…`, `ScrollViewHandler.cs:24`);
  delete `SailfishViewHandler.Invoke` (`:98-111`), `NullViewHandler.Invoke`, `SailfishHandlerCore.TryInvoke`
  (`NullElementHandler.cs:97-117`).
- Break the Handlers↔QtHost cycle: `TransientInputProperties` (`QtHostPageRenderer.cs:708-709`) →
  `SailfishViewKeys.TransientInput`; `GenericBackgroundTypes`/`HasGenericBackground` (`QtHostVisualState.cs:247-266`)
  → `SailfishViewKeys`; replace the handler type test at `QtHostVisualState.cs:259` with a
  `NativeElementHost.IsSurface` flag set in `HostFor`. `ScrollViewHandler.AdapterUri` declares the choice the
  reconcile makes at `QtHostPageRenderer.cs:1765/1774` through `ISailfishRenderSession.IsInListRow`; drop the
  `QmlUri: "scroll-view"` string from `ScrollViewHandler.Snapshot`; `InListRow` also matches `ItemsView<Cell>`
  (legacy ListView, `SailfishViewHandler.cs:228-234`).
- Accept: no `override Invoke` in `Handlers/`; `grep -n "Handlers\." QtHostVisualState.cs` empty; no
  `"scroll-view"` literal in `Handlers/`; `HandlerParityTests`, `PropertyOwnershipTests`, `RendererTests` green;
  `tools/sf matrix controls input text`.

---

## Phase C: renderer split and collections

Run D1 first. Each step keeps every public counter the diagnostics runner reads as a forwarding property on the
renderer (`grep -o 'renderer\.[A-Z]\w*' src/Linux.SailfishOS.Diagnostics/*.cs | sort -u`).

### C1. `HostTreeDiffer` (pure, static) — DONE 2026-10-03 as `HostTreeDiff` (device: 6/6 legs PASS 08:45)
- Done as a shared diff builder rather than a pure planner: `Platform/QtHost/HostTreeDiff.cs` owns the native child
  order basis, the destroy/reparent/create/order ops and `AppliedParentId`; `ReconcileCore` and `ReconcileSubtrees`
  keep their own op sequence (page: all reparents, then creates; subtree: interleaved in pre-order) because the
  tests pin the ops JSON and QML resolves a reparent into a not-yet-created parent differently. The duplicated
  `ParentKey`/`qmlChildren`/destroy/reparent/create/order code is gone from both. Tests: `HostTreeDiffTests` (4);
  `RendererTests`, `ArchitectureAlignmentTests` unchanged and green.
- New `Platform/QtHost/HostTreeDiffer.cs`. Move the diff of `QtHostPageRenderer.cs:1027-1139` (desired/current
  sets, survivors, created + chunk/prioritised prefix, `qmlChildren`, destroy reversed, reparent, create, order
  groups, local `ParentKey`) and `HandlerTree.cs:128-229` (same with `oldHosts` scoping).
- Surface:
  ```csharp
  internal readonly record struct HostTreePlan(
      IReadOnlyList<NativeElementHost> Destroyed,                              // descendants first
      IReadOnlyList<(NativeElementHost Host, string ParentId)> Reparented,
      IReadOnlyList<(NativeElementHost Host, string ParentId)> Created,        // pre-order
      IReadOnlyList<NativeElementHost> Survivors,
      IReadOnlyList<NativeElementHost> Deferred,
      IReadOnlyList<(string ParentId, List<string> Order)> Orders);
  internal static HostTreePlan Diff(IReadOnlyList<NativeElementHost> current, IReadOnlyList<NativeElementHost> desired, int createChunk, ISet<Element>? prioritised);
  internal static HostTreePlan DiffSubtree(IReadOnlyList<NativeElementHost> current, ISet<NativeElementHost> oldHosts, IReadOnlyList<(NativeElementHost Root, List<NativeElementHost> Hosts)> desiredByRoot);
  internal static List<Dictionary<string,object?>> ToOps(in HostTreePlan plan, Func<NativeElementHost, Dictionary<string,object?>> propsOf, Func<NativeElementHost, Dictionary<string,object?>, string, Dictionary<string,object?>> createOp);
  ```
  The differ reads `host.Parent?.Id` and `host.AppliedParentId` only; all writes (`AppliedParentId`,
  `ReleaseHost`, `ReleaseSyntheticSlot`, `_byId`, `_current`, `_awaitingArrange`, `_createDeferred`) stay in a
  renderer `ApplyPlan(plan, props)` used by both `ReconcileCore` and `ReconcileSubtrees`.
- Covered by `RendererTests.cs:75,108`, `ArchitectureAlignmentTests.cs:288,318,396`. New `HostTreeDifferTests`:
  current [a,b,c] desired [a,c,d] → Destroyed [b], Created [d], one Order; chunk 2 with 5 new and a prioritised
  shape whose ancestor is new → both in Created, 3 Deferred; a survivor whose parent changed → Reparented.
- Accept: RendererTests + ArchitectureAlignmentTests green; the ops JSON asserted in
  `A_subtree_pass_removes_reorders_and_nests_like_the_page_reconcile` unchanged; `TreeFixups` unchanged in
  `Idle_timer_polls_do_no_native_work`; `tools/sf matrix tree reconcile`.

### C2. `AdapterEventRouter` — DONE 2026-10-03 (device: 6/6 legs PASS 08:45)
- Done as: `Platform/QtHost/AdapterEventRouter.cs` holds `Handle` (the event switch), `HandleTap`, `HostIdOf`,
  `RouteAdapterEvent` and the 15 `Apply*` write-backs plus their counters; `QtHostPageRenderer.Events.cs` is deleted.
  The renderer keeps `HandleNativeEvent`/`HandleTap` and the counters as one-line forwards (the diagnostics read them)
  and exposes the router's surface: `TryResolveHost`, `TryGetHost`, `SuppressPush`, `PushBatch`, `MarkLayoutDirty`,
  `RestartTimeline`, `SelectTab`, `SheetCancel`, `ScrollRefreshView`, `PageRefreshView`, and the dialog/menu
  completions now `internal`. Kept on the renderer, not moved: `ApplyContextActivated`, `ApplyToolbarActivated`,
  `RoutePanelOpenChanged`, `CompleteDialog` (they own menu/dialog state the renderer still holds; they move with
  the interactions in a later step). `image-failed` is consumed (`SailfishImageHandler` logs it once per source).
  Tests: `AdapterEventRouterTests` (malformed cursor dropped, picker-open mirrored without echo); the event contract
  test reads the router's switch.
- New `Platform/QtHost/AdapterEventRouter.cs`, `internal sealed class AdapterEventRouter(QtHostPageRenderer
  owner)`. Move all of `Events.cs` (`HandleTap`, the `HandleNativeEvent` switch, `RouteAdapterEvent`,
  `ApplyNativeState`, `ApplyFocusChanged`, `ApplyCompleted`, `ApplyDateSelected`, `ApplyPickerOpen`,
  `ApplyTimeSelected`, `ApplyCursorChanged`, `ApplyRefreshRequested`, `ApplyWebViewEvent`, `ApplySwipeItemInvoked`,
  `ApplySwipeState`, `ApplyIndicatorTapped`, `ApplyNestedScrollChanged`), plus `Interactions.cs:264`
  `ApplyContextActivated`, `:282` `ApplyToolbarActivated`, `:400` `ParseDialogText`, `:415` `RoutePanelOpenChanged`,
  `QtHostPageRenderer.cs:72-79` `TryResolveHost`; counters `NativeEventsDelivered/Suppressed, FocusTransitions,
  CompletedFired, CursorWriteBacks, ScrollWriteBacks, ContextMenuActivations, ToolbarActivations, PanelOpenChanges,
  DrawerOpenChanges, WebViewNavigations, LayoutDirtyFromWriteback`; event `PanelOpenChanged`.
- Renderer exposes (internal): `TryRouteHost(string id, out host)`, `SuppressPush()`, `MarkLayoutDirty(bool
  fromWriteBack)`, `PushBatch`, `RequestScrollGeometry`, `Collection`, `ApplyWindowGeometry`, `SelectTab(int level,
  int index)`, `SwipeTab`, `CompleteDialog`, `SheetCancel`, `OpenFlyout`, `PullEntries`, `PageRefresh`,
  `ScrollRefresh`. Surface: `Handle(string name, string payload)`, `HandleTap(string payload)`, the counters. The
  renderer keeps `HandleNativeEvent(name, payload) => _events.Handle(name, payload)` and forwarding counters.
- Also consume `image-failed` (`Image.qml:149-150`, no C# consumer today): route to
  `SailfishImageHandler.OnAdapterEvent` and clear `IsLoading`.
- Covered by `RendererTests.cs:144,167`, `SampleAppRegressionTests.cs:291,367,479,766`,
  `ArchitectureAlignmentTests.cs:191,680`. New `AdapterEventRouterTests`: unknown event with `{"id"}` reaches
  `ISailfishAdapterHandler.OnAdapterEvent`; `cursor-changed` with a negative field is dropped; `picker-open` mirrors
  `mauiOpen` into `AppliedProperties`.
- Accept: RendererTests + SampleAppRegressionTests green; `Events.cs` deleted; `grep -c 'private void Apply'
  QtHostPageRenderer*.cs` = 0; `tools/sf matrix input popup`.

### C3. Snapshot builders into the handlers — DONE 2026-10-03 (device: 7/7 legs PASS 09:02 + 09:04)
- Done as: `QtHostPageRenderer.Props.cs` is deleted; its 57 static builders live in
  `Handlers/Snapshots/AdapterSnapshots.{Common,Containers,TextInput,Values,Label,Button}.cs` (one
  `internal static partial class AdapterSnapshots`, split by control family, so shared helpers need no
  cross-class names). Handlers call `AdapterSnapshots.*`; the renderer calls it only for elements it hosts without
  a handler (`ContainerProps`, `ScrollProps`, `IsRightToLeft`). `RefreshAncestorOf` (instance, needs the page's
  pulley state) moved to the Layout partial. `_swipeWarned` is a `ConditionalWeakTable` (it rooted every warned
  SwipeView). Sizes use `QtHostUnits.ScenePerDp` (Density ÷ DPR) like the geometry; DPR is 1 on the device and in
  the tests, so nothing moved. Tests: `AdapterSnapshotsTests` (DPR 2 font/spacing = `ToQtUnits`, button set
  flags); `TestStatics` restores `DevicePixelRatio`.
- Not done here: `TransientInputProperties` / `RefreshSurfaceProps` stay on the renderer (B5 moves the key lists);
  `HasAppFontSize` stays in `AdapterSnapshots.Common` until B5's single font-size convention; per-control
  `HandlerSnapshotTests` → D2 (the key contract test of A2 already renders every control).
- Create `src/Linux.SailfishOS/Handlers/Snapshots/` with static classes `TextInputSnapshots` (`TextInputProps`,
  `SearchBarProps`, `TextStyleProps`, `MapInputMethodHints`, `EnterKeyIcon`, `ClampMaxLength`, `VAlignName`),
  `LabelSnapshots` (`LabelProps`, `LabelText`, `BuildSpanHtml`, `HtmlEscape`, `MapWrapMode/ElideMode/HAlign/VAlign`),
  `ButtonSnapshots` (`ButtonProps`, `HasExplicitBackground`), `ValueControlSnapshots` (Switch/CheckBox/Slider/
  Progress/Activity/Stepper/Indicator/Picker/Date/Time/Radio + `AddFont`/`AddTextStyle`/`ItemStrings`/`DisplayText`/
  `LocalMidnightMs`/`DateValueText`/`TimeValueText`), `ContainerSnapshots` (`BorderProps`, `FrameProps`, `AddShadow`,
  `AddBorderCanvas`, `IsBorderVisualProperty`, `ContainerProps`, `GridProps`, `StackProps`, `ScrollProps`,
  `ClipsToBounds`, `EffectiveBackground`, `OrientationOf`, `SwipeProps`, `VisibleSwipeItems`, `SwipeItemsJson`,
  `SwipeItemViewJson`, `WebViewProps`). `HasAppFontSize` → `SailfishMeasure` (B5); `IsRightToLeft` → new
  `Platform/QtHost/QtHostFlow.cs`; `RefreshAncestorOf` stays on the renderer; `TransientInputProperties`,
  `RefreshSurfaceProps`, `IsRefreshSurfaceProperty` (`QtHostPageRenderer.cs:110,118,708`) move too; replace static
  `_swipeWarned` (`Props.cs:668`) with a once-per-process `int` flag.
- Each handler's `Snapshot()` calls its own snapshot class; the renderer calls only
  `ISailfishAdapterHandler.AdapterState()`. Re-point callers: `grep -rn 'QtHostPageRenderer\.\(\w*Props\|AddFont\|
  AddTextStyle\|IsRightToLeft\|HasAppFontSize\|MapInputMethodHints\|EnterKeyIcon\|ClampMaxLength\|DateValueText\|
  TimeValueText\|IsBorderVisualProperty\|TransientInputProperties\|RefreshSurfaceProps\|IsRefreshSurfaceProperty\|
  VisibleSwipeItems\)' src tests` (`SampleAppRegressionTests.cs:236` calls `QtHostPageRenderer.ButtonProps`).
- Route every `* SailfishDisplay.Density` through one `Px(double dp)` that calls `QtHostUnits.ToQtUnits`
  (`Props.cs:27,49-51,127,154,350,414,418,603,609,617-620,662,763,792-814`, `QtHostClip.cs:31,101`); on the device
  DPR = 1 so values are unchanged.
- New `AdapterSnapshotsTests`: `LabelProps["mauiPixelSize"]` equals `QtHostUnits.ToQtUnits(fontSize)` with
  `DevicePixelRatio = 2`; `MapInputMethodHints` table; `ButtonProps` set/unset flags. New
  `HandlerSnapshotTests`: one `[Theory]` per control in `HandlerParityTests.Pairs` asserting the exact `props` of
  the `create` op (Entry `placeholder/maxLength/returnType/mauiTextColorSet`, Slider `minimum/maximum/value/
  mauiThumbImage`, Picker `items/selectedIndex`, …).
- Accept: `Props.cs` deleted; `grep -c 'SailfishDisplay.Density' QtHostPageRenderer*.cs` = 0; suite green;
  `tools/sf matrix controls text adapterbench`.

### C4. `NativeStackSync` (navigation + coordinator + page cache) — PARTLY DONE 2026-10-03 (host: 322 tests; device: 8/8 legs PASS 12:10)
- Done: the coordinator is its own class, `Platform/QtHost/NativeStackCoordinator.cs` (the old
  `QtHostPageRenderer.NavigationCoordinator.cs` is gone). It owns the confirmed mirror (`Mirror`, the renderer's
  `_nativePageIds` reads through), the operation in flight, `PopUnsynced` and the counters (`NavOps*`, `NavResyncs`,
  `NativePopSyncs`, `NativePopRacesBlocked`; the renderer forwards them for the diagnostics), and asks the renderer
  through `INativeStackOwner` (expected depth, push/pop model pages, `OnNativePopped`, `OnResynced`, `FollowNative`,
  kick/poll/log). `NavOpKind`/`NavOperation` are namespace-level. Tests: `NativeStackCoordinatorTests` with a
  recording owner (a refused push leaves the mirror and is retried; a native pop MAUI already followed starts no
  operation; a back gesture makes MAUI follow and completes; an unexplained stack is adopted).
- Done from the correctness list: one reconcile gate `CanReconcile` (no transition, no unfollowed pop, a page) used
  by the poll and the window report, which used to reconcile mid-transition (the window-report test now asserts no
  reconcile ran); `TryPop` and the native-follow pops are observed (`Observe` logs a fault); `_largePageWarned` is a
  `ConditionalWeakTable`.
- Not done, with reasons: the page cache and the native push/pop code stay renderer partials. Measured coupling: the
  page cache uses 21 renderer members (host tree, routing table, synthetic hosts, collection bridge, container
  queries), `Navigation.cs` 75; the `IHostTreeOwner` sketched above would carry most of the renderer and buy no
  isolation. The pop-path reconcile at the last popped level stays: it paints the returned-to page before the
  reveal (comment there). `window.DescendantAdded/Removed` are not unsubscribed: the renderer has no end of life
  (one per app window), so there is no `Dispose` to do it in. `_deferNativeDestroy` stays static (C6 touches it).
- New `Platform/QtHost/NativeStackSync.cs` + partials `.Coordinator.cs`, `.PageCache.cs`. Move all of
  `Navigation.cs`, `NavigationCoordinator.cs`, `PageCache.cs` and from `QtHostPageRenderer.cs` the fields
  `_nativePageIds, _nativePageSeq, _navStateAdopted, _lastWindowActive, _lastAppState, _appLifecycleStarted,
  _navOpSeq, FaultNextPush/Pop, _topModelPageId, _nativeTopUnfollowed, _activeSinceMs, _navStackBusy,
  _nativePopUnsynced, _strayScanPending, _navIdleWallMs, _navStopwatch`, the static `_deferNativeDestroy` +
  `PendingNativeDestroys` (become instance), `ActivationSettleMs/ActivationSettled`, counters `NativePushes/Pops/
  PopSyncs/OpFailures/PopRacesBlocked, ModelPageSwitches, NavOps*, NavResyncs, NavigationsSettled, PageCache*,
  ParkedPages, ActivationEvents, Activated/Deactivated/Resumed/StoppedSent, LastAppState, LastWindowActive,
  NativePageIds`.
- Renderer implements `internal interface IHostTreeOwner`: `CurrentHosts`, `TakeCurrentHosts()`,
  `DestroyHosts(hosts, pageJs)`, `ReleaseHost(host)`, `TakeSyntheticHosts()` (`PageCache.cs:52-64`),
  `IsSynthetic(host)`, `RenderedPage`, `CurrentTitle`, `WindowGeometryKnown`, `Reconcile()`,
  `ResetModelPageScopedState(string)`, `KickIn(long)`, `TimelineStart(string)`, `Collection`.
- Surface:
  ```csharp
  internal NativeStackSync(Window window, IMauiContext ctx, IHostTreeOwner owner);
  internal readonly record struct NavSnapshot(bool Busy, bool PopUnsynced, bool TopUnfollowed, string? TopPageId);
  internal NavSnapshot Step();              // SyncNativeNavigation + CompleteSettledNavigation
  internal bool Busy { get; } internal bool ActivationSettled { get; } internal bool NavigationSettled { get; }
  internal void WhenSettled(Action done, int timeoutMs = 3000);   // reports a timeout distinctly (bool argument)
  internal (BackTarget Target, Func<Task>? Pop) ResolveBackTarget();
  internal Page? ResolveCurrentPage(); internal Page? ResolveReconcilePage(); internal IReadOnlyList<Page>? ResolveModalStack(); internal int ExpectedNativeDepth();
  internal IReadOnlyList<string> NativePageIds { get; } internal string? TopPageId { get; }
  internal void SwitchPageInPlace(Page? previous, Page next); internal void ReclaimParked(List<NativeElementHost> desired);
  internal bool IsParked(host); internal IEnumerable<NativeElementHost> ParkedHosts { get; } internal IReadOnlyCollection<string> ParkedHostIds { get; }
  internal void FullPageReset(string reason); internal bool TakeStrayScan();
  internal Task PopModalTopAsync();
  ```
- Renderer keeps: `PollCore` → `var nav = _stack.Step(); if (!nav.Busy && !nav.PopUnsynced && …) Reconcile();`.
  **Every `Reconcile()` entry goes through this one gate** (closes the `ApplyWindowGeometry` bypass at
  `Layout.cs:208` and the pop-path double reconcile at `Navigation.cs:547`). `_ = PopMauiLevelsAsync` and `TryPop`
  observe their tasks (`NavigationCoordinator.cs:193`, `QtHostPageRenderer.cs:610,614`); `window.DescendantAdded/
  Removed` (`:502-503`) are unsubscribed in `Dispose`; `_largePageWarned` becomes a `ConditionalWeakTable`.
- Covered by all of `NavigationCoordinatorTests.cs`, `RendererTests.cs:379,395,417`,
  `ArchitectureAlignmentTests.cs:524,558,582,607,625,659`. New `NativeStackSyncTests` with a recording
  `IHostTreeOwner` stub: a rejected push leaves `NativePageIds` unchanged and calls `owner.Reconcile` zero times; a
  FollowNative with MAUI already popped starts no operation and calls `TakeCurrentHosts` once.
- Accept: NavigationCoordinatorTests + RendererTests + ArchitectureAlignmentTests green; `NavOpsCompleted`,
  `NavResyncs`, `PageCacheRestores` asserted in `A_back_gesture_keeps_the_other_tabs_parked_hosts` and
  `The_least_recently_used_page_beyond_the_limit_is_rebuilt` unchanged; `grep -n 'static' NativeStackSync*.cs` shows
  no mutable field; `tools/sf matrix nav navback shell tabpulley`.

### C5. `RenderScheduler` — DONE 2026-10-03 (host: 318 tests; device: 13/13 legs PASS 11:58)
- Done as: `Platform/QtHost/RenderScheduler.cs`, one per renderer. It owns the kicked-poll latch and `Kick`
  (was the static `NavigationKick` + `s_navKickPending`; now `renderer.PollKick`), the navigation request timestamp
  (was static `s_navRequestTs`), `KickIn`, `KickSettlePolls`, the layout and geometry "posted" latches (Interlocked)
  with `LayoutRequests`, the subtree queue (`QueueSubtree`/`TakeSubtrees`/`DropPendingSubtrees`), and the heartbeat
  (`StartHeartbeat`, moved from the boot code). The renderer keeps the passes and their dirty flags. The shell
  navigation subscriptions are per renderer (`s_navEventsSubscribed` and `ForgetShellSubscriptionsForTests` are
  gone), and `QtHostPageRenderer.Current` is gone: handlers reach the renderer through their session
  (`SailfishRenderSession.RequestPoll/NoteNavigationRequest/RequestSubtree`, `OfElement` for the attached property),
  Diagnostics through its context. `AdapterPreload`/`FlatRows` read `SailfishEnv`; the preload arms only when
  creation is not deferred. `FakeShim.Deferred` queues posts for latch tests. Tests: `RenderSchedulerTests` (100
  parallel layout requests → one pass and 100 counted; geometry; poll kick once until the poll starts; subtree
  collapse; navigation timestamp taken once).
- Deviations: the poll-outcome counters (`TimerPolls`, `KickedPolls`, `TimerPollsWithWork`) stay with `PollCore`,
  which decides them; the `KickIn` coverage test needs a dispatcher on the test thread and is not written. Static
  config knobs stay (`ActivationSettleMs`, `CreateChunk`, `NavAnimation`, settable for tests);
  `_deferNativeDestroy` goes with C4.
- Found and fixed on the way: `templates/maui-sailfish-app/MauiSailfishApp.csproj` referenced the package as
  `Version="SAILFISH_PACKAGE_VERSION"`, which only the template engine replaces, so the solution restored the
  template project only from NuGet's no-op cache; the first `pack-local` that cleared it broke every solution build
  (MSB4181 without a message). The version now goes through `PlatformMauiSailfishVersion` (the repo's
  `Directory.Build.props` in a checkout, the replaced literal in a generated app); a generated app was built
  against the feed.
- New `Platform/QtHost/RenderScheduler.cs`. Move `QtHostPageRenderer.cs:195-228` `KickIn/_kickAtMs`, `:319-350`
  `NoteNavigationRequest/RequestPoll/NavigationKick/s_navKickPending/s_navRequestTs/KickedPoll`, `:392-416`
  `NavIdleKick/SubscribeNavigationEvents/s_navEventsSubscribed`, `:624-679` `RequestLayout/RunRequestedLayout/
  _layoutPosted/RequestScrollGeometry/RunRequestedGeometry/_geometryPosted` (the `_layoutDirty/_geometryDirty`
  bools stay with the passes; the scheduler owns the "posted" latches and an Interlocked request flag),
  `Navigation.cs:143-160` `KickSettlePolls/_settleKickArmed`, `HandlerTree.cs:16-19,53-64,328-336`
  `_subtreeSync/_dirtySubtrees/_subtreePosted/QueueSubtree/TakePendingSubtrees`, `Architecture.cs:49-51`
  `_timerPolls/_kickedPolls/_timerPollsWithWork`, `LayoutRequests`, and the heartbeat loop from
  `SailfishMauiApplication.cs:235-250`.
- Surface:
  ```csharp
  internal RenderScheduler(IDispatcher dispatcher, Action<bool> poll, Action runLayout, Action runGeometry, Action<List<Element>> runSubtrees, Func<bool> inLayoutPass);
  internal void RequestPoll();  internal void NoteNavigationRequest();  internal long TakeNavigationRequestTs();
  internal void KickIn(long ms);  internal void KickSettlePolls(Func<bool> hasWaiters);
  internal void RequestLayout();  internal void RequestScrollGeometry();     // Interlocked latches
  internal void QueueSubtree(Element e);  internal bool TakePendingSubtrees(out List<Element> roots);
  internal void StartHeartbeat(int ms);  internal void SubscribeShellEvents(Action<long> navIdle);
  internal long TimerPolls, KickedPolls, TimerPollsWithWork, LayoutRequests;
  ```
  All posts go through `QtHostRuntime.Post` so `FakeShim.Post` keeps capturing them. `AdapterPreload` and
  `FlatRows` read `SailfishEnv` (not `Environment`); `ArmAdapterPreload` runs only once creation is no longer
  deferred (`QtHostPageRenderer.cs:806-807,1289`).
- Renderer keeps `Poll()`, `KickedPoll()` → `PollCore`; the `ISailfishRenderSession` methods from B1 forward here.
- Covered by `ArchitectureAlignmentTests.cs:51,243,256,288,494`, `RendererTests.cs:350`,
  `SampleAppRegressionTests.cs:766`. New `RenderSchedulerTests`: 100 parallel `RequestLayout()` → exactly one posted
  `runLayout`, `LayoutRequests == 100`; `KickIn(50)` then `KickIn(100)` → one delayed dispatch; two `RequestPoll()`
  before the kick runs → one `poll(true)`.
- Accept: ArchitectureAlignmentTests green with `timerWithWork == 0` in `Idle_timer_polls_do_no_native_work`;
  `grep -n 'static' QtHostPageRenderer.cs` lists no mutable static; `tools/sf matrix page perf stress`.

### C5b. Renderer main file and the reconcile, readable in steps — DONE 2026-10-03 (added; host: 348 tests; device: 14/14 legs PASS 14:20)
- `QtHostPageRenderer.cs` 1999 → about 1250 lines: members moved unchanged into `.Hosts.cs` (host lifecycle: create op,
  attach, pushes, release/destroy, pooled hosts, heal, routes), `.PageCalls.cs` (op batches, page handles,
  `CallPage`) and `.Walk.cs` (element → desired hosts, adapter choice and rebind, flat rows).
- `ReconcileCore` (about 400 lines) is now nine named steps, the code moved verbatim with the shared locals as
  parameters and results: `BeginPass` (page, reset hold, reported subtrees), `WalkPage`, `SwitchRenderedPage`,
  `CreationAllowed`, `PageChromeOps`, the page-cache switch inline, `DiffHostTree` (returns a `TreeChange`),
  `ApplyTreeChange`, `FinishPass` (layout, the unarranged retry, Appearing).

### C6. Row pool hardening (first collections step) — DONE 2026-10-03 (host: 327 tests; device: 9/9 legs PASS 12:27, 0 refused rekeys)
- Done as: `applyMauiOps` answers `created:destroyed:unknown` and `QtHostPageRenderer.ApplyOps` returns the unknown
  count (-1 without an answer). `TryAdoptPooledRow` first takes every pooled row holding one of the row's ids out of
  the pool (`TakePooledHolding`): the row's own former subtree (same ids, same order: a row that scrolled out and
  back) is adopted without a rekey, any other is destroyed so no rekey targets a taken id. A rekey the page still
  refuses destroys both names and creates the row (`RowRekeysRefused` counts it). Before adopting, `IsAlive` checks
  the root's geometry and that its `objectName` is still `maui_<id>`. Pooled destroys go through `DestroyNative`,
  which honours `_deferNativeDestroy` like the regular path. The bridge comment about dead delegates is corrected.
  `RowPoolEnabled` is settable (TestStatics restores it). `FakeShim` mirrors the page's rekey guard, counts
  `RekeysRefused`, answers `objectName`, and can refuse every rekey.
- Found with it: before, a row scrolling back while two rows were pooled got the other row's subtree, the page refused
  both rekeys (its own pooled subtree held the ids) and the hosts were bound to objects still named after the other
  row. Tests (`RowPoolRekeyTests`): `A_row_that_scrolls_back_takes_its_own_pooled_subtree` (fails without the fix:
  2 refused rekeys), `A_refused_rekey_creates_the_row_instead_of_binding_the_old_objects` (mutation-checked),
  `A_dead_pooled_row_is_not_adopted`, `Without_the_row_pool_a_detached_row_is_destroyed`,
  `Removing_the_list_destroys_its_pooled_rows`.
- Found on the device (12:18 run, legs green): every rekey read as refused (`-1`), so pooling was off. The
  `QmlPage.Call` wrapper never returned the called function's value (`if(p&&p.fn)p.fn(...)`), so `ApplyOps` (and the
  remorse trace) always got ""; `FakeShim` parsed the call text and answered anyway. Fixed: the wrapper returns the
  value (`return p.fn(...);return '';`), `FakeShim` answers `applyMauiOps` only when the expression returns it, and
  its page-call pattern and `QmlPageTests` follow the new text. With the old wrapper the pool tests fail now.
- Not written: the 48-row cap test (only rows near the viewport materialise, so attach events cannot fill the
  pool in the harness), the `Compatible`/late-key rejection test (a template's snapshot keys do not vary by row),
  the parked-list re-adoption and image-blank-on-adopt tests.
- `rekey` refused silently: `QtHostListAdapter.RowPool.cs:109-146` must not bind a host to a QML object whose
  `rekey` was refused (`MauiModelPage.qml:316` refuses when `__hosts[to]` exists). Before the ops: if any target id
  is held by another pooled row of the same list, adopt that row when it is the materialising row's own former
  subtree (ids match in order), else destroy it first (`DestroyPooled`). Make `applyMauiOps` return the unknown
  count (`created:destroyed:unknown`) and have `TryAdoptPooledRow` fall back to create when it is non-zero.
- Liveness probe with identity: `TryItemGeometry` + `GetProperty(handle, "objectName") == "maui_" + Id` before
  adopting (`RowPool.cs:120`).
- `DestroyPooledHosts` honours `_deferNativeDestroy` like `DestroyHosts` (`QtHostPageRenderer.cs:1536` vs `1471`).
- `QtHostCollectionBridge.cs:823` comment ("usually already dead with their delegate") is false in both modes; fix.
- Tests to add in `RendererTests`: `Compatible` rejection path (a pooled row with an extra non-late key is
  destroyed, not adopted), `LateKeys`, the cap (49th detached row destroyed), drain on `CleanupList`, parked-list
  pool kept and re-adopted after restore, stray sweep spares pooled ids, image source blanked on adopt,
  `MAUI_SAILFISH_ROW_POOL=0` (make `RowPoolEnabled` settable for tests), dead pooled handle falls back to create,
  **rekey refused falls back to create** (FakeShim must mirror the `__hosts[to]` guard at `FakeShim.cs:124-133`).
- Accept: tests green; `tools/sf matrix collection collection100 collection500 navback` with `RowsPooled/
  RowsAdopted` unchanged on the Kitchen tour.

### C7. `MeasureInvalidated` per row — DONE 2026-10-03 (host: 328 tests; device: 9/9 legs PASS 12:27)
- Done as planned: `Row.MeasureHandler`/`Remeasure`; `WatchRow` subscribes each new item and template row's cell
  views after their first measure (reused rows keep theirs), `UnwatchRow` in `ReleaseRowViews` (so also
  `CleanupList`). `MarkRow` hops to the Qt thread, ignores the list's own passes (`SlotMapping`, the new
  `_inRebuild` around `RebuildRows`) and schedules the pending pass, where `RemeasureRows` measures the marked rows
  under `SlotMapping`, pushes the rows (tops recomputed) and the row's delegate geometry when a height moved, and
  re-measures the list when its extent moved (`RemeasureListIfExtentMoved`, shared with the rebuild). Counter
  `RowsRemeasured`. Test `RowRemeasureTests.A_row_that_grows_pushes_a_new_height_without_a_rebuild` (mutation-checked).
- In `AddItemRows`/`AddTemplateRow` after `AdoptRowView`, subscribe `view.MeasureInvalidated += row.MeasureHandler`
  (new `EventHandler? MeasureHandler` on `Row`, `QtHostCollectionBridge.cs:188-203`); handler → `MarkRowDirty(Row)`
  (new): posts to the Qt thread, ignores while `_bridge.SlotMapping`/`_inRebuild`, re-measures through
  `MeasureItemExtent` (`QtHostListAdapter.cs:625`), updates `row.HeightDp`, invalidates `_rowTopsDp`, calls
  `PushRows()` + `UpdateDgGeometry` for the row's `DgState`, and `InvalidateMeasure` on the list when
  `ContentExtentDp` moved (reuse `:432-437`). Unsubscribe in `ReleaseRowViews` (`:611-622`) and `CleanupList`.
  QML: none (`__syncRows` applies `h`, `ListView.qml:224-227`).
- Test: `A_row_that_grows_pushes_a_new_height_without_a_rebuild` (change a bound label's text, assert
  `mauiRowsJson` changes and `RowsBuilt` does not).
- Accept: test green; `tools/sf matrix collection collection10`.

### C8. Incremental `CollectionChanged` (linear lists) — COVERED, splice not built (2026-10-03)
- Measured first: the rebuild already reuses every unchanged row (the same `Row`, key, views and height, keyed by
  item identity), so an Add or Remove builds one row, the QML keyed diff inserts or removes one delegate, and a
  removed row's views stop being logical children of the list. Pinned by `IncrementalRowsTests`
  (`Adding_one_item_builds_one_row_and_keeps_the_others`, `Removing_an_item_releases_its_view`), green on the
  current code. A splice would save the O(n) walk and dictionary lookups only; not worth a second row-building path.
  Value-type items (ints) are still rebuilt on every change (identity reuse cannot tell two equal ints apart).
- `SourceHandler` (`QtHostListAdapter.cs:212`) → `(_, e) => QueueChange(e)` into a
  `ConcurrentQueue<NotifyCollectionChangedEventArgs>`; `FlushInvalidate` drains it: any `Reset`, grouped, `Span>1`,
  `Horizontal` or `Carousel` → `RebuildRows`; else `ApplyChange(e)`: `Add` → `BuildItemRow(object item, int
  ordinal)` (extracted from `:490-507`) + `Rows.Insert`; `Remove` → `ReleaseRowViews`, `Rows.RemoveAt`, `ClearDg`/
  pool; `Move` → reorder; `Replace` → remove + add. After the splice: `Renumber()` (`:460-469` logic),
  `TotalItems`, `_rowTopsDp = null`, `PushRows`, `RecomputeSelection/PushSelection`, `UpdateRemainingThreshold`,
  `InvalidateMeasure` on extent change. QML keyed diff (`ListView.qml:193-245`) already handles insert/remove/move.
- Tests: `Appending_to_a_grid_keeps_the_full_rows` (`RendererTests.cs:319`) stays; add
  `Adding_one_item_builds_one_row_and_keeps_the_others` (`RowsBuilt` += 1, other `Row` instances reference-equal,
  one delegate rebind) and `Removing_an_item_releases_its_view`.
- Accept: Add/Remove on an `ObservableCollection` bumps `RowsBuilt` by the delta; `tools/sf matrix collection
  collection100 stress`.

### C9. One equality, one lookup, one signature, re-entrancy guard — PARTLY DONE 2026-10-03 (host: 335 tests; device: 8/8 legs PASS 12:40)
- Done, each a real defect with a test that fails without the fix (`CollectionConsistencyTests`):
  - Template change: rows were reused by item, so a new `ItemTemplate` kept every row's old views. `BuildSignature`
    (templates by identity, grouping, axis, carousel, span) gates `Reusable`
    (`Changing_the_item_template_rebuilds_every_row`).
  - Equality: `ItemsEqual` (same object or `Equals`), as MAUI's adapters match; used for the single selection (an
    equal record did not select), the carousel `CurrentItem` and `ResolveRow`
    (`Selection_matches_items_by_value_equality`). Row reuse keeps identity on purpose (an equal but replaced item
    may carry other data).
  - `ScrollTo(index, groupIndex)` on a grouped list treated the index as a flat ordinal; `GroupItemRow`
    (`ScrollTo_with_a_group_index_lands_in_that_group`).
  - A replaced slot view (a new `Header`, content for a slot that had none) was never materialized; `MaterializeSlot`
    treats `!ReferenceEquals(existing.Root, view)` as a remap (`Replacing_the_header_materializes_the_new_view`).
  - Re-entrancy: `Invalidate` while `_inRebuild` (C7) only marks the rows dirty, so the pending pass rebuilds after
    the current rebuild instead of nesting one (`A_rebuild_triggered_from_inside_a_rebuild_runs_after_it`).
- Test-environment note: a test that leaves a dispatcher behind makes later heartbeat polls skip list work (the list
  believes its own pass is scheduled); list tests that need it use `KickedPoll`. Belongs to D2.
- Not done: `FindDelegate`/`DelegateName`/`SlotName` helpers and JSON key constants (naming only), `TakeReusableRow`
  duplicate scan, `mauiPosition` only when changed (the always-push may be what re-snaps the carousel; needs a device
  carousel check first), one `ReadLayout` per rebuild (cheap), the `Loop` property swap and the PathView carousel
  events.
- `ItemsEqual(a, b)` (reference, then `Equals`) at `QtHostListAdapter.cs:521,699,718,855,1571`;
  `FindDelegate(string obj)` (`FindVisual` then `FindObject`) at `:1107-1109,923-925` and
  `QtHostCollectionBridge.cs:171-174`; `DelegateName(int row)` / `SlotName(string)` replacing `:313,1069,1093,922`;
  named constants for the JSON keys `k/r/h/t/n/s` (`:807-812`, `ListView.qml:224-227`).
- `BuildSignature(Template, Horizontal, Carousel, Span, CellWidthDp, CrossDp, Grouped)` computed after
  `ReadLayout()`; fill `Reusable` (`:359-366`) only when it equals `_lastSignature`; replace `:375-376`. In
  `MaterializeSlot` treat `!ReferenceEquals(existing.Root, view)` as `Remap` (`:891-893`). `_inRebuild` flag
  (try/finally in `RebuildRows`); `Invalidate` (`:258`) sets `RowsDirty` and returns while rebuilding.
- `OnScrollToRequested` (`:1522`) honours `GroupIndex`; `TakeReusableRow` (`:516-523`) scans the queue for an item
  that appears twice; `PushLayout` sends `mauiPosition` only when it changed (`:791-792`); `RebuildRows` calls
  `ReadLayout` once (`:372,675`); fix the mis-indented `SchedulePending` at `:1430-1432` and
  `QtHostCollectionBridge.cs:319-322`; `Loop` added to `ViewProperties` (`QtHostCollectionBridge.cs:776-785`) with an
  adapter swap on change; the PathView carousel emits `list-scroll`/`list-item-tapped` (`CarouselView.qml:84-96`)
  so `Scrolled`/`RemainingItemsThresholdReached` work for the default `Loop=true`.
- Tests: `Selection_matches_items_by_value_equality`, `Changing_the_item_template_rebuilds_every_row`,
  `Replacing_the_header_materializes_the_new_view`, `A_rebuild_triggered_from_inside_a_rebuild_runs_once`,
  `ScrollTo_with_a_group_index_lands_in_that_group`.
- Accept: tests green; `tools/sf matrix collection containers`.

### C10. Split the list adapter — PARTLY DONE 2026-10-03 (host: 348 tests; device: 7/7 legs PASS 14:01)
- Done: `QtHostListAdapter.cs` (1764 lines) is split by responsibility into partials of the same class: the main
  file keeps the state, subscriptions, property pushes and cleanup (476 lines); `.Rows.cs` (row model build,
  reuse, per-row remeasure), `.Delegates.cs` (delegate registry, materialization, deferral, release), `.Slots.cs`
  (header/footer/empty), `.Input.cs` (row taps, selection), `.Scroll.cs` (scroll reports, ScrollTo, carousel
  position), `.Lookup.cs`, next to the existing `.RowPool.cs`. The member bodies moved unchanged. The lookups are
  pure functions over the rows (`RowLookup`; the adapter forwards), tested without the shim (`RowLookupTests`:
  ordinals across groups, grouped indices, value lookup).
- Not done: separate classes with intent methods for the bridge (`OnHostRecreated`, `HealHost`, …) and the
  15×250 ms resync sweep replacement; the bridge still reads adapter fields. The partials make those seams visible
  for that step.
- `RowModel` (pure, no Qt): `Rows`, `TotalItems`, `Span/SpacingDp/HSpacingDp/CellWidthDp`, builders (`BuildRows`,
  `AddItemRows`, `AddTemplateRow`, `TakeReusableRow`, `BuildSignature`), lookups (`IndexOfItem`, `ItemAt`,
  `ItemIndexToRow`, `RowToItemIndex`, `LastVisibleItemOrdinal`, `ResolveRow`, `RowExtentDp`, `ContentExtentDp`,
  `CrossExtentDp`), `RowsJson()`/`SelectionJson()`. `internal sealed class RowModel(Func<object?, View?>
  createItemView, Func<View,double,double> measure)`.
- `DelegateRegistry`: `Delegates`, `ByHandle`, `DeferredRows`, `ResyncDelegates`, `RequestMaterialize`,
  `DrainDeferredRows`, `MaterializeRow`, `ClearDg`, `ReleaseDg`, `UnmaterializeDg`, the row pool (`RowPool.cs`
  becomes `RowPool` owned here), `UpdateDgGeometry`, attach retries (from `QtHostCollectionBridge.cs:36-39`).
- `SlotHost`: `Slots`, `HeaderView/FooterView/EmptySlotView`, `MaterializeSlots/MaterializeSlot/MeasureSlot/
  WatchSlot/MarkSlot/UnwatchSlot/UpdateSlotGeometry`, `SlotsDirty`.
- `RowInput`: `OnRowTapped`, `TryFindRowTap`, `RowHasTap`, `RecomputeSelection`, `PushSelection`, `SelectedCells`.
- `ScrollState`: `OnListScroll`, `UpdateRemainingThreshold`, `OnScrollToRequested`, `LastReportedYDp`,
  `First/LastVisibleRow`, `ScrollTick`, `ThresholdReached`, `PushPosition`, `OnCarouselPosition`.
- `QtHostListAdapter` keeps `View`, `Host`, `PageId`, subscriptions, `Push/PushMany/PushLayout/PushRows` and
  intent methods replacing the bridge's field-poking: `OnHostRecreated()` (`QtHostCollectionBridge.cs:349-358`),
  `OnRestored()` (`:321-322`), `ProcessPending(widthDp, clock)` (`:481-504`), `RoutesInto(Dictionary)` (`:367-372`),
  `HoldsHost(host)` (`:609-614`), `HealHost(host)` (`:576-597`), `DescribeRow`, `TryGetRowPoint`. `Row/DgState/
  SlotState` move to their owners. Replace the 15×250 ms resync evals (`:565,378`, armed at
  `QtHostListAdapter.cs:448`) with one post-rebuild sweep once `ListView.qml` guarantees `onMauiRowChanged`.
- Tests: all of `RendererTests.cs:176-348` and `SampleAppRegressionTests.cs:286-401,912` stay green; add a pure
  `RowModelTests` (flatten a grouped source, ordinals, `ItemIndexToRow`) that needs no shim; add
  `Renderer/CollectionBridgeTests.cs` (header/footer/empty slot creation and remeasure, INCC keeps keys,
  `RefreshView` consumed by its list, `list-item-tapped` under `SelectionMode.Multiple`, attach retry,
  `Unregister` after `DisconnectHandler`).
- Accept: `grep -c "public .*;" QtHostListAdapter.cs` shows no public mutable fields; the bridge references no
  adapter field directly; `tools/sf matrix collection collection10 collection100 collection500 containers`.

---

## Phase D: build, tests, diagnostics, hygiene (parallel to A–C)

### D1. Test harness hardening (do first) — DONE 2026-10-03 (`ab808cd`)
- Done as: `FakeShim.Strict` records unmodelled evals in `UnhandledEvals` and `RendererHarness.Dispose` fails the
  test (not a throw inside `Eval`: the renderer catches eval failures, so a throw would change the path and still
  pass); `FakeShim.AllowedUnanswered` lists the probe prefixes with their reason; `FakeShim.PageCalls` records page
  entry points (`setMauiScroll`, `setMauiTabs`, `setMauiRefresh`, `mauiReattachPulleys`, tab drag, remorse,
  `__destroyAllHosts`); `tests/…/TestStatics.cs` captures and restores the shim, `ActivationSettleMs`, text metrics,
  the diagnostics hook and the adapter/library-handler/image-source registries (`CaptureForTests` hooks in core);
  `FakeShimContractTests` prove both. The listed test classes were already in the `renderer` collection. Suite: 268,
  green serial and with `xunit.parallelizeTestCollections=true`.
- `tests/Linux.SailfishOS.Tests/Renderer/FakeShim.cs`: `public bool Strict { get; set; } = true;` and at the end of
  `Eval` (`:93`) `if (Strict) throw new InvalidOperationException($"FakeShim: unhandled eval: {expression[..Math.Min(160, expression.Length)]}");`
  plus `HashSet<string> IgnoredEvalPrefixes` for known fire-and-forget JS (`adapterPreload`, `__setTitle`, …): run the
  suite and add each legitimate prefix to the set, never to a catch-all. Model `setMauiScroll`, `setMauiTabs`,
  `mauiRemorse`, `mauiSetTabDrag`, `__pushDialog` (`"ok"`), `__destroyHostsNotIn`; `ApplyOps` mirrors the `rekey`
  guard (C6). `ApplyGeometry` (`:253`) reads the handle as `GetInt64()` once E1 lands.
- `RendererTests.cs:15-61` `RendererHarness`: a scope that captures and restores `QtHostRuntime.TestShim`,
  `QtHostPageRenderer.ActivationSettleMs` (=250), `QtHostTextMetrics.CacheEnabled` + `ClearCache()`
  (`QtHostTextMetrics.cs:74,83`), `SailfishMauiApplication.Diagnostics`, `SailfishHandlersFactory.LibraryReplacements`
  (add `ResetLibraryReplacements()` at `SailfishHandlersFactory.cs:119`), `QtHostImageSources.Resolvers` (add
  `Reset()` at `QtHostImageSources.cs:13`), `QtHostAdapters._map` (add `Reset()` at `QtHostAdapters.cs:55`),
  `SailfishEssentials._installed` (`ResetForTests()`), `SailfishTheme._current`, `QtHostServices` subscribers
  (add `Reset()`), the loop thread (`SailfishDispatcherProvider.UnbindLoopThread()`).
- Move `SurfaceTests`, `ArchitectureAlignmentTests`, `SKGLViewAndImageSourceTests`, `DispatcherTests`,
  `WindowScopeTests` into `[Collection("renderer")]`.
- Verify: `dotnet test` green, then once with `--no-build -- xunit.parallelizeTestCollections=true`.
- Accept: FakeShim throws on an unknown eval and the suite is order-independent.

### D2. Tests for the uncovered core (as the C steps land) — PARTLY DONE 2026-10-03 (341 tests)
- Done: `OpContractTests.Every_emitted_op_kind_is_handled_by_the_model_page` (every `["op"] = "…"` in C# has an
  `o.op === "…"` branch in `MauiModelPage.qml`, and no branch is dead);
  `AdapterKeyContractTests.No_pushed_value_is_a_clr_type_name` (renders every control and fails on a pushed value or
  op string that is a CLR type name: `BridgeValue.Serialize` falls back to `ToString()` for unknown types, so a
  Brush or Thickness would reach QML as its type name). With the C/E steps: `RenderSchedulerTests`,
  `NativeStackCoordinatorTests`, `RowPoolRekeyTests`, `RowRemeasureTests`, `IncrementalRowsTests`,
  `CollectionConsistencyTests`, `NativeContractTests`, `PublicSurfaceTests`. The suite grew 268 → 341.
- Not done: per-type `BridgeValue` round trips for Brush/Thickness/CornerRadius/FormattedString (the serializer has
  no case for them; the leak test covers what the controls actually push), `CollectionBridgeTests` (C10).
- `Renderer/CollectionBridgeTests.cs` (C10), `Renderer/HandlerSnapshotTests.cs` (C3), `ContractTests`
  `BridgeValueTests` extended with `Brush`, `Thickness`, `CornerRadius`, `FormattedString`, enum and `IList<object>`
  round trips; a test that every `op` kind the renderer emits (`grep '"op"' QtHostPageRenderer*.cs`) is handled by
  `FakeShim.ApplyOps`.
- Accept: `dotnet test` ≥ 300 attributes; one deliberate snapshot-key removal in a handler fails a test.

### D3. `Build.Tasks` assembly; `sf deploy` asks MSBuild — PARTLY DONE 2026-10-03 (host: 351 tests; device: 14:30–14:32)
- Done: `src/Linux.SailfishOS.Build.Tasks` (netstandard2.0, Microsoft.Build.Utilities.Core 17.14.28 private, not
  packable; output in `artifacts/build-tasks/`) holds `SailfishIconSet` and `SailfishPatchLauncherRpath`, moved
  from the inline RoslynCodeTaskFactory code. The targets load them out of process (`TaskHostFactory`: no MSBuild
  node keeps the dll locked) from `$(SailfishBuildTasksAssembly)`: the packed `buildTransitive/net11.0/tasks/` copy,
  else the checkout's `artifacts/build-tasks/`. The core project builds the tasks first (ProjectReference without
  output assembly) and packs the dll. Targets 964 → 685 lines. Tests `BuildTasksTests` (a generated 172 px PNG
  becomes the 86/108/128/172 set; a non-square icon is refused; the rpath placeholder is filled, a longer rpath
  refused). Verified: pack-local carries the dll; the Sample (targets from src) and SkiaSharpProbe (targets from the
  package) deploy and pass page/features and skia/skiainput; a local Harbour publish runs the rpath task.
- Targets fixes: an unknown Linux RID fails `_ValidateSailfishRpmProperties` with a message (it built an x86_64 RPM
  silently); the store metadata YAML carries `$(SailfishVersion)`/`$(SailfishRelease)` (it wrote the assembly
  `Version` and release 1, so it disagreed with the RPM it described).
- Not done: `sf-lib.sh` asking MSBuild for the package name, binary and arch. Every `tools/sf` command sources the
  library, so it would add an MSBuild evaluation (seconds) to each; it needs a lazy, cached query first. The
  remaining items (duplicate defaults, signing only with rpmbuild, the trimmer root) are open.
- New `src/Linux.SailfishOS.Build.Tasks/Linux.SailfishOS.Build.Tasks.csproj` (`netstandard2.0`,
  `Microsoft.Build.Utilities.Core` with `PrivateAssets=all`, `AssemblyName=Microsoft.Maui.SailfishOS.Build.Tasks`,
  `IsPackable=false`). Move `SailfishIconSet` (`Microsoft.Maui.SailfishOS.targets:518-780`) and
  `SailfishPatchLauncherRpath` (`:395-423`) into it; `tests/…/BuildTasksTests.cs` (PNG round trip 172→86,
  RGBA/palette/gray inputs, rpath placeholder too long).
- `Linux.SailfishOS.csproj`: `ProjectReference` with `ReferenceOutputAssembly=false`; pack the dll to
  `buildTransitive/net11.0/tasks/`; targets replace both `UsingTask` with
  `AssemblyFile="$(SailfishBuildTasksAssembly)"` (default `$(MSBuildThisFileDirectory)tasks/…dll`, in-repo
  fallback to the project's bin). Fix `Linux.SailfishOS.csproj:75,89` (script name → `tools/sf native-build`).
- `tools/lib/sf-lib.sh:89-116`: replace the directory-name heuristic with `dotnet msbuild "$SF_SAMPLE_DIR"
  -getProperty:SailfishPackageName -getProperty:AssemblyName -getProperty:SailfishRpmArch
  -getProperty:SailfishRpmOutputDir -getProperty:PublishDir -p:Configuration=… -p:TargetFramework=…
  -p:RuntimeIdentifier=… -p:CreateSailfishRpm=true` (JSON, parse with python3); keep `SF_PKG/SF_BIN` as overrides;
  the Debug `-debug` suffix becomes an MSBuild rule (`targets:226`); delete `-p:SailfishPackageName=` from
  `sf_publish_rpm` (`:230`); derive `SF_RPM_ARCH` from the query (today `aarch64` regardless of `SF_RID`, `:116`).
  Update `ToolsPackagingTests` for the new pack path.
- Targets fixes while there: `:923` store YAML `version: $(SailfishVersion);release: $(SailfishRelease)`; `:244`
  `<Error>` for an unknown RID instead of `x86_64`; `:100-101` add `And '$(SailfishPackageName)' != ''`; delete the
  duplicate defaults `:80-81`; `:909` sign only when `_SailfishRpmBuilder == rpmbuild`, warn otherwise;
  `scaffold/trimmer.xml:6` root `CreateMauiApp` on any type (or `[DynamicDependency]` from the entry point).
- Verify: `tools/sf pack-local`; `dotnet new maui-sailfish --sailfish-only -o /tmp/x && dotnet publish -f
  net11.0-sailfish`; `tools/sf deploy --run && tools/sf verify`; `tools/sf matrix page`.
- Accept: targets ≤ 650 lines, no `RoslynCodeTaskFactory`; `rpm -qp --qf %{NAME}` of the RPM equals
  `-getProperty:SailfishPackageName`.

### D4. One version source — DONE 2026-10-03 (host-verified end to end)
- Done as: `src/Linux.SailfishOS.WorkloadManifest/SailfishVersionedFiles.targets` writes `@SAILFISH_VERSION@`
  templates to `obj/sailfish-versioned/` before build and pack; `data/WorkloadManifest.json.in`/`.targets.in`
  replace the tracked files; the manifest package packs, the workload tool embeds and the template pack ships
  (`template.json` keeps `@SAILFISH_VERSION@` in source) the generated copies. Packing with
  `-p:PlatformMauiSailfishVersion=0.1.1` puts 0.1.1 in all three; the 0.1.0 outputs are byte-identical to the old
  tracked files. Tests: `ContractTests.The_package_version_is_written_down_once` (git-tracked sources),
  `WorkloadToolTests.The_embedded_manifest_carries_the_repo_version`. Verified: `tools/sf pack-local`, then a fresh
  `dotnet new maui-sailfish --sailfish-only` builds for `net11.0-sailfish` with `Version="0.1.0"`.
- Keep `PlatformMauiSailfishVersion` (`Directory.Build.props:17`) as the only literal. WorkloadManifest csproj:
  delete `data/WorkloadManifest.json/.targets` from git; add `_GenerateWorkloadManifest` (BeforeTargets
  `_GetPackageFiles;Build`) writing both from `data/*.in` templates with `@VERSION@` into
  `$(IntermediateOutputPath)data/`; the Workload tool embeds the generated files (`Workload.csproj:23-24`);
  `SailfishOS.Templates.csproj` copies `template.json` through the same replacement.
- Test: `VersionLiteralTests` greps the tracked tree for `\b0\.1\.0\b` outside `Directory.Build.props` and docs;
  `WorkloadToolTests.cs:35` reads the version from assembly metadata.
- Accept: bumping the props value to 0.1.1 and `tools/sf pack-local && tools/sf workload-install && dotnet new
  maui-sailfish --sailfish-only -o /tmp/y && dotnet build /tmp/y -f net11.0-sailfish` works with no other edit.

### D5. Diagnostics toward a separate package — DEFERRED (owner decision 2026-10-02: "maybe a separate package some day")
- Not started on purpose: the owner set it for later. `QtHostRuntime` stays public for app interop (B4), which removes
  half of this step's reason (hiding the runtime behind `IDiagHost`); the runner keeps using internals through
  `InternalsVisibleTo`. When it is picked up, the leg-per-class split below is the place to start.
- Core: `Platform/Diagnostics/IDiagLeg.cs`: `public interface IDiagLeg { string Name { get; } string EnvVar { get; }
  void Run(QtHostDiagnosticsContext context); }` and `SailfishDiagLegs.Register(IDiagLeg)`; make
  `QtHostDiagnosticsContext` public (`QtHostDiagnosticsHook.cs:16`) behind a facade `IDiagHost { string Eval(string
  js); void InjectPointer(int kind, double x, double y); string GetProperty(string hostId, string name); string
  GrabPng(string path); … }` implemented in core (`QtHostDiagHost`).
- Diagnostics: one leg class per partial (`PageLeg`, `ControlsLeg`, … 31 legs from `tools/cmd/matrix.sh:22`) in
  `namespace Microsoft.Maui.SailfishOS.Diagnostics.Legs`, replacing the `_qt*Diag` flag block
  (`QtHostDiagnosticsRunner.cs:251-302`) with `foreach (var leg in SailfishDiagLegs.Registered) if
  (SailfishEnv.Flag(leg.EnvVar)) leg.Run(ctx)`. Move `DiagChecks/DiagQml/DiagPng` to
  `Microsoft.Maui.SailfishOS.Diagnostics`. Make `QtHostRuntime.InjectPointer/InjectTouch/GrabPng/PerfStats/DiagStats/
  RecordStart/RecordStop/Eval` `internal` (B4) and route them through `IDiagHost`; keep the core
  `InternalsVisibleTo` until the facade covers every use (188 `Eval` + 85 `InjectPointer` calls today), then delete it.
- Samples: Sample `MauiProgram.cs:26-30` keeps `SailfishDiagnostics.Register()`; Kitchen `DemoTour.cs:221` and
  SkiaSharpProbe `ProbePage.cs:196-366` switch to `IDiagHost` from `SailfishDiagnostics.Host`. Fix the csproj
  comment (`Diagnostics.csproj:3-7`); `IsPackable=true`, `PackageId=Microsoft.Maui.SailfishOS.Diagnostics`; add to
  `tools/cmd/pack-local.sh` after line 33.
- Verify: `dotnet build Linux.Sailfish.slnx -c Release`; `tools/sf deploy && tools/sf matrix page controls f3 f4
  skia`.
- Accept: `grep -r "QtHostRuntime\." src/Linux.SailfishOS.Diagnostics samples` empty; the legs above PASS.

### D6. Docs and repo hygiene — docs part DONE 2026-10-03; repo/sample part open
- Done: README/tools.md/parity-plan.md leg counts (31) and the CI claim; skiasharp-plan.md test folder and diag
  location; the phantom IVT in `Linux.SailfishOS.SkiaSharp.csproj`; `Linux.SailfishOS.csproj` native-build
  messages; sailfishos-packaging.md product name; a note in aot-and-trimming.md that its Q/A codes refer to the
  removed PLAN (the dated log itself is left as history) and B2 marked removed.
- Open (needs the owner or the phone): ~~`docs/media` to Git LFS~~ (decided 2026-10-04: MP4 sources untracked, GIFs stay); `Directory.Packages.props` is not
  empty (it turns CPM off) and stays; Sample csproj hand copies and `Program.cs`, Kitchen's CTK try/catch and
  `IDispatcher` pin are device-visible changes — do them with a deploy + Kitchen tour.
- Docs: `README.md:24`, `docs/tools.md:25,157-159`, `docs/parity-plan.md:10,25,131-132,167` → 31 legs, drop the
  CI claim; `docs/architecture.md:23` → heartbeat wording; `docs/skiasharp-plan.md:161,183` → the real test folder,
  no diagnostics assembly; `SkiaSharp.csproj:27` delete the phantom IVT; `docs/aot-and-trimming.md:141,355,435-438`
  cite legs instead of Q-numbers; `docs/sailfishos-packaging.md:4` product name.
- Repo: ~~`docs/media/*.mp4|gif` (about 23 MB) to Git LFS or a release asset~~ (MP4s untracked 2026-10-04); delete the empty
  `Directory.Build.targets` and `Directory.Packages.props` (or give them the common props); `Sample.csproj:44-75`
  → `ProjectReference` + the package's `runtimes/` assets instead of hand copies; Sample `Program.cs` → the
  template's shape; Kitchen `MauiProgram.cs:85-99` drop the try/catch around `UseMauiCommunityToolkit`, `:115-116`
  remove the `IDispatcher` singleton.
- Accept: `grep -rn "sf-native-build.sh\|27 legs\|25/25\|ci.yml" --include=*.md --include=*.csproj .` empty;
  `git ls-files | xargs du -ck | tail -1` < 12 MB.

---

## Phase E: native bridge

Order: E1 (version symbol, cheap, makes every later change detectable) → E4 (split + atomics, mechanical) → E2 →
E3 → E5. E2, E3 and E5 each bump `SFHOST_ABI_VERSION`.

### E1. ABI version + stale-shim detection — DONE 2026-10-03 (device: 6/6 legs PASS 12:48, mismatch refused 12:52)
- Done as planned: `SFHOST_ABI_VERSION 2` and `sailfish_host_abi_version()` (native rebuilt); `QtHostNative.AbiVersion`;
  `QtHostRuntime.CheckAbi` before `sailfish_host_init` throws a `QtHostException` naming both versions (an old shim
  without the symbol reads as "unversioned"). Error codes `SFHOST_E_JS = -4` (eval, page push/pop; they returned
  -2, the property code) and `SFHOST_E_LOAD = -5` (a QML file that does not load; was -2), mirrored as
  `QtHostRuntime.SfhostEJs/SfhostELoad`. `tools/sf verify` D6: sha256 of the installed `libsailfishhost.so` against
  the published one. Tests `NativeContractTests` (header version == managed, error codes match, every declared export
  defined exactly once across `Native/*.cpp` and every `DllImport` declared in the header).
- Device: verify D6 OK; a QML that fails to load now logs `native code -5`; a build expecting ABI 3 against the
  installed shim logs `sailfish_host_abi_version failed: libsailfishhost.so ABI 2, this build needs 3 … (redeploy
  the app)` and does not start.
- `sailfish_host.h`: `#define SFHOST_ABI_VERSION 2` and `int sailfish_host_abi_version(void);`; implemented in
  `sailfish_host.cpp` (later `host_core.cpp`). `QtHostNative.cs`: `const int AbiVersion = 2` and the import;
  `QtHostRuntime.Run` (`:139-143`) checks it before `sailfish_host_init` and throws `BootFailed(abi,
  "sailfish_host_abi_version")`; `EntryPointNotFoundException` (old shim) is rethrown as the same `QtHostException`
  with code -1.
- Error codes: add `SFHOST_E_JS = -4` (eval/push/pop) and `SFHOST_E_LOAD = -5` (QML load) to `sailfish_host.h:30-35`
  and `QtHostRuntime.cs:36-48`; fix `sailfish_host.cpp:1159,1605,1622,1641`.
- `tools/cmd/verify.sh`: check D6 = sha256 of the installed `libsailfishhost.so` vs `$SF_PUBLISH_DIR/` (same
  pattern as D4, `:109-117`) plus, on the host, the header macro equals `QtHostNative.AbiVersion`.
- Tests: `ContractTests.AbiVersionTests` (header macro == `QtHostNative.AbiVersion`); `NativeLayoutTests` (every
  `sailfish_host_*` declared in the header is defined exactly once across `Native/*.cpp`).
- Accept: a deliberately old shim on the device makes `tools/sf run` print `sailfish_host_abi_version failed`
  instead of a silent feature loss; `tools/sf matrix bridge`.

### E2. One typed op/prop model, one serializer, unified encodings — COVERED, records not built (2026-10-03)
- Re-checked against the code: every op already comes from one factory (`BridgeOps` + the renderer's `CreateOp` and
  the row pool's rekey) and one serializer (`BridgeValue`); `OpContractTests` now fails when C# emits an op the page
  has no branch for, or the page keeps a branch nothing sends. The two rect encodings serve two readers on purpose:
  geometry batches send compact `x/y/w/h` to `apply_geometry`, property values send `width/height` to the
  QVariant→QRectF conversion; unifying them changes both sides for no behaviour. `NativeElementHost` already crosses
  as a numeric `{"$handle":N}`. A record hierarchy would restate `BridgeOps` and needs its own serializer anyway
  (source-generated System.Text.Json cannot write the `object?` props of a create op under trimming).
- Left as written below in case the op set grows; the non-finite number rule already holds (`BridgeValue.Number`).
- New `Platform/QtHost/Bridge/BridgeOp.cs`: `internal abstract record BridgeOp(string Op)` with
  `[JsonPolymorphic(TypeDiscriminatorPropertyName = "op")]` and derived `CreateOp(Id, Uri, Src, Props, ParentObj,
  Parent)`, `DestroyOp`, `ReparentOp`, `OrderOp`, `RekeyOp(From, To, Parent)`, `TitleOp`, `BusyOp`, `SchemeOp`,
  `BackOp`, `OrientationsOp`, `BackgroundOp`; `BridgeGeometry.cs`: `GeometryEntry(long Handle, double X, Y, W, H,
  int Vis, int Local)`, `BridgeRect(X, Y, W, H)`, `BridgeHandle(long)` serialized as `{"$handle":N}` only.
- `SailfishJsonContext` (`SailfishEssentials.cs:221-229`): add the new types, `TextMeasureRequest`,
  `EventEnvelope[]`; a `JsonConverter<double>` writing `0` for non-finite values (today `QtHostBridge.cs:43-44`).
- Replace `BridgeOps` (`QtHostBridge.cs:139-167`) and the `Dictionary<string, object?>` ops in
  `QtHostPageRenderer.cs:1149-1166,1605-1622`, `QtHostCollectionBridge.cs` (grep `["op"] =`),
  `QtHostListAdapter.RowPool.cs`; `BridgeValue.Serialize` stays for leaf values but `Rect` emits `{x,y,w,h}`
  (`:28`) and `NativeElementHost` a numeric `$handle`; delete `ObjectRaw` (`:125-136`);
  `QtHostPageRenderer.Layout.cs:514-529` builds `GeometryEntry[]`; `QtHostTextMetrics.cs:37-48` uses the record;
  `QtHostDiag.Summary` (`QtHostDiag.cs:198-213`) too.
- C++: `sailfish_host.cpp:827-866` accept `w/h` and `width/height` for one release, then drop `width/height`;
  `:2483-2485` accept `{"$handle":N}` and number, drop the string form; `:871-875` drop bare numbers once C# only
  sends `$handle`. The generic-key skip list (`:2217-2221`) and `MauiModelPage.qml:438-442` both read from one
  `qml/lib/bridge.js` `GENERIC_PROPS` (A2) and a test asserts equality.
- Tests: `BridgeEncodingTests`: every `BridgeOp` subtype round-trips and its `op` discriminator equals the string in
  `MauiModelPage.qml` (regex over `o.op === "…"`); `GeometryEntry` serializes `handle` as a number; `GENERIC_PROPS`
  == the C++ list. `FakeShim.ApplyGeometry` reads `GetInt64()`.
- Accept: `dotnet test` green; `tools/sf matrix geometry bridge` with zero `apply_geometry` failures in the device log.

### E3. `sailfish_host_invoke` replaces JSON-through-eval — PARTLY DONE 2026-10-03 (host: 342 tests; device: 14/14 legs PASS 13:35, no eval fallback)
- Done: `sailfish_host_invoke(handle, method, arg, out, cap)` (`QMetaObject::invokeMethod` on the QML function as
  `QVariant f(QVariant)`; -2 no such method, -3 dead handle; counts `invokes` in `perf_stats` and still counts
  `applyMauiOps` as `opsEvals`, so the perf leg's batch checks keep their meaning); `SFHOST_ABI_VERSION` 3.
  `MauiModelPage` names itself `mauiPage_<id>`; the renderer resolves a page's handle with the existing
  `FindObject` and caches it per page id (a dead handle drops the entry and looks again). `CallPage(pageId, method,
  arg)` calls it and falls back to the eval of the same call when the page has no handle or lacks the method
  (`PageInvokes`/`PageCallFallbacks`, printed by the perf leg). `ApplyOps`, `DestroyHosts` and the pooled destroys
  take a page id instead of a JS expression (no `QmlPage.ById` at those call sites any more); `applyMauiOps`,
  `setMauiTabs`, `setMauiRefresh`, `mauiReattachPulleys`, `__destroyAllHosts` and `__destroyHostsNotIn` go
  through it. `FakeShim.Invoke` models the page functions (ops on the addressed page, the stray sweep, the recorded
  page calls) and a popped page's handle reads as dead. Test `PageInvokeTests.Op_batches_and_page_calls_go_through_invoke_after_a_push`.
- Not done: the calls with non-string arguments or the shell as target stay on eval (tab drag with a number,
  `__openContextMenu`, `__pushDialog`, `mauiOpenPulley`, remorse, the adapter preload, the nav-state poll); the
  buffer retry for long results (a re-invoke would run the function twice; results here are short, a truncation is
  logged).
- C++ exports: `int sailfish_host_invoke(long long handle, const char *method, const char *args_json, char *out,
  int cap)` (`QMetaObject::invokeMethod` with `Q_RETURN_ARG(QVariant)` / `Q_ARG(QVariant, QString)`; returns the
  UTF-8 length, -1 bad args, -2 no such method, -3 dead handle) and `long long sailfish_host_page_handle(const char
  *page_id_or_null)` (resolves `window.mauiPageById(id)` through one cached expression, then `register_handle`).
- C#: imports in `QtHostNative.cs`; `IQtHostShim.Invoke(long, string, string)` and `PageHandle(string?)`;
  `QtHostRuntime.Invoke` with `CheckThread` and a buffer retry when `len >= cap` (closes the 8 KB/4 KB truncation
  for this path; apply the same retry to `Eval`/`GetProperty`/`ScreenInfo`/`PerfStats`, `QtHostRuntime.cs:313-319,
  497-503,562,636`). `QtHostPageRenderer.cs:40` `TopModelPageJs` → `TopModelPageHandle` (cached per
  `NativeTopPageId`, invalidated on `svc-nav-depth`). Replace the Evals at `QtHostPageRenderer.cs:872,1004,1166,
  1323,1612`, `Layout.cs:298,613,655`, `Interactions.cs:161,172,258,380`, `SailfishRemorse.cs:47,92`
  (`window.mauiPreloadAdapters` → handle of the shell, `objectName: "mauiShell"` added at `MauiShell.qml:13`).
  Tab drag (`QtHostInput.cs:385` → `Interactions.cs:161`) stops compiling a `QQmlExpression` per TouchUpdate.
- Delete `QmlPage.Call/Model/ById/ByIdOr` once no caller remains; `Eval` stays public for apps (`docs/native-interop.md`
  §2.3) but a grep test forbids it in `Platform/QtHost/QtHostPageRenderer*.cs`.
- QML: no signature changes; `__pushDialog` takes one JSON `{"src", "props"}` and splits inside
  (`MauiModelPage.qml:768`).
- Tests: `FakeShim.Invoke` dispatches on method (move the `applyMauiOps` parsing from `Eval`, add
  `setMauiScroll`, `__pushDialog` → `"ok"`, `__destroyHostsNotIn`); `FakeShim.Eval` throws for any renderer-path
  call; `ContractTests.InvokeContract` asserts `fake.Invokes` contains `("applyMauiOps", …)` and `fake.Evals` is
  empty after a page push.
- Accept: device `perf_stats.opsEvals == 0` and `evals` ≤ 5 per page push; `tools/sf matrix nav perf`.

### E4. Split `sailfish_host.cpp`; atomics; `g.error` mutex — PARTLY DONE 2026-10-03 (device: full matrix 29/29 PASS 13:16)
- Done: `HostState::app`, `receiver`, `shutdown` are `std::atomic`; `sailfish_host_post/wake/quit` load each once
  (acquire); teardown unpublishes the receiver (`exchange(nullptr)`) before `deleteLater`. The last error is behind
  a mutex: every one of the 48 `g.error = …` sites goes through `set_error`, the reads through `error_text()`
  (`sailfish_host_last_error` copies a snapshot). The engine-keyed static component caches in the background fill
  and the item matrix hold `QPointer`s to engine-parented components (the fill's was unparented and leaked; a
  destroyed engine left dangling pointers). Test `NativeContractTests.Cross_thread_state_is_atomic_and_the_error_text_is_locked`
  (the setter is the only direct write and does not call itself; a bulk rewrite had made it recursive, caught
  before any deploy).
- Not done: the file split into `host_*.cpp`. The file interleaves anonymous-namespace helpers with the C exports
  (two `namespace {` blocks, static helpers between `extern "C"` ones), so a split needs every shared helper moved
  into a named internal namespace behind `host_internal.h` first; a long compile-iterate job with no behaviour
  change, left for a quiet day, `removeEventFilter` on the window, the `measure_text` single-layout rewrite
  (a measurement change that needs its own device comparison).
- `Native/host_internal.h` (`HostState`, `g`, `log_line`, `fail_args`, `copy_out`, `require_handle`,
  `register_handle`, `parse_color`); `host_core.cpp` (`:92-535` state/logging/crash trap/InputFilter/PostEvent/
  wake, `:1101-1555` http cache/load/teardown/init/show/exec/quit/wake/post, `:1711-1769` callbacks/inject,
  `:3285-3313` inject_touch); `host_handles.cpp` (`:713-904` registry + JSON→QVariant, `:1929-2517` find/set/
  apply/get/geometry/generic props/matrix/layer effect, `:2646-2668` destroy, `:538-642` context props/eval_js/JS
  literal, `:1589-1645` push/pop/eval, E3's invoke); `host_text.cpp` (`:1879-1927`, `:2519-2644`);
  `host_surface.cpp` (`:2838-3283`, `:370-395`, `:3315-3333`); `host_diag.cpp` (`:270-368`, `:907-1000`,
  `:1647-1709`, `:1771-1877`, `:2675-2836`; clipboard/open_url may go to `host_essentials.cpp`).
  `tools/cmd/native-build.sh:52-57` compiles the `host_*.cpp` list.
- `HostState::shutdown` → `std::atomic<bool>`; `receiver`, `app` → atomic pointers; `wake/post/quit` load once
  (`acquire`) and touch nothing else; teardown stores `receiver = nullptr` (`release`) before `deleteLater`.
  `g.error` behind `std::mutex` with `set_error(std::string)`; all `g.error = …` sites use it (about 60).
  Counters read by `diag_stats/perf_stats/tick_count` → atomics. `removeEventFilter` (`:1241`) on the window;
  the static component caches in `apply_item_matrix`/`apply_background_fill` (`:2055-2060,2180-2185`) become
  `QPointer` members cleared in teardown; `measure_text` (`:2522-2538`) replaces the O(words²) `QTextLayout`
  candidate loop with one layout per line.
- Tests: `NativeLayoutTests` extended: `native-build.sh` lists every `host_*.cpp`; `grep -c "g.error =" Native/*.cpp`
  == 0; the `HostState` fields read in `wake/post/quit` are `std::atomic`.
- Accept: `tools/sf native-build` yields the same export list (`nm -D`, `native-build.sh:65`) plus
  `sailfish_host_invoke/page_handle/abi_version`; `tools/sf matrix navback lifecycle(features)` reports
  `lateCallbacks=0`, `postsRejected` unchanged.

### E5. Single-encoded events from all pages; `LibraryImport` + `UnmanagedCallersOnly` — PARTLY DONE 2026-10-03 (host: 352 tests; device: 11/11 legs PASS 14:47)
- Done, the defect part: the native drain read only `pageStack.currentPage`'s queue, so events queued on the model
  page under a Silica dialog (the dialog hop forwards into it) or on a parked page waited until that page was on
  top again, then arrived as a stale burst. `MauiShell.__mauiDrainAll()` drains every model page in `mauiPages` and
  the app queue in one call; `drain_qml_events` calls it (the inline top-page form stays as the fallback). Native
  rebuilt (no export changed). Test `EventDrainContractTests`.
- Not done: `LibraryImport`/`UnmanagedCallersOnly` and the single-encoded envelope. A Release build of the core has no
  IL2026/IL3050 warning today (the plan's accept already holds), so the rewrite of every import and callback would
  change no behaviour; the per-page payload strings stay double-encoded.
- QML: one window-level `property var __mauiEvents: []` in `MauiShell.qml` (replacing `__mauiAppQueue`, `:135-139`);
  `MauiModelPage.mauiNotify` (`:167-172`) pushes `{page, name, payload: object}` there (delete `__mauiQueue`,
  `__mauiDrain`, the dialog hop at `:792-794`); emitters pass objects (A1's `emit` helper does the wrapping);
  `MauiShell.__mauiDrain()` returns `JSON.stringify` of the array once.
- C++: `drain_qml_events` (`:647-700`) calls `__mauiDrain` on `g.root` through `QMetaObject::invokeMethod` (no
  `QQmlExpression`), parses once; callback becomes `void (*sfhost_event_fn)(const char *page, const char *name,
  const char *payload_json, void *user_data)`; the C++ `svc-app-state` emitters are gone (A3).
- C#: `QtHostNative.cs` → `[LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]` for every function;
  callbacks as `delegate* unmanaged[Cdecl]<…>`; `[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]`
  static `OnTick`, `OnPointer`, `OnKey`, `OnEvent`, `OnPost`, `OnFrame`, `OnSurfaceTouch` in `QtHostRuntime`/
  `QtHostSurface` with the existing try/catch bodies; delete the delegate types and rooted fields
  (`QtHostRuntime.cs:51-57,357,367`, `QtHostSurface.cs:23,36`); `QmlEvent(string page, string name, JsonElement
  payload)` parsed once (keep an `[Obsolete]` `Action<string,string>` adapter for `docs/native-interop.md` users);
  `SailfishMauiApplication.cs:194-211` routes by page; `QtHostServices.Dispatch` (`QtHostServices.cs:54-81`) and
  the `Events.cs` handlers take `JsonElement` (grep `JsonDocument.Parse(payload)`); clipboard/open_url go through
  `QtHostRuntime` with `TestShim` branches (`Essentials.cs:28,44,343`). `AllowUnsafeBlocks` for
  `Linux.SailfishOS.csproj`.
- Tests: `FakeShim.RaiseEvent(page, name, object payload)`; `ContractTests.EventEnvelope`: every `mauiNotify("…"`
  literal in the QML is handled by `HandleNativeEvent` or a `QtHostServices.Subscribe`; a renderer test raises an
  event for a parked page id and asserts that page's handler ran while another page is top;
  `QtHostNative.cs` contains no `DllImport` and no `delegate ` declarations; every `UnmanagedCallersOnly` body
  contains `try`.
- Accept: a `rendered` event from `mp1` arrives while a `DatePicker` dialog is current (`tools/sf matrix popup
  input`); `grep -c JSON.stringify qml/*.qml` drops to the drain only; `dotnet publish -c Release -r linux-arm64`
  shows zero `IL2026/IL3050` warnings for `QtHostNative`.

---

## Open defects not scheduled above (fix when touching the file)

- `QtHostPageRenderer.cs:747-755,1385`: on a partial shim rejection `PushBatch` returns `true`, so `ApplyUpdates`
  reports the batch as pushed and `reconcileDiffPushes` grows every reconcile; return `false` or the applied count
  (C2/C5 touch this code).
- `QtHostPageRenderer.cs:808`: `_createDeferred` cleared before the reset-hold early return (`:833-836`).
- `QtHostPageRenderer.Events.cs:84`: `window-geometry` reconciles while a dialog/flyout is open.
- `SailfishControlHandlers.cs:80-93`: `_arrangedSize` not reset on disconnect.
- `QtHostImages.cs:135-142`: a second Image sharing a URL returns false from `ReportNaturalSize`.
- `SailfishMeasure.cs:202`: `Indicator` width formula `(2*count+1)*size` looks mis-simplified; check on the device.
- `SailfishSynchronizationContext.Send` blocks without a timeout after the loop ended
  (`SailfishDispatcherProvider.cs:107-115`); `SailfishDispatcher.Dispatch` enqueues after shutdown (`:51-56`);
  timers use wall-clock `DateTime.UtcNow` (`:170,177,197`).
- `tools/lib/sf-lib.sh:116` `SF_RPM_ARCH` ignores `SF_RID` (D3).
- `tools/sf run --wait` reports only whether the app exited, not its exit code; a failed start (exit 2) reads as
  "exited". Surface the code (sf-run-remote.sh `wait $pid; echo EXIT=$?`).
- `tools/sf matrix`: a leg can start while the previous leg's app instance is still shutting down and then read that
  instance's log (2026-10-03 09:02: `collection` after `visual` → NO-VERDICT with the visual verdict in its log). Wait
  for the previous `harbour-sample` to exit (or check its PID) before launching the next leg.

## Metrics to report after each phase

| Metric | Today (1cfd712) | Target |
| --- | --- | --- |
| files using `QtHostPageRenderer.Current` | 19 | 0 |
| largest file in `Platform/QtHost` | `QtHostPageRenderer.cs` 1 907 lines | < 800 |
| `QtHostPageRenderer` partials / lines / fields | 10 / 6 159 / ~165 | 4 / < 2 000 / < 60 |
| `MauiModelPage.qml` | 1 044 lines | < 400 |
| `sailfish_host.cpp` | 3 336 lines, 1 TU | 5 TUs, largest < 1 000 |
| hand-rolled JSON serializers (C#) | 4 | 1 |
| `svc-*` / `maui*` literals outside the constants | all | 0 |
| host tests | 266 | ≥ 300 |
| public types in Platform/Hosting/LifecycleEvents | 64 | about 25 |
| `treeFixups`, `timerWithWork`, `NavResyncs`, `BridgeFailed` on the matrix | 0 | 0 |
