using Microsoft.Maui.SailfishOS.Platform;

namespace Trailhead;

public class Program : SailfishMauiApplication
{
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	public static int Main(string[] args) => new Program().Run(args);
}
