using Trailhead.Data;
using Trailhead.Features.Shared;
using Trailhead.Features.Tour;

namespace Trailhead;

public class App : Application
{
	private readonly AppShell _shell;
	private readonly TripStore _store;
	private readonly Features.Recording.RecordingService _recording;

	public App(AppShell shell, TripStore store, Features.Recording.RecordingService recording)
	{
		_shell = shell;
		_store = store;
		_recording = recording;
		ThemeResources.Apply(this);
	}

	/// <summary>trailhead://trip/&lt;id&gt; opens that trip; a GPX file handed over by another app (Files, the browser's
	/// downloads, an e-mail) is imported and opened.</summary>
	protected override async void OnAppLinkRequestReceived(Uri uri)
	{
		base.OnAppLinkRequestReceived(uri);
		Console.Error.WriteLine($"[Trailhead] app link {uri}");
		await _store.Ready;
		if (uri.Scheme == "trailhead" && uri.Host == "trip")
		{
			var id = uri.AbsolutePath.Trim('/');
			if (_store.Summaries.Any(s => s.Id == id))
				await _shell.GoToAsync($"//trips/{AppShell.TripRoute}?id={id}");
			return;
		}
		if (uri.IsFile && uri.LocalPath.EndsWith(".gpx", StringComparison.OrdinalIgnoreCase))
		{
			try
			{
				var trip = Gpx.Read(await File.ReadAllTextAsync(uri.LocalPath), _store.NewId());
				await _store.SaveAsync(trip);
				await _shell.GoToAsync($"//trips/{AppShell.TripRoute}?id={trip.Id}");
			}
			catch (Exception ex) when (ex is FormatException or System.Xml.XmlException or IOException or UnauthorizedAccessException)
			{
				await _shell.DisplayAlertAsync("Not a GPX track", $"{Path.GetFileName(uri.LocalPath)}: {ex.Message}", "OK");
			}
		}
	}

	private void Lifecycle(string what) =>
		Console.Error.WriteLine($"[Trailhead] LIFECYCLE window {what}{(_recording.IsActive ? $" while recording ({_recording.Track.Count} fixes)" : "")}");

	// The window title is what the cover shows above the app's own lines.
	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(_shell) { Title = AppFlavor.Title };
		window.Created += async (_, _) =>
		{
			// The ambience colours are known once the window is up.
			ThemeResources.Attach(this);
			await _store.InitializeAsync();
			if (Environment.GetEnvironmentVariable("TRAILHEAD_TOUR") is { Length: > 0 } tour)
				await ShotTour.RunAsync(_shell, tour);
		};
		// Lifecycle (plan T11): on Sailfish, Deactivated/Activated come with the cover (the app minimized to the home
		// screen) and Stopped/Resumed when the window is hidden; a recording writes its draft at once, since an app in
		// the background is the one the system kills first under memory pressure.
		window.Activated += (_, _) => Lifecycle("activated");
		window.Deactivated += (_, _) => Lifecycle("deactivated");
		window.Resumed += (_, _) => Lifecycle("resumed");
		window.Stopped += async (_, _) =>
		{
			Lifecycle("stopped");
			await _recording.SaveDraftAsync();
		};
		return window;
	}
}
