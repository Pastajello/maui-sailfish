namespace Microsoft.Maui.SailfishOS.Handlers;

/// <summary>
/// The names handlers share with the QML adapters: adapter kinds (adapters.json), the transient keys pushed outside
/// a snapshot, command names and their arguments (<c>mauiCommand</c>), and the events adapters raise
/// (<c>mauiEvent</c>). One place, so a renamed QML property or command fails <c>AdapterKeyContractTests</c> instead of
/// silently doing nothing on the device. Snapshot builders (<c>AdapterSnapshots</c>) keep their keys next to the
/// value they encode.
/// </summary>
internal static class SailfishKeys
{
	/// <summary>Adapter kinds, the keys of adapters.json.</summary>
	public static class Adapter
	{
		public const string Label = "label";
		public const string Button = "button";
		public const string Entry = "entry";
		public const string Editor = "editor";
		public const string SearchBar = "search-bar";
		public const string Switch = "switch";
		public const string CheckBox = "check-box";
		public const string Slider = "slider";
		public const string ProgressBar = "progress-bar";
		public const string ActivityIndicator = "activity-indicator";
		public const string Stepper = "stepper";
		public const string RadioButton = "radio-button";
		public const string IndicatorView = "indicator-view";
		public const string Picker = "picker";
		public const string DatePicker = "date-picker";
		public const string TimePicker = "time-picker";
		public const string Image = "image";
		public const string Border = "border";
		public const string WebView = "web-view";
		public const string SwipeView = "swipe-view";
		public const string ContentView = "content-view";
		public const string Grid = "grid";
		public const string StackLayout = "stack-layout";
		public const string ScrollView = "scroll-view";
		public const string Shape = "shape";
		public const string GraphicsView = "graphics-view";
		public const string DrawnView = "drawn-view";
	}

	/// <summary>Text-input state pushed on its own, outside the snapshot, so a re-applied snapshot never resets native
	/// focus or caret.</summary>
	public static class Transient
	{
		public const string Focus = "mauiFocus";
		public const string Cursor = "mauiCursor";
		public const string SelectionLength = "mauiSelLen";
	}

	/// <summary>One-shot commands (an adapter's <c>mauiCommand(json)</c>) and their arguments.</summary>
	public static class Command
	{
		/// <summary>WebView history and reload: <see cref="NavAction"/> is <see cref="Back"/>, <see cref="Forward"/>
		/// or <see cref="Reload"/>.</summary>
		public const string Nav = "nav";
		public const string NavAction = "action";
		public const string Back = "back";
		public const string Forward = "forward";
		public const string Reload = "reload";

		/// <summary>WebView script: <see cref="JsRequest"/> pairs the result event with the caller.</summary>
		public const string Js = "js";
		public const string JsRequest = "req";
		public const string JsScript = "script";

		/// <summary>SwipeView: <see cref="OpenSide"/> is "left", "right" or empty (close).</summary>
		public const string Open = "open";
		public const string OpenSide = "side";
	}

	/// <summary>Events adapters raise (<c>mauiEvent(name, payload)</c>) that a handler consumes.</summary>
	public static class Event
	{
		/// <summary>An image's own size became known: <see cref="Source"/>, <see cref="Width"/>, <see cref="Height"/>.</summary>
		public const string ImageNatural = "image-natural";

		/// <summary>An image failed to load (after the adapter's retries): <see cref="Source"/>.</summary>
		public const string ImageFailed = "image-failed";

		public const string Source = "source";
		public const string Width = "width";
		public const string Height = "height";
	}

	/// <summary>TextInput.echoMode values the text adapters take.</summary>
	public static class EchoMode
	{
		public const int Normal = 0;
		public const int Password = 2;
	}
}
