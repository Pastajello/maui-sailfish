using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.SailfishOS.Handlers;
using Microsoft.Maui.SailfishOS.Hosting;
using Microsoft.Maui.SailfishOS.Platform;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>Startup failures found running the dotnet/maui-samples apps (10.0/Apps) on the device.</summary>
[Collection("renderer")]
public class SampleAppRegressionTests
{
	private static ContentPage Page(params View[] children)
	{
		var stack = new VerticalStackLayout();
		foreach (var child in children)
			stack.Children.Add(child);
		return new ContentPage { Title = "Test", Content = stack };
	}

	private sealed class TestApp : Application
	{
	}

	// TipCalc, RpnCalculator, SolitaireEncryption: FontSize="Large" in XAML.
#pragma warning disable CS0612, CS0618
	[Theory]
	[InlineData(NamedSize.Default, 14)]
	[InlineData(NamedSize.Micro, 10)]
	[InlineData(NamedSize.Small, 14)]
	[InlineData(NamedSize.Medium, 17)]
	[InlineData(NamedSize.Large, 22)]
	[InlineData(NamedSize.Body, 16)]
	[InlineData(NamedSize.Caption, 12)]
	[InlineData(NamedSize.Subtitle, 16)]
	[InlineData(NamedSize.Title, 24)]
	[InlineData(NamedSize.Header, 96)]
	public void Named_font_sizes_follow_the_android_table(NamedSize size, double expected)
	{
		var service = new SailfishFontNamedSizeService();
		Assert.Equal(expected, service.GetNamedSize(size, typeof(Label), false));
		Assert.NotEqual(SailfishFontManager.DefaultSize, service.GetNamedSize(size, typeof(Button), false));
	}
#pragma warning restore CS0612, CS0618

	// DeveloperBalance: Syncfusion.Maui.Toolkit's plain-net SfViewHandler throws in CreatePlatformView.
	private sealed class LibraryView : View
	{
	}

	private sealed class PlainNetHandler : ViewHandler<LibraryView, object>
	{
		public PlainNetHandler() : base(new PropertyMapper<LibraryView, PlainNetHandler>())
		{
		}

		protected override object CreatePlatformView() => throw new NotImplementedException();
	}

	[Fact]
	public void A_handler_failing_to_create_its_platform_view_renders_empty_instead_of_crashing()
	{
		var builder = MauiApp.CreateBuilder(useDefaults: false);
		builder.UseMauiAppSailfish<TestApp>();
		builder.ConfigureMauiHandlers(handlers => handlers.AddHandler<LibraryView, PlainNetHandler>());
		using var app = builder.Build();
		var library = new LibraryView();
		using var h = new RendererHarness(Page(library, new Label { Text = "After" }), app.Services);

		Assert.IsType<NullViewHandler>(library.Handler);
		Assert.Equal("After", h.Shim.ByUri("label").Single().Text("text"));
	}

	// DeveloperBalance: SfTextInputLayout draws its outline and hint (IDrawable) around the Entry it lays out; with the
	// empty fallback the forms lost their labels.
	private sealed class DrawnLibraryView : ContentView, Microsoft.Maui.Graphics.IDrawable
	{
		public void Draw(Microsoft.Maui.Graphics.ICanvas canvas, Microsoft.Maui.Graphics.RectF dirtyRect)
		{
			canvas.StrokeColor = Colors.Gray;
			canvas.DrawRoundedRectangle(dirtyRect, 4);
			canvas.DrawString("Task", 8, 4, Microsoft.Maui.Graphics.HorizontalAlignment.Left);
		}
	}

	private sealed class PlainNetDrawnHandler : ViewHandler<DrawnLibraryView, object>
	{
		public PlainNetDrawnHandler() : base(new PropertyMapper<DrawnLibraryView, PlainNetDrawnHandler>())
		{
		}

		protected override object CreatePlatformView() => throw new NotImplementedException();
	}

	[Fact]
	public void A_self_drawing_library_view_renders_its_drawing_under_its_children()
	{
		var builder = MauiApp.CreateBuilder(useDefaults: false);
		builder.UseMauiAppSailfish<TestApp>();
		builder.ConfigureMauiHandlers(handlers => handlers.AddHandler<DrawnLibraryView, PlainNetDrawnHandler>());
		using var app = builder.Build();
		var library = new DrawnLibraryView { HeightRequest = 60, Content = new Entry { Text = "Survey" } };
		using var h = new RendererHarness(Page(library), app.Services);
		for (var i = 0; i < 3; i++)
			h.Poll();

		Assert.IsType<SailfishDrawnViewHandler>(library.Handler);
		var drawn = h.Shim.ByUri("drawn-view").Single();
		Assert.True(drawn.Props["mauiCommands"].GetArrayLength() > 0);
		Assert.Equal("Survey", h.Shim.ByUri("entry").Single().Text("text"));
	}

	// DeveloperBalance Manage Meta: Entries in a grid row stretched by a button showed their text at the top; MAUI's
	// Entry centres by default (an Editor starts at the top).
	[Fact]
	public void An_entry_centres_its_text_by_default_and_an_editor_does_not()
	{
		Assert.Equal("center", AdapterSnapshots.TextStyleProps(new Entry())["mauiVAlign"]);
		Assert.Equal(string.Empty, AdapterSnapshots.TextStyleProps(new Editor())["mauiVAlign"]);
		Assert.Equal("bottom", AdapterSnapshots.TextStyleProps(new Editor { VerticalTextAlignment = TextAlignment.End })["mauiVAlign"]);
	}

	// WeightTracker: UraniumUI registers StatefulButtonHandler : ButtonHandler for every Button (and Plainer an
	// EntryHandler subclass for its EntryView); their plain-net bases throw, and every button vanished.
	private sealed class LibraryButtonHandler : ButtonHandler
	{
	}

	private sealed class LibraryEntry : Entry
	{
	}

	private sealed class LibraryEntryHandler : EntryHandler
	{
	}

