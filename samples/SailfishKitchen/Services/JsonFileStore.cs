using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using SailfishKitchen.Api;
using SailfishKitchen.Models;

namespace SailfishKitchen.Services;

/// <summary>
/// Source-generated serializer context for everything persisted; the trimmed ReadyToRun payload rules out
/// reflection-based serialization.
/// </summary>
[JsonSourceGenerationOptions(
	PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
	DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	WriteIndented = true,
	GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(FavoritesFile))]
[JsonSerializable(typeof(SearchHistoryFile))]
[JsonSerializable(typeof(CacheIndex))]
internal sealed partial class AppJsonContext : JsonSerializerContext;

/// <summary>Persisted favourites. Versioned so a future schema change can migrate instead of crash.</summary>
public sealed class FavoritesFile
{
	public const int CurrentVersion = 1;

	[JsonPropertyName("version")] public int Version { get; set; } = CurrentVersion;

	[JsonPropertyName("meals")] public List<FavoriteMeal> Meals { get; set; } = [];
}

/// <summary>
/// Typed JSON persistence under the app data directory. Writes go to a temp file and are moved into place,
/// so a crash mid-write cannot leave a truncated file.
/// </summary>
public interface IJsonFileStore
{
	string RootDirectory { get; }

	/// <summary>Absolute path of a named JSON file under <see cref="RootDirectory"/>.</summary>
	string ResolvePath(string fileName);

	T? Load<T>(string fileName) where T : class;

	void Save<T>(string fileName, T value) where T : class;

	bool Delete(string fileName);
}

public sealed class JsonFileStore : IJsonFileStore
{
	private readonly ILogger<JsonFileStore> _logger;

	public JsonFileStore(ILogger<JsonFileStore> logger)
	{
		_logger = logger;
		RootDirectory = ResolveRoot();
		Directory.CreateDirectory(RootDirectory);
	}

	public string RootDirectory { get; }

	public string ResolvePath(string fileName)
	{
		// Guard against a future caller escaping the data directory with "../".
		if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains('/') || fileName.Contains('\\') || fileName.Contains(".."))
			throw new ArgumentException($"'{fileName}' is not a valid single-segment file name.", nameof(fileName));

		return Path.Combine(RootDirectory, fileName);
	}

	public T? Load<T>(string fileName) where T : class
	{
		var path = ResolvePath(fileName);
		if (!File.Exists(path))
		{
			_logger.LogDebug("store: no {File} yet", fileName);
			return null;
		}

		try
		{
			using var stream = File.OpenRead(path);
			var value = JsonSerializer.Deserialize(stream, GetTypeInfo(typeof(T))) as T;
			_logger.LogDebug("store: loaded {File}", fileName);
			return value;
		}
		catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
		{
			// A corrupt file degrades to defaults; quarantine it for inspection.
			_logger.LogError(ex, "store: {File} is unreadable, quarantining", fileName);
			TryMove(path, path + ".corrupt");
			return null;
		}
	}

	public void Save<T>(string fileName, T value) where T : class
	{
		var path = ResolvePath(fileName);
		var temp = path + ".tmp";
		try
		{
			using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				JsonSerializer.Serialize(stream, value, GetTypeInfo(typeof(T)));
				stream.Flush(flushToDisk: true);
			}

			File.Move(temp, path, overwrite: true);
			_logger.LogDebug("store: saved {File}", fileName);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
		{
			_logger.LogError(ex, "store: failed to save {File}", fileName);
			throw;
		}
	}

	public bool Delete(string fileName)
	{
		try
		{
			var path = ResolvePath(fileName);
			if (!File.Exists(path))
				return false;

			File.Delete(path);
			return true;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			_logger.LogWarning(ex, "store: could not delete {File}", fileName);
			return false;
		}
	}

	private static string ResolveRoot()
	{
		try
		{
			var dir = Microsoft.Maui.Storage.FileSystem.AppDataDirectory;
			if (!string.IsNullOrWhiteSpace(dir))
				return Path.Combine(dir, "kitchen");
		}
		catch (Exception)
		{
			// NotAvailableException on an uninitialised platform implementation.
		}

		// Harbour convention: $HOME/.local/share/<package>, derived from the install prefix. It is on sailjail's
		// whitelist; /tmp is not, and cached thumbnails there were unreadable by the QML Image.
		var home = Environment.GetEnvironmentVariable("HOME");
		var package = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd('/')).Name;
		if (!string.IsNullOrWhiteSpace(home) && package.StartsWith("harbour-", StringComparison.Ordinal))
		{
			var dir = Path.Combine(home, ".local", "share", package, "kitchen");
			Directory.CreateDirectory(dir);
			return dir;
		}

		var fallback = Path.Combine(Path.GetTempPath(), "sailfish-kitchen");
		Directory.CreateDirectory(fallback);
		return fallback;
	}

	private static JsonTypeInfo GetTypeInfo(Type type) => type switch
	{
		_ when type == typeof(AppSettings) => AppJsonContext.Default.AppSettings,
		_ when type == typeof(FavoritesFile) => AppJsonContext.Default.FavoritesFile,
		_ when type == typeof(SearchHistoryFile) => AppJsonContext.Default.SearchHistoryFile,
		_ when type == typeof(CacheIndex) => AppJsonContext.Default.CacheIndex,
		_ => throw new NotSupportedException($"No source-generated JSON metadata for {type.Name}. Add it to AppJsonContext."),
	};

	private static void TryMove(string from, string to)
	{
		try { File.Move(from, to, overwrite: true); }
		catch (Exception) { /* best effort */ }
	}
}
