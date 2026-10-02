using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;

namespace Microsoft.Maui.SailfishOS.Handlers;

/// <summary>
/// Handler factory for Sailfish. Views resolve like MAUI's own factory — the most derived registration wins, the
/// last one per type — except that a stock MAUI handler (its CreatePlatformView throws on this TFM) gives way to
/// the Sailfish handler for that exact type. An app or library registration therefore beats the Sailfish table,
/// and a type nothing claims falls back to the nearest Sailfish base row, then to <see cref="SailfishLayoutHandler"/>
/// (layouts), <see cref="SailfishPageHandler"/> (pages) or <see cref="SailfishContainerHandler"/>.
/// </summary>
public sealed class SailfishHandlersFactory : IMauiHandlersFactory
{
	private readonly IServiceProvider _services;
	private readonly IMauiHandlersCollection? _collection;

	/// <summary>Handler type a factory registration produces, learned from one instance.</summary>
	private readonly ConcurrentDictionary<ServiceDescriptor, Type> _factoryTypes = new();

	// MauiHandlersFactory is internal, so resolution is re-implemented over the public collection.
	public SailfishHandlersFactory(IServiceProvider services)
	{
		_services = services;
		_collection = services.GetService<IMauiHandlersCollection>();
	}

	public IElementHandler GetHandler(Type type)
	{
		ArgumentNullException.ThrowIfNull(type);
		return Resolve(type) switch
		{
			{ Descriptor: { ImplementationFactory: { } factory } } => (IElementHandler)factory(_services),
			{ Descriptor: { ImplementationType: { } implementation } } =>
				(IElementHandler)ActivatorUtilities.CreateInstance(_services, implementation),
			{ Row: { } row } => row.Create(),
			_ => throw new InvalidOperationException($"no Sailfish handler registered for {type}"),
		};
	}

	public IElementHandler GetHandler<T>() where T : IElement => GetHandler(typeof(T));

	public Type GetHandlerType(Type type)
	{
		ArgumentNullException.ThrowIfNull(type);
		return Resolve(type) switch
		{
			{ Descriptor: { } descriptor } => DescriptorType(descriptor)!,
			{ Row: { } row } => row.Handler,
			_ => throw new InvalidOperationException($"no Sailfish handler registered for {type}"),
		};
	}

	public IMauiHandlersCollection GetCollection() =>
		_collection ?? throw new InvalidOperationException("no handler collection registered");

	public object? GetService(Type serviceType) => _services.GetService(serviceType);

	/// <summary>The registration or Sailfish row that serves <paramref name="type"/>.</summary>
	private (ServiceDescriptor? Descriptor, HandlerRow? Row) Resolve(Type type)
	{
		var isView = typeof(IView).IsAssignableFrom(type);
		// The official menu handlers throw here (no platform menu layer), so menus get the no-op handler.
		if (typeof(BaseMenuItem).IsAssignableFrom(type) || typeof(MenuFlyout).IsAssignableFrom(type))
			return (null, NullElementRow);
		for (var t = type; t is not null; t = t.BaseType)
		{
			if (Registered(t) is { } descriptor && !IsStock(DescriptorType(descriptor)))
				return Replacement(DescriptorType(descriptor)) is { } replaced ? (null, replaced) : (descriptor, null);
			if (isView && ExactRow(t) is { } row)
				return (null, row);
			// A stock registration of a non-view element: the Sailfish application/window handlers, else the no-op one.
			if (!isView && Registered(t) is not null)
				return (null, typeof(IApplication).IsAssignableFrom(type) ? ApplicationRow
					: typeof(IWindow).IsAssignableFrom(type) ? WindowRow
					: NullElementRow);
		}
		// Interface registrations, as MAUI resolves them after the class chain.
		foreach (var contract in type.GetInterfaces())
			if (Registered(contract) is { } descriptor && !IsStock(DescriptorType(descriptor)))
				return Replacement(DescriptorType(descriptor)) is { } replaced ? (null, replaced) : (descriptor, null);
		return isView ? (null, FallbackRow(type)) : (null, null);
	}

