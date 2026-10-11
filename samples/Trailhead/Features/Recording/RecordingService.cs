using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.SailfishOS.Platform;
using Microsoft.Maui.Storage;
using Trailhead.Data;
using Trailhead.Features.Shared;

namespace Trailhead.Features.Recording;

public enum RecordingState
{
	Idle,
	Recording,
	Paused,
}

/// <summary>
/// Records a trip: fixes from the GPS (or a replayed demo track, <see cref="Simulated"/>), filtered, appended to the
/// track and written to a draft every few fixes, so a killed app resumes where it was. While recording the phone does
/// not suspend the app (<see cref="SailfishBackground.KeepAlive"/>), the cover shows the live numbers with pause and
/// waypoint actions, a notification stays in the events view, and every kilometre is a haptic tap. UI thread.
/// </summary>
public sealed partial class RecordingService : ObservableObject
{
	/// <summary>Fixes worse than this (metres) are dropped.</summary>
	public const double MaxAccuracyM = 40;

	/// <summary>A fix closer than this to the last one only advances the clock (GPS jitter while standing).</summary>
	public const double MinStepM = 2;

	private const int DraftEvery = 10;
	private const string SimulateKey = "simulate-gps";

	private readonly TripStore _store;
	private readonly string _draftFile = Path.Combine(FileSystem.AppDataDirectory, "recording.json");
	private Trip _trip = new();
	private IDispatcherTimer? _replay;
	private int _replayIndex;
	private Trip? _replaySource;
	private DateTimeOffset _replayAnchor;     // wall time of the replayed track's start (moves on by each pause)
	private TimeSpan _replayBase;             // track offset where this stretch of replay began
	private int _sinceDraft;
	private int _lastKm;
	private uint _notification;
	private DateTimeOffset _lastFixAt;
	private DateTimeOffset _resumedAt;
	private DateTimeOffset _pausedAt;
	private TimeSpan _movingBefore;
	private IDispatcherTimer? _stillTimer;
	private Task _draftWrite = Task.CompletedTask;
	private bool _draftPending;
	private DateTimeOffset _lastAppendWall;

	public RecordingService(TripStore store)
	{
		_store = store;
	}

	/// <summary>Replay a demo track instead of the GPS (Settings; the tour turns it on).</summary>
	public static bool Simulated
	{
		get => Preferences.Default.Get(SimulateKey, false) || Environment.GetEnvironmentVariable("TRAILHEAD_SIMULATE") == "1";
		set => Preferences.Default.Set(SimulateKey, value);
	}

	/// <summary>Auto-pause: no step of <see cref="MinStepM"/> for this long and the recording counts as standing still
	/// (a break at a hut, traffic lights). Moving time stops anyway (slow steps never count); this says so on the page,
	/// the cover and the notification, and the first step clears it.</summary>
	public static TimeSpan StillAfter { get; set; } = TimeSpan.FromSeconds(30);

	/// <summary>Replay speed: seconds of the track per second of wall clock.</summary>
	public static double ReplaySpeed { get; set; } = 20;

	[ObservableProperty]
	public partial RecordingState State { get; private set; }

	[ObservableProperty]
	public partial double DistanceM { get; private set; }

	[ObservableProperty]
	public partial double AscentM { get; private set; }

	[ObservableProperty]
	public partial double AltitudeM { get; private set; }

	[ObservableProperty]
	public partial double SpeedMps { get; private set; }

	[ObservableProperty]
	public partial TimeSpan Moving { get; private set; }

	/// <summary>Recording, but no step for <see cref="StillAfter"/> (auto-pause).</summary>
	[ObservableProperty]
	public partial bool IsStill { get; private set; }

	public Activity Activity => _trip.Activity;

	public IReadOnlyList<TrackPoint> Track => _trip.Track;

	public IReadOnlyList<Waypoint> Waypoints => _trip.Waypoints;

	public TrackPoint? Last => _trip.Track.Count > 0 ? _trip.Track[^1] : null;

	public bool IsActive => State != RecordingState.Idle;

	/// <summary>Raised for every fix that made it into the track.</summary>
	public event EventHandler<TrackPoint>? PointAdded;

	public bool HasDraft => File.Exists(_draftFile);

	public async Task StartAsync(Activity activity)
	{
		if (IsActive)
			return;
		_trip = new Trip { Id = _store.NewId(), Activity = activity };
		_replaySource = null;
		_replayIndex = 0;
		ResetStats();
		await BeginAsync();
	}

