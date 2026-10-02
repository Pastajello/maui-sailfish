using Microsoft.Maui.Graphics;
using Microsoft.Maui.SailfishOS.Platform;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace Microsoft.Maui.SailfishOS.SkiaSharp;

/// <summary>
/// The raster pipeline of SkiaSharp's views on Sailfish, after SkiaSharp's Android <c>SKCanvasView</c> and its
/// <c>SurfaceFactory</c>: a bitmap the size of the view in device pixels, reused while the size stays, drawn in the
/// frame after an invalidation and committed to the host's drawing surface. Qt thread only.
/// </summary>
internal sealed class SkiaSurfaceRenderer
{
	private readonly Action<SKSurface, SKImageInfo, SKImageInfo> _paint;
	private readonly Action<SKSizeI> _canvasSizeChanged;
	private readonly Func<bool> _ignorePixelScaling;
	private readonly Action _frame;
	private NativeElementHost? _host;
	private Func<bool> _isVisible = static () => true;
	private SKBitmap? _bitmap;
	private SKSizeI _canvasSize;   // last size reported to the view (Info.Size)
	private SKSizeI _pixelSize;    // the arranged size in pixels

	public SkiaSurfaceRenderer(Action<SKSurface, SKImageInfo, SKImageInfo> paint, Action<SKSizeI> canvasSizeChanged,
		Func<bool> ignorePixelScaling)
	{
		_paint = paint;
		_canvasSizeChanged = canvasSizeChanged;
		_ignorePixelScaling = ignorePixelScaling;
		_frame = Paint;
		Touch = new SkiaTouch(ignorePixelScaling);
	}

	public SkiaTouch Touch { get; }

	/// <summary>Paint again in every frame (SKGLView's render loop), not only after an invalidation.</summary>
	public bool RenderLoop
	{
		get => _renderLoop;
		set
		{
			_renderLoop = value;
			if (value)
				Invalidate();
		}
	}

	private bool _renderLoop;

	/// <summary>The pixel size of the arranged view.</summary>
	public SKSizeI PixelSize => _pixelSize;

	/// <summary>Paints that ran (diagnostics).</summary>
	public int Paints { get; private set; }

	/// <summary>Android's density: a float, as <c>DisplayMetrics.Density</c>.</summary>
	internal static float Density => (float)SailfishDisplay.Density;

	public void Connect(NativeElementHost host, Func<bool> isVisible)
	{
		_host = host;
		_isVisible = isVisible;
		host.Attached += OnAttached;
		Touch.Attach(host);
		if (host.IsAttached)
			Invalidate();
	}

	public void Disconnect()
	{
		if (_host is { } host)
		{
			host.Attached -= OnAttached;
			Touch.Detach();
			Release();
		}
		_host = null;
	}

	// A fresh QML object (first creation, navigation back, a recycled row) shows nothing and has touch off: as
	// Android's OnAttachedToWindow, paint again.
	private void OnAttached(NativeElementHost host)
	{
		Touch.Reapply();
		Invalidate();
	}

	/// <summary>The view's pixel rect follows MAUI Android's arrange (<c>ContextExtensions.ToPixels</c> per edge in
	/// the parent's space), so the bitmap has the size Android's view would have.</summary>
	public void Arrange(Rect frame)
	{
		var size = PixelSizeOf(frame, Density);
		if (size == _pixelSize)
			return;
		_pixelSize = size;
		Invalidate();   // Android's OnSizeChanged re-sizes the surface; the layout pass then redraws
	}

	internal static SKSizeI PixelSizeOf(Rect frame, float density) =>
		new(ToPixels(frame.Right, density) - ToPixels(frame.Left, density),
			ToPixels(frame.Bottom, density) - ToPixels(frame.Top, density));

	internal static int ToPixels(double dp, float density) => (int)Math.Ceiling(dp * density - 1.000000013351432E-10);

	/// <summary>Info's size: the pixels, or with IgnorePixelScaling the pixels over the float density, truncated
	/// (Android's SKCanvasView.OnDraw).</summary>
	internal static SKSizeI CanvasSizeOf(SKSizeI pixels, float density, bool ignorePixelScaling) =>
		ignorePixelScaling ? new SKSizeI((int)(pixels.Width / density), (int)(pixels.Height / density)) : pixels;

