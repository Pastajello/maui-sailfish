using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.SailfishOS.Handlers;
using Microsoft.Maui.SailfishOS.Platform;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace Microsoft.Maui.SailfishOS.SkiaSharp;

/// <summary>
/// Sailfish handler of <see cref="ISKCanvasView"/>, the counterpart of SkiaSharp's Android
/// <c>SKCanvasViewHandler</c> + <c>SkiaSharp.Views.Android.SKCanvasView</c>. The app draws into a reused
/// RGBA8888-premultiplied bitmap the size of the view in device pixels, which is committed to the host's drawing
/// surface (<see cref="QtHostSurface"/>). Touch follows SkiaSharp's Android <c>SKTouchHandler</c>.
/// </summary>
/// <remarks>
/// <para>Apps extend <see cref="Mapper"/> and <see cref="CommandMapper"/> here; SkiaSharp's own
/// <c>SKCanvasViewHandler.SKCanvasViewMapper</c> belongs to its handler type, which does not run on Sailfish.</para>
/// </remarks>
public class SailfishSKCanvasViewHandler : SailfishViewHandler<ISKCanvasView>
{
	public static readonly PropertyMapper<ISKCanvasView, SailfishSKCanvasViewHandler> Mapper = BuildMapper();

	public static readonly CommandMapper<ISKCanvasView, SailfishSKCanvasViewHandler> CommandMapper = new(ViewCommandMapper)
	{
		[nameof(ISKCanvasView.InvalidateSurface)] = OnInvalidateSurface,
	};

	private readonly SkiaSurfaceRenderer _renderer;

	public SailfishSKCanvasViewHandler() : this(Mapper, CommandMapper)
	{
	}

	public SailfishSKCanvasViewHandler(IPropertyMapper? mapper, CommandMapper? commandMapper)
		: base(mapper ?? Mapper, commandMapper ?? CommandMapper, SkiaMeasure.Android)
	{
		_renderer = new SkiaSurfaceRenderer(
			paint: (surface, info, rawInfo) =>
			{
				if (ConnectedView is { } view)
					view.OnPaintSurface(new SKPaintSurfaceEventArgs(surface, info, rawInfo));
			},
			canvasSizeChanged: size => ConnectedView?.OnCanvasSizeChanged(size),
			ignorePixelScaling: () => ConnectedView?.IgnorePixelScaling ?? false);
	}

	protected override string? AdapterUri => QtHostSurface.AdapterUri;

	/// <summary>The bitmap size of the last paint, in pixels (empty before the first one).</summary>
	internal SKSizeI PixelSize => _renderer.PixelSize;

	internal SkiaSurfaceRenderer Renderer => _renderer;

	private static PropertyMapper<ISKCanvasView, SailfishSKCanvasViewHandler> BuildMapper()
	{
		var mapper = new PropertyMapper<ISKCanvasView, SailfishSKCanvasViewHandler>(SailfishViewMapper.Mapper)
		{
			[nameof(ISKCanvasView.EnableTouchEvents)] = MapEnableTouchEvents,
			[nameof(ISKCanvasView.IgnorePixelScaling)] = MapIgnorePixelScaling,
		};
		// Android frees the bitmap of a view that is not visible and draws again when it is; input transparency
		// takes the view out of touch delivery. The generic actions of these keys still run first.
		mapper.AppendToMapping(nameof(IView.Visibility), static (handler, _) => handler._renderer.OnVisibilityChanged());
		mapper.AppendToMapping(nameof(IView.InputTransparent), static (handler, view) => MapEnableTouchEvents(handler, view));
		return mapper;
	}

	public static void MapEnableTouchEvents(SailfishSKCanvasViewHandler handler, ISKCanvasView view) =>
		handler._renderer.Touch.Update(view.EnableTouchEvents && !view.InputTransparent, view.OnTouch);

	public static void MapIgnorePixelScaling(SailfishSKCanvasViewHandler handler, ISKCanvasView view) =>
		handler._renderer.Invalidate();   // Android's IgnorePixelScaling setter re-sizes and invalidates

	public static void OnInvalidateSurface(SailfishSKCanvasViewHandler handler, ISKCanvasView view, object? args) =>
		handler._renderer.Invalidate();

	protected override void ConnectHandler(NativeElementHost platformView)
	{
		base.ConnectHandler(platformView);
		_renderer.Connect(platformView, () => ConnectedView is { } v && v.Visibility == Visibility.Visible);
	}

	protected override void DisconnectHandler(NativeElementHost platformView)
	{
		_renderer.Disconnect();
		base.DisconnectHandler(platformView);
	}

	public override void PlatformArrange(Rect frame)
	{
		base.PlatformArrange(frame);
		_renderer.Arrange(frame);
	}
}

/// <summary>Measure of SkiaSharp's views, as Android's <c>View.onMeasure</c> default: the constraint when it is finite
/// (AT_MOST/EXACTLY), 0 when unconstrained (UNSPECIFIED); requests and min/max are applied around it.</summary>
internal static class SkiaMeasure
{
	public static readonly Func<IView, double, double, Size> Android = static (_, widthConstraint, heightConstraint) =>
		new Size(double.IsFinite(widthConstraint) ? widthConstraint : 0, double.IsFinite(heightConstraint) ? heightConstraint : 0);
}