	/// <summary>Picks up a draft a killed app left behind.</summary>
	public async Task<bool> ResumeDraftAsync()
	{
		if (IsActive || !File.Exists(_draftFile))
			return false;
		try
		{
			await using var stream = File.OpenRead(_draftFile);
			if (await JsonSerializer.DeserializeAsync(stream, TrailheadJson.Default.Trip) is not { } draft)
				return false;
			_trip = draft;
		}
		catch (JsonException)
		{
			File.Delete(_draftFile);
			return false;
		}
		ResetStats();
		var stats = TripStats.Compute(_trip.Track);
		DistanceM = stats.DistanceM;
		AscentM = stats.AscentM;
		_movingBefore = stats.MovingTime;
		Moving = _movingBefore;
		_lastKm = (int)(DistanceM / 1000);
		await BeginAsync();
		return true;
	}

	public void Pause()
	{
		if (State != RecordingState.Recording)
			return;
		_movingBefore = Moving;
		_pausedAt = DateTimeOffset.Now;
		IsStill = false;
		State = RecordingState.Paused;
		StopSource();
		UpdateCover();
		UpdateNotification();
	}

	public async Task ResumeAsync()
	{
		if (State != RecordingState.Paused)
			return;
		State = RecordingState.Recording;
		_resumedAt = _lastAppendWall = DateTimeOffset.Now;
		await StartSourceAsync();
		UpdateCover();
		UpdateNotification();
	}

	public void AddWaypoint(string? name = null)
	{
		if (Last is not { } at)
			return;
		var (place, _) = DemoRoutes.Nearest(at.Lat, at.Lon);
		_trip.Waypoints.Add(new Waypoint(at.Lat, at.Lon, name ?? $"Waypoint near {place.Name}", at.Time));
		HapticFeedback.Default.Perform(HapticFeedbackType.Click);
		OnPropertyChanged(nameof(Waypoints));
	}

	/// <summary>Where the camera writes the recording's next photo.</summary>
	public string NewPhotoPath() => _store.PhotoPath(_trip.Id, $"c{_trip.Photos.Count + 1:00}-{DateTime.Now:HHmmss}.jpg");

	/// <summary>A photo taken while recording: placed at the last fix, captioned with the nearest place.</summary>
	public void AddPhoto(string path)
	{
		var at = Last ?? new TrackPoint(0, 0, 0, DateTimeOffset.Now);
		var (place, _) = DemoRoutes.Nearest(at.Lat, at.Lon);
		var photo = new TripPhoto(Path.GetFileName(path), at.Lat, at.Lon, DateTimeOffset.Now, L.F("Near {0}", place.Name));
		_trip.Photos.Add(photo);
		var thumbnail = _store.PhotoPath(_trip.Id, photo.ThumbnailFile);
		_ = Task.Run(() =>
		{
			try
			{
				PhotoThumbnails.Make(path, thumbnail);
			}
			catch (IOException ex)
			{
				Console.Error.WriteLine($"[Trailhead] RECORDING no thumbnail for {Path.GetFileName(path)}: {ex.Message}");
			}
		});
		OnPropertyChanged(nameof(Photos));
	}

	public IReadOnlyList<TripPhoto> Photos => _trip.Photos;

	/// <summary>Stops and saves the trip; null when there was nothing to save (fewer than two fixes).</summary>
	public async Task<Trip?> FinishAsync()
	{
		if (!IsActive)
			return null;
		StopSource();
		State = RecordingState.Idle;
		EndBackground();
		var trip = _trip;
		DeleteDraft();
		if (trip.Track.Count < 2)
			return null;
		var (place, _) = DemoRoutes.Nearest(trip.Track[0].Lat, trip.Track[0].Lon);
		trip.Name = L.F("{0} from {1}", Format.ActivityName(trip.Activity), place.Name);
		trip.Area = place.Name;
		await _store.SaveAsync(trip);
		return trip;
	}

	public void Discard()
	{
		StopSource();
		State = RecordingState.Idle;
		EndBackground();
		DeleteDraft();
		ResetStats();
	}

