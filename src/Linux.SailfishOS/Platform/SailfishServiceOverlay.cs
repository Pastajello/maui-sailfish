using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.SailfishOS.Handlers;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Service provider that falls back to the Sailfish defaults, so apps on plain <c>UseMauiApp&lt;T&gt;()</c> still get the
/// platform services. Services the app registered win.
/// </summary>
internal sealed class SailfishServiceOverlay : IServiceProvider
{
	private readonly IServiceProvider _inner;
	private SailfishDispatcherProvider? _dispatcherProvider;
	private SailfishHandlersFactory? _handlersFactory;
	private SailfishFontManager? _fontManager;
	private QtHostAlertSubscription? _alertSubscription;
	private SailfishModalNavigationPlatformFactory? _modalFactory;
	private Graphics.SailfishImageLoadingService? _imageLoading;
	private Microsoft.Maui.Animations.AnimationManager? _animationManager;

	// The window's render session: the host cache handlers and the renderer share, the renderer once it runs, and the
	// Shell route-page flag (SailfishShellHandler sets it between Navigating and Navigated).
	private readonly SailfishRenderSession _session = new();

	public SailfishServiceOverlay(IServiceProvider inner) =>
		_inner = inner ?? throw new ArgumentNullException(nameof(inner));

	public object? GetService(Type serviceType) => Resolve(serviceType, _inner);

	/// <summary>The overlay's decision for <paramref name="serviceType"/> over the registrations of
	/// <paramref name="registered"/> (the application provider, or a window's scope of it); the Sailfish instances are
	/// this overlay's, whichever provider asks.</summary>
	internal object? Resolve(Type serviceType, IServiceProvider registered)
	{
		if (serviceType == typeof(SailfishRenderSession))
			return _session;
		// The dispatcher must be the Qt-loop-backed one: any other queue never drains. IDispatcher is not looked up in
		// the registrations: MAUI's factory for it (AppHostBuilderExtensions.GetDispatcher) calls
		// DispatcherProvider.SetCurrent with the registered provider, which under plain UseMauiApp is MAUI's own.
		// That silently swapped the global provider back, Dispatcher.GetForCurrentThread() turned null on the Qt
		// thread and every renderer kick (DispatchDelayed) was dropped: chunked creates crawled on the heartbeat poll.
		if (serviceType == typeof(IDispatcher))
			return ((IDispatcherProvider)GetService(typeof(IDispatcherProvider))!).GetForCurrentThread();

		var existing = registered.GetService(serviceType);

		// MAUI builds Shell route pages through these services (ActivatorUtilities.GetServiceOrCreateInstance). The
		// plain-net Controls report Loaded as soon as the page joins the window, which here is before the renderer
		// attached any handler, so a Loaded handler touching one (WhatToEat: searchBar.SetSemanticFocus()) threw inside
		// MAUI's push and aborted it half way. Only during a pushed route navigation: a ShellContent whose page comes
		// from the services marks it service-created and rebuilds it every time its section is shown again
		// (DeveloperBalance's dashboard re-ran its selection command and crashed it).
		if (existing is null && _session.RoutePageNavigation && !serviceType.IsAbstract &&
			typeof(Microsoft.Maui.Controls.Page).IsAssignableFrom(serviceType) && _session.Renderer is { } renderer)
			return CreatePage(serviceType, registered, renderer.MauiContext);

		if (serviceType == typeof(IDispatcherProvider))
		{
			if (existing is SailfishDispatcherProvider sailfishProvider)
				return sailfishProvider;
			return _dispatcherProvider ??= new SailfishDispatcherProvider();
		}
		if (serviceType == typeof(IMauiHandlersFactory))
		{
			// The stock factory hands views to official MAUI handlers, which throw here.
			if (existing is SailfishHandlersFactory sailfishFactory)
				return sailfishFactory;
			return _handlersFactory ??= new SailfishHandlersFactory(this);
		}
		if (serviceType == typeof(IFontManager))
			return _fontManager ??= new SailfishFontManager();
		// Essentials: an app's registration wins, else the registry's default (the instance the facades hold too).
		if (SailfishEssentialsRegistry.Find(serviceType) is { } essential)
			return existing ?? SailfishEssentialsRegistry.DefaultFor(essential);
		// Animations tick on Qt's frame clock. MAUI's own manager (its plain-net timer ticker) gives way; an app's wins.
		if (serviceType == typeof(Microsoft.Maui.Animations.IAnimationManager) &&
		    (existing is null || existing.GetType() == typeof(Microsoft.Maui.Animations.AnimationManager) &&
		     ((Microsoft.Maui.Animations.IAnimationManager)existing).Ticker.GetType() == typeof(Microsoft.Maui.Animations.PlatformTicker)))
			return _animationManager ??= new Microsoft.Maui.Animations.AnimationManager(new QtHost.SailfishFrameTicker());
		if (existing is not null)
			return existing;

		// IImage on QImage (S49): MAUI's plain-net PlatformImage cannot resize.
		if (serviceType == typeof(Microsoft.Maui.Graphics.IImageLoadingService))
			return _imageLoading ??= new Graphics.SailfishImageLoadingService();

		if (serviceType == typeof(Controls.Platform.IAlertManagerSubscription))
			return _alertSubscription ??= new QtHostAlertSubscription(_session);
		if (serviceType == typeof(Controls.Platform.IModalNavigationPlatformFactory))
			return _modalFactory ??= new SailfishModalNavigationPlatformFactory(_session);
		return null;
	}

	/// <summary>The page ActivatorUtilities would create, with its handlers attached as it gets a parent: before the
	/// window reaches it and Loaded fires, as on the platforms whose Loaded waits for the native view.</summary>
	private Microsoft.Maui.Controls.Page? CreatePage(Type pageType, IServiceProvider registered, IMauiContext context)
	{
		Microsoft.Maui.Controls.Page page;
		try
		{
			page = (Microsoft.Maui.Controls.Page)Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance(
				new ResolvingProvider(this, registered), pageType);
		}
		catch (InvalidOperationException)
		{
			return null;   // a constructor the services cannot satisfy: the caller's own fallback reports it
		}
		EventHandler<Microsoft.Maui.Controls.ParentChangingEventArgs>? onParenting = null;
		onParenting = (_, e) =>
		{
			if (e.NewParent is null)
				return;
			page.ParentChanging -= onParenting;
			if (page.Handler is null)
				QtHostLayout.AttachHandlers(page, context);
		};
		page.ParentChanging += onParenting;
		return page;
	}

	/// <summary>Constructor arguments resolve through the overlay over the same registrations.</summary>
	private sealed class ResolvingProvider(SailfishServiceOverlay overlay, IServiceProvider registered) : IServiceProvider
	{
		public object? GetService(Type serviceType) => overlay.Resolve(serviceType, registered);
	}
}
