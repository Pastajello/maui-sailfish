using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.SailfishOS.Handlers;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace Microsoft.Maui.SailfishOS.SkiaSharp;

/// <summary>
/// Sailfish handler of <see cref="ISKGLView"/>, the counterpart of SkiaSharp's Android <c>SKGLViewHandler</c>. It
/// draws through the same raster surface as <see cref="SailfishSKCanvasViewHandler"/>: <c>PaintSurface</c> gets a
/// raster <see cref="SKSurface"/>, <see cref="ISKGLView.GRContext"/> stays null and
/// <see cref="SKPaintGLSurfaceEventArgs.BackendRenderTarget"/> describes no GPU framebuffer. Apps that draw through
/// <c>e.Surface.Canvas</c> work unchanged; <c>HasRenderLoop</c> repaints every frame while the view is in a window,
/// and <c>IgnorePixelScaling</c> follows Android's <c>MauiSKGLTextureView</c>.
/// </summary>
public class SailfishSKGLViewHandler : SailfishViewHandler<ISKGLView>
{
	public static readonly PropertyMapper<ISKGLView, SailfishSKGLViewHandler> Mapper = BuildMapper();

	public static readonly CommandMapper<ISKGLView, SailfishSKGLViewHandler> CommandMapper = new(SailfishViewMapper.CommandMapper)
	{
		[nameof(ISKGLView.InvalidateSurface)] = OnInvalidateSurface,
	};

	private readonly SkiaSurfaceRenderer _renderer;

	public SailfishSKGLViewHandler() : this(Mapper, CommandMapper)
	{
	}

	public SailfishSKGLViewHandler(IPropertyMapper? mapper, CommandMapper? commandMapper)
		: base(mapper ?? Mapper, commandMapper ?? CommandMapper, SkiaMeasure.Android)
	{
		_renderer = new SkiaSurfaceRenderer(
			paint: (surface, info, rawInfo) =>
			{
				if (ConnectedView is not { } view)
					return;
				// No GPU framebuffer behind the raster surface: the target only carries the size.
				using var target = new GRBackendRenderTarget(rawInfo.Width, rawInfo.Height, 0, 0,
					new GRGlFramebufferInfo(0, 0x8058 /* GL_RGBA8 */));
				view.OnPaintSurface(new SKPaintGLSurfaceEventArgs(surface, target, GRSurfaceOrigin.TopLeft, info, rawInfo));
			},
			canvasSizeChanged: size => ConnectedView?.OnCanvasSizeChanged(size),
			ignorePixelScaling: () => ConnectedView?.IgnorePixelScaling ?? false);
	}

	protected override string? AdapterUri => QtHostSurface.AdapterUri;

	internal SkiaSurfaceRenderer Renderer => _renderer;

	private static PropertyMapper<ISKGLView, SailfishSKGLViewHandler> BuildMapper()
	{
		var mapper = new PropertyMapper<ISKGLView, SailfishSKGLViewHandler>(SailfishViewMapper.Mapper)
		{
			[nameof(ISKGLView.EnableTouchEvents)] = MapEnableTouchEvents,
			[nameof(ISKGLView.IgnorePixelScaling)] = MapIgnorePixelScaling,
			[nameof(ISKGLView.HasRenderLoop)] = MapHasRenderLoop,
		};
		mapper.AppendToMapping(nameof(IView.Visibility), static (handler, _) => handler._renderer.OnVisibilityChanged());
		mapper.AppendToMapping(nameof(IView.InputTransparent), static (handler, view) => MapEnableTouchEvents(handler, view));
		return mapper;
	}

	public static void MapEnableTouchEvents(SailfishSKGLViewHandler handler, ISKGLView view) =>
		handler._renderer.Touch.Update(view.EnableTouchEvents && !view.InputTransparent, view.OnTouch);

	public static void MapIgnorePixelScaling(SailfishSKGLViewHandler handler, ISKGLView view) =>
		handler._renderer.Invalidate();

	// ISKGLView.HasRenderLoop is already false while the view has no Window (SKGLView re-maps it on Window changes).
	public static void MapHasRenderLoop(SailfishSKGLViewHandler handler, ISKGLView view) =>
		handler._renderer.RenderLoop = view.HasRenderLoop;

	public static void OnInvalidateSurface(SailfishSKGLViewHandler handler, ISKGLView view, object? args) =>
		handler._renderer.Invalidate();

	protected override void ConnectHandler(NativeElementHost platformView)
	{
		base.ConnectHandler(platformView);
		_renderer.Connect(platformView, () => ConnectedView is { } v && v.Visibility == Visibility.Visible);
	}

	protected override void DisconnectHandler(NativeElementHost platformView)
	{
		_renderer.RenderLoop = false;
		_renderer.Disconnect();
		base.DisconnectHandler(platformView);
	}

	public override void PlatformArrange(Rect frame)
	{
		base.PlatformArrange(frame);
		_renderer.Arrange(frame);
	}
}
