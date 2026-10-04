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
internal sealed partial class QtHostPageRenderer
{
	/// <summary>No hard cap on hosts per page; past this many a one-time warning names the page.</summary>
	private const int LargePageHostWarning = 2000;
	private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Page, object> _largePageWarned = new();   // weak: a warned page may go
	private readonly HashSet<Type> _unsupportedWarned = new();
	private readonly HashSet<string> _missingImageWarned = new();
	private bool _mappingRows => _mappingRowsDepth > 0;   // MapElement runs for a collection row subtree
	private int _mappingRowsDepth;                       // a depth: a row remapped from inside a mapping nests (W1.5)

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

	/// <summary>The top model page of the confirmed native mirror, for the collection bridge (W3.2: it kept a copy).</summary>
	internal string? TopNativePageId => NativeTopPageId;

	private readonly Window _window;
	private readonly IMauiContext _mauiContext;
	private readonly NativeHostCache _cache;   // the session's: the same host objects the handlers hold
	private readonly List<NativeElementHost> _current = new(); // mirror of the QML __order
	private readonly Dictionary<string, NativeElementHost> _byId = new(); // event routing

	private bool _navStackBusy;                          // pageStack.busy at the last nav snapshot
	private bool _pushTransition;                        // that transition is a push (the native depth grew)
	private int _idleNativeDepth = -1;                   // native model pages at the last idle snapshot

	/// <summary>
	/// A pushed page reconciles and lays out during its slide-in instead of after it. Data an app sets once the page
	/// is up (OnAppearing, a view model's first load) otherwise reached QML as property pushes while measure and
	/// arrange waited for the transition's end: Kitchen's recipe title drew on one overflowing line under the photo
	/// for 0.4 s, then the page jumped into place. <c>MAUI_SAILFISH_PUSH_LAYOUT=0</c> holds it as before (A/B).
	/// </summary>
	internal static bool PushTransitionRenders { get; set; } = SailfishEnv.Get("MAUI_SAILFISH_PUSH_LAYOUT") != "0";

	/// <summary>A transition that holds the page's passes: any pop (its outgoing page's hosts are dying), and a push
	/// when <see cref="PushTransitionRenders"/> is off.</summary>
	private bool TransitionHolds => _navStackBusy && !_pushTransition;
	private bool _strayScanPending;                      // a native pop asked for a stray sweep of the returned-to page
	private readonly QtHostCollectionBridge _collection;                  // CollectionView ⇄ ListView bridge
	private readonly HashSet<string> _bridgeFailLogged = new();            // error-report rate limit
	private readonly HashSet<string> _transformLimitWarned = new();        // one-time best-effort transform warnings
	private int _suppressPush;  // >0 while a native event is written back into MAUI

	/// <summary>Scope for writing native state into MAUI without PropertyChanged pushing it straight back.</summary>
	internal SuppressScope SuppressPush()
	{
		_suppressPush++;
		return new SuppressScope(this);
	}

	internal readonly struct SuppressScope(QtHostPageRenderer owner) : IDisposable
	{
		public void Dispose() => owner._suppressPush--;
	}