	/// <summary>The last registration for exactly <paramref name="serviceType"/>.</summary>
	private ServiceDescriptor? Registered(Type serviceType)
	{
		if (_collection is null)
			return null;
		ServiceDescriptor? found = null;
		foreach (var descriptor in _collection)
			if (descriptor.ServiceType == serviceType &&
			    (descriptor.ImplementationType is not null || descriptor.ImplementationFactory is not null))
				found = descriptor;
		return found;
	}

	private Type? DescriptorType(ServiceDescriptor descriptor) =>
		descriptor.ImplementationType ??
		(descriptor.ImplementationFactory is { } factory
			? _factoryTypes.GetOrAdd(descriptor, _ => factory(_services).GetType())
			: null);

	/// <summary>Handlers shipped by MAUI itself: they target the stock platforms and throw on this TFM.</summary>
	private static bool IsStock(Type? handler)
	{
		if (handler is null)
			return true;
		var assembly = handler.Assembly;
		if (assembly == typeof(SailfishHandlersFactory).Assembly)
			return false;
		var name = assembly.GetName().Name ?? string.Empty;
		return name == "Microsoft.Maui" || name.StartsWith("Microsoft.Maui.", StringComparison.Ordinal);
	}

	private static readonly List<HandlerRow> LibraryReplacements = new();

	/// <summary>
	/// Serves every registration of <typeparamref name="TLibraryHandler"/> (or a subclass of it) with
	/// <typeparamref name="TSailfishHandler"/>. For a library whose plain-<c>net</c> handler is a stub with no platform
	/// view (SkiaSharp's <c>SKCanvasViewHandler</c>), so the Sailfish implementation wins wherever and whenever the
	/// app registers the library. Call it before the app's handlers resolve, from a Sailfish extension
	/// (<see cref="Platform.SailfishExtensions"/>).
	/// </summary>
	public static void ReplaceLibraryHandler<TLibraryHandler, TSailfishHandler>()
		where TLibraryHandler : IElementHandler
		where TSailfishHandler : IElementHandler, new()
	{
		lock (LibraryReplacements)
		{
			LibraryReplacements.RemoveAll(r => r.View == typeof(TLibraryHandler));
			// The row's View holds the replaced library handler type here.
			LibraryReplacements.Add(Row<TLibraryHandler, TSailfishHandler>());
		}
	}

	private static HandlerRow? Replacement(Type? registered)
	{
		if (registered is null)
			return null;
		lock (LibraryReplacements)
			foreach (var row in LibraryReplacements)
				if (row.View.IsAssignableFrom(registered))
					return row;
		return null;
	}

	/// <summary>One Sailfish handler row: the view type it serves and how to create it without reflection.</summary>
	internal sealed record HandlerRow(Type View, Type Handler, Func<IElementHandler> Create);

	private static HandlerRow Row<TView, THandler>() where THandler : IElementHandler, new() =>
		new(typeof(TView), typeof(THandler), static () => new THandler());

	private static readonly HandlerRow PageRow = Row<Page, SailfishPageHandler>();

	/// <summary>Attaches the handler of a window's root page, resolved from this registry's rows (so a NavigationPage
	/// root answers the navigation handshake).</summary>
	internal static IViewHandler AttachRootHandler(IView root, IMauiContext context)
	{
		var handler = (IViewHandler)Activator.CreateInstance(ResolveViewHandlerType(root.GetType()))!;
		handler.SetMauiContext(context);
		handler.SetVirtualView(root);
		return handler;
	}
	private static readonly HandlerRow LayoutRow = Row<ILayout, SailfishLayoutHandler>();
	private static readonly HandlerRow ContainerRow = Row<IView, SailfishContainerHandler>();

