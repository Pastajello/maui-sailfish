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
	SailfishTabRow? Tabs { get; }

	/// <summary>A second row under <see cref="Tabs"/> (a Shell section's contents when the item has several sections);
	/// null = none.</summary>
	SailfishTabRow? SubTabs => null;

	/// <summary>Pulley entries that open the container's flyout from <paramref name="shown"/> (its stack root).</summary>
	IEnumerable<(string Text, bool Enabled, Action Activate)> FlyoutMenu(Page shown);

	/// <summary>Whether <paramref name="page"/> is one of this container's pages (on a stack, a tab, the flyout, a
	/// realized Shell content), directly or through a nested container: a page the app can still show.</summary>
	bool Holds(Page page);
}

/// <summary>A tab row: the titles, the selected index, what a tap does, and per tab the badge (null = none).</summary>
internal sealed record SailfishTabRow(List<string> Titles, int Index, Action<int> Select, List<SailfishTabBadge?>? Badges = null);

/// <summary>A tab's badge (MAUI 11 <c>TabbedPage.BadgeText</c>, <c>BaseShellItem.BadgeText</c>): an empty text is a
/// dot, as on Android and Windows; null colours keep the theme's.</summary>
internal readonly record struct SailfishTabBadge(string Text, Color? Color, Color? TextColor)
{
	internal static SailfishTabBadge? Of(string? text, Color? color, Color? textColor) =>
		text is null ? null : new SailfishTabBadge(text, color, textColor);

	internal static SailfishTabBadge? Of(Page page) =>
		Of(TabbedPage.GetBadgeText(page), TabbedPage.GetBadgeColor(page), TabbedPage.GetBadgeTextColor(page));

	internal static SailfishTabBadge? Of(BaseShellItem item) => Of(item.BadgeText, item.BadgeColor, item.BadgeTextColor);

	/// <summary>The badges of a row, or null when no tab has one (the row's JSON stays as before badges).</summary>
	internal static List<SailfishTabBadge?>? Row(IEnumerable<SailfishTabBadge?> badges)
	{
		var list = badges.ToList();
		return list.Any(b => b is not null) ? list : null;
	}
}

/// <summary>Resolves containers through their handlers. Only the window's root page gets its handler from the window;
/// a container nested in another (a TabbedPage as a flyout's Detail) gets its handler when its parent presents it
/// (<see cref="Present"/>), as a platform container creates the native view of the child it shows.</summary>
internal static class SailfishPageContainers
{
	private static readonly IReadOnlyList<Page> NoPages = Array.Empty<Page>();

	internal static bool IsContainer(Page? page) => page is NavigationPage or Shell or TabbedPage or FlyoutPage;

	/// <summary>A tab's text. Silica tab rows are text only, so a tab that relies on its icon (no Title) shows its
	/// NavigationPage root's title, its explicit route, or its page type rather than an empty slot.</summary>
	internal static string TabTitle(BindableObject tab, string? title)
	{
		if (!string.IsNullOrEmpty(title))
			return title;
		if (tab is NavigationPage { RootPage.Title: { Length: > 0 } rootTitle })
			return rootTitle;
		var route = Routing.GetRoute(tab);
		// Shell's generated routes ("IMPL_…", "D_FAULT_…") mean nothing to a user.
		if (!string.IsNullOrEmpty(route) && !route.StartsWith("IMPL_", StringComparison.Ordinal) &&
		    !route.StartsWith("D_FAULT_", StringComparison.Ordinal))
			return route;
		return tab switch
		{
			NavigationPage { RootPage: { } root } => root.GetType().Name,
			Page page => page.GetType().Name,
			_ => string.Empty,
		};
	}

	/// <summary>The container handler of <paramref name="page"/>; null for a page, or a nested container no parent
	/// presented yet (nothing of it is on screen).</summary>
	internal static ISailfishPageContainer? Of(Page? page) =>
		IsContainer(page) ? page!.Handler as ISailfishPageContainer : null;

	/// <summary>A parent container shows <paramref name="page"/>: a nested container gets its handler now.</summary>
	internal static ISailfishPageContainer? Present(Page? page, IMauiContext? context)
	{
		if (IsContainer(page) && page!.Handler is null && context is not null)
			SailfishHandlersFactory.AttachRootHandler(page, context);
		return Of(page);
	}

