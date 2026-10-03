using System.Text.Json;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// What MSBuild bakes into qml/maui-appmeta.json (Microsoft.Maui.SailfishOS.targets, _SailfishWriteAppMeta): window
/// orientation, cover, title, the Harbour identity and the D-Bus openUrl names. Read once; a build without the file
/// (a plain net11.0 head) gets <see cref="Empty"/>.
/// </summary>
internal sealed record SailfishAppMeta(
	string? Orientation,
	bool Cover,
	string Title,
	string CoverQml,
	string? Application,
	string? Organization,
	bool Sandboxed,
	string? DbusName,
	string? DbusPath,
	string? DbusIface)
{
	public static readonly SailfishAppMeta Empty = new(null, false, string.Empty, string.Empty, null, null, false, null, null, null);

	/// <summary>The baked file, next to the app (qml/maui-appmeta.json).</summary>
	public static string FilePath => Path.Combine(AppContext.BaseDirectory, "qml", "maui-appmeta.json");

	private static readonly Lazy<SailfishAppMeta> LazyCurrent = new(Load);

	/// <summary>The app's meta, read on first use.</summary>
	public static SailfishAppMeta Current => LazyCurrent.Value;

	public static SailfishAppMeta Parse(string json)
	{
		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;
		string? Text(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
		bool Flag(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
		return new SailfishAppMeta(Text("orientation"), Flag("cover"), Text("title") ?? string.Empty, Text("coverQml") ?? string.Empty,
			NullIfEmpty(Text("application")), NullIfEmpty(Text("organization")), Flag("sandboxed"),
			NullIfEmpty(Text("dbusName")), Text("dbusPath"), Text("dbusIface"));
	}

	/// <summary>Silica's allowed-orientation mask: Portrait 1, Landscape 2, anything else (Any) 15.</summary>
	public static int OrientationMask(string? orientation) => orientation switch
	{
		"Portrait" => 1,
		"Landscape" => 2,
		_ => 15,   // Any, and an app meta from before the default changed
	};

	private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

	private static SailfishAppMeta Load()
	{
		try
		{
			return File.Exists(FilePath) ? Parse(File.ReadAllText(FilePath)) : Empty;
		}
		catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
		{
			Console.Error.WriteLine($"[Sailfish] app meta: {FilePath} unreadable ({ex.Message}) — defaults");
			return Empty;
		}
	}
}
