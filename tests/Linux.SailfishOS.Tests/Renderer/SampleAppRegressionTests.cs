using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.SailfishOS.Handlers;
using Microsoft.Maui.SailfishOS.Hosting;
using Microsoft.Maui.SailfishOS.Platform;
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
