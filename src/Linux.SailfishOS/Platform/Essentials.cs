using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Essentials services: clipboard and URLs go through the shim on the Qt thread, state persists under ~/.config/&lt;app&gt;/.
/// </summary>
internal sealed class SailfishClipboard : IClipboard
{
	public bool HasText => !string.IsNullOrEmpty(QtThread.Run(ReadNative));

	public event EventHandler<EventArgs>? ClipboardContentChanged;

	public Task<string?> GetTextAsync() => QtThread.RunAsync<string?>(ReadNative);

	// QGuiApplication::clipboard() is GUI-thread only: the shim is called on the Qt thread (QtThread).
	public async Task SetTextAsync(string? text)
	{
		await QtThread.RunAsync(() =>
		{
			var utf8 = MarshalUtf8(text ?? string.Empty);
			try
			{
				QtHostNative.sailfish_host_clipboard_set(utf8);
			}
			finally
			{
				Marshal.FreeCoTaskMem(utf8);
			}
			return true;
		}).ConfigureAwait(false);
		ClipboardContentChanged?.Invoke(this, EventArgs.Empty);
	}

	internal static string ReadNative()
	{
		const int cap = 64 * 1024;
		var buf = Marshal.AllocHGlobal(cap);
		try
		{
			var len = QtHostNative.sailfish_host_clipboard_get(buf, cap);
			return len < 0 ? string.Empty : Marshal.PtrToStringUTF8(buf) ?? string.Empty;
		}
		finally
		{
			Marshal.FreeHGlobal(buf);
		}
	}

	internal static IntPtr MarshalUtf8(string s)
	{
		var bytes = Encoding.UTF8.GetBytes(s);
		var ptr = Marshal.AllocCoTaskMem(bytes.Length + 1);
		Marshal.Copy(bytes, 0, ptr, bytes.Length);
		Marshal.WriteByte(ptr, bytes.Length, 0);
		return ptr;
	}
}

/// <summary>
/// JSON-file preferences under ~/.config/&lt;app&gt;/preferences.json. One store: a shared container's keys are
/// namespaced "&lt;sharedName&gt;::key". Thread-safe (one lock), and every write replaces the file atomically (a temp
/// file renamed over it), so a crash mid-write leaves the previous preferences instead of an empty store.
/// </summary>
internal sealed class SailfishPreferences : IPreferences
{
	private const string SharedSeparator = "::";
	private readonly string _path;
	private readonly object _sync = new();
	private Dictionary<string, JsonElement>? _cache;

	public SailfishPreferences() : this(SailfishAppPaths.ConfigFile("preferences.json"))
	{
	}

	/// <summary>Tests: a store at <paramref name="path"/>.</summary>
	internal SailfishPreferences(string path) => _path = path;

	private Dictionary<string, JsonElement> Load()
	{
		if (_cache is not null)
			return _cache;
		try
		{
			if (File.Exists(_path))
				_cache = JsonSerializer.Deserialize(File.ReadAllText(_path), SailfishJsonContext.Default.DictionaryStringJsonElement) ?? new();
			else
				_cache = new();
		}
		catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
		{
			Console.Error.WriteLine($"[Sailfish] Preferences: {_path} unreadable ({ex.Message}) — starting empty");
			_cache = new();
		}
		return _cache;
	}

	private void Save()
	{
		Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
		// Source-generated: the trimmed app disables reflection-based System.Text.Json.
		var temp = _path + ".tmp";
		File.WriteAllText(temp, JsonSerializer.Serialize(_cache!, SailfishJsonContext.Default.DictionaryStringJsonElement));
		File.Move(temp, _path, overwrite: true);
	}

	public bool ContainsKey(string key)
	{
		lock (_sync)
			return Load().ContainsKey(key);
	}

	public bool ContainsKey(string key, string? sharedName) => ContainsKey(Shared(key, sharedName));

	public void Remove(string key)
	{
		lock (_sync)
			if (Load().Remove(key))
				Save();
	}

	public void Remove(string key, string? sharedName) => Remove(Shared(key, sharedName));

	/// <summary>Clears the default container; shared containers keep their keys, as on the other platforms.</summary>
	public void Clear() => Clear(null);