	/// <summary>Whether <paramref name="candidate"/> is <paramref name="page"/> or held by it (a container). A page
	/// inside a container never presented is not held: it was never shown either.</summary>
	internal static bool Holds(Page? page, Page candidate) =>
		page is not null && (ReferenceEquals(page, candidate) || Of(page)?.Holds(candidate) == true);

	/// <summary>The stack <paramref name="page"/> shows: a container's current stack (the container is presented),
	/// else the page alone.</summary>
	internal static (IReadOnlyList<Page> Pages, Func<Task>? Pop) StackOf(Page? page, IMauiContext? context)
	{
		if (page is null)
			return (NoPages, null);
		return Present(page, context) is { } container ? container.CurrentStack() : (new[] { page }, null);
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
	public static new readonly PropertyMapper<IView, SailfishTabbedPageHandler> Mapper = new(SailfishPageHandler.Mapper);

	public static new readonly CommandMapper<IView, SailfishTabbedPageHandler> CommandMapper = new(SailfishPageHandler.CommandMapper);

	public SailfishTabbedPageHandler() : this(null)
	{
	}

	public SailfishTabbedPageHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
		: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
	{
	}

	private void OnCurrentPageChanged(object? sender, EventArgs e) => SailfishHandlerCore.SessionOf(this)?.RequestPoll();

	private TabbedPage? Tabbed => ConnectedView as TabbedPage;

	// The page whose event is subscribed: follows SetVirtualView (W1.11: subscribed in ConnectHandler only, a handler
	// moved to another page kept listening to the old one, and Disconnect unsubscribed the new one it never joined).
	private TabbedPage? _watched;

	// The children whose PropertyChanged is subscribed: a title or badge change re-pushes the tab row.
	private readonly List<Page> _watchedChildren = new();

	private void Watch(TabbedPage? page)
	{
		if (ReferenceEquals(page, _watched))
			return;
		if (_watched is { } old)
		{
			old.CurrentPageChanged -= OnCurrentPageChanged;
			old.PagesChanged -= OnPagesChanged;
		}
		_watched = page;
		if (page is not null)
		{
			page.CurrentPageChanged += OnCurrentPageChanged;
			page.PagesChanged += OnPagesChanged;
		}
		WatchChildren();
	}

	private void WatchChildren()
	{
		foreach (var child in _watchedChildren)
			child.PropertyChanged -= OnChildPropertyChanged;
		_watchedChildren.Clear();
		if (_watched is null)
			return;
		foreach (var child in _watched.Children)
		{
			child.PropertyChanged += OnChildPropertyChanged;
			_watchedChildren.Add(child);
		}
	}

	// A tab added or removed shows now, not at the next heartbeat.
	private void OnPagesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
	{
		WatchChildren();
		SailfishHandlerCore.SessionOf(this)?.RequestPoll();
	}

	private void OnChildPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if (e.PropertyName == Page.TitleProperty.PropertyName || e.PropertyName == TabbedPage.BadgeTextProperty.PropertyName ||
		    e.PropertyName == TabbedPage.BadgeColorProperty.PropertyName ||
		    e.PropertyName == TabbedPage.BadgeTextColorProperty.PropertyName)
			SailfishHandlerCore.SessionOf(this)?.RequestPoll();
	}

	public override void SetVirtualView(IView view)
	{
		base.SetVirtualView(view);
		Watch(view as TabbedPage);
	}

	protected override void DisconnectHandler(object platformView)
	{
		Watch(null);
		base.DisconnectHandler(platformView);
	}

	(IReadOnlyList<Page> Pages, Func<Task>? Pop) ISailfishPageContainer.CurrentStack() =>
		Tabbed is { } tabbed
			? tabbed.CurrentPage is { } child ? SailfishPageContainers.StackOf(child, MauiContext) : (new Page[] { tabbed }, null)
			: (Array.Empty<Page>(), null);

	SailfishTabRow? ISailfishPageContainer.Tabs
	{
		get
		{
			if (Tabbed is not { } tabbed || tabbed.Children.Count < 2)
				return null;
			var children = tabbed.Children;
			return new SailfishTabRow(children.Select(c => SailfishPageContainers.TabTitle(c, c.Title)).ToList(),
				Math.Max(0, children.IndexOf(tabbed.CurrentPage)),
				i => { if (i >= 0 && i < children.Count) tabbed.CurrentPage = children[i]; },
				SailfishTabBadge.Row(children.Select(c => SailfishTabBadge.Of(c))));
		}
	}

