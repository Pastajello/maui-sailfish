# Architecture

The durable design rules of the Sailfish OS backend and the platform facts they rest on. The
quarter-by-quarter migration plan (Q0–Q24, `PLAN.md` + `PLAN-checklist.md`) that got the backend here is
finished and was removed on 2026-09-28; it and its per-quarter device evidence remain in git history.
Current work: [`parity-plan.md`](parity-plan.md) (roadmap) and [`../BUG_LIST.md`](../BUG_LIST.md) (defects).

## Fixed constraints

- Sailfish OS is fixed, and so is its Qt stack: **Qt 5.6.x** with Sailfish Silica as the native UI toolkit.
- Wayland, the compositor (lipstick) and GPU integration belong to the OS.
- C#/.NET stays the MAUI-facing API and the logical UI layer; QML/Qt Quick/Silica is the visual layer.

```text
MAUI logical tree (Controls: bindings, layout managers, gestures, VSM, navigation)
    │  handler mappers and commands, as on every MAUI platform:
    │  UpdateValue → snapshot / view state / transient input; Invoke(InvalidateMeasure, Add/Remove, Focus, …)
    ▼
Sailfish handlers (SailfishViewHandler<TVirtual> : ViewHandler<TVirtual, NativeElementHost>)
    │  choose the adapter and its state, push changes, arrange their children (PlatformArrange)
    ▼
QtHostPageRenderer: materializes the host tree (create / destroy / order ops, back cache), runs the requested
layout passes, mirrors navigation onto the Silica pageStack, routes input; a 2 s heartbeat only verifies
    │  generic native ABI: batched property + geometry ops (JSON over the shim bridge)
    ▼
libsailfishhost.so (C++ shim, Qt 5.6) ── Silica QML adapters (qml/controls, qml/containers, …)
    ▼
Qt Quick scene graph → Wayland → lipstick → GPU → display
```

## Principles

**Handlers are the MAUI-facing layer, as on the other platforms.** Each control's handler
(`SailfishViewHandler<TVirtual>`, platform view `NativeElementHost`) decides its QML adapter and state, publishes a
static `Mapper` chained from `SailfishViewMapper.Mapper` → `ViewHandler.ViewMapper` and a `CommandMapper` chained
from `SailfishViewMapper.CommandMapper` → `ViewHandler.ViewCommandMapper` (Focus/Unfocus/InvalidateMeasure live
there, so a custom handler must chain from it too), and is resolved like any MAUI handler (an app or library `AddHandler` wins; stock MAUI handlers give way to the Sailfish table).
Controls reach native **only** through the handler: mapper keys push the family snapshot, the generic view state or
transient input; `InvalidateMeasure` requests the layout pass; layout commands and `Content` request the host-tree
sync. Library controls bring their own adapters (`QtHostAdapters.Register`, [`custom-controls.md`](custom-controls.md)).

**MAUI is logical, QML is visual.** MAUI owns controls and their state, bindings, commands, navigation
semantics, measure/arrange and lifecycle semantics. QML owns the native look, animation, focus at the Qt
boundary and Silica behaviour. Never reproduce Silica visually in C#.

**MAUI is the only layout engine.** QML reports the real window geometry and Silica insets; the MAUI
measure/arrange pass computes every rect in dp, and one batched `apply_geometry` pushes Qt scene units. The arrange
recursion is MAUI's own: a handler's `PlatformArrange` calls `CrossPlatformArrange` on its content, as a native
container's layout pass does elsewhere; a pass runs only when a handler asks for one.

**C# does not know Silica classes.** No `if (element is DockedPanel)` in C#, and no C function per Silica
control. A MAUI element maps to a generic native host that loads a QML adapter; the adapter decides how a
semantic MAUI API (`IsOpen`, `Open()`) is implemented (`BottomSheet → BottomSheet.qml → DockedPanel`).

**Generic native ABI.** The boundary is a host/object system (create, destroy, load, set/get property,
invoke, post, tick, event callback, last error). No ABI function exists merely because a Silica class exists.

**No per-frame CPU copy.** The path is QML → scene graph → GPU → Wayland. `grabWindow()` / PNG readback is
for diagnostics and screenshots only, never the renderer. The one CPU-drawn exception is a drawing surface
(`QtHostSurface`). Library code that draws its own pixels, such as SkiaSharp's raster canvas (on Android that
canvas is a CPU `Bitmap` too), commits them once per paint. The shim copies them into a staging image and uploads
them with `glTexSubImage2D` into a persistent texture. This happens only for that view's area, never per frame
without a paint, and never with a readback.

**Native hosts are persistent.** Elements are created once and updated in place, so focus, caret,
selection, scroll and animation state survive reconcile passes. The host tree mirrors MAUI's view tree (F1:
a child host lives inside its parent host; every ScrollView is its own flickable). A container's handler owns
its children's hosts: a layout command (`Add/Insert/Remove/Update/UpdateZIndex/Clear`) or a `Content` swap diffs
only that container's subtree on the next loop turn, and `DisconnectHandler` releases a host whose element left the
page. The page reconcile stays the verifier for navigation and page switches (`treeFixups` counts what it still
had to change).
Nothing is polled: every change reaches the renderer as an event (handler mappers and commands, navigation
requests, the shell's pageStack and lifecycle events, each list adapter's own scheduling), and a 2 s heartbeat only
verifies that nothing was missed (`timerWithWork`, 0 on the device matrix).

