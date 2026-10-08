using System.Text.Json;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>Tracker S30 (plan M9 step 5): what the drawing and text adapters receive — shape paths and props, span
/// rich text, the remote image cache policy.</summary>
[Collection("renderer")]
public sealed class DrawingSerialisationTests
{
	private static RendererHarness Show(View content)
	{
		var h = new RendererHarness(new ContentPage { Title = "T", Content = new VerticalStackLayout { Children = { content } } });
		for (var i = 0; i < 4; i++)
			h.Poll();
		return h;
	}

	private static string? StringProp(FakeShim.FakeObject o, string name) =>
		o.Props.TryGetValue(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

	[Fact]
	public void PathOps_records_moves_lines_curves_and_close_in_device_units()
	{
		var path = new PathF();
		path.MoveTo(0, 0);
		path.LineTo(10, 0);
		path.QuadTo(15, 5, 10, 10);
		path.CurveTo(5, 15, 0, 15, 0, 10);
		path.Close();

		var ops = QtHostShapes.PathOps(path, 2.0);
		var json = JsonSerializer.Serialize(ops);

		Assert.NotEmpty(ops);
		// Density 2: the line's end x=10 dp becomes 20 device px.
		Assert.Contains("20", json);
		Assert.True(ops.Count >= 4, json);
	}

	[Fact]
	public void A_shape_sends_its_geometry_fill_stroke_and_winding()
	{
		var polygon = new Polygon
		{
			Points = new PointCollection { new(0, 0), new(40, 0), new(20, 30) },
			Fill = Colors.Gold,
			Stroke = Colors.Black,
			StrokeThickness = 2,
			FillRule = FillRule.Nonzero,
			WidthRequest = 50,
			HeightRequest = 40,
		};
		var props = QtHostShapes.ShapeProps(polygon);

		Assert.NotNull(props);
		Assert.Equal(1, Convert.ToInt32(props!["mauiWinding"]));   // FillRule order: 0 EvenOdd, 1 Nonzero
		Assert.Contains(props.Keys, k => k.Contains("Fill", StringComparison.OrdinalIgnoreCase));
		Assert.Contains(props.Keys, k => k.Contains("Stroke", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public void Spans_become_escaped_rich_text_with_their_styles()
	{
		var label = new Label
		{
			FormattedText = new FormattedString
			{
				Spans =
				{
					new Span { Text = "bold <b>", FontAttributes = FontAttributes.Bold, TextColor = Colors.Red },
					new Span { Text = "line\nbreak", TextDecorations = TextDecorations.Underline | TextDecorations.Strikethrough },
					new Span { Text = "" },
				},
			},
		};
		using var h = Show(label);
		var text = StringProp(h.Shim.ByUri("label").Single(o => !o.Destroyed), "text") ?? "";

		Assert.Contains("font-weight:bold;", text);
		Assert.Contains("bold &lt;b&gt;", text);
		Assert.Contains("color:#", text);
		Assert.Contains("text-decoration:underline line-through;", text);
		Assert.Contains("line<br/>break", text);
		Assert.Equal(2, text.Split("<span").Length - 1);   // the empty span is dropped
	}

	[Theory]
	[InlineData(true, 2 * 3600, "#maui-cache=7200")]
	[InlineData(false, 3600, "#maui-cache=0")]
	public void A_remote_image_carries_its_cache_policy(bool caching, int validitySeconds, string fragment)
	{
		var source = new UriImageSource
		{
			Uri = new Uri("https://example.invalid/pic.png"),
			CachingEnabled = caching,
			CacheValidity = TimeSpan.FromSeconds(validitySeconds),
		};
		Assert.Equal("https://example.invalid/pic.png" + fragment, QtHostImages.Resolve(source));
	}

	[Fact]
	public void An_apps_own_fragment_keeps_the_default_policy()
	{
		var source = new UriImageSource { Uri = new Uri("https://example.invalid/pic.png#v2") };
		Assert.Equal("https://example.invalid/pic.png#v2", QtHostImages.Resolve(source));
	}
}
