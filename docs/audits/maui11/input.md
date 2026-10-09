<!-- Static audit of commit af3d782 (branch feature/fixes) against .NET MAUI 11.0.0-rc.1.26451.6, 2026-10-04. Produced by a read-only code review with ilspycmd decompiles; nothing was run on the device. Items marked unverified were not checked on the phone. Summary and work packages: docs/maui11-alignment-plan.md -->

# Input audit — maui-sailfish (feature/fixes) vs .NET MAUI 11.0.0-rc.1.26451.6

Static analysis only (no build, no device). Evidence paths are relative to `/Users/mer/Projects/maui-sailfish`;
MAUI facts come from `ilspycmd` decompilation of `Microsoft.Maui.Controls.dll` / `Microsoft.Maui.dll` (copies in
`scratchpad/decomp/*.cs`). Central files: `src/Linux.SailfishOS/Platform/QtHost/QtHostInput.cs` (router),
`src/Linux.SailfishOS/Platform/QtHost/AdapterEventRouter.cs` (adapter events → MAUI),
`src/Linux.SailfishOS/Platform/QtHost/QtHostPageRenderer.Layout.cs` (hit-test + geometry),
`src/Linux.SailfishOS/Handlers/SailfishTextHandlers.cs` + `Handlers/Snapshots/AdapterSnapshots.TextInput.cs` (text input),
`src/Linux.SailfishOS/Native/host_core.cpp` (event filter), `src/Linux.SailfishOS/Platform/SailfishMauiApplication.Boot.cs` (Back key).

## Summary

