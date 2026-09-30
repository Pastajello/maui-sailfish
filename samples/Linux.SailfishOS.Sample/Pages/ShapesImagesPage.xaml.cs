using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.SailfishOS.Sample.Pages;

/// <summary>
/// Gallery of vector graphics: GraphicsView with a custom IDrawable, MAUI
/// Shapes and the image aspect modes.
/// </summary>
public partial class ShapesImagesPage : ContentPage
{
	public ShapesImagesPage()
	{
		InitializeComponent();
		Canvas.Drawable = new SailfishDrawable();
	}

	private sealed class SailfishDrawable : IDrawable
	{
		public void Draw(ICanvas canvas, RectF dirtyRect)
		{
			// Background grid.
			canvas.StrokeColor = Colors.SteelBlue;
			canvas.StrokeSize = 1;
			for (var x = 0f; x < dirtyRect.Width; x += 40)
				canvas.DrawLine(x, 0, x, dirtyRect.Height);
			for (var y = 0f; y < dirtyRect.Height; y += 40)
				canvas.DrawLine(0, y, dirtyRect.Width, y);

			// Filled wave.
			var path = new PathF();
			path.MoveTo(20, dirtyRect.Height - 40);
			path.CurveTo(dirtyRect.Width * 0.3f, 20, dirtyRect.Width * 0.6f, dirtyRect.Height - 20, dirtyRect.Width - 20, 60);
			path.LineTo(dirtyRect.Width - 20, dirtyRect.Height - 40);
			path.Close();
			canvas.FillColor = Colors.MediumPurple.WithAlpha(0.6f);
			canvas.FillPath(path);

			// Stroke + circle + text.
			canvas.StrokeColor = Colors.Aquamarine;
			canvas.StrokeSize = 4;
			canvas.DrawPath(path);
			canvas.FillColor = Colors.Orange;
			canvas.FillCircle(dirtyRect.Width - 60, 60, 26);
			canvas.FontColor = Colors.White;
			canvas.FontSize = 22;
			canvas.DrawString("IDrawable", 24, 34, HorizontalAlignment.Left);

			// A linear gradient via SetFillPaint and a clip rect cutting an oversized fill;
			// the shapes diagnostics leg checks both.
			var gradRect = new RectF(20, 60, dirtyRect.Width - 40, 60);
			var gradPaint = new LinearGradientPaint(
				new[] { new PaintGradientStop(0f, Colors.Red), new PaintGradientStop(1f, Colors.Yellow) },
				new Point(0, 0), new Point(1, 0));
			canvas.SetFillPaint(gradPaint, gradRect);
			canvas.FillRectangle(gradRect);
			canvas.SaveState();
			canvas.ClipRectangle(40, 140, 120, 80);
			canvas.FillColor = Colors.Lime;
			canvas.FillRectangle(0, 100, dirtyRect.Width, 200);   // oversized — must be cut
			canvas.RestoreState();
		}
	}
}
