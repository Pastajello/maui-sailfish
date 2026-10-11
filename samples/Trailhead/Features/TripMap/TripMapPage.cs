using Trailhead.Data;
using Trailhead.Features.Shared;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Maps;
using Map = Microsoft.Maui.Controls.Maps.Map;

namespace Trailhead.Features.TripMap;

/// <summary>
/// The trip on a map: QtLocation with OpenTopoMap or OpenStreetMap tiles, the track, start and finish, and the photos
/// as clustered pins whose info window opens the photo; "Open in a map app" hands the start to whatever opens geo:
/// links. Query: <c>id</c>.
/// </summary>
public sealed class TripMapPage : ContentPage, IQueryAttributable
{
	private readonly TripStore _store;
	private string _id = "";
	private Trip? _trip;
	private readonly Map _map = new() { IsClusteringEnabled = true, MapType = Settings.SettingsPage.TopographicMaps ? MapType.Hybrid : MapType.Street };
	private readonly ToolbarItem _layer = new() { Text = L.T(Settings.SettingsPage.TopographicMaps ? "Street map" : "Topographic map") };

	public TripMapPage(TripStore store)
	{
		_store = store;
		Title = L.T("Map");
		SemanticProperties.SetDescription(_map, L.T("Map of the trip with its photos"));
		ToolbarItems.Add(new ToolbarItem(L.T("Open in a map app"), null, OpenExternal));
		// Hybrid shows OpenTopoMap on this backend: contour lines suit a hiking map better than streets.
		_layer.Clicked += (_, _) =>
		{
			_map.MapType = _map.MapType == MapType.Street ? MapType.Hybrid : MapType.Street;
			_layer.Text = L.T(_map.MapType == MapType.Street ? "Topographic map" : "Street map");
		};
		ToolbarItems.Add(_layer);
		Content = _map;
	}

	public void ApplyQueryAttributes(IDictionary<string, object> query) =>
		_id = query.TryGetValue("id", out var id) ? id.ToString() ?? "" : "";

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		if (_trip is not null)
			return;
		_trip = await _store.LoadAsync(_id);
		if (_trip is null)
			return;
		Title = _trip.Name;
		Show(_trip);
	}

	/// <summary>The number of fixes drawn: enough for the shape, few enough for QtLocation 5.4 to pan smoothly.</summary>
	public const int MaxDrawnPoints = 1500;

	public Map Map => _map;

	private void Show(Trip trip)
	{
		var line = new Polyline { StrokeColor = TrailColors.Route, StrokeWidth = 4 };
		var step = Math.Max(1, trip.Track.Count / MaxDrawnPoints);
		for (var i = 0; i < trip.Track.Count; i += step)
			line.Geopath.Add(new Location(trip.Track[i].Lat, trip.Track[i].Lon));
		line.Geopath.Add(new Location(trip.Track[^1].Lat, trip.Track[^1].Lon));
		_map.MapElements.Add(line);

		_map.Pins.Add(new Pin { Label = L.T("Start"), Address = Format.DayAndTime(trip.Start), Location = At(trip.Track[0]) });
		_map.Pins.Add(new Pin { Label = L.T("Finish"), Address = Format.DayAndTime(trip.End), Location = At(trip.Track[^1]) });
		for (var i = 0; i < trip.Photos.Count; i++)
		{
			var photo = trip.Photos[i];
			var index = i;
			var pin = new Pin { Label = photo.Caption, Address = Format.DayAndTime(photo.Time), Location = new Location(photo.Lat, photo.Lon) };
			pin.InfoWindowClicked += async (_, _) =>
				await Shell.Current.GoToAsync($"{AppShell.PhotoRoute}?id={trip.Id}&index={index}");
			_map.Pins.Add(pin);
		}

		var region = MapSpan.FromLocations(trip.Track.Select(At));
		_map.MoveToRegion(new MapSpan(region.Center, region.LatitudeDegrees * 1.25, region.LongitudeDegrees * 1.25), false);
	}

	private static Location At(TrackPoint p) => new(p.Lat, p.Lon);

	private async void OpenExternal()
	{
		if (_trip is { Track.Count: > 0 } trip)
			await Microsoft.Maui.ApplicationModel.Map.Default.OpenAsync(trip.Track[0].Lat, trip.Track[0].Lon,
				new Microsoft.Maui.ApplicationModel.MapLaunchOptions { Name = trip.Name });
	}
}
