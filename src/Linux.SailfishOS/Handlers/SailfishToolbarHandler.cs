using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;

namespace Microsoft.Maui.SailfishOS.Handlers;

/// <summary>
/// Handler of MAUI's <see cref="IToolbar"/> (a NavigationPage's or a Shell's toolbar, which MAUI 11 keeps up to date
/// on every platform): the page chrome the renderer draws on the Silica page follows it, as the platform toolbar
/// handlers drive their app bars. The toolbar computes what the chrome shows (the ToolbarItems sorted by Priority,
/// with the Shell's and the flyout's; the title; whether a back button and the bar show); a change of any key asks the
/// renderer for a chrome pass.
/// </summary>
public class SailfishToolbarHandler : ElementHandler<IToolbar, object>
{
	public static readonly PropertyMapper<IToolbar, SailfishToolbarHandler> Mapper = new(ElementMapper)
	{
		[nameof(IToolbar.Title)] = MapChrome,
		[nameof(IToolbar.IsVisible)] = MapChrome,
		[nameof(IToolbar.BackButtonVisible)] = MapChrome,
		[nameof(Toolbar.ToolbarItems)] = MapChrome,
		[nameof(Toolbar.TitleView)] = MapChrome,
		[nameof(Toolbar.BackButtonEnabled)] = MapChrome,
		[nameof(Toolbar.DrawerToggleVisible)] = MapChrome,
	};

	public static readonly CommandMapper<IToolbar, SailfishToolbarHandler> CommandMapper = new(ElementCommandMapper);

	public SailfishToolbarHandler() : this(null)
	{
	}

	public SailfishToolbarHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
		: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
	{
	}

	protected override object CreatePlatformElement() => new object();

	/// <summary>The chrome is drawn by the renderer on the Silica page: a toolbar change asks for a pass.</summary>
	public static void MapChrome(SailfishToolbarHandler handler, IToolbar toolbar) =>
		SailfishHandlerCore.SessionOf(handler)?.RequestPoll();
}
