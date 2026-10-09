using Microsoft.Maui.Graphics;
using Microsoft.Maui.SailfishOS.Graphics;
using Microsoft.Maui.SailfishOS.Platform;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>Tracker S50: ICanvas.DrawImage and ImagePaint reach the GraphicsView adapter as file URLs of the image's
/// bytes (Context2D loads images by URL), and a screenshot turns into a SailfishImage.</summary>
[Collection("renderer")]
public sealed class DrawImageTests : IDisposable
{
	private readonly TestStatics _statics = new();

	public DrawImageTests() => QtHostRuntime.TestShim = new FakeShim();

	public void Dispose() => _statics.Dispose();

	private static object?[] Last(QtHostCanvasRecorder canvas) =>
		((System.Collections.IEnumerable)canvas.Commands[^1]!).Cast<object?>().ToArray();

	private static string FileOf(object? url) => new Uri((string)url!).LocalPath;

	[Fact]
	public void DrawImage_records_a_cache_file_of_the_bytes_and_the_rectangle()
	{
		var bytes = FakeShim.FakeImage(40, 20, "jpg");
		var image = SailfishImage.FromBytes(bytes);
		var canvas = new QtHostCanvasRecorder();

		canvas.DrawImage(image, 1, 2, 30.5f, 40);
		var op = Last(canvas);
		canvas.DrawImage(image, 0, 0, 10, 10);

		Assert.Equal("img", op[0]);
		Assert.StartsWith("file://", (string)op[1]!);
		Assert.EndsWith(".jpg", (string)op[1]!);
		Assert.Equal(bytes, File.ReadAllBytes(FileOf(op[1])));
		Assert.Equal(new object?[] { 1.0, 2.0, 30.5, 40.0 }, op[2..]);
		Assert.Equal(op[1], Last(canvas)[1]);   // the same image, the same file
	}

	[Fact]
	public void A_plain_PlatformImage_draws_from_its_bytes_and_a_disposed_image_is_skipped()
	{
		var bytes = FakeShim.FakeImage(8, 8);
		var plain = new Microsoft.Maui.Graphics.Platform.PlatformImage(bytes, ImageFormat.Png);
		var disposed = SailfishImage.FromBytes(FakeShim.FakeImage(9, 9));
		disposed.Dispose();
		var canvas = new QtHostCanvasRecorder();

		canvas.DrawImage(plain, 0, 0, 8, 8);
		var drawn = Last(canvas);
		canvas.DrawImage(disposed, 0, 0, 9, 9);

		Assert.Equal(bytes, File.ReadAllBytes(FileOf(drawn[1])));
		Assert.Equal(string.Empty, Last(canvas)[1]);
	}

	[Fact]
	public void ImagePaint_is_an_image_fill_spec()
	{
		var image = SailfishImage.FromBytes(FakeShim.FakeImage(4, 4));
		var canvas = new QtHostCanvasRecorder();

		canvas.SetFillPaint(new ImagePaint { Image = image }, new RectF(0, 0, 100, 100));

		var op = Last(canvas);
		Assert.Equal("fpaint", op[0]);
		var spec = ((System.Collections.IEnumerable)op[1]!).Cast<object?>().ToArray();
		Assert.Equal("image", spec[0]);
		Assert.StartsWith("file://", (string)spec[1]!);
	}

	[Fact]
	public void The_adapter_loads_the_url_draws_it_and_tiles_ImagePaint()
	{
		var qml = File.ReadAllText(Path.Combine(Repo.Root, "src/Linux.SailfishOS/Platform/QtHost/qml/shapes/GraphicsView.qml"));

		Assert.Contains("onImageLoaded: requestPaint()", qml);
		Assert.Contains("loadImage(url);", qml);
		Assert.Contains("ctx.drawImage(c[1], c[2], c[3], c[4], c[5]);", qml);
		Assert.Contains("ctx.createPattern(spec[1], \"repeat\")", qml);
	}

	[Fact]
	public async Task A_screenshot_becomes_a_SailfishImage()
	{
		var png = FakeShim.FakeImage(1080, 2160);

		var image = await new SailfishScreenshot.ScreenshotImage(png).ToImageAsync();

		Assert.Equal((1080f, 2160f, ImageFormat.Png), (image.Width, image.Height, image.Format));
	}
}
