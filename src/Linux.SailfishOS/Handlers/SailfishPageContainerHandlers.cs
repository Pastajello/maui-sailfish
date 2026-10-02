using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Handlers;

/// <summary>
/// A page that shows other pages (NavigationPage, Shell, TabbedPage, FlyoutPage), as ShellHandler/TabbedPageHandler/
/// FlyoutPageHandler are elsewhere: the handler decides which page stack is shown, what one Back pops, which tabs the
/// chrome offers and what the flyout menu holds. The renderer maps the answer onto the one Silica pageStack.
/// </summary>
internal interface ISailfishPageContainer
{
	/// <summary>The stack shown now, bottom → top (nested containers expanded), and what one Back does (null = nothing).</summary>
	(IReadOnlyList<Page> Pages, Func<Task>? Pop) CurrentStack();

	/// <summary>The tab bar shown with the stack's root page; null = none.</summary>
	(List<string> Titles, int Index, Action<int> Select)? Tabs { get; }

	/// <summary>A second row under <see cref="Tabs"/> (a Shell section's contents when the item has several sections);
	/// null = none.</summary>
	(List<string> Titles, int Index, Action<int> Select)? SubTabs => null;

	/// <summary>Pulley entries that open the container's flyout from <paramref name="shown"/> (its stack root).</summary>
	IEnumerable<(string Text, bool Enabled, Action Activate)> FlyoutMenu(Page shown);

	/// <summary>Whether <paramref name="page"/> is one of this container's pages (on a stack, a tab, the flyout, a
	/// realized Shell content), directly or through a nested container: a page the app can still show.</summary>
	bool Holds(Page page);
}

/// <summary>Resolves containers through their handlers, attaching one where a nested container has none yet (only
/// the window's root page gets its handler from the window; a TabbedPage as a flyout's Detail does not).</summary>
internal static class SailfishPageContainers
{
	private static readonly IReadOnlyList<Page> NoPages = Array.Empty<Page>();

	internal static bool IsContainer(Page? page) => page is NavigationPage or Shell or TabbedPage or FlyoutPage;

	internal static ISailfishPageContainer? Of(Page? page, IMauiContext? context)
	{
		if (!IsContainer(page))
			return null;
		if (page!.Handler is null && context is not null)
			SailfishHandlersFactory.AttachRootHandler(page, context);
		return page.Handler as ISailfishPageContainer;
	}

	/// <summary>Whether <paramref name="candidate"/> is <paramref name="page"/> or held by it (a container).</summary>
	internal static bool Holds(Page? page, Page candidate, IMauiContext? context) =>
		page is not null && (ReferenceEquals(page, candidate) || Of(page, context)?.Holds(candidate) == true);

	/// <summary>The stack <paramref name="page"/> shows: a container's current stack, else the page alone.</summary>
	internal static (IReadOnlyList<Page> Pages, Func<Task>? Pop) StackOf(Page? page, IMauiContext? context)
	{
		if (page is null)
			return (NoPages, null);
		return Of(page, context) is { } container ? container.CurrentStack() : (new[] { page }, null);
	}

	/// <summary>A stack whose top page is itself a container (a NavigationPage whose root is a TabbedPage) continues
	/// into that container's stack; Back pops the innermost level that can.</summary>
	internal static (IReadOnlyList<Page> Pages, Func<Task>? Pop) Expand(IReadOnlyList<Page> pages, Func<Task>? pop,
	                                                                    IMauiContext? context)
	{
		if (pages.Count == 0 || !IsContainer(pages[^1]))
			return (pages, pop);
		var (inner, innerPop) = StackOf(pages[^1], context);
		var all = new List<Page>(pages.Count - 1 + inner.Count);
		for (var i = 0; i < pages.Count - 1; i++)
			all.Add(pages[i]);
		all.AddRange(inner);
		return (all, inner.Count > 1 && innerPop is not null ? innerPop : pages.Count > 1 ? pop : innerPop);
	}
}

