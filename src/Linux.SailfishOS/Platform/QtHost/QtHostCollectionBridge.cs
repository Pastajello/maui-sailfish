using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// Bridges CollectionView/CarouselView to the native ListView adapter. The ListView owns virtualization and
/// flick physics; MAUI owns the content: the bridge flattens ItemsSource into rows, measures each row's
/// template and materializes its element tree as QML hosts inside the delegate, so they scroll and die with it.
/// Host ids follow MAUI element identity, so a row scrolled back only recreates the thin QML adapters.
/// </summary>
internal sealed class QtHostCollectionBridge
{
	internal const int KindItem = 0;
	internal const int KindGroupHeader = 1;
	internal const int KindGroupFooter = 2;

	/// <summary>Safety cap on materialized delegates per list; the cacheBuffer normally bounds this (ListView.qml:
	/// at most __prefetchRows each way beyond the viewport, ~80 one-line rows on a phone). A row past the cap stays
	/// empty, so it sits well above what the buffer can hold.</summary>
	internal const int MaxDelegates = 192;

	private readonly QtHostPageRenderer _renderer;
	private readonly Dictionary<ItemsView, QtHostListAdapter> _byElement = new();
	private readonly Dictionary<string, QtHostListAdapter> _byHostId = new(StringComparer.Ordinal);

	// Delegates unresolvable when their attach/rebind drained: on a hidden page Qt 5.6 parks them unparented
	// in its cache with no destruction event. The poll loop retries until they resolve or the list retires.
	private const int RetryLogInterval = 180;   // ~3 s at the 16 ms poll rate
	private readonly List<(QtHostListAdapter State, int Row, string Dg, int Attempts)> _attachRetries = new();

	/// <summary>Parked attaches of all lists (an adapter queues and drops its own).</summary>
	internal List<(QtHostListAdapter State, int Row, string Dg, int Attempts)> AttachRetries => _attachRetries;

	/// <summary>Whether <paramref name="adapter"/> is the live registration of its view.</summary>
	internal bool IsRegistered(QtHostListAdapter adapter) =>
		_byElement.TryGetValue(adapter.View, out var live) && ReferenceEquals(live, adapter);

	/// <summary>Drops a retired adapter from the registry and the retry queue.</summary>
	internal void Unregister(QtHostListAdapter adapter)
	{
		_attachRetries.RemoveAll(r => r.State == adapter);
		_byElement.Remove(adapter.View);
		_byHostId.Remove(adapter.Host.Id);
	}

	/// <summary>Lists of the page on screen; back-cache lists stay alive but are not addressed by taps,
	/// refresh or diagnostics.</summary>
	private IEnumerable<QtHostListAdapter> ActiveLists => _byElement.Values.Where(s => !_renderer.IsParked(s.Host));

	/* --- Diagnostics counters --- */
	public long RowsBuilt { get; internal set; }
	public long ItemsMaterialized { get; internal set; }

	/// <summary>Delegates currently holding a materialized item tree (the cumulative counters count
	/// different units, so this is the real live count).</summary>
	public int LiveRows => ActiveLists.Sum(s => s.ByHandle.Count);
	public long ItemsDestroyed { get; private set; }

	/// <summary>Rows rebuilt for the very row they already held (dead hosts, or a host set that only looked dead).
	/// A steady list keeps it near zero; a loop shows as thousands.</summary>
	public long SameRowRebuilds { get; internal set; }
	public long ListEvents { get; private set; }
	public long SelectionsApplied { get; internal set; }
	public long ScrollsReported { get; internal set; }

	/// <summary>RemainingItemsThresholdReached deliveries.</summary>
	public long ThresholdReachedFires { get; internal set; }

	public QtHostCollectionBridge(QtHostPageRenderer renderer) => _renderer = renderer;

	/// <summary>A slot is being measured/mapped: slot change events are its own.</summary>
	internal bool SlotMapping { get; set; }

	/// <summary>objectName of the first attached list adapter (diagnostics eval target).</summary>
	public string? FirstListObjectName
	{
		get
		{
			foreach (var state in ActiveLists)
				if (state.Host.IsAttached)
					return "maui_" + state.Host.Id;
			return null;
		}
	}

