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
		var themed = new Label { Text = "Y = ", FontSize = Microsoft.Maui.SailfishOS.Handlers.SailfishMeasure.SilicaMediumFontDp() };
		var small = new Label { Text = "Y = ", FontSize = 14 };
		using var h = new RendererHarness(Page(unset, themed, small));

		Assert.Equal(themed.DesiredSize.Width, unset.DesiredSize.Width, 3);
		Assert.True(unset.DesiredSize.Width > small.DesiredSize.Width);
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
		var unset = Microsoft.Maui.SailfishOS.Platform.QtHost.QtHostPageRenderer.ButtonProps(new Button { Text = "Sign Out" });
		var sized = Microsoft.Maui.SailfishOS.Platform.QtHost.QtHostPageRenderer.ButtonProps(new Button { Text = "Sign Out", FontSize = 20 });

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
			var props = Microsoft.Maui.SailfishOS.Platform.QtHost.QtHostPageRenderer.ButtonProps(top);
			Assert.Equal("top", props["mauiIconPosition"]);
			Assert.Equal(4 * SailfishDisplay.Density, (double)props["mauiIconSpacing"]!, 3);
			Assert.True((bool)props["mauiFillPlate"]!);
			Assert.False((bool)Microsoft.Maui.SailfishOS.Platform.QtHost.QtHostPageRenderer.ButtonProps(left)["mauiFillPlate"]!);
			Assert.True((bool)Microsoft.Maui.SailfishOS.Platform.QtHost.QtHostPageRenderer.ButtonProps(new Button { Text = "x", MinimumHeightRequest = 150 })["mauiFillPlate"]!);

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
