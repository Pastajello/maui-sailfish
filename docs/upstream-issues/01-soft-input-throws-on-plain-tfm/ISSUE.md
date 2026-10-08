# `SoftInputExtensions` throw `NotSupportedException` on the plain `net11.0` TFM once a handler has a platform view

## Description

On the platform-neutral `net11.0` (and `netstandard`) build of `Microsoft.Maui.dll`, the public soft-input API:

- `HideSoftInputAsync(this ITextInput, CancellationToken)`
- `ShowSoftInputAsync(this ITextInput, CancellationToken)`
- `IsSoftInputShowing(this ITextInput)`

throws `NotSupportedException` as soon as the view's handler has a non-null `PlatformView`.

The private per-platform helpers this API dispatches to (`HideSoftInput(this object)`, `ShowSoftInput(this object)`,
`IsSoftInputShowing(this object)`) are implemented on the plain TFM as `throw new NotSupportedException()`.

When there is no handler or no platform view, the same methods return `false`. The XML docs promise the same:
"Returns `true` if the platform was able to hide the soft input pane".

On the plain TFM this only matters to a backend that is not one of the in-box platforms, for example a Linux backend
built on MAUI 11's support for out-of-tree backends. In such a backend every text input has a platform view, so a
common app pattern crashes the call site:

```csharp
async void OnCompleted(object sender, EventArgs e) => await entry.HideSoftInputAsync(CancellationToken.None);
```

The backend cannot intercept any of this. These are static extension methods with no handler, service or interface
in between, so the backend has to tell app authors to avoid a documented cross-platform API.

## Why this reaches a platform TFM

A backend usually has its own platform TFM (here `net11.0-sailfish`), but MAUI's packages ship no assets for it, so
NuGet resolves the nearest framework: `lib/net11.0`. For example, the Sailfish head's `project.assets.json` lists
`lib/net11.0/Microsoft.Maui.dll` and `lib/net11.0/Microsoft.Maui.Controls.dll`. Every out-of-tree backend therefore
runs on the platform-neutral build, and this neutral-build behaviour is what its apps see on the device.

For Sailfish OS in particular:

- Every text input (`Entry`, `Editor`, `SearchBar`) has a platform view, a QML text field, from its first render on,
  so `IsSoftInputShowing()` and `HideSoftInputAsync()` throw for every Sailfish app.
- The phone has no hardware keyboard and Maliit is the only keyboard, so hiding it on submit is one of the most
  common calls in a ported app.
- The app does not have to call the API itself for this to hurt: a shared library or a toolkit behaviour that calls
  `HideSoftInputAsync` (keyboard-dismiss behaviours, "hide keyboard on Completed") throws on the Sailfish head only.

## Steps to reproduce

1. Use the repro project in `repro/` (plain `net11.0` console app, `Microsoft.Maui.Controls` 11.0.0-rc.1.26451.6).
   It gives an `Entry` a minimal `ViewHandler<IEntry, object>` whose platform view is a plain object, as a custom
   backend's handler would.
2. `dotnet run`

Expected:

```
PlatformView: BackendTextField
IsSoftInputShowing() -> False
HideSoftInputAsync() -> False
```

(or, better, a way for the backend to answer; see the proposal below)

Actual:

```
PlatformView: BackendTextField
IsSoftInputShowing() -> System.NotSupportedException
HideSoftInputAsync() -> System.NotSupportedException
```

`ShowSoftInputAsync` behaves the same. When the view is not focused, the exception surfaces through the returned
task instead of synchronously (it is thrown inside the dispatched callback).

## Environment

- .NET SDK 11.0.100-rc.1.26425.128
- Microsoft.Maui.Controls / Microsoft.Maui.Core 11.0.0-rc.1.26451.6
- TFM: `net11.0` (the platform-neutral build of `Microsoft.Maui.dll`)
- Seen on: a Sailfish OS backend, where the app code above runs on a device; the repro needs no device

## Code

`Microsoft.Maui.SoftInputExtensions` in the `net11.0` build (decompiled from 11.0.0-rc.1.26451.6):

```csharp
public static Task<bool> HideSoftInputAsync(this ITextInput targetView, CancellationToken token)
{
    token.ThrowIfCancellationRequested();
    if (!targetView.TryGetPlatformView(out object platformView, out _, out _))
        return Task.FromResult(false);
    return Task.FromResult(platformView.HideSoftInput());   // throws below
}

private static bool HideSoftInput(this object _) => throw new NotSupportedException();
private static bool ShowSoftInput(this object _) => throw new NotSupportedException();
private static bool IsSoftInputShowing(this object _) => throw new NotSupportedException();
```

## Proposal

Either of these would work. The first is the smaller change.

1. **Return `false` on the plain TFM** instead of throwing, matching the no-platform-view branch and the XML docs
   ("Returns `true` if the platform was able to …"). Apps would then degrade gracefully everywhere.
2. **A seam for backends.** On the plain TFM, ask the platform view (or a service from the handler's `MauiContext`)
   before giving up:

   ```csharp
   // net/netstandard partial only
   public interface ISoftInputPlatformView   // name is illustrative
   {
       bool HideSoftInput();
       bool ShowSoftInput();
       bool IsSoftInputShowing { get; }
   }

   private static bool HideSoftInput(this object platformView) =>
       platformView is ISoftInputPlatformView v && v.HideSoftInput();
   ```

   A backend whose input method can be shown and hidden (Qt's `QInputMethod`, GTK's input method contexts) could then
   implement the documented API rather than ask apps to avoid it.

## Workaround in use

The Sailfish backend offers its own `SailfishKeyboard.Show(view)` / `Hide()` / `IsShowing` (Qt `inputMethod`) and
documents that `HideSoftInputAsync`/`ShowSoftInputAsync`/`IsSoftInputShowing` throw on this TFM. Apps have to put
`#if` around every call.
