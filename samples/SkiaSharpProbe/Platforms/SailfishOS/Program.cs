using Microsoft.Maui.SailfishOS.Platform;

namespace SkiaSharpProbe;

public static class Program
{
	private static void Main(string[] args) => new SailfishApplication().Run(args);
}

public class SailfishApplication : SailfishMauiApplication
{
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
