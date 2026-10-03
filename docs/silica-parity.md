# Silica / Sailfish OS native parity

[`handler-parity.md`](handler-parity.md) counts MAUI mapper keys. This document looks from the other side: what a
native Silica app does, and whether a MAUI app on this backend does the same with no Sailfish-specific code. Each row
names the matrix leg (`tools/sf matrix <leg>`) that checks it on the device. The list of Silica types comes from the
phone's `Sailfish/Silica/qmldir` (SFOS 5.2), audited 2026-09-30.

Status: **native** = rendered by the Silica type itself · **Sailfish API** = no MAUI API exists, the backend offers
one · **n/a** = no MAUI concept to map · **open** = known gap.

## Pages and navigation

| Silica | MAUI | Status | Leg |
|---|---|---|---|
| `Page` + `PageHeader` (title) | `Page.Title` | native | page, nav |
| Header and tabs move with a pulley drag (`PageHeader` inside `SilicaFlickable`, `TabView` header) | automatic | native | tabpulley |
| `PageStack` push/pop, back swipe from the left edge | `PushAsync`/`PopAsync`, `PopToRootAsync`, modals | native | nav, navback (real swipe), features |
| Tab bar (`TabBar`/`TabView`), swipe between tabs | `Shell` tabs, `TabbedPage` | native | shell, containers, tabpulley |
| Tab swipe animation with the neighbouring page visible (`SlideshowView`) | — | open ([`parity-plan.md`](parity-plan.md)) | — |
| `PageBusyIndicator` | `Page.IsBusy` on a page without a pulley | native | silica C |
| `PullDownMenu.busy` (pulsing pulley bar) | `Page.IsBusy` on a page with a pulley | native | silica C |
| System dialog look (`Sailfish.Lipstick` `SystemDialog`: top panel, centred title, text buttons, page dimmed below) | `DisplayAlertAsync`, `DisplayPromptAsync`, `DisplayActionSheetAsync` | native (an in-app panel; the blur is of the page, not the wallpaper) | popup, controls, stress |
| Orientation change (`allowedOrientations`, rotation transition) | automatic, `DeviceDisplay` follows | native (app-wide; per page: open) | silica G |
| Application cover (`CoverBackground`, `CoverActionList`) | — | Sailfish API `SailfishCover` | f4 H |
| Page lifecycle (`PageStatus`), app background/foreground | `OnAppearing`/`OnDisappearing`, `Window` lifecycle | native | nav, stress D |

## Menus and interaction

| Silica | MAUI | Status | Leg |
|---|---|---|---|
| `PullDownMenu` / `PushUpMenu` | `ToolbarItem` Primary / Secondary; Shell/FlyoutPage flyout as pull-down | native | controls, pulley, tabpulley, shell E/H |
| Menu per tab (only the tabs that declare one) | `ToolbarItems` of each tab page | native | tabpulley |
| `MenuItem` enabled/text updates in place | `ToolbarItem.IsEnabled`, `Text` | native | silica D |
| `ContextMenu` (press and hold) | `FlyoutBase.ContextFlyout` + `MenuFlyoutItem` | native | controls |
| `RemorsePopup` (page-wide undo countdown) | — | Sailfish API `SailfishRemorse.ExecuteAsync(text, …)` | silica H1/H2 |
| `RemorseItem` (undo countdown over a list row) | — | Sailfish API `SailfishRemorse.ExecuteAsync(view, text, …)` | silica H3/H4 |
| `DockedPanel` | — | Sailfish API `SailfishBottomSheet` | controls H, popup D |
| `Drawer` | — | internal interaction host only (no public API) | controls H |
| Pull to refresh | `RefreshView` (page without a pulley, or a list/ScrollView) | native gesture | collection, f3 L |
| `TapInteractionHint`, `InteractionHintLabel`, `FirstTimeUseCounter` | — | n/a (app-specific tutorials) | — |

## Lists and scrolling

| Silica | MAUI | Status | Leg |
|---|---|---|---|
| `SilicaListView` (virtualized, flick physics, quick scroll) | `CollectionView`, `ListView`, vertical `CarouselView` | native | collection×4, perf, features |
| `VerticalScrollDecorator` on lists | `ItemsView.VerticalScrollBarVisibility` (Default/Always/Never) | native | silica A |
| Scroll decorators on flickables | `ScrollView.*ScrollBarVisibility` | native | f3 L |
| `ViewPlaceholder` (empty-state text) | `ItemsView.EmptyView` as a string on a vertical list | native | silica B |
| List item press/selection highlight | `SelectionMode`, `SelectedItem(s)` | native | collection |
| `SilicaGridView` | `GridItemsLayout` (rows of cells in a `SilicaListView`), vertical and horizontal | native look | collection, silica I, Kitchen |
| `SectionHeader` | `GroupHeaderTemplate` (app content) | n/a — MAUI owns the header look | — |
| `SlideshowView` | `CarouselView` `Loop=true` (`PathView`) | native | f3 G |
| Keyboard: the focused field scrolls above the virtual keyboard | automatic inside a `ScrollView` | native | silica F |

