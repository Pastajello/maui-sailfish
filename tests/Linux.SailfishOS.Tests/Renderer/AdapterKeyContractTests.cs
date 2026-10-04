using System.Text.RegularExpressions;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>
/// The C#↔QML property contract, checked from both ends. The schema is the adapters themselves: every `maui*` key the
/// renderer pushes must be a property the adapter's QML declares (or one of the keys the shim applies natively),
/// because the shim rejects an undeclared key and Qt drops the whole batch with it. This is how CarouselView lost its
/// layout pushes (mauiVBar, mauiPlaceholderText) without any test noticing.
/// </summary>
[Collection("renderer")]
public class AdapterKeyContractTests
{
	private static readonly string QmlRoot = System.IO.Path.Combine(Repo.Root, "src/Linux.SailfishOS/Platform/QtHost/qml");

	/// <summary>uri → QML source, from adapters.json.</summary>
	private static Dictionary<string, string> AdapterSources()
	{
		using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(QmlRoot, "adapters.json")));
		return doc.RootElement.GetProperty("adapters").EnumerateObject()
			.ToDictionary(e => e.Name, e => System.IO.Path.Combine(QmlRoot, e.Value.GetString()!));
	}

	/// <summary>The `maui*` properties a QML file declares, with those of a local base adapter (Grid.qml is a
	/// ContentView { … }).</summary>
	private static HashSet<string> DeclaredKeys(string src)
	{
		var text = Regex.Replace(File.ReadAllText(src), @"//[^\n]*", "");
		var keys = Regex.Matches(text, @"\bproperty\s+[\w.<>]+\s+(maui\w+)").Select(m => m.Groups[1].Value)
			.ToHashSet(StringComparer.Ordinal);
		var root = Regex.Match(text, @"^([A-Z]\w*)\s*\{", RegexOptions.Multiline);
		var local = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(src)!, root.Groups[1].Value + ".qml");
		if (root.Success && File.Exists(local) && local != src)
			keys.UnionWith(DeclaredKeys(local));
		return keys;
	}

	/// <summary>Keys the shim applies itself when the adapter does not declare them (sailfish_host.cpp
	/// apply_generic_prop / apply_item_matrix / the letter-spacing pass).</summary>
	private static HashSet<string> ShimOwnedKeys()
	{
		var cpp = NativeContractTests.HostSources();
		var generic = Regex.Match(cpp, @"const bool generic = (?<list>[^;]+);");
		Assert.True(generic.Success, "host_handles.cpp: the generic-key list moved; update this test");
		var keys = Regex.Matches(generic.Groups["list"].Value, @"""(maui\w+)""").Select(m => m.Groups[1].Value)
			.ToHashSet(StringComparer.Ordinal);
		keys.Add("mauiMatrix");          // apply_item_matrix when the adapter has no such property
		keys.Add("mauiLetterSpacing");   // set on the item's QFont by the shim
		return keys;
	}

	// The three copies of the shim-owned key list must agree: what C# sends as generic view state, what the shim
	// applies natively, and what MauiModelPage strips from createObject's init (an undeclared init key fails create).
	[Fact]
	public void The_shim_owned_key_lists_agree()
	{
		var cpp = ShimOwnedKeys();
		cpp.Remove("mauiMatrix");
		cpp.Remove("mauiLetterSpacing");
		var page = File.ReadAllText(System.IO.Path.Combine(QmlRoot, "MauiModelPage.qml"));
		var create = Regex.Match(page, @"function __createHost\(.*?init\.objectName", RegexOptions.Singleline);
		var qml = Regex.Matches(create.Value, @"k !== ""(maui\w+)""").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
		var visualState = File.ReadAllText(System.IO.Path.Combine(Repo.Root, "src/Linux.SailfishOS/Platform/QtHost/QtHostVisualState.cs"));
		var cs = Regex.Matches(visualState, @"props\[""(maui\w+)""\]").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
		Assert.Equal(cpp.Order(), qml.Order());
		Assert.Equal(cpp.Order(), cs.Order());
	}

	// W5.3: the names handlers share with QML live in SailfishKeys; each must still exist on the QML side.
	[Fact]
	public void Every_handler_key_constant_is_known_to_the_adapters()
	{
		var sources = AdapterSources();
		var adapterConsts = typeof(Microsoft.Maui.SailfishOS.Handlers.SailfishKeys.Adapter)
			.GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToList();
		Assert.NotEmpty(adapterConsts);
		Assert.All(adapterConsts, uri => Assert.True(sources.ContainsKey(uri), $"adapters.json has no '{uri}'"));

		var transient = typeof(Microsoft.Maui.SailfishOS.Handlers.SailfishKeys.Transient)
			.GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToList();
		foreach (var uri in new[] { "entry", "editor", "search-bar" })
		{
			var declared = DeclaredKeys(sources[uri]);
			Assert.All(transient, key => Assert.True(declared.Contains(key), $"{uri} does not declare {key}"));
		}

		// Commands and events: the adapter's QML names them.
		string Qml(string uri) => File.ReadAllText(sources[uri]);
		var web = Qml("web-view");
		foreach (var name in new[] { "nav", "js", "back", "forward", "reload" })
			Assert.Contains($"\"{name}\"", web);
		Assert.Contains("c.action", web);
		Assert.Contains("c.req", web);
		Assert.Contains("c.script", web);
		var swipe = Qml("swipe-view");
		Assert.Contains("\"open\"", swipe);
		Assert.Contains("c.side", swipe);
		var image = Qml("image");
		Assert.Contains("\"image-natural\"", image);
		Assert.Contains("\"image-failed\"", image);
		Assert.Equal(
			new[] { "nav", "action", "back", "forward", "reload", "js", "req", "script", "open", "side" }.Order(),
			typeof(Microsoft.Maui.SailfishOS.Handlers.SailfishKeys.Command).GetFields()
				.Select(f => (string)f.GetRawConstantValue()!).Order());
	}

	// A page with one of every control kind, set away from its defaults so the conditional keys are pushed too.
	private static ContentPage EveryControl()
	{
		var items = Enumerable.Range(0, 6).Select(i => $"item {i}").ToList();
		DataTemplate Row() => new(() =>
		{
			var l = new Label();
			l.SetBinding(Label.TextProperty, ".");
			return new Border { Padding = 4, Content = l };
		});
		var stack = new VerticalStackLayout
		{
			Spacing = 3,
			Children =
			{
				new Label { Text = "Label", TextColor = Colors.Red, FontSize = 20, CharacterSpacing = 2, MaxLines = 2, LineBreakMode = LineBreakMode.TailTruncation },
				new Button { Text = "Button", TextColor = Colors.White, BackgroundColor = Colors.DarkBlue, CornerRadius = 6, BorderColor = Colors.Gray, BorderWidth = 1 },
				new Entry { Text = "entry", Placeholder = "p", IsPassword = true, MaxLength = 8, TextColor = Colors.Blue, BackgroundColor = Colors.LightGray },
				new Editor { Text = "editor", Placeholder = "p", AutoSize = EditorAutoSizeOption.TextChanges },
				new SearchBar { Text = "s", Placeholder = "search" },
				new Switch { IsToggled = true, OnColor = Colors.Green, ThumbColor = Colors.White },
				new CheckBox { IsChecked = true, Color = Colors.Orange },
				new Slider { Minimum = 1, Maximum = 9, Value = 3, MinimumTrackColor = Colors.Red },
				new Stepper { Minimum = 0, Maximum = 10, Value = 2, Increment = 2 },
				new ProgressBar { Progress = 0.4, ProgressColor = Colors.Purple },
				new ActivityIndicator { IsRunning = true, Color = Colors.Teal },
				new Picker { ItemsSource = items, SelectedIndex = 1, Title = "pick" },
				new DatePicker { Date = new DateTime(2026, 10, 3) },
				new TimePicker { Time = new TimeSpan(10, 30, 0) },
				new RadioButton { Content = "radio", IsChecked = true, GroupName = "g" },
				new Image { Source = "file:///tmp/x.png", Aspect = Aspect.AspectFill, HeightRequest = 40 },
				new ImageButton { Source = "file:///tmp/y.png", HeightRequest = 40 },
				new BoxView { Color = Colors.Coral, HeightRequest = 10, CornerRadius = 3 },
				new Ellipse { Fill = Colors.Pink, Stroke = Colors.Black, StrokeThickness = 2, HeightRequest = 20 },
				new Border { StrokeShape = new RoundRectangle { CornerRadius = 8 }, Stroke = Colors.Brown, StrokeThickness = 2, Background = Colors.Beige, Content = new Label { Text = "in border" } },
#pragma warning disable CS0618 // Frame is obsolete but still rendered
				new Frame { BorderColor = Colors.Gray, CornerRadius = 5, HasShadow = true, Content = new Label { Text = "in frame" } },
#pragma warning restore CS0618
				new Grid { ColumnDefinitions = new ColumnDefinitionCollection(new ColumnDefinition(), new ColumnDefinition()), ColumnSpacing = 4, Children = { new Label { Text = "g" } } },
				new HorizontalStackLayout { Spacing = 2, Children = { new Label { Text = "h" } } },
				new ScrollView { HeightRequest = 60, Content = new Label { Text = "scroll" } },
				new CollectionView { ItemsSource = items, ItemTemplate = Row(), HeightRequest = 120, Header = "header", Footer = "footer" },
				new CollectionView { ItemsSource = items, ItemTemplate = Row(), HeightRequest = 120, ItemsLayout = new GridItemsLayout(2, ItemsLayoutOrientation.Vertical) },
				new CollectionView { ItemsSource = new List<string>(), ItemTemplate = Row(), HeightRequest = 60, EmptyView = "nothing" },
				new CarouselView { ItemsSource = items, ItemTemplate = Row(), HeightRequest = 80, Loop = true },
				new CarouselView { ItemsSource = items, ItemTemplate = Row(), HeightRequest = 80, Loop = false },
				new IndicatorView { Count = 3, Position = 1, IndicatorColor = Colors.Gray, SelectedIndicatorColor = Colors.Black },
				new SwipeView { RightItems = new SwipeItems { new SwipeItem { Text = "del", BackgroundColor = Colors.Red } }, Content = new Label { Text = "swipe" } },
				new WebView { Source = new HtmlWebViewSource { Html = "<b>x</b>" }, HeightRequest = 40 },
				new GraphicsView { HeightRequest = 30 },
				new RefreshView { Content = new ScrollView { Content = new Label { Text = "refresh" } } },
				new ContentView { Content = new Label { Text = "content view" }, Opacity = 0.8, Rotation = 10, ScaleX = 1.5 },
			},
		};
		return new ContentPage { Content = new ScrollView { Content = stack } };
	}

	[Fact]
	public void Every_pushed_maui_key_is_declared_by_its_adapter_or_owned_by_the_shim()
	{
		using var h = new RendererHarness(EveryControl());
		for (var i = 0; i < 4; i++)
			h.Poll();
		var sources = AdapterSources();
		var shim = ShimOwnedKeys();
		var declared = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
		var offenders = new SortedSet<string>(StringComparer.Ordinal);
		var uris = new HashSet<string>(StringComparer.Ordinal);
		foreach (var obj in h.Shim.Objects.Where(o => sources.ContainsKey(o.Uri)))
		{
			uris.Add(obj.Uri);
			if (!declared.TryGetValue(obj.Uri, out var keys))
				declared[obj.Uri] = keys = DeclaredKeys(sources[obj.Uri]);
			foreach (var key in obj.Props.Keys.Where(k => k.StartsWith("maui", StringComparison.Ordinal)))
				if (!keys.Contains(key) && !shim.Contains(key))
					offenders.Add($"{obj.Uri}.{key}");
		}
		// The page must have reached the adapters it is meant to cover, or the check proves nothing. ("image" is
		// missing on purpose: an Image whose source has not resolved yet keeps a content-view host, plan step B5.)
		foreach (var uri in new[] { "label", "button", "entry", "editor", "switch", "slider", "picker", "list-view", "carousel-view", "border", "scroll-view" })
			Assert.Contains(uri, uris);
		Assert.True(offenders.Count == 0, "pushed but not declared by the adapter: " + string.Join(", ", offenders));
	}

	// BridgeValue.Serialize falls back to ToString() for a type it does not know: a Brush, a Thickness or a
	// FormattedString would reach QML as its CLR type name and paint nothing, with no error anywhere.
	[Fact]
	public void No_pushed_value_is_a_clr_type_name()
	{
		using var h = new RendererHarness(EveryControl());
		for (var i = 0; i < 4; i++)
			h.Poll();
		var leaks = new SortedSet<string>(StringComparer.Ordinal);
		foreach (var obj in h.Shim.Objects)
			foreach (var (key, value) in obj.Props)
				if (value.ValueKind == System.Text.Json.JsonValueKind.String &&
				    System.Text.RegularExpressions.Regex.IsMatch(value.GetString() ?? string.Empty, @"^(Microsoft|System)\.[A-Z]\w*(\.\w+)+$"))
					leaks.Add($"{obj.Uri}.{key}={value.GetString()}");
		foreach (var op in h.Shim.Ops)
			foreach (var text in System.Text.RegularExpressions.Regex.Matches(op.GetRawText(), @"""(Microsoft|System)\.[A-Z][\w.]+""").Select(m => m.Value))
				leaks.Add($"op: {text}");
		Assert.True(leaks.Count == 0, "CLR type names pushed: " + string.Join(", ", leaks));
	}
}
