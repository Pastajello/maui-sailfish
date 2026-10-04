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

	/// <summary>True while the cover is visible (the app is in the background).</summary>
	public static bool IsActive { get; private set; }

	/// <summary>Raised on the main thread when <see cref="IsActive"/> changes.</summary>
	public static event EventHandler? ActiveChanged;

	/// <summary>Every cover status report, raised after <see cref="IsActive"/> was updated: the application's
	/// OnCoverStatusChanged rides this (W1.9).</summary>
	internal static event Action<SailfishCoverStatus>? StatusChanged;

	/// <summary>A cover action was tapped (its index), raised after its handler ran: the application's
	/// OnCoverActionTriggered rides this.</summary>
	internal static event Action<int>? ActionTriggered;

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
		QtHostServices.SubscribeOnce("cover",
			(ShellEvents.CoverAction, OnAction),
			(ShellEvents.CoverStatus, OnStatus));
		if (_used)
			PushNow();
	}

	private static void OnAction(JsonElement e)
	{
		var index = CoverActionPayload.Parse(e).Index;
		SailfishCoverAction? action;
		lock (Sync)
			action = index >= 0 && index < _actions.Length ? _actions[index] : null;
		action?.Triggered();
		ActionTriggered?.Invoke(index);
	}

	private static void OnStatus(JsonElement e)
	{
		var status = CoverStatusPayload.Parse(e).Status;
		var active = status == SailfishCoverStatus.Active;
		if (active != IsActive)
		{
			IsActive = active;
			ActiveChanged?.Invoke(null, EventArgs.Empty);
		}
		StatusChanged?.Invoke(status);
	}

	/// <summary>Tests: captures the content, actions and status; the action puts them back.</summary>
	internal static Action CaptureForTests()
	{
		lock (Sync)
		{
			var (title, lines, actions, used, active) = (_title, _lines, _actions, _used, IsActive);
			return () =>
			{
				lock (Sync)
					(_title, _lines, _actions, _used) = (title, lines, actions, used);
				IsActive = active;
			};
		}
	}

	/// <summary>The current actions (AppActions reads them back).</summary>
	internal static SailfishCoverAction[] Actions
	{
		get
		{
			lock (Sync)
				return _actions;
		}
	}

	private static void Push()
	{
		if (!SailfishEssentials.IsHostUp)
			return;   // OnHostReady replays it
		QtThread.Post(PushNow);
	}

	private static void PushNow()
	{
		string json;
		lock (Sync)
		{
			json = BridgeJson.Write(w =>
			{
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
			});
		}
		var rc = QtHostRuntime.Eval($"(function(){{var w=window;if(!w||!w.mauiSetCover)return 'no shell';w.mauiSetCover({json});return 'ok';}})()");
		if (rc != "ok")
			QtHostDiag.Warn(QtHostDiagChannel.QtHost, $"cover update failed: {rc}");
	}
}