- All seven MAUI 11 gesture recognizer families except Drag/Drop reach MAUI: Tap (incl. NumberOfTapsRequired with a
  300 ms wait for the longer count), Pan, Swipe, Pinch (second finger, kind 7), Pointer, and **LongPressGestureRecognizer
  is already implemented** (`QtHostInput.cs:81,122-123,800-830`): `MinimumPressDuration` honoured (min over the owner's
  recognizers), `Command` + `LongPressed` + `State` via `SendLongPressed`/`SendLongPressing(Started…Completed)`.
  Deviations: `AllowableMovement` is ignored (fixed 10 dp `TapSlopDp`, coincidentally MAUI's default), no
  `GestureStatus.Canceled` is ever sent (State stays `Started`/stale when the finger moves away), positions are root-space
  dp regardless of `GetPosition(relativeTo)`.
- **Drag & Drop is entirely missing** (no `DragGestureRecognizer`/`DropGestureRecognizer` reference anywhere in `src/`);
  `SendDragStarting`/`SendDragOver`/`SendDrop`/`SendDropCompleted` are public in MAUI 11, so a router-driven implementation
  is possible (design below).
- Three MAUI-visible router bugs: (1) `SwipeGestureRecognizer.Threshold` is ignored and the direction test is `!=`
  equality, so a recognizer with combined flags (`Left|Right`) never fires (`QtHostInput.cs:596-603`); (2) an
  `InputTransparent` hit stops the sequence instead of falling through to the view below, and `CascadeInputTransparent`
  on layouts is not evaluated (`QtHostInput.cs:276`, `Layout.cs:19-52`); (3) hit rects under `Rotation`/`Scale` are the
  *unscaled, unrotated* w×h placed at the transformed origin (`Layout.cs:347-348`), not the 2D footprint the docs claim,
  so a `Scale=2` view is tappable only on its top-left quarter (only `TranslationX/Y` is honoured exactly).
- `TappedEventArgs.GetPosition(relativeTo)` / `PointerEventArgs.GetPosition` ignore `relativeTo` (always root-space dp,
  `QtHostInput.cs:526-527,555,772`), while list-row taps give cell-relative positions (`QtHostListAdapter.Input.cs:80-83`).
- Gestures inside CollectionView rows: only `TapGestureRecognizer` is forwarded (`list-item-tapped` → `TryFindRowTap`);
  Pan/Swipe/Pinch/LongPress/Pointer in a DataTemplate never fire (`ListView.qml:370-380` has `onClicked` only).
  `Span.GestureRecognizers` are not seen (router reads `View.GestureRecognizers` only, not `CompositeGestureRecognizers`,
  `QtHostInput.cs:733`).
- Buttons/adapters: every semantic event in the checklist is wired except `Button/ImageButton Pressed/Released`,
  `Slider DragStarted/DragCompleted`, `SwipeView.SwipeChanging`, and `Editor.Completed` (never raised natively;
  Android/iOS raise it on focus loss).
- Text input: Keyboard/ReturnType/IsPassword/IsReadOnly/MaxLength/Cursor/Selection/ClearButton/Placeholder/alignment
  all map; gaps are `KeyboardFlags.Capitalize*` (no `ImhNoAutoUppercase` for `Keyboard.Create(None)`), `ReturnType.Next`
  not moving focus, `SoftInputExtensions.*` (plain-build extension **throws `NotSupportedException`** whenever a handler
  has a PlatformView — a backend cannot intercept it), and `ContentPage.HideSoftInputOnTapped` (plain-build manager is a
  no-op; router does not implement it).
- Keyboard avoidance: the VKB rect only reaches `SailfishMauiApplication.OnInputMethodChanged` and the lifecycle event
  (`MauiShell.qml:142-151`, `SailfishMauiApplication.cs:188-193`); no renderer code reads it. The page re-lays out
  through `window-geometry` when Silica shrinks the page (`MauiModelPage.qml:554-563` → `Layout.cs:115`), and
  `docs/silica-parity.md:54` records "focused field scrolls above the VKB" as native/verified on device (leg `silica F`);
  the Silica-side mechanism is not verifiable from this repo.
- Hardware Back/Escape pop via `TryPop` (`Boot.cs:188-195`) **without calling `Page.SendBackButtonPressed()`** and
  without honouring `Shell.BackButtonBehavior.Command`; only `IsVisible/IsEnabled`/`HasBackButton` gate Silica's back
  gesture (`QtHostPageRenderer.cs:1229-1231`). Apps that veto back (unsaved-changes prompts) lose that veto.
- Focus: text inputs are Qt-decided (`FocusHost`, replay when the object attaches); every other view's `Focus()` writes
  `IsFocused=true` and grants the request with no native focus (`SailfishHandlerCore.cs:80-85`, `SailfishViewHandler.cs:108`).
  `SetSemanticFocus` is a no-op (plain build) once a handler exists.

## Checklist table

Severity: **blocks** = apps that use it break or throw; **degrades** = feature silently partial; **cosmetic**; **n-a**.

### 1. Gesture recognizers on any View

| Feature | MAUI 11 behaviour | Sailfish state | Evidence (file:line) | Severity | Fix sketch |
|---|---|---|---|---|---|
| Tap: single, `Command`/`CommandParameter`, `Tapped` | `SendTapped(View, getPosition)` runs Command then raises `Tapped` (TappedEventArgs(Parameter, getPosition, Buttons)) | works on any hosted view (not on QML-consumed adapters, see below) | `QtHostInput.cs:497,506-547,551-562`; MAUI `decomp/TapGestureRecognizer.cs` `SendTapped` | — | — |
| Tap: `NumberOfTapsRequired` 1 + 2 on one owner | Android `SingleTapConfirmed`/iOS require-to-fail: the lower count waits | works: 300 ms window, 40 dp slop, lower count deferred while a higher one is possible | `QtHostInput.cs:70-78,512-546`; test `SampleAppRegressionTests.cs:641-677` | — | — |
| Tap: `Buttons=Secondary` | mask filters mouse buttons; on touch a Secondary-only recognizer never fires | router fires every tap regardless of `Buttons`; `TappedEventArgs.Buttons` echoes the recognizer's mask | `QtHostInput.cs:530-535` (no `Buttons` check) | cosmetic | skip recognizers whose `Buttons` lacks `Primary` |
| Tap: `TappedEventArgs.GetPosition(relativeTo)` | relative to `relativeTo`; `null` = window | always the press point in root-space dp (ignores `relativeTo`); list rows: cell-root relative | `QtHostInput.cs:526-527,555`; `QtHostListAdapter.Input.cs:80-83` | degrades | `getPosition = e => e is VisualElement v ? root − AbsoluteOrigin(v) : root` using the host rects (`MauiLogicalBounds`) |
| Tap on a QML-consumed adapter (`Button`, `Entry`, `Switch`, `Slider`, `ScrollView`, `ListView`, …) or an ancestor of one | platform: the recognizer on the Button/ancestor fires alongside Clicked (Android dispatches to the gesture detector before the click) | never: press on a `QmlConsumedUris` host returns before recognizer lookup; an ancestor's Tap does not see it either | `QtHostInput.cs:30-36,266-273` | degrades | after `NativeConsumed`, still run `TryFindRecognizers` on the *parent* chain for Tap/LongPress and dispatch on release (no capture) |
| Pan: `PanUpdated` Started/Running/Completed/Canceled, `TotalX/TotalY` | `IPanGestureController.SendPan*` | works; Started after 10 dp slop, Running on each move, Completed with release position, Canceled when a pinch takes over | `QtHostInput.cs:436-443,487-492,564-588,631-632` | — | — |
| Pan: `TouchPoints` | number of fingers required (iOS honours; Android ignores) | ignored (first finger only) | `QtHostInput.cs:79,564-588` (no `TouchPoints` read); `docs/parity-plan.md:230-232` | n-a (Android parity) | — |
| Pinch: Started/Running/Completed, `Scale`, `ScaleOrigin` | `IPinchGestureController.SendPinchStarted/SendPinch/SendPinchEnded` | works with two fingers (kind 7); scale = change since last update; origin relative to owner rect | `QtHostInput.cs:615-684`; `host_core.cpp:277-283`; test `SampleAppRegressionTests.cs:680-714` | — (done, not re-reported) | — |
| Swipe: four directions, `Swiped`, `Command` | `DetectSwipe` uses flag tests `IsLeft()`… and `Threshold` (default 100) | fires on release of a drag whose dominant axis ≥ **30 dp fixed**; `swipe.Direction != direction` equality → `Left\|Right` or `Up\|Down` recognizers **never fire**; `Threshold` ignored | `QtHostInput.cs:42,590-608`; MAUI `decomp/SwipeGestureRecognizer.cs` `DetectSwipe`, `Threshold` | degrades | `if ((swipe.Direction & direction) == 0) continue;` and `Math.Abs(total) >= Math.Max(swipe.Threshold, SwipeMinDp)`; or call `ISwipeGestureController.SendSwipe(owner,tx,ty)` + `DetectSwipe(owner, dir)` |
| Drag & Drop: `DragGestureRecognizer.CanDrag/DragStarting/DropCompleted`, `DropGestureRecognizer.AllowDrop/DragOver/DragLeave/Drop`, `DataPackage` | `SendDragStarting(View, getPosition, platformArgs)` returns args (Cancel/Handled); `SendDragOver/SendDragLeave(DragEventArgs)`, `SendDrop(DropEventArgs)` (Task, default Text/Image transfer), `SendDropCompleted` | **missing** — no reference in `src/` (`grep -rn "DragGestureRecognizer\|DropGestureRecognizer" src/` → none); `TryFindRecognizers` has no case | `QtHostInput.cs:717-760`; MAUI `decomp/DragGestureRecognizer.cs`, `DropGestureRecognizer.cs`, `DragEventArgs.cs` (public ctor takes only `DataPackage`; `GetPosition` virtual) | degrades (blocks apps built on DnD) | see design notes |
| Pointer: Pressed/Released/Moved | `SendPointerPressed/Released/Moved(View, getPosition, platformArgs=null, button=Primary)` | works: Pressed on capture, Moved per move, Released on release | `QtHostInput.cs:310-311,444,460-461,766-796` | — | — |
| Pointer: Entered/Exited | hover on mouse platforms | synthesised: Entered right before Pressed, Exited right after Released; no hover (touch screen) | `QtHostInput.cs:310,461,764-765` | n-a | — |
| Pointer: `PointerEventArgs.PlatformArgs` | platform event wrapper | always `null` (no `PlatformPointerEventArgs` ctor on plain build) | `QtHostInput.cs:783-787` | n-a | — |
| LongPress (new in 11): `MinimumPressDuration` | default 500 ms; Android ignores (system timeout), iOS honours | honoured: `Math.Max(1, min over owner's recognizers)` ms via `DispatchDelayed` | `QtHostInput.cs:806-808`; MAUI `decomp/LongPressGestureRecognizer.cs` (`MinimumPressDurationProperty` 500) | — | — |
| LongPress: `AllowableMovement` | default 10 px; movement beyond cancels | not read; the fixed 10 dp `TapSlopDp` cancels (`_longPressSeq++` on drag start) | `QtHostInput.cs:39,436-439,810` | cosmetic | use `Max(longPresses.Min(l => l.AllowableMovement), 0)` as the cancel radius in `OnMove` |
| LongPress: `NumberOfTouchesRequired` | iOS only; Android/Windows ignore | ignored | `QtHostInput.cs:800-830` | n-a | — |
| LongPress: `State` / `LongPressing` sequence | backends raise Started once recognised, Running while held, then Completed or **Canceled**; `State` is OneWayToSource | at fire time: `SendLongPressing(Started)`, `SendLongPressed`, `SendLongPressing(Completed)`; no Running, **never Canceled** (a press that moves away or releases early leaves `State` at its last value) | `QtHostInput.cs:818-823,439,459` | cosmetic | send `Started` at press when any recognizer is present, `Canceled` on slop-exceeded/early release/pinch |
| LongPress: `Command`, `LongPressed`, `GetPosition` | `SendLongPressed` runs Command then `LongPressed` | works; position = press point root-space; release after fire is no tap | `QtHostInput.cs:814-821,471-477` | — | — |
| LongPress vs `FlyoutBase.ContextFlyout` hold | separate features | both arm on the same press: LP at its duration, context menu at 600 ms; both fire | `QtHostInput.cs:53,244-264,312` | cosmetic | — |
| `Span.GestureRecognizers` (Label) | Label registers span gestures into `IGestureController.CompositeGestureRecognizers` as `ChildGestureRecognizer`; hit via `GetChildElements(point)` | **missing**: router reads only `v.GestureRecognizers`; label text is flattened to HTML with no span hit regions | `QtHostInput.cs:733`; `AdapterSnapshots.Label.cs:17-38,92-95`; MAUI `Label.SetupSpanGestureRecognizers` (decomp `Label` :452-470), `View.GetChildElements` :285 | degrades | treat `((IGestureController)v).CompositeGestureRecognizers` of type `ChildGestureRecognizer` as taps on the Label; span-level hit test needs QML `Text.positionAt`/`linkAt` → simplest: render spans with gestures as `<a href="span:N">` and use `linkAt(x,y)` in the Label adapter, reported as an adapter event |
| Gestures in CollectionView rows: Tap in template vs `SelectionChanged` | Android: a Tap recognizer consumes the touch, selection does not change; iOS: both | Tap wins, selection untouched (Android parity); documented | `QtHostListAdapter.Input.cs:126-134`; `docs/porting-existing-apps.md:221-223` | — | — |
| Pan/Swipe/Pinch/LongPress/Pointer inside a row template | fire on the platform | **missing**: the ListView consumes the press; only `onClicked` → `list-item-tapped` is wired | `ListView.qml:370-380`; `QtHostInput.cs:35` (`list-view` consumed) | degrades | delegate `MouseArea` → `onPressAndHold` → `list-item-held {row,cell,x,y}`; for pan/swipe hand the sequence to the router: emit `list-item-pressed` and let `OnPress` capture through `TryFindRowTap`-style lookup (`preventStealing` when a Pan owner is hit) |
| `IsEnabled=false` on element/ancestor | no gestures | respected (`EffectiveEnabled` walks ancestors) | `QtHostInput.cs:276`; `QtHostVisualState.cs:303-313` | — | — |
| `InputTransparent` on the hit element | the touch passes to the view *below* | sequence **ignored** (Silica keeps it) — nothing underneath gets the gesture; also `TryHitTest` does not skip transparent hosts although its doc says so | `QtHostInput.cs:275-282`; `Layout.cs:14-17,19-52` | degrades | move the check into `TryHitTest` (`continue` on `ve.InputTransparent` or cascading ancestor) so the next candidate wins |
| `CascadeInputTransparent` (Layout) | `InputTransparent` on a layout with cascade=true also makes its children transparent (platform layout intercepts) | not evaluated (children's own `InputTransparent` is false; router checks only the hit element) | `QtHostInput.cs:276`; MAUI `Layout.CascadeInputTransparent` (decomp :171) | degrades | in hit-test, walk ancestors: `Layout { InputTransparent: true, CascadeInputTransparent: true }` → skip subtree |
| Gesture on a view under `TranslationX/Y` | hit area follows the view | works (translation is in `toRoot.Tx/Ty`) | `Layout.cs:347,384-386`; `QtHostVisualState.cs:319-336` | — | — |
| Gesture on a view under `Rotation` / `Scale` / `ScaleX/Y` / anchor | hit area is the transformed footprint (Android `View` transforms the MotionEvent) | hit rect = **unscaled w×h at the transformed origin**; a `Scale=2` view (anchor .5) is hittable only in its top-left quarter; a rotated view's rect is wrong | `Layout.cs:347-348` (`new Rect(toRoot.Tx, toRoot.Ty, bounds.Width, bounds.Height)`); `QtHostVisualState.cs:391-394` (warns only for 3D) | degrades | keep `toRoot` per host (`Affine2`), hit-test by inverse-mapping the point (`TryInvert` exists, `QtHostVisualState.cs:57`) into local space and testing `0..w,0..h`; the 2D bounding box as a cheap first filter |
| Gesture on a 3D-rotated view | footprint | 2D footprint by design (documented, not re-reported) | `docs/parity-plan.md:233-235` | — | — |
| Conflict: Silica page back-swipe | n/a | the shim only observes (`eventFilter` returns false): a horizontal Pan/Swipe on a MAUI owner **also** drives Silica's `backNavigation` page drag (no `MouseArea` on gesture hosts); tab swipe is suppressed when the owner has pan/swipe/pinch | `host_core.cpp:224-225,245-302`; `QtHostInput.cs:330-332`; `MauiModelPage.qml:266,890-893` (containers have no MouseArea: `ContentView.qml:6`, `Border.qml:9`) | degrades | give gesture-owner hosts a `MouseArea { preventStealing: true }` (or `acceptedButtons`) toggled by a `mauiCaptures` prop pushed when `GestureRecognizers.Count>0`; alternatively push `backNavigation=false` for the duration of a captured horizontal drag |
| Conflict: pulley pull-down / page flickable | n/a | the page flickable is interactive only with a pulley or page-armed RefreshView; a vertical Pan at scroll-top also opens the pulley (observe-only) | `Layout.cs:559-563`; `MauiModelPage.qml:897` | degrades | same `preventStealing` host as above |
| Conflict: Pan inside a `ScrollView` | platform: the pan recognizer and the scroll negotiate (Android `RequestDisallowInterceptTouchEvent`) | both happen: the nested `scroll-view` flickable scrolls natively while the router pans (no capture at the Qt level) | `ScrollView.qml:39` (`interactive`), `host_core.cpp:301` | degrades | `preventStealing` MouseArea on pan owners; or push `interactive:false` on the enclosing scroll host while `_dragging` |
| `ScrollView.Scrolled` | raised from `SetScrolledPosition` | works for every ScrollView (each is a nested `scroll-view` host): `scroll-changed {id,x,y}` → `SetScrolledPosition` | `ScrollView.qml:66`; `AdapterEventRouter.cs:677-708`; `Layout.cs:555-558` (page flickable scrolls no content) | — | — |
| `ScrollView.ScrollToAsync` | animates to position/element, completes on finish | position/element modes jump (no animation), task completes via `SendScrollFinished` | `SailfishScrollViewHandler.cs:76-104` | cosmetic | — |

### 2. Buttons / adapters (event wiring)

| Control event | Wired? | Evidence |
|---|---|---|
| Button `Clicked` | yes (`SendClicked`) | `Button.qml:177`; `AdapterEventRouter.cs:67-74` |
| Button `Pressed`/`Released` | **no** (`SendPressed/SendReleased` never called) | `grep -rn SendPressed src/` → none; `Button.qml:177` has `onClicked` only |
| ImageButton `Clicked` | yes | `Image.qml:255-261` (`mauiTappable` MouseArea); `AdapterEventRouter.cs:61-66` |
| ImageButton `Pressed`/`Released` | **no** | same |
| Switch `Toggled` | yes | `Switch.qml:41`; `AdapterEventRouter.cs:323` |
| CheckBox `CheckedChanged` | yes | `CheckBox.qml:27`; `AdapterEventRouter.cs:326` |
| Slider `ValueChanged` | yes | `Slider.qml:62-66`; `AdapterEventRouter.cs:329` |
| Slider `DragStarted`/`DragCompleted` (+ commands) | **no** | `Slider.qml` has no `pressedChanged`; `grep -rn "DragStarted\|DragCompleted" src/` → none |
| Stepper `ValueChanged` | yes | `Stepper.qml:34`; `AdapterEventRouter.cs:332` |
| RadioButton `CheckedChanged` + groups | yes (group unchecking is MAUI-side) | `RadioButton.qml:117`; `AdapterEventRouter.cs:338-340` |
| Picker `SelectedIndexChanged`, `IsOpen` | yes | `Picker.qml:134,175`; `AdapterEventRouter.cs:342,456-476` |
| DatePicker `DateSelected`, TimePicker `TimeSelected` | yes | `DatePicker.qml:134`, `TimePicker.qml:125`; `AdapterEventRouter.cs:433-454,481-501` |
| Entry `TextChanged`/`Completed`/`Focused`/`Unfocused` | yes | `Entry.qml:98-109,118-127`; `AdapterEventRouter.cs:317,367-392,399-427` |
| Editor `TextChanged`/`Focused`/`Unfocused` | yes | `Editor.qml:86-104` |
| Editor `Completed` | **no** — never raised (TextArea has no `accepted`; Return inserts a newline). Android/iOS raise it on focus loss (platform handler behaviour, not verifiable from the plain dll) | `Editor.qml:5-7`; `AdapterEventRouter.cs:395-398` |
| SearchBar `TextChanged`/`SearchButtonPressed` (+ `SearchCommand`) | yes (`ISearchBarController.OnSearchButtonPressed`) | `SearchBar.qml:81-85,93-104`; `AdapterEventRouter.cs:335,416-418` |
| WebView `Navigating`/`Navigated`/`EvaluateJavaScriptAsync` | yes | `AdapterEventRouter.cs:578-609` |
| SwipeView `SwipeStarted`/`SwipeEnded`/`Invoked`/`IsOpen` | yes | `SwipeView.qml:69,73`; `AdapterEventRouter.cs:613-657` |
| SwipeView `SwipeChanging` | **no** (`SendSwipeChanging` never called) | `AdapterEventRouter.cs:637-657`; MAUI `SwipeView` :291 |
| RefreshView `Refreshing` | yes (`IsRefreshing=true`) | `pullrefresh.js:26`; `AdapterEventRouter.cs:551-575` |
| IndicatorView tap → `Position` | yes | `IndicatorView.qml:53`; `AdapterEventRouter.cs:660-673` |
| CollectionView row tap → selection / template Tap | yes | `ListView.qml:370-380`; `QtHostListAdapter.Input.cs:119-156` |

### 3. Text input

| Feature | MAUI 11 behaviour | Sailfish state | Evidence | Severity | Fix sketch |
|---|---|---|---|---|---|
| `Keyboard.Email/Url/Telephone/Numeric/Plain` | platform input types | mapped to Qt `inputMethodHints` (Email+Url add `ImhNoAutoUppercase`, Numeric = `FormattedNumbersOnly\|PreferNumbers`, Plain = `NoPredictiveText`) | `AdapterSnapshots.TextInput.cs:127-153`; `Entry.qml:74-75` | — | — |
| `Keyboard.Default/Text/Chat` | default text (Chat: no auto-correct suggestions on Android) | hints 0 (Silica default: predictive + auto-caps) | `AdapterSnapshots.TextInput.cs:137-149` | cosmetic | Chat → `ImhNoPredictiveText`? (Android Chat keeps suggestions; leave) |
| `KeyboardFlags.CapitalizeSentence/Word/Character/None`, `Keyboard.Create(None)` | auto-cap mode; `None` = no auto-cap, no spellcheck, no suggestions | only `Suggestions` is read (→ `ImhNoPredictiveText`); **no `ImhNoAutoUppercase`** for `None`/`CapitalizeNone`; Word/Character have no Qt equivalent | `AdapterSnapshots.TextInput.cs:148-149`; MAUI `decomp/KeyboardFlags.cs` | cosmetic | `if (custom.Flags & (CapitalizeSentence\|CapitalizeWord\|CapitalizeCharacter)) == 0) hints \|= ImhNoAutoUppercase` |
| `IsTextPredictionEnabled` / `IsSpellCheckEnabled` | separate toggles | folded into one `ImhNoPredictiveText` (Qt has no separate hint) | `AdapterSnapshots.TextInput.cs:28-29,150-151` | — | — |
| `ReturnType` (Done/Go/Next/Search/Send) icon | IME action label | Silica `EnterKey.iconSource` (Next → enter-next, Search → search, others → accept) | `AdapterSnapshots.TextInput.cs:117-123`; `Entry.qml:78-79`; `SearchBar.qml:57-58` | — | — |
| `ReturnType.Next` moves focus to the next field | Android IME Next focuses the next focusable; iOS no | `Completed` fires, focus stays | `AdapterEventRouter.cs:399-427` | cosmetic | on `completed` with `ReturnType.Next`: find next `InputView` in the page's visual order and `Focus()` it |
| `ReturnCommand` / `Completed` on Enter (Entry, SearchBar) | `SendCompleted` runs `ReturnCommand` + `Completed` | works via `editor.accepted` (hardware Return and VKB enter, one path) | `Entry.qml:116-127`; `AdapterEventRouter.cs:410-418` | — | — |
| `IsPassword` | obscured text | `echoMode=Password` + hints 0x47 composed in QML | `AdapterSnapshots.TextInput.cs:37`; `Entry.qml:72-75` | — | — |
| `IsReadOnly` | no edits, no keyboard | `readOnly` | `AdapterSnapshots.TextInput.cs:27`; test campaign `docs/app-test-campaign.md:214` | — | — |
| `MaxLength` | caps input | Entry/SearchBar: `maximumLength` (clamped to 32767); Editor: adapter truncates and reports | `AdapterSnapshots.TextInput.cs:38,44,156-157`; `Editor.qml:66-97` | — | — |
| `CursorPosition` / `SelectionLength` two-way | handler maps both ways | works: transient push outside the snapshot; native `cursor-changed` written back | `SailfishTextHandlers.cs:94-96,108-114`; `textinput.js:20-38`; `AdapterEventRouter.cs:508-546`; test `AdapterEventRouterTests.cs:10-24` | — | — |
| `ClearButtonVisibility.WhileEditing` | clear glyph while focused | `IconButton` as `rightItem` while focused with text | `Entry.qml:81-91` | — | — |
| `Placeholder` / `PlaceholderColor` | hint text | `placeholderText`, `placeholderColor` | `AdapterSnapshots.TextInput.cs:26,93`; `Entry.qml:51` | — | — |
| H/V text alignment | — | done (not re-reported) | `AdapterSnapshots.TextInput.cs:82-104`; `textinput.js:44-74` | — | — |
| `Editor.AutoSize=TextChanges` | editor grows with text; Controls invalidates measure on text change | measure grows past three lines with wrapped text (needs `QtHostTextMetrics.Enabled`) | `SailfishMeasure.cs:285-302`; MAUI `Editor.UpdateAutoSizeOption` :173-212 | — (unverified on device) | — |
| `entry.Focus()` from code → VKB opens | native requestFocus; keyboard follows | `FocusHost` pushes `mauiFocus` → `forceActiveFocus()` → Maliit opens; Qt's refusal (disabled/hidden) is read back and `IsFocused` corrected; pending focus replayed when the object attaches | `SailfishHandlerCore.cs:74-86`; `Hosts.cs:19-45,230-247`; `textinput.js:6-9`; test `HandlerFixTests.cs:132-153` | — | — |
| `Unfocus()` closes the VKB | yes on platforms | `inputMethod.commit(); focus=false; inputMethod.hide()` | `textinput.js:9-16` | — | — |
| `SoftInputExtensions.HideSoftInputAsync/ShowSoftInputAsync/IsSoftInputShowing` | platform-specific; on the plain `net` build the private `HideSoftInput/ShowSoftInput/IsSoftInputShowing(object)` **throw `NotSupportedException`** whenever `Handler.PlatformView` is non-null (always true here) | not implementable in the backend (static extension in `Microsoft.Maui.dll`); an app calling `entry.HideSoftInputAsync(ct)` throws synchronously | `decomp/SoftInputExtensions.cs` (`TryGetPlatformView` → `platformView.HideSoftInput()` → `throw new NotSupportedException()`) | **blocks** callers (common "hide keyboard on submit" pattern) | document: use `Unfocus()`; offer `SailfishSoftInput.Hide()/Show(view)/IsShowing` (`Qt.inputMethod.hide()`; `svc-input-method` state); upstream: ask MAUI for an `ISoftInputService` hook |
| `ContentPage.HideSoftInputOnTapped` | tapping outside a text input hides the keyboard | plain-build `HideSoftInputOnTappedChangedManager` is a no-op; router does not implement it | `decomp/HideSoftInputOnTappedChangedManager.cs`; `grep -rn HideSoftInputOnTapped src/` → none | degrades | in `OnPress`: if `page is ContentPage { HideSoftInputOnTapped: true }` and the hit host is not a text adapter → `Unfocus()` the focused `InputView` (track it from `focus-changed`) |
| Keyboard overlap (focused field stays visible) | Android `adjustResize`/iOS scroll | VKB rect reaches only `OnInputMethodChanged` (app virtual + lifecycle); no renderer logic. The page re-lays out when Silica shrinks the page (`window-geometry` on `page.height` change). `docs/silica-parity.md:54` marks "focused field scrolls above the VKB" as native, verified on device (leg `silica F`) | `MauiShell.qml:142-151`; `SailfishMauiApplication.cs:78,188-193`; `MauiModelPage.qml:554-563`; `Layout.cs:111-134`; `docs/architecture-handoff.md:47-48` (landscape prompt title panned off) | unverified (works per docs; see design notes for the explicit path) | — |
| `Entry` in a CollectionView row keeps focus across row re-use | platform recyclers lose focus on rebind too | text inputs are **not pooled** (`PoolableUris` excludes `entry/editor/search-bar`), so a recycled row rebuilds its field; focus on a scrolled-out row is lost (as on Android) | `QtHostListAdapter.RowPool.cs:21-26` | — (unverified) | — |

### 4. Hardware / system keys

| Feature | MAUI 11 behaviour | Sailfish state | Evidence | Severity | Fix sketch |
|---|---|---|---|---|---|
| Back / Escape key → pop | Android: `Page.SendBackButtonPressed()` (`OnBackButtonPressed` veto, Shell's override, modal pop) | `Key_Back`/`Key_Escape` press → `renderer.TryPop()` → `ResolveBackTarget` (modal first, then root stack) → `PopModalAsync`/`PopAsync`; **`SendBackButtonPressed` never called** → `OnBackButtonPressed` overrides and `Shell.BackButtonBehavior.Command` are ignored | `Boot.cs:188-195`; `QtHostRuntime.cs:12-15`; `QtHostPageRenderer.cs:606-623`; `Navigation.cs:99-115`; MAUI `Page.SendBackButtonPressed` (decomp :584-616), `Shell.OnBackButtonPressed` :2219 | degrades (apps with unsaved-changes veto / custom back) | in `TryPop`: `if (window.Page is Page root && root.SendBackButtonPressed()) return true;` before `ResolveBackTarget` (Shell's override already walks to the visible page and handles `BackButtonBehavior.Command`); for a modal: `modals[^1].SendBackButtonPressed()` |
| Silica back gesture (page swipe) → native pop | n/a | `backNavigation` pushed from `BackNavigationOf(page)` (`BackButtonBehavior.IsVisible/IsEnabled`, `HasBackButton`); the native pop is then followed by MAUI (`NativeStackCoordinator`); no veto either | `QtHostPageRenderer.cs:1226-1231`; `MauiModelPage.qml:266`; `Navigation.cs:208` | degrades | native pop cannot be vetoed after the fact; push `backNavigation=false` when the page type overrides `OnBackButtonPressed` (reflection on the method's `DeclaringType != typeof(Page)`) and handle Back via the key path only |
| Volume keys | n/a | not routed (shim observes all keys; only Back/Escape acted on) | `host_core.cpp:286-299`; `Boot.cs:191-192` | n-a | — |
| Hardware keyboard typing into Entry | native | native (Qt focus item; observe-only filter) | `host_core.cpp:224-225,301` | — | — |
| `IsTabStop` / `TabIndex` | removed from MAUI 11 Controls (grep on decompiled `VisualElement` → 0 hits) | n/a | — | n-a | — |
| `KeyboardAccelerator` | desktop menus | n/a | — | n-a | — |

### 5. Focus model

| Feature | MAUI 11 behaviour | Sailfish state | Evidence | Severity | Fix sketch |
|---|---|---|---|---|---|
| Text inputs: `Focus()/Unfocus()/IsFocused/Focused/Unfocused` | platform decides; `IsFocused` written back from the native focus change | Qt decides (`activeFocus` read back); `focus-changed` → `ve.Focus()/Unfocus()`; a refused managed focus pulls native back | `Hosts.cs:19-45`; `AdapterEventRouter.cs:367-392`; `Entry.qml:104-109` | — | — |
| Non-text views (`Button.Focus()`, `Grid.Focus()`, …) | native focusable views take focus; `Focus()` returns false when not focusable | `FocusNatively` → null → **`IsFocused=true` written directly and the request granted** with no native focus; `Unfocus()` resets it | `SailfishHandlerCore.cs:74-86`; `SailfishViewHandler.cs:104-108` | cosmetic (deviation: `Focused` event fires, VSM `Focused` state applies, nothing visible natively) | acceptable; alternatively grant only for `Button/Switch/CheckBox/Slider/Picker` by pushing `forceActiveFocus` on the adapter root and reading `activeFocus` back |
| `SetSemanticFocus()` | screen-reader focus | plain-build no-op once `Handler.PlatformView` exists; throws `NullReferenceException` before (documented) | `decomp/SemanticExtensions.cs`; `docs/porting-existing-apps.md:226-229`; `SailfishServiceOverlay.cs:51` | n-a | — |

### 6. Multi-touch

| Feature | Sailfish state | Evidence | Severity |
|---|---|---|---|
| Second finger | kind 7 right after each touch event with ≥2 points: `points.at(1)` + fingers-down count; used **only** for pinch on a captured owner that has a `PinchGestureRecognizer` | `host_core.cpp:277-283`; `QtHostInput.cs:615-654` | — |
| Third+ finger | never emitted | `host_core.cpp:282` (`points.at(1)` only) | n-a |
| Two Sliders (or any two QML adapters) at once | Silica controls are MouseArea-based; Qt synthesises mouse from the first touch point only → the second finger does nothing (native Qt behaviour, not router) | `Slider.qml`, `host_core.cpp:245-256` | degrades (unverified on device) |
| Pan on view A while another finger holds on view B | if the holding finger is first: the pan finger is "second point" and is ignored unless A has a Pinch → **no pan**; if the panning finger is first: pan works, the holder is ignored | `QtHostInput.cs:617-618` | degrades |
| First finger lifts while the second stays | `points.first()` then becomes the remaining finger → `_totalDpX/Y` jump; after a pinch the sequence is already "no tap/pan" | `host_core.cpp:270-275`; `QtHostInput.cs:429-434` | cosmetic (unverified: Qt 5.6 touch-point ordering) |

## Router precedence rules as implemented (`QtHostInput.cs`)

1. **Observe-only at the Qt level.** The shim's `eventFilter` returns `false` for every pointer/key event (`host_core.cpp:245-302`), so Silica/QML always process the event first; the router can only add MAUI dispatch, never remove native handling. The only "capture" is the router's own `_captured` state.
2. **Touch beats synthesized mouse.** Mouse kinds 0–2 arriving during a touch sequence or within 250 ms after `TouchEnd` are dropped (`:185-199`). Wheel is observed only (`:219-223`).
3. **Press → hit-test** (`TryHitTest`, `Layout.cs:19-52`): topmost host whose root-space rect (translation-only transform, see §1) contains the point, honouring `IsVisible`/`Visibility` up the chain, `HitClipDp` of nested scroll viewports, higher `ZIndex`, descendant-over-ancestor. No `InputTransparent` check here.
4. **No host under the finger → Silica keeps it** (`:236-242`; `Ignored++`). Tab-swipe arming happens first (`:234,317-341`): only on a page with a tab bar, outside the 40 dp edge bands, not over a QML-consumed/scroll host, not over an owner with pan/swipe/pinch recognizers, not inside horizontally scrolling content (`ScrollsSideways`, `:345-366`).
5. **`ContextFlyout` on the element or an ancestor arms a 600 ms hold** (`:244-264`), independently of what follows; travel > 10 dp cancels (`:418-424`); a fired hold suppresses the tap (`:479-485`).
6. **QML-consumed adapters end routing** (`QmlConsumedUris`, `:30-36,266-273`): button, entry, editor, switch, slider, check-box, stepper, indicator-view, swipe-view, web-view, dialogs, scroll-view, list-view, carousel-view. Their semantic events come through `AdapterEventRouter`. Note: `image`, `label`, `picker`, `date-picker`, `time-picker`, `radio-button`, `search-bar`, `border`, layouts and shapes are **not** consumed, so a recognizer on them or on their ancestors is honoured (and the native control still reacts, e.g. a Picker opens).
7. **`InputTransparent` or effectively disabled hit → ignored** (`:275-282`) — the sequence ends; nothing falls through.
8. **Nearest `View` ancestor-or-self with any Tap/Pan/Swipe/LongPress/Pointer/Pinch in `GestureRecognizers` owns the sequence** (`TryFindRecognizers`, `:717-760`; walks `Parent as View`, so it stops at the Page). `CompositeGestureRecognizers` (Span gestures, VSM PointerOver) are not consulted. Capture survives the finger leaving the owner (`:293-294`).
9. **During capture:** Pointer Entered+Pressed immediately (`:310-311`); LongPress timer armed (`:312`); move > 10 dp on either axis → Pan Started then Running + cancels LongPress (`:436-444`); second finger with ≥2 down on a Pinch owner → Pan Canceled, pinch Started, sequence flagged `_pinched` (no tap/pan afterwards) (`:627-640`).
10. **Release:** Pointer Released+Exited; then exactly one of: pinch Completed (`:462-470`), nothing after a fired LongPress/hold (`:471-485`), Pan Completed + Swipe (`:487-494`), or Tap (`:497`, multi-tap counting `:512-546`). All dispatch is marshalled to the MAUI dispatcher; handler exceptions are logged, never rethrown into the Qt loop.
11. **Rows of ListView/CarouselView:** a separate path — the delegate's `MouseArea.onClicked` → `list-item-tapped {row,cell,x,y}` → `OnRowTapped` → `TryFindRowTap` (deepest visible, non-transparent, enabled view with a `TapGestureRecognizer`, bubbling to the cell root) else selection (`QtHostListAdapter.Input.cs:77-156`).

Known limitations following from these rules: a MAUI horizontal pan also drags Silica's back-navigation page swipe and a vertical pan at the top also pulls the pulley (observe-only, rule 1); a `TapGestureRecognizer` on a `Button`/`Entry` or on their ancestors never fires (rule 6); gestures other than Tap do not exist inside list rows (rule 11); transforms other than translation break hit-testing (rule 3).

## Design notes

### LongPress — finishing the existing implementation
Files: `QtHostInput.cs` (`OnPress/OnMove/OnRelease/ArmLongPress`), `ListView.qml`/`CarouselView.qml` delegate `MouseArea`, `QtHostListAdapter.Input.cs`.
- Honour `AllowableMovement`: keep `_longPressSlopDp = longPresses.Min(l => l.AllowableMovement)` at capture; in `OnMove` cancel the LP (`_longPressSeq++`, send `Canceled`) when travel exceeds it, independent of `TapSlopDp`.
- State machine per MAUI docstring: `SendLongPressing(Started)` when the timer fires is fine (Android does the same), but add `SendLongPressing(Canceled)` on early release / movement / pinch so `State` is OneWayToSource-correct; optionally `Running` on each move after the fire while the finger is still down (iOS semantics).
- Row templates: add `onPressAndHold` (Qt's 800 ms; or a `Timer` using the recognizer's duration pushed as `mauiHoldMs`) to the delegate `MouseArea` → `list-item-held {row,cell,x,y}` → a `TryFindRowLongPress` twin of `TryFindRowTap`; suppress the following `onClicked`.
- Position: provide a real `getPosition` (root dp → relative to `relativeTo` via the host rects) shared by Tap/Pointer/LongPress.

### Drag & Drop on top of the router
MAUI 11 public surface (decomp): `DragGestureRecognizer.SendDragStarting(View, getPosition, platformArgs=null)` → returns `DragStartingEventArgs` (`Cancel`, `Data: DataPackage`, marks the drag active); `DropGestureRecognizer.SendDragOver(DragEventArgs)`, `SendDragLeave(DragEventArgs)`, `Task SendDrop(DropEventArgs)` (does the default Text/Image transfer when not `Handled`), `DragGestureRecognizer.SendDropCompleted(DropCompletedEventArgs)`. Only the parameterless/`DataPackage` ctors are public; `GetPosition` is `virtual`, so subclass `DragEventArgs`/`DropEventArgs` in the backend to supply positions.
1. `TryFindRecognizers`: add `DragGestureRecognizer` (owner = drag source) and keep a page-wide list of `DropGestureRecognizer` owners (collect in the walk where `GestureRecognizers.Count > 0` already forces a host, `Walk.cs:219-233`).
2. Start condition: Android starts a drag on long-press (`DragAndDropGestureHandler.OnLongPress`); iOS on a long press + move. Use the existing LongPress timer path: at `HoldFireMs`-like timeout (or when `_dragging` begins after a hold ≥ 300 ms) call `SendDragStarting(owner, getPosition)`; if `Cancel`/`Handled` → fall back to normal pan/tap. Else enter `_dragActive` with the `DataPackage`.
3. Visual: push a page-level "drag ghost" host (new `interactions/DragGhost.qml`: an `Item` with `grabToImage` of the source host — available in Qt 5.6 — or a translucent rectangle with the `DataPackage.Text`) positioned from `OnMove` through `CallPage("mauiDragMove", x, y)`; set `backNavigation=false` and the flickables non-interactive for the duration (same mechanism the dialog uses, `MauiModelPage.qml:797-799`).
4. Over/Leave: on each move hit-test (`TryHitTest`) and walk ancestors for a `DropGestureRecognizer { AllowDrop: true }`; on target change send `SendDragLeave` to the old and `SendDragOver` to the new (`DragEventArgs(package)` subclass with position); `AcceptedOperation == None` → show the ghost as "not allowed".
5. Drop: on release over a target `await SendDrop(new SailfishDropEventArgs(package.View, position))`; then `SendDropCompleted(new DropCompletedEventArgs())` on the source recognizer (it fires only while the drag is active). Cancel (second finger, Back key, release outside) → `SendDropCompleted` as well (MAUI has no cancel distinction).
6. Lists: a drag source inside a row needs the row path (`list-item-held` from the LongPress work) and a `preventStealing` MouseArea on the delegate while dragging, otherwise the ListView flicks.
7. Tests: FakeShim router tests like `SampleAppRegressionTests.cs:680-714` (press, hold past the timer via `SailfishRuntime.TickDueTimers`, moves, release) asserting DragStarting/DragOver/Drop/DropCompleted order and the default text transfer into a `Label`.

### Keyboard avoidance — make the path explicit
Today: `Qt.inputMethod` visibility/rect → `svc-input-method` (`MauiShell.qml:142-151`) → `SailfishMauiApplication.OnInputMethodChanged` + lifecycle delegate only. Layout adapts only indirectly, through `page.height` changes reported as `window-geometry` (`MauiModelPage.qml:554-563` → `ApplyWindowGeometry`, `Layout.cs:115`). Whether Silica actually shrinks the page or pans it is not visible in this repo (docs say the field scrolls into view natively, leg `silica F`; the landscape prompt note in `architecture-handoff.md:47-48` shows content being *panned* off the top rather than resized).
Proposed: 
1. Subscribe the renderer to `ShellEvents.InputMethod` (`ShellEvents.cs:20,120-124`) and keep `_keyboardDp` (window px → dp via `QtHostUnits`).
2. Expose it as a bottom safe-area inset in the layout pass (`_windowDp.Height − keyboard overlap`) so MAUI's arrange shrinks like Android `adjustResize`; `Page.Height`/`OnSizeAllocated` then reflect it.
3. After relayout, if the focused `InputView` host rect (`MauiLogicalBounds`) intersects the keyboard rect, scroll the nearest enclosing hosted `ScrollView`/`ListView` (`ScrollToAsync`/`ScrollTo`) so the field's bottom sits above the keyboard (iOS behaviour); track the focused input from `focus-changed` (`AdapterEventRouter.cs:367-392`).
4. Device check (owner): screenshot with the VKB up on a page whose Entry is at the bottom (per memory: screenshots, not state readbacks), landscape too.

### Optional: hook the MAUI 11 gesture plumbing
`Microsoft.Maui.Controls.Platform.IGesturePlatformManagerFactory` (public, DI-registered) replaces the per-handler `GesturePlatformManager` and is created/disposed on every handler connection (`decomp/GestureManager.cs`, `IGesturePlatformManagerFactory.cs`). Registering a Sailfish factory gives the router a per-view hook (recognizer collection changed, handler connected/disconnected, `CompositeGestureRecognizers` incl. Span children) instead of walking `GestureRecognizers` at press time, and is the designed extension point for "community backends that do not use `IPlatformViewHandler`".

## Open questions / unverified

- Silica VKB behaviour (page resize vs pan, auto-scroll of the focused field) cannot be verified from the repo; `docs/silica-parity.md:54` claims device verification (leg `silica F`).
- Android/iOS `Editor.Completed`-on-focus-loss and `ReturnType.Next` focus-advance semantics are from memory of the platform handlers (not in the plain-build dll).
- Qt 5.6 `QTouchEvent::touchPoints()` ordering when the first finger lifts (affects the "first point jumps" note).
- Two Silica Sliders under two fingers: standard Qt mouse-synthesis behaviour, not tested here.
- `Editor.AutoSize` growth and `QtHostTextMetrics.Enabled` on device; LongPress/Pointer/Pan/Swipe have **no host tests** (only Tap multi-tap and Pinch: `SampleAppRegressionTests.cs:641-714`).
- Whether `IGesturePlatformManagerFactory` is consulted on the plain `net11.0` TFM at runtime (the `GestureManager` code path is present in the decompiled Controls dll; not executed here).
