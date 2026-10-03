using System.Text.Json;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Handlers;

/// <summary>
/// Base of Sailfish control handlers, as <c>ViewHandler&lt;ILabel, AppCompatTextView&gt;</c> is on Android: the
/// platform view is the element's <see cref="NativeElementHost"/> (the QML adapter object), the same instance the
/// renderer reconciles. Mappers chain from <see cref="SailfishViewMapper.Mapper"/>, so apps extend them with
/// <c>AppendToMapping</c> and see the host as <c>handler.PlatformView</c>.
/// <para>Every change reaches the host through the mapper. While the handler connects, the mapper pass coalesces
/// into one batch: the adapter's state plus the generic view state.</para>
/// </summary>
public abstract class SailfishViewHandler<TVirtualView> : ViewHandler<TVirtualView, NativeElementHost>,
	ISailfishAdapterHandler, ISailfishViewHandler
	where TVirtualView : class, IView
{
	private readonly Func<IView, double, double, Size>? _measure;
	private bool _connecting;   // inside SetVirtualView: pushes wait for the one batch at its end

	/// <param name="measure">Control-specific measure run inside <see cref="SailfishMeasure.Frame"/>; null keeps
	/// the generic measure.</param>
	protected SailfishViewHandler(IPropertyMapper mapper, CommandMapper? commandMapper = null,
		Func<IView, double, double, Size>? measure = null)
		: base(mapper, commandMapper)
	{
		_measure = measure;
	}

	/// <summary>The adapters.json kind, or null while the control has no adapter of its own (the reconcile then
	/// chooses one and binds the host).</summary>
	protected virtual string? AdapterUri => null;

	/// <summary>The adapter's full state for its create op and the connect batch; null when the handler has none.</summary>
	protected virtual Dictionary<string, object?>? AdapterState() => null;

	/// <summary>An event the adapter raised (<c>mauiEvent(name, payload)</c>) that no built-in control consumes;
	/// <paramref name="payload"/> is the parsed JSON, including the host <c>id</c>.</summary>
	protected virtual void OnAdapterEvent(string name, JsonElement payload)
	{
	}

	/// <summary>Whether the mapper pass of the connect is running (pushes are coalesced into its one batch).</summary>
	protected bool IsConnecting => _connecting;

	/// <summary>The virtual view, or null once disconnected (the typed <c>VirtualView</c> throws then).</summary>
	protected TVirtualView? ConnectedView => ((IElementHandler)this).VirtualView as TVirtualView;

	string? ISailfishAdapterHandler.AdapterUri => AdapterUri;

	Dictionary<string, object?>? ISailfishAdapterHandler.AdapterState() =>
		ConnectedView is null ? null : AdapterState();

	void ISailfishAdapterHandler.OnAdapterEvent(string name, JsonElement payload) => OnAdapterEvent(name, payload);

	/// <summary>False when the adapter draws the whole control and the page reconcile must not host its MAUI children
	/// (a WebView, the default IndicatorView dot strip).</summary>
	protected virtual bool WalksChildren => true;

	bool ISailfishAdapterHandler.WalksChildren => WalksChildren;

	protected override NativeElementHost CreatePlatformView() => SailfishHandlerCore.HostFor(VirtualView, AdapterUri);

	/// <summary>As a native view goes with its handler: a host whose element left the page is destroyed now.</summary>
	protected override void DisconnectHandler(NativeElementHost platformView)
	{
		QtHostPageRenderer.Current?.OnHandlerDisconnected(platformView);
		base.DisconnectHandler(platformView);
	}

	/// <summary>Connects like any MAUI handler; the mapper pass it runs is sent as one batch.</summary>
	public override void SetVirtualView(IView view)
	{
		_connecting = true;
		try
		{
			base.SetVirtualView(view);
		}
		finally
		{
			_connecting = false;
		}
		var batch = AdapterState() ?? new Dictionary<string, object?>();
		if (ConnectedView is VisualElement visual)
			QtHostVisualState.Merge(batch, visual);   // as the reconcile merges it: generic state wins
		PushProps(batch);
	}

	public override void UpdateValue(string property)
	{
		SailfishHandlerCore.TraceUpdate(this, property);
		base.UpdateValue(property);
	}

	public override void Invoke(string command, object? args = null)
	{
		if (command is nameof(IView.Focus) or nameof(IView.Unfocus) &&
		    FocusNatively(command == nameof(IView.Focus)) is { } granted)
		{
			// Qt decided: IsFocused follows it, as the platforms write it back from their focus change.
			if (ConnectedView is VisualElement visual)
				visual.SetValue(VisualElement.IsFocusedPropertyKey, command == nameof(IView.Focus) && granted);
			(args as FocusRequest)?.TrySetResult(granted);
			return;
		}
		if (!SailfishHandlerCore.TryInvoke(this, command, args))
			base.Invoke(command, args);
	}

	/// <summary>Asks the adapter for native focus and returns whether Qt granted it; null when the control has no
	/// native focus (the request is then answered as before, by writing IsFocused).</summary>
	protected virtual bool? FocusNatively(bool focus) => null;

	/// <summary>Native focus of a text adapter (mauiFocus → activeFocus).</summary>
	protected bool? FocusTextInput(bool focus) =>
		((IElementHandler)this).PlatformView is NativeElementHost host ? QtHostPageRenderer.Current?.FocusHost(host, focus) : null;

	/// <summary>Records nothing itself (the geometry pass reads the arranged Bounds) and arranges the children, as a
	/// native container's layout pass would.</summary>
	public override void PlatformArrange(Rect frame)
	{
		base.PlatformArrange(frame);
		SailfishHandlerCore.ArrangeContent(ConnectedView, frame);
	}

	public override Size GetDesiredSize(double widthConstraint, double heightConstraint) =>
		SailfishHandlerCore.DesiredSize(VirtualView, widthConstraint, heightConstraint, _measure);

	/// <summary>Pushes adapter props (transient commands, snapshots) to this handler's host.</summary>
	/// <param name="yieldToNative">Skip the push while native state is being written back into MAUI (the value
	/// came from native, so pushing it would fight the adapter, e.g. a scroll in flight).</param>
	protected void PushProps(Dictionary<string, object?> props, bool yieldToNative = false)
	{
		// The typed PlatformView throws once disconnected.
		if (((IElementHandler)this).PlatformView is NativeElementHost host)
			QtHostPageRenderer.Current?.PushHostProps(host, props, yieldToNative);
	}

	/// <summary>Pushes transient native state (focus, caret) atomically; skipped while native writes it back.</summary>
	protected void PushTransient(params (string Name, object? Value)[] values)
	{
		if (((IElementHandler)this).PlatformView is NativeElementHost host)
			QtHostPageRenderer.Current?.PushTransient(host, values);
	}

	/// <summary>Whether the handler's mapper pushes <paramref name="propertyName"/> (the handler-parity measure).</summary>
	internal virtual bool Covers(string propertyName) => SailfishViewMapper.Covers(propertyName);

	void ISailfishViewHandler.PushViewState()
	{
		if (_connecting || ConnectedView is not VisualElement visual)
			return;
		var state = new Dictionary<string, object?>();
		QtHostVisualState.Merge(state, visual);
		PushProps(state, yieldToNative: true);
	}

	bool ISailfishViewHandler.Covers(string propertyName) => Covers(propertyName);
}

