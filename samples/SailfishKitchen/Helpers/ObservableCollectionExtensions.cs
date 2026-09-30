using System.Collections.ObjectModel;

namespace SailfishKitchen.Helpers;

public static class ObservableCollectionExtensions
{
	/// <summary>
	/// Clears, then adds each item with its own Add: the CollectionView on this backend edits rows per Add/Remove,
	/// so do not swap this for one Reset carrying the new contents.
	/// </summary>
	public static void ReplaceWith<T>(this ObservableCollection<T> collection, IEnumerable<T> items)
	{
		collection.Clear();
		foreach (var item in items)
			collection.Add(item);
	}
}
