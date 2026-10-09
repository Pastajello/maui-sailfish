using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.SailfishOS.Platform;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>Tracker S16 (plan M17 steps 1–3): swipe direction flags and threshold, InputTransparent letting the touch
/// through, and hit-testing a scaled view on its footprint.</summary>
[Collection("renderer")]
public sealed class RouterTests
{
	private static ContentPage Page(View content) => new() { Title = "T", Content = content };

	private static (RendererHarness H, QtHostInputRouter Router, SailfishDispatcher Loop) Start(ContentPage page)
	{
		var h = new RendererHarness(page);
		for (var i = 0; i < 4; i++)
			h.Poll();
		var loop = SailfishDispatcherProvider.BindLoopThread();
		return (h, new QtHostInputRouter(h.Renderer, loop), loop);
	}

	private static void Drag(QtHostInputRouter router, Point fromDp, double dxDp, double dyDp)
	{
		double X(double dp) => QtHostUnits.ToQtUnits(dp);
		router.OnPointer(0, X(fromDp.X), X(fromDp.Y), 0, 0);
		for (var i = 1; i <= 10; i++)
			router.OnPointer(2, X(fromDp.X + dxDp * i / 10), X(fromDp.Y + dyDp * i / 10), 0, 0);
		router.OnPointer(1, X(fromDp.X + dxDp), X(fromDp.Y + dyDp), 0, 0);
	}

	private static void Tap(QtHostInputRouter router, Point dp)
	{
		router.OnPointer(0, QtHostUnits.ToQtUnits(dp.X), QtHostUnits.ToQtUnits(dp.Y), 0, 0);
		router.OnPointer(1, QtHostUnits.ToQtUnits(dp.X), QtHostUnits.ToQtUnits(dp.Y), 0, 0);
	}

	[Fact]
	public void A_swipe_recognizer_for_either_direction_fires_past_its_threshold()
	{
		var both = 0;
		var shortThreshold = 0;
		var box = new BoxView { HeightRequest = 300, WidthRequest = 360, Color = Colors.Blue };
		var either = new SwipeGestureRecognizer { Direction = SwipeDirection.Left | SwipeDirection.Right };
		either.Swiped += (_, _) => both++;
		var near = new SwipeGestureRecognizer { Direction = SwipeDirection.Left, Threshold = 40 };
		near.Swiped += (_, _) => shortThreshold++;
		box.GestureRecognizers.Add(either);
		box.GestureRecognizers.Add(near);
		var (h, router, loop) = Start(Page(box));
		using var _ = h;
		var area = h.Renderer.CurrentHosts.First(x => ReferenceEquals(x.Element, box)).MauiLogicalBounds;
		Assert.True(area.Width > 200, $"box bounds {area}");

		Drag(router, new Point(area.Right - 10, area.Center.Y), -60, 0);    // past 40 dp, short of the default 100
		loop.DrainQueue();
		Assert.Equal((0, 1), (both, shortThreshold));

		Drag(router, new Point(area.Left + 10, area.Center.Y), 150, 0);   // rightwards, past 100: Left|Right takes it, Left does not
		loop.DrainQueue();
		Assert.Equal((1, 1), (both, shortThreshold));
	}

	[Fact]
	public void An_input_transparent_overlay_lets_the_tap_through()
	{
		var taps = 0;
		var below = new BoxView { HeightRequest = 120, WidthRequest = 120, Color = Colors.Blue };
		var tap = new TapGestureRecognizer();
		tap.Tapped += (_, _) => taps++;
		below.GestureRecognizers.Add(tap);
		var overlay = new BoxView { HeightRequest = 120, WidthRequest = 120, Color = Colors.Red, InputTransparent = true };
		var grid = new Grid { HeightRequest = 120, WidthRequest = 120, HorizontalOptions = LayoutOptions.Start, Children = { below, overlay } };
		var (h, router, loop) = Start(Page(grid));
		using var _ = h;
		Assert.True(h.Shim.Objects.Count() > 0);

		Tap(router, h.Renderer.CurrentHosts.First(x => ReferenceEquals(x.Element, below)).MauiLogicalBounds.Center);
		loop.DrainQueue();
		Assert.Equal(1, taps);
	}

