using System.Text.Json;
using Microsoft.Maui.Controls;
using Microsoft.Maui.SailfishOS.Platform;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>Tracker S43 (plan M11 measure and auto-scaling): FormattedText with spans of different fonts is measured run
/// by run, an unsized span measures at the size the label paints, and explicit sizes follow the phone's text size
/// when FontAutoScalingEnabled.</summary>
[Collection("renderer")]
public sealed class MixedFontMeasureTests
{
	private static RendererHarness Show(Label label, Func<string, string?>? theme = null)
	{
		var h = new RendererHarness(new ContentPage { Title = "T", Content = new VerticalStackLayout { Children = { label } } });
		h.Shim.EvalHook = theme;
		for (var i = 0; i < 4; i++)
			h.Poll();
		return h;
	}

	private static double Density => SailfishDisplay.Density;

	private static JsonElement[] Runs(FakeShim shim)
	{
		using var doc = JsonDocument.Parse(shim.LastMeasureRequest!);
		return doc.RootElement.TryGetProperty("runs", out var runs) ? runs.EnumerateArray().Select(r => r.Clone()).ToArray() : [];
	}

	// The phone's "Text size" set to large: Theme.fontSizeMedium is 1.5 times Theme.fontSizeMediumBase.
	private static string? LargeText(string expression) => expression switch
	{
		"Theme.fontSizeMedium" => "60",
		"Theme.fontSizeMediumBase" => "40",
		_ => null,
	};

	// Found on the phone (collection leg, chat rows ~1.9 times too tall): the text-scale probe asked the theme before the
	// screen size was known, and the dp value cached then kept density 1 for the whole run.
	[Fact]
	public void A_theme_size_asked_before_the_screen_is_known_follows_the_density_that_comes_later()
	{
		var (width, height) = (SailfishDisplay.PixelWidth, SailfishDisplay.PixelHeight);
		using var h = Show(new Label { Text = "x" }, e => e == "Theme.fontSizeMedium" ? "48" : null);
		try
		{
			Microsoft.Maui.SailfishOS.Handlers.SailfishMeasure.ClearThemeCache();
			SailfishDisplay.Update(540, 1080);    // density 1, before the real screen
			Assert.Equal(48, Microsoft.Maui.SailfishOS.Handlers.SailfishFontRules.SilicaMediumFontDp(), 3);
			SailfishDisplay.Update(1080, 2160);   // the phone's screen: density 2
			Assert.Equal(24, Microsoft.Maui.SailfishOS.Handlers.SailfishFontRules.SilicaMediumFontDp(), 3);
		}
		finally
		{
			SailfishDisplay.Update(width, height);
		}
	}

	[Fact]
	public void A_two_size_row_is_measured_as_tall_as_its_tallest_span()
	{
		var label = new Label
		{
			FormattedText = new FormattedString { Spans = { new Span { Text = "small ", FontSize = 14 }, new Span { Text = "BIG", FontSize = 40 } } },
		};
		using var h = Show(label);

		var size = label.Measure(double.PositiveInfinity, double.PositiveInfinity);

		var runs = Runs(h.Shim);
		Assert.Equal(2, runs.Length);
		Assert.Equal(0, runs[0].GetProperty("s").GetInt32());
		Assert.Equal(6, runs[0].GetProperty("n").GetInt32());
		Assert.Equal(6, runs[1].GetProperty("s").GetInt32());
		Assert.Equal(14 * Density, runs[0].GetProperty("px").GetDouble(), 3);
		Assert.Equal(40 * Density, runs[1].GetProperty("px").GetDouble(), 3);
		Assert.True(size.Height >= 40 * 1.2 - 0.01, $"height {size.Height} < the 40 dp span's line");   // the fake: a line is 1.2 × the tallest px
	}

	[Fact]
	public void One_font_across_spans_keeps_the_single_font_measure()
	{
		var label = new Label
		{
			FontSize = 20,
			FormattedText = new FormattedString { Spans = { new Span { Text = "one " }, new Span { Text = "font", TextColor = Microsoft.Maui.Graphics.Colors.Red } } },
		};
		using var h = Show(label);

		label.Measure(double.PositiveInfinity, double.PositiveInfinity);

		Assert.Empty(Runs(h.Shim));
		Assert.Contains("\"px\":" + (20 * Density).ToString(System.Globalization.CultureInfo.InvariantCulture), h.Shim.LastMeasureRequest);
	}

	[Fact]
	public void An_unsized_span_measures_at_the_labels_painted_size_and_with_its_text_transform()
	{
		var label = new Label
		{
			FontSize = 30,
			FormattedText = new FormattedString
			{
				Spans = { new Span { Text = "inherits ", TextTransform = TextTransform.Uppercase }, new Span { Text = "tiny", FontSize = 12 } },
			},
		};
		using var h = Show(label);

		label.Measure(double.PositiveInfinity, double.PositiveInfinity);

		var runs = Runs(h.Shim);
		Assert.Equal(30 * Density, runs[0].GetProperty("px").GetDouble(), 3);
		Assert.Equal(12 * Density, runs[1].GetProperty("px").GetDouble(), 3);
		Assert.Contains("\"text\":\"INHERITS tiny\"", h.Shim.LastMeasureRequest);
	}

	[Fact]
	public void Explicit_sizes_follow_the_phones_text_size_unless_auto_scaling_is_off()
	{
		var scaled = new Label { Text = "scaled", FontSize = 20 };
		var fixedSize = new Label { Text = "fixed", FontSize = 20, FontAutoScalingEnabled = false };
		var h = new RendererHarness(new ContentPage { Title = "T", Content = new VerticalStackLayout { Children = { scaled, fixedSize } } });
		using var _h = h;
		h.Shim.EvalHook = LargeText;
		for (var i = 0; i < 4; i++)
			h.Poll();

		double PixelSize(string text) => h.Shim.ByUri("label").Single(o => !o.Destroyed && o.Text("text") == text).Props["mauiPixelSize"].GetDouble();
		Assert.Equal(30 * Density, PixelSize("scaled"), 3);
		Assert.Equal(20 * Density, PixelSize("fixed"), 3);
	}

	[Fact]
	public void A_span_size_is_scaled_in_the_rich_text_and_in_the_measure()
	{
		var label = new Label
		{
			FormattedText = new FormattedString { Spans = { new Span { Text = "a", FontSize = 10 }, new Span { Text = "b", FontSize = 20, FontAutoScalingEnabled = false } } },
		};
		using var h = Show(label, LargeText);
		h.Poll();   // the snapshot after the theme answered

		label.Measure(double.PositiveInfinity, double.PositiveInfinity);

		var runs = Runs(h.Shim);
		Assert.Equal(15 * Density, runs[0].GetProperty("px").GetDouble(), 3);
		Assert.Equal(20 * Density, runs[1].GetProperty("px").GetDouble(), 3);
		var html = h.Shim.ByUri("label").Single(o => !o.Destroyed && o.Text("text")?.Contains("<span") == true).Text("text")!;
		Assert.Contains("font-size:" + (15 * Density).ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + "px", html);
		Assert.Contains("font-size:" + (20 * Density).ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + "px", html);
	}
}
