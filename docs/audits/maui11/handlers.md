<!-- Static audit of commit af3d782 (branch feature/fixes) against .NET MAUI 11.0.0-rc.1.26451.6, 2026-10-04. Produced by a read-only code review with ilspycmd decompiles; nothing was run on the device. Items marked unverified were not checked on the phone. Summary and work packages: docs/maui11-alignment-plan.md -->

# Handler and element coverage audit — maui-sailfish (feature/fixes) vs .NET MAUI 11.0.0-rc.1.26451.6

Scope: TYPE-level handler coverage and COMMAND-mapper coverage. Property-mapper key parity for the 27 controls in
`tests/Linux.SailfishOS.Tests/HandlerParityTests.cs:26-54` is already 100 % and is not re-measured here; this report
covers what that test does not: element types without a Sailfish row, handlers whose mappers the test skips
(CollectionView/CarouselView chain, Line/Path/Polygon/… shape handlers, Page/Flyout/Tabbed/Toolbar, SwipeItem,
HybridWebView, Window/Application, Menu*), every `CommandMapper`, and the extensibility conventions.
Static analysis only (ilspycmd on the two reference assemblies, grep/read on the repo). Nothing was built or run.

Reference facts (decompiled):
- `Microsoft.Maui.Controls.Hosting.AppHostBuilderExtensions.AddControlsHandlers` registers 47 pairs (scratchpad
  `AppHostBuilderExtensions.cs:52-98`): CollectionView, CarouselView, Label, Editor, Picker, RadioButton, TimePicker,
  Switch, ProgressBar, ActivityIndicator, Image, SearchBar, Slider, DatePicker, Entry, Application, BoxView, Button,
  CheckBox, GraphicsView, Layout, ScrollView, Stepper, Page, WebView, HybridWebView, Border, IContentView, ContentView,
  Ellipse, Line, Path, Polygon, Polyline, Rectangle, RoundRectangle, Window, ImageButton, IndicatorView, RefreshView,
  SwipeItem, SwipeView, MenuBar, MenuFlyoutSubItem, MenuFlyoutSeparator, MenuFlyoutItem, MenuBarItem.
- Handler classes in the net11.0 builds: Core `Microsoft.Maui.Handlers.*` (ActivityIndicator, Application, Border,
  Button, CheckBox, ContentView, DatePicker, Editor, Element, Entry, FlyoutView, GraphicsView, HybridWebView,
  ImageButton, Image, IndicatorView, Label, Layout, MenuBar, MenuBarItem, MenuFlyout, MenuFlyoutItem,
  MenuFlyoutSeparator, MenuFlyoutSubItem, NavigationView, Page, Picker, ProgressBar, RadioButton, RefreshView,
  ScrollView, SearchBar, ShapeView, Slider, Stepper, SwipeItemMenuItem, SwipeItemView, SwipeView, Switch, TabbedView,
  TimePicker, Toolbar, View, WebView, Window); Controls `Microsoft.Maui.Controls.Handlers.*` (BoxView, Items.CollectionView,
  Items.CarouselView and the ItemsView/Structured/Selectable/Groupable/Reorderable chain, Line, Path, Polygon,
  Polyline, Rectangle, RoundRectangle). There is NO ShellHandler, ShellItem/Section/Content handler,
  TitleBarHandler, ListView/TableView/Cell renderer or FrameRenderer in the net11.0 reference assemblies (they are
  platform-only / Compatibility); `Microsoft.Maui.Controls.Compatibility` 11 is not in the NuGet cache and is not a
  dependency of the `Microsoft.Maui.Controls` meta-package (nuspec: Build.Tasks, Core, Xaml, Resizetizer).
- Sailfish table: `src/Linux.SailfishOS/Handlers/SailfishHandlersFactory.cs:241-276` (33 rows), fallbacks
  `:198-201` (`Page`→`SailfishPageHandler`, `ILayout`→`SailfishLayoutHandler`, else `SailfishContainerHandler`),
  menus short-circuited to `NullElementHandler` at `:67`, Application/Window registered in
  `src/Linux.SailfishOS/Hosting/AppHostBuilderExtensions.cs:47-52`.

## Summary

- Every view type MAUI 11 registers by default has a Sailfish row or an adequate fallback, with three exceptions:
  **`HybridWebView`** (falls to `SailfishContainerHandler`: blank container, 0/3 commands, no JS bridge),
  **`SwipeItem`** (no handler; the SwipeView snapshot serialises items itself and drops MAUI 11's `IconColor`,
  `TextColor`, `Font`, `CharacterSpacing`), and the **`MenuBar`/`MenuBarItem`/`MenuFlyout*`** family (deliberate
  `NullElementHandler`; `ContextFlyout` is rendered by the renderer, not a handler — see the weak spots below).