	/// <summary>Total flat rows across all live lists (diagnostics).</summary>
	public int TotalRows
	{
		get
		{
			var sum = 0;
			foreach (var state in ActiveLists)
				sum += state.Rows.Count;
			return sum;
		}
	}

	/// <summary>Diagnostics: the MAUI view of one cell of a built row of the first active list, or null.</summary>
	internal View? RowView(int rowIndex, int cell = 0)
	{
		foreach (var state in ActiveLists)
			if (rowIndex >= 0 && rowIndex < state.Rows.Count && cell < state.Rows[rowIndex].CellViews.Count)
				return state.Rows[rowIndex].CellViews[cell];
		return null;
	}

	/// <summary>The objectName of the list delegate showing <paramref name="element"/> (inside a row's content), or null.</summary>
	internal string? DelegateOf(Element element)
	{
		foreach (var state in ActiveLists)
			foreach (var dg in state.Delegates.Values)
				foreach (var (root, _) in dg.Cells)
					for (Element? x = element; x is not null; x = x.Parent)
						if (ReferenceEquals(x, root))
							return dg.Obj;
		return null;
	}

	/// <summary>Diagnostics: one row's delegate, host and bounds state.</summary>
	public string DescribeRow(int rowIndex)
	{
		foreach (var state in _byElement.Values)
		{
			if (rowIndex < 0 || rowIndex >= state.Rows.Count)
				continue;
			var row = state.Rows[rowIndex];
			var dg = state.Delegates.Values.FirstOrDefault(d => d.Row?.Index == rowIndex);
			if (dg is null)
				return $"list {state.Host.Id} row {rowIndex}: no delegate";
			var cells = string.Join(";", dg.Cells.Select(c => $"{c.Root.GetType().Name} bounds={c.Root.Bounds}"));
			var hosts = string.Join(";", dg.Children.Take(3).Select(h => $"{h.Id} att={h.IsAttached} maui={h.MauiLogicalBounds}"));
			var rowCells = string.Join(";", row.CellViews.Select(v => v is null ? "null" : $"{v.GetType().Name} bounds={v.Bounds}"));
			var liveDg = QtHostRuntime.FindVisual(state.Host.NativeHandle, dg.Obj);
			var globalDg = QtHostRuntime.FindObject(dg.Obj);
			var rootHost = dg.Children.FirstOrDefault(h => h.Parent is null);
			var rootGeo = rootHost is not null && QtHostRuntime.TryItemGeometry(rootHost.NativeHandle, out var g) ? $"{g.Width:F0}x{g.Height:F0}@{g.X:F0},{g.Y:F0}" : "-";
			var scoped = rootHost is null ? 0 : QtHostRuntime.FindVisual(liveDg, $"maui_{rootHost.Id}");
			return $"list {state.Host.Id} page={state.PageId} parked={_renderer.IsParked(state.Host)} row {rowIndex}: dg={dg.Obj} handle={dg.Handle} liveDg={liveDg} globalDg={globalDg} currentRow={ReferenceEquals(dg.Row, row)} " +
				$"root {rootHost?.Id} handle={rootHost?.NativeHandle} scene={rootGeo} rootInLiveDg={scoped} cells=[{cells}] modelCells=[{rowCells}] hosts=[{hosts}]";
		}
		return "no list";
	}

	/// <summary>Scene-unit point inside a materialized row's delegate, used as an injected-tap target.</summary>
	public bool TryGetRowPoint(int rowIndex, out double xQt, out double yQt)
	{
		xQt = yQt = -1;
		foreach (var state in ActiveLists)
		{
			foreach (var dg in state.Delegates.Values)
			{
				if (dg.Row?.Index != rowIndex)
					continue;
				if (dg.Handle == 0)
					dg.Handle = QtHostRuntime.FindObject(dg.Obj);
				if (dg.Handle == 0)
					dg.Handle = QtHostRuntime.FindVisual(state.Host.NativeHandle, dg.Obj);
				if (dg.Handle != 0 && QtHostRuntime.TryItemGeometry(dg.Handle, out var geo))
				{
					xQt = geo.X + geo.Width / 2;
					yQt = geo.Y + Math.Min(geo.Height / 2, 20);
					return true;
				}
			}
		}
		return false;
	}

	/* --- State model --- */

