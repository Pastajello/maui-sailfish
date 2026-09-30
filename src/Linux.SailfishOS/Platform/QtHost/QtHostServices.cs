using System.Text.Json;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// App-level platform services: each is one inline-QML object created once on the application window.
/// Events ("svc-&lt;service&gt;-&lt;event&gt;") go through the window's queue so they survive navigation;
/// Qt thread only.
/// </summary>
internal static class QtHostServices
{
	private static readonly Dictionary<string, List<Action<JsonElement>>> Subscribers = new(StringComparer.Ordinal);
	private static readonly HashSet<string> Created = new(StringComparer.Ordinal);

	/// <summary>Creates the service object once (idempotent). False when the
	/// Qt host is not running or the QML failed to load (logged).</summary>
	public static bool Ensure(string name, string qml)
	{
		if (Created.Contains(name))
			return true;
		if (!QtHostRuntime.IsRunning)
			return false;
		var result = QtHostRuntime.Eval($"(function(){{var w=window;return w.mauiService({Js(name)},{Js(qml)});}})()");
		if (result != "ok")
		{
			QtHostDiag.Warn(QtHostDiagChannel.QtHost, $"platform service '{name}' unavailable: {result}");
			return false;
		}
		Created.Add(name);
		return true;
	}

	/// <summary>Evaluates <paramref name="expression"/> with <c>s</c> bound to
	/// the service object; "" when the service is missing.</summary>
	public static string Eval(string name, string expression) =>
		!QtHostRuntime.IsRunning ? string.Empty
			: QtHostRuntime.Eval($"(function(){{var s=window.mauiServices[{Js(name)}];if(!s)return '';return String({expression});}})()");

	public static double EvalNumber(string name, string expression, double fallback = double.NaN) =>
		double.TryParse(Eval(name, expression), System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;

	/// <summary>Subscribes to "svc-&lt;name&gt;" events (payload = parsed JSON).</summary>
	public static void Subscribe(string eventName, Action<JsonElement> handler)
	{
		if (!Subscribers.TryGetValue(eventName, out var list))
			Subscribers[eventName] = list = new List<Action<JsonElement>>();
		list.Add(handler);
	}

	/// <summary>True when <paramref name="name"/> is a service event (routed here).</summary>
	public static bool IsServiceEvent(string name) => name.StartsWith("svc-", StringComparison.Ordinal);

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
