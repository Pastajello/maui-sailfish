using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Canvas leg (MAUI_SAILFISH_QT_HOST_CANVAS_DIAG=1, tracker S30): one GraphicsView draws, side by side, the point
/// DrawString on a marked baseline next to a Label, an EvenOdd star and an Antialias=false diagonal; pixel readbacks
/// from the Canvas check the baseline (no ink below it), the star's empty centre and the line's hard edges. A NonZero
/// star and an antialiased line on a second canvas are the controls. An SF-SHOT shows them.
/// </summary>
internal sealed partial class QtHostDiagnosticsRunner
{
	private bool _qtCanvasDiag;
	private readonly DiagChecks _qtCanvasChecks = new("Qt canvas diag");

	internal const float CanvasBaseline = 60;

	/// <summary>The drawing: "Ag" on the baseline y=60 (a red rule marks it), a five-point star filled with the rule,
	/// a diagonal with or without antialiasing.</summary>
	private sealed class CanvasProbe(WindingMode winding, bool antialias) : IDrawable
	{
		public void Draw(ICanvas canvas, RectF dirtyRect)
		{
			canvas.Antialias = antialias;
			canvas.StrokeColor = Colors.Red;
			canvas.StrokeSize = 1;
			canvas.DrawLine(0, CanvasBaseline, 140, CanvasBaseline);
			canvas.FontColor = Colors.White;
			canvas.FontSize = 40;
			canvas.DrawString("Ag", 10, CanvasBaseline, HorizontalAlignment.Left);
			var star = new PathF();
			for (var i = 0; i < 5; i++)
			{
				var a = -Math.PI / 2 + i * 4 * Math.PI / 5;
				var p = new PointF(230 + (float)(60 * Math.Cos(a)), 70 + (float)(60 * Math.Sin(a)));
				if (i == 0)
					star.MoveTo(p);
				else
					star.LineTo(p);
			}
			star.Close();
			canvas.FillColor = Colors.Gold;
			canvas.FillPath(star, winding);
			canvas.StrokeColor = Colors.White;
			canvas.StrokeSize = 3;
			canvas.DrawLine(310, 10, 400, 130);
		}
	}