/// <summary>
/// The generic view state every Sailfish handler maps: opacity, enabled, z-order, background, semantics, flow
/// direction, shadow and clip land on standard QQuickItem properties, so adapters need not know them
/// (<see cref="QtHostVisualState"/>). A handler's own key of the same name wins, as its snapshot carries it.
/// Transforms and visibility ride the geometry pass instead.
/// </summary>
public static class SailfishViewMapper
{
	/// <summary>The generic keys; "ExcludedWithChildren" also re-maps every descendant.</summary>
	internal static readonly string[] Keys =
	{
		nameof(VisualElement.Opacity), nameof(VisualElement.IsEnabled), nameof(VisualElement.ZIndex),
		nameof(VisualElement.Background), nameof(VisualElement.BackgroundColor),
		nameof(Element.AutomationId), "Description", "Hint", "HeadingLevel", "IsInAccessibleTree",
		nameof(VisualElement.FlowDirection), nameof(VisualElement.Shadow), nameof(VisualElement.Clip),
	};

	internal const string ExcludedWithChildren = "ExcludedWithChildren";

	/// <summary>Keys that move a host without changing any measure: they ask for a geometry pass (the transforms
	/// and visibility ride it; flow direction mirrors the children there too).</summary>
	internal static readonly string[] GeometryKeys =
	{
		nameof(IView.Visibility), nameof(VisualElement.IsVisible),
		nameof(IView.TranslationX), nameof(IView.TranslationY), nameof(IView.Scale), nameof(IView.ScaleX),
		nameof(IView.ScaleY), nameof(IView.Rotation), nameof(IView.RotationX), nameof(IView.RotationY),
		nameof(IView.AnchorX), nameof(IView.AnchorY),
	};

