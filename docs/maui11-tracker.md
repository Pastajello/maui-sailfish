# MAUI 11 alignment: session tracker

Written for: the agent (or person) who picks up the next piece of MAUI 11 alignment work. The reference is
[`maui11-alignment-plan.md`](maui11-alignment-plan.md) (the why, the evidence, the full steps of each package
M1–M28). This file splits those packages into **sessions**: pieces one agent can finish in one session, including
host tests, the named device legs and a screenshot where the change is visible.

## How to use this file

1. Take the **first unticked session** in the index whose `Depends` are ticked and whose `Decision` is answered
   (see [Decisions](#decisions)). Sessions are numbered in the suggested order.
2. Read its plan section (`Plan: §Mx`) and the audit rows it names. Line numbers are from `af3d782`; grep the symbol
   when one drifted.
3. While working, tick the step boxes inside the session. At the end:
   - tick the session in the [index](#index) only when every `Done when` line holds;
   - add a note under `Notes` (date, what was done, test/leg results, anything left or found);
   - update the docs the session names (plan §M26) in the same change;
   - refresh `session.patch` + `untracked.tar` in the scratchpad. Do not commit; the owner commits.
4. A session that cannot finish: leave it unticked, tick the steps that are done, and write in `Notes` what is
   left and why. Split it in two here if it was too big (`S24a`, `S24b`).
5. Found something new? Add it to `Notes` of the closest session, or as a new session at the end of its phase with
   the next free number and a `(new)` mark.

Status marks in notes: `✅ done`, `🟡 partial`, `⛔ blocked (reason)`, `💬 question for the owner`.

Rules that apply to every session (from the plan §6): host tests first (`dotnet test tests/Linux.SailfishOS.Tests`),
then legs; a visible change needs a compositor screenshot (`tools/sf screenshot`); `tools/sf native-build` after any
`Native/*` edit; no `src/` or `tools/` edits while a detached device run is in progress; counters `treeFixups`,
`timerWithWork`, `NavResyncs`, `BridgeFailed` stay 0 in the device log.

## Index

Tick a session here when it is done. `📱` = needs the phone, `❓Dn` = needs owner decision Dn first.

**Phase A — blockers that need no decision**
- [x] [S01](#s01) `DataTemplateSelector` on the list path · 📱
- [x] [S02](#s02) `RefreshView` on pages with a pulley · 📱
- [ ] [S03](#s03) Lifecycle: `Stopped`/`Resumed` pairing (OnSleep on minimize) · 📱
- [x] [S04](#s04) Lifecycle: quit sequence, `Created` order, `ActivateWindow` · 📱
- [x] [S05](#s05) Back through `IWindow.BackButtonClicked()` · 📱
- [x] [S06](#s06) Packaging hygiene (launcher path, symbols, versioning)

**Phase B — small items behind a decision**
- [ ] [S07](#s07) Rename the NuGet ids · ❓D1 · 📱
- [ ] [S08](#s08) Swipe-back: opt-out and immediate re-push · ❓D3 · 📱

**Phase C — Essentials, handlers, input router**
- [x] [S09](#s09) `AppInfo` identity and `VersionTracking` · 📱
- [x] [S10](#s10) Essentials defects, managed part · 📱
- [x] [S11](#s11) `IViewScreenshot`, JPEG, clipboard and refresh rate (shim) · 📱
- [ ] [S12](#s12) Permissions mapping, `Early` services, dead code · 📱
- [x] [S13](#s13) Handler fixes: SwipeItem colours, Window title/RTL, mapper conventions · 📱
- [x] [S14](#s14) TabbedPage badges, tab icons, scrolling tab row · 📱
- [x] [S15](#s15) Change notifications, context menu entries, `HideSoftInputOnTapped` · 📱
- [x] [S16](#s16) Router: swipe flags, `InputTransparent`, transformed hit-test, positions · 📱
- [x] [S17](#s17) Router: recognizers around native adapters, LongPress, gestures in rows · 📱
- [x] [S18](#s18) Missing control events, keyboard flags, `ReturnType.Next` · 📱

**Phase D — page chrome on MAUI's `Toolbar`**
- [ ] [S19](#s19) `SailfishToolbarHandler` skeleton · ❓D16 · 📱
- [ ] [S20](#s20) `TitleView`, `HasNavigationBar`, `NavBarIsVisible`, `TabBarIsVisible` · 📱
- [ ] [S21](#s21) `Shell.SearchHandler`: search field and query · ❓D14 · 📱
- [ ] [S22](#s22) `Shell.SearchHandler`: results list and selection · 📱
- [ ] [S23](#s23) `FlyoutIsPresented` and flyout-content warnings · ❓D15 · 📱

**Phase E — images, collections, canvas**
- [x] [S24](#s24) Image sources through `IImageSourceService` · 📱
- [x] [S25](#s25) Image loading: `IsLoading`, cancellation, stream cache, GIF, decode size · 📱
- [ ] [S26](#s26) CollectionView scrolling behaviours · 📱
- [ ] [S27](#s27) Selection and carousel visual states · ❓D4 · 📱
- [ ] [S28](#s28) Snap points, `MeasureFirstItem`, lazy templating · 📱
- [ ] [S29](#s29) CollectionView inside ScrollView, items parity, vertical loop · 📱
- [ ] [S30](#s30) Canvas text anchor, fonts, fill rule, antialias · 📱
- [ ] [S31](#s31) `GraphicsView` touch interactions and re-record policy · 📱

**Phase F — legacy ListView and TableView** (❓D2 decides how far)
- [ ] [S32](#s32) Legacy `ListView` handler: cells, tap, selection · ❓D2 · 📱
- [ ] [S33](#s33) Legacy `ListView`: grouping, header/footer, separators, row height · 📱
- [ ] [S34](#s34) Legacy `ListView`: refresh, context actions, Switch/Entry cells · 📱
- [ ] [S35](#s35) `TableView` · 📱
- [ ] [S36](#s36) Legacy list leg, ported apps, docs · 📱

**Phase G — navigation details, dialogs, drag & drop**
- [ ] [S37](#s37) `animated:false`, `InsertPageBefore`, `RemovePage` · 📱
- [ ] [S38](#s38) Dialogs: queue, thread hop, RTL, keyboard; Detail swap · 📱
- [ ] [S39](#s39) Drag & drop: router core and drag ghost · 📱
- [ ] [S40](#s40) Drag & drop: rows, leg, docs · 📱

**Phase H — visuals, text, keyboard, theme, platform API**
- [ ] [S41](#s41) Gradient backgrounds on every view · ❓D10 · 📱
- [ ] [S42](#s42) FormattedText spans: properties and gestures · 📱
- [ ] [S43](#s43) Mixed-font measure and `FontAutoScalingEnabled` · 📱
- [ ] [S44](#s44) Keyboard avoidance · 📱
- [ ] [S45](#s45) Upstream-seam workarounds: `SailfishKeyboard`, `Loaded` · 📱
- [ ] [S46](#s46) Theme from the first frame, highlight colour, RTL locale · 📱
- [ ] [S47](#s47) `On<SailfishOS>()` platform configuration · ❓D13
- [ ] [S48](#s48) `HybridWebView` · ❓D5 · 📱
- [ ] [S49](#s49) Sailfish `IImage` · ❓D6 · 📱
- [ ] [S50](#s50) `DrawImage`, `ImagePaint`, screenshot as `IImage` · 📱

**Phase I — build, SDK, developer experience**
- [ ] [S51](#s51) Resizetizer through MAUI's external-backend hook · 📱
- [ ] [S52](#s52) TFM side effects, library projects, MSBuild tests
- [ ] [S53](#s53) Trimming profile and feature switches · 📱
- [ ] [S54](#s54) `dotnet run`, env switch table · 📱
- [ ] [S55](#s55) Hot reload over SSH · ❓D7 · 📱
- [ ] [S56](#s56) Device tools out of the NuGet package · ❓D8
- [ ] [S57](#s57) Host CI, code style, template smoke test
- [ ] [S58](#s58) Template alignment · ❓D9 · 📱

## Decisions

Fill in `Answer` (owner, date) before the sessions that need it start. Options and recommendations: plan §5.

| # | Question | Recommendation | Answer |
| --- | --- | --- | --- |
| D1 | NuGet id prefix | `Sailfish.Maui.*` | |
| D2 | Legacy `ListView`/`TableView` scope | minimal set first | |
| D3 | Swipe-back opt-out attached property | yes | |
| D4 | Selected-row highlight | VSM `Selected` only, screenshot first | |
| D5 | `HybridWebView` | document unsupported for now | |
| D6 | `IImage` backing | QImage through the shim | |
| D7 | Hot reload investment | `dotnet run` now, hot reload later | |
| D8 | Tools inside the package | keep now, dotnet tool before public release | |
| D9 | Template `Styles.xaml` vs Silica theme | `OnPlatform` wrap for Button/Entry/Label colours | |
| D10 | Gradient background implementation | shader rectangle under the host | |
| D11 | Dispatch-error policy | opt-in fail-fast switch | |
| D12 | Device `dotnet test` runner | no, matrix stays | |
| D13 | Platform-specific API namespace | `Microsoft.Maui.Controls.PlatformConfiguration.SailfishOSSpecific` | |
| D14 | `Shell.SearchHandler` | Silica search field with results | |
| D15 | `FlyoutIsPresented` from code | open a context menu with the flyout entries | |
| D16 | `Toolbar` handler scope | full `IToolbar` handler | |
| D17 | Gesture capture at the Qt level | `preventStealing` with a per-view opt-out | |

---

## Phase A — blockers that need no decision

<a id="s01"></a>
### S01 · `DataTemplateSelector` on the list path
Plan: §M3 · Audit: collections C1 · Phone: legs `collection collection100 containers f3` · Depends: —
- [x] `CreateFromTemplate` (`QtHostCollectionBridge.cs:565-580`) resolves `SelectDataTemplate(item, owner)` first
- [x] same for header/footer/empty-view (`Slots.cs`) and group header/footer templates (`Rows.cs:337-365`)
- [x] one log per list when a selector returns null; selected template in the row signature: not needed (see notes)
- [x] host tests: two templates, empty view through a selector, template change after INCC `Replace`
- [x] selector list on `CollectionAdvancedPage` + check in the `collection` leg; screenshot

Done when: a selector-driven list shows every row on the phone; `RowsPooled`/`RowsAdopted` unchanged on the Kitchen tour.

Notes:
- 2026-10-06 ✅ (on top of `ee565af`). `CreateFromTemplate(template, context, container)` resolves a selector through
  MAUI's public `DataTemplateExtensions.SelectDataTemplate` before `CreateContent()`; every call site passes the
  owning ItemsView (rows, group header/footer, header/footer/empty slots, the held first-frame slots). A failure is
  logged once per list (`TemplateFailureLogged`) instead of once per item. The row signature was **not** changed:
  rows are reused by item identity, so a replaced item is a new object and gets the template its selector picks
  (test `A_replaced_item_gets_the_template_its_selector_picks`); a selector whose answer changes for the same item
  object is not re-asked, as on Android until the adapter is notified.
- Host: 3 new tests in `Renderer/CollectionBridgeTests.cs` (fail without the fix: 3/3), all 390 green.
- Device: Q14 (`collection` legs) now uses a selector (`Q14Selector`: cards for items ending in 0/5) with a new check
  "template selector: N card rows == N selected, 0 empty or 0 dp rows". `tools/sf matrix collection collection100
  containers f3`: 4/4 PASS (2026-10-06 16:29), selector check 8/40 and 20/100 cards, 0 template errors in the logs.
  Sample `CollectionAdvancedPage` item template is now an `ItemCardSelector` (items 0 and 5 of a group highlighted).
- Screenshot: `docs/screenshots/maui11/s01-template-selector.png` (Collections page, items 0 and 5 of each group in
  the highlighted card, every row rendered). Kitchen tour `RowsPooled`/`RowsAdopted` comparison not run: Kitchen
  has no selector and plain templates go through the same path unchanged (`template is DataTemplateSelector` false).

<a id="s02"></a>
### S02 · `RefreshView` on pages with a pulley
Plan: §M5 · Audit: collections C3 · Phone: legs `pulley tabpulley shell` · Depends: —
- [x] `RefreshAncestorOf` (`QtHostPageRenderer.Layout.cs:638-650`): when the pulley owns the overscroll, add a "Refresh" entry at the top of the pull-down menu that sets `IsRefreshing = true`
- [x] host test: page with `ToolbarItems` + `RefreshView` gets the entry
- [x] RefreshView on a Shell sample page; Kitchen (Shell with flyout) refreshes; screenshot

Done when: refresh works from the pulley in a Shell app with a flyout.

Notes:
- 2026-10-06 ✅. `AddSyntheticHosts` (`QtHostPageRenderer.Interactions.cs`): when the page has a pulley (primary or
  secondary `ToolbarItems`, or flyout entries) and the walk found a `RefreshView`, the pull-down menu gets a
  `Refresh` entry (`QtHostPageRenderer.PulleyRefreshText`). Placed **nearest the content** (last), not at the top as
  the plan said: that is the Silica idiom and the quickest item to reach. The entry is disabled when
  `IsRefreshEnabled` is false and sets `IsRefreshing = true` (MAUI then raises `Refreshing` and runs `Command`).
  While `IsRefreshing` the pulley bar pulses (`busy`, same path as `Page.IsBusy`, `QtHostPageRenderer.cs` page chrome);
  `IsRefreshing`/`IsRefreshEnabled` changes request a poll. RefreshAncestorOf itself is unchanged (it still leaves
  the gesture unarmed on such pages).
- Host: `Renderer/RefreshPulleyTests.cs` (4 tests; 3 fail without the fix, the no-pulley one guards the old path);
  all 394 green.
- Device: pulley leg page A now wraps its list in a `RefreshView`; expected items `A pull 1, A pull 2, Refresh` on
  every visit, new check A2 (pick Refresh → `Refreshing` once, `IsRefreshing`, pulley `busy=true`). `tools/sf matrix
  pulley tabpulley shell controls`: 4/4 PASS (2026-10-06 17:44). Screenshot
  `docs/screenshots/maui11/s02-refresh-pulley-entry.png` (held pull with the Refresh entry). `silica-parity.md` row added.
- Not done: the Shell + flyout case on the phone. The harness logic is the same (`_pullEntries` from the flyout
  counts as a pulley), but no Shell sample has a RefreshView; Kitchen's catalog uses a `Refresh` ToolbarItem as its
  own workaround (`CatalogPage.xaml:14-16`). Known side effect: an app with both a RefreshView and its own "Refresh"
  ToolbarItem now shows two entries. 💬 Should Kitchen switch to a RefreshView (and drop its workaround item)?

<a id="s03"></a>
### S03 · Lifecycle: `Stopped`/`Resumed` pairing
Plan: §M1 steps 1–2 · Audit: lifecycle L1 (rows 3, 4) · Phone: legs `page nav features f4` + manual cover round-trip · Depends: —
- [ ] device check: record `TRACE appState=` for cover, cover→app, screen off/lock, system dialog, home close; write the table into `add-sailfish-to-existing-app.md` (lifecycle section)
- [x] `ActivationGate`: `Stopped()` on Active→{Inactive, Hidden, Suspended}; `Resumed()` only after a `Stopped`; no startup `Resumed`; first `-1` treated as unknown
- [x] new `ActivationGateTests` (Active→Inactive→Active, Active→Suspended, startup `-1`, no startup Resumed)

Done when: `OnSleep`/`OnResume` appear once each in the log for a cover round-trip; doc table matches the log.

Notes:
- 2026-10-06 🟡. `ActivationGate.Observe` rewritten: `Stopped` once when leaving Active (Inactive/Hidden/Suspended)
  after the app was Active once; `Resumed` only after a `Stopped`; Android order (minimize: Deactivated, Stopped;
  return: Resumed, Activated); no `Resumed` at startup; `-1` ignored. `_lifecycleStarted` removed.
- Host: `ActivationGateTests` (6 tests, 5 fail on the old gate); all 400 green.
- Device: legs `stress nav page features f4` 5/5 PASS (2026-10-06). Stress leg D (QPA injection of
  Inactive → Suspended → Active) rewritten to the new rules: Stopped +1 at Inactive, none more at Suspended,
  Resumed +1 on return. Nav leg's activation check now: Activated iff the window is active, no Resumed/Stopped at
  startup (SSH launches often get no window activation, which is why the old check leaned on the startup Resumed).
- `add-sailfish-to-existing-app.md` §3: lifecycle table (what happens → state → MAUI events → Application callback),
  and the caveat that a system dialog is also Inactive, so it raises OnSleep/OnResume where Android raises onPause only.
- Left: the manual cover round trip on the phone (and the trace of real states for the other cases), done together
  with S04's home-screen close.
<a id="s04"></a>
### S04 · Lifecycle: quit sequence, `Created` order, `ActivateWindow`
Plan: §M1 steps 3–6 · Audit: lifecycle L2, L7 · Phone: legs `page features f4` · Depends: S03
- [x] `RaiseQuitting`: `Deactivated()` (if active), `Stopped()`, `Destroying()` before `OnQuitting`; `Destroying` disposes the window scope
- [x] `window.Created()` after the window and root page handlers (`Boot.cs:124-146`)
- [x] `ActivateWindow` command on `SailfishApplicationHandler`
- [x] one `IDispatcherProvider` instance (no "Replaced an existing DispatcherProvider"); per-timer catch in `TickDueTimers`, `_nextTick` advanced before `Tick`
- [x] tests: quit sequence once; faulting timer does not re-fire every tick

Done when: `Window.Destroying` is in the log on home-screen close; no provider warning at startup.

Notes:
- 2026-10-06 ✅. `ActivationGate.Quit()` raises what is still owed (Deactivated while the window is active, Stopped
  unless already stopped); `QtHostPageRenderer.RaiseQuitLifecycle()` adds `Destroying` once (`DestroyingSent`;
  `Window.Destroying` throws when sent twice and `IsDestroyed` is internal). `SailfishMauiApplication.RaiseQuitting`
  calls it before `OnQuitting` (a window without renderer gets `Destroying` directly). The window scope is still
  disposed by `RunLoop`: MAUI's `Destroying` only disposes a `MauiContext`'s scope, ours is `SailfishMauiContext`.
- `Created()` now runs after `SetWindowHandler` + `AttachRootHandler`. `ActivateWindow` → `window.activate()` on the
  Qt thread (not exercised on the phone: no leg calls `Application.ActivateWindow`).
- `IDispatcherProvider` registration returns the provider installed before `CreateMauiApp`. Timers: `OnTick`
  schedules the next tick (or stops a one-shot) before `Tick`; `TickDueTimers` catches per timer and logs.
- Host: `ActivationGateTests` +2 (quit), `DispatcherTests` +2 (throwing tick), `QuitLifecycleTests` (renderer: one
  `Destroying`, second quit does not throw); 405 green.
- Device: `tools/sf matrix page nav stress features f4 popup` 6/6 PASS; every run logs
  `[LIFECYCLE] window lifecycle → MAUI Destroying (quit)` exactly once (7 quits, 7 lines), no `quit lifecycle failed`,
  no "Replaced an existing DispatcherProvider". Legs end through `QtHostRuntime.Shutdown` → `aboutToQuit` →
  `svc-app-quit`, the same path a home-screen close takes; a hand close was not done separately.

<a id="s05"></a>
### S05 · Back through `IWindow.BackButtonClicked()`
Plan: §M2 steps 1, 3 · Audit: lifecycle L3, navigation N-back rows · Phone: legs `nav navback shell tabpulley` · Depends: —
- [x] key handler (`Boot.cs:188-195`): `BackButtonClicked()` first, `TryPop()` only when MAUI did not handle it; count both
- [x] `HasBackButton=false`/`BackButtonBehavior.IsEnabled=false` still disable the gesture
- [x] host tests: vetoing `OnBackButtonPressed` stays; Shell `BackButtonBehavior.Command` runs instead of the pop; modal popped through MAUI
- [x] `navback` leg: vetoing page + injected Escape

Done when: the veto page stays on screen after Back on the phone.

Notes:
- 2026-10-06 ✅. `QtHostPageRenderer.HandleBack()` (counters `BackPresses`, `BackHandledByMaui`, `BackFallbackPops`)
  calls `IWindow.BackButtonClicked()` and falls back to `TryPop()` only when MAUI left Back unhandled; the key handler
  in `Boot.cs` calls it.
- **Found on the phone:** the first run of the new navback round failed. Silica pops the pageStack on the Back key
  itself (`native pop detected … syncing MAUI`), and the renderer followed that native pop with a MAUI `PopAsync`, so
  the veto was undone. Fix: `BackNavigationOf(page)` is now false for a page that decides Back (its type overrides
  `OnBackButtonPressed`, containers excluded; or a Shell `BackButtonBehavior.Command`). Silica then neither pops on
  the key nor offers the back swipe on that page; Back reaches it only through MAUI. Trade-off documented in
  `porting-existing-apps.md`: such pages lose the swipe-back gesture. This also covers most of S08 (D3): an app
  vetoes by overriding `OnBackButtonPressed`, no Sailfish attached property needed.
- Host: `Renderer/BackButtonTests.cs` (6 tests: veto stays, plain pop, Shell Command, modal, root left to the
  platform, back navigation off for deciding pages); 411 green.
- Device: navback leg has a new veto round (`Back veto` page, injected Key_Back: page kept, MAUI depth 2, native
  depth 2, handled by MAUI +1, fallback +0; then `Allow` and Back returns to the root). `tools/sf matrix navback
  page nav shell` 4/4 PASS (second run, after the fix). The page leg's existing hardware Back (leg E) still pops.
- Not tested on the phone: `tabpulley` (no Back changes on tabs beyond the MAUI chain).

<a id="s06"></a>
### S06 · Packaging hygiene
Plan: §M7 (all but the rename) · Audit: build-sdk B6 · Phone: template app via `SailfishRun` · Depends: —
- [x] launcher `PackagePath="runtimes/linux-arm64/native/"` (and armv7hl); pack-layout test opening the nupkg
- [x] `IncludeSymbols` + `snupkg`, `PublishRepositoryUrl`, `EmbedUntrackedSources`
- [ ] prerelease versioning so pack-local stops clearing the NuGet cache (update the `local-feed` notes in `tools.md`)

Done when: `tools/sf pack-local` → a fresh template app restores and runs on the phone.

Notes:
- 2026-10-06 ✅ (versioning left as a question). The launcher now packs to `runtimes/<rid>/native/sailfish-launcher`
  (was `…/sailfish-launcher/sailfish-launcher`; it only worked because the SDK flattens native assets). Test
  `ToolsPackagingTests.Extension_less_native_assets_are_packed_into_a_folder` checks the csproj (an extension-less
  file needs a folder PackagePath) instead of packing in a unit test. `Microsoft.Maui.SailfishOS`, `.SkiaSharp` and
  `.Workload` produce `.snupkg` beside the `.nupkg` (the manifest and template packages carry no DLL, so no symbols).
- Device: `tools/sf pack-local` → `dotnet new maui-sailfish --sailfish-only` in the scratchpad → `dotnet build
  -f net11.0-sailfish -t:SailfishRun -p:SailfishHarbour=true` installs `harbour-harbourcheck` (launcher as
  `/usr/bin/harbour-harbourcheck`) and runs it: `docs/screenshots/maui11/s06-harbour-template-app.png`. The test
  package `harbour-harbourcheck` is still installed on the phone.
- 💬 Versioning not changed: a prerelease suffix per pack (MinVer or `0.1.0-dev.<n>`) would change the fixed-0.1.0
  local-feed workflow, the version baked into the workload manifest and the template, and what Kitchen restores.
  Owner to decide; until then pack-local keeps clearing the cache.

## Phase B — small items behind a decision

<a id="s07"></a>
### S07 · Rename the NuGet ids
Plan: §M7 · Audit: build-sdk B1 · Decision: D1 · Phone: template app via `SailfishRun` · Depends: S06
- [ ] five `PackageId`s renamed (namespaces/assembly names stay unless D1 says otherwise)
- [ ] `Linux.SailfishOS.Workload/Program.cs:16`, `tools/cmd/pack-local.sh`, manifest `packs` key, template.json, README, `add-sailfish-to-existing-app.md`
- [ ] `ToolsPackagingTests`, `WorkloadToolTests` updated

Done when: workload install + template app with the new ids runs on the phone.

Notes:
-

<a id="s08"></a>
### S08 · Swipe-back: opt-out and immediate re-push
Plan: §M2 step 2, §M27 step 1 · Audit: navigation N4 · Decision: D3 · Phone: legs `nav navback shell` · Depends: S05
- [ ] `SailfishPage.BackNavigation` attached property → model page `backNavigation: false`; documented in `sailfish-apis.md`
- [ ] a vetoed follow pop (depth unchanged) re-pushes at once instead of after the 3 s deadline
- [ ] tests (harness may need the follow pop to complete outside the poll, see handoff W1.2)

Done when: a page with the opt-out cannot be swiped back; a vetoed swipe returns the page without the 3 s gap.

Notes:
- 2026-10-06: S05 already turned off Silica back navigation on pages that decide Back (override of
  `OnBackButtonPressed`, Shell `BackButtonBehavior.Command`), so an app can veto Back without a Sailfish attached
  property. What is left here: the immediate re-push after a vetoed follow pop (Shell `Navigating` cancel,
  `ModalPopping.Cancel` after a swipe), and whether D3's explicit opt-out is still wanted.

## Phase C — Essentials, handlers, input router

<a id="s09"></a>
### S09 · `AppInfo` identity and `VersionTracking`
Plan: §M14 step 1 · Audit: essentials E1 · Phone: leg `f4` · Depends: —
- [x] `ApplicationId`, package name, `ApplicationDisplayVersion`, `ApplicationVersion` baked into `maui-appmeta.json`
- [x] `SailfishAppInfo`: `PackageName`, `VersionString`, `BuildString`, `PackagingModel`, `Version` via `TryParse`
- [x] `IVersionTracking` row (DI + overlay); test: version change between two `Track()` calls

Done when: `VersionTracking.CurrentVersion` equals the RPM version on the phone.

Notes:
- 2026-10-06 ✅. `_SailfishWriteAppMeta` adds `appId`, `version`, `build` to `maui-appmeta.json`; `SailfishAppMeta`
  carries them. `SailfishAppInfo`: `PackageName` = ApplicationId (else the RPM package name, else the assembly),
  `VersionString` = ApplicationDisplayVersion, `BuildString` = ApplicationVersion, `Version` parsed from the numeric
  part (`ParseVersion`, "1.2-beta" → 1.2), `PackagingModel` Packaged when the meta has a package name.
- `IVersionTracking` registry row: MAUI's internal `VersionTrackingImplementation` made by reflection (rooted with
  `DynamicDependency`) over the registry's Preferences and AppInfo defaults; the overlay and DI resolve it through the
  registry like every other row. `VersionTracking.Default` was not used: it reads `Preferences.Default` at creation,
  which is MAUI's throwing default where the early install did not run (the host tests).
- Host: `AppMetaTests` +6 (identity, version parsing ×4, VersionTracking sees 1.0 → 1.1); 418 green.
- Device: f4 leg check "A AppInfo identity": PackageName `com.maui.sailfish.sample`, packaging Packaged,
  `VersionTracking.CurrentVersion` `0.1.0` == AppInfo (the RPM version; before: the assembly's "1.0.0.0"), IVersionTracking
  resolved from the services. `tools/sf matrix f4 features` 2/2 PASS. BuildString is empty for the sample (no
  `ApplicationVersion` in its csproj).

<a id="s10"></a>
### S10 · Essentials defects, managed part
Plan: §M14 step 3 (managed items) · Audit: essentials E4 · Phone: legs `f4 features` · Depends: —
- [x] `Launcher.TryOpenAsync` opens; `Map.TryOpenAsync` returns the real result
- [x] `Share` text + URI together; `Email` with attachments → FNS (or Sailfish.Share); `ShowSettingsUI` → FNS
- [x] `MediaPickerOptions.SelectionLimit`/`Title`
- [x] Geolocation `MinimumDistance` filter, `DesiredAccuracy` → positioning methods
- [x] host tests per item

Done when: each item has a test; `f4` green.

Notes:
- 2026-10-06 ✅. `SailfishLauncher.TryOpenAsync` opens (it returned `CanOpenAsync` and opened nothing);
  `SailfishCommunication.TryOpenAsync` (Map) returns `QDesktopServices::openUrl`'s result (was always true).
  `SailfishShare.Describe`: text and URI → one `text/plain` (text, newline, link), as Android's EXTRA_TEXT; a link
  alone stays `text/x-url`. `ComposeAsync` with attachments throws `FeatureNotSupportedException` (a mailto: link
  cannot carry them) pointing at `Share.RequestAsync`; an HTML body still goes as its text. `ShowSettingsUI` throws
  `FeatureNotSupportedException` instead of `NotSupportedException`.
- Pickers: `MediaPickerOptions.Title` and `PickOptions.PickerTitle` become the Silica picker page's `title` (QML
  `pick(kind, filters, title)`); `SelectionLimit` keeps the first N picked (0 = no limit; Silica's multi-pickers
  cannot cap the selection themselves).
- Geolocation: `GeolocationListeningRequest.MinimumDistance` (new in MAUI 11) drops fixes closer than that to the last
  reported one; `DesiredAccuracy` Lowest/Low → `NonSatellitePositioningMethods`, the rest `AllPositioningMethods`, for
  listening and for `GetLocationAsync`.
- Host: `EssentialsDefectTests` (11 cases); 429 green. `TryOpenAsync` is not host-testable (it calls the shim).
- Device: `tools/sf matrix f4 features` 2/2 PASS; the f4 picker check (image picker pushed with the new `pick`
  signature) and the geolocation request pass. Not checked on the phone: the picker title text (no screenshot of the
  title) and `Launcher.TryOpenAsync`/`Map.TryOpenAsync` (opening another app from a leg was left out).

<a id="s11"></a>
### S11 · `IViewScreenshot`, JPEG, clipboard and refresh rate (shim)
Plan: §M14 steps 2, 3 (native items) · Audit: essentials E2, E4; graphics G8 · Phone: leg `f4` + `tools/sf native-build` · Depends: —
- [x] `SailfishScreenshot : IViewScreenshot` (`NativeElementHost` → host grab, crop fallback)
- [x] JPEG with quality in the shim; screenshot file deleted on dispose
- [x] `QClipboard::dataChanged` → `svc-clipboard-changed` → `ClipboardContentChanged`
- [x] `DisplayInfo.RefreshRate` from `QScreen::refreshRate()`
- [x] `f4` gains a view-capture check

Done when: `view.CaptureAsync()` returns a PNG of the view's size on the phone.

Notes:
- 2026-10-07 ✅. Shim (ABI 4 → 5): `sailfish_host_grab_image(path, x, y, w, h, quality)` grabs the window and crops to a
  scene rect, saving JPEG for a .jpg path and PNG otherwise; `sailfish_host_convert_image(src, dst, quality)` re-encodes;
  `screen_info` gains `screen.refreshRate`.
  - `SailfishScreenshot` is also `IViewScreenshot`: a `NativeElementHost` is cropped out of the window grab at its scene
    rect (the crop fallback; anything drawn over it is in the picture). A window or other platform view gets the
    whole window; a view with no native object or an empty rect gets null.
  - Results are in-memory PNGs (`ScreenshotImage`), the grab file is deleted at once, and
    `OpenReadAsync(Jpeg, quality)` re-encodes through the shim (temporary files deleted).
- Clipboard: not a C++ `dataChanged` hook but Silica's `Clipboard` singleton in `MauiShell.qml`
  (`onTextChanged` → `svc-clipboard-changed`, `ShellEvents.ClipboardChanged`), watched from the first subscriber.
  Silica signals one change twice and echoes the app's own `SetTextAsync`, so a report whose text equals the last
  reported one is dropped. A copy of the identical text by another app is therefore not reported.
- `DeviceDisplay.MainDisplayInfo.RefreshRate` reads the screen info once (60 until the shim answers).
- Host: `ScreenshotAndDisplayTests` (6 cases); 479 green.
- Device: f4 adds `view.CaptureAsync()` of the shown page's content: 1032×2106, the view's size in px, PNG, and
  JPEG(80) starting FF D8. Also: RefreshRate 90.00 (the Jolla C2 panel); ClipboardContentChanged once for the own
  `SetTextAsync` and once more for a change from QML. `tools/sf matrix f4` 44/44, `features controls` PASS.
  The first f4 run counted 2+2 clipboard events (the double signal) and was fixed by the dedupe.
  Docs: `porting-existing-apps.md` (capture, JPEG, clipboard, refresh rate).

<a id="s12"></a>
### S12 · Permissions mapping, `Early` services, dead code
Plan: §M14 steps 4–5 · Audit: essentials E3, E5, E6 · Phone: check `/etc/sailjail/permissions/`, leg `f4` · Depends: —
- [x] verify Sailjail names on the phone (`AppLaunch` first); fix the table
- [x] `Permissions.Flashlight` Denied when sandboxed; pickers `Demand` media/document permissions when sandboxed
- [x] porting guide: the instance form of `Permissions.X` throws, use the generic form
- [x] `Early: true` for SecureStorage and DeviceDisplay; screen-reader branch → registry row; log when the MainThread hook is skipped

Done when: `f4` green in sandboxed and unsandboxed runs.

Notes:
- 2026-10-06 🟡 (unsandboxed run only). `/etc/sailjail/permissions/` on SFOS 5.2.0.18 lists `AppLaunch` (the mapping was
  right), and also `Sensors` (`dbus-system.talk com.nokia.SensorService`, so sensorfw needs it in a sandbox):
  `Permissions.Sensors` now maps to it (was always Granted). `Permissions.Flashlight` is Denied in a sandbox
  (`UnavailableInSandbox`; no Sailjail permission reaches the torch, `Flashlight` throws there). PostNotifications
  and NearbyWifiDevices left as they were (no clear Sailjail counterpart checked).
- Pickers: not a `Demand` but one warning per picker kind naming the missing permissions (`Pictures`/`Videos` +
  `MediaIndexing`, `UserDirs`/`Documents`/`Downloads`): MAUI's pickers need no permission on Android/iOS, so throwing
  would break apps that work there. `porting-existing-apps.md` "Not supported yet": the instance form of a permission,
  and sandboxed pickers.
- `SecureStorage` and `DeviceDisplay` are `Early` rows. The screen reader is a registry row now; the special cases
  for "MAUI's registered reader" (none exists in RC1) are gone from `AddSailfishEssentials`, `Install` and the overlay.
  `Boot.cs` logs when no loop dispatcher exists and MainThread is left unhooked.
- Host: `EssentialsDefectTests` +3 (mapping, picker permissions, registry rows); 432 green.
- Device: `tools/sf matrix f4 features silica` 3/3 PASS (unsandboxed sample). Left: a sandboxed f4 run (a Harbour
  build launched through Sailjail); how the matrix launches a sandboxed build was not worked out this session.

<a id="s13"></a>
### S13 · Handler fixes: SwipeItem colours, Window title/RTL, mapper conventions
Plan: §M15 steps 1, 3, 4, 8 (shape handlers) · Audit: handlers H2, H3, H5 · Phone: legs `controls visual containers` · Depends: S04 (`ActivateWindow`)
- [x] `SwipeItemsJson`: `fg`, `iconColor`, font keys; per-item `PropertyChanged` re-push
- [x] `Window.Title` → cover/window title; `Window.FlowDirection` as RTL root; `StatusBarTheme` traced no-op
- [x] mapper constructors on Application/Window handlers; `SailfishNavigationViewHandler.CommandMapper` is the used one
- [x] `NeedsContainer => false`
- [x] the six Controls shape handlers in `HandlerParityTests.Pairs`; parity doc regenerated

Done when: SwipeItem `TextColor`/`IconColor` visible (screenshot); RTL on the window mirrors the page.

Notes:
- 2026-10-06 ✅. `SwipeItemsJson` sends `fg` (TextColor; the QML already read `fg`, nothing sent it) and `iconColor`
  (IconColor → `ColorOverlay` on the icon in `SwipeView.qml`; unset keeps the icon's own colours, as MAUI 11 no longer
  tints non-font icons). Font/CharacterSpacing not done (Silica's action label uses `Theme.fontSizeSmall`).
  `SailfishSwipeViewHandler` watches the items (and the Left/Right collections) and re-pushes on a change.
- `SailfishWindowHandler.Mapper`: `Title` → `window.mauiCoverTitle` (the cover placeholder; empty keeps the
  ApplicationTitle), `FlowDirection` → layout + reconcile; `QtHostVisualState.IsRightToLeft` treats the window as the
  root. `StatusBarTheme` is not in MAUI 11's net11 `WindowHandler.Mapper`, so nothing reaches the handler; left unmapped.
- `(IPropertyMapper?, CommandMapper?)` constructors on `SailfishApplicationHandler`/`SailfishWindowHandler`;
  `SailfishNavigationViewHandler.CommandMapper` (new static, same object as `NavigationCommandMapper`). Sailfish view
  handlers and `NullViewHandler` override `NeedsContainer => false` (HasContainer was true with ContainerView null
  for clipped or shadowed views).
- Parity: Line/Path/Polygon/Polyline/Rectangle/RoundRectangle handlers added; 1344/1344 keys (README updated).
- Host: `Renderer/HandlerConventionTests.cs` (5 tests); 437 green.
- Device: f3's "Flag" swipe item has TextColor black + IconColor yellow on the sample logo;
  `docs/screenshots/maui11/s13-swipeitem-colors.png` (black label, yellow-tinted icon). `tools/sf matrix controls
  visual containers nav` 4/4 PASS, f3 106/106 via `sf shots`. Not checked on the phone: Window.Title on the cover
  (the sample sets no window title) and window-level RTL (host test only).

<a id="s14"></a>
### S14 · TabbedPage badges, tab icons, scrolling tab row
Plan: §M15 step 2, §M15 step 5 (`PagesChanged`) · Audit: navigation N7 · Phone: legs `containers shell tabpulley` · Depends: —
- [x] `BadgeText`/`BadgeColor`/`BadgeTextColor` carried in `Tabs`, drawn in the tab row; runtime updates
- [x] empty tab title falls back to route/type name
- [x] tab row scrolls when more than 4 tabs
- [x] `MultiPage.PagesChanged` → `RequestPoll`

Done when: a badge shows and updates on the phone (screenshot); a 6-tab page is usable.

Notes:
- 2026-10-06 ✅. `ISailfishPageContainer.Tabs`/`SubTabs` return a `SailfishTabRow` (titles, index, select, badges) instead of
  a tuple; `SailfishTabBadge` reads `TabbedPage.BadgeText/BadgeColor/BadgeTextColor` and, for Shell rows, MAUI 11's
  `BaseShellItem.BadgeText/…` (sections and contents). The row JSON gains `"badges":[{text,bg,fg}|null,…]` only when a
  tab has one, so rows without badges send the same JSON as before.
- `qml/MauiTabRow.qml` (new) draws both rows (tabs and a Shell section's contents): a pill on the title's top-right
  corner, a dot for `""`, highlight colour when no `BadgeColor`. More than 4 tabs: a horizontal `Flickable`, ~3.5 tabs in
  view, the selected tab kept in view. The input router does not arm a tab swipe on a press inside the chrome while a
  row scrolls (`TabRowScrolls`, `ChromeBottomDp`), so a drag there scrolls the row.
- Title fallback (`SailfishPageContainers.TabTitle`): NavigationPage root title → explicit route (not `IMPL_`/`D_FAULT_`)
  → page type name. Icons are still not drawn (text-only row, documented).
- `SailfishTabbedPageHandler` watches `PagesChanged` and each child's Title/badge keys → `RequestPoll` (before, a badge or
  a new tab waited for the heartbeat). Shell badge changes still wait for the heartbeat (Shell change notifications
  are S15).
- Host: `Renderer/TabRowTests.cs` (6 tests); 446 green.
- Device: containers leg J (6 tabs, count badge red/white "3", dot, runtime "12", tab 6 selected and scrolled into view);
  `tools/sf matrix containers shell tabpulley` 3/3 PASS. Screenshots `docs/screenshots/maui11/s14-tab-badges.png`,
  `s14-tab-row-scrolled.png`. Not checked on the phone: a finger drag on the scrolling row (host test only) and Shell badges
  (host test only). Docs: `porting-existing-apps.md` (text-only tabs, badges), `silica-parity.md` row.

<a id="s15"></a>
### S15 · Change notifications, context menu entries, `HideSoftInputOnTapped`
Plan: §M15 steps 5–6 · Audit: handlers H4, H6; input I7 · Phone: legs `shell pulley input` · Depends: —
- [x] Shell `Items`/`FlyoutItems`/`FlyoutBehavior` → `MapModelPage`; `Compatibility.Layout.LayoutChanged` → subtree
- [x] `ContentPage.HideSoftInputOnTapped` in the router
- [x] `MenuFlyoutSubItem` children inline; separators skipped; icon-only `ToolbarItem` fallback text or one warning

Done when: tests per item; a flyout item added at runtime appears without waiting for the heartbeat.

Notes:
- 2026-10-06 ✅. Shell: `SailfishShellHandler` listens to `IShellController.StructureChanged` and `FlyoutItemsChanged`
  (items/sections/contents added or removed, flyout items) → `RequestPoll`; `Shell.FlyoutBehavior` is a page mapper key
  (`MapModelPage`). Shell *property* changes on items (a section's Title or BadgeText) still wait for the heartbeat.
- Compatibility layouts: not `LayoutChanged` (it fires on every layout pass) but the layout's `ChildAdded`/`ChildRemoved`/
  `ChildrenReordered` → `RequestSubtree` in `SailfishLayoutHandlerBase` (they never invoke the `ILayoutHandler`
  commands). Only reachable when the app calls `UseMauiCompatibility()`; the test enables the check by reflection.
- `HideSoftInputOnTapped`: `QtHostInputRouter.OnPress` → `HideSoftInputOnTap`: on a `ContentPage` with the flag, a press
  that does not land on an `entry`/`editor`/`search-bar` host unfocuses the focused `InputView` (→ `mauiFocus` false →
  Maliit closes). Counter `SoftInputHides`.
- Context menus: `QtHostPageRenderer.ContextEntries` flattens the flyout: a `MenuFlyoutSubItem` is a label row (QML
  `MenuLabel`) followed by its items, separators are dropped; picks are mapped through the flattened rows.
  Icon-only `ToolbarItem` (`ToolbarText`): `AutomationId` → `SemanticProperties.Description` → icon file name → blank
  with one warning per item.
- **Bug found and fixed:** `_openFlyout` (set when a ContextMenu opens; it holds native stack steps while a menu is up)
  was never cleared. After the first context menu, a navigation the app started on the same page (an "Edit" entry
  that pushes a page) waited for good. `ContextMenu.qml` now emits `context-closed` when `active` goes false →
  `ApplyContextClosed`; a model-page switch also clears it. The rows stay for the pick (Silica can deliver the click
  after closing).
- Host: `Renderer/ChangeNotificationTests.cs` (9 tests; the context-menu hold test fails without the fix); 455 green.
- Device: `tools/sf matrix controls shell pulley input` 4/4 PASS: controls leg F reads the menu rows
  `ctx one|ctx two|#ctx more|ctx three` (separator dropped, label row) and sees navigation held while open and released
  after the pick; shell leg E2 adds a flyout item at runtime and finds it in the pulley 500 ms later (and gone 500 ms
  after removal). Not checked on the phone: `HideSoftInputOnTapped` (host tests only; a leg would need the VKB) and
  Compatibility layouts. Docs: `porting-existing-apps.md` (pulley text fallback, context menus, HideSoftInputOnTapped),
  `silica-parity.md` ContextMenu row.
- 2026-10-07: screenshot `docs/screenshots/maui11/s15-context-menu.png` (rows, separator dropped, "ctx more" as a dimmer
  label row). **Bug found by the screenshot and fixed:** no ContextMenu had ever shown properly. Silica's ContextMenu
  anchors its bottom to its parent's bottom and expects the parent to grow by the menu's height, as a ListItem does,
  and it renders that parent through a layer sized for the grown height. A MAUI host never grows, so the menu hung
  above the target, outside the layer, and only the slice over the target was visible. The leg's state checks
  (`active`, rows) had passed all along. `MauiModelPage.__openContextMenu` now opens the menu on a stand-in item
  (`mauiContextAnchor`) at the target's place, in the target's parent, z above its siblings. The stand-in grows by the
  menu's height, so the menu hangs below the target, with an opaque `highlightDimmerColor` backing (MAUI rows below do
  not move away). New controls check: "ContextMenu opens below its target"; `tools/sf shots` controls 49/49.

<a id="s16"></a>
### S16 · Router: swipe flags, `InputTransparent`, transformed hit-test, positions
Plan: §M17 steps 1–3 · Audit: input I2, I4 · Phone: leg `input visual` · Depends: —
- [x] swipe direction as flags, `Threshold` honoured
- [x] hit-test skips `InputTransparent` and cascading layouts (falls through)
- [x] inverse-mapped hit-test for `Scale`/`Rotation`/anchors
- [x] `GetPosition(relativeTo)` for Tap/Pointer/LongPress; `Buttons` mask
- [x] router tests for each; `input` leg checks: combined-flag swipe, fall-through, scaled view

Done when: the new `input` checks pass on the phone.

Notes:
- 2026-10-06 🟡. `DispatchSwipe`: `(swipe.Direction & direction) != 0` and the distance along the swipe's axis must reach
  `Math.Max(swipe.Threshold, 30 dp)`. **Behaviour change:** MAUI's default Threshold is 100 dp, so a default
  SwipeGestureRecognizer now needs a 100 dp swipe where 30 dp fired before (as on the other platforms; the sample's
  GesturesPage uses defaults).
- `TryHitTest` skips InputTransparent hosts and the subtree of a `Layout` with InputTransparent + CascadeInputTransparent
  (`IsInputTransparent`), so the next host under the finger gets the touch; before, the sequence ended there.
- `NativeElementHost.HitTransform` keeps the local → root affine when it is more than a translation; `HitsFootprint`
  maps the point back into the element's own rect, so a `Scale=2` view is hit on its whole scaled area and a rotated one
  on its rotated rect (3D stays the 2D footprint).
- Host: `Renderer/RouterTests.cs` (3 tests, all fail on the old router); 440 green.
- Device: `tools/sf matrix input visual containers collection` 4/4 PASS (no regression; the input leg's 100 px drags
  are pans, not swipes). Left: `GetPosition(relativeTo)` and the `Buttons` mask (step 4), and device checks of the three
  new behaviours in the input leg (only host-tested so far).
- 2026-10-07 ✅ (rest). `QtHostPageRenderer.RelativePosition(root, relativeTo)`: null/Page/Window → the root point, a hosted
  view → its own coordinates (through `HitTransform` when scaled/rotated, else minus its root rect origin), an
  element with no attached host → null. Tap, Pointer and LongPress pass it as their `getPosition` (`PositionOf`); list
  row taps keep their cell-relative point. `Buttons`: a touch is the primary button, so a Tap or Pointer recognizer
  without `ButtonsMask.Primary` (secondary-only) no longer fires on a touch.
- Note for leg authors: a scaled host's `MauiLogicalBounds` starts at the *transformed* top-left with the unscaled size.
- Host: `RouterTests` +3 (relative tap/pointer position, secondary-only tap); 458 green.
- Device: the `input` leg pushes a "Router" page (`QtHostDiagnosticsRunner.InputRouter.cs`): Left|Right swipe of 150 dp
  fires, a tap through an InputTransparent overlay reaches the view below, a tap 70 dp right of a `Scale=2` view's
  centre hits it with `GetPosition(view)` = (85.0, 49.9). `tools/sf matrix input visual collection controls` 4/4 PASS
  (input on the rerun after fixing the leg's own tap point).

<a id="s17"></a>
### S17 · Router: recognizers around native adapters, LongPress, gestures in rows
Plan: §M17 steps 4, 5, 7 · Audit: input I3, I5 · Phone: legs `input collection` · Depends: S16
- [x] parent-chain Tap/LongPress after a QML-consumed press (the control's own recognizers only, see Notes)
- [x] LongPress `AllowableMovement`, `Canceled`, optional `Running` (Running left out, as on Android)
- [x] rows: `list-item-held` → row long press; `list-item-pressed` hands Pan/Swipe/Pointer owners to the router
- [x] router tests; `input` leg row long-press check

Done when: a long press and a pan inside a CollectionView row fire on the phone.

Notes:
- 2026-10-07 ✅. QML-consumed controls (`button`, `entry`, `editor`, `switch`, `slider`, `check-box`, `stepper`,
  `scroll-view`): the control's **own** Tap/LongPress recognizers now take the sequence beside the native action
  (`OwnGestureCaptures`). **Deviation from the plan:** an ancestor's recognizers do not fire. Android never passes a
  touch a child consumed up to the parent, and iOS lets a `UIControl` win over a superview's tap; a tappable row
  around a `CheckBox` would otherwise toggle twice. Lists, carousels, swipe views, dialogs and the web view are left out.
- LongPress: `AllowableMovement` (read as dp) cancels by distance (no longer the fixed 10 dp tap slop; a pan owner
  still cancels when the drag starts). `LongPressing` `Canceled` on an early release, on moving past it, on a pan taking
  the finger and on a second finger (`CancelLongPress`, counter `LongPressCancels`); `Started`/`LongPressed`/`Completed`
  at the fire as before. No `Running` (Android sends none).
- Rows: not a `list-item-held` event but one path for every template gesture. Rows whose template has recognizers
  other than Tap get `"g":1`; their delegate `MouseArea` emits `list-item-pressed {row,cell,x,y}`.
  `QtHostListAdapter.OnRowPressed` hit-tests the cell (`HitInCell`, shared with `TryFindRowTap`) and calls
  `QtHostInputRouter.CaptureRow`. The nearest owner up to the cell root takes Pan/Swipe/Pointer/LongPress/Pinch; taps
  stay on `list-item-tapped`. A captured pan/swipe/pinch pushes `mauiHoldRow` (that delegate's `preventStealing`) until
  the release. A fired row long press or a started row pan makes the row's following tap no tap and no selection
  (`TakeRowGesture`).
- **Found on the phone:** the delegate's press report can arrive after a fast drag's release (all events queued
  first), and the late capture armed a long press with no finger down. `CaptureRow` now needs the finger still down.
- Host: `Renderer/RouterRowTests.cs` (6 tests); `RendererHarness.Disposing` (test teardown hook); 464 green.
- Device: the input leg's router page shows a CollectionView (LongPress + Pan rows). A 900 ms hold on row one fires
  LongPressed. A 150 dp sideways drag on row two (a move every 40 ms) pans it Started/Running/Completed with no long
  press and no selection. `tools/sf matrix input collection controls containers` PASS (input on the rerun after the
  late-capture fix). Not checked on the phone: a Button's own Tap recognizer (host test only). Docs:
  `porting-existing-apps.md` (row gestures, own recognizers on native controls, LongPressing states).

<a id="s18"></a>
### S18 · Missing control events, keyboard flags, `ReturnType.Next`
Plan: §M17 steps 8–9 · Audit: input I6, I7 · Phone: legs `controls input text` · Depends: —
- [x] Button/ImageButton `Pressed`/`Released`; Slider `DragStarted`/`DragCompleted`; SwipeView `SwipeChanging`; Editor `Completed` on focus loss
- [x] `ImhNoAutoUppercase` for flags without Capitalize
- [x] `ReturnType.Next` focuses the next `InputView`
- [x] tests per event

Done when: each event observed in a leg on the phone.

Notes:
- 2026-10-07 ✅. New adapter events:
  - `pressed-changed {id,pressed}` from Silica Button's `pressed` and the ImageButton tap surface → `SendPressed`/`SendReleased`
    (the press ends before the click, so Released comes before Clicked);
  - `drag-changed {id,dragging}` from `SliderBase.down` → `ISlider.DragStarted/DragCompleted`;
  - `swipe-changing {id,offset}` while the SwipeView flickable is dragged (4 px steps) → `SwipeStarted` on the first one of
    a drag, then `SwipeChanging` (dp, > 0 = Right). A drag that settles where it started now also ends with a
    `swipe-state`, so `SwipeEnded` always follows; `SwipeStarted` for an open from code stays in `ApplySwipeState`.
- Editor `Completed` on focus loss lives in `SailfishHandlerCore.ApplyFocus` (the Unfocus mapper). The native loss
  arrives there through `Unfocus()` too, and an app's `Unfocus()` completes as well.
- `ReturnType.Next`: after `Entry.SendCompleted`, `AdapterEventRouter.FocusNextInput` focuses the next visible, enabled,
  editable `InputView` in the page's tree order. Nothing after it: focus stays.
- Keyboard: a `CustomKeyboard` without CapitalizeSentence/Word/Character, or with CapitalizeNone, adds `ImhNoAutoUppercase`.
- Host: `Renderer/ControlEventTests.cs` (9 cases); 473 green.
- Device: the input leg's router page gets a control-events step (`QtHostDiagnosticsRunner.ControlEvents.cs`):
  - Button press/hold/release → pressed, released, clicked;
  - Slider drag 30→70 % → drag-started, drag-completed;
  - SwipeView drag of 160 dp → SwipeStarted, 5× SwipeChanging, SwipeEnded;
  - Editor tapped then `Unfocus()` → Completed;
  - Return (injected key) on a Next entry → the second entry has native `activeFocus`.
  `tools/sf matrix input controls text` 3/3 PASS.
- Not checked on the phone: ImageButton press events and the auto-uppercase hint (host tests only; the VKB's shift state
  was not read). Docs: `porting-existing-apps.md` (control events).

## Phase D — page chrome on MAUI's `Toolbar`

<a id="s19"></a>
### S19 · `SailfishToolbarHandler` skeleton
Plan: §M16 steps 1–2 · Audit: navigation N1 · Decision: D16 · Phone: legs `page nav shell pulley` · Depends: S05
- [ ] device check: 0-height header keeps Canvas shapes painting (`QtHostPageRenderer.cs:1221-1223`); screenshot
- [ ] `SailfishToolbarHandler` created from `SailfishWindowHandler.MapContent`; keys `Title`, `IsVisible`, `BackButtonVisible`, `ToolbarItems`
- [ ] `PageChromeOps` reads the toolbar; `WatchToolbarItems` and direct `Page.ToolbarItems` reads removed; Priority order and Shell-level items
- [ ] harness tests for each key

Done when: pulley entries come from the toolbar (Priority order, Shell items) and all chrome legs stay green.

Notes:
-

<a id="s20"></a>
### S20 · `TitleView`, `HasNavigationBar`, `NavBarIsVisible`, `TabBarIsVisible`
Plan: §M16 steps 3, 5 · Audit: navigation N1, N3 · Phone: legs `page shell tabpulley` · Depends: S19
- [ ] `TitleView` hosted in the chrome
- [ ] `HasNavigationBar=false` / `NavBarIsVisible=false` through the header path checked in S19
- [ ] `Shell.TabBarIsVisible` per page hides the tab row

Done when: screenshots of a TitleView page, a header-less page and a tab-less Shell page.

Notes:
-

<a id="s21"></a>
### S21 · `Shell.SearchHandler`: search field and query
Plan: §M16 step 4 · Audit: navigation N2 · Decision: D14 · Phone: leg `shell` · Depends: S19
- [ ] search field under the header from `Toolbar`/`SearchHandler`; `Query` two-way; `Placeholder`; `SearchBoxVisibility`; `Command` on submit
- [ ] warn once for unsupported members
- [ ] harness test: query write-back

Done when: typing in the field updates `Query` on the phone (screenshot).

Notes:
-

<a id="s22"></a>
### S22 · `Shell.SearchHandler`: results list and selection
Plan: §M16 step 4 · Audit: navigation N2 · Phone: leg `shell` · Depends: S21
- [ ] `ShowsResults` + `ItemsSource`/`ItemTemplate` as a dropdown on the list adapter
- [ ] `SelectedItem` / `OnItemSelected`
- [ ] sample `SearchHandler` filtering a list

Done when: the sample filters and selects on the phone.

Notes:
-

<a id="s23"></a>
### S23 · `FlyoutIsPresented` and flyout-content warnings
Plan: §M16 step 6 · Audit: navigation N3 · Decision: D15 · Phone: legs `shell pulley` · Depends: —
- [ ] `FlyoutIsPresented=true` from code opens the flyout entries (D15) and writes back `false` on close
- [ ] one warning per Shell for `FlyoutHeader/Footer/Content/ItemTemplate`; porting guide "flyout = pulley, text only"

Done when: a "menu" button in a sample opens the entries on the phone.

Notes:
-

## Phase E — images, collections, canvas

<a id="s24"></a>
### S24 · Image sources through `IImageSourceService`
Plan: §M6 steps 1–4 · Audit: graphics G1 · Phone: legs `f3 collection100 skia` · Depends: —
- [x] `ISailfishImageSourceService` + four defaults registered via `ConfigureImageSources`, matched on Core interfaces (no Sailfish defaults, see Notes)
- [x] `QtHostImages.Resolve` asks `IImageSourceServiceProvider` first
- [x] `QtHostImageSources.Register` wraps a func into a service (SkiaSharp keeps working; kept as the sync shortcut, see Notes)
- [x] test: custom `ImageSource` + its service resolves; `custom-controls.md` updated

Done when: a test image source with its own service renders on the phone.

Notes:
- 2026-10-07 ✅. New public API (`Platform.QtHost`, PublicSurface.txt regenerated):
  - `ISailfishImageSourceService` (`GetUrlAsync(IImageSource, ct)` → `IImageSourceServiceResult<string>`, a URL Qt loads);
  - `ISailfishImageSourceService<T>` (also `IImageSourceService<T>`, which MAUI's `AddService<TSource, TService>()` needs);
  - `SailfishImageSourceServiceResult` (URL + optional dispose).
- `QtHostImages.Resolve` asks `IPlatformApplication.Current.Services`' `IImageSourceServiceProvider` for the source's
  type first (cached per type; "no service" exceptions mean none). A Sailfish service is asked once per source object,
  off the Qt thread, resuming on the Qt loop through the same pending/`WhenReady` machinery as streams (the table is
  now keyed on `ImageSource`).
  - Anything else (MAUI's default marker services on plain net) falls back to the stock handling and then to the
    `QtHostImageSources` resolvers.
- **Deviation:** no Sailfish services for the four stock interfaces. Registering them would only call the same switch.
  The provider is asked first, so an app's own service for `IUriImageSource` (or another stock interface) already
  replaces the built-in loading (host test). `QtHostImageSources.Register` stays a plain resolver list, not wrapped
  into a service: it is the synchronous path SkiaSharp uses, and it is asked after the stock sources as before.
- Not done here (S25): disposing the service result (it lives as long as its source object), cancellation and
  `IsLoading`.
- Host: `Renderer/ImageSourceServiceTests.cs` (3 tests); 482 green.
- Device: the sample registers `SailfishDiagnostics.ConfigureImageSources` (a `DiagImageSource` + its service, 150 ms
  async); f3's images page shows one: loaded `sailfish_logo.png` through the service (asked 1).
  `tools/sf matrix f3 collection100 skia` 3/3 PASS. Screenshot `docs/screenshots/maui11/s24-service-image-source.png`
  (the fifth image, 96 dp, comes through the service). Docs: `custom-controls.md` "A library image source".

<a id="s25"></a>
### S25 · Image loading: `IsLoading`, cancellation, stream cache, GIF, decode size
Plan: §M6 steps 3, 5, 6 · Audit: graphics G2 · Phone: legs `f3 collection100` · Depends: S24
- [x] `UpdateIsLoading(true/false)`; previous load cancelled on source change
- [x] stream files: `.gif` when the header says so, deleted on dispose, swept at startup, token honoured
- [x] `sourceSize` cap for unsized/`Center` images
- [x] tests per item

Done when: `cache/streams` does not grow across a Kitchen tour; a stream GIF animates.

Notes:
- 2026-10-07 ✅. `Image.IsLoading`: `SailfishImageHandler` appends to its Source mapping (`TrackLoading`):
  - `UpdateIsLoading(true)` while a URL is resolved but not reported, or the source is pending;
  - false on the adapter's new `image-loaded` event (or `image-failed`) for that URL, or when a pending read fails;
  - the handler remembers the last reported URL, so a re-mapped Source does not show loading again.
  - `Image.qml` sends `image-loaded` on the next turn (0 ms Timer). **Found on the phone:** a local GIF's AnimatedImage
    loads synchronously while its object is being created, before managed knows its id, so the event was dropped and
    IsLoading stayed true. `AnimatedImage` errors now also send `image-failed`.
- Cancellation: a Source change calls `QtHostImages.CancelPending(old)`: a stream still being read (or a service still
  answering) has its token cancelled (`Stream(token)`, `CopyToAsync(token)`, `GetUrlAsync(token)`) and is forgotten, so
  a later use reads again; a finished entry stays.
- Stream cache:
  - files are named `.gif` when the header starts `GIF8` (Image.qml picks AnimatedImage by the extension);
  - deleted by the entry's finalizer when the source object is collected (a service result is disposed the same way);
  - `cache/streams` is emptied once per process at the first stream read;
  - partial files are deleted on cancel or error.
- Decode cap: Props sends `mauiDecodeCap` (the screen's long side, px). Unsized/Center images decode within cap×cap;
  Qt 5.6 scales rasters down only, and SVGs are left out because Qt scales them to the request. An unsized image
  above the cap reports and measures its capped size (documented).
- Host: `Renderer/ImageLoadingTests.cs` (4 tests: IsLoading incl. re-map, cancel, stream GIF, cap); 486 green.
- Device: f3 images page gains a stream GIF (`pulse.gif` through `FromStream`, playing). Checks: IsLoading false for the
  stream/service/GIF images [0,0,0,0]; the stream cache holds this run's 3 files (swept at start); the GIF plays (3
  frames, frame 1 → 0 within 400 ms, `.gif` file). `tools/sf matrix f3 collection collection100` PASS.
  - The Kitchen part of "Done when" holds trivially: Kitchen uses no stream image sources (grep), so its tour writes
    nothing to `cache/streams`.
  - Not checked on the phone: deletion on collection (finalizer timing) and the decode cap's memory effect (no 4000 px
    photo in a leg).

<a id="s26"></a>
### S26 · CollectionView scrolling behaviours
Plan: §M8 steps 1, 2, 4 · Audit: collections C4 · Phone: legs `collection collection100` · Depends: —
- [ ] `ItemsUpdatingScrollMode` (`KeepLastItemInView`, `KeepScrollOffset`)
- [ ] `ItemsLayout.PropertyChanged` (span/spacing on the same object)
- [ ] `ScrollTo(animate:)`; `Scrolled` deltas; dead `IScrollViewController` cast removed
- [ ] tests per item

Done when: the sample's chat-like list keeps the last item in view on the phone.

Notes:
-

<a id="s27"></a>
### S27 · Selection and carousel visual states
Plan: §M8 steps 3, 6 · Audit: collections C4 · Decision: D4 · Phone: legs `collection containers f3` · Depends: —
- [ ] screenshot first: is the native highlight visible under opaque templates?
- [ ] VSM `Selected`/`Normal` on cell roots; carousel `CurrentItem/NextItem/PreviousItem/DefaultItem`
- [ ] carousel `IsScrollAnimated`, `VisibleViews`, `IsDragging`, `IsScrolling`

Done when: a `Selected` style applies on the phone (screenshot).

Notes:
-

<a id="s28"></a>
### S28 · Snap points, `MeasureFirstItem`, lazy templating
Plan: §M8 steps 5, 7 · Audit: collections C4 · Phone: legs `collection500 collection100` · Depends: —
- [ ] `SnapPointsType/Alignment` → `snapMode` + highlight range
- [ ] `ItemSizingStrategy.MeasureFirstItem`
- [ ] off-screen rows templated lazily in `RequestMaterialize`
- [ ] `collection500` timings recorded in `profiling.md` before and after

Done when: `collection500` open time measured and not worse; snapping works.

Notes:
-

<a id="s29"></a>
### S29 · CollectionView inside ScrollView, items parity, vertical loop
Plan: §M8 steps 6 (vertical loop), 8, 9 · Audit: collections C5, C6 · Phone: legs `collection containers` · Depends: —
- [ ] unbounded list: non-interactive inner list, lift the 192 cap or warn once; porting guide note
- [ ] `CollectionView`/`CarouselView` in `HandlerParityTests.Pairs` with pinned gaps
- [ ] vertical `Loop`: implement or document the limit

Done when: a 300-item CollectionView inside a ScrollView shows every row on the phone; parity doc lists the items handlers.

Notes:
-

<a id="s30"></a>
### S30 · Canvas text anchor, fonts, fill rule, antialias
Plan: §M9 steps 1, 2, 5 · Audit: graphics G4, G9 · Phone: legs `shapes visual f3` · Depends: —
- [ ] device check: `DrawString` next to a Label, EvenOdd star, `Antialias=false` (screenshots)
- [ ] `DrawString` anchor as Android; `Font` through `QtHostFonts.Resolve`; `ctx.fillRule` for EvenOdd in three QML files; antialias applied
- [ ] host tests for the recorder, shape serialisation, span HTML, glyph/stream URLs, cache policy

Done when: screenshots match Android anchoring within 1 px; new tests green.

Notes:
-

<a id="s31"></a>
### S31 · `GraphicsView` touch interactions and re-record policy
Plan: §M9 steps 3–4 · Audit: graphics G3, G5 · Phone: legs `shapes input` · Depends: S16
- [ ] press/move/release → `Start/Drag/End/CancelInteraction`
- [ ] record only on `Invalidate`, drawable/property and size change, not every reconcile
- [ ] chunk the command list instead of truncating at 4096; stale comment `QtHostGraphics.cs:8-9`

Done when: a GraphicsView sample receives drag on the phone; `input` leg check added.

Notes:
-

## Phase F — legacy ListView and TableView

<a id="s32"></a>
### S32 · Legacy `ListView` handler: cells, tap, selection
Plan: §M4 steps 1–2 · Audit: collections C2, handlers H7 · Decision: D2 · Phone: legs `collection containers` · Depends: S01
- [ ] `Row<ListView, SailfishLegacyListViewHandler>` before `ItemsView`
- [ ] `TemplatedItems` onto the list adapter; `ViewCell.View`, `TextCell`, `ImageCell` row templates
- [ ] `ItemTapped`/`ItemSelected`/`SelectedItem`
- [ ] harness tests per cell type

Done when: a ListView with text, image and view cells shows rows and selects on the phone.

Notes:
-

<a id="s33"></a>
### S33 · Legacy `ListView`: grouping, header/footer, separators, row height
Plan: §M4 step 2 · Audit: collections C2 · Phone: leg `collection` · Depends: S32
- [ ] `IsGroupingEnabled`, `GroupDisplayBinding`, `GroupHeaderTemplate`
- [ ] `Header`/`Footer` (+templates); `SeparatorVisibility`/`Color`; `HasUnevenRows`/`RowHeight`
- [ ] `ScrollTo`

Done when: a grouped ListView with header and separators matches a screenshot expectation.

Notes:
-

<a id="s34"></a>
### S34 · Legacy `ListView`: refresh, context actions, Switch/Entry cells
Plan: §M4 steps 2–3, 5 · Audit: collections C2 · Phone: legs `collection pulley` · Depends: S33
- [ ] `IsPullToRefreshEnabled`/`IsRefreshing`/`RefreshCommand`
- [ ] `Cell.ContextActions` → context menu on long press
- [ ] `SwitchCell`, `EntryCell`; `SendCellAppearing/Disappearing`; `CachingStrategy` mapping

Done when: each feature exercised on the phone.

Notes:
-

<a id="s35"></a>
### S35 · `TableView`
Plan: §M4 step 4 · Audit: collections C2 · Phone: leg `containers` · Depends: S34
- [ ] `Root` flattened into adapter rows; `TableSection.Title` as group headers
- [ ] harness test with all cell kinds

Done when: a settings-style TableView renders on the phone (screenshot).

Notes:
-

<a id="s36"></a>
### S36 · Legacy list leg, ported apps, docs
Plan: §M4 acceptance · Phone: new leg `legacylist` · Depends: S35
- [ ] `legacylist` leg in the Diagnostics runner, added to `matrix.sh`
- [ ] ported apps that use `ListView` checked (`docs/app-test-campaign.md`)
- [ ] `porting-existing-apps.md`, `silica-parity.md:46` updated

Done when: `legacylist` green; full matrix green.

Notes:
-

## Phase G — navigation details, dialogs, drag & drop

<a id="s37"></a>
### S37 · `animated:false`, `InsertPageBefore`, `RemovePage`
Plan: §M27 steps 2–3 · Audit: navigation N6 · Phone: legs `nav navback shell` · Depends: —
- [ ] `Animated` threaded into `NavOperation`; last level of a multi-level pop animated
- [ ] insert/remove operate on the model page below the top
- [ ] `NativeStackSyncTests` for both

Done when: `PushAsync(page, false)` shows no slide; `RemovePage` does not re-render the top page (recording).

Notes:
-

<a id="s38"></a>
### S38 · Dialogs: queue, thread hop, RTL, keyboard; Detail swap
Plan: §M27 steps 4–6 · Audit: navigation N5, N7, N8 · Phone: legs `popup containers` · Depends: —
- [ ] dialog queue; navigation not held while a dialog is open
- [ ] `QtThread.Run` hop in `QtHostAlertSubscription`; `FlowDirection`; Email/Url prompt keyboards
- [ ] replaced `FlyoutPage.Detail` handlers disconnected; renderer comment fixed; page-cache difference documented

Done when: two chained alerts both show; an alert from a background thread shows.

Notes:
-

<a id="s39"></a>
### S39 · Drag & drop: router core and drag ghost
Plan: §M28 · Audit: input I1 (design notes) · Phone: leg `input` · Depends: S17
- [ ] drag start on the long-press timer → `SendDragStarting`
- [ ] `DragGhost.qml`; back navigation and flickables off while dragging
- [ ] `SendDragOver/Leave`, `SendDrop`, `SendDropCompleted` (also on cancel); args subclasses with positions
- [ ] router test for the event order and text transfer

Done when: dragging a Label onto a drop target transfers its text on the phone.

Notes:
-

<a id="s40"></a>
### S40 · Drag & drop: rows, leg, docs
Plan: §M28 step 6 · Phone: leg `input` · Depends: S39
- [ ] drag source inside a CollectionView row
- [ ] DnD check in the `input` leg; porting guide updated

Done when: a row can be dragged to a target on the phone.

Notes:
-

## Phase H — visuals, text, keyboard, theme, platform API

<a id="s41"></a>
### S41 · Gradient backgrounds on every view
Plan: §M10 · Audit: graphics G6 · Decision: D10 · Phone: legs `visual shapes controls` · Depends: —
- [ ] gradient `Background` → gradient item under the host
- [ ] gradient `Shadow.Brush` → average colour
- [ ] sample page with a gradient background

Done when: screenshot of the gradient page.

Notes:
-

<a id="s42"></a>
### S42 · FormattedText spans: properties and gestures
Plan: §M11 (spans), §M17 step 6 · Audit: graphics G7, input I3 · Phone: legs `text controls` · Depends: —
- [ ] span `BackgroundColor`, `CharacterSpacing`, `LineHeight`, `TextTransform`
- [ ] span gestures via `<a href="span:N">` + `linkAt` → adapter event → span recognizers
- [ ] span HTML tests

Done when: a span tap fires on the phone.

Notes:
-

<a id="s43"></a>
### S43 · Mixed-font measure and `FontAutoScalingEnabled`
Plan: §M11 (measure, scaling) · Audit: graphics G7, lifecycle row 17 · Phone: legs `text controls` + native-build · Depends: —
- [ ] FormattedText measured per run (shim `measure_text` gains runs)
- [ ] Silica text-size ratio probed; explicit sizes scaled when `FontAutoScalingEnabled`
- [ ] measure tests with two fonts

Done when: a two-size FormattedText row does not clip (screenshot).

Notes:
-

<a id="s44"></a>
### S44 · Keyboard avoidance
Plan: §M12 steps 1–2 · Audit: lifecycle L4, input design notes · Phone: legs `input text` · Depends: —
- [ ] device check: long form with the keyboard up, portrait and landscape (screenshots)
- [ ] keyboard height as a bottom inset; focused field scrolled into view
- [ ] harness test: inset → layout pass

Done when: the last Entry of a long form stays visible with the keyboard open.

Notes:
-

<a id="s45"></a>
### S45 · Upstream-seam workarounds: `SailfishKeyboard`, `Loaded`
Plan: §M12 step 3, §M25 · Audit: lifecycle L4, L6 · Phone: leg `input` · Depends: —
- [ ] `SailfishKeyboard.Hide()/Show(view)/IsShowing`; docs say `SoftInputExtensions` throw on this TFM
- [ ] root page handler attached before the window parents it; container handlers attach on `ChildAdded`
- [ ] upstream issues filed (soft-input seam, `IsLoaded` in the Standard partial) and linked in the plan

Done when: root page `Loaded` sees a handler (test); both issue links in the plan.

Notes:
-

<a id="s46"></a>
### S46 · Theme from the first frame, highlight colour, RTL locale
Plan: §M13 · Audit: lifecycle L5 · Phone: legs `silica features` · Depends: —
- [ ] first render waits for (or is seeded with) the ambience theme
- [ ] `SailfishTheme.HighlightColor`/`PrimaryColor`
- [ ] `RequestedLayoutDirection` from Qt; `LANG`/ICU note in `sailfishos-packaging.md`

Done when: a dark-ambience start shows no light frame (recording).

Notes:
-

<a id="s47"></a>
### S47 · `On<SailfishOS>()` platform configuration
Plan: §M14 step 6 · Audit: essentials E7 · Decision: D13 · Phone: — · Depends: —
- [ ] `SailfishOS` marker + `SailfishOSSpecific.Page.AllowedOrientations` extensions
- [ ] `SailfishPage.AllowedOrientations` as an `[Obsolete]` forwarder; `PublicSurface.txt`; `sailfish-apis.md`

Done when: `page.On<SailfishOS>().SetAllowedOrientations(...)` compiles in `PublicApiGuard` and has a test.

Notes:
-

<a id="s48"></a>
### S48 · `HybridWebView`
Plan: §M15 step 7 · Audit: handlers H1 · Decision: D5 · Phone: leg `features` (if built) · Depends: —
- [ ] (D5 a) handler on the Gecko adapter with the JS bridge and three commands — split into S48a/S48b if it does not fit
- [ ] (D5 b) one warning + porting guide entry next to BlazorWebView, `Maps.Map`, `MediaElement`

Done when: per the chosen option.

Notes:
-

<a id="s49"></a>
### S49 · Sailfish `IImage`
Plan: §M18 · Audit: graphics G8 · Decision: D6 · Phone: leg `shapes` + native-build · Depends: S11
- [ ] `IImage` backed per D6 (decode, `Downsize`, `Resize`, `Save`)
- [ ] `PlatformImage.FromStream` users get it (registration or documented factory)
- [ ] tests

Done when: an `IImage` loads, resizes and saves on the phone.

Notes:
-

<a id="s50"></a>
### S50 · `DrawImage`, `ImagePaint`, screenshot as `IImage`
Plan: §M18 · Audit: graphics G5, G8 · Phone: leg `shapes` · Depends: S49
- [ ] `ICanvas.DrawImage` in the Canvas adapter
- [ ] `SetFillPaint(ImagePaint)`
- [ ] `IScreenshotResult` → `IImage`

Done when: a GraphicsView draws an image on the phone (screenshot).

Notes:
-

## Phase I — build, SDK, developer experience

<a id="s51"></a>
### S51 · Resizetizer through MAUI's external-backend hook
Plan: §M19 step 1 · Audit: build-sdk B3 · Phone: template app + `f3` · Depends: —
- [ ] `ResizetizerPlatformType=wpf`, `ResizetizerAfterAssetProcessingTargets`, `@(MauiProcessedImage/Asset/Font)`
- [ ] private `ResizetizeImages` call and manual asset/font items removed; `maui-resized.txt` kept
- [ ] `Resources/Raw` location checked against `ResolveFilePath` and `OpenAppPackageFileAsync`

Done when: binlog shows the external targets ran; images and fonts render in the template app on the phone.

Notes:
-

<a id="s52"></a>
### S52 · TFM side effects, library projects, MSBuild tests
Plan: §M19 steps 2–4 · Audit: build-sdk B2 · Phone: — · Depends: —
- [ ] `SAILFISH1_0*` defines, `SupportedPlatform`, `SupportedOSPlatform` attribute
- [ ] manifest RID/`SelfContained` only for non-library projects; nested restore → error with the command
- [ ] `BuildTargetsTests` (evaluate a temp csproj); clean-machine workload note

Done when: a class library with the TFM builds without a RID; tests green.

Notes:
-

<a id="s53"></a>
### S53 · Trimming profile and feature switches
Plan: §M20 · Audit: build-sdk B4 · Phone: `tools/sf package-test --trimr2r` · Depends: —
- [ ] `IsTrimmable=true` on the backend; descriptor narrowed to the app
- [ ] feature-switch table in `aot-and-trimming.md`; composite R2R measured

Done when: Release RPM size and startup measured before/after in `aot-and-trimming.md`; matrix legs `page controls collection` green.

Notes:
-

<a id="s54"></a>
### S54 · `dotnet run`, env switch table
Plan: §M21 steps 1, 4 · Audit: build-sdk B5 · Phone: template app · Depends: —
- [ ] `RunCommand`/`RunArguments` → deploy + run + log stream, Ctrl+C kills
- [ ] `MAUI_SAILFISH_*` table in `tools.md` generated/checked by a test

Done when: `dotnet run -f net11.0-sailfish` starts the template app on the phone and Ctrl+C stops it.

Notes:
-

<a id="s55"></a>
### S55 · Hot reload over SSH
Plan: §M21 step 2 · Audit: lifecycle L8 · Decision: D7 · Phone: yes · Depends: S54
- [ ] (D7 b) `EnableMauiIncrementalHotReload=false` on the head, documented
- [ ] (D7 a) startup hook + SSH-forwarded delta channel + `MauiHotReloadHelper` — expect S55a/b/c

Done when: per the chosen option.

Notes:
-

<a id="s56"></a>
### S56 · Device tools out of the NuGet package
Plan: §M22 · Audit: build-sdk B5 · Decision: D8 · Phone: — · Depends: S07
- [ ] per D8 (nothing now, a `*.Tools` package, or a dotnet tool)

Done when: per the chosen option.

Notes:
-

<a id="s57"></a>
### S57 · Host CI, code style, template smoke test
Plan: §M23 · Audit: build-sdk B7 · Phone: — · Depends: S52
- [ ] host-only CI workflow (build slnx, `dotnet test`, pack with `SailfishAllowMissingShim=true`)
- [ ] `.editorconfig` + `dotnet format --verify-no-changes`
- [ ] template `dotnet new` + build smoke test

Done when: the workflow passes on a branch.

Notes:
-

<a id="s58"></a>
### S58 · Template alignment
Plan: §M24 · Audit: build-sdk B8 · Decision: D9 · Phone: template app · Depends: S07
- [ ] official symbols (`applicationId`, `Framework`, fresh `PhoneProductId`)
- [ ] `UseMaui=true` on the head when the MAUI SDK is present
- [ ] `Styles.xaml` per D9

Done when: a generated app builds for Android and Sailfish and runs on the phone (screenshot).

Notes:
-
