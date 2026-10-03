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

/// <summary>Scroll state both ways: native scroll reports into MAUI (Scrolled, RemainingItemsThreshold), managed ScrollTo and carousel positions out.</summary>
internal sealed partial class QtHostListAdapter
{
	/// <summary>Moves the native carousel to page <paramref name="index"/>.</summary>
	internal void PushPosition(int index)
	{
		if (!Carousel || index < 0)
			return;
		Push("mauiPosition", index);
	}

	/// <summary>The native carousel settled on a page; Position and CurrentItem follow.</summary>
	internal void OnCarouselPosition(int index)
	{
		if (View is not CarouselView carousel || index < 0)
			return;
		var item = ItemAt(index);
		// The native carousel is already on this page: record it, so no push echoes it back.
		if (Host.IsAttached)
			Host.AppliedProperties["mauiPosition"] = BridgeValue.Serialize(index);
		if (carousel.Position != index)
			carousel.Position = index;
		if (!ItemsEqual(carousel.CurrentItem, item) && item is not null)
			carousel.CurrentItem = item;
		QtHostDiag.Trace(QtHostDiagChannel.Input, $"carousel '{Host}' page {index} → Position/CurrentItem");
	}

	internal void OnListScroll(double yQt, int firstRow, int lastRow)
	{
		_bridge.ScrollsReported++;
		FirstVisibleRow = firstRow;
		LastVisibleRow = lastRow;
		// Flicks recycle delegates silently, so resync the visible rows.
		if (ResyncPending < ResyncTicksAfterScroll)
			ResyncPending = ResyncTicksAfterScroll;
			_bridge.SchedulePending(ResyncIntervalMs);
		var yDp = QtHostUnits.ToLogical(yQt);
		if (yDp == LastReportedYDp)
			return;   // echo of the last reported offset
		LastReportedYDp = yDp;

		// MAUI mirrors the scroll this for observers; nothing is pushed back to the ListView (no echo loop).
		if (View is IScrollViewController controller)
			controller.SetScrolledPosition(Horizontal ? yDp : 0, Horizontal ? 0 : yDp);
		var args = new ItemsViewScrolledEventArgs
		{
			// The adapter reports the offset along ITS scroll axis.
			HorizontalOffset = Horizontal ? yDp : 0,
			VerticalOffset = Horizontal ? 0 : yDp,
			FirstVisibleItemIndex = RowToItemIndex(firstRow),
			LastVisibleItemIndex = RowToItemIndex(lastRow),
		};
		args.CenterItemIndex = (Math.Max(0, args.FirstVisibleItemIndex) + Math.Max(0, args.LastVisibleItemIndex)) / 2;
		View.SendScrolled(args);
		UpdateRemainingThreshold(lastRow);
	}

	/// <summary>
	/// Raises <see cref="ItemsView.RemainingItemsThresholdReached"/> once per crossing, re-armed when the
	/// remaining count grows back (scroll up or items appended); -1 (the default) disables it.
	/// </summary>
	internal void UpdateRemainingThreshold(int lastRow)
	{
		var threshold = View.RemainingItemsThreshold;
		if (threshold < 0 || TotalItems == 0 || lastRow < 0)
			return;
		var last = LastVisibleItemOrdinal(lastRow);
		if (last < 0)
			return;
		var reached = threshold == 0
			? last == TotalItems - 1
			: TotalItems - 1 - last <= threshold;
		if (reached == ThresholdReached)
			return;
		ThresholdReached = reached;
		if (!reached)
			return;
		_bridge.ThresholdReachedFires++;
		QtHostDiag.Trace(QtHostDiagChannel.QmlSignal,
			$"collection '{Host}' remaining {TotalItems - 1 - last} <= threshold {threshold} " +
			$"(last visible item {last} of {TotalItems}) → RemainingItemsThresholdReached");
		View.SendRemainingItemsThresholdReached();
	}

	/// <summary>Deepest flat item ordinal up to <paramref name="lastRow"/>; -1 when none.</summary>
	internal int LastVisibleItemOrdinal(int lastRow)
	{
		var ordinal = -1;
		for (var r = 0; r <= lastRow && r < Rows.Count; r++)
		{
			var row = Rows[r];
			if (row.Kind != KindItem || row.FirstItemOrdinal < 0)
				continue;
			ordinal = Math.Max(ordinal, row.FirstItemOrdinal + Math.Max(1, row.CellItems.Count) - 1);
		}
		return ordinal;
	}

	/// <summary>ScrollTo → native positionViewAtIndex as an immediate jump; the tick re-fires equal targets.</summary>
	internal void OnScrollToRequested(ScrollToRequestEventArgs e)
	{
		FlushInvalidate();   // an Add right before ScrollTo must be in the rows first
		if (!Host.IsAttached)
			return;
		// ScrollTo(index, groupIndex) on a grouped list: the index counts within that group.
		var rowIndex = e.Mode == ScrollToMode.Position && e.GroupIndex >= 0 && View is GroupableItemsView { IsGrouped: true }
			? GroupItemRow(e.GroupIndex, e.Index)
			: ResolveRow(e.Item ?? e.Index);
		if (rowIndex < 0)
		{
			QtHostDiag.Warn(QtHostDiagChannel.Navigation, $"collection ScrollTo target '{e.Item ?? e.Index}' not found in the row model");
			return;
		}
		if (Carousel)
		{
			// A carousel scrolls by pages: the target page becomes current.
			var page = IndexOfItem(ItemAtRow(rowIndex));
			PushPosition(page);
			return;
		}
		var pos = e.ScrollToPosition switch
		{
			ScrollToPosition.Start => 0,     // ListView.Beginning
			ScrollToPosition.Center => 1,
			ScrollToPosition.End => 2,
			_ => 3,                          // MakeVisible → Contain
		};
		ScrollRequests++;
		AdapterCommands.Send(Host, "scrollTo", new() { ["row"] = rowIndex, ["pos"] = pos });
		QtHostDiag.Trace(QtHostDiagChannel.Navigation, $"collection ScrollTo('{e.Item ?? e.Index}', {e.ScrollToPosition}) → row {rowIndex} (request {ScrollRequests})");
	}
}
