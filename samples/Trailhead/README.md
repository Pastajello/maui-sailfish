# Trailhead

A hiking, running, riding and ski-touring journal that runs only on Sailfish OS: the showcase app of this backend.
It records trips with the GPS, shows them on OpenStreetMap / OpenTopoMap, keeps photos with them, draws elevation
profiles and statistics, and does it the Sailfish way: pulley menus, press-and-hold context menus with a remorse
countdown, a live cover with actions, ambience colours. The plan, the decisions and what is still open are in
[`docs/trailhead-plan.md`](../../docs/trailhead-plan.md).

## Running it

```sh
SF_SAMPLE_DIR=samples/Trailhead tools/sf deploy --jit --run          # Full flavor
SF_SAMPLE_DIR=samples/Trailhead SF_PUBLISH_PROPS="-p:SailfishHarbour=true" tools/sf deploy --run   # Jolla Store flavor
```

The first launch generates the demo set (12 trips on real terrain, about 90 photos painted from each trip's own
elevation profile) in under a second. The Full flavor's RPM requires QtLocation from the `jolla` repository, so the
phone installs it with the app.

**The tour** walks through the app with real touches and checks what it sees: `CHECK PASS`/`CHECK FAIL` lines in
the device log and in `/tmp/trailhead-tour.log`, a screenshot per state:

```sh
SF_SAMPLE_DIR=samples/Trailhead tools/sf shots --env TRAILHEAD_TOUR=all
SF_SAMPLE_DIR=samples/Trailhead tools/sf shots --env TRAILHEAD_TOUR=record,map --env TRAILHEAD_LANG=pl
```

Parts: `trips` (hint, pulley), `context` (press and hold a row), `detail` (elevation scrub), `photo` (carousel, pinch,
double tap), `import` (the Silica photo picker opened and cancelled, with no screenshot: it shows the owner's
pictures; two photos imported and removed again), `map`, `edit` (toolkit validation), `record` (a replayed track: live
map, compass, cover, KeepAlive, pause/resume, standing still, save), `camera` (preview, a photo into the recording),
`stats` (records slide in, a new one is celebrated), `lab`, `stress` (10,000 trips, 50,000 points, memory),
`listscale` (list sizes up to 10,000 rows, both sizing strategies), `diagnostics` (sensor census, version). The tour
deletes the trips it records; `cleanup` (only when named) removes those older tours left. A full tour takes about 20
minutes: `SF_SHOTS_SECONDS=1500`. `TRAILHEAD_SIMULATE=1` replays a demo track instead of the GPS outside the tour too;
`TRAILHEAD_LANG=pl|en` picks the language; `TRAILHEAD_HINT=1` shows the first-launch hint again.

Host tests of the platform-free core (generator, GPX, statistics, photo painter): `dotnet test samples/Trailhead.Tests`.

## What it shows

| Area | Where | On Sailfish |
|---|---|---|
| `CollectionView` grouped, `DataTemplateSelector`, `RemainingItemsThreshold`, `EmptyView` | trips list | `SilicaListView`, section headers, `ViewPlaceholder` |
| `ToolbarItems`, `FlyoutBase.ContextFlyout` | every page, trip rows | `PullDownMenu`/`PushUpMenu`, Silica `ContextMenu` |
| `SailfishRemorse` | delete a trip, finish a recording | `RemorseItem` over the row, `RemorsePopup` |
| `Microsoft.Maui.Controls.Maps` | trip map, recording | QtLocation (package `Microsoft.Maui.Platforms.SailfishOS.Maps`): tiles, clustered pins, polyline with a casing |
| `Geolocation` listening, `SailfishBackground.KeepAlive`, `SailfishNotifications`, `SailfishCover` | recording | records with the screen off; live cover with pause/waypoint actions; a notification updated in place |
| `SailfishCameraView` | camera, from the recording's pulley | QtMultimedia (package `Microsoft.Maui.Platforms.SailfishOS.Camera`): preview, capture, flash, front/back, pinch zoom |
| `MediaPicker.PickPhotosAsync` | trip detail, pulley "Import photos" | Silica's `MultiImagePickerDialog`; each photo goes on the track by its time |
| Custom control with its own QML adapter | the compass (`Controls/CompassView`, `Platforms/Sailfish`) | the public path of `docs/custom-controls.md` |
| SkiaSharp | elevation profile (touch scrub), bar charts, route drawing, the demo photos | raster surface; a scrub keeps its drag from the back gesture |
| Animations | counters, growing bars, hint, Lab | `Animation`, `*ToAsync`, `AbortAnimation` |
| Behaviors (CommunityToolkit) | trip editing, Lab | validation, masks, typing debounce, animation |
| Effects and mappers | Lab | a `RoutingEffect` and a mapper key, side by side |
| `CarouselView`, pinch and pan, per-page orientation | photo viewer | the page turns to landscape on its own |
| Sensors, `Compass`, haptics, `Vibration` | recording, Diagnostics, SOS | no barometer on the Jolla Phone: elevation from the GPS |
| App links, file types | `trailhead://trip/<id>`, GPX files from other apps | `SailfishUrlSchemes`, `SailfishMimeTypes` |
| Localization | Polish and English | the session's `LANG` or Settings (invariant globalization: no ICU) |
| Flavors | `-p:SailfishHarbour=true` | Store-allowed permissions only |
