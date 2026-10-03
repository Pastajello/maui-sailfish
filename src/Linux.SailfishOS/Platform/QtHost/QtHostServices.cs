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

	/// <summary>Tests: captures the created/unavailable services and the subscribers; the action puts them back.</summary>
	internal static Action CaptureForTests()
	{
		var created = Created.ToArray();
		var unavailable = Unavailable.ToArray();
		var subscribers = Subscribers.ToDictionary(kv => kv.Key, kv => kv.Value.ToList(), StringComparer.Ordinal);
		return () =>
		{
			Created.Clear();
			Created.UnionWith(created);
			Unavailable.Clear();
			Unavailable.UnionWith(unavailable);
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

	/// <summary>True when <paramref name="name"/> is a service event (routed here).</summary>
	public static bool IsServiceEvent(string name) => name.StartsWith(ShellEvents.Prefix, StringComparison.Ordinal);

	public static void Dispatch(string name, string payload)
	{
		ServiceEvents++;
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

	/// <summary>Diagnostics: service events delivered.</summary>
	public static int ServiceEvents { get; private set; }

	internal static string Js(string value) => JsonSerializer.Serialize(value, SailfishJsonContext.Default.String);
}