	/// <summary>Resolves an adapter event's {"id"} to its live host.</summary>
	internal bool TryResolveHost(JsonElement root, out string? id,
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
	private string _renderedBack = string.Empty;           // back navigation allowed, last pushed
	private string _renderedOrientations = string.Empty;   // SailfishPage.AllowedOrientations, last pushed
	private string _renderedScheme = string.Empty;         // palette color scheme, last pushed
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
	private readonly HashSet<string> _unknownOpsLogged = new(StringComparer.Ordinal);   // pages whose batch had unknown ops
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
	// Interaction hosts (docked panel, drawer) declared by the app: the declaration (id → adapter uri) outlives the
	// page it was first rendered on; the NativeElementHost is page-scoped and recreated by the next reconcile when
	// a push dropped it (RetainOutgoingPage) or its QML object died.
	private readonly Dictionary<string, string> _interactionUris = new();
	private readonly Dictionary<string, NativeElementHost> _interactionHosts = new();
	private readonly Dictionary<string, Dictionary<string, object?>> _interactionProps = new();
	private int _interactionSeq;
	private TaskCompletionSource<object?>? _dialogTcs;   // alert(bool) / prompt(string?) / sheet(string)
	private string _sheetCancel = string.Empty;          // action-sheet dismiss → cancel text

	// --- Native navigation: one MauiModelPage per MAUI page on the Silica pageStack ---
	// Sync runs both ways. Single-level push/pop animate like native Silica once the back cache is armed;
	// multi-level syncs use PageStackAction.Immediate (initialPage/animatorPush caveat: docs/architecture.md).
	// Only the top page is reconciled; the outgoing page's hosts are parked in a one-level back cache so a
	// pop-back repaints from live objects.
	/// <summary>MAUI ⇄ pageStack operations, one at a time; owns the confirmed native stack.</summary>
	private readonly NativeStackCoordinator _stack;
	private List<string> _nativePageIds => _stack.Mirror;   // mirror of MauiShell.mauiPages
	private NavOperation? _navOp => _stack.Operation;
	private int _nativePageSeq;                              // "mp<N>" id generator (mp1 = shell root)
	private bool _navStateAdopted;                           // first registry read adopted
	private readonly ActivationGate _activation;             // window lifecycle bridge + the creation wait (W3.7a)
	private Page? _pendingAppearing;                         // SendAppearing after the create batch
	private long _navOpSeq;                                  // monotonic id for the navigation operation log

	/// <summary>Acceptance-harness fault injection (one-shot): the next native push/pop reports rejection
	/// without touching the pageStack, so the rollback paths run on device. Counted in <see cref="NativeOpFailures"/>.</summary>
	internal bool FaultNextPush;
	internal bool FaultNextPop;
	private string _topModelPageId = string.Empty;           // registry top — the page the hosts live on
	private bool _nativeTopUnfollowed;   // the last nav read showed a top the mirror has not followed (a back gesture)
	private bool _fullResetPending;                          // a dead host asks for a whole-page rebuild
	private int _fullResetsDone;                             // per-page cap on full rebuilds (loop guard)
	private int _healedSinceReconcile;                       // dead-host burst counter (per reconcile)
	private long _resetHoldUntilMs;                          // creation held after a full-page reset (deferred QML deletes)

	/// <summary>How long the application must stay Active before hosts are created: Silica rebuilds model pages in
	/// the activation, and objects created meanwhile die. Tests set 0.</summary>
	internal static int ActivationSettleMs { get; set; } = 250;

	/// <summary>The application is stably Active (see <see cref="ActivationSettleMs"/>).</summary>
	private bool ActivationSettled => _activation.Settled;

	/// <summary>Runs a kicked poll in <paramref name="ms"/> (see <see cref="RenderScheduler.KickIn"/>).</summary>
	private void KickIn(long ms) => _scheduler.KickIn(ms);

	/// <summary>True while host creation is deferred: before the first window report or until the app is active.
	/// Silica rebuilds model-page visuals around activation and hosts created earlier die en masse (which has
	/// crashed QV4); the collection bridge uses the same gate.</summary>
	internal bool CreationDeferred => !_windowGeometryKnown || !_activation.AllowsCreation;

	/// <summary>Property values pushed through the bridge.</summary>
	public long BridgeApplied { get; private set; }

	/// <summary>Property pushes rejected or failed by the shim.</summary>
	public long BridgeFailed { get; private set; }

	/// <summary>Adapter events written back into MAUI.</summary>
	public long NativeEventsDelivered => Events.NativeEventsDelivered;

	/// <summary>Adapter event echoes dropped by change suppression.</summary>
	public long NativeEventsSuppressed => Events.NativeEventsSuppressed;

	/// <summary>Geometry entries applied to native items.</summary>
	public long GeometryApplied { get; private set; }

	/// <summary>Geometry entries rejected by the shim.</summary>
	public long GeometryFailed { get; private set; }

	/// <summary>Adapter "focus-changed" events that drove VisualElement.Focus/Unfocus.</summary>
	public long FocusTransitions => Events.FocusTransitions;

	/// <summary>Entry/Editor completions fired from adapter "completed" events (hardware or VKB Return).</summary>
	public long CompletedFired => Events.CompletedFired;

	/// <summary>Native caret/selection reports written back into InputView.CursorPosition/SelectionLength.</summary>
	public long CursorWriteBacks => Events.CursorWriteBacks;

	/// <summary>Native scroll reports written back into ScrollView.ScrollY.</summary>
	public long ScrollWriteBacks => Events.ScrollWriteBacks;

	/// <summary>ContextMenu item activations delivered to MAUI MenuFlyoutItems.</summary>
	public long ContextMenuActivations { get; private set; }

	/// <summary>Pulley-menu item activations delivered to MAUI ToolbarItems.</summary>
	public long ToolbarActivations { get; private set; }

	/// <summary>Native DockedPanel open-state changes reported over the bridge.</summary>
	public long PanelOpenChanges => Events.PanelOpenChanges;


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
	private long _tlStart, _tlNative, _tlHosts, _tlLayout, _tlAppear;
	private int _tlHostCount;
	private double _tlOpsEvalMs;
	private double _lastOpsEvalMs;   // the last reconcile's applyMauiOps eval (slow-poll diagnostics)
	/// <summary>Hosts created per pass when content grows on a shown page (see ReconcileCore).</summary>
	internal static int CreateChunk { get; set; } = Math.Max(1, SailfishEnv.Int("MAUI_SAILFISH_CREATE_CHUNK") ?? 24);
	/// <summary>The first chunk of a page being switched to: its visible top.</summary>
	internal static int CreateFirstChunk { get; set; } = Math.Max(CreateChunk, SailfishEnv.Int("MAUI_SAILFISH_CREATE_FIRST_CHUNK") ?? 96);
	private bool _createDeferred;   // this pass left hosts for the next one
	/// <summary>Diagnostics: the last pass left hosts for a later chunk, so the page is not fully created yet.</summary>
	internal bool CreationPending => _createDeferred;
	private int _lastOpsCreated;
	private string _tlKind = string.Empty;

	/// <summary>When the passes run: requests are latched there and reach the loop once.</summary>
	private readonly RenderScheduler _scheduler;

	/// <summary>The navigation handler saw a MAUI push/pop request (any thread).</summary>
	internal void NoteNavigationRequest() => _scheduler.NoteNavigationRequest();

	/// <summary>Runs the navigation sync + reconcile on the next loop turn instead of at the next heartbeat; repeated
	/// requests before it runs collapse into one (any thread).</summary>
	internal void RequestPoll() => _scheduler.RequestPoll();

	/// <summary>Managed push/pop animate like native Silica navigation; MAUI_SAILFISH_QT_HOST_NAV_ANIMATION=0
	/// restores Immediate transitions.</summary>
	internal static bool NavAnimation { get; set; } = !string.Equals(
		SailfishEnv.Get("MAUI_SAILFISH_QT_HOST_NAV_ANIMATION"), "0", StringComparison.Ordinal);

	/// <summary>Set by the host loop: queues one <see cref="KickedPoll"/> on the Qt thread. Null until the loop runs
	/// (requests before it are dropped); tests set their own.</summary>
	internal Action? PollKick
	{
		get => _scheduler.Kick;
		set => _scheduler.Kick = value;
	}

	/// <summary>The kicked poll (see <see cref="RequestPoll"/>).</summary>
	internal void KickedPoll()
	{
		_scheduler.PollStarted();
		PollCore(kicked: true);
	}

	/// <summary>Milliseconds between two Stopwatch timestamps.</summary>
	private static double Ms(long from, long to) => (to - from) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

	/// <summary><see cref="Ms"/> for the navigation timeline, where 0 marks a step not reached (-1).</summary>
	private static double TlMs(long from, long to) => from == 0 || to == 0 ? -1 : Ms(from, to);

	private void TimelineStart(string kind)
	{
		if (_tlStart != 0)
			return;
		var now = System.Diagnostics.Stopwatch.GetTimestamp();
		var request = _scheduler.TakeNavigationRequestTs();
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
	/// <summary>The shell reports the end of a pageStack transition and a native depth change (a back gesture): the
	/// sync runs then, not at the next heartbeat (MauiShell.qml). One renderer per window, so once per renderer.</summary>
	private void SubscribeNavigationEvents()
	{
		QtHostServices.Subscribe(ShellEvents.NavIdle, e =>
		{
			if (e.TryGetProperty("t", out var t) && t.TryGetInt64(out var ms))
				_navIdleWallMs = ms;
			if (NavIdleKick)
				RequestPoll();
		});
		QtHostServices.Subscribe(ShellEvents.NavDepth, _ =>
		{
			if (NavIdleKick)
				RequestPoll();
		});
		// Lifecycle (Resumed/Stopped/Activated) and the activation gate follow the application state at once.
		QtHostServices.Subscribe(ShellEvents.AppState, _ => RequestPoll());
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
	public long LayoutRequests => _scheduler.LayoutRequests;

	/// <summary>_layoutDirty arms from native write-backs (text/scroll/date/time).</summary>
	public long LayoutDirtyFromWriteback => Events.LayoutDirtyFromWriteback;

	private System.Diagnostics.Stopwatch? _navStopwatch;

	/// <summary>Managed-driven pageStack pops (PopAsync/PopModal/PopToRoot/hardware Back).</summary>
	public long NativePops { get; private set; }

	/// <summary>Native-side pops (Silica back gesture) synced back into MAUI.</summary>
	public long NativePopSyncs => _stack.NativePopSyncs;

	/// <summary>Native stack mutations rejected by the pageStack. The mirror is rolled back each time, so a
	/// non-zero value means the retry path ran, not that the stacks diverged.</summary>
	public long NativeOpFailures { get; private set; }

	/// <summary>Depth-sync pushes suppressed because a native→MAUI pop was still in flight (the pop-vs-push race).</summary>
	public long NativePopRacesBlocked => _stack.NativePopRacesBlocked;

	/// <summary>Navigation operations the native stack confirmed.</summary>
	public long NavOpsCompleted => _stack.NavOpsCompleted;

	/// <summary>Operations that timed out or failed verification (each followed by a resync).</summary>
	public long NavOpsFailed => _stack.NavOpsFailed;

	/// <summary>Times the coordinator adopted a native stack no operation explained.</summary>
	public long NavResyncs => _stack.NavResyncs;




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
	public int LastAppState => _activation.LastAppState;
	public bool? LastWindowActive => _activation.LastWindowActive;

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
		// The window's session exists before the renderer (handlers connect first); a context without one (a bare
		// service provider in a test) gets its own.
		var session = SailfishRenderSession.Of(mauiContext.Services) ?? new SailfishRenderSession();
		session.Renderer = this;
		_cache = session.Cache;
		_scheduler = new RenderScheduler(RunRequestedLayout, RunRequestedGeometry, RunSubtreeReconciles);
		_stack = new NativeStackCoordinator(this);
		_activation = new ActivationGate(RaiseWindowLifecycle);
		_pageCache = new PageCache(this);
		_collection = new QtHostCollectionBridge(this);
		// View children arrive through their layout handler (Add/Insert/Remove) or a content mapper, which reconcile
		// right away; pages (a Shell section, a tab, a modal) still announce themselves through the window.
		window.DescendantAdded += (_, e) => { if (e.Element is Page) RequestPoll(); };
		window.DescendantRemoved += (_, e) => { if (e.Element is Page) RequestPoll(); };
		SubscribeNavigationEvents();
	}

	/// <summary>The CollectionView ⇄ native ListView bridge.</summary>
	internal QtHostCollectionBridge Collection => _collection;

	// Adapter events (QML → MAUI write-backs) are decoded by the router; these are the renderer's entry points.
	private AdapterEventRouter? _events;
	internal AdapterEventRouter Events => _events ??= new AdapterEventRouter(this);

	/// <summary>Delivers a QML tap ({"id"}) to the mapped MAUI button or image button.</summary>
	public void HandleTap(string payload) => Events.HandleTap(payload);

	/// <summary>Routes a semantic adapter event to the mapped MAUI element (main thread). Handler failures are logged,
	/// never rethrown toward the Qt loop.</summary>
	public void HandleNativeEvent(string name, string payload) => Events.Handle(name, payload);


	/// <summary>Asks for a layout pass on the next loop turn (e.g. a stream image became ready; any thread).</summary>
	internal void InvalidateLayout() => QtHostRuntime.RunOnQtThread(RequestLayout);

	/// <summary>A native write-back may change measured sizes: the next pass lays out.</summary>
	internal void MarkLayoutDirty() => _layoutDirty = true;

	/// <summary>The live host registered under <paramref name="id"/>.</summary>
	internal bool TryGetHost(string id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out NativeElementHost? host) =>
		_byId.TryGetValue(id, out host);

	/// <summary>A fresh navigation timeline (a tab switch): the last one stays open when its page builds no list rows.</summary>
	internal void RestartTimeline(string kind)
	{
		_tlStart = 0;
		TimelineStart(kind);
	}

	/// <summary>The RefreshView a pull on the hosted scroll view refreshes, and the page-armed one.</summary>
	internal RefreshView? ScrollRefreshView => _scrollRefresh.View;
	internal RefreshView? PageRefreshView => _pageRefresh.View;

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

	/// <summary>How long creation waits after a full-page reset for QML's deferred deletes.</summary>
	private const int ResetHoldMs = 300;

	/// <summary>The heartbeat poll interval (ms) when MAUI_SAILFISH_POLL_MS is unset.</summary>
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
		_stack.PopUnsynced = false;
		_reconciledThisPoll = false;
		SyncNativeNavigation();
		CompleteSettledNavigation();
		var t1 = t0 != 0 ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
		// At most one pass per poll (W1.4): a pop already reconciled from inside the sync, to paint the returned-to page
		// before it is revealed; what the pop left for later (the stray sweep) runs on the poll it requested.
		if (CanReconcile && !_reconciledThisPoll)
			Reconcile();
		var t2 = t0 != 0 ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
		// Deferred row builds, slots, resync; the heartbeat leaves scheduled list work to the lists' own clock, so
		// what it does here is work no list scheduled.
		if (kicked || !_collection.PendingScheduled)
			_collection.ProcessPending();
		if (t0 != 0)
		{
			var t3 = System.Diagnostics.Stopwatch.GetTimestamp();
			if (Ms(t0, t3) >= SailfishRuntime.SlowWorkMs)
				Console.Error.WriteLine($"[Sailfish][SLOW] poll {(kicked ? "kicked" : "timer")} {Ms(t0, t3):F0} ms: " +
					$"nav {Ms(t0, t1):F0} reconcile {Ms(t1, t2):F0} (last {LastReconcileMs:F0}; QML create {_lastOpsEvalMs:F0} ms for {_lastOpsCreated} hosts, layout {LastLayoutSplit.MeasureArrangeMs:F0}+{LastLayoutSplit.CollectMs:F0}+{LastLayoutSplit.FlushMs:F0}) rows {Ms(t2, t3):F0} " +
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
			// What only the heartbeat did: work no event scheduled (target: none).
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
				Observe(PopModalTopAsync(), "hardware Back → PopModalAsync");
				return true;
			default:
				QtHostDiag.Trace(QtHostDiagChannel.Navigation, $"hardware Back → MAUI PopAsync ({target})");
				Observe(nav!(), "hardware Back → PopAsync");
				return true;
		}
	}

	/// <summary>A navigation task nothing awaits: its failure is logged instead of going unobserved.</summary>
	private static void Observe(Task task, string what) =>
		task.ContinueWith(t => QtHostDiag.Error(QtHostDiagChannel.Navigation, $"{what} failed: {t.Exception?.GetBaseException()}"),
			CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

	/// <summary>The one condition every reconcile outside a pass of its own waits for: no animated transition (its
	/// geometry flush would count the dying page's hosts dead), no native pop MAUI has not followed (it would flash
	/// the old page), and a page to render.</summary>
	private bool CanReconcile => !TransitionHolds && !_stack.PopUnsynced && !MauiFollowPending && ResolveReconcilePage() is not null;

	/// <summary>MAUI has not finished following a native pop. The poll clears PopUnsynced and only the coordinator's
	/// Step sets it again; with a dialog or flyout open SyncNativeNavigation returns before Step, so the operation is
	/// asked directly (W1.2): until MAUI pops, its page is the one the user backed out of.</summary>
	private bool MauiFollowPending => _navOp is { Kind: NavOpKind.FollowNative, MauiDone: false };

	/// <summary>
	/// The one gate of the posted layout and geometry passes (W1.3): the reconcile's (no holding transition, MAUI in
	/// step with the native stack) plus what a pass outside the reconcile must also respect: the native top followed,
	/// no full-page reset pending or holding, creation not deferred. A pass in that window flushed MAUI's geometry onto
	/// the hosts of the page being left, dead on the device, and the heal reset the whole page. A skipped pass leaves
	/// its dirty flag for the next poll's reconcile.
	/// </summary>
	private bool PassesAllowed =>
		!TransitionHolds && !_stack.PopUnsynced && !MauiFollowPending && !_nativeTopUnfollowed &&
		!_fullResetPending && _resetHoldUntilMs <= Environment.TickCount64 && !CreationDeferred;

	// --- Layout requests ---
	// As a native view requests a layout pass, a handler asks for one: MAUI's InvalidateMeasure reaches it through
	// Handler.Invoke, and the geometry keys (transforms, visibility, flow direction, scroll position) through its
	// mapper. Requests coalesce into one pass on the next loop turn; the pass's own arrange writes are not requests.

	private bool _inLayoutPass;   // RunLayoutPass is running: its Bounds writes invalidate nothing

	/// <summary>Row building: its item views (logical children of their list) re-measure as they are built; the list's
	/// own size follows from the finished rows (QtHostListAdapter.PushRows), so those requests are not passes.</summary>
	private int _layoutRequestHold;

	/// <summary>Holds layout requests for a <c>using</c> (row building; W4: replaces the public counter).</summary>
	internal LayoutHoldToken HoldLayoutRequests()
	{
		_layoutRequestHold++;
		return new LayoutHoldToken(this);
	}

	internal readonly struct LayoutHoldToken(QtHostPageRenderer renderer) : IDisposable
	{
		public void Dispose() => renderer._layoutRequestHold--;
	}

	/// <summary>Asks for a layout + geometry pass on the next loop turn (any thread).</summary>
	internal void RequestLayout()
	{
		if (_inLayoutPass || _layoutRequestHold > 0)
			return;
		_layoutDirty = true;
		_scheduler.RequestLayout();
	}

	private void RunRequestedLayout()
	{
		// The reconcile lays out itself; during a transition or before the first frame the next poll does.
		if (!_layoutDirty || !PassesAllowed || _rendered is not { } page)
			return;
		RunLayoutPass(page);
	}

	private bool _geometryDirty;    // root rects are stale (a scroll moved) but MAUI's layout is not

	/// <summary>A ScrollView's position moved (a native flick or ScrollX/ScrollY): MAUI's layout does not depend on it,
	/// only the content's root rects do (hit-testing, the viewport clip). Queues a geometry pass without measure and
	/// arrange (a full layout pass per native scroll report cost 100+ ms of the GUI thread while flicking).</summary>
	internal void RequestScrollGeometry()
	{
		_geometryDirty = true;
		if (_inLayoutPass)
			return;
		_scheduler.RequestGeometry();
	}

	private void RunRequestedGeometry()
	{
		if (!_geometryDirty || !PassesAllowed || _rendered is not { } page || !_windowGeometryKnown)
			return;
		if (_layoutDirty)
		{
			RunLayoutPass(page);   // a real layout is due anyway; it collects the geometry too
			return;
		}
		RunGeometryPass(page);
	}

	/// <summary>The walk skipped a shape that is not arranged yet (retried after the layout pass).</summary>
	private bool _skippedUnarranged;
	private bool _unarrangedRetry;
	/// <summary>Passes that retried their unarranged shapes after the layout pass (diagnostics, tests).</summary>
	internal int UnarrangedRetries { get; private set; }
	private bool _createdInPass;   // this reconcile created hosts (new content, not only a navigation)

	/// <summary>Reconcile core: diffs the current page's logical tree against the persistent hosts and applies
	/// the minimal create/update/destroy set (Qt thread only), timed for diagnostics.</summary>
	private bool _reconciledThisPoll;   // a pass already ran in this poll (the pop path paints before the reveal)

	private void Reconcile()
	{
		// The pageStack shows another model page than the mirror's top (a back gesture's transition, not yet
		// followed): MAUI still renders the page being left, onto the mirror top whose objects are dying. A window
		// report in that gap (the revealed page's taller header) reconciled it, the dead handles reset the whole
		// page and the revealed one flashed empty with the old title. The next poll follows the stack first.
		if (_nativeTopUnfollowed)
		{
			_layoutDirty = true;
			RequestPoll();
			return;
		}
		_skippedUnarranged = false;
		_reconciledThisPoll = true;
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

	/// <summary>
	/// One pass from the MAUI page to its native hosts, in steps: the gate (a page, no reset hold), the walk (desired hosts
	/// and their props), the page switch, the creation gate (window geometry, activation), the page chrome ops, the
	/// host-tree diff, its op batch, then layout and Appearing.
	/// </summary>
	private void ReconcileCore()
	{
		if (!_adapterPreloadArmed && _current.Count > 0)
			ArmAdapterPreload();   // the first page is up: the rest of the adapters can load behind it
		if (BeginPass(out var handlerReported) is not { } page)
			return;
		var (desired, props, awaitedBefore) = WalkPage(page);
		var pageChanged = SwitchRenderedPage(page, desired, props, out var previousPage);
		if (!CreationAllowed())
			return;
		// Cleared only by a pass that gets to create: one stopped by the reset hold or the creation gate leaves the
		// earlier pass's deferred hosts pending (W1.7).
		_createDeferred = false;
		var ops = PageChromeOps(page, out var title);
		if (desired.Count > LargePageHostWarning && _largePageWarned.TryAdd(page, page))
			QtHostDiag.Warn(QtHostDiagChannel.QtHost,
				$"'{TitleOf(page)}' needs {desired.Count} hosts (> {LargePageHostWarning}) — consider a CollectionView for long content");

		// A tab, section or detail switch keeps the page switched away from, and brings the page switched to back.
		if (pageChanged)
			SwitchPageInPlace(previousPage, page);
		_pageCache.Reclaim(desired);

		var change = DiffHostTree(desired, props, awaitedBefore, pageChanged, ops);
		ApplyTreeChange(title, ops, change, props, pageChanged, handlerReported);
		FinishPass(page);
	}

	/// <summary>The page to reconcile, unless a dead-host burst's reset is still holding creation. Takes the subtree
	/// changes handlers reported (the full pass applies them).</summary>
	private Page? BeginPass(out bool handlerReported)
	{
		handlerReported = false;
		var page = ResolveReconcilePage();
		if (page is null)
		{
			QtHostDiag.Warn(QtHostDiagChannel.QtHost, "no MAUI page to render yet");
			return null;
		}

		_healedSinceReconcile = 0;
		handlerReported = TakePendingSubtrees();
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
			return null;
		}
		return page;
	}

	/// <summary>The walk: every desired host of the page in pre-order with its props (generic visual state and clip
	/// merged), the routing table, the stray sweep, and pull-to-refresh armed where it belongs.</summary>
	private (List<NativeElementHost> Desired, Dictionary<NativeElementHost, Dictionary<string, object?>> Props, HashSet<Element>? AwaitedBefore) WalkPage(Page page)
	{
		_byId.Clear();
		var desired = new List<NativeElementHost>();
		var props = new Dictionary<NativeElementHost, Dictionary<string, object?>>();
		_primaryScrollWalk = null;   // the main ScrollView is detected per reconcile
		_scrollRefreshWalk = null;
		_scrollRefreshHostWalk = null;
		_refreshWalk = null;
		_contextFlyouts.Clear();   // flyout registry follows the tree
		// The walk re-adds every shape still waiting for a size, so the set holds only shapes on the page as it is now.
		// Shapes the previous pass skipped go first in a chunked create (see below).
		var awaitedBefore = _awaitingArrange.Count == 0 ? null : new HashSet<Element>(_awaitingArrange);
		_awaitingArrange.Clear();
		Walk(page, desired, props);
		AddSyntheticHosts(page, desired, props);   // page-level surfaces
		foreach (var host in desired)
		{
			_byId[host.Id] = host;   // event routing table (ids follow the reconcile)
			MergeGenericState(host, props, page);
		}
		_collection.ContributeRouting(_byId);   // item/slot child ids join the table
		if (_strayScanPending)
		{
			_strayScanPending = false;
			// Parked hosts are known too: a Shell's other tab pages wait hidden on this same model page (Profitocracy's
			// Home lost all 78 hosts here after a back gesture on Settings, and the next tab switch reset the page).
			var known = BridgeValue.Serialize(_byId.Keys.Concat(_pageCache.ParkedHostIds)
				.Concat(_collection.PooledHostIds).Distinct().ToList());
			CallPage(null, "__destroyHostsNotIn", known);
		}
		// Page-level pull-to-refresh, unless a hosted list or scroll view consumes it or a page pulley owns the overscroll.
		ArmRefresh(_refreshWalk is not null && !_collection.ConsumesRefresh(_refreshWalk)
		           && !ReferenceEquals(_scrollRefreshWalk, _refreshWalk)
		           && !PageHasPulley
			? _refreshWalk
			: null);
		ArmScrollRefresh(_scrollRefreshWalk, _scrollRefreshHostWalk);
		return (desired, props, awaitedBefore);
	}

	/// <summary>Records the rendered page; on a switch re-arms the page-instance state and sends Disappearing to the
	/// previous page (Appearing follows once the new hosts are live).</summary>
	private bool SwitchRenderedPage(Page page, List<NativeElementHost> desired,
		Dictionary<NativeElementHost, Dictionary<string, object?>> props, out Page? previousPage)
	{
		var pageChanged = !ReferenceEquals(page, _rendered);
		// Trace button state on every page swap.
		if (pageChanged)
			foreach (var witnessHost in desired)
				if (witnessHost.Element is Button witnessButton && props.TryGetValue(witnessHost, out var witnessProps))
					QtHostDiag.Trace(QtHostDiagChannel.QtHost,
						$"button witness '{witnessButton.Text}' enabled={witnessProps.GetValueOrDefault("enabled")} color={witnessProps.GetValueOrDefault("mauiTextColor")} plate={witnessProps.GetValueOrDefault("mauiPlateColor")} " +
						$"(Background {witnessButton.Background?.GetType().Name ?? "null"} {QtHostPaint.Solid(witnessButton.Background)}, BackgroundColor {witnessButton.BackgroundColor}) on '{TitleOf(page)}'");
		previousPage = _rendered;
		_rendered = page;
		if (pageChanged)
			_renderedPageSeq++;   // diag trigger (MAUI_SAILFISH_OPEN_PULLEY page seq)
		if (pageChanged)
		{
			_fullResetsDone = 0;   // the rebuild cap is per page
			ReArmPageChrome();
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
		return pageChanged;
	}

	/// <summary>Whether hosts may be created now: the window geometry is known and the app is stably active (bounded,
	/// so a stuck sync cannot leave the app blank).</summary>
	private bool CreationAllowed()
	{
		// The gate is CreationDeferred (W3.5: this restated it); here it also keeps the deferral's bookkeeping.
		// Before the first window report Silica still rebuilds model pages and hosts created then keep dead handles.
		// Defer all creation until the geometry is known; ApplyWindowGeometry reconciles again.
		if (!_windowGeometryKnown)
			return false;

		// Until the app is active the Silica stack is in motion: hosts would attach to a model page about to be
		// replaced or die in the activation rebuild. Defer creation until ApplicationActive (or the native stack
		// catches up); bounded so a stuck sync can't leave the app blank.
		if (CreationDeferred)
		{
			if (_activation.Wait(out var recheckMs))
				QtHostDiag.Warn(QtHostDiagChannel.Navigation,
					$"activation: app not stably active yet (Qt.application.state={_activation.LastAppState}, MAUI depth {ExpectedNativeDepth()} vs native {_nativePageIds.Count}) — " +
					"deferring host creation until the Silica stack settles at activation");
			KickIn(recheckMs);
			return false;
		}
		if (_activation.EndWait(out var ticks, out var waitedMs))
			QtHostDiag.Trace(QtHostDiagChannel.Navigation,
				$"activation: native stack caught up after {ticks} deferred reconcile(s), {waitedMs} ms — creating the page now");
		return true;
	}

	/// <summary>The model page's own state as ops (title, busy, palette, back navigation, orientations, background);
	/// the tab bar goes by its own page call. Only what changed since the last pass.</summary>
	private List<Dictionary<string, object?>> PageChromeOps(Page page, out string title)
	{
		title = HeaderTitleOf(page);
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
		// UserAppTheme = Light under a dark ambience (Profitocracy's theme setting) put the app's light pages under a
		// header and tab row still drawn light-on-dark: unreadable. The page's palette follows the app's theme.
		var scheme = Application.Current?.RequestedTheme == AppTheme.Light ? "light" : "dark";
		if (scheme != _renderedScheme)
		{
			_renderedScheme = scheme;
			ops.Add(BridgeOps.Scheme(scheme == "light"));
		}
		var back = BackNavigationOf(page) ? "1" : "0";
		if (back != _renderedBack)
		{
			_renderedBack = back;
			ops.Add(BridgeOps.Back(back == "1"));
		}
		var orientations = (int)SailfishPage.Effective(page);
		var orientationsKey = orientations.ToString(System.Globalization.CultureInfo.InvariantCulture);
		if (orientationsKey != _renderedOrientations)
		{
			_renderedOrientations = orientationsKey;
			ops.Add(BridgeOps.Orientations(orientations));
		}

		// The tab bar (Shell tabs / TabbedPage) belongs to the model page instance; pushed when it changes.
		var tabs = ResolveTabs();
		var subTabs = ResolveSubTabs();
		var subJson = subTabs is { } st
			? ",\"sub\":{\"titles\":" + BridgeValue.Serialize(st.Titles) + ",\"index\":" + st.Index.ToString(CultureInfo.InvariantCulture) + "}"
			: string.Empty;
		var tabsJson = tabs is { } t
			? "{\"titles\":" + BridgeValue.Serialize(t.Titles) + ",\"index\":" + t.Index.ToString(CultureInfo.InvariantCulture) + subJson + "}"
			: "{\"titles\":[],\"index\":0" + subJson + "}";
		_tabSelect = tabs?.Select;
		_subTabSelect = subTabs?.Select;
		_tabIndex = tabs?.Index ?? 0;
		_tabCount = tabs?.Titles.Count ?? 0;
		if (tabsJson != _renderedTabs)
		{
			_renderedTabs = tabsJson;
			CallPage(null, "setMauiTabs", tabsJson);
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
		return ops;
	}

	/// <summary>The host tree's change from the current hosts to the desired ones: destroys (descendants first),
	/// re-parents, creates (chunked for a big page), in-place property updates and the child order, appended to
	/// <paramref name="ops"/>.</summary>
	private TreeChange DiffHostTree(List<NativeElementHost> desired, Dictionary<NativeElementHost, Dictionary<string, object?>> props,
		HashSet<Element>? awaitedBefore, bool pageChanged, List<Dictionary<string, object?>> ops)
	{
		var desiredSet = new HashSet<NativeElementHost>(desired);
		// Retire lists that left the tree, except those of the parked page: its rows must stay materialized for the
		// back gesture. DropRetention retires them later.
		_collection.SyncDesired(WithParked(desired));
		var currentSet = new HashSet<NativeElementHost>(_current);
		var survivors = _current.Where(desiredSet.Contains).ToList();       // in QML order
		var created = desired.Where(h => !currentSet.Contains(h)).ToList(); // in desired order
		// A big batch is created a chunk per frame instead of blocking (~0.7 ms of QML per host: a recipe's 117 hosts
		// stalled the UI thread ~140 ms), top first as Silica lists fill in. A page being switched to gets a larger
		// first chunk (what shows while it slides in); the rest, below the fold, follows once the transition ends.
		// The pre-order prefix keeps every parent ahead of its children; the rest waits for the next pass.
		// The unarranged retry (shapes that just got their size) adds only those shapes, with any ancestor not yet
		// created; a normal pass takes them ahead of its chunk, so a row's shape shows with the row.
		HashSet<NativeElementHost>? deferredCreate = null;
		var chunk = _unarrangedRetry ? 0 : pageChanged ? CreateFirstChunk : CreateChunk;
		if (created.Count > chunk)
		{
			var take = new HashSet<NativeElementHost>(created.Take(chunk));
			if (awaitedBefore is not null)
			{
				var createdSet = new HashSet<NativeElementHost>(created);
				foreach (var host in created)
					if (host.Element is Element element && awaitedBefore.Contains(element))
						for (var x = host; x is not null && createdSet.Contains(x) && take.Add(x); x = x.Parent) { }
			}
			if (take.Count < created.Count)
			{
				deferredCreate = created.Where(h => !take.Contains(h)).ToHashSet();
				created = created.Where(take.Contains).ToList();   // still pre-order
				_createDeferred = true;
			}
		}

		// The host tree's change as one op batch, after the page ops above (HostTreeDiff keeps the native child order).
		var diff = new HostTreeDiff(_current);

		// Destroy descendants before ancestors so no op addresses an item its parent's teardown already took.
		var destroyCount = _current.Count - survivors.Count;
		var destroyed = _current.Where(h => !desiredSet.Contains(h)).ToList();
		destroyed.Reverse();
		foreach (var host in destroyed)
			diff.Destroy(host);
		// The ops are in the diff's batch. A destroyed synthetic host drops its slot, so a reappearance gets a fresh
		// object (a stale diff would swallow the first push).
		ReleaseHosts(destroyed, pageId: null, sendOps: false);

		// A survivor that moved to another container keeps its QML object and is re-attached in the new parent;
		// the order ops below settle its position.
		foreach (var host in survivors)
			diff.Reparent(host);

		var createCount = created.Count;
		_createdInPass = createCount > 0;
		foreach (var host in created)
		{
			diff.Create(host, props.TryGetValue(host, out var p) ? p : EmptyProps, CreateOp);
			_current.Add(host);
		}

		// Update: property diff applied in place through the native handle.
		var updateCount = 0;
		foreach (var host in survivors)
		{
			var pushed = ApplyUpdates(host, props.TryGetValue(host, out var p) ? p : EmptyProps, "reconcile-diff");
			updateCount += pushed;
			// Synthetic surfaces (pulleys, panels) have no handler: the reconcile is their only channel.
			if (host.Element.Handler is Handlers.ISailfishViewHandler)
				_reconcileDiffPropertyPushes += pushed;
		}

		var reordered = diff.Order(desired);
		ops.AddRange(diff.Ops);
		// _current mirrors the desired pre-order (hit-test tie order: later = painted above).
		_current.Clear();
		_current.AddRange(deferredCreate is null ? desired : desired.Where(h => !deferredCreate.Contains(h)));
		if (deferredCreate is not null)
			KickIn(16);   // the next chunk, after a frame

		// Tree or property changes can move geometry.
		if (ops.Count > 0 || updateCount > 0)
			_layoutDirty = true;
		return new TreeChange(created, createCount, destroyCount, updateCount, reordered);
	}

	private readonly record struct TreeChange(List<NativeElementHost> Created, int CreateCount, int DestroyCount, int UpdateCount, bool Reordered);

	/// <summary>Sends the pass's op batch to the top model page and attaches the created objects.</summary>
	private void ApplyTreeChange(string title, List<Dictionary<string, object?>> ops, TreeChange change,
		Dictionary<NativeElementHost, Dictionary<string, object?>> props, bool pageChanged, bool handlerReported)
	{
		var (created, createCount, destroyCount, updateCount, reordered) = change;
		if (ops.Count > 0)
		{
			NoteTreeFixup(ops, pageChanged || createCount == _current.Count, handlerReported);
			foreach (var host in created)
				_awaitingArrange.Remove(host.Element);   // a skipped shape got its size and its host
			// A pulley created in this batch attaches to the page flickable only if it is already interactive;
			// otherwise it settles on a hosted list and is moved later, which Silica does not fully follow (the
			// push-up bar stayed unpainted). So the flickable state goes first, as in a Silica page's declaration.
			if (created.Any(h => ReferenceEquals(h, _pullHost) || ReferenceEquals(h, _pushHost)))
			{
				PushScrollState();
				_pulleyReattachPending = true;   // after the layout pass shows this page's hosts
			}
			// Addressed to the top model page by its id, through ApplyOps (W3.3: the main batch bypassed it, so
			// LastOpsExpression/LastApplyOpsSplit missed it and the page's unknown count went unread).
			var opsTs = System.Diagnostics.Stopwatch.GetTimestamp();
			var unknown = ApplyOps(ops);
			if (unknown > 0 && _unknownOpsLogged.Add(NativeTopPageId ?? string.Empty))
				QtHostDiag.Warn(QtHostDiagChannel.QmlObject,
					$"'{title}': the page could not apply {unknown} of {ops.Count} ops (ids it does not hold); logged once per page");
			if (_navIdleWallMs != 0)
			{
				// QML transition end → the next page content natively (the reconcile waits out the animation).
				var ms = (double)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _navIdleWallMs);
				_navIdleWallMs = 0;
				IdleToRenderCount++;
				IdleToRenderTotalMs += ms;
				QtHostDiag.Trace(QtHostDiagChannel.Navigation, $"NAV-IDLE→render {ms:F0} ms");
			}
			_lastOpsEvalMs = TlMs(opsTs, System.Diagnostics.Stopwatch.GetTimestamp());   // QML object creation
			_lastOpsCreated = created.Count;
			if (_tlStart != 0 && _tlHosts == 0)
				_tlOpsEvalMs = _lastOpsEvalMs;
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
	}

	/// <summary>The layout pass, the retry for shapes that just got their size, and Appearing once the page is live.</summary>
	private void FinishPass(Page page)
	{
		RunLayoutPass(page);   // MAUI layout + SetGeometry batch
		if (_tlStart != 0 && _tlLayout == 0 && (_tlHosts != 0 || _tlKind != "push"))
			_tlLayout = System.Diagnostics.Stopwatch.GetTimestamp();

		// Content reconciled before its first arrange (a navigation, or data that just arrived) skipped size-derived
		// shapes; create them now, in the same frame, instead of a poll later. One retry: a 0x0 shape stays skipped.
		if (_skippedUnarranged && !_unarrangedRetry && (_tlStart != 0 || _createdInPass))
		{
			// ReconcileCore, not Reconcile: this runs inside the outer pass, whose stopwatch and count cover it.
			_unarrangedRetry = true;
			UnarrangedRetries++;
			try
			{
				_skippedUnarranged = false;
				ReconcileCore();
			}
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

	/// <summary>The header of the page shown first, so the native page is created with it (no placeholder title
	/// before the first reconcile).</summary>
	internal string CurrentTitle => ResolveCurrentPage() is { } page ? HeaderTitleOf(page) : string.Empty;

	/// <summary>The page Title, else the ShellContent/ShellSection title (like in-box Shell toolbars); null when neither
	/// is set.</summary>
	private static string? ExplicitTitleOf(Page page)
	{
		if (!string.IsNullOrEmpty(page.Title))
			return page.Title;
		for (Element? e = page.Parent; e is not null and not Shell; e = e.Parent)
			if (e is BaseShellItem { Title: { Length: > 0 } shellTitle })
				return shellTitle;
		return null;
	}

	/// <summary>The page in traces and diagnostics: its title, else its type name.</summary>
	internal static string TitleOf(Page page) => ExplicitTitleOf(page) ?? page.GetType().Name;

	/// <summary>The PageHeader text: the page's title, else the app's name (ApplicationTitle), not the type name a
	/// bare `new Window(new MainPage())` would show. The header stays: Canvas-painted shapes on a page without a
	/// rendered PageHeader never reached the screen on the device (Jolla Phone, SFOS 5.2).</summary>
	internal static string HeaderTitleOf(Page page) => ExplicitTitleOf(page) ?? AppTitle.Value;

	/// <summary>Whether the Silica back gesture and indicator stay on: Shell's BackButtonBehavior IsVisible/IsEnabled
	/// false and NavigationPage.HasBackButton false remove the toolbar back button elsewhere, and Silica's back
	/// affordance is the gesture (a first-run modal must not be swiped away).</summary>
	internal static bool BackNavigationOf(Page page) =>
		Shell.GetBackButtonBehavior(page) is not { IsVisible: false } and not { IsEnabled: false } &&
		NavigationPage.GetHasBackButton(page);

	private static readonly Lazy<string> AppTitle = new(() => new SailfishAppInfo().Name);

	/// <summary>Forces a reconcile pass now (Qt thread only).</summary>
	public void Render()
	{
		if (ImageTrace)
			QtHostRuntime.Eval("window.mauiImageTrace=true");
		if (SailfishEnv.Flag("MAUI_SAILFISH_OPS_TIMING"))
			QtHostRuntime.Eval("window.mauiOpsTiming=true");
		if (SailfishEnv.Get("MAUI_SAILFISH_LIST_PREFETCH") is { Length: > 0 } prefetch &&
		    double.TryParse(prefetch, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var viewports) &&
		    viewports > 0)
			QtHostRuntime.Eval("window.mauiListPrefetch=" + viewports.ToString(System.Globalization.CultureInfo.InvariantCulture));
		Reconcile();
		// Behind the first page only: while creation is deferred (the activation gate) the preload would compete with
		// it; ReconcileCore arms it once the first hosts exist.
		if (!CreationDeferred)
			ArmAdapterPreload();
	}

	/// <summary>MAUI_SAILFISH_ADAPTER_PRELOAD=0 leaves adapters to load and warm up on first use (A/B). Measured on the
	/// Jolla phone 2026-10-02: a first push of the Controls page 54–61 → 44–45 ms to Appearing, the Kitchen detail
	/// 183–203 → 166–170 ms stall, the catalog that opens meanwhile unchanged.</summary>
	internal static readonly bool AdapterPreload = SailfishEnv.Get("MAUI_SAILFISH_ADAPTER_PRELOAD") != "0";
	private bool _adapterPreloadArmed;

	/// <summary>A second after the first page showed, the shell loads the visual adapters in the background and makes one
	/// throwaway instance of each (MauiShell.adapterPreload), so the first host of a kind on a later page is as cheap as
	/// the second. The most used adapters go first.</summary>
	private void ArmAdapterPreload()
	{
		if (_adapterPreloadArmed || !AdapterPreload || QtHostRuntime.TestShim is not null)
			return;
		if (Microsoft.Maui.Dispatching.Dispatcher.GetForCurrentThread() is not { } dispatcher)
			return;
		_adapterPreloadArmed = true;
		dispatcher.DispatchDelayed(TimeSpan.FromSeconds(1), () =>
		{
			string[] first = { "label", "button", "image", "border", "grid", "stack-layout", "content-view", "shape", "list-view", "scroll-view" };
			// Visual adapters only: an instance is made and dropped off screen, so nothing that attaches to the page
			// (pulleys, panels, dialogs), starts an engine (the web view: Gecko) or owns a native surface.
			string[] warm =
			{
				"entry", "editor", "switch", "slider", "progress-bar", "activity-indicator", "search-bar", "picker",
				"date-picker", "time-picker", "radio-button", "indicator-view", "stepper", "check-box", "swipe-view",
				"graphics-view", "carousel-view",
			};
			var sources = first.Concat(warm).Select(uri => QtHostAdapters.TryGetSrc(uri, out var src) ? src : null)
				.Where(src => !string.IsNullOrEmpty(src))
				.Distinct(StringComparer.Ordinal)
				.ToList();
			QtHostRuntime.Eval("window.mauiPreloadAdapters(" + BridgeValue.Quote(BridgeValue.Serialize(sources)) + ")");
			QtHostDiag.Trace(QtHostDiagChannel.QmlLoad, $"adapter preload: {sources.Count} adapters queued");
		});
	}

	/// <summary>MAUI_SAILFISH_IMAGE_TRACE=1 logs every image load (ms to Ready, on screen when it arrived).</summary>
	private static readonly bool ImageTrace = SailfishEnv.Flag("MAUI_SAILFISH_IMAGE_TRACE");

	private static readonly string[] GenericNativeKeys =
		{ "mauiBackgroundFill", "mauiAccessibleName", "mauiAccessibleDescription", "mauiAutomationId", "mauiLayerShadow", "mauiLayerClip",
		  "mauiAccessibleRole", "mauiAccessibleIgnored", "mauiMirrored" };

	// Animated pop: the popped page's hosts leave the managed mirror at once, but their native objects are released
	// only after the transition (the pageStack deletes the page with them; the shim then just forgets the handles).
	// This renderer's own state (W1.6: it was process-wide), flushed when the stack is idle again, when a pop fails
	// and when the mirror is resynced.
	private bool _deferNativeDestroy;
	private readonly List<long> _pendingNativeDestroys = new();

	// --- Collection bridge integration: the same create/attach/route/geometry machinery for hosts inside
	// ListView delegates/slots ---

	internal NativeHostCache Cache => _cache;
	internal IMauiContext MauiContext => _mauiContext;

	// --- Page calls: a model page's functions called directly (sailfish_host_invoke), by page id ---

	private readonly Dictionary<string, long> _pageHandles = new(StringComparer.Ordinal);

	/// <summary>Page calls made through sailfish_host_invoke, and those that fell back to an eval.</summary>
	public long PageInvokes { get; private set; }
	public long PageCallFallbacks { get; private set; }

	/// <summary>The last op batch (its JSON; diagnostics replay).</summary>
	internal string LastOpsExpression { get; private set; } = string.Empty;

	/// <summary>The last op batch in ms: JSON + expression build, the eval, the trace note; and the expression length.</summary>
	public (double BuildMs, double EvalMs, double NoteMs, int Chars) LastApplyOpsSplit { get; private set; }

	private string _lastOps = string.Empty;   // "create:label:e12,order:…" of the last op batch (timer-poll trace)

	/// <summary>Flat rows: transparent row layouts go without a host; MAUI_SAILFISH_FLAT_ROWS=0 gives every layout its host (A/B).</summary>
	internal static readonly bool FlatRows = SailfishEnv.Get("MAUI_SAILFISH_FLAT_ROWS") != "0";
	private static readonly object s_flatMarker = new();
	private readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, object> _flatRowContainers = new();


}
