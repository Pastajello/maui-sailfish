using Microsoft.Maui.SailfishOS.Diagnostics;
using Trailhead.Data;
using Trailhead.Features.Photos;
using Trailhead.Features.Shared;
using Trailhead.Features.TripEdit;
using Trailhead.Features.Trips;

namespace Trailhead.Features.Tour;

/// <summary>
/// A scripted walk through the app with real touches, a <c>SF-SHOT</c> marker at every state worth a screenshot and a
/// <c>TOUR CHECK</c> line for every state it can read back: <c>tools/sf shots --env TRAILHEAD_TOUR=all</c>
/// (SF_SAMPLE_DIR=samples/Trailhead). Every session of the plan adds its part here, so one run shows the whole app.
/// The value names the parts to run ("all", or a comma list such as "context,photo").
/// </summary>
public static class ShotTour
{
	private static readonly (string Name, Func<Shell, Task> Run)[] Parts =
	{
		("trips", TripsAsync),
		("context", ContextMenuAsync),
		("detail", DetailAsync),
		("photo", PhotoAsync),
		("import", ImportAsync),
		("map", MapAsync),
		("edit", EditAsync),
		("record", RecordAsync),
		("camera", CameraAsync),
		("stats", StatsAsync),
		("lab", LabAsync),
		("stress", StressAsync),
		("listscale", ListScaleAsync),
		("diagnostics", DiagnosticsAsync),
	};

	private static int _failed;

	/// <summary>Parts that run only when named.</summary>
	private static readonly (string Name, Func<Shell, Task> Run)[] Extras =
	{
		("cleanup", CleanupAsync),
		("soak", SoakAsync),
	};

	public static async Task RunAsync(Shell shell, string which)
	{
		var wanted = which is "1" or "all" ? Parts.Select(p => p.Name).ToHashSet() : which.Split(',').ToHashSet();
		try
		{
			File.Delete("/tmp/trailhead-tour.log");
		}
		catch (IOException)
		{
		}
		Log($"start ({string.Join(",", wanted)})");
		// A task that failed with nobody awaiting it surfaces only when the finalizer runs: count them all tour long.
		var unobserved = new List<string>();
		TaskScheduler.UnobservedTaskException += (_, e) => unobserved.Add(e.Exception.InnerException?.Message ?? e.Exception.Message);
		await Task.Delay(1500);
		foreach (var (name, run) in Extras.Concat(Parts).Where(p => wanted.Contains(p.Name)))
		{
			try
			{
				Log($"part {name}");
				await shell.GoToAsync("//trips");
				await Task.Delay(800);
				await run(shell);
			}
			catch (Exception ex)
			{
				_failed++;
				Log($"part {name} failed: {ex}");
			}
		}
		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();
		await Task.Delay(200);
		Check($"no task failed unobserved ({unobserved.Count}{(unobserved.Count > 0 ? ": " + string.Join(" | ", unobserved) : "")})", unobserved.Count == 0);
		Log(_failed == 0 ? "done, all checks passed" : $"done, {_failed} FAILED");
		// Under tools/sf shots (it sets AUTO_SHUTDOWN) the app quits when the tour ends, so shots stops with it instead of
		// polling until its deadline.
		if (Environment.GetEnvironmentVariable("MAUI_SAILFISH_QT_HOST_AUTO_SHUTDOWN") == "1")
		{
			await Task.Delay(1000);
			Application.Current?.Quit();
		}
	}

	private static async Task TripsAsync(Shell shell)
	{
		// The first-launch hint (TRAILHEAD_HINT=1 shows it again): shot while it runs, then wait for it to go.
		if (shell.CurrentPage is TripsPage { IsHinting: true } hinted)
		{
			await SailfishUiDriver.ShotAsync("trips-hint");
			for (var i = 0; i < 40 && hinted.IsHinting; i++)
				await Task.Delay(250);
			Check("the pull-down hint ran and went", !hinted.IsHinting);
		}
		await SailfishUiDriver.ShotAsync("trips-list");

		// The pulley: pull, hold for the shot, push back so nothing is chosen.
		var size = SailfishUiDriver.SceneSize;
		SailfishUiDriver.Press(size.Width / 2, size.Height * 0.3);
		await SailfishUiDriver.MoveToAsync(size.Width / 2, size.Height * 0.75, 500);
		await Task.Delay(400);
		await SailfishUiDriver.ShotAsync("trips-pulley");
		await SailfishUiDriver.MoveToAsync(size.Width / 2, size.Height * 0.3, 400);
		SailfishUiDriver.Release();
		await Task.Delay(800);
		Check("the pulley closed without choosing an item", shell.CurrentPage is TripsPage);
	}

