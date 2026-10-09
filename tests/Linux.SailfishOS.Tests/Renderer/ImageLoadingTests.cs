using Microsoft.Maui.Controls;
using Microsoft.Maui.SailfishOS.Platform;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>Tracker S25 (plan M6 steps 3, 5, 6): Image.IsLoading, cancelling a replaced source's pending read, stream
/// GIFs, the decode cap of unsized images.</summary>
[Collection("renderer")]
public sealed class ImageLoadingTests
{
	private static RendererHarness Start(View content)
	{
		var h = new RendererHarness(new ContentPage { Title = "Images", Content = new VerticalStackLayout { Children = { content } } });
		for (var i = 0; i < 4; i++)
			h.Poll();
		return h;
	}

	[Fact]
	public void Is_loading_until_the_adapter_reports_the_image()
	{
		var logo = Path.Combine(Path.GetTempPath(), $"s25-{Guid.NewGuid():N}.png");
		File.WriteAllBytes(logo, new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10 });
		try
		{
			var image = new Image { Source = ImageSource.FromFile(logo), WidthRequest = 50, HeightRequest = 50 };
			using var h = Start(image);
			Assert.True(image.IsLoading);

			var host = h.Renderer.CurrentHosts.First(x => ReferenceEquals(x.Element, image));
			var url = QtHostImages.Resolve(image.Source)!;
			h.Renderer.HandleNativeEvent("image-loaded", $"{{\"id\":\"{host.Id}\",\"source\":\"{url}\"}}");
			Assert.False(image.IsLoading);

			// Source mapped again (a re-connect) for the image already shown: it does not load again.
			image.Handler!.UpdateValue(nameof(Image.Source));
			Assert.False(image.IsLoading);
		}
		finally
		{
			File.Delete(logo);
		}
	}

	[Fact]
	public void A_replaced_source_cancels_its_pending_read()
	{
		CancellationToken seen = default;
		var never = new TaskCompletionSource<Stream>();
		var first = new StreamImageSource { Stream = token => { seen = token; return never.Task; } };
		var image = new Image { Source = first, WidthRequest = 50, HeightRequest = 50 };
		using var h = Start(image);
		Assert.True(QtHostImages.IsPending(first));
		Assert.True(image.IsLoading);
		var cancelled = QtHostImages.Cancelled;

		image.Source = new FontImageSource { Glyph = "x" };
		h.Poll();

		Assert.True(seen.IsCancellationRequested);
		Assert.Equal(cancelled + 1, QtHostImages.Cancelled);
		Assert.False(QtHostImages.IsPending(first));   // forgotten: a later use reads it again
	}

	[Fact]
	public void A_gif_stream_is_cached_as_a_gif_so_it_animates()
	{
		var gif = new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a', 1, 0, 1, 0, 0, 0, 0 };
		var source = new StreamImageSource { Stream = _ => Task.FromResult<Stream>(new MemoryStream(gif)) };
		var image = new Image { Source = source, WidthRequest = 50, HeightRequest = 50 };
		using var h = Start(image);
		for (var i = 0; i < 4; i++)
			h.Poll();

		var url = QtHostImages.Resolve(source);
		Assert.NotNull(url);
		Assert.EndsWith(".gif", url);
		Assert.True(QtHostImages.IsGif(new Uri(url!).LocalPath));
	}

	[Fact]
	public void An_unsized_image_carries_the_decode_cap()
	{
		var file = Path.Combine(Path.GetTempPath(), $"s25-{Guid.NewGuid():N}.png");
		File.WriteAllBytes(file, new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10 });
		try
		{
			var image = new Image { Source = ImageSource.FromFile(file) };
			using var h = Start(image);
			var props = QtHostImages.Props(image);
			Assert.NotNull(props);
			Assert.Equal(QtHostImages.DecodeCap, props!["mauiDecodeCap"]);
			Assert.True(QtHostImages.DecodeCap > 0);
		}
		finally
		{
			File.Delete(file);
		}
	}
}
