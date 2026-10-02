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
	private SailfishClipboard? _clipboard;
	private SailfishBrowser? _browser;
	private SailfishLauncher? _launcher;
	private SailfishAppInfo? _appInfo;
	private SailfishPreferences? _preferences;
	private SailfishSecureStorage? _secureStorage;
	private SailfishFileSystem? _fileSystem;
	private SailfishDeviceInfo? _deviceInfo;
	private SailfishDeviceDisplay? _deviceDisplay;
	private SailfishBattery? _battery;
	private SailfishConnectivity? _connectivity;
	private SailfishVibration? _vibration;
	private SailfishHapticFeedback? _haptics;
	private SailfishSemanticScreenReader? _screenReader;
	private readonly Dictionary<Type, object> _f4 = new();
	private QtHostAlertSubscription? _alertSubscription;
	private SailfishModalNavigationPlatformFactory? _modalFactory;

	/// <summary>A Shell push or insert is under way (SailfishShellHandler, between Navigating and Navigated): the
	/// route pages it builds get their handlers as they join the stack.</summary>
	internal static bool RoutePageNavigation;

	public SailfishServiceOverlay(IServiceProvider inner) =>
		_inner = inner ?? throw new ArgumentNullException(nameof(inner));

	public object? GetService(Type serviceType) => Resolve(serviceType, _inner);

	/// <summary>The overlay's decision for <paramref name="serviceType"/> over the registrations of
	/// <paramref name="registered"/> (the application provider, or a window's scope of it); the Sailfish instances are
	/// this overlay's, whichever provider asks.</summary>
	internal object? Resolve(Type serviceType, IServiceProvider registered)
	{
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
		if (existing is null && RoutePageNavigation && !serviceType.IsAbstract &&
			typeof(Microsoft.Maui.Controls.Page).IsAssignableFrom(serviceType) && QtHostPageRenderer.Current is { } renderer)
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
		if (serviceType == typeof(Microsoft.Maui.ApplicationModel.DataTransfer.IClipboard))
			return existing ?? (_clipboard ??= new SailfishClipboard());
		if (serviceType == typeof(Microsoft.Maui.ApplicationModel.IBrowser))
			return existing ?? (_browser ??= new SailfishBrowser());
		if (serviceType == typeof(Microsoft.Maui.ApplicationModel.ILauncher))
			return existing ?? (_launcher ??= new SailfishLauncher());
		if (serviceType == typeof(Microsoft.Maui.ApplicationModel.IAppInfo))
			return existing ?? (_appInfo ??= new SailfishAppInfo());
		if (serviceType == typeof(Microsoft.Maui.Storage.IPreferences))
			return existing ?? (_preferences ??= new SailfishPreferences());
		if (serviceType == typeof(Microsoft.Maui.Storage.ISecureStorage))
			return existing ?? (_secureStorage ??= new SailfishSecureStorage());
		if (serviceType == typeof(Microsoft.Maui.Storage.IFileSystem))
			return existing ?? (_fileSystem ??= new SailfishFileSystem());
		// Platform services
		if (serviceType == typeof(Microsoft.Maui.Devices.IDeviceInfo))
			return existing ?? (_deviceInfo ??= new SailfishDeviceInfo());
		if (serviceType == typeof(Microsoft.Maui.Devices.IDeviceDisplay))
			return existing ?? (_deviceDisplay ??= new SailfishDeviceDisplay());
		if (serviceType == typeof(Microsoft.Maui.Devices.IBattery))
			return existing ?? (_battery ??= new SailfishBattery());
		if (serviceType == typeof(Microsoft.Maui.Networking.IConnectivity))
			return existing ?? (_connectivity ??= new SailfishConnectivity());
		if (serviceType == typeof(Microsoft.Maui.Devices.IVibration))
			return existing ?? (_vibration ??= new SailfishVibration());
		if (serviceType == typeof(Microsoft.Maui.Devices.IHapticFeedback))
			return existing ?? (_haptics ??= new SailfishHapticFeedback());
		// MAUI registers its own reference-assembly reader (it throws); only an app's registration wins.
		if (serviceType == typeof(Microsoft.Maui.Accessibility.ISemanticScreenReader))
			return existing is not null && existing.GetType().Assembly != serviceType.Assembly
				? existing
				: _screenReader ??= new SailfishSemanticScreenReader();
		if (existing is null && CreateF4(serviceType) is { } created)
			return created;
		if (existing is not null)
			return existing;

		if (serviceType == typeof(Controls.Platform.IAlertManagerSubscription))
			return _alertSubscription ??= new QtHostAlertSubscription();
		if (serviceType == typeof(Controls.Platform.IModalNavigationPlatformFactory))
			return _modalFactory ??= new SailfishModalNavigationPlatformFactory();
		return null;
	}

	/// <summary>Sensor, location and sharing services (one instance each).</summary>
	private object? CreateF4(Type serviceType)
	{
		if (_f4.TryGetValue(serviceType, out var cached))
			return cached;
		object? created =
			serviceType == typeof(Microsoft.Maui.Devices.Sensors.IAccelerometer) ? new SailfishAccelerometer()
			: serviceType == typeof(Microsoft.Maui.Devices.Sensors.IGyroscope) ? new SailfishGyroscope()
			: serviceType == typeof(Microsoft.Maui.Devices.Sensors.IMagnetometer) ? new SailfishMagnetometer()
			: serviceType == typeof(Microsoft.Maui.Devices.Sensors.ICompass) ? new SailfishCompass()
			: serviceType == typeof(Microsoft.Maui.Devices.Sensors.IBarometer) ? new SailfishBarometer()
			: serviceType == typeof(Microsoft.Maui.Devices.Sensors.IOrientationSensor) ? new SailfishOrientationSensor()
			: serviceType == typeof(Microsoft.Maui.Devices.Sensors.IGeolocation) ? new SailfishGeolocation()
			: serviceType == typeof(Microsoft.Maui.ApplicationModel.DataTransfer.IShare) ? new SailfishShare()
			: serviceType == typeof(Microsoft.Maui.Media.IMediaPicker) || serviceType == typeof(Microsoft.Maui.Storage.IFilePicker)
				? Shared<SailfishPickers>()
			: serviceType == typeof(Microsoft.Maui.ApplicationModel.IPermissions) ? new SailfishPermissions()
			: serviceType == typeof(Microsoft.Maui.ApplicationModel.Communication.IPhoneDialer)
			  || serviceType == typeof(Microsoft.Maui.ApplicationModel.Communication.IEmail)
			  || serviceType == typeof(Microsoft.Maui.ApplicationModel.Communication.ISms)
			  || serviceType == typeof(Microsoft.Maui.ApplicationModel.IMap)
				? Shared<SailfishCommunication>()
			: serviceType == typeof(Microsoft.Maui.Media.IScreenshot) ? new SailfishScreenshot()
			: null;
		if (created is not null)
			_f4[serviceType] = created;
		return created;
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

	/// <summary>One instance serving several interfaces.</summary>
	private T Shared<T>() where T : class, new()
	{
		if (!_f4.TryGetValue(typeof(T), out var instance))
			_f4[typeof(T)] = instance = new T();
		return (T)instance;
	}
}
