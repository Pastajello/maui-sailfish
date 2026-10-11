using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
using Trailhead.Data;
using Trailhead.Features.Shared;

namespace Trailhead.Features.Charts;

/// <summary>
/// The elevation profile of a track over distance (SkiaSharp): a filled area in the ambience's highlight colour, the
/// lowest and highest point marked. A finger on it scrubs: a vertical line and "3.2 km · 1,450 m" follow it, and
/// <see cref="Scrubbed"/> reports the fix under the finger (null when it lifts).
/// </summary>
public sealed class ElevationChart : SKCanvasView
{
	public static readonly BindableProperty TrackProperty = BindableProperty.Create(nameof(Track), typeof(IReadOnlyList<TrackPoint>),
		typeof(ElevationChart), propertyChanged: (b, _, _) => ((ElevationChart)b).Rebuild());

	public static readonly BindableProperty ColorProperty = BindableProperty.Create(nameof(Color), typeof(Color),
		typeof(ElevationChart), Colors.SkyBlue, propertyChanged: (b, _, _) => ((ElevationChart)b).InvalidateSurface());

	public static readonly BindableProperty TextColorProperty = BindableProperty.Create(nameof(TextColor), typeof(Color),
		typeof(ElevationChart), Colors.White, propertyChanged: (b, _, _) => ((ElevationChart)b).InvalidateSurface());

	private double[] _distance = Array.Empty<double>();
	private int _scrub = -1;

	public ElevationChart()
	{
		EnableTouchEvents = true;
		HeightRequest = 150;
	}

	public IReadOnlyList<TrackPoint>? Track
	{
		get => (IReadOnlyList<TrackPoint>?)GetValue(TrackProperty);
		set => SetValue(TrackProperty, value);
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

	/// <summary>The fix under the finger while scrubbing; null when the finger lifts.</summary>
	public event EventHandler<TrackPoint?>? Scrubbed;

	/// <summary>The index of the scrubbed fix, -1 when none (tour checks).</summary>
	public int ScrubIndex => _scrub;

	/// <summary>Touch events received, by action (tour checks).</summary>
	public Dictionary<SKTouchAction, int> TouchCounts { get; } = new();

	private void Rebuild()
	{
		var track = Track;
		_distance = new double[track?.Count ?? 0];
		for (var i = 1; i < _distance.Length; i++)
			_distance[i] = _distance[i - 1] + Geo.DistanceM(track![i - 1], track[i]);
		_scrub = -1;
		InvalidateSurface();
	}

	protected override void OnTouch(SKTouchEventArgs e)
	{
		TouchCounts[e.ActionType] = TouchCounts.GetValueOrDefault(e.ActionType) + 1;
		if (Track is not { Count: > 1 } track || _distance.Length != track.Count)
			return;
		switch (e.ActionType)
		{
			case SKTouchAction.Pressed:
			case SKTouchAction.Moved:
				var total = _distance[^1];
				var plot = PlotRect(CanvasSize.Width, CanvasSize.Height, PixelScale());
				var at = Math.Clamp((e.Location.X - plot.Left) / plot.Width, 0, 1) * total;
				var index = Array.BinarySearch(_distance, at);
				_scrub = index >= 0 ? index : Math.Min(~index, _distance.Length - 1);
				Scrubbed?.Invoke(this, track[_scrub]);
				e.Handled = true;
				break;
			case SKTouchAction.Released:
			case SKTouchAction.Cancelled:
				_scrub = -1;
				Scrubbed?.Invoke(this, null);
				e.Handled = true;
				break;
		}
		InvalidateSurface();
	}

	private float PixelScale() => Width > 0 ? (float)(CanvasSize.Width / Width) : 1;

	private static SKRect PlotRect(float width, float height, float scale) =>
		new(44 * scale, 12 * scale, width - 8 * scale, height - 22 * scale);

	protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
	{
		var canvas = e.Surface.Canvas;
		canvas.Clear(SKColors.Transparent);
		if (Track is not { Count: > 1 } track || _distance.Length != track.Count)
			return;

		var scale = (float)(e.Info.Width / Math.Max(1, Width));
		var plot = PlotRect(e.Info.Width, e.Info.Height, scale);
		double min = track.Min(p => p.Ele), max = track.Max(p => p.Ele);
		if (max - min < 20)
			max = min + 20;
		var total = _distance[^1];
		SKPoint At(int i) => new(plot.Left + (float)(_distance[i] / total) * plot.Width,
			plot.Bottom - (float)((track[i].Ele - min) / (max - min)) * plot.Height);

		var color = Color.ToSKColor();
		using var area = new SKPath();
		area.MoveTo(plot.Left, plot.Bottom);
		var step = Math.Max(1, track.Count / (int)Math.Max(1, plot.Width / 2));
		for (var i = 0; i < track.Count; i += step)
			area.LineTo(At(i));
		area.LineTo(At(track.Count - 1));
		area.LineTo(plot.Right, plot.Bottom);
		area.Close();
		using (var fill = new SKPaint
		{
			IsAntialias = true,
			Shader = SKShader.CreateLinearGradient(new SKPoint(0, plot.Top), new SKPoint(0, plot.Bottom),
				new[] { color.WithAlpha(170), color.WithAlpha(25) }, SKShaderTileMode.Clamp),
		})
			canvas.DrawPath(area, fill);
		using (var line = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2 * scale, Color = color })
		{
			using var top = new SKPath();
			top.MoveTo(At(0));
			for (var i = step; i < track.Count; i += step)
				top.LineTo(At(i));
			top.LineTo(At(track.Count - 1));
			canvas.DrawPath(top, line);
		}

		var text = TextColor.ToSKColor();
		using var font = new SKFont(SKTypeface.Default, 12 * scale);
		using var ink = new SKPaint { IsAntialias = true, Color = text.WithAlpha(170) };
		canvas.DrawText(Format.Height(max), 2 * scale, plot.Top + 10 * scale, SKTextAlign.Left, font, ink);
		canvas.DrawText(Format.Height(min), 2 * scale, plot.Bottom, SKTextAlign.Left, font, ink);
		canvas.DrawText(Format.Distance(total), plot.Right, e.Info.Height - 4 * scale, SKTextAlign.Right, font, ink);
		canvas.DrawText("0", plot.Left, e.Info.Height - 4 * scale, SKTextAlign.Left, font, ink);

		if (_scrub >= 0 && _scrub < track.Count)
		{
			var p = At(_scrub);
			using var cursor = new SKPaint { IsAntialias = true, Color = text, StrokeWidth = 1.5f * scale };
			canvas.DrawLine(p.X, plot.Top, p.X, plot.Bottom, cursor);
			canvas.DrawCircle(p, 5 * scale, cursor);
			var label = $"{Format.Distance(_distance[_scrub])} · {Format.Height(track[_scrub].Ele)}";
			using var bold = new SKFont(SKTypeface.FromFamilyName(null, SKFontStyle.Bold), 14 * scale);
			var width = bold.MeasureText(label);
			var x = Math.Clamp(p.X - width / 2, plot.Left, plot.Right - width);
			using var back = new SKPaint { Color = SKColors.Black.WithAlpha(150), IsAntialias = true };
			canvas.DrawRoundRect(new SKRect(x - 6 * scale, plot.Top, x + width + 6 * scale, plot.Top + 20 * scale), 4 * scale, 4 * scale, back);
			canvas.DrawText(label, x, plot.Top + 15 * scale, SKTextAlign.Left, bold, cursor);
		}
	}
}
