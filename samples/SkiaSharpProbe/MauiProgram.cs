using SkiaSharp.Views.Maui.Controls.Hosting;

namespace SkiaSharpProbe;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder.UseMauiApp<App>().UseSkiaSharp();
		return builder.Build();
	}
}

public class App : Application
{
	protected override Window CreateWindow(IActivationState? activationState) =>
		new(new NavigationPage(new ProbePage()));
}
