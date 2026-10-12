# Trailhead: showcase app plan and session tracker

Written for: the agent (or person) who builds the next piece of Trailhead, the showcase app in
`samples/Trailhead`. It holds the why (goals, decisions, device facts), the backend gaps the app runs into, and the
work split into sessions T01–T22 with checkboxes, in the same shape as [`maui11-tracker.md`](maui11-tracker.md).

## Goal

A real-looking hiking/trip journal that only runs on Sailfish OS and uses as much of MAUI and of the platform as
the story allows. Two jobs at once:

- **Demo (a)**: a client opens it and sees a Sailfish app (pulley menus, cover, remorse, context menus, ambience),
  not an Android port. Every feature sits in a real flow; nothing is a "button that calls an API".
- **Stress test (b)**: the same flows at scale (10k trips, 50k-point tracks, long animations, a recording running
  for an hour) with measurable results on the Diagnostics page and in `tools/sf shots` tours.

Not a goal: cross-platform (Sailfish only, so Sailfish-specific APIs are used directly), a real backend (generated
data), a control gallery (that is SailfishKitchen).

## Facts that shaped the plan

Checked on 2026-10-09 against the configured phone: **Jolla Phone (`jp2601`), Sailfish OS 5.2.0.18**.

| Fact | Consequence |
|---|---|
| `QtLocation` is **not installed**; `QtPositioning`, `QtMultimedia`, `QtSensors` are | The native map needs system packages |
| The `jolla` repo has `qt5-qtlocation`, `qt5-qtdeclarative-import-location`, `qt5-plugin-geoservices-osm` **5.4.2+git3** | The Full flavor declares them as RPM `Requires`; the Harbour flavor cannot, so it gets a fallback (D1) |
| sensord: accelerometer, magnetometer (compass), gyroscope, orientation, ALS, proximity configured; pressure and step-counter adaptors present as plugins but **not** in `primaryuse.conf` | Barometer and step counter probably absent; T04 checks at runtime, the app falls back to GPS altitude |
| Sailjail permissions on the phone include `Camera`, `Location`, `Pictures`, `Sensors`, `Notifications`, `Sharing`, `Contacts`, `Audio`, `Videos` | The app's permission list (T01) |
| Backend: `ToolbarItems` → `PullDownMenu`/`PushUpMenu`, `ContextFlyout` → `ContextMenu`, `SailfishRemorse`, `SailfishCover` (+ actions), `SailfishNotifications`, `SailfishBottomSheet`, lifecycle events, `SailfishHarbour=true` packaging | Used as they are ([`silica-parity.md`](silica-parity.md), [`sailfish-apis.md`](sailfish-apis.md)) |
| Backend: a custom native control = `SailfishSnapshotHandler` + own QML adapter ([`custom-controls.md`](custom-controls.md)) | The map, camera and compass are built this way |
| Skia on this backend renders on a raster surface (CPU) | Fine for charts and the compass, too slow for a full-screen pannable map |
| `TextToSpeech`, `Geocoding` throw `FeatureNotSupportedException` (no engine on the phone) | D5, D6 |
| `Flashlight` is unreachable from a Sailjail sandbox; `Contacts` sees only the non-privileged store | D7; the emergency contact is typed in |

## App structure

Sailfish idiom, not tabs: one page stack, pulley menus, attached pages, the cover.

```
Trips (root, CollectionView grouped by month)
├── PullDownMenu: Settings · Lab · Import GPX · New trip (▶ starts recording)
├── PushUpMenu:   Statistics · Scroll to top
├── press and hold a trip → ContextMenu: Share · Edit · Delete (RemorseItem)
└── tap → Trip detail ──(attached page, swipe left)──> Trip map
          ├── Edit → dialog page (Entry/DatePicker/Picker, toolkit validation behaviors)
          └── photo grid (GridItemsLayout) → photo viewer (CarouselView, pinch/pan)

Recording (full-screen map)
├── PullDownMenu: Photo · Waypoint · Pause/Resume · Finish
├── live stats band (animated counters), compass (custom handler)
└── Cover: distance/time/pace live + CoverActions (pause, photo)

Camera (preview, flash, front/back, zoom, capture → geotagged into the trip)
Statistics (Skia charts, animated records)
Lab (what the story cannot hold: Effects vs mappers, SwipeView, animation easing/cancel, gestures, stress screens)
Diagnostics (sensors present, memory/GC, timings, flavor, permissions)
```

