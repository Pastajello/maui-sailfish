# BUG_LIST — backend MAUI.Sailfish (Qt/Silica)

Checklist of Qt/Silica host defects. Source of findings: rendering comparison with the
iOS leg (`samples/SailfishKitchen`, same XAML) and on-device diag
(`sf run --env MAUI_SAILFISH_QT_HOST_DIAG=1`). Add new bugs to the
matching group in the **Open** section; after the fix is verified on the device, remove the entry and record the
cause and proof in the commit message. Classification rule: we fix what
breaks the app's intent (properties/styles set explicitly); native Silica chrome
(back, page header, pulley instead of toolbars, button theme)
stays native and is not a defect.

## Open (as of 2026-10-02)

Collected from the old "To do" list, from `BUG_LIST_S1.md` (analysis of the 2026-09-20 recording, removed
2026-09-28) and from code review. Bigger work items (navigation, performance, AOT) are in
[`docs/parity-plan.md`](docs/parity-plan.md).

**Navigation and page loading (Kitchen)**
- [ ] **Entering a Kitchen detail** still stalls one tour step 160–167 ms (detail 12: 112–123 ms; iOS 80–150 ms).
      2026-10-02: the offline tour had measured an error page (the seed had no `lookup.php`, so "HTTP 503" plus a
      logged stack trace); the seed now answers lookups from its search records. The ingredient rows are not the
      cost (one Label per row was 193–200 ms against 182–191 ms). The detail's load now yields first
      (`MealDetailViewModel.LoadAsync`, −20 ms). What is left is the push itself: QML host creation (~40% of the
      block in an EventPipe trace) and `NavigationPage.PushAsync` (~23%), the idle adapter preload in
      [`docs/parity-plan.md`](docs/parity-plan.md) is the next step. Numbers: [`docs/profiling.md`](docs/profiling.md) §6a.

**Phone state, not code**
- Leg f4: Sailfish Secrets collection locked ("requires device lock authentication") — unlock with the code.
- A system dialog in front (e.g. "USB cable connected — switch mode") keeps the test app Inactive
  (`Qt.application.state=2`): no hosts are created and every leg reads `absent`. Dismiss it on the phone.
- A broken old Kitchen image cache sits on the phone as `images.utf8corrupt` — delete by hand.
- `unknown=2` noise on destroy (ids whose objects are already gone from `__hosts`) — census clean, keep watching.

## Fixed

The list of fixed defects (2026-09-18 – 2026-09-28, with causes and proofs) was removed
2026-09-28; it is in git history (`git show dc17c41:BUG_LIST.md`).
