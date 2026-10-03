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

/// <summary>The row model: ItemsSource flattened into rows (groups, grid spans), each row's views built from the template and measured; unchanged rows are reused across rebuilds, a cell that changes size re-measures its row alone.</summary>
internal sealed partial class QtHostListAdapter
{
	internal void RebuildRows(double widthDp)
	{
		_inRebuild = true;
		try
		{
			RebuildRowsCore(widthDp);
		}
		finally
		{
			_inRebuild = false;
		}
	}

	private void RebuildRowsCore(double widthDp)
	{
		_bridge.MarkSceneDirty();
		UnsubscribeGroups();
		DeferredRows.Clear();   // row indices change; the resync after the rebuild queues them again
		_rowTopsDp = null;
		Reusable.Clear();
		var previous = Rows.ToList();
		Rows.Clear();
		LastWidthDp = widthDp;
		RowsDirty = false;   // a change raised during the build sets it again (Invalidate)

		ReadLayout();   // layout props may have changed without a reconcile
		// Horizontal items are arranged across the list's height: a list that grew (an Auto row taking its tallest
		// item) lays every item out again rather than keeping rows arranged for the old height.
		var crossMoved = (Horizontal || Carousel) && Math.Abs(Host.MauiLogicalBounds.Height - LastCrossDp) > 0.5;
		LastCrossDp = Host.MauiLogicalBounds.Height;
		// Rows whose items are unchanged are reused (same Row, key, views, height), so an Add/Remove only
		// inserts/removes the changed rows in QML and other delegates stay. Keyed by the row's first item, by identity
		// (an equal but replaced item may carry other data). Only while the rows would be built the same way: a new
		// template or layout builds every row again.
		var signature = BuildSignature();
		if (!crossMoved && signature == _lastSignature)
			foreach (var old in previous)
				if (old.Kind == KindItem && old.CellItems.Count >= 1 && old.CellItems[0] is { } oldItem && !oldItem.GetType().IsValueType)
				{
					if (!Reusable.TryGetValue(oldItem, out var queue))
						Reusable[oldItem] = queue = new Queue<Row>();
					queue.Enqueue(old);
				}
		_lastSignature = signature;
		var span = Math.Max(1, Span);
		// The cells share the cross axis: the width of a vertical grid, the height of a horizontal one.
		var crossDp = Horizontal && span > 1 ? Math.Max(0, Host.MauiLogicalBounds.Height) : widthDp;
		CellWidthDp = span > 1
			? (crossDp - (span - 1) * HSpacingDp) / span
			: widthDp;

		var view = View;
		var grouped = view is GroupableItemsView { IsGrouped: true } gv ? gv : null;
		var items = view.ItemsSource;
		_renderer.LayoutRequestHold++;
		try
		{
			BuildRows(items, grouped, widthDp);
		}
		finally
		{
			_renderer.LayoutRequestHold--;
		}
		ReleaseRowViews(previous, keep: Rows);

		// RemainingItemsThreshold counts flat items, not rows.
		TotalItems = 0;
		foreach (var row in Rows)
			if (row.Kind == KindItem)
				TotalItems += row.CellItems.Count;

		// Slot views are measured later by MaterializeSlots; placeholders may not exist yet.
		if (view is StructuredItemsView siv)
		{
			HeaderView = CreateSlotView(siv.Header, siv.HeaderTemplate);
			FooterView = CreateSlotView(siv.Footer, siv.FooterTemplate);
			InheritOwnerContext(siv, HeaderView);
			InheritOwnerContext(siv, FooterView);
		}
		// A plain-text EmptyView on a vertical list is Silica's ViewPlaceholder (the native empty-state text);
		// views, templates and other layouts keep the MAUI content in the empty slot.
		var placeholder = view.EmptyView is string text && view.EmptyViewTemplate is null && !Horizontal && !Carousel ? text : null;
		EmptySlotView = placeholder is null ? CreateSlotView(view.EmptyView, view.EmptyViewTemplate) : null;
		InheritOwnerContext(view, EmptySlotView);
		Push("mauiPlaceholderText", placeholder ?? string.Empty);

		_bridge.RowsBuilt += Rows.Count;
		QtHostDiag.Trace(QtHostDiagChannel.QmlObject, $"collection '{Host}' rows={Rows.Count} span={span} " +
			$"cellWidth={CellWidthDp.ToString("F0", CultureInfo.InvariantCulture)}dp " +
			$"spacing={SpacingDp.ToString("F0", CultureInfo.InvariantCulture)}dp grouped={(grouped is not null)}");

		PushLayout();
		PushRows();
		RecomputeSelection();
		PushSelection();
		SlotsDirty = true;
		// A list measured without a bound along its scroll axis (in a StackLayout or ScrollView) sizes to its rows,
		// as RecyclerView/UICollectionView do; its first measure ran before the rows existed.
		RemeasureListIfExtentMoved();
		_bridge.SchedulePending();
		// A carousel always has a current page, as with the in-box handlers.
		if (View is CarouselView carouselView && TotalItems > 0)
		{
			var position = Math.Clamp(carouselView.Position, 0, TotalItems - 1);
			PushPosition(position);
			if (carouselView.CurrentItem is null || IndexOfItem(carouselView.CurrentItem) < 0)
				OnCarouselPosition(position);
		}
		// The ListModel was refilled and reused delegates may stay silent: resync for the next few polls.
		ResyncPending = ResyncTicksAfterRebuild;
		_bridge.SchedulePending(ResyncIntervalMs);
		_renderer.NoteRowsBuilt(Rows.Count);
	}

