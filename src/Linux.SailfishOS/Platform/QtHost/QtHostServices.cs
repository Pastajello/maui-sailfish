using System.Text.Json;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// App-level platform services: each is one inline-QML object created once on the application window.
/// Events ("svc-&lt;service&gt;-&lt;event&gt;") go through the window's queue so they survive navigation.
/// Callable from any thread (HOP): Ensure, Eval and Subscribe run on the Qt thread through <see cref="QtThread"/>,
/// so a service built on them needs no thread checks of its own.
/// </summary>
internal static class QtHostServices
{
	private static readonly Dictionary<string, List<Action<JsonElement>>> Subscribers = new(StringComparer.Ordinal);
	private static readonly HashSet<string> Created = new(StringComparer.Ordinal);
	private static readonly HashSet<string> Wired = new(StringComparer.Ordinal);   // SubscribeOnce keys

	/// <summary>Creates the service object once (idempotent). False when the
	/// Qt host is not running or the QML failed to load (logged).</summary>
	public static bool Ensure(string name, string qml) => QtThread.Run(() =>
	{
		if (Created.Contains(name))
			return true;
		if (!QtHostRuntime.IsRunning)
			return false;
		var result = QtHostRuntime.Eval($"(function(){{var w=window;return w.mauiService({Js(name)},{Js(qml)});}})()");
		if (result != "ok")
		{
			if (Unavailable.Add(name))   // once per service: readers retry Ensure on every property read
				QtHostDiag.Warn(QtHostDiagChannel.QtHost, $"platform service '{name}' unavailable: {result}");
			return false;
		}
		Created.Add(name);
		return true;
	});

	private static readonly HashSet<string> Unavailable = new(StringComparer.Ordinal);

	/// <summary>
	/// The start of every service: creates it once and, the first time it exists, subscribes
	/// <paramref name="subscriptions"/> and runs <paramref name="started"/> (an initial read), so a service needs no
	/// flag of its own. False when the host is not running or the QML failed to load; a later call tries again.
	/// </summary>
	public static bool Ensure(string name, string qml, Action? started,
		params (string Event, Action<JsonElement> Handler)[] subscriptions) => QtThread.Run(() =>
	{
		if (!Ensure(name, qml))
			return false;
		if (SubscribeOnce(name, subscriptions))
			started?.Invoke();
		return true;
	});

	/// <summary><see cref="Ensure(string, string, Action?, ValueTuple{string, Action{JsonElement}}[])"/> without an
	/// initial read.</summary>
	public static bool Ensure(string name, string qml, params (string Event, Action<JsonElement> Handler)[] subscriptions) =>
		Ensure(name, qml, null, subscriptions);

	/// <summary>Subscribes <paramref name="subscriptions"/> the first time <paramref name="key"/> is seen; true then.
	/// For events of the shell itself (the cover), which no <see cref="Ensure(string, string)"/> creates.</summary>
	public static bool SubscribeOnce(string key, params (string Event, Action<JsonElement> Handler)[] subscriptions) =>
		QtThread.Run(() =>
		{
			if (!Wired.Add(key))
				return false;
			foreach (var (name, handler) in subscriptions)
				Subscribe(name, handler);
			return true;
		});

	/// <summary>The service's <c>s.snapshot()</c> (a JSON string), parsed; null when the service is missing or the
	/// snapshot is empty.</summary>
	public static JsonElement? Snapshot(string name)
	{
		var json = Eval(name, "s.snapshot()");
		if (json.Length == 0)
			return null;
		try
		{
			using var doc = JsonDocument.Parse(json);
			return doc.RootElement.Clone();
		}
		catch (JsonException ex)
		{
			QtHostDiag.Warn(QtHostDiagChannel.QtHost, $"platform service '{name}': malformed snapshot ({ex.Message})");
			return null;
		}
	}

	/// <summary>Tests: captures the created/unavailable services and the subscribers; the action puts them back.</summary>
	internal static Action CaptureForTests()
	{
		var created = Created.ToArray();
		var unavailable = Unavailable.ToArray();
		var wired = Wired.ToArray();
		var subscribers = Subscribers.ToDictionary(kv => kv.Key, kv => kv.Value.ToList(), StringComparer.Ordinal);
		return () =>
		{
			Created.Clear();
			Created.UnionWith(created);
			Unavailable.Clear();
			Unavailable.UnionWith(unavailable);
			Wired.Clear();
			Wired.UnionWith(wired);
			Subscribers.Clear();
			foreach (var (name, list) in subscribers)
				Subscribers[name] = list;
		};
	}

	/// <summary>True when the host runs and <paramref name="name"/> failed to load (as opposed to: not tried yet).</summary>
	public static bool IsUnavailable(string name) => QtThread.Run(() => Unavailable.Contains(name));

	/// <summary>Evaluates <paramref name="expression"/> with <c>s</c> bound to
	/// the service object; "" when the service is missing.</summary>
	public static string Eval(string name, string expression) =>
		!QtHostRuntime.IsRunning ? string.Empty
			: QtThread.Run(() => QtHostRuntime.Eval($"(function(){{var s=window.mauiServices[{Js(name)}];if(!s)return '';return String({expression});}})()"));

	public static double EvalNumber(string name, string expression, double fallback = double.NaN) =>
		double.TryParse(Eval(name, expression), System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;

	/// <summary>Subscribes to "svc-&lt;name&gt;" events (payload = parsed JSON).</summary>
	public static void Subscribe(string eventName, Action<JsonElement> handler) => QtThread.Run(() =>
	{
		if (!Subscribers.TryGetValue(eventName, out var list))
			Subscribers[eventName] = list = new List<Action<JsonElement>>();
		list.Add(handler);
	});

	/// <summary>Subscribers of <paramref name="eventName"/> (tests).</summary>
	internal static int SubscriberCount(string eventName) =>
		QtThread.Run(() => Subscribers.TryGetValue(eventName, out var list) ? list.Count : 0);

	/// <summary>True when <paramref name="name"/> is a service event (routed here).</summary>
	public static bool IsServiceEvent(string name) => name.StartsWith(ShellEvents.Prefix, StringComparison.Ordinal);

	public static void Dispatch(string name, string payload)
	{
		if (!Subscribers.TryGetValue(name, out var list))
			return;
		JsonElement root;
		try
		{
			using var doc = JsonDocument.Parse(string.IsNullOrEmpty(payload) ? "{}" : payload);
			root = doc.RootElement.Clone();
		}
		catch (JsonException ex)
		{
			QtHostDiag.Warn(QtHostDiagChannel.QmlSignal, $"service event '{name}' with malformed payload: {ex.Message}");
			return;
		}
		foreach (var handler in list.ToArray())
		{
			try
			{
				handler(root);
			}
			catch (Exception ex)
			{
				QtHostDiag.Error(QtHostDiagChannel.QmlSignal, $"service event '{name}' handler failed: {ex}");
			}
		}
	}

	internal static string Js(string value) => BridgeValue.Quote(value);
}
