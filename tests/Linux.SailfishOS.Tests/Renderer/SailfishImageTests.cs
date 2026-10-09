using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.SailfishOS.Graphics;
using Microsoft.Maui.SailfishOS.Platform;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>Tracker S49 (D6 a): MAUI's IImage on QImage. The shim decodes, resizes and re-encodes; the managed side
/// picks the target size and mode, keeps the bytes and the format. The fake shim answers every op at its w × h.</summary>
[Collection("renderer")]
public sealed class SailfishImageTests : IDisposable
{
	private readonly TestStatics _statics = new();
	private readonly FakeShim _shim = new();

	public SailfishImageTests() => QtHostRuntime.TestShim = _shim;

	public void Dispose() => _statics.Dispose();

	private static SailfishImage Load(int w, int h, string format = "png") =>
		SailfishImage.FromStream(new MemoryStream(FakeShim.FakeImage(w, h, format)));

	[Fact]
	public void FromStream_reads_the_decoded_size_and_the_format_from_the_header()
	{
		var png = Load(400, 200);
		var jpeg = SailfishImage.FromStream(new MemoryStream(FakeShim.FakeImage(30, 40, "jpg")), ImageFormat.Png);

		Assert.Equal((400f, 200f, ImageFormat.Png), (png.Width, png.Height, png.Format));
		Assert.Equal((30f, 40f, ImageFormat.Jpeg), (jpeg.Width, jpeg.Height, jpeg.Format));
		Assert.Throws<InvalidDataException>(() => SailfishImage.FromBytes([1, 2, 3, 4]));
		Assert.Throws<InvalidDataException>(() => SailfishImage.FromBytes([]));
	}

	[Fact]
	public void Downsize_keeps_the_aspect_and_leaves_a_small_image_alone()
	{
		var image = Load(400, 200);

		var down = image.Downsize(100);
		var both = image.Downsize(300, 50);
		var same = image.Downsize(1000);

		Assert.Equal((100f, 50f), (down.Width, down.Height));
		Assert.Equal((100f, 50f), (both.Width, both.Height));
		Assert.Equal((400f, 200f), (same.Width, same.Height));
		Assert.Equal(2, _shim.ImageOps.Count);   // the unscaled one never reached the shim
		Assert.Contains("\"mode\":\"stretch\"", _shim.ImageOps[0]);
		Assert.Contains("\"w\":100,\"h\":50", _shim.ImageOps[0]);
	}

	[Theory]
	[InlineData(ResizeMode.Fit, "fit")]
	[InlineData(ResizeMode.Bleed, "fill")]
	[InlineData(ResizeMode.Stretch, "stretch")]
	public void Resize_maps_mauis_modes_and_comes_out_at_the_asked_size(ResizeMode mode, string qtMode)
	{
		var resized = Load(400, 200).Resize(64.4f, 32.6f, mode);

		Assert.Equal((64f, 33f), (resized.Width, resized.Height));
		Assert.Contains($"\"mode\":\"{qtMode}\"", _shim.ImageOps.Single());
		Assert.Contains("\"format\":\"png\"", _shim.ImageOps.Single());
	}

	[Fact]
	public void Save_passes_the_bytes_through_in_their_own_format_and_reencodes_otherwise()
	{
		var bytes = FakeShim.FakeImage(40, 20);
		var image = SailfishImage.FromBytes(bytes);

		using var same = new MemoryStream();
		image.Save(same);
		using var jpeg = new MemoryStream();
		image.Save(jpeg, ImageFormat.Jpeg, 0.8f);

		Assert.Equal(bytes, same.ToArray());
		Assert.Contains("\"mode\":\"keep\",\"format\":\"jpg\",\"quality\":80", _shim.ImageOps.Single());
		Assert.Equal(ImageFormat.Jpeg, SailfishImage.FromBytes(jpeg.ToArray()).Format);
	}

	[Fact]
	public async Task SaveAsync_writes_what_Save_writes()
	{
		var image = Load(40, 20);
		using var sync = new MemoryStream();
		using var async = new MemoryStream();

		image.Save(sync, ImageFormat.Bmp);
		await image.SaveAsync(async, ImageFormat.Bmp);

		Assert.Equal(sync.ToArray(), async.ToArray());
	}

	[Fact]
	public void A_gif_resizes_to_png_and_cannot_be_saved_as_gif_again()
	{
		var gif = Load(20, 20, "gif");

		var resized = (SailfishImage)gif.Resize(10, 10);

		Assert.Equal(ImageFormat.Png, resized.Format);
		Assert.Throws<NotSupportedException>(() => resized.Save(new MemoryStream(), ImageFormat.Gif));
	}

	[Fact]
	public void DisposeOriginal_disposes_the_source()
	{
		var image = Load(400, 200);

		var down = image.Downsize(100, disposeOriginal: true);

		Assert.Throws<ObjectDisposedException>(() => image.Save(new MemoryStream()));
		down.Save(new MemoryStream());
	}

	[Fact]
	public void The_services_give_the_sailfish_loader_and_a_plain_PlatformImage_converts()
	{
		var services = new SailfishServiceOverlay(new ServiceCollection().BuildServiceProvider());
		var loader = Assert.IsType<SailfishImageLoadingService>(services.GetService(typeof(IImageLoadingService)));
		var plain = new Microsoft.Maui.Graphics.Platform.PlatformImage(FakeShim.FakeImage(12, 8), ImageFormat.Png);

		var loaded = loader.FromBytes(FakeShim.FakeImage(12, 8));
		var converted = SailfishImage.From(plain);

		Assert.Equal((12f, 8f), (loaded.Width, loaded.Height));
		Assert.Equal((12f, 8f), (converted.Width, converted.Height));
		Assert.Same(converted, SailfishImage.From(converted));
		Assert.Same(converted, converted.ToPlatformImage());
	}
}