	private async Task BeginAsync()
	{
		State = RecordingState.Recording;
		_resumedAt = _lastAppendWall = DateTimeOffset.Now;
		IsStill = false;
		var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.GetForCurrentThread()!;
		_stillTimer = dispatcher.CreateTimer();
		_stillTimer.Interval = TimeSpan.FromSeconds(5);
		_stillTimer.Tick += (_, _) => CheckStill();
		_stillTimer.Start();
		SailfishBackground.KeepAlive = true;
		_notification = SailfishNotifications.Show(L.T("Recording"), Summary(), preview: false, icon: "icon-lock-information");
		SailfishCover.ActiveChanged += OnCoverActiveChanged;
		await StartSourceAsync();
		UpdateCover();
	}

	private void EndBackground()
	{
		SailfishBackground.KeepAlive = false;
		_stillTimer?.Stop();
		_stillTimer = null;
		IsStill = false;
		SailfishCover.ActiveChanged -= OnCoverActiveChanged;
		if (_notification != 0)
			SailfishNotifications.Close(_notification);
		_notification = 0;
	}

	private void ResetStats()
	{
		DistanceM = AscentM = AltitudeM = SpeedMps = 0;
		Moving = _movingBefore = TimeSpan.Zero;
		_lastKm = 0;
		_sinceDraft = 0;
	}

	/* --- location sources --- */

