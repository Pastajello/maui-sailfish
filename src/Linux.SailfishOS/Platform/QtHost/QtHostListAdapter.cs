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

/// <summary>
/// One CollectionView/CarouselView's side of the native ListView, owned by its handler as a RecyclerView adapter is
/// by the Android handler: it flattens ItemsSource into rows, measures each row's template, and materializes a row's
/// element tree as QML hosts inside the delegate the ListView binds (list-item-attached/rebind), so they scroll and
/// die with it. <see cref="QtHostCollectionBridge"/> routes the QML events to it and schedules its deferred work.
/// </summary>
internal sealed partial class QtHostListAdapter
{
	private readonly QtHostCollectionBridge _bridge;
	private readonly QtHostPageRenderer _renderer;

	internal QtHostListAdapter(QtHostCollectionBridge bridge, QtHostPageRenderer renderer)
	{
		_bridge = bridge;
		_renderer = renderer;
	}

	/// <summary>Subscribes to the view's scroll requests, selection and source (once, on registration).</summary>
	internal void Attach()
	{
		ScrollHandler = (_, e) => OnScrollToRequested(e);
		View.ScrollToRequested += ScrollHandler;
		if (View is SelectableItemsView selectable)
		{
			// SelectionChanged also fires when SelectedItems mutates, where PropertyChanged stays silent.
			SelectionHandler = (_, _) =>
			{
				// May be raised off the Qt thread; PushSelection mutates QML.
				QtHostRuntime.RunOnQtThread(() =>
				{
					RecomputeSelection();
					PushSelection();
				});
			};
			selectable.SelectionChanged += SelectionHandler;
		}
		SubscribeSource();
		ReadLayout();
	}

	/// <summary>A mapped ItemsView property changed (SailfishListViewHandler's mapper).</summary>
	internal void OnViewProperty(string propertyName)
	{
		if (!_bridge.IsRegistered(this))
			return;   // retired: the next registration computes everything
		switch (propertyName)
		{
			case nameof(ItemsView.ItemsSource):
				SubscribeSource();
				Invalidate();
				break;
			case nameof(SelectableItemsView.SelectionMode):
			case nameof(SelectableItemsView.SelectedItem):
			case nameof(SelectableItemsView.SelectedItems):
				RecomputeSelection();
				PushSelection();
				PushRows();   // the tappable flag follows SelectionMode
				break;
			case nameof(CarouselView.Position):
			{
				// App-set Position: move the native page and update CurrentItem (our own push is not reported back).
				var carousel = (CarouselView)View;
				PushPosition(carousel.Position);
				OnCarouselPosition(carousel.Position);
				break;
			}
			case nameof(CarouselView.CurrentItem):
			{
				// App-set current item: the page moves there and Position follows.
				var carousel = (CarouselView)View;
				if (IndexOfItem(carousel.CurrentItem) is var ci and >= 0 && ci != carousel.Position)
				{
					PushPosition(ci);
					OnCarouselPosition(ci);
				}
				break;
			}
			case nameof(CarouselView.IsSwipeEnabled):
			case nameof(CarouselView.IsBounceEnabled):
			case nameof(ItemsView.VerticalScrollBarVisibility):
			case nameof(ItemsView.HorizontalScrollBarVisibility):
				PushLayout();
				break;
			case nameof(CarouselView.PeekAreaInsets):
			case nameof(StructuredItemsView.ItemsLayout):
			case nameof(StructuredItemsView.Header):
			case nameof(StructuredItemsView.Footer):
			case nameof(ItemsView.EmptyView):
			case nameof(ItemsView.ItemTemplate):
			case nameof(GroupableItemsView.IsGrouped):
			case nameof(GroupableItemsView.GroupHeaderTemplate):
			case nameof(GroupableItemsView.GroupFooterTemplate):
				Invalidate();
				break;
		}
	}


	public ItemsView View = null!;
	public NativeElementHost Host = null!;
	public readonly List<Row> Rows = new();

	private double _measuredExtentDp;

	/// <summary>The rows' extent along the scroll axis with the spacing between them (dp): what the list measures to
	/// when nothing bounds it along that axis (SailfishMeasure.Collection).</summary>
	internal double ContentExtentDp
	{
		get
		{
			double extent = 0;
			foreach (var row in Rows)
				extent += row.HeightDp;
			return Rows.Count > 1 ? extent + (Rows.Count - 1) * SpacingDp : extent;
		}
	}

	/// <summary>A horizontal list's tallest item measured without a height bound (dp): its height when nothing bounds
	/// it across the scroll axis (an Auto grid row), as a wrap_content RecyclerView takes its tallest child.</summary>
	internal double CrossExtentDp
	{
		get
		{
			double extent = 0;
			foreach (var row in Rows)
				extent = Math.Max(extent, row.NaturalCrossDp);
			return extent;
		}
	}

	private double _measuredCrossDp;
	public readonly Dictionary<object, Queue<Row>> Reusable = new(ReferenceEqualityComparer.Instance);   // RebuildRows scratch
	public readonly Dictionary<string, DgState> Delegates = new(StringComparer.Ordinal);
	public readonly Dictionary<long, DgState> ByHandle = new();   // ListView recycles+renames delegates
	public readonly Dictionary<string, SlotState> Slots = new(StringComparer.Ordinal);
	public readonly HashSet<(int Row, int Cell)> SelectedCells = new();   // grid rows hold several items

	/// <summary>Model-page instance the row/slot hosts were created on. Destroys must go there: during a
	/// push the mirror top is the incoming page, where they would be a no-op and leak.</summary>
	public string PageId = string.Empty;

	public bool RowsDirty = true;
	public bool SlotsDirty = true;
	public double LastWidthDp = -1;
	public double LastCrossDp = -1;     // horizontal/carousel: the list height the rows were built for
	public bool Horizontal;             // horizontal LinearItemsLayout (rows = columns)
	public bool Carousel;               // one snapped page per item
	public double PeekStartDp;          // PeekAreaInsets along the scroll axis
	public double PeekEndDp;
	public double CellWidthDp;
	public double HSpacingDp;
	public double SpacingDp;
	public int Span = 1;
	public string LastCellLayout = string.Empty;   // mauiSpan/mauiCellWidth/mauiCellStride diff basis
	public int InvalidatePending;                   // 1 while a coalesced rebuild is queued (any thread)