	IEnumerable<(string Text, bool Enabled, Action Activate)> ISailfishPageContainer.FlyoutMenu(Page shown) =>
		Tabbed?.CurrentPage is { } child && SailfishPageContainers.Of(child) is { } inner
			? inner.FlyoutMenu(shown)
			: Enumerable.Empty<(string, bool, Action)>();

	bool ISailfishPageContainer.Holds(Page page) =>
		Tabbed?.Children.Any(child => SailfishPageContainers.Holds(child, page)) == true;
}

/// <summary>FlyoutPage: the Detail's stack, plus the Flyout page while presented (a native push, closed by Back).</summary>
public class SailfishFlyoutPageHandler : SailfishPageHandler, ISailfishPageContainer
{
	public static new readonly PropertyMapper<IView, SailfishFlyoutPageHandler> Mapper = new(SailfishPageHandler.Mapper);

	public static new readonly CommandMapper<IView, SailfishFlyoutPageHandler> CommandMapper = new(SailfishPageHandler.CommandMapper);

	public SailfishFlyoutPageHandler() : this(null)
	{
	}

	public SailfishFlyoutPageHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
		: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
	{
	}

	private void OnPresentedChanged(object? sender, EventArgs e) => SailfishHandlerCore.SessionOf(this)?.RequestPoll();

	private FlyoutPage? Flyout => ConnectedView as FlyoutPage;

	private FlyoutPage? _watched;   // follows SetVirtualView, as SailfishTabbedPageHandler's

	private void Watch(FlyoutPage? page)
	{
		if (ReferenceEquals(page, _watched))
			return;
		if (_watched is { } old)
		{
			old.IsPresentedChanged -= OnPresentedChanged;
			old.PropertyChanging -= OnFlyoutPageChanging;
			old.PropertyChanged -= OnFlyoutPageChanged;
		}
		_watched = page;
		if (page is not null)
		{
			page.IsPresentedChanged += OnPresentedChanged;
			page.PropertyChanging += OnFlyoutPageChanging;
			page.PropertyChanged += OnFlyoutPageChanged;
		}
	}

	private Page? _replacedDetail;

	private void OnFlyoutPageChanging(object? sender, Microsoft.Maui.Controls.PropertyChangingEventArgs e)
	{
		if (e.PropertyName == nameof(FlyoutPage.Detail))
			_replacedDetail = (sender as FlyoutPage)?.Detail;
	}

	/// <summary>A replaced Detail lets go of its handlers, as the platforms' FlyoutPage handlers disconnect the old
	/// detail (tracker S38): its subscriptions no longer keep it, or follow it.</summary>
	private void OnFlyoutPageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if (e.PropertyName != nameof(FlyoutPage.Detail) || _replacedDetail is not { } old)
			return;
		_replacedDetail = null;
		if (!ReferenceEquals(old, (sender as FlyoutPage)?.Detail))
			((IView)old).DisconnectHandlers();
	}

	public override void SetVirtualView(IView view)
	{
		base.SetVirtualView(view);
		Watch(view as FlyoutPage);
	}

	protected override void DisconnectHandler(object platformView)
	{
		Watch(null);
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

	SailfishTabRow? ISailfishPageContainer.Tabs =>
		Flyout is { IsPresented: false, Detail: { } detail } ? SailfishPageContainers.Of(detail)?.Tabs : null;

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
		(SailfishPageContainers.Holds(flyout.Detail, page) || SailfishPageContainers.Holds(flyout.Flyout, page));
}

/// <summary>Shell: the current section's stack (its root slot materialized from the ShellContent), its sections or
/// contents as tabs, the flyout items as the pulley menu.</summary>
public class SailfishShellHandler : SailfishPageHandler, ISailfishPageContainer
{
	public static new readonly PropertyMapper<IView, SailfishShellHandler> Mapper = new(SailfishPageHandler.Mapper);

	public static new readonly CommandMapper<IView, SailfishShellHandler> CommandMapper = new(SailfishPageHandler.CommandMapper);

	public SailfishShellHandler() : this(null)
	{
	}

	public SailfishShellHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
		: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
	{
	}

	private void OnNavigated(object? sender, ShellNavigatedEventArgs e)
	{
		if (SailfishHandlerCore.SessionOf(this) is { } session)
			session.RoutePageNavigation = false;
		SailfishHandlerCore.SessionOf(this)?.RequestPoll();
	}

	// Shell raises Navigating before it builds a pushed route page: only those pages come from the service overlay.
	private void OnNavigating(object? sender, ShellNavigatingEventArgs e)
	{
		if (SailfishHandlerCore.SessionOf(this) is { } session)
			session.RoutePageNavigation = e.Source is ShellNavigationSource.Push or ShellNavigationSource.Insert;
	}