	private static async Task ContextMenuAsync(Shell shell)
	{
		// Press and hold a trip: Silica's context menu opens; the release must not open the trip.
		if (FirstRow<TripItem>() is not { } row || SailfishUiDriver.CollectionRowPoint(row) is not { } at)
		{
			Check("a trip row is on screen", false);
			return;
		}
		SailfishUiDriver.Press(at.X, at.Y);
		await Task.Delay(1300);
		await SailfishUiDriver.ShotAsync("trips-context-held");
		SailfishUiDriver.Release();
		await Task.Delay(700);
		await SailfishUiDriver.ShotAsync("trips-context-menu");
		Check("press and hold keeps the trips page (no navigation)", shell.CurrentPage is TripsPage);
		var size = SailfishUiDriver.SceneSize;
		await SailfishUiDriver.TapAsync(size.Width / 2, size.Height * 0.06);   // outside: closes it
		await Task.Delay(600);
	}

	private static async Task DetailAsync(Shell shell)
	{
		if (Showcase(shell) is not { } trip)
			return;
		await shell.GoToAsync($"{AppShell.TripRoute}?id={trip.Id}");
		await Task.Delay(1500);
		await SailfishUiDriver.ShotAsync("detail");

		// Scrub the elevation profile: hold a finger on it for the shot, then lift.
		var profile = ((TripDetail.TripDetailPage)shell.CurrentPage).ElevationProfile;
		if (SailfishUiDriver.SceneBounds(profile) is { } r)
		{
			Log($"profile at {r}");
			SailfishUiDriver.Press(r.X + r.Width * 0.3, r.Center.Y);
			await Task.Delay(500);
			var still = profile.ScrubIndex;
			SailfishUiDriver.Release();
			await Task.Delay(300);
			Log($"profile touch after a still press: index {still}, events {string.Join(",", profile.TouchCounts.Select(kv => $"{kv.Key}={kv.Value}"))}");
			SailfishUiDriver.Press(r.X + r.Width * 0.2, r.Center.Y);
			await SailfishUiDriver.MoveToAsync(r.X + r.Width * 0.6, r.Center.Y, 400);
			await Task.Delay(300);
			var held = profile.ScrubIndex;
			await SailfishUiDriver.ShotAsync("detail-profile-scrub");
			SailfishUiDriver.Release();
			await Task.Delay(400);
			Log($"profile touch after the drag: events {string.Join(",", profile.TouchCounts.Select(kv => $"{kv.Key}={kv.Value}"))}");
			Check($"a finger on the profile scrubs it (index {held}) and lifting clears it ({profile.ScrubIndex})",
				held > 0 && profile.ScrubIndex == -1);
			// Semantics reach Qt accessibility on the native item (what a screen reader would read).
			var accessible = SailfishUiDriver.NativeProperty(profile, "Accessible.name") ?? "";
			Check($"the profile carries its accessible name ('{accessible}')", accessible == L.T("Elevation profile"));
		}
		else
			Check("the elevation profile is on screen", false);
	}

	private static async Task CameraAsync(Shell shell)
	{
		var services = shell.Handler!.MauiContext!.Services;
		var recording = services.GetRequiredService<Recording.RecordingService>();
		Recording.RecordingService.Simulated = true;
		await recording.StartAsync(Activity.Hike);
		await shell.GoToAsync(AppShell.RecordingRoute);
		await Task.Delay(4000);
		await shell.GoToAsync(Camera.CameraPage.Route);
		var page = (Camera.CameraPage)shell.CurrentPage;
		for (var i = 0; i < 40 && !page.Camera.IsAvailable; i++)
			await Task.Delay(250);
		await Task.Delay(1500);
		await SailfishUiDriver.ShotAsync("camera");
		Log($"camera: {SailfishUiDriver.Probe(page.Camera)}");
		Check($"the camera preview runs (available {page.Camera.IsAvailable}, max zoom {page.Camera.MaximumZoomFactor:0.#})", page.Camera.IsAvailable);

		var path = await page.TakeAsync();
		Log($"camera capture: {path ?? "failed"} ({page.StatusText})");
		await Task.Delay(1500);
		var size = path is not null && File.Exists(path) ? new FileInfo(path).Length : 0;
		Check($"a photo is taken ({path}, {size / 1024} KB) and joins the recording ({recording.Photos.Count} photo)",
			size > 10_000 && recording.Photos.Count == 1);
		await SailfishUiDriver.ShotAsync("camera-taken");
		await shell.GoToAsync("..");
		var trip = await recording.FinishAsync();
		await Task.Delay(800);
		var store = services.GetRequiredService<TripStore>();
		var thumb = trip is { Photos.Count: 1 } ? store.PhotoPath(trip.Id, trip.Photos[0].ThumbnailFile) : "";
		Check($"the saved trip keeps the photo and its thumbnail ({trip?.Photos.Count} photo, thumbnail {File.Exists(thumb)})",
			trip is { Photos.Count: 1 } && File.Exists(thumb));
		if (trip is not null)
		{
			await shell.GoToAsync("..");
			await shell.GoToAsync($"{AppShell.TripRoute}?id={trip.Id}");
			await Task.Delay(1500);
			await SailfishUiDriver.ShotAsync("camera-trip");
			await store.DeleteAsync(trip.Id);   // a tour leaves no trips behind
		}
	}