	/// <summary>Clears one container: the default one for a null or empty <paramref name="sharedName"/>.</summary>
	public void Clear(string? sharedName)
	{
		lock (_sync)
		{
			var store = Load();
			var prefix = string.IsNullOrEmpty(sharedName) ? null : sharedName + SharedSeparator;
			var doomed = store.Keys.Where(k => prefix is null ? !k.Contains(SharedSeparator, StringComparison.Ordinal)
				: k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
			if (doomed.Count == 0)
				return;
			foreach (var key in doomed)
				store.Remove(key);
			Save();
		}
	}

	public T Get<T>(string key, T defaultValue)
	{
		JsonElement element;
		lock (_sync)
			if (!Load().TryGetValue(key, out element))
				return defaultValue;
		try
		{
			return Convert(element, defaultValue);
		}
		catch (Exception ex) when (ex is InvalidOperationException or FormatException or JsonException)
		{
			return defaultValue;   // stored as another type
		}
	}

	public T Get<T>(string key, T defaultValue, string? sharedName) => Get(Shared(key, sharedName), defaultValue);

	public void Set<T>(string key, T value)
	{
		var element = Value(value);
		lock (_sync)
		{
			Load()[key] = element;
			Save();
		}
	}

	public void Set<T>(string key, T value, string? sharedName) => Set(Shared(key, sharedName), value);

	// No OS-level shared preferences, so shared sets are namespaced into the single store.
	private static string Shared(string key, string? sharedName) =>
		string.IsNullOrEmpty(sharedName) ? key : sharedName + SharedSeparator + key;

	private static JsonElement Value<T>(T value) => value switch
	{
		null => JsonSerializer.SerializeToElement(null, SailfishJsonContext.Default.String),
		bool b => JsonSerializer.SerializeToElement(b, SailfishJsonContext.Default.Boolean),
		double d => JsonSerializer.SerializeToElement(d, SailfishJsonContext.Default.Double),
		float f => JsonSerializer.SerializeToElement(f, SailfishJsonContext.Default.Single),
		int i => JsonSerializer.SerializeToElement(i, SailfishJsonContext.Default.Int32),
		long l => JsonSerializer.SerializeToElement(l, SailfishJsonContext.Default.Int64),
		string s => JsonSerializer.SerializeToElement(s, SailfishJsonContext.Default.String),
		DateTime dt => JsonSerializer.SerializeToElement(dt.ToString("O", CultureInfo.InvariantCulture), SailfishJsonContext.Default.String),
		DateTimeOffset dto => JsonSerializer.SerializeToElement(dto.ToString("O", CultureInfo.InvariantCulture), SailfishJsonContext.Default.String),
		_ => throw new NotSupportedException($"Preferences value type {typeof(T)} is not supported."),
	};

	private static T Convert<T>(JsonElement element, T defaultValue)
	{
		var target = typeof(T);
		if (target == typeof(string))
			return (T)(object)(element.ValueKind == JsonValueKind.String ? element.GetString()! : element.ToString());
		if (target == typeof(bool))
			return (T)(object)element.GetBoolean();
		if (target == typeof(int))
			return (T)(object)element.GetInt32();
		if (target == typeof(long))
			return (T)(object)element.GetInt64();
		if (target == typeof(double))
			return (T)(object)element.GetDouble();
		if (target == typeof(float))
			return (T)(object)element.GetSingle();
		if (target == typeof(DateTime))
			return (T)(object)DateTime.Parse(element.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
		if (target == typeof(DateTimeOffset))
			return (T)(object)DateTimeOffset.Parse(element.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
		return defaultValue;
	}
}

/// <summary>Fallback SecureStorage when Sailfish Secrets is unavailable: AES file store with the key next to it,
/// so obfuscation only.</summary>
internal sealed class SailfishFileSecureStore : ISecureStorage
{
	private readonly string _path = SailfishAppPaths.ConfigFile("secure.json");

	internal static string DataPath => SailfishAppPaths.ConfigFile("secure.json");

	internal static string KeyPath => SailfishAppPaths.ConfigFile("secure.key");

	internal static bool Exists => File.Exists(DataPath);

	/// <summary>Every stored entry, decrypted (for migration to Secrets).</summary>
	internal Dictionary<string, string> ReadAll()
	{
		var result = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var (key, blob) in Load())
		{
			try
			{
				result[key] = Unprotect(blob);
			}
			catch
			{
				// An entry the key no longer opens is unrecoverable anyway.
			}
		}
		return result;
	}

	/// <summary>Deletes the store and its key (after a migration).</summary>
	internal static void Delete()
	{
		File.Delete(DataPath);
		File.Delete(KeyPath);
	}

	private byte[] Key()
	{
		var keyPath = SailfishAppPaths.ConfigFile("secure.key");
		if (File.Exists(keyPath))
			return File.ReadAllBytes(keyPath);
		Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
		var key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
		File.WriteAllBytes(keyPath, key);
		Chmod600(keyPath);
		return key;
	}

	private static void Chmod600(string path)
	{
		try
		{
			File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
		}
		catch
		{
			// best effort on non-POSIX hosts
		}
	}

	private Dictionary<string, string> Load()
	{
		try
		{
			if (!File.Exists(_path))
				return new();
			var blob = JsonSerializer.Deserialize(File.ReadAllText(_path), SailfishJsonContext.Default.DictionaryStringString);
			return blob ?? new();
		}
		catch
		{
			return new();
		}
	}

	private void Save(Dictionary<string, string> map)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
		File.WriteAllText(_path, JsonSerializer.Serialize(map, SailfishJsonContext.Default.DictionaryStringString));
		Chmod600(_path);
	}

	private string Protect(string plain)
	{
		using var aes = System.Security.Cryptography.Aes.Create();
		aes.Key = Key();
		aes.GenerateIV();
		using var enc = aes.CreateEncryptor();
		var cipher = enc.TransformFinalBlock(Encoding.UTF8.GetBytes(plain), 0, Encoding.UTF8.GetByteCount(plain));
		return System.Convert.ToBase64String(aes.IV.Concat(cipher).ToArray());
	}

	private string Unprotect(string blob)
	{
		var all = System.Convert.FromBase64String(blob);
		using var aes = System.Security.Cryptography.Aes.Create();
		aes.Key = Key();
		aes.IV = all.Take(16).ToArray();
		using var dec = aes.CreateDecryptor();
		var plain = dec.TransformFinalBlock(all, 16, all.Length - 16);
		return Encoding.UTF8.GetString(plain);
	}

	public Task<string?> GetAsync(string key) =>
		Task.FromResult(Load().TryGetValue(key, out var blob) ? Unprotect(blob) : null);

	public Task SetAsync(string key, string data)
	{
		var map = Load();
		map[key] = Protect(data);
		Save(map);
		return Task.CompletedTask;
	}

	public bool Remove(string key)
	{
		var map = Load();
		var removed = map.Remove(key);
		if (removed)
			Save(map);
		return removed;
	}

	public void RemoveAll()
	{
		Save(new());
	}
}

/// <summary>App-package file access (files published next to the binary).</summary>
internal sealed class SailfishFileSystem : IFileSystem
{
	public string CacheDirectory => SailfishAppPaths.CacheDirectory;