	/// <summary>A list measured without a bound along its scroll axis sizes to its rows: when they moved its extent,
	/// it measures again.</summary>
	private void RemeasureListIfExtentMoved()
	{
		if (Math.Abs(ContentExtentDp - _measuredExtentDp) <= 0.5 && Math.Abs(CrossExtentDp - _measuredCrossDp) <= 0.5)
			return;
		_measuredExtentDp = ContentExtentDp;
		_measuredCrossDp = CrossExtentDp;
		((Microsoft.Maui.IView)View).InvalidateMeasure();
	}

	/// <summary>What decides how a row is built: the templates, the layout and its axis. Rows built under another
	/// signature are not reused.</summary>
	private string BuildSignature()
	{
		var view = View;
		var grouped = view as GroupableItemsView;
		return string.Join("|",
			System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(view.ItemTemplate ?? (object)string.Empty),
			grouped?.IsGrouped == true,
			System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(grouped?.GroupHeaderTemplate ?? (object)string.Empty),
			System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(grouped?.GroupFooterTemplate ?? (object)string.Empty),
			Horizontal, Carousel, Math.Max(1, Span));
	}

	internal void AddItemRows(IEnumerable items, int groupIndex)
	{
		var span = Math.Max(1, Span);
		var cellWidth = CellWidthDp;
		Row? row = null;
		var itemIndex = 0;
		// Flat item ordinal continues after the last built row (grouped sources).
		var ordinal = 0;
		if (Rows.Count > 0)
		{
			for (var i = Rows.Count - 1; i >= 0; i--)
				if (Rows[i].FirstItemOrdinal >= 0)
				{
					ordinal = Rows[i].FirstItemOrdinal + Math.Max(1, Rows[i].CellItems.Count);
					break;
				}
		}
		var list = items as IList ?? items.Cast<object?>().ToList();
		for (var i = 0; i < list.Count; i++)
		{
			var item = list[i];
			// A row starting here whose cells are exactly the same items: take it whole. A changed row (e.g. the
			// last, partial grid row after an append) is built new, so no view is shared between two rows.
			var atRowStart = row is null || row.CellViews.Count >= span;
			if (atRowStart && TakeReusableRow(list, i, span, cellWidth) is { } kept)
			{
				kept.Index = Rows.Count;
				kept.GroupIndex = groupIndex;
				kept.ItemIndex = itemIndex;
				kept.FirstItemOrdinal = ordinal;
				Rows.Add(kept);
				row = null;
				itemIndex += kept.CellItems.Count;
				ordinal += kept.CellItems.Count;
				i += kept.CellItems.Count - 1;
				continue;
			}
			var cell = row?.CellViews.Count ?? span;
			if (row is null || cell >= span)
			{
				row = NewRow(KindItem, groupIndex, itemIndex);
				row.FirstItemOrdinal = ordinal;
				row.CellWidthDp = cellWidth;
				cell = 0;
			}
			var itemView = CreateItemView(item);
			if (itemView is not null && Horizontal && !Carousel && span == 1)
				row.NaturalCrossDp = Math.Max(row.NaturalCrossDp, NaturalHeight(itemView));
			var height = itemView is null ? 0 : MeasureItemExtent(itemView, cellWidth);
			WatchRow(row, itemView);
			row.CellViews.Add(itemView);
			row.CellItems.Add(item);
			row.CellX.Add(cell * (cellWidth + HSpacingDp));
			row.HeightDp = Math.Max(row.HeightDp, height);
			itemIndex++;
			ordinal++;
		}
	}

