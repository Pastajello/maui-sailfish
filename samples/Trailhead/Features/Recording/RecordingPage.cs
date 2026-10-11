using System.ComponentModel;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.SailfishOS.Platform;
using Trailhead.Data;
using Trailhead.Features.Shared;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps;
using Map = Microsoft.Maui.Controls.Maps.Map;

namespace Trailhead.Features.Recording;

/// <summary>
/// The live recording: the track on the map (updated in batches, so the map is not re-sent for every fix), four
/// animated numbers, and the pulley: Finish (a remorse countdown, then the trip opens), Waypoint, Pause/Resume. Leaving
/// the page keeps recording; the cover and a notification carry on.
/// </summary>
public sealed class RecordingPage : ContentPage
{
	/// <summary>How often new fixes reach the map.</summary>
	public static readonly TimeSpan FlushEvery = TimeSpan.FromSeconds(2);

	private readonly RecordingService _recording;
	private readonly ToolbarItem _pause = new() { Text = L.T("Pause") };
	private readonly ToolbarItem _follow = new() { Text = L.T("Stop following"), Order = ToolbarItemOrder.Secondary };
	private readonly AnimatedCounter _distance = new() { PulseEvery = 1000 };
	private readonly AnimatedCounter _time = new();
	private readonly AnimatedCounter _pace = new() { FontSize = 22 };
	private readonly AnimatedCounter _altitude = new() { FontSize = 22 };
	private readonly Controls.CompassView _compass = new()
	{
		WidthRequest = 120,
		HeightRequest = 120,
		HorizontalOptions = LayoutOptions.End,
		VerticalOptions = LayoutOptions.Start,
		Margin = new Thickness(16),
	};
	private readonly Label _status = new() { FontSize = 14, HorizontalTextAlignment = TextAlignment.Center, IsVisible = false };
	private IDispatcherTimer? _flush;
	private int _drawn;
	private bool _following = true;
	private readonly Map _map = new() { MapType = Settings.SettingsPage.TopographicMaps ? MapType.Hybrid : MapType.Street };
	private readonly Polyline _line = new() { StrokeWidth = 5 };
	private readonly Pin _you = new() { Label = L.T("You") };

