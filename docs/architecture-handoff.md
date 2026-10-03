# Architecture handoff: second review and the remaining work

Written for: the agent (or person) who continues the architecture work after commit `ab808cd` ("chtecture
refactoring", 2026-10-03), without having taken part in the two reviews or the refactor day.

This is the result of a second review of the code as it is **after** `docs/architecture-plan.md` was executed. It
does not repeat the plan; it lists what the plan left open plus what the second review found, each as a work
package with files, symbols, line numbers (as of `ab808cd`), tests and an acceptance check. Nothing in this file has
been implemented. Read `docs/architecture-plan.md` first for the ground rules, the owner decisions and the device
evidence log; its status table at the top says what each plan step is at.

## State at `ab808cd`

| Measure | Value |
| --- | --- |
| Core source (`src/Linux.SailfishOS`, .cs) | 23 783 lines, largest file 1 323 (`QtHostPageRenderer.cs`) |
| QML + JS | 6 498 lines, largest `MauiModelPage.qml` 1 055 |
| Native shim | `sailfish_host.cpp` 3 388 lines, one file, ABI 3 |
| Host tests | 352 (`dotnet test tests/Linux.SailfishOS.Tests`, about 1 s) |
| Device matrix | 31/31 legs PASS, 2026-10-03 15:12 (`/tmp/sf-matrix-summary-20261003-151004.txt`, `…-151217.txt`) |
| Diagnostics project | 10 179 lines, reaches the core through `InternalsVisibleTo` (501 `QtHostRuntime.` uses, 498 `renderer.` uses) |

Layer direction today: `Handlers` → `Platform/QtHost` is allowed and heavy (19 `NativeElementHost`, 17 `QtHostUnits`,
15 `QtHostTextMetrics`, 12 `QtHostRuntime` uses); `Platform/QtHost` → `Handlers` is down to interfaces and three
concrete types (`AdapterSnapshots`, `SailfishPageContainers`, `SailfishListViewHandler`, `SailfishWebViewHandler`
in `AdapterEventRouter.cs`, `GetHandler` in `QtHostLayout.cs`). Two raw `Environment.GetEnvironmentVariable` reads
remain outside `SailfishEnv` (`QtHostRuntime.cs` `MAUI_SAILFISH_STRICT_THREAD`, `QtHostListAdapter.cs`
`MAUI_SAILFISH_LIST_DEFER`).

## After `ab808cd` (2026-10-03 evening, uncommitted)

Work done on top of `ab808cd` in a separate session; nothing in the packages below was touched except W1.7.

| Change | Files | Evidence |
| --- | --- | --- |
| MAUI animations tick on Qt's frame clock (`SailfishFrameTicker`; the service overlay answers `IAnimationManager` unless the app registered its own) | `Platform/QtHost/SailfishFrameTicker.cs`, `SailfishServiceOverlay.cs` | test `An_animation_advances_one_tick_per_Qt_frame_and_then_stops`; leg `visual` G: RotateTo 600 ms = 55 ticks in 644 ms (~85 Hz panel), 0 layout passes |
| Dialogs laid out as Jolla's own (header buttons, large highlight title, message under it, ambience behind, scrolling; prompt Enter accepts; action sheet as full-width rows, destructive in `palette.errorColor`) | `qml/dialogs/*.qml` | leg `popup` 15/15 with a new "rows span the page and show their text" check; `tools/sf shots` popup-alert/prompt/sheet |
| W1.7 `ReportNaturalSize` and a bigger defect behind it: a remote `Image` without a size in a **list row** stayed 0 × 0, because `IView.InvalidateMeasure()` raises no `MeasureInvalidated` and the row watcher listens to that event. Fixed with `QtHostImages.InvalidateIntrinsicSize` (the Controls path), used for remote and stream sizes | `QtHostImages.cs`, `SailfishControlHandlers.cs` (image handler), `QtHostPageRenderer.Walk.cs` | test `Every_row_image_of_a_shared_remote_url_takes_the_size_once_it_loaded` fails without either half |
| Bench: one composite QML component per DataTemplate vs five createObject calls (dropped, −5%) | `QtHostDiagnosticsRunner.AdapterBench.cs` | leg `adapterbench` "bench card" line |

State: 354 host tests; full matrix 31/31 PASS 2026-10-03 17:41 (`/tmp/sf-matrix-summary-20261003-174115.txt`), then
`f3 collection collection100 controls visual` 5/5 after the image fix (`…-175223.txt`). Kitchen tour: detail stall
150–177 ms, catalog 440–450 ms.

Same pattern elsewhere, worth a look in W5: any handler that changes a view's size without a property change must
use the Controls invalidation (`InvalidateMeasureNonVirtual(MeasureChanged)`), or list rows never see it.

## How to work

- The rules in `docs/architecture-plan.md` ("Ground rules") hold: never commit, never restart lipstick, no edits to
  `src/` or `tools/*.sh` while a detached device run is in progress, back up after each milestone, host tests then
  device legs, `tools/sf native-build` after any `Native/*` edit, `tools/sf pack-local` after any change the
  SkiaSharp probe or the template must see.
- Every package below ends with its acceptance check. A package that touches the renderer, the bridge, QML or the
  shim is closed on the phone by the named legs; a package that touches every path (W1, W3, W8) is closed by the
  full matrix (`tools/sf matrix` with no legs, detached, about 25 min, plus `SF_SAMPLE_DIR=samples/SkiaSharpProbe
  tools/sf deploy && tools/sf matrix skia skiainput`).
- Two pitfalls from the refactor day, both caught before the phone saw them, one not:
  - The fake shim can agree with a bug. `QmlPage.Call` dropped the called function's return value; `FakeShim`
    parsed the call text and answered anyway, so every host test passed while the row pool was off on the device
    (found in the device log, not by a verdict). After a bridge change, read the device log for the counters the
    change should move, not only the PASS line.
  - Bulk regex rewrites need a read of the rewritten helper itself (`set_error` became recursive; a rename missed a
    capitalised QML handler). Both have contract tests now (`NativeContractTests`, `AdapterQmlTests`); add one when
    you do the same kind of rewrite.
- Line numbers below are from `ab808cd`. They drift; grep the symbol when one no longer matches.

## Work packages, in the order to do them

Priority: W1 and W2 first (defects and the harness that would hide them), then W3–W6 (structure, each closed by
legs), W7 anytime, W8–W9 when the phone and a quiet hour are available, W10 needs the owner (18 decisions, each
with a recommendation).

### W1. Correctness defects found by the second review

Each is small. Do them one at a time with a host test that fails without the fix (as the refactor day did), then
the legs named. All of them together: full matrix.

1. **Reconcile re-entry double-counts time.** `FinishPass` calls `Reconcile()` from inside the outer `Reconcile()`
   (`QtHostPageRenderer.cs:1158-1163`); the inner pass adds to `ReconcileTotalMs`/`ReconcileCount` and the outer
   stopwatch (`:709-719`) counts the same time again, and `LastReconcileMs` is overwritten. Fix: the unarranged retry
   calls `ReconcileCore()` directly under the existing `_unarrangedRetry` guard, or the outer timing is skipped when
   `_unarrangedRetry` is set. Test: a page with a shape measured after its first arrange; assert `ReconcileCount`
   grows by the number of passes and `ReconcileTotalMs` is not more than the wall time of the outer call.
2. **The reconcile gate misses an operation in flight.** `_stack.PopUnsynced` is cleared on every poll
   (`QtHostPageRenderer.cs:564`) and only set again by `NativeStackCoordinator.Step`; `SyncNativeNavigation` returns
   before `Step` when a dialog or flyout is open (`QtHostPageRenderer.Navigation.cs:328-335`), so `CanReconcile`
   (`:633`) can pass with a `FollowNative` operation still running. `ReconcileSubtrees` checks `_navOp`
   (`HandlerTree.cs:83`); `CanReconcile` does not. Fix: `CanReconcile` adds `_navOp is null or { Kind:
   FollowNative, MauiDone: true }` (mirror `NavigationSettled`, `Navigation.cs:127`) and `PopUnsynced` is cleared by
   the coordinator when the follow completes, not by the poll. Test: in `NativeStackCoordinatorTests` style with the
   harness: push a dialog (`DisplayAlert`), pop natively, assert no reconcile until MAUI followed.
3. **Posted layout/geometry passes lack the reconcile guards.** `RunRequestedLayout`/`RunRequestedGeometry`
   (`QtHostPageRenderer.cs:655-686`) check `_navStackBusy` and `CreationDeferred` only, not `_nativeTopUnfollowed`,
   `PopUnsynced`, `_navOp` or the reset hold `_resetHoldUntilMs` (`:779-783`). A pass in that window flushes
   geometry onto dying hosts → `HealIfDead` → `_fullResetPending` (`Layout.cs:551-552`, `Hosts.cs:382-383`). Fix:
   one `bool PassesAllowed` used by both and by `CanReconcile`'s callers.
4. **Two reconcile entry points bypass the gate.** `PopModelPages` calls `Reconcile()` directly from inside
   `SyncNativeNavigation` → `Step` (`Navigation.cs:531`; the comment explains the paint-before-reveal reason) and
   `PollCore` then reconciles again (`:568-569`); `ApplyWindowGeometry` calls it directly too (`Layout.cs:211`, now
   gated by `CanReconcile` but still outside `PollCore`). Keep the pop-path paint but mark the pass done for the poll
   (`_reconciledThisPoll`), so a poll runs at most one reconcile.
5. **Flags that break when nested.** `_mappingRows` (`QtHostPageRenderer.cs:27`, `Walk.cs:19-31`): a handler push
   during `MapItemSubtree` can reach `OnFlattenedPush` → `RemapRowContaining` → `MaterializeRow` → `MapItemSubtree`
   (`Hosts.cs:123-126` → `Walk.cs:229-234`) and the inner `finally` clears the flag under the outer mapping.
   `QtHostCollectionBridge.SlotMapping` (`QtHostListAdapter.Rows.cs:283/308`, `Slots.cs:93-123`) and `_inRebuild`
   (`Rows.cs:18-26`) are the same shape. Fix: depth counters with a `using` scope (`_suppressPush` and
   `LayoutRequestHold` already are counters). Test: a template whose view sets a list property during the build
   (the existing `A_rebuild_triggered_from_inside_a_rebuild_runs_after_it`) plus one for `MapItemSubtree` re-entry.
6. **Static deferred destroys.** `_deferNativeDestroy` and `PendingNativeDestroys` (`QtHostPageRenderer.cs:1290-1291`)
   are process-wide; the flag is set around `TearDownHosts` (`Navigation.cs:514-516`) which also retires lists, so
   row destroys are deferred too; the flush runs only when a nav snapshot reads `!busy` (`:265-266`) and never if the
   pop fails at `:544`. Fix: instance fields on the renderer, flushed also from `OnResynced` and from the pop
   failure path. Test: pop with `FaultNextPop`, assert no handle stays queued (expose the count internally).
7. **Open defects carried over from the plan, still open:** `PushBatch` returns `true` on a partial shim rejection
   (`Hosts.cs:81-89`), so `ApplyUpdates` counts the batch as pushed and `reconcileDiffPushes` grows; `_createDeferred`
   is cleared before the reset-hold early return (`QtHostPageRenderer.cs:732` vs `BeginPass`); `CanReconcile` ignores
   an open dialog/flyout (`_dialogTcs`, `_openFlyout`); ~~`QtHostImages.ReportNaturalSize` returns false for a second
   `Image` sharing a URL~~ (done after `ab808cd`, see above); `SailfishMeasure.Indicator` width formula looks mis-simplified
   (check on the device with the `controls` leg before changing); `SailfishSynchronizationContext.Send` has no
   timeout after the loop ended and `Dispatch` enqueues after shutdown (`SailfishDispatcherProvider.cs`).
8. **Collections:** `_attachRetries` is both the lock object (`QtHostCollectionBridge.cs:395,403,424,430,442`) and a
   list the adapter mutates without that lock (`QtHostListAdapter.Delegates.cs:19-36`); `SailfishConnectivity`
   getters run `ReadInterfaces()` off the Qt thread and write `_access`/`_profiles` unsynchronized
   (`SailfishDevices.cs:278-280` vs `Apply`); `SailfishPickers._pending` is a **static** `TaskCompletionSource` on
   an instance class, read and written without a lock (`SailfishShareAndPickers.cs:106-107,179-180`). Two
   misleading indentations change behaviour from how it reads: `QtHostCollectionBridge.cs:326-327` (`SchedulePending`
   runs once after the loop) and `QtHostListAdapter.Scroll.cs:46-48` (the resync reschedule runs on every scroll
   report).
9. **Lifecycle ordering.** The app subscribes to theme and cover events in `Run()`
   (`SailfishMauiApplication.cs:151-168`) but `SailfishTheme`/`SailfishCover` subscribe at the first Qt tick
   (`SailfishEssentials.cs:140-142`), so `OnColorSchemeChanged` runs before `AppInfo.RequestedTheme` changed and
   `OnCoverStatusChanged` before `SailfishCover.IsActive` changed. `SailfishOpenUrl.Configure` runs in
   `ApplyAppMetaToShell` after `RaiseLaunched` (`Boot.cs:28` vs `:48`), so `SailfishOpenUrl.Enabled` is false inside
   `OnLaunched` (a `WebAuthenticator` call there throws). `RaiseQuitting` runs straight from `QmlEvent`, outside
   `QtHostServices.Dispatch`'s try/catch. The theme has no initial value read (orientation does, `:145-150`).
   Fix: one subscription per event in the services, the application subscribes to the service's typed event (as
   `SailfishSystemService` does); `Configure` before `RaiseLaunched`. Test: `LifecycleTests` with a theme event,
   assert the override sees the new `RequestedTheme`.
