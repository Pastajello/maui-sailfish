using Microsoft.Maui.Graphics;
using Microsoft.Maui.SailfishOS.Graphics;

namespace Microsoft.Maui.SailfishOS.Platform;

using IImage = Microsoft.Maui.Graphics.IImage;

/// <summary>
/// Shapes leg, IImage (tracker S49): a 400×200 PNG, left half red and right half blue, goes through QImage in the shim.
/// The results are decoded back here, so the checks read real pixels: Downsize keeps the aspect, Fit letterboxes with
/// transparent bars, Bleed crops the centre, Stretch distorts, and Save writes JPEG (quality changes the size) and BMP.
/// </summary>
internal sealed partial class QtHostDiagnosticsRunner
{
	private void RunQtImageChecks()
	{
		try
		{
			RunQtImageChecksCore();
		}
		catch (Exception ex)
		{
			_qtShapesChecks.Check($"S49 IImage: {ex.GetType().Name}: {ex.Message}", false);
		}
	}

	private void RunQtImageChecksCore()
	{
		static (byte, byte, byte, byte) Halves(int x, int y) => x < 200 ? ((byte)255, (byte)0, (byte)0, (byte)255) : ((byte)0, (byte)0, (byte)255, (byte)255);
		static bool Red((int R, int G, int B, int A) p) => p.R > 200 && p.B < 60 && p.A > 200;
		static bool Blue((int R, int G, int B, int A) p) => p.B > 200 && p.R < 60 && p.A > 200;
		static DiagPng Png(IImage image)
		{
			using var stream = new MemoryStream();
			image.Save(stream);
			return DiagPng.FromBytes(stream.ToArray());
		}

		var services = Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services;
		var loader = services?.GetService(typeof(IImageLoadingService)) as IImageLoadingService;
		var source = DiagPng.Encode(400, 200, Halves);
		var image = loader?.FromBytes(source);
		_qtShapesChecks.Check($"S49 IImage: IImageLoadingService is {loader?.GetType().Name ?? "<none>"}, the PNG reads as {image?.Width}×{image?.Height}",
			loader is SailfishImageLoadingService && image is { Width: 400, Height: 200 });
		if (image is null)
			return;

		var down = image.Downsize(100);
		var downPng = Png(down);
		_qtShapesChecks.Check($"S49 IImage: Downsize(100) → {down.Width}×{down.Height} (decoded {downPng.Width}×{downPng.Height}), red left {downPng.At(10, 25)}, blue right {downPng.At(90, 25)}",
			down is { Width: 100, Height: 50 } && downPng is { Width: 100, Height: 50 } && Red(downPng.At(10, 25)) && Blue(downPng.At(90, 25)));

		var fit = Png(image.Resize(64, 64, ResizeMode.Fit));
		_qtShapesChecks.Check($"S49 IImage: Resize Fit 64×64 letterboxes: bar {fit.At(32, 4)} transparent, image {fit.At(8, 32)} red / {fit.At(56, 32)} blue",
			fit is { Width: 64, Height: 64 } && fit.At(32, 4).A < 20 && fit.At(32, 60).A < 20 && Red(fit.At(8, 32)) && Blue(fit.At(56, 32)));

		var bleed = Png(image.Resize(64, 64, ResizeMode.Bleed));
		_qtShapesChecks.Check($"S49 IImage: Resize Bleed 64×64 crops the centre: top {bleed.At(32, 2)} opaque, {bleed.At(8, 32)} red / {bleed.At(56, 32)} blue",
			bleed is { Width: 64, Height: 64 } && bleed.At(32, 2).A > 200 && Red(bleed.At(8, 32)) && Blue(bleed.At(56, 32)));

		var stretch = Png(image.Resize(50, 100, ResizeMode.Stretch));
		_qtShapesChecks.Check($"S49 IImage: Resize Stretch 50×100 → {stretch.Width}×{stretch.Height}, {stretch.At(5, 95)} red / {stretch.At(45, 5)} blue",
			stretch is { Width: 50, Height: 100 } && Red(stretch.At(5, 95)) && Blue(stretch.At(45, 5)));

		byte[] Saved(ImageFormat format, float quality)
		{
			using var stream = new MemoryStream();
			image.Save(stream, format, quality);
			return stream.ToArray();
		}
		var good = Saved(ImageFormat.Jpeg, 0.95f);
		var poor = Saved(ImageFormat.Jpeg, 0.05f);
		var jpeg = SailfishImage.FromBytes(good);
		var bmp = SailfishImage.FromBytes(Saved(ImageFormat.Bmp, 1));
		_qtShapesChecks.Check($"S49 IImage: Save JPEG ({jpeg.Format} {jpeg.Width}×{jpeg.Height}, {good.Length} B at 0.95 > {poor.Length} B at 0.05) and BMP ({bmp.Format} {bmp.Width}×{bmp.Height})",
			jpeg is { Format: ImageFormat.Jpeg, Width: 400, Height: 200 } && good.Length > poor.Length &&
			bmp is { Format: ImageFormat.Bmp, Width: 400, Height: 200 });
	}
}

/// <summary>
/// Shapes leg, DrawImage (tracker S50): a GraphicsView page draws the red/blue IImage into its top-left 200×100 dp,
/// fills 200×60 dp from y 120 with an ImagePaint (a 20×20 tile, green over yellow; Canvas2D tiles from the canvas origin, one image pixel per dp) and draws a window screenshot
/// (Screenshot → ToImageAsync) on the right. The view is captured (IViewScreenshot) and its pixels read back.
/// </summary>
internal sealed partial class QtHostDiagnosticsRunner
{
	private sealed class DrawImageDrawable : Microsoft.Maui.Graphics.IDrawable
	{
		public IImage? Image;
		public IImage? Tile;
		public IImage? Screenshot;

