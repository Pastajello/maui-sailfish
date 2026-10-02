# BUG_LIST — backend MAUI.Sailfish (Qt/Silica)

Checklist of Qt/Silica host defects. Source of findings: rendering comparison with the
iOS leg (`samples/SailfishKitchen`, same XAML) and on-device diag
(`sf run --env MAUI_SAILFISH_QT_HOST_DIAG=1`). Add new bugs to the
matching group in the **Open** section; after the fix is verified on the device, remove the entry and record the
cause and proof in the commit message. Classification rule: we fix what
breaks the app's intent (properties/styles set explicitly); native Silica chrome
(back, page header, pulley instead of toolbars, button theme)
stays native and is not a defect.

## Open (as of 2026-09-30)

Collected from the old "To do" list, from `BUG_LIST_S1.md` (analysis of the 2026-09-20 recording, removed
2026-09-28) and from code review. Bigger work items (navigation, performance, AOT) are in
[`docs/parity-plan.md`](docs/parity-plan.md).

**Navigation and page loading (Kitchen)**
- [ ] **Entering a Kitchen detail** still stalls one tour step ~181 ms (iOS 80–150 ms). The host side is done
      (2026-09-30): hosts are created in chunks (first 96, then 24 per pass; `MAUI_SAILFISH_CREATE_FIRST_CHUNK` /
      `MAUI_SAILFISH_CREATE_CHUNK`), shapes the layout waits for first, which took the longest poll from 144 to
      97 ms. About 70–80 ms of the rest is MAUI's `BindableLayout` building the ingredient rows (managed); next step
      is a lighter ingredient row.

**Controls**
- [ ] **Kitchen: card thumbnails interrupted by `Shutdown` stay placeholders after returning.** Fix in place
      (2026-09-30: `CatalogViewModel.OnActivated` resumes cards without a thumbnail); not yet reproduced on the
      phone, where a warm image cache finishes before the push.
- [ ] **An `Image` without a size request measures 100×100 dp whatever its source** (`SailfishMeasure.Image`). On
      Android it takes the image's own size: a resizetizer `MauiImage` at its `BaseSize`, a bitmap (SkiaSharp's
      `SKBitmapImageSource`, a stream) at its pixels ÷ density. The Sailfish head packs resizetizer images as one 4×
      raster (`_SailfishProcessMauiImages`), so the intrinsic size needs the source's kind, not just the PNG size.
      Found 2026-10-02 while adding SkiaSharp's image sources.

**Phone state, not code**
- Leg f4: Sailfish Secrets collection locked ("requires device lock authentication") — unlock with the code.
- A system dialog in front (e.g. "USB cable connected — switch mode") keeps the test app Inactive
  (`Qt.application.state=2`): no hosts are created and every leg reads `absent`. Dismiss it on the phone.
- A broken old Kitchen image cache sits on the phone as `images.utf8corrupt` — delete by hand.
- `unknown=2` noise on destroy (ids whose objects are already gone from `__hosts`) — census clean, keep watching.

## Fixed

The list of fixed defects (2026-09-18 – 2026-09-28, with causes and proofs) was removed
2026-09-28; it is in git history (`git show dc17c41:BUG_LIST.md`).
