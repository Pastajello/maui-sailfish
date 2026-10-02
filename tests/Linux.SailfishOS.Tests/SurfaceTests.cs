using System.Reflection;
using Linux.SailfishOS.Tests.Renderer;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.SailfishOS.Handlers;
using Microsoft.Maui.SailfishOS.Hosting;
using Microsoft.Maui.SailfishOS.Platform;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;

[assembly: AssemblyMetadata(SailfishExtensions.MetadataKey, "Linux.SailfishOS.Tests.SurfaceTests+TestExtension, Linux.SailfishOS.Tests")]

namespace Linux.SailfishOS.Tests;

/// <summary>The platform primitives library handlers draw and take input through: <see cref="QtHostSurface"/>,
/// library handler replacement, Sailfish extensions and image-source resolvers.</summary>
[Collection("renderer")]
public class SurfaceTests
{
	private static NativeElementHost PaintedHost(RendererHarness harness, ContentView view)
	{
		var host = Assert.IsType<NativeElementHost>(view.Handler!.PlatformView);
		Assert.True(host.IsAttached);
		return host;
	}

	[Fact]
	public void Frame_requests_coalesce_and_a_request_made_during_a_frame_waits_for_the_next()
	{
		using var harness = new RendererHarness(new ContentPage());
		var runs = new List<string>();
		Action second = null!;
		Action first = () =>
		{
			runs.Add("first");
			QtHostSurface.RequestFrame(second);   // e.g. an animation asking for the following frame
		};
		second = () => runs.Add("second");
		var before = harness.Shim.FrameRequests;

		QtHostSurface.RequestFrame(first);
		QtHostSurface.RequestFrame(first);   // the same callback again: it still runs once
		Assert.True(harness.Shim.FrameRequests > before);   // every request reaches the shim, which coalesces them
		Assert.True(QtHostSurface.IsFrameRequested(first));

		QtHostSurface.RunFrame();
		Assert.Equal(new[] { "first" }, runs);
		Assert.True(QtHostSurface.IsFrameRequested(second));

		QtHostSurface.RunFrame();
		Assert.Equal(new[] { "first", "second" }, runs);
		QtHostSurface.RunFrame();   // nothing pending: nothing runs
		Assert.Equal(2, runs.Count);
	}

	[Fact]
	public void A_throwing_frame_callback_does_not_stop_the_others()
	{
		using var harness = new RendererHarness(new ContentPage());
		var ran = false;
		QtHostSurface.RequestFrame(() => throw new InvalidOperationException("paint failed"));
		QtHostSurface.RequestFrame(() => ran = true);
		QtHostSurface.RunFrame();
		Assert.True(ran);
	}

	[Fact]
	public void Commit_sends_the_pixels_of_an_attached_host_and_release_frees_them()
	{
		var view = new ContentView { BackgroundColor = Colors.Red, WidthRequest = 20, HeightRequest = 10 };
		using var harness = new RendererHarness(new ContentPage { Content = view });
		var host = PaintedHost(harness, view);
		var pixels = new byte[2 * 2 * 4 + 8];   // 2x2 with 4 bytes of row padding
		pixels[0] = 255;
		pixels[3] = 255;
		var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
		try
		{
			Assert.True(QtHostSurface.Commit(host, handle.AddrOfPinnedObject(), 2, 2, 12));
		}
		finally
		{
			handle.Free();
		}
		var commit = Assert.Single(harness.Shim.SurfaceCommits);
		Assert.Equal(host.NativeHandle, commit.Handle);
		Assert.Equal((2, 2), (commit.Width, commit.Height));
		Assert.Equal(new byte[] { 255, 0, 0, 255 }, commit.Pixels[..4]);

		QtHostSurface.Release(host);
		Assert.Equal(0, harness.Shim.SurfaceCommits[^1].Width);
	}

	[Fact]
	public void Commit_on_a_host_without_a_native_object_is_refused()
	{
		using var harness = new RendererHarness(new ContentPage());
		var host = new NativeElementHost("e999", "content-view", new ContentView());
		Assert.False(QtHostSurface.Commit(host, (IntPtr)1, 2, 2, 8));
		Assert.Empty(harness.Shim.SurfaceCommits);
	}