	/// <summary>Chained from <see cref="ViewHandler.ViewMapper"/>; every Sailfish handler mapper chains from it.</summary>
	public static readonly PropertyMapper<IView, IViewHandler> Mapper = Build();

	private static PropertyMapper<IView, IViewHandler> Build()
	{
		var mapper = new PropertyMapper<IView, IViewHandler>(ViewHandler.ViewMapper);
		foreach (var key in Keys)
			mapper[key] = MapViewState;
		mapper[ExcludedWithChildren] = MapExcludedWithChildren;
		foreach (var key in GeometryKeys)
			mapper[key] = key is nameof(IView.Visibility) or nameof(VisualElement.IsVisible) ? MapGeometry : MapTransform;
		mapper[nameof(VisualElement.FlowDirection)] = static (handler, view) =>
		{
			MapViewState(handler, view);
			MapGeometry(handler, view);
		};
		return mapper;
	}

	/// <summary>Asks for a layout pass (visibility changes what the page measures).</summary>
	public static void MapGeometry(IViewHandler handler, IView view) => QtHostPageRenderer.Current?.RequestLayout();

	/// <summary>A transform (TranslationX/Y, Scale*, Rotation*, Anchor*) changes no measure in MAUI, so outside list
	/// rows it asks for a geometry pass only: an animation (TranslateTo, RotateTo, ScaleTo, a spinner) moves its host
	/// each frame without the whole page being measured and arranged. Row content is placed by its list's own pass.</summary>
	public static void MapTransform(IViewHandler handler, IView view)
	{
		if (QtHostPageRenderer.Current is not { } renderer)
			return;
		if (view is Element element && !InListRow(element))
			renderer.RequestScrollGeometry();
		else
			renderer.RequestLayout();
	}

	private static bool InListRow(Element element)
	{
		for (var e = element.Parent; e is not null; e = e.Parent)
			if (e is ItemsView)
				return true;
		return false;
	}

	/// <summary>Pushes the element's generic view state.</summary>
	public static void MapViewState(IViewHandler handler, IView view) =>
		(handler as ISailfishViewHandler)?.PushViewState();

	/// <summary>Accessibility exclusion covers the subtree: the descendants re-map it too (MAUI does not propagate
	/// this attached property).</summary>
	public static void MapExcludedWithChildren(IViewHandler handler, IView view)
	{
		MapViewState(handler, view);
		if (view is not IVisualTreeElement tree)
			return;
		foreach (var child in tree.GetVisualChildren())
			if (child is IView { Handler: { } childHandler })
				childHandler.UpdateValue(ExcludedWithChildren);
	}

	internal static bool Covers(string propertyName) =>
		propertyName == ExcludedWithChildren || Array.IndexOf(Keys, propertyName) >= 0 ||
		Array.IndexOf(GeometryKeys, propertyName) >= 0;
}

/// <summary>What the renderer asks a Sailfish handler for a view it has no built-in mapping for.</summary>
internal interface ISailfishAdapterHandler
{
	string? AdapterUri { get; }

	Dictionary<string, object?>? AdapterState();

	bool WalksChildren { get; }

	void OnAdapterEvent(string name, JsonElement payload);
}

/// <summary>The shared generic mapper reaches the typed handlers through this.</summary>
internal interface ISailfishViewHandler
{
	void PushViewState();

	bool Covers(string propertyName);
}
