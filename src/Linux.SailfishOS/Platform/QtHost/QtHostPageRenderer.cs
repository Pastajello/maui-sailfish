using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// Renders a MAUI page into QML (MauiModelPage.qml) with persistent native hosts: each pass reconciles the
/// MAUI logical tree against the cached <see cref="NativeElementHost"/>s (create, in-place property diff,
/// reorder, destroy) so native focus/selection/scroll state survives.
/// MAUI is the only layout engine: <see cref="QtHostLayout"/> lays out in dp and one batched geometry call per
/// pass converts through <see cref="QtHostUnits"/>.
/// Adapter events come back through <see cref="HandleNativeEvent"/> under change suppression so echoes never
/// loop, and pointer input is routed by <see cref="QtHostInputRouter"/>.
/// </summary>
public sealed partial class QtHostPageRenderer
{
	/// <summary>No hard cap on hosts per page; past this many a one-time warning names the page.</summary>
	private const int LargePageHostWarning = 2000;
	private readonly HashSet<Page> _largePageWarned = new();
	private readonly HashSet<Type> _unsupportedWarned = new();
	private bool _mappingRows;   // MapElement runs for a collection row subtree

	private static readonly Dictionary<string, object?> EmptyProps = new();

	/// <summary>MAUI_SAILFISH_QT_HOST_GEOMETRY_TRACE=1 logs every SetGeometry entry and window report.</summary>
	private static readonly bool GeometryTrace = SailfishEnv.Flag("MAUI_SAILFISH_QT_HOST_GEOMETRY_TRACE");

	/// <summary>MAUI_SAILFISH_QT_HOST_INPUT_TRACE=1 logs hit-test results and focus transitions.</summary>
	internal static readonly bool InputTrace = SailfishEnv.Flag("MAUI_SAILFISH_QT_HOST_INPUT_TRACE");

	/// <summary>Eval target for op batches that create hosts. Right after a pageStack.pop the dying page still
	/// resolves as mauiModelPage/currentPage, so the top page is addressed by id from the shell registry
	/// (falls back to QmlPage.Model).</summary>
	private string TopModelPageJs => NativeTopPageId is { } top ? QmlPage.ByIdOr(top, QmlPage.Model) : QmlPage.Model;

	/// <summary>Registry id of the top page on the native pageStack mirror; null when empty.</summary>
	private string? NativeTopPageId => _nativePageIds.Count > 0 ? _nativePageIds[^1] : null;

	private readonly Window _window;
	private readonly IMauiContext _mauiContext;
	private readonly NativeHostCache _cache = new();
	private readonly List<NativeElementHost> _current = new(); // mirror of the QML __order
	private readonly Dictionary<string, NativeElementHost> _byId = new(); // event routing

	private bool _navStackBusy;                          // pageStack.busy at the last nav snapshot
	private bool _nativePopUnsynced;                     // this poll saw a native pop MAUI has not followed yet
	private bool _strayScanPending;                      // a native pop asked for a stray sweep of the returned-to page
	private readonly QtHostCollectionBridge _collection;                  // CollectionView ⇄ ListView bridge
	private readonly HashSet<string> _bridgeFailLogged = new();            // error-report rate limit
	private readonly HashSet<string> _transformLimitWarned = new();        // one-time best-effort transform warnings
	private int _suppressPush;  // >0 while a native event is written back into MAUI

	/// <summary>Scope for writing native state into MAUI without PropertyChanged pushing it straight back.</summary>
	private SuppressScope SuppressPush()
	{
		_suppressPush++;
		return new SuppressScope(this);
	}

	private readonly struct SuppressScope(QtHostPageRenderer owner) : IDisposable
	{
		public void Dispose() => owner._suppressPush--;
	}

	/// <summary>Resolves an adapter event's {"id"} to its live host.</summary>
	private bool TryResolveHost(JsonElement root, out string? id,
		[System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out NativeElementHost? host)
	{
		id = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("id", out var idEl) &&
		     idEl.ValueKind == JsonValueKind.String ? idEl.GetString() : null;
		host = null;
		return !string.IsNullOrEmpty(id) && _byId.TryGetValue(id, out host);
	}
	private Page? _rendered;
	private string _renderedTitle = string.Empty;
	private string _renderedBusy = string.Empty;           // Page.IsBusy + pulley presence last pushed
	private string _renderedBackground = string.Empty;

	// --- Window geometry / layout state ---
	private bool _windowGeometryKnown;                 // first QML report received
	private bool _layoutDirty = true;                  // relayout needed before the next geometry push
	private Size _windowDp;                            // full window in dp (root space extent)
	private Rect _contentRectDp;                       // window minus the Silica insets (ROOT coordinate space)
	private double _lastPageW, _lastPageH, _lastHeader = -1, _lastStatus = -1, _lastDpr = -1;
	private string _lastOrientation = string.Empty;
	private readonly List<(NativeElementHost Host, NativeGeometry Geo, bool Visible, bool Local)> _geometryBatch = new();

	// Every ScrollView is its own native flickable host (see ApplyNestedScrollChanged); the page flickable
	// stays interactive only for the overscroll that drives pulleys or a page-armed refresh.
	private ScrollView? _primaryScroll;                     // the page's first vertical ScrollView
	private ScrollView? _primaryScrollWalk;                 // Walk detection
	// RefreshView paints nothing itself: the wrapped scroll surface arms the pull gesture and spinner.
	// One per page (page-scoped id); a page pulley owns the top overscroll, so refresh arms only without one.
	private readonly QtHostRefreshBinding _pageRefresh = new();   // page-armed refresh context
	private RefreshView? _refreshWalk;                       // Walk detection (first RefreshView)
	private string _lastRefreshPush = string.Empty;          // setMauiRefresh diff basis
	internal const string RefreshId = "refresh1";

	/// <summary>Pull-to-refresh props for an armed surface: the id only while IsRefreshEnabled, the spinner
	/// state and its tint (transparent = Silica highlight).</summary>
	internal static Dictionary<string, object?> RefreshSurfaceProps(RefreshView refresh) => new()
	{
		["mauiRefreshId"] = refresh.IsRefreshEnabled ? RefreshId : string.Empty,
		["mauiRefreshing"] = refresh.IsRefreshing,
		["mauiRefreshColor"] = refresh.RefreshColor ?? Colors.Transparent,
	};

	/// <summary>The RefreshView properties an armed surface re-pushes.</summary>
	internal static bool IsRefreshSurfaceProperty(string? propertyName) =>
		propertyName is nameof(RefreshView.IsRefreshing) or nameof(RefreshView.IsRefreshEnabled) or nameof(RefreshView.RefreshColor);
	// A RefreshView around a ScrollView arms the scroll-view host itself.
	private RefreshView? _scrollRefreshWalk;                 // Walk detection
	private NativeElementHost? _scrollRefreshHostWalk;
	private readonly QtHostRefreshBinding _scrollRefresh = new();   // armed on _scrollRefreshHost
	private NativeElementHost? _scrollRefreshHost;
	private readonly HashSet<string> _layoutFailStackOnce = new(StringComparer.Ordinal);
	private string _lastScrollPush = string.Empty;          // setMauiScroll diff basis
	private bool _pulleyReattachPending;                    // new pulley hosts: re-attach after the next layout pass
	private bool _pulleyOpened;                              // diag: MAUI_SAILFISH_OPEN_PULLEY
	private int _renderedPageSeq;                            // distinct page renders (diag trigger)

	// --- Interaction surfaces ---
	// Context flyouts are rebuilt per reconcile. Synthetic page-level hosts (pulley menus, context menu,
	// panels) reconcile like tree hosts but skip the geometry pass: Silica positions them (mauiDetached).
	private readonly Dictionary<Element, MenuFlyout> _contextFlyouts = new();
	private MenuFlyout? _openFlyout;
	private NativeElementHost? _ctxMenuHost;
	private NativeElementHost? _pullHost;
	private NativeElementHost? _pushHost;
	// Owner pages of the synthetic hosts: a menu built for another page instance has stale QML state and opens blank.
	private Page? _pullPage;
	private Page? _pushPage;
	private Page? _ctxPage;
	private readonly Dictionary<string, NativeElementHost> _interactionHosts = new();
	private readonly Dictionary<string, Dictionary<string, object?>> _interactionProps = new();
	private TaskCompletionSource<object?>? _dialogTcs;   // alert(bool) / prompt(string?) / sheet(string)
	private string _sheetCancel = string.Empty;          // action-sheet dismiss → cancel text

	// --- Native navigation: one MauiModelPage per MAUI page on the Silica pageStack ---
	// Sync runs both ways. Single-level push/pop animate like native Silica once the back cache is armed;
	// multi-level syncs use PageStackAction.Immediate (initialPage/animatorPush caveat: docs/architecture.md).
	// Only the top page is reconciled; the outgoing page's hosts are parked in a one-level back cache so a
	// pop-back repaints from live objects.
	private readonly List<string> _nativePageIds = new();   // mirror of MauiShell.mauiPages
	private int _nativePageSeq;                              // "mp<N>" id generator (mp1 = shell root)
	private bool _navStateAdopted;                           // first registry read adopted
	private bool? _lastWindowActive;                         // activation-bridge state
	private int _lastAppState = -1;                          // Qt.application.state mirror
	private bool _appLifecycleStarted;                       // Created/Resumed delivered once
	private Page? _pendingAppearing;                         // SendAppearing after the create batch
	private long _navOpSeq;                                  // monotonic id for the navigation operation log

	/// <summary>Acceptance-harness fault injection (one-shot): the next native push/pop reports rejection
	/// without touching the pageStack, so the rollback paths run on device. Counted in <see cref="NativeOpFailures"/>.</summary>
	internal bool FaultNextPush;
	internal bool FaultNextPop;
	private string _topModelPageId = string.Empty;           // registry top — the page the hosts live on
	private int _deferredModelPageTicks;                     // reconciles spent waiting for a stable activation
	private long _deferredSinceMs;                           // when that wait began (bounds it in time)
	private long _activeSinceMs;                             // since when appState==Active (0 = not active)
	private bool _fullResetPending;                          // a dead host asks for a whole-page rebuild
	private int _fullResetsDone;                             // per-page cap on full rebuilds (loop guard)
	private int _healedSinceReconcile;                       // dead-host burst counter (per reconcile)
	private long _resetHoldUntilMs;                          // creation held after a full-page reset (deferred QML deletes)

	/// <summary>Max consecutive polls host creation may wait for the native stack (≈10 s at 250 ms), so a
	/// stuck depth sync can't leave the app blank forever.</summary>
	/// <summary>Longest wait for a stable activation before hosts are created anyway (a stuck sync must not leave
	/// the app blank); it was 40 polls of 250 ms.</summary>
	private const int DeferredModelPageLimitMs = 10_000;

	/// <summary>How long the application must stay Active before hosts are created: Silica rebuilds model pages in
	/// the activation, and objects created meanwhile die (README #5). It was two polls. Tests set 0.</summary>
	internal static int ActivationSettleMs { get; set; } = 250;

	/// <summary>The application is stably Active (see <see cref="ActivationSettleMs"/>).</summary>
	private bool ActivationSettled =>
		_activeSinceMs != 0 && Environment.TickCount64 - _activeSinceMs >= ActivationSettleMs;

	private long _kickAtMs;   // the earliest scheduled kick (0 = none)

	/// <summary>Runs a kicked poll in <paramref name="ms"/> (a deadline the renderer waits for: the activation gate,
	/// a navigation operation's timeout). Earlier requests win; tests drive their polls themselves.</summary>
	private void KickIn(long ms)
	{
		var now = Environment.TickCount64;
		var at = now + Math.Max(1, ms);
		// An earlier kick still pending covers this one; one whose time passed has run (or was lost) and covers nothing.
		if (_kickAtMs > now && _kickAtMs <= at)
			return;
		if (Microsoft.Maui.Dispatching.Dispatcher.GetForCurrentThread() is not { } dispatcher)
			return;
		_kickAtMs = at;
		if (QtHostDiag.TraceEnabled)
			QtHostDiag.Trace(QtHostDiagChannel.QtHost, $"kick scheduled in {ms} ms");
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(Math.Max(1, ms)), () =>
		{
			if (_kickAtMs == at)
				_kickAtMs = 0;
			if (QtHostDiag.TraceEnabled)
				QtHostDiag.Trace(QtHostDiagChannel.QtHost, $"kick fired ({Environment.TickCount64 - at:+0;-0} ms vs due)");
			RequestPoll();
		});
	}