	[Fact]
	public void Touch_pointer_ids_are_handed_out_like_android_pointer_ids()
	{
		var view = new ContentView { BackgroundColor = Colors.Red, WidthRequest = 20, HeightRequest = 10 };
		using var harness = new RendererHarness(new ContentPage { Content = view });
		var host = PaintedHost(harness, view);
		var seen = new List<SurfaceTouch>();
		QtHostSurface.SetTouch(host, t =>
		{
			seen.Add(t);
			return true;
		});
		Assert.True(harness.Shim.SurfaceTouch[host.NativeHandle]);
		var h = host.NativeHandle;

		// Qt ids count up (17, 18, 19); Android reuses the lowest free slot.
		Assert.True(QtHostSurface.DeliverTouch(h, SurfaceTouchAction.Pressed, 17, 1, 2, 1, false, 0));
		QtHostSurface.DeliverTouch(h, SurfaceTouchAction.Pressed, 18, 3, 4, 1, false, 0);
		QtHostSurface.DeliverTouch(h, SurfaceTouchAction.Released, 17, 1, 2, 0, false, 0);
		QtHostSurface.DeliverTouch(h, SurfaceTouchAction.Pressed, 19, 5, 6, 1, false, 0);
		QtHostSurface.DeliverTouch(h, SurfaceTouchAction.Moved, 18, 7, 8, 1, false, 0);
		QtHostSurface.DeliverTouch(h, SurfaceTouchAction.Moved, 99, 0, 0, 1, false, 0);   // never pressed: dropped

		Assert.Equal(new[] { 0, 1, 0, 0, 1 }, seen.Select(t => t.PointerId));
		Assert.Equal(new[] { SurfaceTouchAction.Pressed, SurfaceTouchAction.Pressed, SurfaceTouchAction.Released,
			SurfaceTouchAction.Pressed, SurfaceTouchAction.Moved }, seen.Select(t => t.Action));
		Assert.Equal((7d, 8d), (seen[^1].X, seen[^1].Y));

		QtHostSurface.SetTouch(host, null);
		Assert.False(harness.Shim.SurfaceTouch[h]);
		Assert.False(QtHostSurface.DeliverTouch(h, SurfaceTouchAction.Pressed, 1, 0, 0, 1, false, 0));
	}

	[Fact]
	public void The_handler_result_of_a_touch_is_what_the_surface_returns()
	{
		var view = new ContentView { BackgroundColor = Colors.Red, WidthRequest = 20, HeightRequest = 10 };
		using var harness = new RendererHarness(new ContentPage { Content = view });
		var host = PaintedHost(harness, view);
		QtHostSurface.SetTouch(host, _ => false);
		Assert.False(QtHostSurface.DeliverTouch(host.NativeHandle, SurfaceTouchAction.Pressed, 1, 0, 0, 1, false, 0));
		QtHostSurface.SetTouch(host, _ => throw new InvalidOperationException());
		Assert.False(QtHostSurface.DeliverTouch(host.NativeHandle, SurfaceTouchAction.Pressed, 2, 0, 0, 1, false, 0));
		QtHostSurface.SetTouch(host, null);
	}

	/* --- library handler replacement --- */

	private sealed class StubView : View
	{
	}

	private sealed class DerivedStubView : View
	{
	}

	// A library's plain-net stub, like SkiaSharp's SKCanvasViewHandler: no platform view on this TFM.
	private class StubLibraryHandler : ViewHandler<IView, object>
	{
		public StubLibraryHandler() : base(ViewMapper)
		{
		}

		protected override object CreatePlatformView() => throw new NotImplementedException();
	}

	private sealed class AppSubclassOfStubHandler : StubLibraryHandler
	{
	}

	private sealed class SailfishStubHandler : SailfishContainerHandler
	{
	}

	private sealed class TestApp : Application
	{
	}

	[Fact]
	public void A_replaced_library_handler_resolves_to_the_sailfish_one_wherever_it_is_registered()
	{
		SailfishHandlersFactory.ReplaceLibraryHandler<StubLibraryHandler, SailfishStubHandler>();
		var builder = MauiApp.CreateBuilder(useDefaults: false);
		builder.UseMauiAppSailfish<TestApp>();
		builder.ConfigureMauiHandlers(h =>
		{
			h.AddHandler<StubView, StubLibraryHandler>();
			h.AddHandler<DerivedStubView, AppSubclassOfStubHandler>();
		});
		using var app = builder.Build();
		var factory = Assert.IsType<SailfishHandlersFactory>(app.Services.GetService(typeof(IMauiHandlersFactory)));

		Assert.IsType<SailfishStubHandler>(factory.GetHandler(typeof(StubView)));
		Assert.Equal(typeof(SailfishStubHandler), factory.GetHandlerType(typeof(StubView)));
		Assert.IsType<SailfishStubHandler>(factory.GetHandler(typeof(DerivedStubView)));
	}

	/* --- Sailfish extensions --- */

	public static class TestExtension
	{
		public static int Calls;

		public static void Register() => Calls++;
	}

	[Fact]
	public void An_extension_named_in_the_app_assembly_registers_once()
	{
		SailfishExtensions.Load(typeof(SurfaceTests).Assembly);
		SailfishExtensions.Load(typeof(SurfaceTests).Assembly);
		Assert.Equal(1, TestExtension.Calls);
		Assert.Contains("Linux.SailfishOS.Tests.SurfaceTests+TestExtension, Linux.SailfishOS.Tests", SailfishExtensions.Registered);
	}

	/* --- image sources --- */

	private sealed class LibraryImageSource : ImageSource
	{
		public string Name { get; init; } = "";
	}

	[Fact]
	public void A_library_image_source_resolves_through_its_resolver()
	{
		QtHostImageSources.Register(s => s is LibraryImageSource { Name: "ok" } ? "file:///tmp/lib.png" : null);
		QtHostImageSources.Register(s => s is LibraryImageSource { Name: "bad" } ? throw new InvalidOperationException() : null);
		Assert.Equal("file:///tmp/lib.png", QtHostImages.Resolve(new LibraryImageSource { Name = "ok" }));
		Assert.Null(QtHostImages.Resolve(new LibraryImageSource { Name = "bad" }));
		Assert.Null(QtHostImages.Resolve(new LibraryImageSource { Name = "other" }));
	}
}
