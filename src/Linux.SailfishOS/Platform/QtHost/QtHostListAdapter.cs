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
	private readonly ListCounters _counters;

	internal QtHostListAdapter(QtHostCollectionBridge bridge, QtHostPageRenderer renderer, ListCounters counters)
	{
		_bridge = bridge;
		_renderer = renderer;
		_counters = counters;
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
			case nameof(CarouselView.Loop):
				// The other adapter (PathView ↔ ListView): the walk of the list's container swaps it (RegisterList).
				if (View.Parent is IView container)
					_renderer.RequestSubtree(container);
				else
					_renderer.RequestPoll();
				break;
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
	public readonly Dictionary<object, List<Row>> Reusable = new(ReferenceEqualityComparer.Instance);   // RebuildRows scratch, by first item, in order
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
	public int ScrollRequests;   // managed ScrollTo commands sent
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

	/// <summary>The list's QML object was created again (W4: the bridge used to reset these fields itself): every push
	/// goes out again, the slot placeholders are new, and the rows are rebuilt for the new object.</summary>
	internal void OnNativeObjectRecreated()
	{
		LastRowsJson = string.Empty;
		LastSelJson = string.Empty;
		foreach (var slot in Slots.Values)
			QtHostCollectionBridge.UnwatchSlot(slot);
		Slots.Clear();
		SlotsDirty = true;
		RowsDirty = true;
		LastWidthDp = -1;
		ReadLayout();
	}

	/// <summary>Resyncs the delegates for at least <paramref name="ticks"/> pending passes (Qt 5.6 reuses delegates
	/// without telling).</summary>
	internal void RequestResync(int ticks) => ResyncPending = Math.Max(ResyncPending, ticks);

	/// <summary>This list's share of the bridge's pending pass: rows built for the laid-out size, slots, re-measured
	/// rows, the delegate resync. The bridge keeps the loop and the rescheduling (W4).</summary>
	internal void RunPendingWork()
	{
		if (!Host.IsAttached)
			return;
		var widthDp = Host.MauiLogicalBounds.Width;
		if (widthDp <= 0)
			return;   // the layout pass hasn't placed the list yet
		// Horizontal lists and carousels also size items from the list height.
		var crossChanged = (Horizontal || Carousel) && Math.Abs(Host.MauiLogicalBounds.Height - LastCrossDp) > 0.5;
		if (RowsDirty || crossChanged || Math.Abs(widthDp - LastWidthDp) > 0.5)
		{
			RebuildRows(widthDp);
			if (crossChanged && Horizontal && Slots.Count > 0)
				SlotsDirty = true;   // horizontal header/footer widths follow the height
		}
		if (SlotsDirty)
			MaterializeSlots(widthDp);
		if (RowsRemeasure)
			RemeasureRows();
		if (ResyncPending > 0)
		{
			ResyncPending--;
			ResyncDelegates();
		}
	}

	/// <summary>Self-heal: drops a dead row/slot host of this list so the next resync re-materializes it (a row) or
	/// the slot is built again. False when the host is not this list's.</summary>
	internal bool ForgetDeadHost(NativeElementHost host)
	{
		foreach (var dg in ByHandle.Values)
		{
			var i = dg.Children.IndexOf(host);
			if (i < 0)
				continue;
			dg.Children.RemoveAt(i);
			if (dg.Row is not null)
				dg.Row.DgObj = null;   // resync re-materializes this row
			RequestResync(ResyncTicksAfterRebuild);
			_bridge.SchedulePending(ResyncIntervalMs);
			return true;
		}
		foreach (var slot in Slots.Values)
		{
			var i = slot.Children.IndexOf(host);
			if (i < 0)
				continue;
			slot.Children.RemoveAt(i);
			SlotsDirty = true;
			_bridge.SchedulePending();
			return true;
		}
		return false;
	}

	/// <summary>Marks the rows stale; rebuilds at once when the list is live, else on its scheduled pass.</summary>
	internal void Invalidate()
	{
		RowsDirty = true;
		_bridge.SchedulePending();
		// A change raised while the rows are being built (a template that sets a list property) rebuilds after this
		// rebuild, on the pending pass, instead of nesting a second one inside it.
		if (_inRebuild)
			return;
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

	private bool _inRebuild => _rebuildDepth > 0;   // RebuildRows is running: its measures and templating are not cell changes
	private int _rebuildDepth;                       // a depth: a nested rebuild's exit must not end the outer one (W1.5)

	private string? _lastSignature;

	/// <summary>Rows whose cells asked for a measure (<see cref="RemeasureRows"/> runs in the next pending pass).</summary>
	internal bool RowsRemeasure { get; private set; }


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

	// A swipe moves the native carousel page itself, but the adapter sets its mauiPosition with it and reports it
	// (OnCarouselPosition records it as applied), so the applied value is the native one for every property.
	private bool Changed(string name, string json) =>
		!Host.AppliedProperties.TryGetValue(name, out var applied) || applied != json;

	/// <summary>The keys of one row in mauiRowsJson, the roles of ListView.qml's rowModel.</summary>
	internal static class RowJson
	{
		public const string Key = "k";        // stable row key (survives inserts and removes)
		public const string Row = "r";        // the row's index
		public const string Height = "h";     // Qt units
		public const string Tap = "t";        // 0 none, 1 selectable, 2 a tap gesture in the row
		public const string Cells = "n";      // cells in a grid row
		public const string Selected = "s";   // the role mauiSelectedRows flips
	}

	/// <summary>objectName prefix of this list's row delegates: the delegate of row r is <c>prefix + r</c>
	/// (ListView.qml/CarouselView.qml).</summary>
	internal string DelegatePrefix => "maui_" + Host.Id + "__r";

	/// <summary>objectName of a header/footer/empty slot item (ListView.qml).</summary>
	internal string SlotObjectName(string slot) => "maui_" + Host.Id + "__" + slot;

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
			sb.Append("{\"" + RowJson.Key + "\":").Append(row.Key.ToString(CultureInfo.InvariantCulture))
			  .Append(",\"" + RowJson.Row + "\":").Append(i.ToString(CultureInfo.InvariantCulture))
			  .Append(",\"" + RowJson.Height + "\":").Append(QtHostUnits.ToQtUnits(row.HeightDp).ToString("R", CultureInfo.InvariantCulture))
			  .Append(",\"" + RowJson.Tap + "\":").Append(row.Kind != KindItem ? '0' : selectable ? '1' : RowHasTap(row) ? '2' : '0')
			  .Append(",\"" + RowJson.Cells + "\":").Append((row.Kind == KindItem ? row.CellItems.Count : 0).ToString(CultureInfo.InvariantCulture))
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

	/// <summary>Rows whose delegates sit in the ListView's cache buffer, off screen: materialized a few per loop
	/// turn nearest-first (<see cref="DrainDeferredRows"/>), so a page fill or a load-more never blocks one frame
	/// on every new delegate. MAUI_SAILFISH_LIST_DEFER=0 materializes everything at once (A/B runs).</summary>
	internal readonly List<int> DeferredRows = new();
	internal static readonly bool DeferOffscreenRows = Environment.GetEnvironmentVariable("MAUI_SAILFISH_LIST_DEFER") != "0";
	private const double DeferMarginViewports = 0.5;   // rows this close to the viewport are built at once
	private double[]? _rowTopsDp;                       // row tops along the scroll axis; reset by RebuildRows

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