	public double LastReportedYDp = -1;
	public int FirstVisibleRow = -1;
	public int LastVisibleRow = -1;
	public int ScrollTick;
	public int TotalItems;              // flat item count (threshold contract)
	public bool ThresholdReached;       // edge state: fire once per crossing
	public readonly QtHostRefreshBinding RefreshBinding = new();
	public RefreshView? Refresh => RefreshBinding.View;   // wrapping RefreshView

	/// <summary>Remaining poll passes that re-enumerate live delegates from the visual tree. Qt 5.6 reuses
	/// cached delegates after a model reset or scroll without firing onCompleted or rebind.</summary>
	public int ResyncPending;

	public string LastRowsJson = string.Empty;
	public string LastSelJson = string.Empty;

	public View? HeaderView;
	public View? FooterView;
	public View? EmptySlotView;

	public INotifyCollectionChanged? SourceSub;
	public NotifyCollectionChangedEventHandler? SourceHandler;
	public readonly List<(INotifyCollectionChanged Sub, NotifyCollectionChangedEventHandler Handler)> GroupSubs = new();
	public EventHandler<ScrollToRequestEventArgs>? ScrollHandler;
	public EventHandler<SelectionChangedEventArgs>? SelectionHandler;

	/// <summary>Swaps the list's RefreshView and its IsRefreshing subscription.</summary>
	internal void ArmRefresh(RefreshView? refresh)
	{
		RefreshBinding.Arm(refresh, r =>
		{
			foreach (var kv in QtHostPageRenderer.RefreshSurfaceProps(r))
				Push(kv.Key, kv.Value);
		});
	}

	internal void SubscribeSource()
	{
		if (SourceSub is not null && SourceHandler is not null)
			SourceSub.CollectionChanged -= SourceHandler;
		SourceSub = View.ItemsSource as INotifyCollectionChanged;
		if (SourceSub is not null)
		{
			SourceHandler ??= (_, _) => QueueInvalidate();
			SourceSub.CollectionChanged += SourceHandler;
		}
	}

	internal void ReadLayout()
	{
		Span = 1;
		SpacingDp = 0;
		HSpacingDp = 0;
		Horizontal = false;
		Carousel = false;
		PeekStartDp = 0;
		PeekEndDp = 0;
		if (View is CarouselView carousel)
		{
			// One snapped page per item (horizontal by default); PeekAreaInsets shrink it so neighbours show.
			Carousel = true;
			var linearLayout = carousel.ItemsLayout;
			Horizontal = linearLayout is null || linearLayout.Orientation == ItemsLayoutOrientation.Horizontal;
			SpacingDp = linearLayout?.ItemSpacing ?? 0;
			var peek = carousel.PeekAreaInsets;
			PeekStartDp = Horizontal ? peek.Left : peek.Top;
			PeekEndDp = Horizontal ? peek.Right : peek.Bottom;
			return;
		}
		if (View is not StructuredItemsView siv)
			return;
		switch (siv.ItemsLayout)
		{
			case GridItemsLayout grid:
				// A horizontal grid scrolls along x: each list row is a column of Span cells stacked down the list's
				// height. SpacingDp runs along the scroll axis, HSpacingDp between the cells of one row.
				Span = Math.Max(1, grid.Span);
				Horizontal = grid.Orientation == ItemsLayoutOrientation.Horizontal;
				SpacingDp = Horizontal ? grid.HorizontalItemSpacing : grid.VerticalItemSpacing;
				HSpacingDp = Horizontal ? grid.VerticalItemSpacing : grid.HorizontalItemSpacing;
				break;
			case LinearItemsLayout linear:
				SpacingDp = linear.ItemSpacing;
				Horizontal = linear.Orientation == ItemsLayoutOrientation.Horizontal;
				break;
		}
	}

	/// <summary>Marks the rows stale; rebuilds at once when the list is live, else on its scheduled pass.</summary>
	internal void Invalidate()
	{
		RowsDirty = true;
		_bridge.SchedulePending();
		if (Host.IsAttached && Host.MauiLogicalBounds.Width > 0)
			RebuildRows(Host.MauiLogicalBounds.Width);
	}

	/// <summary>
	/// Collections may change on any thread, but rebuilding pushes into QML, so it runs on the Qt thread. A burst of
	/// changes (a page of results appended one Add at a time) rebuilds once, on the next loop turn.
	/// </summary>
	internal void QueueInvalidate()
	{
		if (Interlocked.Exchange(ref InvalidatePending, 1) == 0)
			QtHostRuntime.Post(() => FlushInvalidate());
	}

	/// <summary>Runs a queued rebuild now; for work that needs the rows to match the collection (ScrollTo).</summary>
	internal void FlushInvalidate()
	{
		if (Interlocked.Exchange(ref InvalidatePending, 0) == 1 &&
		    _bridge.IsRegistered(this))
			Invalidate();
	}

	/// <summary>Parks an unresolvable delegate attach for poll retries (deduped by delegate; newest row wins).</summary>
	internal void QueueAttachRetry(int rowIndex, string dgObj)
	{
		for (var i = 0; i < _bridge.AttachRetries.Count; i++)
		{
			if (_bridge.AttachRetries[i].State == this && _bridge.AttachRetries[i].Dg == dgObj)
			{
				_bridge.AttachRetries[i] = (this, rowIndex, dgObj, _bridge.AttachRetries[i].Attempts);
				return;
			}
		}
		QtHostDiag.Warn(QtHostDiagChannel.QmlObject, $"delegate '{dgObj}' not resolvable yet (row {rowIndex}) — parked for poll retry (lastError='{QtHostRuntime.LastErrorText}')");
		_bridge.AttachRetries.Add((this, rowIndex, dgObj, 0));
		_bridge.SchedulePending(ResyncIntervalMs);
	}

