using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.Maui.SailfishOS.Hosting;
using Microsoft.Maui.SailfishOS.Handlers;
using Microsoft.Maui.SailfishOS.Platform;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Linux.SailfishOS.PublicApiGuard;

public class App : Application
{
	protected override Window CreateWindow(IActivationState? activationState) =>
		new(new ContentPage { Content = new RatingView { Value = 3 } });
}

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder.UseMauiAppSailfish<App>();
		builder.UseRatingControls();
		// A mapper customization sees the typed native host, as Android code sees AppCompatTextView.
		SailfishLabelHandler.Mapper.AppendToMapping("GuardTrace", (handler, label) =>
		{
			NativeElementHost host = handler.PlatformView;
			_ = (host.Id, host.QmlUri, label.Text);
		});
		IViewHandler<ILabel, NativeElementHost> typed = new SailfishLabelHandler();
		_ = typed;
		builder.ConfigureLifecycleEvents(events => events.AddSailfish(sf => sf
			.OnLaunched((_, _) => SailfishCover.SetContent("Guard", "public API only"))
			.OnApplicationStateChanged((_, state) => SailfishCover.SetActions(
				new SailfishCoverAction("image://theme/icon-cover-refresh", () => { })))
			.OnQuitting(_ => { })));
		return builder.Build();
	}
}

/// <summary>Platforms/SailfishOS/SailfishApplication.cs of the template.</summary>
public class SailfishApplication : SailfishMauiApplication
{
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	protected override void OnQuitting()
	{
		using var sheet = new SailfishBottomSheet { Text = "Bye", Dock = "bottom" };
		// Remorse: page-wide and over one element.
		_ = SailfishRemorse.ExecuteAsync("Clearing", () => { });
		_ = SailfishRemorse.ExecuteAsync(new Label(), null, () => { }, SailfishRemorse.DefaultTimeoutMs);
		SailfishRemorse.CancelAll();
	}
}
