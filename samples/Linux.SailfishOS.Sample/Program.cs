using Microsoft.Maui.SailfishOS.Platform;

namespace Microsoft.Maui.SailfishOS.Sample;

public class Program : SailfishMauiApplication
{
	protected override Microsoft.Maui.Hosting.MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	// The template's shape: Run returns the exit code (non-zero when the host or the app failed to start); crashes are
	// traced by the framework itself (stderr and /tmp/maui_trace.log).
	public static int Main(string[] args) => new Program().Run(args);
}