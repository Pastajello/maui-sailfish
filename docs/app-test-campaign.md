# Real-app test campaign

Each app below gets a full walkthrough on the phone, recorded and analysed frame by frame. Every bug found goes
into its table and gets fixed. Then the walkthrough is recorded again until a clean run. Framework bugs are fixed in
`src/` with a regression test; app-side porting work stays in the scratch copy and goes into
[porting-existing-apps.md](porting-existing-apps.md).

## Procedure (per app)

1. **Build and deploy** from the app's directory:
   `SF_PUBLISH_PROPS="-p:SailfishOnly=true" dotnet build <proj> -f net11.0-sailfish -p:SailfishOnly=true -t:SailfishRun`
   (net10 samples: no `SailfishOnly`). After a framework change, run `tools/sf pack-local` first.
2. **Script the walkthrough** in `MAUI_SAILFISH_TAPS`, 4 s per step, in screenshot pixels, each entry prefixed with
   its ms after launch: `9000:x,y` taps, `9000:x,y>x2,y2` drags (back gesture: `8,1100>900,1100`), `9000:"text"` typing. Every screen, every tab, every
   back gesture, and every form at least once.
3. **Record:** `tools/sf rec start -f tour.mp4`, `tools/sf run --env MAUI_SAILFISH_TAPS=… --env MAUI_SAILFISH_QT_HOST_DIAG=1`,
   wait out the script, then `tools/sf rec stop -f tour.mp4 --keep-raw`, and pull `/tmp/sf_run.log`.
4. **Analyse:** `tour.raw.mp4.frames.tsv` maps the device clock (`arrival_mono_ms`) to video time (`pts_ms`). Each
   `dev tap #N … @mono=` line gives the tap's moment, so cut frames at +0.3/+0.8/+1.5/+3 s after every tap (and
   finer, every 0.2 s, around back gestures and tab switches). Look for flashes, wrong titles, missing tab rows,
   empty pages, slow transitions and wrong targets. Cross-check the log for `full page host reset`, `dead handle`,
   `not confirmed within`, `resync`, `[ERROR]` and `failed for`.
5. **Write the bugs below, fix, rerun 3–4** until the recording is clean. Record the clean run's file name.

Questions that block a decision go to **Open questions** at the end; work continues on whatever is not blocked.

Scratch copies: `$SCRATCH/oss/maui-samples/10.0/Apps/…` (samples), `$SCRATCH/cand/<owner>_<repo>/…` (advanced apps),
where `$SCRATCH` is this session's scratchpad. `SF_PKG`/`SF_BIN` select the app for `tools/sf run|kill|screenshot`.

## Apps