	internal sealed class Row
	{
		public int Index;                 // flat row index (model position)
		public int Kind;                  // KindItem / KindGroupHeader / KindGroupFooter
		public int GroupIndex = -1;       // group ordinal (grouped sources)
		public int ItemIndex;             // item index within the group / flat source
		public int FirstItemOrdinal = -1; // flat item ordinal (Scrolled event indices)
		public double HeightDp;
		public readonly List<View?> CellViews = new();
		public readonly List<object?> CellItems = new();
		public readonly List<double> CellX = new();   // dp offsets within the row
		public string? DgObj;                          // delegate currently hosting the row
		public long Key;                               // stable across rebuilds (QML diffs by it)
		public double CellWidthDp;                     // the width its cells were measured at
	}

	internal sealed class DgState
	{
		public string Obj = string.Empty;   // delegate objectName ("maui_<id>__r<row>")
		public long Handle;                 // shim handle (lazy FindObject)
		public Row? Row;
		public readonly List<NativeElementHost> Children = new();
		public readonly List<(View Root, double CellX)> Cells = new();   // laid out delegate-relative
	}

	internal sealed class SlotState
	{
		public string Obj = string.Empty;   // "maui_<id>__header|__footer|__empty"
		public long Handle;
		public readonly List<NativeElementHost> Children = new();
		public View? Root;                  // laid out slot-relative
		public string HeightProp = string.Empty;
		public double ExtentDp;             // last pushed slot extent
		public double CrossDp = -1;         // horizontal slot: list height it was measured for
		public double WidthDp = -1;         // vertical slot: list width it was measured for (rotation, resize)
		public bool Remeasure;              // content invalidated its measure since the last push
		public bool Remap;                  // content gained or lost views: its hosts are re-created
		public EventHandler? MeasureHandler;
		public EventHandler<ElementEventArgs>? TreeHandler;
	}


	/* --- Renderer integration (Walk / Reconcile / Poll) --- */

	/// <summary>
	/// Walk hook: registers the list host and its props; item trees are materialized per delegate, not walked.
	/// </summary>
	public void RegisterList(ItemsView view, List<NativeElementHost> desired,
	                         Dictionary<NativeElementHost, Dictionary<string, object?>> props)
	{
		var host = _renderer.Cache.GetOrAdd(view, AdapterUriFor(view));
		var state = EnsureState(view, host);
		// Rows are never in the create op: mauiEvent connects after createObject, so delegate events fired
		// during property init would be lost. Rows are pushed after attach instead.
		props[host] = state.LayoutProps();
		// A wrapping RefreshView makes this list the pull-to-refresh surface, unless a page pulley owns the overscroll.
		state.ArmRefresh(_renderer.RefreshAncestorOf(view));
		if (state.Refresh is { } refresh)
			foreach (var kv in QtHostPageRenderer.RefreshSurfaceProps(refresh))
				props[host][kv.Key] = kv.Value;
		desired.Add(host);
	}

	/// <summary>A looping horizontal CarouselView uses the PathView adapter (a ListView cannot wrap); both
	/// share the bridge contract. Fixed at creation.</summary>
	internal static string AdapterUriFor(IView? view) =>
		view is CarouselView { Loop: true } carousel && (carousel.ItemsLayout?.Orientation ?? ItemsLayoutOrientation.Horizontal) == ItemsLayoutOrientation.Horizontal
			? "carousel-view"
			: "list-view";


	/// <summary>True when a hosted list owns this RefreshView, so the page-level arm stays out.</summary>
	public bool ConsumesRefresh(RefreshView refresh)
	{
		foreach (var state in _byElement.Values)
			if (ReferenceEquals(state.Refresh, refresh))
				return true;
		return false;
	}

	/// <summary>List-side release gesture: sets IsRefreshing; false when no hosted list owns the id.</summary>
	public bool HandleRefreshRequested(string id)
	{
		if (id != QtHostPageRenderer.RefreshId)
			return false;
		foreach (var state in ActiveLists)
		{
			if (state.Refresh is not { } refresh)
				continue;
			QtHostDiag.Trace(QtHostDiagChannel.Input, $"pull-to-refresh gesture on '{state.Host}' → RefreshView.IsRefreshing=true");
			((IRefreshView)refresh).IsRefreshing = true;
			return true;
		}
		return false;
	}