10. **Leaks on the services.** `SailfishBottomSheet.Close` unsubscribes `PanelOpenChanged` only if the renderer still
    exists (`SailfishBottomSheet.cs:76-80`); `SailfishDeviceDisplay.Start()` adds a `SailfishDisplay.Changed`
    handler on every call (`SailfishEssentials.cs:351`); `RouteHostEvents` adds `QtHostRuntime.QmlEvent`/`KeyInput`
    handlers never removed (`Boot.cs:166,185`: a second `Run` would dispatch twice).
11. **Handlers.** `ScrollViewHandler` answers the legacy `IScrollViewController.ScrollToRequested` event
    (`ScrollViewHandler.cs:51-69`) instead of the `IScrollView.RequestScrollTo` command, so an app's
    `CommandMapper.AppendToMapping(nameof(IScrollView.RequestScrollTo), …)` never fires; verify against MAUI 11's
    `ScrollView` (it calls `Handler.Invoke(RequestScrollTo)`) and map the command, finishing with `ScrollFinished`.
    `SnapshotMapper` maps `FlowDirection` to `MapSnapshotAndViewState` (`SailfishControlHandlers.cs:44,50-54`), which
    skips the `MapGeometry` the generic mapper runs for that key (`SailfishViewHandler.cs:219-223`): Label, Entry,
    Editor and SearchBar list `FlowDirection`, so their RTL flip may skip the geometry pass (check with an RTL page in
    the harness; mirror state is pushed, positions may not move). TabbedPage/FlyoutPage request a poll twice per
    change (`SailfishPageContainerHandlers.cs:88,136` and `SailfishPageHandler.cs:30-31`). Page containers subscribe
    only in `ConnectHandler`, so a re-`SetVirtualView` to another page leaves the old page subscribed
    (`SailfishPageContainerHandlers.cs:84-96,132-144,196-214`); use `ConnectedView` and (un)subscribe in
    `SetVirtualView`.

