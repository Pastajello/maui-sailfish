using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Handlers;

/// <summary>
/// Handler of pages. A page has no host of its own: the renderer maps it onto a Silica model page (title, background,
/// pulley menus) and arranges its content below the chrome. The handler reports what changes that model page, so the
/// reconcile follows at once instead of at the next safety-net poll.
/// </summary>
public class SailfishPageHandler : NullViewHandler
{
	/// <summary>The page state the model page shows.</summary>
	public static new readonly PropertyMapper<IView, SailfishPageHandler> Mapper = new(NullViewHandler.Mapper)
	{
		[nameof(Page.Title)] = MapModelPage,
		[nameof(VisualElement.BackgroundColor)] = MapModelPage,
		[nameof(VisualElement.Background)] = MapModelPage,
		[nameof(Page.BackgroundImageSource)] = MapModelPage,
		[nameof(IContentView.Content)] = MapModelPage,
		[nameof(Page.IsBusy)] = MapModelPage,
		[nameof(FlyoutPage.Detail)] = MapModelPage,
		[nameof(FlyoutPage.Flyout)] = MapModelPage,
		// What switches a container's shown page, as ShellHandler/TabbedPageHandler map them elsewhere: the Shell
		// item and its state (a section or tab inside it), the selected tab, the presented flyout.
		[nameof(Shell.CurrentItem)] = MapModelPage,
		[nameof(Shell.CurrentState)] = MapModelPage,
		[nameof(TabbedPage.CurrentPage)] = MapModelPage,
		[nameof(FlyoutPage.IsPresented)] = MapModelPage,
		// The Shell flyout is the pulley: turning it off or on changes the menu.
		[nameof(Shell.FlyoutBehavior)] = MapModelPage,
		// Presenting the flyout from code opens its entries as a context menu (tracker S23).
		[nameof(Shell.FlyoutIsPresented)] = MapModelPage,
		// The header and tab rows (tracker S20): attached properties report their change on the page.
		[NavigationPage.HasNavigationBarProperty.PropertyName] = MapModelPage,
		[NavigationPage.TitleViewProperty.PropertyName] = MapModelPage,
		[Shell.NavBarIsVisibleProperty.PropertyName] = MapModelPage,
		[Shell.TabBarIsVisibleProperty.PropertyName] = MapModelPage,
		// A Shell's (or a nested NavigationPage's) MAUI toolbar lives on the page: it gets its handler (tracker S19).
		[nameof(IToolbarElement.Toolbar)] = MapToolbar,
	};

	public static readonly CommandMapper<IView, SailfishPageHandler> CommandMapper = new(SailfishViewMapper.CommandMapper);

	public SailfishPageHandler() : this(null)
	{
	}

	public SailfishPageHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
		: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
	{
	}

	/// <summary>The page's MAUI toolbar gets its handler, which drives the chrome.</summary>
	public static void MapToolbar(IViewHandler handler, IView view)
	{
		if (view is IToolbarElement { Toolbar: { Handler: null } toolbar } && handler.MauiContext is { } context)
			SailfishHandlersFactory.AttachToolbarHandler(toolbar, context);
		SailfishHandlerCore.SessionOf(handler)?.RequestPoll();
	}

	/// <summary>Asks the renderer to re-sync the model page and its content.</summary>
	public static void MapModelPage(IViewHandler handler, IView view) => SailfishHandlerCore.SessionOf(handler)?.RequestPoll();
}
