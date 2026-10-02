using Linux.SailfishOS.Tests.Renderer;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.SailfishOS.Handlers;
using Microsoft.Maui.SailfishOS.Hosting;
using Microsoft.Maui.SailfishOS.Platform;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Microsoft.Maui.SailfishOS.SkiaSharp;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
using SkiaSharp.Views.Maui.Controls.Hosting;
using SkiaSharp.Views.Maui.Handlers;
using Xunit;

namespace Linux.SailfishOS.Tests.SkiaSharp;

/// <summary>SKCanvasView on Sailfish against SkiaSharp's Android behaviour (SKCanvasViewHandler,
/// SkiaSharp.Views.Android.SKCanvasView + SurfaceFactory, SKTouchHandler), through the real renderer and
/// <see cref="FakeShim"/>.</summary>
[Collection("renderer")]
public class SKCanvasViewHandlerTests
{
	private sealed class TestApp : Application
	{
	}

	private sealed class ChartView : SKCanvasView   // a library's canvas subclass (Microcharts' ChartView)
	{
	}

	private static IServiceProvider SkiaServices()
	{
		SailfishSkiaSharp.Register();
		var builder = MauiApp.CreateBuilder(useDefaults: false);
		builder.UseMauiAppSailfish<TestApp>();
		builder.UseSkiaSharp();
		return builder.Build().Services;
	}

	/// <summary>A page with <paramref name="canvas"/> rendered and arranged; the pending frames ran once.</summary>
	/// <remarks>The harness window is 1080 px wide: density 2.</remarks>
	private static RendererHarness Render(View canvas)
	{
		var harness = new RendererHarness(new ContentPage { Content = new VerticalStackLayout { Children = { canvas } } },
			SkiaServices());
		QtHostSurface.RunFrame();
		return harness;
	}

	private static SailfishSKCanvasViewHandler HandlerOf(SKCanvasView canvas) =>
		Assert.IsType<SailfishSKCanvasViewHandler>(canvas.Handler);

	[Fact]
	public void UseSkiaSharp_canvases_and_their_subclasses_get_the_sailfish_handler()
	{
		var factory = Assert.IsType<SailfishHandlersFactory>(SkiaServices().GetService(typeof(IMauiHandlersFactory)));
		Assert.IsType<SailfishSKCanvasViewHandler>(factory.GetHandler(typeof(SKCanvasView)));
		Assert.IsType<SailfishSKCanvasViewHandler>(factory.GetHandler(typeof(ChartView)));
	}

	[Fact]
	public void The_mapper_answers_every_key_and_command_of_SkiaSharps_handler()
	{
		var ours = SailfishSKCanvasViewHandler.Mapper.GetKeys().ToHashSet();
		foreach (var key in SKCanvasViewHandler.SKCanvasViewMapper.GetKeys())
			Assert.Contains(key, ours);
		Assert.NotNull(SailfishSKCanvasViewHandler.CommandMapper.GetCommand(nameof(ISKCanvasView.InvalidateSurface)));
	}

	[Theory]
	// Android: each edge to px as ContextExtensions.ToPixels (ceil(dp * density - 1e-10), float density), the size the
	// difference; IgnorePixelScaling divides by the float density and truncates (SKCanvasView.OnDraw).
	[InlineData(1.0f, 0, 101.3, 50.7, 102, 51, 102, 51)]
	[InlineData(1.5f, 0, 101.3, 50.7, 152, 77, 101, 51)]
	[InlineData(1.75f, 0, 101.3, 50.7, 178, 89, 101, 50)]
	[InlineData(2.0f, 0, 101.3, 50.7, 203, 102, 101, 51)]
	[InlineData(2.5f, 0, 101.3, 50.7, 254, 127, 101, 50)]
	[InlineData(3.0f, 0, 101.3, 50.7, 304, 153, 101, 51)]
	// The same dp width is 51 px at x 0 (ceil 50.25) and 50 px at x 10.3 (ceil 76 - ceil 25.75), as on Android.
	[InlineData(2.5f, 0, 20.1, 20.1, 51, 51, 20, 20)]
	[InlineData(2.5f, 10.3, 20.1, 20.1, 50, 50, 20, 20)]
	public void The_pixel_and_canvas_sizes_follow_androids_rounding(float density, double x, double width, double height,
		int pxWidth, int pxHeight, int dpWidth, int dpHeight)
	{
		var px = SkiaSurfaceRenderer.PixelSizeOf(new Microsoft.Maui.Graphics.Rect(x, x, width, height), density);
		Assert.Equal(new SKSizeI(pxWidth, pxHeight), px);
		Assert.Equal(px, SkiaSurfaceRenderer.CanvasSizeOf(px, density, false));
		Assert.Equal(new SKSizeI(dpWidth, dpHeight), SkiaSurfaceRenderer.CanvasSizeOf(px, density, true));
	}

