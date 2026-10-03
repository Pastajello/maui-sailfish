using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using static Microsoft.Maui.SailfishOS.Platform.QtHost.QtHostCollectionBridge;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>Lookups between items, item ordinals and rows (<see cref="RowLookup"/> over this list's rows).</summary>
internal sealed partial class QtHostListAdapter
{
	/// <summary>MAUI's item equality (the selection, CurrentItem, ScrollTo): the same object, or one that Equals it.</summary>
	internal static bool ItemsEqual(object? a, object? b) => RowLookup.ItemsEqual(a, b);

	/// <summary>The flat item ordinal of <paramref name="item"/> (-1 when absent).</summary>
	internal int IndexOfItem(object? item) => RowLookup.IndexOfItem(Rows, item);

	internal object? ItemAt(int ordinal) => RowLookup.ItemAt(Rows, ordinal);

	/// <summary>Row index → ordinal of its first item (-1 for header/footer or invalid rows).</summary>
	internal int RowToItemIndex(int rowIndex) => RowLookup.RowToItemIndex(Rows, rowIndex);

	/// <summary>Item ordinal → the row carrying it (-1 when none).</summary>
	internal int ItemIndexToRow(int itemOrdinal) => RowLookup.ItemIndexToRow(Rows, itemOrdinal);

	internal object? ItemAtRow(int rowIndex) => RowLookup.ItemAtRow(Rows, rowIndex);

	/// <summary>The row holding item <paramref name="index"/> of group <paramref name="groupIndex"/>; -1 when none.</summary>
	internal int GroupItemRow(int groupIndex, int index) => RowLookup.GroupItemRow(Rows, groupIndex, index);

	/// <summary>Resolves a ScrollTo target (item ordinal or item) to a row index.</summary>
	internal int ResolveRow(object? index) => RowLookup.ResolveRow(Rows, index);
}

/// <summary>
/// The row model's lookups, pure functions of the rows (no Qt, no MAUI views): rows are flat (group headers, item rows
/// holding one or a grid row's several items, group footers); an item's ordinal counts items across groups.
/// </summary>
internal static class RowLookup
{
	public static bool ItemsEqual(object? a, object? b) => ReferenceEquals(a, b) || (a is not null && a.Equals(b));

	public static int IndexOfItem(IReadOnlyList<Row> rows, object? item)
	{
		if (item is null)
			return -1;
		var ordinal = 0;
		foreach (var row in rows)
		{
			if (row.Kind != KindItem)
				continue;
			foreach (var cell in row.CellItems)
			{
				if (ItemsEqual(cell, item))
					return ordinal;
				ordinal++;
			}
		}
		return -1;
	}

	public static object? ItemAt(IReadOnlyList<Row> rows, int ordinal)
	{
		var i = 0;
		foreach (var row in rows)
		{
			if (row.Kind != KindItem)
				continue;
			foreach (var cell in row.CellItems)
			{
				if (i == ordinal)
					return cell;
				i++;
			}
		}
		return null;
	}

	public static int RowToItemIndex(IReadOnlyList<Row> rows, int rowIndex) =>
		rowIndex >= 0 && rowIndex < rows.Count ? rows[rowIndex].FirstItemOrdinal : -1;

	public static int ItemIndexToRow(IReadOnlyList<Row> rows, int itemOrdinal)
	{
		if (itemOrdinal < 0)
			return -1;
		for (var r = 0; r < rows.Count; r++)
		{
			var row = rows[r];
			if (row.FirstItemOrdinal < 0 || row.FirstItemOrdinal > itemOrdinal)
				continue;
			if (row.FirstItemOrdinal + Math.Max(1, row.CellItems.Count) > itemOrdinal)
				return r;
		}
		return -1;
	}

	public static object? ItemAtRow(IReadOnlyList<Row> rows, int rowIndex) =>
		rowIndex >= 0 && rowIndex < rows.Count && rows[rowIndex].CellItems.Count > 0
			? rows[rowIndex].CellItems[0]
			: null;

	public static int GroupItemRow(IReadOnlyList<Row> rows, int groupIndex, int index)
	{
		for (var r = 0; r < rows.Count; r++)
		{
			var row = rows[r];
			if (row.Kind == KindItem && row.GroupIndex == groupIndex &&
			    index >= row.ItemIndex && index < row.ItemIndex + Math.Max(1, row.CellItems.Count))
				return r;
		}
		return -1;
	}

	public static int ResolveRow(IReadOnlyList<Row> rows, object? index)
	{
		switch (index)
		{
			case null:
				return -1;
			case int ordinal:
				return ItemIndexToRow(rows, ordinal);
		}
		for (var r = 0; r < rows.Count; r++)
		{
			var row = rows[r];
			if (row.Kind != KindItem)
				continue;
			foreach (var cell in row.CellItems)
				if (ItemsEqual(cell, index))
					return r;
		}
		return -1;
	}
}
