using System.Text.Json;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>Tracker S41 (plan M10, D10 a): a gradient Background reaches any host as the shim's gradient spec; Border,
/// shapes and BoxView keep painting it themselves; a gradient Shadow.Brush shadows in its average colour.</summary>
[Collection("renderer")]
public sealed class GradientBackgroundTests
{
	private static LinearGradientBrush Linear() => new(new GradientStopCollection
	{
		new GradientStop(Colors.Red, 0), new GradientStop(Colors.Blue, 1),
	}, new Point(0, 0), new Point(1, 1));

	[Fact]
	public void A_layout_and_a_label_take_their_gradient_backgrounds()
	{
		var grid = new Grid { Background = Linear(), HeightRequest = 100 };
		var label = new Label
		{
			Text = "radial",
			Background = new RadialGradientBrush(new GradientStopCollection { new(Colors.White, 0), new(Colors.Black, 1) }, new Point(0.5, 0.5), 0.5),
		};
		var border = new Border { Background = Linear(), HeightRequest = 40 };
		using var h = new RendererHarness(new ContentPage { Content = new VerticalStackLayout { Children = { grid, label, border } } });
		for (var i = 0; i < 4; i++)
			h.Poll();

		JsonElement Spec(Element e)
		{
			var host = h.Renderer.CurrentHosts.First(x => ReferenceEquals(x.Element, e));
			return JsonDocument.Parse(h.Shim.ById(host.Id)!.Text("mauiBackgroundGradient")!).RootElement;
		}
		var linear = Spec(grid);
		Assert.Equal("linear", linear.GetProperty("t").GetString());
		Assert.Equal(1.0, linear.GetProperty("x1").GetDouble());
		Assert.Equal(2, linear.GetProperty("stops").GetArrayLength());
		var radial = Spec(label);
		Assert.Equal("radial", radial.GetProperty("t").GetString());
		Assert.Equal(0.5, radial.GetProperty("r").GetDouble());

		var borderHost = h.Renderer.CurrentHosts.First(x => ReferenceEquals(x.Element, border));
		Assert.Null(h.Shim.ById(borderHost.Id)!.Text("mauiBackgroundGradient"));   // its own fill spec paints it

		// Back to a solid colour: the gradient goes.
		grid.Background = new SolidColorBrush(Colors.Green);
		for (var i = 0; i < 4; i++)
			h.Poll();
		var gridHost = h.Renderer.CurrentHosts.First(x => ReferenceEquals(x.Element, grid));
		Assert.Equal(string.Empty, h.Shim.ById(gridHost.Id)!.Text("mauiBackgroundGradient"));
	}

	[Fact]
	public void A_gradient_shadow_brush_shadows_in_its_average_colour()
	{
		var spec = QtHostVisualState.ShadowSpec(new Shadow { Brush = Linear(), Radius = 4, Opacity = 1 }, 1);
		Assert.StartsWith(BridgeValue.ColorString(new Color(0.5f, 0, 0.5f, 1)), spec);
	}
}
