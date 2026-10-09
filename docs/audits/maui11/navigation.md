<!-- Static audit of commit af3d782 (branch feature/fixes) against .NET MAUI 11.0.0-rc.1.26451.6, 2026-10-04. Produced by a read-only code review with ilspycmd decompiles; nothing was run on the device. Items marked unverified were not checked on the phone. Summary and work packages: docs/maui11-alignment-plan.md -->

# Navigation semantics audit — maui-sailfish (feature/fixes @ af3d782) vs .NET MAUI 11.0.0-rc.1.26451.6

Static analysis only (no build, no device). File:line references are to the repo at af3d782; MAUI references are to
ilspycmd decompiles of `Microsoft.Maui.Controls.dll` / `Microsoft.Maui.dll` 11.0.0-rc.1.26451.6 (line numbers of the
decompiled type, kept in the scratchpad under `maui-decomp/`). W1–W3 of `docs/architecture-handoff.md` and the
deliberately open items of `docs/parity-plan.md` (tab swipe preview, one window) are not re-reported.

## Summary

- **Push/pop/pop-to-root, modal push/pop, modal NavigationPage, hardware Back, Silica swipe-back, TabbedPage,
  FlyoutPage (flyout as a pushed page, Detail swap), Shell sections/tabs/routes/flyout-as-pulley and the three
  dialogs all work** and are exercised on the device (legs `nav`, `shell`, `containers`, `popup`, `silica`) and in host
  tests (`NativeStackSyncTests`, `ArchitectureAlignmentTests`). `IStackNavigation.NavigationFinished` and
  `IModalNavigationPlatform.Push/PopModalAsync` complete when the Silica pageStack shows the result
  (`QtHostPageRenderer.Navigation.cs:127-165`, `SailfishModalNavigation.cs:26-40`), as the other platforms complete
  after their transition — with a 3 s "completing anyway" safety net that is a contract softening, not a violation.
- **The biggest MAUI-visible gap is the Back contract.** Hardware Back goes `TryPop → ResolveBackTarget → PopAsync /
  PopModalAsync` directly (`QtHostPageRenderer.cs:606-622`, `Navigation.cs:99-112`) instead of
  `IWindow.BackButtonClicked()` (`Window.cs:1038-1043`), so `Page.OnBackButtonPressed` overrides,
  `Shell.BackButtonBehavior.Command`, `NavigationPage/Shell/FlyoutPage.OnBackButtonPressed` never run. The Silica
  swipe-back pops natively first, so a veto there is impossible by construction; a cancelled `Shell.Navigating` or
  `Window.ModalPopping` after a swipe is "repaired" only after the 3 s coordinator timeout
  (`NativeStackCoordinator.cs:112-135,176-178`).
- **Page chrome bypasses MAUI 11's `Toolbar` abstraction.** MAUI builds a `NavigationPageToolbar`/`ShellToolbar`
  (`NavigationPage.cs:986-1002`, `Shell.cs:2040`) that already computes Title, `IsVisible` (HasNavigationBar /
  NavBarIsVisible), `BackButtonVisible`, `BackButtonTitle`, `TitleView`, Priority-sorted `ToolbarItems`
  (`ToolbarTracker.cs:11`). The renderer reads `Page.Title`, `Page.ToolbarItems`, `HasBackButton` and
  `BackButtonBehavior` directly (`QtHostPageRenderer.cs:930-998, 1208-1231`, `Interactions.cs:21-79`), so
  `HasNavigationBar=false`, `NavBarIsVisible=false`, `TitleView`, `Shell.ToolbarItems`, `ToolbarItem.Priority`
  ordering and `ToolbarItem.IconImageSource` are silently ignored. A `SailfishToolbarHandler` on `IToolbar` would
  close most of these in one place.
- **Shell features with no Sailfish mapping, all ignored silently (no warning):** `FlyoutIsPresented` set from code,
  `FlyoutHeader/Footer/Content(+Template)`, `ItemTemplate`, `SearchHandler`, `TabBarIsVisible=false`,
  `NavBarIsVisible=false`, `TitleView`, tab/flyout icons, `BackButtonBehavior.Command/TextOverride/IconOverride`,
  `PresentationMode.NotAnimated`. `SearchHandler` and `FlyoutIsPresented` are the two most likely to break real apps
  (search disappears; a hamburger button does nothing).
- **Dialogs:** one at a time — a second concurrent dialog is answered immediately with the negative result
  (`MauiModelPage.qml:790-791`, `Interactions.cs:394-398`); any navigation requested while a dialog is open is held
  until it closes (`Navigation.cs:297-304`), so a fire-and-forget `DisplayAlertAsync` followed by a push completes
  only via the 3 s timeout; a dialog call from a non-UI thread does not hop and completes with the fallback result
  (`QtHostRuntime.cs:120-137` strict thread, `QtHostAlertSubscription.cs:84-104`); `FlowDirection` and non-numeric
  `Keyboard` arguments are ignored. MAUI 11 marks `DisplayAlert`/`DisplayActionSheet` `[Obsolete]`
  (`Page.cs:350-442`) and `Page.IsBusy` obsolete (`Page.cs:56`); the backend keeps implementing both.
- **MAUI 11 `TabbedPage.BadgeText/BadgeColor/BadgeTextColor`** exist in the dll (`TabbedPage.cs:48-55`) and are
  pushed to the handler as `ItemsSource` updates (`TabbedPage.cs:239-246`); nothing on Sailfish reads them (missing,
  as expected). The Sailfish tab row also does not scroll: N tabs share the width with shrinking text
  (`MauiModelPage.qml:980-994`).
