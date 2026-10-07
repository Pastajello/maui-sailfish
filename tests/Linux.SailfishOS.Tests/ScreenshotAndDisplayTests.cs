using Microsoft.Maui.Media;
using Microsoft.Maui.SailfishOS.Platform;
using Xunit;

namespace Linux.SailfishOS.Tests;

/// <summary>Tracker S11 (plan M14): IViewScreenshot, in-memory screenshot results, the refresh rate from the shim's
/// screen info.</summary>
public class ScreenshotAndDisplayTests
{
	// view.CaptureAsync() (MAUI 11) resolves IScreenshot and needs it to be an IViewScreenshot, else it returns null.
	[Fact]
	public void The_screenshot_service_captures_views()
	{
		Assert.IsAssignableFrom<IViewScreenshot>(new SailfishScreenshot());
	}

	[Fact]
	public void A_screenshot_result_reads_its_size_from_the_png_and_serves_the_bytes()
	{
		var png = new byte[32];
		new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10 }.CopyTo(png, 0);
		png[16 + 2] = 0x01; png[16 + 3] = 0x2c;   // width 300
		png[20 + 2] = 0x00; png[20 + 3] = 0x96;   // height 150
		var result = new SailfishScreenshot.ScreenshotImage(png);

		Assert.Equal((300, 150), (result.Width, result.Height));
		using var stream = result.OpenReadAsync().GetAwaiter().GetResult();
		using var copy = new MemoryStream();
		stream.CopyTo(copy);
		Assert.Equal(png, copy.ToArray());
	}

	[Theory]
	[InlineData("{\"screen\":{\"refreshRate\":59.94}}", 59.94f)]
	[InlineData("{\"screen\":{\"dpr\":1}}", 0f)]
	[InlineData("", 0f)]
	[InlineData("not json", 0f)]
	public void The_refresh_rate_comes_from_the_screen_info(string json, float expected)
	{
		Assert.Equal(expected, SailfishDeviceDisplay.ParseRefreshRate(json), 2);
	}
}
