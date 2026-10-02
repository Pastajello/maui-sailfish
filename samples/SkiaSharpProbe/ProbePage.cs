using System.Diagnostics;
using System.Text.Json;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace SkiaSharpProbe;

/// <summary>
/// SkiaSharp scenes with known pixels and touch targets. With MAUI_SAILFISH_SKIA_DIAG=render they check rendering
/// (sizes, pixels 1:1, background and alpha, invalidation, visibility, SKGLView, image sources, tiles, frame rate);
/// with =input they check touch through Qt's real input path (injected touches). Each check prints a line, the run
/// prints one ACCEPTANCE verdict (tools/sf matrix skia / skiainput), and MAUI_SAILFISH_QT_HOST_AUTO_SHUTDOWN=1 quits.
/// </summary>
public class ProbePage : ContentPage
{
	private readonly SKCanvasView _bars = new() { WidthRequest = 300.3, HeightRequest = 120.7 };
	private readonly SKCanvasView _transparent = new() { WidthRequest = 200, HeightRequest = 50, BackgroundColor = Color.FromRgb(255, 255, 0) };
	private readonly SKCanvasView _rotated = new() { WidthRequest = 160, HeightRequest = 50, Rotation = 12 };
	private readonly SKCanvasView _touch = new() { WidthRequest = 300, HeightRequest = 150, EnableTouchEvents = true };
	private readonly SKCanvasView _unhandled = new() { WidthRequest = 300, HeightRequest = 150, EnableTouchEvents = true };
	private readonly SKCanvasView _hidden = new() { WidthRequest = 100, HeightRequest = 40 };
	private readonly SKCanvasView _anim = new() { HeightRequest = 400 };
	private readonly SKCanvasView _big = new() { HeightRequest = 2600 };
	private readonly SKGLView _gl = new() { WidthRequest = 200, HeightRequest = 60 };
	private readonly Image _image = new() { WidthRequest = 60, HeightRequest = 30 };
	private int _glPaints;
	private readonly SKCanvasView _disabled = new() { WidthRequest = 300, HeightRequest = 100, EnableTouchEvents = true, IsEnabled = false };
	private readonly List<SKTouchEventArgs> _disabledTouches = new();
	private static readonly string? Mode = Environment.GetEnvironmentVariable("MAUI_SAILFISH_SKIA_DIAG");
	private static string Marker => Mode == "input" ? "Qt skiainput diag" : "Qt skia diag";
	private readonly ScrollView _scroll;
	private readonly List<string> _results = new();
	private readonly List<SKTouchEventArgs> _touches = new();
	private readonly List<SKTouchEventArgs> _unhandledTouches = new();
	private readonly List<SKPoint> _dots = new();
	private int _barsPaints, _hiddenPaints, _animPaints;
	private bool _animating;
	private bool _started;

