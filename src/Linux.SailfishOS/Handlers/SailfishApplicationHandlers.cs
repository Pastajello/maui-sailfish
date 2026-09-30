using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.SailfishOS.Platform;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Handlers;

/// <summary>Application handler: Application.Quit() → Terminate ends the Qt/Silica loop (the app exits as from the
/// window's close). The platform application is the SailfishMauiApplication.</summary>
public class SailfishApplicationHandler : ElementHandler<IApplication, object>
{
	public static readonly PropertyMapper<IApplication, SailfishApplicationHandler> Mapper = new(ElementMapper);

	public static readonly CommandMapper<IApplication, SailfishApplicationHandler> CommandMapper = new(ElementCommandMapper)
	{
		["Terminate"] = MapTerminate,   // ApplicationHandler.TerminateCommandKey (internal): Application.Quit()
	};

	public SailfishApplicationHandler() : base(Mapper, CommandMapper)
	{
	}

	protected override object CreatePlatformElement() =>
		(object?)Microsoft.Maui.IPlatformApplication.Current ?? new object();

	/// <summary>Quits on the Qt thread, after the current loop turn.</summary>
	public static void MapTerminate(SailfishApplicationHandler handler, IApplication application, object? args) =>
		QtHostRuntime.Post(QtHostRuntime.Quit);
}

/// <summary>Window handler: the Silica ApplicationWindow is the platform window (one per app); content changes follow
/// at once and density requests answer from the display.</summary>
public class SailfishWindowHandler : ElementHandler<IWindow, object>
{
	public static readonly PropertyMapper<IWindow, SailfishWindowHandler> Mapper = new(ElementMapper)
	{
		[nameof(IWindow.Content)] = MapContent,
	};

	public static readonly CommandMapper<IWindow, SailfishWindowHandler> CommandMapper = new(ElementCommandMapper)
	{
		[nameof(IWindow.RequestDisplayDensity)] = static (_, _, args) =>
		{
			if (args is DisplayDensityRequest request)
				request.SetResult((float)SailfishDisplay.Density);
		},
	};

	public SailfishWindowHandler() : base(Mapper, CommandMapper)
	{
	}

	protected override object CreatePlatformElement() => new object();

	/// <summary>As WindowHandler.MapContent elsewhere: the root page gets its handler (a Shell/Tabbed/Flyout root
	/// follows its sections and tabs through it; the renderer only attaches the content page's), then the renderer
	/// syncs.</summary>
	public static void MapContent(SailfishWindowHandler handler, IWindow window)
	{
		if (window.Content is IView { Handler: null } root && handler.MauiContext is { } context)
			SailfishHandlersFactory.AttachRootHandler(root, context);
		QtHostPageRenderer.RequestPoll();
	}
}
