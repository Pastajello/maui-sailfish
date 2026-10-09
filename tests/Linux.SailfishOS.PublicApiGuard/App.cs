using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.PlatformConfiguration.SailfishOSSpecific;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.Maui.SailfishOS.Hosting;
using Microsoft.Maui.SailfishOS.Handlers;
using Microsoft.Maui.SailfishOS.Platform;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Linux.SailfishOS.PublicApiGuard;

public class App : Application
{
	protected override Window CreateWindow(IActivationState? activationState)
	{
		var button = new Button { Text = "drag me" };
		var page = new ContentPage { Content = new VerticalStackLayout { new RatingView { Value = 3 }, button } };
		// The platform-specific API (decision D13, tracker S47), as page.On<Android>() elsewhere. Qualified here only
		// because this project's own namespace (Linux.SailfishOS.*) makes a bare SailfishOS the namespace.
		page.On<Microsoft.Maui.Controls.PlatformConfiguration.SailfishOS>().SetAllowedOrientations(SailfishOrientations.LandscapeMask);
		button.On<Microsoft.Maui.Controls.PlatformConfiguration.SailfishOS>().SetKeepsDrag(false);
		return new(page);
	}
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
			.OnDisplayStateChanged((app, state) => { var off = state == SailfishDisplayState.Off; })
			.OnScreenLockChanged((app, locked) => { var shown = locked; })
			.OnMemoryLevelChanged((app, level) => { var trim = level >= SailfishMemoryLevel.Warning; })
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
		using var sheet = new SailfishBottomSheet { Text = "Bye", Dock = SailfishDockEdge.Bottom };
		_ = Microsoft.Maui.Devices.DeviceInfo.Platform == SailfishPlatform.DevicePlatform;
		// Remorse: page-wide and over one element.
		_ = SailfishRemorse.ExecuteAsync("Clearing", () => { });
		_ = SailfishRemorse.ExecuteAsync(new Label(), null, () => { }, SailfishRemorse.DefaultTimeoutMs);
		SailfishRemorse.CancelAll();
		_ = (DisplayState, ScreenLocked, MemoryLevel);
	}
}
