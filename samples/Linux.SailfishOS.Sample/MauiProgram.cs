using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.SailfishOS.Diagnostics;
using Microsoft.Maui.SailfishOS.Hosting;
using Microsoft.Maui.SailfishOS.Sample.Services;

namespace Microsoft.Maui.SailfishOS.Sample;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();

		builder
			.UseMauiAppSailfish<App>()
			.ConfigureFonts(fonts =>
			{
				// fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			})
			// The f3 leg's custom ImageSource and its service, registered as a library registers its own.
			.ConfigureImageSources(SailfishDiagnostics.ConfigureImageSources);

		// Shared sample data, injected into App and the pages.
		builder.Services.AddSingleton<TaskStore>();

		// Opt in to the diagnostics legs and hand them the app pages they push.
		SailfishDiagnostics.Register();
		SailfishDiagnostics.RegisterPage("page", () => new Pages.TextPage());
		SailfishDiagnostics.RegisterPage("shapes", () => new Pages.ShapesImagesPage());
		SailfishDiagnostics.RegisterPage("visual", () => new Pages.VisualPage());
		SailfishDiagnostics.RegisterPage("essentials", () => new Pages.EssentialsPage());

		return builder.Build();
	}
}