	private static async Task StatsAsync(Shell shell)
	{
		// Another trip "held" the longest-trip record at the last visit, so this visit celebrates it.
		Preferences.Default.Set("record-longest", "an-older-trip");
		await shell.GoToAsync(AppShell.StatisticsRoute);
		await Task.Delay(1300);
		await SailfishUiDriver.ShotAsync("statistics");
		var page = (Statistics.StatisticsPage)shell.CurrentPage;
		Check($"the month bars grew in ({page.Months.Bars?.Count} bars, progress {page.Months.Progress:0.00})",
			page.Months.Bars is { Count: 12 } && page.Months.Progress > 0.99);
		await Task.Delay(1500);
		var rows = page.RecordRows;
		Check($"the records slid in ({rows.Count} rows, opacity {string.Join("/", rows.Select(r => r.Opacity.ToString("0.#")))}) and the new one is celebrated ({page.Celebrated})",
			rows.Count == 4 && rows.All(r => r.Opacity > 0.99 && r.TranslationX == 0) && page.Celebrated == 1);
	}

	private static async Task PhotoAsync(Shell shell)
	{
		if (Showcase(shell) is not { } trip)
			return;
		var size = SailfishUiDriver.SceneSize;
		await shell.GoToAsync($"{AppShell.TripRoute}?id={trip.Id}");
		await Task.Delay(800);
		await shell.GoToAsync($"{AppShell.PhotoRoute}?id={trip.Id}&index=1");
		await Task.Delay(1500);
		await SailfishUiDriver.ShotAsync("photo");
		var viewer = (PhotoViewerViewModel)shell.CurrentPage.BindingContext;

		await SailfishUiDriver.DragAsync(size.Width * 0.85, size.Height * 0.45, size.Width * 0.1, size.Height * 0.45, 250);
		await Task.Delay(1200);
		await SailfishUiDriver.ShotAsync("photo-next");
		Check($"a swipe moves the carousel (position {viewer.Position}==2) and zooms nothing (scale {Scale(shell):0.##}==1)",
			viewer.Position == 2 && Math.Abs(Scale(shell) - 1) < 0.01);

		// Double tap: 2×, then back.
		await SailfishUiDriver.DoubleTapAsync(size.Width / 2, size.Height * 0.45);
		await Task.Delay(500);
		await SailfishUiDriver.ShotAsync("photo-zoomed");
		Check($"a double tap zooms to 2× (scale {Scale(shell):0.##})", Math.Abs(Scale(shell) - 2) < 0.05);
	}

	/// <summary>
	/// MediaPicker multi-import (T13): the real Silica picker opens (then back out: a cancel returns no files), then two
	/// painted JPEGs go through the same import the picker feeds. The trip is put back afterwards, so repeated tours do
	/// not pile photos up.
	/// </summary>
	private static async Task ImportAsync(Shell shell)
	{
		if (Showcase(shell) is not { } summary)
			return;
		var store = shell.Handler!.MauiContext!.Services.GetRequiredService<TripStore>();
		var original = (await store.LoadAsync(summary.Id))!;
		var before = original.Photos.ToList();
		await shell.GoToAsync($"{AppShell.TripRoute}?id={summary.Id}");
		await Task.Delay(1200);
		var page = (TripDetail.TripDetailPage)shell.CurrentPage;
		var viewModel = (TripDetail.TripDetailViewModel)page.BindingContext;

		var picking = MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions { Title = "Import photos", SelectionLimit = 10 });
		// No screenshot of the picker: it shows the phone owner's own pictures.
		await Task.Delay(2500);
		var pickerShown = SailfishUiDriver.Eval("String(pageStack.currentPage.objectName || pageStack.currentPage)");
		SailfishUiDriver.Eval("pageStack.pop()");
		var cancelled = await Task.WhenAny(picking, Task.Delay(5000)) == picking ? picking.Result : null;
		Check($"the Silica picker opens ({pickerShown}) and a back-out returns no files ({cancelled?.Count.ToString() ?? "no answer"})",
			pickerShown.Contains("Picker", StringComparison.Ordinal) && cancelled is { Count: 0 });

