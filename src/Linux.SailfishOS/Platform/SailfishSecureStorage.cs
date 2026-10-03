using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Microsoft.Maui.Storage;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// <see cref="ISecureStorage"/> on the Sailfish Secrets daemon (opt in with <c>SailfishSecureStorage=Secrets</c>).
/// Without the daemon it falls back to <see cref="SailfishFileSecureStore"/> and migrates those entries once Secrets appears.
/// Daemon calls run on the Qt thread (QtDBus); <c>MAUI_SAILFISH_SECURESTORAGE=file</c> forces the file store.
/// </summary>
internal sealed class SailfishSecureStorage : ISecureStorage
{
	private static readonly TimeSpan HostWait = TimeSpan.FromSeconds(15);

	private readonly SemaphoreSlim _gate = new(1, 1);
	private SailfishFileSecureStore? _file;
	private bool _useSecrets;
	private bool _decided;

	/// <summary>"secrets" or "file" once decided (diagnostics).</summary>
	public string Backend => !_decided ? "undecided" : _useSecrets ? "secrets" : "file";

	/// <summary>Why the file store is in use (null on Secrets).</summary>
	public string? FallbackReason { get; private set; }

	public async Task<string?> GetAsync(string key)
	{
		ArgumentException.ThrowIfNullOrEmpty(key);
		if (!await UseSecretsAsync().ConfigureAwait(false))
			return await _file!.GetAsync(key).ConfigureAwait(false);
		return await OnQt(() => SecretsNative.Get(key)).ConfigureAwait(false);
	}

	public async Task SetAsync(string key, string value)
	{
		ArgumentException.ThrowIfNullOrEmpty(key);
		ArgumentNullException.ThrowIfNull(value);
		if (!await UseSecretsAsync().ConfigureAwait(false))
		{
			await _file!.SetAsync(key, value).ConfigureAwait(false);
			return;
		}
		await OnQt(() =>
		{
			SecretsNative.Set(key, value);
			return true;
		}).ConfigureAwait(false);
	}

	public bool Remove(string key)
	{
		ArgumentException.ThrowIfNullOrEmpty(key);
		// Remove is synchronous: inline on the Qt thread, otherwise it waits for the hop.
		if (!UseSecretsAsync().GetAwaiter().GetResult())
			return _file!.Remove(key);
		return OnQt(() => SecretsNative.Delete(key)).GetAwaiter().GetResult();
	}

	public void RemoveAll()
	{
		if (!UseSecretsAsync().GetAwaiter().GetResult())
		{
			_file!.RemoveAll();
			return;
		}
		OnQt(() =>
		{
			SecretsNative.Clear();
			return true;
		}).GetAwaiter().GetResult();
	}

	private async Task<bool> UseSecretsAsync()
	{
		if (_decided)
			return _useSecrets;
		await _gate.WaitAsync().ConfigureAwait(false);
		try
		{
			if (_decided)
				return _useSecrets;
			string? reason;
			if (string.Equals(SailfishEnv.Get("MAUI_SAILFISH_SECURESTORAGE"), "file", StringComparison.OrdinalIgnoreCase))
				reason = "MAUI_SAILFISH_SECURESTORAGE=file";
			else if (!SecretsNative.TryLoad(out reason))
			{
				// TryLoad set the reason.
			}
			else
			{
				reason = await OnQt(() => SecretsNative.Open(CollectionName)).ConfigureAwait(false);
				if (reason is null)
					await OnQt(MigrateFileStore).ConfigureAwait(false);
			}
			_useSecrets = reason is null;
			FallbackReason = reason;
			if (!_useSecrets)
			{
				_file = new SailfishFileSecureStore();
				Console.Error.WriteLine($"[Sailfish] SecureStorage: Sailfish Secrets unavailable ({reason}) — using the file store " +
				                        "(obfuscation only; add <SailfishSecureStorage>Secrets</SailfishSecureStorage> to the app project)");
			}
			else
			{
				QtHostDiag.Trace(QtHostDiagChannel.QtHost, $"SecureStorage: Sailfish Secrets collection '{CollectionName}'");
			}
			_decided = true;
			return _useSecrets;
		}
		finally
		{
			_gate.Release();
		}
	}

	/// <summary>The app's Secrets collection name: global, alphanumeric and under 32 chars, so an app-name suffix plus
	/// 8 hex of its SHA-256 keeps "a-b" and "ab" apart.</summary>
	internal static string CollectionName
	{
		get
		{
			var segment = SailfishAppPaths.AppSegment;
			var alnum = new StringBuilder(segment.Length);
			foreach (var c in segment)
				if (char.IsAsciiLetterOrDigit(c))
					alnum.Append(c);
			var tail = alnum.Length > 19 ? alnum.ToString(alnum.Length - 19, 19) : alnum.ToString();
			var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(segment));
			return "maui" + tail + Convert.ToHexStringLower(hash, 0, 4);
		}
	}

	private static bool MigrateFileStore()
	{
		if (!SailfishFileSecureStore.Exists)
			return true;
		var entries = new SailfishFileSecureStore().ReadAll();
		foreach (var (key, value) in entries)
			SecretsNative.Set(key, value);
		SailfishFileSecureStore.Delete();
		Console.Error.WriteLine($"[Sailfish] SecureStorage: moved {entries.Count} entr{(entries.Count == 1 ? "y" : "ies")} from the file store into Sailfish Secrets");
		return true;
	}

	/// <summary>Runs <paramref name="work"/> on the Qt thread (QtThread), waiting for the host if it is not up yet. The Qt
	/// loop itself cannot wait for its own first tick: a synchronous call there before the host is ready fails at once
	/// instead of stalling the loop for <see cref="HostWait"/>.</summary>
	private static async Task<T> OnQt<T>(Func<T> work)
	{
		if (!QtHostRuntime.IsRunning || !SailfishEssentials.HostReady.Task.IsCompleted)
		{
			if (QtHostRuntime.IsRunning && QtHostRuntime.IsQtThread)
				throw new InvalidOperationException(
					"SecureStorage needs the running Qt host (Sailfish Secrets is reached over QtDBus) — " +
					"call it after the first page shows, not while the app starts on the main thread.");
			try
			{
				await SailfishEssentials.HostReady.Task.WaitAsync(HostWait).ConfigureAwait(false);
			}
			catch (TimeoutException)
			{
				throw new InvalidOperationException(
					"SecureStorage needs the running Qt host (Sailfish Secrets is reached over QtDBus) — " +
					"do not block the main thread on SecureStorage before the first page shows.");
			}
		}
		return await QtThread.RunAsync(work).ConfigureAwait(false);
	}
}