	public ProbePage()
	{
		Title = "SkiaProbe";
		_bars.PaintSurface += (_, e) =>
		{
			_barsPaints++;
			Log($"bars paint #{_barsPaints} info={e.Info.Width}x{e.Info.Height} {e.Info.ColorType}/{e.Info.AlphaType} raw={e.RawInfo.Width}x{e.RawInfo.Height}");
			DrawBars(e.Surface.Canvas, e.Info);
		};
		_transparent.PaintSurface += (_, e) =>
		{
			e.Surface.Canvas.Clear(SKColors.Transparent);
			using var paint = new SKPaint { Color = new SKColor(0, 0, 255, 128), IsAntialias = true };
			e.Surface.Canvas.DrawCircle(e.Info.Width / 2f, e.Info.Height / 2f, e.Info.Height / 3f, paint);
		};
		_rotated.PaintSurface += (_, e) => DrawBars(e.Surface.Canvas, e.Info);
		_touch.PaintSurface += (_, e) =>
		{
			e.Surface.Canvas.Clear(new SKColor(30, 30, 60));
			using var paint = new SKPaint { Color = SKColors.Orange, IsAntialias = true };
			foreach (var dot in _dots)
				e.Surface.Canvas.DrawCircle(dot, 12, paint);
		};
		_touch.Touch += (_, e) =>
		{
			_touches.Add(e);
			if (e.ActionType is SKTouchAction.Pressed or SKTouchAction.Moved)
				_dots.Add(e.Location);
			e.Handled = true;
			_touch.InvalidateSurface();
		};
		_unhandled.PaintSurface += (_, e) => e.Surface.Canvas.Clear(new SKColor(60, 30, 30));
		_unhandled.Touch += (_, e) => _unhandledTouches.Add(e);   // Handled stays false
		_disabled.PaintSurface += (_, e) => e.Surface.Canvas.Clear(new SKColor(60, 60, 60));
		_disabled.Touch += (_, e) =>
		{
			_disabledTouches.Add(e);
			e.Handled = true;
		};
		_hidden.PaintSurface += (_, e) =>
		{
			_hiddenPaints++;
			e.Surface.Canvas.Clear(SKColors.Green);
		};
		_anim.PaintSurface += (_, e) =>
		{
			_animPaints++;
			var hue = _animPaints * 3 % 360;
			e.Surface.Canvas.Clear(SKColor.FromHsv(hue, 80, 90));
			using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
			var x = _animPaints * 7 % Math.Max(1, e.Info.Width);
			e.Surface.Canvas.DrawCircle(x, e.Info.Height / 2f, 40, paint);
			if (_animating)
				_anim.InvalidateSurface();
		};
		_big.PaintSurface += (_, e) =>
		{
			e.Surface.Canvas.Clear(SKColors.DarkSlateGray);
			using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
			using var font = new SKFont { Size = 48 };
			for (var y = 0; y < e.Info.Height; y += 400)
				e.Surface.Canvas.DrawText($"y={y}", 20, y + 60, SKTextAlign.Left, font, paint);
		};

		_gl.PaintSurface += (_, e) =>
		{
			_glPaints++;
			e.Surface.Canvas.Clear(_glPaints % 2 == 0 ? SKColors.Teal : SKColors.Purple);
		};
		var red = new SKBitmap(8, 8);
		red.Erase(SKColors.Red);
		_image.Source = new SKBitmapImageSource { Bitmap = red };

		_scroll = new ScrollView
		{
			Content = new VerticalStackLayout
			{
				Spacing = 12,
				Padding = 10,
				Children =
				{
					new Label { Text = "bars / transparent / rotated / gl / image" },
					_bars, _transparent, _rotated,
					new HorizontalStackLayout { Spacing = 10, Children = { _gl, _image } },
					new Label { Text = "touch (handled) / unhandled" },
					_touch, _unhandled, _disabled, _hidden,
					new Label { Text = "animation / big" },
					_anim, _big,
				},
			},
		};
		Content = _scroll;
	}

	private static void Log(string text) => Console.Error.WriteLine($"[SkiaSharpProbe] {text}");

	private void Check(string name, bool ok, string detail)
	{
		_results.Add($"{(ok ? "OK  " : "FAIL")} {name}");
		Log($"{Marker}: CHECK {(ok ? "OK" : "FAIL")} {name} — {detail}");
	}

