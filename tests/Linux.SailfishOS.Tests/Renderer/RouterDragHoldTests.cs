using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.SailfishOS.Platform;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;
using SailfishView = Microsoft.Maui.Controls.PlatformConfiguration.SailfishOSSpecific.VisualElement;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>Tracker S60 (plan M17 step 10, D17 a): a view's captured Pan/Swipe/Pinch drag holds the page's back swipe
/// and pulley until the finger lifts; KeepsDrag=false leaves them to Silica.</summary>
[Collection("renderer")]
public sealed class RouterDragHoldTests
{
	private static (RendererHarness H, QtHostInputRouter Router, SailfishDispatcher Loop) Start(View content)
	{
		var h = new RendererHarness(new ContentPage { Title = "T", Content = content });
		for (var i = 0; i < 4; i++)
			h.Poll();
		var loop = SailfishDispatcherProvider.BindLoopThread();
		var router = new QtHostInputRouter(h.Renderer, loop);
		return (h, router, loop);
	}

	private static double Px(double dp) => QtHostUnits.ToQtUnits(dp);

	private static List<string> Holds(RendererHarness h) =>
		h.Shim.PageCalls.Where(c => c.Method == "mauiHoldDrag").Select(c => c.Arg.Trim('"')).ToList();

	private static void Drag(QtHostInputRouter router, Point from, double dx)
	{
		router.OnPointer(0, Px(from.X), Px(from.Y), 0, 0);
		for (var i = 1; i <= 5; i++)
			router.OnPointer(2, Px(from.X + dx * i / 5), Px(from.Y), 0, 0);
		router.OnPointer(1, Px(from.X + dx), Px(from.Y), 0, 0);
	}

	private static BoxView Box() => new() { HeightRequest = 200, WidthRequest = 300, Color = Colors.Teal };

	[Fact]
	public void A_pan_holds_the_page_drag_from_press_to_release()
	{
		var box = Box();
		var updates = 0;
		var pan = new PanGestureRecognizer();
		pan.PanUpdated += (_, e) => { if (e.StatusType == GestureStatus.Running) updates++; };
		box.GestureRecognizers.Add(pan);
		var (h, router, loop) = Start(box);
		using var _ = h;
		var at = h.Renderer.CurrentHosts.First(x => ReferenceEquals(x.Element, box)).MauiLogicalBounds.Center;

		Drag(router, at, 120);
		loop.DrainQueue();

		Assert.True(updates > 0);
		Assert.Equal(new[] { "true", "false" }, Holds(h));
		Assert.Equal(1, router.PageDragHolds);
	}

	[Fact]
	public void A_swipe_holds_it_too_and_a_tap_does_not()
	{
		var swiped = new Box_();
		var box = swiped.View;
		box.GestureRecognizers.Add(new SwipeGestureRecognizer { Direction = SwipeDirection.Right });
		var tapBox = Box();
		tapBox.GestureRecognizers.Add(new TapGestureRecognizer());
		var (h, router, loop) = Start(new VerticalStackLayout { Children = { box, tapBox } });
		using var _ = h;
		var tapAt = h.Renderer.CurrentHosts.First(x => ReferenceEquals(x.Element, tapBox)).MauiLogicalBounds.Center;
		router.OnPointer(0, Px(tapAt.X), Px(tapAt.Y), 0, 0);
		router.OnPointer(1, Px(tapAt.X), Px(tapAt.Y), 0, 0);
		Assert.Empty(Holds(h));

		var at = h.Renderer.CurrentHosts.First(x => ReferenceEquals(x.Element, box)).MauiLogicalBounds.Center;
		Drag(router, at, 150);
		loop.DrainQueue();
		Assert.Equal(new[] { "true", "false" }, Holds(h));
	}

	[Fact]
	public void KeepsDrag_false_leaves_the_drag_to_silica()
	{
		var box = Box();
		box.GestureRecognizers.Add(new PanGestureRecognizer());
		SailfishView.SetKeepsDrag(box, false);
		var (h, router, loop) = Start(box);
		using var _ = h;
		var at = h.Renderer.CurrentHosts.First(x => ReferenceEquals(x.Element, box)).MauiLogicalBounds.Center;

		Drag(router, at, 120);
		loop.DrainQueue();

		Assert.Empty(Holds(h));
		Assert.Equal(0, router.PageDragHolds);
	}

	private sealed class Box_
	{
		public BoxView View { get; } = Box();
	}
}