	/// <summary>True while host creation is deferred: before the first window report or until the app is active.
	/// Silica rebuilds model-page visuals around activation and hosts created earlier die en masse (which has
	/// crashed QV4); the collection bridge uses the same gate.</summary>
	internal bool CreationDeferred =>
		!_windowGeometryKnown || (!ActivationSettled && !DeferralExpired);

	/// <summary>The activation wait ran out (<see cref="DeferredModelPageLimitMs"/>): create the page anyway.</summary>
	private bool DeferralExpired =>
		_deferredSinceMs != 0 && Environment.TickCount64 - _deferredSinceMs >= DeferredModelPageLimitMs;

	/// <summary>Property values pushed through the bridge.</summary>
	public long BridgeApplied { get; private set; }

	/// <summary>Property pushes rejected or failed by the shim.</summary>
	public long BridgeFailed { get; private set; }

	/// <summary>Adapter events written back into MAUI.</summary>
	public long NativeEventsDelivered { get; private set; }

	/// <summary>Adapter event echoes dropped by change suppression.</summary>
	public long NativeEventsSuppressed { get; private set; }

	/// <summary>Geometry entries applied to native items.</summary>
	public long GeometryApplied { get; private set; }

	/// <summary>Geometry entries rejected by the shim.</summary>
	public long GeometryFailed { get; private set; }

	/// <summary>Adapter "focus-changed" events that drove VisualElement.Focus/Unfocus.</summary>
	public long FocusTransitions { get; private set; }

	/// <summary>Entry/Editor completions fired from adapter "completed" events (hardware or VKB Return).</summary>
	public long CompletedFired { get; private set; }

	/// <summary>Native caret/selection reports written back into InputView.CursorPosition/SelectionLength.</summary>
	public long CursorWriteBacks { get; private set; }

	/// <summary>Native scroll reports written back into ScrollView.ScrollY.</summary>
	public long ScrollWriteBacks { get; private set; }

	/// <summary>ContextMenu item activations delivered to MAUI MenuFlyoutItems.</summary>
	public long ContextMenuActivations { get; private set; }

	/// <summary>Pulley-menu item activations delivered to MAUI ToolbarItems.</summary>
	public long ToolbarActivations { get; private set; }

	/// <summary>Native DockedPanel open-state changes reported over the bridge.</summary>
	public long PanelOpenChanges { get; private set; }

	/// <summary>Native Drawer open-state changes reported over the bridge.</summary>
	public long DrawerOpenChanges { get; private set; }

	/// <summary>Managed-driven pageStack pushes.</summary>
	public long NativePushes { get; private set; }

	// Perf timing: Reconcile = tree walk + diff per poll; NavToAppearing = wall time from push/pop to SendAppearing.

	/// <summary>Reconcile passes (poll + Render).</summary>
	public long ReconcileCount { get; private set; }

	/// <summary>Total reconcile time (ms).</summary>
	public double ReconcileTotalMs { get; private set; }

	/// <summary>Last reconcile time (ms).</summary>
	public double LastReconcileMs { get; private set; }

	/// <summary>Average reconcile time (ms per pass).</summary>
	public double AvgReconcileMs => ReconcileCount > 0 ? ReconcileTotalMs / ReconcileCount : 0;

	/// <summary>Wall time of the last push/pop → SendAppearing (ms).</summary>
	public double LastNavToAppearingMs { get; private set; }

	// Nav timeline (Navigation trace channel): one line per navigation splitting request → native push/pop →
	// hosts → layout → Appearing → first list rows.
	private static long s_navRequestTs;
	private long _tlStart, _tlNative, _tlHosts, _tlLayout, _tlAppear;
	private int _tlHostCount;
	private double _tlOpsEvalMs;
	private string _tlKind = string.Empty;

	/// <summary>The navigation handler saw a MAUI push/pop request (any thread).</summary>
	internal static void NoteNavigationRequest()
	{
		Interlocked.Exchange(ref s_navRequestTs, System.Diagnostics.Stopwatch.GetTimestamp());
		RequestPoll();
	}

	/// <summary>
	/// Runs the navigation sync + reconcile on the next loop turn instead of at the next 250 ms poll; repeated
	/// requests before it runs collapse into one. A request before the host loop set the kick must not latch the flag.
	/// </summary>
	internal static void RequestPoll()
	{
		if (NavigationKick is { } kick && Interlocked.Exchange(ref s_navKickPending, 1) == 0)
			kick();
	}

	/// <summary>Managed push/pop animate like native Silica navigation; MAUI_SAILFISH_QT_HOST_NAV_ANIMATION=0
	/// restores Immediate transitions.</summary>
	internal static bool NavAnimation { get; set; } = !string.Equals(
		SailfishEnv.Get("MAUI_SAILFISH_QT_HOST_NAV_ANIMATION"), "0", StringComparison.Ordinal);

	/// <summary>Set by the host loop: queues one immediate <see cref="Poll"/> on the Qt thread.</summary>
	internal static Action? NavigationKick;
	private static int s_navKickPending;

	/// <summary>The kicked poll (see <see cref="NoteNavigationRequest"/>).</summary>
	internal void KickedPoll()
	{
		Interlocked.Exchange(ref s_navKickPending, 0);
		PollCore(kicked: true);
	}

	private static double TlMs(long from, long to) =>
		from == 0 || to == 0 ? -1 : (to - from) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

	private void TimelineStart(string kind)
	{
		if (_tlStart != 0)
			return;
		var now = System.Diagnostics.Stopwatch.GetTimestamp();
		var request = Interlocked.Exchange(ref s_navRequestTs, 0);
		_tlStart = request != 0 && TlMs(request, now) < 5000 ? request : now;
		_tlNative = now;
		_tlKind = kind;
		_tlHosts = _tlLayout = _tlAppear = 0;
		_tlHostCount = 0;
		_tlOpsEvalMs = 0;
	}

	/// <summary>The collection bridge built a list's rows; the first build after a navigation closes the timeline.</summary>
	internal void NoteRowsBuilt(int rows)
	{
		if (_tlAppear == 0)
			return;
		QtHostDiag.Trace(QtHostDiagChannel.Navigation,
			$"NAV-TIMELINE rows: {rows} list rows built +{TlMs(_tlStart, System.Diagnostics.Stopwatch.GetTimestamp()):F0} ms after the request");
		_tlAppear = 0;
		_tlStart = 0;
	}

	/// <summary>Measured navigations (push/pop with SendAppearing).</summary>
	public long NavTimings { get; private set; }

	/// <summary>Total measured navigation time (ms).</summary>
	public double NavTotalMs { get; private set; }

	private long _navIdleWallMs;   // Date.now() of the last QML pageStack busy → idle, until the next op batch

	/// <summary>MAUI_SAILFISH_NAV_IDLE_KICK=0 leaves the transition-end and native depth events to the next poll (A/B).</summary>
	private static readonly bool NavIdleKick = SailfishEnv.Get("MAUI_SAILFISH_NAV_IDLE_KICK") != "0";
	private static bool s_navEventsSubscribed;

	/// <summary>The shell reports the end of a pageStack transition and a native depth change (a back gesture): the
	/// sync runs then, not at the next 250 ms poll (MauiShell.qml).</summary>
	private static void SubscribeNavigationEvents()
	{
		if (s_navEventsSubscribed)
			return;
		s_navEventsSubscribed = true;
		QtHostServices.Subscribe("svc-nav-idle", e =>
		{
			if (Current is { } renderer && e.TryGetProperty("t", out var t) && t.TryGetInt64(out var ms))
				renderer._navIdleWallMs = ms;
			if (NavIdleKick)
				RequestPoll();
		});
		QtHostServices.Subscribe("svc-nav-depth", _ =>
		{
			if (NavIdleKick)
				RequestPoll();
		});
		// Lifecycle (Resumed/Stopped/Activated) and the activation gate follow the application state at once.
		QtHostServices.Subscribe("svc-app-state", _ => RequestPoll());
	}

	/// <summary>Op batches that followed a pageStack transition end, and their total delay (ms).</summary>
	public long IdleToRenderCount { get; private set; }
	public double IdleToRenderTotalMs { get; private set; }
	public double AvgIdleToRenderMs => IdleToRenderCount > 0 ? IdleToRenderTotalMs / IdleToRenderCount : 0;