	private static void DrawBars(SKCanvas canvas, SKImageInfo info)
	{
		canvas.Clear(SKColors.White);
		var colors = new[] { SKColors.Red, SKColors.Lime, SKColors.Blue, SKColors.Cyan, SKColors.Magenta, SKColors.Yellow };
		var half = info.Height / 2;
		using var paint = new SKPaint();
		for (var i = 0; i < colors.Length; i++)
		{
			paint.Color = colors[i];
			canvas.DrawRect(SKRect.Create(i * info.Width / (float)colors.Length, 0, info.Width / (float)colors.Length, half), paint);
		}
		for (var x = 0; x < info.Width; x++)
		{
			paint.Color = new SKColor(0, 0, 0, (byte)(x * 255 / Math.Max(1, info.Width - 1)));
			canvas.DrawRect(SKRect.Create(x, half, 1, info.Height - half), paint);
		}
		paint.Color = SKColors.Red;
		canvas.DrawRect(SKRect.Create(0, 0, 1, info.Height), paint);
		paint.Color = SKColors.Blue;
		canvas.DrawRect(SKRect.Create(info.Width - 1, 0, 1, info.Height), paint);
		paint.Color = SKColors.Lime;
		canvas.DrawRect(SKRect.Create(0, 0, info.Width, 1), paint);
		paint.Color = SKColors.Magenta;
		canvas.DrawRect(SKRect.Create(0, info.Height - 1, info.Width, 1), paint);
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		if (_started || Mode is not ("render" or "input"))
			return;
		_started = true;
		await Task.Delay(3000);
		try
		{
			if (Mode == "render")
				await RenderChecks();
			else
				await TouchChecks();
		}
		catch (Exception ex)
		{
			Check("no exception", false, ex.ToString());
		}
		var failed = _results.Count(r => r.StartsWith("FAIL", StringComparison.Ordinal));
		Log($"{Marker}: ACCEPTANCE checks={_results.Count} failed={failed} => {(failed == 0 ? "OK" : "FAIL")} — " +
		    (Mode == "render"
			    ? "SkiaSharp views draw through the Sailfish drawing surface as on Android (sizes, pixels 1:1, SKGLView, image sources)"
			    : "SkiaSharp touch follows Android's SKTouchHandler through Qt's input path (ids, Handled, parent interception)"));
		if (Environment.GetEnvironmentVariable("MAUI_SAILFISH_QT_HOST_AUTO_SHUTDOWN") == "1")
		{
			await Task.Delay(500);
			QtHostRuntime.Shutdown();
		}
	}

	private static NativeElementHost Host(View view) => (NativeElementHost)view.Handler!.PlatformView!;

	private static NativeGeometry Geometry(View view)
	{
		QtHostRuntime.TryItemGeometry(Host(view).NativeHandle, out var g);
		return g;
	}

	private static int ToPixels(double dp) => (int)Math.Ceiling(dp * (float)Microsoft.Maui.SailfishOS.Platform.SailfishDisplay.Density - 1.000000013351432E-10);

	private async Task RenderChecks()
	{
		var density = (float)Microsoft.Maui.SailfishOS.Platform.SailfishDisplay.Density;
		Log($"orientation={Microsoft.Maui.SailfishOS.Platform.SailfishDisplay.Orientation} density={density} handler={_bars.Handler?.GetType().FullName}");
		Check("handler", _bars.Handler?.GetType().Name == "SailfishSKCanvasViewHandler", _bars.Handler?.GetType().FullName ?? "null");

		var f = _bars.Frame;
		var px = (ToPixels(f.Right) - ToPixels(f.Left), ToPixels(f.Bottom) - ToPixels(f.Top));
		Check("canvas size = Android px", _bars.CanvasSize == new SKSize(px.Item1, px.Item2),
			$"frame={f} canvasSize={_bars.CanvasSize} expected={px}");

		await PixelChecks(px.Item1, px.Item2);

		// Coalescing: five invalidations, one paint.
		var before = _barsPaints;
		for (var i = 0; i < 5; i++)
			_bars.InvalidateSurface();
		await Task.Delay(300);
		Check("invalidate coalesces", _barsPaints - before == 1, $"paints={_barsPaints - before}");

		// Hidden: no paint; shown: paints.
		var hiddenBefore = _hiddenPaints;
		_hidden.IsVisible = false;
		await Task.Delay(200);
		_hidden.InvalidateSurface();
		await Task.Delay(200);
		var whileHidden = _hiddenPaints - hiddenBefore;
		_hidden.IsVisible = true;
		await Task.Delay(300);
		Check("hidden does not paint, shown paints", whileHidden == 0 && _hiddenPaints > hiddenBefore,
			$"whileHidden={whileHidden} after={_hiddenPaints - hiddenBefore}");

		await GlAndImageChecks();
		await AnimationCheck();
		// Taller than the GPU's texture limit, so the surface must tile.
		var maxTexture = JsonDocument.Parse(QtHostRuntime.PerfStats()).RootElement.GetProperty("surfaceMaxTexture").GetInt64();
		_big.HeightRequest = (maxTexture + 300) / Microsoft.Maui.SailfishOS.Platform.SailfishDisplay.Density;
		await Task.Delay(1500);
		var perf = JsonDocument.Parse(QtHostRuntime.PerfStats()).RootElement;
		var tiles = perf.GetProperty("surfaceTiles").GetInt64();
		Check("big canvas is tiled past the texture limit", tiles >= 2, $"maxTexture={maxTexture} surfaceTiles={tiles} bigPx={_big.CanvasSize}");
	}

