using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace Trailhead.Features.Charts;

public sealed record Bar(string Label, double Value);

/// <summary>
/// Vertical bars (SkiaSharp) that grow in from zero when their values arrive: <see cref="Progress"/> runs 0 → 1 with
/// a MAUI <see cref="Animation"/> and every frame redraws, so the drawing itself is animated, not a view.
/// </summary>
public sealed class BarChart : SKCanvasView
{
	public static readonly BindableProperty BarsProperty = BindableProperty.Create(nameof(Bars), typeof(IReadOnlyList<Bar>),
		typeof(BarChart), propertyChanged: (b, _, _) => ((BarChart)b).Grow());

	public static readonly BindableProperty ColorProperty = BindableProperty.Create(nameof(Color), typeof(Color),
		typeof(BarChart), Colors.SkyBlue, propertyChanged: (b, _, _) => ((BarChart)b).InvalidateSurface());

	public static readonly BindableProperty TextColorProperty = BindableProperty.Create(nameof(TextColor), typeof(Color),
		typeof(BarChart), Colors.White, propertyChanged: (b, _, _) => ((BarChart)b).InvalidateSurface());

	private double _progress = 1;

	public BarChart()
	{
		HeightRequest = 180;
	}

	public IReadOnlyList<Bar>? Bars
	{
		get => (IReadOnlyList<Bar>?)GetValue(BarsProperty);
		set => SetValue(BarsProperty, value);
	}

	public Color Color
	{
		get => (Color)GetValue(ColorProperty);
		set => SetValue(ColorProperty, value);
	}

	public Color TextColor
	{
		get => (Color)GetValue(TextColorProperty);
		set => SetValue(TextColorProperty, value);
	}

	/// <summary>How far the bars have grown, 0…1.</summary>
	public double Progress => _progress;

	public Func<double, string> ValueFormat { get; set; } = v => v.ToString("0");

	private void Grow()
	{
		this.AbortAnimation("grow");
		new Animation(v =>
		{
			_progress = v;
			InvalidateSurface();
		}, 0, 1, Easing.CubicOut).Commit(this, "grow", length: 700);
	}

	protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
	{
		var canvas = e.Surface.Canvas;
		canvas.Clear(SKColors.Transparent);
		if (Bars is not { Count: > 0 } bars)
			return;
		var scale = (float)(e.Info.Width / Math.Max(1, Width));
		var max = Math.Max(1e-9, bars.Max(b => b.Value));
		var labelH = 18 * scale;
		var valueH = 16 * scale;
		var slot = e.Info.Width / (float)bars.Count;
		var barW = slot * 0.62f;
		var bottom = e.Info.Height - labelH;
		var height = bottom - valueH;
		var color = Color.ToSKColor();
		var text = TextColor.ToSKColor();
		using var fill = new SKPaint { IsAntialias = true, Color = color };
		using var font = new SKFont(SKTypeface.Default, 11 * scale);
		using var ink = new SKPaint { IsAntialias = true, Color = text.WithAlpha(170) };
		using var valueInk = new SKPaint { IsAntialias = true, Color = text };
		for (var i = 0; i < bars.Count; i++)
		{
			var x = i * slot + (slot - barW) / 2;
			var h = (float)(bars[i].Value / max * height * _progress);
			canvas.DrawRoundRect(new SKRect(x, bottom - h, x + barW, bottom), 3 * scale, 3 * scale, fill);
			canvas.DrawText(bars[i].Label, x + barW / 2, e.Info.Height - 4 * scale, SKTextAlign.Center, font, ink);
			if (bars[i].Value > 0 && _progress > 0.95)
				canvas.DrawText(ValueFormat(bars[i].Value), x + barW / 2, bottom - h - 4 * scale, SKTextAlign.Center, font, valueInk);
		}
	}
}
