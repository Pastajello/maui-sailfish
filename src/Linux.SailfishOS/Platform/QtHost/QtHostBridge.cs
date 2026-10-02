using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// Serializes managed values to the JSON the shim converts against the target QML property type.
/// Colors become "#AARRGGBB" and element hosts become <c>{"$handle":N}</c>.
/// </summary>
public static class BridgeValue
{
	/// <summary>Serializes a managed value to its bridge JSON form (raw JSON text).</summary>
	public static string Serialize(object? value) => value switch
	{
		null => "null",
		bool b => b ? "true" : "false",
		string s => Quote(s),
		char c => Quote(c.ToString()),
		byte or sbyte or short or ushort or int or uint or long or ulong
			=> Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
		float f => float.IsFinite(f) ? f.ToString("R", CultureInfo.InvariantCulture) : "0",
		double d => Number(d),
		decimal m => m.ToString(CultureInfo.InvariantCulture),
		Enum e => Quote(e.ToString()),
		Color color => $"\"{ColorString(color)}\"",
		Rect r => Object(("x", r.X), ("y", r.Y), ("width", r.Width), ("height", r.Height)),
		Point p => Object(("x", p.X), ("y", p.Y)),
		Size s => Object(("width", s.Width), ("height", s.Height)),
		NativeElementHost host => ObjectRaw(("$handle", host.NativeHandle.ToString(CultureInfo.InvariantCulture))),
		// Must precede IEnumerable. Match the concrete type: a pattern on IDictionary throws
		// TypeLoadException because its forwarder is missing from the published System.Runtime.
		Dictionary<string, object?> map => Map(map),
		// Arrive in QML `property var` as a QVariantList.
		System.Collections.IEnumerable items => Array(items),
		_ => Quote(value.ToString() ?? string.Empty),
	};

	/// <summary>Round-trip invariant number text, the form both JSON and JS parse. JSON has no NaN/Infinity: a
	/// non-finite value becomes 0 (as the text-metrics, shape and recorder serializers already do) instead of a token
	/// that makes QJsonDocument reject the whole batch.</summary>
	public static string Number(double value) =>
		double.IsFinite(value) ? value.ToString("R", CultureInfo.InvariantCulture) : "0";

	/// <summary>
	/// Minimal JSON string escaping; reflection-based System.Text.Json is trimmed out of the published app.
	/// U+2028/U+2029 are escaped too, so the result is also a valid JS literal for sailfish_host_eval.
	/// </summary>
	public static string Quote(string value)
	{
		var sb = new StringBuilder(value.Length + 2);
		sb.Append('"');
		foreach (var c in value)
		{
			switch (c)
			{
				case '\\': sb.Append("\\\\"); break;
				case '"': sb.Append("\\\""); break;
				case '\n': sb.Append("\\n"); break;
				case '\r': sb.Append("\\r"); break;
				case '\u2028': sb.Append("\\u2028"); break;
				case '\u2029': sb.Append("\\u2029"); break;
				default:
					if (c < 0x20)
						sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
					else
						sb.Append(c);
					break;
			}
		}
		return sb.Append('"').ToString();
	}

	private static string Map(Dictionary<string, object?> map)
	{
		var sb = new StringBuilder(64);
		sb.Append('{');
		var first = true;
		foreach (var entry in map)
		{
			if (!first)
				sb.Append(',');
			first = false;
			sb.Append(Quote(entry.Key)).Append(':').Append(Serialize(entry.Value));
		}
		return sb.Append('}').ToString();
	}

	private static string Array(System.Collections.IEnumerable items)
	{
		var sb = new StringBuilder(64);
		sb.Append('[');
		var first = true;
		foreach (var item in items)
		{
			if (!first)
				sb.Append(',');
			first = false;
			sb.Append(Serialize(item));
		}
		return sb.Append(']').ToString();
	}

	private static int Channel(float v) => Math.Clamp((int)Math.Round(v * 255f), 0, 255);

	/// <summary>Unquoted "#AARRGGBB" form of a color, as QColor parses it.</summary>
	public static string ColorString(Color color) =>
		$"#{Channel(color.Alpha):X2}{Channel(color.Red):X2}{Channel(color.Green):X2}{Channel(color.Blue):X2}";

	private static string Object(params (string Name, double Value)[] fields)
	{
		var sb = new StringBuilder(64);
		sb.Append('{');
		for (var i = 0; i < fields.Length; i++)
		{
			if (i > 0)
				sb.Append(',');
			sb.Append('"').Append(fields[i].Name).Append("\":")
			  .Append(Number(fields[i].Value));
		}
		return sb.Append('}').ToString();
	}