	private async Task PixelChecks(int width, int height)
	{
		var path = Path.Combine(FileSystem.CacheDirectory, "skiaprobe-grab.png");
		var rc = QtHostRuntime.GrabPng(path);
		using var grab = rc == 0 ? SKBitmap.Decode(path) : null;
		if (grab is null)
		{
			Check("grab", false, $"rc={rc}");
			return;
		}
		var g = Geometry(_bars);
		var ox = (int)Math.Round(g.X);
		var oy = (int)Math.Round(g.Y);
		Log($"grab {grab.Width}x{grab.Height} bars item at {g} -> origin {ox},{oy}");

		using var reference = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
		using (var canvas = new SKCanvas(reference))
			DrawBars(canvas, reference.Info);

		var (bad, maxDiff, first) = Compare(grab, reference, ox, oy);
		var detail = $"mismatched={bad}/{width * height} maxDiff={maxDiff} first={first}";
		if (bad > 0)
			foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
				detail += $" | shift({dx},{dy})={Compare(grab, reference, ox + dx, oy + dy).Bad}";
		Check("pixels on screen = the bitmap (1:1, exact)", bad == 0, detail);

		var gt = Geometry(_transparent);
		var corner = grab.GetPixel((int)Math.Round(gt.X) + 2, (int)Math.Round(gt.Y) + 2);
		var center = grab.GetPixel((int)Math.Round(gt.X + gt.Width / 2), (int)Math.Round(gt.Y + gt.Height / 2));
		Check("background shows under a transparent canvas", corner == new SKColor(255, 255, 0),
			$"corner={corner} center={center} (expect yellow, and ~half blue over yellow in the center)");
		Check("premultiplied alpha blends over the background",
			Math.Abs(center.Red - 127) <= 2 && Math.Abs(center.Green - 127) <= 2 && Math.Abs(center.Blue - 128) <= 2,
			$"center={center}");
		await Task.CompletedTask;
	}

	private async Task GlAndImageChecks()
	{
		var start = _glPaints;
		_gl.HasRenderLoop = true;
		await Task.Delay(2000);
		_gl.HasRenderLoop = false;
		var looped = _glPaints - start;
		await Task.Delay(300);
		var stopped = _glPaints;
		await Task.Delay(500);
		Check("SKGLView render loop paints every frame and stops", looped >= 60 && _glPaints == stopped,
			$"paintsIn2s={looped} afterStop={_glPaints - stopped} grContext={_gl.GRContext?.ToString() ?? "null"}");

		var path = Path.Combine(FileSystem.CacheDirectory, "skiaprobe-image.png");
		SKColor ImagePixel()
		{
			QtHostRuntime.GrabPng(path);
			using var grab = SKBitmap.Decode(path);
			var g = Geometry(_image);
			return grab.GetPixel((int)(g.X + g.Width / 2), (int)(g.Y + g.Height / 2));
		}
		var first = ImagePixel();
		var blue = new SKBitmap(8, 8);
		blue.Erase(SKColors.Blue);
		((SKBitmapImageSource)_image.Source).Bitmap = blue;
		await Task.Delay(800);
		var second = ImagePixel();
		Check("SKBitmapImageSource shows in an Image and reloads when its bitmap changes",
			first == SKColors.Red && second == SKColors.Blue, $"first={first} second={second}");
	}

