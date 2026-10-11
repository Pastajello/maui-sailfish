using System.Diagnostics;
using System.Text.Json;
using Microsoft.Maui.Storage;

namespace Trailhead.Data;

/// <summary>
/// Trips on disk (plan D8): <c>trips/index.json</c> holds the summaries the list shows, <c>trips/&lt;id&gt;/trip.json</c>
/// a full trip, <c>trips/&lt;id&gt;/photos/</c> its photos. The first launch generates the demo set. UI thread.
/// </summary>
public sealed class TripStore
{
	/// <summary>Bump when the generator changes: the demo trips are generated again on the next launch.</summary>
	private const string DemoVersion = "1";

	private readonly string _root;
	private readonly TaskCompletionSource _ready = new();
	private List<TripSummary> _summaries = new();
	private (string Id, Trip Trip)? _last;

	public TripStore() : this(Path.Combine(FileSystem.AppDataDirectory, "trips"))
	{
	}

	public TripStore(string root)
	{
		_root = root;
	}

	/// <summary>Newest first.</summary>
	public IReadOnlyList<TripSummary> Summaries => _summaries;

	/// <summary>Completes once the index is loaded (and the demo set generated on the first launch).</summary>
	public Task Ready => _ready.Task;

	/// <summary>Raised after any change to the set of trips or a trip's summary.</summary>
	public event EventHandler? Changed;

	/// <summary>Progress of the first-launch generation, 0…1.</summary>
	public event EventHandler<double>? Generating;

	public string PhotoPath(string tripId, string file) => Path.Combine(_root, tripId, "photos", file);

	public async Task InitializeAsync()
	{
		try
		{
			Directory.CreateDirectory(_root);
			var versionFile = Path.Combine(_root, "demo.version");
			var index = await ReadIndexAsync();
			if (index is null || !File.Exists(versionFile) || await File.ReadAllTextAsync(versionFile) != DemoVersion)
			{
				index = (index ?? new()).Where(s => !s.Id.StartsWith("demo-", StringComparison.Ordinal)).ToList();
				index.AddRange(await GenerateDemoAsync());
				await File.WriteAllTextAsync(versionFile, DemoVersion);
			}
			_summaries = Sorted(index);
			await WriteIndexAsync();
			_ready.TrySetResult();
			Changed?.Invoke(this, EventArgs.Empty);
		}
		catch (Exception ex)
		{
			_ready.TrySetException(ex);
			throw;
		}
	}

	/// <summary>Deletes the demo trips (the user's own stay) and generates them again.</summary>
	public async Task RegenerateDemoAsync()
	{
		foreach (var demo in _summaries.Where(s => s.Id.StartsWith("demo-", StringComparison.Ordinal)).ToList())
		{
			var dir = Path.Combine(_root, demo.Id);
			if (Directory.Exists(dir))
				await Task.Run(() => Directory.Delete(dir, recursive: true));
		}
		_last = null;
		var own = _summaries.Where(s => !s.Id.StartsWith("demo-", StringComparison.Ordinal));
		_summaries = Sorted(own.Concat(await GenerateDemoAsync()));
		await WriteIndexAsync();
		Changed?.Invoke(this, EventArgs.Empty);
	}

	public async Task<Trip?> LoadAsync(string id)
	{
		if (_last is { } last && last.Id == id)
			return last.Trip;
		var file = TripFile(id);
		if (!File.Exists(file))
			return null;
		await using var stream = File.OpenRead(file);
		var trip = await JsonSerializer.DeserializeAsync(stream, TrailheadJson.Default.Trip);
		if (trip is not null)
			_last = (id, trip);
		return trip;
	}

	public async Task SaveAsync(Trip trip)
	{
		await WriteTripAsync(trip);
		_last = (trip.Id, trip);
		var summary = TripSummary.Of(trip);
		_summaries = Sorted(_summaries.Where(s => s.Id != trip.Id).Append(summary));
		await WriteIndexAsync();
		Changed?.Invoke(this, EventArgs.Empty);
	}

	public async Task DeleteAsync(string id)
	{
		_summaries = _summaries.Where(s => s.Id != id).ToList();
		if (_last?.Id == id)
			_last = null;
		var dir = Path.Combine(_root, id);
		if (Directory.Exists(dir))
			await Task.Run(() => Directory.Delete(dir, recursive: true));
		await WriteIndexAsync();
		Changed?.Invoke(this, EventArgs.Empty);
	}

	public string NewId() => "trip-" + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);

	private async Task<List<TripSummary>> GenerateDemoAsync()
	{
		var watch = Stopwatch.StartNew();
		var routes = DemoRoutes.All;
		var summaries = new TripSummary[routes.Length];
		var done = 0;
		// Generation and painting are CPU work: off the UI thread, one route per worker.
		await Parallel.ForEachAsync(Enumerable.Range(0, routes.Length), async (i, ct) =>
		{
			var trip = TripGenerator.Generate(routes[i]);
			for (var p = 0; p < trip.Photos.Count; p++)
				PhotoPainter.Paint(trip, trip.Photos[p], p, PhotoPath(trip.Id, trip.Photos[p].File),
					PhotoPath(trip.Id, trip.Photos[p].ThumbnailFile));
			await WriteTripAsync(trip);
			summaries[i] = TripSummary.Of(trip);
			var n = Interlocked.Increment(ref done);
			MainThread.BeginInvokeOnMainThread(() => Generating?.Invoke(this, (double)n / routes.Length));
		});
		Console.Error.WriteLine($"[Trailhead] demo set generated: {routes.Length} trips, " +
			$"{summaries.Sum(s => s.PhotoCount)} photos in {watch.ElapsedMilliseconds} ms");
		return summaries.ToList();
	}

	private string TripFile(string id) => Path.Combine(_root, id, "trip.json");

	private async Task WriteTripAsync(Trip trip)
	{
		Directory.CreateDirectory(Path.Combine(_root, trip.Id));
		await WriteAtomicAsync(TripFile(trip.Id), s => JsonSerializer.SerializeAsync(s, trip, TrailheadJson.Default.Trip));
	}

	private async Task<List<TripSummary>?> ReadIndexAsync()
	{
		var file = Path.Combine(_root, "index.json");
		if (!File.Exists(file))
			return null;
		try
		{
			await using var stream = File.OpenRead(file);
			return await JsonSerializer.DeserializeAsync(stream, TrailheadJson.Default.ListTripSummary);
		}
		catch (JsonException ex)
		{
			Console.Error.WriteLine($"[Trailhead] index.json unreadable, rebuilding: {ex.Message}");
			return null;
		}
	}

	private Task WriteIndexAsync() =>
		WriteAtomicAsync(Path.Combine(_root, "index.json"), s => JsonSerializer.SerializeAsync(s, _summaries, TrailheadJson.Default.ListTripSummary));

	/// <summary>Write to a temp file, then rename: a kill mid-write never leaves half a file.</summary>
	private static async Task WriteAtomicAsync(string file, Func<Stream, Task> write)
	{
		var temp = file + ".tmp";
		await using (var stream = File.Create(temp))
			await write(stream);
		File.Move(temp, file, overwrite: true);
	}

	private static List<TripSummary> Sorted(IEnumerable<TripSummary> summaries) =>
		summaries.OrderByDescending(s => s.Start).ToList();
}
