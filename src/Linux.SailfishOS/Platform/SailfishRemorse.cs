using System.Text.Json;
using Microsoft.Maui.Controls;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Silica's remorse: the Sailfish way to confirm a destructive action. Instead of a "Are you sure?" dialog the action
/// runs after a short countdown the user can tap away ("Tap to undo"). Page-wide actions show a RemorsePopup at the
/// top of the page; an action on one element (a list row, a card) shows a RemorseItem over it. MAUI has no API for
/// this, so an app calls it from its Sailfish code path. Safe from any thread; the action runs on the main thread.
/// </summary>
public static class SailfishRemorse
{
	/// <summary>Silica's default countdown.</summary>
	public const int DefaultTimeoutMs = 4000;

	private static readonly object Sync = new();
	private static readonly Dictionary<int, (TaskCompletionSource<bool> Done, Action? Execute)> Pending = new();
	private static int _nextToken;

	/// <summary>A RemorsePopup at the top of the current page. <paramref name="text"/> null shows Silica's localized
	/// "Deleted". Completes true after <paramref name="onExecute"/> ran, false when the user tapped it away.</summary>
	public static Task<bool> ExecuteAsync(string? text, Action? onExecute = null, int timeoutMs = DefaultTimeoutMs) =>
		Start(null, text, onExecute, timeoutMs);

	/// <summary>A RemorseItem over <paramref name="item"/>: the whole row when it sits in a CollectionView/ListView
	/// row, else the element itself. Completes like <see cref="ExecuteAsync(string?, Action?, int)"/>; false at once
	/// when the element is not on screen.</summary>
	public static Task<bool> ExecuteAsync(VisualElement item, string? text, Action? onExecute = null, int timeoutMs = DefaultTimeoutMs)
	{
		ArgumentNullException.ThrowIfNull(item);
		return Start(item, text, onExecute, timeoutMs);
	}

	/// <summary>Cancels every pending countdown (their tasks complete false), e.g. before the data they act on reloads.</summary>
	public static void CancelAll()
	{
		int[] tokens;
		lock (Sync)
			tokens = Pending.Keys.ToArray();
		if (tokens.Length == 0 || !QtHostRuntime.IsRunning)
			return;
		QtHostRuntime.RunOnQtThread(() =>
		{
			foreach (var token in tokens)
				QtHostRuntime.Eval($"{QmlPage.Model}.mauiRemorseCancel({token})");
		});
	}

	private static Task<bool> Start(VisualElement? item, string? text, Action? onExecute, int timeoutMs)
	{
		if (!QtHostRuntime.IsRunning)
			return Task.FromResult(false);
		var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		int token;
		lock (Sync)
		{
			token = ++_nextToken;
			Pending[token] = (done, onExecute);
		}
		QtHostRuntime.RunOnQtThread(() =>
		{
			string? dg = null, host = null;
			if (item is not null && QtHostPageRenderer.Current is { } renderer)
			{
				dg = renderer.Collection.DelegateOf(item);
				host = dg is null ? renderer.HostIdOf(item) : null;
				if (dg is null && host is null)
				{
					Complete(token, false);
					return;
				}
			}
			using var stream = new MemoryStream();
			using (var w = new Utf8JsonWriter(stream))
			{
				w.WriteStartObject();
				w.WriteNumber("token", token);
				if (text is null)
					w.WriteNull("text");
				else
					w.WriteString("text", text);
				w.WriteNumber("timeout", Math.Max(1, timeoutMs));
				if (dg is not null)
					w.WriteString("dg", dg);
				if (host is not null)
					w.WriteString("host", host);
				w.WriteEndObject();
			}
			var json = System.Text.Encoding.UTF8.GetString(stream.ToArray());
			var rc = QtHostRuntime.Eval(QmlPage.Call(QmlPage.Model, "mauiRemorse", BridgeValue.Quote(json)));
			QtHostDiag.Trace(QtHostDiagChannel.QtHost, $"remorse {token} ({(dg ?? host ?? "page")}) → {rc}");
		});
		return done.Task;
	}

	/// <summary>"remorse-done" {token, executed} from the model page (Qt thread).</summary>
	internal static void OnDone(string payload)
	{
		using var doc = JsonDocument.Parse(payload);
		var root = doc.RootElement;
		if (!root.TryGetProperty("token", out var t) || !t.TryGetInt32(out var token))
			return;
		Complete(token, root.TryGetProperty("executed", out var e) && e.ValueKind == JsonValueKind.True);
	}

	private static void Complete(int token, bool executed)
	{
		(TaskCompletionSource<bool> Done, Action? Execute) entry;
		lock (Sync)
		{
			if (!Pending.Remove(token, out entry))
				return;
		}
		if (executed)
		{
			try
			{
				entry.Execute?.Invoke();
			}
			catch (Exception ex)
			{
				QtHostDiag.Error(QtHostDiagChannel.QtHost, $"remorse action failed: {ex.Message}");
				entry.Done.TrySetException(ex);
				return;
			}
		}
		entry.Done.TrySetResult(executed);
	}
}