	private QtHostListAdapter EnsureState(ItemsView view, NativeElementHost host)
	{
		if (_byElement.TryGetValue(view, out var state))
		{
			state.Host = host;
			_byHostId[host.Id] = state;
		}
		else
		{
			state = new QtHostListAdapter(this, _renderer) { View = view, Host = host };
			_byElement[view] = state;
			_byHostId[host.Id] = state;
			state.Attach();
		}
		// The list's handler owns its adapter, as a CollectionView handler owns its RecyclerView adapter; a
		// handler connected again (after DisconnectHandler) takes over the live one.
		if (view.Handler is Handlers.SailfishListViewHandler handler)
			handler.Adapter = state;
		return state;
	}






	/// <summary>A back-cached page returned: re-scan its lists' delegates, which Qt 5.6 reuses without
	/// firing attach/rebind.</summary>
	public void OnPageRestored(IReadOnlyCollection<NativeElementHost> hosts)
	{
		_sceneDirty = true;
		foreach (var state in _byElement.Values)
			if (hosts.Contains(state.Host))
				state.ResyncPending = Math.Max(state.ResyncPending, ResyncTicksAfterRebuild);
				SchedulePending(ResyncIntervalMs);
	}

	/// <summary>Reconcile hook: retires lists that left the desired tree.</summary>
	public void SyncDesired(List<NativeElementHost> desired)
	{
		if (_byElement.Count == 0)
			return;
		var set = new HashSet<NativeElementHost>(desired);
		foreach (var kv in _byElement.ToList())
			if (!set.Contains(kv.Value.Host))
				kv.Value.CleanupList();
	}

	/// <summary>Rows/slots changed since the last scene refresh.</summary>
	private bool _sceneDirty = true;

	/// <summary>Row/slot content changed: the next layout refresh re-lays out the live delegates.</summary>
	internal void MarkSceneDirty() => _sceneDirty = true;

	/// <summary>Reconcile hook: freshly created list hosts owe their initial row push.</summary>
	public void OnHostsCreated(IReadOnlyList<NativeElementHost> created)
	{
		_sceneDirty = true;
		foreach (var host in created)
			if (_byHostId.TryGetValue(host.Id, out var state))
			{
				state.LastRowsJson = string.Empty;   // fresh QML object: force the pushes
				state.LastSelJson = string.Empty;
				foreach (var slot in state.Slots.Values)
					UnwatchSlot(slot);
				state.Slots.Clear();                 // slot placeholders were re-created
				state.SlotsDirty = true;
				state.RowsDirty = true;
				SchedulePending();
				state.LastWidthDp = -1;
				state.ReadLayout();
			}
	}

	/// <summary>Reconcile hook: adds item/slot hosts to the id routing table so their native events resolve.</summary>
	public void ContributeRouting(Dictionary<string, NativeElementHost> byId)
	{
		foreach (var state in _byElement.Values)
		{
			foreach (var dg in state.Delegates.Values)
				foreach (var host in dg.Children)
					byId[host.Id] = host;
			foreach (var slot in state.Slots.Values)
				foreach (var host in slot.Children)
					byId[host.Id] = host;
		}
	}

	/// <summary>How often the delegate resync and parked attaches retry while they have work (they wait for Qt's
	/// delegate cache to settle; nothing announces that).</summary>
	internal const int ResyncIntervalMs = 250;

	private long _pendingAtMs;   // the earliest scheduled ProcessPending (0 = none)

