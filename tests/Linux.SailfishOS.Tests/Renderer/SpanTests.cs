using System.Text.Json;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>Tracker S42 (plan M11 spans, M17 step 6): span BackgroundColor, CharacterSpacing and TextTransform reach the
/// rich text; a span with a TapGestureRecognizer is a link whose tap fires it.</summary>
[Collection("renderer")]
public sealed class SpanTests
{
	private static (RendererHarness H, FakeShim.FakeObject Native) Show(Label label)
	{
		var h = new RendererHarness(new ContentPage { Title = "T", Content = new VerticalStackLayout { Children = { label } } });
		for (var i = 0; i < 4; i++)
			h.Poll();
		return (h, h.Shim.ByUri("label").Single(o => !o.Destroyed && o.Text("text")?.Contains("<span") == true));
	}

	[Fact]
	public void Span_background_spacing_and_transform_reach_the_rich_text()
	{
		var label = new Label
		{
			FormattedText = new FormattedString
			{
				Spans =
				{
					new Span { Text = "marked", BackgroundColor = Colors.Yellow },
					new Span { Text = "spaced", CharacterSpacing = 2 },
					new Span { Text = "shout", TextTransform = TextTransform.Uppercase },
				},
			},
		};
		var (h, native) = Show(label);
		using var _ = h;
		var text = native.Text("text")!;

		Assert.Contains("background-color:#", text);
		Assert.Contains("letter-spacing:", text);
		Assert.Contains(">SHOUT</span>", text);
		Assert.DoesNotContain("<a href", text);
		Assert.False(native.Props["mauiSpanLinks"].GetBoolean());
	}

	[Fact]
	public void A_tappable_span_is_a_link_whose_tap_fires_its_recognizer()
	{
		object? sender = null;
		var tap = new TapGestureRecognizer();
		tap.Tapped += (s, _) => sender = s;
		var commanded = 0;
		var link = new Span { Text = "terms", TextColor = Colors.Orange };
		link.GestureRecognizers.Add(tap);
		link.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => commanded++) });
		var label = new Label { FormattedText = new FormattedString { Spans = { new Span { Text = "Read the " }, link } } };
		var (h, native) = Show(label);
		using var _ = h;

		Assert.Contains("<a href=\"span:1\">", native.Text("text"));
		Assert.True(native.Props["mauiSpanLinks"].GetBoolean());

		h.Renderer.HandleNativeEvent("span-tapped", $"{{\"id\":\"{native.Id}\",\"index\":1}}");

		Assert.Same(label, sender);
		Assert.Equal(1, commanded);
	}
}
