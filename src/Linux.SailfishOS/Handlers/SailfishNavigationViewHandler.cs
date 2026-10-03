using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;

namespace Microsoft.Maui.SailfishOS.Handlers;

/// <summary>
/// NavigationPage handler: answers RequestNavigation with <see cref="IStackNavigation.NavigationFinished"/> once the
/// Silica pageStack shows the new stack (QtHostPageRenderer.WhenNavigationSettled), as the other platforms finish after
/// their native transition; without it PushAsync/PopAsync never complete.
/// </summary>
public class SailfishNavigationViewHandler : SailfishPageHandler, ISailfishPageContainer
{
	public static readonly CommandMapper<IStackNavigationView, SailfishNavigationViewHandler> NavigationCommandMapper =
		new(SailfishViewMapper.CommandMapper)
		{
			[nameof(IStackNavigation.RequestNavigation)] = MapRequestNavigation,
		};

	public SailfishNavigationViewHandler() : base(Mapper, NavigationCommandMapper)
	{
	}

	private NavigationPage? Nav => ((IElementHandler)this).VirtualView as NavigationPage;

	(IReadOnlyList<Page> Pages, Func<Task>? Pop) ISailfishPageContainer.CurrentStack() =>
		Nav is { } nav
			? SailfishPageContainers.Expand(nav.Navigation.NavigationStack, () => nav.Navigation.PopAsync(), MauiContext)
			: (Array.Empty<Page>(), null);

	/// <summary>A stack at its root shows the root's tabs when the root is itself a container.</summary>
	(List<string> Titles, int Index, Action<int> Select)? ISailfishPageContainer.Tabs =>
		Nav?.Navigation.NavigationStack is { Count: 1 } stack ? SailfishPageContainers.Of(stack[0], MauiContext)?.Tabs : null;

	IEnumerable<(string Text, bool Enabled, Action Activate)> ISailfishPageContainer.FlyoutMenu(Page shown) =>
		Nav?.Navigation.NavigationStack is { Count: 1 } stack && SailfishPageContainers.Of(stack[0], MauiContext) is { } root
			? root.FlyoutMenu(shown)
			: Enumerable.Empty<(string, bool, Action)>();

	bool ISailfishPageContainer.Holds(Page page) =>
		Nav?.Navigation.NavigationStack.Any(p => SailfishPageContainers.Holds(p, page, MauiContext)) == true;

	private static void MapRequestNavigation(SailfishNavigationViewHandler handler, IStackNavigationView view, object? args)
	{
		if (args is not NavigationRequest request)
			return;
		SailfishHandlerCore.SessionOf(handler)?.NoteNavigationRequest();   // navigation timeline start
		void Finish() => view.NavigationFinished(request.NavigationStack);
		if (SailfishHandlerCore.SessionOf(handler)?.Renderer is { } renderer)
			renderer.WhenNavigationSettled(Finish);
		else
			Finish();   // no native stack to wait for
	}
}
