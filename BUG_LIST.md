# BUG_LIST — backend MAUI.Sailfish (Qt/Silica)

Checklist of Qt/Silica host defects. Source of findings: rendering comparison with the
iOS leg (`samples/SailfishKitchen`, same XAML) and on-device diag
(`sf-run.sh --env MAUI_SAILFISH_QT_HOST_DIAG=1`). Add new bugs to the
matching group in the **Open** section; after the fix is verified on the device, remove the entry and record the
cause and proof in the commit message. Classification rule: we fix what
breaks the app's intent (properties/styles set explicitly); native Silica chrome
(back, page header, pulley instead of toolbars, button theme)
stays native and is not a defect.

## Open (as of 2026-09-28)

Collected from the old "To do" list, from `BUG_LIST_S1.md` (analysis of the 2026-09-20 recording, removed
2026-09-28) and from code review. Bigger work items (navigation, performance, AOT) are in
[`docs/parity-plan.md`](docs/parity-plan.md).

**Platform and lifecycle**
- [ ] **No `OpenUrl` / `OnNewIntent`.** `.desktop` has `Exec` without `%U` and without `MimeType`, and the running
      instance has no D-Bus service, so a second launch with a URL/file never reaches the app.
- [ ] **`Launcher.OpenAsync(OpenFileRequest)` throws `NotSupportedException`** (`Essentials.cs:366`) —
      opening local files in an external app is not wired up.
- [ ] **No MCE events:** display off/on, device lock, memory pressure (system D-Bus).
- [ ] **To confirm manually on the phone:** `OnInputMethodChanged`, `OnCoverStatusChanged`,
      `OnColorSchemeChanged` (the `NativeEv` app logs `NATIVE …`); `OnOrientationChanged` exercised during
      the landscape work (769f2db).

**Navigation and page loading (Kitchen)**
- [ ] **To verify with a 30 fps recording:** S1-2 (title of the popped page after pop), S1-3 (torn slide
      geometry), S1-5 (push blanks the outgoing page). Fixes landed (7c39e4e, 22a5181, 5352ee4), no proof from
      a recording yet.
- [ ] **A page two levels down has no hosts** until you return to it: the back cache kept one level, so
      the second pop was a full rebuild (847 ms). Likely fixed by the per-page cache (2026-09-29, LRU of 4);
      re-measure on the phone before closing.
- [ ] **Entering a Kitchen detail:** ~117 QML objects at once, blocking peak 170–260 ms (iOS 80–150 ms).
      Direction: create in batches, lighter ingredient row, create during the enter animation.
- [ ] **Thumbnails from scratch on push despite a warm cache** (S2-3): a new `Image` adapter = a new
      decode/assign; pops no longer suffer (retention).
- [ ] **Empty state "No recipes here…" while the catalog loads** (S2-2), ~0.6–0.9 s next to the progress bar.
      The VM gate (`ShowEmpty => !HasContent && !IsBusy`) is correct, so host timing is suspect;
      compare the first second with the iOS leg.
- [ ] **Cold start** (S3-1): ~1.6 s of an unstyled window with a small splash rectangle, then a page with the
      default title "MAUI" (`MauiModelPage.qml:16`) before reconcile pushes the real title.
- [ ] **Collection bridge pushes properties one by one** (`QtHostCollectionBridge.Push`, no diff or batch).
      Left unchanged, because a `mauiApplying` envelope would suppress events after `ScrollTo`.

**Controls**
- [ ] **Horizontal `GridItemsLayout` renders vertically** (warning in `QtHostCollectionBridge.cs:428`).
- [ ] **`CollectionView` throws at startup:** one `ArgumentException: Unable to find IAnimationManager for
      'Microsoft.Maui.Controls.CollectionView'` per run (Kitchen trace). First-chance exceptions are expensive; find who asks.
- [ ] **QML duplication:** `PullDownMenu`/`PushUpMenu`, `DockedPanel`/`Drawer`, containers
      `Grid`/`StackLayout`/`ContentView`.
- [ ] **Kitchen:** the grid/list toggle (`IsGridLayout`) changes nothing (`CatalogPage.xaml.cs` reads
      only `GridSpan`); card thumbnails interrupted by `Shutdown` stay placeholders after returning.

**Tools and packaging**
- [ ] **`sf-record.sh` starts ~15 s after the app** and misses the start of the run (workaround: `KITCHEN_TOUR_DELAY_MS`).
- [ ] **Workload manifest without a clone:** `sf-pack-local.sh` does not pack it into the feed, and the package does not contain
      `sf-workload-install.sh`.
- [ ] **Noisy rpmbuild log** during `dotnet publish`.
- [ ] **`sf-run.sh` / `sf-deploy.sh` sometimes hang on ssh after the app exits** (2026-09-28, 3× in one
      session): the app ends cleanly (`event loop exited rc=0`), but the local `expect`+`ssh` keeps waiting;
      the matrix stalls on the leg until the ssh process is killed by hand. No session timeout in `sf-run-remote.sh`.
- [ ] **`sf-shots.sh` saves the screenshot one marker too late** (state `f3-o-library` in file `f3-n2-rtl.png`).
- [ ] **Kitchen, slow tour (`KITCHEN_TOUR_DELAY_MS=4000`): Silica errors `PulleyMenuBase.qml … 'dragging'/'contentY' of
      null`** when returning from the recipe detail (2026-09-29, build with A5): the pulley menu of the popped page loses its
      flickable before itself. In the fast tour and in tours from before A5 — 0 occurrences; not checked whether this is new (the
      animated pop path did not change). To compare on an old build; destroying the pulley host before the pop
      animation may be enough.
- [ ] **The heartbeat once found work in Kitchen** (same slow tour): `geometry +1` on the detail page after the data arrived
      — a layout change without `RequestLayout`. To find with the `timer-poll work` trace (the trace is disabled in Kitchen).
- [ ] **`DispatchDelayed` sometimes does not fire** (2026-09-29, nav leg): a 60 ms timer scheduled in the tick right after
      an immediate pop (modal close) did not fire for ~2 s, until the heartbeat; across the whole leg 27/29 scheduled
      `KickIn` fired (some are the tail at close). Same thread and dispatcher (Qt thread). The renderer no longer
      depends on it (operation confirmation goes through the regular `Dispatch` queue); to investigate in
      `SailfishDispatcherTimer`/`TickDueTimers`/`sailfish_host_wake`. Trace `kick scheduled`/`kick fired`.

**Phone state, not code**
- Leg f4: Sailfish Secrets collection locked ("requires device lock authentication") — unlock with the code.
- A broken old Kitchen image cache sits on the phone as `images.utf8corrupt` — delete by hand.
- `unknown=2` noise on destroy (ids whose objects are already gone from `__hosts`) — census clean, keep watching.

## Fixed

The list of fixed defects (2026-09-18 – 2026-09-28, with causes and proofs) was removed
2026-09-28; it is in git history (`git show dc17c41:BUG_LIST.md`).