	private async Task StartSourceAsync()
	{
		if (Simulated)
		{
			StartReplay();
			return;
		}
		Geolocation.Default.LocationChanged += OnLocationChanged;
		try
		{
			await Geolocation.Default.StartListeningForegroundAsync(
				new GeolocationListeningRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(1)) { MinimumDistance = MinStepM });
		}
		catch (Exception ex) when (ex is FeatureNotSupportedException or PermissionException or FeatureNotEnabledException)
		{
			Geolocation.Default.LocationChanged -= OnLocationChanged;
			Console.Error.WriteLine($"[Trailhead] GPS unavailable ({ex.GetType().Name}: {ex.Message}); replaying a demo track");
			StartReplay();
		}
	}

	private void StopSource()
	{
		Geolocation.Default.LocationChanged -= OnLocationChanged;
		if (Geolocation.Default.IsListeningForeground)
			Geolocation.Default.StopListeningForeground();
		_replay?.Stop();
		_replay = null;
	}

	private void OnLocationChanged(object? sender, GeolocationLocationChangedEventArgs e)
	{
		var l = e.Location;
		if (l.Accuracy is > MaxAccuracyM)
			return;
		Append(new TrackPoint(l.Latitude, l.Longitude, l.Altitude ?? AltitudeM, l.Timestamp));
	}

	/// <summary>Replays a demo route matching the activity, ReplaySpeed× faster. After a pause it goes on from where it
	/// stopped, its fix times continuing from the resume (the pause shows as a gap, as it does with a real GPS).</summary>
	private void StartReplay()
	{
		_replaySource ??= TripGenerator.Generate(DemoRoutes.All.FirstOrDefault(r => r.Activity == Activity) ?? DemoRoutes.All[0]);
		var source = _replaySource.Track;
		_replayBase = source[Math.Min(_replayIndex, source.Count - 1)].Time - source[0].Time;
		// The replayed clock runs ReplaySpeed× ahead of the wall clock: after a pause the fixes go on from the last one,
		// one pause later, not from "now" (which is earlier than the last fix's time).
		_replayAnchor = _replayIndex > 0 && Last is { } last
			? last.Time - (source[_replayIndex - 1].Time - source[0].Time) + (DateTimeOffset.Now - _pausedAt)
			: DateTimeOffset.Now - _replayBase;
		var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.GetForCurrentThread()!;
		_replay = dispatcher.CreateTimer();
		_replay.Interval = TimeSpan.FromMilliseconds(250);
		_replay.Tick += (_, _) =>
		{
			var due = _replayBase + (DateTimeOffset.Now - _resumedAt) * ReplaySpeed;
			while (_replayIndex < source.Count && source[_replayIndex].Time - source[0].Time <= due)
			{
				var p = source[_replayIndex++];
				Append(p with { Time = _replayAnchor + (p.Time - source[0].Time) });
			}
		};
		_replay.Start();
	}

	/* --- the track --- */

	private void Append(TrackPoint p)
	{
		if (State != RecordingState.Recording)
			return;
		var track = _trip.Track;
		if (track.Count > 0)
		{
			var prev = track[^1];
			var step = Geo.DistanceM(prev, p);
			var seconds = (p.Time - prev.Time).TotalSeconds;
			if (seconds <= 0 || step / seconds > 60)
				return;   // a jump no hiker, runner or rider makes
			if (step < MinStepM)
				return;
			DistanceM += step;
			SpeedMps = step / seconds;
			if (p.Ele - prev.Ele > 0)
				AscentM += p.Ele - prev.Ele;
			if (SpeedMps >= 0.15)
				Moving += TimeSpan.FromSeconds(seconds);
		}
		track.Add(p);
		AltitudeM = p.Ele;
		_lastFixAt = p.Time;
		_lastAppendWall = DateTimeOffset.Now;
		if (IsStill)
		{
			IsStill = false;
			UpdateCover();
			UpdateNotification();
		}
		PointAdded?.Invoke(this, p);

		var km = (int)(DistanceM / 1000);
		if (km > _lastKm)
		{
			_lastKm = km;
			HapticFeedback.Default.Perform(HapticFeedbackType.LongPress);   // plan D5: no TTS on Sailfish
			UpdateNotification();
		}
		if (++_sinceDraft >= DraftEvery)
		{
			_sinceDraft = 0;
			ScheduleDraft();
		}
		if (SailfishCover.IsActive)
			UpdateCover();
	}

	/// <summary>Writes the draft now (the app goes to the background); nothing when not recording.</summary>
	public Task SaveDraftAsync()
	{
		if (IsActive)
			ScheduleDraft();
		return _draftWrite;
	}

	/// <summary>
	/// One draft write at a time: a burst of fixes (the replay catching up, a GPS backlog) asks for several, and two
	/// writers on the same temporary file failed ("being used by another process", an unobserved task). A request while
	/// one runs is remembered and written after it, from a fresh snapshot.
	/// </summary>
	private void ScheduleDraft()
	{
		if (!_draftWrite.IsCompleted)
		{
			_draftPending = true;
			return;
		}
		_draftWrite = WriteDraftsAsync();
	}

	private async Task WriteDraftsAsync()
	{
		do
		{
			_draftPending = false;
			// The snapshot is taken here, on the UI thread, in one go: serializing across awaits read a track that
			// Append kept changing.
			var json = JsonSerializer.SerializeToUtf8Bytes(_trip, TrailheadJson.Default.Trip);
			var temp = _draftFile + ".tmp";
			try
			{
				await File.WriteAllBytesAsync(temp, json);
				File.Move(temp, _draftFile, overwrite: true);
			}
			catch (IOException ex)
			{
				Console.Error.WriteLine($"[Trailhead] RECORDING draft not written: {ex.Message}");
			}
		}
		while (_draftPending && IsActive);
		// The recording ended while this write was under way: its draft must not outlive it (the next launch would
		// offer to continue a recording that was saved).
		if (!IsActive)
			DeleteDraft();
	}

	private void DeleteDraft()
	{
		try
		{
			File.Delete(_draftFile);
		}
		catch (IOException)
		{
		}
	}

	/* --- cover and notification --- */

	public string Summary() => $"{Format.Distance(DistanceM)} · {Format.Clock(Moving)}";

	private void CheckStill()
	{
		if (State != RecordingState.Recording || IsStill || DateTimeOffset.Now - _lastAppendWall < StillAfter)
			return;
		IsStill = true;
		Console.Error.WriteLine($"[Trailhead] RECORDING auto-paused: no step for {StillAfter.TotalSeconds:0} s");
		UpdateCover();
		UpdateNotification();
	}

	private string StatusTitle() => State == RecordingState.Paused ? "Paused" : IsStill ? "Standing still" : "";

	private void OnCoverActiveChanged(object? sender, EventArgs e)
	{
		if (SailfishCover.IsActive)
			UpdateCover();
	}

	private void UpdateCover()
	{
		if (!IsActive)
			return;
		SailfishCover.SetContent(StatusTitle() is { Length: > 0 } status ? L.T(status) : Format.ActivityName(Activity),
			Format.Distance(DistanceM), Format.Clock(Moving), Format.Pace(Activity, SpeedMps));
		SailfishCover.SetActions(
			State == RecordingState.Paused
				? new SailfishCoverAction("image://theme/icon-cover-play", () => _ = ResumeAsync())
				: new SailfishCoverAction("image://theme/icon-cover-pause", Pause),
			new SailfishCoverAction("image://theme/icon-cover-new", () => AddWaypoint()));
	}

	private void UpdateNotification()
	{
		if (_notification != 0)
			SailfishNotifications.Update(_notification,
				L.T(State == RecordingState.Paused ? "Recording paused" : IsStill ? "Recording · standing still" : "Recording"), Summary());
	}
}