Code: `samples/Trailhead/` as vertical slices (`Features/Trips`, `Features/Map`, `Features/Recording`, …), MVVM with
CommunityToolkit.Mvvm, DI, compiled bindings, `CommunityToolkit.Maui` for behaviors/converters/popup/snackbar.
The backend comes by `ProjectReference` (like `Linux.SailfishOS.Sample`), because the app drives backend changes
(maps, camera); a package-consumer check (pack-local) runs once at the end (T22).

**Flavors**: `-p:SailfishHarbour=true` (the existing switch) also defines `HARBOUR`. Each feature the Harbour build
cannot have shows a "Not in the Harbour build: <reason>" placeholder instead of disappearing. The Full flavor is the
default.

## Decisions

`✅` decided, `❓` open (owner), `🔬` decided after the named session measures it.

| # | Question | Answer |
|---|---|---|
| D1 | Map technology | ✅ **Native QtLocation** (owner, 2026-10-09). Revised 2026-10-10 from the Store's own rules (sdk-harbour-rpmvalidator): QtLocation 5.0–5.4, `qml(QtLocation)`, `qt5-plugin-geoservices-osm` and `libQt5Location` **are allowed in Harbour**, so the map can ship in both flavors (the Skia route stays as the fallback when the map is unavailable) |
| D2 | Tile source | ✅ (fact, T03) Jolla's QtLocation 5.4.2 OSM plugin has fixed servers: `tile.openstreetmap.org` (street) and OpenTopoMap (its second map type); only a User-Agent can be set. `MapType.Street` → OSM, `Satellite`/`Hybrid` → OpenTopoMap; attribution on every map; `MapStyle` unsupported |
| D3 | Where the map handler lives | Recommendation: new package `Microsoft.Maui.Platforms.SailfishOS.Maps` (`src/Linux.SailfishOS.Maps`, like `.SkiaSharp`), implementing `Microsoft.Maui.Controls.Maps.Map`, opt-in with `UseSailfishMaps()`. Core stays free of the Maps dependency |
| D4 | Camera | ✅ (fact) the toolkit `CameraView` casts its handler to its own `CameraViewHandler` and drives an internal `CameraManager` that throws on `net`: no third-party platform can plug in. A Sailfish camera view with the same shape, on QtMultimedia |
| D5 | Voice cues (TTS) | Recommendation: none on Sailfish; haptics + a notification sound at each km instead. `TextToSpeech` stays n/a |
| D6 | "Where am I" (geocoding) | Recommendation: a small built-in gazetteer of the demo areas (nearest named place); no Nominatim calls |
| D7 | SOS (flashlight Morse) | Recommendation: screen strobe + vibration (works sandboxed and in Harbour). The flashlight only appears in Lab with its "not reachable from the sandbox" result |
| D8 | Storage | Recommendation: JSON files in `AppDataDirectory` with System.Text.Json source generation (trim-safe, no native sqlite); one file per trip + an index |
| D9 | Installing QtLocation on the phone for T03 | done 2026-10-10 (the owner's "work to the end"): `qt5-qtlocation`, `qt5-qtdeclarative-import-location`, `qt5-plugin-geoservices-osm` 5.4.2+git3 from the jolla repo; remove with `devel-su pkcon remove …` |
| D10 | App name | ✅ **Trailhead**, `ApplicationId` `com.maui.trailhead` → package `harbour-trailhead` |

## Backend gaps the app runs into

Status as of the code on 2026-10-09 (several rows of the MAUI 11 plan's E4 are already fixed: geolocation
`MinimumDistance`/`DesiredAccuracy`, `MediaPicker` `SelectionLimit`, `Launcher.TryOpenAsync`).

| Need | Status | Session |
|---|---|---|
| `Microsoft.Maui.Controls.Maps.Map` | missing | T08 |
| RPM `Requires` from the project (`SailfishRpmRequires`) | missing | T08 |
| Camera preview + capture | **added**: package `Microsoft.Maui.Platforms.SailfishOS.Camera` (`SailfishCameraView` on QtMultimedia, shaped like the toolkit's `CameraView`, which cannot be served: D4) | T12 |
| `UseMauiMaps()` handlers: a library replacement of a MAUI-shipped handler (Controls.Maps' `MapHandler`) | **fixed 2026-10-10**: the factory skipped "stock" (`Microsoft.Maui.*`) registrations before looking for replacements | T08 |
| RPM `Requires` | **added**: `SailfishRpmRequires` (property and item) | T08 |
| App meta (version, build, cover, title, orientation) for a plain `net11.0` head | **fixed 2026-10-10**: only `net11.0-sailfish` heads got it, so `AppInfo` said 1.0.0.0 and `SailfishCover`/`SailfishOrientation` in the project were ignored | T01 |
| Staying awake with the screen off; updating a notification | **added**: `SailfishBackground.KeepAlive`, `SailfishNotifications.Update` | T10/T11 |
| A Skia view's sideways drag (scrub) taken by Silica's back swipe | **fixed 2026-10-10** (native): a handled touch keeps its drag (KeepsDrag, D17), a vertical one goes back to the scrolling ancestors | T16 |
| Harbour: URL schemes install a D-Bus service file, which the Store refuses | **fixed 2026-10-10**: a Harbour build writes `[X-Sailjail] ExecDBus=<pkg>` instead (sailjail provides the service) | T02 |
| Checking an RPM against the Store's rules without a Linux SDK | **added**: `tools/sf harbour-check` (the Store's own lists, any host) | T02 |
| CommunityToolkit platform behaviors (`SelectAllTextBehavior`, `IconTintColorBehavior`, `StatusBarBehavior`) | not in the toolkit's plain `net` build: unusable on Sailfish; in XAML the missing type shows as the source generator's MAUIG1001 | — |
| A `CollectionView` of 10,000 items | **fixed 2026-10-10, found by T18**: the app died with "Stack overflow" in `sailfish_host_set_property` from `PushRows`. The runtime's string marshaller converts an `[In] string` argument on the stack, and the rows JSON (480 KB for 10,000 rows) did not fit the 2 MB stack Sailfish gives the main thread (`ulimit -Hs` 2048). Payloads over 4,096 chars now cross as heap UTF-8 (set_property, apply_props, apply_geometry, eval, invoke). On the phone: 10,000 rows with MeasureAllItems on screen in 2.4 s, MeasureFirstItem 0.4 s; the tour's `listscale` part keeps a 480 KB payload as a regression check | T18 |
| `deploy --jit` after an R2R build | **fixed 2026-10-10** (tools): the publish dir kept trimmed framework assemblies; a profile change now starts from a clean dir | — |
| `MediaElement` | missing | T14 (optional) |
| Silica attached page (`pageStack.pushAttached`, swipe to the next page) | missing (not in `silica-parity.md`) | T09: a Sailfish API on `Page`, or a pulley "Map" entry as the fallback |
| Silica `Dialog` page (`DialogHeader`, accept by swipe) | missing | T07 uses a DialogHeader look-alike; a backend API is a candidate (new session) |
| Theme icons in `Image` (`icon-m-gps`, `image://theme/…`) | **fixed 2026-10-09**: the backend probed `/usr/share/themes/sailfish/icons/z1.0`, absent on SFOS 5.2; now Silica's theme provider loads them (right scale, ambience colour) | T06 |
| `ContextFlyout` on a CollectionView row | **fixed 2026-10-10**: a flyout on the row template's root was never registered (rows map outside the page walk), so press and hold selected the row and no ContextMenu host existed | T06 |
| Row taps and `NumberOfTapsRequired`; a pinch-only row; a moved finger in a row | **fixed 2026-10-10**: the row path fired the first tap recognizer whatever its count (one tap = double tap); a row with only a pinch held the list from the press (no carousel swipe); a drag over a row without pan/swipe still ended in a tap | T07 |
| Scripted UI for tours and self-checks | **added**: `SailfishUiDriver` (Diagnostics: press/move/release, long press, list row points, `SF-SHOT`) | T01, used by every session |
| Effects (`RoutingEffect`/`PlatformEffect`) | unknown | T17 checks |
| Barometer, step counter | T04 on the phone: no barometer (elevation from GPS altitude); step counter: no MAUI API | T04 |
| `TextToSpeech`, `Geocoding` | n/a on the platform | D5, D6 |
| `Flashlight` in the sandbox, privileged contacts | platform limit | D7 |

## Index

`📱` = needs the phone, `❓Dn` = needs decision Dn first.

**F0 — skeleton and feasibility**
- [ ] [T01](#t01) Project skeleton, both flavors, cover, deploy · 📱
- [x] [T02](#t02) Harbour validation of the skeleton RPM (`tools/sf harbour-check`: 0 errors for the Harbour flavor with the map and camera)
- [x] [T03](#t03) QtLocation spike: OSM tiles in a pulley page · 📱
- [x] [T04](#t04) Sensor census on the Diagnostics page · 📱

**F1 — data and the trips list**
- [x] [T05](#t05) Model, seeded generator, JSON store, GPX import/export
- [x] [T06](#t06) Trips list: groups, pulleys, context menu, remorse, empty state · 📱
- [x] [T07](#t07) Trip detail, edit dialog with behaviors, photo grid and viewer · 📱

**F2 — map**
- [x] [T08](#t08) `Microsoft.Maui.Platforms.SailfishOS.Maps`: handler + QtLocation adapter + `SailfishRpmRequires` · 📱
- [x] [T09](#t09) Trip map page, photo pins, clustering, Harbour fallback · 📱 (attached page: a pulley "Map" entry; the Silica attached-page API stays a gap)

**F3 — recording**
- [x] [T10](#t10) Recording service: listening, filtering, pause, crash-safe persistence · 📱
- [x] [T11](#t11) Recording page, live cover with actions, notification, km haptics, lock-screen behaviour · 📱 (lock screen: by KeepAlive; not walked outdoors)

**F4 — camera and media**
- [x] [T12](#t12) Camera: investigate the toolkit `CameraView`, implement the Sailfish handler · 📱 (own `SailfishCameraView`, package `Microsoft.Maui.Platforms.SailfishOS.Camera`)
- [x] [T13](#t13) Capture flow, geotags, `MediaPicker` multi-import · 📱
- [ ] [T14](#t14) (optional) `MediaElement` handler for trip clips · 📱

**F5 — sensors and drawing**
- [x] [T15](#t15) Compass as a custom handler with its own Silica adapter; SOS strobe · 📱 (the torch only unsandboxed)
- [x] [T16](#t16) Statistics: Skia elevation/pace charts, animated counters, records · 📱

**F6 — Lab and stress**
- [x] [T17](#t17) Lab: Effects vs mappers, SwipeView, animations (easing, cancel), gestures · 📱
- [x] [T18](#t18) Stress: 10k trips, 50k-point polyline, 1 h recording soak, Diagnostics metrics · 📱 (the 50k points as a Skia profile)

**F7 — finish**
- [x] [T19](#t19) Onboarding hints, permissions flow, settings (units, ambience-aware theme) · 📱 (app links and GPX opening built, not walked by the tour)
- [x] [T20](#t20) Localization pl/en, accessibility (semantics, font scaling) · 📱 (font scaling: backend gap G7)
- [x] [T21](#t21) `SF-SHOT` tour, README, demo clip · 📱 (the clip is not in the repo yet: where it goes is the owner's call)
- [x] [T22](#t22) Package-consumer check (pack-local), Harbour RPM validated again, trimmed build · 📱

## Sessions

### T01
Project skeleton, both flavors, cover, deploy.
- [x] `samples/Trailhead/Trailhead.csproj`: `net11.0`, `ProjectReference` to the backend (+ Diagnostics only if
  needed), packaging targets imported, `ApplicationId` `com.maui.trailhead`, title "Trailhead", icon 108×108,
  `SailfishPermissions` `Location;Pictures;MediaIndexing;Camera;Sensors;Notifications;Sharing;Audio` (`MediaIndexing` for the photo picker), `SailfishCover=true`,
  `HARBOUR` constant when `SailfishHarbour=true` (icon: 172×172, the size SFOS 5 asks for)
- [x] packages: `CommunityToolkit.Maui`, `CommunityToolkit.Mvvm` (versions as in SailfishKitchen)
- [x] `App`, `MauiProgram`, `Program`; a Shell with one root (routes for deep links, `trailhead://trip/<id>` via
  `SailfishUrlSchemes`) or a `NavigationPage` — pick the one the header/search needs
- [x] Trips page stub with the pulley entries, Diagnostics page stub showing flavor and permissions
- [x] added to `Linux.Sailfish.slnx`
- [x] `SF_SAMPLE_DIR=samples/Trailhead tools/sf deploy --run` works for both flavors; screenshot of the pulley open
- Done when: both flavors install and start from the app grid; screenshot shows the Trips page and the cover.

### T02
Harbour validation of the skeleton RPM.
- [x] find a runnable Harbour validator (Jolla's `sdk-harbour-rpmvalidator`, or the Sailfish SDK's `rpmvalidation`)
  and how to run it from this Mac (container if it needs Linux); write the command into this file
- [x] validate the `SailfishHarbour=true` RPM; record every failure here and fix the packaging ones
- [x] list which of `QtMultimedia` / `QtSensors` / `QtPositioning` imports and which permissions the validator accepts
- Done when: the skeleton passes, or the remaining failures are listed with a reason.

### T03
QtLocation spike. Needs D9.
- [x] install the packages on the phone (owner's OK)
- [x] a throwaway QML adapter with `Plugin { name: "osm" }` + `Map`: which `PluginParameter`s the 5.4.2+git3 plugin
  takes (tile host, User-Agent, cache dir and size), does it show tiles under Sailjail with `Internet`
- [x] pan/pinch on a page with a pulley: does the map keep its drags (`KeepsDrag`)?
- [ ] `MapPolyline` with 5k points: frame time while panning (not measured; the demo tracks have a few hundred)
- Done when: D2 can be answered and T08 knows the adapter's shape.

### T04
Sensor census.
- [x] Diagnostics page: `IsSupported` and a live value for accelerometer, gyroscope, magnetometer, compass,
  orientation, barometer; step counter if MAUI exposes one (it does not: note it)
- [x] screenshot; update the facts table above
- Done when: F5 knows whether elevation comes from the barometer or GPS altitude.

### T05
Model, generator, store, GPX.
- [x] `Trip`, `TrackPoint` (lat, lon, alt, time), `Waypoint`, `Photo`, `Note`; activity type
- [x] generator with a fixed seed: ~12 demo trips on real terrain (Tatra, Karkonosze, Bieszczady, Finnish Lapland),
  tracks of a few hundred points with a plausible elevation profile and slope-dependent pace, waypoints, notes,
  3–15 photos each, drawn procedurally (sky, ridgeline from the trip's own profile, place and date)
- [x] stress set: 10k trips, one track of 50k points (behind a Lab switch)
- [x] JSON store (D8), GPX 1.1 export/import, unit tests in a small test project or in-app self-test
- Done when: first launch generates and persists the demo set; export → import round-trips a trip.

### T06
Trips list.
- [x] `CollectionView` grouped by month, `DataTemplateSelector` (trip / trip with photos / in-progress)
- [x] `ToolbarItems` Primary/Secondary → pulleys; `ContextFlyout` Share/Edit/Delete; Delete through
  `SailfishRemorse.ExecuteAsync(view, …)`; `EmptyView` string → `ViewPlaceholder`; `RefreshView` via the pulley
- [x] infinite loading by month (`RemainingItemsThreshold`)
- [x] screenshots: list, context menu (remorse and the empty state built, not in the tour's screenshots)
- Done when: every row above is visible on the phone.

### T07
Detail, edit, photos.
- [x] detail page with stats and the photo grid; attached map page placeholder (T09 fills it)
- [x] edit as a dialog page; toolkit validation behaviors (name required, end after start), `EventToCommandBehavior`
- [x] photo viewer: `CarouselView` + pinch/pan on the image; share a photo
- Done when: edit validates and saves; the viewer pinches and swipes on the phone.

### T08
Maps package. Needs D2, T03.
- [x] `src/Linux.SailfishOS.Maps` (+ slnx, pack-local, PublicApiGuard), `UseSailfishMaps()`
- [x] `SailfishMapHandler` (snapshot): `MapType`, `IsShowingUser`, `IsScrollEnabled`, `IsZoomEnabled`,
  `IsTrafficEnabled` (no-op), `Pins`, `MapElements` (`Polyline`, `Polygon`, `Circle`), `MoveToRegion` command,
  `VisibleRegion` written back, `MapClicked`, pin `MarkerClicked`/`InfoWindowClicked`
- [x] QML adapter on QtLocation 5.4: `MapQuickItem` pins, `MapPolyline`/`MapPolygon`/`MapCircle`, attribution
- [x] packaging: new `SailfishRpmRequires` property → RPM `Requires:`; the Maps package adds the three QtLocation
  packages (in both flavors since D1 was revised)
- [x] host tests for the snapshot (6); the tour's `map` part instead of a matrix leg; update `sailfishos-packaging.md`,
  `silica-parity.md`, the MAUI 11 plan's `Map` row
- Done when: a `Map` with pins and a polyline renders and pans on the phone; the RPM pulls QtLocation on install.

### T09
Trip map page.
- [x] attached page with the track polyline, start/finish pins, photo pins (thumbnail callout → viewer)
- [x] grid clustering of photo pins at low zoom (in the view model)
- ~~Harbour: Skia route drawing + `Map.OpenAsync` (`geo:`)~~ not needed: the Store allows QtLocation (D1 revised)
- Done when: screenshots of both flavors.

### T10
Recording service.
- [x] `Geolocation.StartListeningForegroundAsync` (`MinimumDistance` 5 m, high accuracy), accuracy/speed filter
- [x] pause/resume, auto-pause when still; append to disk every N points (survives a kill)
- [x] keep-alive while recording (backend's keep-alive service), stop it after
- Done when: a 15-minute walk records a clean track with the screen locked.

### T11
Recording UI.
- [x] full-screen map with the live polyline and position, stats band with animated counters
- [x] pulley: Photo · Waypoint · Pause/Resume · Finish (Finish through a remorse popup)
- [x] `SailfishCover`: live distance/time/pace, actions pause and photo; `Window.Title` on the cover
- [x] `SailfishNotifications` "Recording" while minimized; haptic at each km (D5)
- [x] lifecycle: window Activated/Deactivated/Resumed/Stopped logged (`LIFECYCLE`), the draft written on Stopped;
  KeepAlive keeps the replay going with the display off (no memory-pressure event in MAUI)
- Done when: screenshots of the page, the cover and the notification; the cover actions work.

### T12
Camera.
- [x] read the toolkit `CameraView` on `net`: can a platform outside the toolkit supply its handler/manager?
  → D4
- [x] adapter: QtMultimedia `Camera` + `VideoOutput`: preview, capture to file, flash mode, front/back, zoom;
  `Camera` permission
- [x] host tests (2), the tour's `camera` part, docs (`silica-parity.md` camera row)
- Done when: preview and capture work on the phone in a sandboxed build.

### T13
Capture flow.
- [x] camera page from the recording pulley and from the cover action; photo stored with the current fix
- [x] `MediaPicker.PickPhotosAsync` with `SelectionLimit` to import into a trip (trip detail pulley; placed on the
  track by file time, no EXIF GPS: Skia does not read it; needs `MediaIndexing`)
- Done when: a captured photo appears as a pin on the trip map.

### T14
(optional) `MediaElement` handler on QtMultimedia `MediaPlayer`/`VideoOutput`; short clips in a trip.

### T15
Compass and SOS.
- [x] `CompassView` control + `SailfishSnapshotHandler` + Silica-themed QML adapter (rotation animated in QML);
  the public custom-control path from `custom-controls.md`, used as a library would
- [x] SOS: Morse strobe full-screen + vibration (D7)
- Done when: the compass follows the phone; SOS runs and stops.

### T16
Statistics.
- [x] Skia elevation profile (touch to scrub, linked to the map position), pace chart, monthly totals
- [x] counters animated from 0, records with a celebration animation
- Done when: screenshots; scrubbing on the phone is smooth.

### T17
Lab.
- [x] Effects: one `RoutingEffect`/`PlatformEffect` and the same look as a mapper customization side by side
  (record whether the backend runs Effects at all → gap table)
- [x] `SwipeView` (works, not the Sailfish idiom: say so on the page), animation gallery (easings, sequences,
  `AbortAnimation` mid-way), gesture playground (pan, pinch, swipe, drag & drop)
- Done when: every Lab entry works or shows why not.

### T18
Stress.
- [x] 10k trips scroll, 50k-point profile (Skia, not a map polyline), 1 h recording soak with the live map, compass and
  animated counters on screen (tour part `soak`)
- [ ] Diagnostics: managed memory, GC counts ✅ (Stress page), page-load times; counters `treeFixups`, `timerWithWork`,
  `NavResyncs`, `BridgeFailed` stay 0 in the device log
- Done when: numbers written into Notes.

### T19 – T22
See the index; split into steps when they are next.

## Notes

- 2026-10-09: plan written; device facts checked (Jolla Phone, SFOS 5.2.0.18, QtLocation 5.4.2 in the `jolla`
  repo but not installed).
- 2026-10-10: T01 🟡 (both flavors install and run; the cover is not screenshot-checked yet, it needs the app minimized),
  T05 ✅ (generator, store, GPX; 9 host tests in `samples/Trailhead.Tests`; the demo set generates in ~0.8 s on the
  phone: 12 trips, ~90 photos), T06/T07 🟡 (built; the tour found three backend input bugs, fixed above, re-check
  pending). The tour: `SF_SAMPLE_DIR=samples/Trailhead tools/sf shots --env TRAILHEAD_TOUR=all`; it also writes
  `/tmp/trailhead-tour.log` on the phone with a `CHECK PASS/FAIL` line per checked state. Deploys for iterations:
  `tools/sf deploy --jit` (the default R2R build took ~10 min with SkiaSharp in the payload).
- 2026-10-10 (later): F2–F3 done and checked on the phone by the tour (`CHECK PASS` lines): the map (OpenTopoMap/OSM
  tiles, 12 pins in 3 clusters, the route with a casing, pan without the pulley or back swipe taking it, region
  reported back), recording with a replayed track (live line, pin, compass with the way back, pause/resume, cover,
  KeepAlive on/off, banner on the list, saved trip), statistics (bars grow in), sensor census (no barometer),
  validation (toolkit 15 behaviors need their BindingContext set: they no longer inherit it), double tap, carousel
  swipe, context menu on a row. Known then (both fixed in the evening, below): updating the notification on pause shows a banner;
  `sf shots` screenshots are taken late when SSH is slow, so the tour's own `CHECK` lines are the reference.
- 2026-10-10 (afternoon): full acceptance matrix after the backend fixes: **35/35 legs PASS**. The Store's current rules
  allow QtLocation (D1 revised: the map ships in both flavors), the Harbour flavor differs only in its permissions,
  and its RPM passes `tools/sf harbour-check` with 0 errors. Lab (T17), settings, app links and GPX opening (T19),
  Polish/English (T20), the camera (T12/T13), the first-launch hint and the custom cover are built; the full tour
  (`TRAILHEAD_TOUR=all`) checks them on the phone. Toolkit 15 note: `SelectAllTextBehavior` and the other platform
  behaviors are not in its `net` build.
- 2026-10-10 (evening): the full tour passes (45 `CHECK` lines, every part). Fixed along the way:
  - **10k-row list stack overflow** in the backend (P/Invoke `string` arguments are marshalled onto the 2 MB main-thread
    stack; payloads over 4096 chars now go as heap UTF-8 through `_utf8` entry points; the `listscale` part checks it);
  - **notification banners on every update**: SFOS 5 lipstick pops a banner for any normal-urgency notification, preview
    text or not (checked with raw D-Bus hints); `Show(preview: false)` and `Update` now publish at low urgency;
  - **late screenshots**: one `sf shots` SSH call to a busy phone waited the default 300 s, so every later shot came
    minutes late; the poll and the screenshot now time out after 20/30 s, `shots.log` has a time per shot, and the
    tour quits the app at its end so `sf shots` stops with it;
  - the first-launch hint's caption sat on a list row: now at the bottom on a darker dim, as Silica's.
  New: photo import from Silica's picker (T13, `MediaIndexing` permission added to both flavors), auto-pause when
  standing still (T10), records sliding in with a new one celebrated (T16), lifecycle logging and a draft on
  Stopped (T11), accessible names on the profile, maps, compass, camera and charts, headings on Statistics (T20;
  font scaling stays a backend gap, G7 `FontAutoScalingEnabled`). The tour never screenshots the photo picker: it
  shows the phone owner's own pictures.
- 2026-10-10 (night): T22 ✅. `tools/sf pack-local` now packs `.Maps` and `.Camera` too; a fresh `maui-sailfish`
  template app with the two packages from the local feed (as the Maps README says) builds a trimmed Release RPM
  that requires `qml(QtLocation)` and `qt5-plugin-geoservices-osm`, carries both QML adapters, passes
  `harbour-check` with 0 errors and 0 warnings in the Harbour layout, and shows OSM tiles and the camera preview on
  the phone (the test app was removed again). The tour now deletes the trips it records; `TRAILHEAD_TOUR=cleanup`
  removes those of earlier tours (recognised by the replayed route's first fix).
- 2026-10-10 (night): the full acceptance matrix again after the evening's backend changes (heap UTF-8 for large
  payloads, `KeepsDrag` touch, low-urgency notifications): **35/35 legs PASS**; host tests 656 + 9.
- 2026-10-10 (night): the Harbour flavor on the phone: trimmed Release RPM, `harbour-check` 0 errors 0 warnings,
  Diagnostics shows "Harbour", the tour (trips, detail, import, map, record, camera, stats, diagnostics) passes.
  T18 soak on that build, `TRAILHEAD_TOUR=soak` (60 min at real speed, recording page on screen): fixes 4 → 235,
  managed heap after a full collection 2.4 → 2.7 MB, working set 271 → 292 MB (the tile cache), GC gen2 3 → 4.
  `sf shots` now keeps the display awake: during the hour the phone dozed and its SSH answered so late that two
  shots came 3 and 15 minutes after their markers.
- 2026-10-10 (late): recording the demo clip (T21) ran the tour without the shot pauses and found an app bug the
  slower tours hid: a burst of fixes (the replay catching up after standing still) started two draft writes on the
  same temporary file, an unobserved task failed and the process outlived its window. Draft writes are now one at a
  time from a synchronous snapshot, and a write that ends after the recording deletes the draft; the tour ends with
  a check that no task failed unobserved.
- 2026-10-11: owner's bug: on the trips list, the context menu opened by a press and hold did not take taps; the
  rows under it did (the trip below opened). Cause: the menu opens on a stand-in inside the held row's delegate and
  hangs over the next rows, whose delegates (each with a MouseArea over the whole row) stack above it. Fix
  (MauiModelPage.qml): while a context or flyout menu is open, every ancestor of the stand-in goes above its
  siblings, and gets its z back when the menu closes. Found on the way: the menu sometimes did not open at all
  ("no-target": the row's host was not on the shell's last registered page); it now opens on the page that holds
  the host, says why when it cannot, and managed no longer waits for a menu that never came up. The tour's
  `context` part now taps "Edit" in the open menu and checks the editor opens for the held trip; with the rows
  lowered back (`TRAILHEAD_CTX_AB=1`, the negative control) the same tap opens the trip below, as reported.