	internal void DropAttachRetries(string dgObj)
	{
		for (var i = _bridge.AttachRetries.Count - 1; i >= 0; i--)
			if (_bridge.AttachRetries[i].State == this && _bridge.AttachRetries[i].Dg == dgObj)
				_bridge.AttachRetries.RemoveAt(i);
	}

	/// <summary>Materializes the row delegates currently in the QML visual tree (idempotent). Needed because
	/// Qt 5.6 refills from its delegate cache without onCompleted or rebind events.</summary>
	internal void ResyncDelegates()
	{
		if (!Host.IsAttached)
			return;
		var prefix = "maui_" + Host.Id + "__r";
		// Walk the list's own page: a back-cached page's list is not under currentPage.
		var pageJs = PageId.Length > 0
			? QmlPage.ByIdOr(PageId, "pageStack.currentPage")
			: "pageStack.currentPage";
		// Delegates are direct children of the list's contentItem (a PathView carousel: of the view itself), so
		// one level there is enough; the whole-page walk is only the fallback when the list host is not found.
		var js = "(function(){var p='" + prefix + "',pg=" + pageJs + ",a=[];" +
			"var h=pg&&pg.__hosts?pg.__hosts['" + Host.Id + "']:null,v=h&&h.item;" +
			"if(v){var k=(v.contentItem||v).children;" +
			"for(var i=0;i<k.length;++i){var nm=k[i].objectName;if(nm&&(''+nm).indexOf(p)===0)a.push(''+nm);}" +
			"return a.join(',');}" +
			"function N(o){var nm=o.objectName;" +
			"if(nm&&(''+nm).indexOf(p)===0)a.push(''+nm);" +
			"var c=o.children;if(c)for(var j=0;j<c.length;++j)N(c[j]);}" +
			"if(pg)N(pg);return a.join(',');})()";
		string names;
		try
		{
			names = QtHostRuntime.Eval(js);
		}
		catch (Exception ex)
		{
			QtHostDiag.Error(QtHostDiagChannel.QmlSignal, $"collection resync eval failed: {ex.Message}");
			return;
		}
		if (string.IsNullOrEmpty(names))
			return;
		foreach (var name in names.Split(','))
		{
			if (name.Length <= prefix.Length || !name.StartsWith(prefix, StringComparison.Ordinal))
				continue;
			if (!int.TryParse(name.AsSpan(prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var row))
				continue;
			RequestMaterialize(row, name);
		}
	}

	internal void RebuildRows(double widthDp)
	{
		_bridge.MarkSceneDirty();
		UnsubscribeGroups();
		DeferredRows.Clear();   // row indices change; the resync after the rebuild queues them again
		_rowTopsDp = null;
		// Rows whose items are unchanged are reused (same Row, key, views, height), so an Add/Remove only
		// inserts/removes the changed rows in QML and other delegates stay. Keyed by the row's first item.
		Reusable.Clear();
		foreach (var old in Rows)
			if (old.Kind == KindItem && old.CellItems.Count >= 1 && old.CellItems[0] is { } oldItem && !oldItem.GetType().IsValueType)
			{
				if (!Reusable.TryGetValue(oldItem, out var queue))
					Reusable[oldItem] = queue = new Queue<Row>();
				queue.Enqueue(old);
			}
		var previous = Rows.ToList();
		Rows.Clear();
		LastWidthDp = widthDp;
		RowsDirty = false;

		ReadLayout();   // layout props may have changed without a reconcile
		// Horizontal items are arranged across the list's height: a list that grew (an Auto row taking its tallest
		// item) lays every item out again rather than keeping rows arranged for the old height.
		if ((Horizontal || Carousel) && Math.Abs(Host.MauiLogicalBounds.Height - LastCrossDp) > 0.5)
			Reusable.Clear();
		LastCrossDp = Host.MauiLogicalBounds.Height;
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
		if (Math.Abs(ContentExtentDp - _measuredExtentDp) > 0.5 || Math.Abs(CrossExtentDp - _measuredCrossDp) > 0.5)
		{
			_measuredExtentDp = ContentExtentDp;
			_measuredCrossDp = CrossExtentDp;
			((Microsoft.Maui.IView)View).InvalidateMeasure();
		}
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
		row.CellViews.Add(view);
		row.CellItems.Add(context);
		row.CellX.Add(0);
		row.HeightDp = height;
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

	/// <summary>Orientation/paging props of the adapter, in Qt units.</summary>
	internal Dictionary<string, object?> LayoutProps()
	{
		var props = new Dictionary<string, object?>
		{
			["mauiSpacing"] = SpacingDp * SailfishDisplay.Density,
			["mauiOrientation"] = Horizontal ? "horizontal" : "vertical",
			["mauiCarousel"] = Carousel,
			// ScrollBarVisibility (Default 0 / Always 1 / Never 2) → the Silica scroll decorator every SilicaListView has.
			["mauiVBar"] = (int)View.VerticalScrollBarVisibility,
			["mauiHBar"] = (int)View.HorizontalScrollBarVisibility,
		};
		if (View is CarouselView carousel)
		{
			props["mauiPeekStart"] = QtHostUnits.ToQtUnits(PeekStartDp);
			props["mauiPeekEnd"] = QtHostUnits.ToQtUnits(PeekEndDp);
			props["mauiSwipeEnabled"] = carousel.IsSwipeEnabled;
			props["mauiBounce"] = carousel.IsBounceEnabled;
			props["mauiPosition"] = carousel.Position;
		}
		return props;
	}

	internal void PushLayout()
	{
		ReadLayout();
		PushMany(LayoutProps());
	}

	/// <summary>Moves the native carousel to page <paramref name="index"/>.</summary>
	internal void PushPosition(int index)
	{
		if (!Carousel || index < 0)
			return;
		Push("mauiPosition", index);
	}

	/// <summary>The flat item ordinal of <paramref name="item"/> (-1 when absent).</summary>
	internal int IndexOfItem(object? item)
	{
		if (item is null)
			return -1;
		var ordinal = 0;
		foreach (var row in Rows)
		{
			if (row.Kind != KindItem)
				continue;
			foreach (var cell in row.CellItems)
			{
				if (ReferenceEquals(cell, item) || Equals(cell, item))
					return ordinal;
				ordinal++;
			}
		}
		return -1;
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
		if (!ReferenceEquals(carousel.CurrentItem, item) && item is not null)
			carousel.CurrentItem = item;
		QtHostDiag.Trace(QtHostDiagChannel.Input, $"carousel '{Host}' page {index} → Position/CurrentItem");
	}

	internal object? ItemAt(int ordinal)
	{
		var i = 0;
		foreach (var row in Rows)
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

	internal void UnsubscribeGroups()
	{
		foreach (var (sub, handler) in GroupSubs)
			sub.CollectionChanged -= handler;
		GroupSubs.Clear();
	}

	internal void Push(string name, object? value)
	{
		if (!Host.IsAttached)
			return;
		var json = BridgeValue.Serialize(value);
		if (!Changed(name, json))
			return;
		// Recorded as applied, so the page reconcile's diff does not send it again.
		if (QtHostRuntime.SetProperty(Host.NativeHandle, name, json) == 0)
			Host.AppliedProperties[name] = json;
	}

	/// <summary>Several properties in one ordered native batch (no mauiApplying envelope: adapter events stay live),
	/// only those that changed.</summary>
	internal void PushMany(IEnumerable<KeyValuePair<string, object?>> props)
	{
		if (!Host.IsAttached)
			return;
		var changed = new List<(string Name, string Json)>();
		foreach (var (name, value) in props)
		{
			var json = BridgeValue.Serialize(value);
			if (Changed(name, json))
				changed.Add((name, json));
		}
		if (changed.Count == 0)
			return;
		if (changed.Count == 1)
		{
			if (QtHostRuntime.SetProperty(Host.NativeHandle, changed[0].Name, changed[0].Json) == 0)
				Host.AppliedProperties[changed[0].Name] = changed[0].Json;
			return;
		}
		var sb = new StringBuilder("[");
		for (var i = 0; i < changed.Count; i++)
			sb.Append(i > 0 ? "," : string.Empty).Append("{\"name\":").Append(BridgeValue.Quote(changed[i].Name))
			  .Append(",\"value\":").Append(changed[i].Json).Append('}');
		if (QtHostRuntime.ApplyProperties(Host.NativeHandle, sb.Append(']').ToString()) == 0)
			foreach (var (name, json) in changed)
				Host.AppliedProperties[name] = json;
	}

	// The native list moves its carousel page itself (a swipe), so the last pushed position is no proof of the
	// native one: it is always sent. Everything else only changes through these pushes.
	private bool Changed(string name, string json) =>
		name == "mauiPosition" || !Host.AppliedProperties.TryGetValue(name, out var applied) || applied != json;

	internal void PushRows()
	{
		if (!Host.IsAttached)
			return;
		var selectable = View is SelectableItemsView { SelectionMode: not SelectionMode.None };
		var sb = new StringBuilder(32 + Rows.Count * 32);
		sb.Append('[');
		for (var i = 0; i < Rows.Count; i++)
		{
			var row = Rows[i];
			if (i > 0)
				sb.Append(',');
			// No selection flag: a new mauiRowsJson resets the whole QML model, while mauiSelectedRows only flips a role.
			sb.Append("{\"k\":").Append(row.Key.ToString(CultureInfo.InvariantCulture))
			  .Append(",\"r\":").Append(i.ToString(CultureInfo.InvariantCulture))
			  .Append(",\"h\":").Append(QtHostUnits.ToQtUnits(row.HeightDp).ToString("R", CultureInfo.InvariantCulture))
			  .Append(",\"t\":").Append(row.Kind != KindItem ? '0' : selectable ? '1' : RowHasTap(row) ? '2' : '0')
			  .Append(",\"n\":").Append((row.Kind == KindItem ? row.CellItems.Count : 0).ToString(CultureInfo.InvariantCulture))
			  .Append('}');
		}
		sb.Append(']');
		PushCellLayout();
		var json = sb.ToString();
		if (json == LastRowsJson)
			return;
		LastRowsJson = json;
		Push("mauiRowsJson", json);
		Push("mauiSpacing", SpacingDp * SailfishDisplay.Density);
	}

	/// <summary>Grid cell geometry (Qt units, along x) so the delegate can highlight and report the touched cell.</summary>
	internal void PushCellLayout()
	{
		var span = Math.Max(1, Span);
		var width = QtHostUnits.ToQtUnits(CellWidthDp);
		var stride = QtHostUnits.ToQtUnits(CellWidthDp + HSpacingDp);
		var key = $"{span}|{BridgeValue.Number(width)}|{BridgeValue.Number(stride)}";
		if (key == LastCellLayout)
			return;
		LastCellLayout = key;
		Push("mauiSpan", span);
		Push("mauiCellWidth", width);
		Push("mauiCellStride", stride);
	}

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
				if (row.CellItems[c] is { } cell && (ReferenceEquals(cell, single) || selected?.Contains(cell) == true))
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

	internal void MaterializeSlots(double widthDp)
	{
		if (!Host.IsAttached)
			return;
		var pending = false;
		// In a horizontal list header/footer extents are widths measured at the list height.
		pending |= MaterializeSlot("header", HeaderView, "mauiHeaderH", widthDp, Horizontal);
		pending |= MaterializeSlot("footer", FooterView, "mauiFooterH", widthDp, Horizontal);
		pending |= MaterializeSlot("empty", EmptySlotView, "mauiEmptyH", widthDp, false);
		SlotsDirty = pending;
		if (pending)
			_bridge.SchedulePending(ResyncIntervalMs);   // a placeholder not resolvable yet: retry shortly
	}

	/// <summary>Materializes one slot's content in its placeholder; true while the placeholder is not
	/// resolvable yet (retried on the list's own clock).</summary>
	internal bool MaterializeSlot(string slot, View? view, string heightProp, double widthDp, bool alongRows)
	{
		if (Slots.TryGetValue(slot, out var existing))
		{
			if (existing.Remap && existing.Root is not null)
			{
				UnwatchSlot(existing);
				_bridge.DestroyHosts(existing.Children, PageId);
				Slots.Remove(slot);   // materialized again below, in the same placeholder
			}
			else
			{
				var sizeChanged = alongRows
					? Math.Abs(Host.MauiLogicalBounds.Height - existing.CrossDp) > 0.5
					: Math.Abs(widthDp - existing.WidthDp) > 0.5;
				if (existing.Root is not null && (existing.Remeasure || sizeChanged))
				{
					existing.CrossDp = alongRows ? Host.MauiLogicalBounds.Height : existing.CrossDp;
					existing.WidthDp = widthDp;
					var extent = MeasureSlot(existing.Root, widthDp, alongRows);
					if (Math.Abs(extent - existing.ExtentDp) > 0.25)
					{
						existing.ExtentDp = extent;
						_rowTopsDp = null;   // rows start below the header
						Push(existing.HeightProp, QtHostUnits.ToQtUnits(extent));
						_bridge.MarkSceneDirty();
					}
				}
				existing.Remeasure = false;
				UpdateSlotGeometry(existing);
				return false;
			}
		}
		var objName = $"maui_{Host.Id}__{slot}";
		var handle = QtHostRuntime.FindObject(objName);
		if (handle == 0)
			handle = QtHostRuntime.FindVisual(Host.NativeHandle, objName);
		if (handle == 0)
		{
			if (view is null)
			{
				// No content: done at height 0, the placeholder stays invisible.
				Slots[slot] = new SlotState { Obj = objName, Handle = 0 };
				return false;
			}
			return true;   // placeholder still being created
		}

		var heightDp = 0.0;
		var slotState = new SlotState { Obj = objName, Handle = handle, HeightProp = heightProp };
		if (view is not null)
		{
			if (alongRows)
				slotState.CrossDp = Host.MauiLogicalBounds.Height;
			slotState.WidthDp = widthDp;
			heightDp = MeasureSlot(view, widthDp, alongRows);
			slotState.ExtentDp = heightDp;
			_rowTopsDp = null;
			var desired = new List<NativeElementHost>();
			var props = new Dictionary<NativeElementHost, Dictionary<string, object?>>();
			_bridge.SlotMapping = true;   // handler attach and templating raise tree/measure events of their own
			try
			{
				_renderer.MapItemSubtree(view, desired, props);
			}
			finally
			{
				_bridge.SlotMapping = false;
			}
			_bridge.CollectHosts(view, slotState.Children);
			slotState.Root = view;
			CreateAndAttachHosts(desired, props, objName, handle);
			PageId = ListPageId();
			WatchSlot(slotState, view);
		}
		Slots[slot] = slotState;
		Push(heightProp, QtHostUnits.ToQtUnits(heightDp));
		UpdateSlotGeometry(slotState);
		return false;
	}

	internal double MeasureSlot(View view, double widthDp, bool alongRows)
	{
		_bridge.SlotMapping = true;
		try
		{
			return alongRows ? MeasureItemExtent(view, widthDp) : _bridge.MeasureItemView(view, widthDp);
		}
		finally
		{
			_bridge.SlotMapping = false;
		}
	}

	// A header on iOS/Android grows with its content (a bound section appearing, rows added to a stack); here
	// the slot re-measures on MeasureInvalidated and re-creates its hosts when views come or go.
	internal void WatchSlot(SlotState slot, View view)
	{
		slot.MeasureHandler = (_, _) => MarkSlot(slot, remap: false);
		slot.TreeHandler = (_, _) => MarkSlot(slot, remap: true);
		view.MeasureInvalidated += slot.MeasureHandler;
		view.DescendantAdded += slot.TreeHandler;
		view.DescendantRemoved += slot.TreeHandler;
	}

	internal void MarkSlot(SlotState slot, bool remap)
	{
		if (!QtHostRuntime.IsQtThread)
		{
			QtHostRuntime.Post(() => MarkSlot(slot, remap));
			return;
		}
		if (_bridge.SlotMapping || slot.MeasureHandler is null)
			return;   // our own measure/map pass, or a retired slot
		slot.Remeasure = true;
		slot.Remap |= remap;
		SlotsDirty = true;
		_bridge.SchedulePending();
		QtHostPageRenderer.RequestPoll();
	}

	/// <summary>Rows whose delegates sit in the ListView's cache buffer, off screen: materialized a few per loop
	/// turn nearest-first (<see cref="DrainDeferredRows"/>), so a page fill or a load-more never blocks one frame
	/// on every new delegate. MAUI_SAILFISH_LIST_DEFER=0 materializes everything at once (A/B runs).</summary>
	internal readonly List<int> DeferredRows = new();
	internal static readonly bool DeferOffscreenRows = Environment.GetEnvironmentVariable("MAUI_SAILFISH_LIST_DEFER") != "0";
	private const double DeferMarginViewports = 0.5;   // rows this close to the viewport are built at once
	private double[]? _rowTopsDp;                       // row tops along the scroll axis; reset by RebuildRows

	/// <summary>A delegate for <paramref name="rowIndex"/> appeared or rebound: materialize it now when it is on
	/// (or next to) the screen, otherwise queue it for the budgeted drain.</summary>
	internal void RequestMaterialize(int rowIndex, string dgObj)
	{
		if (!ShouldDefer(rowIndex) ||
		    (Delegates.TryGetValue(dgObj, out var known) && known.Row is { } r && rowIndex < Rows.Count &&
		     ReferenceEquals(r, Rows[rowIndex]) && known.Children.Count > 0))
		{
			MaterializeRow(rowIndex, dgObj);   // on screen, or an already built row that only needs placing
			return;
		}
		if (!DeferredRows.Contains(rowIndex))
			DeferredRows.Add(rowIndex);
		_bridge.SchedulePending();
	}

	private bool ShouldDefer(int rowIndex)
	{
		if (!DeferOffscreenRows || Horizontal || Carousel || rowIndex < 0 || rowIndex >= Rows.Count)
			return false;
		var viewport = Host.MauiLogicalBounds.Height;
		if (viewport <= 0)
			return false;
		var (top, bottom) = RowExtentDp(rowIndex);
		var offset = Math.Max(0, LastReportedYDp);
		var margin = viewport * DeferMarginViewports;
		return bottom < offset - margin || top > offset + viewport + margin;
	}

	private (double Top, double Bottom) RowExtentDp(int rowIndex)
	{
		if (_rowTopsDp is null || _rowTopsDp.Length != Rows.Count)
		{
			// The same arithmetic as ListView.qml's __reportScroll: rows start below the header slot.
			_rowTopsDp = new double[Rows.Count];
			var y = Slots.TryGetValue("header", out var header) ? header.ExtentDp : 0;
			for (var i = 0; i < Rows.Count; i++)
			{
				_rowTopsDp[i] = y;
				y += Rows[i].HeightDp + SpacingDp;
			}
		}
		return (_rowTopsDp[rowIndex], _rowTopsDp[rowIndex] + Rows[rowIndex].HeightDp);
	}

	/// <summary>Materializes queued off-screen rows nearest to the viewport first until <paramref name="budgetMs"/>
	/// is spent (at least one row per call). True when rows are left for a later turn.</summary>
	internal bool DrainDeferredRows(System.Diagnostics.Stopwatch clock, double budgetMs)
	{
		if (DeferredRows.Count == 0)
			return false;
		var prefix = "maui_" + Host.Id + "__r";
		var viewTop = Math.Max(0, LastReportedYDp);
		var viewBottom = viewTop + Math.Max(0, Host.MauiLogicalBounds.Height);
		do
		{
			var best = 0;
			var bestDistance = double.MaxValue;
			for (var i = 0; i < DeferredRows.Count; i++)
			{
				var row = DeferredRows[i];
				if (row >= Rows.Count)
					continue;
				var (top, bottom) = RowExtentDp(row);
				var distance = bottom < viewTop ? viewTop - bottom : top > viewBottom ? top - viewBottom : 0;
				if (distance < bestDistance)
				{
					bestDistance = distance;
					best = i;
				}
			}
			var next = DeferredRows[best];
			DeferredRows.RemoveAt(best);
			// The delegate is addressed by row: a recycled one was renamed; a released one is simply gone.
			if (next < Rows.Count)
				MaterializeRow(next, prefix + next.ToString(CultureInfo.InvariantCulture), retryIfMissing: false);
		}
		while (DeferredRows.Count > 0 && clock.Elapsed.TotalMilliseconds < budgetMs);
		return DeferredRows.Count > 0;
	}

	internal void MaterializeRow(int rowIndex, string dgObj, bool retryIfMissing = true)
	{
		_bridge.MarkSceneDirty();
		if (rowIndex < 0 || rowIndex >= Rows.Count || dgObj.Length == 0)
			return;
		// The list's visual tree first: after a jump (ScrollTo) a released delegate can still carry the same name while
		// Qt deletes it later, and a global name search may return it; the content would die with it. Released
		// delegates are unparented from the list, so the scoped search finds the live one.
		var handle = QtHostRuntime.FindVisual(Host.NativeHandle, dgObj);
		if (handle == 0)
			handle = QtHostRuntime.FindObject(dgObj);   // a parked page's delegates hang outside the visual tree
		if (handle == 0 && !retryIfMissing)
			return;   // a deferred row whose delegate Qt released meanwhile: nothing to build
		if (handle == 0)
		{
			// Hidden-page delegates are parked unparented by Qt 5.6; retry from the poll loop.
			QueueAttachRetry(rowIndex, dgObj);
			return;
		}
		DropAttachRetries(dgObj);   // resolved: cancel any parked retry

		DgState dg;
		if (ByHandle.TryGetValue(handle, out var recycled))
		{
			dg = recycled;
			if (dg.Obj != dgObj)
			{
				// The recycled delegate was renamed for its new row: re-key the name index. Renames cascade on an
				// insert (r0→r1, r1→r2, …), so the old name may already be the next delegate's: drop it only while it
				// is still this one's. A delegate not renamed yet may still hold the new name; it re-keys itself on
				// its own rebind, and ByHandle keeps it meanwhile (the name index is only a lookup by name).
				if (Delegates.TryGetValue(dg.Obj, out var holder) && ReferenceEquals(holder, dg))
					Delegates.Remove(dg.Obj);
				dg.Obj = dgObj;
				Delegates[dgObj] = dg;
			}
			// Compare Row objects, not indices: after a rebuild a silently reused delegate must move to the new
			// cell views, or it paints the old ones' stale bounds.
			if (ReferenceEquals(dg.Row, Rows[rowIndex]) && dg.Children.Count > 0 && ChildrenAlive(dg))
			{
				QtHostDiag.Trace(QtHostDiagChannel.QmlObject,
					$"Q14 row {rowIndex} dg '{dgObj}' early-return (children={dg.Children.Count})");
				UpdateDgGeometry(dg);   // re-attach of the same row: place only
				return;
			}
			QtHostDiag.Trace(QtHostDiagChannel.QmlObject,
				$"Q14 row {rowIndex} dg '{dgObj}' rebind — clearing children={dg.Children.Count} prevRow={dg.Row?.Index.ToString() ?? "-"}");
			if (ReferenceEquals(dg.Row, Rows[rowIndex]) && dg.Children.Count > 0)
				_bridge.SameRowRebuilds++;   // the same row built again: its hosts died, or it only looked dead
			ClearDg(dg);                       // rebind: drop the previous row's hosts
		}
		else
		{
			if (ByHandle.Count >= MaxDelegates)
			{
				QtHostDiag.Warn(QtHostDiagChannel.QmlObject, $"collection delegate cap ({MaxDelegates}) reached — row {rowIndex} not materialized");
				return;
			}
			// A different delegate may still hold the name: a dead one is dropped; a live one is only waiting for its
			// rename (an insert shifts every row) and keeps its content.
			if (Delegates.TryGetValue(dgObj, out var stale) && !QtHostRuntime.TryItemGeometry(stale.Handle, out _))
			{
				ByHandle.Remove(stale.Handle);
				ClearDg(stale);
			}
			dg = new DgState { Obj = dgObj, Handle = handle };
			Delegates[dgObj] = dg;
			ByHandle[handle] = dg;
			QtHostDiag.Trace(QtHostDiagChannel.QmlObject,
				$"Q14 row {rowIndex} dg '{dgObj}' fresh delegate (registry={ByHandle.Count})");
		}

		var row = Rows[rowIndex];
		dg.Row = row;
		row.DgObj = dgObj;

		var desired = new List<NativeElementHost>();
		var props = new Dictionary<NativeElementHost, Dictionary<string, object?>>();
		for (var c = 0; c < row.CellViews.Count; c++)
		{
			var cellView = row.CellViews[c];
			if (cellView is null)
				continue;
			_renderer.MapItemSubtree(cellView, desired, props);
			_bridge.CollectHosts(cellView, dg.Children);
			dg.Cells.Add((cellView, row.CellX[c]));
		}
		if (!TryAdoptPooledRow(dg, desired, props))
			CreateAndAttachHosts(desired, props, dgObj, dg.Handle);
		dg.Own.Clear();
		dg.Own.AddRange(desired);
		PageId = ListPageId();
		QtHostDiag.Trace(QtHostDiagChannel.QmlObject,
			$"Q14 row {rowIndex} dg '{dgObj}' materialized desired={desired.Count} children={dg.Children.Count} ids=[{string.Join(",", dg.Children.Select(h => h.Id))}]");
		_bridge.ItemsMaterialized += row.CellViews.Count;
		UpdateDgGeometry(dg);
	}

	/// <summary>Creates the hosts as QML children of the placeholder and wires them like page hosts. The
	/// batch targets the list's own page, which for a back-cached page is not the top one.</summary>
	internal void CreateAndAttachHosts(List<NativeElementHost> desired,
	                                  Dictionary<NativeElementHost, Dictionary<string, object?>> props,
	                                  string parentObj, long placeholderHandle)
	{
		_bridge.MarkSceneDirty();
		if (desired.Count == 0)
			return;
		var ops = new List<Dictionary<string, object?>>(desired.Count);
		foreach (var host in desired)
			ops.Add(QtHostPageRenderer.CreateChildOp(host, props.TryGetValue(host, out var p) ? p : new(), parentObj));
		_renderer.ApplyOps(ops, _renderer.IsParked(Host) && PageId.Length > 0
			? QmlPage.ById(PageId)
			: null);
		foreach (var host in desired)
		{
			QtHostPageRenderer.AttachHost(host, props.TryGetValue(host, out var p) ? p : new(), placeholderHandle);
			_renderer.RegisterRoute(host.Id, host);
			// QML resolved parentObj by name, and a recycling ListView can briefly have two delegates with that
			// name, so pin the subtree root to the placeholder resolved by handle.
			if (host.Parent is null && host.IsAttached && placeholderHandle != 0 &&
			    !QtHostRuntime.SetParentItem(host.NativeHandle, placeholderHandle))
				QtHostDiag.Warn(QtHostDiagChannel.QmlObject,
					$"row root {host} could not be pinned to its placeholder '{parentObj}': {QtHostRuntime.LastErrorText}");
		}
	}

	/// <summary>Drops a delegate's row content but keeps its registry entry; the QML delegate stays alive.</summary>
	internal void ClearDg(DgState dg)
	{
		dg.Own.Clear();
		_bridge.DestroyHosts(dg.Children, PageId);
		dg.Cells.Clear();
		if (dg.Row is not null && dg.Row.DgObj == dg.Obj)
			dg.Row.DgObj = null;
		dg.Row = null;
	}

	/// <summary>Maps the row (or slot) holding <paramref name="element"/> again, e.g. when a flattened layout starts
	/// painting and needs its own host (F4a). False when this list does not hold it.</summary>
	internal bool RemapRowContaining(Element element)
	{
		static bool Holds(Element root, Element e)
		{
			for (Element? x = e; x is not null; x = x.Parent)
				if (ReferenceEquals(x, root))
					return true;
			return false;
		}
		foreach (var dg in ByHandle.Values.ToList())
		{
			if (dg.Row is not { } row || !dg.Cells.Any(c => Holds(c.Root, element)))
				continue;
			var index = row.Index;
			ClearDg(dg);
			MaterializeRow(index, dg.Obj);
			return true;
		}
		foreach (var slot in Slots.Values)
			if (slot.Root is { } root && Holds(root, element))
			{
				MarkSlot(slot, remap: true);
				return true;
			}
		return false;
	}

	/// <summary>A delegate removed from the model handed its "__r" name to its replacement (ListView.qml
	/// onRemove): its content stays under the released name until the delegate's own detach.</summary>
	internal void ReleaseDg(string dgObj, string releasedObj)
	{
		if (releasedObj.Length == 0 || !Delegates.Remove(dgObj, out var dg))
			return;
		if (dg.Row is { } row && row.DgObj == dgObj)
			row.DgObj = null;
		dg.Obj = releasedObj;
		Delegates[releasedObj] = dg;
	}

	internal void UnmaterializeDg(DgState dg)
	{
		// The delegate is gone; its row subtree lives on (canvas-owned) and waits in the pool, or is destroyed.
		if (TryPoolRow(dg))
		{
			dg.Cells.Clear();
			if (dg.Row is not null && dg.Row.DgObj == dg.Obj)
				dg.Row.DgObj = null;
			dg.Row = null;
		}
		else
			ClearDg(dg);
		if (Delegates.TryGetValue(dg.Obj, out var holder) && ReferenceEquals(holder, dg))
			Delegates.Remove(dg.Obj);
		ByHandle.Remove(dg.Handle);
	}

	/// <summary>The model page a list's row hosts live on: the top page, except for a back-cached page's list.</summary>
	internal string ListPageId() =>
		_renderer.IsParked(Host) && PageId.Length > 0 ? PageId : _bridge.MirrorTop();


	internal void UpdateDgGeometry(DgState dg)
	{
		if (dg.Handle == 0 || dg.Children.Count == 0)
			return;
		var hosts = new HashSet<NativeElementHost>(dg.Children);
		foreach (var (root, cellX) in dg.Cells)
			_renderer.PushItemGeometry(root, cellX, hosts, crossAlongY: Horizontal);
	}

	internal void UpdateSlotGeometry(SlotState slot)
	{
		if (slot.Handle == 0 || slot.Children.Count == 0 || slot.Root is null)
			return;
		_renderer.PushItemGeometry(slot.Root, 0, new HashSet<NativeElementHost>(slot.Children));
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

	internal void OnRowTapped(int rowIndex, int cellIndex, double xQt = double.NaN, double yQt = double.NaN)
	{
		if (rowIndex < 0 || rowIndex >= Rows.Count)
			return;
		var row = Rows[rowIndex];
		if (row.Kind != KindItem || cellIndex < 0 || cellIndex >= row.CellItems.Count)
			return;
		// A tap recognizer in the template takes the tap, as on Android, where it consumes the touch before selection.
		if (!double.IsNaN(xQt) && cellIndex < row.CellViews.Count && row.CellViews[cellIndex] is { } cellRoot &&
		    TryFindRowTap(cellRoot, row.CellX[cellIndex], Horizontal, QtHostUnits.ToLogical(xQt), QtHostUnits.ToLogical(yQt),
			    out var tap, out var owner, out var position))
		{
			_bridge.RowTapsFired++;
			QtHostDiag.Trace(QtHostDiagChannel.Input, $"collection row {rowIndex} cell {cellIndex} tapped → {owner!.GetType().Name} TapGestureRecognizer");
			QtHostInputRouter.SendTapped(tap!, owner, position);
			return;
		}
		if (View is not SelectableItemsView sel || sel.SelectionMode == SelectionMode.None)
			return;
		var item = row.CellItems[cellIndex];

		_bridge.SelectionsApplied++;
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

	/// <summary>Row index → ordinal of its first item (-1 for header/footer or invalid rows).</summary>
	internal int RowToItemIndex(int rowIndex) =>
		rowIndex >= 0 && rowIndex < Rows.Count ? Rows[rowIndex].FirstItemOrdinal : -1;


	/// <summary>Item ordinal → the row carrying it (-1 when none).</summary>
	internal int ItemIndexToRow(int itemOrdinal)
	{
		if (itemOrdinal < 0)
			return -1;
		for (var r = 0; r < Rows.Count; r++)
		{
			var row = Rows[r];
			if (row.FirstItemOrdinal < 0 || row.FirstItemOrdinal > itemOrdinal)
				continue;
			if (row.FirstItemOrdinal + Math.Max(1, row.CellItems.Count) > itemOrdinal)
				return r;
		}
		return -1;
	}

	/// <summary>ScrollTo → native positionViewAtIndex as an immediate jump; the tick re-fires equal targets.</summary>
	internal void OnScrollToRequested(ScrollToRequestEventArgs e)
	{
		FlushInvalidate();   // an Add right before ScrollTo must be in the rows first
		if (!Host.IsAttached)
			return;
		var rowIndex = ResolveRow(e.Item ?? e.Index);
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
		ScrollTick++;
		Push("mauiScrollRow", rowIndex);
		Push("mauiScrollPos", pos);
		Push("mauiScrollTick", ScrollTick);
		QtHostDiag.Trace(QtHostDiagChannel.Navigation, $"collection ScrollTo('{e.Item ?? e.Index}', {e.ScrollToPosition}) → row {rowIndex} (tick {ScrollTick})");
	}

	internal object? ItemAtRow(int rowIndex) =>
		rowIndex >= 0 && rowIndex < Rows.Count && Rows[rowIndex].CellItems.Count > 0
			? Rows[rowIndex].CellItems[0]
			: null;


	/// <summary>Resolves a ScrollTo target (item ordinal or item) to a row index.</summary>
	internal int ResolveRow(object? index)
	{
		switch (index)
		{
			case null:
				return -1;
			case int ordinal:
				return ItemIndexToRow(ordinal);
		}
		for (var r = 0; r < Rows.Count; r++)
		{
			var row = Rows[r];
			if (row.Kind != KindItem)
				continue;
			foreach (var cell in row.CellItems)
				if (cell is not null && (ReferenceEquals(cell, index) || cell.Equals(index)))
					return r;
		}
		return -1;
	}

	/// <summary>Retires one list: destroys its child hosts and releases subscriptions; the renderer destroys
	/// the adapter itself.</summary>
	internal void CleanupList()
	{
		foreach (var dg in ByHandle.Values.ToList())
			ClearDg(dg);
		DrainRowPool();
		Delegates.Clear();
		ByHandle.Clear();
		foreach (var slot in Slots.Values)
		{
			UnwatchSlot(slot);
			_bridge.DestroyHosts(slot.Children, PageId);
			slot.Root = null;
		}
		Slots.Clear();
		UnsubscribeList();
		ReleaseRowViews(Rows);
		Rows.Clear();
		SelectedCells.Clear();
		LastRowsJson = string.Empty;
		LastCellLayout = string.Empty;
		LastSelJson = string.Empty;
		_bridge.Unregister(this);
		if (View.Handler is Handlers.SailfishListViewHandler { } handler && ReferenceEquals(handler.Adapter, this))
			handler.Adapter = null;
		QtHostDiag.Trace(QtHostDiagChannel.QmlObject, $"collection list '{Host}' retired (rows/delegates/slots released)");
	}

	internal void UnsubscribeList()
	{
		UnsubscribeGroups();
		if (SourceSub is not null && SourceHandler is not null)
			SourceSub.CollectionChanged -= SourceHandler;
		SourceSub = null;
		if (ScrollHandler is not null)
			View.ScrollToRequested -= ScrollHandler;
		ScrollHandler = null;
		if (SelectionHandler is not null && View is SelectableItemsView selectable)
			selectable.SelectionChanged -= SelectionHandler;
		SelectionHandler = null;
		RefreshBinding.Disarm();
	}
}