- **Window root swap (`Windows[0].Page = new AppShell()`)** works: MAUI maps it to `IWindow.Content`
  (`Window.cs:634`) → `SailfishWindowHandler.MapContent` attaches the new root's handler
  (`SailfishApplicationHandlers.cs:75-80`); MAUI itself disconnects the old root's handlers (`Window.cs:963-976`);
  the page cache prunes pages the app no longer holds (`PageCache.cs:127-138`); the native depth re-syncs. Host test
  `ArchitectureAlignmentTests.cs:495-520`, device legs `Shell.cs:112,230,268`.
- **Page cache (LRU 4)** is the one memory/behaviour difference from Android's retained handler views: beyond 4
  pages back the QML of a page is rebuilt on return (`QtHostPageRenderer.PageCache.cs:13`,
  `PageCache.cs:136-137`); MAUI pages and their bindings stay alive exactly as on the other platforms. Lifecycle
  counts match MAUI because `Page.SendAppearing/SendDisappearing` are idempotent (`Page.cs:749-756, 790-794`) and
  MAUI core fires them first; the renderer's extra sends only matter for the FlyoutPage flyout (see table).
- Division of labour (handlers describe, renderer executes on one pageStack) is a sound adaptation of the one-window,
  one-pageStack platform (`docs/architecture.md:91-100`); the contract deviations are in the chrome/Toolbar and Back
  paths above, not in the stack sync itself.

## Checklist table

Severity: **blocks** = a common app pattern stops working; **degrades** = works with a visible/behavioural difference;
**cosmetic**; **n/a** = no Sailfish equivalent. "Unverified" = inferred from code, not run.