## Controls

| Silica | MAUI | Status | Leg |
|---|---|---|---|
| `Button` | `Button` | native | controls, input, visual |
| `IconButton` | `ImageButton`, Stepper buttons, Entry clear button | native | controls, f3 C/K |
| `TextField`, `PasswordField` | `Entry` (`IsPassword`) | native | text, f3 K |
| `SearchField` | `SearchBar` | native | controls, f3 K |
| `TextArea` | `Editor` | native | text G, f3 K |
| `Switch` | `Switch` | native | bridge, controls |
| `TextSwitch`, `IconTextSwitch` | — | n/a (MAUI puts a Label next to a Switch) | — |
| `Slider` | `Slider` | native | controls, f3 N |
| `ProgressBar` | `ProgressBar` (groove sized to the MAUI rect) | native | controls, geometry, f3 |
| `ProgressCircle` | — | n/a | — |
| `BusyIndicator` | `ActivityIndicator` | native | controls, tree |
| `ComboBox` | `Picker` | native | controls, f3 L |
| `ValueButton` + `DatePickerDialog` | `DatePicker` | native | controls, f3 H/L |
| `ValueButton` + `TimePickerDialog` | `TimePicker` (IsOpen, accept writes `Time` back) | native | controls, silica E |
| `Label`, `LinkedLabel` | `Label` (QtQuick `Text` with Silica theme values, RichText for Html) | native look | page, f3 H/N |
| `ColorPicker`, `Keypad`, `ExpandingSection` | — | n/a | — |

## Platform

| Sailfish | MAUI | Status | Leg |
|---|---|---|---|
| Ambience colour scheme | `AppTheme`, `RequestedThemeChanged` | native (simulated event in the test) | f4 B |
| Notifications (`Nemo.Notifications`) | — | Sailfish API `SailfishNotifications` | f4 G |
| Share (`Sailfish.Share`), pickers (`Sailfish.Pickers`) | `Share`, `MediaPicker`, `FilePicker` | native | f4 E |
| Secrets | `SecureStorage` | native (needs the Secrets daemon and an unlocked device-lock collection; else the file store) | f4 A |
| Torch (`org.sailfish.flashlight.provider`) | `Flashlight` | native (not reachable from a Sailjail sandbox) | unit + manual |
| Address book (`org.nemomobile.contacts`), `ContactSelectPage` | `Contacts` | native, but only the non-privileged store: the user's address book is privileged data, open to system apps only (platform limit) | unit + manual |
| Cover actions | `AppActions` | native (two actions) | unit + manual |
| Browser + URL scheme callback | `WebAuthenticator` | native (needs `SailfishUrlSchemes`) | unit |
| — | `TextToSpeech`, `Geocoding`, `Passkeys` | n/a — `FeatureNotSupportedException` (no engine, geocoder or authenticator) | unit |
| Sensors, location, battery, connectivity, haptics, keep-alive | Essentials | native | f4 C/D |
| Open a URL in the browser | `Launcher`, `Browser` | native, not tested (it would move the test app to the background) | — |
| Open a file in another app | `Launcher.OpenAsync(OpenFileRequest)` | native (the other app sees only its sandbox) | silica N |
| Links and files handed to the app (`MimeType`, D-Bus `openUrl`) | `Application.OnAppLinkRequestReceived` (+ `SailfishUrlSchemes`/`SailfishMimeTypes`) | native | silica K |
| Display on/off, lock screen, memory pressure (MCE) | — | Sailfish lifecycle events | silica J |
| Keyboard, rotation, ambience, cover state events | — | Sailfish lifecycle events | silica M |
| Camera capture | `MediaPicker.CapturePhotoAsync` | n/a — Sailfish has no in-app capture API | — |

## Not tested on purpose

- **Opening another app** (URL, share sheet UI): it takes focus away from the test app and every later leg.
- **A real ambience switch**: it changes the phone's settings. f4 B injects the ambience event instead.
- **The Secrets unlock prompt**: there is none for a third-party app (the system password agent refuses it). While
  the app's Secrets collection is locked, f4 checks the file-store fallback and its reason instead.