	/// <summary>Average push/pop → SendAppearing (ms).</summary>
	public double AvgNavToAppearingMs => NavTimings > 0 ? NavTotalMs / NavTimings : 0;

	/// <summary>Dirty layout passes (MeasureAndArrange + geometry flush); should stay ~0 at rest.</summary>
	public long LayoutPasses { get; private set; }

	/// <summary>Layout passes requested by handlers (InvalidateMeasure, geometry keys); 0 at rest.</summary>
	public long LayoutRequests { get; private set; }

	/// <summary>_layoutDirty arms from native write-backs (text/scroll/date/time).</summary>
	public long LayoutDirtyFromWriteback { get; private set; }

	private System.Diagnostics.Stopwatch? _navStopwatch;

	/// <summary>Managed-driven pageStack pops (PopAsync/PopModal/PopToRoot/hardware Back).</summary>
	public long NativePops { get; private set; }

	/// <summary>Native-side pops (Silica back gesture) synced back into MAUI.</summary>
	public long NativePopSyncs { get; private set; }

	/// <summary>Native stack mutations rejected by the pageStack. The mirror is rolled back each time, so a
	/// non-zero value means the retry path ran, not that the stacks diverged.</summary>
	public long NativeOpFailures { get; private set; }

	/// <summary>Depth-sync pushes suppressed because a native→MAUI pop was still in flight (the pop-vs-push race).</summary>
	public long NativePopRacesBlocked { get; private set; }

	/// <summary>Top-model-page changes that re-armed the page-instance-scoped render state.</summary>
	public long ModelPageSwitches { get; private set; }

	/// <summary>Reconciles that deferred host creation because the MAUI stack outran the native one.</summary>
	public long DeferredModelPageSyncs { get; private set; }

	/// <summary>Page.SendAppearing deliveries; the backend owns appearing semantics, MAUI core never fires them.</summary>
	public long AppearingSent { get; private set; }

	/// <summary>Page.SendDisappearing deliveries.</summary>
	public long DisappearingSent { get; private set; }

	/// <summary>Window/application activation transitions delivered to MAUI.</summary>
	public long ActivationEvents { get; private set; }

	/// <summary>Per-event activation-bridge counters (a subset of ActivationEvents).</summary>
	public long ActivatedSent { get; private set; }
	public long DeactivatedSent { get; private set; }
	public long ResumedSent { get; private set; }
	public long StoppedSent { get; private set; }

	/// <summary>Last polled Qt::ApplicationState and window.active mirrors (diagnostics).</summary>
	public int LastAppState => _lastAppState;
	public bool? LastWindowActive => _lastWindowActive;

	/// <summary>Dialogs (alert / prompt / action sheet) pushed on the Silica pageStack.</summary>
	public long DialogPushes { get; private set; }

	/// <summary>Dialog results completed back into MAUI tasks.</summary>
	public long DialogResults { get; private set; }

	/// <summary>Native DockedPanel open-state write-backs (SailfishBottomSheet.OpenChanged).</summary>
	public event Action<string, bool>? PanelOpenChanged;

	/// <summary>Managed mirror of the native model-page stack (ids bottom → top).</summary>
	public IReadOnlyList<string> NativePageIds => _nativePageIds;

	/// <summary>The page's main (first vertical) ScrollView; diagnostics drive ScrollToAsync through it.</summary>
	internal ScrollView? ScrollContext => _primaryScroll;

	/// <summary>The last window/safe-area geometry report, human-readable.</summary>
	public string LastWindowGeometryReport { get; private set; } = "(no window-geometry report yet)";

	public QtHostPageRenderer(Window window, IMauiContext mauiContext)
	{
		ArgumentNullException.ThrowIfNull(window);
		ArgumentNullException.ThrowIfNull(mauiContext);
		_window = window;
		_mauiContext = mauiContext;
		_collection = new QtHostCollectionBridge(this);
		// View children arrive through their layout handler (Add/Insert/Remove) or a content mapper, which reconcile
		// right away; pages (a Shell section, a tab, a modal) still announce themselves through the window.
		window.DescendantAdded += (_, e) => { if (e.Element is Page) RequestPoll(); };
		window.DescendantRemoved += (_, e) => { if (e.Element is Page) RequestPoll(); };
		// The alert manager and SailfishBottomSheet resolve the live surface through this (one renderer per run).
		Current = this;
		SubscribeNavigationEvents();
	}

	/// <summary>The renderer of the current window (set on construction).</summary>
	public static QtHostPageRenderer? Current { get; private set; }

	/// <summary>The CollectionView ⇄ native ListView bridge.</summary>
	internal QtHostCollectionBridge Collection => _collection;

	/// <summary>The id of the nearest attached host at or above <paramref name="element"/> (a flattened layout has
	/// none of its own), or null.</summary>
	internal string? HostIdOf(Element element)
	{
		for (Element? e = element; e is not null && e is not Page; e = e.Parent)
			if (_cache.TryGet(e, out var host) && host is { IsAttached: true })
				return host.Id;
		return null;
	}

	/// <summary>The MAUI page that should be visible right now (NavigationPage-aware).</summary>
	public Page? CurrentPage => ResolveCurrentPage();

	/// <summary>Hosts currently mirrored in QML (diagnostics/acceptance evidence).</summary>
	public IReadOnlyList<NativeElementHost> CurrentHosts => _current;

	/// <summary>
	/// Reconciles the MAUI logical tree against the native tree (Qt-thread poll). Cheap no-op when nothing changed.
	/// </summary>
	public void Poll() => PollCore(kicked: false);

	/// <summary>How long creation waits after a full-page reset for QML's deferred deletes (it was two 250 ms polls).</summary>
	private const int ResetHoldMs = 300;

	/// <summary>The heartbeat poll interval (ms) when MAUI_SAILFISH_POLL_MS is unset; it was 250 ms.</summary>
	public const int DefaultHeartbeatMs = 2000;

	private void PollCore(bool kicked)
	{
		if (kicked)
			_kickedPolls++;
		else
			_timerPolls++;
		var work = NativeWork;
		var (sets0, geo0, ops0) = (QtHostRuntime.PropertySets, QtHostRuntime.GeometryBatches, _opsEvals);
		var t0 = SailfishRuntime.SlowWorkMs > 0 ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
		// The long-press hold timer rides the tick: a still finger produces no pointer events.
		QtHostInputRouter.Active?.PollHold();
		if (_navOp is not null && QtHostDiag.TraceEnabled)
			QtHostDiag.Trace(QtHostDiagChannel.Navigation, $"poll ({(kicked ? "kicked" : "heartbeat")}) with {_navOp} in flight");
		// Navigation sync runs before the reconcile so teardown/creation targets the actual top page.
		_nativePopUnsynced = false;
		SyncNativeNavigation();
		CompleteSettledNavigation();
		var t1 = t0 != 0 ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
		// Skip the reconcile during an animated transition (its geometry flush would count the dying page's hosts
		// dead and wipe the revealed page) and in a poll with an unfollowed native pop (it would flash the old page).
		if (!_navStackBusy && !_nativePopUnsynced && ResolveReconcilePage() is not null)
			Reconcile();
		var t2 = t0 != 0 ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
		// Deferred row builds, slots, resync; the heartbeat leaves scheduled list work to the lists' own clock, so
		// what it does here is work no list scheduled.
		if (kicked || !_collection.PendingScheduled)
			_collection.ProcessPending();
		if (t0 != 0)
		{
			static double Ms(long a, long b) => (b - a) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
			var t3 = System.Diagnostics.Stopwatch.GetTimestamp();
			if (Ms(t0, t3) >= SailfishRuntime.SlowWorkMs)
				Console.Error.WriteLine($"[Sailfish][SLOW] poll {(kicked ? "kicked" : "timer")} {Ms(t0, t3):F0} ms: " +
					$"nav {Ms(t0, t1):F0} reconcile {Ms(t1, t2):F0} (last {LastReconcileMs:F0}) rows {Ms(t2, t3):F0} " +
					$"layoutPasses={LayoutPasses} page='{(_rendered is null ? "-" : TitleOf(_rendered))}'");
		}
		if (!kicked && NativeWork != work)
		{
			_timerPollsWithWork++;
			if (_timerPollsWithWork <= 5)
				QtHostDiag.Warn(QtHostDiagChannel.QtHost,
					$"heartbeat poll found work no event announced (#{_timerPollsWithWork}, page " +
					$"'{(_rendered is null ? "-" : TitleOf(_rendered))}', ops +{_opsEvals - ops0}, geometry +{QtHostRuntime.GeometryBatches - geo0}, " +
					$"sets +{QtHostRuntime.PropertySets - sets0}) — see the timer-poll work trace");
			// What only the safety net did (A7 of the architecture plan removes the timer once this is empty).
			if (QtHostDiag.TraceEnabled)
				QtHostDiag.Trace(QtHostDiagChannel.QtHost,
					$"timer-poll work: ops +{_opsEvals - ops0} geometry +{QtHostRuntime.GeometryBatches - geo0} " +
					$"sets +{QtHostRuntime.PropertySets - sets0} batches +{NativeWork - work - (_opsEvals - ops0) - (QtHostRuntime.GeometryBatches - geo0) - (QtHostRuntime.PropertySets - sets0)} " +
					$"(deferred={CreationDeferred} navBusy={_navStackBusy} page='{(_rendered is null ? "-" : TitleOf(_rendered))}') " +
					$"ops=[{(_opsEvals != ops0 ? _lastOps : string.Empty)}]");
		}
	}

	/// <summary>
	/// Pops for hardware Back; false when there is nothing to pop. An open modal pops first, and a modal
	/// NavigationPage absorbs one inner level first; the decision is shared with the native-pop path via
	/// <see cref="ResolveBackTarget"/>.
	/// </summary>
	public bool TryPop()
	{
		var (target, nav) = ResolveBackTarget();
		switch (target)
		{
			case BackTarget.None:
				return false;
			case BackTarget.Modal:
				QtHostDiag.Trace(QtHostDiagChannel.Navigation, "hardware Back → MAUI PopModalAsync");
				_ = PopModalTopAsync();
				return true;
			default:
				QtHostDiag.Trace(QtHostDiagChannel.Navigation, $"hardware Back → MAUI PopAsync ({target})");
				_ = nav!();
				return true;
		}
	}

