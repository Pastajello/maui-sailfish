using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Seam between the host loop and the diagnostics assembly: with MAUI_SAILFISH_QT_HOST_DIAG=1,
/// <see cref="SailfishMauiApplication.Run"/> hands the live host objects to the registered
/// implementation just before entering the Qt loop.
/// </summary>
internal interface IQtHostDiagnostics
{
	void Attach(QtHostDiagnosticsContext context);
}

/// <summary>The host objects a diagnostics run drives (all on the Qt thread).</summary>
internal sealed record QtHostDiagnosticsContext(
	SailfishMauiApplication Application,
	IWindow Window,
	IMauiContext MauiContext,
	QtHostPageRenderer? Renderer,
	SailfishDispatcher Dispatcher,
	QtHostInputRouter? InputRouter);