	[Fact]
	public void A_library_handler_built_on_a_stock_one_falls_back_to_the_sailfish_handler()
	{
		var builder = MauiApp.CreateBuilder(useDefaults: false);
		builder.UseMauiAppSailfish<TestApp>();
		builder.ConfigureMauiHandlers(handlers =>
		{
			handlers.AddHandler<Button, LibraryButtonHandler>();
			handlers.AddHandler<LibraryEntry, LibraryEntryHandler>();
		});
		using var app = builder.Build();
		var button = new Button { Text = "Next" };
		var entry = new LibraryEntry { Placeholder = "Weight" };
		using var h = new RendererHarness(Page(button, entry), app.Services);

		Assert.IsType<SailfishButtonHandler>(button.Handler);
		Assert.IsType<SailfishEntryHandler>(entry.Handler);
		Assert.Equal("Next", h.Shim.ByUri("button").Single().Text("text"));
		Assert.Equal("Weight", h.Shim.ByUri("entry").Single().Text("placeholderText"));
	}

	// Calculator, Weather: new Window(new MainPage()) showed "MainPage" in the PageHeader.
	[Fact]
	public void An_untitled_page_shows_the_app_name_in_its_header()
	{
		var appName = new SailfishAppInfo().Name;
		Assert.False(string.IsNullOrEmpty(appName));

		using (var h = new RendererHarness(new ContentPage { Content = new Label { Text = "0" } }))
		{
			var title = h.Shim.Ops.Last(op => op.GetProperty("op").GetString() == "title");
			Assert.Equal(appName, title.GetProperty("text").GetString());
		}

		using (var h = new RendererHarness(new ContentPage { Title = "Calc", Content = new Label { Text = "0" } }))
		{
			var title = h.Shim.Ops.Last(op => op.GetProperty("op").GetString() == "title");
			Assert.Equal("Calc", title.GetProperty("text").GetString());
		}
	}

