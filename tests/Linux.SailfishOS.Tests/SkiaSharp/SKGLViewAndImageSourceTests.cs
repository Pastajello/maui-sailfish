using Linux.SailfishOS.Tests.Renderer;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.SailfishOS.Handlers;
using Microsoft.Maui.SailfishOS.Hosting;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Microsoft.Maui.SailfishOS.SkiaSharp;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
using SkiaSharp.Views.Maui.Controls.Hosting;
using SkiaSharp.Views.Maui.Handlers;
using Xunit;

namespace Linux.SailfishOS.Tests.SkiaSharp;

/// <summary>SKGLView (raster-backed, no GRContext) and SkiaSharp's image sources on Sailfish.</summary>
[Collection("renderer")]
public class SKGLViewAndImageSourceTests
{
	private sealed class TestApp : Application
	{
	}

	// SKGLView binds its Window in the constructor, and a binding needs a dispatcher: the test thread plays the Qt
	// loop before any view exists, as the app's main thread does.
	public SKGLViewAndImageSourceTests()
	{
		Microsoft.Maui.SailfishOS.Platform.SailfishDispatcherProvider.BindLoopThread();
		if (Microsoft.Maui.Dispatching.DispatcherProvider.Current is not Microsoft.Maui.SailfishOS.Platform.SailfishDispatcherProvider)
			Microsoft.Maui.Dispatching.DispatcherProvider.SetCurrent(new Microsoft.Maui.SailfishOS.Platform.SailfishDispatcherProvider());
	}

	private static IServiceProvider SkiaServices()
	{
		SailfishSkiaSharp.Register();
		var builder = MauiApp.CreateBuilder(useDefaults: false);
		builder.UseMauiAppSailfish<TestApp>();
		builder.UseSkiaSharp();
		return builder.Build().Services;
	}

	private static RendererHarness Render(View view)
	{
		var harness = new RendererHarness(new ContentPage { Content = new VerticalStackLayout { Children = { view } } },
			SkiaServices());
		QtHostSurface.RunFrame();
		return harness;
	}

	[Fact]
	public void UseSkiaSharp_gl_views_get_the_sailfish_handler_with_skiasharps_keys()
	{
		var factory = Assert.IsType<SailfishHandlersFactory>(SkiaServices().GetService(typeof(IMauiHandlersFactory)));
		Assert.IsType<SailfishSKGLViewHandler>(factory.GetHandler(typeof(SKGLView)));
		var ours = SailfishSKGLViewHandler.Mapper.GetKeys().ToHashSet();
		foreach (var key in SKGLViewHandler.SKGLViewMapper.GetKeys())
			Assert.Contains(key, ours);
		Assert.NotNull(SailfishSKGLViewHandler.CommandMapper.GetCommand(nameof(ISKGLView.InvalidateSurface)));
	}

	[Fact]
	public void A_gl_view_paints_through_the_raster_surface_without_a_GRContext()
	{
		var view = new SKGLView { WidthRequest = 10, HeightRequest = 5 };
		SKPaintGLSurfaceEventArgs? seen = null;
		view.PaintSurface += (_, e) =>
		{
			seen = e;
			e.Surface.Canvas.Clear(SKColors.Red);
		};
		using var harness = Render(view);

		Assert.NotNull(seen);
		Assert.Equal((20, 10), (seen!.Info.Width, seen.Info.Height));
		Assert.Equal((20, 10), (seen.RawInfo.Width, seen.RawInfo.Height));
		Assert.Null(view.GRContext);
		Assert.Equal(new byte[] { 255, 0, 0, 255 }, harness.Shim.SurfaceCommits.Last(c => c.Width > 0).Pixels[..4]);
	}