	/// <summary>Tracker S41: gradient Backgrounds on a layout, a label and a button get the shim's shader child.</summary>
	private void GradientPage(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, Microsoft.Maui.Controls.Window window)
	{
		var grid = new Grid
		{
			HeightRequest = 260,
			Background = new LinearGradientBrush(new GradientStopCollection
			{
				new(Colors.MidnightBlue, 0), new(Colors.MediumPurple, 0.5f), new(Colors.Orange, 1),
			}, new Point(0, 0), new Point(1, 1)),
			Children = { new Label { Text = "gradient layout", Margin = 16, TextColor = Colors.White } },
		};
		var label = new Label
		{
			Text = "radial label",
			Padding = 24,
			HeightRequest = 160,
			TextColor = Colors.Black,
			Background = new RadialGradientBrush(new GradientStopCollection { new(Colors.White, 0), new(Colors.SeaGreen, 1) }, new Point(0.5, 0.5), 0.6),
		};
		var button = new Button
		{
			Text = "gradient button",
			Background = new LinearGradientBrush(new GradientStopCollection { new(Colors.Crimson, 0), new(Colors.Gold, 1) }, new Point(0, 0.5), new Point(1, 0.5)),
		};
		window.Page = new NavigationPage(new ContentPage
		{
			Title = "Gradients",
			Content = new VerticalStackLayout { Padding = 16, Spacing = 20, Children = { grid, label, button } },
		});
		WhenStackIdle(dispatcher, () => dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(800), () =>
		{
			string Child(View view) => QtHost.QtHostRuntime.Eval(
				$"(function(){{var t={DiagQml.ItemJs(DiagQml.HostOf(renderer, view))};if(!t)return 'no host';var k=t.children;for(var i=0;i<k.length;i++)if(k[i].objectName==='mauiBackgroundGradient')return k[i].visible?'visible':'hidden';return 'none';}})()");
			var g = Child(grid);
			var l = Child(label);
			var b = Child(button);
			_qtCanvasChecks.Check($"gradient Background S41: the layout ({g}), the label ({l}) and the button ({b}) each carry a visible gradient shader",
				g == "visible" && l == "visible" && b == "visible");
			Shot(dispatcher, "canvas-2-gradients", () =>
			{
				_qtCanvasChecks.CheckNoOffThreadCalls();
				_qtCanvasChecks.Accept("OK — DrawString anchors at the baseline, EvenOdd fills leave holes, Antialias=false draws hard edges, gradient Backgrounds draw under any view");
				QtHost.QtHostRuntime.Shutdown();
			});
		}));
	}

	private void RunQtCanvasDiagnostics(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		if (_context.Window is not Microsoft.Maui.Controls.Window window)
		{
			QtHost.QtHostRuntime.Shutdown();
			return;
		}
		var probe = new GraphicsView { Drawable = new CanvasProbe(WindingMode.EvenOdd, antialias: false), HeightRequest = 150, WidthRequest = 420 };
		var control = new GraphicsView { Drawable = new CanvasProbe(WindingMode.NonZero, antialias: true), HeightRequest = 150, WidthRequest = 420 };
		var label = new Label { Text = "Ag", FontSize = 40, TextColor = Colors.White };
		window.Page = new NavigationPage(new ContentPage
		{
			Title = "Canvas",
			Content = new VerticalStackLayout { Padding = 16, Spacing = 24, Children = { probe, control, label } },
		});
		WhenStackIdle(dispatcher, () => dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(800), () =>
		{
			string Js(GraphicsView view) => DiagQml.ItemJs(DiagQml.HostOf(renderer, view));
			// Pixel (dp → canvas px through mauiScale) of a canvas, as [r,g,b,a].
			int[] Pixel(GraphicsView view, double xDp, double yDp)
			{
				var raw = QtHost.QtHostRuntime.Eval(
					$"(function(){{var c={Js(view)};if(!c)return '';var s=c.mauiScale||1;var d=c.getContext('2d').getImageData(Math.round({xDp.ToString(System.Globalization.CultureInfo.InvariantCulture)}*s),Math.round({yDp.ToString(System.Globalization.CultureInfo.InvariantCulture)}*s),1,1).data;return d[0]+','+d[1]+','+d[2]+','+d[3];}})()");
				var parts = (raw ?? "").Split(',');
				return parts.Length == 4 ? parts.Select(p => int.TryParse(p, out var v) ? v : -1).ToArray() : new[] { -1, -1, -1, -1 };
			}
			string N(double v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);
			// Text ink (bright, not the red rule) in a region of the canvas, counted in one eval: the "A" stands on the
			// baseline, only the "g" descends (x 12..28 is the "A" alone; the "g" starts near 32).
			int Ink(GraphicsView view, double y0, double y1, double x0 = 12, double x1 = 28) =>
				(int)DiagQml.EvalNum($"(function(){{var c={Js(view)};if(!c)return -1;var s=c.mauiScale||1;" +
					$"var X=Math.round({N(x0)}*s),Y=Math.round({N(y0)}*s),W=Math.round(({N(x1)}-{N(x0)})*s),H=Math.round(({N(y1)}-{N(y0)})*s);" +
					"var d=c.getContext('2d').getImageData(X,Y,W,H).data,n=0;for(var i=0;i<d.length;i+=4)if(d[i+3]>128&&d[i+1]>128)n++;return n;})()", -1);
			var above = Ink(probe, CanvasBaseline - 20, CanvasBaseline - 2);
			var below = Ink(probe, CanvasBaseline + 2, CanvasBaseline + 12);
			_qtCanvasChecks.Check($"DrawString(x, y): y is the baseline as on Android — the 'A' has ink above it ({above}>0) and none below ({below}==0)",
				above > 0 && below == 0);
			var centre = Pixel(probe, 230, 75);
			var arm = Pixel(probe, 230, 25);
			var controlCentre = Pixel(control, 230, 75);
			_qtCanvasChecks.Check($"EvenOdd FillPath: the star's centre stays empty (alpha {centre[3]}==0) while its arm is filled ({arm[3]}>0); NonZero fills it ({controlCentre[3]}>0)",
				centre[3] == 0 && arm[3] > 0 && controlCentre[3] > 0);
			// Partly covered pixels along the diagonal (310,10)-(400,130): antialiasing makes them, hard edges do not.
			int Partial(GraphicsView view) =>
				(int)DiagQml.EvalNum($"(function(){{var c={Js(view)};if(!c)return -1;var s=c.mauiScale||1;" +
					"var d=c.getContext('2d').getImageData(Math.round(300*s),Math.round(30*s),Math.round(110*s),Math.round(80*s)).data;" +
					"var w=Math.round(110*s),n=0;for(var i=0;i<d.length;i+=4){var a=d[i+3];if(a>10&&a<245)n++;}return n;})()", -1);
			var hard = Partial(probe);
			var soft = Partial(control);
			_qtCanvasChecks.Check($"Antialias=false: the diagonal has hard edges ({hard} partly covered pixels, antialiased control {soft})",
				soft > 0 && hard * 4 < soft);
			Shot(dispatcher, "canvas-1-text-fill-aa", () => GradientPage(renderer, dispatcher, window));
		}));
	}
}