	// --- Layout requests ---
	// As a native view requests a layout pass, a handler asks for one: MAUI's InvalidateMeasure reaches it through
	// Handler.Invoke, and the geometry keys (transforms, visibility, flow direction, scroll position) through its
	// mapper. Requests coalesce into one pass on the next loop turn; the pass's own arrange writes are not requests.

	private bool _layoutPosted;   // a RunRequestedLayout is queued
	private bool _inLayoutPass;   // RunLayoutPass is running: its Bounds writes invalidate nothing

	/// <summary>Asks for a layout + geometry pass on the next loop turn (any thread).</summary>
	internal void RequestLayout()
	{
		if (_inLayoutPass)
			return;
		_layoutDirty = true;
		LayoutRequests++;
		if (_layoutPosted)
			return;
		_layoutPosted = true;
		QtHostRuntime.Post(RunRequestedLayout);
	}

	private void RunRequestedLayout()
	{
		_layoutPosted = false;
		// The reconcile lays out itself; during a transition or before the first frame the next poll does.
		if (!_layoutDirty || _navStackBusy || _rendered is not { } page || CreationDeferred)
			return;
		RunLayoutPass(page);
	}

	private bool _geometryDirty;    // root rects are stale (a scroll moved) but MAUI's layout is not
	private bool _geometryPosted;   // a RunRequestedGeometry is queued

	/// <summary>A ScrollView's position moved (a native flick or ScrollX/ScrollY): MAUI's layout does not depend on it,
	/// only the content's root rects do (hit-testing, the viewport clip). Queues a geometry pass without measure and
	/// arrange (F5: a full layout pass per native scroll report cost 100+ ms of the GUI thread while flicking).</summary>
	internal void RequestScrollGeometry()
	{
		_geometryDirty = true;
		if (_geometryPosted || _inLayoutPass)
			return;
		_geometryPosted = true;
		QtHostRuntime.Post(RunRequestedGeometry);
	}

	private void RunRequestedGeometry()
	{
		_geometryPosted = false;
		if (!_geometryDirty || _navStackBusy || _rendered is not { } page || CreationDeferred || !_windowGeometryKnown)
			return;
		if (_layoutDirty)
		{
			RunLayoutPass(page);   // a real layout is due anyway; it collects the geometry too
			return;
		}
		RunGeometryPass(page);
	}

	/// <summary>
	/// Focus decided by Qt, as requestFocus is by the Android view: pushes the focus request to a text adapter and reads
	/// its activeFocus back (a disabled or hidden field refuses). Null when the host is not native yet. Qt thread.
	/// </summary>
	internal bool? FocusHost(NativeElementHost host, bool focus)
	{
		if (!QtHostRuntime.IsQtThread || !host.IsAttached)
			return null;
		bool Active() => QtHostRuntime.GetProperty(host.NativeHandle, "activeFocus") == "true";
		var json = BridgeValue.Serialize(focus);
		if (Active() == focus)
		{
			host.AppliedProperties["mauiFocus"] = json;   // already there (e.g. the native focus-changed follow-up)
			return true;
		}
		// The adapter acts on a change: re-arm a stale "true" first.
		if (focus && host.IsApplied("mauiFocus", json))
			PushBatch(host, new[] { ("mauiFocus", BridgeValue.Serialize(false)) });
		PushBatch(host, new[] { ("mauiFocus", json) });
		var granted = Active() == focus;
		if (focus && !granted)
			PushBatch(host, new[] { ("mauiFocus", BridgeValue.Serialize(false)) });   // refused: stay consistent
		return granted;
	}

	/// <summary>Text-input properties the handlers leave out of their snapshots, so native focus and caret
	/// survive the reconcile poll; their mappers push them on their own (<see cref="PushTransient"/>).</summary>
	internal static readonly string[] TransientInputProperties =
		{ nameof(VisualElement.IsFocused), nameof(InputView.CursorPosition), nameof(InputView.SelectionLength) };

	/// <summary>
	/// Pushes transient native state from a handler mapper (focus; caret and selection as one atomic pair, so the
	/// managed set order cannot leave a wrong selection natively). Skipped while native state is written back into
	/// MAUI, and when native already holds every value.
	/// </summary>
	internal void PushTransient(NativeElementHost host, IReadOnlyList<(string Name, object? Value)> values)
	{
		void Apply()
		{
			if (_suppressPush > 0 || !host.IsAttached)
				return;
			var batch = new (string Name, string ValueJson)[values.Count];
			var changed = false;
			for (var i = 0; i < values.Count; i++)
			{
				batch[i] = (values[i].Name, BridgeValue.Serialize(values[i].Value));
				changed |= !host.IsApplied(batch[i].Name, batch[i].ValueJson);
			}
			if (changed && PushBatch(host, batch))
				_handlerPropertyPushes += batch.Length;
		}
		QtHostRuntime.RunOnQtThread(Apply);
	}

	/// <summary>Sends one ordered, suppressed batch through the shim and records the applied state; failures are
	/// logged once per host/context.</summary>
	private bool PushBatch(NativeElementHost host, IReadOnlyList<(string Name, string ValueJson)> changed)
	{
		var batch = QtHostBridge.BuildBatch(changed, suppress: true);
		var failed = QtHostRuntime.ApplyProperties(host.NativeHandle, batch);
		if (failed < 0)
		{
			BridgeFailed++;
			LogBridgeFailure(host, "batch", failed);
			return false;
		}
		foreach (var (name, json) in changed)
			host.AppliedProperties[name] = json;
		BridgeApplied += changed.Count;
		if (failed > 0)
		{
			BridgeFailed += failed;
			LogBridgeFailure(host, "batch", failed);
			HealIfDead(host);   // dead handle → recreate
		}
		return true;
	}

	private void LogBridgeFailure(NativeElementHost host, string what, int rc)
	{
		if (!_bridgeFailLogged.Add($"{host.Id}:{what}:{rc}"))
			return;
		QtHostDiag.Error(QtHostDiagChannel.QmlProperty, $"apply failed on {host} ({what}) rc={rc}: {QtHostRuntime.LastErrorText}");
	}

	/// <summary>The walk skipped a shape that is not arranged yet (retried after the layout pass).</summary>
	private bool _skippedUnarranged;
	private bool _unarrangedRetry;
	private bool _createdInPass;   // this reconcile created hosts (new content, not only a navigation)

	/// <summary>Reconcile core: diffs the current page's logical tree against the persistent hosts and applies
	/// the minimal create/update/destroy set (Qt thread only), timed for diagnostics.</summary>
	private void Reconcile()
	{
		_skippedUnarranged = false;
		var sw = System.Diagnostics.Stopwatch.StartNew();
		try
		{
			ReconcileCore();
		}
		finally
		{
			sw.Stop();
			LastReconcileMs = sw.Elapsed.TotalMilliseconds;
			ReconcileTotalMs += LastReconcileMs;
			ReconcileCount++;
		}
	}

