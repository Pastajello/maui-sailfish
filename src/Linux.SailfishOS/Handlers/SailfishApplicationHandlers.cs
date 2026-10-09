using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.SailfishOS.Platform;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Handlers;

/// <summary>Application handler: Application.Quit() → Terminate ends the Qt/Silica loop (the app exits as from the
/// window's close), CloseWindow on the app's window does the same, OpenWindow is dropped (one window per app). The
/// platform application is the SailfishMauiApplication.</summary>
public class SailfishApplicationHandler : ElementHandler<IApplication, object>
{
	public static readonly PropertyMapper<IApplication, SailfishApplicationHandler> Mapper = new(ElementMapper);

	public static readonly CommandMapper<IApplication, SailfishApplicationHandler> CommandMapper = new(ElementCommandMapper)
	{
		["Terminate"] = MapTerminate,   // ApplicationHandler.TerminateCommandKey (internal): Application.Quit()
		["OpenWindow"] = MapOpenWindow,
		["CloseWindow"] = MapCloseWindow,
		["ActivateWindow"] = MapActivateWindow,
	};

	public SailfishApplicationHandler() : this(null)
	{
	}

	/// <summary>For a subclass that brings its own mappers, as MAUI's ApplicationHandler(mapper, commandMapper).</summary>
	public SailfishApplicationHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
		: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
	{
	}

	protected override object CreatePlatformElement() =>
		(object?)Microsoft.Maui.IPlatformApplication.Current ?? new object();

	/// <summary>Quits on the Qt thread, after the current loop turn.</summary>
	public static void MapTerminate(SailfishApplicationHandler handler, IApplication application, object? args) =>
		QtHostRuntime.Post(QtHostRuntime.Quit);

	/// <summary>A Sailfish app has one window, the Silica ApplicationWindow (lipstick shows one per app), so a second one
	/// is not opened: as on iOS without multiple scenes, the request is dropped, with a warning.</summary>
	public static void MapOpenWindow(SailfishApplicationHandler handler, IApplication application, object? args) =>
		QtHostDiag.Warn(QtHostDiagChannel.Navigation,
			"Application.OpenWindow: a Sailfish OS app has a single window; the new window is not opened");

	/// <summary>Application.ActivateWindow: the one window comes to the front (Silica ApplicationWindow.activate(), as
	/// lipstick does from the cover); a window this app does not show is ignored.</summary>
	public static void MapActivateWindow(SailfishApplicationHandler handler, IApplication application, object? args)
	{
		if (args is IWindow window && !ReferenceEquals(window, application.Windows.FirstOrDefault()))
			return;
		QtThread.Later(() => QtHostRuntime.Eval("(typeof window!=='undefined'&&window&&window.activate)?(window.activate(),'ok'):'no window'"));
	}

	/// <summary>Closing the app's window ends the app, as finishing the last activity does on Android.</summary>
	public static void MapCloseWindow(SailfishApplicationHandler handler, IApplication application, object? args)
	{
		if (args is IWindow window && !ReferenceEquals(window, application.Windows.FirstOrDefault()))
			return;   // not a window this app shows
		QtHostRuntime.Post(QtHostRuntime.Quit);
	}
}

/// <summary>Window handler: the Silica ApplicationWindow is the platform window (one per app); content changes follow
/// at once and density requests answer from the display.</summary>
public class SailfishWindowHandler : ElementHandler<IWindow, object>
{
	public static readonly PropertyMapper<IWindow, SailfishWindowHandler> Mapper = new(ElementMapper)
	{
		[nameof(IWindow.Content)] = MapContent,
		[nameof(IWindow.Title)] = MapTitle,
		[nameof(IWindow.FlowDirection)] = MapFlowDirection,
		[nameof(IToolbarElement.Toolbar)] = MapToolbar,
	};

	public static readonly CommandMapper<IWindow, SailfishWindowHandler> CommandMapper = new(ElementCommandMapper)
	{
		[nameof(IWindow.RequestDisplayDensity)] = static (_, _, args) =>
		{
			if (args is DisplayDensityRequest request)
				request.SetResult((float)SailfishDisplay.Density);
		},
	};

	public SailfishWindowHandler() : this(null)
	{
	}

	/// <summary>For a subclass that brings its own mappers, as MAUI's WindowHandler(mapper, commandMapper).</summary>
	public SailfishWindowHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
		: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
	{
	}

	/// <summary>Window.Title is the app's name on its home-screen cover (the cover's placeholder title), as it names
	/// the window on desktop heads; an empty title keeps the one baked from ApplicationTitle (tracker S13).</summary>
	public static void MapTitle(SailfishWindowHandler handler, IWindow window)
	{
		if (string.IsNullOrEmpty(window.Title))
			return;
		var title = BridgeValue.Quote(window.Title);
		QtThread.Later(() => QtHostRuntime.Eval(
			"(typeof window!=='undefined'&&window)?(window.mauiCoverTitle=" + title + ",'ok'):'no window'"));
	}

	/// <summary>Window.FlowDirection mirrors every page (QtHostVisualState.IsRightToLeft reads the window as the root):
	/// the hosts' mirror state and positions are pushed again.</summary>
	public static void MapFlowDirection(SailfishWindowHandler handler, IWindow window)
	{
		var session = SailfishHandlerCore.SessionOf(handler);
		session?.RequestLayout();
		session?.RequestPoll();
	}

	/// <summary>As WindowHandler.MapToolbar on the platforms: MAUI's toolbar (set by a NavigationPage or a Shell)
	/// gets its handler, which drives the page chrome (tracker S19).</summary>
	public static void MapToolbar(SailfishWindowHandler handler, IWindow window)
	{
		if (window is IToolbarElement { Toolbar: { Handler: null } toolbar } && handler.MauiContext is { } context)
			SailfishHandlersFactory.AttachToolbarHandler(toolbar, context);
		SailfishHandlerCore.SessionOf(handler)?.RequestPoll();
	}

	protected override object CreatePlatformElement() => new object();

	/// <summary>As WindowHandler.MapContent elsewhere: the root page gets its handler (a Shell/Tabbed/Flyout root
	/// follows its sections and tabs through it; the renderer only attaches the content page's), then the renderer
	/// syncs.</summary>
	public static void MapContent(SailfishWindowHandler handler, IWindow window)
	{
		if (window.Content is IView { Handler: null } root && handler.MauiContext is { } context)
			SailfishHandlersFactory.AttachRootHandler(root, context);
		SailfishHandlerCore.SessionOf(handler)?.RequestPoll();
	}
}