	public RecordingPage(RecordingService recording)
	{
		_recording = recording;
		Title = L.T("Recording");
		SemanticProperties.SetDescription(_map, L.T("Live map"));
		SemanticProperties.SetDescription(_compass, L.T("Compass, with the way back to the start"));

		var finish = new ToolbarItem { Text = L.T("Finish") };
		finish.Clicked += async (_, _) => await FinishAsync();
		var photo = new ToolbarItem { Text = L.T("Photo") };
		photo.Clicked += async (_, _) => await Shell.Current.GoToAsync(Camera.CameraPage.Route);
		var waypoint = new ToolbarItem { Text = L.T("Waypoint") };
		waypoint.Clicked += (_, _) => _recording.AddWaypoint();
		_pause.Clicked += async (_, _) =>
		{
			if (_recording.State == RecordingState.Paused)
				await _recording.ResumeAsync();
			else
				_recording.Pause();
		};
		_follow.Clicked += (_, _) =>
		{
			_following = !_following;
			_follow.Text = L.T(_following ? "Stop following" : "Follow me");
		};
		ToolbarItems.Add(finish);
		ToolbarItems.Add(photo);
		ToolbarItems.Add(waypoint);
		ToolbarItems.Add(_pause);
		ToolbarItems.Add(_follow);

		_distance.Formatter = Format.Distance;
		_time.Formatter = s => Format.Clock(TimeSpan.FromSeconds(s));
		_pace.Formatter = mps => Format.Pace(_recording.Activity, mps);
		_altitude.Formatter = Format.Height;
		foreach (var counter in new[] { _distance, _time, _pace, _altitude })
		{
			counter.SetDynamicResource(Label.TextColorProperty, ThemeResources.Highlight);
			counter.HorizontalTextAlignment = TextAlignment.Center;
		}
		_status.SetDynamicResource(Label.TextColorProperty, ThemeResources.SecondaryHighlight);

		var band = new Grid
		{
			Padding = new Thickness(16, 8, 16, 16),
			RowSpacing = 2,
			ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
			RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto) },
		};
		band.Add(_status, 0, 0);
		Grid.SetColumnSpan(_status, 2);
		band.Add(_distance, 0, 1);
		band.Add(Caption(L.T("distance")), 0, 2);
		band.Add(_time, 1, 1);
		band.Add(Caption(L.T("moving")), 1, 2);
		band.Add(_pace, 0, 3);
		band.Add(Caption(L.T(recording.Activity is Activity.Bike or Activity.Ski ? "speed" : "pace")), 0, 4);
		band.Add(_altitude, 1, 3);
		band.Add(Caption(L.T("altitude")), 1, 4);

		var root = new Grid { RowDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
		_line.StrokeColor = TrailColors.Route;
		_map.MapElements.Add(_line);
		_map.IsShowingUser = !RecordingService.Simulated;
		root.Add(_map);
		root.Add(_compass);
		root.Add(band, 0, 1);
		Content = root;
		_compass.Tapped += (_, _) =>
		{
			_following = true;
			_follow.Text = L.T("Stop following");
			Flush(force: true);
		};
	}

	private static Label Caption(string text)
	{
		var label = new Label { Text = text, FontSize = 13, HorizontalTextAlignment = TextAlignment.Center };
		label.SetDynamicResource(Label.TextColorProperty, ThemeResources.Secondary);
		return label;
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		_recording.PropertyChanged += OnRecordingChanged;
		_recording.PointAdded += OnPointAdded;
		// The phone's heading from the magnetometer; a replayed track has none to match, so it uses the course instead.
		if (!RecordingService.Simulated && Compass.Default.IsSupported)
		{
			Compass.Default.ReadingChanged += OnCompass;
			try
			{
				Compass.Default.Start(SensorSpeed.UI, applyLowPassFilter: true);
			}
			catch (FeatureNotSupportedException)
			{
				Compass.Default.ReadingChanged -= OnCompass;
			}
		}
		_flush = Dispatcher.CreateTimer();
		_flush.Interval = FlushEvery;
		_flush.Tick += (_, _) => Flush();
		_flush.Start();
		Flush();
		UpdateNumbers();
		UpdateState();
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();
		_recording.PropertyChanged -= OnRecordingChanged;
		_recording.PointAdded -= OnPointAdded;
		Compass.Default.ReadingChanged -= OnCompass;
		if (Compass.Default.IsMonitoring)
			Compass.Default.Stop();
		_flush?.Stop();
	}

	private void OnRecordingChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(RecordingService.State) or nameof(RecordingService.IsStill))
			UpdateState();
		else
			UpdateNumbers();
	}

	private void UpdateNumbers()
	{
		_distance.Value = _recording.DistanceM;
		_time.Value = _recording.Moving.TotalSeconds;
		_pace.Value = _recording.SpeedMps;
		_altitude.Value = _recording.AltitudeM;
	}

	private void UpdateState()
	{
		var paused = _recording.State == RecordingState.Paused;
		_pause.Text = L.T(paused ? "Resume" : "Pause");
		_status.Text = L.T(paused ? "Paused" : "Standing still");
		_status.IsVisible = paused || _recording.IsStill;
		// A slow blink while paused: an animation that runs until the state changes.
		_status.AbortAnimation("blink");
		if (paused)
			new Animation(v => _status.Opacity = v, 1, 0.3, Easing.SinInOut)
				.Commit(_status, "blink", length: 900, repeat: () => _recording.State == RecordingState.Paused);
		else
			_status.Opacity = 1;
	}

	private void OnCompass(object? sender, CompassChangedEventArgs e) => _compass.Heading = e.Reading.HeadingMagneticNorth;

	/// <summary>The course from the last two fixes (the heading of a replayed track) and the way back to the start.</summary>
	private void OnPointAdded(object? sender, TrackPoint p)
	{
		var track = _recording.Track;
		if (track.Count < 2)
			return;
		if (RecordingService.Simulated || !Compass.Default.IsMonitoring)
			_compass.Heading = Geo.BearingDeg(track[^2].Lat, track[^2].Lon, p.Lat, p.Lon);
		_compass.TargetBearing = Geo.DistanceM(p, track[0]) > 30 ? Geo.BearingDeg(p.Lat, p.Lon, track[0].Lat, track[0].Lon) : null;
	}

	/// <summary>The fixes since the last flush onto the map, in one change.</summary>
	private void Flush(bool force = false)
	{
		var track = _recording.Track;
		if (track.Count == 0 || (track.Count == _drawn && !force))
			return;
		for (var i = _drawn; i < track.Count; i++)
			_line.Geopath.Add(new Location(track[i].Lat, track[i].Lon));
		_drawn = track.Count;
		var last = track[^1];
		_you.Location = new Location(last.Lat, last.Lon);
		if (RecordingService.Simulated && !_map.Pins.Contains(_you))
			_map.Pins.Add(_you);
		if (_following)
			_map.MoveToRegion(MapSpan.FromCenterAndRadius(_you.Location, Distance.FromMeters(600)), true);
	}

	/// <summary>Finish behind a page-wide remorse countdown; the saved trip opens in place of this page.</summary>
	private async Task FinishAsync()
	{
		if (!await SailfishRemorse.ExecuteAsync(L.T("Finishing the trip"), () => { }))
			return;
		var trip = await _recording.FinishAsync();
		await Shell.Current.GoToAsync("..");
		if (trip is not null)
			await Shell.Current.GoToAsync($"{AppShell.TripRoute}?id={trip.Id}");
	}

	public Map Map => _map;
}
