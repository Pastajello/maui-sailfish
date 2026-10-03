using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.SailfishOS.Platform;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>The window's render session: resolved from MAUI's services, it exists before the renderer.</summary>
[Collection("renderer")]
public class RenderSessionTests
{
	// A handler that connects before the renderer exists (the root page's, in SailfishMauiApplication.Run) used to get a
	// host from a detached cache: the reconcile then made a second host for the same element and the handler's pushes
	// went nowhere. Both now come from the session's one cache.
	[Fact]
	public void A_handler_connected_before_the_renderer_holds_the_host_the_reconcile_uses()
	{
		using var statics = new TestStatics();
		var shim = new FakeShim();
		QtHostRuntime.TestShim = shim;
		var context = new SailfishMauiContext(new SailfishServiceOverlay(new ServiceCollection().BuildServiceProvider()));
		var label = new Label { Text = "early" };
		var handler = context.Handlers.GetHandler(label.GetType())!;
		handler.SetMauiContext(context);
		handler.SetVirtualView(label);
		var early = (NativeElementHost)handler.PlatformView!;

		var renderer = new QtHostPageRenderer(new Window(new ContentPage { Content = label }), context);

		Assert.True(renderer.Cache.TryGet(label, out var reconcileHost));
		Assert.Same(early, reconcileHost);
		Assert.Same(renderer, SailfishRenderSession.Of(context.Services)!.Renderer);
	}
}
