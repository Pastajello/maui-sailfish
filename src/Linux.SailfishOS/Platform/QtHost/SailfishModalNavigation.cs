using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Platform;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// Modal navigation through MAUI's public platform seam (IModalNavigationPlatformFactory): MAUI keeps the modal stacks,
/// the renderer shows the top modal on the Silica pageStack, and PushModalAsync/PopModalAsync complete once the native
/// stack shows it, as the other platforms complete after their presentation transition.
/// </summary>
internal sealed class SailfishModalNavigationPlatformFactory : IModalNavigationPlatformFactory
{
	public IModalNavigationPlatform? CreateModalNavigationPlatform(IModalNavigationHost host) =>
		new SailfishModalNavigationPlatform();
}

internal sealed class SailfishModalNavigationPlatform : IModalNavigationPlatform
{
	/// <summary>The native stack exists once the renderer runs; until then MAUI keeps the modals logical.</summary>
	public bool IsReady => QtHostPageRenderer.Current is not null;

	public Task PushModalAsync(Page modal, bool animated) => Settled();

	public Task PopModalAsync(Page modal, bool animated) => Settled();

	public void PageAttached() => QtHostPageRenderer.RequestPoll();

	private static Task Settled()
	{
		if (QtHostPageRenderer.Current is not { } renderer)
			return Task.CompletedTask;
		var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		QtHostPageRenderer.RequestPoll();
		renderer.WhenNavigationSettled(() => done.TrySetResult());
		return done.Task;
	}

	public void Dispose()
	{
	}
}