		var dir = Path.Combine(Path.GetTempPath(), "trailhead-import");
		Directory.CreateDirectory(dir);
		var files = new List<string>();
		for (var i = 0; i < 2; i++)
		{
			var path = Path.Combine(dir, $"import-{i}.jpg");
			var photo = original.Photos.Count > 0 ? original.Photos[0] with { Caption = "lake" } : new TripPhoto("x.jpg", 0, 0, original.Start, "lake");
			PhotoPainter.Paint(original, photo, 100 + i, path, Path.Combine(dir, $"import-{i}_t.jpg"));
			// One file from inside the trip's time (placed by time), one from long before it (spread along the track).
			File.SetLastWriteTime(path, i == 0 ? original.Start.AddMinutes(30).LocalDateTime : new DateTime(2001, 1, 1));
			files.Add(path);
		}
		var added = await viewModel.ImportPhotosAsync(files);
		await Task.Delay(1500);
		await SailfishUiDriver.ShotAsync("import-done");
		var saved = (await store.LoadAsync(summary.Id))!;
		var imported = saved.Photos.Where(p => p.File.StartsWith("import-", StringComparison.Ordinal)).ToList();
		var timed = imported.FirstOrDefault(p => Math.Abs((p.Time - original.Start.AddMinutes(30)).TotalMinutes) < 5);
		Check($"two photos imported ({added}), saved ({saved.Photos.Count}=={before.Count + 2}), thumbnails made, the grid shows them ({viewModel.Photos.Count})",
			added == 2 && saved.Photos.Count == before.Count + 2 && viewModel.Photos.Count == before.Count + 2
			&& imported.All(p => File.Exists(store.PhotoPath(saved.Id, p.ThumbnailFile))));
		Check($"the photo from the trip's time sits on the track at that time ({timed?.Time:HH:mm})", timed is not null);

