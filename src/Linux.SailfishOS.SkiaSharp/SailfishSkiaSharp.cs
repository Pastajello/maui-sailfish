using Microsoft.Maui.SailfishOS.Handlers;
using SkiaSharp.Views.Maui.Handlers;

namespace Microsoft.Maui.SailfishOS.SkiaSharp;

/// <summary>
/// Registers the Sailfish implementation of SkiaSharp's MAUI views. The backend calls <see cref="Register"/> before
/// <c>CreateMauiApp</c> (the package's targets name it in the app assembly), so the app keeps calling
/// <c>UseSkiaSharp()</c> as on every platform; SkiaSharp's plain-<c>net</c> handlers, which have no platform view,
/// are served by the Sailfish ones wherever they are registered.
/// </summary>
public static class SailfishSkiaSharp
{
	private static bool _registered;

	public static void Register()
	{
		if (_registered)
			return;
		_registered = true;
		SailfishHandlersFactory.ReplaceLibraryHandler<SKCanvasViewHandler, SailfishSKCanvasViewHandler>();
		SailfishHandlersFactory.ReplaceLibraryHandler<SKGLViewHandler, SailfishSKGLViewHandler>();
		Microsoft.Maui.SailfishOS.Platform.QtHost.QtHostImageSources.Register(SkiaImageSources.Resolve);
	}
}
