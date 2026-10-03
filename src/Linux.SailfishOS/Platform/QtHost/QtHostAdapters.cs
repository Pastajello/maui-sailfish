using System.Text.Json;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// Maps adapter URIs to QML files under qml/, loaded from qml/adapters.json, so C# never names a
/// Silica/QML type and retargeting an adapter needs no C# change.
/// </summary>
public static class QtHostAdapters
{
	/// <summary>The SilicaFlickable host of a ScrollView outside collection rows. The reconcile chooses it (in a row the
	/// ListView delegate scrolls and the ScrollView is a plain container), so the handler follows the bound host.</summary>
	public const string ScrollView = "scroll-view";

	/// <summary>Built-in copy of qml/adapters.json (fallback when the file is missing/corrupt).</summary>
	private static readonly Dictionary<string, string> Fallback = new(StringComparer.Ordinal)
	{
		["label"] = "controls/Label.qml",
		["button"] = "controls/Button.qml",
		["image"] = "controls/Image.qml",
		["entry"] = "controls/Entry.qml",
		["editor"] = "controls/Editor.qml",
		["switch"] = "controls/Switch.qml",
		["slider"] = "controls/Slider.qml",
		["progress-bar"] = "controls/ProgressBar.qml",
		["activity-indicator"] = "controls/ActivityIndicator.qml",
		["search-bar"] = "controls/SearchBar.qml",
		["picker"] = "controls/Picker.qml",
		["date-picker"] = "controls/DatePicker.qml",
		["time-picker"] = "controls/TimePicker.qml",
		["radio-button"] = "controls/RadioButton.qml",
		["indicator-view"] = "controls/IndicatorView.qml",
		["stepper"] = "controls/Stepper.qml",
		["check-box"] = "controls/CheckBox.qml",
		["swipe-view"] = "controls/SwipeView.qml",
		["web-view"] = "controls/MauiWebView.qml",
		["shape"] = "shapes/Shape.qml",
		["graphics-view"] = "shapes/GraphicsView.qml",
		["content-view"] = "containers/ContentView.qml",
		["drawn-view"] = "containers/DrawnView.qml",
		["border"] = "containers/Border.qml",
		["grid"] = "containers/Grid.qml",
		["stack-layout"] = "containers/StackLayout.qml",
		[ScrollView] = "containers/ScrollView.qml",
		["list-view"] = "containers/ListView.qml",
		["carousel-view"] = "containers/CarouselView.qml",
		["alert-dialog"] = "dialogs/AlertDialog.qml",
		["prompt-dialog"] = "dialogs/PromptDialog.qml",
		["action-sheet"] = "dialogs/ActionSheet.qml",
		["context-menu"] = "interactions/ContextMenu.qml",
		["pull-down-menu"] = "interactions/PullDownMenu.qml",
		["push-up-menu"] = "interactions/PushUpMenu.qml",
		["docked-panel"] = "interactions/DockedPanel.qml",
		["drawer"] = "interactions/Drawer.qml",
	};

	private static Dictionary<string, string>? _map;

	/// <summary>Tests: captures the adapter map and returns the action that puts it back.</summary>
	internal static Action CaptureForTests()
	{
		var saved = _map is null ? null : new Dictionary<string, string>(_map, _map.Comparer);
		return () => _map = saved;
	}

	/// <summary>The active uri → src map (adapters.json when readable, else the fallback).</summary>
	public static IReadOnlyDictionary<string, string> Map => _map ??= Load();

	/// <summary>
	/// Registers a library's adapter: <paramref name="src"/> is relative to the app's <c>qml/</c> directory (where
	/// the library ships its QML as content) or an absolute <c>file:///</c> URL. A library control's handler returns
	/// <paramref name="uri"/> as its AdapterUri. Call it while the app is built, before the first page renders.
	/// </summary>
	public static void Register(string uri, string src)
	{
		ArgumentException.ThrowIfNullOrEmpty(uri);
		ArgumentException.ThrowIfNullOrEmpty(src);
		_map ??= Load();
		_map[uri] = src;
	}

	/// <summary>Resolves the QML file of an adapter URI (false = unknown URI).</summary>
	public static bool TryGetSrc(string uri, out string src) => Map.TryGetValue(uri, out src!);

	private static Dictionary<string, string> Load()
	{
		try
		{
			var path = Path.Combine(AppContext.BaseDirectory, "qml", "adapters.json");
			if (File.Exists(path))
			{
				using var doc = JsonDocument.Parse(File.ReadAllText(path));
				var map = new Dictionary<string, string>(StringComparer.Ordinal);
				if (doc.RootElement.TryGetProperty("adapters", out var adapters))
					foreach (var entry in adapters.EnumerateObject())
						if (entry.Value.GetString() is { Length: > 0 } src)
							map[entry.Name] = src;
				if (map.Count > 0)
				{
					Console.Error.WriteLine($"[Sailfish] Qt adapters: loaded {map.Count} URI mappings from {path}");
					return map;
				}
				Console.Error.WriteLine($"[Sailfish] Qt adapters: {path} has no usable entries — using built-in map");
			}
			else
			{
				Console.Error.WriteLine($"[Sailfish] Qt adapters: {path} not found — using built-in map");
			}
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[Sailfish] Qt adapters: failed to read adapters.json ({ex.Message}) — using built-in map");
		}
		return new Dictionary<string, string>(Fallback, StringComparer.Ordinal);
	}
}