	public string AppDataDirectory => SailfishAppPaths.DataDirectory;

	public Task<bool> AppPackageFileExistsAsync(string filename) =>
		Task.FromResult(File.Exists(Path.Combine(AppContext.BaseDirectory, filename)));

	public Task<Stream> OpenAppPackageFileAsync(string filename)
	{
		var path = Path.Combine(AppContext.BaseDirectory, filename);
		return Task.FromResult<Stream>(File.OpenRead(path));
	}
}

/// <summary>Browser/Launcher over QDesktopServices (shim sailfish_host_open_url).</summary>
internal sealed class SailfishBrowser : IBrowser
{
	public Task<bool> OpenAsync(Uri uri) => OpenUrl(uri.ToString());

	public Task<bool> OpenAsync(string uri) => OpenAsync(new Uri(uri));

	public Task<bool> OpenAsync(Uri uri, BrowserLaunchMode launchMode) => OpenUrl(uri.ToString());

	public Task<bool> OpenAsync(string uri, BrowserLaunchMode launchMode) => OpenAsync(new Uri(uri), launchMode);

	// Launch options (title mode/flags/native colors) have no Silica equivalent.
	public Task<bool> OpenAsync(Uri uri, BrowserLaunchOptions options) => OpenUrl(uri.ToString());

