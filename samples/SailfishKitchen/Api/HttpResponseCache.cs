using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SailfishKitchen.Services;

namespace SailfishKitchen.Api;

/// <summary>Index of cached response bodies, kept apart from the bodies so freshness checks read no payload.</summary>
public sealed class CacheIndex
{
	[JsonPropertyName("version")] public int Version { get; set; } = 1;

	[JsonPropertyName("entries")] public List<CacheIndexEntry> Entries { get; set; } = [];
}

public sealed class CacheIndexEntry
{
	[JsonPropertyName("key")] public string Key { get; set; } = string.Empty;

	[JsonPropertyName("file")] public string File { get; set; } = string.Empty;

	[JsonPropertyName("url")] public string Url { get; set; } = string.Empty;

	[JsonPropertyName("cachedAtUtc")] public DateTimeOffset CachedAtUtc { get; set; }

	[JsonPropertyName("bytes")] public long Bytes { get; set; }
}

/// <summary>
/// On-disk cache of raw TheMealDB response bodies, primed from the bundled seed. Caching bodies rather than
/// domain objects keeps online and offline on the same parser and DTOs.
/// </summary>
public sealed class HttpResponseCache
{
	private const string IndexFileName = "index.json";
	private const string CacheDirectoryName = "http-cache";

	private readonly string _directory;
	private readonly ILogger<HttpResponseCache> _logger;
	private readonly object _gate = new();
	private readonly Dictionary<string, CacheIndexEntry> _index = new(StringComparer.Ordinal);
	private bool _indexLoaded;

	public HttpResponseCache(IJsonFileStore store, ILogger<HttpResponseCache> logger)
	{
		_logger = logger;
		_directory = Path.Combine(store.RootDirectory, CacheDirectoryName);
		System.IO.Directory.CreateDirectory(_directory);
	}

	public string Directory => _directory;

	public int EntryCount
	{
		get { lock (_gate) { EnsureLoaded(); return _index.Count; } }
	}

	public long TotalBytes
	{
		get { lock (_gate) { EnsureLoaded(); return _index.Values.Sum(e => e.Bytes); } }
	}

	/// <summary>A filesystem-safe, collision-resistant key for an absolute URI.</summary>
	public static string MakeKey(Uri uri)
	{
		var endpoint = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "root";
		var query = string.IsNullOrEmpty(uri.Query) ? string.Empty : "_" + uri.Query.TrimStart('?');

		var raw = endpoint + query;
		var builder = new StringBuilder(raw.Length + 8);
		foreach (var c in raw)
			builder.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '=' or '-' ? c : '_');

		return builder.Append(".json").ToString();
	}

	public bool TryGet(string key, TimeSpan ttl, out string body)
	{
		body = string.Empty;

		CacheIndexEntry? entry;
		lock (_gate)
		{
			EnsureLoaded();
			if (!_index.TryGetValue(key, out entry))
				return false;

			// ttl == Zero means never expires, so bundled seed entries survive a device with a wrong clock.
			if (ttl > TimeSpan.Zero && DateTimeOffset.UtcNow - entry.CachedAtUtc > ttl)
			{
				_logger.LogDebug("http-cache: {Key} stale (age {Age})", key, DateTimeOffset.UtcNow - entry.CachedAtUtc);
				return false;
			}
		}

		try
		{
			body = File.ReadAllText(Path.Combine(_directory, entry.File));
			return true;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			_logger.LogWarning(ex, "http-cache: {Key} indexed but unreadable", key);
			lock (_gate) { _index.Remove(key); PersistIndex(); }
			return false;
		}
	}

	public bool Contains(string key)
	{
		lock (_gate) { EnsureLoaded(); return _index.ContainsKey(key); }
	}

	public void Put(string key, Uri url, string body)
	{
		try
		{
			var path = Path.Combine(_directory, key);
			File.WriteAllText(path, body);

			lock (_gate)
			{
				EnsureLoaded();
				_index[key] = new CacheIndexEntry
				{
					Key = key,
					File = key,
					Url = url.ToString(),
					CachedAtUtc = DateTimeOffset.UtcNow,
					Bytes = new FileInfo(path).Length,
				};
				PersistIndex();
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
		{
			_logger.LogWarning(ex, "http-cache: could not store {Key}", key);
		}
	}

	/// <summary>
	/// Copies bundled recorded responses in for keys not yet cached, so a first offline run shows a full catalog.
	/// Returns the number of entries primed.
	/// </summary>
	public int PrimeFromSeed(string seedDirectory)
	{
		if (!System.IO.Directory.Exists(seedDirectory))
		{
			_logger.LogInformation("http-cache: no seed directory at {Dir}", seedDirectory);
			return 0;
		}

		var primed = 0;
		lock (_gate)
		{
			EnsureLoaded();

			foreach (var seedFile in System.IO.Directory.GetFiles(seedDirectory, "*.json"))
			{
				var key = Path.GetFileName(seedFile);
				if (_index.ContainsKey(key))
					continue;

				try
				{
					var body = File.ReadAllText(seedFile);
					File.WriteAllText(Path.Combine(_directory, key), body);
					_index[key] = new CacheIndexEntry
					{
						Key = key,
						File = key,
						Url = "seed://" + key,
						CachedAtUtc = DateTimeOffset.UtcNow,
						Bytes = body.Length,
					};
					primed++;
				}
				catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
				{
					_logger.LogWarning(ex, "http-cache: could not prime seed entry {Key}", key);
				}
			}

			if (primed > 0)
				PersistIndex();
		}

		if (primed > 0)
			_logger.LogInformation("http-cache: primed {Count} offline seed entries", primed);

		return primed;
	}

	public void Clear()
	{
		lock (_gate)
		{
			EnsureLoaded();
			foreach (var entry in _index.Values)
			{
				try { File.Delete(Path.Combine(_directory, entry.File)); }
				catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
			}

			_index.Clear();
			PersistIndex();
		}

		_logger.LogInformation("http-cache: cleared");
	}

	/// <summary>Caller holds <see cref="_gate"/>.</summary>
	private void EnsureLoaded()
	{
		if (_indexLoaded)
			return;

		_indexLoaded = true;

		var indexPath = Path.Combine(_directory, IndexFileName);
		if (!File.Exists(indexPath))
			return;

		try
		{
			using var stream = File.OpenRead(indexPath);
			var index = JsonSerializer.Deserialize(stream, AppJsonContext.Default.CacheIndex);
			if (index?.Entries is null)
				return;

			foreach (var entry in index.Entries)
			{
				if (!string.IsNullOrEmpty(entry?.Key))
					_index[entry.Key] = entry;
			}

			_logger.LogInformation("http-cache: index has {Count} entries", _index.Count);
		}
		catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
		{
			_logger.LogWarning(ex, "http-cache: index unreadable, starting empty");
			_index.Clear();
		}
	}

	/// <summary>Caller holds <see cref="_gate"/>.</summary>
	private void PersistIndex()
	{
		try
		{
			var payload = new CacheIndex { Version = 1, Entries = _index.Values.ToList() };

			var temp = Path.Combine(_directory, IndexFileName + ".tmp");
			using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				JsonSerializer.Serialize(stream, payload, AppJsonContext.Default.CacheIndex);
			}

			File.Move(temp, Path.Combine(_directory, IndexFileName), overwrite: true);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
		{
			// Losing the index only costs re-fetches; bodies stay on disk.
			_logger.LogWarning(ex, "http-cache: could not persist index");
		}
	}
}