Acceptance for W1: each item has a host test that fails without the fix; `dotnet test` green; full matrix green;
`treeFixups`, `timerWithWork`, `NavResyncs`, `BridgeFailed` 0 in the device logs.

### W2. Test harness and process-wide state (do with W1)

1. **Delayed work never runs in the harness.** `RendererHarness.Poll` drains the dispatcher queue (`DrainQueue`)
   but the delayed dispatches are `SailfishDispatcherTimer`s that only `SailfishRuntime.TickDueTimers(now)` fires
   (`SailfishDispatcherProvider.cs:61-78`, `SailfishRuntime.cs:32`, called from the real loop at
   `QtHostRuntime.cs:217`). `QtHostCollectionBridge.SchedulePending(delay)` therefore sets `_pendingAtMs` and the
   heartbeat `Poll()` skips list work (`QtHostPageRenderer.cs:573`: `kicked || !PendingScheduled`), while
   `KickedPoll()` runs it. That is why `Replacing_the_header_materializes_the_new_view` had to use `KickedPoll`.
   Fix: `RendererHarness.Poll()` advances a test clock and calls `SailfishRuntime.TickDueTimers`, or `FakeShim` owns
   a fake timer queue; then list tests use `Poll()` again. Test: the header test back on `Poll()`.
2. **TestStatics restores subscribers but not the "subscribed" flags.** It puts back `QtHostServices.Subscribers`
   but the service instances shared through `SailfishEssentialsRegistry.Defaults` (`:34`) keep their `_started`/
   `_subscribed` flags (`SailfishDevices.cs:18,177`, `SailfishSensors.cs:58-62,239-250`,
   `SailfishShareAndPickers.cs:107`, `SailfishAppServices.cs:211-224`, `SailfishCover.cs:20`), so after one test they
   never resubscribe. Also not restored: `SailfishEssentials._installed` (`:24`) and `HostReady` (`:134`),
   `SailfishTheme._current`, `SailfishDeviceDisplay._instance`, all of `SailfishDisplay`, `SailfishCover`,
   `SailfishOpenUrl` (reset by hand in `MoreEssentialsTests.cs:129-162`), `SailfishWebAuthenticator.OpenBrowser`,
   `SailfishRemorse.Pending/_nextToken`, `SailfishMauiApplication.NativeEventCounts`, `SailfishRuntime._timers/
   WakeHook`, `SailfishMainThread` hook, `IPlatformApplication.Current`, `QtHostServices.ServiceEvents`.
   Fix in two steps: (a) W6.1 removes most flags (one `Ensure(name, qml, subscriptions)` that is idempotent in
   `QtHostServices`, which TestStatics already resets); (b) the rest get a `ResetForTests` captured by
   `TestStatics`. Test: `FakeShimContractTests` style, "a renderer test leaves no subscriber behind".
3. **The harness duplicates production code.** `RendererHarness` attaches the root handler with
   `ResolveViewHandlerType` + `Activator` (`RendererTests.cs:36-42`) instead of `SailfishHandlersFactory.AttachRootHandler`
   (the B5 fix), so an app registration for the root page type is not exercised the way the app does it.
4. **Test-only members in the production assembly.** `SailfishViewKeys.Handled/RendererOwned/NotApplicable`
   (`SailfishViewKeys.cs:10-75`) and the whole `Covers` chain (`SailfishViewHandler.cs:152,163,268`,
   `SailfishControlHandlers.cs:111,138,252,288,321`) exist for `HandlerParityTests` only;
   `SailfishHandlersFactory.ResolveViewHandlerType` (`:229`) and `CaptureLibraryReplacementsForTests` (`:122`) too.
   Move the key tables to the test project (the parity test can read the handlers' `Keys` through reflection or an
   internal accessor) and keep only `TransientInput` in `SailfishViewKeys`.

