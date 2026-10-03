using Microsoft.Maui.Controls;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// The render session of the app's window, resolved from MAUI's services as any platform service is. It exists before
/// the renderer: handlers connect while the window and its root page are set up, so the session owns the one host
/// cache (a handler's platform view and the reconcile's host are the same object from the first connect) and forwards
/// to the renderer once <see cref="QtHostPageRenderer"/> attaches. Calls before that are no-ops, as on a platform whose
/// native view does not exist yet. One per app: Sailfish shows one window (docs/parity-plan.md, "One window per app").
/// </summary>
internal sealed class SailfishRenderSession
{
	/// <summary>Hosts by element, shared by the handlers and the reconcile.</summary>
	public NativeHostCache Cache { get; } = new();

	/// <summary>The renderer, once it runs; null before (and in tests that only build handlers).</summary>
	public QtHostPageRenderer? Renderer { get; internal set; }

	/// <summary>A Shell route page is being built for a push: the service overlay builds it with its handler attached
	/// (set by the Shell handler around MAUI's navigation).</summary>
	public bool RoutePageNavigation { get; set; }

	/// <summary>The session of <paramref name="services"/> (a handler's MauiContext.Services), or of the running app.</summary>
	public static SailfishRenderSession? Of(IServiceProvider? services) =>
		(services ?? IPlatformApplication.Current?.Services)?.GetService(typeof(SailfishRenderSession)) as SailfishRenderSession;

	/// <summary>The running app's session (static Sailfish APIs without a MAUI context: BottomSheet, Remorse, images).</summary>
	public static SailfishRenderSession? OfApp => Of(null);

	/// <summary>The session of the element's handler context, else the running app's.</summary>
	public static SailfishRenderSession? OfElement(Element? element) => Of(element?.Handler?.MauiContext?.Services);

	/// <summary>A navigation sync + reconcile on the next loop turn (no-op before the renderer runs).</summary>
	public void RequestPoll() => Renderer?.RequestPoll();

	/// <summary>A MAUI push/pop was requested: starts the navigation timeline and kicks a poll.</summary>
	public void NoteNavigationRequest() => Renderer?.NoteNavigationRequest();

	/// <summary>A container handler changed its children: its subtree is diffed on the next loop turn.</summary>
	public void RequestSubtree(IView container) => Renderer?.RequestSubtree(container);

	public void RequestLayout() => Renderer?.RequestLayout();

	public void RequestScrollGeometry() => Renderer?.RequestScrollGeometry();
}
