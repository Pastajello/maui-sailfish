using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.SailfishOS.Camera;
using Microsoft.Maui.SailfishOS.Hosting;
using Microsoft.Maui.SailfishOS.Platform;
using Xunit;

namespace Linux.SailfishOS.Tests.Maps;

/// <summary>The camera package: its handler is the app's registration, not a "stock" MAUI one.</summary>
[Collection("renderer")]
public class SailfishCameraViewTests
{
	private sealed class TestApp : Application
	{
	}

	// The factory took every Microsoft.Maui.* assembly for MAUI's own (stock, throwing on this TFM) and skipped its
	// registrations: the backend's own packages are named Microsoft.Maui.SailfishOS.*, so the camera view fell back to an
	// empty container on the phone.
	[Fact]
	public void A_backend_package_handler_is_not_taken_for_a_stock_one()
	{
		SailfishDispatcherProvider.BindLoopThread();
		var builder = MauiApp.CreateBuilder(useDefaults: false);
		builder.UseMauiAppSailfish<TestApp>();
		builder.UseSailfishCamera();
		var factory = (IMauiHandlersFactory)builder.Build().Services.GetService(typeof(IMauiHandlersFactory))!;

		Assert.Equal(typeof(SailfishCameraViewHandler), factory.GetHandlerType(typeof(SailfishCameraView)));
	}

	[Fact]
	public void A_capture_without_the_camera_handler_fails_at_once()
	{
		var camera = new SailfishCameraView();
		var thrown = Record.Exception(() => { _ = camera.CaptureImageAsync(); });
		Assert.IsType<InvalidOperationException>(thrown);
	}
}
