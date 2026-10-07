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

/// <summary>Taps on rows (selection, a cell's own tap gestures) and the selection pushed back to the delegates.</summary>
internal sealed partial class QtHostListAdapter
{
	internal void RecomputeSelection()
	{
		SelectedCells.Clear();
		if (View is not SelectableItemsView sel || sel.SelectionMode == SelectionMode.None)
			return;
		var selected = sel.SelectedItems;
		// Some MAUI builds don't mirror SelectedItem into SelectedItems, so match both.
		var single = sel.SelectedItem;
		if ((selected is null || selected.Count == 0) && single is null)
			return;
		for (var i = 0; i < Rows.Count; i++)
		{
			var row = Rows[i];
			if (row.Kind != KindItem)
				continue;
			for (var c = 0; c < row.CellItems.Count; c++)
				if (row.CellItems[c] is { } cell && (ItemsEqual(cell, single) || selected?.Contains(cell) == true))
					SelectedCells.Add((i, c));
		}
	}

	internal void PushSelection()
	{
		// "row" for single-column lists, "row:cell" for grids: the delegate highlights just that cell.
		var json = string.Join(',', SelectedCells.OrderBy(sc => sc.Row).ThenBy(sc => sc.Cell).Select(sc =>
			Span > 1
				? sc.Row.ToString(CultureInfo.InvariantCulture) + ":" + sc.Cell.ToString(CultureInfo.InvariantCulture)
				: sc.Row.ToString(CultureInfo.InvariantCulture)));
		if (json == LastSelJson)
			return;
		LastSelJson = json;
		Push("mauiSelectedRows", json);
	}

	/// <summary>Whether a cell's template carries a TapGestureRecognizer: the delegate then reports taps on an
	/// unselectable list too (the ListView consumes the press, so the input router never sees the row's content).</summary>
	private static bool RowHasTap(Row row)
	{
		foreach (var view in row.CellViews)
			if (view is not null && HasTap(view))
				return true;
		return false;

		static bool HasTap(View view)
		{
			foreach (var recognizer in view.GestureRecognizers)
				if (recognizer is TapGestureRecognizer)
					return true;
			if (view is ItemsView)
				return false;
			foreach (var child in ((IVisualTreeElement)view).GetVisualChildren())
				if (child is View v && HasTap(v))
					return true;
			return false;
		}
	}

	/// <summary>
	/// The template's TapGestureRecognizer under a delegate-relative point (dp): the deepest visible, hit-testable
	/// element in the cell, then up to the cell root, as gestures bubble elsewhere. Position is cell-root relative.
	/// </summary>
	internal static bool TryFindRowTap(View cellRoot, double cellX, bool horizontal, double xDp, double yDp,
	                                   out TapGestureRecognizer? tap, out View? owner, out Point position)
	{
		var hit = HitInCell(cellRoot, cellX, horizontal, xDp, yDp, out position);
		for (View? v = hit; v is not null; v = ReferenceEquals(v, cellRoot) ? null : v.Parent as View)
		{
			if (!v.IsEnabled)
				break;
			foreach (var recognizer in v.GestureRecognizers)
				if (recognizer is TapGestureRecognizer t)
				{
					tap = t;
					owner = v;
					return true;
				}
		}
		tap = null;
		owner = null;
		return false;
	}

	/// <summary>The deepest visible, hit-testable element of a cell under a delegate-relative point (dp);
	/// <paramref name="position"/> is the point relative to the cell root.</summary>
	internal static View HitInCell(View cellRoot, double cellX, bool horizontal, double xDp, double yDp, out Point position)
	{
		var local = horizontal
			? new Point(xDp - cellRoot.Bounds.X, yDp - cellX - cellRoot.Bounds.Y)
			: new Point(xDp - cellX - cellRoot.Bounds.X, yDp - cellRoot.Bounds.Y);
		position = local;
		var hit = cellRoot;
		var p = local;
		for (var descended = true; descended;)
		{
			descended = false;
			if (hit is ItemsView)
				break;   // a nested list hit-tests its own rows
			var children = ((IVisualTreeElement)hit).GetVisualChildren();
			for (var i = children.Count - 1; i >= 0; i--)
			{
				if (children[i] is not View child || !child.IsVisible || child.InputTransparent || !child.Bounds.Contains(p))
					continue;
				p = new Point(p.X - child.Bounds.X, p.Y - child.Bounds.Y);
				hit = child;
				descended = true;
				break;
			}
		}
		return hit;
	}

