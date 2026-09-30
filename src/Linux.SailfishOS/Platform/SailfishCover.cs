using System.Text.Json;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>A cover action: a Silica cover icon (e.g. <c>image://theme/icon-cover-next</c>) and its tap handler (main thread).</summary>
public sealed record SailfishCoverAction(string Icon, Action Triggered);

/// <summary>
/// The app's home-screen cover: a title, up to three lines and two actions. A <c>SailfishCoverQml</c> item replaces the
/// content and receives <c>{title, lines}</c> in <c>mauiCoverData</c>. Any call enables the cover; safe from any thread.
/// </summary>
public static class SailfishCover
{
	private static readonly object Sync = new();
	private static string? _title;
	private static string[] _lines = Array.Empty<string>();
	private static SailfishCoverAction[] _actions = Array.Empty<SailfishCoverAction>();
	private static bool _used;
	private static bool _subscribed;

	/// <summary>True while the cover is visible (the app is in the background).</summary>
	public static bool IsActive { get; private set; }

	/// <summary>Raised on the main thread when <see cref="IsActive"/> changes.</summary>
	public static event EventHandler? ActiveChanged;

	/// <summary>Title (null = the application title) and up to three lines.</summary>
	public static void SetContent(string? title, params string[] lines)
	{
		lock (Sync)
		{
			_title = title;
			_lines = lines.Take(3).ToArray();
			_used = true;
		}
		Push();
	}

	/// <summary>Up to two cover actions (extra ones are ignored).</summary>
	public static void SetActions(params SailfishCoverAction[] actions)
	{
		lock (Sync)
		{
			_actions = actions.Take(2).ToArray();
			_used = true;
		}
		Push();
	}

	/// <summary>Host start (Qt thread): replays state set earlier.</summary>
	internal static void OnHostReady()
	{
		if (!_subscribed)
		{
			_subscribed = true;
			QtHostServices.Subscribe("svc-cover-action", e =>
			{
				SailfishCoverAction? action;
				lock (Sync)
				{
					var i = e.TryGetProperty("index", out var idx) ? idx.GetInt32() : -1;
					action = i >= 0 && i < _actions.Length ? _actions[i] : null;
				}
				Triggered++;
				action?.Triggered();
			});
			QtHostServices.Subscribe("svc-cover-status", e =>
			{
				var active = e.TryGetProperty("active", out var a) && a.ValueKind == JsonValueKind.True;
				if (active == IsActive)
					return;
				IsActive = active;
				ActiveChanged?.Invoke(null, EventArgs.Empty);
			});
		}
		if (_used)
			PushNow();
	}

	/// <summary>Diagnostics: cover actions delivered.</summary>
	internal static int Triggered { get; private set; }

	private static void Push()
	{
		if (!QtHostRuntime.IsRunning || !SailfishEssentials.HostReady.Task.IsCompleted)
			return;   // OnHostReady replays it
		QtHostRuntime.RunOnQtThread(PushNow);
	}

	private static void PushNow()
	{
		string json;
		lock (Sync)
		{
			using var stream = new MemoryStream();
			using (var w = new Utf8JsonWriter(stream))
			{
				w.WriteStartObject();
				if (_title is null)
					w.WriteNull("title");
				else
					w.WriteString("title", _title);
				w.WriteStartArray("lines");
				foreach (var line in _lines)
					w.WriteStringValue(line);
				w.WriteEndArray();
				w.WriteStartArray("actions");
				foreach (var action in _actions)
					w.WriteStringValue(action.Icon);
				w.WriteEndArray();
				w.WriteEndObject();
			}
			json = System.Text.Encoding.UTF8.GetString(stream.ToArray());
		}
		var rc = QtHostRuntime.Eval($"(function(){{var w=window;if(!w||!w.mauiSetCover)return 'no shell';w.mauiSetCover({json});return 'ok';}})()");
		if (rc != "ok")
			QtHostDiag.Warn(QtHostDiagChannel.QtHost, $"cover update failed: {rc}");
	}
}