/// <summary>TabbedPage: the selected child's stack, the children as tabs.</summary>
public class SailfishTabbedPageHandler : SailfishPageHandler, ISailfishPageContainer
{
	private readonly EventHandler _onCurrentPage = (_, _) => QtHostPageRenderer.RequestPoll();

	private TabbedPage? Tabbed => ((IElementHandler)this).VirtualView as TabbedPage;

	protected override void ConnectHandler(object platformView)
	{
		base.ConnectHandler(platformView);
		if (Tabbed is { } tabbed)
			tabbed.CurrentPageChanged += _onCurrentPage;
	}

	protected override void DisconnectHandler(object platformView)
	{
		if (Tabbed is { } tabbed)
			tabbed.CurrentPageChanged -= _onCurrentPage;
		base.DisconnectHandler(platformView);
	}

	(IReadOnlyList<Page> Pages, Func<Task>? Pop) ISailfishPageContainer.CurrentStack() =>
		Tabbed is { } tabbed
			? tabbed.CurrentPage is { } child ? SailfishPageContainers.StackOf(child, MauiContext) : (new Page[] { tabbed }, null)
			: (Array.Empty<Page>(), null);

	(List<string> Titles, int Index, Action<int> Select)? ISailfishPageContainer.Tabs
	{
		get
		{
			if (Tabbed is not { } tabbed || tabbed.Children.Count < 2)
				return null;
			var children = tabbed.Children;
			return (children.Select(c => c.Title ?? string.Empty).ToList(),
				Math.Max(0, children.IndexOf(tabbed.CurrentPage)),
				i => { if (i >= 0 && i < children.Count) tabbed.CurrentPage = children[i]; });
		}
	}

	IEnumerable<(string Text, bool Enabled, Action Activate)> ISailfishPageContainer.FlyoutMenu(Page shown) =>
		Tabbed?.CurrentPage is { } child && SailfishPageContainers.Of(child, MauiContext) is { } inner
			? inner.FlyoutMenu(shown)
			: Enumerable.Empty<(string, bool, Action)>();

	bool ISailfishPageContainer.Holds(Page page) =>
		Tabbed?.Children.Any(child => SailfishPageContainers.Holds(child, page, MauiContext)) == true;
}

/// <summary>FlyoutPage: the Detail's stack, plus the Flyout page while presented (a native push, closed by Back).</summary>
public class SailfishFlyoutPageHandler : SailfishPageHandler, ISailfishPageContainer
{
	private readonly EventHandler _onPresented = (_, _) => QtHostPageRenderer.RequestPoll();

	private FlyoutPage? Flyout => ((IElementHandler)this).VirtualView as FlyoutPage;

	protected override void ConnectHandler(object platformView)
	{
		base.ConnectHandler(platformView);
		if (Flyout is { } flyout)
			flyout.IsPresentedChanged += _onPresented;
	}

	protected override void DisconnectHandler(object platformView)
	{
		if (Flyout is { } flyout)
			flyout.IsPresentedChanged -= _onPresented;
		base.DisconnectHandler(platformView);
	}

	(IReadOnlyList<Page> Pages, Func<Task>? Pop) ISailfishPageContainer.CurrentStack()
	{
		if (Flyout is not { } flyout)
			return (Array.Empty<Page>(), null);
		var (detailPages, detailPop) = SailfishPageContainers.StackOf(flyout.Detail, MauiContext);
		if (!flyout.IsPresented || flyout.Flyout is null)
			return (detailPages, detailPop);
		var pages = new List<Page>(detailPages) { flyout.Flyout };
		return (pages, () => { flyout.IsPresented = false; return Task.CompletedTask; });
	}

	(List<string> Titles, int Index, Action<int> Select)? ISailfishPageContainer.Tabs =>
		Flyout is { IsPresented: false, Detail: { } detail } ? SailfishPageContainers.Of(detail, MauiContext)?.Tabs : null;