	/// <summary>The handler of a view no row serves: a page's (no host of its own), a layout's, or a plain container's.</summary>
	private static HandlerRow FallbackRow(Type type) =>
		typeof(Page).IsAssignableFrom(type) ? PageRow
		: typeof(ILayout).IsAssignableFrom(type) ? LayoutRow
		: ContainerRow;
	private static readonly HandlerRow NullElementRow = Row<IElement, NullElementHandler>();
	private static readonly HandlerRow ApplicationRow = Row<IApplication, SailfishApplicationHandler>();
	private static readonly HandlerRow WindowRow = Row<IWindow, SailfishWindowHandler>();

	private static HandlerRow? ExactRow(Type type)
	{
		foreach (var row in ViewHandlers)
			if (row.View == type)
				return row;
		return null;
	}

	/// <summary>
	/// The Sailfish handler of the nearest built-in control <paramref name="type"/> derives from, ignoring app and
	/// library registrations; null for other views. The fallback when a library handler built on a stock one throws
	/// in CreatePlatformView on this TFM (UraniumUI's StatefulButtonHandler : ButtonHandler, Plainer's EntryView):
	/// the control then works without the library's platform tweaks instead of vanishing.
	/// </summary>
	internal static IElementHandler? BuiltInFallback(Type type)
	{
		for (var t = type; t is not null && t != typeof(View); t = t.BaseType)
			if (ExactRow(t) is { } row)
				return row.Create();
		return null;
	}

	/// <summary>The Sailfish handler type for a view type with no registrations (also used by the parity test).</summary>
	internal static Type ResolveViewHandlerType(Type type)
	{
		foreach (var row in ViewHandlers)
			if (row.View.IsAssignableFrom(type))
				return row.Handler;
		return FallbackRow(type).Handler;
	}

	/// <summary>View type → Sailfish handler; derived types precede their bases.</summary>
	internal static readonly HandlerRow[] ViewHandlers =
	{
		Row<NavigationPage, SailfishNavigationViewHandler>(),
		Row<Shell, SailfishShellHandler>(),
		Row<TabbedPage, SailfishTabbedPageHandler>(),
		Row<FlyoutPage, SailfishFlyoutPageHandler>(),
		Row<ScrollView, ScrollViewHandler>(),
		Row<ItemsView, SailfishListViewHandler>(),
		Row<IndicatorView, SailfishIndicatorViewHandler>(),
		Row<WebView, SailfishWebViewHandler>(),
		Row<SwipeView, SailfishSwipeViewHandler>(),
		Row<Stepper, SailfishStepperHandler>(),
		Row<Entry, SailfishEntryHandler>(),
		Row<Editor, SailfishEditorHandler>(),
		Row<SearchBar, SailfishSearchBarHandler>(),
		Row<CheckBox, SailfishCheckBoxHandler>(),
		Row<Switch, SailfishSwitchHandler>(),
		Row<Slider, SailfishSliderHandler>(),
		Row<ProgressBar, SailfishProgressBarHandler>(),
		Row<ActivityIndicator, SailfishActivityIndicatorHandler>(),
		Row<Picker, SailfishPickerHandler>(),
		Row<DatePicker, SailfishDatePickerHandler>(),
		Row<TimePicker, SailfishTimePickerHandler>(),
		Row<RadioButton, SailfishRadioButtonHandler>(),
		Row<Image, SailfishImageHandler>(),
		Row<ImageButton, SailfishImageHandler>(),   // implements IImage without deriving from Image
		Row<Border, SailfishBorderHandler>(),
#pragma warning disable CS0618 // Frame is obsolete but must stay routable.
		Row<Frame, SailfishBorderHandler>(),
#pragma warning restore CS0618
		Row<BoxView, SailfishShapeHandler>(),
		Row<Microsoft.Maui.Controls.Shapes.Shape, SailfishShapeHandler>(),
		Row<GraphicsView, SailfishGraphicsHandler>(),
		Row<Grid, SailfishGridHandler>(),
		Row<StackBase, SailfishStackHandler>(),
		Row<ContentView, SailfishContentViewHandler>(),
		Row<Label, SailfishLabelHandler>(),
		Row<Button, SailfishButtonHandler>(),
	};
}
