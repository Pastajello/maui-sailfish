# Sailfish Kitchen

An "enterprise-shaped" MAUI sample for the Sailfish OS backend: an MVVM recipe
browser over [TheMealDB](https://www.themealdb.com/api.php) (free tier, key `1`),
built to exercise everything a production app needs — dependency injection, a
resilient HTTP layer, incremental loading, offline support, persistence,
animations and a full loading/error/empty state machine — and to document, with
device evidence, exactly where this backend's Qt/Silica host draws the line.

```
samples/SailfishKitchen/
├── Api/            TheMealDB wire contract: DTOs, source-generated JSON context,
│                   typed HttpClient, cache-first DelegatingHandler, response cache
├── Controls/       SpinnerView (animated arc), SkeletonPanel, NotificationBanner
├── Helpers/        PagedCollection (incremental loading), Debouncer
├── Messaging/      Cross-view-model messages (WeakReferenceMessenger)
├── Models/         Domain records, Result<T>/PagedList<T>, MealQuery, AppSettings
├── Resources/      C#-built style dictionary, placeholder art, offline seed JSON
├── Services/       Stores, favourites, settings, image cache, connectivity, dialogs
├── ViewModels/     One view model per screen + per-row card view models
├── Views/          Six XAML pages with compiled bindings
└── tools/          generate-seed.py — re-records the offline catalog from the API
```

## Running it

The project is a plain MAUI app, laid out like one made from the
`maui-sailfish` template:
- the Sailfish head is `net11.0-sailfish`;
- the backend comes from the `Microsoft.Maui.Platforms.SailfishOS` package;
- nothing in the csproj points into the repo.

To build it against this checkout, pack the checkout into the local feed
first, and again after every backend change:

```bash
tools/sf pack-local                                  # package + template → ~/.local/share/maui-sailfish/feed

cd samples/SailfishKitchen
dotnet build -f net11.0-sailfish -t:SailfishRun         # build, deploy, launch on the phone
dotnet publish -f net11.0-sailfish                      # the harbour RPM (bin/SailfishRpm/)

SF_SAMPLE_DIR=$PWD ../../tools/sf deploy             # repo tooling: RPM → device + verify
SF_SAMPLE_DIR=$PWD ../../tools/sf run --env KITCHEN_OFFLINE=1
```

F5 in VS Code: with the [MAUI Sailfish Tools](https://github.com/Pastajello-Organization/sailfishos_maui_tools)
extension, Run and Debug offers "MAUI Sailfish: SailfishKitchen" without any
launch.json (Debug or Release from the status bar switch). From the repo root,
the shell-based `Sailfish: F5 Deploy & Run (choose app...)` and `Sailfish: F5
DEBUG (deploy + attach, choose app...)` configurations work too: `sf-lib.sh`
derives `harbour-sailfishkitchen` / `SailfishKitchen` from the directory name,
and the csproj's `ApplicationId` last segment matches it.

Environment knobs (inject with `sf run --env NAME=VALUE`), mirroring the
backend's own diagnostics culture:

| Variable | Effect |
|---|---|
| `KITCHEN_OFFLINE=1` | never touch the network; serve the bundled seed catalog |
| `KITCHEN_PREFER_OFFLINE=1` | serve cached responses regardless of age |
| `KITCHEN_NO_IMAGES=1` | skip thumbnail downloads |
| `KITCHEN_NO_ANIMATIONS=1` | disable card/hero transitions |
| `KITCHEN_PAGE_SIZE=N` | rows per incremental page (6–48) |
| `KITCHEN_GRID_SPAN=N` | catalog columns (1–4) |
| `KITCHEN_API_BASE=…` | point at another TheMealDB-compatible host |
| `KITCHEN_LOG_LEVEL=…` | `/tmp/kitchen.log` verbosity |
| `KITCHEN_START_PAGE=…` | `home`/`catalog`/`search`/`favorites`/`settings`/`meal` |
| `KITCHEN_START_MEAL=…` | recipe id for `KITCHEN_START_PAGE=meal` |

### The iOS simulator leg

The project multi-targets `net11.0-sailfish;net11.0-ios` (iOS on a Mac). The iOS leg carries **no Sailfish
code at all** — same XAML, same styles, same view models, same container — and
exists so the Qt/Silica rendering can be compared side by side with a native one.
Expect the chrome to differ on purpose: `UINavigationBar` instead of the Silica
page stack, UIKit alerts instead of Silica dialogs, and the Sailfish pulley menus
have no iOS counterpart (there the `ToolbarItem`s render in the nav bar).

```bash
dotnet build samples/SailfishKitchen/SailfishKitchen.csproj -f net11.0-ios

# -t:Run builds and installs but its mlaunch invocation is unquoted, which breaks
# on hosts whose dotnet SDK lives under a path containing a space; install and
# launch through simctl instead:
dotnet publish samples/SailfishKitchen/SailfishKitchen.csproj -f net11.0-ios \
    -r iossimulator-arm64 -o /tmp/kitchen-ios-app
xcrun simctl install booted /tmp/kitchen-ios-app/SailfishKitchen.app
xcrun simctl launch booted com.maui.sailfishkitchen

# the same KITCHEN_* knobs work, but simctl passes environment through the
# SIMCTL_CHILD_ prefix rather than as positional arguments:
xcrun simctl terminate booted com.maui.sailfishkitchen
SIMCTL_CHILD_KITCHEN_START_PAGE=catalog xcrun simctl launch booted com.maui.sailfishkitchen
xcrun simctl io booted screenshot /tmp/kitchen-ios.png
```

The app's two diagnostic files live under the iOS cache directory instead of
`/tmp` (which the sandbox hides); pull the whole data container with
`xcrun simctl get_app_container booted com.maui.sailfishkitchen data`.

With `-p:EnableMauiDevFlow=true` the iOS leg also registers the MAUI DevFlow
agent, so `maui-devflow` (visual tree, screenshots, taps) can drive the running
simulator app. The Sailfish leg keeps this repo's own device tooling for its
evidence: the platform agent package ships no neutral-TFM asset.

## Architecture notes

- **Paging over an API with no paging.** TheMealDB has no offset cursor; its only
  real page unit is the alphabet (`search.php?f=<letter>`). `RecipeRepository`
  walks letters on demand, de-duplicates across them, and memoizes per query with
  `Lazy<Task<…>>` so a burst of identical requests issues one HTTP call. Category
  and search queries fetch once and slice.
- **Resilience.** `AddHttpClient<IMealDbClient, MealDbClient>()` + the standard
  Polly pipeline (retry ×2 with jitter, 20 s attempt timeout, 60 s total, circuit
  breaker sampling 60 s — the sampler must be ≥ 2× the attempt timeout or options
  validation throws on first client creation, i.e. at startup).
- **Cache-first HTTP.** `CachingHttpMessageHandler` sits outside the resilience
  pipeline: a hit never enters retry. On forward failure it serves a stale entry
  rather than an error screen. `Resources/Seed/*.json` are recorded live responses
  primed into the same cache, so offline mode is the *same* code path, not a
  second implementation. Regenerate with `tools/generate-seed.py`.
- **Images.** Online, tiles bind the remote `https` URI: Qt decodes it in the scene
  graph. A background download keeps a copy under the app data directory so the
  same tile renders with the radio off. `file://` sources are used only offline.
- **Threading.** Every observable write that can be reached from an awaited
  continuation is marshalled through `IDispatcher`; the fetches complete on
  thread-pool threads and this host has one UI thread owning the Qt scene graph.
- **MVVM.** CommunityToolkit.Mvvm source generators (`[ObservableProperty]`,
  `[RelayCommand]`) plus `WeakReferenceMessenger`. Activation is hand-rolled
  rather than `ObservableRecipient`, whose `OnActivated`/`IsActive` carry
  `[RequiresDynamicCode]` and would emit IL3050/IL3051 under the trim analyzers
  this repo enables in Release.
- **CommunityToolkit.Maui** is registered inside a guard: it declares support only
  for Android/iOS/MacCatalyst/Tizen/Windows. Its pure-MAUI pieces (converters,
  behaviors) work here; its handler-backed pieces (toast, snackbar, popup) do not,
  so notifications also ride an in-app `NotificationBanner`.

## Backend limitations found while building this (device-verified, SFOS 5.2)

Each of these cost a redesign; they are recorded so the next sample does not
rediscover them the hard way.

1. ~~**`ActivityIndicator` paints a static dot.**~~ — fixed 2026-09-24: the
   spinner graphic was there all along; Silica's `BusyIndicator` only runs its
   render-thread animator while `Qt.application.active`, which the compositor
   does not grant in this launch mode. The adapter now spins its own copy of the
   theme graphic with a GUI-thread `RotationAnimation` (and honours `Color`).
   `Controls/SpinnerView` (a rotated 270° `Path` arc) predates the fix and stays
   as a sample of custom drawing.
2. **Bindings inside `CollectionView.Header`/`Footer` do not inherit the page's
   `BindingContext`** — the header row is materialised as its own native row, so
   bound content there renders empty. All bound content lives outside the
   CollectionView (see the comment in `Views/HomePage.xaml`).
3. ~~**An `Image` created with `Source = null` never picks up a source assigned
   later**~~ — fixed 2026-09-19: a null source no longer placeholder-locks the
   element in the host cache; the reconcile after the Source lands builds the
   image adapter. Row view models still pre-seed the placeholder (harmless).
4. ~~**`file://` image sources do not paint**~~ — fixed 2026-09-19: the sources
   were fine, the cached bodies were not — thumbnails share the API host, so the
   JSON response cache ran binary bodies through `ReadAsStringAsync`/UTF-8 and
   re-encoded them corrupted (Qt: "Error decoding"). `CachingHttpMessageHandler`
   now passes non-JSON bodies through untouched; fresh caches hold valid JPEG/PNG
   bytes and offline tiles paint from `file://` (device-verified, 0 image-failed).
   Data lives in `$HOME/.local/share/<package>/`, derived from the install
   prefix.
5. ~~**A page pushed before the window's first measure/arrange collapses**~~ —
   fixed 2026-09-19: the host defers element creation until the Silica stack
   settles at activation and re-arms per-page state when the model page
   changes, so a pre-activation push renders whole on its own model page
   (repro escape: `KITCHEN_STARTUP_NOWAIT=1`; before/after shots in
   `docs/screenshots/kitchen-sailfish-nowait-*.png`). Startup navigation still
   waits for `window.Width/Height > 1` as the app-side pattern, and
   `Window.Activated` still never fires on this host.
6. **A custom control deriving from a multi-child `Layout` breaks the XAML source
   generator** (`MAUIG1001 … ElementNode … not present in the dictionary`).
   `SkeletonPanel` therefore derives from `ContentView` and hosts its stack.
7. ~~**`RemainingItemsThresholdReached` never fires**~~ — fixed 2026-09-19: the
   collection bridge evaluates the threshold against the flat item count and
   the last visible item of every native scroll report (edge-triggered, one
   event per crossing), so incremental loading rides the MAUI contract again
   (`Views/CatalogPage.xaml.cs`, `Views/SearchPage.xaml.cs`).
   `CollectionView.Scrolled` still fires as before.
8. ~~**No `RefreshView` adapter**~~ — fixed 2026-09-19: `RefreshView` arms
   pull-to-refresh on the scroll surface it wraps (the hosted list, or the
   page flickable when the page has no pulley — with ToolbarItems the Silica
   pulley stays the refresh idiom by design), `ImageButton` rides the image
   adapter with a tap surface, `Frame` paints through the border adapter, and
   `FlexLayout`/`AbsoluteLayout` lay out through MAUI's own cross-platform
   manager. This sample still refreshes from the top pulley (`ToolbarItem
   Order=Primary`) — the native Sailfish idiom — and still uses a `Button`
   with a glyph for the favourite star.
9. **`Border` children are not clipped by the box itself** — the box forwards
   `StrokeShape`/`StrokeThickness` (radius, outline), but children are sibling
   hosts on the flat canvas, so a card image used to paint square over the top
   corners. Since 2026-09-18 the renderer pushes a corner-clip spec
   (`QtHostClip`) and the Image adapter paints concave corner caps, so cards
   read rounded top and bottom like everywhere else; the mechanism is exact for
   flat surrounds only. See `BUG_LIST.md` at the repo root for the trail.
10. ~~**`SearchBar.CancelButtonColor` is ignored**~~ — fixed 2026-09-19: an
    explicit tint re-colours the theme icon of the SearchField's clear/cancel
    button (unset keeps the Silica palette).

### Fixed on the Qt host since the 2026-09-18 device baseline

Found by running the iOS leg side by side with the device and chasing each delta
to its root cause; each fix is device-verified by screenshot or diag witness.

1. **Every `Image` letterboxed instead of cropping.** The bridge crossed
   `(int)image.Aspect` as-is, but MAUI 11 reordered the enum
   (`AspectFit=0, AspectFill=1, Fill=2, Center=3`) while the adapter contract is
   the historical `0 Fill, 1 AspectFit, 2 AspectFill, 3 Center` — so AspectFill
   arrived as "1" and the adapter fit it. `QtHostImages` now translates by member
   name, and `qml/controls/Image.qml` documents the contract as its own.
2. **The favourite star painted as a tinted chip.** The Button mapping skipped
   backgrounds with `Alpha == 0`, so the XAML's explicit
   `BackgroundColor="Transparent"` never crossed and Silica's highlight plate
   showed through. `HasExplicitBackground` now separates "unset" (Silica palette
   survives — native buttons stay native) from "explicitly transparent".
3. **The Silica pulley menus never appeared** (`ToolbarItem`s vanished on this
   host while iOS shows them in the nav bar). Three stacked root causes in
   `interactions/PullDownMenu.qml` / `PushUpMenu.qml`: assigning a JS array to
   Silica's `_content` alias fails on Qt 5.6 ("Cannot assign object to list
   property"), which left the adapter unloaded; any non-`MenuItem` child
   (a `Repeater`, a `Timer`) lands in the same default property and breaks the
   load too; and registering the menu on the page-level `SilicaFlickable` is
   unreachable on pages whose content scrolls inside a hosted `SilicaListView`
   (that flickable is non-interactive, so the gesture never reaches it). The
   adapters now build items into the content column through an invisible anchor
   `MenuItem`, retry with `Qt.callLater`, and attach to the flickable the thumb
   actually drags — witnessed on-device as
   `pulley-attached {interactive:true, pageFlick:false}`.
4. **Card images painted square over the Border's top corners.** Children of a
   Border are sibling hosts on the flat canvas, so the box's radius cannot clip
   them (iOS clips). `QtHostClip` now computes, per hosted element, the ancestor
   Border's radius plus a bitmask of the corners the element paints over and the
   colour of what sits behind the border, and `qml/controls/Image.qml` paints a
   concave cap (square minus quarter disc, Canvas) over each covered corner —
   Qt 5.6 has no rounded clip and `OpacityMask` mis-rendered its mask here.
   Cards now read rounded top and bottom; exact for flat surrounds, which is
   every case in this sample.

## Verification evidence

- `dotnet build Linux.Sailfish.slnx -c Release`: 0 errors. The only warnings are
  the four pre-existing ones (CS0414 in `Linux.SailfishOS`, 3× CS0618 `Frame` in
  the other sample's generated XAML) plus, on macOS, the iOS SDK's machine-level
  notice about a deprecated Xamarin settings plist; the sample itself is
  warning-clean under the trim + AOT analyzers.
- Device (`sf deploy`): RPM `harbour-sailfishkitchen-…aarch64`, 236 payload
  files, `sf verify` → `VERIFY PASSED`.
- Device screenshots: home (category grid with live photos, suggestion card,
  cuisine picker), catalog (2-column incremental grid, "12 recipes loaded",
  Load more), plus search/favorites/settings reachable via `KITCHEN_START_PAGE`.
- Live HTTP from the device to TheMealDB: ~70–200 ms per request, 200s, including
  thumbnail downloads.
- iOS simulator (iPhone 17 Pro, iOS 26.5): home and catalog render the same XAML
  natively — `docs/screenshots/kitchen-ios-home.png`,
  `docs/screenshots/kitchen-ios-catalog.png`; the device counterpart after the
  fixes above is `docs/screenshots/kitchen-sailfish-catalog.png`.
- Qt-host diagnostics (`sf run --env MAUI_SAILFISH_QT_HOST_DIAG=1`): zero
  `ADAPTER load failed`, pulley menus witnessed attached to an interactive
  flickable on every Kitchen page, image snapshots crossing with
  `aspect=AspectFill`.
