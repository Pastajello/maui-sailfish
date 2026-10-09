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
- [x] [S07](#s07) Rename the NuGet ids · ✅D1 · 📱
- [x] [S08](#s08) Swipe-back: opt-out and immediate re-push · ✅D3 · 📱

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
- [x] [S59](#s59) (new) Unhandled exceptions on the UI thread: crash unless the app handles them · ✅D11 · 📱
- [x] [S60](#s60) (new) Views with gestures keep their drags from Silica (`preventStealing`) · ✅D17 · 📱
- [x] [S61](#s61) (new) A facade only a library references is published but missing from `deps.json` · 📱

**Phase D — page chrome on MAUI's `Toolbar`**
- [x] [S19](#s19) `SailfishToolbarHandler` skeleton · ✅D16 · 📱
- [x] [S20](#s20) `TitleView`, `HasNavigationBar`, `NavBarIsVisible`, `TabBarIsVisible` · 📱
- [x] [S21](#s21) `Shell.SearchHandler`: search field and query · ✅D14 · 📱
- [x] [S22](#s22) `Shell.SearchHandler`: results list and selection · 📱
- [x] [S23](#s23) `FlyoutIsPresented` and flyout-content warnings · ✅D15 · 📱

**Phase E — images, collections, canvas**
- [x] [S24](#s24) Image sources through `IImageSourceService` · 📱
- [x] [S25](#s25) Image loading: `IsLoading`, cancellation, stream cache, GIF, decode size · 📱
- [x] [S26](#s26) CollectionView scrolling behaviours · 📱
- [x] [S27](#s27) Selection and carousel visual states · ✅D4 · 📱
- [x] [S28](#s28) Snap points, `MeasureFirstItem`, lazy templating · 📱
- [x] [S29](#s29) CollectionView inside ScrollView, items parity, vertical loop · 📱
- [x] [S30](#s30) Canvas text anchor, fonts, fill rule, antialias · 📱
- [x] [S31](#s31) `GraphicsView` touch interactions and re-record policy · 📱

**Phase F — legacy ListView and TableView** (D2 (b): the minimal set; `TableView`, context actions and Switch/Entry cells only when a ported app needs them)
- [x] [S32](#s32) Legacy `ListView` handler: cells, tap, selection · ✅D2 · 📱
- [x] [S33](#s33) Legacy `ListView`: grouping, header/footer, separators, row height · 📱
- [x] [S34](#s34) Legacy `ListView`: refresh, context actions, Switch/Entry cells · 📱 (D2 b: refresh only; the rest when an app needs it)
- [ ] [S35](#s35) `TableView` · 📱 (deferred by D2 b)
- [x] [S36](#s36) Legacy list leg, ported apps, docs · 📱

**Phase G — navigation details, dialogs, drag & drop**
- [x] [S37](#s37) `animated:false`, `InsertPageBefore`, `RemovePage` · 📱
- [x] [S38](#s38) Dialogs: queue, thread hop, RTL, keyboard; Detail swap · 📱
- [x] [S39](#s39) Drag & drop: router core and drag ghost · 📱
- [x] [S40](#s40) Drag & drop: rows, leg, docs · 📱

**Phase H — visuals, text, keyboard, theme, platform API**
- [x] [S41](#s41) Gradient backgrounds on every view · ✅D10 · 📱
- [x] [S42](#s42) FormattedText spans: properties and gestures · 📱
- [x] [S43](#s43) Mixed-font measure and `FontAutoScalingEnabled` · 📱
- [x] [S44](#s44) Keyboard avoidance · 📱
- [ ] [S45](#s45) Upstream-seam workarounds: `SailfishKeyboard`, `Loaded` · 📱
- [x] [S46](#s46) Theme from the first frame, highlight colour, RTL locale · 📱
- [x] [S47](#s47) `On<SailfishOS>()` platform configuration · ✅D13
- [x] [S48](#s48) `HybridWebView` · ✅D5 · 📱
- [x] [S49](#s49) Sailfish `IImage` · ✅D6 · 📱
- [x] [S50](#s50) `DrawImage`, `ImagePaint`, screenshot as `IImage` · 📱

**Phase I — build, SDK, developer experience**
- [x] [S51](#s51) Resizetizer through MAUI's external-backend hook · 📱
- [x] [S52](#s52) TFM side effects, library projects, MSBuild tests
- [x] [S53](#s53) Trimming profile and feature switches · 📱
- [x] [S54](#s54) `dotnet run`, env switch table · 📱
- [ ] [S55](#s55) Hot reload over SSH · ✅D7 · 📱 (deferred by D7 b)
- [ ] [S56](#s56) Device tools out of the NuGet package · ✅D8
- [ ] [S57](#s57) Host CI, code style, template smoke test
- [x] [S58](#s58) Template alignment · ✅D9 · 📱

## Decisions

All seventeen were answered on 2026-10-07; the sessions they block are open. Each row is a question for the owner. Write the chosen letter (and a note if needed) in `Answer`, with your initials
and the date, before the sessions in `Blocks` start. Background for each: the plan package named in brackets (§4 of
the plan).

| # | Question | Options | Suggested | Blocks | Answer |
| --- | --- | --- | --- | --- | --- |
| D1 | Which prefix should the NuGet package ids use? nuget.org reserves `Microsoft.*` for Microsoft, so today's `Microsoft.Maui.SailfishOS*` ids can never be published there. (M7) | (a) `Sailfish.Maui.*` (`Sailfish.Maui`, `Sailfish.Maui.Templates`, …)<br>(b) `MauiSailfish.*`<br>(c) your organisation's prefix, e.g. `<Org>.Maui.SailfishOS.*`<br>(d) keep `Microsoft.Maui.SailfishOS*` and ship only through private or local feeds | (a); C# namespaces stay as they are (see D13) | S07, S56, S58 | **(d)**, as maui-labs' GTK backend: keep `Microsoft.*` ids in its pattern `Microsoft.Maui.Platforms.SailfishOS[.Feature]` (see S07). — owner, 2026-10-07 |
| D2 | How far should the legacy `ListView` and `TableView` (MAUI's old, obsolete list controls) be supported? Today they render an empty area. (M4) | (a) full: every cell type, grouping, context actions, `TableView`<br>(b) minimal: `TextCell`/`ImageCell`/`ViewCell`, tap and selection, grouping, pull-to-refresh; the rest documented as limits<br>(c) not supported: one warning and a doc note pointing to `CollectionView` | (b), then (a) when a ported app needs more | S32–S35 | **(b)** — owner, 2026-10-07 |
| D3 | Does a page need an explicit switch to turn off Silica's swipe-back? Since S05 a page that overrides `OnBackButtonPressed` already loses the swipe and its Back veto works. (M2) | (a) add an attached property `SailfishPage.BackNavigation` (false: no swipe-back and no back indicator), on top of S05<br>(b) no new API: the S05 behaviour is enough; document it<br>(c) (a), and make the S05 override detection opt-in instead of automatic | (b); (a) when an app needs the swipe off without overriding `OnBackButtonPressed` | S08 | **(b)** — owner, 2026-10-07 |
| D4 | How should a selected `CollectionView` row look? Today Silica's highlight is drawn under the row content, so a template with an opaque background hides it. (M8) | (a) MAUI's `VisualStateManager` `Selected` state only (the app styles it, as on Android/iOS); no Silica highlight<br>(b) keep Silica's highlight under the content (today)<br>(c) both: the VSM `Selected` state and a Silica highlight drawn above the content | (a), with a before/after screenshot before considering (c) | S27 | **(a)** — owner, 2026-10-07 |
| D5 | Should `HybridWebView` (MAUI's web view with a JavaScript ↔ .NET bridge) work on Sailfish? Today it renders nothing. (M15) | (a) build a handler on the Gecko web view, with the JS bridge<br>(b) unsupported: one warning, documented next to `BlazorWebView` | (b) until an app asks for it | S48 | **(a)**: build the Gecko-backed handler — owner, 2026-10-07 |
| D6 | What should back MAUI's `IImage` (`PlatformImage.FromStream`, `canvas.DrawImage`, image resize)? Today these throw or draw nothing. (M18) | (a) `QImage` in the native shim: every app gets it, no extra package<br>(b) Skia, only inside the SkiaSharp package<br>(c) none, documented | (a) | S49 | **(a)** — owner, 2026-10-07 |
| D7 | How much should go into hot reload (changing XAML/C# in the running app on the phone)? (M21) | (a) build a delta agent over SSH (MAUI's hot reload into the app on the phone)<br>(b) none for now: redeploy with `dotnet build -t:SailfishRun` | (b) now, (a) together with the VS Code extension work | S55 | **(b)** — owner, 2026-10-07 |
| D8 | Where should the `tools/sf` scripts (deploy, run, screenshots, matrix) live for people who use the package? Today they are bash, inside the package. (M22) | (a) a separate `*.Tools` NuGet package<br>(b) rewritten as a C# dotnet tool (works on Windows too)<br>(c) keep them as they are (macOS and Linux build machines only) | (c) now, (b) before a public release | S56 | **(b)**: a C# dotnet tool — owner, 2026-10-07 |
| D9 | Should the app template keep MAUI's default `Styles.xaml` colours, or let Sailfish's ambience colours show? (M24) | (a) keep MAUI's colours on Sailfish too (looks like the other platforms)<br>(b) wrap the Button/Entry/Label colours in `OnPlatform`, so Sailfish uses the ambience palette<br>(c) take the colours out of the template's styles entirely | (b) | S58 | **(b)** — owner, 2026-10-07 |
| D10 | How should gradient backgrounds (`LinearGradientBrush`/`RadialGradientBrush` as `Background`) be drawn? (M10) | (a) a shader rectangle under the element<br>(b) a QML `Canvas` | (a): GPU-drawn, no repaint on every frame | S41 | **(a)** — owner, 2026-10-07 |
| D11 | What should happen when app code run through the dispatcher throws? Today the exception is logged and the app goes on. (L7) | (a) log and continue (today)<br>(b) crash, as Android does<br>(c) log and continue by default; `MAUI_SAILFISH_CRASH_ON_DISPATCH_ERROR=1` makes it crash (used in the matrix) | (c) | S59 | **(b)**: crash by default, with a handler the app can use to decide to continue or rethrow (see S59) — owner, 2026-10-07 |
| D12 | Should unit tests be able to run on the phone with `dotnet test`? (M21) | (a) yes: a Microsoft.Testing.Platform runner packed into the RPM<br>(b) no: the device matrix (`tools/sf matrix`) stays the on-device test | (b) | — | **(b)** — owner, 2026-10-07 |
| D13 | In which namespace should Sailfish's platform-specific APIs (`On<SailfishOS>()…`) live? (M14) | (a) `Microsoft.Maui.Controls.PlatformConfiguration.SailfishOSSpecific`, like Android/iOS/Windows and the GTK and Tizen backends<br>(b) `Sailfish.Maui.PlatformConfiguration`, following a D1 rename | (a), unless D1 also renames the C# namespaces | S47 | **(a)** — owner, 2026-10-07 |
| D14 | How should `Shell.SearchHandler` (the search box in a Shell page's title bar) work? Today it is ignored. (M16) | (a) a Silica `SearchField` under the page header with a results list below it (query, suggestions, selection)<br>(b) only the search field: query events, no results list<br>(c) unsupported: one warning, documented | (a): search is the core of some Shell apps | S21, S22 | **(a)** — owner, 2026-10-07 |
| D15 | What should `Shell.FlyoutIsPresented = true` (opening the flyout from code) do? On Sailfish the flyout is the pull-down menu, which cannot be opened from code. (M16) | (a) open a Silica context menu with the flyout entries<br>(b) push the flyout entries as a page of their own (as `FlyoutPage.IsPresented` does)<br>(c) open the pull-down menu by animating the page (no Silica API; may look odd)<br>(d) one warning, nothing happens | (a) | S23 | **(a)** for now; revisit once S19–S23 are in — owner, 2026-10-07 |
| D16 | How should the page chrome (title, toolbar items, back button, `TitleView`, hiding the navigation bar) be driven? (M16) | (a) a full `IToolbar` handler, as MAUI 11 does on the other platforms, replacing the renderer's direct reads of `Page`<br>(b) keep the renderer and patch it for `HasNavigationBar`, `TitleView` and `ToolbarItem.Priority` only | (a): one place for all chrome, matches MAUI 11 | S19, S20 | **(a)** — owner, 2026-10-07 |
| D17 | Should a view with MAUI gestures stop Silica from taking over its drag? Today a horizontal pan also drags the page back and a vertical pan at the top opens the pull-down menu. (M17) | (a) yes: views with gesture recognizers keep their drags (`preventStealing`), with a per-view opt-out<br>(b) yes, always, no opt-out<br>(c) no: keep observing only, document the conflicts | (a); CollectionView rows already do this since S17 | S60 | **(a)** (see S60) — owner, 2026-10-07 |

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
Plan: §M7 · Audit: build-sdk B1 · Decision: D1 (d) · Phone: template app via `SailfishRun` · Depends: S06

D1 (d) follows maui-labs (`Microsoft.Maui.Platforms.Linux.Gtk4`, `…Gtk4.Templates`, `Microsoft.Maui.Platforms.MacOS`,
`…MacOS.Essentials`): `Microsoft.Maui.SailfishOS` → `Microsoft.Maui.Platforms.SailfishOS`, `Microsoft.Maui.SailfishOS.SkiaSharp` →
`Microsoft.Maui.Platforms.SailfishOS.SkiaSharp`, `Microsoft.Maui.SailfishOS.Workload` → `Microsoft.Maui.Platforms.SailfishOS.Workload`,
the workload manifest `microsoft.maui.sailfishos.Manifest-<band>` → `microsoft.maui.platforms.sailfishos.Manifest-<band>`. The
templates package (`Microsoft.Maui.Platforms.SailfishOS.Templates`) already follows it. C# namespaces and assembly names stay (D13 a).
- [x] the `PackageId`s renamed as above
- [x] `Linux.SailfishOS.Workload/Program.cs:16`, `tools/cmd/pack-local.sh`, manifest `packs` key, template.json, README, `add-sailfish-to-existing-app.md`
- [x] `ToolsPackagingTests`, `WorkloadToolTests` updated

Done when: workload install + template app with the new ids runs on the phone.

Notes:
- 2026-10-07 ✅. Package ids follow maui-labs (D1 d): `Microsoft.Maui.Platforms.SailfishOS`, `.SkiaSharp`, `.Workload`,
  `microsoft.maui.platforms.sailfishos.Manifest-<band>` (manifest id and its `packs` key); `.Templates` was already
  named so.
  - The `buildTransitive` targets are renamed with them (`Microsoft.Maui.Platforms.SailfishOS.targets`,
    `…SkiaSharp.targets`), since NuGet imports `<PackageId>.targets`. The sample's direct import is updated as well.
  - The implicit `PackageReference` in `WorkloadManifest.targets`, the PackageReferences of the template, Kitchen and
    SkiaSharpProbe, the scripts (`pack-local`, `workload-install`, `doctor`, `detect`, `sf-rpmbuild.py`), the live docs
    and tests are updated.
  - C# namespaces and assembly names are unchanged (D13 a). The historical docs (architecture plan/handoff, audits,
    this plan's M7 text) keep the old names.
- Upgrade path: an SDK still has the old `sdk-manifests/<band>/microsoft.maui.sailfishos` manifest, which defines the
  same "sailfish" workload as the new one (the SDK refuses two manifests for one workload).
  `sailfish-workload install`/`uninstall` (`LegacyManifestId`) and `tools/sf workload-install` remove it.
- Verified on this machine: `tools/sf pack-local` writes the new nupkgs. `dnx Microsoft.Maui.Platforms.SailfishOS.Workload
  install` removed the old manifest and installed the new one. The solution builds with the samples restoring the
  new ids from the feed.
  - A fresh `dotnet new maui-sailfish --sailfish-only` app (PackageReference `Microsoft.Maui.Platforms.SailfishOS`) ran
    on the phone through `SailfishRun`: `docs/screenshots/maui11/s07-template-new-ids.png`.
  - `tools/sf matrix page features skia` 3/3 PASS (skia consumes both renamed packages from the feed). Host 486 green.
- Left on the phone: the test package `harbour-s07app` (and `harbour-harbourcheck` from S06).
- Other machines need `tools/sf pack-local` (or the new feed) and the new workload install; the VS Code extension repo
  (`~/Projects/sailfishos_maui_tools`) names no package id (grep).
-

<a id="s08"></a>
### S08 · Swipe-back: opt-out and immediate re-push
Plan: §M2 step 2, §M27 step 1 · Audit: navigation N4 · Decision: D3 · Phone: legs `nav navback shell` · Depends: S05
- [x] ~~`SailfishPage.BackNavigation` attached property~~ — not built, D3 (b): a page that overrides `OnBackButtonPressed` turns the swipe off (S05); document it in `sailfish-apis.md`
- [x] a vetoed follow pop (depth unchanged) re-pushes at once instead of after the 3 s deadline
- [x] tests (harness may need the follow pop to complete outside the poll, see handoff W1.2)

Done when: a vetoed swipe returns the page without the 3 s gap; `sailfish-apis.md` names the `OnBackButtonPressed` opt-out.

Notes:
- 2026-10-07 ✅. `NativeStackCoordinator.Step`: a FollowNative operation whose MAUI pop finished (`MauiDone`) while the
  MAUI depth stayed above the native one, with the native stack equal to the mirror (`FollowVetoed`), ends at once
  (counter `FollowVetoes`, nav log `VETO`). The depth sync in the same step re-pushes the page. Before, the step
  waited for the operation's 3 s deadline and a resync re-pushed it (`NavResyncs` +1).
- D3 (b): no attached property. `sailfish-apis.md` gains "The back gesture" (the `OnBackButtonPressed` override,
  `Shell.Navigating` cancel, `HasBackButton`/`BackButtonBehavior`).
- Host: `NativeStackSyncTests.A_vetoed_back_gesture_brings_the_page_back_at_once` (Shell `Navigating` cancels
  `Pop`/`PopToRoot`; a back from depth 2 arrives as `PopToRoot`); fails without the fix; 487 green. The harness completed
  the follow pop inside its polls, so the W1.2 caveat did not bite.
- Device: shell leg C2 pushes the detail again, cancels the native back through `Shell.Navigating`, and finds the
  detail back on screen within 1 s (section 2, native depth 2, follow vetoes +1, resyncs +0). It then removes the
  handler and backs out normally. `tools/sf matrix shell nav navback` 3/3 PASS. No screenshot: nothing new to see
  (the same page comes back).
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
- 2026-10-09: a sandboxed launch exists now. `tools/remote/sf-run-remote.sh` starts `sailjail -p <pkg> -- /usr/bin/<pkg>` when the forwarded environment has `SF_SAILJAIL=1` (`SF_MATRIX_EXTRA_ENV="SF_SAILJAIL=1"` for the matrix; the `sailfish` tool embeds the same helper). `verify.sh` knows the Harbour layout (`/usr/share/<pkg>/lib`): L1/D4/D6 failed on it before.
  - The sandboxed f4 runs:
    1. Harbour build (`SF_PUBLISH_PROPS=-p:SailfishHarbour=true`, permissions Internet only): it ran inside Sailjail, 42/46. The 4 failures are the sandbox working as designed: Contacts → `PermissionException` naming `Contacts`; Flashlight unsupported (no Sailjail permission reaches the torch); accelerometer no readings (no `Sensors`); Geolocation `PermissionException` (no `Location`).
    2. The f4 flashlight check now expects "unsupported" when `SailfishPermissions.IsSandboxed`.
    3. A sandboxed build with `Contacts;Location;Sensors` (`-p:SailfishSandboxing=true -p:SailfishPermissions=Contacts%3BLocation%3BSensors`): no output, gone after ~80 s. sailjaild `GetLaunchAllowed(100000, harbour-sample)` = 0 (undecided): new permissions need the user's consent once (app-grid launch → allow), and a launch over SSH waits for it.
  - Left: the user approves the permissions on the phone (the sandboxed build is installed), then `SF_MATRIX_EXTRA_ENV="SF_SAILJAIL=1" tools/sf matrix f4`. Not granted from here through sailjaild's `SetLaunchAllowed`/`SetGrantedPermissions`: that is the owner's security setting.

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

<a id="s59"></a>
### S59 · (new) Unhandled exceptions on the UI thread: crash unless the app handles them
Plan: §M1 (lifecycle audit L7) · Audit: lifecycle L7 · Decision: D11 (b) · Phone: leg `error` · Depends: —

D11 (b): today an exception thrown by app code that runs on the Qt loop (a dispatched action, a timer, an event handler
called from a native event) is logged and the app carries on in an unknown state. Android ends the app instead.
- [x] one public hook, raised on the UI thread with the exception and a `Handled` flag (name and place decided in the
  session, next to `SailfishMauiApplication`'s lifecycle events); `AppDomain.UnhandledException` still fires for a crash
- [x] not handled → the app ends (the exception and its stack in the log, a non-zero exit), as on Android; handled →
  the loop continues, as today
- [x] every place that catches and logs app exceptions on the loop goes through it (dispatcher, timers, adapter event
  routing, the input router's gesture dispatch, `QtThread`)
- [x] host tests: handled continues, unhandled ends (a test seam instead of a real exit); `error` leg: a handled
  exception from a button handler keeps the app running
- [x] docs: `sailfish-apis.md` (the hook), `porting-existing-apps.md` (crash instead of log)

Done when: on the phone an unhandled exception in a Clicked handler ends the app with the stack in the log, and with
the hook set to handled the app goes on.

Notes:
- 2026-10-07 ✅. Public API in `Microsoft.Maui.SailfishOS.Platform` (PublicSurface.txt regenerated):
  - `SailfishExceptions.Unhandled` (static event);
  - `SailfishUnhandledExceptionEventArgs` (`Exception`, `Source`, `Handled`).
  - `SailfishExceptions.Report(ex, source)` logs the exception, raises the event, and returns only when a handler set
    `Handled`. A handler that throws does not keep the app.
- How the app ends: the exception is marked (`Exception.Data`) and rethrown with its stack. Outer catch sites see the
  mark and pass it on, so it reaches the native Qt callback, where the runtime treats it as unhandled: "Unhandled
  exception.", `AppDomain.UnhandledException`, process end.
  - **Found on the phone:** the first version rethrew it on a new `Thread`. The sample's trimmed RPM ships
    `System.Threading.Thread.dll`, but its `deps.json` did not list it, so the runtime could not load it
    (`FileNotFoundException`) and the app ended for that reason instead. See S61.
- Routed through it: the dispatcher queue (`DrainQueue`), dispatcher timers (`TickDueTimers`), the shim's
  pointer/key/QML-event/tick callbacks (`QtHostRuntime`), adapter event routing (`AdapterEventRouter.Handle`), the input
  router's Tapped/Pointer/LongPress/Pinch dispatch, and service event subscribers (`QtHostServices`). `QtThread.RunAsync`
  keeps handing exceptions to its awaiting caller. Quit-time lifecycle failures stay logged.
- Host: `UnhandledExceptionTests` (4: dispatched work ends the app, a handler keeps it, a throwing handler does not, a
  throwing Clicked ends it). The test assembly's module initializer (`TestCrashes`) swaps the process end for a
  recorder. 491 green.
- Device:
  - The `error` leg gains leg F: a handled exception from dispatched work is seen by the hook and the loop goes on; the
    error budget counts it. `tools/sf matrix error` PASS.
  - `MAUI_SAILFISH_DIAG_CRASH=1` (with `MAUI_SAILFISH_QT_HOST_DIAG=1`) throws from a Clicked handler 2 s after the first
    page. The device log shows the exception, "ending the app", `CRASH AppDomain`, then the runtime's "Unhandled exception."
    with the original exception, and the app process ended.
- Full matrix after S59 (unhandled exceptions now end the app, so a leg that used to swallow a backend error would end
  early): page, controls, nav, popup, collection, collection10 PASS. Stopped there because the phone's screen had gone
  off (`svc-display` state 2) and each leg took ~15 min.
- 2026-10-07 (evening, screen on): the other 25 legs, 23/25 PASS. The two failures were rerun and passed (3/3 on
  2026-10-08), so all 31 legs pass after S59. No leg ended through an unhandled exception.
  - collection100's list host never attached on its first run, the first leg after the screen woke; both reruns passed.
  - f3's S25 GIF check read the frame twice 400 ms apart and hit the same frame after a whole loop of the 3-frame GIF.
    It now samples four times, 120 ms apart.

<a id="s61"></a>
### S61 · (new) A facade only a library references is published but missing from `deps.json`
Plan: §M23 (payload) · Audit: build-sdk · Phone: an RPM with a library that uses `Thread` · Depends: —

Found in S59: `Microsoft.Maui.SailfishOS` used `new Thread(…)` (its first use of a type in the
`System.Threading.Thread` facade). The trimmed Release publish of the sample copied `System.Threading.Thread.dll`, but
`Linux.SailfishOS.Sample.deps.json` did not list it. At the first use the runtime threw `FileNotFoundException:
System.Threading.Thread, Version=11.0.0.0`. Any NuGet library a Sailfish app uses can hit it with any facade-only type.
- [x] reproduce: a library (or the backend in a branch) that uses `Thread`, published with `SailfishTrim=true`
  (partial); compare the publish folder with the `deps.json` runtime entries (S59 used a short Python check)
- [x] find which step drops the entry (ILLink's trimmed list feeding `GenerateDepsFile` while the facade is kept as a
  reference of a `copy` assembly) and fix it in the targets, or list such facades explicitly
- [x] a build test: every `.dll` in the publish folder is in `deps.json`

Done when: an RPM whose library uses `Thread` runs that code on the phone.

Notes:
- 2026-10-08: **cause: an incremental publish, not the trimmer's list.**
  - A clean trimmed publish is right. A template app (net11.0-sailfish, package consumer) with a `ThreadLib` library using `new Thread`, trimmable or not, gave 193 DLLs, all in `deps.json`, in both the publish folder and the RPM. A clean publish of the Sample with a `Thread` probe also listed `System.Threading.Thread.dll`.
  - The bug: publish the Sample once without the probe, then incrementally with it. ILLink keeps the facade (59 DLLs) but `deps.json` stays the old one (58 entries). The SDK's `GeneratePublishDependencyFile` is incremental on `ProjectAssetsFile`, `ProjectAssetsCacheFile`, `MSBuildAllProjects` and a property hash, never on the trimmer's output. `sf deploy` always publishes incrementally; S59's change was exactly that case.
- Fix: `buildTransitive/…targets` adds `_SailfishRegenerateTrimmedDepsFile` (`BeforeTargets=GeneratePublishDependencyFile`, `PublishTrimmed` and not AOT), which deletes `$(IntermediateDepsFilePath)` so `deps.json` is rebuilt (under a second). Checked: probe-less publish, then incremental with the probe: 59 DLLs, the facade listed.
- Build check: `tools/py/sf-depscheck.py <publish-dir> <app>` (exit 1 with the unlisted names; negative case checked). `sf_publish_rpm` runs it after every publish, so `sf deploy` and `sf package-test` stop on a gap ("publish payload: 58 assemblies, all in Linux.SailfishOS.Sample.deps.json"). Packed with the tools (`Linux.SailfishOS.csproj` py list); `tools/sf pack-local` re-run.
- Phone: the Sample with a temporary `ModuleInitializer` probe (`new Thread` when `S61_PROBE=1`), deployed incrementally, printed `S61-THREAD-OK`, and the header leg passed 18/18 in the same run. The probe file was removed afterwards. The plain `sf run` try never activated the window (display blanked, `sf run` does not wake it); `sf shots` does.
- Docs: `aot-and-trimming.md` risks.

<a id="s60"></a>
### S60 · (new) Views with gestures keep their drags from Silica (`preventStealing`)
Plan: §M17 step 10 · Audit: input (Silica conflicts) · Decision: D17 (a) · Phone: legs `input shell pulley` · Depends: S17

D17 (a): a horizontal pan on a MAUI view also drags the page back, and a vertical pan at the top also opens the
pull-down menu, because the router only observes Qt's events. CollectionView rows already hold their drag (S17,
`mauiHoldRow`).
- [x] a view with Pan/Swipe/Pinch recognizers keeps a drag the router captured: its host's `MouseArea { preventStealing }`
  (pushed while captured), or the page's `backNavigation`/the flickable's `interactive` held for the drag
- [x] a per-view opt-out (attached property under the D13 namespace)
- [x] router/harness tests; `input` leg: a horizontal pan does not move the page, a vertical pan at the top does not open
  the pulley; with the opt-out, Silica gets the drag again
- [x] docs: `porting-existing-apps.md`, `silica-parity.md`

Done when: the two `input` leg checks pass on the phone.

Notes:
- 2026-10-08: the page-level hold, not `preventStealing`: plain MAUI hosts have no MouseArea of their own (the router watches Qt's events from the window).
  - `QtHostInputRouter.OnPress`, after a capture whose owner has Pan, Swipe or Pinch recognizers and `KeepsDrag` true, calls `QtHostPageRenderer.HoldPageDrag(true)`. That is page call `mauiHoldDrag`, released in `ClearCapture`; the counter is `PageDragHolds`.
  - QML `MauiModelPage.mauiHoldDrag(arg)`: page calls pass a string, so `"false"` must not read as true (caught by the harness test). It sets `backNavigation` false and `mauiDragHeld`; the flickable's `interactive` is `mauiScrollEnabled && !mauiDragHeld`. The back setting goes back to the page, or to a dialog that opened meanwhile (`__dialogBack`); a `back` op during the hold goes to `__heldBack`.
  - Row captures keep S17's `mauiHoldRow`.
- Opt-out: `Microsoft.Maui.Controls.PlatformConfiguration.SailfishOSSpecific.VisualElement.KeepsDrag` (attached, default true), the first type in the D13 namespace; S47 adds the `On<SailfishOS>()` form. PublicSurface.txt regenerated.
- Tests: `Renderer/RouterDragHoldTests.cs` (3): a pan holds from press to release (true, false); a swipe does too and a tap does not; KeepsDrag=false holds nothing. `FakeShim` knows `mauiHoldDrag`. Suite 520 green.
- Phone: `input` leg, new step `RunQtDragHoldChecks` (`QtHostDiagnosticsRunner.DragHold.cs`): a pushed page with a pulley, two full-width boxes with Pan, the second KeepsDrag=false. Results:
  - a real edge swipe (x 6→560 px) over the first pans it (14 updates) and the page stays (depth 2, page x mid-swipe 0);
  - a pull down over it pans it and the flickable stays at 0 mid-pull (pulley shut);
  - the same edge swipe over the second pans it and goes back (depth 1).

  `router-checks=13/13`. The lines were read from the device log: `sf run`'s capture had stopped earlier. Screenshot `input-drag-held` (the page in place after the swipe) checked.
- Docs: porting guide, silica-parity row, `sailfish-apis.md` (back gesture section).
- Regression: `tools/sf matrix input navback shell pulley tabpulley controls collection features header` 9/9 PASS (the first try was cut when the previous session ended; rerun detached).

## Phase D — page chrome on MAUI's `Toolbar`

<a id="s19"></a>
### S19 · `SailfishToolbarHandler` skeleton
Plan: §M16 steps 1–2 · Audit: navigation N1 · Decision: D16 · Phone: legs `page nav shell pulley` · Depends: S05
- [x] device check: 0-height header keeps Canvas shapes painting (`QtHostPageRenderer.cs:1221-1223`); screenshot — done in S20 (leg `header`, shot `header-2-hidden`)
- [x] `SailfishToolbarHandler` created from `SailfishWindowHandler.MapContent`; keys `Title`, `IsVisible`, `BackButtonVisible`, `ToolbarItems`
- [x] `PageChromeOps` reads the toolbar; `WatchToolbarItems` and direct `Page.ToolbarItems` reads removed; Priority order and Shell-level items
- [x] harness tests for each key

Done when: pulley entries come from the toolbar (Priority order, Shell items) and all chrome legs stay green.

Notes:
- 2026-10-08: `Handlers/SailfishToolbarHandler.cs` (`ElementHandler<IToolbar, object>`, no native view). Its keys (`Title`, `IsVisible`, `BackButtonVisible`, `ToolbarItems`, `TitleView`, `BackButtonEnabled`, `DrawerToggleVisible`) all ask the session for a poll; the chrome is still built by the renderer, now from the toolbar. `SailfishHandlersFactory` resolves any stock `IToolbar` (NavigationPageToolbar, ShellToolbar) to it unless the app registered its own; `AttachToolbarHandler` connects it.
- The toolbar is mapped from `IToolbarElement.Toolbar` on both the window (`SailfishWindowHandler.MapToolbar`) and the page (`SailfishPageHandler.MapToolbar`): MAUI puts the Shell's toolbar on the Shell, not on the window.
- Renderer: `ToolbarItemsOf(page)` takes the toolbar's items (Priority sorted by MAUI's `ToolbarTracker`, Shell items included) when they cover the page's own items, else the page's own items sorted by Priority. `ToolbarOf(page)` returns the toolbar only for the root stack's top page and not while a modal is open, so a stale toolbar never lends its items to another page. `AddSyntheticHosts`, `WatchToolbarItems`, `ApplyToolbarActivated` and `RefreshAncestorOf` use it. `WatchToolbarItems` stays as the change trigger; it no longer reads `Page.ToolbarItems` directly.
- Title/back still come from the page (same values the toolbar carries); S20 moves them with `TitleView`/`HasNavigationBar`.
- Tests: `tests/.../Renderer/ToolbarHandlerTests.cs` (handler attached, Priority order, Shell items, runtime add); 495 green; PublicSurface.txt regenerated.
- Phone: `tools/sf matrix shell page nav pulley tabpulley silica` 6/6 PASS. New check in leg E3: pulley `[Alpha,Beta,SH early,SH global,SH late]`, the page's P1/P5 items around the Shell's P3 item in Priority order.

<a id="s20"></a>
### S20 · `TitleView`, `HasNavigationBar`, `NavBarIsVisible`, `TabBarIsVisible`
Plan: §M16 steps 3, 5 · Audit: navigation N1, N3 · Phone: legs `page shell tabpulley` · Depends: S19
- [x] `TitleView` hosted in the chrome
- [x] `HasNavigationBar=false` / `NavBarIsVisible=false` through the header path checked in S19
- [x] `Shell.TabBarIsVisible` per page hides the tab row

Done when: screenshots of a TitleView page, a header-less page and a tab-less Shell page.

Notes:
- 2026-10-08: new op `header {on, title}`. QML wraps the `PageHeader` in `mauiHeaderBox`: hidden = 0 high and clipped, never destroyed (the Canvas-shapes rule). The geometry report adds `titleHeight` (the header band).
- Renderer: `HeaderShownOf` (HasNavigationBar on the page chain, else `Shell.NavBarIsVisible` via `ShellValue` — the nearest of page → ShellContent → section → item → Shell). `TitleViewOf` (NavigationPage.TitleView on the chain, else Shell.TitleView; none while the header is hidden). The TitleView is walked after the page (Walk skips it as a page child, so it gets one host) and arranged in `_titleRectDp`; the title text is cleared under it.
- Shell `TabBarIsVisible` false (nearest setting, the current page first): the sections' row gives way to the section's contents (`SectionTabsShown`).
- Page handler mapper keys for the four attached properties; `ReArmPageChrome` resets the header op.
- Tests: `Renderer/HeaderChromeTests.cs` (8); suite 503 green.
- Phone: new leg `header` (`MAUI_SAILFISH_QT_HOST_HEADER_DIAG`), 10/10, screenshots `header-1-titleview` … `header-5-tabbar-shown` checked: the TitleView label and button in the header band (a real tap clicks the button); with the header collapsed the Ellipse/RoundRectangle/BoxView paint and the content starts at the top; bar back on at runtime; the tab-less Shell page shows only the contents row. `tools/sf matrix header page nav shell tabpulley pulley silica` 7/7 PASS.
- Docs: porting guide (header, TitleView, TabBarIsVisible bullets), silica-parity row, tools.md legs list.

<a id="s21"></a>
### S21 · `Shell.SearchHandler`: search field and query
Plan: §M16 step 4 · Audit: navigation N2 · Decision: D14 · Phone: leg `shell` · Depends: S19
- [x] search field under the header from `Toolbar`/`SearchHandler`; `Query` two-way; `Placeholder`; `SearchBoxVisibility`; `Command` on submit
- [x] warn once for unsupported members
- [x] harness test: query write-back

Done when: typing in the field updates `Query` on the phone (screenshot).

Notes:
- 2026-10-08: `Platform/QtHost/QtHostPageRenderer.Search.cs`. `SearchHandlerOf(page)` = `Shell.GetSearchHandler(page)` as MAUI's Shell toolbars read it, for a page inside a Shell. It is null when `SearchBoxVisibility.Hidden` or when the header is hidden (the other platforms carry the box in the navigation bar). It is read from the page, not from `Toolbar`: MAUI's toolbars carry no search handler. New op `search {on, placeholder, enabled, text}`; `text` is null unless the app changed `Query`. The field's own keystrokes are never echoed back (`_searchNativeText`), so fast typing does not jump. `Collapsible` shows the field expanded (Silica has no search icon that opens it).
- QML: `mauiSearchBox` with a Silica `SearchField` between `headerBox` and the tab rows, counted in `topInset`/`headerHeight`. Events: `search-changed {text}` sets `Query` (MAUI runs `OnQueryChanged`); `search-submit` (enter key) sets `Query`, then `ISearchHandlerController.QueryConfirmed()` runs `Command(CommandParameter)`/`OnQueryConfirmed`.
- The handler's `PropertyChanged` (Query, Placeholder, IsSearchEnabled, SearchBoxVisibility) asks for a poll; page mapper key `Shell.SearchHandlerProperty`. One warning per handler lists the set members a SearchField does not take: icons, colours, fonts, alignment, `TextTransform`, `Keyboard`. The results-list members are left for S22.
- Tests: `Renderer/SearchHandlerTests.cs` (7): placeholder, write-back without echo, the app's Query, Command on enter, IsSearchEnabled/Hidden at runtime, a hidden navigation bar, no Shell. Suite 510 green.
- Phone: leg `header` steps 6–7 (14/14). A real tap focuses the field, injected keys "kiwi" reach `Query`, Return runs Command with "kiwi", and `Query = "pear"` from code fills the field. Screenshots `header-6-search` (keyboard up, "kiwi") and `header-7-search-set` were checked. The leg is `header`, not `shell` as planned: it already ends on a Shell.
- Docs: porting guide bullet, silica-parity row.

<a id="s22"></a>
### S22 · `Shell.SearchHandler`: results list and selection
Plan: §M16 step 4 · Audit: navigation N2 · Phone: leg `shell` · Depends: S21
- [x] `ShowsResults` + `ItemsSource`/`ItemTemplate` as a dropdown on the list adapter
- [x] `SelectedItem` / `OnItemSelected`
- [x] sample `SearchHandler` filtering a list (in the `header` leg and the porting guide; see Notes)

Done when: the sample filters and selects on the phone.

Notes:
- 2026-10-08: `QtHostPageRenderer.Search.cs`. `SearchResultsViewOf(page)` builds a page overlay: a `Grid` holding a `CollectionView` (Single selection). It shows while `ShowsResults` is set and `ItemsSource` has items. A pick or the enter key closes it until `Query` changes. It uses the handler's `ItemTemplate`, else a Label row bound to `DisplayMemberName` or `.` (`stringFormat "{0}"`; IL2026 suppressed, as MAUI's own default template does it). The overlay's `Parent` is the page (BindingContext, handler context), but it is not a logical child. It is walked after the TitleView and arranged in `_contentRectDp`; `CollectTitleView` became `CollectPageOverlays` (TitleView + results). It rides the normal list adapter (`list-view`).
- The handler's `PropertyChanged` (ShowsResults, ItemsSource, ItemTemplate, DisplayMemberName) and an observable `ItemsSource`'s `CollectionChanged` ask for a poll.
- A tapped row → `ISearchHandlerController.ItemSelected`: `OnItemSelected`, `SelectedItem`, then the query confirmed, as on Android/iOS. The list closes and the search op carries `blur` (the field lets go of the keyboard).
- Bug found on the phone and fixed: the first background (black, alpha 215) let "header search body" show through the rows. It is now opaque (`#0A0C0E` dark, `#FAFAFA` light).
- Tests: `SearchHandlerTests` +3 (list over the content: same top, after it in the walk, 2 rows; a pick selects and closes it and blurs the field; typing on reopens it; an observable source and ShowsResults at runtime; the enter key closes it). Suite 513 green.
- Phone: leg `header` 16/16. `HeaderSearchHandler` filters `[apple, apricot, kiwi, peach, pear, plum]` on Query: "kiwi" typed gives 1 row and Return closes it; `Query = "p"` gives 5 rows; a real tap on row 3 picks "pear" (`OnItemSelected`, `SelectedItem`) and the list closes. Screenshots `header-6-search` (kiwi row under the field), `header-7-search-results` (5 rows, opaque), `header-8-search-picked` were checked. `tools/sf matrix header page nav shell containers pulley tabpulley silica collection collection100` 10/10 PASS.
- Sample: no sample app has a Shell root (Kitchen and Sample use a NavigationPage), so the filtering handler lives in the `header` leg and as a code example in the porting guide.
- Docs: porting guide (results list + example), silica-parity row.

<a id="s23"></a>
### S23 · `FlyoutIsPresented` and flyout-content warnings
Plan: §M16 step 6 · Audit: navigation N3 · Decision: D15 · Phone: legs `shell pulley` · Depends: —
- [x] `FlyoutIsPresented=true` from code opens the flyout entries (D15) and writes back `false` on close
- [x] one warning per Shell for `FlyoutHeader/Footer/Content/ItemTemplate`; porting guide "flyout = pulley, text only"

Done when: a "menu" button in a sample opens the entries on the phone.

Notes:
- 2026-10-08: `QtHostPageRenderer.Flyout.cs`.
  - After each pass, `SyncShellFlyoutMenu` (called from `FinishPass`) opens the root Shell's `FlyoutEntries` (the pulley's flyout entries) when `FlyoutIsPresented` is true, through the page's context-menu host. The host is now also created while `ShellFlyoutWantsMenu`.
  - QML: `__openFlyoutMenu(items)` opens it on the context-menu stand-in at the top of the visible content (0 high, full width; the anchor creation was factored out to `__openContextMenuAnchor`); `__closeFlyoutMenu()` closes it.
  - A pick runs the entry (`ActivateShellFlyoutRow`, kept for a late pick). `context-closed` sets `FlyoutIsPresented = false`. `FlyoutIsPresented = false` from code closes the menu: the close is asked once, then `context-closed` follows. A model-page switch closes it too.
  - Navigation waits while either menu is open (`AnyMenuOpen`).
  - With no entries (a disabled flyout, a modal on top), FlyoutIsPresented is set back to false, with one warning.
- One warning per Shell lists the set flyout members the pulley does not show: header/footer/content and their templates, `ItemTemplate`, `MenuItemTemplate`, background, icon, size, backdrop.
- Page handler mapper key `FlyoutIsPresented`.
- Tests: `Renderer/ShellFlyoutPresentedTests.cs` (4): open with both entries, once; a pick switches the item and the close writes back false; a dismiss writes back; false from code closes once and the menu reopens later; a disabled flyout is not presented. Suite 517 green.
- Phone: leg `header` steps 9–10 (18/18). A real tap on an "HD menu" button (`FlyoutIsPresented = true`) opens a Silica ContextMenu with "HD flyout one/two" under the header. A real tap on "HD flyout two" switches the item and FlyoutIsPresented is false again. Screenshots `header-9-flyout-menu` and `header-10-flyout-picked` were checked. The "menu" button lives in the leg: no sample has a Shell root.
- Regression after the ContextMenu QML refactor: `tools/sf matrix controls shell pulley containers tabpulley features` 6/6 PASS (long-press menus in `controls`).
- Docs: porting guide bullet (flyout = pulley, text only; FlyoutIsPresented), silica-parity row.

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
- [x] `ItemsUpdatingScrollMode` (`KeepLastItemInView`, `KeepScrollOffset`)
- [x] `ItemsLayout.PropertyChanged` (span/spacing on the same object)
- [x] `ScrollTo(animate:)`; `Scrolled` deltas; dead `IScrollViewController` cast removed
- [x] tests per item

Done when: the sample's chat-like list keeps the last item in view on the phone.

Notes:
- 2026-10-08:
  - `ItemsUpdatingScrollMode` is pushed as `mauiUpdatingMode` (CollectionView only; a carousel moves by pages) and is a watched ItemsView property. `ListView.qml.__rebuildRows` reads the count and offset before the keyed diff; afterwards `KeepLastItemInView` calls `positionViewAtEnd()` when rows were added, and `KeepScrollOffset` puts back `contentY − originY`. `KeepItemsInView` is Qt's own behaviour.
  - `ItemsLayout`: the adapter subscribes to the layout object's `PropertyChanged` (Span, ItemSpacing, Vertical/HorizontalItemSpacing → `Invalidate` on the Qt thread). It resubscribes when ItemsLayout is replaced and unsubscribes in `UnsubscribeList`.
  - `Scrolled` carries Horizontal/VerticalDelta from the last reported offset (the first report counts from 0). The dead `IScrollViewController` cast is gone: ItemsView never implements it.
  - `ScrollTo(animate: true)`: the command carries `animate`. QML finds the target with `positionViewAtIndex`, goes back and eases there with a `NumberAnimation` (300 ms InOutQuad).
  - Found on the phone: a long animation's estimated target drifts as rows are created on the way, so the end lands on the row (`positionViewAtIndex` in `onStopped`). A superseded animation clears its row first.
- Tests: `Renderer/ListScrollBehaviourTests.cs` (4): mode pushed and changed; Span 2→3 on the same GridItemsLayout gives 3→2 rows; deltas +100/−40; animate true/false in the command. Suite 524 green.
- Phone: `collection` leg, new chat step (`QtHostDiagnosticsRunner.Chat.cs`). With 30 messages and KeepLastItemInView, 3 appended ones bring the end into view (atYEnd, last visible item 32 of 33). Then an animated `ScrollTo(0, Start)` is mid-way at contentY 804 and lands on item 0 (contentY 0). `atYBeginning` stays false on this list, so the check reads the first visible item. 25/25. Screenshot `collection-chat` checked (ends on "chat message 33").
- Docs: porting guide bullet.
- Regression: `tools/sf matrix collection collection10 collection100 collection500 containers f3` 6/6 PASS.

<a id="s27"></a>
### S27 · Selection and carousel visual states
Plan: §M8 steps 3, 6 · Audit: collections C4 · Decision: D4 · Phone: legs `collection containers f3` · Depends: —
- [x] screenshot first: is the native highlight visible under opaque templates?
- [x] VSM `Selected`/`Normal` on cell roots; carousel `CurrentItem/NextItem/PreviousItem/DefaultItem`
- [x] carousel `IsScrollAnimated`, `VisibleViews`, `IsDragging`, `IsScrolling`

Done when: a `Selected` style applies on the phone (screenshot).

Notes:
- 2026-10-08: `Platform/QtHost/QtHostListAdapter.States.cs`.
  - `ApplySelectionStates` (from `PushSelection`, before its no-change return, so rebuilt rows get their state) puts `Selected` on the selected cells' roots and `Normal` on those that left the selection.
  - `ApplyCarouselStates(position)` (on `carousel-position`, `PushPosition` and after a rows rebuild) gives each page CurrentItem, PreviousItem, NextItem or DefaultItem (wrapping when `Loop`). It sets `VisibleViews` to the current page, plus its neighbours while peek areas show them.
  - `carousel-motion {dragging, moving}` (ListView.qml in carousel mode, CarouselView.qml) → `SetIsDragging` / `IsScrolling`.
  - `IsScrollAnimated` → `mauiScrollAnimated` → `highlightMoveDuration` 250/0. It is set imperatively, because the rebuild assigns the duration.
- D4 (a): the "before" screenshot (`collection-item-states`, Silica highlight still drawn) showed the selected row's highlight hidden under an opaque template, with only the VSM colour visible. So ListView.qml draws only the press highlight now; `mauiSelectedRows` still flips the `s` role. Porting guide: a list that relied on a default colour needs a `Selected` state.
- Tests: `Renderer/ItemVisualStateTests.cs` (3): Selected/Normal across two taps, the BackgroundColor setter applied; carousel states [Previous, Current, Next, Default] at position 1 and VisibleViews; IsScrollAnimated pushed false, motion on/off. Suite 527 green.
- Phone: `collection` leg, step `RunColItemStateChecks` (`QtHostDiagnosticsRunner.ItemStates.cs`), 28/28. A real tap on row 2 selects it and its root turns SteelBlue (others stay #202428). The carousel's page A is CurrentItem (DarkGreen); a real swipe sees IsDragging, lands on page 1, page B turns CurrentItem and A leaves it. Before and after screenshots checked.
- Docs: porting guide, silica-parity row.
- Regression: `tools/sf matrix collection collection100 features f3 containers controls` 6/6 PASS.

<a id="s28"></a>
### S28 · Snap points, `MeasureFirstItem`, lazy templating
Plan: §M8 steps 5, 7 · Audit: collections C4 · Phone: legs `collection500 collection100` · Depends: —
- [x] `SnapPointsType/Alignment` → `snapMode` + highlight range
- [x] `ItemSizingStrategy.MeasureFirstItem`
- [x] off-screen rows templated lazily in `RequestMaterialize`
- [x] `collection500` timings recorded in `profiling.md` before and after

Done when: `collection500` open time measured and not worse; snapping works.

Notes:
- 2026-10-08: `QtHostListAdapter.Sizing.cs`.
  - `MeasureFirstOnly` applies to a vertical, non-carousel list with MeasureFirstItem and an ItemTemplate that is not a `DataTemplateSelector`. Under it, `AddItemRows` templates and measures only the build's first item (a kept row seeds it too). Every later row gets its extent, `LazyCells` and a null view.
  - `MaterializeRow` → `TemplateLazyCells` creates the views when the delegate shows them, laid out at the row's extent (`MeasureAndArrangeItemFixed`), watched, then `ApplySelectionStates`. Counter: `LazyTemplated`.
  - Lazy rows take their tap/gesture flags from the first item's template (`RowHasTap`/`RowHasGestures` are instance methods now).
  - `ItemSizingStrategy` is watched (Invalidate) and part of `BuildSignature`, so rows are not reused across a switch.
- Snap points: `mauiSnapType`/`mauiSnapAlign` from the ItemsLayout (CollectionView only), also re-pushed when the layout object's SnapPointsType or Alignment changes.
  - ListView.qml: `snapMode` SnapToItem (Mandatory) or SnapOneItem (MandatorySingle).
  - Center/End use `ApplyRange` with a one-row highlight range at the centre or end; the row extent is `__avgRowH`.
- Tests: `Renderer/ListSizingTests.cs` (4): lazy rows (null views, one height, tap flag from the template); an attached on-screen row templated at the first item's height; MeasureAllItems eager; a runtime switch rebuilds; snap keys follow the layout object. Suite 531 green.
- Phone, `collection` leg, step `RunColSizingChecks` (`QtHostDiagnosticsRunner.Sizing.cs`), 30/30:
  - The same 500-item list: MeasureAllItems builds its rows 223 ms after the push (500 views); MeasureFirstItem 61 ms, with 18 views (17 on materialize).
  - A 60-row list with Mandatory snap points, dragged and released mid-row, rests on a row boundary: offset 275 px, stride 138 px. Screenshot `collection-snap`.
- `collection500` before/after (`NAV-TIMELINE rows: 500`, 3 runs): 234/246/244 → 230/237/241 ms. That list uses a template selector, so it stays eager: not worse. Recorded in `profiling.md`. One extra run was needed: one `sf shots` run stopped before launch, without an app log.
- Docs: porting guide bullets, profiling table.
- Regression: `tools/sf matrix collection collection10 collection100 collection500 containers f3 features input controls` 9/9 PASS.

<a id="s29"></a>
### S29 · CollectionView inside ScrollView, items parity, vertical loop
Plan: §M8 steps 6 (vertical loop), 8, 9 · Audit: collections C5, C6 · Phone: legs `collection containers` · Depends: —
- [x] unbounded list: non-interactive inner list, lift the 192 cap or warn once; porting guide note
- [x] `CollectionView`/`CarouselView` in `HandlerParityTests.Pairs` with pinned gaps
- [x] vertical `Loop`: implement or document the limit

Done when: a 300-item CollectionView inside a ScrollView shows every row on the phone; parity doc lists the items handlers.

Notes:
- 2026-10-08:
  - The cause: `SailfishMeasure.Collection` sizes a vertical list that has no height bound to all its rows, so its native ListView is that tall. Every row lies in its own viewport, and rows past `MaxDelegates` (192) stayed empty.
  - Now the measure sets `QtHostListAdapter.SetUnbounded` (not for carousels). That pushes `mauiUnbounded`: ListView.qml turns `interactive` off and the outer ScrollView scrolls it.
  - `DelegateCap` = rows + 8 for an unbounded list; one warning above 200 rows suggests a height, or Header/Footer instead of the ScrollView. The list cannot virtualize there, as a RecyclerView in a ScrollView cannot.
- Parity: `HandlerParityTests.Pairs` now include `CollectionViewHandler` and `CarouselViewHandler`. `HeaderTemplate`, `FooterTemplate` and `EmptyViewTemplate` were the open gaps; they became watched ItemsView properties (they were already used at build), so the gaps closed. `CanReorderItems` is pinned in `KnownGaps` (no reorder gesture). `docs/handler-parity.md` regenerated.
- Vertical `Loop`: documented limit. `AdapterUriFor` keeps the ListView (no wrap) with one warning per carousel, because the PathView path is horizontal.
- Tests: `ListSizingTests` +2 (a 300-item list in a ScrollView: unbounded, cap ≥ 300, as tall as its rows; a bounded list keeps the cap 192); parity ratchet. Suite 533 green.
- Phone: `collection` leg, step `RunColUnboundedCheck` (`QtHostDiagnosticsRunner.Unbounded.cs`), 31/31. In a ScrollView the list is unbounded, all 300 rows have their delegate, and after `ScrollToAsync(list, End)` the last row shows "nested row 300". Screenshot `collection-nested-end` (rows 280–300) checked.
  - The first try failed only in the check's lookup: item hosts are the delegate's `Children`, not `CurrentHosts`.
- Docs: porting guide (nested list, vertical loop), handler-parity.md.
- Regression: `tools/sf matrix collection collection10 collection100 collection500 containers f3 features controls page` 9/9 PASS.

<a id="s30"></a>
### S30 · Canvas text anchor, fonts, fill rule, antialias
Plan: §M9 steps 1, 2, 5 · Audit: graphics G4, G9 · Phone: legs `shapes visual f3` · Depends: —
- [x] device check: `DrawString` next to a Label, EvenOdd star, `Antialias=false` (screenshots)
- [x] `DrawString` anchor as Android; `Font` through `QtHostFonts.Resolve`; `ctx.fillRule` for EvenOdd in three QML files; antialias applied
- [x] host tests for the recorder, shape serialisation, span HTML, glyph/stream URLs, cache policy

Done when: screenshots match Android anchoring within 1 px; new tests green.

Notes:
- 2026-10-08:
  - `DrawString(value, x, y, align)`: GraphicsView.qml draws it with `textBaseline = "alphabetic"`, so y is the baseline, as Android's `Canvas.drawText` and MAUI's Skia backend do. It used to be "top".
  - `Font` → `QtHostFonts.Resolve` in the recorder (a ConfigureFonts alias becomes its Qt family; off the Qt thread it passes through).
  - Fill rule: Qt 5.6's Context2D ignores the argument of `fill("evenodd")`/`clip("evenodd")`, so EvenOdd was never applied. `ctx.fillRule = Qt.OddEvenFill/WindingFill` is now set around the fill/clip in GraphicsView.qml (fpath, clipp), Shape.qml and MauiLayerEffect.qml.
  - `Antialias=false` anywhere in the stream turns the Canvas item's `antialiasing` off for that view (Context2D has no per-op antialias).
- Device check: new leg `canvas` (`MAUI_SAILFISH_QT_HOST_CANVAS_DIAG`, `QtHostDiagnosticsRunner.Canvas.cs`; added to `matrix.sh` and `tools.md`), 4/4, with pixel readbacks through `getImageData`:
  - the "A" of `DrawString("Ag", 10, 60)` has ink above the red baseline and none below it;
  - an EvenOdd star's centre is empty (alpha 0) while the NonZero control is filled;
  - an Antialias=false diagonal has 0 partly covered pixels against 306 on the antialiased control.

  Screenshot `canvas-1-text-fill-aa` checked: the "A" stands on the red line. The first run's baseline region also took in the "g"'s descender (x 12..36); it is the "A" alone now (12..28). There was no "before" screenshot: the code was changed first. The fill-rule cause was confirmed against the Qt 5.6 Context2D API.
- Tests:
  - `Renderer/CanvasRecorderTests.cs` (4): state ops (aa, fos, fon with an alias, sv/rs), winding on fpath/clipp, point/rect text, the geometry and transform ops.
  - `Renderer/DrawingSerialisationTests.cs` (6): `PathOps` in device units; a Polygon's winding/fill/stroke props; span rich text (bold, colour, both decorations, escaping, `<br/>`, an empty span dropped); the remote image cache fragments (`#maui-cache=7200`, `=0`, an app's own fragment kept).
  - Glyph and stream URLs are covered by the existing `ImageLoadingTests`.
  - Suite 545 green.
- Docs: porting guide bullet (shared with S31), silica-parity row.
- Regression (S30 + S31 build): `tools/sf matrix shapes visual f3 input canvas features tree skia` 8/8 PASS.

<a id="s31"></a>
### S31 · `GraphicsView` touch interactions and re-record policy
Plan: §M9 steps 3–4 · Audit: graphics G3, G5 · Phone: legs `shapes input` · Depends: S16
- [x] press/move/release → `Start/Drag/End/CancelInteraction`
- [x] record only on `Invalidate`, drawable/property and size change, not every reconcile
- [x] chunk the command list instead of truncating at 4096; stale comment `QtHostGraphics.cs:8-9` (cap raised instead of chunking, see Notes)

Done when: a GraphicsView sample receives drag on the phone; `input` leg check added.

Notes:
- 2026-10-08: `QtHostInputRouter`.
  - A press on a host whose element is an `IGraphicsView` calls `StartInteraction` with the point relative to the view (dp). Moves call `DragInteraction`; the release calls `EndInteraction(points, isInsideBounds)`; a second finger calls `CancelInteraction`. The view's recognizers still follow as anywhere.
  - The interaction holds the page drag like S60 (KeepsDrag opts out). Counter: `Interactions`.
- Re-record policy: `SailfishGraphicsHandler.Snapshot` records and caches. The reconcile walk's `AdapterState()` returns a copy of the cache while the size holds (the walk merges generic state into it). Invalidate, mapped properties and a new arranged size record again (`Recordings` counter). The stale `QtHostGraphics` comment was fixed. Self-drawing library views (`SailfishDrawnViewHandler`) still record per pass, because their children change.
- Command cap: 4096 → 32768 (`MaxCommands`). The stream is one property push and one replay per paint, so chunking would still be one paint and one push; a higher cap is the actual fix. It still guards against a runaway `Draw`.
- Tests: `Renderer/GraphicsViewInteractionTests.cs` (2): a drag gives start 10,20 → drag 60,20 → end 400,20 inside=False plus the hold true/false; no re-record over 7 polls and a layout pass, +1 on Invalidate, more after a resize. Suite 539 green.
- Phone: `input` leg, step `RunQtGraphicsInteractionCheck` (`QtHostDiagnosticsRunner.GraphicsInput.cs`), router-checks 14/14. A real drag gives Start at (32,32) dp in the view (expected 31,31), 12 Drags and 1 End. Screenshot `input-graphics-drag`: the orange trail the app draws through Invalidate follows the drag.
- Porting guide: GraphicsView draws again only on Invalidate, a property or a size change (an app relying on the old per-pass redraw must call Invalidate).
- Regression: see S30 (8/8, the same build).

## Phase F — legacy ListView and TableView

<a id="s32"></a>
### S32 · Legacy `ListView` handler: cells, tap, selection
Plan: §M4 steps 1–2 · Audit: collections C2, handlers H7 · Decision: D2 · Phone: legs `collection containers` · Depends: S01
- [x] `Row<ListView, SailfishLegacyListViewHandler>` before `ItemsView`
- [x] `TemplatedItems` onto the list adapter; `ViewCell.View`, `TextCell`, `ImageCell` row templates
- [x] `ItemTapped`/`ItemSelected`/`SelectedItem`
- [x] harness tests per cell type

Done when: a ListView with text, image and view cells shows rows and selects on the phone.

Notes:
- 2026-10-08: a mirror, not a second adapter. `Platform/QtHost/QtHostLegacyList.cs`: `LegacyListMirror.Of(listView)` keeps a CollectionView (Single selection, `Parent` = the ListView for its BindingContext; not a logical child).
  - Its template is `LegacyCellView`: for each item it builds the cell the ListView would build (its ItemTemplate, a selector's choice, else `CreateDefaultCell`, `BindingContext` = item). `ViewCell` → its `View`; `TextCell` → two labels bound to the cell (Detail hidden when empty, colours only when set); `ImageCell` → a 48 dp image plus the labels. Any other cell renders empty, with one warning per type.
  - `RowHeight` (HasUnevenRows false) and `Cell.Height` (HasUnevenRows) become the row's HeightRequest.
  - Sync: ItemsSource and SelectedItem → the mirror; the template, RowHeight or HasUnevenRows changing rebuilds the rows. A mirror selection → `ListView.NotifyRowTapped(0, index, cell)`, so MAUI itself sets SelectedItem and raises ItemTapped, ItemSelected and Cell.Tapped. With `SelectionMode.None` the mirror selection is cleared again.
- The mirror is the ListView's only child for the renderer: `QtHostVisualChildren.Of` is used by the walk, `AttachHandlers` and `CollectGeometry`. `SailfishLegacyListViewHandler` (NullViewHandler) measures and arranges the mirror in its frame. Factory row `Row<ListView, SailfishLegacyListViewHandler>` (ListView is `ItemsView<Cell>`, not the CollectionView `ItemsView`). PublicSurface regenerated.
- Tests: `Renderer/LegacyListViewTests.cs` (5): TextCell text + detail, no template (item text), ViewCell and ImageCell (image source set), a tap selects with ItemTapped/ItemSelected and the code's SelectedItem reaching the mirror, SelectionMode None still taps. Suite 550 green.
- Phone: `collection` leg, step `RunColLegacyListCheck` (`QtHostDiagnosticsRunner.LegacyList.cs`), 33/33. A selector over text, image and view cells gives 4 rows, handler SailfishLegacyListViewHandler. A real tap on row 4 sets SelectedItem 'legacy second text' and raises ItemTapped ×1 and ItemSelected ×1. Screenshot `collection-legacy-list` checked (detail line, the image cell's icon, the ViewCell's bold label).
- Docs: porting guide bullet.
- Regression: `tools/sf matrix collection collection100 collection500 containers f3 features controls page shapes` 9/9 PASS.

<a id="s33"></a>
### S33 · Legacy `ListView`: grouping, header/footer, separators, row height
Plan: §M4 step 2 · Audit: collections C2 · Phone: leg `collection` · Depends: S32
- [x] `IsGroupingEnabled`, `GroupDisplayBinding`, `GroupHeaderTemplate`
- [x] `Header`/`Footer` (+templates); `SeparatorVisibility`/`Color`; `HasUnevenRows`/`RowHeight`
- [x] `ScrollTo`

Done when: a grouped ListView with header and separators matches a screenshot expectation.

Notes:
- 2026-10-08, on the S32 mirror (`QtHostLegacyList.cs`):
  - `IsGroupingEnabled` → `IsGrouped`, with group header rows from `LegacyGroupHeaderView`: the `GroupHeaderTemplate` cell, else a bold label bound through a copy of `GroupDisplayBinding` (a non-`Binding` falls back to the group's text).
  - `Header`/`Footer` and their templates go straight to the mirror's slots.
  - `SeparatorVisibility.Default` puts a 1 dp line under each row: `SeparatorColor`, else a faint line for the theme. Changing either rebuilds the rows. `HasUnevenRows`/`RowHeight` as in S32.
  - `ListView.ScrollTo(item[, group], position, animated)` → `ScrollToRequested` → the mirror's `ScrollTo`.
  - Taps in a grouped list find the item's group and index, so `ItemTapped` carries the group.
- Tests: `LegacyListViewTests` +3: grouped headers ["Red","Other"] and a tap reporting group + item; header/footer slots, a red separator and its removal with `SeparatorVisibility.None`; ScrollTo → `scrollTo` row 40. The S32 tests now search descendants (rows are wrapped by the separator grid). Suite 553 green.
- Phone: `collection` leg, step `RunColLegacyGroupedCheck`, 35/35. Three groups (25 items) give 3 header and 25 item rows plus the header/footer slots; `ScrollTo("grain 20", group 3, End)` makes item 24 the last visible. Screenshots `collection-legacy-grouped` (bold section headers, red separators, the header text) and `collection-legacy-scrolled` (ends on "grain 20") checked.

<a id="s34"></a>
### S34 · Legacy `ListView`: refresh, context actions, Switch/Entry cells
Plan: §M4 steps 2–3, 5 · Audit: collections C2 · Phone: legs `collection pulley` · Depends: S33
- [x] `IsPullToRefreshEnabled`/`IsRefreshing`/`RefreshCommand`
- [ ] `Cell.ContextActions` → context menu on long press — D2 (b): only when a ported app needs it
- [x] `SwitchCell`, `EntryCell`; `SendCellAppearing/Disappearing`; `CachingStrategy` mapping — D2 (b): `SendCellAppearing/Disappearing` only

Done when: each feature exercised on the phone.

Notes:
- 2026-10-08, D2 (b) scope (refresh, appearing):
  - Pull-to-refresh: `LegacyListMirror.Root` is the mirror inside an internal `RefreshView` while `IsPullToRefreshEnabled`; the children helper and the handler use `Root`, and the handler's `IsPullToRefreshEnabled` key re-walks the subtree. `IsRefreshing`/`RefreshControlColor` → the RefreshView.
  - A pull → `ListView.BeginRefresh` (IsRefreshing, Refreshing, RefreshCommand); a refresh the ListView does not allow ends at once. The RefreshView ending → `EndRefresh`.
  - `ItemAppearing`/`ItemDisappearing`: from the mirror's `Scrolled` visible range (flat items, grouped too) through `SendCellAppearing/Disappearing`. `Scrolled` is forwarded through `SendScrolled`.
  - Not done (D2 b): `ContextActions`, `SwitchCell`/`EntryCell` (one warning, the row shows nothing), `CachingStrategy` (the list adapter recycles natively anyway). Porting guide says so.
- Tests: `LegacyListViewTests` +2: a native `refresh-requested` runs RefreshCommand and sets IsRefreshing, EndRefresh clears the RefreshView; scroll reports 0..4 then 3..7 give ItemAppearing [0..4], then [5,6,7], and ItemDisappearing [0,1,2]. Suite 555 green.
- Phone: `collection` leg, step `RunColLegacyRefreshCheck`, 36/36. A real pull from the list's header runs RefreshCommand once; during it IsRefreshing is true and the list's native `mauiRefreshing` is true; EndRefresh stops it. 25 rows in view raised ItemAppearing.
  - Screenshot `collection-legacy-refreshing` shows the spinner. The first try's screenshot came after the refresh had ended (EndRefresh after 1.5 s); the leg now ends it after the screenshot.
- Regression: `tools/sf matrix collection collection10 collection500 containers f3 features controls pulley page` 9/9 PASS.

<a id="s35"></a>
### S35 · `TableView` — deferred by D2 (b)
Plan: §M4 step 4 · Audit: collections C2 · Phone: leg `containers` · Depends: S34

D2 (b) leaves `TableView` out of the minimal set: until an app needs it, it gets one warning and a porting-guide entry
(done in S32).
- [ ] `Root` flattened into adapter rows; `TableSection.Title` as group headers
- [ ] harness test with all cell kinds

Done when: a settings-style TableView renders on the phone (screenshot).

Notes:
- 2026-10-08: the warning was not there yet: `WalkChild` now warns once per TableView ("not supported … renders nothing"), and the porting guide's legacy ListView bullet names it. The rendering itself stays deferred. The S32–S34 mirror (`LegacyListMirror`) is the way in when an app needs it: flatten `Root` sections into grouped items.

<a id="s36"></a>
### S36 · Legacy list leg, ported apps, docs
Plan: §M4 acceptance · Phone: new leg `legacylist` · Depends: S35
- [x] `legacylist` leg in the Diagnostics runner, added to `matrix.sh`
- [ ] ported apps that use `ListView` checked (`docs/app-test-campaign.md`)
- [x] `porting-existing-apps.md`, `silica-parity.md:46` updated

Done when: `legacylist` green; full matrix green.

Notes:
- 2026-10-08:
  - New leg `legacylist` (`MAUI_SAILFISH_QT_HOST_LEGACYLIST_DIAG`, own `DiagChecks`), with the S32–S34 steps moved out of the `collection` leg; added to `matrix.sh` and `tools.md`, 6/6.
  - Full matrix: `tools/sf matrix` (all legs) 34/34 PASS, f4 and skia included.
  - Porting guide: the legacy ListView bullet (supported cells, taps, selection, grouping, refresh; what is not supported). silica-parity: the `SilicaListView` row already names `ListView`.
- Ported apps not checked: the campaign's scratch copies (`$SCRATCH/cand`, `$SCRATCH/oss/maui-samples`) lived in an earlier session's scratchpad and are gone. This machine has no `gh` to search the samples repo. Re-cloning and porting the apps is a campaign round of its own.

## Phase G — navigation details, dialogs, drag & drop

<a id="s37"></a>
### S37 · `animated:false`, `InsertPageBefore`, `RemovePage`
Plan: §M27 steps 2–3 · Audit: navigation N6 · Phone: legs `nav navback shell` · Depends: —
- [x] `Animated` threaded into `NavOperation`; last level of a multi-level pop animated (the flag yes; multi-level pops stay immediate, see Notes)
- [ ] insert/remove operate on the model page below the top (they no longer slide; the top is still re-rendered, see Notes)
- [x] `NativeStackSyncTests` for both

Done when: `PushAsync(page, false)` shows no slide; `RemovePage` does not re-render the top page (recording).

Notes:
- 2026-10-08:
  - `SailfishNavigationViewHandler.MapRequestNavigation` passes `NavigationRequest.Animated` to `NoteNavigationRequest(animated)`. `animated: false` sets `_nextNavImmediate`; the next `PushModelPages`/`PopModelPages` use `PageStackAction.Immediate` and clear it. `LastNativeNavStep` ("PUSH Animated", "POP Immediate") is exposed for tests and diagnostics.
  - Probe before the change: `InsertPageBefore` pushed a new model page with a slide (the unchanged top slid in again), and `RemovePage` popped with a slide. MAUI sends both with `Animated=false`, so they now go at once.
  - What is left: the top page is still re-rendered on the other model page, in one frame, without a slide. Silica's PageStack has no insert below the current page, and `replaceAbove` would rebuild the top QML page as well, so it stays this way. A multi-level pop stays immediate: each level pops the top, so animating the last one would slide a page that never showed.
- Tests: `Renderer/NavStackEditTests.cs` (4): animated/unanimated push and pop (after a warm-up push: before activation settles every push goes at once); Insert/Remove without a slide with the top kept; every request finishing so the next push runs, through the NavigationPage and through the root page's own `Navigation`.
- Phone: new leg `navdialog` (`MAUI_SAILFISH_QT_HOST_NAVDIALOG_DIAG`, `QtHostDiagnosticsRunner.NavDialog.cs`; in `matrix.sh` and `tools.md`):
  - `PushAsync(page, false)` → PUSH Immediate with `pageStack.busy` false 120 ms later, against PUSH Animated with busy true for an animated push;
  - InsertPageBefore → PUSH Immediate and RemovePage → POP Immediate, both with busy false and the top unchanged.
- Found on the way, in the leg: its starting page was a pushed one ("Statistics"). Once PopToRoot removed it, its own `Navigation` proxy was empty, so later pushes went nowhere. The leg uses the NavigationPage's `Navigation`; a harness test pins that a root page's proxy keeps working.
- Regression 2026-10-08: `tools/sf matrix popup nav navback shell containers navdialog visual shapes controls canvas features text input` 13/13 PASS.

<a id="s38"></a>
### S38 · Dialogs: queue, thread hop, RTL, keyboard; Detail swap
Plan: §M27 steps 4–6 · Audit: navigation N5, N7, N8 · Phone: legs `popup containers` · Depends: —
- [x] dialog queue; navigation not held while a dialog is open (the queue yes; navigation still waits, see Notes)
- [x] `QtThread.Run` hop in `QtHostAlertSubscription`; `FlowDirection`; Email/Url prompt keyboards
- [x] replaced `FlyoutPage.Detail` handlers disconnected; renderer comment fixed; page-cache difference documented

Done when: two chained alerts both show; an alert from a background thread shows.

Notes:
- 2026-10-08:
  - Queue: `PushDialogCore` enqueues a dialog asked for while one is open; `CompleteDialog`/a failed open opens the next (`OpenNextDialog`). It used to complete at once with the negative result.
  - Thread hop: `QtHostAlertSubscription.OnQt` runs the push through `QtThread.RunAsync` off the Qt thread.
  - `mauiMirrored` (the page's effective FlowDirection) → `DialogPanel.qml` `LayoutMirroring`.
  - Prompt keyboards: `Keyboard.Email`/`Url` → `mauiHints` (Qt::ImhEmailCharactersOnly / ImhUrlCharactersOnly) on the PromptDialog field.
  - `SailfishFlyoutPageHandler` watches `Detail` PropertyChanging/Changed and calls `DisconnectHandlers()` on the replaced detail. The "MAUI core never fires appearing" comment was rewritten.
  - Porting guide: the queue, any thread, RTL, keyboards, and the page cache (the four most recent pages keep their native views).
- Navigation is still held while a dialog is open, a deliberate deviation from plan step 4: the dialog is a panel on its model page, so a push would cover it until the user came back to that page. Android keeps the dialog above the new page. Moving the panel to the new top page is the way to drop the gate.
- Tests: `Renderer/DialogQueueTests.cs` (4): two alerts in order with their answers; mirrored on an RTL page; the email hint; a replaced Detail's handler gone. Suite 565 green.
- Phone: `navdialog` leg:
  - two `DisplayAlertAsync` calls back to back: the first shows, the second waits, both answer True after real taps on their accept buttons;
  - an alert from `Task.Run` shows and answers.

  Screenshots `navdialog-1/2/3` checked (the second alert after the first, over the Statistics page).
- Regression 2026-10-08: `tools/sf matrix popup nav navback shell containers navdialog visual shapes controls canvas features text input` 13/13 PASS.

<a id="s39"></a>
### S39 · Drag & drop: router core and drag ghost
Plan: §M28 · Audit: input I1 (design notes) · Phone: leg `input` · Depends: S17
- [x] drag start on the long-press timer → `SendDragStarting`
- [x] `DragGhost.qml`; back navigation and flickables off while dragging
- [x] `SendDragOver/Leave`, `SendDrop`, `SendDropCompleted` (also on cancel); args subclasses with positions
- [x] router test for the event order and text transfer

Done when: dragging a Label onto a drop target transfers its text on the phone.

Notes:
- 2026-10-09, `Platform/QtHost/QtHostInput.Drag.cs` (the router is now `partial`): a press on a view with a `DragGestureRecognizer { CanDrag: true }` (it or an ancestor) arms a 500 ms timer (`DragStartMs`, Android's long-press timeout). Travel beyond the tap slop first disarms it (a scroll or pan). When it fires, `SendDragStarting(owner, getPosition)` runs; `Cancel`/`Handled` leave the press to the other gestures. Otherwise the drag owns the finger: the pending long press is canceled, the context hold and tab swipe disarmed, `mauiHoldDrag` (S60) holds the back swipe and flickables, and the ghost shows. On each move a hit-test walks up the ancestors to a `DropGestureRecognizer { AllowDrop: true }`: `SendDragLeave` to the old target on a change, `SendDragOver` to the current one (each move, as Android's ACTION_DRAG_LOCATION), and `AcceptedOperation` drives the ghost's opacity. A release over an accepting target awaits `SendDrop(new SailfishDropEventArgs(package.View, …))` (MAUI's default `TrySetValue`), then `SendDropCompleted` on the source. A release elsewhere, a second finger or the page going away (`CancelDrag`) send DragLeave and DropCompleted without a drop, since MAUI has no cancel event. The release is no tap or pan. `SailfishDragEventArgs`/`SailfishDropEventArgs` override `GetPosition` (MAUI's position constructors are internal).
- Ghost: no separate `DragGhost.qml`. It is a `ShaderEffectSource` in `MauiModelPage.qml` (`mauiDragGhost(json)`: show/x/y in window px/allowed/id): a still snapshot of the source host, the grab point kept under the finger, opacity 0.85 over an accepting target and 0.45 elsewhere, z above content and chrome. Found on the phone: `__hosts[id]` is a record whose QML item is `.item`, so the first build had no ghost (the check saw opacity −1).
- Tests `DragDropTests` (6 here plus a row test for S40): MAUI order and text transfer, the ghost calls and `mauiHoldDrag` true/false; moving before the hold is no drag; leaving the target then releasing elsewhere gives DragOver, DragLeave and DropCompleted without a drop; `AcceptedOperation=None` gives no Drop; `Cancel` in DragStarting gives no ghost and no completion; a second finger ends without a drop. Timers advance through `SailfishRuntime.TickDueTimers`.
- Phone: `input` leg step `RunQtDragDropCheck`: real press on "Drag me", held 750 ms, 20 moves onto "Drop here", screenshot mid-drag, release. CHECK OK: text 'Drag me', events `DragStarting,DragOver,Drop,DropCompleted`, ghost opacity 0.85, router drags 1 drops 1. Screenshots `input-dnd-dragging` (the ghost over the target) and `input-dnd-dropped` (the target reads "Drag me") checked.
- Regression `tools/sf matrix input collection legacylist controls page shell navdialog shapes canvas features` 10/10 PASS. Suite 594/594.

<a id="s40"></a>
### S40 · Drag & drop: rows, leg, docs
Plan: §M28 step 6 · Phone: leg `input` · Depends: S39
- [x] drag source inside a CollectionView row
- [x] DnD check in the `input` leg; porting guide updated

Done when: a row can be dragged to a target on the phone.

Notes:
- 2026-10-09: the ListView consumes the press; the delegate's `list-item-pressed` reaches `CaptureRow`, which now also arms a drag (`ArmRowDrag`, the source looked up from the element under the finger up to the cell root; `RowHasGesturesView` already counted a DragGestureRecognizer). At the start the row is held (`mauiHoldRow`, so the ListView does not flick), its tap is suppressed (`_rowGestureTook`: the release neither taps nor selects), and the ghost finds the delegate by name (`maui_<list>__r<row>`, `__findByName` in the page) since rows have no page host.
- Visible defect fixed: the dragged row kept its press highlight during the drag, and the ghost (a snapshot of the delegate) carried it. New `mauiDragRow` on the ListView hides that row's highlight; a pan in a row keeps its highlight (it holds the row from the press, so `mauiHoldRow` would have hidden every pan row's).
- Test `A_row_of_a_collection_view_can_be_dragged_onto_a_target`: the target gets "two", the row is held and marked while dragging and released after, nothing is selected, and the ghost names `maui_<list>__r1`.
- Phone: `input` leg step `RunQtRowDragCheck` (row 2 of a 4-row CollectionView onto a target under it): CHECK OK, 'Row two', nothing selected, ghost visible. Screenshots `input-dnd-row-dragging` (no highlight on the row; the ghost over the target) and `input-dnd-row-dropped` checked. `input` leg 16/16.
- Docs: `porting-existing-apps.md` (drag & drop as on Android, in-app only), `silica-parity.md` row. Regression: see S39.

## Phase H — visuals, text, keyboard, theme, platform API

<a id="s41"></a>
### S41 · Gradient backgrounds on every view
Plan: §M10 · Audit: graphics G6 · Decision: D10 · Phone: legs `visual shapes controls` · Depends: —
- [x] gradient `Background` → gradient item under the host
- [x] gradient `Shadow.Brush` → average colour
- [x] sample page with a gradient background

Done when: screenshot of the gradient page.

Notes:
- 2026-10-08, D10 (a), GPU-drawn:
  - `QtHostPaint.GradientSpec` gives linear `{x0,y0,x1,y1}` or radial `{x0,y0,r}` (MAUI's relative units) with sorted `[offset, colour]` stops. `QtHostVisualState` pushes it as the generic `mauiBackgroundGradient` for any element whose Background is (or was) set, except Border, shapes and BoxView, which paint the brush through their own fill spec.
  - The shim (`host_handles.cpp` `apply_background_gradient`, `tools/sf native-build`) creates a lazy child from `qml/effects/GradientFill.qml`. That draws the stops into a 256 px Rectangle-gradient ramp and samples it in a `ShaderEffect` fragment shader (linear: projection on p0→p1; radial: distance over radius × the larger side, as Android). No per-frame repaint.
  - The key is added to the shim-owned lists (`GenericNativeKeys`, `MauiModelPage.__createHost`).
  - Shadow: `QtHostPaint.Average` gives a gradient brush's mean colour (iOS's behaviour); the shadow used to be dropped.
- Tests: `Renderer/GradientBackgroundTests.cs` (2): a Grid's linear and a Label's radial spec, Border left to its own paint, back to solid clears it; a red→blue gradient shadow as #800080. Suite 563 green at the time.
- Phone: `canvas` leg, second page, 5/5. A Grid (linear, three stops, a diagonal), a Label (radial) and a Button (horizontal linear) each carry a visible `mauiBackgroundGradient` child. Screenshot `canvas-2-gradients` checked.
  - The linear isolines run corner to corner, as iOS draws relative points; Android works in pixel space, so on a wide view its band is steeper.
- Regression 2026-10-08: `tools/sf matrix popup nav navback shell containers navdialog visual shapes controls canvas features text input` 13/13 PASS.

<a id="s42"></a>
### S42 · FormattedText spans: properties and gestures
Plan: §M11 (spans), §M17 step 6 · Audit: graphics G7, input I3 · Phone: legs `text controls` · Depends: —
- [x] span `BackgroundColor`, `CharacterSpacing`, `LineHeight`, `TextTransform` (LineHeight per span not possible, see Notes)
- [x] span gestures via `<a href="span:N">` + `linkAt` → adapter event → span recognizers
- [x] span HTML tests

Done when: a span tap fires on the phone.

Notes:
- 2026-10-08:
  - `AdapterSnapshots.BuildSpanHtml` adds `background-color`, `letter-spacing` (CharacterSpacing × density px) and the span's TextTransform. Per-span LineHeight cannot be done: Qt's rich text spaces whole lines; the label's LineHeight applies.
  - A span with a TapGestureRecognizer is wrapped in `<a href="span:N">` (N = its index in Spans), and its inner span sets `text-decoration:none`. `mauiSpanLinks` makes Label.qml bind `linkColor` to the label's colour (an HTML label's own links keep Qt's link colour).
  - `onLinkActivated` → `span-tapped {id,index}` → `AdapterEventRouter.ApplySpanTapped` → each TapGestureRecognizer's `SendTapped(label)` (Tapped and Command).
- Tests: `Renderer/SpanTests.cs` (2): the new styles and an uppercased span, no link; a tappable span's `<a href="span:1">`, `mauiSpanLinks`, and `span-tapped` firing Tapped (sender = the label) and Command.
- Phone: `navdialog` leg, `SpanTap`. `Text.linkAt` finds "span:1" at 262,198 and a real tap there fires the recognizer once. Screenshot `navdialog-4-span` checked (the orange underlined span in the sentence).
  - The first tries failed in the leg, not in the backend: the page push went nowhere (see S37), and the scan started at half height while the label fills the page with its text at the top.
- Regression 2026-10-08: `tools/sf matrix popup nav navback shell containers navdialog visual shapes controls canvas features text input` 13/13 PASS.

<a id="s43"></a>
### S43 · Mixed-font measure and `FontAutoScalingEnabled`
Plan: §M11 (measure, scaling) · Audit: graphics G7, lifecycle row 17 · Phone: legs `text controls` + native-build · Depends: —
- [x] FormattedText measured per run (shim `measure_text` gains runs)
- [x] Silica text-size ratio probed; explicit sizes scaled when `FontAutoScalingEnabled`
- [x] measure tests with two fonts

Done when: a two-size FormattedText row does not clip (screenshot).

Notes:
- 2026-10-08, shim: `measure_text` takes an optional `runs` array (`s`/`n` UTF-16 offsets, family/px/bold/italic/ls). `measure_runs` lays each paragraph out as one `QTextLayout` with a `FormatRange` per run, the way the rich-text label does. Each line counts `line.height()` with leading included, so a line is as tall as its tallest run. Without runs the single-font path is unchanged. `tools/sf native-build` done.
- Managed: `SailfishMeasure.Label` sends runs only when the spans differ in font (`MixedFonts`); one font across spans keeps the tuned single-font path. `LabelTextMapper` now builds spans as the HTML paints them: a span without `FontSize` takes the label's painted size (`LabelFontSize ?? Theme.fontSizeMedium`, previously MAUI's 14 dp), and span text carries its `TextTransform`. `TextSpan.FontSize` is a double. The headless estimate uses the tallest span for the line advance.
- Auto-scaling: SFOS has Settings › Display › Text size (`/desktop/jolla/theme/font/sizeCategory`: normal/large/huge/gigantic). Silica exposes `Theme.fontSizeMedium` (scaled) and `Theme.fontSizeMediumBase` (not), as found in `libsailfishsilica`'s moc data. `SailfishFontRules.TextScale()` is their ratio (clamped 0.5–4, 1 until the theme answers, read once per run). `AppFontSize`, the one place Label/Button/Entry/Editor/SearchBar/pickers/RadioButton decide their size, multiplies explicit sizes by it unless the element (`ITextStyle.Font.AutoScalingEnabled`, `Span.FontAutoScalingEnabled`) turned it off. Span HTML sizes and the measure use the same rule (`LabelTextMapper.SpanFontSize`).
- Phone, `navdialog` step `MixedFontRow` (14/40 dp spans, bold, in an Auto grid row, a row below it): measured 50.2 dp, QML `contentHeight` 96 px = 50.2 dp, the next row at 66.2 ≥ 66.2. Screenshot `navdialog-4b-fonts`: "Total **1 234,56 zł** incl. VAT" in full, the next row underneath. With the old measure (first span's 14 dp font) the row would have been about a third of that height. The theme probe answers on the phone (`Theme.fontSizeMedium,Base` = `48,48` at the "normal" setting → factor 1.00). A non-default setting was **not** tried, since it would change the owner's phone settings; the hosts tests cover the arithmetic.
- **Regression found by the matrix and fixed:** the `collection` leg's chat check failed (atYEnd false), reproducibly. A pre-S43 copy of the repo (`HEAD` in the scratchpad, same shim) passed, so it was S43. Row heights told the story: 155 px against 100 px before. A temporary log showed unsized labels measured at `fontSize=48` dp instead of 25: `SailfishMeasure.ThemeDp` cached its answer **in dp**, and `TextScale()` now asked for `Theme.fontSizeMedium` from `AppFontSize` before the screen size was known (density 1), so 48 px became 48 "dp" for the whole run. This latent bug only needed an earlier first query. Fix: the theme cache keeps Qt units and converts on every read (`_themeQt`, also the text-input margin probe). Test `A_theme_size_asked_before_the_screen_is_known_follows_the_density_that_comes_later` (fails on the old cache). Two symptom fixes tried first in `ListView.qml` (pinning to the end, an exact fractional end; on the phone `contentY` cannot hold a fraction, Silica rounds it) were reverted. The chat check keeps the geometry in its message.
- Regression after the fix: `tools/sf matrix text controls page features collection shell navdialog` 7/7 PASS. The chat rows are back at the pre-S43 size (`contentHeight` 3307.33, atYEnd true), the mixed-font row is unchanged (50.2 dp = contentHeight).
- Tests: `MixedFontMeasureTests` (6): runs and tallest line, one font without runs, inherited size and TextTransform, auto-scaling on and off for Label, span sizes in HTML and measure, the theme cache across a density change. `FakeShim` honours runs and keeps `LastMeasureRequest`; `TestStatics` clears the theme cache. Docs: `porting-existing-apps.md` (mixed spans, Text size).

<a id="s44"></a>
### S44 · Keyboard avoidance
Plan: §M12 steps 1–2 · Audit: lifecycle L4, input design notes · Phone: legs `input text` · Depends: —
- [x] device check: long form with the keyboard up, portrait and landscape (screenshots)
- [x] keyboard height as a bottom inset; focused field scrolled into view
- [x] harness test: inset → layout pass

Done when: the last Entry of a long form stays visible with the keyboard open.

Notes:
- 2026-10-09, device check first: a new `navdialog` step `KeyboardForm` covers a ScrollView form (14 fields) and a plain one (8 fields portrait, 3 landscape, so it fits the page; a field below the screen is unreachable without a ScrollView on any platform). It calls `SailfishKeyboard.Show(last)` and checks in the page's own coordinates (they turn with the orientation) that the field lies within `0..page.height`. For the plain page it also checks that the page is back where it was once the keyboard hides.
- What Silica does on its own: `ApplicationWindow` shrinks the page area by the keyboard panel (`height − panelSize`, clipped), and `TextBase`'s `VerticalAutoScroll` keeps the cursor line visible in the nearest Flickable. **ScrollView forms already worked** in both orientations (the field's bottom exactly at the keyboard's top). **Plain content failed, nondeterministically:** MAUI got a layout pass at the shrunk height (`mauiContentHeight` 1436 / 416), so the page flickable had nothing to scroll and the field stayed clipped. One early run passed because Silica's scroll won the race.
- Fix (`MauiModelPage.qml`): `__keyboardInset` = `pageStack.panelSize` while the page is active. When the keyboard opens and the focused item has no Flickable between it and the page flickable (`__focusInScrollingContainer`), `__keyboardPan` makes the page report `pageHeight: page.height + inset`. MAUI keeps the full layout, and `__panToFocus` scrolls the page flickable so the whole host is in view (the ancestor on `canvas`; the focused item is only the inner `textEditor`, which left the underline 26 px under the keyboard). That runs 250 ms after the last inset, height or content change, once Silica's own cursor scroll has settled. When the keyboard closes the page goes back (`contentY = 0` when the page does not scroll). Fields in a ScrollView or list keep Silica's resize. During the panel animation `page.height + inset` stays constant, so MAUI sees no intermediate sizes.
- Found on the way: `Qt.callLater` does not exist in Qt 5.6 (TypeError on the phone), replaced by a Timer. An unqualified `import QtQuick.Window` shadowed Silica's `Screen` (`Screen.topCutout` TypeError ×10), now imported as `QtWindow`. The page's two `onHeightChanged` handlers were merged.
- Phone, after the fix: portrait and landscape, 16/16 checks each. Screenshots `navdialog-6-form`/`-7-form-plain`, both orientations, checked: the focused field with its underline above the keyboard, the page back afterwards. Regression `tools/sf matrix input text navdialog page controls shell containers navback pulley features collection` 11/11 PASS.
- 2026-10-09, regression found by the `f3` leg (S48 run) and fixed: `__panToFocus` scrolled to "the ancestor on `canvas`", and on a page whose form sits in a layout with its own host (a background) that is the whole form, so the page scrolled to its end and the focused Entry went off screen under the keyboard. The F3 K clear-button tap then hit nothing. The pan now targets the nearest adapter (`mauiId`), the field itself (contract assert in `KeyboardInsetTests`). `f3` 112/112, `navdialog` 16/16 after the fix.
- Tests `KeyboardInsetTests` (3): a shorter reported page lays out again and moves the bottom field up; the full height while panning leaves the layout alone; the QML contract (the inset in `pageHeight`, the qualified Window import, no `Qt.callLater`). Suite 587/587. Docs: `porting-existing-apps.md` (resize in a ScrollView, pan in plain content).

<a id="s45"></a>
### S45 · Upstream-seam workarounds: `SailfishKeyboard`, `Loaded`
Plan: §M12 step 3, §M25 · Audit: lifecycle L4, L6 · Phone: leg `input` · Depends: —
- [x] `SailfishKeyboard.Hide()/Show(view)/IsShowing`; docs say `SoftInputExtensions` throw on this TFM
- [ ] root page handler attached before the window parents it; container handlers attach on `ChildAdded` (not possible from the backend, see Notes)
- [ ] upstream issues filed (soft-input seam, `IsLoaded` in the Standard partial) and linked in the plan (needs the owner: an outward action)

Done when: root page `Loaded` sees a handler (test); both issue links in the plan.

Notes:
- 2026-10-08:
  - `Platform/SailfishKeyboard.cs` (public): `Show(view)` focuses the view and calls `Qt.inputMethod.show()`; `Hide()` unfocuses any focused InputView of the window's page and calls `Qt.inputMethod.hide()`; `IsShowing` reads `Qt.inputMethod.visible`. All run on the Qt thread.
  - Checked against the decompiled MAUI 11 Core: on `net11.0` `SoftInputExtensions` reach `platformView.Show/HideSoftInput/IsSoftInputShowing`, which throw `NotSupportedException`.
  - Phone: the last `navdialog` step passes. Show(entry) focuses the entry and Maliit opens (`Qt.inputMethod.visible`); Hide closes it and unfocuses the entry. Screenshot `navdialog-5-keyboard` checked.
- `Loaded` on a root page, probed in the harness: `Loaded` fires with no handler on the page or its child. In MAUI's plain-net build `IsLoaded => Window != null`, so `Loaded` fires the moment the app's `new Window(page)` parents the page. The app creates both objects, so the backend cannot attach a handler earlier, and re-raising `Loaded` would mean a fake `Unloaded` through reflection. This stays an upstream seam, documented in the porting guide (use `OnAppearing`/`HandlerChanged`). Pages pushed later already get their handlers first (`NativeStackSyncTests.A_shell_route_page_has_its_handlers_when_Loaded_fires`).
- Upstream issues: not filed — opening issues on dotnet/maui is for the owner to decide. The texts are ready: the soft-input partials throw on the plain TFM, and `IsLoaded` raises `Loaded` before a handler can exist.
- 2026-10-08: drafts written to [`docs/upstream-issues/`](upstream-issues/README.md): `ISSUE.md` plus a runnable `repro/` (plain `net11.0`, no device) for each. Both repros were run against MAUI 11.0.0-rc.1.26451.6 and reproduce. The code quotes are checked against that version's decompiled `net11.0` assemblies.
- Regression 2026-10-08: `tools/sf matrix popup nav navback shell containers navdialog visual shapes controls canvas features text input` 13/13 PASS.

<a id="s46"></a>
### S46 · Theme from the first frame, highlight colour, RTL locale
Plan: §M13 · Audit: lifecycle L5 · Phone: legs `silica features` · Depends: —
- [x] first render waits for (or is seeded with) the ambience theme
- [x] `SailfishTheme.HighlightColor`/`PrimaryColor`
- [x] `RequestedLayoutDirection` from Qt; `LANG`/ICU note in `sailfishos-packaging.md`

Done when: a dark-ambience start shows no light frame (recording).

Notes:
- 2026-10-09, measured before changing anything: a `tools/sf record` of the template app's start (its Styles use `AppThemeBinding`, dark ambience "fresh") already showed no light frame. The theme service ran before the first render (`StartRendering` dispatches `OnHostReady` before `Render`), and the per-frame brightness of the compositor frames (`ffmpeg signalstats`, `mpdecimate`) fell monotonically 111 → 51 with the window fading in over the ambience; a white page would push it towards 200. The gap was earlier: code reading `RequestedTheme` in the app's constructor or `CreateWindow` got `Unspecified` (AppThemeBinding falls back to Light), so colours set once at start stayed wrong.
- Seed: `SailfishTheme.Seed()` at the very start of `Run()` reads the dconf key `/desktop/jolla/theme/color_scheme` (0 light text on dark = Dark, 1 = Light, unset = Silica's default Dark) on a background task while MAUI builds the app. `SailfishTheme.Current` waits for it (200 ms at most) while still Unspecified. The theme service's `Theme.colorScheme` stays authoritative: it confirms, or corrects with ThemeChanged. On the phone, inside the sandbox: "theme: seeded Dark from dconf in 30 ms" (a process start, overlapping with BuildApplication). Recording again after the change: brightness 110 → 51 with no rise.
- Colours: the theme service's QML object exposes `palette` (highlight|primary|secondary|secondaryHighlight). It is read at start and `svc-theme-palette` reports changes (another ambience may keep the same light/dark, which ThemeChanged would not report). Public `SailfishTheme.HighlightColor/PrimaryColor/SecondaryColor/SecondaryHighlightColor` (Color?, null before the window) and `event ColorsChanged`. The `f4` leg checks that HighlightColor matches `Theme.highlightColor` (#bffe7f): CHECK OK.
- Layout direction: `SailfishAppInfo.RequestedLayoutDirection` → `SailfishLayoutDirection`: Qt's `Qt.application.layoutDirection` (from the locale) read at host ready, the current UI culture before (an InvariantGlobalization build only gets Qt's answer). Phone: LeftToRight for the English phone. An RTL language was not tried on the phone (it changes the owner's settings); the harness test covers Qt answering 1.
- Tests `ThemeSeedTests` (7): dconf parsing, the seed reaching AppInfo before the host, the palette and ColorsChanged once per change, Qt's RTL reaching RequestedLayoutDirection. `TestStatics` restores the layout direction and the seed. Legs `f4 silica features` 3/3 PASS. Suite 601/601. Docs: `sailfish-apis.md` (colours, the theme from the start), `sailfishos-packaging.md` "Locale and right to left" (LANG, ICU 73, InvariantGlobalization, Qt's direction).

<a id="s47"></a>
### S47 · `On<SailfishOS>()` platform configuration
Plan: §M14 step 6 · Audit: essentials E7 · Decision: D13 · Phone: — · Depends: —
- [x] `SailfishOS` marker + `SailfishOSSpecific.Page.AllowedOrientations` extensions
- [x] `SailfishPage.AllowedOrientations` as an `[Obsolete]` forwarder; `PublicSurface.txt`; `sailfish-apis.md`

Done when: `page.On<SailfishOS>().SetAllowedOrientations(...)` compiles in `PublicApiGuard` and has a test.

Notes:
- 2026-10-08:
  - `PlatformConfiguration/SailfishOS.cs` (`IConfigPlatform`). `PlatformConfiguration/SailfishOSSpecific/Page.cs` owns `AllowedOrientationsProperty` with static get/set and the `IPlatformElementConfiguration<SailfishOS, Page>` extensions; the renderer's `Effective` moved there.
  - `SailfishPage` is `[Obsolete]` and forwards to the same property (XAML `sf:SailfishPage.AllowedOrientations` keeps working).
  - `SailfishOSSpecific.VisualElement` (S60's KeepsDrag) got `On<SailfishOS>()` extensions too. The interface is covariant, so `button.On<SailfishOS>().SetKeepsDrag(…)` works on controls that have `On<T>()`; any view takes the static setter.
- Guard: `PublicApiGuard/App.cs` calls `page.On<SailfishOS>().SetAllowedOrientations(…)` and `button.On<SailfishOS>().SetKeepsDrag(false)`, and compiles. It also found that a bare `SailfishOS` inside a `…SailfishOS…` namespace resolves to the namespace; the guard's own namespace needs the qualified name, an app's normally does not.
- Test: `SampleAppRegressionTests` (allowedOrientations reach the model page) now sets the page through `page.On<SailfishOS>()` and containers through the static setter. In-repo users moved off `SailfishPage` (diagnostics, tests). PublicSurface regenerated. Suite 565 green.
- Docs: `sailfish-apis.md` section rewritten around `On<SailfishOS>()` (namespaces, the `Page` name clash as with AndroidSpecific, the XAML namespace, the obsolete spelling).

<a id="s48"></a>
### S48 · `HybridWebView`
Plan: §M15 step 7 · Audit: handlers H1 · Decision: D5 · Phone: leg `features` (if built) · Depends: —
- [x] (D5 a, chosen) handler on the Gecko adapter with the JS bridge and three commands — split into S48a/S48b if it does not fit
- [x] ~~(D5 b) one warning + porting guide entry~~ — not chosen

Done when: a `HybridWebView` page loads its `HybridRoot`, and JS → .NET (`RawMessageReceived`) and .NET → JS messages work on the phone.

Notes:
- 2026-10-09: fit in one session, no split. What decided the design: MAUI's `hybridwebview.js` (embedded in `Microsoft.Maui.dll` as `_framework/hybridwebview.js`) talks HTTP on its Android path. JS → .NET is `fetch` POST to `<origin>/__hwvSendMessage` (raw messages, InvokeJavaScript results) and `<origin>/__hwvInvokeDotNet`, with the `X-Maui-Invoke-Token` header; .NET → JS is a `message` event without a source window. On plain `net11.0` the matching handler logic (`MessageReceived`, `InvokeDotNetAsync`, `MapInvokeJavaScriptAsyncImpl`) is internal and the commands are no-ops, so the backend rebuilds it.
- `Platform/QtHost/HybridWebViewServer.cs`: an `HttpListener` per view on `http://127.0.0.1:<free port>/` serves `HybridRoot` files from the app root (where MauiAssets land; read directly, as SailfishFileSystem does), the embedded script, and both endpoints. Posts need the token plus an Origin or Referer of this page (MAUI's `HasExpectedHeaders`), otherwise 403.
- `Handlers/SailfishHybridWebViewHandler.cs` (factory row `HybridWebView`): the snapshot loads `origin/DefaultFile` in the Gecko adapter (`MauiWebView.qml`, unchanged). `EvaluateJavaScriptAsync` goes through the adapter's js command (`AdapterEventRouter` now routes `webview-js` to this handler as well). `InvokeJavaScriptAsync` follows MAUI's sequence: a task id, `HybridWebView.__InvokeJavaScript(id, method, [args])`, a result or error posted back, deserialized with the request's `JsonTypeInfo`; errors throw `SailfishHybridWebViewJavaScriptException` (MAUI's exception and task manager are internal), and the `HybridWebView.InvokeJavaScriptThrowsExceptions` switch is honoured. `SendRawMessage` dispatches `new MessageEvent('message', {data})`. `InvokeDotNet` runs `Invoker.InvokeMethodAsync` on the main thread and answers in MAUI's JSON shape. Disconnect stops the server and cancels pending calls. Same WebView-permission warning as WebView. Release build: 0 trim warnings.
- Tests `HybridWebViewTests` (6, real HTTP against the server): origin URL and the script, HybridRoot files with content type and 404, raw message to `RawMessageReceived` and 403 without the token, InvokeDotNet → `Target.Add`, SendRawMessage → message event, InvokeJavaScriptAsync completed (3) and failed (TypeError → exception). `PublicSurface.txt` +2 (handler, exception).
- Phone, `f3` leg step N (sample `Resources/Raw/hybridroot/index.html` as Content): the page loads from `http://127.0.0.1:39067` and says "ready" (JS → .NET); `SendRawMessage("ping")` reaches it, it calls `InvokeDotNet('Multiply', [6, 7])` and answers "echo:ping:42"; `InvokeJavaScriptAsync("add", [2, 3])` → 5. Screenshot `f3-n-hybrid` (the page's own log of the round trip) checked. The same run found the S44 pan regression (F3 K, fixed, see S44). After the fix `f3` 112/112, `navdialog` 16/16. Suite 607/607.
- Docs: `porting-existing-apps.md` (HybridWebView as on Android, HybridRoot, permission, exception type; BlazorWebView stays out).

<a id="s49"></a>
### S49 · Sailfish `IImage`
Plan: §M18 · Audit: graphics G8 · Decision: D6 · Phone: leg `shapes` + native-build · Depends: S11
- [x] `IImage` backed per D6 (decode, `Downsize`, `Resize`, `Save`)
- [x] `PlatformImage.FromStream` users get it (registration or documented factory)
- [x] tests

Done when: an `IImage` loads, resizes and saves on the phone.

Notes:
- 2026-10-09: on plain `net11.0`, MAUI's `PlatformImage` keeps the bytes and reads the PNG/JPEG size; `Downsize`/`Resize` throw `PlatformNotSupportedException`, and the `PlatformImage.FromStream` static cannot be redirected. So the backend has its own image plus the service, and the docs say so (porting-existing-apps.md).
- Native `Native/host_image.cpp` (added to native-build.sh): `sailfish_host_image_info` (QImageReader size, EXIF orientation) and `sailfish_host_image_transform` (op JSON `{w,h,mode,format,quality}`: `stretch`/`fit` letterboxed with transparent bars/`fill` cover + centre crop/`keep`, then `QImage::save`; JPEG/BMP converted to RGB32 first). Stateless, bytes in and bytes out, so no handle and no Qt thread needed: no `CheckThread`. The result size is returned and a second call is made when the first buffer (w·h·4 + 64 KiB) is too small.
- Managed: public `Microsoft.Maui.SailfishOS.Graphics.SailfishImage : IImage` (`FromStream`, `FromBytes`, `From(IImage)` for a `PlatformImage`), format sniffed from the header. `Downsize` keeps the aspect and returns an unscaled copy when the image already fits. `Resize` maps `Fit`→fit, `Bleed`→fill, `Stretch`→stretch. `Save` passes the bytes through when the format matches (and JPEG quality is 1), otherwise it re-encodes; GIF is read-only in Qt, so a resized GIF becomes PNG and `Save(Gif)` throws `NotSupportedException`. `ToPlatformImage` returns itself, `Draw` → `canvas.DrawImage` (drawn by S50). `SailfishImageLoadingService` is answered by `SailfishServiceOverlay` for `IImageLoadingService` unless the app registers one. Native calls go through `IQtHostShim.TryImageInfo`/`ImageTransform`.
- Tests `SailfishImageTests` (10; FakeShim answers with PNG IHDR / format magic at the op's size): size and format, garbage → `InvalidDataException`, Downsize aspect and no shim call when small, the three modes and rounding, Save pass-through vs JPEG q80, SaveAsync = Save, GIF → PNG, disposeOriginal, service + `PlatformImage` conversion. `PublicSurface.txt` +2. Suite 617/617.
- Phone, `shapes` leg (6 new checks, `QtHostDiagnosticsRunner.Image.cs`; `DiagPng.Encode` writes a 400×200 PNG, red left half, blue right): the loader is `SailfishImageLoadingService`; Downsize(100) → 100×50 red/blue; Fit 64×64 has transparent bars (0,0,0,0) with red/blue image; Bleed 64×64 is opaque at the top (centre crop); Stretch 50×100; JPEG 2476 B at 0.95 > 2036 B at 0.05, BMP reads back 400×200. Pixels decoded from QImage's output on the device, not just properties. `shapes` 11/11. Nothing is drawn on screen yet, so no screenshot here; the visible check comes with S50.

<a id="s50"></a>
### S50 · `DrawImage`, `ImagePaint`, screenshot as `IImage`
Plan: §M18 · Audit: graphics G5, G8 · Phone: leg `shapes` · Depends: S49
- [x] `ICanvas.DrawImage` in the Canvas adapter
- [x] `SetFillPaint(ImagePaint)`
- [x] `IScreenshotResult` → `IImage`

Done when: a GraphicsView draws an image on the phone (screenshot).

Notes:
- 2026-10-09: Context2D loads images by URL, so `Platform/QtHost/QtHostDrawnImages.cs` writes an image's encoded bytes once per image object (ConditionalWeakTable) to `~/.cache/<app>/drawn/<sha1>.<ext>`. The file is written to a temp name and renamed, and the folder is emptied once per process. Bytes come from `SailfishImage.Data`, MAUI's plain `PlatformImage.Bytes`, or `Save` for any other `IImage`. The recorder records `["img", url, x, y, w, h]`; an empty URL (disposed image) is a skip.
- `GraphicsView.qml`: `__imageReady(url)` calls `loadImage` once, logs a load error once, and `onImageLoaded: requestPaint()`. `img` → `ctx.drawImage(url, x, y, w, h)`. `fpaint ["image", url]` (from `QtHostShapes.PaintSpec(ImagePaint)`) → `ctx.createPattern(url, "repeat")`, tiled from the canvas origin, one image pixel per dp. New counter `mauiImageOps`. Known difference (porting guide): the first paint of a new image draws without it and the load paints again.
- Screenshot: MAUI 11 has no public screenshot → `IImage` API on any target, so there is a public `SailfishScreenshotExtensions.ToImageAsync(this IScreenshotResult)` → `SailfishImage` (reads the grabbed PNG directly for ours, otherwise through `OpenReadAsync`). `IViewScreenshot` was already in place.
- Tests `DrawImageTests` (5): the cache file holds the bytes and the op the rectangle, same image same URL, `PlatformImage` from its bytes, disposed → empty URL, `ImagePaint` → image fill spec, the QML contract, screenshot → 1080×2160 image. `PublicSurface.txt` +1. Suite 622/622.
- Phone, `shapes` leg (2 new checks plus shots `shapes-gallery`, `shapes-drawimage`, `shapes-drawimage-late`): `Screenshot.Default` → `ToImageAsync` 1032×2272; a pushed GraphicsView page reports 3 image ops; `CaptureViewAsync` of the view (573×344 px at density 1.91) reads red (255,0,0)/blue (0,0,255) from the drawn IImage and green (0,200,0)/yellow (255,220,0) from the ImagePaint tiles. Screenshot checked: the red/blue image, the striped ImagePaint fill and the gallery screenshot drawn small next to them. `shapes` 13/13 (on-device log; the local run.log stopped capturing earlier, as usual). In the first run the early shot showed the page without the canvas content, although the view grab 600 ms before already had it. That run had one 5.9 s render frame (the first start after a deploy). The second run's early shot has the content, and the empty shot did not happen again.

## Phase I — build, SDK, developer experience

<a id="s51"></a>
### S51 · Resizetizer through MAUI's external-backend hook
Plan: §M19 step 1 · Audit: build-sdk B3 · Phone: template app + `f3` · Depends: —
- [x] `ResizetizerPlatformType=wpf`, `ResizetizerAfterAssetProcessingTargets`, `@(MauiProcessedImage/Asset/Font)`
- [x] private `ResizetizeImages` call and manual asset/font items removed; `maui-resized.txt` kept
- [x] `Resources/Raw` location checked against `ResolveFilePath` and `OpenAppPackageFileAsync`

Done when: binlog shows the external targets ran; images and fonts render in the template app on the phone.

Notes:
- 2026-10-08: the app head sets `ResizetizerPlatformType=wpf`. Adding `AssignTargetPaths` to `ResizetizeBeforeTargets`/`ProcessMauiFontsBeforeTargets` runs MAUI's targets; nothing else triggers them on an external head. The three `ResizetizerAfter{Image,Font,Asset}ProcessingTargets` hooks hand over to `_SailfishMauiProcessedImages` (→ `images/`, app icon → `_SailfishAppIconSource` for the icon set, `maui-resized.txt` from `@(MauiImage)` metadata), `_SailfishMauiProcessedFonts` (→ `fonts/`) and `_SailfishMauiProcessedAssets` (→ `%(Link)` at the app root). Removed: `_SailfishProcessMauiImages` (the private `ResizetizeImages` call), `_SailfishMauiAssets`, the `Content @(MauiFont)` and the raw-`MauiImage` fallback. Heads only: a library's items reach the app through `ResizetizeCollectItems` on its ProjectReference, as on the in-box heads (previously each library processed its own).
- A probe on Kitchen first: `@(MauiProcessedImage)` carries no metadata. The app icon is found by the `MauiIcon` file name, and its raster (`resizetizer/r/appicon.png`, 432 px) is byte-identical to the old `sailfish-appicon.png`, as is `meal_placeholder.png`.
- Same output: Kitchen (in-repo import) 28 non-binary output files, `diff` identical. Template app (package consumer, OpenSans fonts, `dotnet_bot.svg`, `Resources/Raw/AboutAssets.txt`) identical outside `publish/`. A clean build with `-v:d -bl` ran `ResizetizeCollectItems`, `ResizetizeImages`, `ProcessMauiFonts`, `ProcessMauiAssets`, the three `ResizetizerAfter…` targets and our three consumers. With the package the evaluation-time path applies; `_PrepareExternal*` were not needed.
- `Resources/Raw`: `LogicalName` → `Link` = path under the app root. `SailfishFileSystem.OpenAppPackageFileAsync` and `QtHostImages.ResolveFilePath` both read `AppContext.BaseDirectory/<path>`, matching Android's convention.
- Phone: the template app deploys (VERIFY PASSED). The RPM holds `fonts/OpenSans-*.ttf`, `images/dotnet_bot.png`, `images/maui-resized.txt`, `AboutAssets.txt` and hicolor 86/108/128/172. The `.desktop` icon hash `appicon-529c54f2014b` is the same as in the two earlier deploys with the old pipeline. Screenshot `deploy-20261008-181543.png`: dotnet_bot renders and the page lays out as before. The fonts are installed byte-identically; which face draws the text is not checked separately (the `f3` images/fonts checks cover that path for the in-repo sample). The `f3` leg was not needed: its sample is a plain `net11.0` project that ships its images as its own Content, so this change does not touch it.
- `BuildTargetsTests.The_app_head_opts_into_the_resizetizer_external_backend_contract` (the library leaves it unset). Suite 578/578.

<a id="s52"></a>
### S52 · TFM side effects, library projects, MSBuild tests
Plan: §M19 steps 2–4 · Audit: build-sdk B2 · Phone: — · Depends: —
- [x] `SAILFISH1_0*` defines, `SupportedPlatform`, `SupportedOSPlatform` attribute
- [x] manifest RID/`SelfContained` only for non-library projects; nested restore → error with the command
- [x] `BuildTargetsTests` (evaluate a temp csproj); clean-machine workload note

Done when: a class library with the TFM builds without a RID; tests green.

Notes:
- Defines and attributes: `_SailfishDefuseMauiFrameworkConversion` still clears the TPI before `ProcessFrameworkReferences`. The new `_SailfishRestorePlatformIdentifier` (AfterTargets `ProcessFrameworkReferences`) puts `sailfish` back, so the SDK's own targets now produce `SAILFISH1_0`, `SAILFISH1_0_OR_GREATER`, `[TargetPlatform("sailfish1.0")]` and `[SupportedOSPlatform("sailfish1.0")]`. Before this, SailfishKitchen's AssemblyInfo had none of them. `<SupportedPlatform Include="sailfish" />` is added for CA1416. MAUI's platform-folder pruning (`_MauiCollectPlatformSpecificCompileItems`) runs after the restore, so its path (B) now sees `sailfish`; the sailfish head has no `SingleProject`, so nothing changes there. Not tried: the plan's binlog experiment (`UseMauiCore`-style properties instead of clearing the TPI). Keeping the clear-and-restore pair is enough for the effects listed above.
- Library vs app: `_SailfishIsApp` (`OutputType` `Exe`/`WinExe`). Only an app gets the RID, `SelfContained`, `CreateSailfishRpm` and the generated `Main` plus its trimmer descriptor, in both the manifest and the package targets. A library previously got all four, and the generated Main failed to compile without a MAUI reference. Removed the dead `<OutputType Condition="''">Exe</OutputType>`: the SDK props already default it to Library.
- Bug fixed on the way: the manifest set `RuntimeIdentifier=linux-arm64` before the package targets read `SailfishRuntimeIdentifier`, so `sf deploy` to a 32-bit phone (`SF_RID=linux-arm`) restored and built arm64. The manifest now reads `SailfishRuntimeIdentifier` first (test `SailfishRuntimeIdentifier_picks_the_heads_rid`).
- Nested restore: `_SailfishRefreshRestoreForRid`, which ran `dotnet restore` from inside the build, became `_SailfishCheckRestoreHasRid` (error `SAILFISH0001`, before `ResolvePackageAssets`, apps only). It prints the restore command, using `-p:SailfishRuntimeIdentifier` so other heads are left alone, and the manifest install command. Checked by restoring with `linux-arm` and then building with `--no-restore`: SAILFISH0001 instead of NETSDK1047.
- `BuildTargetsTests` (4 tests): a temp csproj imports the in-repo `WorkloadManifest.targets.in` (CustomBefore) and package targets (CustomAfter), with `MSBuildEnableWorkloadResolver=false` so an older installed manifest cannot interfere. Covered: app head defaults, library defaults, `SailfishRuntimeIdentifier`, and a library build that fails on `#error` without the three defines and checks the attributes in AssemblyInfo.
- Clean-machine note: already in `add-sailfish-to-existing-app.md` §1 (`dotnet workload install sailfish` only works after the tool installs the manifest; option (a) stands). That doc now lists the new symbols and attribute and how a class library behaves.
- Refreshed the local feed (`tools/sf pack-local`) and the SDK manifest (`tools/sf workload-install`). A scratch `dotnet new maui-sailfish` app with a `net11.0-sailfish` class library (code under `#if SAILFISH1_0_OR_GREATER`) builds: the library has no RID folder and carries the attributes.
- Phone: the first `sf deploy` of that app failed with NETSDK1152, because the library also wrote `obj/maui-appmeta.json` as Content at `qml/maui-appmeta.json`. Fixed: `_SailfishWriteAppMeta` and the `images/maui-resized.txt` Content are now heads only, and a library's images still reach the app. The build test asserts that the library writes no app meta. After the fix, `deploy --run --screenshot` verified (D4–D6). Screenshot `artifacts/screenshots/deploy-20261008-140145.png` shows the button text "S52 lib: SAILFISH1_0_OR_GREATER", coming from the library.
- `PublicSurface.txt` was stale (the full suite failed on it): added the deliberate APIs of earlier sessions, namely the PlatformConfiguration `SailfishOS`/`SailfishOSSpecific.Page`/`VisualElement` (S47/S60), `SailfishLegacyListViewHandler` (S32), `SailfishToolbarHandler`, `SailfishExceptions` + `SailfishUnhandledExceptionEventArgs` (D11) and `SailfishKeyboard` (S45). Suite 571/571.

<a id="s53"></a>
### S53 · Trimming profile and feature switches
Plan: §M20 · Audit: build-sdk B4 · Phone: `tools/sf package-test --trimr2r` · Depends: —
- [x] `IsTrimmable=true` on the backend; descriptor narrowed to the app
- [x] feature-switch table in `aot-and-trimming.md`; composite R2R measured

Done when: Release RPM size and startup measured before/after in `aot-and-trimming.md`; matrix legs `page controls collection` green.

Notes:
- 2026-10-08, results in `aot-and-trimming.md` §2.5.
- `IsTrimmable=true` on `Microsoft.Maui.SailfishOS.dll`: the trim analyzer already ran in Release with 0 warnings. Effect −60 KB RPM. Startup and CPU stay within run-to-run noise.
- Descriptor: `scaffold/trimmer.xml` (`*MauiProgram` rooted in **every** assembly) is removed. `_SailfishTrimmerDescriptor` writes `obj/sailfish-trimmer.xml` naming the app assembly (BeforeTargets `PrepareForILLink`), covered by a test. The plan's alternative (a generated attribute instead of reflection discovery) was not needed: under partial the app is copied untrimmed anyway.
- Feature switches, found while building the table: the SDK turns reflection-based System.Text.Json off for every trimmed app. Android turns it back on for partial, we didn't, so `GetFromJsonAsync<T>`/`Deserialize<T>` without a source-gen context worked in Debug and threw on the phone in Release. Now aligned with Android: JSON reflection and `[DefaultValue]` on (partial); DI open-generic verification, startup hooks and HTTP activity propagation off. Kept deliberately different: `EventSourceSupport` (dotnet-trace), `DebuggerSupport` (Release attach), `UseSystemResourceKeys` (readable exceptions in the log), `UseSizeOptimizedLinq`. Cost +0.4 MB RPM. Test `A_trimmed_release_sets_the_android_feature_switches`. f4 leg on the phone: "reflection System.Text.Json round trip (IsReflectionEnabledByDefault=True)" CHECK OK.
- New metric in the perf leg: `processToFirstFrameMs` (process start → first frame; `firstFrameMs` only counts from the shim's start, after runtime start and JIT).
- Measured, perf leg ×3 per variant, all PASS. Launch → first frame: before 934–977 ms / 13.70 MB RPM; shipped (IsTrimmable + switches) 930–957 ms / 14.04 MB; composite R2R 882–902 ms / 15.10 MB (opt-in, not the default: +10% RPM, +8.5 MB on disk for −5% startup); composite partial with MAUI's Android MIBC 1184–1215 ms / 8.97 MB (Android's profiles don't cover our paths; a Sailfish MIBC recorded with dotnet-pgo is a follow-up, not tried).
- Found on the way: `sf-depscheck.py` (S61) rejected the composite `<app>.r2r.dll` (not in deps.json by design). It now skips `*.r2r.dll`.
- Matrix `page controls collection f4`: 4/4 PASS (18/49/31/45 checks).
- Phone state during the run: from ~17:24 Android App Support was starting up (load average 12.5, CPU idle). The ssh session to the launch helper hung for ~9 min per leg. Verdicts are unaffected: they come from the device log. Nothing restarted.

<a id="s54"></a>
### S54 · `dotnet run`, env switch table
Plan: §M21 steps 1, 4 · Audit: build-sdk B5 · Phone: template app · Depends: —
- [x] `RunCommand`/`RunArguments` → deploy + run + log stream, Ctrl+C kills
- [x] `MAUI_SAILFISH_*` table in `tools.md` generated/checked by a test

Done when: `dotnet run -f net11.0-sailfish` starts the template app on the phone and Ctrl+C stops it.

Notes:
- The SDK's device protocol, as Android uses it (`Microsoft.Android.Sdk.Application.targets`): after the build the CLI calls `DeployToDevice` if the target exists, then `ComputeRunArguments`, then runs `RunCommand`. New `buildTransitive/Microsoft.Maui.Platforms.SailfishOS.Run.targets` holds `DeployToDevice` → `_SailfishDeployToDevice` (`sf setup --if-needed` + `sf deploy`, with `SF_PKG/SF_BIN/SF_TFM/SF_CONFIGURATION/SF_RID` from the project) and `_SailfishComputeRunArguments` (`/usr/bin/env SF_… bash sf run --follow`). It is imported for the sailfish app head only, because a `DeployToDevice` on every leg would replace the Android/iOS one when a multi-head app references the package there too. No `ComputeAvailableDevices`: with one paired phone there is nothing to choose, and `--device` would need a list `sf` doesn't keep.
- `sf run --follow` (new): launch, then `tail -f /tmp/sf_run.log` until the process is gone (expect timeout off), returning the app's logged exit code. Ctrl+C stops the app. Trapping INT wasn't enough: under expect the ssh lives in its own session, and bash skips the trap when the foreground child exits on its own. So when the stream ends while the app still runs, the stream was cut and the app gets stopped.
- Found on the phone: on a fresh clone (no `obj/`) the CLI checks for `DeployToDevice` **before restore**, where the package targets aren't imported yet, so it skipped the deploy and ran the old install. It also calls the target on that same pre-restore evaluation (with a plain `DependsOnTargets` on the package target: MSB4057). Fix: the workload manifest gets a third file, `WorkloadManifest.Run.targets`, imported for sailfish app heads. Its `DeployToDevice` calls `<MSBuild Projects="$(MSBuildProjectFullPath)" Targets="_SailfishDeployToDevice">` with an extra global property, so the project is evaluated again (restored by then) and not taken from the cache. The file ships in the manifest nupkg (`data/`), is embedded in the `sailfish-workload` tool (`ManifestFiles`) and copied by `tools/sf workload-install`. With an older manifest, the package's own `DeployToDevice` still covers every run after the first restore.
- Checked with a fake `sf` (prints its arguments): fresh clone, second run, `-v:n`, `-p:`, all `setup --if-needed` → `deploy` → `run --follow`. Phone, scratch template app plus S52 library, fresh `obj/`, package tools: `dotnet run -f net11.0-sailfish` → VERIFY PASSED → launched → "streaming the app log", screenshot `s54-running.png` shows the app. SIGINT to the process group (as a terminal's Ctrl+C) → "stopping harbour-s52app on the device", exit 130, `pgrep` on the phone: NOT-RUNNING.
- Env switches: the tools.md tables listed 37 of the 89 names the sources read. Added the missing 52: every diagnostics leg by name, the runtime/A-B knobs, and a new "Developer aids" table (`TAPS`, `SHOT_*`, `BACK_AFTER`, `PULL_GESTURE`, `OPEN_PULLEY`, `NAVBACK_FILM`, `DIAG_CRASH`). Removed the stale `…_BRIDGE` (now `…_BRIDGE_DIAG`). `EnvSwitchTableTests` checks both directions over `src/**/*.{cs,cpp,h,qml,js}` (bin/obj excluded), and understands the `…_X` shorthand for `MAUI_SAILFISH_QT_HOST_X`. I chose checked over generated: the descriptions need prose a generator could not write.
- `BuildTargetsTests` +2: run arguments on an app head; no `DeployToDevice` on a library. Docs: README, `add-sailfish-to-existing-app.md` §4 and `sailfishos-packaging.md` lead with `dotnet run`; `SailfishRun` remains for "back after the launch window". Suite 575/575.

<a id="s55"></a>
### S55 · Hot reload over SSH — deferred by D7 (b)
Plan: §M21 step 2 · Audit: lifecycle L8 · Decision: D7 · Phone: yes · Depends: S54
- [ ] (D7 b) `EnableMauiIncrementalHotReload=false` on the head, documented
- [ ] (D7 a) startup hook + SSH-forwarded delta channel + `MauiHotReloadHelper` — expect S55a/b/c

Done when: per the chosen option.

Notes:
-

<a id="s56"></a>
### S56 · Device tools out of the NuGet package
Plan: §M22 · Audit: build-sdk B5 · Decision: D8 · Phone: — · Depends: S07
- [x] D8 (b): what a package user needs from `tools/sf` (setup, deploy, run, screenshot, logs) as a C# dotnet tool (works on Windows
  too); the repo's own matrix/shots tooling may stay bash
- [x] the package no longer ships the bash scripts; the `SailfishRun`/`SailfishSetup` targets call the tool
- [x] docs: `tools.md`, `connecting-your-phone.md`, README

Done when: a template app is set up, deployed and run on the phone from Windows (or a clean macOS) with only the dotnet tool.

Notes:
- 2026-10-09, split. A dotnet tool alone does not make Windows work: the RPM was built by host `rpmbuild` or `python3 tools/py/sf-rpmbuild.py`, and the targets ran `chmod`, `ln -sf`, `find` and `command -v`. Parts: **S56a** packaging with no Unix program; **S56b** the `sailfish` dotnet tool (setup/pair, deploy, run `--follow`, kill, screenshot, logs) over the system `ssh`/`scp` (OpenSSH ships with Windows 10+), with the askpass transport and devel-su on stdin as in `sf-lib.sh`; **S56c** the package stops shipping the bash scripts, `SailfishRun`/`SailfishSetup`/`DeployToDevice`/`dotnet run` call the tool, docs.
- S56a (done): `Build.Tasks/SailfishRpmPack.cs` ports the writer of `sf-rpmbuild.py` (lead, tag-sorted headers with the immutable-region trailer, type-aligned store, sha256 file digests, the signature semantics the phone's rpm 4.16 checks, payload digest). The metadata comes from MSBuild, so there is no spec parser. Differences from the python writer: the payload is gzip (no xz in the BCL; sample 18.2 MB vs 14.1 MB). Modes come from content (dirs and ELF/`#!` 0755, else 0644, the same on every host; the python builder took host modes, `.so` 0744 on this Mac). `/usr/bin/<pkg>` is a declared symlink (0777). The targets use the task by default; `-p:SailfishRpmBuilder=rpmbuild` keeps host rpmbuild, and the chmod/ln/find Execs now run only on that path. The store metadata sha256 uses `GetFileHash` instead of python3. Same buildroot through both writers: 161 entries, identical paths, sizes and digests (sf-rpmquery), `bsdtar` reads the gzip payload. Tests `RpmPackTests` (4): files, modes, digests and link read back from the headers and the cpio; the signature as rpm checks it; missing listed path → error; the targets run no python3/`command -v`, and chmod/ln/find only for rpmbuild. Suite 626/626.
- Phone: `tools/sf deploy` with the task-built RPM: install OK, verify L1/D1–D6 passed (D2 161/161 files, D3 `rpm -V` clean). The matrix refused while the phone was locked; once it was unlocked, the task-built RPM ran the `shapes` leg 13/13 through the new tool (below), and the sample rendered (screenshot).
- S56b (done): `src/Linux.SailfishOS.Tools` (net10.0, RollForward Major, `PackAsTool`, command `sailfish`, package `Microsoft.Maui.Platforms.SailfishOS.Tools`). Commands `setup [--force|--if-needed]`, `deploy [--run] [--follow]`, `run [--follow] [--env N=V]`, `kill`, `logs [--follow]`, `screenshot [-o]`; options `--project`, `-c`, `-f`, `--rid` (default from the phone's `uname -m`), `--package`, `--property`, `--connect-info`.
  - Transport as `sf-lib.sh`: the system `ssh`/`scp` with the same options, the script on stdin, and the host key pinned in `~/.config/maui-sailfish/known_hosts` (Windows `%APPDATA%\maui-sailfish`). The password reaches ssh through the tool acting as `SSH_ASKPASS` (`SSH_ASKPASS_REQUIRE=force`; a temp script under `dotnet sailfish.dll` on Unix), and devel-su gets it base64'd on stdin.
  - The phone side is the embedded `tools/remote` scripts, pushed in one round trip.
  - `setup` asks again on errors and rejects a malformed address. With an empty password ssh asks itself. With no console it uses `/dev/tty` (Unix; MSBuild Exec), else it prints the command to run.
  - Deploy follows `sf deploy`: publish, one RPM, kill, scp, sha256 check, `rpm -Uvh` as root (or pkcon), `rpm -q`, rm. Not ported: `verify.sh`'s digest diff and `sf-depscheck.py`; they stay checkout-only.
  - A Debug build without `--package` installs as `<pkg>-debug`, as `tools/sf` does.
- S56c (done): the backend csproj packs `artifacts/sailfish-tool/sailfish.{dll,runtimeconfig.json,deps.json}` under `buildTransitive/net11.0/tools/sailfish/` (ProjectReference, no output reference) instead of the 26 bash/python files. The targets run `"$(DOTNET_HOST_PATH)" "$(SailfishToolDll)" …`: SailfishRun → `setup --if-needed` + `deploy --run`, SailfishSetup → `setup [--force]`, Run.targets DeployToDevice → `deploy`, RunCommand = dotnet, RunArguments = `sailfish.dll run --follow --project … -f … -c … --package … --rid …`. No `bash`, `/usr/bin/env` or `SailfishToolsDir` is left in the targets. pack-local also packs the tool package. The template's `.vscode` keeps `SF_TOOLS_DIR` (a checkout's tools).
- Fixed on the way: `run --follow` (tool and `tools/cmd/run.sh`) treated an app that finished inside the helper's 10 s launch window as a failed start. It now reports the logged exit code (shapes leg: 13/13, exit 0). Ctrl+C used to be `CancelKeyPress` only; it is now `PosixSignalRegistration` SIGINT+SIGTERM, so an IDE or `dotnet run` stopping the tool also stops the app.
- Tests: `SailfishToolTests` (10: connect.info read/write and 0600, options, env blob quoting, root script hides the password and the script, RID/arch, quoting, embedded scripts); `ToolsPackagingTests` rewritten (the package carries the tool and nothing from tools/, the targets call packed files and no bash, the template's `SF_TOOLS_DIR` files exist); `BuildTargetsTests` run arguments. Suite 635/635.
- Phone (this Mac):
  - `sailfish deploy --run` of the sample → `logs` → `screenshot` (checked) → `kill`.
  - `run --follow` of the shapes leg → 13/13, exit 0.
  - SIGINT and SIGTERM during `--follow` → rc 130 and the app stopped on the phone.
  - Askpass checked with key login off: wrong password rc 255, right one rc 0.
  - A `dotnet new maui-sailfish` app outside the repo, `dotnet run -f net11.0-sailfish` with the repo's tools off PATH: built (Debug, 35.5 MB), uploaded, installed, started, streamed, stopped by SIGTERM (test package removed afterwards).
  - Not done: a Windows run (no Windows machine here) and a truly clean macOS, so S56 stays open on its "done when". The interactive `setup` was exercised through a pty only up to its key-install path (the askpass part was checked separately as above).

<a id="s57"></a>
### S57 · Host CI, code style, template smoke test
Plan: §M23 · Audit: build-sdk B7 · Phone: — · Depends: S52
- [x] host-only CI workflow (build slnx, `dotnet test`, pack with `SailfishAllowMissingShim=true`)
- [x] `.editorconfig` + `dotnet format --verify-no-changes` (format replaced by `tools/ci/style-check.cs`, see notes)
- [x] template `dotnet new` + build smoke test

Done when: the workflow passes on a branch.

Notes:
- 2026-10-09: `tools/ci/host-ci.sh` (stages style, build, test, pack, template) is the CI, and `.github/workflows/host-ci.yml` runs it: an ubuntu-latest job (gate, uploads the packages and the smoke RPM) and a windows-latest job under Git Bash (build, test, pack, template; `continue-on-error` until it has passed once — it is also the Windows proof S56 is missing).
  - Build uses `tools/ci/Linux.Sailfish.ci.slnf`: the slnx minus the template app, whose Android/iOS heads need MAUI workloads a Linux runner lacks. The template is covered by the template stage instead.
- Style: `dotnet format whitespace --verify-no-changes` was measured and rejected. It reports ~6600 items, and applied to one file it re-indented comments to column 60 and broke aligned continuation lines (tabs plus space alignment). The gate is `tools/ci/style-check.cs`, a file-based app: LF, a final newline, no trailing whitespace except Markdown, tab-indented C#; `--fix` repairs all but indentation. `.editorconfig` documents these and the C# layout. `.gitattributes` (`* text=auto eol=lf`) keeps LF on Windows checkouts, where CRLF would break the scripts under Git Bash; the index held only LF and binary files. The fixes: 37 files without a final newline or with trailing whitespace (`--fix`), `SailfishIconSet.cs` and one line of `sf-trace-analyze.cs` re-indented with tabs. SailfishKitchen's `Resources/Seed` data is excluded.
- The template stage installs everything from the packages only:
  - its own NuGet cache, the manifest through the packed workload tool's `--manifest-root`, the template from the packed template package into a custom hive;
  - `dotnet new maui-sailfish --sailfish-only`, `dotnet build`, then a Release `dotnet publish` with `SailfishSkipNativeCheck`, which must write the RPM.
- Two real bugs this found on the first fresh-copy runs:
  1. The `sailfish-workload` tool (S54) never had `WorkloadManifest.Run.targets`: MSBuild read `.Run.` as the culture "run" and put the file in a `Run/` satellite assembly, so `install` failed. Fixed with `WithCulture="false"`; test `Every_manifest_file_is_embedded_in_the_tool_itself`.
  2. A Release `dotnet publish` of a package-based app on a clean NuGet cache failed with NETSDK1094. NuGet evaluates with `ExcludeRestorePackageImports`, so the package's ReadyToRun/trim settings never reached the restore and crossgen2 was not downloaded; it only worked where the packs were already cached. The workload manifest now sets `PublishReadyToRun`/`PublishTrimmed` during restore only, unless `SailfishReadyToRun`/`SailfishTrim=false`. Test `The_restore_brings_the_ready_to_run_and_trimming_packs_…`.
- Verified: a fresh copy of the working tree (tracked + untracked files, no artifacts/bin/obj) runs `tools/ci/host-ci.sh` end to end on this Mac: style 591 files clean, build, 637 tests, 8 packages, template RPM 16.7 MB. Not run on GitHub: the user commits and pushes. Open: "passes on a branch" and the first Windows result.

<a id="s58"></a>
### S58 · Template alignment
Plan: §M24 · Audit: build-sdk B8 · Decision: D9 · Phone: template app · Depends: S07
- [x] official symbols (`applicationId`, `Framework`, fresh `PhoneProductId`)
- [x] `UseMaui=true` on the head when the MAUI SDK is present
- [x] `Styles.xaml` per D9

Done when: a generated app builds for Android and Sailfish and runs on the phone (screenshot).

Notes:
- 2026-10-09, symbols from the MAUI 11 `maui-mobile` template (`microsoft.maui.templates.net11` 11.0.0-rc.1.26451.6):
  - `applicationId` → `nameToLower`/`nameToAppId`/`defaultAppId`/`finalAppId`, which replaces `com.companyname.mauisailfishapp`. The audit's "always com.companyname.mauisailfishapp" was only half true: sourceName already lower-cased the name into it; what was missing was the override.
  - `Framework` (choice net11.0, replaces `net11.0`, `-f/--framework` through `dotnetcli.host.json` as officially) and `HostIdentifier`.
  - `PhoneProductId`: the guid generator replaces the fixed `1FB9328B-…` in `Package.appxmanifest`; the literal stays, so the in-repo project still builds.
- `UseMaui`: `<UseMaui>true</UseMaui>` unconditional in the multi-head variant, as in the official template. That machine has the MAUI workload anyway for Android, and the Sailfish head built with it: implicit `Microsoft.Maui.Controls` from the workload, the resizetizer through S51's external-backend hook. `--sailfish-only` sets none: no workload there. In the repo both `#if` branches are active, so building `templates/maui-sailfish-app` in place needs the MAUI workload; CI builds the slnf without it and checks the template through generation.
- D9 (b): all 72 colour setters in `Styles.xaml` (`*Color` with `AppThemeBinding`/`StaticResource`) are wrapped in `<OnPlatform x:TypeArguments="Color"><On Platform="Android, iOS, MacCatalyst, WinUI" …/>`, with a comment at the top of the file. On SailfishOS no branch matches, the value stays null and the backend takes the Silica theme. It covers Page/Shell/NavigationPage backgrounds too: wrapping only Button/Entry/Label left an opaque OffBlack page over the wallpaper (first phone experiment, screenshot). Android compiles the `On` value as before. Porting guide and README describe it.
- Tests `TemplateTests` (3): the symbols replace what the files contain, no colour setter outside an in-box-only OnPlatform, UseMaui only in the multi-head branch. Suite 640/640; style clean; `host-ci.sh build pack template` passes (smoke RPM 16.9 MB).
- Generated apps (pack-local): AlphaApp (`com.companyname.alphaapp`, PhoneProductId 9719B947-…), Beta.App with `--applicationId org.example.betatest` (92004C81-…), Gamma `--sailfish-only` (net11.0-sailfish, no UseMaui), Delta with `-f net11.0`. AlphaApp: `dotnet build -f net11.0-android` OK; `sailfish deploy --run -f net11.0-sailfish` → screenshot: ambience wallpaper behind the page, headings in the highlight colour, translucent Silica button, the Shell flyout as the pull-down indicator. Test packages removed from the phone afterwards.