	private static string ObjectRaw(params (string Name, string RawJson)[] fields)
	{
		var sb = new StringBuilder(64);
		sb.Append('{');
		for (var i = 0; i < fields.Length; i++)
		{
			if (i > 0)
				sb.Append(',');
			sb.Append('"').Append(fields[i].Name).Append("\":").Append(fields[i].RawJson);
		}
		return sb.Append('}').ToString();
	}
}

/// <summary>Structural ops for MauiModelPage.applyMauiOps; the field names are contract with the QML side.</summary>
internal static class BridgeOps
{
	public static Dictionary<string, object?> Destroy(string id) => new() { ["op"] = "destroy", ["id"] = id };

	public static Dictionary<string, object?> Reparent(string id, string parentId) =>
		new() { ["op"] = "reparent", ["id"] = id, ["parent"] = parentId };

	/// <summary>Re-attaches <paramref name="ids"/> under <paramref name="parentId"/> in this order ("" = page canvas).</summary>
	public static Dictionary<string, object?> Order(string parentId, List<string> ids) =>
		new() { ["op"] = "order", ["parent"] = parentId, ["ids"] = ids };

	public static Dictionary<string, object?> Title(string text) => new() { ["op"] = "title", ["text"] = text };

	/// <summary>Page.IsBusy: the pulley pulses when the page has a pull-down menu, else a PageBusyIndicator runs.</summary>
	public static Dictionary<string, object?> Busy(bool on, bool onPulley) => new() { ["op"] = "busy", ["on"] = on, ["pulley"] = onPulley };

	/// <summary>The page's Silica palette: the app's own theme (UserAppTheme) over the ambience's.</summary>
	public static Dictionary<string, object?> Scheme(bool light) => new() { ["op"] = "scheme", ["light"] = light };

	/// <summary>Silica Page.backNavigation: the back gesture and indicator.</summary>
	public static Dictionary<string, object?> Back(bool enabled) => new() { ["op"] = "back", ["on"] = enabled };

	/// <summary>Silica Page.allowedOrientations (SailfishPage.AllowedOrientations); 0 = the app's.</summary>
	public static Dictionary<string, object?> Orientations(int mask) => new() { ["op"] = "orientations", ["mask"] = mask };

	public static Dictionary<string, object?> Background(string color, string image = "") =>
		new() { ["op"] = "background", ["color"] = color, ["image"] = image };
}

/// <summary>Reads numbers out of adapter event payloads, with a fallback for missing or non-numeric fields.</summary>
internal static class BridgeJson
{
	public static double Num(JsonElement element, string name, double fallback = 0) =>
		NumberProp(element, name, out var prop) && prop.TryGetDouble(out var value) ? value : fallback;

	public static int Int(JsonElement element, string name, int fallback = -1) =>
		NumberProp(element, name, out var prop) && prop.TryGetInt32(out var value) ? value : fallback;

	// TryGetDouble/TryGetInt32 throw on a non-number, so check the kind first.
	private static bool NumberProp(JsonElement element, string name, out JsonElement prop)
	{
		prop = default;
		return element.ValueKind == JsonValueKind.Object
			&& element.TryGetProperty(name, out prop)
			&& prop.ValueKind == JsonValueKind.Number;
	}
}

/// <summary>
/// Builds one ordered property batch per host per pass, applied in a single native call.
/// The optional <c>mauiApplying</c> true/false envelope stops adapters echoing managed changes back as events.
/// </summary>
public static class QtHostBridge
{
	/// <summary>Name of the adapter's change-suppression flag.</summary>
	public const string SuppressFlag = "mauiApplying";

	/// <summary>Builds the ordered batch JSON: [{"name":…,"value":…}, …].</summary>
	/// <param name="props">Pre-serialized (name, raw JSON value) pairs, in application order.</param>
	/// <param name="suppress">Wrap in the mauiApplying true … false envelope.</param>
	public static string BuildBatch(IReadOnlyList<(string Name, string ValueJson)> props, bool suppress = true)
	{
		var sb = new StringBuilder(96 + props.Count * 32);
		sb.Append('[');
		if (suppress)
			AppendEntry(sb, SuppressFlag, "true");
		foreach (var (name, valueJson) in props)
			AppendEntry(sb, name, valueJson);
		if (suppress)
			AppendEntry(sb, SuppressFlag, "false");
		return sb.Append(']').ToString();
	}

	private static void AppendEntry(StringBuilder sb, string name, string rawValueJson)
	{
		if (sb.Length > 1)
			sb.Append(',');
		sb.Append("{\"name\":").Append(BridgeValue.Quote(name)).Append(",\"value\":").Append(rawValueJson).Append('}');
	}
}