| # | App | Project (scratch) | SF_PKG / SF_BIN | Status |
|---|---|---|---|---|
| 1 | Profitocracy | `cand/KrawMire_profitocracy/src/Profitocracy.Mobile` | harbour-profitocracy / Profitocracy.Mobile | clean (round 5); language switch left |
| 2 | MoneyFox | `cand/MoneyFox_MoneyFox/Src/MoneyFox.Ui` | harbour-moneyfox / MoneyFox.Ui | clean (round 3); account/payment creation blocked by an app bug |
| 3 | WeightTracker | `cand/fdmomtaz_WeightTracker-DotNetMaui/WeightTracker` | harbour-weighttracker / WeightTracker | clean (round 1) within the known gaps |
| 4 | GitTrends | `cand/TheCodeTraveler_GitTrends/GitTrends` | harbour-gittrends / GitTrends | blocked at start (see questions) |
| 5 | GameSpur | `cand/GameSpur_MobileApp/App` | (private config needed) | blocked |
| 6 | WeatherTwentyOne | `Apps/WeatherTwentyOne/src/WeatherTwentyOne` | harbour-weathertwentyone / WeatherTwentyOne | clean (round 3) |
| 7 | WhatToEat | `Apps/WhatToEat/src/WhatToEat` | harbour-whattoeat / WhatToEat | clean (round 3) |
| 8 | EmployeeDirectory | `Apps/EmployeeDirectory/EmployeeDirectory` | harbour-employeedirectory / EmployeeDirectory | clean (XAML and C# UIs); ED13 intermittent |
| 9 | DeveloperBalance | `Apps/DeveloperBalance` | harbour-developerbalance / DeveloperBalance | clean (round 2); Syncfusion text inputs and chart undrawn (see questions) |
| 10 | Calculator | `Apps/Calculator/src/Calculator` | harbour-calculator / Calculator | clean (round 1) |
| 11 | Weather | `Apps/Weather/Weather` | harbour-weather / Weather | clean (round 1) |
| 12 | TipCalc | `Apps/TipCalc/TipCalc` | harbour-tipcalc / TipCalc | clean (round 1) |
| 13 | RpnCalculator | `Apps/RpnCalculator/RpnCalculator` | harbour-rpncalculator / RpnCalculator | clean (round 1) |
| 14 | SolitaireEncryption | `Apps/SolitaireEncryption/SolitaireEncryption` | harbour-solitaireencryption / SolitaireEncryption | clean (round 1) |
| 15 | GameOfLife | `Apps/GameOfLife/GameOfLife` | harbour-gameoflife / GameOfLife | clean (round 1) |
| 16 | BugSweeper | `Apps/BugSweeper/BugSweeper` | harbour-bugsweeper / BugSweeper | clean (round 3, with the app's `#if` ported) |
| 17 | WordPuzzle | `Apps/WordPuzzle/WordPuzzle` | harbour-wordpuzzle / WordPuzzle | clean (round 2) |
| 18 | PointOfSale | `Apps/PointOfSale/src/PointOfSale` | — | not attempted: BlazorWebView (Razor), SkiaSharp (Microcharts), ZXing camera and MSAL, none with a Sailfish handler (see the SkiaSharp question) |

## 1. Profitocracy

Walkthrough:
- [x] First run: profile wizard (name, currency picker, initial balance), Save
- [x] Home: balance card, Start new period, Today/Tomorrow, spending types, category expenses
- [x] Transactions: list, sub-tabs All / Recurring, FAB → new transaction form, close (×)
- [x] Overview: cards; since 2026-10-02 (`Microsoft.Maui.SailfishOS.SkiaSharp`) the LiveCharts charts draw (axes, and
      the pie charts after one expense was added); chart touch is LiveCharts' own plain-`net` stub
- [x] Settings → Profiles (list, swipe a row: delete/edit, edit with typed name), back
- [x] Settings → Categories, Import/Export, Authentication, Notifications, Theme, Language: each in and back
- [x] Settings → Theme: switch Light/Dark/System and return
- [ ] Settings → Language: pick another language and return
- [x] Add a transaction for real (amount, description), see it on Home and Transactions
- [x] Recurring tab FAB → recurring transaction form, close
- [x] Toolbar pulley (Filters) on Transactions → Transactions Filters page, picker inside
- [x] Repeated in/back on Settings entries, then every tab (rounds 3–5: no resets, resyncs, dead hosts or errors)

Bugs:

| # | Seen | Cause | Where | Status |
|---|---|---|---|---|
| P1 | Settings → Profiles → back: Profiles re-appeared ~3 s later; tab row lost; tabs dead afterwards | AppShell built after an await on a pool thread captured a pool-thread dispatcher nobody drained; Shell's pop (DispatchAsync) never ran, the resync re-pushed the page | `SailfishDispatcherProvider` (only loop threads get a dispatcher), sync context installed before `CreateMauiApp` | fixed |
| P2 | Each back rebuilt all of Settings (54 hosts) | page cache treated a `ContentTemplate` tab page as abandoned (`Content` is null; the page lives in `IShellContentController.Page`) | `SailfishShellHandler.Holds` | fixed |
| P3 | Transactions → Recurring unreachable | a Shell section's contents (top tabs) were not shown when the item has several sections | second tab row (`SubTabs`) | fixed |
| P4 | Back from Theme: Settings flashed empty under the "Theme" title for ~0.4 s | a window report during the back transition reconciled the page being left onto the dying model page; dead handles reset the revealed page | `Reconcile` waits for the stack sync (`_nativeTopUnfollowed`) | fixed |
| P5 | After a back gesture, the next tap on the Home tab rebuilt everything (78 dead hosts) | the stray sweep after a native pop destroyed the other tabs' hosts parked on the same model page | stray sweep keeps parked hosts | fixed |
| P6 | Tab switch logged as "gesture-pop … 3638 ms" | the timeline kept the previous navigation's start | tab switches start their own timeline | fixed |
| P7 | Notifications page titled "Authentication" | app bug (`Title="{x:Static …Pages_Authentication}"`) | — | app, not ours |
| P8 | Theme → Light under a dark ambience: header and tab row nearly invisible | the page's Silica palette stayed the ambience's | `scheme` op → `palette.colorScheme`; tab rows use `palette.*` | fixed |
| P9 | First transaction row drawn over the All/Recurring row | the reported header height left out the second tab row | `reportWindowGeometry` | fixed |
| P10 | Home kept old totals after adding a transaction | a tab tap set `CurrentItem` directly; Shell sent `Navigated` but not the page's `NavigatedTo` (Home reloads there) | tabs go through `ProposeSection`, contents through `ProposeNavigation(ShellContentChanged)` | fixed |
| P11 | Pulley not opened by a quick 0.3 s drag | a Silica pulley needs a slow drag held at the end; not a bug | dev taps: `ms:x,y>x2,y2@1500` | tooling |
| P12 | Transactions list empty for ~0.5 s after its tab is selected | app reloads its list in `NavigatedTo` | — | app, not ours |

Latency (round 3, tap → page appearing): tabs 110–200 ms, push 115–250 ms, the transaction form 470 ms, a back
gesture ~490 ms including the 300 ms drag.

Recordings: round 1 `rec/tour.mp4` (P4, P5), round 2 `rec/tour2.mp4` (P5), round 3 `rec/pc3.mp4` (clean),
round 4 `rec/pc4.mp4` (P8, P9, P10), round 5 `rec/pc5.mp4` (clean).

## 2. MoneyFox

Walkthrough:
- [x] Dashboard: income/expense tiles, Add Payment → form, back
- [x] Statistics: entries in and back (charts expected empty); Category Spreading stepper − / +
- [x] Menu: Budgets, Categories (search typing), Backup, Settings, About, each in and back (back with the keyboard open too)
- [x] Go to Accounts → Accounts → Add Account form, name typed
- [ ] Create an account, add a payment: blocked, Save never enables (MF4)

Bugs:

| # | Seen | Cause | Where | Status |
|---|---|---|---|---|
| MF1 | Picker with a Title: big "Selected Account" label beside a smaller, raised value | the app's FontSize (style: 14) reached the value only | `Picker.qml`: title label takes the size and family | fixed |
| MF2 | Category Spreading stepper value invisible between − and + | the 50 dp Entry kept Silica's page margin on both sides of its text | `Entry.qml`/`Editor.qml`: `textMargin` shrinks with the field | fixed |
| MF3 | Statistics/Menu tab shows a spinner for ~0.5 s | the app's `DelayedView` | — | app, by design |
| MF4 | Add Account: Save stays disabled after typing a name | `SaveCommand` is a new `AsyncRelayCommand` on every read and nobody raises `CanExecuteChanged`; MAUI's Button re-reads `CanExecute` only on that event (so any platform; not checked on Android) | — | app |

Recordings: round 1 `rec/mf1.mp4` (MF1, MF2), round 2 `rec/mf2.mp4` (clean), round 3 `rec/mf3.mp4` (MF4).

## 3. WeightTracker

Walkthrough:
- [x] Onboarding: gender picker, unit picker, height entry, finish (earlier session; fixes: library-handler fallback, picker selection page, menu reset)
- [x] Home: values, Week/Month/Year tiles
- [x] Graph page: Weight Interval picker → Silica selection page → Month, back (chart expected empty)
- [x] Settings page (AiForms.SettingsView: expected empty), back
- [x] Add weight: the CommunityToolkit 7 popup does not open (expected, see questions)

Bugs: none new in round 1 (`rec/wt1.mp4`). Known gaps: AiForms.SettingsView, CT v1 Popup (so no weight can be added).
Since 2026-10-02 the Microcharts graph draws through `Microsoft.Maui.SailfishOS.SkiaSharp` (empty data: the add-weight
popup is the CT v1 gap).

## 4. GitTrends

Walkthrough: blocked at the first page (see questions).

## 6–17. Samples

Each: every screen and control once, every back gesture, every text field typed into, orientation not tested.
- WeatherTwentyOne: Home (24 h, daily), Settings (units radios, theme radios, Sign Out), Favorites, Map tab — done:

  | # | Seen | Cause | Where | Status |
  |---|---|---|---|---|
  | W1 | A drag along the hourly forecast switched to the next tab | the tab swipe armed on any host but list/scroll views themselves; the press hit a label inside the horizontal ScrollView | `QtHostInputRouter.ScrollsSideways` | fixed |
  | W2 | Light theme: Imperial/Metric check mark white on light grey | the app's `checkmark_icon.png` is white | — | app |
  | W3 | "Search" on Favorites takes no text | it is a Label, not an input | — | app, by design |
  | W4 | Map tab: Gecko asks for location permission over the page | WebView (windy.com embed) | — | expected |

  Recordings: `rec/w21a.mp4` (W1), `rec/w21b.mp4`, `rec/w21c.mp4`.
- WhatToEat: category tiles → results → detail → back (twice), diet tile, My Recipes carousel (swipe, item →
  detail, back), pulley New → New Recipe (name typed, Ingredients typed past three lines, Save), pulley Edit on a
  recipe, back gestures, tab switches — done:

  | # | Seen | Cause | Where | Status |
  |---|---|---|---|---|
  | E1 | Category tiles narrower than the style's `MinimumWidthRequest=150` | the measure applied explicit sizes but never Minimum/Maximum requests | `SailfishMeasure.Frame` (as MAUI's ResolveConstraints) | fixed |
  | E2 | A tile tap took 2–3 s to show the results (the mock delay is 0.5–1.5 s) | the results page's `Loaded` handler calls `searchBar.SetSemanticFocus()`; the plain-net Controls fire Loaded as the page joins the window, before any handler existed, so it threw inside MAUI's push: no DescendantAdded, no Navigated, the page appeared on the 2 s heartbeat | `SailfishServiceOverlay`: pages MAUI builds through the services (Shell routes, type templates) get their handlers as they get a parent | fixed |
  | E3 | That exception was invisible | dispatched work (async void handlers) only wrote to Debug output | `SailfishDispatcher.DrainQueue` logs an ERROR with the stack | fixed |
  | E4 | My Recipes carousel ran past the bottom of the screen | the app sizes it from `OnSizeAllocated` (height − 150); the page was arranged over the whole window, header and tabs included | `QtHostLayout.MeasureAndArrange`: the page gets the content area, as on Android/iOS | fixed |
  | E5 | Plain buttons had no Silica plate (seen on New Recipe's Save) | an unset `Background` is `Brush.Default` (empty, not null) and counted as an explicit transparent background | `HasExplicitBackground` uses `Brush.IsNullOrEmpty` | fixed |
  | E6 | New Recipe Save did nothing (recipe stored, page stayed) | `SemanticScreenReader.Announce` threw (no implementation) | `SailfishSemanticScreenReader` (Sailfish has no screen reader: no-op + trace) | fixed |
  | E7 | The form jumped down at the first typed character, up to 2 s late | an empty Entry measured its line height on the fallback estimate, a filled one on the text | `SailfishMeasure.TextInput` measures the line on a fixed sample | fixed |
  | E8 | Save turned enabled up to 2 s after the name was typed | a push from inside a native write-back (`_suppressPush`) was dropped and left to the heartbeat | `PushHostProps` asks for a poll instead | fixed |
  | E9 | Ingredients/Recipe (`AutoSize="TextChanges"`) stayed three lines | the Editor measure ignored AutoSize | `SailfishMeasure.Editor` grows with the wrapped text | fixed |
  | E10 | Save's VSM `Background` (AppThemeBinding Dark=DarkGray / Light=resource) never applied; TextColor did | the compiled AppThemeBinding value does not convert to a Brush (runtime XAML converts it); MAUI-side, not checked on Android | — | open (MAUI/app) |
  | E11 | Three result images missing | the mock's image URLs answer 404 | — | app data |
  | E12 | A full pulley drag leaves the menu open, no item chosen | Silica: releasing past the items keeps the menu open for a tap | — | expected |

  Recordings: `rec/wte1.mp4` (E2–E4), `rec/wte2.mp4` (E6), `rec/wte3.mp4` (clean).
- EmployeeDirectory (two UIs, `App.uiImplementation`): loading page replaced by the list (InsertPageBefore + Pop),
  grouped list scroll, person → detail (favorite switch off/on, property list), back, second person, back, FAB; C#
  UI: login modal (username, password, Login), pulley "search" → search page, list drags, type "Fri", pick the
  result, back twice, FAB → search, back — done:

  | # | Seen | Cause | Where | Status |
  |---|---|---|---|---|
  | ED1 | Round avatars (Image in a 60×60 `Ellipse` Border inside a card) showed square | the corner mask knew only `RoundRectangle`, ignored content inset by the stroke, and took its colour past the card (page colour instead of the card's) | `QtHostClip`: Ellipse radius, stroke inset, only the clipping Border skipped | fixed |
  | ED2 | The avatar's 1 dp `Stroke` ring was hidden under the photo | the mask covers the stroke's inner half | the mask draws the Border's solid stroke (`mauiClipStroke`) | fixed |
  | ED3 | "[Im…" text next to "Added to favorites" | `heart.png` is not in the app; a missing file showed an "[Image]" label | nothing shown + one warning per file | fixed |
  | ED4 | Group footers (BoxView, style `BackgroundColor` under `Color="Transparent"`) a 2 dp line instead of an 8 dp band | not plain, so the Canvas painted it, slowly and only partly | `QtHostShapes.BoxViewProps`: background-only boxes on the fast rectangle | fixed |
  | ED5 | Login card title "Employee Directory" (28 bold) wrapped onto the logo | the shim measured with hinted `QFontMetricsF` advances; QQuickText lays out with design metrics, a few px wider, so a label sized to its own text wrapped | `sailfish_host_measure_text` measures lines with `QTextLayout` + design metrics | fixed |
  | ED6 | Login page: dark band under the header, light content | the app colours its root Grid, not the page; the header always shows here | — | by design (porting doc) |
  | ED7 | Favorites list content dims while flicking | Silica's end-of-list feedback (`BoundsBehavior`); this list is only 73 px taller than the screen | — | native |
  | ED8 | C# detail page and login fields: white text on white cards | the app's Label/Entry styles use White in Dark theme, its C# views hard-code light backgrounds | — | app |
  | ED9 | Property rows read "TitleCEO" | the app's HorizontalStackLayout has no `Spacing` | — | app |
  | ED10 | B group order changes after toggling a favorite | the app keeps the favorites file order inside a group | — | app |
  | ED11 | After back from a detail page the list blinked out and in, once (rec `ed4`) stayed blank until the next touch | the page sets a new `ItemsSource` in `OnAppearing`; the list animated it as an edit (every row faded out and in) and one add transition stalled | `ListView.qml`: edit transitions only when some row is kept; a new source redraws at once, as on Android/iOS | fixed |
  | ED12 | After that rebuild the avatars showed square for ~0.5 s | the corner caps were four Canvases per image, painted asynchronously | caps are two scene-graph rounded frames (mask + stroke), drawn with the first frame | fixed |
  | ED13 | Once (rec `ed9`, #15): after back from Jo Ann's detail her list avatar stayed empty until the next rebuild | not reproduced in three reruns with `MAUI_SAILFISH_IMAGE_TRACE=1` (the avatar loads from cache in 0–1 ms); likely Qt 5.6's pixmap cache when the old row's Image with the same URL is destroyed under load | — | open, intermittent |

  Recordings: `rec/ed1.mp4` (XAML, before ED5), `rec/ed2.mp4` (C#), `rec/ed4.mp4` (ED11), `rec/ed8.mp4` (XAML, clean), `rec/ed9.mp4` (C#, clean but ED13).
- DeveloperBalance (the `dotnet new maui --sample-content` app: Shell flyout, Syncfusion Toolkit, CommunityToolkit,
  SQLite): dashboard scroll, project card → detail, back, task → detail (title typed, completed ticked, Save), task
  ticked on the dashboard, FAB → new task, back, pulley → Projects → project → back, pulley → Manage Meta (field
  focused), pulley → Dashboard — done:

  | # | Seen | Cause | Where | Status |
  |---|---|---|---|---|
  | DB1 | Project cards empty | the card's SfShimmer binds IsActive through {RelativeSource AncestorType} to the page model; collection row views had no parent, the binding never resolved, the shimmer kept the content hidden | `QtHostListAdapter`: row views are the ItemsView's logical children while their row lives (Android/iOS do the same); page-wide walks stop at an ItemsView | fixed |
  | DB2 | Project cards cut off below the description (tags missing) | the horizontal list in an Auto grid row took its MinimumHeightRequest (250); a rebuild then kept rows laid out for the old height | `SailfishMeasure.Collection` takes the tallest item across a horizontal list (`CrossExtentDp`); rows are laid out again when the list's height changes | fixed |
  | DB3 | Syncfusion's self-drawn containers (SfView: text input outline and hint, shimmer, effects) drew nothing | their plain-net SfViewHandler throws; the fallback rendered an empty container | `SailfishDrawnViewHandler`: a failing handler on an IDrawable view records its Draw (GraphicsView replay, Canvas only when it draws) under its children | fixed (generic) |
  | DB4 | SfTextInputLayout still draws no outline or hint (forms show bare fields; "Completed" has no label) | its Draw measures text through Syncfusion's TextMeasurer, which throws on plain .NET | — | open (see questions) |
  | DB5 | Manage Meta: field text above the colour swatch and button | Entry text kept the Silica layout in a row stretched by a button; MAUI's Entry centres by default | Entry pushes its VerticalTextAlignment (default Center); a borderless field without placeholder hides Silica's label line | fixed |
  | DB6 | Back to Dashboard through the pulley: NullReferenceException in the app's selection command | the E2 overlay returned every page type from the services, so the ShellContent marked its page service-created and rebuilt it on return; the new page re-applied SelectedItem and ran the command | `SailfishServiceOverlay` builds pages only during a Shell push/insert (route pages) | fixed |
  | DB7 | Task Categories chart empty | Syncfusion SfCircularChart | — | open (see questions) |

  Recordings: `rec/db1.mp4` (DB6), `rec/db2.mp4` (clean).
- Calculator: every key (7 × 8 =, C, 12.5 + 3 =, +/−, %, C, 9 ÷ 00 0 − 6 4 =), the display after each — clean, no
  framework bugs. Plain transparent buttons are the app's style; +/− and % act only before an operator, and a second
  operator replaces the first (the app's logic). Recording: `rec/calc1.mp4`.
- Weather: city typed into the 40 dp field, Get Weather → "API Key Missing" alert (the sample ships without an
  OpenWeatherMap key), OK, again, dismissed with the back gesture — clean. The alert is Silica's dialog (its single
  button on the cancel side); the keyboard comes back to the still-focused field when it closes. Recording:
  `rec/w1.mp4`.
- TipCalc: subtotal and receipt total typed (numeric keyboard), tip percent typed past 100 (the slider's TwoWay
  binding clamps it), slider dragged (rounds to 0.5 while moving) — clean. The total rounds to a quarter (the app's
  model) and the currency follows the phone's locale. Recording: `rec/tc1.mp4`.
- RpnCalculator: digits, ENTER, binary (+, yˣ, ÷, ×, x⇔y) and unary (√, sin, deg, rad, +/−, 1/x) keys, backspace,
  decimal point, CE, C — clean; the binary keys enable and disable with the stack depth at once (Command
  CanExecute). Binary keys need two ENTERed values (RPN). Recordings: `rec/rpn1.mp4`, `rec/rpn2.mp4`.
- SolitaireEncryption: plaintext and key typed, Encrypt, Decrypt, the read-only result field tapped (no keyboard),
  tap outside — clean. Recording: `rec/se1.mp4`.
- GameOfLife: a glider tapped into the 14×27 BoxView grid (TapGestureRecognizer per cell), Run (the glider moves,
  Run → Pause, Clear disabled), Pause, Clear, About → page with the Wikipedia link, back gesture — clean (the
  ~380 BoxViews draw as plain rectangles, no GL context each). Recording: `rec/gol1.mp4`.
- BugSweeper: first double tap (safe, flood-fills), tap to flag and unflag a corner, more double taps — done:

  | # | Seen | Cause | Where | Status |
  |---|---|---|---|---|
  | BS1 | A double tap fired the single-tap recognizer twice (flag on, flag off) | multi-tap was not tracked: every tap fired the first TapGestureRecognizer | `QtHostInputRouter`: taps on one owner within 300 ms / 40 dp count up; recognizers fire on their NumberOfTapsRequired; a shorter count waits out the window when a longer one exists (Android's single-tap-confirmed) | fixed |
  | BS2 | Double tap still revealed nothing | the app adds its double-tap recognizer only under `#if ANDROID \|\| IOS \|\| MACCATALYST` | scratch copy: `\|\| SAILFISH` (porting doc) | app |

  Recordings: `rec/bs1.mp4`, `rec/bs2.mp4` (BS2), `rec/bs3.mp4` (clean).
- WordPuzzle (15-puzzle, LayoutTo animations): tile moves into the gap, a diagonal tap (ignored by the game),
  Randomize (200 animated shifts, button disabled meanwhile, timer label appears), six taps across rows and columns
  after it (multi-tile shifts both ways) — clean. The scratch copy adds `SAILFISH` to two `#if ANDROID || IOS`
  blocks (font size, multiplier) the shared code needs to compile. The timer label pushes the board down after
  Randomize: round 1's later taps aimed at the old layout. Recordings: `rec/wp1.mp4`, `rec/wp2.mp4` (clean).

## Found outside the apps

| # | Seen | Cause | Where | Status |
|---|---|---|---|---|
| X2 | SecureStorage threw `rc=1011`/`1060` on every call (f4 failed since 2026-09-27) | the app's device-lock Secrets collection stays locked: the daemon never gets the lock code without Jolla's device-lock plugin, and the system password agent refuses third-party apps (no prompt) | `sfsec_open` probes the collection; locked → the file store with the reason | fixed (f4 36/36) |
| X3 | Picked files: `FileResult.ContentType` threw | plain-net `FileBase` cannot look up a MIME type | pickers pass it (`MimeTypes`); `OpenReadAsync` stays MAUI-internal (documented) | fixed / documented |
| X4 | Contacts: "Count contacts" showed 0, while the phone has a contact (also sandboxed with `Permissions=Contacts`) | the address book is in `~/.local/share/system/privileged` (privileged:privileged 0770). Firejail's `privileged-data Contacts` mounts it, but only system apps get the `privileged` group (`Privileged` permission + `mapplauncherd` `privileges.d`, e.g. `jolla-contacts,hip`). Other apps read the non-privileged store | platform limit; the wrong "needs a sandbox" PermissionException removed | documented, not worked around |
| X1 | `/tmp/kitchen-diag.log` on the phone held 335 MB of RAM (tmpfs) | the diagnostics mirror appended every line of every run, opening the file per line | `QtHostDiag.Mirror`: each process starts the file afresh, one writer, 32 MB cap | fixed |

## Open questions

- ~~Missing Essentials~~ — done 2026-10-02: Flashlight, Contacts, AppActions and WebAuthenticator implemented;
  TextToSpeech, Geocoding and Passkeys report FeatureNotSupported (no engine or provider on the platform). Checked
  by hand on the phone (Sample → Features → Essentials).
- **SecureStorage without Jolla's device-lock integration** falls back to the file store (obfuscation only). Real
  protection would need that integration or an app passphrase (Secrets `CustomLock`). Worth doing?

- **Syncfusion Toolkit** (the MAUI template's sample content uses it): its self-drawn views now render through the
  IDrawable fallback (DB3), but SfTextInputLayout, the chart and other text-drawing controls call Syncfusion's
  TextMeasurer, which throws on plain .NET. A `Microsoft.Maui.SailfishOS.Syncfusion` add-on could install a Qt
  text measurer into that private static (reflection, brittle across versions). Build it, or document the gap?
- ~~**SkiaSharp** (LiveCharts, Microcharts).~~ Decided 2026-10-01 and built: `Microsoft.Maui.SailfishOS.SkiaSharp` with
  the SkiaSharp 3.x view handlers ([`skiasharp-plan.md`](skiasharp-plan.md)). Libraries built on it port their own
  platform code (LiveCharts' input).
- **CommunityToolkit integration** (v1 Popup, Toast, Snackbar). Same question: a separate package?
- **GitTrends**: on MAUI 11 rc1 CommunityToolkit.Maui.Markup's typed bindings throw `MethodAccessException`, because
  MAUI 11 dropped the toolkit's `InternalsVisibleTo`. Is there a newer MAUI 11 build or a toolkit preview to try,
  or should the port's bindings be rewritten by path?
- **GameSpur**: needs the authors' private API configuration (shaSalt/api_host); skip unless one is available.