	[Theory]
	[InlineData(false, 203, 102)]
	[InlineData(true, 101, 51)]
	public void PaintSurface_gets_the_pixel_info_or_the_dp_one_with_a_pre_scaled_canvas(bool ignore, int infoWidth, int infoHeight)
	{
		var canvas = new SKCanvasView { WidthRequest = 101.3, HeightRequest = 50.7, IgnorePixelScaling = ignore };
		var infos = new List<(SKImageInfo Info, SKImageInfo Raw, SKMatrix Matrix)>();
		canvas.PaintSurface += (_, e) => infos.Add((e.Info, e.RawInfo, e.Surface.Canvas.TotalMatrix));
		using var harness = Render(canvas);   // density 2

		var (info, raw, matrix) = Assert.Single(infos);
		Assert.Equal((infoWidth, infoHeight), (info.Width, info.Height));
		Assert.Equal(SKColorType.Rgba8888, info.ColorType);
		Assert.Equal(SKAlphaType.Premul, info.AlphaType);
		Assert.Equal((203, 102), (raw.Width, raw.Height));
		Assert.Equal(ignore ? 2f : 1f, matrix.ScaleX);
		Assert.Equal(new SKSize(infoWidth, infoHeight), canvas.CanvasSize);
	}

	[Fact]
	public void The_drawing_is_committed_to_the_hosts_surface()
	{
		var canvas = new SKCanvasView { WidthRequest = 10, HeightRequest = 5 };
		canvas.PaintSurface += (_, e) => e.Surface.Canvas.Clear(new SKColor(255, 0, 0, 128));
		using var harness = Render(canvas);

		var host = Assert.IsType<NativeElementHost>(canvas.Handler!.PlatformView);
		Assert.Equal(QtHostSurface.AdapterUri, host.QmlUri);
		var commit = harness.Shim.SurfaceCommits.Last(c => c.Width > 0);
		Assert.Equal(host.NativeHandle, commit.Handle);
		Assert.Equal((20, 10), (commit.Width, commit.Height));
		Assert.Equal(new byte[] { 128, 0, 0, 128 }, commit.Pixels[..4]);   // RGBA, premultiplied
	}

	[Fact]
	public void Invalidations_coalesce_into_one_paint_in_the_next_frame()
	{
		var canvas = new SKCanvasView { WidthRequest = 10, HeightRequest = 10 };
		var paints = 0;
		canvas.PaintSurface += (_, _) => paints++;
		using var harness = Render(canvas);
		Assert.Equal(1, paints);

		canvas.InvalidateSurface();
		canvas.InvalidateSurface();
		canvas.InvalidateSurface();
		Assert.Equal(1, paints);   // nothing until the frame
		QtHostSurface.RunFrame();
		Assert.Equal(2, paints);
		QtHostSurface.RunFrame();
		Assert.Equal(2, paints);
	}

	[Fact]
	public void The_bitmap_keeps_its_pixels_between_paints_of_the_same_size()
	{
		var canvas = new SKCanvasView { WidthRequest = 4, HeightRequest = 4 };
		var first = true;
		canvas.PaintSurface += (_, e) =>
		{
			if (first)
				e.Surface.Canvas.Clear(SKColors.Blue);   // the second paint draws nothing
			first = false;
		};
		using var harness = Render(canvas);
		canvas.InvalidateSurface();
		QtHostSurface.RunFrame();
		Assert.Equal(new byte[] { 0, 0, 255, 255 }, harness.Shim.SurfaceCommits[^1].Pixels[..4]);
	}

	[Fact]
	public void A_hidden_canvas_frees_its_pixels_and_paints_again_when_shown()
	{
		var canvas = new SKCanvasView { WidthRequest = 10, HeightRequest = 10 };
		var paints = 0;
		canvas.PaintSurface += (_, _) => paints++;
		using var harness = Render(canvas);

		canvas.IsVisible = false;
		harness.Poll();
		QtHostSurface.RunFrame();
		canvas.InvalidateSurface();
		QtHostSurface.RunFrame();
		Assert.Equal(1, paints);
		Assert.Equal(0, harness.Shim.SurfaceCommits[^1].Width);   // released

		canvas.IsVisible = true;
		harness.Poll();
		QtHostSurface.RunFrame();
		Assert.Equal(2, paints);
	}

