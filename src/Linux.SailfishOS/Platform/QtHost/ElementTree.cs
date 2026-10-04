using Microsoft.Maui.Controls;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>Logical-tree ancestry, written once (W3.5: five copies in the renderer, the page cache and the lists).</summary>
internal static class ElementTree
{
	/// <summary>Whether <paramref name="ancestor"/> is a proper ancestor of <paramref name="element"/> (itself excluded).</summary>
	public static bool IsAncestor(Element? ancestor, Element? element)
	{
		for (var e = element?.Parent; e is not null; e = e.Parent)
			if (ReferenceEquals(e, ancestor))
				return true;
		return false;
	}

	/// <summary>Whether <paramref name="element"/> is <paramref name="root"/> or inside it.</summary>
	public static bool IsWithin(Element? element, Element root)
	{
		for (var e = element; e is not null; e = e.Parent)
			if (ReferenceEquals(e, root))
				return true;
		return false;
	}
}