| Feature | MAUI 11 behaviour (Android/iOS) | Sailfish state | Evidence (file:line) | Severity | Fix sketch |
|---|---|---|---|---|---|
| NavigationPage PushAsync / PopAsync | `SendHandlerUpdateAsync` serialises requests, `RequestNavigation` → handler, completes on `NavigationFinished` | **works**; handler answers `NavigationFinished` once the pageStack settles (3 s cap) | `SailfishNavigationViewHandler.cs:50-61`; `Navigation.cs:127-165`; `NavigationPage.cs:1007-1060, 862-883`; test `NativeStackSyncTests.cs:324-338` | — | — |
| `animated:false` on Push/Pop/PopToRoot/PushModal | `NavigationRequest.Animated`, `IModalNavigationPlatform.PushModalAsync(modal, animated)` | **partial**: flag ignored; single-level ops always animate once activation settled, multi-level always Immediate | `SailfishNavigationViewHandler.cs:52-56` (`request.Animated` unused); `Navigation.cs:441, 488-489`; `SailfishModalNavigation.cs:26-28` | cosmetic | Thread `Animated` from the request/modal call into `NavOperation` and pick `PageStackAction` from it |
| PopToRootAsync | one animated transition to root | **works**, multi-level pops are `Immediate` (no animation) | `Navigation.cs:475-543`; leg E `QtHostDiagnosticsRunner.cs:2708-2726`; test `ArchitectureAlignmentTests.cs:524` | cosmetic | animate the last level only (already structured per level) |
| InsertPageBefore | `SendHandlerUpdateAsync(animated:false)`, same top page, stack grows | **partial (unverified)**: depth +1 → `PushModelPages(1)` pushes a *new* model page for the unchanged top, parks then drops/re-creates its hosts (page re-rendered on the new model page, animated) | `NavigationPage.cs:35-60`; `Navigation.cs:421-473`; `PageCache.cs:155-170` (Reclaim drops entry not on top) | degrades (flash/re-render) | when the MAUI top is unchanged and depth grows, insert a model page *below* (`pageStack.pushAttached`/insert) instead of pushing on top |
| RemovePage (non-current) | `SendHandlerUpdateAsync(animated:false)`, removed page's handlers disconnected | **partial (unverified)**: depth −1 → `PopModelPages(1)` pops the top model page while the MAUI top is unchanged; the top page is re-rendered onto the model page below, then popped Immediate | `NavigationPage.cs:155-185`; `Navigation.cs:475-543, 37-66` | degrades (re-render) | pop the model page *under* the top (`pageStack.pop(page)` of the removed one) |
| Pushed / Popped / PoppedToRoot events | fired after `NavigationFinished` | **works** (timing = native settle or 3 s cap) | `NavigationPage.cs:94,127,151`; `Navigation.cs:146-165` | — | — |
| Stack mutation while a transition runs | MAUI serialises (`SemaphoreSlim`); platform queues | **works**: one coordinator op at a time; next request waits for `NavigationSettled` | `NavigationPage.cs:1024`; `NativeStackCoordinator.cs:110-169`; test `NativeStackSyncTests.cs:324-338` | — | — |
| HasNavigationBar=false | `NavigationPageToolbar.IsVisible=false` → no app bar | **missing**: header always shown (app title when `Title` empty); deliberate device workaround | grep: no `HasNavigationBar` in src; `QtHostPageRenderer.cs:1221-1224`; `NavigationPageToolbar.cs:285`; doc `porting-existing-apps.md:235-236` | degrades (custom-header / onboarding pages get a second header) | honour `Toolbar.IsVisible` by collapsing `pageHeader`/`chrome` height to 0 while keeping the PageHeader item alive (the Canvas-paint workaround) — needs device check |
| HasBackButton=false | toolbar back arrow hidden; hardware back still pops | **works** (Silica back gesture + indicator off) | `QtHostPageRenderer.cs:1229-1231`, `MauiModelPage.qml:266`; doc `porting-existing-apps.md:214-215` | — | note: on Android hardware Back still pops with HasBackButton=false; here the hardware key still pops too (`TryPop`), only the gesture is off — consistent |
| BackButtonTitle | iOS back label | **n/a** (ignored silently) | grep: none | n/a | — |
| NavigationPage.TitleView / Shell.TitleView | shown in the toolbar instead of the title | **missing** (never rendered; MAUI removes it from the page's logical children so it is not in the walk either) | grep: no `TitleView` in src; `NavigationPage.cs:84-86, 267-271`; `ShellToolbar.cs:172-178` | degrades (search box / logo in title lost) | Toolbar handler: render `Toolbar.TitleView` as a host inside `chrome` under/instead of `PageHeader` |
| BarBackgroundColor / BarTextColor / IconColor / Shell colours | toolbar colours | **n/a by rule**, ignored silently (page mapper has no key; `PropertyMapper.UpdateProperty` with an unknown key is a no-op) | `SailfishPageHandler.cs:16-32` | n/a | — |
| Page.Title binding update after push | toolbar title updates | **works** (mapper key → poll → title op) | `SailfishPageHandler.cs:18`; `QtHostPageRenderer.cs:932-938`; Shell fallback to `ShellContent.Title` `:1208-1216` matches `ShellToolbar.cs:181-191` | — | — |
| ToolbarItems Primary → pulley, Secondary → push-up | app bar items / overflow | **works**; Clicked+Command via `IMenuItemController.Activate`; IsEnabled/Text live; add/remove watched | `Interactions.cs:21-79, 86-111, 294-330`; doc `silica-parity.md:31-33` | — | — |
| ToolbarItem.Priority ordering | `ToolbarTracker` sorts by Priority | **partial**: collection order, Priority only re-syncs | `ToolbarTracker.cs:11`; `Interactions.cs:27-34, 106-111` | cosmetic | `OrderBy(Priority)` in `AddSyntheticHosts` or read `Toolbar.ToolbarItems` |
| ToolbarItem.IconImageSource | icon in app bar (text optional) | **missing**: text-only `MenuItem`; an icon-only item is a blank pulley row | `Interactions.cs:220-231` (`text`,`enabled` only); `pulley.js:64-76` | degrades | fall back to `AutomationId`/`Text`; Silica pulleys are text-only, so at least warn once on an empty text |
| Shell.ToolbarItems (shell-level items merged by ToolbarTracker) | shown on every page | **missing (unverified)**: only `page.ToolbarItems` read | `Interactions.cs:27`; `ToolbarTracker` | degrades | read `Toolbar.ToolbarItems` |
| Page.IsBusy | obsolete in MAUI 11 (`[Obsolete]`), still functional | **works** (pulsing pulley / `PageBusyIndicator`) | `Page.cs:56,121`; `QtHostPageRenderer.cs:940-945`; `MauiModelPage.qml:1055-1058` | — | keep; note the obsoletion |
| Page.BackgroundImageSource | page background | **works** | `QtHostPageRenderer.cs:989-996`; `MauiModelPage.qml:879-889` | — | — |
| Page.Padding / SafeAreaEdges (MAUI 10/11) | iOS-only semantics; `Page` reports `HasExplicitSafeAreaEdges=false` | **n/a / partial**: content always starts below status area + header + tab rows; `SafeAreaEdges`/`ISafeAreaView2` never consulted (there is no bottom inset to honour) | `Layout.cs:181-183`; `MauiModelPage.qml:46`; `Page.cs:220-235, 340-346`; grep: no `SafeArea` in src | n/a | — |
| Page.OnBackButtonPressed veto (hardware Back) | `IWindow.BackButtonClicked()` → `Page.SendBackButtonPressed()` → `OnBackButtonPressed` chain (NavigationPage/Shell/FlyoutPage/modal) | **missing**: `TryPop` pops directly; override never called | `Boot.cs:188-195`; `QtHostPageRenderer.cs:606-622`; `Navigation.cs:99-112`; `Window.cs:1038-1043`; `NavigationPage.cs:785-796`; `Shell.cs:2219`; `Page.cs:584-586, 616-633` | **blocks** ("unsaved changes" guards, login pages that swallow Back) | `TryPop`: call `((IWindow)_window).BackButtonClicked()` first; fall back to `ResolveBackTarget` only when it returns false and a surface is still poppable |
| Page.OnBackButtonPressed veto (Silica swipe-back) | n/a on Android (gesture = hardware back); iOS swipe can be disabled via `HasBackButton`/interactive pop gesture | **impossible after the fact**: native pops first, MAUI follows; a veto (cancelled `Shell.Navigating`, `ModalPopping.Cancel`, `PopAsync` returning null) leaves native one page short until the 3 s timeout resync re-pushes it | `NativeStackCoordinator.cs:112-135, 176-178, 210-231`; `Navigation.cs:182-222`; `ShellSection.cs:975-990`; `ModalNavigationManager.cs:580-584` | degrades (page returns after ~3 s) | (a) when the follow pop did not change the MAUI depth, re-push at once instead of waiting for the deadline (`MauiDone && Expected > mirror` → immediate `PushNative`); (b) let apps opt out of the gesture: `backNavigation=false` when a page overrides `OnBackButtonPressed` cannot be detected, so document `NavigationPage.SetHasBackButton(page,false)` as the Sailfish way |
| Appearing / Disappearing / NavigatedTo / NavigatingFrom ordering | NavigatingFrom (sync in PushAsync) → Disappearing(old) + Appearing(new) (sync, `HasAppeared` guard) → platform transition → `NavigationFinished` → NavigatedFrom/NavigatedTo | **works, same order**: MAUI fires all of them itself; the renderer's `SendDisappearing`/`SendAppearing` at the page switch are idempotent no-ops in the NavigationPage/Shell/Tabbed/modal cases | `NavigationPage.cs:81-147, 799-823, 912-923`; `Page.cs:749-756, 790-794`; `QtHostPageRenderer.cs:866-898, 1175-1179`; `ShellSection.cs:1136-1160`; `MultiPage.cs:86-91`; `ModalNavigationManager.cs:638-651` | — | the renderer comment "MAUI core never fires appearing" (`QtHostPageRenderer.cs:887`) is wrong for MAUI 11; harmless |
| PushModalAsync / PopModalAsync, ModalStack | `ModalNavigationManager` + `IModalNavigationPlatform` override | **works**: platform factory from the service overlay; one model page per modal, inner stack of a modal NavigationPage expanded; completes on settle | `SailfishServiceOverlay.cs:92-93`; `SailfishModalNavigation.cs:13-45`; `Navigation.cs:23-35, 168-177`; `ModalNavigationManager.cs:111, 402-452`; legs C/D/G `QtHostDiagnosticsRunner.cs:2670-2800`, `Silica.cs:573-575` | — | — |
| ModalPushing/Pushed/Popping/Popped (Window + Application) | raised by `ModalNavigationManager` around the platform call | **works** (MAUI-side; `ModalPopping.Cancel` honoured for hardware Back and code; after a swipe-back see veto row) | `ModalNavigationManager.cs:580-584, 616, 632, 672`; `Window.cs:470-476, 745-776`; `Application.cs:278-284, 394-407` | — | — |
| Modal over a Shell | modal covers Shell; Shell's `IsPushingModalStack` suppresses double Appearing | **works (unverified on device)**: `RootPage().Navigation.ModalStack` is the window's; tabs/pulley suppressed while a modal is open | `Navigation.cs:27-31`; `Interactions.cs:121-126, 189-194`; `ModalNavigationManager.cs:640-651` | — | — |
| Back button closing a modal | `Page.OnBackButtonPressed` default pops the modal | **works** (TryPop → `BackTarget.Modal`; swipe-back → FollowNative → `PopModalAsync`), but the modal page's own `OnBackButtonPressed` override is skipped (see veto row) | `QtHostPageRenderer.cs:613-616`; `Navigation.cs:189-192`; `Page.cs:616-633`; leg D `QtHostDiagnosticsRunner.cs:2693-2699` | degrades | same as the veto row |
| Modal NavigationPage | inner stack navigates inside the modal | **works** | `Navigation.cs:29-31, 103-104, 175`; leg G `QtHostDiagnosticsRunner.cs:2760-2800`; sample `NavigationDemoPage.xaml.cs:57` | — | — |
| `Shell.GoToAsync` absolute `//`, `///`, relative, `..`, query strings, `IQueryAttributable`, `QueryPropertyAttribute` | `ShellNavigationManager`/`ShellUriHandler`, all MAUI-side | **works** (MAUI-side; the backend only renders the resulting section stack); route pushes verified on device and host | `ShellNavigationManager.cs:31-80`; `SailfishPageContainerHandlers.cs:297-317`; leg B `Shell.cs:118-128`; test `NativeStackSyncTests.cs:76-105` | — | — |
| `GoToAsync(..., animate:false)` | `Animated` on the section's push | **partial**: ignored (see `animated` row) | `ShellNavigationManager.cs:36,60` | cosmetic | as above |
| `Routing.RegisterRoute` pages via DI | `Routing.GetOrCreateContent` → services | **works** (service overlay creates the page with handlers attached, only during a route push) | `SailfishServiceOverlay.cs:49-57, 99-122`; `SailfishPageContainerHandlers.cs:257-262`; test `NativeStackSyncTests.cs:76-105` | — | — |
| `Shell.Navigating` cancel / `GetDeferral` | honoured before the platform moves | **works** for `GoToAsync`/hardware Back; **degrades** for swipe-back (native already popped; re-pushed after 3 s) | `ShellNavigatingEventArgs.cs:38-59, 86`; `ShellSection.cs:983-986`; `NativeStackCoordinator.cs:129-135` | degrades | as the veto row |
| `Shell.Navigated`, `CurrentState`, `CurrentPage`, `ShellNavigationState` | MAUI-side | **works**; the handler listens to `Navigated` to re-sync | `SailfishPageContainerHandlers.cs:250-262, 268-283`; tests `NativeStackSyncTests.cs:60-66, 297-321` | — | — |
| FlyoutBehavior Flyout / Locked / Disabled | drawer / permanent panel / none | **works for Flyout & Disabled**; **Locked treated as Flyout** (pulley), both the page's and the shell's `Disabled` checked | `SailfishPageContainerHandlers.cs:365-366`; doc `architecture.md:98` | cosmetic (Locked) | — |
| `Shell.FlyoutIsPresented` set from code | opens/closes the drawer | **missing, silent**: no mapper key; a pulley cannot be opened programmatically | `Shell.cs:369, 746-754, 813-825`; `SailfishPageHandler.cs:16-32` (no key) | degrades (hamburger/"menu" buttons do nothing) | map `FlyoutIsPresented=true` to a Silica `ContextMenu`/`Menu` listing the flyout entries opened from the header, or at least `QtHostDiag.Warn` once |
| FlyoutHeader / FlyoutFooter / FlyoutContent (+Templates), `Shell.ItemTemplate`, `FlyoutBackdrop`, `FlyoutWidth` | custom flyout UI | **missing, silent** (only `GenerateFlyoutGrouping` text is used) | `SailfishPageContainerHandlers.cs:368-385`; `Shell.cs:348-429, 410-416` | degrades (header with user/avatar, custom item templates, logout footer vanish) | warn once per Shell when any is set; document "flyout = pulley, text only" in `porting-existing-apps.md` |
| FlyoutItem with several ShellSections/Tabs; `Tab` with several `ShellContent` | bottom tabs + top tabs | **works** (tab row + sub-tab row, `ProposeSection`/`ProposeNavigation` so Navigating/Navigated fire) | `SailfishPageContainerHandlers.cs:319-360, 408-415`; `MauiModelPage.qml:966-1049`; tests `NativeStackSyncTests.cs:180-204, 298-321` | — | — |
| `MenuItem` in flyout | flyout entry | **works** | `SailfishPageContainerHandlers.cs:373-383` | — | — |
| `Shell.FlyoutItemIsVisible` | hides the entry | **works (unverified)**: `GenerateFlyoutGrouping` filters invisible items MAUI-side | `Shell.cs:201, 953-961, 2128-2136` | — | — |
| Shell tab / flyout icons (`Icon`, `FlyoutIcon`) | icon + title | **missing**: titles only; a tab with icon only has an empty label | `SailfishPageContainerHandlers.cs:328, 337, 375` | degrades | fall back to `Route` or type name when `Title` is empty; icons stay out (Sailfish idiom) |
| `Shell.TabBarIsVisible=false` / `SetTabBarIsVisible` | hides the tab bar on that page | **missing, silent** (tab row shown on every section root; MAUI pushes it as `UpdateValue` to the `ShellItem` handler, which is `NullElementHandler` here) | `Shell.cs:209, 2300-2356`; grep: none in src; `Interactions.cs:189-194` | degrades (login/onboarding ShellContent inside a TabBar shows tabs) | check `Shell.GetTabBarIsVisible(page)` in `ResolveTabs` |
| `Shell.NavBarIsVisible=false`, `NavBarHasShadow` | hides the nav bar | **missing / n/a** (header always shown) | `Shell.cs:175`; `ShellToolbar.cs:121-129`; `QtHostPageRenderer.cs:1221-1224` | degrades (cosmetic) | with the Toolbar handler (see HasNavigationBar) |
| `Shell.SearchHandler` | search box in the nav bar, suggestions list | **missing, silent** — the whole search feature of a Shell app disappears | `Shell.cs:195, 1222`; grep: none in src | **blocks** for Shell apps built around search | render `Toolbar`'s SearchHandler as a Silica `SearchField` under the `PageHeader` (QML `controls/SearchBar` adapter exists for `SearchBar`); write `Query` back and show `ItemsSource` via a `CollectionView`-like adapter; warn once meanwhile |
| `Shell.BackButtonBehavior` IsVisible / IsEnabled | hides/disables the back arrow | **works** (gesture + indicator off) | `QtHostPageRenderer.cs:1229-1231`; doc `porting-existing-apps.md:214-215` | — | — |
| `Shell.BackButtonBehavior` Command / CommandParameter / TextOverride / IconOverride | Command replaces the pop (toolbar and hardware Back) | **missing**: Command never executed, pop happens anyway | `Shell.cs:2219-2232`; `QtHostPageRenderer.cs:606-622` | degrades (confirm-before-leave patterns) | via `IWindow.BackButtonClicked()`; for the gesture, set `backNavigation=false` while a Command is set and offer the command as the way back (Silica has no back button to relabel) |
| `Shell.PresentationMode` Modal / ModalAnimated / NotAnimated | route pushed as a modal; animation flag | **partial (unverified)**: Modal routes go through `ModalNavigationManager` (works); `NotAnimated` ignored | `Shell.cs:161`; `ShellSection.cs:643-675` | cosmetic | — |
| `ShellContent.ContentTemplate` lazy creation; service-created page rebuilt each time | created on first show, cached unless `_createdViaService` | **works**: created via `GetOrCreateContent` when the section is shown; overlay creates pages only during a route push so templates are not marked service-created | `SailfishPageContainerHandlers.cs:303-305`; `ShellContent.cs:273-320`; `SailfishServiceOverlay.cs:49-57`; test `NativeStackSyncTests.cs:107-127` | — | — |
| Nested NavigationPage inside Shell | MAUI throws (`NotSupportedException`) | **n/a** (same exception) | `ShellContent.cs:318-321` | n/a | — |
| TabbedPage CurrentPage from code | `MultiPage` fires Disappearing/Appearing, handler `CurrentPage` key | **works** | `SailfishPageContainerHandlers.cs:100-123, 131-147`; `MultiPage.cs:86-91`; test `ArchitectureAlignmentTests.cs:509-515` | — | — |
| TabbedPage children added/removed at runtime | handler `ItemsSource` update | **works (unverified)**: `Window.DescendantAdded/Removed(Page)` → poll → tabs recomputed | `QtHostPageRenderer.cs:479-480`; `QtHostPageRenderer.cs:969-985` | — | — |
| TabbedPage `ItemsSource` + `ItemTemplate` | templated children | **works (unverified)**: MultiPage materialises children; titles from the templated page | `MultiPage.cs:17-20, 107`; `TabbedPage.cs:312-313` | — | host test worth adding |
| TabbedPage Bar colours, `SelectedTabColor/UnselectedTabColor` | tab bar colours | **n/a by rule**, silent | `TabbedPage.cs:307-311`; `SailfishPageHandler.cs:16-32` | n/a | — |
| TabbedPage tab icons (`IconImageSource`) | icon + title | **missing** (titles only) | `SailfishPageContainerHandlers.cs:143` | degrades | as Shell icons |
| MAUI 11 `TabbedPage.BadgeText/BadgeColor/BadgeTextColor` (new) | badge on the tab (iOS/Android) | **missing** (nothing reads them; the dll has them) | `TabbedPage.cs:48-55, 146-195, 239-246` | degrades (unread-count badges) | append `BadgeText` to the title in the tab row (`"Inbox (3)"`) or a small highlight dot in `MauiModelPage.qml` tab item; listen to the page's `PropertyChanged` for the three keys in `SailfishTabbedPageHandler` |
| Swipe between tabs | n/a on Android TabbedPage (yes on Shell top tabs) | **works** (title strip only; preview deliberately open) | `QtHostInput.cs:315-410`; `MauiModelPage.qml:59-116` | — | — |
| More than ~4 tabs | Android scrolls the tab strip / overflow | **degrades**: N tabs split the width; text shrinks to `fontSizeSmall` then fades; no scroll | `MauiModelPage.qml:980-994` | degrades | put `tabRow` in a horizontal `SilicaFlickable` once `count > 4` |
| FlyoutPage `IsPresented` (code and gesture) | drawer slides in | **works**: flyout presented as a pushed page, Back/swipe closes it, write-back `IsPresented=false` | `SailfishPageContainerHandlers.cs:203-226`; legs `Shell.cs:268-283`, `Containers.cs:127-137`; doc `architecture.md:98` | — | the write-back sets `flyout.IsPresented` directly rather than through `IFlyoutView.IsPresented` (identical setter, `FlyoutPage.cs:105-113`) — fine |
| FlyoutPage `IsGestureEnabled` | disables the drawer swipe | **n/a** (there is no drawer gesture; the pulley entry is the only opener) | `FlyoutPage.cs:19`; grep: none | n/a | — |
| FlyoutPage `FlyoutLayoutBehavior` Split modes | side-by-side on tablets | **n/a** (phone, one pageStack) | `FlyoutPage.cs:25, 228-232` | n/a | — |
| FlyoutPage Detail swap at runtime (`Detail = new NavigationPage(...)`) | `FlyoutViewHandler` MapDetail; old detail Disappearing | **works**: new container gets its handler on present, old pages leave the cache; **old Detail's handler is not disconnected** (stays subscribed; MAUI Android disconnects via `MapDetail`) | `SailfishPageHandler.cs:24`; `SailfishPageContainerHandlers.cs:47-52, 207`; `PageCache.cs:131-134`; `FlyoutPage.cs:69-73`; test `ArchitectureAlignmentTests.cs:608-625`; leg I `Containers.cs:139-150` | cosmetic (minor leak) | in `SailfishFlyoutPageHandler` track the presented Detail and call `DisconnectHandlers` on the replaced one |
| Flyout opener only on the detail's root page | hamburger only on root pages (Android) | **works / same** | `SailfishPageContainerHandlers.cs:222-223` | — | — |
| DisplayAlertAsync (title, message, accept, cancel) | system alert | **works** (system-dialog panel; outside tap = cancel; single button = accept) | `QtHostAlertSubscription.cs:23-36`; `Interactions.cs:334-342`; `AlertDialog.qml:15-19`; `DialogPanel.qml:99-102`; leg A `QtHostDiagnosticsRunner.cs:2959-2961` | — | — |
| DisplayAlertAsync `FlowDirection` | RTL layout of the alert | **missing, silent** | `QtHostAlertSubscription.cs:31-35` (`arguments.FlowDirection` unread) | cosmetic | pass `mauiMirrored` to `DialogPanel` |
| DisplayActionSheetAsync (cancel, destruction, buttons) | sheet; outside dismiss returns cancel | **works** (empty button texts filtered; destructive first) | `QtHostAlertSubscription.cs:57-73`; `Interactions.cs:363-376`; `ActionSheet.qml:23-40`; leg C `:3052-3054` | — | `FlowDirection` ignored (cosmetic) |
| DisplayPromptAsync (keyboard, maxLength, initialValue, placeholder) | text prompt | **works**; `Keyboard` mapped for Numeric/Telephone only (Email/Url/Chat/Plain → default) | `QtHostAlertSubscription.cs:38-55`; `PromptDialog.qml:25-41` | cosmetic | map `Keyboard.Email/Url` to `Qt.ImhEmailCharactersOnly/ImhUrlCharactersOnly` |
| Two dialogs stacked | both shown, LIFO | **degrades**: second call completes at once with false / null / cancel (`__pushDialog` → "busy") | `MauiModelPage.qml:786-791`; `Interactions.cs:380-400` | degrades | queue dialogs (`Queue<TaskCompletionSource>` in the renderer; open the next on `CompleteDialog`) |
| Dialog while a push animates | shown on the new page | **works (unverified)**: created on the mirror top page; a dialog destroyed with its page rejects | `Interactions.cs:392`; `QtHostPageRenderer.cs:41`; `MauiModelPage.qml:850-853` | — | — |
| Navigation requested while a dialog is open | push proceeds under the alert | **degrades**: `SyncNativeNavigation` returns before `Step` while `_dialogTcs != null`; `PushAsync` completes only via the 3 s "completing anyway" path, the page appears when the dialog closes | `Navigation.cs:295-304, 146-165` | degrades (fire-and-forget alert + navigate) | let `Step` run with a dialog open (dialogs are in-page panels now, W1.7 notes the gate is obsolete) or move the dialog to the new top page |
| `DisplayAlert` from a non-UI thread | MAUI calls `AlertManager.RequestAlert` on the calling thread (no dispatch in Controls); Android/iOS implementations run on the UI thread | **degrades**: `QtHostRuntime.Eval` throws off-thread (strict), caught → fallback result (false/null/cancel) + stderr line | `Page.cs:474-510`; `AlertManager.cs` (RequestAlert → subscription, no dispatch); `QtHostRuntime.cs:120-137`; `QtHostAlertSubscription.cs:84-104`; `Interactions.cs:392` | degrades | hop in `QtHostAlertSubscription`: `QtThread.Run(() => renderer.PushAlertAsync(...))` (the helper exists, `QtThread.cs`) |
| MAUI 11 obsoletion of `DisplayAlert`/`DisplayActionSheet` | `[Obsolete("Use DisplayAlertAsync instead")]`; they forward to the Async versions | **n/a** — both reach the same `IAlertManagerSubscription`; samples already use the Async names | `Page.cs:350-357, 416-442`; `DialogsPage.xaml.cs:17-35` | n/a | — |
| Window root swap (`Windows[0].Page = new AppShell()`) | `IWindow.Content` → `WindowHandler.MapContent`; old page's handlers disconnected | **works**: `MapContent` attaches the new root handler and polls; MAUI disconnects the old tree; `PageHeld` false → cache pruned; depth re-synced; the renderer also catches it via `DescendantAdded(Page)` | `Window.cs:634, 924-980`; `SailfishApplicationHandlers.cs:75-80`; `SailfishHandlersFactory.cs:178-193`; `PageCache.cs:131-134`; `QtHostPageRenderer.cs:479-480`; test `ArchitectureAlignmentTests.cs:495-520`; legs `Shell.cs:112, 230, 268` | — | the old root's `Toolbar` object stays referenced by the window until MAUI replaces it (MAUI quirk, same on Android) |
| Page cache (LRU 4, `MAUI_SAILFISH_PAGE_CACHE`) vs Android/iOS retained views | Android keeps every page's handler view in the back stack; iOS keeps view controllers | **partial**: pages the app holds keep live QML up to 4, then the LRU one is rebuilt on return (slower, native-only transient state such as caret/selection lost; MAUI-owned state — text, ScrollY, bindings — is restored); Appearing/Disappearing counts unchanged (MAUI fires them; renderer's are deduped); parked pages' handlers keep receiving pushes | `QtHostPageRenderer.PageCache.cs:13, 36-56, 63-83`; `PageCache.cs:127-138`; `Page.cs:749-756`; test `ArchitectureAlignmentTests.cs:627+` (`The_least_recently_used_page_beyond_the_limit_is_rebuilt`) | degrades (deep stacks > 4) | document; consider raising the default when memory allows |
| Renderer-sent Disappearing on FlyoutPage flyout present | Android: detail stays "appeared" while the drawer is open | **differs (unverified)**: presenting the flyout is a page switch → `SendDisappearing(detail)`, `SendAppearing(flyout)`; closing reverses | `QtHostPageRenderer.cs:883-896`; `SailfishPageContainerHandlers.cs:208-211` | cosmetic (extra OnDisappearing/OnAppearing on the detail) | acceptable given the flyout is a pushed page here |