	[Fact]
	public void The_render_loop_paints_every_frame_while_on_and_stops_when_off()
	{
		var view = new SKGLView { WidthRequest = 10, HeightRequest = 10 };
		var paints = 0;
		view.PaintSurface += (_, _) => paints++;
		using var harness = Render(view);
		var start = paints;

		view.HasRenderLoop = true;
		QtHostSurface.RunFrame();
		QtHostSurface.RunFrame();
		QtHostSurface.RunFrame();
		Assert.Equal(start + 3, paints);

		view.HasRenderLoop = false;
		QtHostSurface.RunFrame();   // the frame already asked for
		var stopped = paints;
		QtHostSurface.RunFrame();
		QtHostSurface.RunFrame();
		Assert.Equal(stopped, paints);
	}

	[Fact]
	public void Ignoring_pixel_scaling_reports_dp_and_pre_scales_the_canvas_as_androids_texture_view()
	{
		var view = new SKGLView { WidthRequest = 101.3, HeightRequest = 50.7, IgnorePixelScaling = true };
		(SKImageInfo Info, SKImageInfo Raw, float Scale)? seen = null;
		view.PaintSurface += (_, e) => seen = (e.Info, e.RawInfo, e.Surface.Canvas.TotalMatrix.ScaleX);
		using var harness = Render(view);
		Assert.Equal((101, 51), (seen!.Value.Info.Width, seen.Value.Info.Height));
		Assert.Equal((203, 102), (seen.Value.Raw.Width, seen.Value.Raw.Height));
		Assert.Equal(2f, seen.Value.Scale);
	}

	/* --- image sources --- */

	private static SKBitmap Bitmap(int width, int height, SKColor color)
	{
		var bitmap = new SKBitmap(width, height);
		bitmap.Erase(color);
		return bitmap;
	}

	private static SKColor PixelAt(string url)
	{
		using var decoded = SKBitmap.Decode(new Uri(url).LocalPath);
		return decoded.GetPixel(0, 0);
	}

	[Theory]
	[InlineData("bitmap")]
	[InlineData("image")]
	[InlineData("pixmap")]
	[InlineData("picture")]
	public void Each_skiasharp_image_source_resolves_to_a_png_with_its_pixels(string kind)
	{
		SailfishSkiaSharp.Register();
		using var bitmap = Bitmap(6, 4, kind switch { "bitmap" => SKColors.Red, "image" => SKColors.Lime, _ => SKColors.Blue });
		using var image = SKImage.FromBitmap(bitmap);
		using var pixmap = bitmap.PeekPixels();
		using var recorder = new SKPictureRecorder();
		recorder.BeginRecording(SKRect.Create(0, 0, 6, 4)).Clear(SKColors.Yellow);
		using var picture = recorder.EndRecording();
		var (source, color) = kind switch
		{
			"bitmap" => ((ImageSource)new SKBitmapImageSource { Bitmap = bitmap }, SKColors.Red),
			"image" => (new SKImageImageSource { Image = image }, SKColors.Lime),
			"pixmap" => (new SKPixmapImageSource { Pixmap = pixmap }, SKColors.Blue),
			_ => (new SKPictureImageSource { Picture = picture, Dimensions = new SKSizeI(6, 4) }, SKColors.Yellow),
		};

		var url = QtHostImages.Resolve(source);
		Assert.NotNull(url);
		Assert.Equal(color, PixelAt(url!));
		Assert.Equal((6, 4), QtHostImages.PixelSize(source));
		Assert.Equal(url, QtHostImages.Resolve(source));   // cached: the same file while nothing changed
	}

	[Fact]
	public void A_new_bitmap_writes_a_new_file_and_a_null_one_resolves_to_nothing()
	{
		SailfishSkiaSharp.Register();
		using var red = Bitmap(2, 2, SKColors.Red);
		using var blue = Bitmap(2, 2, SKColors.Blue);
		var source = new SKBitmapImageSource { Bitmap = red };
		var first = QtHostImages.Resolve(source)!;
		source.Bitmap = blue;
		var second = QtHostImages.Resolve(source)!;
		Assert.NotEqual(first, second);
		Assert.False(File.Exists(new Uri(first).LocalPath));   // the old file is cleaned up
		Assert.Equal(SKColors.Blue, PixelAt(second));
		Assert.Null(QtHostImages.Resolve(new SKBitmapImageSource()));
	}
}
