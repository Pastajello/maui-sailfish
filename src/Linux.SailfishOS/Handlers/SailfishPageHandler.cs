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
	public static readonly PropertyMapper<IView, SailfishPageHandler> Mapper = new(ViewMapper)
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
	};

	public SailfishPageHandler() : base(Mapper, null)
	{
	}

	protected SailfishPageHandler(IPropertyMapper mapper, CommandMapper? commandMapper) : base(mapper, commandMapper)
	{
	}

	/// <summary>Asks the renderer to re-sync the model page and its content.</summary>
	public static void MapModelPage(IViewHandler handler, IView view) => QtHostPageRenderer.RequestPoll();
}
