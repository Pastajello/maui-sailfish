# Sailfish handlers for custom and library controls

On Android a control library ships `MyViewHandler : ViewHandler<IMyView, AppCompatTextView>` and registers it
with `AddHandler`. Sailfish follows the same model, except that the native view is a QML adapter object hosted
by the backend: `NativeElementHost`.

## Customizing a built-in control

Every Sailfish handler publishes its mapper the way MAUI handlers do (`SailfishLabelHandler.Mapper`,
`SailfishEntryHandler.Mapper`, …). The mapper chains from `SailfishViewMapper.Mapper` (the generic view state), which
chains from `ViewHandler.ViewMapper`, and `handler.PlatformView` is the control's `NativeElementHost`:

```csharp
#if SAILFISH
SailfishLabelHandler.Mapper.AppendToMapping("Trace", (handler, label) =>
{
    NativeElementHost host = handler.PlatformView;   // QmlUri "label", Id "e17", …
    Console.WriteLine($"{host} shows {label.Text}");
});
#endif
```

The built-in mappers differ from Android's in one way: they are snapshot mappers. Every key of a control's family
(for a Label: `Text`, `TextColor`, `FontSize`, `HorizontalTextAlignment`, …) runs the same action, which sends the
family's complete state to the adapter in one batch. Two things follow:

- Replacing or skipping a key with `ModifyMapping` does not keep that value from the adapter: the next change of any
  key in the family sends it again with the rest.
- A value an `AppendToMapping` action writes to the adapter by other means lasts only until that next send.

Use `AppendToMapping` for side effects: tracing, reading the host, hooking events. To change what a control shows,
set the MAUI property, or give the control an adapter of its own (below).

A handler registered by the app wins over the Sailfish one, whether it replaces a built-in control or serves a
new one:

```csharp
builder.ConfigureMauiHandlers(handlers =>
{
#if SAILFISH
    handlers.AddHandler<Label, MyLabelHandler>();          // MyLabelHandler : SailfishLabelHandler
    handlers.AddHandler<RatingView, RatingViewHandler>();  // a library control, below
#endif
});
```

A subclass can bring its own mapper and command mapper, as with `LabelHandler(mapper, commandMapper)` elsewhere.
Chain it from the built-in one: the keys that send the snapshot are read from the chain, so the family still sends
as one batch, and the subclass's own keys run beside it:

```csharp
#if SAILFISH
public class MyLabelHandler : SailfishLabelHandler
{
    public static readonly PropertyMapper<ILabel, MyLabelHandler> MyMapper = new(SailfishLabelHandler.Mapper)
    {
        ["Badge"] = (handler, label) => { /* a side effect */ },
    };

    public MyLabelHandler() : base(MyMapper) { }
}
#endif
```

Every Sailfish view handler has these two constructors (`()` and `(IPropertyMapper? mapper, CommandMapper? commandMapper = null)`),
pages and page containers (`SailfishPageHandler`, `SailfishNavigationViewHandler`, `SailfishTabbedPageHandler`,
`SailfishFlyoutPageHandler`, `SailfishShellHandler`) included; their mappers chain from `SailfishViewMapper.Mapper`
too.

Resolution walks the view's type hierarchy. At each level a registration from the app or a library wins, and a
stock MAUI handler (it throws on this TFM) gives way to the Sailfish handler for that exact type. A type that
nothing claims falls back to the nearest Sailfish base handler.

## A library control with its own adapter

A control deriving from `View` that the backend does not know gets a plain container host, so its children
still paint. To render it natively, the library ships three pieces.

**1. The handler.** It names the adapter and pushes the adapter's state. Every key in the list re-sends the whole
snapshot in one batch, so related values never arrive half-updated:

```csharp
#if SAILFISH
using Microsoft.Maui.SailfishOS.Handlers;

public class RatingViewHandler : SailfishSnapshotHandler
{
    public RatingViewHandler()
        : base(new[] { nameof(RatingView.Value), nameof(RatingView.Maximum) },
               (view, widthConstraint, heightConstraint) => new Size(Math.Min(widthConstraint, 240), 48))
    {
    }

    protected override string? AdapterUri => "rating-view";

    protected override Dictionary<string, object?>? Snapshot(IView view) =>
        view is RatingView rating
            ? new() { ["value"] = rating.Value, ["maximum"] = rating.Maximum }
            : null;

    // Events the adapter raises with mauiEvent(name, payload); payload carries the host "id".
    protected override void OnAdapterEvent(string name, JsonElement payload)
    {
        if (name == "rating-changed" && ConnectedView is RatingView rating)   // null once disconnected
            rating.Value = payload.GetProperty("value").GetInt32();
    }
}
#endif
```

A control with a Core interface of its own can derive from `SailfishSnapshotHandler<IMyView>` instead, and
publish a static `Mapper` built with `SnapshotMapper<THandler>(keys)`, as the built-in handlers do. The measure
function returns the control's size in dp; without one the backend uses a generic estimate.

**2. The adapter registration**, in the library's builder extension:

```csharp
public static MauiAppBuilder UseRatingControls(this MauiAppBuilder builder)
{
#if SAILFISH
    Microsoft.Maui.SailfishOS.Platform.QtHost.QtHostAdapters.Register("rating-view", "RatingControls/RatingView.qml");
    builder.ConfigureMauiHandlers(handlers => handlers.AddHandler<RatingView, RatingViewHandler>());
#endif
    return builder;
}
```