	private void ReconcileCore()
	{
		var page = ResolveReconcilePage();
		if (page is null)
		{
			QtHostDiag.Warn(QtHostDiagChannel.QtHost, "no MAUI page to render yet");
			return;
		}

		_healedSinceReconcile = 0;
		var handlerReported = TakePendingSubtrees();
		// A burst of dead hosts means a Silica rebuild took the page's visuals: wipe the top page's hosts and
		// rebuild from current MAUI state.
		if (_fullResetPending)
		{
			_fullResetPending = false;
			if (_fullResetsDone < 3)
			{
				_fullResetsDone++;
				FullPageReset($"dead-host burst, rebuild #{_fullResetsDone}");
				// QML destroy() is deferred, so a same-tick create would resolve its handle to the dying object (same
				// objectName) and strand the page blank. Hold creation until the deletes ran.
				_resetHoldUntilMs = Environment.TickCount64 + ResetHoldMs;
				KickIn(ResetHoldMs);
			}
		}
		if (_resetHoldUntilMs > Environment.TickCount64)
		{
			return;
		}

		_byId.Clear();
		var desired = new List<NativeElementHost>();
		var props = new Dictionary<NativeElementHost, Dictionary<string, object?>>();
		_primaryScrollWalk = null;   // the main ScrollView is detected per reconcile
		_scrollRefreshWalk = null;
		_scrollRefreshHostWalk = null;
		_refreshWalk = null;
		_contextFlyouts.Clear();   // flyout registry follows the tree
		// The walk re-adds every shape still waiting for a size, so the set holds only shapes on the page as it is now.
		_awaitingArrange.Clear();
		Walk(page, desired, props);
		AddSyntheticHosts(page, desired, props);   // page-level surfaces
		foreach (var host in desired)
		{
			_byId[host.Id] = host;   // event routing table (ids follow the reconcile)
			// Generic visual state (opacity/enabled/z cascades computed managed-side) joins every snapshot.
			if (host.Element is VisualElement visualState && props.TryGetValue(host, out var hostProps))
				QtHostVisualState.Merge(hostProps, visualState);
			// Children of a rounded Border are sibling hosts, so the corner clip crosses as a spec and the adapter
			// masks itself. Only adapters that declare mauiClipRadius/mauiClipCorners may get it: an unknown name
			// fails the whole apply_props batch.
			if (host.QmlUri == "image" && props.TryGetValue(host, out var clipProps))
				QtHostClip.Merge(clipProps, host.Element as VisualElement, page);
		}
		_collection.ContributeRouting(_byId);   // item/slot child ids join the table
		if (_strayScanPending)
		{
			_strayScanPending = false;
			var known = BridgeValue.Serialize(_byId.Keys.ToList());
			QtHostRuntime.Eval(QmlPage.Call(QmlPage.Model, "__destroyHostsNotIn", BridgeValue.Quote(known)));
		}
		// Page-level pull-to-refresh, unless a hosted list or scroll view consumes it or a page pulley owns the overscroll.
		ArmRefresh(_refreshWalk is not null && !_collection.ConsumesRefresh(_refreshWalk)
		           && !ReferenceEquals(_scrollRefreshWalk, _refreshWalk)
		           && !PageHasPulley
			? _refreshWalk
			: null);
		ArmScrollRefresh(_scrollRefreshWalk, _scrollRefreshHostWalk);
		var pageChanged = !ReferenceEquals(page, _rendered);
		// Trace button state on every page swap.
		if (pageChanged)
			foreach (var witnessHost in desired)
				if (witnessHost.Element is Button witnessButton && props.TryGetValue(witnessHost, out var witnessProps))
					QtHostDiag.Trace(QtHostDiagChannel.QtHost,
						$"button witness '{witnessButton.Text}' enabled={witnessProps.GetValueOrDefault("enabled")} color={witnessProps.GetValueOrDefault("color")} plate={witnessProps.GetValueOrDefault("backgroundColor")} on '{TitleOf(page)}'");
		var previousPage = _rendered;
		_rendered = page;
		if (pageChanged)
			_renderedPageSeq++;   // diag trigger (MAUI_SAILFISH_OPEN_PULLEY page seq)
		if (pageChanged)
		{
			// A fresh setMauiScroll push for the new page's flickable state.
			_lastScrollPush = string.Empty;
			_fullResetsDone = 0;   // the rebuild cap is per page
			// Title/background/tabs belong to the model page instance, which is fresh or cleared after navigation, so re-emit them.
			_renderedTitle = string.Empty;
			_renderedBusy = string.Empty;
			_renderedBackground = string.Empty;
			_renderedTabs = string.Empty;
			_layoutDirty = true;
			// MAUI core never fires appearing/disappearing; the backend owns them. Disappearing fires before the new
			// hosts exist, Appearing after the create batch + layout.
			if (previousPage is not null)
			{
				((IPageController)previousPage).SendDisappearing();
				DisappearingSent++;
				QtHostDiag.Trace(QtHostDiagChannel.Navigation, $"'{TitleOf(previousPage)}' → SendDisappearing");
			}
			_pendingAppearing = page;
		}

		// Before the first window report Silica still rebuilds model pages and hosts created then keep dead handles.
		// Defer all creation until the geometry is known; ApplyWindowGeometry reconciles again.
		if (!_windowGeometryKnown)
			return;

		// Until the app is active the Silica stack is in motion: hosts would attach to a model page about to be
		// replaced or die in the activation rebuild. Defer creation until ApplicationActive (or the native stack
		// catches up); bounded so a stuck sync can't leave the app blank.
		if (!ActivationSettled && !DeferralExpired)
		{
			_deferredModelPageTicks++;
			DeferredModelPageSyncs++;
			if (_deferredSinceMs == 0)
			{
				_deferredSinceMs = Environment.TickCount64;
				QtHostDiag.Warn(QtHostDiagChannel.Navigation,
					$"README #5: app not stably active yet (Qt.application.state={_lastAppState}, MAUI depth {ExpectedNativeDepth()} vs native {_nativePageIds.Count}) — " +
					"deferring host creation until the Silica stack settles at activation");
			}
			// Come back when the gate opens (the app-state event covers the not-yet-active case).
			KickIn(_activeSinceMs != 0
				? ActivationSettleMs - (Environment.TickCount64 - _activeSinceMs)
				: DeferredModelPageLimitMs - (Environment.TickCount64 - _deferredSinceMs));
			return;
		}
		if (_deferredModelPageTicks > 0)
		{
			QtHostDiag.Trace(QtHostDiagChannel.Navigation,
				$"README #5: native stack caught up after {_deferredModelPageTicks} deferred reconcile(s), " +
				$"{Environment.TickCount64 - _deferredSinceMs} ms — creating the page now");
			_deferredModelPageTicks = 0;
		}
		_deferredSinceMs = 0;

		var title = TitleOf(page);
		var ops = new List<Dictionary<string, object?>>();
		if (title != _renderedTitle)
		{
			_renderedTitle = title;
			ops.Add(BridgeOps.Title(title));
		}
		// Silica shows a busy page as a pulsing pulley bar when it has a pull-down menu, else a PageBusyIndicator.
		var busy = (page.IsBusy ? "1" : "0") + (_pullHost is not null ? "p" : "");
		if (busy != _renderedBusy)
		{
			_renderedBusy = busy;
			ops.Add(BridgeOps.Busy(page.IsBusy, _pullHost is not null));
		}

		// The tab bar (Shell tabs / TabbedPage) belongs to the model page instance; pushed when it changes.
		var tabs = ResolveTabs();
		var tabsJson = tabs is { } t
			? "{\"titles\":" + BridgeValue.Serialize(t.Titles) + ",\"index\":" + t.Index.ToString(CultureInfo.InvariantCulture) + "}"
			: "{\"titles\":[],\"index\":0}";
		_tabSelect = tabs?.Select;
		_tabIndex = tabs?.Index ?? 0;
		_tabCount = tabs?.Titles.Count ?? 0;
		if (tabsJson != _renderedTabs)
		{
			_renderedTabs = tabsJson;
			QtHostRuntime.Eval(QmlPage.Call(TopModelPageJs, "setMauiTabs", BridgeValue.Quote(tabsJson)));
		}

		// The model page paints the page background (colour, then BackgroundImageSource cropped to fill) behind the
		// Silica flickable so MAUI text colors stay readable.
		var backgroundColor = BridgeValue.ColorString(page.BackgroundColor ?? Colors.Transparent);
		var backgroundImage = QtHostImages.Resolve(page.BackgroundImageSource) ?? string.Empty;
		var background = backgroundColor + "|" + backgroundImage;
		if (background != _renderedBackground)
		{
			_renderedBackground = background;
			ops.Add(BridgeOps.Background(backgroundColor, backgroundImage));
		}

		if (desired.Count > LargePageHostWarning && _largePageWarned.Add(page))
			QtHostDiag.Warn(QtHostDiagChannel.QtHost,
				$"'{TitleOf(page)}' needs {desired.Count} hosts (> {LargePageHostWarning}) — consider a CollectionView for long content");

		// A tab, section or detail switch keeps the page switched away from, and brings the page switched to back.
		if (pageChanged)
			SwitchPageInPlace(previousPage, page);
		ReclaimParked(desired);

		var desiredSet = new HashSet<NativeElementHost>(desired);
		// Retire lists that left the tree, except those of the parked page: its rows must stay materialized for the
		// back gesture. DropRetention retires them later.
		_collection.SyncDesired(_parkedHosts.Count == 0 ? desired : desired.Concat(_parkedHosts).ToList());
		var currentSet = new HashSet<NativeElementHost>(_current);
		var survivors = _current.Where(desiredSet.Contains).ToList();       // in QML order
		var created = desired.Where(h => !currentSet.Contains(h)).ToList(); // in desired order

		// QML child order of every host (canvas key "") before this batch: the diff basis for "order" ops.
		static string ParentKey(NativeElementHost? parent) => parent?.Id ?? string.Empty;
		var qmlChildren = new Dictionary<string, List<string>>();
		foreach (var host in _current)
		{
			var key = host.AppliedParentId ?? string.Empty;
			if (!qmlChildren.TryGetValue(key, out var list))
				qmlChildren[key] = list = new List<string>();
			list.Add(host.Id);
		}

		// Destroy descendants before ancestors so no op addresses an item its parent's teardown already took.
		var destroyCount = _current.Count - survivors.Count;
		var destroyed = _current.Where(h => !desiredSet.Contains(h)).ToList();
		destroyed.Reverse();
		foreach (var host in destroyed)
		{
			ops.Add(BridgeOps.Destroy(host.Id));
			ReleaseHost(host);
			_current.Remove(host);
			// A destroyed synthetic host drops its slot so a reappearance gets a fresh object (a stale diff would swallow the first push).
			ReleaseSyntheticSlot(host);
			if (qmlChildren.TryGetValue(host.AppliedParentId ?? string.Empty, out var siblings))
				siblings.Remove(host.Id);
			host.AppliedParentId = null;
		}

		// A survivor that moved to another container keeps its QML object and is re-attached in the new parent;
		// the order ops below settle its position.
		foreach (var host in survivors)
		{
			var want = ParentKey(host.Parent);
			var have = host.AppliedParentId ?? string.Empty;
			if (want == have)
				continue;
			ops.Add(BridgeOps.Reparent(host.Id, want));
			if (qmlChildren.TryGetValue(have, out var oldSiblings))
				oldSiblings.Remove(host.Id);
			if (!qmlChildren.TryGetValue(want, out var newSiblings))
				qmlChildren[want] = newSiblings = new List<string>();
			newSiblings.Add(host.Id);
			host.AppliedParentId = want;
		}

		var createCount = created.Count;
		_createdInPass = createCount > 0;
		foreach (var host in created)
		{
			var parentKey = ParentKey(host.Parent);
			ops.Add(CreateOp(host, props.TryGetValue(host, out var p) ? p : EmptyProps, parentKey));
			host.AppliedParentId = parentKey;
			if (!qmlChildren.TryGetValue(parentKey, out var siblings))
				qmlChildren[parentKey] = siblings = new List<string>();
			siblings.Add(host.Id);
			_current.Add(host);
		}

		// Update: property diff applied in place through the native handle.
		var updateCount = 0;
		foreach (var host in survivors)
		{
			var pushed = ApplyUpdates(host, props.TryGetValue(host, out var p) ? p : EmptyProps, "reconcile-diff");
			updateCount += pushed;
			// Synthetic surfaces (pulleys, panels) have no handler yet: the reconcile is their only channel until A3.
			if (host.Element.Handler is Handlers.ISailfishViewHandler)
				_reconcileDiffPropertyPushes += pushed;
		}

		// Where appending cannot express a parent's desired child order, an "order" op re-attaches the same QObjects
		// in sequence. Child order is the stacking order among siblings.
		var reordered = false;
		foreach (var group in desired.GroupBy(h => ParentKey(h.Parent)))
		{
			var want = group.Select(h => h.Id).ToList();
			var have = qmlChildren.TryGetValue(group.Key, out var list) ? list : new List<string>();
			if (have.SequenceEqual(want))
				continue;
			ops.Add(BridgeOps.Order(group.Key, want));
			reordered = true;
		}
		// _current mirrors the desired pre-order (hit-test tie order: later = painted above).
		_current.Clear();
		_current.AddRange(desired);

		// Tree or property changes can move geometry.
		if (ops.Count > 0 || updateCount > 0)
			_layoutDirty = true;

		if (ops.Count > 0)
		{
			NoteTreeFixup(ops, pageChanged || createCount == _current.Count, handlerReported);
			foreach (var host in created)
				_awaitingArrange.Remove(host.Element);   // a skipped shape got its size and its host
			var json = BridgeValue.Serialize(ops);
			// A pulley created in this batch attaches to the page flickable only if it is already interactive;
			// otherwise it settles on a hosted list and is moved later, which Silica does not fully follow (the
			// push-up bar stayed unpainted). So the flickable state goes first, as in a Silica page's declaration.
			if (created.Any(h => ReferenceEquals(h, _pullHost) || ReferenceEquals(h, _pushHost)))
			{
				PushScrollState();
				_pulleyReattachPending = true;   // after the layout pass shows this page's hosts
			}
			// Addressed by the top model-page id (see TopModelPageJs).
			var opsTs = System.Diagnostics.Stopwatch.GetTimestamp();
			QtHostRuntime.Eval(QmlPage.Call(TopModelPageJs, "applyMauiOps", BridgeValue.Quote(json)));
			_opsEvals++;
			NoteOps(ops);
			if (_navIdleWallMs != 0)
			{
				// QML transition end → the next page content natively (the reconcile waits out the animation).
				var ms = (double)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _navIdleWallMs);
				_navIdleWallMs = 0;
				IdleToRenderCount++;
				IdleToRenderTotalMs += ms;
				QtHostDiag.Trace(QtHostDiagChannel.Navigation, $"NAV-IDLE→render {ms:F0} ms");
			}
			if (_tlStart != 0 && _tlHosts == 0)
				_tlOpsEvalMs = TlMs(opsTs, System.Diagnostics.Stopwatch.GetTimestamp());   // QML object creation
			foreach (var host in created)
			{
				AttachNative(host, props.TryGetValue(host, out var p) ? p : EmptyProps);
			}
			_collection.OnHostsCreated(created);   // fresh list adapters owe their row push
			if (_tlStart != 0 && _tlHosts == 0 && createCount > 0)
			{
				_tlHosts = System.Diagnostics.Stopwatch.GetTimestamp();
				_tlHostCount = createCount;
			}
			QtHostDiag.Trace(QtHostDiagChannel.QtHost, $"'{title}' reconcile create={createCount} destroy={destroyCount} " +
				$"update={updateCount} hosts={_current.Count} ops={ops.Count} ({(pageChanged ? "page switch" : "same page")}" +
				$"{(reordered ? ", reordered" : string.Empty)})");
		}
		else if (updateCount > 0)
		{
			QtHostDiag.Trace(QtHostDiagChannel.QtHost, $"'{title}' in-place update={updateCount} hosts={_current.Count} " +
				"(no ops — QML tree untouched)");
		}