	/// <summary>The previous build's row for list[start..] if it holds exactly those items at this width.</summary>
	internal Row? TakeReusableRow(IList list, int start, int span, double cellWidth)
	{
		if (list[start] is not { } first || !Reusable.TryGetValue(first, out var queue) || queue.Count == 0)
			return null;
		var candidate = queue.Peek();
		var count = Math.Min(span, list.Count - start);
		if (Math.Abs(candidate.CellWidthDp - cellWidth) >= 0.5 || candidate.CellItems.Count != count)
			return null;
		for (var k = 0; k < count; k++)
			if (!ReferenceEquals(candidate.CellItems[k], list[start + k]))
				return null;
		return queue.Dequeue();
	}

	internal void AddTemplateRow(DataTemplate? template, object context, int kind, int groupIndex, double widthDp)
	{
		if (template is null)
			return;
		var row = NewRow(kind, groupIndex, -1);
		var view = AdoptRowView(CreateFromTemplate(template, context));
		var height = view is null ? 0 : _bridge.MeasureItemView(view, widthDp);
		WatchRow(row, view);
		row.CellViews.Add(view);
		row.CellItems.Add(context);
		row.CellX.Add(0);
		row.HeightDp = height;
	}

	/// <summary>A cell that changes size (a bound text that grows, an item that shows a section) re-measures its row
	/// alone, as RecyclerView re-lays out one item on requestLayout: the row height is pushed, no row is rebuilt.</summary>
	private void WatchRow(Row row, View? view)
	{
		if (view is null)
			return;
		row.MeasureHandler ??= (_, _) => MarkRow(row);
		view.MeasureInvalidated += row.MeasureHandler;
	}

	private void UnwatchRow(Row row)
	{
		if (row.MeasureHandler is not { } handler)
			return;
		foreach (var view in row.CellViews)
			if (view is not null)
				view.MeasureInvalidated -= handler;
		row.MeasureHandler = null;
	}

	private void MarkRow(Row row)
	{
		if (!QtHostRuntime.IsQtThread)
		{
			QtHostRuntime.Post(() => MarkRow(row));
			return;
		}
		if (_bridge.SlotMapping || _inRebuild || row.MeasureHandler is null)
			return;   // our own measure/map pass, or a released row
		row.Remeasure = true;
		RowsRemeasure = true;
		_bridge.SchedulePending();
	}

	/// <summary>Measures the rows that asked again; a changed height moves the rows below it (the row tops are
	/// recomputed), is pushed with the row model and re-places the row's delegate content.</summary>
	internal void RemeasureRows()
	{
		RowsRemeasure = false;
		var changed = false;
		_bridge.SlotMapping = true;   // measuring raises measure events of its own
		try
		{
			foreach (var row in Rows)
			{
				if (!row.Remeasure)
					continue;
				row.Remeasure = false;
				var height = 0.0;
				foreach (var view in row.CellViews)
					if (view is not null)
						height = Math.Max(height, row.Kind == KindItem
							? MeasureItemExtent(view, row.CellWidthDp)
							: _bridge.MeasureItemView(view, LastWidthDp));
				if (Math.Abs(height - row.HeightDp) <= 0.5)
					continue;
				row.HeightDp = height;
				changed = true;
				RowsRemeasured++;
				if (row.DgObj is { } obj && Delegates.TryGetValue(obj, out var dg))
					UpdateDgGeometry(dg);
			}
		}
		finally
		{
			_bridge.SlotMapping = false;
		}
		if (!changed)
			return;
		_rowTopsDp = null;
		PushRows();
		RemeasureListIfExtentMoved();
	}

