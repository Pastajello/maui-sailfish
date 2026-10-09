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

	public static new readonly PropertyMapper<IView, SailfishNavigationViewHandler> Mapper = new(SailfishPageHandler.Mapper);

	/// <summary>The command mapper this handler uses (the inherited SailfishPageHandler.CommandMapper is not), so an
	/// app's <c>SailfishNavigationViewHandler.CommandMapper.AppendToMapping(...)</c> runs (tracker S13).
	/// <see cref="NavigationCommandMapper"/> is the same object.</summary>
	public static new readonly CommandMapper<IStackNavigationView, SailfishNavigationViewHandler> CommandMapper = NavigationCommandMapper;

	public SailfishNavigationViewHandler() : this(null)
	{
	}

	public SailfishNavigationViewHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
		: base(mapper ?? Mapper, commandMapper ?? NavigationCommandMapper)
	{
	}

	private NavigationPage? Nav => ConnectedView as NavigationPage;

	(IReadOnlyList<Page> Pages, Func<Task>? Pop) ISailfishPageContainer.CurrentStack() =>
		Nav is { } nav
			? SailfishPageContainers.Expand(nav.Navigation.NavigationStack, () => nav.Navigation.PopAsync(), MauiContext)
			: (Array.Empty<Page>(), null);

	/// <summary>A stack at its root shows the root's tabs when the root is itself a container.</summary>
	SailfishTabRow? ISailfishPageContainer.Tabs =>
		Nav?.Navigation.NavigationStack is { Count: 1 } stack ? SailfishPageContainers.Of(stack[0])?.Tabs : null;

	IEnumerable<(string Text, bool Enabled, Action Activate)> ISailfishPageContainer.FlyoutMenu(Page shown) =>
		Nav?.Navigation.NavigationStack is { Count: 1 } stack && SailfishPageContainers.Of(stack[0]) is { } root
			? root.FlyoutMenu(shown)
			: Enumerable.Empty<(string, bool, Action)>();

	bool ISailfishPageContainer.Holds(Page page) =>
		Nav?.Navigation.NavigationStack.Any(p => SailfishPageContainers.Holds(p, page)) == true;

	private static void MapRequestNavigation(SailfishNavigationViewHandler handler, IStackNavigationView view, object? args)
	{
		if (args is not NavigationRequest request)
			return;
		var session = SailfishHandlerCore.SessionOf(handler);
		session?.NoteNavigationRequest(request.Animated);   // navigation timeline start; animated or not
		void Finish() => view.NavigationFinished(request.NavigationStack);
		if (session is not null)
			session.WhenNavigationSettled(Finish);
		else
			Finish();   // no session (a handler built without the app): nothing to wait for
	}
}