		foreach (var p in imported)
		{
			File.Delete(store.PhotoPath(saved.Id, p.File));
			File.Delete(store.PhotoPath(saved.Id, p.ThumbnailFile));
		}
		saved.Photos = before;
		await store.SaveAsync(saved);
		Directory.Delete(dir, recursive: true);
	}

	private static async Task MapAsync(Shell shell)
	{
		if (Showcase(shell) is not { } trip)
			return;
		await shell.GoToAsync($"{AppShell.TripRoute}?id={trip.Id}");
		await Task.Delay(800);
		await shell.GoToAsync($"{AppShell.TripMapRoute}?id={trip.Id}");
		await Task.Delay(5000);   // tiles over the network
		await SailfishUiDriver.ShotAsync("trip-map");
		var page = (TripMap.TripMapPage)shell.CurrentPage;
		var map = page.Map;
		Log($"trip map: {SailfishUiDriver.Probe(map)}");
		Check($"the map has the Sailfish handler ({map.Handler?.GetType().Name})", map.Handler?.GetType().Name == "SailfishMapHandler");
		var region = map.VisibleRegion;
		Check($"the adapter reports its visible region ({region?.Center.Latitude:0.000},{region?.Center.Longitude:0.000} " +
			$"span {region?.LongitudeDegrees:0.0000}°)", region is { LongitudeDegrees: > 0 });
		Check($"{map.Pins.Count} pins, {map.MapElements.Count} element", map.Pins.Count >= 2 && map.MapElements.Count == 1);

		// A drag across the map pans it: the page stays, no pulley opens, the region moves.
		var size = SailfishUiDriver.SceneSize;
		await SailfishUiDriver.DragAsync(size.Width * 0.3, size.Height * 0.6, size.Width * 0.7, size.Height * 0.4, 400);
		await Task.Delay(1500);
		var moved = map.VisibleRegion;
		Check($"a drag pans the map (centre {region?.Center.Longitude:0.0000} → {moved?.Center.Longitude:0.0000}) and keeps the page",
			shell.CurrentPage == page && moved is not null && region is not null &&
			Math.Abs(moved.Center.Longitude - region.Center.Longitude) > region.LongitudeDegrees * 0.05);
		await SailfishUiDriver.ShotAsync("trip-map-panned");

		map.MapType = Microsoft.Maui.Maps.MapType.Street;
		await Task.Delay(4000);
		await SailfishUiDriver.ShotAsync("trip-map-street");
	}

	private static async Task EditAsync(Shell shell)
	{
		if (Showcase(shell) is not { } trip)
			return;
		await shell.GoToAsync($"{AppShell.TripRoute}?id={trip.Id}");
		await Task.Delay(800);
		await shell.GoToAsync($"{AppShell.TripEditRoute}?id={trip.Id}");
		await Task.Delay(1500);
		await SailfishUiDriver.ShotAsync("trip-edit");
		var edit = (TripEditViewModel)shell.CurrentPage.BindingContext;
		var entry = Find<Entry>(shell.CurrentPage);
		var validation = entry?.Behaviors.OfType<CommunityToolkit.Maui.Behaviors.TextValidationBehavior>().FirstOrDefault();
		Log($"edit: entry text '{entry?.Text}', style {(entry?.Style is null ? "none" : "set")}, behavior IsValid {validation?.IsValid}, " +
			$"Value '{validation?.Value}', IsNotValid {validation?.IsNotValid}");
		Check($"a loaded name is valid (IsNameValid {edit.IsNameValid}, '{edit.Name}') and Save is enabled ({edit.SaveCommand.CanExecute(null)})",
			edit.IsNameValid && edit.SaveCommand.CanExecute(null));
		edit.Name = "ab";
		await Task.Delay(1000);
		Log($"edit after 'ab': entry text '{entry?.Text}', behavior IsValid {validation?.IsValid}, Value '{validation?.Value}', " +
			$"view model IsNameValid {edit.IsNameValid}");
		await SailfishUiDriver.ShotAsync("trip-edit-invalid");
		Check($"two characters are invalid (IsNameValid {edit.IsNameValid}) and Save is disabled",
			!edit.IsNameValid && !edit.SaveCommand.CanExecute(null));
	}

	private static async Task RecordAsync(Shell shell)
	{
		var services = shell.Handler!.MauiContext!.Services;
		var recording = services.GetRequiredService<Recording.RecordingService>();
		var store = services.GetRequiredService<TripStore>();
		Recording.RecordingService.Simulated = true;
		var before = store.Summaries.Count;

		await recording.StartAsync(Activity.Hike);
		await shell.GoToAsync(AppShell.RecordingRoute);
		await Task.Delay(12000);
		await SailfishUiDriver.ShotAsync("recording");
		var recordingMap = ((Recording.RecordingPage)shell.CurrentPage).Map;
		var probe = SailfishUiDriver.Probe(recordingMap) ?? "";
		Log($"recording map: {probe}; MAUI {recordingMap.Pins.Count} pins, line {((Microsoft.Maui.Controls.Maps.Polyline)recordingMap.MapElements[0]).Geopath.Count} points");
		Check("the live track and the position reach the map", probe.Contains("pins=1") && !probe.Contains("points=0"));
		var compass = Find<Controls.CompassView>(shell.CurrentPage);
		var compassProbe = compass is null ? "" : SailfishUiDriver.Probe(compass) ?? "";
		Log($"compass: {compassProbe}");
		Check($"the compass follows the course and points back to the start ({compassProbe})",
			compass is { TargetBearing: not null } && compassProbe.StartsWith("heading=", StringComparison.Ordinal));
		Check($"recording: {recording.Track.Count} fixes, {recording.DistanceM:0} m, moving {recording.Moving:hh\\:mm\\:ss}",
			recording.Track.Count > 10 && recording.DistanceM > 100);
		Check($"the phone keeps the app awake while recording (KeepAlive {Microsoft.Maui.SailfishOS.Platform.SailfishBackground.NativeState})",
			Microsoft.Maui.SailfishOS.Platform.SailfishBackground.NativeState == "true");

		recording.AddWaypoint();
		recording.Pause();
		var pausedAt = recording.Track.Count;
		await Task.Delay(3000);
		await SailfishUiDriver.ShotAsync("recording-paused");
		Check($"a pause stops the track ({pausedAt} → {recording.Track.Count})", recording.Track.Count == pausedAt);
		await recording.ResumeAsync();
		await Task.Delay(4000);
		Check($"resume continues it ({pausedAt} → {recording.Track.Count})", recording.Track.Count > pausedAt);

		// Auto-pause: the replay stands still (speed 0) for longer than StillAfter, then walks on.
		var stillAfter = Recording.RecordingService.StillAfter;
		var speed = Recording.RecordingService.ReplaySpeed;
		Recording.RecordingService.StillAfter = TimeSpan.FromSeconds(6);
		Recording.RecordingService.ReplaySpeed = 0;
		await Task.Delay(13000);
		await SailfishUiDriver.ShotAsync("recording-still");
		Check($"standing still auto-pauses the recording (IsStill {recording.IsStill}, state {recording.State})",
			recording.IsStill && recording.State == Recording.RecordingState.Recording);
		var stillAt = recording.Track.Count;
		Recording.RecordingService.ReplaySpeed = speed;
		await Task.Delay(3000);
		Check($"the first step ends it (IsStill {recording.IsStill}, {stillAt} → {recording.Track.Count} fixes)",
			!recording.IsStill && recording.Track.Count > stillAt);
		Recording.RecordingService.StillAfter = stillAfter;

		await shell.GoToAsync("..");
		await Task.Delay(1500);
		await SailfishUiDriver.ShotAsync("trips-recording-banner");
		var vm = (TripsViewModel)shell.CurrentPage.BindingContext;
		Check($"the list shows the recording banner ('{vm.RecordingBanner}')", vm.IsRecording && vm.RecordingBanner.Contains(L.T("Recording"), StringComparison.Ordinal));

		var trip = await recording.FinishAsync();
		await Task.Delay(1000);
		Check($"finish saves the trip ('{trip?.Name}', {trip?.Track.Count} fixes, {trip?.Waypoints.Count} waypoint; {before} → {store.Summaries.Count} trips)",
			trip is { Waypoints.Count: 1 } && store.Summaries.Count == before + 1);
		Check($"and lets the phone sleep again (KeepAlive {Microsoft.Maui.SailfishOS.Platform.SailfishBackground.NativeState})",
			Microsoft.Maui.SailfishOS.Platform.SailfishBackground.NativeState == "false");
		if (trip is not null)
		{
			await shell.GoToAsync($"{AppShell.TripRoute}?id={trip.Id}");
			await Task.Delay(1500);
			await SailfishUiDriver.ShotAsync("recorded-trip");
			await store.DeleteAsync(trip.Id);   // a tour leaves no trips behind
		}
	}

	/// <summary>
	/// Not in "all" (plan T18): a recording at real speed for TRAILHEAD_SOAK_MINUTES (default 60) with the recording page
	/// on screen, so the live map, the compass and the animated counters run the whole time. A line a minute with the
	/// fixes, managed memory and GC counts; the checks: fixes kept coming, and the managed heap after a full collection
	/// ends within 30 MB of where it started (a leak per fix or per frame would grow it steadily).
	/// </summary>
	private static async Task SoakAsync(Shell shell)
	{
		var minutes = int.TryParse(Environment.GetEnvironmentVariable("TRAILHEAD_SOAK_MINUTES"), out var m) && m > 0 ? m : 60;
		var services = shell.Handler!.MauiContext!.Services;
		var recording = services.GetRequiredService<Recording.RecordingService>();
		var store = services.GetRequiredService<TripStore>();
		var speed = Recording.RecordingService.ReplaySpeed;
		Recording.RecordingService.Simulated = true;
		Recording.RecordingService.ReplaySpeed = 1;
		await recording.StartAsync(Activity.Hike);
		await shell.GoToAsync(AppShell.RecordingRoute);
		await Task.Delay(30000);

		static double Heap()
		{
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();
			return GC.GetTotalMemory(true) / 1048576.0;
		}
		var startHeap = Heap();
		var startFixes = recording.Track.Count;
		var watch = System.Diagnostics.Stopwatch.StartNew();
		var stalls = 0;
		for (var minute = 1; minute <= minutes; minute++)
		{
			var before = recording.Track.Count;
			await Task.Delay(TimeSpan.FromMinutes(1));
			if (recording.Track.Count == before)
				stalls++;
			Log($"soak {minute}/{minutes} min: {recording.Track.Count} fixes, {recording.DistanceM / 1000:0.00} km, " +
				$"managed {GC.GetTotalMemory(false) / 1048576.0:0.0} MB, GCs {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}, " +
				$"working set {Environment.WorkingSet / 1048576.0:0} MB");
			if (minute == 1 || minute % 15 == 0)
				await SailfishUiDriver.ShotAsync($"soak-{minute:000}");
		}
		var endHeap = Heap();
		Check($"a {minutes}-minute recording kept getting fixes ({startFixes} → {recording.Track.Count}, {stalls} still minutes, {watch.Elapsed:hh\\:mm})",
			recording.Track.Count > startFixes + minutes && stalls <= minutes / 4);
		Check($"the managed heap stays flat ({startHeap:0.0} → {endHeap:0.0} MB after a full collection)", endHeap - startHeap < 30);

		await shell.GoToAsync("..");
		var trip = await recording.FinishAsync();
		if (trip is not null)
			await store.DeleteAsync(trip.Id);
		Recording.RecordingService.ReplaySpeed = speed;
	}

	/// <summary>
	/// Not in "all": deletes the trips earlier tours recorded before they cleaned up after themselves. They are the ones
	/// recorded from the simulated GPS: their first fix is exactly the replayed demo route's first point, which a real
	/// recording never is to the last digit. Demo trips and real recordings stay.
	/// </summary>
	private static async Task CleanupAsync(Shell shell)
	{
		var store = shell.Handler!.MauiContext!.Services.GetRequiredService<TripStore>();
		var starts = DemoRoutes.All.Select(r => TripGenerator.Generate(r).Track[0]).Select(p => (p.Lat, p.Lon)).ToHashSet();
		var removed = new List<string>();
		foreach (var summary in store.Summaries.Where(s => !s.Id.StartsWith("demo-", StringComparison.Ordinal)).ToList())
			if (await store.LoadAsync(summary.Id) is { Track.Count: > 0 } trip && starts.Contains((trip.Track[0].Lat, trip.Track[0].Lon)))
			{
				await store.DeleteAsync(trip.Id);
				removed.Add($"{trip.Id} '{trip.Name}'");
			}
		Log($"cleanup removed {removed.Count}, {store.Summaries.Count} trips left: {string.Join(", ", removed)}");
	}

	private static async Task LabAsync(Shell shell)
	{
		await shell.GoToAsync(Lab.LabPage.Route);
		await Task.Delay(1200);
		await SailfishUiDriver.ShotAsync("lab");

		await shell.GoToAsync(Lab.AnimationsLabPage.Route);
		await Task.Delay(1000);
		var animations = (Lab.AnimationsLabPage)shell.CurrentPage;
		await animations.RaceAsync();
		await SailfishUiDriver.ShotAsync("lab-animations");
		var ends = animations.RacerPositions;
		Check($"every easing ends at the finish ({string.Join(",", ends.Select(e => e.ToString("0")))})",
			ends.Count == 6 && ends.All(e => Math.Abs(e - ends[0]) < 1 && e > 40));
		_ = animations.SequenceAsync();
		animations.ToggleSlow();
		await Task.Delay(1500);
		animations.ToggleSlow();
		var stoppedAt = animations.SlowPosition;
		await Task.Delay(800);
		Check($"an animation cancelled mid-way stays where it stopped ({stoppedAt:0} → {animations.SlowPosition:0} of 260)",
			stoppedAt is > 20 and < 240 && Math.Abs(animations.SlowPosition - stoppedAt) < 0.5);
		await shell.GoToAsync("..");

		await shell.GoToAsync(Lab.GesturesLabPage.Route);
		await Task.Delay(1000);
		await SailfishUiDriver.ShotAsync("lab-gestures");
		await shell.GoToAsync("..");

		await shell.GoToAsync(Lab.BehaviorsLabPage.Route);
		await Task.Delay(1000);
		var behaviors = (Lab.BehaviorsLabPage)shell.CurrentPage;
		behaviors.EmailEntry.Text = "not an e-mail";
		await Task.Delay(300);
		var invalid = behaviors.EmailCheck.IsValid;
		behaviors.EmailEntry.Text = "hiker@example.com";
		await Task.Delay(300);
		Check($"the e-mail check flips (invalid {!invalid}, valid {behaviors.EmailCheck.IsValid})", !invalid && behaviors.EmailCheck.IsValid);
		foreach (var prefix in new[] { "K", "Ka", "Kas", "Kasp" })
		{
			behaviors.SearchEntry.Text = prefix;
			await Task.Delay(120);
		}
		await Task.Delay(1200);
		Check($"typing four letters searches once, after the pause ({behaviors.Searches}×: '{behaviors.SearchResult}')",
			behaviors.Searches == 1 && behaviors.SearchResult.Contains("Kasprowy"));
		await SailfishUiDriver.ShotAsync("lab-behaviors");
		await shell.GoToAsync("..");

		await shell.GoToAsync(Lab.EffectsLabPage.Route);
		await Task.Delay(1200);
		var effects = (Lab.EffectsLabPage)shell.CurrentPage;
		await SailfishUiDriver.ShotAsync("lab-effects");
		Check($"the RoutingEffect reached its Sailfish PlatformEffect (attached {effects.EffectAttached} " +
			$"[{Lab.SailfishGlowEffect.Attached}/{Lab.SailfishGlowEffect.Detached}, {Lab.SailfishGlowEffect.LastAttach}], shadow {effects.EffectShadow is not null})",
			effects.EffectAttached && effects.EffectShadow is not null);
		Check($"the mapper key ran (shadow {effects.MapperShadow is not null})", effects.MapperRan && effects.MapperShadow is not null);
		await shell.GoToAsync("..");

		await shell.GoToAsync(Lab.SwipeLabPage.Route);
		await Task.Delay(1000);
		await SailfishUiDriver.ShotAsync("lab-swipe");
		await shell.GoToAsync("..");

		await shell.GoToAsync(Lab.SosPage.Route);
		await Task.Delay(800);
		var sos = (Lab.SosPage)shell.CurrentPage;
		_ = sos.RunAsync();
		await Task.Delay(2600);
		await SailfishUiDriver.ShotAsync("lab-sos");
		var flashes = sos.Flashes;
		sos.Stop();
		Check($"SOS flashes the screen ({flashes} signals in 2.6 s)", flashes >= 3);
		Log($"torch: {await Lab.SosPage.TryTorchAsync()}");
		await shell.GoToAsync("..");

	}

	private static async Task StressAsync(Shell shell)
	{
		await shell.GoToAsync(Lab.StressLabPage.Route);
		await Task.Delay(800);
		var stress = (Lab.StressLabPage)shell.CurrentPage;
		await stress.ListAsync(10_000);
		await SailfishUiDriver.ShotAsync("lab-stress-list");
		await stress.ProfileAsync(50_000);
		await SailfishUiDriver.ShotAsync("lab-stress-profile");
		stress.Memory();
		foreach (var line in stress.Results)
			Log($"stress: {line}");
		Check($"the stress loads ran ({stress.Results.Count} results)", stress.Results.Count == 3);
		await shell.GoToAsync("..");
	}

	/// <summary>Where a long list stops working: each step logs before it starts, so a crash names its size.</summary>
	private static async Task ListScaleAsync(Shell shell)
	{
		// A 480 KB rows payload through the shim's set_property: the runtime's string marshaller copied it onto the 2 MB
		// main-thread stack and the app died ("Stack overflow"); large payloads now cross as heap UTF-8.
		await shell.GoToAsync(Lab.StressLabPage.Route);
		await Task.Delay(800);
		var page = (Lab.StressLabPage)shell.CurrentPage;
		await page.ListAsync(100, ItemSizingStrategy.MeasureFirstItem);
		await Task.Delay(1500);
		// The same rows through the shim's set_property, as PushRows sends them.
		var json = "[" + string.Join(",", Enumerable.Range(0, 10_000).Select(i =>
			$"{{\"k\":{2_000_000 + i},\"r\":{i},\"h\":90,\"t\":1,\"g\":0,\"n\":1}}")) + "]";
		Log($"listscale live adapter 10k via set_property starting ({json.Length} chars)");
		var rc = SailfishUiDriver.SetAdapterString(page.List!, "mauiRowsJson", json);
		var probe = SailfishUiDriver.Probe(page.List!) ?? "";
		Log($"listscale live adapter 10k via set_property: rc {rc}, {probe}");
		Check($"a 480 KB payload crosses to the shim ({json.Length} chars: rc {rc}, '{probe}')", rc == 0 && probe.StartsWith("10000 rows"));
		await shell.GoToAsync("..");
		await shell.GoToAsync(Lab.StressLabPage.Route);
		await Task.Delay(800);
		var stress = (Lab.StressLabPage)shell.CurrentPage;
		foreach (var (count, sizing) in new[]
		{
			(10_000, ItemSizingStrategy.MeasureFirstItem), (1_000, ItemSizingStrategy.MeasureAllItems),
			(3_000, ItemSizingStrategy.MeasureAllItems), (6_000, ItemSizingStrategy.MeasureAllItems),
		})
		{
			Log($"listscale {count} {sizing} starting");
			await stress.ListAsync(count, sizing);
			Log($"listscale {count} {sizing}: {stress.Results.LastOrDefault()}");
			Check($"a list of {count:N0} ({sizing}) comes up", stress.Results.LastOrDefault()?.Contains($"{count:N0}") == true);
		}
		await shell.GoToAsync("..");
	}

	private static async Task DiagnosticsAsync(Shell shell)
	{
		await shell.GoToAsync(AppShell.DiagnosticsRoute);
		await Task.Delay(1500);
		await Task.Delay(2000);   // the sensor census listens for 1.5 s
		await SailfishUiDriver.ShotAsync("diagnostics");
		var vm = (Diagnostics.DiagnosticsViewModel)shell.CurrentPage.BindingContext;
		foreach (var row in vm.Sensors)
			Log($"sensor {row.Name}: {row.Value}");
		Check($"the sensor census ran ({vm.Sensors.Count} rows)", vm.Sensors.Count >= 6);
		var version = vm.Build.FirstOrDefault(r => r.Name == "Version")?.Value;
		Check($"AppInfo carries the project's version ({version})", version == "0.1 (1)");
	}

	/// <summary>The trip the tour shows: one with several photos.</summary>
	private static TripSummary? Showcase(Shell shell)
	{
		var store = shell.Handler!.MauiContext!.Services.GetRequiredService<TripStore>();
		return store.Summaries.FirstOrDefault(s => s.PhotoCount > 3) ?? store.Summaries.FirstOrDefault();
	}

	private static double Scale(Shell shell) =>
		(shell.CurrentPage as PhotoViewerPage)?.CurrentImageScale ?? double.NaN;

	/// <summary>The flat row index of the first materialized row bound to a <typeparamref name="T"/>.</summary>
	private static int? FirstRow<T>()
	{
		for (var i = 0; i < 12; i++)
			if (SailfishUiDriver.CollectionRow(i)?.BindingContext is T)
				return i;
		return null;
	}

	private static void Check(string what, bool ok)
	{
		if (!ok)
			_failed++;
		Log($"CHECK {(ok ? "PASS" : "FAIL")} {what}");
	}

	/// <summary>Also /tmp/trailhead-tour.log on the phone: tools/sf run stops capturing after a while.</summary>
	private static void Log(string message)
	{
		var line = $"[Trailhead] TOUR {message}";
		Console.Error.WriteLine(line);
		try
		{
			File.AppendAllText("/tmp/trailhead-tour.log", $"{DateTime.Now:HH:mm:ss.fff} {line}\n");
		}
		catch (IOException)
		{
		}
	}

	private static T? Find<T>(IVisualTreeElement root) where T : class =>
		root as T ?? root.GetVisualChildren().Select(Find<T>).FirstOrDefault(x => x is not null);
}