	public void OnVisibilityChanged()
	{
		if (_isVisible())
			Invalidate();
		else
			Release();
	}

	/// <summary>One paint in the next frame, however often it is asked.</summary>
	public void Invalidate()
	{
		if (_host is not null)
			QtHostSurface.RequestFrame(_frame);
	}

	private void Paint()
	{
		if (_host is not { IsAttached: true } host)
			return;
		if (!_isVisible())
		{
			Release();   // Android: OnDraw of a view that is not VISIBLE frees the bitmap
			return;
		}
		var (width, height) = (_pixelSize.Width, _pixelSize.Height);
		if (width <= 0 || height <= 0)
		{
			Release();
			return;
		}
		var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
		if (_bitmap is null || _bitmap.Width != width || _bitmap.Height != height)
		{
			_bitmap?.Dispose();
			_bitmap = new SKBitmap(info);
			_bitmap.Erase(SKColors.Transparent);   // a fresh Android Bitmap starts transparent
		}
		var ignore = _ignorePixelScaling();
		var density = Density;
		var userSize = CanvasSizeOf(info.Size, density, ignore);
		if (userSize != _canvasSize)
		{
			_canvasSize = userSize;
			_canvasSizeChanged(userSize);
		}
		Paints++;
		using (var surface = SKSurface.Create(info, _bitmap.GetPixels(), _bitmap.RowBytes))
		{
			if (ignore)
			{
				surface.Canvas.Scale(density);
				surface.Canvas.Save();
			}
			_paint(surface, info.WithSize(userSize), info);
			surface.Canvas.Flush();
		}
		QtHostSurface.Commit(host, _bitmap.GetPixels(), width, height, _bitmap.RowBytes);
		if (_renderLoop)
			Invalidate();
	}

	private void Release()
	{
		if (_host is { } host)
			QtHostSurface.Release(host);
		_bitmap?.Dispose();
		_bitmap = null;
	}
}

/// <summary>Touch of SkiaSharp's views on Sailfish, after SkiaSharp's Android <c>SKTouchHandler</c>.</summary>
internal sealed class SkiaTouch
{
	private readonly Func<bool> _ignorePixelScaling;
	private readonly Func<SurfaceTouch, bool> _onTouch;
	private NativeElementHost? _host;
	private bool _enabled;
	private Action<SKTouchEventArgs>? _sink;

	public SkiaTouch(Func<bool> ignorePixelScaling)
	{
		_ignorePixelScaling = ignorePixelScaling;
		_onTouch = OnTouch;
	}

	public void Update(bool enabled, Action<SKTouchEventArgs> sink)
	{
		_enabled = enabled;
		_sink = sink;
		Apply();
	}

	public void Attach(NativeElementHost host)
	{
		_host = host;
		Apply();
	}

	public void Reapply() => Apply();

	public void Detach()
	{
		if (_host is { } host)
			QtHostSurface.SetTouch(host, null);
		_host = null;
	}

	private void Apply()
	{
		if (_host is { } host)
			QtHostSurface.SetTouch(host, _enabled ? _onTouch : null);
	}

	internal bool OnTouch(SurfaceTouch touch)
	{
		if (_sink is not { } sink)
			return false;
		double x = touch.X, y = touch.Y;
		if (_ignorePixelScaling())
		{
			// Android's FromPixels: pixels / density.
			x /= SkiaSurfaceRenderer.Density;
			y /= SkiaSurfaceRenderer.Density;
		}
		var action = touch.Action switch
		{
			SurfaceTouchAction.Pressed => SKTouchAction.Pressed,
			SurfaceTouchAction.Moved => SKTouchAction.Moved,
			SurfaceTouchAction.Released => SKTouchAction.Released,
			_ => SKTouchAction.Cancelled,
		};
		var button = !touch.IsMouse ? SKMouseButton.Left
			: touch.MouseButton switch { 1 => SKMouseButton.Middle, 2 => SKMouseButton.Right, _ => SKMouseButton.Left };
		var args = new SKTouchEventArgs(touch.PointerId, action, button,
			touch.IsMouse ? SKTouchDeviceType.Mouse : SKTouchDeviceType.Touch,
			new SKPoint((float)x, (float)y), inContact: action is SKTouchAction.Pressed or SKTouchAction.Moved,
			wheelDelta: 0, pressure: (float)touch.Pressure);
		sink(args);
		return args.Handled;
	}
}