/// <summary>P/Invoke to libsailfishsecretsbridge.so, loaded on demand because it is optional.</summary>
internal static unsafe class SecretsNative
{
	private const string Library = "libsailfishsecretsbridge.so";

	private static delegate* unmanaged<byte*, int> _open;
	private static delegate* unmanaged<byte*, byte*, int, int> _set;
	private static delegate* unmanaged<byte*, byte**, int*, int> _get;
	private static delegate* unmanaged<byte*, int> _delete;
	private static delegate* unmanaged<int> _clear;
	private static delegate* unmanaged<byte*, void> _free;
	private static delegate* unmanaged<byte*> _lastError;
	private static bool _loaded;

	internal static bool TryLoad(out string? reason)
	{
		reason = null;
		if (_loaded)
			return true;
		if (!OperatingSystem.IsLinux())
		{
			reason = "not Linux";
			return false;
		}
		var path = Path.Combine(AppContext.BaseDirectory, Library);
		if (!File.Exists(path))
		{
			reason = $"{Library} not deployed";
			return false;
		}
		if (!File.Exists("/usr/lib64/libsailfishsecrets.so.0") && !File.Exists("/usr/lib/libsailfishsecrets.so.0"))
		{
			reason = "Sailfish Secrets not installed (no libsailfishsecrets.so.0)";
			return false;
		}
		try
		{
			var lib = NativeLibrary.Load(path);
			_open = (delegate* unmanaged<byte*, int>)NativeLibrary.GetExport(lib, "sfsec_open");
			_set = (delegate* unmanaged<byte*, byte*, int, int>)NativeLibrary.GetExport(lib, "sfsec_set");
			_get = (delegate* unmanaged<byte*, byte**, int*, int>)NativeLibrary.GetExport(lib, "sfsec_get");
			_delete = (delegate* unmanaged<byte*, int>)NativeLibrary.GetExport(lib, "sfsec_delete");
			_clear = (delegate* unmanaged<int>)NativeLibrary.GetExport(lib, "sfsec_clear");
			_free = (delegate* unmanaged<byte*, void>)NativeLibrary.GetExport(lib, "sfsec_free");
			_lastError = (delegate* unmanaged<byte*>)NativeLibrary.GetExport(lib, "sfsec_last_error");
			_loaded = true;
			return true;
		}
		catch (Exception ex)
		{
			reason = $"{Library}: {ex.Message.Split('\n')[0].Trim()}";
			return false;
		}
	}

	/// <summary>Null on success, else the reason the daemon is unusable.</summary>
	internal static string? Open(string collection)
	{
		fixed (byte* c = Utf8(collection))
		{
			var rc = _open(c);
			return rc == 0 ? null : $"sfsec_open rc={rc}: {LastError()}";
		}
	}

	internal static void Set(string key, string value)
	{
		var data = Encoding.UTF8.GetBytes(value);
		fixed (byte* k = Utf8(key))
		fixed (byte* d = data)
		{
			var rc = _set(k, d, data.Length);
			if (rc != 0)
				throw new InvalidOperationException($"Sailfish Secrets store failed (rc={rc}): {LastError()}");
		}
	}

	internal static string? Get(string key)
	{
		byte* data = null;
		var len = 0;
		fixed (byte* k = Utf8(key))
		{
			var rc = _get(k, &data, &len);
			try
			{
				if (rc == 1)
					return null;
				if (rc != 0)
					throw new InvalidOperationException($"Sailfish Secrets read failed (rc={rc}): {LastError()}");
				return Encoding.UTF8.GetString(data, len);
			}
			finally
			{
				if (data is not null)
					_free(data);
			}
		}
	}

	internal static bool Delete(string key)
	{
		fixed (byte* k = Utf8(key))
		{
			var rc = _delete(k);
			if (rc > 1 || rc < 0)
				throw new InvalidOperationException($"Sailfish Secrets delete failed (rc={rc}): {LastError()}");
			return rc == 0;
		}
	}

	internal static void Clear()
	{
		var rc = _clear();
		if (rc != 0)
			throw new InvalidOperationException($"Sailfish Secrets clear failed (rc={rc}): {LastError()}");
	}

	private static string LastError() => Marshal.PtrToStringUTF8((IntPtr)_lastError()) ?? string.Empty;

	private static byte[] Utf8(string s)
	{
		var bytes = new byte[Encoding.UTF8.GetByteCount(s) + 1];
		Encoding.UTF8.GetBytes(s, 0, s.Length, bytes, 0);
		return bytes;
	}
}
