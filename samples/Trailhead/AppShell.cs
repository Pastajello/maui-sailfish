using Microsoft.Maui.Controls.PlatformConfiguration;
using Microsoft.Maui.Controls.PlatformConfiguration.SailfishOSSpecific;
using Microsoft.Maui.SailfishOS.Platform;
using Trailhead.Features.Diagnostics;
using Trailhead.Features.Photos;
using Trailhead.Features.Recording;
using Trailhead.Features.TripDetail;
using Trailhead.Features.TripEdit;
using Trailhead.Features.TripMap;
using Trailhead.Features.Trips;

namespace Trailhead;

/// <summary>
/// One root page and routes, no tabs and no flyout: a Sailfish app is a page stack with pulley menus. Shell is here
/// for routing (GoToAsync with query parameters, app links later). Portrait, except pages that ask for more (photos).
/// </summary>
public class AppShell : Shell
{
	public const string DiagnosticsRoute = "diagnostics";
	public const string TripRoute = "trip";
	public const string TripEditRoute = "tripedit";
	public const string TripMapRoute = "tripmap";
	public const string PhotoRoute = "photo";
	public const string RecordingRoute = "recording";
	public const string StatisticsRoute = "statistics";

	public AppShell(IServiceProvider services)
	{
		FlyoutBehavior = FlyoutBehavior.Disabled;
		this.On<SailfishOS>().SetAllowedOrientations(SailfishOrientations.Portrait);
		Items.Add(new ShellContent
		{
			Route = "trips",
			ContentTemplate = new DataTemplate(services.GetRequiredService<TripsPage>),
		});

		Routing.RegisterRoute(DiagnosticsRoute, typeof(DiagnosticsPage));
		Routing.RegisterRoute(TripRoute, typeof(TripDetailPage));
		Routing.RegisterRoute(TripEditRoute, typeof(TripEditPage));
		Routing.RegisterRoute(TripMapRoute, typeof(TripMapPage));
		Routing.RegisterRoute(PhotoRoute, typeof(PhotoViewerPage));
		Routing.RegisterRoute(RecordingRoute, typeof(RecordingPage));
		Routing.RegisterRoute(Features.Camera.CameraPage.Route, typeof(Features.Camera.CameraPage));
		Routing.RegisterRoute(StatisticsRoute, typeof(Features.Statistics.StatisticsPage));
		Features.Lab.LabPage.RegisterRoutes();
		Routing.RegisterRoute(Features.Settings.SettingsPage.Route, typeof(Features.Settings.SettingsPage));
	}
}