	[Fact]
	public void A_scaled_view_is_hit_on_its_scaled_footprint()
	{
		var taps = 0;
		var box = new BoxView { HeightRequest = 100, WidthRequest = 100, Color = Colors.Blue, Scale = 2,
			HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
		var tap = new TapGestureRecognizer();
		tap.Tapped += (_, _) => taps++;
		box.GestureRecognizers.Add(tap);
		var (h, router, loop) = Start(Page(new Grid { HeightRequest = 600, Children = { box } }));
		using var _ = h;
		var host = h.Renderer.CurrentHosts.First(x => ReferenceEquals(x.Element, box));
		// The scaled box covers its centre ± 100 dp; a point 80 dp right of the centre is outside the unscaled rect.
		var (cx, cy) = host.HitTransform!.Value.Transform(50, 50);
		Tap(router, new Point(cx + 80, cy));
		loop.DrainQueue();
		Assert.Equal(1, taps);

		Tap(router, new Point(cx + 130, cy));   // beyond the scaled footprint: no tap
		loop.DrainQueue();
		Assert.Equal(1, taps);
	}

	// GetPosition(relativeTo) returned the root point whatever was asked.
	[Fact]
	public void A_tap_position_is_relative_to_the_element_asked_for()
	{
		Point? inBox = null, inWindow = null, inPage = null;
		var box = new BoxView { HeightRequest = 200, WidthRequest = 200, Margin = new Thickness(40, 60, 0, 0), Color = Colors.Blue };
		var tap = new TapGestureRecognizer();
		tap.Tapped += (_, e) => { inBox = e.GetPosition(box); inWindow = e.GetPosition(null); inPage = e.GetPosition(box.Parent?.Parent as Page); };
		box.GestureRecognizers.Add(tap);
		var (h, router, loop) = Start(Page(new VerticalStackLayout { Children = { box } }));
		using var _ = h;
		var area = h.Renderer.CurrentHosts.First(x => ReferenceEquals(x.Element, box)).MauiLogicalBounds;

		Tap(router, new Point(area.X + 30, area.Y + 50));
		loop.DrainQueue();

		Assert.NotNull(inBox);
		Assert.InRange(inBox!.Value.X, 29, 31);
		Assert.InRange(inBox.Value.Y, 49, 51);
		Assert.InRange(inWindow!.Value.X, area.X + 29, area.X + 31);
		Assert.Equal(inWindow, inPage);
	}

	[Fact]
	public void A_secondary_button_tap_recognizer_ignores_a_touch()
	{
		var primary = 0;
		var secondary = 0;
		var box = new BoxView { HeightRequest = 200, WidthRequest = 200, Color = Colors.Blue };
		var right = new TapGestureRecognizer { Buttons = ButtonsMask.Secondary };
		right.Tapped += (_, _) => secondary++;
		var left = new TapGestureRecognizer();
		left.Tapped += (_, _) => primary++;
		box.GestureRecognizers.Add(right);
		box.GestureRecognizers.Add(left);
		var (h, router, loop) = Start(Page(box));
		using var _ = h;
		var area = h.Renderer.CurrentHosts.First(x => ReferenceEquals(x.Element, box)).MauiLogicalBounds;

		Tap(router, area.Center);
		loop.DrainQueue();

		Assert.Equal((1, 0), (primary, secondary));
	}

	[Fact]
	public void A_pointer_position_is_relative_to_the_element_asked_for()
	{
		Point? pressed = null;
		var box = new BoxView { HeightRequest = 200, WidthRequest = 200, Margin = new Thickness(40, 60, 0, 0), Color = Colors.Blue };
		var pointer = new PointerGestureRecognizer();
		pointer.PointerPressed += (_, e) => pressed = e.GetPosition(box);
		box.GestureRecognizers.Add(pointer);
		var (h, router, loop) = Start(Page(new VerticalStackLayout { Children = { box } }));
		using var _ = h;
		var area = h.Renderer.CurrentHosts.First(x => ReferenceEquals(x.Element, box)).MauiLogicalBounds;

		Tap(router, new Point(area.X + 10, area.Y + 20));
		loop.DrainQueue();

		Assert.NotNull(pressed);
		Assert.InRange(pressed!.Value.X, 9, 11);
		Assert.InRange(pressed.Value.Y, 19, 21);
	}
}