## Contract deviations (handler vs renderer responsibilities)

What MAUI expects of a platform and what Sailfish does instead:

1. **Container handlers only describe the stack; the renderer executes.** `ISailfishPageContainer` (`CurrentStack`,
   `Tabs`, `SubTabs`, `FlyoutMenu`, `Holds`; `SailfishPageContainerHandlers.cs:12-30`) is read by
   `QtHostPageRenderer.ResolveRootStack/ResolveTabs/FlyoutEntries` (`Navigation.cs:20-21`, `Interactions.cs:121-126,
   189-202`) and the `NativeStackCoordinator` drives the one Silica pageStack. On Android/iOS each of
   `NavigationViewHandler`, `TabbedViewHandler`, `FlyoutViewHandler`, `ShellHandler` owns a platform view. Given one
   `pageStack` per window and no nestable native containers in Silica, this is an acceptable adaptation
   (`docs/architecture.md:91-100`): the MAUI-visible contracts the handlers must honour are still answered by the
   handlers — `IStackNavigation.RequestNavigation/NavigationFinished` (`SailfishNavigationViewHandler.cs:17, 56-58`),
   `IShellItemController.ProposeSection` / `IShellController.ProposeNavigation` / `OnFlyoutItemSelected` /
   `GenerateFlyoutGrouping` (`SailfishPageContainerHandlers.cs:332, 369, 383, 412`), `IShellContentController.
   GetOrCreateContent` (`:304`), `IFlyoutView.IsPresented` write-back (`:211`). `ITabbedView` is empty in MAUI 11
   (`core_ITabbedView.cs`), so nothing is missed there. **Verdict: no stack-sync contract violated.**