	private static (int Bad, int MaxDiff, string First) Compare(SKBitmap grab, SKBitmap reference, int ox, int oy)
	{
		int bad = 0, maxDiff = 0;
		var first = "-";
		for (var y = 0; y < reference.Height; y++)
		for (var x = 0; x < reference.Width; x++)
		{
			var gx = ox + x;
			var gy = oy + y;
			var r = reference.GetPixel(x, y);
			var s = gx >= 0 && gy >= 0 && gx < grab.Width && gy < grab.Height ? grab.GetPixel(gx, gy) : SKColors.Empty;
			var d = Math.Max(Math.Max(Math.Abs(r.Red - s.Red), Math.Abs(r.Green - s.Green)), Math.Abs(r.Blue - s.Blue));
			if (d > 0)
			{
				if (bad == 0)
					first = $"({x},{y}) ref={r} screen={s}";
				bad++;
				maxDiff = Math.Max(maxDiff, d);
			}
		}
		return (bad, maxDiff, first);
	}

	private async Task AnimationCheck()
	{
		var perf0 = JsonDocument.Parse(QtHostRuntime.PerfStats()).RootElement;
		var paints0 = _animPaints;
		var sw = Stopwatch.StartNew();
		_animating = true;
		_anim.InvalidateSurface();
		await Task.Delay(3000);
		_animating = false;
		var seconds = sw.Elapsed.TotalSeconds;
		var perf1 = JsonDocument.Parse(QtHostRuntime.PerfStats()).RootElement;
		long D(string k) => perf1.GetProperty(k).GetInt64() - perf0.GetProperty(k).GetInt64();
		var fps = (_animPaints - paints0) / seconds;
		var uploads = Math.Max(1, D("surfaceUploads"));
		var commits = Math.Max(1, D("surfaceCommits"));
		Log($"PERF anim canvas {_anim.CanvasSize} fps={fps:F1} commitAvgUs={D("surfaceCommitUs") / commits} " +
		    $"uploadAvgUs={D("surfaceUploadUs") / uploads} uploadMaxUs={perf1.GetProperty("surfaceUploadMaxUs").GetInt64()} " +
		    $"renderLoop={perf1.GetProperty("renderLoop").GetString()}");
		Check("continuous invalidation animates", fps >= 30, $"fps={fps:F1}");
	}

	private static void Inject(params (int Id, double X, double Y, int State)[] points) =>
		QtHostRuntime.InjectTouch(points.Select(p => p.Id).ToArray(),
			points.SelectMany(p => new[] { p.X, p.Y }).ToArray(), points.Select(p => p.State).ToArray());

