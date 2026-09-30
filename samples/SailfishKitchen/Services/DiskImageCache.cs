using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Maui.Controls;
using SailfishKitchen.Api;

namespace SailfishKitchen.Services;

/// <summary>
/// Resolves thumbnail URLs to image sources and keeps a disk copy of each (bounded concurrency,
/// in-flight de-duplication) so tiles still render offline.
/// </summary>
public interface IImageCache
{
	/// <summary>Remote URI online (caching a copy), cached file offline, placeholder when neither is available.</summary>
	Task<ImageSource> ResolveAsync(string url, CancellationToken ct = default);

	/// <summary>True when a copy of this URL is already on disk.</summary>
	bool HasCached(string url);

	/// <summary>Bundled art shown while a thumb is in flight or when it never arrives.</summary>
	ImageSource Placeholder { get; }

	int CachedCount { get; }

	long TotalBytes { get; }

	Task ClearAsync();

	/// <summary>Drops the oldest files until the cache is under <paramref name="maxBytes"/>.</summary>
	Task PruneAsync(long maxBytes);
}

public sealed class DiskImageCache : IImageCache
{
	private const string ImagesDirectoryName = "images";
	private const string PlaceholderFileName = "meal_placeholder.png";
	private const long DefaultMaxBytes = 24 * 1024 * 1024;

	private readonly string _directory;
	private readonly IMealDbClient _client;
	private readonly ISettingsService _settings;
	private readonly SemaphoreSlim _concurrency;
	private readonly ILogger<DiskImageCache> _logger;
	private readonly object _gate = new();
	private readonly Dictionary<string, Task<string?>> _inFlight = new(StringComparer.Ordinal);
	private ImageSource? _placeholder;

	public DiskImageCache(
		IJsonFileStore store,
		IMealDbClient client,
		ISettingsService settings,
		IOptions<MealDbOptions> options,
		ILogger<DiskImageCache> logger)
	{
		_client = client;
		_settings = settings;
		_logger = logger;
		_directory = Path.Combine(store.RootDirectory, ImagesDirectoryName);
		Directory.CreateDirectory(_directory);
		_concurrency = new SemaphoreSlim(Math.Max(1, options.Value.MaxConcurrentImageDownloads));
	}

	public ImageSource Placeholder => _placeholder ??= ImageSource.FromFile(PlaceholderFileName);

	public int CachedCount => SafeEnumerate().Count();

	public long TotalBytes => SafeEnumerate().Sum(f => { try { return f.Length; } catch (IOException) { return 0L; } });

	public bool HasCached(string url)
	{
		if (string.IsNullOrWhiteSpace(url))
			return false;

		return File.Exists(PathFor(url));
	}

	/// <summary>
	/// Online this returns the remote URI: Qt's QML Image loads http(s) natively, while file:// sources are
	/// unreliable and paths outside sailjail's whitelist are unreadable. Offline the cached file is used.
	/// </summary>
	public async Task<ImageSource> ResolveAsync(string url, CancellationToken ct = default)
	{
		if (string.IsNullOrWhiteSpace(url))
			return Placeholder;

		var path = PathFor(url);
		var offline = !_settings.Current.AllowNetwork;

		if (offline)
			return File.Exists(path) ? ImageSource.FromFile(path) : Placeholder;

		if (Uri.TryCreate(url, UriKind.Absolute, out var remote))
		{
			// Fire and forget: the tile shows the remote picture now, the disk copy serves offline mode later.
			if (_settings.Current.CacheImages && !File.Exists(path))
				_ = DownloadOnceAsync(url, path, ct);

			return ImageSource.FromUri(remote);
		}

		return Placeholder;
	}

	public async Task ClearAsync()
	{
		foreach (var file in SafeEnumerate())
		{
			try { file.Delete(); }
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
		}

		_logger.LogInformation("image-cache: cleared");
		await Task.CompletedTask.ConfigureAwait(false);
	}

	public async Task PruneAsync(long maxBytes)
	{
		var budget = maxBytes <= 0 ? DefaultMaxBytes : maxBytes;
		var files = SafeEnumerate().OrderBy(f => f.LastWriteTimeUtc).ToList();
		var total = files.Sum(f => { try { return f.Length; } catch (IOException) { return 0L; } });

		var removed = 0;
		foreach (var file in files)
		{
			if (total <= budget)
				break;

			try
			{
				total -= file.Length;
				file.Delete();
				removed++;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
		}

		if (removed > 0)
			_logger.LogInformation("image-cache: pruned {Count} files, {Bytes} bytes remain", removed, total);

		await Task.CompletedTask.ConfigureAwait(false);
	}

	/// <summary>Collapses concurrent requests for one URL, since recycled cells ask for the same thumb repeatedly.</summary>
	private Task<string?> DownloadOnceAsync(string url, string path, CancellationToken ct)
	{
		lock (_gate)
		{
			if (_inFlight.TryGetValue(path, out var existing))
				return existing;

			var task = DownloadAsync(url, path, ct);
			_inFlight[path] = task;
			return task;
		}
	}

	private async Task<string?> DownloadAsync(string url, string path, CancellationToken ct)
	{
		try
		{
			if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
				return null;

			await _concurrency.WaitAsync(ct).ConfigureAwait(false);
			try
			{
				if (File.Exists(path))
					return path;

				var ok = await _client.DownloadImageAsync(uri, path, ct).ConfigureAwait(false);
				return ok && File.Exists(path) ? path : null;
			}
			finally
			{
				_concurrency.Release();
			}
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested)
		{
			return null;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "image-cache: failed to fetch {Url}", url);
			return null;
		}
		finally
		{
			lock (_gate) { _inFlight.Remove(path); }
		}
	}

	private string PathFor(string url)
	{
		var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
		var extension = GuessExtension(url);
		return Path.Combine(_directory, hash + extension);
	}

	private static string GuessExtension(string url)
	{
		var path = url.Split('?', '#')[0];
		var extension = Path.GetExtension(path).ToLowerInvariant();
		return extension is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" ? extension : ".jpg";
	}

	private IEnumerable<FileInfo> SafeEnumerate()
	{
		try
		{
			return new DirectoryInfo(_directory).EnumerateFiles("*.jpg")
				.Concat(new DirectoryInfo(_directory).EnumerateFiles("*.png"));
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
		{
			return Array.Empty<FileInfo>();
		}
	}
}