Acceptance: `dotnet test` green serially and with `xunit.parallelizeTestCollections=true` (D1's check), no
`KickedPoll` needed for list tests, `grep -rn "ForTests" src/` lists only `TestStatics` capture hooks.

### W3. Renderer: finish C4 and remove the duplication the split exposed

The renderer is 4 449 lines across ten partials plus `NativeStackCoordinator`, `RenderScheduler`, `HostTreeDiff`.
The second review found the same logic written several times; unify before extracting anything else.

1. **One host-release path.** Ten places destroy hosts with different bookkeeping: `DiffHostTree`
   (`QtHostPageRenderer.cs:1042-1049`: diff.Destroy, ReleaseHost, `_current.Remove`, ReleaseSyntheticSlot),
   `ReconcileSubtrees` (`HandlerTree.cs:145-151`: no synthetic-slot release), `DestroyHosts` (`Hosts.cs:345-356`:
   does not clear `AppliedParentId`, so `OnHandlerDisconnected` does it by hand at `HandlerTree.cs:257-258`),
   `DestroyPooledHosts` (`Hosts.cs:323-330`), `TearDownHosts` (`Navigation.cs:578-594`) and `PageCache.Drop`
   (`:239-243`) with the same "pageAlive ? DestroyHosts : ReleaseHost loop" fork, `RetainOutgoingPage` (`PageCache.cs:59`),
   the refused-rekey destroy (`RowPool.cs:158`), the bridge wrapper (`QtHostCollectionBridge.cs:822-835`).
   Target: `ReleaseHosts(IReadOnlyList<NativeElementHost> hosts, string? pageId, HostRelease mode)` with
   `mode ∈ {DestroyNative, ForgetDead, ToPool}` that always clears `AppliedParentId`, unroutes, releases synthetic
   slots and removes from `_current`; `HostTreeDiff.Destroy` calls it. Native-state reset is copied five times
   (`Hosts.cs:207-209,263-264,295-297,309-314,372-374`): `NativeElementHost.ResetNative()`.
2. **One page-target resolution.** `TopModelPageJs` (`QtHostPageRenderer.cs:40`), `NativeTopPageId` (`:43`),
   `Bridge.MirrorTop` (`QtHostCollectionBridge.cs:838-839`, a copy), `ListPageId` (`Delegates.cs:358`), `PageTarget`
   (`RowPool.cs:228`), an inline copy (`Delegates.cs:274`), `Bridge.DestroyHosts` (`:827`); `QmlPage.Model` used
   directly at `Interactions.cs:270,392` and `Layout.cs:595` (can address the dying page, see `:37-39`);
   `PushScrollState` still evals `TopModelPageJs.setMauiScroll` (`Layout.cs:607`) while `PushRefreshState` uses
   `CallPage`. Target: `string? QtHostListAdapter.PageTarget` (one property), every page call through
   `CallPage(pageId, …)`, `QmlPage` reduced to the eval fallback.
3. **`ApplyTreeChange` bypasses `ApplyOps`** (`QtHostPageRenderer.cs:1112-1114` calls `CallPage("applyMauiOps")`
   and counts itself), so `LastOpsExpression`/`LastApplyOpsSplit` miss the main batch and its unknown count is not
   read. Route it through `ApplyOps` and act on a non-zero unknown count (log once per page).
4. **Object lookup order differs**: `MaterializeRow` scoped-then-global (`Delegates.cs:172-174`), `MaterializeSlot`
   global-then-scoped (`Slots.cs:67-69`), `AttachNative` scoped, global, global-visual (`Hosts.cs:200-206`),
   `Bridge.TryGetRowPoint` global first (`:174-177`). One `FindHost(id, scopeHandle)` with the documented order
   (scoped first: a released delegate can keep the global name).
5. **Small duplicates worth one pass:** the "visual-state merge + image clip" step three times
   (`QtHostPageRenderer.cs:808-819`, `HandlerTree.cs:136-139`, `Walk.cs:34-43`); `SyncDesired(... _parkedHosts)` at
   `:1005` and `HandlerTree.cs:260`; the root matrix built three times (`Layout.cs:237-243,282-291`,
   `Walk.cs:56-60`); the local `Ms()` helper four times plus `TlMs`; ancestry checks five times (`PageCache.cs:249`,
   `HandlerTree.cs:231`, `Layout.cs:53`, `Delegates.cs:303`, `QtHostCollectionBridge.cs:133`); the `synth-` prefix
   rule three times; `_tlStart = 0; TimelineStart(...)` three times where `RestartTimeline` exists;
   `SwitchRenderedPage` vs `ResetModelPageScopedState` (`QtHostPageRenderer.cs:860-870`, `Navigation.cs:361-370`)
   reset overlapping but different sets (`_renderedTabs`/`_fullResetsDone` only in the first,
   `AppliedGeometrySet` only in the second); `CreationDeferred` (`:201-202`) restates `CreationAllowed` (`:886-912`).
6. **Counters nothing reads** (remove, or wire into a leg): renderer `ReconcilesDeferredForNativeTop`,
   `ModelPageSwitches`, `DeferredModelPageSyncs`, `NavOperationInFlight` (and the coordinator's copy),
   `DrawerOpenChanges`, `WebViewNavigations`, `NavigationsSettled`, `PageCacheParks`, `SubtreeReconcileTotalMs`,
   `AdapterRebinds`; bridge `RowTapsFired`, `ThresholdReachedFires`, `PendingRuns`; adapter `RowsRemeasured`,
   `ScrollRequests`. Read only by tests: `NavOpsCompleted/Failed`, `NavResyncs`, `GeometryPasses`, `RowsPooled`,
   `RowsAdopted`, `RowRekeysRefused` (keep, they are the regression alarm; consider printing them in the
   `collection` and `nav` legs).
7. **C4, the rest.** Measured coupling: `Navigation.cs` touches 75 renderer members, `PageCache.cs` 21. Two
   concerns are separable now: (a) activation/lifecycle (`_lastWindowActive`, `_lastAppState`, `_activeSinceMs`,
   `_appLifecycleStarted`, `RaiseWindowLifecycle :419`, `ActivationSettled`, `DeferralExpired`) into
   `ActivationGate`; (b) the page cache behind a small port (`_current` add/remove, `_byId` remove, release, the
   mirror, `PageHeld`, `_collection`), keeping `RetainOutgoingPage`'s synthetic part (`PageCache.cs:52-64`) in the
   renderer. The push/pop executors stay until W3.1 gives them one release path.

Acceptance: `grep -c "ReleaseHost(\|DestroyHosts(\|DestroyNative(" Platform/QtHost/*.cs` ≤ 4 call sites;
`NavigationCoordinatorTests`, `RendererTests`, `ArchitectureAlignmentTests`, `SampleAppRegressionTests` green;
`tools/sf matrix page nav navback shell tabpulley containers collection collection100 reconcile tree stress`.

### W4. Collections: bridge ↔ adapter intent methods (C10 rest, C9 rest)

The adapter is split into responsibility partials and the lookups are pure (`RowLookup`), but the bridge still
pokes the adapter's fields and the adapter the bridge's. Replace field access with these methods (each replaces
the listed sites), then make the fields private:

| Method | Replaces |
| --- | --- |
| `adapter.OnNativeObjectRecreated()` | `QtHostCollectionBridge.cs:354-363` (`LastRowsJson`, `LastSelJson`, slots cleared, `SlotsDirty`, `RowsDirty`, `LastWidthDp=-1`, `ReadLayout`) |
| `adapter.RequestResync(int ticks)` | `:326-327`, `:591-592`, `QtHostListAdapter.Rows.cs:125-126`, `Scroll.cs:46-48` |
| `bool adapter.RunPendingWork(Stopwatch clock)` | the per-list body of `ProcessPendingOnce` `:488-519` (the bridge keeps the loop and the rescheduling) |
| `bool adapter.ForgetDeadHost(host)`, `bool adapter.OwnsCellHost(host)` | `:583-603`, `:616-620` |
| `IEnumerable<NativeElementHost> adapter.CellHosts` | `:372-377` |
| `adapter.RefreshGeometry()`, `adapter.HandleEvent(name, root)` | `:855-858`, the switch `:746-773` |
| `adapter.DescribeRow`, `TryGetRowPoint`, `DelegateHolding(element)`, `RowView(i, cell)` | `:119-187` (also stops the bridge writing `dg.Handle` at `:170-178`) |
| `bridge.ParkAttach(adapter, row, dg)`, `bridge.CancelAttach(adapter, dg)` | `QtHostListAdapter.Delegates.cs:17-37`; then `AttachRetries` and its lock go private |
| a `ListCounters` object handed to the adapter | the `internal set` counters incremented from the adapter (`Delegates.cs:212,258`, `Rows.cs:102`, `RowPool.cs:95,160,174`, `Scroll.cs:42,90`, `Input.cs:131,140`) |
| `using var _ = bridge.MappingScope()` | the `SlotMapping` bool (see W1.5) |
| `renderer.HoldLayoutRequests()` scope | `_renderer.LayoutRequestHold++/--` at `Rows.cs:70,77` |
| `adapter.PageTarget` | W3.2 |

C9 leftovers (naming only, do with the above): `FindDelegate`/`DelegateName`/`SlotName` helpers and constants
for the row JSON keys `k/r/h/t/n/s` (`QtHostListAdapter.cs` `PushRows`, `ListView.qml:224-227`); `TakeReusableRow`
duplicate-item scan; one `ReadLayout` per rebuild; `mauiPosition` pushed only on change needs a device carousel
check first (`containers` leg with the Kitchen carousel); `Loop` into `ViewProperties` with an adapter swap and the
PathView carousel's `list-scroll`/`list-item-tapped` events (`CarouselView.qml:84-96`) so `Scrolled` and
`RemainingItemsThresholdReached` work with `Loop=true`.

Tests to add (`Renderer/CollectionBridgeTests.cs`): header/footer/empty slot creation and remeasure, INCC keeps
keys, `RefreshView` consumed by its list, `list-item-tapped` under `SelectionMode.Multiple`, attach retry,
`Unregister` after `DisconnectHandler`. Acceptance: `grep -n "state\.\(RowsDirty\|SlotsDirty\|ResyncPending\|LastWidthDp\|LastRowsJson\)" QtHostCollectionBridge.cs` empty;
`grep -n "_bridge\.AttachRetries\|_bridge\.SlotMapping" QtHostListAdapter*.cs` empty; `tools/sf matrix collection
collection10 collection100 collection500 containers stress`, `RowsPooled`/`RowsAdopted` unchanged on the Kitchen
tour.

### W5. Handlers: one idiom per job, the file split by family

`SailfishControlHandlers.cs` (1 106 lines) holds 29 types of repeated boilerplate; no class is large. Do the
mechanical part first, then the idioms.

1. **Split by family** (mirrors `Snapshots/AdapterSnapshots.*.cs`): `SailfishSnapshotHandler.cs` (both bases,
   `:14-164`), `SailfishTextHandlers.cs` (Label, Button, a new `SailfishTextInputHandlerBase<T>` for Entry/Editor/
   SearchBar: the `Covers`/`FocusNatively` pair is copied three times at `:252/254, 288/290, 321/323`),
   `SailfishValueHandlers.cs` (Switch, CheckBox, Slider, ProgressBar, ActivityIndicator, Stepper, RadioButton,
   IndicatorView), `SailfishPickerHandlers.cs`, `SailfishDrawingHandlers.cs` (Image, Border, Shape, Graphics; Shape
   and Graphics are clones `:822-840` vs `:853-873`), `SailfishCompositeHandlers.cs` (WebView, SwipeView, ListView),
   `SailfishLayoutHandlers.cs` (LayoutHandlerBase, Layout, Grid, Stack, ContentView, Container, and
   `ScrollViewHandler` renamed `SailfishScrollViewHandler`: it has no prefix and collides with
   `Microsoft.Maui.Handlers.ScrollViewHandler`, which the file imports). `SailfishHandlerCore` and
   `ISailfishNativeFocus` out of `NullElementHandler.cs` (`:64-137`) into their own file.
2. **One idiom each:**
   - reading the host: a `protected NativeElementHost? Host` on `SailfishViewHandler` replaces the six
     `((IElementHandler)this).PlatformView is NativeElementHost` casts (`SailfishViewHandler.cs:109,132,140,147`,
     `SailfishControlHandlers.cs:99`, `ScrollViewHandler.cs:47`);
   - reading the view: `ConnectedView` everywhere (typed `VirtualView` throws after disconnect: `:538,756,893,997`;
     IndicatorView mixes both `:717-725`; page containers cast `IElementHandler` `SailfishPageContainerHandlers.cs:82,130,194`);
   - asking for work: `SessionOf(this)?.RequestLayout()` only (the `Renderer` shortcut `SailfishViewHandler.cs:66`
     and the `SessionOf(h)?.Renderer` route `:235-240` go);
   - keys: `ContainerKeys` shared by Grid/Stack/Layout (`:904-905,928-929,1092-1093`); drop the redundant
     interface/class `nameof` pairs (`:171,554,679`); read the owned set from the mapper instead of passing `Keys`
     twice (`:184/188`); `Grid.RowDefinitions` is a key but `GridProps` sends only the column count
     (`Containers.cs:149`);
   - measure: every handler passes its measure delegate (IndicatorView overrides `GetDesiredSize` `:716-719`,
     Shape inlines a lambda `:829-830`);
   - diagnostics: `QtHostDiag.Warn`, not `Console.Error` (`:582`); `FailedSources` (`:773`) is an unbounded static
     set without a lock, `_warnedSandbox` (`:574-583`) a static set in a constructor.
3. **QML names out of handlers.** `"mauiFocus"/"mauiCursor"/"mauiSelLen"` (`:127,134`), the WebView and SwipeView
   command payload keys (`:593,606,671`), image event names and fields (`:765-769`), Entry's echo mode `2` (`:260`),
   adapter URIs as literals next to `QtHostShapes.AdapterUri`/`QtHostGraphics.AdapterUri`/`QtHostAdapters.ScrollView`.
   This is the `SailfishKeys` idea from plan step A3 that was not done: one static class of `maui*` names and
   command names, used by handlers, snapshots and `AdapterKeyContractTests` (which can then check that every
   constant is declared by an adapter).
4. **Snapshot encodings differ for the same concept** (the QML contract, device-visible, so each change needs a
   screenshot): horizontal alignment is a Qt int for Label (`Common.cs:40`), "left/right/center" for text inputs
   (`TextInput.cs:84-91`), "start/center/end" for Picker (`Values.cs:73-79`); vertical alignment int vs string;
   text colour `mauiColor` (Label, inputs) vs `mauiTextColor` (Button, pickers); clip `clip` (ContainerProps) vs
   `mauiClip` (Border/Frame); "unset" markers are explicit `…Set` flags for Button, Transparent/-1/0/"" elsewhere;
   Label and TextStyleProps build font keys inline instead of `AddFont`. Pick one encoding per concept, change the
   adapters with it, run `controls text visual` and compare screenshots.
5. **What a MAUI developer expects:** `(IPropertyMapper?, CommandMapper?)` constructors on the concrete handlers
   (only parameterless exist, `:188`), so a subclass can supply mappers as with `LabelHandler(mapper,
   commandMapper)`; `SailfishPageHandler.Mapper` chains from `ViewHandler.ViewMapper`, not `SailfishViewMapper.Mapper`
   (`SailfishPageHandler.cs:16`), and the Tabbed/Flyout/Shell handlers declare no mappers at all; `NullViewHandler`'s
   mappers chain from nothing. Per-property `MapXxx` methods are a design choice (today every owned key re-pushes the
   whole snapshot, `:39-46`, so an app's `AppendToMapping` value on `PlatformView` is reset by the next push of any
   key): decide with the owner whether to add per-property maps for the text family at least, or document the
   snapshot semantics in `docs/custom-controls.md`.
6. **Misplaced responsibilities:** `SailfishMeasure` holds the paint font rules (`AppFontSize`, `LabelFontSize`,
   `PaintFontSizeDp`, `:24-36`) that snapshots depend on → a `SailfishFontRules` class; `SailfishMeasure.Collection`
   reads `view.Handler is SailfishListViewHandler { Adapter }` (`:372`); `TextInputProps(input, echoMode, maxLength)`
   takes values the Entry handler computes (`:260-261`) while Editor computes inside the builder; the list handler
   is a shell whose `Adapter` the bridge assigns and clears (`QtHostCollectionBridge.cs:309-310`,
   `QtHostListAdapter.cs:457-458`; ownership inverted relative to a MAUI handler: the handler should create and own
   the adapter in `ConnectHandler`/`DisconnectHandler`); `SailfishPageContainers.Of` attaches a handler inside a
   lookup (`SailfishPageContainerHandlers.cs:44-45`); `SailfishDrawnViewHandler` re-implements the
   `SnapshotDependsOnSize` push with `RequestPoll` (`:29-38`).
7. **Dead or stale:** the unused `NullViewHandler(PropertyMapper<IView, NullViewHandler>)` constructor
   (`NullElementHandler.cs:33`); `case IStepper` in `SailfishMeasure.Generic` (`:88-89`); Border/Frame measured in
   both `Generic` (`:92-101`) and `Boxed` (`:437-447`); `ValueBoxHeightDp`/`SilicaMediumFontDp` re-implement the
   `ThemeDp` cache (`:464-497`; `_silicaButtonFontDp` is misnamed); the `PushProps` doc block sits above
   `SendCommand` (`SailfishViewHandler.cs:122-129`); `NullViewHandler`'s summary still says it serves views no
   handler serves (`NullElementHandler.cs:22-23`); `SailfishMeasure.cs:22` points the 18-dp rule at
   `handler-parity.md` (it is in `porting-existing-apps.md:283-286`).

Acceptance: `HandlerParityTests`, `PropertyOwnershipTests`, `AdapterKeyContractTests`, `HandlerFixTests` green;
`grep -rn '"maui[A-Z]' Handlers/*.cs` lists only the keys class; `tools/sf matrix controls text input visual
geometry` plus a `controls-gallery` screenshot compared with `scratchpad/evidence-controls-gallery-b5.png` from the
refactor day (same layout).

### W6. Platform services: one start pattern, one thread hop, no static TCS

1. **One start pattern.** The Ensure-plus-subscribe-once dance with a flag is written seven times (Battery
   `SailfishDevices.cs:49-64`, Connectivity `:196-203`, Geolocation `SailfishSensors.cs:239-250`, `SailfishSensor`
   `:58-62`, Pickers `SailfishShareAndPickers.cs:162-178`, Contacts `SailfishAppServices.cs:211-224`, Cover
   `SailfishCover.cs:54-76`); Theme/OpenUrl/SystemService do it without a flag. Add
   `QtHostServices.Ensure(string name, string qml, params (string Event, Action<JsonElement> Handler)[] subscriptions)`
   that subscribes once per service name (idempotent through the existing `Created` set, which `TestStatics` resets)
   and delete the flags. This is the `QtQmlService` base the plan dropped, as a method instead of a class.
2. **One thread hop.** Four ways today: `QtThread.Run/RunAsync/Post`, `QtHostRuntime.RunOnQtThread` (Cover `:98`,
   Remorse `:44,62`), `QtHostRuntime.Post` (`SailfishMauiApplication.cs:145,232`), direct `QtHostRuntime.Eval`
   (`SailfishOpenUrl.CallSelf :111`). Keep `QtThread` only; `RunOnQtThread` becomes internal to QtHost. Battery,
   Connectivity, Flashlight and BottomSheet wrap `QtHostServices` calls in `QtThread.Run` although `QtHostServices`
   hops itself (harmless; remove for clarity).
3. **Static state to instance:** `SailfishPickers._pending/_subscribed` (W1.8), `SailfishRemorse.Pending/_nextToken`
   (`:18-20`), `SailfishCover`'s content/actions/flags (`:15-26`), `SailfishOpenUrl`'s configuration and events
   (`:13-21`), `SailfishWebAuthenticator.OpenBrowser` (`SailfishAppServices.cs:64`), `SailfishDeviceDisplay._instance`
   (`SailfishEssentials.cs:306`), `SailfishEssentials._installed/HostReady` (`:24,134`). The registry's `Defaults`
   (`SailfishEssentialsRegistry.cs:34`) should live on the session/overlay, not on a static, so two apps (and two
   tests) get two sets. Static counters nothing reads: `Vibrations`, `Performed`, `SailfishTheme.Changes`,
   `SailfishCover.Triggered`, `SailfishSemanticScreenReader.Announced`, `QtHostServices.ServiceEvents`,
   `SailfishSensor.Readings` (remove or print in `f4`).
4. **Duplicated helpers:** "eval `s.snapshot()`, parse, apply" (Battery `:65-73`, Connectivity `:204-209`,
   Geolocation `:263-268`); `Utf8JsonWriter`-to-string (`SailfishCover.cs:106-124`, `SailfishRemorse.cs:75-91`);
   hand-built JSON (Notifications `:391-393`, Share `:40-56`, picker filters `:181`); two JS quoting helpers
   (`QtHostServices.Js :111`, `BridgeValue.Quote`); the theme `light` parse twice (`SailfishEssentials.cs:188`,
   `ThemeChangedPayload`) and the cover status parse twice with different field names (`SailfishCover.cs:70`
   `active` vs `CoverStatusPayload` `status`: check which the shell sends); hand-rolled JSON readers where
   `BridgeJson` and the `*Payload.Parse` records exist; `MarshalUtf8` twice (`Essentials.cs:57-64`,
   `SailfishSecureStorage.cs:293-298`); the "host is up" check twice (`SailfishCover.cs:96`,
   `SailfishSecureStorage.cs:152`); the activate/deactivate JS snippet six times (`SailfishSensors.cs:70,78,282,296,317,328`).
5. **Application class:** `ApplyAppMetaToShell` (`SailfishMauiApplication.cs:208-241`) is shell configuration with
   a side effect (`SailfishOpenUrl.Configure`); `SubscribeNativeEvents` (`:130-191`) duplicates the Theme and Cover
   subscriptions (W1.9); the lifecycle pair `OnX(v); Invoke<OnX>(…)` is written out 11 times (`:109-189`), with
   de-duplication for some events and not others (CoverStatus, ColorScheme, InputMethod); `RouteHostEvents` and
   `StartRendering` in `Boot.cs:164-219` are QtHost concerns; `InstallDispatcherProvider` (`:43-47`) belongs in the
   provider; the `DynamicDependency` attributes in `SailfishEssentials.cs:51-88` attach to `InstallEarly` because the
   XML doc sits between them and `Install`. Two configuration paths build `QtHostAlertSubscription` and
   `SailfishModalNavigationPlatformFactory`: DI with a null session (`AppHostBuilderExtensions.cs:57-61`) and the
   overlay with its session (`SailfishServiceOverlay.cs:84-87`); keep the overlay's.

Acceptance: `grep -rn "_subscribed\|_started" Platform/Sailfish*.cs` empty; `grep -rn "RunOnQtThread\|QtHostRuntime.Post(" Platform/Sailfish*.cs` empty;
`EssentialsTests`, `MoreEssentialsTests`, `LifecycleTests`, `PlatformServiceThreadingTests` green; `tools/sf matrix
f4 features silica popup`.

### W7. Documentation that is out of date (fix any time, no device run)

- `docs/architecture.md:89` names `NavigationCoordinator` (now `NativeStackCoordinator`, `Step`); `:53` says
  `BottomSheet.qml` (the adapter is `qml/interactions/DockedPanel.qml` behind the `docked-panel` interaction host);
  `:5` lists current work as `parity-plan.md` and `BUG_LIST.md` only (add `architecture-plan.md` and this file).
- `docs/native-interop.md`: `:170,286` route `svc-` events at `SailfishMauiApplication.cs#L154` (now
  `Boot.cs:162-182` `RouteHostEvents`); `:167` links `MauiShell.qml#L153` (`mauiService` is at `:167`); `:241-242`
  (L2/L3) link `QtHostRuntime.cs#L294/#L298` (`Eval` is at `:335`) and L2 says there is no method call
  (`sailfish_host_invoke` exists, internal); `:288-290` describe `sailfish_host_invoke` as future (it exists with
  `(handle, method, arg, out, cap)`); `:245` links `sailfish_host.cpp#L1083` for import paths (`addImportPath` is at
  `:1162-1163`); `:246` links the targets `#L761` for `AutoReqProv` (now `:565`).
- `README.md:24`: "26 of the 27 run" is the old leg count (31 now). `docs/skiasharp-plan.md:151` uses "25/25" as a
  current gate. `docs/aot-and-trimming.md:80` says 18 legs; `:119,229,355` cite `QtHostTextMetrics.JsonString` and
  `QtHostPageRenderer.ToJsString`, which do not exist; its renderer line numbers point past the file.
- `tests/Linux.SailfishOS.Tests/Renderer/NavigationCoordinatorTests.cs` keeps the old class name (rename to
  `NativeStackSyncTests` or leave; the file tests the renderer path, `NativeStackCoordinatorTests` tests the class).
- `QtHostPageRenderer.PageCalls.cs:14-17` has two `<summary>` blocks; the first mentions `targetJs`. `QmlPage.cs:4-5`
  says every eval goes through it (it is the fallback now). A doc comment is split across files (starts
  `QtHostCollectionBridge.cs:694`, ends `QtHostListAdapter.Rows.cs:403`).
- `docs/architecture-plan.md` says "uncommitted on top of `1cfd712`" in its header and status table; it is all in
  `ab808cd` now.

### W8. Native shim (E4 split, E3 and E5 leftovers)

1. **The file split.** `sailfish_host.cpp` is two anonymous-namespace blocks (`:91-1273`, `:1846-…`, `:2897-3300`)
   with helpers between the `extern "C"` exports (`:1347-3388`); the crash handler (`:1280-1345`) is static outside
   both. Recipe that compiles at every step: (1) create `host_internal.h` declaring `HostState`, `g`, `set_error`,
   `error_text`, `log_line`, `fail_args`, `copy_out`, `require_handle`, `resolve_handle`, `register_handle`,
   `parse_color`, `json_value_to_variant`, `json_to_js_literal`, `eval_js`, `current_engine`, `attach_window`,
   `drain_qml_events` in `namespace sfhost` (not anonymous); (2) move the helper definitions into `host_core.cpp`
   keeping `sailfish_host.cpp` as the exports file and build; (3) then peel exports by family into `host_handles.cpp`
   (find/set/apply/get/geometry/parent/destroy/invoke, `:1979-2721`), `host_text.cpp` (`register_font`,
   `render_glyph`, `measure_text`), `host_surface.cpp` (`:3302-3388` plus the surface helpers in the third
   anonymous block), `host_diag.cpp` (`grab_png`, `record_*`, `diag_*`, `perf_stats`, `inject_*`), leaving
   init/load/show/exec/quit/wake/post/eval/push/pop/clipboard/open_url in `sailfish_host.cpp`. `tools/cmd/native-build.sh:54`
   compiles the list. `NativeContractTests.Every_declared_export_is_defined_once_and_imported` already checks every
   export is defined exactly once across `Native/*.cpp`; add that `native-build.sh` names every `host_*.cpp`.
2. **Still open from E4:** `removeEventFilter` on the window at teardown (`:1241` region), `measure_text`'s
   O(words²) candidate loop (`:2593-2697`) → one `QTextLayout` per line (a measurement change: compare
   `text`/`controls` legs and the `adapterbench` numbers before and after).
3. **E3 leftovers:** page calls with non-string arguments or the shell as target still eval: `mauiSetTabDrag`/
   `mauiEndTabDrag` (`Interactions.cs:173,184`, a number), `__openContextMenu` (`:269`, two args), `__pushDialog`
   (`:392`), `mauiOpenPulley` (`Layout.cs:594`), `setMauiScroll` (`:607`), remorse (`SailfishRemorse.cs:47,92`),
   `mauiPreloadAdapters`/trace flags (`QtHostPageRenderer.cs:1231-1276`), the nav-state poll (`Navigation.cs:386`),
   `window.mauiService` (`QtHostServices.cs:24,62`), the delegate name scan (`Delegates.cs:64`), the Silica theme
   probes in `SailfishMeasure.cs:276,348,476,492`. Cheapest closure: make the QML functions take one JSON argument
   (`mauiSetTabDrag("{\"shown\":…}")`) and give the shell an `objectName: "mauiShell"` so `CallPage` can target it;
   then `QmlPage` can go. `perf` leg: `evals` per push should drop below 5.
4. **E5 leftovers:** `LibraryImport`/`UnmanagedCallersOnly` has no warning to fix today (Release build of the core
   shows no IL2026/IL3050); do it only if a trimming warning appears. The per-page payloads are still
   double-encoded strings (`mauiNotify(name, JSON.stringify(...))` then the drain's `JSON.stringify` of the array);
   `__mauiDrainAll` is the place to pass objects through once.

Acceptance: `tools/sf native-build`, full matrix, `NativeContractTests` green, `tools/sf verify` D6 OK.

### W9. Tools, build and packaging

- `tools/lib/sf-lib.sh:89-116` derives the package name, binary and arch from directory names; the plan's D3 wants
  an MSBuild query (`dotnet msbuild -getProperty:SailfishPackageName …`). Every `tools/sf` command sources the
  library, so the query must be lazy and cached (a file under `obj/` keyed by the csproj mtime). `SF_RPM_ARCH`
  (`:116`) ignores `SF_RID`.
- `tools/sf run --wait` reports only whether the app exited, not its exit code (`tools/cmd/run.sh:72-74`,
  `tools/remote/sf-run-remote.sh`); a failed start (exit 2, the ABI check) reads as "exited".
- `tools/sf matrix` can start a leg while the previous instance is still shutting down and read its log
  (seen 2026-10-03 09:02); wait for the previous PID before launching.
- Targets (`buildTransitive/Microsoft.Maui.SailfishOS.targets`, 685 lines): duplicate defaults near `:80`,
  `_SignSailfishRpm` only when the builder is `rpmbuild` (warn otherwise), `scaffold/trimmer.xml` rooting
  `CreateMauiApp` on any type. The `Build.Tasks` dll is loaded with `TaskHostFactory`; a stale
  `artifacts/build-tasks/` after a `git clean` is rebuilt by the core's ProjectReference, but a package consumer
  needs the packed copy (pack-local does it).
- `samples/Linux.SailfishOS.Sample` hand-copies the native assets (`Sample.csproj:44-75`) instead of a
  `ProjectReference` plus the package's `runtimes/`; Kitchen's try/catch around `UseMauiCommunityToolkit` and its
  `IDispatcher` singleton (`MauiProgram.cs:85-116`) are device-visible changes: deploy and run the Kitchen tour.
- `docs/media` (about 23 MB of mp4/gif) to Git LFS is an owner decision.

### W10. Decisions for the owner (decided 2026-10-03)

The owner's answers, then the questions as they were put (options and recommendations kept for the reasoning).

| # | Decision | Status |
| --- | --- | --- |
| — | Dialogs look like Sailfish's system dialogs (a panel across the top, the page visible and dimmed below), not full-screen Silica dialog pages | done: `dialogs/DialogPanel.qml`, legs popup/controls/stress |
| 1 | (b) an unset Label `FontSize` paints `Theme.fontSizeMedium` | done: `SailfishMeasure.LabelFontSize` = `AppFontSize`; 10 legs PASS, screenshot `controls-gallery` |
| 2 | (b) a single-button alert's button is the acknowledgement (accepts) | done (centred, as the system's one-button dialogs) |
| 3 | (c) open a list page in two turns, only for lists taller than the screen, after measuring on the Kitchen catalog | done: `QtHostListAdapter.FirstFrame.cs` (`MAUI_SAILFISH_LIST_FIRST_FRAME=0` turns it off); catalog push stall 164–179 → 136–158 ms (`profiling.md` §6a) |
| 4 | (a) keep the tab title in the swipe strip | closed |
| 5 | (b) snapshot encodings change only when an adapter is touched anyway | standing rule |
| 6 | (b) snapshot semantics documented | done: `docs/custom-controls.md` "Customizing a built-in control" |
| 7 | `QtHostRuntime.Invoke` stays internal | closed |
| 8 | D5 diagnostics package stays deferred | closed |
| 9 | (a) SecureStorage file fallback kept and documented | done: `docs/sailfishos-packaging.md` (already described) |
| 10 | (b) Syncfusion text gap documented, no add-on | done: `docs/porting-existing-apps.md` "Not supported yet" |
| 11 | no CommunityToolkit add-on package: the work stays in `Microsoft.Maui.SailfishOS`; gap documented | done: same section |
| 12 | camera capture, Geocoding, TextToSpeech, BlazorWebView, `MauiSplashScreen` stay out | confirmed in `parity-plan.md` and the porting guide |
| 13 | (a) `dotnet workload install` tried in an isolated SDK (nothing global) | done: works after the tool's manifest install; two manifest defects fixed (`parity-plan.md`); a workload set of our own is a new decision |
| 14 | NativeAOT stays frozen | closed |
| 15 | armv7hl labelled untested | done: README, `sailfishos-packaging.md` |
| 16 | `docs/media` | open (no recommendation given) |
| 17 | (a) GitTrends waits for a newer MAUI 11/toolkit | closed until then |
| 18 | GameSpur skipped | closed |

**Look and behaviour apps see (device-visible)**

1. **Label default font size.** A `Label` without `FontSize` paints 18 dp (`SailfishMeasure.LabelFontSize`); Silica
   controls use `Theme.fontSizeMedium`. (a) keep 18 dp; (b) theme size, which enlarges every unstyled Label in every
   app. Recommendation: (a), it matches what ported apps were laid out against.
2. **Single-button alert.** `DisplayAlertAsync(title, message, "OK")` is MAUI's *cancel* button, so "OK" sits at the
   header's left (Silica's cancel side) and the dialog can still be swiped forward. (a) keep (semantics as MAUI);
   (b) show the lone button as accept on the right, as an acknowledgement. Recommendation: (b), it reads as native;
   the result (`Task` completes) is the same either way.
3. **Opening a page with a list in two turns.** Page and header first, visible rows in the next frame: ~80–120 ms
   less stall, the list empty for 1–2 frames of the enter animation. (a) no; (b) yes; (c) yes, only for lists taller
   than the screen. Recommendation: (c) after measuring it on the Kitchen catalog.
4. **Tab swipe preview.** Today the next tab's title shows in the uncovered strip. A real preview of the next tab's
   content means keeping its hosts alive (memory, two pages laid out). (a) keep the title; (b) build it.
   Recommendation: (a).
5. **Snapshot encodings (W5.4).** One encoding per concept (alignment, text colour, unset markers) changes what
   adapters receive; invisible to apps, every change needs a screenshot comparison. (a) now; (b) only when an adapter
   is touched anyway. Recommendation: (b).

**API**

6. **Per-property `MapXxx` (W5.5).** Every owned key re-pushes the whole snapshot, so an app's `AppendToMapping` value
   is reset by the next push of any key. (a) per-property maps for the text family; (b) document the snapshot
   semantics in `docs/custom-controls.md`. Recommendation: (b) now, (a) when a ported app needs it.
7. **`QtHostRuntime.Invoke`.** Public for interop (`docs/native-interop.md`) or internal with apps on `Eval`.
   Recommendation: keep internal until an app asks; `Eval` covers the interop doc.
8. **Diagnostics package (D5).** Deferred "some day"; the seam it needs is listed in "State at `ab808cd`".
   Recommendation: stay deferred.

**Platform features and add-ons**

9. **SecureStorage without Jolla's device-lock integration** falls back to the file store (obfuscation only).
   (a) keep and document (today); (b) an app passphrase (Secrets `CustomLock`) the user types; (c) chase the
   device-lock integration with Jolla. Recommendation: (a), revisit if an app stores real credentials.
10. **Syncfusion Toolkit** text controls throw (private `TextMeasurer`). (a) an add-on package that installs a Qt
    measurer by reflection (brittle across versions); (b) document the gap. Recommendation: (b).
11. **CommunityToolkit.Maui** Popup v1, Toast, Snackbar have no Sailfish implementation. (a) a
    `Microsoft.Maui.SailfishOS.CommunityToolkit` package (Toast/Snackbar as Silica notifications/banners);
    (b) document. Recommendation: (a) for Toast/Snackbar only, they are common in ported apps.
12. **Deliberately out** (confirm they stay out): in-app camera capture, Geocoding, TextToSpeech, BlazorWebView,
    `MauiSplashScreen`. Recommendation: confirm.

**Build, release, repo**

13. **`dotnet workload install`** needs the `microsoft.net.workloads.<band>` aggregate. Experimenting changes the
    machine's global SDK. (a) do it in an isolated SDK (`DOTNET_ROOT` in a scratch folder), then decide;
    (b) keep the `sailfish-workload` tool as the install path. Recommendation: (a), it touches nothing global.
14. **NativeAOT** is frozen by G2. Reopen (needs a Linux builder for `ReleaseAot`) or keep frozen.
    Recommendation: keep frozen.
15. **armv7hl** is built but never ran: needs a 32-bit device or an accepted "untested" label. Recommendation:
    label it untested in the README until a device is at hand.
16. **`docs/media` (about 23 MB of mp4/gif)** to Git LFS or release assets, or keep in the tree.

**Ported apps (test campaign)**

17. **GitTrends** fails at start on MAUI 11 rc1 (CommunityToolkit.Maui.Markup typed bindings, `MethodAccessException`).
    (a) wait for a newer MAUI 11/toolkit build; (b) rewrite the port's bindings by path. Recommendation: (a).
18. **GameSpur** needs the authors' private API configuration: skip unless it is available. Recommendation: skip.

## Metrics to report after each package

`dotnet test` count; `wc -l` of the largest files; the counters on the full matrix (`treeFixups`, `timerWithWork`,
`NavResyncs`, `BridgeFailed`, `pageCallFallbacks`, `rekey refused` lines, `RowsPooled/RowsAdopted` on the Kitchen
tour); `grep -c` acceptance lines of the package. Append a row to the device evidence log in
`docs/architecture-plan.md` with the summary file path of every run.
