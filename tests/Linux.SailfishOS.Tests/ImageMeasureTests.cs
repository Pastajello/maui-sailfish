using System.Buffers.Binary;
using Linux.SailfishOS.Tests.Renderer;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.SailfishOS.Handlers;
using Microsoft.Maui.SailfishOS.Platform;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;

namespace Linux.SailfishOS.Tests;

/// <summary>An Image without a size request takes its source's own size, as Android's ImageView does.</summary>
[Collection("renderer")]
public class ImageMeasureTests
{
	private static byte[] Png(int w, int h)
	{
		var b = new byte[33];
		new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(b, 0);
		BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(16), w);
		BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(20), h);
		return b;
	}

	private static byte[] Jpeg(int w, int h)
	{
		var b = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE0, 0, 16 };
		b.AddRange(new byte[14]);   // APP0 body
		b.AddRange(new byte[] { 0xFF, 0xFF });   // a fill byte before the next marker
		b.AddRange(new byte[] { 0xC2, 0, 17, 8, (byte)(h >> 8), (byte)h, (byte)(w >> 8), (byte)w });
		b.AddRange(new byte[10]);
		return b.ToArray();
	}

	private static byte[] Gif(int w, int h) =>
		new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a', (byte)w, (byte)(w >> 8), (byte)h, (byte)(h >> 8), 0, 0 };

	private static byte[] Bmp(int w, int h)
	{
		var b = new byte[30];
		b[0] = (byte)'B';
		b[1] = (byte)'M';
		BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(18), w);
		BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(22), -h);   // top-down
		return b;
	}

	private static byte[] WebPExtended(int w, int h)
	{
		var b = new byte[30];
		"RIFF"u8.CopyTo(b);
		"WEBPVP8X"u8.CopyTo(b.AsSpan(8));
		b[24] = (byte)(w - 1);
		b[25] = (byte)((w - 1) >> 8);
		b[27] = (byte)(h - 1);
		b[28] = (byte)((h - 1) >> 8);
		return b;
	}

	private static byte[] WebPLossless(int w, int h)
	{
		var b = new byte[30];
		"RIFF"u8.CopyTo(b);
		"WEBPVP8L"u8.CopyTo(b.AsSpan(8));
		var bits = (uint)(w - 1) | ((uint)(h - 1) << 14);
		b[20] = 0x2F;
		BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(21), bits);
		return b;
	}

	[Theory]
	[InlineData("png")]
	[InlineData("jpeg")]
	[InlineData("gif")]
	[InlineData("bmp")]
	[InlineData("webp-x")]
	[InlineData("webp-l")]
	public void Header_gives_the_pixel_size_of_each_format(string format)
	{
		var bytes = format switch
		{
			"png" => Png(300, 120),
			"jpeg" => Jpeg(300, 120),
			"gif" => Gif(300, 120),
			"bmp" => Bmp(300, 120),
			"webp-x" => WebPExtended(300, 120),
			_ => WebPLossless(300, 120),
		};
		Assert.Equal((300, 120), ImageHeader.PixelSize(new MemoryStream(bytes)));
	}

	[Fact]
	public void Header_of_something_else_is_unknown()
	{
		Assert.Null(ImageHeader.PixelSize(new MemoryStream("not an image"u8.ToArray())));
		Assert.Null(ImageHeader.PixelSize(new MemoryStream(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 4 })));   // cut short
	}

	[Fact]
	public void Packaged_images_measure_at_base_size_or_one_dp_per_pixel_and_other_files_at_pixels_over_density()
	{
		using var harness = new RendererHarness(new ContentPage());
		var density = SailfishDisplay.Density;
		Assert.NotEqual(1, density);
		var images = Path.Combine(AppContext.BaseDirectory, "images");
		Directory.CreateDirectory(images);
		var manifest = Path.Combine(images, QtHostImages.ResizedManifest);
		var other = Path.Combine(Path.GetTempPath(), "sf-measure-" + Guid.NewGuid().ToString("N") + ".png");
		try
		{
			File.WriteAllLines(manifest, new[] { "measure_vector" });
			File.WriteAllBytes(Path.Combine(images, "measure_vector.png"), Png(400, 200));   // BaseSize 100×50
			File.WriteAllBytes(Path.Combine(images, "measure_copied.png"), Png(64, 32));     // no BaseSize: copied
			File.WriteAllBytes(other, Png(200, 100));

			Assert.Equal(new Size(100, 50), QtHostImages.IntrinsicSize(ImageSource.FromFile("measure_vector.png")));
			Assert.Equal(new Size(64, 32), QtHostImages.IntrinsicSize(ImageSource.FromFile("measure_copied.png")));
			Assert.Equal(new Size(200 / density, 100 / density), QtHostImages.IntrinsicSize(ImageSource.FromFile(other)));
			Assert.Null(QtHostImages.IntrinsicSize(ImageSource.FromFile("measure_missing.png")));
		}
		finally
		{
			File.Delete(manifest);
			File.Delete(Path.Combine(images, "measure_vector.png"));
			File.Delete(Path.Combine(images, "measure_copied.png"));
			File.Delete(other);
		}
	}

	[Fact]
	public void Image_sizes_to_its_source_and_keeps_the_aspect_ratio_under_a_request_or_a_constraint()
	{
		using var harness = new RendererHarness(new ContentPage());
		var density = SailfishDisplay.Density;
		var file = Path.Combine(Path.GetTempPath(), "sf-measure-" + Guid.NewGuid().ToString("N") + ".png");
		File.WriteAllBytes(file, Png((int)(200 * density), (int)(100 * density)));   // 200×100 dp
		try
		{
			Size Measure(Image image, double wc = double.PositiveInfinity, double hc = double.PositiveInfinity) =>
				SailfishMeasure.Frame(image, wc, hc, SailfishMeasure.Image);

			Assert.Equal(new Size(200, 100), Measure(new Image { Source = file }));
			// A narrower constraint shrinks both sides (adjustViewBounds).
			Assert.Equal(new Size(100, 50), Measure(new Image { Source = file }, wc: 100));
			// One requested side gives the other.
			Assert.Equal(new Size(400, 200), Measure(new Image { Source = file, WidthRequest = 400 }));
			Assert.Equal(new Size(80, 40), Measure(new Image { Source = file, HeightRequest = 40 }));
			// Both requested: the requests.
			Assert.Equal(new Size(30, 30), Measure(new Image { Source = file, WidthRequest = 30, HeightRequest = 30 }));
			// Nothing to show: 0 × 0, not a 100 × 100 box.
			Assert.Equal(Size.Zero, Measure(new Image()));
			Assert.Equal(Size.Zero, Measure(new Image { Source = "measure_missing.png" }));
		}
		finally
		{
			File.Delete(file);
		}
	}

	[Fact]
	public void A_remote_image_measures_empty_until_the_adapter_reports_its_size()
	{
		using var harness = new RendererHarness(new ContentPage());
		var density = SailfishDisplay.Density;
		var source = new UriImageSource { Uri = new Uri("https://example.invalid/measure-" + Guid.NewGuid().ToString("N") + ".jpg") };
		Assert.Null(QtHostImages.IntrinsicSize(source));
		var url = QtHostImages.Resolve(source)!;
		Assert.True(QtHostImages.ReportNaturalSize(url, (int)(120 * density), (int)(60 * density)));
		Assert.False(QtHostImages.ReportNaturalSize(url, (int)(120 * density), (int)(60 * density)));   // no change
		Assert.Equal(new Size(120, 60), QtHostImages.IntrinsicSize(source));
	}

	// Two list rows showing one remote URL both measure 0 × 0 while it loads. The first report records the size and
	// re-measures its row; the second finds the size known, and its row must still grow (handoff W1.7).
	[Fact]
	public void Every_row_image_of_a_shared_remote_url_takes_the_size_once_it_loaded()
	{
		var url = "https://example.invalid/shared-" + Guid.NewGuid().ToString("N") + ".jpg";
		var list = new CollectionView
		{
			ItemsSource = new[] { url, url, url },
			ItemTemplate = new DataTemplate(() =>
			{
				var image = new Image { HorizontalOptions = LayoutOptions.Start };
				image.SetBinding(Image.SourceProperty, ".");
				return image;
			}),
			HeightRequest = 600,
		};
		using var h = new RendererHarness(new ContentPage { Title = "T", Content = list });
		var native = h.Shim.ByUri("list-view").Single();
		for (var i = 0; i < 4; i++)
			h.Poll();
		foreach (var row in new[] { 0, 1 })
		{
			var dg = $"maui_{native.Id}__r{row}";
			h.Shim.AddNative(dg);
			h.Renderer.HandleNativeEvent("list-item-attached", $"{{\"id\":\"{native.Id}\",\"row\":{row},\"dg\":\"{dg}\"}}");
		}
		var hosts = h.Shim.ByUri("image").Where(o => !o.Destroyed).ToList();
		Assert.Equal(2, hosts.Count);
		var rows = new[] { (Image)h.Renderer.Collection.RowView(0)!, (Image)h.Renderer.Collection.RowView(1)! };
		Assert.All(rows, image => Assert.Equal(0, image.Height));
		var resolved = QtHostImages.Resolve(new UriImageSource { Uri = new Uri(url) })!;
		var density = SailfishDisplay.Density;
		foreach (var host in hosts)
			h.Renderer.HandleNativeEvent("image-natural",
				$"{{\"id\":\"{host.Id}\",\"source\":\"{resolved}\",\"width\":{(int)(120 * density)},\"height\":{(int)(60 * density)}}}");
		for (var i = 0; i < 4; i++)
			h.Renderer.KickedPoll();   // the rows re-measure in the list's own pending pass
		Assert.All(rows, image => Assert.Equal(60, Math.Round(image.Height)));
	}
}