		RunLayoutPass(page);   // MAUI layout + SetGeometry batch
		if (_tlStart != 0 && _tlLayout == 0 && (_tlHosts != 0 || _tlKind != "push"))
			_tlLayout = System.Diagnostics.Stopwatch.GetTimestamp();

		// Content reconciled before its first arrange (a navigation, or data that just arrived) skipped size-derived
		// shapes; create them now, in the same frame, instead of a poll later. One retry: a 0x0 shape stays skipped.
		if (_skippedUnarranged && !_unarrangedRetry && (_tlStart != 0 || _createdInPass))
		{
			_unarrangedRetry = true;
			try { Reconcile(); }
			finally { _unarrangedRetry = false; }
			return;   // the inner pass delivered SendAppearing
		}

		// The new page is live (hosts created, geometry applied): deliver SendAppearing once per switch.
		if (_pendingAppearing is not null && ReferenceEquals(_pendingAppearing, page))
		{
			_pendingAppearing = null;
			((IPageController)page).SendAppearing();
			AppearingSent++;
			// Close the navigation timing measurement.
			if (_navStopwatch is { } navSw)
			{
				_navStopwatch = null;
				navSw.Stop();
				LastNavToAppearingMs = navSw.Elapsed.TotalMilliseconds;
				NavTotalMs += LastNavToAppearingMs;
				NavTimings++;
			}
			if (_tlStart != 0)
			{
				_tlAppear = System.Diagnostics.Stopwatch.GetTimestamp();
				QtHostDiag.Trace(QtHostDiagChannel.Navigation,
					$"NAV-TIMELINE {_tlKind} '{TitleOf(page)}': request→native {TlMs(_tlStart, _tlNative):F0} ms, " +
					$"→hosts {TlMs(_tlStart, _tlHosts):F0} ms ({_tlHostCount} created, QML ops {_tlOpsEvalMs:F0} ms, reconcile {LastReconcileMs:F0} ms), " +
					$"→layout {TlMs(_tlStart, _tlLayout):F0} ms, →appearing {TlMs(_tlStart, _tlAppear):F0} ms " +
					$"(request@mono={_tlStart * 1000 / System.Diagnostics.Stopwatch.Frequency})");
			}
			QtHostDiag.Trace(QtHostDiagChannel.Navigation, $"'{TitleOf(page)}' → SendAppearing (native pages [{string.Join(",", _nativePageIds)}])");
		}
	}

	/// <summary>The header text: the page Title, else the ShellContent/ShellSection title (like in-box Shell
	/// toolbars), else the type name.</summary>
	private static string TitleOf(Page page)
	{
		if (!string.IsNullOrEmpty(page.Title))
			return page.Title;
		for (Element? e = page.Parent; e is not null and not Shell; e = e.Parent)
			if (e is BaseShellItem { Title: { Length: > 0 } shellTitle })
				return shellTitle;
		return page.GetType().Name;
	}

	/// <summary>Forces a reconcile pass now (Qt thread only).</summary>
	public void Render()
	{
		if (ImageTrace)
			QtHostRuntime.Eval("window.mauiImageTrace=true");
		if (SailfishEnv.Get("MAUI_SAILFISH_LIST_PREFETCH") is { Length: > 0 } prefetch &&
		    double.TryParse(prefetch, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var viewports) &&
		    viewports > 0)
			QtHostRuntime.Eval("window.mauiListPrefetch=" + viewports.ToString(System.Globalization.CultureInfo.InvariantCulture));
		Reconcile();
	}

	/// <summary>MAUI_SAILFISH_IMAGE_TRACE=1 logs every image load (ms to Ready, on screen when it arrived).</summary>
	private static readonly bool ImageTrace = SailfishEnv.Flag("MAUI_SAILFISH_IMAGE_TRACE");

	/// <summary>
	/// Property-push entry point for MAUI handler mappers: the same diffed apply plus a Qt-thread hop, since
	/// Handler.UpdateValue runs on whichever thread wrote the property.
	/// </summary>
	/// <param name="yieldToNative">Skip while native state is written back into MAUI (the value came from native).</param>
	internal void PushHostProps(NativeElementHost host, Dictionary<string, object?> want, bool yieldToNative = false)
	{
		// Trace distinguishes the mapper path from the reconcile diff.
		void Apply()
		{
			if (yieldToNative && _suppressPush > 0)
				return;
			if (!host.IsAttached && host.Element is VisualElement flat && IsFlattened(flat))
			{
				OnFlattenedPush(flat);   // F4a: a row layout without a host may need one now
				return;
			}
			_handlerSnapshots++;
			host.HandlerSnapshots++;
			var changed = ApplyUpdates(host, want);
			_handlerPropertyPushes += changed;
			if (changed > 0)
				QtHostDiag.Trace(QtHostDiagChannel.QmlProperty, $"handler push {host} changed={changed}");
		}
		QtHostRuntime.RunOnQtThread(Apply);
	}

	/// <summary>Applies one host's property diff in place as a single typed, suppressed batch; returns the
	/// number of changed properties pushed.</summary>
	/// <param name="traceAs">Traces the pushed names under this source (the reconcile diff should push none).</param>
	private int ApplyUpdates(NativeElementHost host, Dictionary<string, object?> want, string? traceAs = null)
	{
		if (!host.IsAttached)
			return 0;
		List<(string Name, string ValueJson)>? changed = null;
		foreach (var kv in want)
		{
			// The diff basis is the serialized bridge JSON.
			var json = BridgeValue.Serialize(kv.Value);
			if (host.IsApplied(kv.Key, json))
				continue;
			(changed ??= new List<(string, string)>()).Add((kv.Key, json));
		}
		if (changed is null)
			return 0;
		if (traceAs is not null && QtHostDiag.TraceEnabled)
			QtHostDiag.Trace(QtHostDiagChannel.QmlProperty, $"{traceAs} push {host} {host.Element.GetType().Name}: {string.Join(",", changed.Select(c => c.Name))}");
		return PushBatch(host, changed) ? changed.Count : 0;
	}

	private static Dictionary<string, object?> CreateOp(NativeElementHost host, Dictionary<string, object?> props,
	                                                   string parentId = "")
	{
		// Colors ride the op JSON as "#AARRGGBB": the QML create init assigns props directly to color-typed
		// properties, and a serialized Color object would fail that.
		Dictionary<string, object?>? normalized = null;
		foreach (var kv in props)
		{
			if (kv.Value is Color color)
			{
				normalized ??= new Dictionary<string, object?>(props);
				normalized[kv.Key] = BridgeValue.ColorString(color);
			}
		}
		var op = new Dictionary<string, object?>
		{
			["op"] = "create",
			["id"] = host.Id,
			["uri"] = host.QmlUri,
			["props"] = normalized ?? new Dictionary<string, object?>(props),
		};
		// The host of the nearest hosted MAUI ancestor ("" = page canvas).
		if (parentId.Length > 0)
			op["parent"] = parentId;
		// QML instantiates the adapter file resolved from qml/adapters.json.
		if (QtHostAdapters.TryGetSrc(host.QmlUri, out var src))
			op["src"] = src;
		else
			QtHostDiag.Warn(QtHostDiagChannel.QmlLoad, $"no adapter src for uri '{host.QmlUri}' (fallback component)");
		return op;
	}

	/// <summary>
	/// Resolves the native handle of a freshly created host (objectName "maui_&lt;Id&gt;") and seeds the applied state.
	/// </summary>
	private static void AttachNative(NativeElementHost host, Dictionary<string, object?> props, long scopeHandle = 0)
	{
		// Collection rows re-create the same host id, and a ListView may keep a stale twin alive in a cached
		// delegate, so resolve inside the placeholder first.
		host.NativeHandle = scopeHandle != 0 ? QtHostRuntime.FindVisual(scopeHandle, $"maui_{host.Id}") : 0;
		if (host.NativeHandle == 0)
			host.NativeHandle = QtHostRuntime.FindObject($"maui_{host.Id}");
		if (host.NativeHandle == 0)
			// Hosts inside a ListView delegate hang in the visual tree only (the Qt 5.6 incubator never re-parents the
			// QObject chain), so findChild misses them; BFS childItems like QML's __mauiFindByName.
			host.NativeHandle = QtHostRuntime.FindVisual(0, $"maui_{host.Id}");
		host.AppliedProperties.Clear();
		host.AppliedGeometrySet = false;   // fresh QML object: geometry must be re-pushed
		host.AppliedVisible = true;
		foreach (var kv in props)
			host.AppliedProperties[kv.Key] = BridgeValue.Serialize(kv.Value);
		if (!host.IsAttached)
			QtHostDiag.Warn(QtHostDiagChannel.QmlObject, $"native handle not resolved for {host}");
		// Create props are plain QML assignments, so the shim's native QFont letter-spacing write never runs and the
		// seeded diff would suppress later pushes. Re-push through set_property (Qt 5.6 QML only has PercentageSpacing).
		if (host.IsAttached &&
		    props.TryGetValue("mauiLetterSpacing", out var spacing) &&
		    spacing is double spacingPx && spacingPx > 0)
			QtHostRuntime.SetProperty(host.NativeHandle, "mauiLetterSpacing",
				BridgeValue.Serialize(spacingPx));
		// Generic view props (background fill, semantics, automation id) are shim-side special cases too; the
		// defaults ("" and false) are what a fresh QML object already has.
		if (host.IsAttached)
			foreach (var key in GenericNativeKeys)
				if (props.TryGetValue(key, out var generic) && generic is not ("" or false))
					QtHostRuntime.SetProperty(host.NativeHandle, key, BridgeValue.Serialize(generic));
	}

	private static readonly string[] GenericNativeKeys =
		{ "mauiBackgroundFill", "mauiAccessibleName", "mauiAccessibleDescription", "mauiAutomationId", "mauiLayerShadow", "mauiLayerClip",
		  "mauiAccessibleRole", "mauiAccessibleIgnored", "mauiMirrored" };

	/// <summary>
	/// Deterministic destroy: shim deleteLater through the handle (revoking it in the QPointer registry), then
	/// drop the handle and property state.
	/// </summary>
	private static void DetachNative(NativeElementHost host)
	{
		if (host.NativeHandle != 0)
			QtHostRuntime.DestroyObject(host.NativeHandle);
		host.NativeHandle = 0;
		host.AppliedProperties.Clear();
	}

	/// <summary>Managed side of a destroy: property subscription off, native handle released.</summary>
	private static void ReleaseHost(NativeElementHost host)
	{
		DetachNative(host);
	}

	/// <summary>Frees the slot field a synthetic host (pulley, push-up menu, context menu, interaction) sat in.</summary>
	private void ReleaseSyntheticSlot(NativeElementHost host)
	{
		if (ReferenceEquals(host, _pullHost)) _pullHost = null;
		else if (ReferenceEquals(host, _pushHost)) _pushHost = null;
		else if (ReferenceEquals(host, _ctxMenuHost)) _ctxMenuHost = null;
		else _interactionHosts.Remove(host.Id);
	}

	/// <summary>
	/// Destroys <paramref name="hosts"/> in list order on the page instance that owns them (<paramref name="pageJs"/>,
	/// null = top model page) and releases their managed side; <paramref name="unroute"/> also drops event routes.
	/// </summary>
	internal void DestroyHosts(IReadOnlyList<NativeElementHost> hosts, string? pageJs, bool unroute = false)
	{
		if (hosts.Count == 0)
			return;
		ApplyOps(hosts.Select(h => BridgeOps.Destroy(h.Id)).ToList(), pageJs);
		foreach (var host in hosts)
		{
			ReleaseHost(host);
			if (unroute)
				_byId.Remove(host.Id);
		}
	}

	/// <summary>
	/// Self-heal: hosts attached to a model page object that Silica later rebuilt look attached, but every push
	/// dies silently. Probe liveness and drop the attachment so the next reconcile re-creates the host (rows
	/// re-materialize through <see cref="QtHostCollectionBridge.OnHostHealed"/>).
	/// </summary>
	internal bool HealIfDead(NativeElementHost host)
	{
		if (!host.IsAttached)
			return false;
		if (QtHostRuntime.TryItemGeometry(host.NativeHandle, out _))
			return false;   // handle alive: the failure was something else

		// Classify before OnHostHealed, which drops the host from its delegate/slot registry.
		var collectionCell = _collection.IsCollectionCellHost(host);
		host.NativeHandle = 0;   // no DestroyObject: the QML object is already gone
		host.AppliedProperties.Clear();
		host.AppliedGeometrySet = false;
		_current.Remove(host);   // next reconcile sees it as new → create op
		_collection.OnHostHealed(host);
		_layoutDirty = true;
		if (!collectionCell)
			RequestPoll();   // the next reconcile recreates it (a list's own resync re-materializes its cells)
		// Deaths come in bursts when Silica rebuilds a page: three per reconcile ask for a full-page rebuild.
		// Collection cells don't count, since ListView recycling kills them one at a time during normal scrolling.
		if (!collectionCell && ++_healedSinceReconcile >= 3)
			_fullResetPending = true;
		QtHostDiag.Warn(QtHostDiagChannel.Geometry,
			$"healed dead host {host} — QML object died before first window report; next reconcile recreates it");
		return true;
	}

	// --- Collection bridge integration: the same create/attach/route/geometry machinery for hosts inside
	// ListView delegates/slots ---

	internal NativeHostCache Cache => _cache;
	internal IMauiContext MauiContext => _mauiContext;

	/// <summary>Applies a bridge op batch on the top model page. <paramref name="targetJs"/> overrides the address
	/// for objects on another page instance (parked or popped); addressing them at the top would leak them.</summary>
	internal void ApplyOps(IReadOnlyList<Dictionary<string, object?>> ops, string? targetJs = null)
	{
		if (ops.Count == 0)
			return;
		var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
		var json = BridgeValue.Serialize(ops);
		var target = targetJs ?? TopModelPageJs;
		var expression = QmlPage.Call(target, "applyMauiOps", BridgeValue.Quote(json));
		var t1 = System.Diagnostics.Stopwatch.GetTimestamp();
		QtHostRuntime.Eval(expression);
		var t2 = System.Diagnostics.Stopwatch.GetTimestamp();
		_opsEvals++;
		NoteOps(ops);
		var t3 = System.Diagnostics.Stopwatch.GetTimestamp();
		static double Ms(long a, long b) => (b - a) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
		LastApplyOpsSplit = (Ms(t0, t1), Ms(t1, t2), Ms(t2, t3), expression.Length);
		LastOpsExpression = expression;
	}

	/// <summary>The last op batch expression (diagnostics replay).</summary>
	internal string LastOpsExpression { get; private set; } = string.Empty;

	/// <summary>The last op batch in ms: JSON + expression build, the eval, the trace note; and the expression length.</summary>
	public (double BuildMs, double EvalMs, double NoteMs, int Chars) LastApplyOpsSplit { get; private set; }

	private string _lastOps = string.Empty;   // "create:label:e12,order:…" of the last op batch (timer-poll trace)

	private void NoteOps(IReadOnlyList<Dictionary<string, object?>> ops)
	{
		if (!QtHostDiag.TraceEnabled)
			return;
		_lastOps = string.Join(",", ops.Take(6).Select(o =>
			$"{o.GetValueOrDefault("op")}:{o.GetValueOrDefault("uri") ?? string.Empty}:{o.GetValueOrDefault("id") ?? o.GetValueOrDefault("parent")}"))
			+ (ops.Count > 6 ? $",+{ops.Count - 6}" : string.Empty);
	}

	internal static Dictionary<string, object?> CreateChildOp(NativeElementHost host,
	                                                            Dictionary<string, object?> props, string parentObj)
	{
		// A row/slot root lands in the placeholder (parentObj, resolved by objectName); other hosts nest in their parent host.
		if (host.Parent is { } parent)
			return CreateOp(host, props, parent.Id);
		var op = CreateOp(host, props);
		op["parentObj"] = parentObj;   // MauiModelPage.__mauiFindByName re-parents the create
		return op;
	}

	/// <summary>Attaches a row/slot host; <paramref name="scopeHandle"/> scopes the name lookup to its placeholder.</summary>
	internal static void AttachHost(NativeElementHost host, Dictionary<string, object?> props, long scopeHandle = 0) =>
		AttachNative(host, props, scopeHandle);


	internal void RegisterRoute(string id, NativeElementHost host) => _byId[id] = host;

	/// <summary>Lays out one row/slot subtree inside its placeholder, rooted at the cell offset. Nothing depends
	/// on the delegate's scene position, so scrolling never re-pushes row geometry; row
	/// <see cref="NativeElementHost.MauiLogicalBounds"/> are delegate-relative.</summary>
	internal void PushItemGeometry(VisualElement root, double cellX, IReadOnlyCollection<NativeElementHost> hosts)
	{
		if (hosts.Count == 0)
			return;
		// The root's arranged position in its cell is its Margin (the cell is arranged at 0,0), as for the page root.
		var rootMatrix = QtHostVisualState
			.LocalTransform(root, root.Bounds.Width, root.Bounds.Height)
			.Then(Affine2.Translation(cellX + root.Bounds.X, root.Bounds.Y));
		CollectGeometry(root, rootMatrix, rootMatrix, hosts as HashSet<NativeElementHost> ?? new HashSet<NativeElementHost>(hosts),
			parentVisible: true, hitClip: null);
		FlushGeometry();
	}

	/// <summary>Maps one item template instantiation (root included) like the page Walk.</summary>
	internal void MapItemSubtree(VisualElement root, List<NativeElementHost> desired,
	                             Dictionary<NativeElementHost, Dictionary<string, object?>> props)
	{
		var first = desired.Count;
		_mappingRows = true;
		try
		{
			MapElement(root, desired, props);
			// The row root has no host parent: it lands in the delegate/slot placeholder.
			var rootHost = desired.Count > first && ReferenceEquals(desired[first].Element, root) ? desired[first] : null;
			if (rootHost is not null)
				rootHost.Parent = null;
			Walk(root, desired, props, parentHost: rootHost, nest: true);
		}
		finally
		{
			_mappingRows = false;
		}
		// Visual state and corner clips join row snapshots here, since rows never pass through the page reconcile.
		for (var i = first; i < desired.Count; i++)
		{
			if (!props.TryGetValue(desired[i], out var p))
				continue;
			if (desired[i].Element is VisualElement ve)
				QtHostVisualState.Merge(p, ve);
			// Row content isn't attached to the page tree, so the rendered page is the surround-colour fallback.
			if (desired[i].QmlUri == "image")
				QtHostClip.Merge(p, desired[i].Element as VisualElement, _rendered);
		}
	}

	/// <summary>
	/// Pre-order walk of the MAUI visual tree into the desired host list, keyed by element identity so hosts
	/// survive MAUI rebuilds. With <paramref name="nest"/> each host records its nearest hosted ancestor
	/// (<see cref="NativeElementHost.Parent"/>, null = page canvas), so the native tree mirrors MAUI's.
	/// </summary>
	private void Walk(IVisualTreeElement element, List<NativeElementHost> desired,
	                  Dictionary<NativeElementHost, Dictionary<string, object?>> props,
	                  NativeElementHost? parentHost = null, bool nest = true)
	{
		foreach (var child in element.GetVisualChildren())
		{
			// ContextFlyout registry (an attached property on FlyoutBase in .NET 11).
			if (child is View flyoutView &&
			    FlyoutBase.GetContextFlyout(flyoutView) is MenuFlyout flyout && flyout.Count > 0)
				_contextFlyouts[flyoutView] = flyout;

			var before = desired.Count;
			var walkChildren = child is not Element childElement || MapElement(childElement, desired, props);
			// The child's own host is the first one added for it; list/row bridges may append more.
			var childHost = desired.Count > before && ReferenceEquals(desired[before].Element, child)
				? desired[before]
				: null;
			if (nest && childHost is not null)
				childHost.Parent = parentHost;
			if (!walkChildren)
				continue;   // the collection bridge owns the subtree

			Walk(child, desired, props, childHost ?? parentHost, nest);
		}
	}

	/// <summary>
	/// Maps one element to its adapter host. The element's handler chooses the adapter and supplies its state, as a
	/// MAUI handler creates its native view (<see cref="HostingOf"/>); the cases here only add what needs the page
	/// around the element (collections, the refresh surface, the page's main scroll, image placeholders). Returns false
	/// when the page reconcile must not walk the subtree (a collection's items are materialized per delegate by
	/// <see cref="QtHostCollectionBridge"/>, a WebView or a drawn IndicatorView has no MAUI children to host).
	/// </summary>
	private bool MapElement(Element child, List<NativeElementHost> desired,
	                        Dictionary<NativeElementHost, Dictionary<string, object?>> props)
	{
		switch (child)
		{
			case CollectionView collection:
				// Native virtualized ListView: the bridge registers the host and materializes item trees per delegate.
				_collection.RegisterList(collection, desired, props);
				return false;
			case CarouselView carousel:
				// The same bridge in carousel mode: one snapped page per item (Position/CurrentItem sync).
				_collection.RegisterList(carousel, desired, props);
				return false;
			case ScrollView rowScroll when _mappingRows:
			{
				// In collection rows the ListView delegate is the scroller, so a template ScrollView is a plain container.
				var host = _cache.GetOrAdd(rowScroll, "content-view");
				props[host] = ContainerProps(rowScroll);
				desired.Add(host);
				return true;
			}
			case ScrollView scrollView:
			{
				// Every ScrollView is its own SilicaFlickable host whose content scrolls natively (ScrollX/ScrollY sync both
				// ways); Silica pulleys clone onto it like onto a hosted list.
				var host = _cache.GetOrAdd(scrollView, "scroll-view");
				props[host] = ScrollProps(scrollView);
				if (_primaryScrollWalk is null && scrollView.Orientation == ScrollOrientation.Vertical)
					_primaryScrollWalk = scrollView;
				// The first ScrollView inside a RefreshView carries the pull gesture and spinner itself.
				if (_scrollRefreshWalk is null && RefreshAncestorOf(scrollView) is { } refresh)
				{
					_scrollRefreshWalk = refresh;
					_scrollRefreshHostWalk = host;
					foreach (var kv in RefreshSurfaceProps(refresh))
						props[host][kv.Key] = kv.Value;
				}
				desired.Add(host);
				return true;
			}
			case IImage image when HostingOf((View)child) is { State: null }:
			{
				// QtHostImages resolves no URL: an unresolvable file keeps a visible placeholder, a stream still being read
				// hosts nothing yet, and a null source hosts nothing (a placeholder would lock the element to a label host
				// in the first-URI-wins cache, so a later Source could never create the image host).
				if (image.Source is not null && !QtHostImages.IsPending(image.Source as ImageSource))
					AddPlaceholder(child, "[Image]", desired, props);
				else if (child.Parent is IView container)
					QtHostImages.WhenReady(image.Source as ImageSource, container, () => RequestSubtree(container));
				return true;
			}
			case Microsoft.Maui.Controls.Shapes.Shape or BoxView when HostingOf((View)child) is { State: null }:
				// A shape reduces to a path of its arranged size: no host before the first arrange, the poll retries.
				_skippedUnarranged = true;
				_awaitingArrange.Add(child);
				return true;
			case RefreshView refreshView:
				// RefreshView modifies a scroll surface (the wrapped list or flickable arms the gesture); it still owns a
				// plain container host for its content.
				_refreshWalk ??= refreshView;
				break;
		}
		if (child is not View view)
			return true;
		// F4a: in collection rows a layout that paints nothing gets no QML host of its own; its children go to the nearest
		// hosted ancestor (CollectGeometry folds its offset into their rects). A push that makes it paint remaps the row.
		if (_mappingRows && FlatRows && view is Grid or StackBase)
		{
			if (view.Handler is null)
				QtHostLayout.AttachHandlers(view, _mauiContext);
			if (CanFlatten(view))
			{
				_flatRowContainers.AddOrUpdate(view, s_flatMarker);
				return true;
			}
		}
		var hosting = HostingOf(view) ?? (Uri: "content-view", State: null, WalksChildren: true);
		if (hosting.Uri == "content-view" && view is not (Layout or TemplatedView or IContentView) &&
		    view.Handler is not Handlers.ISailfishAdapterHandler { AdapterUri: not null } && _unsupportedWarned.Add(view.GetType()))
			QtHostDiag.Warn(QtHostDiagChannel.QtHost,
				$"no Sailfish adapter for {view.GetType().FullName} — rendered as an empty container (its children still paint)");
		var viewHost = _cache.GetOrAdd(view, hosting.Uri);
		props[viewHost] = hosting.State ?? ContainerProps(view);
		desired.Add(viewHost);
		return hosting.WalksChildren;
	}

	/// <summary>F4a: transparent row layouts go without a host; MAUI_SAILFISH_FLAT_ROWS=0 gives every layout its host (A/B).</summary>
	internal static readonly bool FlatRows = Environment.GetEnvironmentVariable("MAUI_SAILFISH_FLAT_ROWS") != "0";
	private static readonly object s_flatMarker = new();
	private readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, object> _flatRowContainers = new();

	/// <summary>Row layouts mapped without a host (F4a).</summary>
	internal bool IsFlattened(VisualElement element) => _flatRowContainers.TryGetValue(element, out _);

	/// <summary>True when the layout paints and does nothing a QML item would carry: no fill, clip, shadow, transform,
	/// opacity, input, gesture, semantics or z-order of its own. Such a container is pure geometry.</summary>
	internal static bool CanFlatten(View view)
	{
		if (view is not ILayout layout || layout.ClipsToBounds || view.Clip is not null || view.Shadow is not null)
			return false;
		if (QtHostPaint.Background(view) is { Alpha: > 0 })
			return false;
		if (view.Opacity < 1 || !view.IsVisible || !view.IsEnabled || view.InputTransparent || view.ZIndex != 0 ||
		    view.GestureRecognizers.Count > 0 || FlyoutBase.GetContextFlyout(view) is not null)
			return false;
		if (view.Rotation != 0 || view.RotationX != 0 || view.RotationY != 0 || view.Scale != 1 || view.ScaleX != 1 ||
		    view.ScaleY != 1 || view.TranslationX != 0 || view.TranslationY != 0)
			return false;
		if (!string.IsNullOrEmpty(view.AutomationId) || SemanticProperties.GetDescription(view) is not null ||
		    SemanticProperties.GetHint(view) is not null || SemanticProperties.GetHeadingLevel(view) != SemanticHeadingLevel.None ||
		    AutomationProperties.GetIsInAccessibleTree(view) == false || AutomationProperties.GetExcludedWithChildren(view) == true)
			return false;
		// Only the plain container adapters; anything with an adapter of its own keeps it.
		return view.Handler is not Handlers.ISailfishAdapterHandler { AdapterUri: { } uri } ||
		       uri is "grid" or "stack-layout" or "content-view";
	}

	/// <summary>A handler pushed to a flattened row layout (it has no live host): when the layout now paints, the row
	/// is mapped again and the layout gets its host.</summary>
	private void OnFlattenedPush(VisualElement element)
	{
		if (element is not View view || CanFlatten(view))
			return;
		_flatRowContainers.Remove(element);
		_collection.RemapRowContaining(element);
	}

	/// <summary>The adapter the element's handler chose and its state (handlers attach on demand); null for a handler
	/// that is not a Sailfish one (the element then gets a plain container host).</summary>
	private (string Uri, Dictionary<string, object?>? State, bool WalksChildren)? HostingOf(View view)
	{
		if (view.Handler is null)
			QtHostLayout.AttachHandlers(view, _mauiContext);
		if (view.Handler is not Handlers.ISailfishAdapterHandler handler)
			return null;
		// A library adapter must be registered; an unknown URI would degrade to the fallback label.
		var uri = handler.AdapterUri is { } chosen && QtHostAdapters.TryGetSrc(chosen, out _) ? chosen : "content-view";
		return (uri, handler.AdapterState(), handler.WalksChildren);
	}

	private void AddPlaceholder(Element element, string text, List<NativeElementHost> desired,
	                            Dictionary<NativeElementHost, Dictionary<string, object?>> props)
	{
		var host = _cache.GetOrAdd(element, "label");
		props[host] = new Dictionary<string, object?> { ["text"] = text, ["mauiEmphasis"] = "secondary" };
		desired.Add(host);
	}
}