	private async Task TouchChecks()
	{
		const int Pressed = 1, Moved = 2, Stationary = 4, Released = 8;
		var g = Geometry(_touch);
		double cx = g.X + g.Width / 2, cy = g.Y + g.Height / 2;

		// One finger: press, move, release.
		_touches.Clear();
		Inject((101, cx, cy, Pressed));
		await Task.Delay(50);
		Inject((101, cx + 10, cy + 2, Moved));
		await Task.Delay(50);
		Inject((101, cx + 10, cy + 2, Released));
		await Task.Delay(100);
		var seq = string.Join(",", _touches.Select(t => $"{t.ActionType}#{t.Id}@{t.Location.X:F0},{t.Location.Y:F0}/{t.InContact}"));
		var p0 = _touches.FirstOrDefault();
		Check("one finger: Pressed, Moved, Released, id 0, px location",
			_touches.Select(t => t.ActionType).SequenceEqual(new[] { SKTouchAction.Pressed, SKTouchAction.Moved, SKTouchAction.Released })
			&& _touches.All(t => t.Id == 0 && t.DeviceType == SKTouchDeviceType.Touch)
			&& p0 is not null && Math.Abs(p0.Location.X - g.Width / 2) <= 1 && Math.Abs(p0.Location.Y - g.Height / 2) <= 1,
			seq);

		// Two fingers.
		_touches.Clear();
		Inject((201, cx - 40, cy, Pressed));
		await Task.Delay(40);
		Inject((201, cx - 40, cy, Stationary), (202, cx + 40, cy, Pressed));
		await Task.Delay(40);
		Inject((201, cx - 45, cy, Moved), (202, cx + 45, cy, Moved));
		await Task.Delay(40);
		Inject((201, cx - 45, cy, Released), (202, cx + 45, cy, Stationary));
		await Task.Delay(40);
		Inject((202, cx + 45, cy, Released));
		await Task.Delay(100);
		seq = string.Join(",", _touches.Select(t => $"{t.ActionType}#{t.Id}"));
		Check("two fingers: ids 0 and 1, a Moved per pointer", seq == "Pressed#0,Pressed#1,Moved#0,Moved#1,Released#0,Released#1", seq);

		// Unhandled press in the page ScrollView: the scroll view takes the drag.
		var gu = Geometry(_unhandled);
		double ux = gu.X + gu.Width / 2, uy = gu.Y + gu.Height / 2;
		_unhandledTouches.Clear();
		var scroll0 = _scroll.ScrollY;
		Inject((301, ux, uy, Pressed));
		for (var i = 1; i <= 12; i++)
		{
			await Task.Delay(16);
			Inject((301, ux, uy - i * 25, Moved));
		}
		await Task.Delay(16);
		Inject((301, ux, uy - 300, Released));
		await Task.Delay(1200);
		var scrolled = _scroll.ScrollY - scroll0;
		Check("unhandled press: the parent scrolls, the canvas gets only Pressed",
			scrolled > 50 && _unhandledTouches.Select(t => t.ActionType).SequenceEqual(new[] { SKTouchAction.Pressed }),
			$"scrolledDp={scrolled:F0} touches={string.Join(",", _unhandledTouches.Select(t => t.ActionType))}");
		await _scroll.ScrollToAsync(0, 0, false);
		await Task.Delay(500);

		// Handled press, then a vertical drag: the flickable takes over and the canvas gets Cancelled (Android's
		// ScrollView interception), or the canvas keeps it; report which.
		g = Geometry(_touch);
		cx = g.X + g.Width / 2;
		cy = g.Y + g.Height / 2;
		_touches.Clear();
		scroll0 = _scroll.ScrollY;
		Inject((401, cx, cy, Pressed));
		for (var i = 1; i <= 12; i++)
		{
			await Task.Delay(16);
			Inject((401, cx, cy - i * 25, Moved));
		}
		await Task.Delay(16);
		Inject((401, cx, cy - 300, Released));
		await Task.Delay(1200);
		var actions = string.Join(",", _touches.Select(t => t.ActionType).Distinct());
		Check("handled press + vertical drag: the scroll view takes it, the canvas gets Cancelled",
			_touches.Any(t => t.ActionType == SKTouchAction.Cancelled) && _scroll.ScrollY - scroll0 > 50,
			$"actions={actions} scrolledDp={_scroll.ScrollY - scroll0:F0}");
		await _scroll.ScrollToAsync(0, 0, false);
		await Task.Delay(500);

		// A disabled canvas gets no touch (Android calls no touch listener on a disabled view); its drag scrolls.
		var gd = Geometry(_disabled);
		double dx = gd.X + gd.Width / 2, dy = gd.Y + gd.Height / 2;
		scroll0 = _scroll.ScrollY;
		Inject((501, dx, dy, Pressed));
		for (var i = 1; i <= 12; i++)
		{
			await Task.Delay(16);
			Inject((501, dx, dy - i * 25, Moved));
		}
		await Task.Delay(16);
		Inject((501, dx, dy - 300, Released));
		await Task.Delay(1200);
		Check("disabled canvas: no touch events, the parent scrolls",
			_disabledTouches.Count == 0 && _scroll.ScrollY - scroll0 > 50,
			$"touches={_disabledTouches.Count} scrolledDp={_scroll.ScrollY - scroll0:F0}");
		await _scroll.ScrollToAsync(0, 0, false);
	}
}