**Only visible state crosses the bridge.** Adapter events (`tap`, `text-changed`, `toggled`,
`value-changed`, `list-item-tapped`, …) write back into MAUI under change suppression, so echoes never loop.

**Collections stay native.** `CollectionView`/`ListView` ride the virtualized Silica `ListView`: QML owns
delegates, flicking and the viewport; MAUI owns item content, selection and scroll state. Qt 5.6 destroys a delegate
that scrolls out; its row's hosts outlive it and the next row of the same shape takes them over (the row pool,
`QtHostListAdapter.RowPool.cs`), as RecyclerView reuses view holders.

**Every change is proven on the device.** Build, deploy (`tools/sf deploy`), prove the device runs this
build (`tools/sf verify`), run the self-verifying diagnostic leg (`tools/sf matrix <leg>`), look at a
compositor screenshot (`tools/sf screenshot`). A host-only test does not close a device-visible change.

**MAUI navigation idioms map to native Sailfish ones.** Silica has one `pageStack` per window; container
handlers (Shell, TabbedPage, FlyoutPage, NavigationPage) say what to show and the `NavigationCoordinator` runs one
operation at a time on that stack, completing it only when the native stack shows the result.

| MAUI idiom | On Sailfish | Why |
|---|---|---|
| Flyout (Shell flyout, FlyoutPage) | **Pulley menu** of the section's root page, flyout items as `PullDownMenu` entries | the most native Sailfish idiom, no new UI elements |
| Tabs (Shell TabBar/Tab, TabbedPage) | **Tab bar under the page header + horizontal swipe** between pages | what Jolla apps do (Settings, Gallery); a bottom bar collides with system gestures |
| Page under a push, tab or section switched away from | kept as the same QML object (LRU, `MAUI_SAILFISH_PAGE_CACHE`, default 4) until the app no longer holds it | going back shows a ready page |

## Platform facts (measured on the device)

**No Sailfish SDK needed.** Qt 5.6.3 headers (with the generated `qconfig.h`), `libsailfishapp-devel` and the
Silica plugin headers come from the public release repository
(`https://releases.jolla.com/releases/<version>/jolla/aarch64/`, no authentication). `tools/sf sysroot`
assembles the sysroot from those RPMs plus runtime `.so` files, and `zig c++ -target aarch64-linux-gnu`
cross-builds the shim on macOS or Linux (`tools/sf native-build`).

**glibc, not musl.** Sailfish OS 5.2 has glibc 2.34, so the RID is `linux-arm64` (`linux-arm` for armv7hl).

**`initialPage` never appears outside the launcher flow.** `ApplicationWindow` pushes `initialPage` through
`pageStack.animatorPush`, and in a window lipstick has not activated that animation never progresses: the
window shows only its clear colour. The shell therefore sets no `initialPage` and pushes pages itself
(`MauiShell.qml`); multi-level syncs use `PageStackAction.Immediate`, single-level push/pop animate like
native Silica once the window is active.

**A second window can crash EGL.** `ApplicationWindow` creates a cover `QQuickWindow`; under `qmlscene` some
runs died silently with `invalid handle: (nil)` from hybris EGL. The shim creates its windows itself from C++
and controls when the cover exists.

**The booster needs PIE.** `invoker --type=silica-qt5` dlopens its target, so a launcher binary must be a
position-independent executable (launcher details: [`sailfishos-packaging.md`](sailfishos-packaging.md)).

**Qt 5.6 QML gaps that shaped the adapters:** no `setLineDash` on Canvas (dashes are cut in JS), assigning a
JS array to a list alias fails ("Cannot assign object to list property"), a non-`MenuItem` child breaks a
pulley menu, and there is no `MultiEffect` (shadow/clip use `OpacityMask` + `DropShadow`).

**Managed debugging works** with vsdbg (linux-arm64) and the real VS Code client:
- vsdbg rejects a custom DAP client through a licensing handshake, so only VS Code itself can drive it.
- VS Code sends SHA384/SHA512 breakpoint checksums that vsdbg does not support, and vsdbg then rejects the whole
  `setBreakpoints` request (`ErrorCode 3001`). `tools/py/sf-debug-dap-filter.py` (hooked into `tools/sf-debug-pipe.sh`)
  strips them; the VS Code extension has the same filter.
- Device side: the CLR diagnostic port (`DOTNET_EnableDiagnostics=1`, `sf run --diagnostics`), the PDB and
  `libmscordaccore.so` / `libmscordbi.so` in the untrimmed Debug package (`<pkg>-debug`), `kernel.yama.ptrace_scope=0`.
- Release attaches only without Just My Code: vsdbg treats trimmed ReadyToRun code as "not yours".