	// TipCalc: a padded ContentView in an Auto row left its Label 10 of 30 dp, the text spilling below the box.
	[Fact]
	public void A_padded_content_view_measures_its_content_plus_padding()
	{
		var label = new Label { Text = "£0.00", FontSize = 22 };
		var box = new ContentView { Padding = new Thickness(10, 10, 40, 10), Content = label };
		var grid = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto) } };
		grid.Add(box);
		using var h = new RendererHarness(new ContentPage { Title = "T", Content = grid });

		Assert.True(label.DesiredSize.Height > 0);
		Assert.Equal(label.DesiredSize.Height, label.Bounds.Height, 3);
		Assert.Equal(label.DesiredSize.Height + 20, box.Bounds.Height, 3);
	}

	// RpnCalculator: an unset-size Label paints at Theme.fontSizeMedium (Label.qml) but was measured at 14 dp, so
	// "Y = " in an Auto column wrapped to "Y".
	[Fact]
	public void An_unset_size_label_is_measured_at_the_theme_size_it_paints_with()
	{
		// FontSize 0 is how an unset size reads on the device (Label.qml then paints Theme.fontSizeMedium).
		var unset = new Label { Text = "Y = ", FontSize = 0 };
		var themed = new Label { Text = "Y = ", FontSize = Microsoft.Maui.SailfishOS.Handlers.SailfishFontRules.SilicaMediumFontDp() };
		var small = new Label { Text = "Y = ", FontSize = 14 };
		using var h = new RendererHarness(Page(unset, themed, small));

		Assert.Equal(themed.DesiredSize.Width, unset.DesiredSize.Width, 3);
		Assert.True(unset.DesiredSize.Width > small.DesiredSize.Width);
	}

	// Measure and paint share one Label rule (SailfishFontRules.LabelFontSize): a never-sized Label, one at MAUI's 18
	// and one at 0 are each measured at the size their adapter paints, and none of them names a size, so Label.qml
	// paints Theme.fontSizeMedium (owner decision 2026-10-03). A sized Label keeps its size.
	[Fact]
	public void A_label_is_measured_at_the_size_it_paints()
	{
		var never = new Label { Text = "Y = 1234" };
		var eighteen = new Label { Text = "Y = 1234", FontSize = 18 };
		var zero = new Label { Text = "Y = 1234", FontSize = 0 };
		var sized = new Label { Text = "Y = 1234", FontSize = 12 };
		using var h = new RendererHarness(Page(never, eighteen, zero, sized));

		string PixelSize(Label label) => h.Shim.ByUri("label").Single(o => h.Renderer.CurrentHosts.Any(x =>
			ReferenceEquals(x.Element, label) && x.Id == o.Id)).Text("mauiPixelSize")!;
		Assert.All(new[] { never, eighteen, zero }, label => Assert.Equal(0, double.Parse(PixelSize(label), System.Globalization.CultureInfo.InvariantCulture)));
		Assert.Equal(12 * SailfishDisplay.Density, double.Parse(PixelSize(sized), System.Globalization.CultureInfo.InvariantCulture), 3);

		foreach (var label in new[] { never, eighteen, zero, sized })
		{
			var native = h.Shim.ByUri("label").Single(o => h.Renderer.CurrentHosts.Any(x =>
				ReferenceEquals(x.Element, label) && x.Id == o.Id));
			var px = double.Parse(native.Text("mauiPixelSize")!, System.Globalization.CultureInfo.InvariantCulture);
			var paintedDp = px > 0 ? px / SailfishDisplay.Density : Microsoft.Maui.SailfishOS.Handlers.SailfishFontRules.SilicaMediumFontDp();
			var (width, _) = QtHostTextMetrics.Measure("Y = 1234", null, FontAttributes.None, (int)Math.Round(paintedDp),
				0, QtHostTextMetrics.WordWrap, 1.0, 0, 0);
			Assert.Equal(width, label.DesiredSize.Width, 3);
		}
	}

	// WeatherTwentyOne Settings: theme RadioButtons with a ControlTemplate showed a Silica radio labelled
	// "Microsoft.Maui.Controls.Grid" (the content's ToString()).
	[Fact]
	public void A_templated_radio_button_renders_its_template_not_a_silica_radio()
	{
		var template = new ControlTemplate(() => new Border { Padding = 4, Content = new ContentPresenter() });
		var templated = new RadioButton { ControlTemplate = template, Content = new Label { Text = "Dark" } };
		var plain = new RadioButton { Content = "Imperial" };
		using var h = new RendererHarness(Page(templated, plain));

		var radios = h.Shim.ByUri("radio-button").ToList();
		Assert.Single(radios);
		Assert.Equal("Imperial", radios[0].Text("text"));
		Assert.Contains(h.Shim.ByUri("label"), l => l.Text("text") == "Dark");
		Assert.True(templated.Height > 0);
	}

	// WeatherTwentyOne Settings: "Sign Out" in a fixed 100 dp column faded out at Theme.fontSizeMedium.
	[Fact]
	public void An_unset_size_button_may_shrink_its_label_down_to_the_maui_default()
	{
		var density = SailfishDisplay.Density;
		var unset = Microsoft.Maui.SailfishOS.Handlers.AdapterSnapshots.ButtonProps(new Button { Text = "Sign Out" });
		var sized = Microsoft.Maui.SailfishOS.Handlers.AdapterSnapshots.ButtonProps(new Button { Text = "Sign Out", FontSize = 20 });

		Assert.Equal(SailfishMeasure.DefaultFontSize * density, (double)unset["mauiFitPixelSize"]!, 3);
		Assert.Equal(0.0, (double)sized["mauiFitPixelSize"]!);
	}

	// WhatToEat: category tiles (ContentLayout="Top,0", MinimumHeightRequest) drew the image left of the text on a
	// short Silica plate.
	private static string WritePng(int width, int height)
	{
		var path = Path.Combine(Path.GetTempPath(), $"sf-icon-{width}x{height}-{Guid.NewGuid():N}.png");
		var bytes = new byte[33];
		new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(bytes, 0);
		System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), width);
		System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), height);
		File.WriteAllBytes(path, bytes);
		return path;
	}

	[Fact]
	public void Button_content_layout_reaches_the_adapter_and_the_measure()
	{
		var icon = WritePng(96, 96);
		try
		{
			var top = new Button { Text = "Breakfast", ImageSource = icon, ContentLayout = new Button.ButtonContentLayout(Button.ButtonContentLayout.ImagePosition.Top, 4) };
			var left = new Button { Text = "Breakfast", ImageSource = icon };
			var plain = new Button { Text = "Breakfast" };
			var props = Microsoft.Maui.SailfishOS.Handlers.AdapterSnapshots.ButtonProps(top);
			Assert.Equal("top", props["mauiIconPosition"]);
			Assert.Equal(4 * SailfishDisplay.Density, (double)props["mauiIconSpacing"]!, 3);
			Assert.True((bool)props["mauiFillPlate"]!);
			Assert.False((bool)Microsoft.Maui.SailfishOS.Handlers.AdapterSnapshots.ButtonProps(left)["mauiFillPlate"]!);
			Assert.True((bool)Microsoft.Maui.SailfishOS.Handlers.AdapterSnapshots.ButtonProps(new Button { Text = "x", MinimumHeightRequest = 150 })["mauiFillPlate"]!);

			var row = new HorizontalStackLayout { Children = { plain, top, left } };
			using var h = new RendererHarness(new ContentPage { Title = "T", Content = row });
			var iconDp = 96 / SailfishDisplay.Density;
			Assert.True(top.DesiredSize.Height >= plain.DesiredSize.Height + iconDp - 0.01, $"top {top.DesiredSize} vs plain {plain.DesiredSize}");
			Assert.True(left.DesiredSize.Width >= plain.DesiredSize.Width + iconDp - 0.01, $"left {left.DesiredSize} vs plain {plain.DesiredSize}");
		}
		finally
		{
			File.Delete(icon);
		}
	}

	// MoneyFox Statistics: a CollectionView in a VerticalStackLayout (unbounded height) collapsed to 0 and showed
	// nothing; RecyclerView/UICollectionView size to their rows there.
	[Fact]
	public void An_unbounded_collection_view_sizes_to_its_rows()
	{
		var list = new CollectionView { ItemsSource = new[] { "Cash flow", "Category spreading", "Category summary" } };
		using var h = new RendererHarness(new ContentPage { Title = "T", Content = new VerticalStackLayout { Children = { list } } });
		for (var i = 0; i < 4; i++)
			h.Poll();

		var adapter = ((SailfishListViewHandler)list.Handler!).Adapter!;
		Assert.Equal(3, adapter.Rows.Count);
		Assert.True(adapter.ContentExtentDp > 0);
		Assert.Equal(adapter.ContentExtentDp, list.Height, 1);
	}

	// DeveloperBalance: project cards bind their SfShimmer's IsActive to the page model through
	// {RelativeSource AncestorType}; the row views had no parent, the binding never resolved and the shimmer hid
	// the content. Android and iOS add item views as the ItemsView's logical children.
	[Fact]
	public void Item_views_resolve_ancestor_bindings_and_leave_with_their_rows()
	{
		var list = new CollectionView
		{
			ItemsSource = new[] { "a", "b" },
			ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label();
				label.SetBinding(Label.TextProperty, new Binding("BindingContext.Name",
					source: new RelativeBindingSource(RelativeBindingSourceMode.FindAncestor, typeof(ContentPage))));
				return label;
			}),
		};
		var page = new ContentPage { Title = "T", BindingContext = new { Name = "page model" }, Content = list };
		using var h = new RendererHarness(page);
		for (var i = 0; i < 4; i++)
			h.Poll();

		var adapter = ((SailfishListViewHandler)list.Handler!).Adapter!;
		var first = Assert.IsType<Label>(adapter.Rows[0].CellViews[0]);
		Assert.Same(list, first.Parent);
		Assert.Equal("page model", first.Text);
		Assert.Equal("a", first.BindingContext);

		list.ItemsSource = new[] { "c" };   // a new source: the old rows' views leave the list
		for (var i = 0; i < 4; i++)
			h.Poll();

		Assert.Null(first.Parent);
		Assert.Single(((IVisualTreeElement)list).GetVisualChildren());
	}

	// DeveloperBalance: the horizontal project list (MinimumHeightRequest 250) sits in an Auto grid row; it took its
	// minimum and cut the cards' tags off. A wrap_content RecyclerView takes its tallest item.
	[Fact]
	public void A_horizontal_list_in_an_auto_row_takes_its_tallest_item()
	{
		var list = new CollectionView
		{
			ItemsSource = new[] { "a", "b" },
			ItemsLayout = LinearItemsLayout.Horizontal,
			MinimumHeightRequest = 250,
			ItemTemplate = new DataTemplate(() => new Border
			{
				WidthRequest = 200,
				MinimumHeightRequest = 250,
				StrokeThickness = 0,
				Content = new BoxView { HeightRequest = 300 },
			}),
		};
		var grid = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) } };
		grid.Add(list);
		using var h = new RendererHarness(new ContentPage { Title = "T", Content = grid });
		for (var i = 0; i < 4; i++)
			h.Poll();

		Assert.Equal(300, list.Height, 1);
		var card = ((SailfishListViewHandler)list.Handler!).Adapter!.Rows[0].CellViews[0]!;
		Assert.Equal(300, card.Height, 1);   // the items are laid out again for the list's new height
	}

	// MoneyFox Menu/Statistics: Cards with a TapGestureRecognizer in an unselectable CollectionView never navigated;
	// the ListView consumed the press and its delegate only reported taps for selection.
	[Fact]
	public void A_tap_recognizer_in_an_item_template_fires_on_an_unselectable_list()
	{
		var tapped = new List<object?>();
		var list = new CollectionView
		{
			ItemsSource = new[] { "Budgets", "Categories" },
			ItemTemplate = new DataTemplate(() =>
			{
				var card = new Border { HeightRequest = 50, Margin = new Thickness(15, 6, 15, 0), Content = new Label() };
				card.Content.SetBinding(Label.TextProperty, ".");
				var tap = new TapGestureRecognizer();
				tap.Tapped += (s, _) => tapped.Add(((BindableObject)s!).BindingContext);
				card.GestureRecognizers.Add(tap);
				return new Grid { Children = { card } };
			}),
		};
		using var h = new RendererHarness(Page(list));
		for (var i = 0; i < 4; i++)
			h.Poll();
		var native = h.Shim.ByUri("list-view").Single();
		Assert.Contains("\"t\":2", native.Text("mauiRowsJson"));

		var inCard = QtHostUnits.ToQtUnits(30);
		h.Renderer.HandleNativeEvent("list-item-tapped", FormattableString.Invariant($"{{\"id\":\"{native.Id}\",\"row\":1,\"cell\":0,\"x\":{inCard},\"y\":{inCard}}}"));
		Assert.Equal(new object?[] { "Categories" }, tapped);

		// The margin above the card is the row's, not the card's.
		var inMargin = QtHostUnits.ToQtUnits(3);
		h.Renderer.HandleNativeEvent("list-item-tapped", FormattableString.Invariant($"{{\"id\":\"{native.Id}\",\"row\":0,\"cell\":0,\"x\":{inCard},\"y\":{inMargin}}}"));
		Assert.Single(tapped);
		Assert.Null(list.SelectedItem);
	}

	// MoneyFox Categories: a BorderlessEntry (BackgroundColor="Transparent" in a rounded Border) kept the Silica
	// underline; on Android a set background replaces it.
	[Fact]
	public void A_text_input_with_a_set_background_drops_the_silica_underline()
	{
		var plain = new Entry { Placeholder = "Name" };
		var borderless = new Entry { Placeholder = "Search", BackgroundColor = Colors.Transparent };
		var filled = new Editor { Background = new SolidColorBrush(Colors.Gray) };
		using var h = new RendererHarness(Page(plain, borderless, filled));

		var entries = h.Shim.ByUri("entry").ToList();
		Assert.Equal("False", entries.Single(e => e.Text("placeholderText") == "Name").Text("mauiNoUnderline"));
		Assert.Equal("True", entries.Single(e => e.Text("placeholderText") == "Search").Text("mauiNoUnderline"));
		Assert.Equal("True", h.Shim.ByUri("editor").Single().Text("mauiNoUnderline"));
	}

	// Profitocracy: Padding="16" on its pages was ignored, the content touching the screen edges.
	[Fact]
	public void Page_padding_insets_the_content()
	{
		var content = new Grid();
		var unpadded = new Grid();
		using (new RendererHarness(new ContentPage { Title = "T", Content = unpadded }))
		using (new RendererHarness(new ContentPage { Title = "T", Padding = new Thickness(16, 8, 12, 4), Content = content }))
		{
			Assert.Equal(unpadded.Bounds.X + 16, content.Bounds.X, 3);
			Assert.Equal(unpadded.Bounds.Y + 8, content.Bounds.Y, 3);
			Assert.Equal(unpadded.Bounds.Width - 28, content.Bounds.Width, 3);
			Assert.Equal(unpadded.Bounds.Height - 12, content.Bounds.Height, 3);
		}
	}

	// Profitocracy: the first-run profile modal hides its back button (BackButtonBehavior IsVisible/IsEnabled false),
	// yet showed Silica's back indicator and could be swiped away.
	[Theory]
	[InlineData("visible", false)]
	[InlineData("enabled", false)]
	[InlineData("hasBackButton", false)]
	[InlineData("none", true)]
	public void A_hidden_back_button_turns_off_silica_back_navigation(string hide, bool expected)
	{
		var page = new ContentPage { Title = "Profile", Content = new Label { Text = "x" } };
		if (hide == "visible")
			Shell.SetBackButtonBehavior(page, new BackButtonBehavior { IsVisible = false });
		else if (hide == "enabled")
			Shell.SetBackButtonBehavior(page, new BackButtonBehavior { IsEnabled = false });
		else if (hide == "hasBackButton")
			NavigationPage.SetHasBackButton(page, false);
		using var h = new RendererHarness(page);

		var back = h.Shim.Ops.Last(op => op.GetProperty("op").GetString() == "back");
		Assert.Equal(expected, back.GetProperty("on").GetBoolean());
	}

	// SailfishPage.AllowedOrientations reaches the model page as Silica's allowedOrientations; a container's setting
	// covers its pages, and clearing it hands the page back to the app's (0).
	[Fact]
	public void Allowed_orientations_follow_the_page_or_its_container()
	{
		var page = new ContentPage { Title = "Player", Content = new Label { Text = "x" } };
		var nav = new NavigationPage(page);
		SailfishPage.SetAllowedOrientations(nav, SailfishOrientations.PortraitMask);
		using var h = new RendererHarness(nav);
		int Mask() => h.Shim.Ops.Last(op => op.GetProperty("op").GetString() == "orientations").GetProperty("mask").GetInt32();
		Assert.Equal(5, Mask());

		SailfishPage.SetAllowedOrientations(page, SailfishOrientations.LandscapeMask);
		h.Poll();
		Assert.Equal(10, Mask());

		SailfishPage.SetAllowedOrientations(page, SailfishOrientations.Default);
		SailfishPage.SetAllowedOrientations(nav, SailfishOrientations.Default);
		h.Poll();
		Assert.Equal(0, Mask());
	}

	// Profitocracy Profiles/Categories: delete/edit are SwipeItemViews (an icon in a coloured circle); they were
	// dropped, leaving the rows without actions.
	[Fact]
	public void A_swipe_item_view_becomes_a_swipe_action_and_invokes()
	{
		var icon = WritePng(24, 24);
		try
		{
			var deleted = 0;
			var edited = 0;
			var delete = new SwipeItemView
			{
				BackgroundColor = Colors.Transparent,
				Content = new VerticalStackLayout
				{
					Children = { new Border { BackgroundColor = Colors.Red, Content = new Image { Source = icon } } },
				},
			};
			delete.Invoked += (_, _) => deleted++;
			var edit = new SwipeItem { Text = "Edit", BackgroundColor = Colors.Blue };
			edit.Invoked += (_, _) => edited++;
			var swipe = new SwipeView { RightItems = new SwipeItems { delete, edit }, Content = new Label { Text = "Profile" } };
			using var h = new RendererHarness(Page(swipe));

			var native = h.Shim.ByUri("swipe-view").Single();
			using var items = System.Text.Json.JsonDocument.Parse(native.Text("mauiRightItems"));
			var first = items.RootElement[0];
			Assert.Equal(2, items.RootElement.GetArrayLength());
			Assert.Equal(BridgeValue.ColorString(Colors.Red), first.GetProperty("bg").GetString());
			Assert.EndsWith(Path.GetFileName(icon), first.GetProperty("icon").GetString());

			h.Renderer.HandleNativeEvent("swipe-item-invoked", $"{{\"id\":\"{native.Id}\",\"side\":\"right\",\"index\":0}}");
			h.Renderer.HandleNativeEvent("swipe-item-invoked", $"{{\"id\":\"{native.Id}\",\"side\":\"right\",\"index\":1}}");
			Assert.Equal(1, deleted);
			Assert.Equal(1, edited);
		}
		finally
		{
			File.Delete(icon);
		}
	}

	// Profitocracy, Settings → Theme → Light under a dark ambience: the light pages kept a light-on-dark header and tab
	// row, nearly invisible on the light background.
	[Fact]
	public void The_page_palette_follows_the_apps_own_theme()
	{
		var app = new TestApp();
		Application.Current = app;
		try
		{
			app.UserAppTheme = AppTheme.Light;
			using (var h = new RendererHarness(Page(new Label { Text = "x" })))
				Assert.True(h.Shim.Ops.Last(op => op.GetProperty("op").GetString() == "scheme").GetProperty("light").GetBoolean());
			app.UserAppTheme = AppTheme.Dark;
			using (var h = new RendererHarness(Page(new Label { Text = "x" })))
				Assert.False(h.Shim.Ops.Last(op => op.GetProperty("op").GetString() == "scheme").GetProperty("light").GetBoolean());
		}
		finally
		{
			Application.Current = null;
		}
	}

	// WeatherTwentyOne: a drag along the hourly forecast (a horizontal ScrollView) switched to the next tab.
	[Fact]
	public void A_drag_in_sideways_scrolling_content_is_no_tab_swipe()
	{
		var hour = new Label { Text = "11 pm" };
		_ = new ContentPage { Content = new VerticalStackLayout { Children = { new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = new HorizontalStackLayout { Children = { hour } } } } } };
		var row = new Label { Text = "Daily" };
		_ = new ContentPage { Content = new ScrollView { Content = new VerticalStackLayout { Children = { row } } } };
		var action = new Label { Text = "Delete" };
		_ = new SwipeView { Content = new Grid { Children = { action } } };

		Assert.True(Microsoft.Maui.SailfishOS.Platform.QtHost.QtHostInputRouter.ScrollsSideways(hour));
		Assert.False(Microsoft.Maui.SailfishOS.Platform.QtHost.QtHostInputRouter.ScrollsSideways(row));
		Assert.True(Microsoft.Maui.SailfishOS.Platform.QtHost.QtHostInputRouter.ScrollsSideways(action));
	}

	// WeatherTwentyOne Settings: the Metric row (a Grid with a TapGestureRecognizer) never got its tap; the page's
	// stack, whose host was created after the row's, won the hit test.
	[Fact]
	public void A_layout_never_takes_the_hit_from_its_own_child()
	{
		var metric = new Grid { HeightRequest = 60, Children = { new Label { Text = "Metric" } } };
		metric.GestureRecognizers.Add(new TapGestureRecognizer());
		var stack = new VerticalStackLayout { Children = { new Label { Text = "Units" }, metric } };
		using var h = new RendererHarness(new ContentPage { Title = "T", Content = stack });
		stack.BackgroundColor = Colors.DarkBlue;   // now it needs a host of its own, created after the row's
		for (var i = 0; i < 4; i++)
			h.Poll();

		Assert.True(h.Renderer.TryFindTapTarget(out _, out var rowBounds));
		Assert.True(h.Renderer.TryHitTest(rowBounds.Center.X, rowBounds.Center.Y, out var host));
		var hit = host!.Element as Element;
		while (hit is not null && !ReferenceEquals(hit, metric))
			hit = hit.Parent;
		Assert.True(hit is not null, $"hit {host.Element?.GetType().Name} outside the Metric row");
	}

	// The visual leg's pinch box inside a nested ScrollView: the ScrollView's host came after the box's in the host list,
	// won the equal-ZIndex tie and swallowed the touch as "QML consumes". An ancestor never beats its descendant.
	[Fact]
	public void The_hit_test_prefers_a_descendant_over_an_ancestor_hosted_after_it()
	{
		var box = new BoxView { HeightRequest = 80, WidthRequest = 80, Color = Colors.Blue };
		box.GestureRecognizers.Add(new PinchGestureRecognizer());
		var grid = new Grid { HeightRequest = 120, Children = { box } };
		using var h = new RendererHarness(Page(grid));
		for (var i = 0; i < 4; i++)
			h.Poll();
		grid.BackgroundColor = Colors.DarkBlue;   // the grid now needs a host, created after the box's
		for (var i = 0; i < 4; i++)
			h.Poll();
		var boxHost = h.Renderer.CurrentHosts.Single(x => ReferenceEquals(x.Element, box));
		var c = boxHost.MauiLogicalBounds.Center;
		Assert.True(h.Renderer.TryHitTest(c.X, c.Y, out var hit));
		Assert.Same(box, hit!.Element);
	}

	// BugSweeper: a tile flags on a tap and reveals on a double tap (NumberOfTapsRequired 1 and 2); every tap fired the
	// single recognizer, so a double tap flagged and unflagged the tile and nothing was ever revealed.
	[Fact]
	public void A_double_tap_fires_the_double_tap_recognizer_only()
	{
		var singles = 0;
		var doubles = 0;
		var tile = new BoxView { HeightRequest = 80, WidthRequest = 80, Color = Colors.Blue };
		var single = new TapGestureRecognizer { NumberOfTapsRequired = 1 };
		single.Tapped += (_, _) => singles++;
		var @double = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
		@double.Tapped += (_, _) => doubles++;
		tile.GestureRecognizers.Add(single);
		tile.GestureRecognizers.Add(@double);
		using var h = new RendererHarness(Page(tile));
		for (var i = 0; i < 4; i++)
			h.Poll();
		var loop = SailfishDispatcherProvider.BindLoopThread();
		var router = new QtHostInputRouter(h.Renderer, loop);
		Assert.True(h.Renderer.TryFindTapTarget(out _, out var bounds));
		var x = QtHostUnits.ToQtUnits(bounds.Center.X);
		var y = QtHostUnits.ToQtUnits(bounds.Center.Y);
		void Tap()
		{
			router.OnPointer(0, x, y, 0, 0);   // mouse press
			router.OnPointer(1, x, y, 0, 0);   // mouse release
		}

		Tap();
		Tap();
		Thread.Sleep(400);
		loop.DrainQueue();
		Assert.Equal((0, 1), (singles, doubles));

		Tap();
		Thread.Sleep(400);
		SailfishRuntime.TickDueTimers(DateTime.UtcNow);   // the app loop's timer pump
		loop.DrainQueue();
		Assert.Equal((1, 1), (singles, doubles));   // a lone tap fires once the double-tap window has passed
	}

	// Two fingers on a PinchGestureRecognizer owner: Started at the midpoint, Running with the change of the finger
	// distance since the last update, Completed when a finger lifts; the sequence is no tap and no pan.
	[Fact]
	public void Two_fingers_pinch_and_neither_tap_nor_pan()
	{
		var updates = new List<(GestureStatus Status, double Scale, Point Origin)>();
		var taps = 0;
		var pans = 0;
		var photo = new BoxView { HeightRequest = 200, WidthRequest = 200, Color = Colors.Blue };
		var pinch = new PinchGestureRecognizer();
		pinch.PinchUpdated += (_, e) => updates.Add((e.Status, e.Scale, e.ScaleOrigin));
		var tap = new TapGestureRecognizer();
		tap.Tapped += (_, _) => taps++;
		var pan = new PanGestureRecognizer();
		pan.PanUpdated += (_, _) => pans++;
		photo.GestureRecognizers.Add(pinch);
		photo.GestureRecognizers.Add(tap);
		photo.GestureRecognizers.Add(pan);
		using var h = new RendererHarness(Page(photo));
		for (var i = 0; i < 4; i++)
			h.Poll();
		var loop = SailfishDispatcherProvider.BindLoopThread();
		var router = new QtHostInputRouter(h.Renderer, loop);
		Assert.True(h.Renderer.TryFindTapTarget(out _, out var b));
		double Qx(double dp) => QtHostUnits.ToQtUnits(b.X + dp);
		double Qy(double dp) => QtHostUnits.ToQtUnits(b.Y + dp);

		router.OnPointer(4, Qx(80), Qy(100), 0, 1);      // first finger down
		router.OnPointer(5, Qx(80), Qy(100), 0, 2);      // second finger joins, 40 dp apart
		router.OnPointer(7, Qx(120), Qy(100), 0, 2);
		router.OnPointer(5, Qx(60), Qy(100), 0, 2);      // spread to 80 dp apart: ×2
		router.OnPointer(7, Qx(140), Qy(100), 0, 2);
		router.OnPointer(5, Qx(60), Qy(100), 0, 2);      // second finger lifts
		router.OnPointer(7, Qx(140), Qy(100), 0, 1);
		router.OnPointer(6, Qx(60), Qy(100), 0, 1);      // first finger lifts
		loop.DrainQueue();

		Assert.Equal(new[] { GestureStatus.Started, GestureStatus.Running, GestureStatus.Completed },
			updates.Select(u => u.Status).ToArray());
		Assert.Equal(2, updates[1].Scale, 3);
		Assert.Equal(0.5, updates[1].Origin.X, 2);   // the midpoint, relative to the 200 dp box
		Assert.Equal(0.5, updates[1].Origin.Y, 2);
		Assert.Equal((0, 0), (taps, pans));
	}

	// WhatToEat: category tiles (MinimumWidthRequest=150) were as narrow as their text ("Lunch" ~120 dp).
	[Fact]
	public void Minimum_and_maximum_requests_bound_the_measured_size()
	{
		var lunch = new Button { Text = "Lunch", MinimumWidthRequest = 150 };
		var capped = new Label { Text = new string('x', 200), MaximumWidthRequest = 100 };
		using var h = new RendererHarness(new ContentPage { Title = "T", Content = new HorizontalStackLayout { Children = { lunch, capped } } });

		Assert.Equal(150, lunch.DesiredSize.Width, 3);
		Assert.True(capped.DesiredSize.Width <= 100.001, $"capped {capped.DesiredSize.Width}");
	}

	// WhatToEat New Recipe: Save's VisualStateManager sets Background per state through AppThemeBinding (Normal white,
	// Disabled dark gray); with Save disabled the button showed no plate at all.
	[Fact]
	public void A_visual_state_background_reaches_a_disabled_button()
	{
		var save = new Button();
		Microsoft.Maui.Controls.Xaml.Extensions.LoadFromXaml(save, """
			<Button xmlns="http://schemas.microsoft.com/dotnet/2021/maui" Text="Save">
			    <VisualStateManager.VisualStateGroups>
			        <VisualStateGroup x:Name="CommonStates" xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml">
			            <VisualState x:Name="Normal">
			                <VisualState.Setters>
			                    <Setter Property="Background" Value="{AppThemeBinding Dark=White, Light=White}" />
			                </VisualState.Setters>
			            </VisualState>
			            <VisualState x:Name="Disabled">
			                <VisualState.Setters>
			                    <Setter Property="Background" Value="{AppThemeBinding Dark=DarkGray, Light=DarkGray}" />
			                    <Setter Property="TextColor" Value="{AppThemeBinding Dark=LightGray, Light=LightGray}" />
			                </VisualState.Setters>
			            </VisualState>
			        </VisualStateGroup>
			    </VisualStateManager.VisualStateGroups>
			</Button>
			""");
		using var h = new RendererHarness(new ContentPage { Title = "T", Content = new VerticalStackLayout { Children = { save } } });
		var host = h.Shim.ByUri("button").Single();
		Assert.Equal(Colors.White.ToArgbHex(true).ToLowerInvariant(), host.Text("mauiPlateColor")?.ToLowerInvariant());

		save.Command = new Command(() => { }, () => false);   // the view model's SaveCommand arrives with the binding
		h.Poll();

		Assert.False(save.IsEnabled);
		Assert.Equal(Colors.DarkGray.ToArgbHex(true).ToLowerInvariant(), host.Text("mauiPlateColor")?.ToLowerInvariant());
	}

	// WhatToEat New Recipe: the empty name field measured shorter than a filled one (an empty measure fell back to an
	// estimate), so the first typed character pushed the whole form down.
	[Fact]
	public void An_entry_measures_the_same_height_empty_and_filled()
	{
		var entry = new Entry { Placeholder = "Recipe Name, Required field" };
		using var h = new RendererHarness(new ContentPage { Title = "T", Content = new VerticalStackLayout { Children = { entry } } });

		var empty = SailfishMeasure.TextInput(entry, 400, double.PositiveInfinity).Height;
		entry.Text = "Test pancakes";
		var filled = SailfishMeasure.TextInput(entry, 400, double.PositiveInfinity).Height;

		Assert.Equal(empty, filled, 1);
	}

	// WhatToEat New Recipe: Ingredients and Recipe are Editors with AutoSize="TextChanges"; they stayed three lines
	// whatever was typed.
	[Fact]
	public void An_auto_sized_editor_grows_with_its_text()
	{
		var fixedSize = new Editor { Text = string.Join("\n", Enumerable.Range(1, 8).Select(i => $"Step {i}")) };
		var auto = new Editor { AutoSize = EditorAutoSizeOption.TextChanges };
		using var h = new RendererHarness(new ContentPage { Title = "T", Content = new VerticalStackLayout { Children = { fixedSize, auto } } });

		var empty = SailfishMeasure.Editor(auto, 400, double.PositiveInfinity).Height;
		auto.Text = string.Join(" ", Enumerable.Repeat("Mix the flour, eggs and milk.", 12));
		var long_ = SailfishMeasure.Editor(auto, 400, double.PositiveInfinity).Height;

		Assert.True(long_ > empty, $"auto-sized {long_} vs empty {empty}");
		Assert.Equal(empty, SailfishMeasure.Editor(fixedSize, 400, double.PositiveInfinity).Height, 1);   // AutoSize off: three lines
	}

	// WhatToEat New Recipe: typing the name enables Save (Entry.Text → view model → CanExecute → IsEnabled), all inside
	// the native write-back; the push was dropped there and Save stayed disabled until the 2 s heartbeat.
	[Fact]
	public void A_button_enabled_by_typing_updates_without_waiting_for_the_heartbeat()
	{
		var entry = new Entry();
		string? name = null;
		var save = new Button { Text = "Save" };
		var command = new Command(() => { }, () => !string.IsNullOrEmpty(name));
		save.Command = command;
		entry.TextChanged += (_, e) => { name = e.NewTextValue; command.ChangeCanExecute(); };
		using var h = new RendererHarness(new ContentPage { Title = "T", Content = new VerticalStackLayout { Children = { entry, save } } });
		var button = h.Shim.ByUri("button").Single();
		Assert.False(button.Props["enabled"].GetBoolean());
		var kicks = 0;
		var previous = h.Renderer.PollKick;
		var loop = SailfishDispatcherProvider.BindLoopThread();
		h.Renderer.PollKick = () => { kicks++; loop.Dispatch(h.Renderer.KickedPoll); };
		try
		{
			h.Renderer.HandleNativeEvent("text-changed", $"{{\"id\":\"{h.Shim.ByUri("entry").Single().Id}\",\"text\":\"T\"}}");
			loop.DrainQueue();   // the next loop turn, no heartbeat

			Assert.True(save.IsEnabled);
			Assert.True(kicks > 0, "the dropped push asked for no sync");
			Assert.True(button.Props["enabled"].GetBoolean());
		}
		finally
		{
			h.Renderer.PollKick = previous;
		}
	}

	// WhatToEat: an unset Background is Brush.Default (empty, not null); it counted as an explicit transparent plate,
	// so every plain button showed its label without Silica's plate.
	[Fact]
	public void A_plain_button_keeps_the_Silica_plate()
	{
		Assert.Equal(false, AdapterSnapshots.ButtonProps(new Button { Text = "Plain" })["mauiPlateSet"]);
		var flat = AdapterSnapshots.ButtonProps(new Button { Text = "Flat", Background = Colors.Transparent });
		Assert.Equal((true, Colors.Transparent), ((bool)flat["mauiPlateSet"]!, (Color)flat["mauiPlateColor"]!));
		Assert.Equal(Colors.Red, AdapterSnapshots.ButtonProps(new Button { Text = "Red", BackgroundColor = Colors.Red })["mauiPlateColor"]);
	}

	// Setting a Button's TextColor or BackgroundColor back to null must give Silica's theme colours back: the snapshot
	// used to drop the key, the diff pushed nothing, and the old colour stayed on the native button.
	[Fact]
	public void Clearing_a_button_colour_hands_the_Silica_colour_back()
	{
		var button = new Button { Text = "Go", TextColor = Colors.Red, BackgroundColor = Colors.Blue };
		using var h = new RendererHarness(new ContentPage { Title = "T", Content = new VerticalStackLayout { Children = { button } } });
		var host = h.Shim.ByUri("button").Single();
		Assert.Equal(("true", "true"), (host.Text("mauiTextColorSet")?.ToLowerInvariant(), host.Text("mauiPlateSet")?.ToLowerInvariant()));

		button.TextColor = null;
		button.BackgroundColor = null;
		h.Poll();

		Assert.Equal(("false", "false"), (host.Text("mauiTextColorSet")?.ToLowerInvariant(), host.Text("mauiPlateSet")?.ToLowerInvariant()));

		button.TextColor = Colors.Red;   // the same colour again must cross, not be taken for already applied
		h.Poll();
		Assert.Equal("true", host.Text("mauiTextColorSet")?.ToLowerInvariant());
	}

	// EmployeeDirectory: round avatars are an Image in a 60x60 Border with an Ellipse StrokeShape, inside a card
	// Border; the photos showed square, the corners painted nothing, and the ring stroke was hidden under the photo.
	[Fact]
	public void An_image_in_an_ellipse_border_is_masked_round_in_the_card_color()
	{
		var photo = new Image();
		var avatar = new Border
		{
			WidthRequest = 60,
			HeightRequest = 60,
			StrokeThickness = 1,
			Stroke = Colors.Gray,
			StrokeShape = new Microsoft.Maui.Controls.Shapes.Ellipse(),
			Content = photo,
		};
		var card = new Border
		{
			BackgroundColor = Colors.DarkGray,
			Padding = 16,
			StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
			Content = new Grid { Children = { avatar } },
		};
		var page = Page(card);
		page.BackgroundColor = Colors.Black;
		using var h = new RendererHarness(page);

		var props = new Dictionary<string, object?>();
		QtHostClip.Merge(props, photo, page);

		Assert.Equal(58, photo.Width, 1);
		Assert.Equal(QtHostClip.TopLeft | QtHostClip.TopRight | QtHostClip.BottomRight | QtHostClip.BottomLeft, props["mauiClipCorners"]);
		Assert.Equal(29 * SailfishDisplay.Density, (double)props["mauiClipRadius"]!, 1);
		Assert.Equal(Colors.DarkGray, props["mauiClipColor"]);
		Assert.Equal(Colors.Gray, props["mauiClipStroke"]);   // the caps cover the stroke's inner half, so they draw it
		Assert.Equal(SailfishDisplay.Density, (double)props["mauiClipStrokeWidth"]!, 2);
	}

	// GameOfLife: hundreds of BackgroundColor-only cells; EmployeeDirectory: group footers whose style BackgroundColor
	// sits under Color="Transparent". Both are plain rectangles: on the Canvas they were slow (and the footer only
	// partly painted).
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void A_box_showing_only_its_background_is_a_plain_rectangle(bool clearColor)
	{
		var box = new BoxView { HeightRequest = 8, BackgroundColor = Colors.LightGray };
		if (clearColor)
			box.Color = Colors.Transparent;
		using var h = new RendererHarness(Page(box));

		var rect = Assert.IsType<List<object?>>(QtHostShapes.BoxViewProps(box)!["mauiRect"]);

		Assert.Equal(4, rect.Count);
		Assert.Equal(QtHostShapes.SolidSpec(Colors.LightGray)![1], rect[0]);
	}

	// WhatToEat My Recipes sizes its carousel from OnSizeAllocated (HeightRequest = height - 150); the page reported the
	// whole window, header included, so the carousel ran past the bottom of the screen.
	[Fact]
	public void A_page_is_sized_to_the_content_area_below_the_header()
	{
		var page = new SizeRecordingPage { Title = "T", Content = new Label { Text = "x" } };
		using var h = new RendererHarness(page);

		Assert.True(page.Allocated.Height > 0);
		Assert.True(page.Allocated.Height < h.Window.Height, $"page {page.Allocated} in window {h.Window.Width}x{h.Window.Height}");
		Assert.Equal(page.Allocated.Height, ((View)page.Content).Height, 1);
		Assert.Equal(h.Window.Height, page.Bounds.Bottom, 1);   // it ends at the bottom of the window
	}

	private sealed class SizeRecordingPage : ContentPage
	{
		public Size Allocated { get; private set; }

		protected override void OnSizeAllocated(double width, double height)
		{
			base.OnSizeAllocated(width, height);
			Allocated = new Size(width, height);
		}
	}

	// WhatToEat My Recipes: a CarouselView (WidthRequest 350, HeightRequest 570) in a StackLayout; its pages ran past
	// the bottom of the screen.
	[Fact]
	public void A_carousel_page_spans_the_carousel_height()
	{
		var cells = new List<Grid>();
		var carousel = new CarouselView
		{
			WidthRequest = 350,
			HeightRequest = 570,
			HorizontalOptions = LayoutOptions.Fill,
			VerticalOptions = LayoutOptions.Fill,
			Loop = false,
			ItemsSource = new[] { "Egg roll", "Stew", "Salmon" },
			ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label { HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
				label.SetBinding(Label.TextProperty, ".");
				var cell = new Grid { WidthRequest = 340, Padding = new Thickness(0, 0, 10, 0), Children = { new BoxView { Color = Colors.Orange }, label } };
				cells.Add(cell);
				return cell;
			}),
		};
		using var h = new RendererHarness(new ContentPage
		{
			Title = "T",
			Content = new StackLayout { Margin = 20, Children = { new Label { Text = "Your recipes", FontSize = 30 }, carousel } },
		});
		for (var i = 0; i < 4; i++)
			h.Poll();

		Assert.Equal(570, carousel.Height, 1);
		Assert.NotEmpty(cells);
		Assert.All(cells, c => Assert.True(c.Height <= 570.01, $"cell {c.Height} x {c.Width}"));
	}

	// GameOfLife: ~400 BackgroundColor BoxViews, each on its own Canvas GL context, exhausted EGL.
	[Fact]
	public void A_background_color_box_takes_the_rectangle_fast_path()
	{
		var box = new BoxView { BackgroundColor = Colors.White, WidthRequest = 20, HeightRequest = 20 };
		using var h = new RendererHarness(Page(box));

		var rect = h.Shim.ByUri("shape").Single().Props["mauiRect"];
		Assert.Equal(System.Text.Json.JsonValueKind.Array, rect.ValueKind);
		Assert.Equal(4, rect.GetArrayLength());
	}
}
