# On the plain `net11.0` TFM, `Loaded` fires before any handler can exist (`IsLoaded => Window != null`)

## Description

On the platform-neutral `net11.0` (and `netstandard`) build of `Microsoft.Maui.Controls.dll`,
`VisualElement.IsLoaded` is:

```csharp
public bool IsLoaded => Window != null;
```

`Loaded` is raised from `UpdatePlatformUnloadedLoadedWiring` → `HandlePlatformUnloadedLoaded` → `IsLoaded ?
SendLoaded : SendUnloaded`. On the plain build it therefore fires as soon as an element gets a `Window`, which for the
root page is inside `new Window(page)` in the app's `Application.CreateWindow`.

That is before the backend has seen the window, so before any handler exists for the page or its children. Nothing
raises `Loaded` again when the handlers arrive.

On Android, iOS and Windows, `IsLoaded` follows the platform view (Android:
`((IPlatformViewHandler)Handler).PlatformView.IsLoaded()`). There a `Loaded` handler can rely on `Handler` and
`Handler.PlatformView`. Code written that way, which is common, sees `Handler == null` on a backend built on the plain
TFM:

```csharp
page.Loaded += (_, _) => ((IPlatformViewHandler)page.Handler!).PlatformView ...;   // NullReferenceException
```

`SemanticExtensions.SetSemanticFocus()` called from `Loaded` hits the same null handler.

The backend cannot fix this from its side. The app constructs the page and the window itself, and on this build
`Loaded` has no hook other than `Window` being set. The backend gets the window only after `CreateWindow` returns. By
then `Loaded` has fired, and raising it again would need a fake `Unloaded` through private members.

## Why this reaches a platform TFM

A backend usually has its own platform TFM (here `net11.0-sailfish`), but MAUI's packages ship no assets for it, so
NuGet resolves the nearest framework: `lib/net11.0`. For example, the Sailfish head's `project.assets.json` lists
`lib/net11.0/Microsoft.Maui.dll` and `lib/net11.0/Microsoft.Maui.Controls.dll`. Every out-of-tree backend therefore
runs on the platform-neutral build, and this neutral-build behaviour is what its apps see on the device.

For Sailfish OS in particular:

- The root page of every app, and every page the app constructs itself (`PushAsync(new DetailPage())`), raises
  `Loaded` with `Handler == null`. Only Shell route pages, which the backend constructs and can hook before parenting,
  are covered.
- Ported apps and libraries use `Loaded` to reach the platform view (focus, semantic focus, measuring, starting
  animations, custom handler setup). These run fine on Android and iOS and fail, or silently do nothing, on Sailfish.
- The backend has no way around it: the app builds the page and the window, and `Loaded` fires inside
  `new Window(page)`, before the backend receives the window.

## Steps to reproduce

1. Use the repro project in `repro/` (plain `net11.0` console app, `Microsoft.Maui.Controls` 11.0.0-rc.1.26451.6).
2. `dotnet run`

Expected (as on the in-box platforms): `Loaded` fires once the element is backed by a handler / platform view:

```
after new Window(page): page.IsLoaded = False
label.Loaded: label.Handler = BackendLabelHandler
after the handler: label.Handler = BackendLabelHandler, label.IsLoaded = True
```

Actual:

```
page.Loaded:  page.Handler = null, label.Handler = null
label.Loaded: label.Handler = null
after new Window(page): page.IsLoaded = True
after the handler: label.Handler = BackendLabelHandler, label.IsLoaded = True
```

## Environment

- .NET SDK 11.0.100-rc.1.26425.128
- Microsoft.Maui.Controls 11.0.0-rc.1.26451.6
- TFM: `net11.0` (the platform-neutral build of `Microsoft.Maui.Controls.dll`)
- Seen on: a Sailfish OS backend (root page `Loaded` handlers see no handler); the repro needs no device

## Code

`Microsoft.Maui.Controls.VisualElement`, `net11.0` build (decompiled from 11.0.0-rc.1.26451.6):

```csharp
public bool IsLoaded => Window != null;

private protected override void OnHandlerChangedCore()
{
    base.OnHandlerChangedCore();
    IsPlatformEnabled = Handler != null;
    UpdatePlatformUnloadedLoadedWiring(Window);
}

private void HandlePlatformUnloadedLoaded()
{
    if (IsLoaded) SendLoaded(updateWiring: false);
    else SendUnloaded(updateWiring: false);
}
```

## Proposal

Make the plain-TFM `IsLoaded` also require a handler:

```csharp
// net/netstandard partial
public bool IsLoaded => Window != null && IsPlatformEnabled;   // IsPlatformEnabled == (Handler != null)
```

Nothing else needs to change. `OnHandlerChangedCore` already sets `IsPlatformEnabled` and re-runs
`UpdatePlatformUnloadedLoadedWiring(Window)`, so `Loaded` would fire when the handler attaches (or when the window is
set, whichever comes last). `Unloaded` would fire when the handler is disconnected, which matches the in-box platforms,
where `IsLoaded` is false without a platform view.

Compatibility: code that uses Controls on the plain TFM without any handlers (unit tests of view models and layouts)
would no longer see `Loaded`. If that matters, the change could apply only once the `Window` has a handler, or be
opt-in through an `AppContext` switch that a backend sets.

## Workaround in use

The Sailfish backend documents the gap and points apps to `OnAppearing` / `HandlerChanged` for work that needs the
platform view. For pages the backend constructs itself (Shell route pages), it attaches handlers in `ParentChanging`
before the `Window` is set, so their `Loaded` already sees a handler. The root page and pages the app constructs
(`PushAsync(new MyPage())`) cannot be covered that way.