- **Legacy `ListView` (`ItemsView<Cell>`), `TableView` and every `Cell`** have no route at all. `Row<ItemsView,
  SailfishListViewHandler>` (`SailfishHandlersFactory.cs:246`) does not match `ItemsView<Cell>`; the renderer only
  special-cases `CollectionView`/`CarouselView` (`QtHostPageRenderer.Walk.cs:122-129`); nothing calls
  `ListView.SetupContent`, so `_visualChildren` stays empty. Result: an empty container plus one warning
  (`Walk.cs:202-206`). The only mention in code is `SailfishViewHandler.cs:239`. `docs/silica-parity.md:46` lists
  `ListView` as "native" — the doc contradicts the code — and `docs/porting-existing-apps.md:295-313` ("Not supported
  yet") does not list ListView/TableView/HybridWebView/MenuBar.
- Command mappers: 30 of 35 official command keys that matter on this platform are answered; the misses are
  `ApplicationHandler.ActivateWindow` (silent no-op), the three `HybridWebView` commands, and the Menu* Add/Remove/
  Clear/Insert (no-op by design). `Frame`/`ZIndex` from `ViewHandler.ViewCommandMapper` arrive through chaining.
  `IScrollView.RequestScrollTo` is not in `SailfishScrollViewHandler.CommandMapper` but the handler answers the
  Controls `ScrollToRequested` event (`SailfishScrollViewHandler.cs:61-105`) and a test proves an app's
  `AppendToMapping` on the key still runs (`W1CorrectnessTests.cs:140`).
- Window: `SailfishWindowHandler.Mapper` maps `Content` only of MAUI 11's `Title, Content, X, Y, Width, Height`.
  `Title` is meaningful (the cover shows the .desktop title, `SailfishMauiApplication.cs:220`; `Window.Title`
  never reaches it). `Window.FlowDirection` (raised by Controls, `Ctl_Window.cs:979`) is not mapped and the RTL root
  resolution skips the Window because it is not a `VisualElement` (`QtHostVisualState.cs:220-228`): an app that sets
  RTL on the window gets LTR. `StatusBarTheme` exists on `Controls.Window` but is not in the net11 `WindowHandler.Mapper`
  and has no Sailfish meaning (lipstick owns the status bar) → n/a.
- Renderer-owned chrome works but bypasses handlers: `Page.ToolbarItems` → pulley/push-up menus
  (`QtHostPageRenderer.Interactions.cs:24-63`), `ContextFlyout` → Silica `ContextMenu` (`:249-290`). Weak spots:
  icon-only `ToolbarItem`s become blank pulley entries (only `Text`/`IsEnabled` are serialised, `:36`, `:220-231`);
  `MenuFlyoutSubItem` is flattened to one entry (its children are lost) and `MenuFlyoutSeparator` becomes an empty,
  enabled entry (`:233-245`, `qml/interactions/ContextMenu.qml:44-45`).
- Extensibility: all 31 public view/page handlers expose public static `Mapper` + `CommandMapper` and the
  `(IPropertyMapper?, CommandMapper?)` constructor (handoff W5.5 done, verified). Deviations: `SailfishApplicationHandler`
  and `SailfishWindowHandler` have only the parameterless constructor; `SailfishNavigationViewHandler` uses
  `NavigationCommandMapper` while the inherited static `CommandMapper` (from `SailfishPageHandler`) is visible on the
  type and is NOT what the handler uses — `SailfishNavigationViewHandler.CommandMapper.AppendToMapping("RequestNavigation", …)`
  compiles and silently does nothing.
- Snapshot re-push (decision W10.6b) is documented in `docs/custom-controls.md`; its consequence is that
  `ModifyMapping` cannot suppress a value and an app's write to the adapter survives only until the next push of any
  family key. `PlatformView` is `NativeElementHost` (read-mostly: Id, QmlUri, Element, IsAttached, Attached event,
  applied geometry); apps can send one-shot `SendCommand`s only from a subclass (protected).
- Container semantics: the net11 `ViewHandler<,>.SetupContainer/RemoveContainer` are empty, `NeedsContainer` =
  `Clip != null || Shadow != null`, so `HasContainer` flips to true while `ContainerView` stays null; harmless for the
  Sailfish host (clip/shadow ride the host's layer effect) but visible to app code and to `PlatformEffect.Container`.
- Effects do work: `Element.OnHandlerChangedCore` sets `EffectControlProvider`, `RegisterEffect` resolves through
  `EffectsFactory` (`ConfigureEffects`), and the net11 `PlatformEffect.Control` is `Handler.PlatformView` (the
  `NativeElementHost`). The Compatibility package is not referenced; apps that bring a `PlatformEffect` for Sailfish
  get it attached (documented at `docs/porting-existing-apps.md:181-183`, `:420-432`).

## Element/handler coverage table

Legend — State: dedicated / fallback-ok / fallback-weak / missing / n-a. Severity: high / med / low / none.

| MAUI 11 element | Official handler | Sailfish handler / fallback | State | Evidence | Severity | Fix sketch |
|---|---|---|---|---|---|---|
| Label | LabelHandler | SailfishLabelHandler | dedicated | Factory:275; SailfishTextHandlers.cs:8-40 | none | — |
| Label.FormattedText / Span | (LabelHandler "FormattedText"… ) | spans flattened to Qt rich-text HTML; per-span `CharacterSpacing`/`LineHeight` unsupported; **Span.GestureRecognizers not routed** (only view-level `GestureRecognizers`, QtHostInput.cs:733) | fallback-weak | AdapterSnapshots.Label.cs:17-33, 92-128 (comment "Per-span CharacterSpacing/LineHeight are not supported") | med | emit `<a href="span:i">` for spans with tap recognizers; handle `linkActivated` in Label.qml and route to `Span.GestureRecognizers` in AdapterEventRouter |
| Button | ButtonHandler | SailfishButtonHandler | dedicated | Factory:276 | none | — |
| ImageButton | ImageButtonHandler | SailfishImageHandler (shared; `mauiTappable`, frame keys) | dedicated | Factory:264; SailfishDrawingHandlers.cs:8-20; QtHostImages.cs:54-68; AdapterEventRouter.cs:60-63 | none | — |
| Entry / Editor / SearchBar | Entry/Editor/SearchBarHandler | SailfishEntry/Editor/SearchBarHandler (+ transient focus/caret) | dedicated | Factory:251-253; SailfishTextHandlers.cs:76-218 | none | — |
| CheckBox, Switch, Slider, Stepper, ProgressBar, ActivityIndicator, RadioButton | respective handlers | SailfishValueHandlers.cs | dedicated | Factory:250,254-258,262 | none | — |
| Picker (incl. `ItemDisplayBinding`) | PickerHandler | SailfishPickerHandler; `ItemDisplayBinding` with a simple path resolved managed-side, else `ToString()` | dedicated | SailfishPickerHandlers.cs:8-35; AdapterSnapshots.Values.cs:61,141-165 | low | multi-segment / converter bindings fall back to ToString (documented in code) |
| DatePicker / TimePicker (dialogs) | DatePicker/TimePickerHandler | SailfishDatePicker/TimePickerHandler, `IsOpen` key, Silica dialogs in `qml/controls/DatePicker.qml`, `TimePicker.qml` | dedicated | SailfishPickerHandlers.cs:40-95 | none | — |
| Image | ImageHandler | SailfishImageHandler | dedicated | Factory:263 | none | — |
| Border | BorderHandler | SailfishBorderHandler | dedicated | Factory:265 | none | — |
| Frame (obsolete) | — (Compatibility FrameRenderer, platform-only) | SailfishBorderHandler (FrameProps) | dedicated | Factory:267; SailfishDrawingHandlers.cs:100-104,139 | none | — |
| BoxView | BoxViewHandler (Controls) : ShapeViewHandler | SailfishShapeHandler | dedicated | Factory:269 | none | — |
| Ellipse, Line, Path, Polygon, Polyline, Rectangle, RoundRectangle | ShapeViewHandler + Controls Line/Path/Polygon/Polyline/Rectangle/RoundRectangleHandler (extra keys X1,Y1,X2,Y2 / Shape,Data,RenderTransform / Points,FillRule / RadiusX,RadiusY / CornerRadius) | SailfishShapeHandler on `Shape` base; `OwnsProperty` = every non-visual-state property re-pushes, so the per-shape keys are covered without being listed; **not covered by HandlerParityTests** (only BoxView vs ShapeViewHandler) | dedicated | Factory:270; SailfishDrawingHandlers.cs:122-128; QtHostShapes.cs:29-56,198-201 | low | add the six Controls shape handlers to `HandlerParityTests.Pairs` (keys resolve via `OwnsProperty`) |
| GraphicsView | GraphicsViewHandler | SailfishGraphicsHandler (+ `Invalidate` command) | dedicated | Factory:271; SailfishDrawingHandlers.cs:154-186 | none | — |
| ContentView | ContentViewHandler | SailfishContentViewHandler | dedicated | Factory:274 | none | — |
| TemplatedView (non-ContentView subclass), ControlTemplate | ContentViewHandler via `IContentView` registration (stock → skipped) | FallbackRow → **SailfishLayoutHandler** (TemplatedView : Compatibility.Layout : ILayout); `ControlTemplate` change → `RequestSubtree` | fallback-ok | Ctl_TemplatedView.cs:13; Factory:198-201; SailfishSnapshotHandler.cs:86-87 | low | add `Row<TemplatedView, SailfishContentViewHandler>` for clarity (ContentView row stays first) |
| ContentPresenter | ContentViewHandler via `IContentView` (stock) | FallbackRow → SailfishLayoutHandler; `Compatibility.Layout` never invokes `ILayoutHandler.Add/Remove` (no `Handler.Invoke` in Ctl_Compatibility_Layout.cs), but the owning ContentView's `Content`/`ControlTemplate` key re-diffs the subtree, so the common template case works | fallback-ok | Ctl_ContentPresenter.cs:14; Compatibility.Layout decompile (only `LayoutChanged?.Invoke`) | low | none needed; note in docs |
| Grid | LayoutHandler | SailfishGridHandler | dedicated | Factory:272; SailfishLayoutHandlers.cs:99-120 | none | — |
| VerticalStackLayout / HorizontalStackLayout / StackLayout | LayoutHandler | SailfishStackHandler (`StackBase`) | dedicated | Factory:273; SailfishLayoutHandlers.cs:123-144 | none | — |
| AbsoluteLayout, FlexLayout, custom `Layout` subclasses | LayoutHandler | FallbackRow → SailfishLayoutHandler; MAUI layout manager runs (`CrossPlatformMeasure` SailfishMeasure.cs:33, `CrossPlatformArrange` SailfishHandlerCore.cs:100-104); layout commands answered (`LayoutCommands`, SailfishLayoutHandlers.cs:30-42) | fallback-ok | Factory:194,198-201 | none | — |
| Compatibility.Grid/StackLayout/AbsoluteLayout/RelativeLayout/FlexLayout (`[Obsolete]`) | LayoutHandler via `Compatibility.Layout : ILayout`? (no stock registration for the Compatibility base) | SailfishLayoutHandler; **runtime `Children.Add/Remove` raise no `ILayoutHandler` command** → new hosts appear only at the next poll (page mapper key, navigation or 2 s heartbeat); `RequestLayout` (InvalidateMeasure) does not reconcile hosts (QtHostPageRenderer.cs:674-688) | fallback-weak | Ctl_Compatibility_Layout.cs:15 `[Obsolete]`; grep: no `Invoke(` to a handler | low (obsolete API) | in `SailfishLayoutHandler.ConnectHandler`, subscribe `ILayoutController`/`LayoutChanged` for `Compatibility.Layout` and call `RequestSubtree` |
| ScrollView | ScrollViewHandler | SailfishScrollViewHandler | dedicated | Factory:245 | none | — |
| RefreshView | RefreshViewHandler | SailfishContentViewHandler (`AdapterUri` null for RefreshView) + renderer arms the wrapped scroll surface | dedicated (split) | SailfishLayoutHandlers.cs:165-166; QtHostPageRenderer.Walk.cs:182-186; QtHostRefreshBinding.cs | none | — |
| CollectionView | Items.CollectionViewHandler (chain ItemsView→Structured→Selectable→Groupable→Reorderable; chain adds `IsGrouped`, `CanReorderItems`) | SailfishListViewHandler via `Row<ItemsView,…>` + QtHostCollectionBridge; follows `QtHostCollectionBridge.ViewProperties` (ItemsSource, SelectionMode, SelectedItem(s), ItemsLayout, Header, Footer, EmptyView, ItemTemplate, IsGrouped, Group*Template, scroll-bar visibility, Carousel keys). **`CanReorderItems` not in the list; not covered by HandlerParityTests** | dedicated | Factory:246; SailfishCompositeHandlers.cs:145-213; QtHostCollectionBridge.cs:681-691 | low | add CollectionView/CarouselView to `HandlerParityTests.Pairs` (official `Mapper` is `PropertyMapper<CollectionView, CollectionViewHandler>`); decide reorder = n/a or implement |
| CarouselView | Items.CarouselViewHandler | same handler; `carousel-view` (PathView) adapter when `Loop` + horizontal, else `list-view` | dedicated | QtHostCollectionBridge.cs:261-264; QtHostListAdapter.cs:73-105 | none | — |
| IndicatorView | IndicatorViewHandler | SailfishIndicatorViewHandler (dots adapter or template walk) | dedicated | Factory:247 | none | — |
| **ListView (legacy, `ItemsView<Cell>`)** | none in net11 (Compatibility `ListViewRenderer`, platform-only) | **no row matches** (`ListView : ItemsView<Cell>` is not `ItemsView`); Resolve → interfaces (none registered) → `SailfishContainerHandler`; renderer `MapElement` has no case; `GetVisualChildren()` returns `_visualChildren`, filled only by `SetupContent` which no renderer calls → **empty container + one `[WARN] no Sailfish adapter for Microsoft.Maui.Controls.ListView`** | missing | Ctl_ListView.cs:18,445-447,625-635; Factory:246,198-201; Walk.cs:122-129,202-206; only mention `SailfishViewHandler.cs:239`; `docs/silica-parity.md:46` claims "native" | high | either (a) a `SailfishLegacyListViewHandler : SailfishSnapshotHandler<IView>` that drives `TemplatedItemsList` (`IListViewController.TemplatedItems`, `GetOrCreateContent`) onto the existing `list-view` adapter with `ViewCell.View`/`TextCell`/`ImageCell`/`SwitchCell`/`EntryCell` templates, or (b) document "not supported, use CollectionView" in `porting-existing-apps.md` and fix `silica-parity.md:46` |
| **TableView** + TableRoot/TableSection | none in net11 (Compatibility, platform-only) | `TableView : View` → `SailfishContainerHandler`; cells are `Element`s, never hosted → empty container + warning | missing | Ctl_TableView.cs:18; Factory:198-201; Walk.cs:202-206 | med | same decision as ListView; a cheap path is a managed adapter that maps TableSection/TextCell/SwitchCell/EntryCell onto the `list-view` adapter with Silica `SectionHeader` rows |
| **Cell, TextCell, ImageCell, SwitchCell, EntryCell, ViewCell** | none in net11 | `Cell : Element` (not IView) → if any code asks the factory: `Resolve` returns `(null,null)` → `GetHandler` throws `InvalidOperationException`; `QtHostLayout.AttachHandlersCore` catches and gives `NullElementHandler` | missing | Ctl_Cell.cs:13; Factory:37-41,88; QtHostLayout.cs:40-52 | (with ListView/TableView) | as above |
| SwipeView | SwipeViewHandler | SailfishSwipeViewHandler | dedicated | Factory:249; SailfishCompositeHandlers.cs:95-143 | none | — |
| **SwipeItem** (MAUI 11: `IconColor`, `TextColor`) | SwipeItemMenuItemHandler (Mapper: Visibility, Background, Text, TextColor, CharacterSpacing, Font, Source, IconColor) | no handler; `SwipeItemsJson` emits `text`, `icon`, `bg` only; QML reads `modelData.fg` (never emitted) for the label colour and never tints the icon; Font/CharacterSpacing ignored; **Top/BottomItems not rendered (warned)** | fallback-weak | AdapterSnapshots.Containers.cs:239-241,269-290; qml/controls/SwipeView.qml:176,196; Ctl_SwipeItem.cs:19-22 | med | emit `fg` from `TextColor`, add `iconColor` (QML `Image` → `ColorOverlay`/`icon.color`), `fontSize`/`fontFamily`; add a `SwipeItem` PropertyChanged watch in the SwipeView handler so a runtime `TextColor` change re-pushes (`SwipeItems` is an ObservableCollection of elements that are not views) |
| SwipeItemView | SwipeItemViewHandler (Content, Visibility) | content not hosted; first opaque background, Image and Label found are shown as a Silica action | fallback-weak | AdapterSnapshots.Containers.cs:293-325 (comment) | low | documented approximation; acceptable for Silica idiom |
| WebView | WebViewHandler | SailfishWebViewHandler (Gecko) | dedicated | Factory:248; SailfishCompositeHandlers.cs:8-93 | none | — |
| **HybridWebView** (MAUI 9+) | HybridWebViewHandler (commands EvaluateJavaScriptAsync, InvokeJavaScriptAsync, SendRawMessage; `IHybridWebViewTaskManager` scoped service) | `HybridWebView : View` (not WebView) → `SailfishContainerHandler` → **blank container, warning once; `InvokeJavaScriptAsync` tasks never complete** | missing | Ctl_HybridWebView.cs:14; Factory:198-201; grep: no "HybridWebView" in src | med | `SailfishHybridWebViewHandler : SailfishSnapshotHandler<IHybridWebView>` on the Gecko adapter: serve `HybridRoot` from the app bundle via a local file URL, inject the `HybridWebView.js` bridge, route `window.external.sendMessage` → `IHybridWebView.RawMessageReceived`/`MessageReceived`; or document as unsupported next to BlazorWebView |
| BlazorWebView | (separate package) | out of scope by decision (W10.12) | n-a | docs/porting-existing-apps.md:313 | none | — |
| Microsoft.Maui.Controls.Maps.Map | (separate package) | no Sailfish implementation; plain-net handler would throw → `BuiltInFallback` null → empty container | n-a (documented nowhere) | QtHostLayout.cs:55-75 | low | add a line to "Not supported yet" |
| CommunityToolkit MediaElement | (toolkit) | not supported (toolkit in cache: `communitytoolkit.maui.mediaelement`); W10.11 "no toolkit add-on" | n-a | docs/porting-existing-apps.md:297-301 | low | add MediaElement to the toolkit paragraph |
| Window | WindowHandler | SailfishWindowHandler (`Content` only) | dedicated (partial) | SailfishApplicationHandlers.cs:51-80 | med | see Window table |
| Window.TitleBar / `TitleBar` | (desktop) no TitleBarHandler in net11 | never attached (`TitleBar` key not mapped); `TitleBar : TemplatedView` would resolve to SailfishLayoutHandler if attached | n-a | Ctl_TitleBar.cs:18 | none | — |
| Window.StatusBarTheme (new on Controls.Window) | not in net11 `WindowHandler.Mapper` | not mapped; lipstick owns the status bar | n-a | Ctl_Window.cs:120-121,326-336; WindowHandler.Mapper keys | none | — |
| Application | ApplicationHandler | SailfishApplicationHandler | dedicated (3/4 commands) | Hosting/AppHostBuilderExtensions.cs:49 | low | add `ActivateWindow` (no-op or `QtHostRuntime` raise/activate) |
| Page / ContentPage | PageHandler (Mapper: Title + ContentView chain; ContentPage adds `HideSoftInputOnTapped`) | SailfishPageHandler (Title, Background*, BackgroundImageSource, Content, IsBusy, container keys). **`HideSoftInputOnTapped` not mapped anywhere** | dedicated | SailfishPageHandler.cs:15-33; grep: no "HideSoftInput" in src | low | map `HideSoftInputOnTapped` → on a page tap outside a text adapter, `FocusHost(host,false)` (the renderer already knows the focused host) |
| NavigationPage | NavigationViewHandler (no Mapper keys; `RequestNavigation`) + Toolbar | SailfishNavigationViewHandler (`RequestNavigation` → `WhenNavigationSettled`); `HasBackButton` read live | dedicated | SailfishNavigationViewHandler.cs:15-19,51-62; QtHostPageRenderer.cs:1226-1231 | none | — |
| Toolbar / NavigationPage bar (`BarBackgroundColor`, `BarTextColor`, `TitleView`, `TitleIcon`, `IconColor`, `BackButtonTitle`) | ToolbarHandler (Mapper: Title; platform builds add the rest) | no Toolbar handler is ever created (Window mapper has no `Toolbar` key); Silica page header shows `Page.Title`; `TitleView`/bar colours/`IconColor` ignored (grep: none in QtHost) | n-a / fallback-ok | Interactions.cs:24-63; grep `TitleView|BarBackground` → nothing | low | `TitleView` could be hosted as the page header's content item (Silica `PageHeader` allows a custom item); colours are Silica-themed on purpose |
| Page.ToolbarItems / ToolbarItem | (via Toolbar) | renderer: primary → `pull-down-menu`, Secondary → `push-up-menu`; `Text`, `IsEnabled`, `Order`, `Priority` watched; **`IconImageSource` ignored → icon-only items are blank entries** | fallback-weak | Interactions.cs:24-36,86-110,220-231 | med | fall back to `AutomationId`/`SemanticProperties.Description`/the icon file name when `Text` is empty, or render a Silica `IconButton` row in the pulley |
| TabbedPage (`BarBackground`, `BarBackgroundColor`, `BarTextColor`, `(Un)SelectedTabColor`, `ItemsSource`, `ItemTemplate`, `SelectedItem`, `CurrentPage`) | TabbedViewHandler (+ TabbedPage.RemapForControls keys) | SailfishTabbedPageHandler: `CurrentPage` key + `CurrentPageChanged`; tabs read from `Children`; colours Silica-themed (n-a); **`Children`/`ItemsSource` changes (`PagesChanged`) do not request a poll** → strip updates at next poll | dedicated | SailfishPageContainerHandlers.cs:84-152; Ctl_TabbedPage.cs:307-315; grep `PagesChanged` → nothing | low | watch `MultiPage.PagesChanged` in `Watch()` → `RequestPoll` |
| FlyoutPage (`Flyout`, `Detail`, `IsPresented`, `FlyoutBehavior`, `FlyoutWidth`, `IsGestureEnabled`, `FlyoutLayoutBehavior`) | FlyoutViewHandler | SailfishFlyoutPageHandler: Flyout/Detail/IsPresented; flyout as a pulley entry + native push; width/gesture/behaviour n-a by design (architecture.md idiom table) | dedicated | SailfishPageContainerHandlers.cs:158-230 | none | — |
| Shell, ShellItem, ShellSection, ShellContent, FlyoutItem, Tab, MenuItem (flyout), SearchHandler, TitleView, FlyoutHeader/Footer | ShellHandler + item handlers (platform-only; absent in net11) | SailfishShellHandler: CurrentItem/CurrentState keys, Navigating/Navigated, sections/contents as tabs, flyout grouping as pulley entries, `BackButtonBehavior` read live. **`SearchHandler`, `TitleView`, `FlyoutHeader/Footer`, `NavBarIsVisible`, `TabBarIsVisible` not consumed (grep: none)**; `Items`/`FlyoutItems` changes (Controls raises `UpdateValue("Items"/"FlyoutItems")`) not mapped → next poll | dedicated (partial) | SailfishPageContainerHandlers.cs:233-424; Ctl_Shell.cs:2080-2088,2600,2704 | low-med | map `Items`, `FlyoutItems`, `FlyoutBehavior` → `MapModelPage`; `SearchHandler` → Silica `SearchField` header item is a natural idiom; `TabBarIsVisible=false` should hide the tab strip |
| MenuBar / MenuBarItem | MenuBarHandler / MenuBarItemHandler (commands Add/Remove/Clear/Insert) | `NullElementHandler` (short-circuit) — no desktop menu bar on Sailfish; `Window.MenuBar` never mapped | n-a | Factory:67; NullElementHandler.cs:12-19 | none | optionally surface `MenuBarItem`s as extra pulley entries |
| MenuFlyout / ContextFlyout (`FlyoutBase.ContextFlyout`) | MenuFlyoutHandler (Add/Remove/Clear/Insert) | `NullElementHandler`; renderer registry → Silica `ContextMenu` on long-press, picks → `MenuFlyoutItem.Clicked/Command` | fallback-ok | Factory:67; Walk.cs:88-92; Interactions.cs:249-290; QtHostInput.cs:244-249 | none | — |
| MenuFlyoutItem | MenuFlyoutItemHandler | via renderer (text, IsEnabled); `IconImageSource`, `KeyboardAccelerators` ignored (fine on touch) | fallback-ok | Interactions.cs:233-245 | none | — |
| MenuFlyoutSubItem | MenuFlyoutSubItemHandler | **flattened**: shown as one item (`as MenuItem` succeeds), its children are lost; tapping fires the sub item itself | fallback-weak | Interactions.cs:233-245; ContextMenu.qml:27-45 | low | render sub items inline with an indent/`SectionHeader` or skip them with their children appended |
| MenuFlyoutSeparator | MenuFlyoutSeparatorHandler | `flyout[i] as MenuItem` is null → `{text:"",enabled:true}` → **an empty tappable row** | fallback-weak | Interactions.cs:233-245; ContextMenu.qml:44-45 | low | skip separators (Silica ContextMenu has none) or emit `{separator:true}` and build a `Separator` |
| Library views with their own adapter / stub handlers (SkiaSharp, Syncfusion SfView, UraniumUI) | app registration | `ReplaceLibraryHandler`, `BuiltInFallback`, `SailfishDrawnViewHandler` | dedicated | Factory:135-168,220-227; QtHostLayout.cs:55-75 | none | — |

## Command mapper table

Official keys from the decompiled `CommandMapper` initialisers (scratchpad `decomp/*.cs`). "Chained" = reached through `new CommandMapper(ViewHandler.ViewCommandMapper)` without a Sailfish override.

| Handler | Command key | Sailfish mapped? | Evidence |
|---|---|---|---|
| ViewHandler.ViewCommandMapper | InvalidateMeasure | yes (RequestLayout + base) | SailfishViewHandler.cs:196-201; SailfishHandlerCore.cs:91-95 |
| | Frame | chained (official `MapFrame` is empty in net11) | decomp/ViewHandler.cs:565-567 |
| | ZIndex | chained (`MapZIndex` → parent layout `UpdateZIndex` → `LayoutCommands`) | decomp/ViewHandler.cs:575-581; SailfishLayoutHandlers.cs:40 |
| | Focus / Unfocus | yes (native focus or IsFocused write-back; FocusRequest completed) | SailfishViewHandler.cs:198-199; SailfishHandlerCore.cs:72-89; test HandlerFixTests.cs:162 |
| LayoutHandler | Add, Insert, Remove, Update, UpdateZIndex, Clear | yes — all six → `RequestSubtree` | SailfishLayoutHandlers.cs:30-42 (Layout, Grid, Stack handlers) |
| ScrollViewHandler | RequestScrollTo | **not in the Sailfish CommandMapper**; answered via `IScrollViewController.ScrollToRequested` (always `SendScrollFinished`); an app's `AppendToMapping(RequestScrollTo)` still runs because Controls also invokes the command (Ctl_ScrollView.cs:588,612) | SailfishScrollViewHandler.cs:24,61-105; W1CorrectnessTests.cs:140-152 |
| WebViewHandler | GoBack, GoForward, Reload, Eval, EvaluateJavaScriptAsync | yes 5/5 (pending JS completed with null on disconnect) | SailfishCompositeHandlers.cs:18-30,66-93 |
| HybridWebViewHandler | EvaluateJavaScriptAsync, InvokeJavaScriptAsync, SendRawMessage | **no** (no handler; awaiting tasks never complete) | Factory:198-201; decomp/HybridWebViewHandler.cs |
| SwipeViewHandler | RequestOpen, RequestClose | yes 2/2 (`open` command, side left/right; top/bottom map to right) | SailfishCompositeHandlers.cs:115-119,135-143; test HandlerFixTests.cs:680 |
| GraphicsViewHandler | Invalidate | yes (re-records the drawable) | SailfishDrawingHandlers.cs:160-164 |
| NavigationViewHandler | RequestNavigation | yes (`NavigationFinished` after the pageStack settles) | SailfishNavigationViewHandler.cs:15-19,51-62; RendererTests.cs:37 |
| ApplicationHandler | Terminate | yes (`QtHostRuntime.Quit`) | SailfishApplicationHandlers.cs:17,29-30 |
| | OpenWindow | yes (warn + drop, single window) | :18,34-36 |
| | CloseWindow | yes (quits when it is the app's window) | :19,39-44 |
| | **ActivateWindow** (new in MAUI 11) | **no** — the Sailfish mapper chains from `ElementCommandMapper`, not `ApplicationHandler.CommandMapper`, so the key is silently ignored | decomp/ApplicationHandler.cs:16-22 vs SailfishApplicationHandlers.cs:15-20 |
| WindowHandler | RequestDisplayDensity | yes (`SailfishDisplay.Density`) | SailfishApplicationHandlers.cs:57-63 |
| MenuBarHandler / MenuBarItemHandler / MenuFlyoutHandler / MenuFlyoutSubItemHandler | Add, Remove, Clear, Insert | no-op by design (`NullElementHandler` has no CommandMapper; `ElementHandler.Invoke` null-checks it) | NullElementHandler.cs:12-19; decomp/ElementHandler.cs:101-105. Note: a `ContextFlyout` whose items change after the page rendered is re-read only at the next poll (registry rebuilt in `Walk`) |
| PageHandler, ContentViewHandler, RefreshViewHandler, FlyoutViewHandler, TabbedViewHandler, ToolbarHandler, Label/Entry/Editor/SearchBar/Picker/DatePicker/TimePicker/Button/Image/ImageButton/Border/ShapeView/IndicatorView/Switch/CheckBox/Slider/Stepper/ProgressBar/ActivityIndicator/RadioButton handlers, SwipeItemMenuItem/SwipeItemView handlers, Items.*ViewHandler chain | (no CommandMapper keys beyond the View ones in net11) | n/a — Sailfish mappers chain from `SailfishViewMapper.CommandMapper` | decomp key extraction (empty) |
| CollectionView `ScrollTo` | not a command (Controls event `ScrollToRequested`) | yes, via `QtHostListAdapter.Scroll.cs`; test `ScrollTo_with_a_group_index_lands_in_that_group` | HandlerFixTests.cs:547 |

Totals: official keys that are meaningful here = 35 (5 View + 6 Layout + 1 Scroll + 5 WebView + 3 Hybrid + 2 Swipe + 1 Graphics + 1 Nav + 4 App + 1 Window + 4×4 Menu counted as 4 families + 1… see table); answered 30, missing 4 (HybridWebView ×3, ActivateWindow ×1), by-design no-op: Menu* Add/Remove/Clear/Insert.

## Window/Application mapper table

MAUI 11 net11.0: `WindowHandler.Mapper` = ElementMapper + Title, Content, X, Y, Width, Height (decomp/WindowHandler.cs:7-15);
`Window.RemapForControls()` is EMPTY in this build (decomp/Ctl_Window.cs) — the `MaximumWidth/Height`, `MinimumWidth/Height`,
`FlowDirection`, `TitleBar`, `MenuBar`, `Toolbar`, `StatusBarTheme` mappings exist only in the platform heads (unverified
here; `IWindow` exposes Min/Max/FlowDirection and Controls.Window raises `UpdateValue("FlowDirection")` at
Ctl_Window.cs:979 and `UpdateValue("Title")` at :1033). `ApplicationHandler.Mapper` = ElementMapper only;
`Application.RemapForControls()` is empty.

| Handler | Key | Sailfish | Meaningful on Sailfish? | Evidence / fix |
|---|---|---|---|---|
| Window | Content | mapped (`MapContent`: attaches the root page handler, `RequestPoll`) | yes | SailfishApplicationHandlers.cs:54,74-79 |
| Window | Title | **not mapped** | yes — the cover and the task switcher show the app's .desktop title (`meta.Title`, SailfishMauiApplication.cs:220); `Window.Title`/`Page.Title` could set `SailfishCover` title and the `ApplicationWindow` title | add `[nameof(IWindow.Title)] = (h,w) => SailfishCover.SetContent(w.Title, …)` or a shim `window.title` push |
| Window | X, Y, Width, Height | not mapped | no (lipstick sizes the window; `FrameChanged` is reported the other way) | n-a |
| Window | MinimumWidth/Height, MaximumWidth/Height | not mapped (not in net11 Mapper either) | no | n-a |
| Window | FlowDirection | **not mapped**, and `QtHostVisualState.IsRightToLeft` walks `Parent` but tests `e is VisualElement` — `Window` is not one, so a Page with `MatchParent` resolves to LTR even when `Window.FlowDirection = RightToLeft` | yes (RTL root) | QtHostVisualState.cs:220-228; QtHostPageRenderer.Layout.cs:334-336. Fix: treat `Window`/`Application.FlowDirection` (`IWindow.FlowDirection`) as the root in `IsRightToLeft`, and map the key → `RequestLayout` |
| Window | TitleBar, MenuBar, Toolbar (platform-only keys) | not mapped | TitleBar/MenuBar no (mobile); Toolbar: the Silica header is page-owned | n-a |
| Window | StatusBarTheme (Controls property) | not mapped | no — lipstick draws the status bar per ambience | n-a |
| Window (commands) | RequestDisplayDensity | mapped | yes | :57-63 |
| Application | (no property keys) | `Mapper = new(ElementMapper)` | — | :13 |
| Application (commands) | Terminate, OpenWindow, CloseWindow | mapped | yes | :15-20 |
| Application (commands) | ActivateWindow | **not mapped** (silently ignored) | low — single window; a no-op or raising the window via the shim would be correct | add the key |

## Extensibility conventions findings

Checked for all 31 public handler types listed in `tests/Linux.SailfishOS.Tests/PublicSurface.txt:15-59`.

1. **Public static `Mapper` + `CommandMapper`**: present on every public view/page/container handler (Label, Button,
   Entry, Editor, SearchBar, Switch, CheckBox, Slider, ProgressBar, ActivityIndicator, Stepper, RadioButton,
   IndicatorView, Picker, DatePicker, TimePicker, Image, Border, Shape, Graphics, Layout, Grid, Stack, ContentView,
   Container, ScrollView, WebView, SwipeView, ListView, Page, Tabbed, Flyout, Shell, Window, Application;
   `NullViewHandler` has `Mapper` only). Exception: **`SailfishNavigationViewHandler`** publishes
   `NavigationCommandMapper` (`SailfishNavigationViewHandler.cs:15-19`) and inherits the static name
   `SailfishPageHandler.CommandMapper` (`SailfishPageHandler.cs:34`), which the navigation handler does not use
   (`:26-28` passes `NavigationCommandMapper`). `SailfishNavigationViewHandler.CommandMapper.AppendToMapping(
   nameof(IStackNavigation.RequestNavigation), …)` compiles and never fires. Fix: rename to
   `public static new readonly CommandMapper<IStackNavigationView, SailfishNavigationViewHandler> CommandMapper`
   (as the Tabbed/Flyout/Shell handlers do with `new`), keep `NavigationCommandMapper` as an alias.
2. **`(IPropertyMapper?, CommandMapper?)` constructor**: present on every public view/page handler
   (e.g. SailfishTextHandlers.cs:31, SailfishPageHandler.cs:40, SailfishPageContainerHandlers.cs:95,169,245,
   SailfishCompositeHandlers.cs:37,125,198, SailfishScrollViewHandler.cs:33). Missing on
   **`SailfishApplicationHandler`** (`:22`) and **`SailfishWindowHandler`** (`:66`) — MAUI's ApplicationHandler/
   WindowHandler offer `(mapper)` and `(mapper, commandMapper)` (decomp/WindowHandler.cs:24-31). `NullElementHandler`
   has none (fine: internal fallback). Add the two constructors.
3. **`PlatformView` type**: `NativeElementHost` (public, `Platform/QtHost/NativeElementHost.cs:14-109`): `Id`,
   `QmlUri`, `IsBound`, `Element`, `NativeHandle`, `AppliedProperties`, `IsApplied(name,json)`, `IsAttached`,
   `Attached` event, `MauiLogicalBounds`, `AppliedGeometry`, `AppliedVisible`. It has no public setter/`SetProperty`;
   an `AppendToMapping` action can observe, hook `Attached`, or reach the QML object through `QtHostRuntime.Eval`
   (public; `Invoke` is internal per W10.7). Sending adapter commands (`SendCommand`) and pushing props (`PushProps`,
   `PushTransient`) are `protected` on `SailfishViewHandler<T>` (`SailfishViewHandler.cs:148-174`) — available to a
   subclass handler, not to a mapping lambda. Documented in `docs/custom-controls.md` ("Customizing a built-in control").
4. **Per-property `MapXxx`**: none in the MAUI sense (`MapText(ILabelHandler, ILabel)`). What exists:
   `SailfishSnapshotHandler<T>.MapSnapshot` / `MapSnapshotAndViewState` (`SailfishSnapshotHandler.cs:63-75`),
   `SailfishViewMapper.MapViewState/MapGeometry/MapTransform/MapExcludedWithChildren` (`SailfishViewHandler.cs:216-262`),
   `SailfishPageHandler.MapModelPage`, `SailfishListViewHandler.MapItemsProperty`, `SailfishWindowHandler.MapContent`,
   `SailfishApplicationHandler.MapTerminate/MapOpenWindow/MapCloseWindow`. Decision W10.6b (document, don't add) is
   implemented: `docs/custom-controls.md` states both consequences. Effects, verified in code:
   - `AppendToMapping(key)` / `PrependToMapping(key)` run after/before the snapshot push for that key — side effects
     work; a value written to the adapter (via `Eval`) is overwritten by the next push of ANY family key
     (`SailfishSnapshotHandler.cs:7-13` docstring, `:84-86` PushSnapshot) and by the connect batch
     (`SailfishViewHandler.cs:89-100`).
   - `ModifyMapping(key, (h,v,action) => …)` that skips `action` does not keep the property from the adapter: the
     family snapshot carries it on the next push of another key (`docs/custom-controls.md` "Replacing or skipping a
     key with ModifyMapping does not keep that value from the adapter").
   - A subclass mapper chained from a built-in one keeps the family (`SailfishHandlerCore.OwnedKeys`,
     `SailfishHandlerCore.cs:16-43`; test `HandlerFixTests.cs:48`).
   - Keys that are both owned and generic (Background on Entry) run snapshot then view state
     (`SailfishSnapshotHandler.cs:50-56`).
5. **`ConnectHandler`/`DisconnectHandler` symmetry**: base `DisconnectHandler` → `Session.OnHandlerDisconnected`
   (`SailfishViewHandler.cs:73-78`). ScrollView subscribes in Connect, unsubscribes in Disconnect
   (`SailfishScrollViewHandler.cs:66-84`). WebView warns in Connect, completes pending JS in Disconnect
   (`SailfishCompositeHandlers.cs:43-51,81-93`). Tabbed/Flyout/Shell watch in `SetVirtualView` and unwatch in
   `DisconnectHandler` (`SailfishPageContainerHandlers.cs:105-127,182-197,264-294`, W1.11). ListView handler owns
   nothing (adapter looked up through the session, `:173-178`). `SailfishWindowHandler.MapContent` attaches the new
   root's handler but does not disconnect the old root's on a `Content` swap (MAUI's `Window` does it itself via
   `Page.Handler` lifecycle — unverified here).
6. **`IPlatformViewHandler` / `ContainerView` / `HasContainer` / `NeedsContainer`**: `IPlatformViewHandler` does not
   exist in the net11 core (only `IElementHandler`, `IViewHandler`). `ViewHandler<,>.SetupContainer/RemoveContainer`
   are empty in the net11 build (decomp/ViewHandler2.cs:97-103), `NeedsContainer()` = `Clip != null || Shadow != null`
   (decomp/Platform_ViewExtensions.cs), `MapContainerView` sets `HasContainer = NeedsContainer` and re-maps
   `Visibility` (decomp/ViewHandler.cs:531-543). So on Sailfish a view with Clip/Shadow reports `HasContainer == true`
   with `ContainerView == null`; the host's layer effect does the clipping/shadow (parity-plan.md:78), the parity test
   marks `ContainerView` n/a. No Sailfish code touches these members (grep: none). Consequence for apps:
   `PlatformEffect.Container` = `ContainerView ?? PlatformView` → the `NativeElementHost` (decomp PlatformEffect:13-22);
   handler code that branches on `HasContainer` gets a misleading true. Low; a `SailfishViewHandler.NeedsContainer => false`
   override would make it honest.
7. **Effects / `Microsoft.Maui.Controls.Compatibility`**: the Compatibility package is not referenced
   (`Linux.SailfishOS.csproj:24-26`) and is not a dependency of the Controls meta-package; `IsStock`
   (`SailfishHandlersFactory.cs:118-128`) would ignore its handlers anyway (assembly prefix `Microsoft.Maui.`). Effects
   need no Compatibility: `Element.OnHandlerChangedCore` sets `EffectControlProvider = this` (decomp/Ctl_Element.cs:1410),
   `IEffectControlProvider.RegisterEffect` resolves a `PlatformEffect` through `EffectsFactory` (`:1466-1480`), which
   `MauiAppBuilder.ConfigureEffects` registers (decomp AppHostBuilderExtensions.cs:183-191). A `RoutingEffect` whose
   platform half is missing on this TFM attaches nothing (documented `docs/porting-existing-apps.md:181-183`, with a
   worked Sharpnado example `:420-432`). No Sailfish gap beyond documentation.

## Open questions / unverified

- Legacy `ListView`/`TableView`: concluded from the decompile (`SetupContent` is the only writer of
  `_visualChildren`; no caller in the Sailfish tree) — not executed. A matrix leg with a legacy `ListView` would
  confirm "empty container + warning" (no sample or test in the repo uses `new ListView`/`<ListView`; grep over
  samples/tests/src returned nothing).
- Whether `ItemsView<Cell>`/`TemplatedItemsList` can be driven without a platform renderer
  (`IListViewController.TemplatedItems`, `GetOrCreateContent`) for fix sketch (a) — plausible from the API, not tried.
- `Window.RemapForControls` in the Android/iOS heads adds `FlowDirection`, `TitleBar`, `MenuBar`, min/max — stated from
  MAUI source knowledge; only the (empty) net11 body was decompiled.
- `UseMauiCompatibility()` on a plain-net TFM: the 11.0 package is not in the cache; the 10.0.101 net10.0 build
  exposes `UseMauiCompatibility` but I did not list what it registers there.
- `SailfishWindowHandler.MapContent` and disconnection of the previous root page handler on `Window.Page` swap —
  not traced through Controls.
- The command-key totals count the four Menu* handlers' Add/Remove/Clear/Insert as by-design no-ops, not misses.
- Whether Silica's `ContextMenu`/`PullDownMenu` can show icons (for the icon-only `ToolbarItem` fix) was not checked
  against the Silica QML API.