	/// <summary>
	/// Runs <see cref="ProcessPending"/> on the loop in <paramref name="delayMs"/> (0 = the next turn): rows to build,
	/// slots to materialize, a resync or parked attaches to retry. Any thread; earlier requests win. Replaces the
	/// renderer's safety-net poll as the driver of list work, as a RecyclerView schedules its own layout.
	/// </summary>
	internal void SchedulePending(int delayMs = 0)
	{
		var at = Environment.TickCount64 + delayMs;
		lock (_attachRetries)
		{
			if (_pendingAtMs != 0 && _pendingAtMs <= at)
				return;
			_pendingAtMs = at;
		}
		void Run()
		{
			lock (_attachRetries)
			{
				if (_pendingAtMs != at)
					return;   // superseded by an earlier run
				_pendingAtMs = 0;
			}
			if (_processing)
			{
				_processAgain = true;   // asked from inside a pass: one more pass when it ends
				return;
			}
			PendingRuns++;
			ProcessPending();
		}
		if (delayMs <= 0)
			QtHostRuntime.Post(Run);
		else if (Microsoft.Maui.Dispatching.Dispatcher.GetForCurrentThread() is { } dispatcher)
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(delayMs), Run);
		else if (!QtHostRuntime.IsQtThread)
			QtHostRuntime.Post(() =>
			{
				lock (_attachRetries)
					if (_pendingAtMs == at)
						_pendingAtMs = 0;
				SchedulePending(delayMs);   // on the loop, where the dispatcher lives
			});
		else
			lock (_attachRetries)
				_pendingAtMs = 0;   // no dispatcher (tests): their polls drive the work
	}

	private bool _processing;     // ProcessPending is running
	private bool _processAgain;   // work was scheduled meanwhile

	/// <summary>List work is scheduled on the lists' own clock (the heartbeat leaves it to that clock).</summary>
	internal bool PendingScheduled
	{
		get
		{
			lock (_attachRetries)
				return _pendingAtMs != 0;
		}
	}

	/// <summary>List work runs scheduled by the lists themselves (diagnostics).</summary>
	public long PendingRuns { get; private set; }

	/// <summary>Deferred row building (needs the laid-out width), slot materialization, the delegate resync and
	/// parked attaches; scheduled by <see cref="SchedulePending"/>, and run by every renderer poll.</summary>
	public void ProcessPending()
	{
		if (_processing)
		{
			_processAgain = true;
			return;
		}
		_processing = true;
		var passes = 0;
		try
		{
			do
			{
				_processAgain = false;
				ProcessPendingOnce();
			}
			while (_processAgain && ++passes < 4);
		}
		finally
		{
			_processing = false;
		}
		if (_processAgain)
			SchedulePending();   // work that keeps rescheduling itself yields the loop turn
	}

	private void ProcessPendingOnce()
	{
		if (_byElement.Count == 0)
			return;
		// Wait while host creation is deferred: objects created before activation die with Silica's rebuild.
		if (_renderer.CreationDeferred)
			return;
		var clock = System.Diagnostics.Stopwatch.StartNew();   // the turn's budget for deferred off-screen rows
		foreach (var state in _byElement.Values.ToList())
		{
			if (!state.Host.IsAttached)
				continue;
			var widthDp = state.Host.MauiLogicalBounds.Width;
			if (widthDp <= 0)
				continue;   // the layout pass hasn't placed the list yet
			// Horizontal lists and carousels also size items from the list height.
			var crossChanged = (state.Horizontal || state.Carousel) &&
			                   Math.Abs(state.Host.MauiLogicalBounds.Height - state.LastCrossDp) > 0.5;
			if (state.RowsDirty || crossChanged || Math.Abs(widthDp - state.LastWidthDp) > 0.5)
			{
				state.RebuildRows(widthDp);
				if (crossChanged && state.Horizontal && state.Slots.Count > 0)
					state.SlotsDirty = true;   // horizontal header/footer widths follow the height
			}
			if (state.SlotsDirty)
				state.MaterializeSlots(widthDp);
			if (state.ResyncPending > 0)
			{
				state.ResyncPending--;
				state.ResyncDelegates();
			}
		}
		if (_attachRetries.Count > 0)
			RetryPendingAttaches();
		// Off-screen rows (the cache buffer) take what is left of the turn's budget; the rest waits a frame so Qt
		// can paint in between.
		var deferredLeft = false;
		foreach (var state in _byElement.Values.ToList())
			if (state.Host.IsAttached && state.DrainDeferredRows(clock, DeferredRowsBudgetMs))
				deferredLeft = true;
		if (deferredLeft)
			SchedulePending(DeferredRowsTurnMs);
		// Work that waits for Qt keeps its own clock while it lasts.
		if (_attachRetries.Count > 0 || _byElement.Values.Any(s => s.ResyncPending > 0))
			SchedulePending(ResyncIntervalMs);
	}

	/// <summary>Maps the row holding <paramref name="element"/> again in whichever list holds it (F4a).</summary>
	internal void RemapRowContaining(Element element)
	{
		foreach (var state in _byElement.Values.ToList())
			if (state.RemapRowContaining(element))
				return;
	}

	internal const double DeferredRowsBudgetMs = 6;   // per loop turn, including the turn's other list work
	internal const int DeferredRowsTurnMs = 8;        // next drain after a frame's worth of loop time



	/// <summary>Retries parked attaches every poll, unbounded while the list lives: Qt 5.6 reuses cached
	/// delegates without a rebind, so this is the only path that materializes them.</summary>
	private void RetryPendingAttaches()
	{
		var count = _attachRetries.Count;
		for (var i = count - 1; i >= 0; i--)
		{
			if (i >= _attachRetries.Count)
				continue;
			var entry = _attachRetries[i];
			if (!_byElement.ContainsValue(entry.State))
			{
				_attachRetries.RemoveAt(i);   // the list was retired meanwhile
				continue;
			}
			_attachRetries[i] = (entry.State, entry.Row, entry.Dg, entry.Attempts + 1);
			entry.State.MaterializeRow(entry.Row, entry.Dg);
			var stillParked = false;
			for (var j = 0; j < _attachRetries.Count; j++)
				if (_attachRetries[j].State == entry.State && _attachRetries[j].Dg == entry.Dg)
				{
					stillParked = true;
					var attempts = _attachRetries[j].Attempts;
					if (attempts % RetryLogInterval == 0)
						QtHostDiag.Warn(QtHostDiagChannel.QmlObject, $"delegate '{entry.Dg}' still parked (row {entry.Row}, {attempts} poll retries, lastError='{QtHostRuntime.LastErrorText}')");
					break;
				}
			if (!stillParked)
				QtHostDiag.Trace(QtHostDiagChannel.QmlObject, $"delegate '{entry.Dg}' resolved after {entry.Attempts + 1} poll retries (row {entry.Row})");
		}
	}

	internal const int ResyncTicksAfterRebuild = 15;   // model refill lands within ~1-2 frames; cover slow incubation
	internal const int ResyncTicksAfterScroll = 3;     // settle window after a native scroll report

	/// <summary>
	/// Self-heal: drops a dead row/slot host so the next resync re-materializes it; MaterializeRow only
	/// rebuilds delegates whose children are gone.
	/// </summary>
	internal void OnHostHealed(NativeElementHost host)
	{
		foreach (var state in _byElement.Values)
		{
			foreach (var dg in state.Delegates.Values)
			{
				var i = dg.Children.IndexOf(host);
				if (i < 0)
					continue;
				dg.Children.RemoveAt(i);
				if (dg.Row is not null)
					dg.Row.DgObj = null;   // resync re-materializes this row
				state.ResyncPending = Math.Max(state.ResyncPending, ResyncTicksAfterRebuild);
				SchedulePending(ResyncIntervalMs);
				return;
			}
			foreach (var slot in state.Slots.Values)
			{
				var i = slot.Children.IndexOf(host);
				if (i < 0)
					continue;
				slot.Children.RemoveAt(i);
				state.SlotsDirty = true;
				SchedulePending();
				return;
			}
		}
	}

	/// <summary>
	/// Cell hosts die with recycled delegates as normal virtualization; the renderer's dead-host burst
	/// counter must skip them, or one scroll jump triggers a full page reset.
	/// </summary>
	internal bool IsCollectionCellHost(NativeElementHost host)
	{
		foreach (var state in _byElement.Values)
		{
			foreach (var dg in state.Delegates.Values)
				if (dg.Children.Contains(host))
					return true;
			foreach (var slot in state.Slots.Values)
				if (slot.Children.Contains(host))
					return true;
		}
		return false;
	}


	/* --- Row model: flatten → measure → push --- */





	private long _rowKeys;

	/// <summary>A row key unique across rebuilds and lists (QML diffs rows by it).</summary>
	internal long NextRowKey() => ++_rowKeys;



	internal static View? CreateFromTemplate(DataTemplate template, object? context)
	{
		try
		{
			if (template.CreateContent() is View view)
			{
				view.BindingContext = context;
				return view;
			}
		}
		catch (Exception ex)
		{
			QtHostDiag.Error(QtHostDiagChannel.QmlObject, $"collection template instantiation failed: {ex.Message}");
		}
		return null;
	}

	internal static View? CreateSlotView(object? content, DataTemplate? template)
	{
		if (content is View direct)
			return direct;
		if (content is null)
			return null;
		if (template is not null)
			return CreateFromTemplate(template, content);
		return new Label { Text = content.ToString() ?? string.Empty };
	}

	/// <summary>
	/// Slot views are never parented to the CollectionView, so BindingContext does not flow to them; bind
	/// it to the owner's unless the slot sets its own.
	/// </summary>
	internal static void InheritOwnerContext(BindableObject owner, View? slot)
	{
		if (slot is null || slot.IsSet(BindableObject.BindingContextProperty))
			return;
		slot.SetBinding(BindableObject.BindingContextProperty,
			new Binding(nameof(BindableObject.BindingContext), source: owner));
	}

	internal double MeasureItemView(View view, double widthDp)
	{
		try
		{
			QtHostLayout.AttachHandlers(view, _renderer.MauiContext);
			return QtHostLayout.MeasureAndArrangeItem(view, widthDp);
		}
		catch (Exception ex)
		{
			QtHostDiag.Error(QtHostDiagChannel.Geometry, $"collection item measure failed: {ex.Message}");
			return 0;
		}
	}

	/// <summary>An item's extent along the scroll axis: height in a vertical list, width in a horizontal one;








	/* --- Managed → native pushes --- */






	/* --- Slot materialization (Header/Footer/EmptyView) --- */





	internal static void UnwatchSlot(SlotState slot)
	{
		if (slot.Root is not null && slot.MeasureHandler is not null && slot.TreeHandler is not null)
		{
			slot.Root.MeasureInvalidated -= slot.MeasureHandler;
			slot.Root.DescendantAdded -= slot.TreeHandler;
			slot.Root.DescendantRemoved -= slot.TreeHandler;
		}
		slot.MeasureHandler = null;
		slot.TreeHandler = null;
	}


	/* --- Delegate materialization --- */

	/// <summary>Adapter event entry, called on the MAUI main thread.</summary>
	public void HandleEvent(string name, string payload)
	{
		ListEvents++;
		_sceneDirty = true;
		try
		{
			using var doc = JsonDocument.Parse(payload);
			var root = doc.RootElement;
			var id = root.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty;
			if (!_byHostId.TryGetValue(id, out var state))
			{
				QtHostDiag.Warn(QtHostDiagChannel.QmlSignal, $"collection event '{name}' for unknown list id='{id}'");
				return;
			}
			switch (name)
			{
				case "list-item-attached":
				case "list-item-rebind":
					state.RequestMaterialize((int)BridgeJson.Num(root, "row", -1), root.GetProperty("dg").GetString() ?? string.Empty);
					break;
				case "list-item-detached":
				{
					var dgName = root.GetProperty("dg").GetString() ?? string.Empty;
					if (state.Delegates.TryGetValue(dgName, out var dg))
						state.UnmaterializeDg(dg);
					break;
				}
				case "list-item-released":
					state.ReleaseDg(root.GetProperty("dg").GetString() ?? string.Empty,
						root.GetProperty("to").GetString() ?? string.Empty);
					break;
				case "list-item-tapped":
					state.OnRowTapped((int)BridgeJson.Num(root, "row", -1), BridgeJson.Int(root, "cell", 0));
					break;
				case "carousel-position":
					state.OnCarouselPosition((int)BridgeJson.Num(root, "index", -1));
					break;
				case "list-scroll":
					state.OnListScroll(BridgeJson.Num(root, "y", -1), (int)BridgeJson.Num(root, "first", -1), (int)BridgeJson.Num(root, "last", -1));
					break;
			}
		}
		catch (Exception ex)
		{
			QtHostDiag.Error(QtHostDiagChannel.QmlSignal, $"collection event '{name}' handling failed: {ex.Message}");
		}
	}

	/// <summary>The ItemsView properties the list follows; SailfishListViewHandler maps them (A7: one channel, the
	/// handler's mapper, instead of a PropertyChanged subscription of the bridge).</summary>
	internal static readonly string[] ViewProperties =
	{
		nameof(ItemsView.ItemsSource), nameof(SelectableItemsView.SelectionMode), nameof(SelectableItemsView.SelectedItem),
		nameof(SelectableItemsView.SelectedItems), nameof(CarouselView.Position), nameof(CarouselView.CurrentItem),
		nameof(CarouselView.IsSwipeEnabled), nameof(CarouselView.IsBounceEnabled), nameof(CarouselView.PeekAreaInsets),
		nameof(StructuredItemsView.ItemsLayout), nameof(StructuredItemsView.Header), nameof(StructuredItemsView.Footer),
		nameof(ItemsView.EmptyView), nameof(ItemsView.ItemTemplate), nameof(GroupableItemsView.IsGrouped),
		nameof(GroupableItemsView.GroupHeaderTemplate), nameof(GroupableItemsView.GroupFooterTemplate),
		nameof(ItemsView.VerticalScrollBarVisibility), nameof(ItemsView.HorizontalScrollBarVisibility),
	};



	/// <summary>The row's content still exists natively (it dies with a delegate Qt released under it).</summary>
	internal static bool ChildrenAlive(DgState dg)
	{
		var root = dg.Children[0];
		return root.IsAttached && QtHostRuntime.TryItemGeometry(root.NativeHandle, out _);
	}


	/// <summary>The hosts of an item subtree in pre-order (parents first).</summary>
	internal void CollectHosts(VisualElement element, List<NativeElementHost> children)
	{
		// A flattened row layout (F4a) has a cached host that is never created: counting it would make the row look
		// dead to ChildrenAlive (a flattened row root is Children[0]) and every resync would rebuild the row.
		if (_renderer.Cache.TryGet(element, out var host) && host is not null && !_renderer.IsFlattened(element))
			children.Add(host);
		foreach (var child in ((IVisualTreeElement)element).GetVisualChildren())
		{
			if (child is VisualElement visual)
				CollectHosts(visual, children);
		}
	}



	internal void DestroyHosts(List<NativeElementHost> hosts, string pageId)
	{
		if (hosts.Count == 0)
			return;
		// Target the page the hosts were created on, not the mirror top (during a push that is the incoming page).
		var target = pageId.Length > 0 ? QmlPage.ById(pageId) : null;
		QtHostDiag.Trace(QtHostDiagChannel.QmlObject,
			$"Q14 destroy {hosts.Count} row/slot hosts on '{pageId}' ids=[{string.Join(",", hosts.Select(h => h.Id))}]");
		// The QML objects are usually already dead with their delegate; destroy ops clean the registry.
		// Descendants first (the list is pre-order).
		_renderer.DestroyHosts(Enumerable.Reverse(hosts).ToList(), target, unroute: true);
		ItemsDestroyed += hosts.Count;
		hosts.Clear();
	}

	/// <summary>The model-page instance new row/slot hosts are created on (the reconcile eval's target).</summary>
	internal string MirrorTop() =>
		_renderer.NativePageIds.Count > 0 ? _renderer.NativePageIds[_renderer.NativePageIds.Count - 1] : string.Empty;

	/* --- Geometry: row/slot subtrees are placeholder-relative, so scrolling pushes nothing --- */



	/// <summary>Re-lays out every live delegate/slot subtree, parked pages included: their late-attached rows
	/// only get a size here. Geometry is delegate-relative, so this is valid off screen.</summary>
	public void RefreshSceneBounds(bool force)
	{
		// Only after a layout pass or when rows/slots changed since the last one.
		if (_byElement.Count == 0 || (!force && !_sceneDirty))
			return;
		_sceneDirty = false;
		foreach (var state in _byElement.Values)
		{
			foreach (var dg in state.Delegates.Values)
				state.UpdateDgGeometry(dg);
			foreach (var slot in state.Slots.Values)
				state.UpdateSlotGeometry(slot);
		}
	}

	/* --- Selection: native tap → MAUI → highlight push back --- */


	/* --- Scrolling: native flick → MAUI scroll state; ScrollTo → native jump --- */







	/* --- Teardown --- */



	/// <summary>Retires every list; called before the renderer destroys the adapter objects.</summary>
	public void TearDownAll()
	{
		if (_byElement.Count == 0)
			return;
		foreach (var state in _byElement.Values.ToList())
			state.CleanupList();
	}

	/// <summary>Retires only the lists whose host is in the set, keeping back-cached pages' lists warm.</summary>
	public void TearDownListsForHosts(HashSet<string> hostIds)
	{
		if (_byElement.Count == 0 || hostIds.Count == 0)
			return;
		foreach (var state in _byElement.Values.ToList())
			if (hostIds.Contains(state.Host.Id))
				state.CleanupList();
	}
}