	IEnumerable<(string Text, bool Enabled, Action Activate)> ISailfishPageContainer.FlyoutMenu(Page shown)
	{
		// The flyout is its own page: one entry presents it through a native push.
		if (Flyout is not { IsPresented: false, Flyout: { } flyoutRoot } flyout)
			yield break;
		if (SailfishPageContainers.StackOf(flyout.Detail, MauiContext).Pages.Count != 1)
			yield break;
		var title = string.IsNullOrEmpty(flyoutRoot.Title) ? "Menu" : flyoutRoot.Title;
		yield return (title, true, () => flyout.IsPresented = true);
	}

	bool ISailfishPageContainer.Holds(Page page) =>
		Flyout is { } flyout &&
		(SailfishPageContainers.Holds(flyout.Detail, page, MauiContext) || SailfishPageContainers.Holds(flyout.Flyout, page, MauiContext));
}

/// <summary>Shell: the current section's stack (its root slot materialized from the ShellContent), its sections or
/// contents as tabs, the flyout items as the pulley menu.</summary>
public class SailfishShellHandler : SailfishPageHandler, ISailfishPageContainer
{
	private readonly EventHandler<ShellNavigatedEventArgs> _onNavigated = (_, _) =>
	{
		Platform.SailfishServiceOverlay.RoutePageNavigation = false;
		QtHostPageRenderer.RequestPoll();
	};

	// Shell raises Navigating before it builds a pushed route page: only those pages come from the service overlay.
	private readonly EventHandler<ShellNavigatingEventArgs> _onNavigating = (_, e) =>
		Platform.SailfishServiceOverlay.RoutePageNavigation = e.Source is ShellNavigationSource.Push or ShellNavigationSource.Insert;

	private Shell? ShellView => ((IElementHandler)this).VirtualView as Shell;

	protected override void ConnectHandler(object platformView)
	{
		base.ConnectHandler(platformView);
		if (ShellView is { } shell)
		{
			shell.Navigating += _onNavigating;
			shell.Navigated += _onNavigated;
		}
	}

	protected override void DisconnectHandler(object platformView)
	{
		if (ShellView is { } shell)
		{
			shell.Navigating -= _onNavigating;
			shell.Navigated -= _onNavigated;
		}
		base.DisconnectHandler(platformView);
	}

	(IReadOnlyList<Page> Pages, Func<Task>? Pop) ISailfishPageContainer.CurrentStack()
	{
		var section = ShellView?.CurrentItem?.CurrentItem;
		if (section is null)
			return (Array.Empty<Page>(), null);
		// No Shell renderer creates the section's root page: materialize it here.
		var rootPage = section.CurrentItem is { } content
			? ((IShellContentController)content).GetOrCreateContent()
			: null;
		var pages = new List<Page>();
		var stack = section.Navigation.NavigationStack;
		for (var i = 0; i < stack.Count; i++)
		{
			var page = stack[i] ?? (i == 0 ? rootPage : null);
			if (page is not null)
				pages.Add(page);
		}
		if (pages.Count == 0 && rootPage is not null)
			pages.Add(rootPage);
		return (pages, () => section.Navigation.PopAsync());
	}

	(List<string> Titles, int Index, Action<int> Select)? ISailfishPageContainer.Tabs
	{
		get
		{
			if (ShellView?.CurrentItem is not { } item)
				return null;
			// Bottom tabs: the item's sections; top tabs: a section's contents.
			var sections = ((IShellItemController)item).GetItems();
			if (sections.Count > 1)
				return (sections.Select(sec => sec.Title ?? string.Empty).ToList(),
					Math.Max(0, IndexOf(sections, item.CurrentItem)),
					// As the platform tab bars switch: ProposeSection runs Shell's navigation (Navigating/Navigated, the page's
					// NavigatedTo) before setting CurrentItem; a bare assignment skipped the page events.
					i => { if (i >= 0 && i < sections.Count) ((IShellItemController)item).ProposeSection(sections[i], true); });
			if (item.CurrentItem is { } section)
			{
				var contents = ((IShellSectionController)section).GetItems();
				if (contents.Count > 1)
					return (contents.Select(c => c.Title ?? string.Empty).ToList(),
						Math.Max(0, IndexOf(contents, section.CurrentItem)),
						i => SelectContent(section, contents, i));
			}
			return null;
		}
	}

