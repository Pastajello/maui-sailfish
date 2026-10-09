using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.SailfishOS.Handlers;
using Microsoft.Maui.SailfishOS.Platform;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>Tracker S31 (plan M9 steps 3–4): touches on a GraphicsView reach Start/Drag/EndInteraction in its own
/// coordinates and hold the page drag; the drawable is recorded on Invalidate and size changes, not every reconcile.</summary>
[Collection("renderer")]
public sealed class GraphicsViewInteractionTests
{
	private sealed class Dot : IDrawable
	{
		public int Draws;
		public void Draw(ICanvas canvas, RectF dirtyRect)
		{
			Draws++;
			canvas.FillColor = Colors.Red;
			canvas.FillCircle(10, 10, 5);
		}
	}

	private static (RendererHarness H, GraphicsView View, Dot Drawable) Show()
	{
		var drawable = new Dot();
		var view = new GraphicsView { Drawable = drawable, HeightRequest = 200, WidthRequest = 300 };
		var h = new RendererHarness(new ContentPage { Title = "T", Content = new VerticalStackLayout { Padding = 20, Children = { view } } });
		for (var i = 0; i < 4; i++)
			h.Poll();
		return (h, view, drawable);
	}

	private static double Px(double dp) => QtHostUnits.ToQtUnits(dp);

	[Fact]
	public void A_drag_on_a_graphics_view_reaches_its_interaction_events_in_its_own_coordinates()
	{
		var (h, view, _) = Show();
		using var _h = h;
		var events = new List<string>();
		view.StartInteraction += (_, e) => events.Add($"start {e.Touches[0].X:F0},{e.Touches[0].Y:F0}");
		view.DragInteraction += (_, e) => events.Add($"drag {e.Touches[0].X:F0},{e.Touches[0].Y:F0}");
		view.EndInteraction += (_, e) => events.Add($"end {e.Touches[0].X:F0},{e.Touches[0].Y:F0} inside={e.IsInsideBounds}");
		var loop = SailfishDispatcherProvider.BindLoopThread();
		var router = new QtHostInputRouter(h.Renderer, loop);
		var bounds = h.Renderer.CurrentHosts.First(x => ReferenceEquals(x.Element, view)).MauiLogicalBounds;

		router.OnPointer(0, Px(bounds.X + 10), Px(bounds.Y + 20), 0, 0);
		router.OnPointer(2, Px(bounds.X + 60), Px(bounds.Y + 20), 0, 0);
		router.OnPointer(1, Px(bounds.X + 400), Px(bounds.Y + 20), 0, 0);   // released outside it

		Assert.Equal(new[] { "start 10,20", "drag 60,20", "end 400,20 inside=False" }, events);
		Assert.Equal(new[] { "true", "false" },
			h.Shim.PageCalls.Where(c => c.Method == "mauiHoldDrag").Select(c => c.Arg.Trim('"')));
	}

	[Fact]
	public void The_drawable_is_recorded_on_Invalidate_and_on_a_new_size_not_on_every_reconcile()
	{
		var (h, view, drawable) = Show();
		using var _h = h;
		var handler = (SailfishGraphicsHandler)view.Handler!;
		var recordings = handler.Recordings;
		var draws = drawable.Draws;

		for (var i = 0; i < 6; i++)
			h.Poll();
		h.Renderer.MarkLayoutDirty();
		h.Poll();
		Assert.Equal(recordings, handler.Recordings);
		Assert.Equal(draws, drawable.Draws);

		view.Invalidate();
		Assert.Equal(recordings + 1, handler.Recordings);

		view.HeightRequest = 260;
		for (var i = 0; i < 4; i++)
			h.Poll();
		Assert.True(handler.Recordings >= recordings + 2, $"recordings {handler.Recordings} after a resize");
	}
}