		public void Draw(ICanvas canvas, RectF dirtyRect)
		{
			if (Image is not null)
				canvas.DrawImage(Image, 0, 0, 200, 100);
			if (Tile is not null)
			{
				canvas.SetFillPaint(new ImagePaint { Image = Tile }, new RectF(0, 120, 200, 60));
				canvas.FillRectangle(0, 120, 200, 60);
			}
			if (Screenshot is not null)
				canvas.DrawImage(Screenshot, 210, 0, 90, 180);
		}
	}

	private void RunQtDrawImageCheck(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, Action next)
	{
		if (renderer.CurrentPage?.Navigation is not { } navigation)
		{
			_qtShapesChecks.Check("S50 DrawImage: a page to push from", false);
			next();
			return;
		}
		var drawable = new DrawImageDrawable
		{
			Image = SailfishImage.FromBytes(DiagPng.Encode(400, 200, (x, _) => x < 200 ? ((byte)255, (byte)0, (byte)0, (byte)255) : ((byte)0, (byte)0, (byte)255, (byte)255))),
			Tile = SailfishImage.FromBytes(DiagPng.Encode(20, 20, (_, y) => y < 10 ? ((byte)0, (byte)200, (byte)0, (byte)255) : ((byte)255, (byte)220, (byte)0, (byte)255))),
		};
		Shot(dispatcher, "shapes-gallery", () => PushDrawImagePage(drawable, dispatcher, navigation, next));
	}

	private void PushDrawImagePage(DrawImageDrawable drawable, SailfishDispatcher dispatcher, Microsoft.Maui.Controls.INavigation navigation, Action next)
	{
		var screenshotTask = Microsoft.Maui.Media.Screenshot.Default.CaptureAsync();
		WaitFor(dispatcher, () => screenshotTask.IsCompleted, 5000, () =>
		{
			var shotImage = screenshotTask.IsCompletedSuccessfully ? screenshotTask.Result.ToImageAsync() : Task.FromException<SailfishImage>(screenshotTask.Exception!);
			WaitFor(dispatcher, () => shotImage.IsCompleted, 5000, () =>
			{
				drawable.Screenshot = shotImage.IsCompletedSuccessfully ? shotImage.Result : null;
				_qtShapesChecks.Check($"S50 screenshot as IImage: Screenshot.Default ({Microsoft.Maui.Media.Screenshot.Default.GetType().Name}) → ToImageAsync " +
					$"{drawable.Screenshot?.Width}×{drawable.Screenshot?.Height} {(shotImage.IsFaulted ? shotImage.Exception?.GetBaseException().Message : "")}",
					drawable.Screenshot is { Width: > 100, Height: > 100 });
				var view = new Microsoft.Maui.Controls.GraphicsView { Drawable = drawable, WidthRequest = 300, HeightRequest = 180, HorizontalOptions = Microsoft.Maui.Controls.LayoutOptions.Start };
				Console.Error.WriteLine("[Sailfish] Qt shapes diag: S50 — pushing the DrawImage page");
				_ = navigation.PushAsync(new Microsoft.Maui.Controls.ContentPage { Title = "DrawImage", Content = view }, false);
				double ImageOps() => view.Handler?.PlatformView is QtHost.NativeElementHost { IsAttached: true } host ? HostPropNum(host, "mauiImageOps") : 0;
				WaitFor(dispatcher, () => ImageOps() >= 3, 8000, () => VerifyDrawImage(view, ImageOps(), dispatcher, navigation, next));
			});
		});
	}

	private void VerifyDrawImage(Microsoft.Maui.Controls.GraphicsView view, double imageOps, SailfishDispatcher dispatcher,
		Microsoft.Maui.Controls.INavigation navigation, Action next)
	{
		var capture = view.Handler?.PlatformView is { } platformView
			? ((Microsoft.Maui.Media.IViewScreenshot)Microsoft.Maui.Media.Screenshot.Default).CaptureViewAsync(platformView)
			: Task.FromResult<Microsoft.Maui.Media.IScreenshotResult?>(null);
		WaitFor(dispatcher, () => capture.IsCompleted, 5000, () =>
		{
			var d = SailfishDisplay.Density;
			DiagPng? png = null;
			if (capture.IsCompletedSuccessfully && capture.Result is SailfishScreenshot.ScreenshotImage grabbed)
				png = DiagPng.FromBytes(grabbed.Png);
			(int R, int G, int B, int A) At(double x, double y) => png?.At((int)(x * d), (int)(y * d)) ?? (0, 0, 0, 0);
			var (red, blue, green, yellow) = (At(50, 50), At(150, 50), At(100, 124), At(100, 136));
			_qtShapesChecks.Check($"S50 DrawImage: GraphicsView drew {imageOps} image ops (DrawImage ×2 + ImagePaint); view grab {png?.Width}×{png?.Height} at density {d:F2}: " +
				$"image red {red} / blue {blue}, ImagePaint green {green} / yellow {yellow}, screenshot area {At(255, 90)}",
				imageOps >= 3 && png is not null &&
				red is { R: > 200, G: < 60, B: < 60 } && blue is { B: > 200, R: < 60, G: < 60 } &&
				green is { G: > 150, R: < 60, B: < 60 } && yellow is { R: > 200, G: > 170, B: < 60 });
			Shot(dispatcher, "shapes-drawimage", () => dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1500), () =>
				Shot(dispatcher, "shapes-drawimage-late", () =>
				{
					var popped = navigation.PopAsync(false);
					WaitFor(dispatcher, () => popped.IsCompleted, 4000, next);
				})));
		});
	}
}
