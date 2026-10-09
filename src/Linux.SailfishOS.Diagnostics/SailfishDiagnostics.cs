using Microsoft.Maui.Controls;
using Microsoft.Maui.SailfishOS.Platform;

namespace Microsoft.Maui.SailfishOS.Diagnostics;

/// <summary>
/// Opt-in entry point for the Qt-host diagnostics (MAUI_SAILFISH_QT_HOST_DIAG=1 plus per-leg
/// *_DIAG switches). Call from CreateMauiApp; without it no diagnostics run.
/// </summary>
public static partial class SailfishDiagnostics
{
	/// <summary>Registers the diagnostics legs with the Qt host.</summary>
	public static void Register() =>
		SailfishMauiApplication.Diagnostics ??= new QtHostDiagnosticsRunner();

	/// <summary>Registers an app page factory for legs that push one ("page", "shapes", "visual"),
	/// since the backend cannot reference the app assembly.</summary>
	public static void RegisterPage(string leg, Func<Page> factory) =>
		QtHostDiagnosticsRunner.RegisterDiagnosticPage(leg, factory);
}