The path is relative to the app's `qml/` directory; an absolute `file:///` URL works too.

**3. The QML adapter**, copied into the app's `qml/` directory:

```xml
<ItemGroup Condition="'$(TargetPlatformIdentifier)' == 'sailfish'">
  <Content Include="Sailfish/qml/**/*.qml" Link="qml/RatingControls/%(RecursiveDir)%(Filename)%(Extension)"
           CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

A NuGet package needs a `buildTransitive` target that adds the same `Content` items in the consuming app, the way
`Microsoft.Maui.SailfishOS` adds its own `qml/`.

The adapter implements the backend's contract. The generic properties (opacity, enabled, background, semantics,
shadow, clip) are handled for every item by the host, so the adapter declares only its own:

```qml
import QtQuick 2.6
import Sailfish.Silica 1.0

Row {
    id: root

    // Contract: every adapter declares these three.
    property string mauiId: ""
    property string mauiProbe: ""
    signal mauiEvent(string name, string payload)

    // True while managed values are applied: raise no events for them (no echo loop).
    property bool mauiApplying: false

    // The handler's snapshot keys. An undeclared name rejects the whole property batch.
    property int value: 0
    property int maximum: 5

    Repeater {
        model: root.maximum
        IconButton {
            icon.source: index < root.value ? "image://theme/icon-m-favorite-selected" : "image://theme/icon-m-favorite"
            onClicked: if (!root.mauiApplying)
                root.mauiEvent("rating-changed", JSON.stringify({ id: root.mauiId, value: index + 1 }))
        }
    }
}
```

The backend sets the adapter's geometry from the MAUI layout pass. Silica items that bind their own
`width`/`height` to their content need plain `width: 0; height: 0`, or the bindings overwrite the managed
geometry.

State crosses as properties, actions as commands. A one-shot action (scroll to an item, run a script, open a
panel) is a call of the adapter's `mauiCommand(json)` function, made once per request with `{"name": …}` plus its
arguments. The handler sends it with `SendCommand`; before the adapter exists there is nothing to act on and
`SendCommand` returns false:

```csharp
public void Reset() => SendCommand("reset", new() { ["to"] = 0 });   // in the handler
```

```qml
function mauiCommand(json) {
    var c = JSON.parse(json);
    if (c.name === "reset")
        root.value = c.to;
}
```

Do not model an action as a property plus a counter that changes to re-fire an equal value: the counter is state
the adapter keeps for nothing, and an equal value pushed twice is dropped by the property diff.

## A control that draws its own pixels

A library that renders with its own engine (a raster canvas, a video frame) uses the generic drawing surface
instead of a QML adapter. `Microsoft.Maui.SailfishOS.SkiaSharp` is built this way: it is the Sailfish counterpart
of SkiaSharp's Android platform views.

```csharp
public class MyCanvasHandler : SailfishViewHandler<IMyCanvas>
{
    private readonly Action _paint;

    public MyCanvasHandler() : base(Mapper) => _paint = Paint;

    protected override string? AdapterUri => QtHostSurface.AdapterUri;   // an empty item the surface fills

    protected override void ConnectHandler(NativeElementHost host)
    {
        base.ConnectHandler(host);
        // A re-created QML object (navigation back, a recycled row) starts empty and with touch off.
        host.Attached += _ => { QtHostSurface.SetTouch(host, OnTouch); QtHostSurface.RequestFrame(_paint); };
    }

    // InvalidateSurface → QtHostSurface.RequestFrame(_paint): runs once in the next frame, however often asked.
    private void Paint()
    {
        // Draw RGBA8888 premultiplied pixels into a reused buffer, then:
        QtHostSurface.Commit(PlatformView, pixels, widthPx, heightPx, rowBytes);   // copied; reuse the buffer
    }

    // Synchronous, during Qt's event delivery; the first Pressed's result decides whether the surface keeps the
    // gesture, as an unhandled ACTION_DOWN does on Android. A parent Flickable taking the drag sends Cancelled.
    private bool OnTouch(SurfaceTouch touch) => true;
}
```

The pixels are shown 1:1 from the host's top-left corner, so size the buffer from the arranged frame in device
pixels. When a library's plain-`net` handler is a stub, its Sailfish handler replaces it wherever the app registers
it with `SailfishHandlersFactory.ReplaceLibraryHandler<TStub, TSailfish>()`. The package calls that from a
registrar the app assembly names with `[assembly: AssemblyMetadata("Microsoft.Maui.SailfishOS.Extension",
"Type, Assembly")]`, written by its buildTransitive targets (`SailfishExtensions`). Library image sources resolve
through `QtHostImageSources.Register`.

## Limits

- A registered adapter is used for controls that derive from `View` directly. A control deriving from a built-in
  one (`Entry`, `ContentView`, a `Layout`) keeps the built-in adapter; customize it through the built-in
  handler's `Mapper` instead.
- The adapter's children are not MAUI views: a control whose content is MAUI views (a templated control) should
  stay a `ContentView`/`TemplatedView` and let the backend host its content.
- Property changes reach the adapter only through the handler's mapper: the family snapshot for the handler's
  keys, the generic view state (opacity, enabled, background, semantics, …) for the keys of
  `SailfishViewMapper.Mapper`, which every Sailfish mapper chains from. The page reconcile still diffs the host as
  a safety net; with a complete snapshot it finds nothing to push.