	internal static Task<bool> OpenUrl(string url) => QtThread.RunAsync(() =>
	{
		var utf8 = SailfishClipboard.MarshalUtf8(url);
		try
		{
			return QtHostNative.sailfish_host_open_url(utf8) == 0;
		}
		finally
		{
			Marshal.FreeCoTaskMem(utf8);
		}
	});
}

internal sealed class SailfishLauncher : ILauncher
{
	public Task<bool> CanOpenAsync(string uri) => Task.FromResult(uri.Length > 0);

	public Task<bool> CanOpenAsync(Uri uri) => Task.FromResult(uri is not null);

	public Task<bool> OpenAsync(string uri) => SailfishBrowser.OpenUrl(uri);

	public Task<bool> OpenAsync(Uri uri) => SailfishBrowser.OpenUrl(uri.ToString());

	public Task<bool> TryOpenAsync(string uri) => CanOpenAsync(uri);

	public Task<bool> TryOpenAsync(Uri uri) => CanOpenAsync(uri);

	/// <summary>Opens the file in the app registered for its type (QDesktopServices → the system's default
	/// handler, as xdg-open picks it). False when there is no such file. Under Sailjail the other app sees only the
	/// locations its own sandbox allows (Documents, Downloads, Pictures, …), not this app's private data.</summary>
	public Task<bool> OpenAsync(OpenFileRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		var path = request.File?.FullPath;
		if (string.IsNullOrEmpty(path) || !File.Exists(path))
			return Task.FromResult(false);
		return SailfishBrowser.OpenUrl(new Uri(Path.GetFullPath(path)).AbsoluteUri);
	}
}

/// <summary>App identity from the baked app meta + entry assembly.</summary>
internal sealed class SailfishAppInfo : IAppInfo
{
	private static readonly Lazy<(string Title, string Version)> Meta = new(() =>
	{
		var title = SailfishAppMeta.Current.Title;
		var asm = System.Reflection.Assembly.GetEntryAssembly()?.GetName();
		return (string.IsNullOrEmpty(title) ? asm?.Name ?? string.Empty : title,
			asm?.Version?.ToString() ?? "0.0.0");
	});

	public string PackageName => System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? string.Empty;

	public string Name => Meta.Value.Title;

	public string VersionString => Meta.Value.Version;

	public Version Version => Version.Parse(Meta.Value.Version);

	public string BuildString => string.Empty;

	/// <summary>The Silica ambience: LightOnDark is Dark, DarkOnLight is Light.</summary>
	public AppTheme RequestedTheme => SailfishTheme.Current;

	public AppPackagingModel PackagingModel => AppPackagingModel.Unpackaged;

	public LayoutDirection RequestedLayoutDirection => LayoutDirection.LeftToRight;

	public void ShowSettingsUI() =>
		throw new NotSupportedException("Sailfish has no in-app settings surface.");
}

internal static class SailfishAppPaths
{
	public static string CacheDirectory => Directory.CreateDirectory(Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
		".cache", AppSegment)).FullName;

	public static string DataDirectory => Directory.CreateDirectory(Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
		".local", "share", AppSegment)).FullName;

	/// <summary>Cache subdirectory for a feature (images, glyphs, fonts…).</summary>
	public static string Cache(string kind) =>
		Directory.CreateDirectory(Path.Combine(CacheDirectory, kind)).FullName;

	/// <summary>A Sailjail-sandboxed app may only write under ORG/APP; an unsandboxed one uses the assembly name.</summary>
	internal static string AppSegment => Meta.Value.Segment;

	/// <summary>The RPM package name, which names the .desktop file and the /usr/bin launcher.</summary>
	internal static string PackageName => Meta.Value.Package;

	private static readonly Lazy<(string Segment, string Package)> Meta = new(() =>
	{
		var assembly = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "maui-sailfish";
		var package = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? assembly);
		var meta = SailfishAppMeta.Current;
		if (meta.Application is not { } application)
			return (assembly, package);
		return meta.Sandboxed && meta.Organization is { } organization
			? (Path.Combine(organization, application), application)
			: (assembly, application);
	});

	public static string ConfigFile(string name) =>
		Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
			".config", AppSegment, name);
}