	[Fact]
	public void An_empty_canvas_does_not_paint()
	{
		var canvas = new SKCanvasView();   // no request: 0 high in a vertical stack, as Android's UNSPECIFIED measure
		var paints = 0;
		canvas.PaintSurface += (_, _) => paints++;
		using var harness = Render(canvas);
		Assert.Equal(0, paints);
		Assert.Equal(0, canvas.Height);
	}

	[Fact]
	public void A_throwing_paint_is_logged_and_the_next_paint_runs()
	{
		var canvas = new SKCanvasView { WidthRequest = 10, HeightRequest = 10 };
		var calls = 0;
		canvas.PaintSurface += (_, _) =>
		{
			if (++calls == 1)
				throw new InvalidOperationException("app bug");
		};
		using var harness = Render(canvas);
		canvas.InvalidateSurface();
		QtHostSurface.RunFrame();
		Assert.Equal(2, calls);
	}

	[Fact]
	public void Touch_is_off_until_enabled_and_follows_androids_touch_handler()
	{
		var canvas = new SKCanvasView { WidthRequest = 100, HeightRequest = 100 };
		var touches = new List<SKTouchEventArgs>();
		canvas.Touch += (_, e) =>
		{
			touches.Add(e);
			e.Handled = true;
		};
		using var harness = Render(canvas);
		var host = Assert.IsType<NativeElementHost>(canvas.Handler!.PlatformView);
		Assert.False(harness.Shim.SurfaceTouch.GetValueOrDefault(host.NativeHandle));

		canvas.EnableTouchEvents = true;
		Assert.True(harness.Shim.SurfaceTouch[host.NativeHandle]);
		var h = host.NativeHandle;
		Assert.True(QtHostSurface.DeliverTouch(h, SurfaceTouchAction.Pressed, 7, 20, 30, 0.5, false, 0));
		QtHostSurface.DeliverTouch(h, SurfaceTouchAction.Moved, 7, 24, 34, 0.5, false, 0);
		QtHostSurface.DeliverTouch(h, SurfaceTouchAction.Released, 7, 24, 34, 0, false, 0);

		Assert.Equal(new[] { SKTouchAction.Pressed, SKTouchAction.Moved, SKTouchAction.Released }, touches.Select(t => t.ActionType));
		Assert.Equal(new[] { true, true, false }, touches.Select(t => t.InContact));
		Assert.All(touches, t => Assert.Equal((0L, SKTouchDeviceType.Touch, SKMouseButton.Left), (t.Id, t.DeviceType, t.MouseButton)));
		Assert.Equal(new SKPoint(20, 30), touches[0].Location);   // pixels
		Assert.Equal(0.5f, touches[0].Pressure);

		canvas.IgnorePixelScaling = true;
		touches.Clear();
		QtHostSurface.DeliverTouch(h, SurfaceTouchAction.Pressed, 8, 20, 30, 1, false, 0);
		Assert.Equal(new SKPoint(10, 15), touches[0].Location);   // dp at density 2

		canvas.InputTransparent = true;
		Assert.False(harness.Shim.SurfaceTouch[h]);
	}

	[Fact]
	public void An_unhandled_press_is_reported_as_not_handled()
	{
		var canvas = new SKCanvasView { WidthRequest = 100, HeightRequest = 100, EnableTouchEvents = true };
		canvas.Touch += (_, _) => { };   // Handled stays false: Android gives the gesture to the parent
		using var harness = Render(canvas);
		var host = Assert.IsType<NativeElementHost>(canvas.Handler!.PlatformView);
		Assert.False(QtHostSurface.DeliverTouch(host.NativeHandle, SurfaceTouchAction.Pressed, 1, 1, 1, 1, false, 0));
	}

	[Fact]
	public void A_re_created_native_object_is_painted_again_and_gets_touch_back()
	{
		var canvas = new SKCanvasView { WidthRequest = 10, HeightRequest = 10, EnableTouchEvents = true };
		var paints = 0;
		canvas.PaintSurface += (_, _) => paints++;
		using var harness = Render(canvas);
		var host = Assert.IsType<NativeElementHost>(canvas.Handler!.PlatformView);
		Assert.Equal(1, paints);

		host.RaiseAttached();   // as the renderer does for a re-created QML object
		QtHostSurface.RunFrame();
		Assert.Equal(2, paints);
		Assert.True(harness.Shim.SurfaceTouch[host.NativeHandle]);
	}
}
