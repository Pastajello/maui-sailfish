using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;
using static Microsoft.Maui.SailfishOS.Platform.QtHost.QtHostCollectionBridge;

namespace Linux.SailfishOS.Tests;

/// <summary>The row model's lookups on their own (no shim, no views): a grouped grid flattened into rows.</summary>
public class RowLookupTests
{
	private sealed record Item(string Text);

	// group 0: header, [a b], [c], footer; group 1: header, [d e]
	private static List<Row> Rows()
	{
		var rows = new List<Row>();
		Row Add(int kind, int group, int itemIndex, int ordinal, params Item[] items)
		{
			var row = new Row { Index = rows.Count, Kind = kind, GroupIndex = group, ItemIndex = itemIndex, FirstItemOrdinal = ordinal };
			row.CellItems.AddRange(items);
			rows.Add(row);
			return row;
		}
		Add(KindGroupHeader, 0, -1, -1);
		Add(KindItem, 0, 0, 0, new Item("a"), new Item("b"));
		Add(KindItem, 0, 2, 2, new Item("c"));
		Add(KindGroupFooter, 0, -1, -1);
		Add(KindGroupHeader, 1, -1, -1);
		Add(KindItem, 1, 0, 3, new Item("d"), new Item("e"));
		return rows;
	}

	[Fact]
	public void Ordinals_count_items_across_groups_and_skip_headers()
	{
		var rows = Rows();
		Assert.Equal(3, RowLookup.IndexOfItem(rows, new Item("d")));   // by value
		Assert.Equal(new Item("e"), RowLookup.ItemAt(rows, 4));
		Assert.Null(RowLookup.ItemAt(rows, 5));
		Assert.Equal(2, RowLookup.ItemIndexToRow(rows, 2));
		Assert.Equal(5, RowLookup.ItemIndexToRow(rows, 4));
		Assert.Equal(-1, RowLookup.RowToItemIndex(rows, 0));   // a header carries no item
	}

	[Fact]
	public void A_group_index_addresses_items_within_its_group()
	{
		var rows = Rows();
		Assert.Equal(5, RowLookup.GroupItemRow(rows, 1, 1));
		Assert.Equal(2, RowLookup.GroupItemRow(rows, 0, 2));
		Assert.Equal(-1, RowLookup.GroupItemRow(rows, 1, 2));
		Assert.Equal(5, RowLookup.ResolveRow(rows, new Item("e")));
		Assert.Equal(1, RowLookup.ResolveRow(rows, 1));
	}
}
