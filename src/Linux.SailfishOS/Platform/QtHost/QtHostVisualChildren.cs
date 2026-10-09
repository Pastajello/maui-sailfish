// The legacy ListView and its cells are obsolete in MAUI 11; supporting apps that still use them is the point here.
#pragma warning disable CS0618

using Microsoft.Maui.Controls;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>The children the renderer walks, lays out and places for an element: its visual children, except a legacy
/// ListView, whose only child is its mirror CollectionView, inside a RefreshView while pull-to-refresh is on (tracker
/// S32, S34).</summary>
internal static class QtHostVisualChildren
{
	public static IEnumerable<IVisualTreeElement> Of(IVisualTreeElement element) =>
		element is ListView list ? new IVisualTreeElement[] { LegacyListMirror.Of(list).Root } : element.GetVisualChildren();
}