	internal Row NewRow(int kind, int groupIndex, int itemIndex)
	{
		var row = new Row { Index = Rows.Count, Kind = kind, GroupIndex = groupIndex, ItemIndex = itemIndex, Key = _bridge.NextRowKey() };
		Rows.Add(row);
		return row;
	}

	internal View? CreateItemView(object? item)
	{
		var template = View.ItemTemplate;
		if (template is not null)
			return AdoptRowView(CreateFromTemplate(template, item));
		// No template: MAUI shows ToString() — mirror that with a plain label.
		return item is null ? null : AdoptRowView(new Label { Text = item.ToString() ?? string.Empty });
	}

	private void BuildRows(IEnumerable? items, GroupableItemsView? grouped, double widthDp)
	{
		if (items is not null)
		{
			if (grouped is not null)
			{
				var groupIndex = -1;
				foreach (var group in items)
				{
					groupIndex++;
					if (group is not IEnumerable groupItems)
						continue;
					AddTemplateRow(grouped.GroupHeaderTemplate, group, KindGroupHeader, groupIndex, widthDp);
					AddItemRows(groupItems, groupIndex);
					AddTemplateRow(grouped.GroupFooterTemplate, group, KindGroupFooter, groupIndex, widthDp);
					if (group is INotifyCollectionChanged groupIncc)
					{
						NotifyCollectionChangedEventHandler handler = (_, _) => QueueInvalidate();
						groupIncc.CollectionChanged += handler;
						GroupSubs.Add((groupIncc, handler));
					}
				}
			}
			else
			{
				AddItemRows(items, -1);
			}
		}
	}

	private double NaturalHeight(View view)
	{
		try
		{
			QtHostLayout.AttachHandlers(view, _renderer.MauiContext);
			return Math.Max(0, ((Microsoft.Maui.IView)view).Measure(double.PositiveInfinity, double.PositiveInfinity).Height);
		}
		catch (Exception ex)
		{
			QtHostDiag.Error(QtHostDiagChannel.Geometry, $"collection item measure failed: {ex.Message}");
			return 0;
		}
	}

	/// <summary>
	/// A row's view is a logical child of the ItemsView while its row lives, as the Android and iOS handlers add it:
	/// {RelativeSource AncestorType=…} bindings in item templates (a page model's command, its IsBusy) resolve
	/// through the parent chain. Unparented, DeveloperBalance's project cards kept their shimmer on over the content.
	/// </summary>
	private View? AdoptRowView(View? view)
	{
		if (view is not null && view.Parent is null)
			View.AddLogicalChild(view);
		return view;
	}

	private void ReleaseRowViews(IEnumerable<Row> rows, IReadOnlyCollection<Row>? keep = null)
	{
		var kept = keep is null ? null : new HashSet<Row>(keep, ReferenceEqualityComparer.Instance);
		foreach (var row in rows)
		{
			if (kept is not null && kept.Contains(row))
				continue;
			UnwatchRow(row);
			foreach (var view in row.CellViews)
				if (view is not null && ReferenceEquals(view.Parent, View))
					View.RemoveLogicalChild(view);
		}
	}

	/// a carousel page is the viewport minus the peek insets.</summary>
	internal double MeasureItemExtent(View view, double widthDp)
	{
		if (!Horizontal && !Carousel)
			return _bridge.MeasureItemView(view, widthDp);
		try
		{
			QtHostLayout.AttachHandlers(view, _renderer.MauiContext);
			var heightDp = Math.Max(0, Host.MauiLogicalBounds.Height);
			if (Carousel)
			{
				var w = Horizontal ? Math.Max(0, widthDp - PeekStartDp - PeekEndDp) : widthDp;
				var h = Horizontal ? heightDp : Math.Max(0, heightDp - PeekStartDp - PeekEndDp);
				QtHostLayout.MeasureAndArrangeItemFixed(view, w, h);
				return Horizontal ? w : h;
			}
			// A horizontal grid cell spans its share of the height (widthDp carries the cell's cross extent).
			return QtHostLayout.MeasureAndArrangeItemAcross(view, Span > 1 ? widthDp : heightDp);
		}
		catch (Exception ex)
		{
			QtHostDiag.Error(QtHostDiagChannel.Geometry, $"collection item measure failed: {ex.Message}");
			return 0;
		}
	}
}