	/// <summary>Whether a cell's template carries recognizers other than Tap (Pan, Swipe, LongPress, Pointer, Pinch):
	/// the delegate then reports its presses ("list-item-pressed") so the input router can capture them.</summary>
	private static bool RowHasGestures(Row row)
	{
		foreach (var view in row.CellViews)
			if (view is not null && HasGestures(view))
				return true;
		return false;

		static bool HasGestures(View view)
		{
			foreach (var recognizer in view.GestureRecognizers)
				if (recognizer is not TapGestureRecognizer)
					return true;
			if (view is ItemsView)
				return false;
			foreach (var child in ((IVisualTreeElement)view).GetVisualChildren())
				if (child is View v && HasGestures(v))
					return true;
			return false;
		}
	}

	/// <summary>A press on a row with template gestures: the element under the finger hands the sequence to the input
	/// router (Pan/Swipe/Pointer/LongPress/Pinch inside rows, which the ListView's press would otherwise swallow).</summary>
	internal void OnRowPressed(int rowIndex, int cellIndex, double xQt, double yQt)
	{
		if (rowIndex < 0 || rowIndex >= Rows.Count || double.IsNaN(xQt) || QtHostInputRouter.Active is not { } router)
			return;
		var row = Rows[rowIndex];
		if (row.Kind != KindItem || cellIndex < 0 || cellIndex >= row.CellViews.Count || row.CellViews[cellIndex] is not { } cellRoot)
			return;
		var hit = HitInCell(cellRoot, row.CellX[cellIndex], Horizontal, QtHostUnits.ToLogical(xQt), QtHostUnits.ToLogical(yQt), out _);
		if (!hit.IsEnabled)
			return;
		var captured = router.CaptureRow(hit, cellRoot, hold => Push("mauiHoldRow", hold ? rowIndex : -1));
		QtHostDiag.Trace(QtHostDiagChannel.Input, $"collection row {rowIndex} cell {cellIndex} pressed on {hit.GetType().Name} → router capture={captured}");
	}

	internal void OnRowTapped(int rowIndex, int cellIndex, double xQt = double.NaN, double yQt = double.NaN)
	{
		if (rowIndex < 0 || rowIndex >= Rows.Count)
			return;
		// A long press in the row's template fired, or its pan took the finger: the release is no tap and no selection.
		if (QtHostInputRouter.Active?.TakeRowGesture() == true)
		{
			QtHostDiag.Trace(QtHostDiagChannel.Input, $"collection row {rowIndex} released after a row gesture — no tap");
			return;
		}
		var row = Rows[rowIndex];
		if (row.Kind != KindItem || cellIndex < 0 || cellIndex >= row.CellItems.Count)
			return;
		// A tap recognizer in the template takes the tap, as on Android, where it consumes the touch before selection.
		if (!double.IsNaN(xQt) && cellIndex < row.CellViews.Count && row.CellViews[cellIndex] is { } cellRoot &&
		    TryFindRowTap(cellRoot, row.CellX[cellIndex], Horizontal, QtHostUnits.ToLogical(xQt), QtHostUnits.ToLogical(yQt),
			    out var tap, out var owner, out var position))
		{
			QtHostDiag.Trace(QtHostDiagChannel.Input, $"collection row {rowIndex} cell {cellIndex} tapped → {owner!.GetType().Name} TapGestureRecognizer");
			QtHostInputRouter.SendTapped(tap!, owner, position);
			return;
		}
		if (View is not SelectableItemsView sel || sel.SelectionMode == SelectionMode.None)
			return;
		var item = row.CellItems[cellIndex];

		_counters.SelectionsApplied++;
		try
		{
			// Single: a re-tap never deselects, as with the in-box handlers. Multiple: toggle the tapped cell.
			if (item is not null && sel.SelectionMode == SelectionMode.Single)
				sel.SelectedItem = item;
			else if (item is not null && !sel.SelectedItems.Remove(item))
				sel.SelectedItems.Add(item);
		}
		catch (Exception ex)
		{
			QtHostDiag.Error(QtHostDiagChannel.QmlSignal, $"collection selection write-back failed: {ex.Message}");
		}
		// Idempotent safety net; the change handlers already recompute.
		RecomputeSelection();
		PushSelection();
		QtHostDiag.Trace(QtHostDiagChannel.Input, $"collection row {rowIndex} cell {cellIndex} tapped → selected [{LastSelJson}]");
	}
}