	// An item, section or content added or removed at runtime (the tab rows), or a flyout item (the pulley): shown
	// now, not at the next heartbeat.
	private void OnStructureChanged(object? sender, EventArgs e) => SailfishHandlerCore.SessionOf(this)?.RequestPoll();

	private Shell? ShellView => ConnectedView as Shell;

	private Shell? _watched;   // follows SetVirtualView, as SailfishTabbedPageHandler's

	private void Watch(Shell? shell)
	{
		if (ReferenceEquals(shell, _watched))
			return;
		if (_watched is { } old)
		{
			old.Navigating -= OnNavigating;
			old.Navigated -= OnNavigated;
			((IShellController)old).StructureChanged -= OnStructureChanged;
			((IShellController)old).FlyoutItemsChanged -= OnStructureChanged;
		}
		_watched = shell;
		if (shell is not null)
		{
			shell.Navigating += OnNavigating;
			shell.Navigated += OnNavigated;
			((IShellController)shell).StructureChanged += OnStructureChanged;
			((IShellController)shell).FlyoutItemsChanged += OnStructureChanged;
		}
	}

	public override void SetVirtualView(IView view)
	{
		base.SetVirtualView(view);
		Watch(view as Shell);
	}

	protected override void DisconnectHandler(object platformView)
	{
		Watch(null);
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

	SailfishTabRow? ISailfishPageContainer.Tabs
	{
		get
		{
			if (ShellView?.CurrentItem is not { } item)
				return null;
			// Bottom tabs: the item's sections; top tabs: a section's contents.
			var sections = ((IShellItemController)item).GetItems();
			if (SectionTabsShown(item, sections))
				return new SailfishTabRow(sections.Select(sec => SailfishPageContainers.TabTitle(sec, sec.Title)).ToList(),
					Math.Max(0, IndexOf(sections, item.CurrentItem)),
					// As the platform tab bars switch: ProposeSection runs Shell's navigation (Navigating/Navigated, the page's
					// NavigatedTo) before setting CurrentItem; a bare assignment skipped the page events.
					i => { if (i >= 0 && i < sections.Count) ((IShellItemController)item).ProposeSection(sections[i], true); },
					SailfishTabBadge.Row(sections.Select(sec => SailfishTabBadge.Of(sec))));
			return item.CurrentItem is { } section ? ContentTabs(section) : null;
		}
	}

	SailfishTabRow? ISailfishPageContainer.SubTabs
	{
		get
		{
			// Bottom tabs are the sections; a section's own contents are its top tabs (Profitocracy: All / Recurring).
			if (ShellView?.CurrentItem is not { } item || !SectionTabsShown(item, ((IShellItemController)item).GetItems()) ||
			    item.CurrentItem is not { } section)
				return null;
			return ContentTabs(section);
		}
	}

	/// <summary>The item's sections show as tabs: two or more, and Shell.TabBarIsVisible (on the shown page, its content,
	/// section, item or the Shell) not false. A hidden bar keeps the section's contents as the top tabs, as on Android.</summary>
	private bool SectionTabsShown(ShellItem item, IReadOnlyList<ShellSection> sections) =>
		sections.Count > 1 &&
		QtHostPageRenderer.ShellValue((Element?)ShellView?.CurrentPage ?? item, Shell.TabBarIsVisibleProperty, true);

	/// <summary>A section's contents as a tab row (null with fewer than two).</summary>
	private SailfishTabRow? ContentTabs(ShellSection section)
	{
		var contents = ((IShellSectionController)section).GetItems();
		if (contents.Count < 2)
			return null;
		return new SailfishTabRow(contents.Select(c => SailfishPageContainers.TabTitle(c, c.Title)).ToList(),
			Math.Max(0, IndexOf(contents, section.CurrentItem)),
			i => SelectContent(section, contents, i),
			SailfishTabBadge.Row(contents.Select(c => SailfishTabBadge.Of(c))));
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
				if (section.Navigation.NavigationStack.Any(p => p is not null && SailfishPageContainers.Holds(p, page)))
					return true;
				// A ContentTemplate page lives in the controller's Page; Content stays the template's null.
				foreach (var content in section.Items)
					if ((((IShellContentController)content).Page ?? content.Content as Page) is { } realized &&
					    SailfishPageContainers.Holds(realized, page))
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
