using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Platform;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// Modal navigation through MAUI's public platform seam (IModalNavigationPlatformFactory): MAUI keeps the modal stacks,
/// the renderer shows the top modal on the Silica pageStack, and PushModalAsync/PopModalAsync complete once the native
/// stack shows it, as the other platforms complete after their presentation transition.
/// </summary>
/// <param name="session">The overlay passes its session; DI (UseMauiAppSailfish registers this type) does not, and the
/// app's session is looked up when a modal is shown.</param>
internal sealed class SailfishModalNavigationPlatformFactory(SailfishRenderSession? session = null) : IModalNavigationPlatformFactory
{
	public IModalNavigationPlatform? CreateModalNavigationPlatform(IModalNavigationHost host) =>
		new SailfishModalNavigationPlatform(session);
}

internal sealed class SailfishModalNavigationPlatform(SailfishRenderSession? session) : IModalNavigationPlatform
{
	private QtHostPageRenderer? Renderer => (session ?? SailfishRenderSession.OfApp)?.Renderer;

	/// <summary>The native stack exists once the renderer runs; until then MAUI keeps the modals logical.</summary>
	public bool IsReady => Renderer is not null;

	public Task PushModalAsync(Page modal, bool animated) => Settled();

	public Task PopModalAsync(Page modal, bool animated) => Settled();

	public void PageAttached() => Renderer?.RequestPoll();

	private Task Settled()
	{
		if (Renderer is not { } renderer)
			return Task.CompletedTask;
		var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		renderer.RequestPoll();
		renderer.WhenNavigationSettled(() => done.TrySetResult());
		return done.Task;
	}

	public void Dispose()
	{
	}
}