2. **`NavigationFinished` timing.** MAUI expects it after the platform transition; here after `NavigationSettled`
   (`Navigation.cs:127-129`) *or* a 3 s timeout that completes anyway (`:159-163`). On a stuck transition MAUI's
   `NavigationStack` is then declared in sync with a pageStack that is not; the coordinator later resyncs. A softening,
   logged, not a violation in normal operation.
3. **Hardware Back bypasses `IWindow.BackButtonClicked`.** Android's `MauiAppCompatActivity.OnBackPressed` →
   `Window.BackButtonClicked()` → `Page.SendBackButtonPressed()` → the `OnBackButtonPressed` chain (`Window.cs:1038-1043`,
   `NavigationPage.cs:785-796`, `Shell.cs:2219-2232`, `Page.cs:616-633`). Sailfish's `TryPop` re-implements the
   decision (`QtHostPageRenderer.cs:606-622`) and skips every override, `BackButtonBehavior.Command` and
   `FlyoutPage`'s "close the flyout first". **This is the one real contract violation**; the fix is small (call
   `BackButtonClicked()` first, keep `ResolveBackTarget` as the fallback for the native-pop follow path).
4. **Page chrome bypasses `IToolbar`/`ToolbarHandler`.** MAUI 11 computes the toolbar state in
   `NavigationPageToolbar`/`ShellToolbar` (`NavigationPage.cs:986-1002`, `Shell.cs:2040`, `NavigationPageToolbar.cs:285,
   297`, `ShellToolbar.cs:104-129, 170-191`, `ToolbarTracker.cs:11`) and platforms attach a `ToolbarHandler` to
   `Window.Toolbar`/`FlyoutPage.Toolbar`/`Page.Toolbar` (`ToolbarElement.cs:5-17` disconnects the old one). The Sailfish
   renderer reads the page directly in `PageChromeOps`/`AddSyntheticHosts`. Consequences: `HasNavigationBar`,
   `NavBarIsVisible`, `TitleView`, `BackButtonTitle`, `Shell.ToolbarItems`, Priority order, `SearchHandler`
   (ShellToolbar) are all ignored, and `NavigationFinished`'s `UpdateValue("BackButtonVisible")` (`NavigationPage.cs:
   869-876`) goes nowhere. A `SailfishToolbarHandler : ElementHandler<IToolbar, object>` created from
   `SailfishWindowHandler.MapContent` (`(window as IToolbarElement).Toolbar?.ToHandler(context)`) with mapper keys
   `Title`, `IsVisible`, `BackButtonVisible`, `BackButtonTitle`, `TitleView`, `ToolbarItems`, `BarBackground` (ignored)
   feeding `PageChromeOps` would align the chrome with MAUI 11 and remove the hand-rolled `WatchToolbarItems`.
5. **Lifecycle sends from the renderer.** `SendDisappearing`/`SendAppearing` at the page switch
   (`QtHostPageRenderer.cs:889-895, 1175-1179`) duplicate what MAUI core already does for NavigationPage, Shell,
   MultiPage and modals; they are harmless because `Page.SendAppearing` is idempotent (`Page.cs:752`), and they are
   the only source for the FlyoutPage flyout page. Not a violation; the comment at `QtHostPageRenderer.cs:887`
   ("MAUI core never fires appearing") should be corrected.
6. **Modal platform seam is used correctly.** `IModalNavigationPlatformFactory`/`IModalNavigationPlatform`
   (`IsReady`, `PushModalAsync`, `PopModalAsync`, `PageAttached`; `IModalNavigationPlatform.cs:75-169`) are implemented
   (`SailfishModalNavigation.cs`) and `ModalNavigationManager` keeps the stack and events (`ModalNavigationManager.cs:
   111, 402-452, 572-677`). The `animated` argument is dropped (cosmetic).
7. **Handlers attached by the renderer, not by a parent handler.** Content pages get `SailfishPageHandler` from
   `QtHostLayout.AttachHandlers` during the walk (`Walk.cs:252-254`, `QtHostLayout.cs:21-60`) or from the service
   overlay for route pages (`SailfishServiceOverlay.cs:99-122`); nested containers from `SailfishPageContainers.Present`
   (`SailfishPageContainerHandlers.cs:47-52`). On Android a parent handler's `MapDetail/MapContent` does this. Equivalent
   outcome; the one gap is that a replaced `FlyoutPage.Detail` is never disconnected (table).
8. **Alert subscription threading.** MAUI Controls does not dispatch alerts (`AlertManager.cs`, `Page.cs:474-510`); the
   platform subscription is expected to be UI-thread safe. The Sailfish one is not (table row); a `QtThread.Run` hop in
   `QtHostAlertSubscription` restores parity with Android, where `DisplayAlert` off the UI thread also works only
   because the implementation posts to the main looper.

## Open questions / unverified

- InsertPageBefore / RemovePage visual behaviour (new top model page for an unchanged page; re-render onto the lower
  page) is inferred from `PushModelPages`/`PopModelPages`; no host test or device leg exercises either API.
- `TabbedPage.ItemsSource/ItemTemplate`, runtime `Children` add/remove and `Shell.PresentationMode=Modal` route pushes
  have no host test or leg; inferred from MAUI's MultiPage/ShellSection code plus the `DescendantAdded(Page)` poll.
- A cancelled `Shell.Navigating` / `ModalPopping.Cancel` after a swipe-back: the 3 s resync re-push is read from
  `NativeStackCoordinator.Step`; not reproduced in a test (the harness completes the follow pop inside the poll).
- Navigation requested while a dialog is open: the hold in `SyncNativeNavigation:297-304` is certain; whether the
  3 s "completing anyway" then leaves a visible desync after the dialog closes is not verified.
- `DisplayAlert` from a background thread: the throw-and-fallback path follows from `StrictThread` default on
  (`QtHostRuntime.cs:120`); `MAUI_SAILFISH_STRICT_THREAD=0` would instead log and call the shim off-thread (unsafe).
- Whether MAUI's `ViewExtensions.DisconnectHandlers(oldPage)` runs at once on a root swap or waits for the Window's
  `Unloaded` (`Window.cs:963-976`) depends on `oldPage.IsLoaded` in the plain-net Controls build; on Android the same
  code runs, so any delay is MAUI's, not Sailfish's.
- `HasNavigationBar=false` cannot simply hide the `PageHeader`: `QtHostPageRenderer.cs:1221-1223` records that
  Canvas-painted shapes did not reach the screen on a page without a rendered header (Jolla, SFOS 5.2). Any fix needs
  a device check.
- Shell `FlyoutBehavior.Locked`: treated as `Flyout` (pulley); whether an app relying on a permanently visible menu
  (tablet layouts) exists among the ported apps is unknown.
