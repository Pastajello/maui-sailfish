using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Input leg, GraphicsView touch (tracker S31): a real drag on a GraphicsView arrives as StartInteraction, several
/// DragInteraction and EndInteraction in the view's own coordinates; the drawable follows the finger (it is drawn again
/// only because the app calls Invalidate), shown in a screenshot.
/// </summary>
internal sealed partial class QtHostDiagnosticsRunner
{
	private sealed class TrailDrawable : IDrawable
	{
		public readonly List<PointF> Points = new();

		public void Draw(ICanvas canvas, RectF dirtyRect)
		{
			canvas.StrokeColor = Colors.Orange;
			canvas.StrokeSize = 6;
			for (var i = 1; i < Points.Count; i++)
				canvas.DrawLine(Points[i - 1], Points[i]);
		}
	}

	private void RunQtGraphicsInteractionCheck(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, Action done)
	{
		if (renderer.CurrentPage?.Navigation is not { } navigation)
		{
			done();
			return;
		}
		var trail = new TrailDrawable();
		var view = new GraphicsView { Drawable = trail, HeightRequest = 400, BackgroundColor = Color.FromArgb("#202428") };
		var starts = 0;
		var drags = 0;
		var ends = 0;
		PointF? first = null;
		view.StartInteraction += (_, e) => { starts++; first = e.Touches[0]; trail.Points.Clear(); trail.Points.Add(e.Touches[0]); view.Invalidate(); };
		view.DragInteraction += (_, e) => { drags++; trail.Points.Add(e.Touches[0]); view.Invalidate(); };
		view.EndInteraction += (_, _) => ends++;
		_ = navigation.PushAsync(new ContentPage { Title = "Draw", Content = new VerticalStackLayout { Padding = 16, Children = { view } } }, false);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1800), () =>
		{
			var host = renderer.CurrentHosts.FirstOrDefault(h => h.IsAttached && ReferenceEquals(h.Element, view));
			if (host is null || !QtHost.QtHostRuntime.TryItemGeometry(host.NativeHandle, out var scene))
			{
				_qtInputChecks.Check("graphics S31: the GraphicsView is hosted", false);
				Finish();
				return;
			}
			var x0 = scene.X + 60;
			var y0 = scene.Y + 60;
			QtHost.QtHostRuntime.InjectPointer(0, x0, y0);
			var i = 0;
			void Step()
			{
				i++;
				var x = x0 + i * 40;
				var y = y0 + i * 25;
				if (i < 12)
				{
					QtHost.QtHostRuntime.InjectPointer(2, x, y);
					dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(16), Step);
					return;
				}
				QtHost.QtHostRuntime.InjectPointer(1, x, y);
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(600), () =>
				{
					var expectX = (float)QtHost.QtHostUnits.ToLogical(60);
					_qtInputChecks.Check($"graphics S31: a drag on a GraphicsView arrives as Start {starts}==1 at ({first?.X:F0},{first?.Y:F0}) ≈ ({expectX:F0},{expectX:F0}) in its own dp, " +
						$"Drag x{drags}>=5, End {ends}==1",
						starts == 1 && drags >= 5 && ends == 1 && first is { } p && Math.Abs(p.X - expectX) <= 2 && Math.Abs(p.Y - expectX) <= 2);
					Shot(dispatcher, "input-graphics-drag", Finish);
				});
			}
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(16), Step);
		});

		void Finish()
		{
			_ = navigation.PopAsync(false);
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200), done);
		}
	}
}