	(List<string> Titles, int Index, Action<int> Select)? ISailfishPageContainer.SubTabs
	{
		get
		{
			// Bottom tabs are the sections; a section's own contents are its top tabs (Profitocracy: All / Recurring).
			if (ShellView?.CurrentItem is not { } item || ((IShellItemController)item).GetItems().Count < 2 ||
			    item.CurrentItem is not { } section)
				return null;
			var contents = ((IShellSectionController)section).GetItems();
			if (contents.Count < 2)
				return null;
			return (contents.Select(c => c.Title ?? string.Empty).ToList(),
				Math.Max(0, IndexOf(contents, section.CurrentItem)),
				i => SelectContent(section, contents, i));
		}
	}

	IEnumerable<(string Text, bool Enabled, Action Activate)> ISailfishPageContainer.FlyoutMenu(Page shown)
	{
		// On a section's root page only, with the flyout enabled; picks go through OnFlyoutItemSelected (MAUI semantics).
		if (ShellView is not { } shell || ((ISailfishPageContainer)this).CurrentStack().Pages.Count != 1 ||
		    Shell.GetFlyoutBehavior(shown) == FlyoutBehavior.Disabled || shell.FlyoutBehavior == FlyoutBehavior.Disabled)
			yield break;
		var controller = (IShellController)shell;
		foreach (var group in controller.GenerateFlyoutGrouping())
		{
			foreach (var element in group)
			{
				var text = element switch
				{
					BaseShellItem item => item.Title,
					MenuItem menu => menu.Text,
					_ => null,
				};
				if (string.IsNullOrEmpty(text))
					continue;
				var enabled = element is not MenuItem { IsEnabled: false };
				var target = element;
				yield return (text, enabled, () => controller.OnFlyoutItemSelected(target));
			}
		}
	}

	bool ISailfishPageContainer.Holds(Page page)
	{
		if (ShellView is not { } shell)
			return false;
		foreach (var item in shell.Items)
			foreach (var section in item.Items)
			{
				if (section.Navigation.NavigationStack.Any(p => p is not null && SailfishPageContainers.Holds(p, page, MauiContext)))
					return true;
				// A ContentTemplate page lives in the controller's Page; Content stays the template's null.
				foreach (var content in section.Items)
					if ((((IShellContentController)content).Page ?? content.Content as Page) is { } realized &&
					    SailfishPageContainers.Holds(realized, page, MauiContext))
						return true;
			}
		return false;
	}

	/// <summary>A section's content as Android's ShellSectionRenderer selects it: proposed to Shell first, so the
	/// switch is a navigation (Navigating/Navigated, the page's NavigatedTo).</summary>
	private void SelectContent(ShellSection section, IReadOnlyList<ShellContent> contents, int i)
	{
		if (i < 0 || i >= contents.Count || ShellView is not IShellController shell)
			return;
		if (shell.ProposeNavigation(ShellNavigationSource.ShellContentChanged, section.Parent as ShellItem, section, contents[i],
		        section.Stack, true))
			section.SetValueFromRenderer(ShellSection.CurrentItemProperty, contents[i]);
	}

	private static int IndexOf<T>(IReadOnlyList<T> list, T? value) where T : class
	{
		for (var i = 0; i < list.Count; i++)
			if (ReferenceEquals(list[i], value))
				return i;
		return -1;
	}
}
