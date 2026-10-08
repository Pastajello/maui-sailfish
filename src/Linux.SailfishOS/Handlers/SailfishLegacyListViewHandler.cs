// The legacy ListView and its cells are obsolete in MAUI 11; supporting apps that still use them is the point here.
#pragma warning disable CS0618

using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platform;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Handlers;

/// <summary>
/// Handler of the legacy ListView (tracker S32, D2 b): no native view of its own. Its mirror CollectionView
/// (<see cref="LegacyListMirror"/>) is measured and arranged in its place and renders the rows on the list adapter.
/// </summary>
public class SailfishLegacyListViewHandler : NullViewHandler
{
	/// <summary>Turning pull-to-refresh on or off swaps what stands in the ListView's place: its subtree is walked
	/// again.</summary>
	public static new readonly PropertyMapper<IView, SailfishLegacyListViewHandler> Mapper = new(NullViewHandler.Mapper)
	{
		[nameof(ListView.IsPullToRefreshEnabled)] = static (handler, view) => SailfishHandlerCore.SessionOf(handler)?.RequestSubtree(view),
	};

	public SailfishLegacyListViewHandler() : base(Mapper, null)
	{
	}

	private View? Mirror =>
		ConnectedView is ListView list ? LegacyListMirror.Of(list).Root : null;

	public override Size GetDesiredSize(double widthConstraint, double heightConstraint)
	{
		if (Mirror is not { } mirror || MauiContext is null)
			return base.GetDesiredSize(widthConstraint, heightConstraint);
		QtHostLayout.AttachHandlers(mirror, MauiContext);
		var desired = ((IView)mirror).Measure(widthConstraint, heightConstraint);
		return SailfishHandlerCore.DesiredSize(ConnectedView, widthConstraint, heightConstraint, (_, _, _) => desired);
	}

	public override void PlatformArrange(Rect frame)
	{
		base.PlatformArrange(frame);
		if (Mirror is { } mirror)
			((IView)mirror).Arrange(new Rect(0, 0, frame.Width, frame.Height));
	}
}
