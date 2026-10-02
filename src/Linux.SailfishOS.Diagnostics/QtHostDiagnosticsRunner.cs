using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.SailfishOS.Diagnostics;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Qt-host acceptance legs. <see cref="Attach"/> runs right before the Qt loop with the live renderer,
/// dispatcher and input router; each leg prints its own ACCEPTANCE line and the auto-shutdown ends the run.
/// </summary>
internal sealed partial class QtHostDiagnosticsRunner : IQtHostDiagnostics
{
	private QtHostDiagnosticsContext _context = null!;

	// Reconcile diag (MAUI_SAILFISH_QT_HOST_RECONCILE_DIAG=1): baseline lifecycle counters + a probe on a native label host.
	private int _qtBaselineCreated = -1;
	private int _qtBaselineDestroyed = -1;
	private long _qtProbeHandle;
	private string _qtProbeExpectText = string.Empty;

	// Bridge diag (MAUI_SAILFISH_QT_HOST_BRIDGE_DIAG=1): Switch state before the mutation; the injected tap must flip it back via event write-back.
	private bool _qtBridgeToggleBefore;
	private bool _qtBridgePushOk;          // leg verdicts, folded into the bridge ACCEPTANCE line
	private bool _qtBridgeSuppressionOk;
	private bool _qtBridgeWriteBackOk;

	// Geometry diag (MAUI_SAILFISH_QT_HOST_GEOMETRY_DIAG=1): MAUI logical bounds vs Qt scene readback.
	private bool _qtGeometryDiag;

	// Input diag (MAUI_SAILFISH_QT_HOST_INPUT_DIAG=1): injected Qt tap + drag at a MAUI gesture target, checks input-router ownership counters.
	private bool _qtInputDiag;
	private QtHost.QtHostInputRouter? _qtInputRouter;
	// Set when a native Entry/Editor was tapped; acceptance then also requires a focus-changed round trip.
	private bool _qtInputFocusLeg;

	// Text-input diag (MAUI_SAILFISH_QT_HOST_TEXT_DIAG=1): focus push to Maliit, injected keys, cursor/selection sync, completion, input modes, multiline Editor.
	private bool _qtTextDiag;

	// Page diag (MAUI_SAILFISH_QT_HOST_PAGE_DIAG=1): push a complete page (MAUI_SAILFISH_QT_HOST_PAGE_TYPE, default TextPage) and check
	// label semantics, geometry, scrolling both ways and the hardware-Back pop.
	private bool _qtPageDiag;
	private int _qtPageStage;   // 0 idle, 1 awaiting page render, 4 awaiting back-pop render
	private QtHost.NativeElementHost? _qtPageDeep;   // a host deep in the scrolled content
	private double _qtPageDeepSceneY;                // its scene Y (dp) before the scroll
	private string _qtPageReturnTitle = string.Empty;
	private readonly DiagChecks _qtPageChecks = new("Qt page diag");

	// Controls diag (MAUI_SAILFISH_QT_HOST_CONTROLS_DIAG=1): Silica controls gallery — property pushes, event write-backs, radio exclusivity, injected tap.
	// Wheel dialogs are separate Silica windows, so their accepts go through the page's __diagFireEvent hook.
	private bool _qtControlsDiag;
	private int _qtCtlStage;   // 0 idle, 1 awaiting gallery render, 2+ verification legs
	private readonly DiagChecks _qtCtlChecks = new("Qt controls diag");
	private readonly Dictionary<string, Element> _qtCtl = new();   // gallery control references by diag key
	private int _qtCtlTaps;
	private int _qtCtlSearches;
	private int _qtCtlBorderTaps;        // tap on the button inside the Border
	private int _qtCtlImageTaps;         // ImageButton Clicked via the image adapter tap
	private double _qtCtlScrollTarget;   // ScrollToAsync target (dp)
	private int _qtCtlCtxClicks;         // MenuFlyoutItem clicks from the ContextMenu
	private int _qtCtlPullClicks;        // ToolbarItem clicks from the PullDownMenu
	private int _qtCtlPushClicks;        // ToolbarItem clicks from the PushUpMenu

	// Nav diag (MAUI_SAILFISH_QT_HOST_NAV_DIAG=1): MAUI ⇄ Silica pageStack mapping — push/pop both ways, modals, PopToRoot, lifecycle counters.
	private bool _qtNavDiag;
	private int _qtNavBaseDepth;         // mirror depth at cycle start (root + StatsPage)
	private Page? _qtNavRootPage;        // bottom-of-stack page (leg return target)
	private Page? _qtNavStartPage;       // top-of-stack page at cycle start (Statistics)
	private readonly DiagChecks _qtNavChecks = new("Qt nav diag");

	// Popup diag (MAUI_SAILFISH_QT_HOST_POPUP_DIAG=1): alert, prompt and action sheet against native Silica dialogs, plus SailfishBottomSheet → DockedPanel.
	private bool _qtPopupDiag;
	private int _qtPopupBaseDepth;                  // native model-page depth at cycle start
	private Task<bool>? _qtPopupAlert;              // leg A DisplayAlertAsync task
	private Task<string>? _qtPopupPrompt;           // leg B DisplayPromptAsync task
	private Task<string>? _qtPopupSheet;            // leg C DisplayActionSheetAsync task
	private SailfishBottomSheet? _qtPopupBottomSheet; // leg D sheet instance
	private int _qtPopupSheetCloses;                // leg D native close write-backs observed
	private readonly DiagChecks _qtPopupChecks = new("Qt popup diag");

	// Collection diag (MAUI_SAILFISH_QT_HOST_COLLECTION_DIAG=1): virtualized ListView bridge — row model, delegates, selection, INCC, scrolling.
	private bool _qtCollectionDiag;
	// Dataset size; the test matrix re-runs the same leg at several scales.
	private int _qtColRows = 40;
	private ContentPage? _qtColPage;                // the pushed diagnostics page
	private CollectionView? _qtColView;             // its CollectionView
	private System.Collections.ObjectModel.ObservableCollection<string>? _qtColItems;
	private int _qtColSelections;                   // SelectionChanged fires observed
	private int _qtColScrolled;                     // Scrolled event fires observed
	private RefreshView? _qtColRefresh;             // leg E: the wrapping RefreshView
	private int _qtColRefreshes;                    // Refreshing fires observed
	private int _qtColThresholds;                   // RemainingItemsThresholdReached fires
	private ItemsViewScrolledEventArgs? _qtColLastScroll; // last Scrolled payload (indices)
	private readonly DiagChecks _qtColChecks = new("Qt collection diag");

	// Shapes diag (MAUI_SAILFISH_QT_HOST_SHAPES_DIAG=1): Shape path ops, GraphicsView command stream, image Aspect, plus a pixel hash.
	// mauiWantHash switches the Canvas to the Image render target so getImageData can hash what was actually painted.
	private bool _qtShapesDiag;
	private int _qtShapesStage;                 // 0 idle, 1 awaiting page render, 2 awaiting hash repaint, 3 done
	private string _qtShapesReturnTitle = string.Empty;
	private readonly DiagChecks _qtShapesChecks = new("Qt shapes diag");

	// Visual diag (MAUI_SAILFISH_QT_HOST_VISUAL_DIAG=1): Opacity, IsEnabled (injected tap must be blocked), ZIndex, Rotation/Scale/Anchor, visibility, VSM setters.
	private bool _qtVisualDiag;
	private int _qtVisualStage;                 // 0 idle, 1 awaiting page render, 2.. legs, 9 done
	private string _qtVisualReturnTitle = string.Empty;
	private readonly DiagChecks _qtVisualChecks = new("Qt visual diag");

	// Stress diag (MAUI_SAILFISH_QT_HOST_STRESS_DIAG=1): open/close/reopen, background/resume, dialog, destroy. Asserts no late callbacks,
	// no use-after-free (dead handles read back DEAD), no leaked QML objects or GCHandles; the destroy leg is the auto-shutdown (must exit rc=0).
	private bool _qtStressDiag;
	private int _qtStressBaseDepth;             // native model-page depth at cycle start
	private long _qtStressBaseRegistry;         // shim handle-registry baseline (advisory: attach timing varies)
	private long _qtStressBaseLive;             // live MAUI hosts at cycle start (QML-leak witness)
	private long _qtStressDeadHandle;           // leg-A host handle (must read back dead from leg B on)
	private Task<bool>? _qtStressAlert;         // leg E DisplayAlertAsync task
	private Page? _qtStressPageC;               // the leg-C re-opened page (dialog host)
	private readonly DiagChecks _qtStressChecks = new("Qt stress diag");

	// Perf diag (MAUI_SAILFISH_QT_HOST_PERF_DIAG=1): idle → scroll (200-item list + flicks) → navigation, sampling shim perf stats and GC allocations
	// at each boundary. Checks: no per-frame readback, no full-tree recreation, batched updates, native virtualization, bounded crossings.
	private bool _qtPerfDiag;
	private readonly DiagChecks _qtPerfChecks = new("Qt perf diag");
	private PerfSnap _perfS0, _perfS1, _perfS2, _perfS3;   // baseline/idle/scroll-settle/post-flick snapshots
	private long _perfA0, _perfA1, _perfA2, _perfA3;       // GC.GetTotalAllocatedBytes at the snapshots
	private long _perfNavA0;                               // GC alloc bytes at the nav-phase start
	private long _perfScrollWrites0;                       // renderer.ScrollWriteBacks at flick start
	private long _perfPushesBefore;                        // renderer.NativePushes before the collection push
	private string? _perfListObj;                          // FirstListObjectName of the perf collection page
	private double _perfListY0;                            // ListView contentY before the flicks
	private long _perfListEvents0;                         // bridge ListEvents before the flicks
	private long _perfLayoutPasses0, _perfDirtyProp0, _perfDirtyWb0;   // idle-churn attribution baselines
	private long _perfGeoReads0, _perfRowsBuilt0;
	private int _qtPerfBaseDepth;                          // native model-page depth at cycle start
	private long _qtPerfBaseLive;                          // live MAUI hosts census at cycle start
	private ContentPage? _qtPerfColPage;                   // the 200-item scroll workload page
	private Label? _qtPerfMutateLabel;                     // in-place property-update target
	private readonly List<double> _qtPerfNavPushMs = new();    // per-leg push→SendAppearing
	private readonly List<(long OpsEvals, long Created, long PropsBatches, long PropertySets, long OpsBatches)> _qtPerfNavLegs = new();

	// Error diag (MAUI_SAILFISH_QT_HOST_ERROR_DIAG=1): inject deterministic native errors (dead handle, bad args, unknown property, bad JSON).
	// Each must return a stable sfhost_err code with last_error context and be logged via its QtHostDiag channel; TotalErrors == baseline + injected.
	private bool _qtErrorDiag;
	private readonly DiagChecks _qtErrorChecks = new("QT ERROR DIAG");

	/// <summary>
	/// Logs "SF-SHOT &lt;name&gt;" and holds for MAUI_SAILFISH_SHOT_HOLD_MS (tools/sf shots screenshots each marker) before continuing.
	/// </summary>
	private static void Shot(SailfishDispatcher dispatcher, string name, Action next)
	{
		var hold = SailfishEnv.Int("MAUI_SAILFISH_SHOT_HOLD_MS") ?? 0;
		Console.Error.WriteLine($"[Sailfish] SF-SHOT {name}");
		if (!SailfishEnv.Flag("MAUI_SAILFISH_SHOT_SYNC"))
		{
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(Math.Max(200, hold)), next);
			return;
		}
		// tools/sf shots acknowledges each screenshot with /tmp/sf-shot-ack/<name>: the state is held until the
		// compositor capture (seconds) is done, then for the hold, so no shot shows the next marker's state.
		var ack = Path.Combine("/tmp/sf-shot-ack", name);
		var waited = 0;
		void Poll()
		{
			if (File.Exists(ack) || waited >= 20000)
			{
				if (waited >= 20000)
					Console.Error.WriteLine($"[Sailfish] SF-SHOT {name}: no acknowledgement after 20 s, moving on");
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(Math.Max(0, hold)), next);
				return;
			}
			waited += 100;
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(100), Poll);
		}
		Poll();
	}

	public void Attach(QtHostDiagnosticsContext context)
	{
		_context = context;
		_qtInputRouter = context.InputRouter;
		var renderer = context.Renderer;
		var dispatcher = context.Dispatcher;
		var virtualWindow = context.Window;

		// Runs with MAUI_SAILFISH_QT_HOST_DIAG=1. Log every Qt pointer event (kinds as in sailfish_host.h) and QML event reaching managed code.
		QtHost.QtHostRuntime.PointerInput += (kind, x, y, _, _) =>
			Console.Error.WriteLine($"[Sailfish] Qt pointer kind={kind} at {x},{y}");

		QtHost.QtHostRuntime.QmlEvent += (name, payload) =>
			Console.Error.WriteLine($"[Sailfish] QML event {name}: {payload}");

		// On the first render, inject a real Qt tap on a model button (tap → QML → shim → SendClicked → navigation).
		// Skipped for the back-swipe scenario and the showcase: their own navigation would race the extra push.
		var injected = SailfishEnv.Int("MAUI_SAILFISH_BACK_AFTER") is not null
			|| SailfishEnv.Flag("MAUI_SAILFISH_QT_HOST_SHOWCASE");   // the tour taps itself
		QtHost.QtHostRuntime.QmlEvent += (name, payload) =>
		{
			if (injected || name != "rendered" || !payload.Contains("\"buttons\"", StringComparison.Ordinal))
				return;
			try
			{
				using var doc = System.Text.Json.JsonDocument.Parse(payload);
				var buttons = doc.RootElement.GetProperty("buttons");
				if (buttons.GetArrayLength() == 0)
					return;
				injected = true;
				var button = buttons[buttons.GetArrayLength() > 1 ? 1 : 0].Clone();
				var cx = button.GetProperty("x").GetDouble() + button.GetProperty("w").GetDouble() / 2;
				var cy = button.GetProperty("y").GetDouble() + button.GetProperty("h").GetDouble() / 2;
				Console.Error.WriteLine($"[Sailfish] Qt diag: injecting tap id={button.GetProperty("id").GetString()} at {cx},{cy}");
				DiagQml.Tap(cx, cy);
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine($"[Sailfish] Qt diag: tap injection failed: {ex.Message}");
			}
		};

		// MAUI_SAILFISH_BACK_AFTER=<n>: inject Silica's left-edge back swipe once after the nth page render, exercising the native back path unattended.
		// Combine with MAUI_SAILFISH_OPEN_PULLEY on the next seq to screenshot the returned-to page's pulley.
		var backAfter = SailfishEnv.Int("MAUI_SAILFISH_BACK_AFTER") ?? 0;
		var backRendered = 0;
		var backDone = false;
		QtHost.QtHostRuntime.QmlEvent += (name, payload) =>
		{
			if (backAfter <= 0 || backDone || name != "rendered" || !payload.Contains("\"buttons\"", StringComparison.Ordinal))
				return;
			backRendered++;
			if (backRendered != backAfter)
				return;
			backDone = true;
			_ = Task.Run(async () =>
			{
				await Task.Delay(700);   // let the page settle post-render
				Console.Error.WriteLine("[Sailfish] Qt diag: injecting native back swipe (left edge drag)");
				const double y = 1200;
				QtHost.QtHostRuntime.InjectPointer(0, 6, y);
				for (var x = 40; x <= 420; x += 60)
				{
					QtHost.QtHostRuntime.InjectPointer(2, x, y);
					await Task.Delay(16);
				}
				QtHost.QtHostRuntime.InjectPointer(1, 440, y);
			});
		};

		// MAUI_SAILFISH_QT_HOST_AUTO_SHUTDOWN=1: once the injected tap has navigated and the next page rendered, run the enabled legs, then tear down.
		if (SailfishEnv.Flag("MAUI_SAILFISH_QT_HOST_AUTO_SHUTDOWN"))
		{
			// Reconcile leg: one property change must update the native item in place, not recreate the tree.
			var reconcileDiag = SailfishEnv.Flag("MAUI_SAILFISH_QT_HOST_RECONCILE_DIAG");
			// Bridge leg: typed push, suppression and native→MAUI write-back.
			var bridgeDiag = SailfishEnv.Flag("MAUI_SAILFISH_QT_HOST_BRIDGE_DIAG");
			// Geometry leg: MAUI bounds vs Qt scene readback, plus Qt window/screen info.
			_qtGeometryDiag = SailfishEnv.Flag("MAUI_SAILFISH_QT_HOST_GEOMETRY_DIAG");
			// Input leg: runs after geometry.
			_qtInputDiag = SailfishEnv.Flag("MAUI_SAILFISH_QT_HOST_INPUT_DIAG");
			// Text leg: runs after the bridge legs, before geometry/input.
			_qtTextDiag = SailfishEnv.Flag("MAUI_SAILFISH_QT_HOST_TEXT_DIAG");
			// The legs below push their own page and are meant to run as separate diag cycles.
			_qtPageDiag = SailfishEnv.Flag("MAUI_SAILFISH_QT_HOST_PAGE_DIAG");
		_qtControlsDiag = SailfishEnv.Flag("MAUI_SAILFISH_QT_HOST_CONTROLS_DIAG");
		_qtNavDiag = SailfishEnv.Flag("MAUI_SAILFISH_QT_HOST_NAV_DIAG");
		_qtPopupDiag = SailfishEnv.Flag("MAUI_SAILFISH_QT_HOST_POPUP_DIAG");
		_qtCollectionDiag = SailfishEnv.Flag("MAUI_SAILFISH_QT_HOST_COLLECTION_DIAG");
		// MAUI_SAILFISH_QT_HOST_COLLECTION_ROWS overrides the dataset size (default 40).
		if (SailfishEnv.Int("MAUI_SAILFISH_QT_HOST_COLLECTION_ROWS") is int colRows && colRows >= 2 && colRows <= 5000)
			_qtColRows = colRows;
		_qtShapesDiag = SailfishEnv.Flag("MAUI_SAILFISH_QT_HOST_SHAPES_DIAG");
		_qtVisualDiag = SailfishEnv.Flag("MAUI_SAILFISH_QT_HOST_VISUAL_DIAG");
		_qtStressDiag = SailfishEnv.Flag("MAUI_SAILFISH_QT_HOST_STRESS_DIAG");
		_qtPerfDiag = SailfishEnv.Flag("MAUI_SAILFISH_QT_HOST_PERF_DIAG");
		_qtErrorDiag = SailfishEnv.Flag("MAUI_SAILFISH_QT_HOST_ERROR_DIAG");
				// Host tree (QtHostDiagnosticsRunner.Tree.cs).
				_qtTreeDiag = SailfishEnv.Flag("MAUI_SAILFISH_QT_HOST_TREE_DIAG");
				// Shell core (QtHostDiagnosticsRunner.Shell.cs).
				_qtShellDiag = SailfishEnv.Flag("MAUI_SAILFISH_QT_HOST_SHELL_DIAG");
				// Nested page containers and the page cache (QtHostDiagnosticsRunner.Containers.cs).
				_qtContainersDiag = SailfishEnv.Flag("MAUI_SAILFISH_QT_HOST_CONTAINERS_DIAG");
				// Pulley menus across navigation (QtHostDiagnosticsRunner.Pulley.cs).
				_qtPulleyDiag = SailfishEnv.Flag("MAUI_SAILFISH_QT_HOST_PULLEY_DIAG");
				// Pulley menus following the selected tab (QtHostDiagnosticsRunner.TabPulley.cs).
				_qtTabPulleyDiag = SailfishEnv.Flag("MAUI_SAILFISH_QT_HOST_TABPULLEY_DIAG");
				// Native Silica idioms behind MAUI APIs (QtHostDiagnosticsRunner.Silica.cs).
				_qtSilicaDiag = SailfishEnv.Flag("MAUI_SAILFISH_QT_HOST_SILICA_DIAG");
				// Recording scenes for docs/sailfish-apis.md (QtHostDiagnosticsRunner.ApiDemo.cs).
				_qtApiDemo = SailfishEnv.Get("MAUI_SAILFISH_QT_HOST_APIDEMO") is { Length: > 0 } demo ? demo : null;
				// Forward navigation after a back swipe (QtHostDiagnosticsRunner.NavBack.cs).
				_qtNavBackDiag = SailfishEnv.Flag("MAUI_SAILFISH_QT_HOST_NAVBACK_DIAG");
				// Adapter creation cost and candidate pixel parity (QtHostDiagnosticsRunner.AdapterBench.cs).
				_qtAdapterBenchDiag = SailfishEnv.Flag("MAUI_SAILFISH_QT_HOST_ADAPTERBENCH_DIAG");
				// F3 controls (QtHostDiagnosticsRunner.F3.cs).
				_qtF3Diag = SailfishEnv.Flag("MAUI_SAILFISH_QT_HOST_F3_DIAG");
				// F4 platform services (QtHostDiagnosticsRunner.F4.cs).
				_qtF4Diag = SailfishEnv.Flag("MAUI_SAILFISH_QT_HOST_F4_DIAG");
				// Features-hub round trips (QtHostDiagnosticsRunner.Features.cs).
				_qtFeaturesDiag = SailfishEnv.Flag("MAUI_SAILFISH_QT_HOST_FEATURES_DIAG");
				// Showcase tour for screen recordings (QtHostDiagnosticsRunner.Showcase.cs).
				_qtShowcase = SailfishEnv.Flag("MAUI_SAILFISH_QT_HOST_SHOWCASE");
			var pageLegs = PageOwningLegs();
			var diagStage = 0;   // 0=idle 1=mutation applied, awaiting verify report 2=done
			string? firstTitle = null;
			QtHost.QtHostRuntime.QmlEvent += (name, payload) =>
			{
				if (name != "rendered")
					return;
				var title = ExtractRenderedTitle(payload);
				if (title is null)
					return;

				// Once a page-owning diag cycle is armed, its own rendered listener drives the run; without this guard
				// the generic chain below would re-enter on the pushed page and shut down mid-cycle.
				if ((_qtShowcase || pageLegs.Any(l => l.Enabled)) && diagStage != 0)
					return;

				// The verification report arrives with the same title, right after the mutation leg's forced reportRendered().
				if (reconcileDiag && diagStage == 1)
				{
					diagStage = 2;
					VerifyQtReconcileDiagnostics(payload);
					if (bridgeDiag && renderer is not null)
					{
						RunQtBridgeDiagnostics(renderer, dispatcher);
						return; // shutdown follows the bridge/text/geometry legs
					}
					if (_qtTextDiag && renderer is not null)
					{
						RunQtTextDiagnostics(renderer, dispatcher);
						return; // shutdown follows the text/geometry legs
					}
					if (_qtGeometryDiag && renderer is not null)
					{
						RunQtGeometryDiagnostics(renderer, dispatcher);
						return; // shutdown follows the geometry leg
					}
					if (_qtInputDiag && renderer is not null && _qtInputRouter is not null)
					{
						RunQtInputDiagnostics(renderer, dispatcher, _qtInputRouter);
						return; // shutdown follows the input leg
					}
					Console.Error.WriteLine("[Sailfish] Qt diag: reconcile diag done; auto-shutdown requested");
					QtHost.QtHostRuntime.Shutdown();
					return;
				}

				// Once the reconcile chain owns the run, re-renders of the same page must not reach the generic shutdown below,
				// or the text/input/geometry legs never print their ACCEPTANCE line.
				if (reconcileDiag && diagStage != 0)
					return;

				// The showcase tour starts on the first render of the home page (no startup tap).
				if (_qtShowcase && diagStage == 0 && renderer is not null)
				{
					diagStage = 1;   // the showcase tour owns the run from here
					dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1500), () => RunQtShowcase(renderer, dispatcher));
					return; // shutdown follows the tour
				}
				if (title == firstTitle)
					return;
				if (firstTitle is null)
				{
					firstTitle = title;
					return;
				}
				Console.Error.WriteLine($"[Sailfish] Qt diag: navigation confirmed ({firstTitle} -> {title})");
				// The first enabled page-owning leg owns the run from here; its shutdown follows its own report.
				if (diagStage == 0 && renderer is not null && pageLegs.FirstOrDefault(l => l.Enabled).Run is { } runLeg)
				{
					diagStage = 1;
					runLeg(renderer, dispatcher, title);
					return;
				}
				if (reconcileDiag && diagStage == 0 && renderer is not null)
				{
					diagStage = 1;
					RunQtReconcileDiagnostics(renderer, payload);
					return; // shutdown follows the verification report
				}
				Console.Error.WriteLine("[Sailfish] Qt diag: auto-shutdown requested");
				QtHost.QtHostRuntime.Shutdown();
			};
		}

		// MAUI_SAILFISH_PULL_GESTURE=<ms>: inject a real slow pull on the top page's pulley so Silica opens it and keeps it open for a screenshot.
		// Faking contentY doesn't paint the items: PulleyMenuLogic keys the menu content on real drag state.
		if (SailfishEnv.IsSet("MAUI_SAILFISH_PULL_GESTURE"))
		{
			var pulleyArmed = false;
			QtHost.QtHostRuntime.QmlEvent += (name, payload) =>
			{
				if (pulleyArmed || name != "rendered" || renderer is null)
					return;
				pulleyArmed = true;
				MaybeInjectPulleyGesture(renderer, dispatcher);
			};
		}


		dispatcher.DispatchDelayed(TimeSpan.FromSeconds(2), () =>
		{
			Console.Error.WriteLine("[Sailfish] Qt host tick leg OK (dispatcher timer fired on Qt thread)");
			QtHost.QtHostRuntime.Post(() =>
				Console.Error.WriteLine("[Sailfish] Qt host post leg OK (work marshalled onto Qt thread)"));
		});
	}

	/// <summary>
	/// Extracts "title" from a QML "rendered" payload; null when absent or malformed.
	/// </summary>
	private static string? ExtractRenderedTitle(string payload)
	{
		try
		{
			using var doc = System.Text.Json.JsonDocument.Parse(payload);
			return doc.RootElement.TryGetProperty("title", out var title) ? title.GetString() : null;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// Reconcile mutation leg (Qt thread): plants a probe on a native label host, mutates the MAUI Label.Text,
	/// reads it back natively and forces a QML report for the verification leg.
	/// </summary>
	private void RunQtReconcileDiagnostics(QtHost.QtHostPageRenderer renderer, string payload)
	{
		ParseRenderedCounters(payload, out _qtBaselineCreated, out _qtBaselineDestroyed);
		QtHost.QtHostRuntime.Eval("pageStack.currentPage.flickY = 42");

		var host = renderer.CurrentHosts.FirstOrDefault(h => h.IsAttached && h.QmlUri == "label" && h.Element is Label);
		if (host?.Element is not Label label || string.IsNullOrEmpty(label.Text))
		{
			Console.Error.WriteLine("[Sailfish] QtHost diag: no attached label host to mutate — reconcile diag skipped");
			return;
		}

		var probeRc = QtHost.QtHostRuntime.SetProperty(host.NativeHandle, "mauiProbe", "\"alive\"");
		_qtProbeHandle = host.NativeHandle;
		label.Text += " (Q4)";
		_qtProbeExpectText = label.Text;
		renderer.Render();

		// Read-back through the native bridge (no QML round-trip involved).
		var text = QtHost.QtHostRuntime.GetProperty(host.NativeHandle, "text");
		var textOk = text == _qtProbeExpectText ? "OK" : $"MISMATCH (native='{text}')";
		Console.Error.WriteLine($"[Sailfish] QtHost diag: mutated {host} via MAUI Label.Text; in-place readback {textOk}; " +
			$"probe_rc={probeRc}; baseline created={_qtBaselineCreated} destroyed={_qtBaselineDestroyed}");

		QtHost.QtHostRuntime.Eval("pageStack.currentPage.reportRendered()");
	}

	/// <summary>
	/// Reconcile verification leg: created/destroyed counters must be unchanged (tree not recreated) and the probe still alive.
	/// </summary>
	private void VerifyQtReconcileDiagnostics(string payload)
	{
		ParseRenderedCounters(payload, out var created, out var destroyed);
		var flickY = ExtractRenderedNumber(payload, "flickY");
		if (_qtProbeHandle == 0)
		{
			Console.Error.WriteLine($"[Sailfish] QtHost diag: VERIFY skipped (mutation leg found no label host); created={created} destroyed={destroyed}");
			return;
		}

		var probe = QtHost.QtHostRuntime.GetProperty(_qtProbeHandle, "mauiProbe");
		var noRecreate = created == _qtBaselineCreated && destroyed == _qtBaselineDestroyed;
		var ok = noRecreate && probe == "alive";
		Console.Error.WriteLine($"[Sailfish] QtHost diag: VERIFY created={created} (baseline {_qtBaselineCreated}) " +
			$"destroyed={destroyed} (baseline {_qtBaselineDestroyed}) probe='{probe}' flickY={flickY} => " +
			(ok ? "OK — property change did NOT recreate the QML tree; native state survived (PLAN Q4 acceptance)"
			    : "FAIL — tree was recreated or probe lost"));
		_qtProbeHandle = 0;
		_qtProbeExpectText = string.Empty;
	}

	// Bridge diag: leg 1 mutates typed properties on the real MAUI elements and reads them back natively with the suppression
	// counters; leg 2 taps the Silica Switch, whose "toggled" event must flip MAUI Switch.IsToggled back.

	private void RunQtBridgeDiagnostics(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		QtHost.NativeElementHost? entry = null, sw = null, slider = null, label = null;
		foreach (var host in renderer.CurrentHosts)
		{
			switch (host.QmlUri)
			{
				case "entry" when entry is null: entry = host; break;
				case "switch" when sw is null: sw = host; break;
				case "slider" when slider is null: slider = host; break;
				case "label" when label is null && host.Element is Label { FontSize: >= 24 }: label = host; break;
			}
		}
		if (entry?.IsAttached != true || sw?.IsAttached != true || slider?.IsAttached != true || label?.IsAttached != true)
		{
			Console.Error.WriteLine($"[Sailfish] Qt bridge diag: hosts incomplete (entry={entry} switch={sw} slider={slider} label={label}) — legs skipped");
			QtHost.QtHostRuntime.Shutdown();
			return;
		}

		Console.Error.WriteLine("[Sailfish] Qt bridge diag: mutating typed MAUI properties (string/bool/double/color)...");
		if (entry.Element is Entry e) e.Text = "Q6 bridge OK";
		if (sw.Element is Switch s) { _qtBridgeToggleBefore = s.IsToggled; s.IsToggled = !s.IsToggled; }
		if (slider.Element is Slider sl) sl.Value = Math.Clamp(7.5, sl.Minimum, sl.Maximum);
		if (label.Element is Label l) l.TextColor = Color.FromArgb("#4FC3F7");

		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(400),
			() => VerifyQtBridgeReadback(renderer, dispatcher, entry, sw, slider, label));
	}

	private void VerifyQtBridgeReadback(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher,
	                                    QtHost.NativeElementHost entry, QtHost.NativeElementHost sw,
	                                    QtHost.NativeElementHost slider, QtHost.NativeElementHost label)
	{
		var text = QtHost.QtHostRuntime.GetProperty(entry.NativeHandle, "text");
		var checkedText = QtHost.QtHostRuntime.GetProperty(sw.NativeHandle, "checked");
		var valueText = QtHost.QtHostRuntime.GetProperty(slider.NativeHandle, "value");
		var colorText = QtHost.QtHostRuntime.GetProperty(label.NativeHandle, "mauiColor");
		var suppEntry = QtHost.QtHostRuntime.GetProperty(entry.NativeHandle, "mauiSuppressedCount");
		var suppSwitch = QtHost.QtHostRuntime.GetProperty(sw.NativeHandle, "mauiSuppressedCount");
		var suppSlider = QtHost.QtHostRuntime.GetProperty(slider.NativeHandle, "mauiSuppressedCount");

		var expectChecked = _qtBridgeToggleBefore ? "false" : "true";
		var pushOk = text == "Q6 bridge OK"
			&& checkedText == expectChecked
			&& double.TryParse(valueText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
			&& Math.Abs(v - 7.5) < 1e-9
			&& colorText == "#ff4fc3f7";
		Console.Error.WriteLine($"[Sailfish] Qt bridge diag: PUSH readback text='{text}' checked={checkedText} (expected {expectChecked}) " +
			$"value={valueText} color={colorText} => " +
			(pushOk ? "OK — typed MAUI→native bridge (string/bool/double/color)" : "MISMATCH"));

		var suppressed = int.TryParse(suppEntry, out var se) && se > 0
			&& int.TryParse(suppSwitch, out var ss) && ss > 0
			&& int.TryParse(suppSlider, out var sl) && sl > 0;
		Console.Error.WriteLine($"[Sailfish] Qt bridge diag: SUPPRESSION mauiSuppressedCount entry={suppEntry} switch={suppSwitch} slider={suppSlider} => " +
			(suppressed ? "OK — managed pushes did not echo back as events" : "FAIL — echo suppression not observed"));
		_qtBridgePushOk = pushOk;
		_qtBridgeSuppressionOk = suppressed;

		// Leg 2: injected tap toggles the Silica Switch → "toggled" → write-back.
		if (QtHost.QtHostRuntime.TryItemGeometry(sw.NativeHandle, out var geo) && geo.Width > 0 && geo.Height > 0)
		{
			var cx = geo.X + geo.Width / 2;
			var cy = geo.Y + geo.Height / 2;
			Console.Error.WriteLine($"[Sailfish] Qt bridge diag: injecting tap into switch {sw.Id} at {cx},{cy}");
			DiagQml.Tap(cx, cy);
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(400), () => VerifyQtBridgeWriteBack(renderer, dispatcher, sw));
		}
		else
		{
			Console.Error.WriteLine("[Sailfish] Qt bridge diag: switch geometry unavailable — write-back leg skipped");
			FinishBridgeDiag(renderer, dispatcher);
		}
	}

	private void VerifyQtBridgeWriteBack(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, QtHost.NativeElementHost sw)
	{
		// The mutation leg flipped IsToggled; the injected tap must flip it back via QML → native callback → managed write-back.
		var now = sw.Element is Switch s ? (bool?)s.IsToggled : null;
		var ok = now == _qtBridgeToggleBefore;
		Console.Error.WriteLine($"[Sailfish] Qt bridge diag: WRITE-BACK MAUI Switch.IsToggled={now?.ToString() ?? "?"} " +
			$"(expected {_qtBridgeToggleBefore} after the injected tap) => " +
			(ok == true ? "OK — QML state change → native callback → managed event → MAUI" : "FAIL"));
		_qtBridgeWriteBackOk = ok;
		FinishBridgeDiag(renderer, dispatcher);
	}

	private void FinishBridgeDiag(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		Console.Error.WriteLine($"[Sailfish] Qt bridge diag: counters applied={renderer.BridgeApplied} failed={renderer.BridgeFailed} " +
			$"events={renderer.NativeEventsDelivered} echoes-suppressed={renderer.NativeEventsSuppressed}");
		var bridgeOk = _qtBridgePushOk && _qtBridgeSuppressionOk && _qtBridgeWriteBackOk && renderer.BridgeFailed == 0;
		Console.Error.WriteLine($"[Sailfish] Qt bridge diag: ACCEPTANCE push={(_qtBridgePushOk ? 1 : 0)} suppression={(_qtBridgeSuppressionOk ? 1 : 0)} " +
			$"write-back={(_qtBridgeWriteBackOk ? 1 : 0)} failed={renderer.BridgeFailed} => " +
			(bridgeOk ? "OK — typed push, echo suppression and native write-back (PLAN Q6)" : "FAIL — see the leg lines above"));
		// Leave the bridge end state on screen as the final evidence.
		var grabRc = QtHost.QtHostRuntime.GrabPng("/tmp/q6-bridge-final.png");
		Console.Error.WriteLine($"[Sailfish] Qt bridge diag: final screenshot rc={grabRc} -> /tmp/q6-bridge-final.png");
		if (_qtTextDiag)
		{
			RunQtTextDiagnostics(renderer, dispatcher);   // geometry/input legs follow the text leg
			return;
		}
		if (_qtGeometryDiag)
		{
			RunQtGeometryDiagnostics(renderer, dispatcher);   // shutdown follows the geometry leg
			return;
		}
		if (_qtInputDiag && _qtInputRouter is not null)
		{
			RunQtInputDiagnostics(renderer, dispatcher, _qtInputRouter);   // shutdown follows the input leg
			return;
		}
		Console.Error.WriteLine("[Sailfish] Qt diag: bridge diag done; auto-shutdown requested");
		QtHost.QtHostRuntime.Shutdown();
	}

	// Text-input diag: the native Qt text-input path (no SDL soft keyboard) drives MAUI Entry/Editor on the bridge-diag page.
	// Legs: A focus push → Maliit VKB, B injected hardware keys, C/D cursor+selection both ways, E Return → Completed; F/G follow.
	// Maliit is a separate Wayland surface, so only a compositor screenshot (sf screenshot) shows the VKB, never grabWindow.

	private void RunQtTextDiagnostics(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		QtHost.NativeElementHost? entryHost = null, editorHost = null;
		foreach (var host in renderer.CurrentHosts)
		{
			switch (host.QmlUri)
			{
				case "entry" when entryHost is null && host.Element is Entry: entryHost = host; break;
				case "editor" when editorHost is null && host.Element is Editor: editorHost = host; break;
			}
		}
		if (entryHost?.IsAttached != true || entryHost.Element is not Entry entry ||
		    editorHost?.IsAttached != true || editorHost.Element is not Editor editor)
		{
			Console.Error.WriteLine($"[Sailfish] Qt text diag: text hosts incomplete (entry={entryHost is { IsAttached: true } and { Element: Entry }}, editor={editorHost is { IsAttached: true } and { Element: Editor }}) — legs skipped");
			ContinueAfterQtTextDiagnostics(renderer, dispatcher);
			return;
		}

		// Legs C/D assert absolute cursor positions (select(3,5)), so seed a long enough text when the Entry is empty.
		// The push lands before leg A's 900 ms settle, so leg B types onto the seeded text.
		if (string.IsNullOrEmpty(entry.Text))
			entry.Text = "Q6 bridge OK";

		var entryHandle = entryHost.NativeHandle;
		var editorHandle = editorHost.NativeHandle;
		var textBefore = entry.Text ?? string.Empty;
		var completed = new int[1];   // holder: the Completed handler and the nested legs share it
		entry.Completed += (_, _) => completed[0]++;

		Console.Error.WriteLine("[Sailfish] Qt text diag: leg A — managed→native focus push (Entry.Focus() → mauiFocus → forceActiveFocus → Maliit VKB through the Qt text-input stack)");
		entry.Focus();

		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900), () =>
		{
			var activeFocus = QtHost.QtHostRuntime.GetProperty(entryHandle, "activeFocus");
			var mirror = QtHost.QtHostRuntime.GetProperty(entryHandle, "mauiFocus");
			var focusOk = activeFocus == "true" && mirror == "true" && entry.IsFocused;
			Console.Error.WriteLine($"[Sailfish] Qt text diag: leg A — native activeFocus={activeFocus} mauiFocus={mirror} MAUI IsFocused={entry.IsFocused} => " +
				(focusOk ? "OK — Qt text-input path focused; Maliit VKB should be open (compositor screenshot is the VKB truth)" : "FAIL — focus push did not reach native"));
			QtHost.QtHostRuntime.GrabPng("/tmp/q9-a-focus.png");
			RunQtTextLegsBtoE(renderer, dispatcher, entry, editor, entryHandle, editorHandle, focusOk, textBefore, completed);
		});
	}


	// Legs B–E: sequential injected events with settle delays; each step logs its own OK/FAIL line.
	private void RunQtTextLegsBtoE(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher,
	                               Entry entry, Editor editor, long entryHandle, long editorHandle,
	                               bool focusOk, string textBefore, int[] completed)
	{
		Console.Error.WriteLine("[Sailfish] Qt text diag: leg B — hardware keyboard path (injecting QKeyEvents 'Q','9' → focused TextInput)");
		InjectQtKeyTap(QtHost.QtHostRuntime.QtKeyA + ('Q' - 'A'), "Q");
		InjectQtKeyTap(QtHost.QtHostRuntime.QtKey0 + 9, "9");
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(500), () =>
		{
			var typed = entry.Text ?? string.Empty;
			var typedOk = typed == textBefore + "Q9";
			Console.Error.WriteLine($"[Sailfish] Qt text diag: leg B — Entry.Text='{typed}' (expected '{textBefore}Q9') => " +
				(typedOk ? "OK — HW key events consumed natively, written back via text-changed" : "FAIL"));

			Console.Error.WriteLine("[Sailfish] Qt text diag: leg C — cursor/selection managed→native (CursorPosition=3, SelectionLength=2 → select(3,5))");
			entry.CursorPosition = 3;
			entry.SelectionLength = 2;
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(400), () =>
			{
				var nativeCursor = QtHost.QtHostRuntime.GetProperty(entryHandle, "cursorPosition");
				var nativeStart = QtHost.QtHostRuntime.GetProperty(entryHandle, "selectionStart");
				var nativeEnd = QtHost.QtHostRuntime.GetProperty(entryHandle, "selectionEnd");
				var selOk = nativeStart == "3" && nativeEnd == "5";
				Console.Error.WriteLine($"[Sailfish] Qt text diag: leg C — native cursorPosition={nativeCursor} selectionStart={nativeStart} selectionEnd={nativeEnd} => " +
					(selOk ? "OK — selection applied natively (Qt caret sits at the selection end)" : "FAIL"));

				Console.Error.WriteLine("[Sailfish] Qt text diag: leg D — cursor native→managed (injecting Left: collapses the selection → cursor-changed write-back)");
				InjectQtKeyTap(QtHost.QtHostRuntime.QtKeyLeft, null);
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(400), () =>
				{
					var cursorOk = entry.CursorPosition == 3 && entry.SelectionLength == 0;
					Console.Error.WriteLine($"[Sailfish] Qt text diag: leg D — MAUI CursorPosition={entry.CursorPosition} SelectionLength={entry.SelectionLength} (expected 3/0; writebacks={renderer.CursorWriteBacks}) => " +
						(cursorOk ? "OK — native caret movement written back into the MAUI InputView" : "FAIL"));

					Console.Error.WriteLine("[Sailfish] Qt text diag: leg E — completion (injecting Return → accepted → completed → SendCompleted)");
					InjectQtKeyTap(QtHost.QtHostRuntime.QtKeyReturn, "\r");
					dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(400), () =>
					{
						var completedOk = completed[0] >= 1 && renderer.CompletedFired >= 1;
						Console.Error.WriteLine($"[Sailfish] Qt text diag: leg E — Completed handler count={completed[0]} renderer.CompletedFired={renderer.CompletedFired} => " +
							(completedOk ? "OK — MAUI Completed semantics preserved through the native accepted path" : "FAIL"));

						RunQtTextModeLegs(renderer, dispatcher, entry, editor, entryHandle, editorHandle,
						                  focusOk, typedOk, selOk, cursorOk, completedOk);
					});
				});
			});
		});
	}

	/// <summary>Injects a hardware key press+release, posted like a QPA event so Qt delivers it to the focused text input.</summary>
	private static void InjectQtKeyTap(int key, string? text)
	{
		QtHost.QtHostRuntime.InjectKey(0, key, 0, text);
		QtHost.QtHostRuntime.InjectKey(1, key, 0, text);
	}


	// Leg F: password/read-only/keyboard-hint modes with native readback; leg G follows.
	private void RunQtTextModeLegs(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher,
	                               Entry entry, Editor editor, long entryHandle, long editorHandle,
	                               bool focusOk, bool typedOk, bool selOk, bool cursorOk, bool completedOk)
	{
		Console.Error.WriteLine("[Sailfish] Qt text diag: leg F — modes (IsPassword → echoMode+hints; IsReadOnly → readOnly; Keyboard.Numeric → inputMethodHints)");
		entry.IsPassword = true;
		entry.IsReadOnly = true;
		entry.Keyboard = Keyboard.Numeric;
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(400), () =>
		{
			var echo = QtHost.QtHostRuntime.GetProperty(entryHandle, "echoMode");
			var readOnly = QtHost.QtHostRuntime.GetProperty(entryHandle, "readOnly");
			var hints = QtHost.QtHostRuntime.GetProperty(entryHandle, "inputMethodHints");
			// echoMode 2 = TextInput.Password; hints = Numeric (0x20000 | 0x8 = 131080) OR'd natively with the
			// password hints (0x47, Silica PasswordField parity) = 131151.
			var modesOk = echo == "2" && readOnly == "true" && hints == "131151";
			Console.Error.WriteLine($"[Sailfish] Qt text diag: leg F — native echoMode={echo} (expected 2) readOnly={readOnly} (expected true) inputMethodHints={hints} (expected 131151 = Numeric|password) => " +
				(modesOk ? "OK — password/read-only/input-method modes synced" : "FAIL"));

			entry.IsPassword = false;
			entry.IsReadOnly = false;
			entry.Keyboard = Keyboard.Default;
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(400), () =>
			{
				var echo2 = QtHost.QtHostRuntime.GetProperty(entryHandle, "echoMode");
				var readOnly2 = QtHost.QtHostRuntime.GetProperty(entryHandle, "readOnly");
				var hints2 = QtHost.QtHostRuntime.GetProperty(entryHandle, "inputMethodHints");
				var resetOk = echo2 == "0" && readOnly2 == "false" && hints2 == "0";
				Console.Error.WriteLine($"[Sailfish] Qt text diag: leg F2 — reset readback echoMode={echo2} readOnly={readOnly2} inputMethodHints={hints2} => {(resetOk ? "OK — modes reversible" : "FAIL")}");
				RunQtTextEditorLeg(renderer, dispatcher, editor, editorHandle,
				                   focusOk, typedOk, selOk, cursorOk, completedOk, modesOk, resetOk);
			});
		});
	}


	// Leg G: multiline Editor, focus push + injected keys incl. Return → "ab\nc". Ends with the ACCEPTANCE line and a 15 s hold
	// with the VKB open for a compositor screenshot.
	private void RunQtTextEditorLeg(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher,
	                                Editor editor, long editorHandle,
	                                bool focusOk, bool typedOk, bool selOk, bool cursorOk,
	                                bool completedOk, bool modesOk, bool resetOk)
	{
		Console.Error.WriteLine("[Sailfish] Qt text diag: leg G — Editor (TextArea): focus push + injected 'a','b',Return,'c' → multiline write-back");
		editor.Focus();
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(700), () =>
		{
			var editorActive = QtHost.QtHostRuntime.GetProperty(editorHandle, "activeFocus");
			var editorFocusOk = editorActive == "true" && editor.IsFocused;
			Console.Error.WriteLine($"[Sailfish] Qt text diag: leg G — Editor native activeFocus={editorActive} MAUI IsFocused={editor.IsFocused} => " +
				(editorFocusOk ? "OK — multiline focus push (Maliit VKB switches to the TextArea)" : "FAIL"));
			InjectQtKeyTap(QtHost.QtHostRuntime.QtKeyA, "a");
			InjectQtKeyTap(QtHost.QtHostRuntime.QtKeyA + 1, "b");
			InjectQtKeyTap(QtHost.QtHostRuntime.QtKeyReturn, "\n");
			InjectQtKeyTap(QtHost.QtHostRuntime.QtKeyA + 2, "c");
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(500), () =>
			{
				// Qt 5.6 TextEdit may report U+2028 line separators; normalize before comparing.
				var editorText = (editor.Text ?? string.Empty).Replace('\u2028', '\n');
				var editorOk = editorFocusOk && editorText == "ab\nc";
				Console.Error.WriteLine($"[Sailfish] Qt text diag: leg G — Editor.Text='{editorText.Replace("\n", "\\n")}' (expected 'ab\\nc'; Return inserted a newline — multiline semantics, no completed trigger on SFOS) => " +
					(editorOk ? "OK — multiline Editor driven by the native Qt text-input path" : "FAIL"));
				QtHost.QtHostRuntime.GrabPng("/tmp/q9-g-editor.png");

				var ok = focusOk && typedOk && selOk && cursorOk && completedOk && modesOk && resetOk && editorOk;
				Console.Error.WriteLine($"[Sailfish] Qt text diag: ACCEPTANCE focus={(focusOk ? 1 : 0)} hw-text={(typedOk ? 1 : 0)} selection={(selOk ? 1 : 0)} cursor={(cursorOk ? 1 : 0)} " +
					$"completed={(completedOk ? 1 : 0)} modes={(modesOk ? 1 : 0)} mode-reset={(resetOk ? 1 : 0)} editor={(editorOk ? 1 : 0)} " +
					$"counters(writebacks={renderer.CursorWriteBacks} completed={renderer.CompletedFired} focus-transitions={renderer.FocusTransitions}) => " +
					(ok
						? "OK — the native Sailfish keyboard/text-input path drives MAUI Entry/Editor; no SDL soft keyboard involved"
						: "FAIL — see the leg lines above"));
				Console.Error.WriteLine("[Sailfish] Qt text diag: 15s compositor window — Maliit VKB is open on the focused Editor; run tools/sf screenshot for the VKB truth");
				dispatcher.DispatchDelayed(TimeSpan.FromSeconds(15), () => ContinueAfterQtTextDiagnostics(renderer, dispatcher));
			});
		});
	}

	// The text leg sits between the bridge and geometry legs; run whatever is armed next, else shut down.
	private void ContinueAfterQtTextDiagnostics(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		if (_qtGeometryDiag)
		{
			RunQtGeometryDiagnostics(renderer, dispatcher);   // shutdown follows the geometry leg
			return;
		}
		if (_qtInputDiag && _qtInputRouter is not null)
		{
			RunQtInputDiagnostics(renderer, dispatcher, _qtInputRouter);   // shutdown follows the input leg
			return;
		}
		Console.Error.WriteLine("[Sailfish] Qt diag: text diag done; auto-shutdown requested");
		QtHost.QtHostRuntime.Shutdown();
	}


	// Geometry diag: every host's MAUI absolute rect (dp, root space) must match the Qt scene readback within 1 dp, via the single
	// QtHostUnits conversion. The Qt window/screen report (px, dpr, orientation) is logged as environment evidence.

	private void RunQtGeometryDiagnostics(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		Console.Error.WriteLine("[Sailfish] Qt geometry diag: collecting evidence (Qt screen info + MAUI-vs-native bounds)...");
		var screenInfo = QtHost.QtHostRuntime.ScreenInfo();
		Console.Error.WriteLine($"[Sailfish] Qt geometry diag: QT SCREEN/WINDOW INFO {(string.IsNullOrEmpty(screenInfo) ? "(unavailable)" : screenInfo)}");
		Console.Error.WriteLine($"[Sailfish] Qt geometry diag: window report — {renderer.LastWindowGeometryReport}");
		// One more reconcile so the geometry push reflects the final state.
		renderer.Render();
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(400), () => VerifyQtGeometry(renderer, dispatcher));
	}

	private void VerifyQtGeometry(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		int pass = 0, fail = 0;
		var worst = 0.0;
		// The reconcile leg pins flickY=42: pushed rects are root-space, but the Qt readback is on-screen, shifted by the flickable's contentY.
		// Normalize like TryHitTest (root = on-screen + flickY); a no-op at flickY=0.
		var flickYQt = DiagQml.EvalNum("pageStack.currentPage.flickY", 0);
		var flickYDp = QtHost.QtHostUnits.ToLogical(flickYQt);
		var scrollDump = QtHost.QtHostRuntime.Eval(
			"(function(){var p=pageStack.currentPage;if(!p||!p.__diagDump)return 'n/a';var d=JSON.parse(p.__diagDump());" +
			"return JSON.stringify({flickY:d.flickY,originY:d.originY,topMargin:d.topMargin,contentItemY:d.contentItemY,canvasSceneY:d.canvasSceneY,pageSceneY:d.pageSceneY});})()");
		Console.Error.WriteLine($"[Sailfish] Qt geometry diag: scroll state {scrollDump}");
		if (flickYDp > 0.5)
			Console.Error.WriteLine($"[Sailfish] Qt geometry diag: flickable scrolled flickY={flickYQt:F0}qt ({flickYDp:F1}dp) — " +
				"Qt readback compensated back to root space (the same Q10 scroll translation hit-testing applies)");
		foreach (var host in renderer.CurrentHosts)
		{
			if (!host.IsAttached)
				continue;
			if (!QtHost.QtHostRuntime.TryItemGeometry(host.NativeHandle, out var scene))
			{
				fail++;
				Console.Error.WriteLine($"[Sailfish] Qt geometry diag: {host} readback FAILED (dead handle)");
				continue;
			}
			var onScreen = QtHost.QtHostUnits.ToLogical(scene); // Qt scene units → dp (single conversion, inverted)
			var native = new Microsoft.Maui.Graphics.Rect(onScreen.X, onScreen.Y + flickYDp, onScreen.Width, onScreen.Height);
			var maui = host.MauiLogicalBounds;                  // dp, root coordinate space
			var delta = Math.Max(Math.Max(Math.Abs(native.X - maui.X), Math.Abs(native.Y - maui.Y)),
				Math.Max(Math.Abs(native.Width - maui.Width), Math.Abs(native.Height - maui.Height)));
			var ok = delta <= 1.0;
			if (ok) pass++; else fail++;
			if (delta > worst) worst = delta;
			Console.Error.WriteLine($"[Sailfish] Qt geometry diag: {host} maui=({maui.X:F1},{maui.Y:F1} {maui.Width:F1}x{maui.Height:F1})dp " +
				$"qt=({native.X:F1},{native.Y:F1} {native.Width:F1}x{native.Height:F1})dp Δ={delta:F2} => {(ok ? "OK" : "FAIL")}");
		}
		Console.Error.WriteLine($"[Sailfish] Qt geometry diag: counters geometry-applied={renderer.GeometryApplied} geometry-failed={renderer.GeometryFailed}");
		Console.Error.WriteLine($"[Sailfish] Qt geometry diag: ACCEPTANCE matched={pass} mismatched={fail} worstΔ={worst:F2}dp => " +
			(fail == 0 && pass > 0
				? "OK — known MAUI rectangles have identical logical bounds before/after migration; final pixels produced by Qt Quick"
				: "FAIL — MAUI and Qt scene geometry diverge"));
		var grabRc = QtHost.QtHostRuntime.GrabPng("/tmp/q7-geometry.png");
		Console.Error.WriteLine($"[Sailfish] Qt geometry diag: screenshot rc={grabRc} -> /tmp/q7-geometry.png");
		if (_qtInputDiag && _qtInputRouter is not null)
		{
			RunQtInputDiagnostics(renderer, dispatcher, _qtInputRouter);   // shutdown follows the input leg
			return;
		}
		Console.Error.WriteLine("[Sailfish] Qt diag: geometry diag done; auto-shutdown requested");
		QtHost.QtHostRuntime.Shutdown();
	}

	// Page diag: a complete MAUI page (TextPage, 25 labels in a ScrollView) end-to-end through the Qt pipeline. Legs: A navigation push,
	// B label semantics + geometry for every host, C managed scroll, D injected-drag scroll write-back, E hardware-Back pop.
	// Qt text measurement (no SDL_ttf) is armed at startup for this cycle.

	// Diagnostic pages live in the application assembly, which the backend can't reference, so the app registers factories
	// at startup (no trim-unsafe assembly scanning). An unregistered leg fails loudly.
	private static readonly Dictionary<string, Func<Page>> _diagPageFactories = new(StringComparer.Ordinal);

	/// <summary>Trim-safe stand-in for <c>new Binding(".")</c> on immutable string items (path Bindings need reflection).
	/// The collection leg's INCC Replace check covers the update path.</summary>
	private static Label SelfTextLabel()
	{
		var label = new Label();
		label.BindingContextChanged += (_, _) => label.Text = label.BindingContext as string ?? string.Empty;
		return label;
	}

	/// <summary>Registers a diagnostic page factory under a leg key ("page",
	/// "shapes", "visual"); the application calls this at startup.</summary>
	internal static void RegisterDiagnosticPage(string leg, Func<Page> factory) =>
		_diagPageFactories[leg] = factory;

	private Page? CreateDiagnosticPage(string leg)
	{
		if (_diagPageFactories.TryGetValue(leg, out var factory))
			return factory();
		Console.Error.WriteLine($"[Sailfish] Qt diag: leg '{leg}' has no registered page factory (SailfishDiagnostics.RegisterPage) — leg skipped; auto-shutdown");
		return null;
	}

	private void RunQtPageDiagnostics(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var pageInstance = CreateDiagnosticPage("page");
		var nav = RootNav;
		if (pageInstance is null || nav is null)
		{
			Console.Error.WriteLine($"[Sailfish] Qt page diag: cannot resolve target page (page={pageInstance?.GetType().Name ?? "?"}, nav={nav is not null}) — leg skipped; auto-shutdown");
			QtHost.QtHostRuntime.Shutdown();
			return;
		}

		// The rendered-report listener for this cycle: stage 1 awaits the push
		// (title change), stage 4 awaits the Back pop (title back).
		QtHost.QtHostRuntime.QmlEvent += (name, payload) =>
		{
			if (name != "rendered")
				return;
			var title = ExtractRenderedTitle(payload);
			if (title is null)
				return;
			if (_qtPageStage == 1 && title != _qtPageReturnTitle)
			{
				_qtPageStage = 2;
				Console.Error.WriteLine($"[Sailfish] Qt page diag: leg A — '{title}' pushed through MAUI navigation and rendered by the Qt pipeline");
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(400), () => VerifyQtPageRender(renderer, dispatcher));
			}
			else if (_qtPageStage == 4 && title == _qtPageReturnTitle)
			{
				_qtPageStage = 5;
				VerifyQtPageBack(renderer, dispatcher);
			}
		};

		_qtPageStage = 1;
		Console.Error.WriteLine($"[Sailfish] Qt page diag: leg A — pushing {pageInstance.GetType().FullName} through NavigationPage.PushAsync");
		_ = nav.PushAsync(pageInstance);
	}

	// --- Shapes / GraphicsView / image legs ---

	/// <summary>Shapes leg A: push the shapes &amp; images gallery; the render of the new title starts leg B.</summary>
	private void RunQtShapesDiagnostics(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var pageInstance = CreateDiagnosticPage("shapes");
		var nav = RootNav;
		if (pageInstance is null || nav is null)
		{
			Console.Error.WriteLine($"[Sailfish] Qt shapes diag: cannot resolve target page (page={pageInstance?.GetType().Name ?? "?"}, nav={nav is not null}) — leg skipped; auto-shutdown");
			QtHost.QtHostRuntime.Shutdown();
			return;
		}

		QtHost.QtHostRuntime.QmlEvent += (name, payload) =>
		{
			if (name != "rendered")
				return;
			var title = ExtractRenderedTitle(payload);
			if (title is null || _qtShapesStage != 1 || title == _qtShapesReturnTitle)
				return;
			_qtShapesStage = 2;
			Console.Error.WriteLine($"[Sailfish] Qt shapes diag: leg A — '{title}' pushed through MAUI navigation and rendered by the Qt pipeline");
			// Canvas adapters paint asynchronously (Cooperative strategy): give them a frame before arming the pixel readback.
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(700), () => ArmQtShapesHash(renderer, dispatcher));
		};

		_qtShapesStage = 1;
		Console.Error.WriteLine($"[Sailfish] Qt shapes diag: leg A — pushing {pageInstance.GetType().FullName} through NavigationPage.PushAsync");
		_ = nav.PushAsync(pageInstance);
	}

	/// <summary>Shapes leg B: inventory the vector adapters and arm the pixel readback. mauiWantHash switches each Canvas to the
	/// Image render target (Qt 5.6 renders Threaded/Cooperative canvases only into an FBO) and hashes alpha via getImageData.</summary>
	private void ArmQtShapesHash(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var hosts = renderer.CurrentHosts.Where(h => h.IsAttached).ToList();
		var shapes = hosts.Where(h => h.QmlUri == QtHost.QtHostShapes.AdapterUri).ToList();
		var canvases = hosts.Where(h => h.QmlUri == QtHost.QtHostGraphics.AdapterUri).ToList();
		var images = hosts.Where(h => h.QmlUri == QtHost.QtHostImages.AdapterUri).ToList();
		_qtShapesChecks.Check($"inventory: hosts={hosts.Count} shape={shapes.Count} canvas={canvases.Count} image={images.Count} " +
		            "(gallery: Rectangle/Ellipse/Line/Polygon/Path + GraphicsView + 4 aspect modes)",
			shapes.Count >= 5 && canvases.Count == 1 && images.Count >= 4);

		foreach (var host in shapes.Concat(canvases))
		{
			var rc = QtHost.QtHostRuntime.SetProperty(host.NativeHandle, "mauiWantHash", "true");
			if (rc != 0)
				Console.Error.WriteLine($"[Sailfish] Qt shapes diag: mauiWantHash push failed rc={rc} ({QtHost.QtHostRuntime.LastErrorText})");
		}
		Console.Error.WriteLine($"[Sailfish] Qt shapes diag: leg B — pixel readback armed on {shapes.Count + canvases.Count} Canvas adapters");
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200),
			() => VerifyQtShapesPaint(shapes, canvases, images));
	}

	/// <summary>Shapes leg C: every Canvas adapter really painted pixels, the IDrawable stream was replayed and Qt decoded all four Aspect modes.</summary>
	private void VerifyQtShapesPaint(List<QtHost.NativeElementHost> shapes,
	                                 List<QtHost.NativeElementHost> canvases,
	                                 List<QtHost.NativeElementHost> images)
	{
		var painted = 0;
		foreach (var host in shapes)
		{
			var kind = HostProp(host, "mauiKind");
			var ops = HostPropNum(host, "mauiPaintedOps");
			var hash = HostProp(host, "mauiPixelHash");
			var pixels = PaintedPixels(hash);
			if (pixels > 0)
				painted++;
			Console.Error.WriteLine($"[Sailfish] Qt shapes diag: shape kind={kind} ops={ops} hash={hash}");
		}
		_qtShapesChecks.Check($"shapes painted pixels: {painted}/{shapes.Count} (PathF ops → Canvas fill/stroke, hash != 0)",
			shapes.Count > 0 && painted == shapes.Count);

		var executed = 0.0;
		var skipped = 0.0;
		var canvasPixels = 0;
		foreach (var host in canvases)
		{
			executed = HostPropNum(host, "mauiExecutedOps");
			skipped = HostPropNum(host, "mauiSkippedOps");
			var hash = HostProp(host, "mauiPixelHash");
			canvasPixels = PaintedPixels(hash);
			Console.Error.WriteLine($"[Sailfish] Qt shapes diag: canvas executed={executed} skipped={skipped} hash={hash}");
		}
		_qtShapesChecks.Check($"IDrawable stream replayed in Qt: executed={executed} skipped={skipped} pixels={canvasPixels} " +
		            "(grid + filled path + circle + text; the skips are the Qt 5.6 gaps: line dash, clip subtraction, images)",
			executed >= 20 && canvasPixels > 0);

		// The extended IDrawable stream carries a LinearGradientPaint fill (fpaint) and a ClipRectangle (clipr); the adapter counts both.
		var gradOps = canvases.Count > 0 ? HostPropNum(canvases[0], "mauiGradOps") : 0;
		var clipOps = canvases.Count > 0 ? HostPropNum(canvases[0], "mauiClipOps") : 0;
		_qtShapesChecks.Check($"Q20 gradient+clip cells: fpaint (LinearGradientPaint) executed {gradOps:F0}>=1 and clipr (ClipRectangle) executed {clipOps:F0}>=1 inside the Qt canvas paint pass (executed={executed} skipped={skipped})",
			gradOps >= 1 && clipOps >= 1);

		var loaded = images.Count(h => HostProp(h, "mauiLoaded") == "true" && HostPropNum(h, "mauiNaturalWidth") > 0);
		var aspects = string.Join(",", images.Select(h => HostProp(h, "mauiAspect")));
		_qtShapesChecks.Check($"images decoded by Qt: {loaded}/{images.Count} aspects=[{aspects}] (Fill/AspectFit/AspectFill/Center)",
			images.Count >= 4 && loaded == images.Count);

		FinishQtShapesDiagnostics();
	}

	/// <summary>The painted-pixel count of a mauiPixelHash witness ("hash:painted/total").</summary>
	private static int PaintedPixels(string hash)
	{
		var parts = (hash ?? string.Empty).Split(':');
		if (parts.Length < 2)
			return 0;
		var counts = parts[1].Split('/');
		return int.TryParse(counts[0], out var painted) ? painted : 0;
	}


	private void FinishQtShapesDiagnostics()
	{
		var failed = _qtShapesChecks.Failed;
		Console.Error.WriteLine($"[Sailfish] QT SHAPES DIAG: {_qtShapesChecks.Count - failed}/{_qtShapesChecks.Count} checks OK — " +
		                        "MAUI Shapes + GraphicsView(IDrawable) + Image aspect modes painted through QML Canvas adapters inside the Qt scene graph" +
		                        (failed == 0 ? " => OK" : $" => {failed} FAILED"));
		_qtShapesStage = 3;
		// MAUI_SAILFISH_QT_HOST_SHAPES_HOLD=1 stays on the gallery instead of shutting down, for a screenshot.
		if (SailfishEnv.Flag("MAUI_SAILFISH_QT_HOST_SHAPES_HOLD"))
		{
			Console.Error.WriteLine("[Sailfish] Qt shapes diag: HOLD — staying on the rendered gallery for a screenshot (no auto-shutdown)");
			return;
		}
		QtHost.QtHostRuntime.Shutdown();
	}

	// --- Visual state legs ---

	/// <summary>Visual leg A: push the Visual gallery; the render of the new title starts the verification legs.</summary>
	private void RunQtVisualDiagnostics(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var pageInstance = CreateDiagnosticPage("visual");
		var nav = RootNav;
		if (pageInstance is null || nav is null)
		{
			Console.Error.WriteLine($"[Sailfish] Qt visual diag: cannot resolve target page (page={pageInstance?.GetType().Name ?? "?"}, nav={nav is not null}) — leg skipped; auto-shutdown");
			QtHost.QtHostRuntime.Shutdown();
			return;
		}

		QtHost.QtHostRuntime.QmlEvent += (name, payload) =>
		{
			if (name != "rendered")
				return;
			var title = ExtractRenderedTitle(payload);
			if (title is null || _qtVisualStage != 1 || title == _qtVisualReturnTitle)
				return;
			_qtVisualStage = 2;
			Console.Error.WriteLine($"[Sailfish] Qt visual diag: leg A — '{title}' pushed through MAUI navigation and rendered by the Qt pipeline");
			// Let the reconcile poll + geometry pass settle the generic state.
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(600), () => VerifyQtVisualState(renderer, dispatcher));
		};

		_qtVisualStage = 1;
		Console.Error.WriteLine($"[Sailfish] Qt visual diag: leg A — pushing {pageInstance.GetType().FullName} through NavigationPage.PushAsync");
		_ = nav.PushAsync(pageInstance);
	}

	/// <summary>Visual leg B: Opacity, Rotation/Scale (TopLeft origin) and ZIndex read back from the native QQuickItems.</summary>
	private void VerifyQtVisualState(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var hosts = renderer.CurrentHosts.Where(h => h.IsAttached).ToList();
		Console.Error.WriteLine($"[Sailfish] Qt visual diag: leg B — verifying the generic state on {hosts.Count} attached hosts");

		var faded = hosts.FirstOrDefault(h => h.Element is Microsoft.Maui.Controls.BoxView b && Math.Abs(b.Opacity - 0.35) < 0.01);
		var fadedNative = HostPropNum(faded, "opacity");
		_qtVisualChecks.Check($"Opacity: BoxView 0.35 -> native opacity={fadedNative:F2} (cascade computed managed-side — the flat canvas has no QML parent chain)",
			faded is not null && Math.Abs(fadedNative - 0.35) < 0.01);

		var rotated = hosts.FirstOrDefault(h => h.Element is Microsoft.Maui.Controls.BoxView b && Math.Abs(b.Rotation - 18) < 0.01);
		var rotNative = HostPropNum(rotated, "rotation");
		var origin = HostProp(rotated, "transformOrigin");
		_qtVisualChecks.Check($"Rotation: BoxView 18deg -> native rotation={rotNative:F1} transformOrigin={origin} (accumulated matrix decomposed around TopLeft; the enum reads back as its ordinal, QQuickItem TopLeft=0)",
			rotated is not null && Math.Abs(rotNative - 18) < 0.2 && origin is "TopLeft" or "0");

		var scaled = hosts.FirstOrDefault(h => h.Element is Microsoft.Maui.Controls.BoxView b && Math.Abs(b.Scale - 0.6) < 0.01 && b.AnchorX == 0);
		var scaleNative = HostPropNum(scaled, "scale");
		_qtVisualChecks.Check($"Scale+Anchor: BoxView scale 0.6 anchor(0,0) -> native scale={scaleNative:F2} (the anchor pivot rides the pushed x/y — TopLeft origin)",
			scaled is not null && Math.Abs(scaleNative - 0.6) < 0.01);

		var zRed = hosts.FirstOrDefault(h => h.Element is Microsoft.Maui.Controls.BoxView b && b.ZIndex == 1);
		var zBlue = hosts.FirstOrDefault(h => h.Element is Microsoft.Maui.Controls.BoxView b && b.ZIndex == 5);
		var zRedNative = HostProp(zRed, "z");
		var zBlueNative = HostProp(zBlue, "z");
		_qtVisualChecks.Check($"ZIndex: red z={zRedNative} blue z={zBlueNative} -> native QQuickItem z (paint + hit-test stacking; the managed hit-test mirrors it)",
			zRed is not null && zBlue is not null && zRedNative == "1" && zBlueNative == "5");

		RunQtVisualEnabledLeg(renderer, dispatcher);
	}

	/// <summary>Visual leg B2: two injected fingers spread over the PinchGestureRecognizer box; it grows.</summary>
	private void RunQtVisualPinchLeg(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var host = renderer.CurrentHosts.FirstOrDefault(h => h.IsAttached && h.Element is Microsoft.Maui.Controls.BoxView b &&
			b.GestureRecognizers.OfType<PinchGestureRecognizer>().Any());
		var box = host?.Element as Microsoft.Maui.Controls.BoxView;
		if (host is null || box is null || !QtHost.QtHostRuntime.TryItemGeometry(host.NativeHandle, out var g))
		{
			_qtVisualChecks.Check("Pinch: the gallery's pinch box found", false);
			FinishQtVisualDiagnostics();
			return;
		}
		var log = FindPage(box) is { } page
			? page.GetType().GetProperty("PinchLog")?.GetValue(page) as System.Collections.IList
			: null;
		var cx = g.X + g.Width / 2;
		var cy = g.Y + g.Height / 2;
		renderer.TryHitTest(QtHost.QtHostUnits.ToLogical(cx), QtHost.QtHostUnits.ToLogical(cy), out var hitHost);
		Console.Error.WriteLine($"[Sailfish] Qt visual diag: pinch box native {g.X:F0},{g.Y:F0} {g.Width:F0}x{g.Height:F0}, " +
			$"managed rect {host.MauiLogicalBounds} clip {host.HitClipDp?.ToString() ?? "-"}; hit at the centre: {hitHost}");
		var ids = new[] { 1, 2 };
		void Touch(double half, int state) =>
			QtHost.QtHostRuntime.InjectTouch(ids, new[] { cx - half, cy, cx + half, cy }, new[] { state, state });
		Touch(30, 1);
		var step = 0;
		void Spread()
		{
			step++;
			Touch(30 + step * 10, 2);
			if (step < 6)
			{
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(30), Spread);
				return;
			}
			Touch(90, 8);
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(500), () =>
			{
				var statuses = log is null ? "(no log)" : string.Join(",", log.Cast<object>());
				_qtVisualChecks.Check($"Pinch: two fingers 60→180 px → {statuses}, box scale {box.Scale:F2} (~3, clamped)",
					statuses.StartsWith("Started,Running", StringComparison.Ordinal) && statuses.EndsWith("Completed", StringComparison.Ordinal) &&
					box.Scale > 2);
				Shot(dispatcher, "visual-pinch", () =>
				{
					box.Scale = 1;
					FinishQtVisualDiagnostics();
				});
			});
		}
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(30), Spread);

		static Page? FindPage(Element element)
		{
			for (var e = element; e is not null; e = e.Parent)
				if (e is Page p)
					return p;
			return null;
		}
	}

	/// <summary>Injects a real-Qt tap (press+release) at the center of a host's
	/// native scene geometry.</summary>
	private static void InjectQtTapAtHost(QtHost.NativeElementHost host)
	{
		if (!QtHost.QtHostRuntime.TryItemGeometry(host.NativeHandle, out var scene))
		{
			Console.Error.WriteLine("[Sailfish] Qt visual diag: tap inject — no native geometry");
			return;
		}
		var cx = scene.X + scene.Width / 2;
		var cy = scene.Y + scene.Height / 2;
		Console.Error.WriteLine($"[Sailfish] Qt visual diag: injecting a tap at {cx:F0},{cy:F0} (scene units)");
		DiagQml.Tap(cx, cy);
	}

	/// <summary>Visual leg C: a disabled Button must be enabled=false natively and swallow an injected tap; re-enabling restores both.</summary>
	private void RunQtVisualEnabledLeg(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var buttonHost = renderer.CurrentHosts.FirstOrDefault(h => h.IsAttached && h.QmlUri == "button" &&
			h.Element is Button b && (b.Text ?? string.Empty).StartsWith("state:", StringComparison.Ordinal));
		if (buttonHost?.Element is not Button button)
		{
			_qtVisualChecks.Check("IsEnabled: the gallery's state Button found", false);
			FinishQtVisualDiagnostics();
			return;
		}

		var textBefore = button.Text;
		button.IsEnabled = false;
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(450), () =>
		{
			var enabled = HostProp(buttonHost, "enabled");
			_qtVisualChecks.Check($"IsEnabled=false -> native enabled={enabled} (immediate push; a disabled ANCESTOR would cascade the same way managed-side)",
				enabled == "false");
			InjectQtTapAtHost(buttonHost);
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(400), () =>
			{
				_qtVisualChecks.Check($"disabled tap blocked: text '{textBefore}' -> '{button.Text}' (Clicked must NOT fire)",
					button.Text == textBefore);
				button.IsEnabled = true;
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(450), () =>
				{
					var enabledAgain = HostProp(buttonHost, "enabled");
					_qtVisualChecks.Check($"IsEnabled=true -> native enabled={enabledAgain} (restored)", enabledAgain == "true");
					InjectQtTapAtHost(buttonHost);
					dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(400), () =>
					{
						_qtVisualChecks.Check($"enabled tap fires Clicked: text '{textBefore}' -> '{button.Text}'",
							button.Text != textBefore);
						RunQtVisualVsmLeg(renderer, dispatcher, buttonHost, button);
					});
				});
			});
		});
	}

	/// <summary>Visual leg D: VSM setters are plain BindableProperty writes, so they reach the Silica Button through the ordinary property diff.</summary>
	private void RunQtVisualVsmLeg(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, QtHost.NativeElementHost buttonHost, Button button)
	{
		VisualStateManager.GoToState(button, VisualStateManager.CommonStates.Disabled);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(450), () =>
		{
			var bgDisabled = HostProp(buttonHost, "backgroundColor");
			var fgDisabled = HostProp(buttonHost, "color");
			_qtVisualChecks.Check($"VSM Disabled setters crossed the bridge: native backgroundColor={bgDisabled} color={fgDisabled} (expect #ff222222/#ff666666)",
				bgDisabled.Contains("222222", StringComparison.OrdinalIgnoreCase) &&
				fgDisabled.Contains("666666", StringComparison.OrdinalIgnoreCase));
			VisualStateManager.GoToState(button, VisualStateManager.CommonStates.Normal);
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(450), () =>
			{
				var bgNormal = HostProp(buttonHost, "backgroundColor");
				var fgNormal = HostProp(buttonHost, "color");
				_qtVisualChecks.Check($"VSM GoToState(Normal): native backgroundColor={bgNormal} color={fgNormal} (expect #ff37474f/#ffffffff)",
					bgNormal.Contains("37474F", StringComparison.OrdinalIgnoreCase) &&
					fgNormal.Contains("ffffff", StringComparison.OrdinalIgnoreCase));
				RunQtVisualVisibilityLeg(renderer, dispatcher);
			});
		});
	}

	/// <summary>Visual leg E: the geometry pass's "vis" flag must hide and show the native item.</summary>
	private void RunQtVisualVisibilityLeg(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var label = HostByText(renderer, "ZIndex overlap");
		if (label?.Element is not Label labelElement)
		{
			_qtVisualChecks.Check("IsVisible: the gallery label found", false);
			FinishQtVisualDiagnostics();
			return;
		}
		labelElement.IsVisible = false;
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(450), () =>
		{
			var vis = HostProp(label, "visible");
			_qtVisualChecks.Check($"IsVisible=false -> native visible={vis} (geometry pass 'vis' flag)", vis == "false");
			labelElement.IsVisible = true;
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(450), () =>
			{
				var vis2 = HostProp(label, "visible");
				_qtVisualChecks.Check($"IsVisible=true -> native visible={vis2} (restored)", vis2 == "true");
				RunQtVisual3DLeg(renderer, dispatcher);
			});
		});
	}


	/// <summary>Visual leg F: the gallery's last row, scrolled into view: RotationY and ScaleX≠ScaleY reach the item as a
	/// Matrix4x4 (Qt maps the corners through it), then a pinch.</summary>
	private void RunQtVisual3DLeg(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var tiltedBox = renderer.CurrentHosts.Select(h => h.Element).OfType<Microsoft.Maui.Controls.BoxView>().FirstOrDefault(b => b.RotationY == 50);
		Element? scroller = tiltedBox;
		while (scroller is not null and not ScrollView)
			scroller = scroller.Parent;
		if (tiltedBox?.Parent is View row && scroller is ScrollView scroll)
			_ = scroll.ScrollToAsync(row, ScrollToPosition.Center, false);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(700), () =>
		{
			var hosts = renderer.CurrentHosts.Where(h => h.IsAttached).ToList();
			// RotationY and ScaleX≠ScaleY reach the item as a Matrix4x4: Qt maps the corners through it.
			string Corners(QtHost.NativeElementHost? host) => host is null ? "" : QtHost.QtHostRuntime.Eval(
				"(function(i){if(!i)return '';var w=i.width,h=i.height,p=[[0,0],[w,0],[0,h],[w,h]],o=[];" +
				"for(var k=0;k<4;k++){var q=i.mapToItem(null,p[k][0],p[k][1]);o.push(q.x.toFixed(1),q.y.toFixed(1));}" +
				"return o.join(',')+','+w.toFixed(1)+','+h.toFixed(1);})(" + DiagQml.ItemJs(host) + ")");
			double[] Parse(string text) => text.Split(',').Select(v => DiagQml.Num(v)).ToArray();
			var tilted = hosts.FirstOrDefault(h => h.Element is Microsoft.Maui.Controls.BoxView { RotationY: 50 });
			var t = Parse(Corners(tilted));
			var tiltOk = t.Length == 10 && Math.Abs(t[2] - t[0]) < 0.8 * t[8] &&      // foreshortened width
			             Math.Abs((t[7] - t[3]) - (t[5] - t[1])) > 2;                  // the far edge is shorter: perspective
			_qtVisualChecks.Check($"RotationY 50: corners {string.Join(",", t.Select(v => v.ToString("F0")))} — narrower and in perspective", tiltOk);
			var stretched = hosts.FirstOrDefault(h => h.Element is Microsoft.Maui.Controls.BoxView { ScaleX: 1.6 });
			var st = Parse(Corners(stretched));
			var stretchOk = st.Length == 10 && Math.Abs((st[2] - st[0]) - 1.6 * st[8]) < 2 && Math.Abs((st[5] - st[1]) - 0.6 * st[9]) < 2;
			_qtVisualChecks.Check($"ScaleX 1.6 / ScaleY 0.6: mapped {st.ElementAtOrDefault(2) - st.ElementAtOrDefault(0):F0}×{st.ElementAtOrDefault(5) - st.ElementAtOrDefault(1):F0} " +
				$"of {st.ElementAtOrDefault(8):F0}×{st.ElementAtOrDefault(9):F0}", stretchOk);
			Shot(dispatcher, "visual-3d", () => RunQtVisualPinchLeg(renderer, dispatcher));
		});
	}

	private void FinishQtVisualDiagnostics()
	{
		var failed = _qtVisualChecks.Failed;
		Console.Error.WriteLine($"[Sailfish] QT VISUAL DIAG: {_qtVisualChecks.Count - failed}/{_qtVisualChecks.Count} checks OK — " +
		                        "IsEnabled/Opacity/ZIndex/transforms/visibility/VSM cross to the native QQuickItem state" +
		                        (failed == 0 ? " => OK" : $" => {failed} FAILED"));
		_qtVisualStage = 9;
		// MAUI_SAILFISH_QT_HOST_VISUAL_HOLD=1 stays on the gallery instead of shutting down, for a screenshot.
		if (SailfishEnv.Flag("MAUI_SAILFISH_QT_HOST_VISUAL_HOLD"))
		{
			Console.Error.WriteLine("[Sailfish] Qt visual diag: HOLD — staying on the rendered gallery for a screenshot (no auto-shutdown)");
			return;
		}
		QtHost.QtHostRuntime.Shutdown();
	}


	private static QtHost.NativeElementHost? HostByText(QtHost.QtHostPageRenderer renderer, string prefix) =>
		renderer.CurrentHosts.FirstOrDefault(h => h.IsAttached && h.QmlUri == "label" &&
			h.Element is Label l && (l.Text ?? string.Empty).StartsWith(prefix, StringComparison.Ordinal));

	private static string HostProp(QtHost.NativeElementHost? host, string name) =>
		host is { IsAttached: true } ? QtHost.QtHostRuntime.GetProperty(host.NativeHandle, name) : "(host missing)";

	private static double HostPropNum(QtHost.NativeElementHost? host, string name) =>
		DiagQml.Num(HostProp(host, name));

	/// <summary>Leg B: adapter-state + geometry verification of the rendered page.</summary>
	private void VerifyQtPageRender(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var hosts = renderer.CurrentHosts.Where(h => h.IsAttached).ToList();
		var labels = hosts.Count(h => h.QmlUri == "label");
		// Containers own hosts too (the labels nest inside them).
		var containers = hosts.Count(h => h.QmlUri is "content-view" or "grid" or "stack-layout" or "border" or "scroll-view");
		_qtPageChecks.Check($"inventory: hosts={hosts.Count} labels={labels} containers={containers} (every element mapped, >= 20 labels)",
			hosts.Count > 0 && labels + containers == hosts.Count && labels >= 20 && containers >= 1);

		// Geometry at scroll offset 0: MAUI logical bounds (dp, root space) vs the Qt scene readback.
		int pass = 0, fail = 0;
		var worst = 0.0;
		foreach (var host in hosts)
		{
			// Synthetic page-level hosts (pulleys, context menu, docked panels, drawers) are positioned by Silica and have no MAUI bounds.
			if (host.Id.StartsWith("synth-", StringComparison.Ordinal))
				continue;
			if (!QtHost.QtHostRuntime.TryItemGeometry(host.NativeHandle, out var scene))
			{
				fail++;
				continue;
			}
			var native = QtHost.QtHostUnits.ToLogical(scene);
			var maui = host.MauiLogicalBounds;
			var delta = Math.Max(Math.Max(Math.Abs(native.X - maui.X), Math.Abs(native.Y - maui.Y)),
				Math.Max(Math.Abs(native.Width - maui.Width), Math.Abs(native.Height - maui.Height)));
			if (delta <= 1.0) pass++; else fail++;
			if (delta > worst) worst = delta;
			if (delta > 1.0)
				Console.Error.WriteLine($"[Sailfish] Qt page diag: GEOM FAIL {host} maui=({maui.X:F1},{maui.Y:F1} {maui.Width:F1}x{maui.Height:F1})dp " +
					$"qt=({native.X:F1},{native.Y:F1} {native.Width:F1}x{native.Height:F1})dp Δ={delta:F2}");
		}
		_qtPageChecks.Check($"geometry: matched={pass} mismatched={fail} worstΔ={worst:F2}dp (MAUI layout == Qt scene)", fail == 0 && pass >= 20);

		VerifyQtPageTextSemantics(renderer);

		var grabRc = QtHost.QtHostRuntime.GrabPng("/tmp/q10-textpage-top.png");
		Console.Error.WriteLine($"[Sailfish] Qt page diag: screenshot rc={grabRc} -> /tmp/q10-textpage-top.png");

		// Leg C: managed scroll push (ScrollToAsync → setMauiScroll → contentY).
		var scroll = renderer.ScrollContext;
		if (scroll is null)
		{
			_qtPageChecks.Check("scroll context: page-level ScrollView tracked by the renderer", false);
			FinishQtPageDiagnostics(renderer, dispatcher);
			return;
		}
		_qtPageChecks.Check("scroll context: page-level ScrollView tracked by the renderer", true);
		// A deep host must move up by the scroll offset inside the ScrollView's flickable.
		_qtPageDeep = renderer.CurrentHosts
			.Where(h => h.IsAttached && h.MauiLogicalBounds.Y > 300)
			.OrderByDescending(h => h.MauiLogicalBounds.Y)
			.FirstOrDefault();
		_qtPageDeepSceneY = _qtPageDeep is not null && QtHost.QtHostRuntime.TryItemGeometry(_qtPageDeep.NativeHandle, out var before)
			? QtHost.QtHostUnits.ToLogical(before).Y
			: double.NaN;
		Console.Error.WriteLine("[Sailfish] Qt page diag: leg C — ScrollView.ScrollToAsync(0, 300) → scroll-view host → native contentY");
		_ = scroll.ScrollToAsync(0, 300, animated: false);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(700), () => VerifyQtPageScroll(renderer, dispatcher, scroll));
	}

	/// <summary>Leg B text semantics: the full label property set read back through the native handles.</summary>
	private void VerifyQtPageTextSemantics(QtHost.QtHostPageRenderer renderer)
	{
		var density = SailfishDisplay.Density;

		var wrapHost = HostByText(renderer, "Sailfish OS renders");
		// MauiLogicalBounds is already dp; no second conversion.
		var wrapW = wrapHost?.MauiLogicalBounds.Width ?? double.NaN;
		var wrapH = wrapHost?.MauiLogicalBounds.Height ?? double.NaN;
		_qtPageChecks.Check($"wrapping: 420dp column w={wrapW:F0}dp, multi-line h={wrapH:F0}dp>=40, wrapMode={HostProp(wrapHost, "wrapMode")}",
			HostProp(wrapHost, "mauiWrap") == "1" && Math.Abs(wrapW - 420) <= 3 && wrapH >= 40);

		_qtPageChecks.Check($"h-align center: horizontalAlignment={HostProp(HostByText(renderer, "center"), "horizontalAlignment")} (Text.AlignHCenter=4)",
			HostProp(HostByText(renderer, "center"), "mauiHAlign") == "4" &&
			HostProp(HostByText(renderer, "center"), "horizontalAlignment") == "4");

		var middleHost = HostByText(renderer, "middle");
		var middleH = middleHost?.MauiLogicalBounds.Height ?? double.NaN;
		_qtPageChecks.Check($"v-align middle in 90u box: h={middleH:F0}dp, verticalAlignment={HostProp(middleHost, "verticalAlignment")} (Text.AlignVCenter=128)",
			HostProp(middleHost, "mauiVAlign") == "128" && Math.Abs(middleH - 90) <= 2);

		_qtPageChecks.Check($"LineBreakMode: tail elide={HostProp(HostByText(renderer, "TailTruncation:"), "elide")} head={HostProp(HostByText(renderer, "HeadTruncation:"), "elide")} " +
		          $"middle={HostProp(HostByText(renderer, "MiddleTruncation:"), "elide")} charWrap={HostProp(HostByText(renderer, "CharacterWrap"), "wrapMode")} (3/1/2/3)",
			HostProp(HostByText(renderer, "TailTruncation:"), "mauiElide") == "3" &&
			HostProp(HostByText(renderer, "HeadTruncation:"), "mauiElide") == "1" &&
			HostProp(HostByText(renderer, "HeadTruncation:"), "mauiWrap") == "0" &&
			HostProp(HostByText(renderer, "MiddleTruncation:"), "mauiElide") == "2" &&
			HostProp(HostByText(renderer, "CharacterWrap"), "mauiWrap") == "3");

		_qtPageChecks.Check($"MaxLines=2: maximumLineCount={HostProp(HostByText(renderer, "First line."), "maximumLineCount")}",
			HostProp(HostByText(renderer, "First line."), "maximumLineCount") == "2");

		_qtPageChecks.Check($"fonts: bold={HostProp(HostByText(renderer, "Bold attribute"), "font.bold")} italic={HostProp(HostByText(renderer, "Italic attribute"), "font.italic")} " +
		          $"both={HostProp(HostByText(renderer, "Bold+Italic"), "font.bold")}/{HostProp(HostByText(renderer, "Bold+Italic"), "font.italic")}",
			HostProp(HostByText(renderer, "Bold attribute"), "font.bold") == "true" &&
			HostProp(HostByText(renderer, "Italic attribute"), "font.italic") == "true" &&
			HostProp(HostByText(renderer, "Bold+Italic"), "font.bold") == "true" &&
			HostProp(HostByText(renderer, "Bold+Italic"), "font.italic") == "true");

		var serif = HostProp(HostByText(renderer, "DejaVu Serif family"), "font.family");
		var mono = HostProp(HostByText(renderer, "DejaVu Sans Mono family"), "font.family");
		_qtPageChecks.Check($"families: serif='{serif}' mono='{mono}'",
			serif.Contains("DejaVu Serif", StringComparison.Ordinal) &&
			mono.Contains("DejaVu Sans Mono", StringComparison.Ordinal));

		var tracking = HostPropNum(HostByText(renderer, "character spacing 6"), "font.letterSpacing");
		var lineHeight = HostPropNum(HostByText(renderer, "line height 1.8"), "lineHeight");
		var pixelSize = HostPropNum(HostByText(renderer, "Wrapping (narrow column)"), "font.pixelSize");
		_qtPageChecks.Check($"metrics: letterSpacing={tracking:F1}px (6dp×{density:F2}), lineHeight={lineHeight:F2}, pixelSize={pixelSize:F0}px (16dp×{density:F2})",
			Math.Abs(tracking - 6 * density) <= 0.51 && Math.Abs(lineHeight - 1.8) < 0.001 &&
			Math.Abs(pixelSize - 16 * density) <= 0.51);

		_qtPageChecks.Check($"decorations: underline={HostProp(HostByText(renderer, "underlined text"), "font.underline")} strikeout={HostProp(HostByText(renderer, "strikethrough text"), "font.strikeout")}",
			HostProp(HostByText(renderer, "underlined text"), "font.underline") == "true" &&
			HostProp(HostByText(renderer, "strikethrough text"), "font.strikeout") == "true");

		var spanHost = renderer.CurrentHosts.FirstOrDefault(h => h.IsAttached && h.QmlUri == "label" &&
			h.Element is Label { FormattedText.Spans.Count: > 0 });
		var spanText = HostProp(spanHost, "text");
		_qtPageChecks.Check($"FormattedText spans: textFormat={HostProp(spanHost, "textFormat")} (RichText=1), html={(spanText.Length > 60 ? spanText[..60] + "…" : spanText)}",
			HostProp(spanHost, "mauiTextFormat") == "1" &&
			HostProp(spanHost, "textFormat") == "1" &&
			spanText.StartsWith("<span", StringComparison.Ordinal) &&
			spanText.Contains("font-weight:bold", StringComparison.Ordinal) &&
			spanText.Contains("text-decoration:underline", StringComparison.Ordinal) &&
			spanText.Contains("DejaVu Sans Mono", StringComparison.Ordinal));

		_qtPageChecks.Check($"label background: mauiBackground={HostProp(HostByText(renderer, "top"), "mauiBackground")} (#ff202030)",
			HostProp(HostByText(renderer, "top"), "mauiBackground") == "#ff202030");
	}

	/// <summary>Leg C verify: ScrollToAsync landed on the native contentY and moved the hosts; then leg D injects a drag that must
	/// scroll natively and write ScrollY back via "scroll-changed".</summary>
	private void VerifyQtPageScroll(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, ScrollView scroll)
	{
		var flickYText = QtHost.QtHostRuntime.Eval($"{PrimaryScrollItemJs(renderer)}.contentY");
		var flickY = DiagQml.Num(flickYText, 0);
		var expected = QtHost.QtHostUnits.ToQtUnits(300);
		var scrollYOk = Math.Abs(scroll.ScrollY - 300) <= 1;
		_qtPageChecks.Check($"managed scroll: MAUI ScrollY={scroll.ScrollY:F0}dp, native contentY={flickY:F0}qt (expected {expected:F0}qt)",
			scrollYOk && Math.Abs(flickY - expected) <= 2);

		// The deep host moved up in the scene and its root (hit-test) rect followed.
		var deep = _qtPageDeep;
		if (deep is not null && !double.IsNaN(_qtPageDeepSceneY) && QtHost.QtHostRuntime.TryItemGeometry(deep.NativeHandle, out var scene))
		{
			var sceneDp = QtHost.QtHostUnits.ToLogical(scene);
			var expectedY = _qtPageDeepSceneY - scroll.ScrollY;
			_qtPageChecks.Check($"scene translation: {deep} sceneY={sceneDp.Y:F1}dp == before {_qtPageDeepSceneY:F1} − {scroll.ScrollY:F0} scrolled = {expectedY:F1}dp, hit-test rect Y={deep.MauiLogicalBounds.Y:F1}dp",
				Math.Abs(sceneDp.Y - expectedY) <= 2 && Math.Abs(deep.MauiLogicalBounds.Y - sceneDp.Y) <= 2);
		}
		else
		{
			_qtPageChecks.Check("scene translation: deep host geometry readback", false);
		}

		var grabRc = QtHost.QtHostRuntime.GrabPng("/tmp/q10-textpage-scrolled.png");
		Console.Error.WriteLine($"[Sailfish] Qt page diag: screenshot rc={grabRc} -> /tmp/q10-textpage-scrolled.png");

		// Leg D: an injected drag over the content must scroll natively and write ScrollView.ScrollY back via "scroll-changed".
		var scrollBefore = scroll.ScrollY;
		var writeBacksBefore = renderer.ScrollWriteBacks;
		var pageW = DiagQml.EvalNum("pageStack.currentPage.width", 0);
		var topInset = DiagQml.EvalNum("pageStack.currentPage.topInset", 0);
		var cx = (pageW > 0 ? pageW : 540) / 2;
		var cy = topInset + 300;
		Console.Error.WriteLine($"[Sailfish] Qt page diag: leg D — injecting an upward drag at {cx:F0},{cy:F0} (ScrollY before={scrollBefore:F0}dp)");
		// Spread the events over time: SilicaFlickable delays the press (pressDelay) and Qt 5.6's QQuickFlickable ignores moves that
		// arrive before it, so a single-pass burst never starts a drag. Moves 60 ms apart, release at +420 ms, verify at +1620 ms.
		QtHost.QtHostRuntime.InjectPointer(0, cx, cy);
		for (var i = 1; i <= 5; i++)
		{
			var moveY = cy - i * 30.0;
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(60 * i),
				() => QtHost.QtHostRuntime.InjectPointer(2, cx, moveY));
		}
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(420),
			() => QtHost.QtHostRuntime.InjectPointer(1, cx, cy - 150.0));
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1620),
			() => VerifyQtPageDrag(renderer, dispatcher, scroll, scrollBefore, writeBacksBefore));
	}

	private void VerifyQtPageDrag(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher,
	                              ScrollView scroll, double scrollBefore, long writeBacksBefore)
	{
		var writeBacks = renderer.ScrollWriteBacks - writeBacksBefore;
		// Include the native flickable state (interactive, contentHeight, contentY) in the message for failure forensics.
		var scrollItem = PrimaryScrollItemJs(renderer);
		var flickState = QtHost.QtHostRuntime.Eval(
			$"(function(f){{return f?JSON.stringify({{ie:f.interactive,ch:f.contentHeight,fy:f.contentY}}):'no scroll host';}})({scrollItem})");
		_qtPageChecks.Check($"gesture scroll: drag → native flick → scroll-changed write-backs={writeBacks}, ScrollY {scrollBefore:F0}→{scroll.ScrollY:F0}dp (> before), flick={flickState}",
			writeBacks >= 1 && scroll.ScrollY > scrollBefore + 10);
		var grabRc = QtHost.QtHostRuntime.GrabPng("/tmp/q10-textpage-dragged.png");
		Console.Error.WriteLine($"[Sailfish] Qt page diag: screenshot rc={grabRc} -> /tmp/q10-textpage-dragged.png");

		// Leg E: hardware Back → renderer.TryPop → the previous page re-renders.
		_qtPageStage = 4;
		Console.Error.WriteLine($"[Sailfish] Qt page diag: leg E — injecting hardware Back (return to '{_qtPageReturnTitle}')");
		QtHost.QtHostRuntime.InjectKey(0, QtHost.QtHostRuntime.QtKeyBack);
		QtHost.QtHostRuntime.InjectKey(1, QtHost.QtHostRuntime.QtKeyBack);
		// Safety net: if the pop's rendered report never arrives, finish anyway.
		dispatcher.DispatchDelayed(TimeSpan.FromSeconds(5), () =>
		{
			if (_qtPageStage == 4)
			{
				Console.Error.WriteLine("[Sailfish] Qt page diag: leg E — no rendered report after Back within 5 s — finishing with what we have");
				_qtPageStage = 5;
				VerifyQtPageBack(renderer, dispatcher);
			}
		});
	}

	private void VerifyQtPageBack(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var title = renderer.CurrentPage?.Title ?? "(none)";
		var hosts = renderer.CurrentHosts.Count(h => h.IsAttached);
		_qtPageChecks.Check($"navigation/back: CurrentPage='{title}' (expected '{_qtPageReturnTitle}'), re-rendered hosts={hosts}",
			title == _qtPageReturnTitle && hosts > 0);
		FinishQtPageDiagnostics(renderer, dispatcher);
	}

	private void FinishQtPageDiagnostics(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var failed = _qtPageChecks.Failed;
		Console.Error.WriteLine($"[Sailfish] Qt page diag: scroll write-backs total={renderer.ScrollWriteBacks} bridge applied={renderer.BridgeApplied} failed={renderer.BridgeFailed} " +
			$"geometry applied={renderer.GeometryApplied} failed={renderer.GeometryFailed}");
		Console.Error.WriteLine($"[Sailfish] Qt page diag: ACCEPTANCE checks={_qtPageChecks.Count} failed={failed} => " +
			(failed == 0
				? "OK — first complete MAUI page (TextPage) renders and works end-to-end through the Qt pipeline (PLAN Q10)"
				: "FAIL — see the CHECK lines above"));
		var grabRc = QtHost.QtHostRuntime.GrabPng("/tmp/q10-textpage-back.png");
		Console.Error.WriteLine($"[Sailfish] Qt page diag: screenshot rc={grabRc} -> /tmp/q10-textpage-back.png");
		Console.Error.WriteLine("[Sailfish] Qt diag: page diag done; auto-shutdown in 20s (compositor screenshot window)");
		dispatcher.DispatchDelayed(TimeSpan.FromSeconds(20), () => QtHost.QtHostRuntime.Shutdown());
	}

	// Controls diag: Silica control adapters and container hosts (Border → styled Rectangle, since Silica has no border type).
	// Legs: A push + inventory + geometry, B managed→native readback + echo suppression, C unsuppressed native writes back into MAUI
	// (dialog accepts via __diagFireEvent, radio exclusivity), D injected taps incl. the Button inside the Border, E ScrollView contract.

	private void RunQtControlsDiagnostics(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var nav = RootNav;
		if (nav is null)
		{
			Console.Error.WriteLine("[Sailfish] Qt controls diag: no NavigationPage — leg skipped; auto-shutdown");
			QtHost.QtHostRuntime.Shutdown();
			return;
		}

		// The gallery page owns every stage after its first render.
		QtHost.QtHostRuntime.QmlEvent += (name, payload) =>
		{
			if (name != "rendered")
				return;
			var title = ExtractRenderedTitle(payload);
			if (title is null)
				return;
			if (_qtCtlStage == 1 && title == QtCtlGalleryTitle)
			{
				_qtCtlStage = 2;
				Console.Error.WriteLine($"[Sailfish] Qt controls diag: leg A — '{title}' pushed through MAUI navigation and rendered by the Qt pipeline");
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(400), () => VerifyQtControlsRender(renderer, dispatcher));
			}
		};

		_qtCtlStage = 1;
		var page = BuildControlsGalleryPage();
		Console.Error.WriteLine($"[Sailfish] Qt controls diag: leg A — pushing the {QtCtlGalleryTitle} gallery through NavigationPage.PushAsync");
		_ = nav.PushAsync(page);
	}

	private const string QtCtlGalleryTitle = "Q11 Controls";

	/// <summary>Builds the controls gallery: one control per adapter plus container hosts, inside a ScrollView.</summary>
	private ContentPage BuildControlsGalleryPage()
	{
		var label = new Label { Text = "Q11 controls gallery" };
		var button = new Button { Text = "Q11 tap target" };
		button.Clicked += (_, _) => _qtCtlTaps++;
		var indicator = new ActivityIndicator { IsRunning = false, HeightRequest = 40 };
		var slider = new Slider(0, 10, 5);
		var progress = new ProgressBar { Progress = 0.25 };
		var sw = new Switch { IsToggled = false };
		var checkBox = new CheckBox { IsChecked = false };
		var searchBar = new SearchBar { Placeholder = "search…" };
		searchBar.SearchButtonPressed += (_, _) => _qtCtlSearches++;
		searchBar.CancelButtonColor = Colors.OrangeRed;   // CancelButtonColor bridge witness
		var picker = new Picker { Title = "fruit", ItemsSource = new[] { "apple", "banana", "cherry" }, SelectedIndex = 0 };
		var datePicker = new DatePicker { Date = new DateTime(2026, 9, 11) };
		var timePicker = new TimePicker { Time = new TimeSpan(9, 30, 0) };
		var radioA = new RadioButton { Content = "radio A", GroupName = "q11", IsChecked = true };
		var radioB = new RadioButton { Content = "radio B", GroupName = "q11" };
		// Frame (obsolete, border semantics), ImageButton (image adapter + tap) and Flex/Absolute layouts with paintable backgrounds.
#pragma warning disable CS0618 // Frame is obsolete but must stay covered.
		var frame = new Frame
		{
			BorderColor = Colors.Goldenrod,
			CornerRadius = 10,
			BackgroundColor = Color.FromArgb("#22FFCC66"),
			Padding = new Thickness(10),
			Content = new Label { Text = "frame child" },
		};
#pragma warning restore CS0618
		// sailfish_logo.png ships in the install prefix; QtHostImages resolves bare names against AppContext.BaseDirectory.
		var imageButton = new ImageButton { Source = "sailfish_logo.png", WidthRequest = 48, HeightRequest = 48 };
		imageButton.Clicked += (_, _) => _qtCtlImageTaps++;
		var flex = new FlexLayout
		{
			BackgroundColor = Color.FromArgb("#33AA44AA"),
			Direction = Microsoft.Maui.Layouts.FlexDirection.Row,
			JustifyContent = Microsoft.Maui.Layouts.FlexJustify.SpaceAround,
			Children = { new Label { Text = "flex a" }, new Label { Text = "flex b" } },
		};
		var absolute = new AbsoluteLayout { BackgroundColor = Color.FromArgb("#334488CC"), HeightRequest = 80 };
		var absChild = new Label { Text = "abs @60,20" };
		AbsoluteLayout.SetLayoutBounds(absChild, new Rect(60, 20, 120, 40));
		absolute.Children.Add(absChild);

		// Containers: a Border (stacking/hit-test witness) and ContentView/Grid/StackLayout with backgrounds. A background-less layout
		// paints nothing and gets no host, so every container here paints.
		var borderLabel = new Label { Text = "bordered content" };
		var borderButton = new Button { Text = "border tap" };
		borderButton.Clicked += (_, _) => _qtCtlBorderTaps++;
		var border = new Border
		{
			Stroke = new SolidColorBrush(Colors.Firebrick),
			StrokeThickness = 3,
			StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(12) },
			BackgroundColor = Color.FromArgb("#22FFFFFF"),
			Padding = new Thickness(12),
			Content = new VerticalStackLayout { Spacing = 8, Children = { borderLabel, borderButton } },
		};
		var contentView = new ContentView
		{
			BackgroundColor = Color.FromArgb("#3388AACC"),
			Padding = new Thickness(8),
			Content = new Label { Text = "content-view child" },
		};
		var grid = new Grid
		{
			BackgroundColor = Color.FromArgb("#3344AA88"),
			Padding = new Thickness(8),
			RowSpacing = 6,
			ColumnSpacing = 6,
			ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
			RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) },
		};
		var gridA = new Label { Text = "grid r0c0" };
		var gridB = new Label { Text = "grid r0c1" };
		var gridSpan = new Label { Text = "grid r1 (column span 2)" };
		grid.Children.Add(gridA);
		grid.Children.Add(gridB);
		Grid.SetColumn(gridB, 1);
		grid.Children.Add(gridSpan);
		Grid.SetRow(gridSpan, 1);
		Grid.SetColumnSpan(gridSpan, 2);
		var stack = new VerticalStackLayout
		{
			BackgroundColor = Color.FromArgb("#33CC8844"),
			Spacing = 6,
			Padding = new Thickness(8),
			Children = { new Label { Text = "stack child 1" }, new Label { Text = "stack child 2" } },
		};
		// The flickable only scrolls to contentHeight - viewportHeight; leg E needs room below the ScrollToAsync target to drag into.
		var tail = new Label { Text = "scroll tail spacer (leg E drag headroom)", HeightRequest = 500 };

		// Long-press target: ContextFlyout → Silica ContextMenu.
		var ctxLabel = new Label { Text = "long-press me (context menu)" };
		var ctxFlyout = new MenuFlyout();
		var ctxOne = new MenuFlyoutItem { Text = "ctx one" };
		ctxOne.Clicked += (_, _) => _qtCtlCtxClicks++;
		var ctxTwo = new MenuFlyoutItem { Text = "ctx two" };
		ctxFlyout.Add(ctxOne);
		ctxFlyout.Add(ctxTwo);
		// .NET 11: ContextFlyout is an attached property on FlyoutBase.
		FlyoutBase.SetContextFlyout(ctxLabel, ctxFlyout);

		_qtCtl.Clear();
		_qtCtl["label"] = label;
		_qtCtl["button"] = button;
		_qtCtl["indicator"] = indicator;
		_qtCtl["slider"] = slider;
		_qtCtl["progress"] = progress;
		_qtCtl["switch"] = sw;
		_qtCtl["checkBox"] = checkBox;
		_qtCtl["searchBar"] = searchBar;
		_qtCtl["picker"] = picker;
		_qtCtl["datePicker"] = datePicker;
		_qtCtl["timePicker"] = timePicker;
		_qtCtl["radioA"] = radioA;
		_qtCtl["radioB"] = radioB;
		_qtCtl["frame"] = frame;
		_qtCtl["imageButton"] = imageButton;
		_qtCtl["flex"] = flex;
		_qtCtl["absolute"] = absolute;
		_qtCtl["border"] = border;
		_qtCtl["borderButton"] = borderButton;
		_qtCtl["contentView"] = contentView;
		_qtCtl["grid"] = grid;
		_qtCtl["stack"] = stack;
		_qtCtl["ctxLabel"] = ctxLabel;

		var layout = new VerticalStackLayout
		{
			Spacing = 10,
			Padding = new Thickness(20),
			Children = { label, button, indicator, slider, progress, sw, checkBox, searchBar, picker, datePicker, timePicker, radioA, radioB, frame, imageButton, flex, absolute, border, contentView, grid, stack, ctxLabel, tail },
		};
		var scrollView = new ScrollView { Content = layout };
		_qtCtl["scrollView"] = scrollView;
		var page = new ContentPage
		{
			Title = QtCtlGalleryTitle,
			Content = scrollView,
		};
		// Pulley sources: Primary → PullDownMenu, Secondary → PushUpMenu.
		var pullOne = new ToolbarItem { Text = "Pull one", Order = ToolbarItemOrder.Primary };
		pullOne.Clicked += (_, _) => _qtCtlPullClicks++;
		var pullTwo = new ToolbarItem { Text = "Pull two", Order = ToolbarItemOrder.Primary };
		var pushOne = new ToolbarItem { Text = "Push one", Order = ToolbarItemOrder.Secondary };
		pushOne.Clicked += (_, _) => _qtCtlPushClicks++;
		page.ToolbarItems.Add(pullOne);
		page.ToolbarItems.Add(pullTwo);
		page.ToolbarItems.Add(pushOne);
		return page;
	}


	/// <summary>The attached native host of a gallery control (by diag key).</summary>
	private QtHost.NativeElementHost? CtlHost(QtHost.QtHostPageRenderer renderer, string key) =>
		_qtCtl.TryGetValue(key, out var ctl)
			? renderer.CurrentHosts.FirstOrDefault(h => h.IsAttached && ReferenceEquals(h.Element, ctl))
			: null;

	/// <summary>Leg A verification (inventory, geometry) and leg B mutations, read back in <see cref="VerifyQtControlsPushed"/>.</summary>
	private void VerifyQtControlsRender(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var hosts = renderer.CurrentHosts.Where(h => h.IsAttached).ToList();
		var uris = string.Join(",", hosts.GroupBy(h => h.QmlUri).OrderBy(g => g.Key, StringComparer.Ordinal)
			.Select(g => $"{g.Key}×{g.Count()}"));
		var expected = new[] { "activity-indicator", "border", "button", "check-box", "content-view", "date-picker", "grid", "image", "label", "picker", "progress-bar", "radio-button", "search-bar", "slider", "stack-layout", "switch", "time-picker" };
		var haveUris = hosts.Select(h => h.QmlUri).Distinct().ToHashSet(StringComparer.Ordinal);
		_qtCtlChecks.Check($"inventory: hosts={hosts.Count} ({uris}) — every Group A/B/C/D uri present",
			hosts.Count >= 26 && expected.All(haveUris.Contains));

		// Geometry at scroll offset 0: the new adapters must honor the managed geometry contract too.
		int pass = 0, fail = 0;
		var worst = 0.0;
		foreach (var host in hosts)
		{
			// Synthetic page-level hosts (pulleys, context menu, docked panels, drawers) are positioned by Silica and have no MAUI bounds.
			if (host.Id.StartsWith("synth-", StringComparison.Ordinal))
				continue;
			if (!QtHost.QtHostRuntime.TryItemGeometry(host.NativeHandle, out var scene))
			{
				fail++;
				continue;
			}
			var native = QtHost.QtHostUnits.ToLogical(scene);
			var maui = host.MauiLogicalBounds;
			var delta = Math.Max(Math.Max(Math.Abs(native.X - maui.X), Math.Abs(native.Y - maui.Y)),
				Math.Max(Math.Abs(native.Width - maui.Width), Math.Abs(native.Height - maui.Height)));
			if (delta <= 1.0) pass++; else fail++;
			if (delta > worst) worst = delta;
			if (delta > 1.0)
				Console.Error.WriteLine($"[Sailfish] Qt controls diag: GEOM FAIL {host} maui=({maui.X:F1},{maui.Y:F1} {maui.Width:F1}x{maui.Height:F1})dp " +
					$"qt=({native.X:F1},{native.Y:F1} {native.Width:F1}x{native.Height:F1})dp Δ={delta:F2}");
		}
		_qtCtlChecks.Check($"geometry: matched={pass} mismatched={fail} worstΔ={worst:F2}dp (MAUI layout == Qt scene)", fail == 0 && pass >= 26);

		var grabRc = QtHost.QtHostRuntime.GrabPng("/tmp/q11-controls-gallery.png");
		Console.Error.WriteLine($"[Sailfish] Qt controls diag: screenshot rc={grabRc} -> /tmp/q11-controls-gallery.png");

		// The Border's create state read back natively (leg B mutates it).
		var density = SailfishDisplay.Density;
		var bd0 = CtlHost(renderer, "border");
		_qtCtlChecks.Check($"Border create state: stroke={HostProp(bd0, "mauiBorderColor")}=='#ffb22222' width={HostProp(bd0, "mauiBorderWidth")}≈{3 * density:F1}px radius={HostProp(bd0, "mauiCornerRadius")}≈{12 * density:F1}px fill={HostProp(bd0, "mauiBackground")}=='#22ffffff'",
			HostProp(bd0, "mauiBorderColor") == "#ffb22222" &&
			Math.Abs(HostPropNum(bd0, "mauiBorderWidth") - 3 * density) < 0.51 &&
			Math.Abs(HostPropNum(bd0, "mauiCornerRadius") - 12 * density) < 0.51 &&
			HostProp(bd0, "mauiBackground") == "#22ffffff");

		// Frame rides the border adapter, ImageButton the image adapter with a tap flag, Flex/Absolute paint through content-view.
		var fr0 = CtlHost(renderer, "frame");
		_qtCtlChecks.Check($"Frame rides the border adapter: outline={HostProp(fr0, "mauiBorderColor")}=='#ffdaa520' radius={HostProp(fr0, "mauiCornerRadius")}≈{10 * density:F1}px fill={HostProp(fr0, "mauiBackground")}=='#22ffcc66'",
			HostProp(fr0, "mauiBorderColor") == "#ffdaa520" &&
			Math.Abs(HostPropNum(fr0, "mauiCornerRadius") - 10 * density) < 0.51 &&
			HostProp(fr0, "mauiBackground") == "#22ffcc66");
		var ib0 = CtlHost(renderer, "imageButton");
		_qtCtlChecks.Check($"ImageButton rides the image adapter, tappable: mauiTappable={HostProp(ib0, "mauiTappable")}==true with a resolved source",
			HostProp(ib0, "mauiTappable") == "true" && HostProp(ib0, "mauiSource").Length > 0);
		var flexHost = CtlHost(renderer, "flex");
		var absHost = CtlHost(renderer, "absolute");
		var sb0 = CtlHost(renderer, "searchBar");
		_qtCtlChecks.Check($"Flex/Absolute positioners paint their fill through the content-view adapter (flex={HostProp(flexHost, "mauiBackground")}=='#33aa44aa', absolute={HostProp(absHost, "mauiBackground")}=='#334488cc')",
			HostProp(flexHost, "mauiBackground") == "#33aa44aa" && HostProp(absHost, "mauiBackground") == "#334488cc");
		_qtCtlChecks.Check($"SearchBar.CancelButtonColor crosses the bridge (README #10): mauiCancelColor={HostProp(sb0, "mauiCancelColor")}=='#ffff4500'",
			HostProp(sb0, "mauiCancelColor") == "#ffff4500");

		// Leg B: one managed mutation per control; the PropertyChanged pushes must land in the adapter state.
		Console.Error.WriteLine("[Sailfish] Qt controls diag: leg B — mutating MAUI properties (managed→native pushes)");
		((ActivityIndicator)_qtCtl["indicator"]).IsRunning = true;
		((ProgressBar)_qtCtl["progress"]).Progress = 0.6;
		((Slider)_qtCtl["slider"]).Value = 7.5;
		((Switch)_qtCtl["switch"]).IsToggled = true;
		((CheckBox)_qtCtl["checkBox"]).IsChecked = true;
		((Picker)_qtCtl["picker"]).SelectedIndex = 1;
		((DatePicker)_qtCtl["datePicker"]).Date = new DateTime(2030, 1, 15);
		((TimePicker)_qtCtl["timePicker"]).Time = new TimeSpan(14, 5, 0);
		((RadioButton)_qtCtl["radioB"]).IsChecked = true;
		((SearchBar)_qtCtl["searchBar"]).Text = "qs";
		// Containers: Border snapshot re-diff plus layout background/spacing pushes.
		var borderCtl = (Border)_qtCtl["border"];
		borderCtl.StrokeThickness = 6;
		borderCtl.Stroke = new SolidColorBrush(Colors.MediumSeaGreen);
		borderCtl.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(24) };
		((Grid)_qtCtl["grid"]).BackgroundColor = Color.FromArgb("#3366AA66");
		((VerticalStackLayout)_qtCtl["stack"]).Spacing = 18;
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(700), () => VerifyQtControlsPushed(renderer, dispatcher));
	}

	/// <summary>Leg B verification: the adapter state read back through the
	/// native handles must match every managed mutation.</summary>
	private void VerifyQtControlsPushed(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var ind = CtlHost(renderer, "indicator");
		_qtCtlChecks.Check($"ActivityIndicator: native running={HostProp(ind, "running")} (IsRunning=true pushed)",
			HostProp(ind, "running") == "true");

		var prog = CtlHost(renderer, "progress");
		_qtCtlChecks.Check($"ProgressBar: native value={HostProp(prog, "value")}≈0.6",
			Math.Abs(HostPropNum(prog, "value") - 0.6) < 1e-6);

		var sld = CtlHost(renderer, "slider");
		_qtCtlChecks.Check($"Slider: native value={HostProp(sld, "value")}≈7.5",
			Math.Abs(HostPropNum(sld, "value") - 7.5) < 1e-6);

		var sw = CtlHost(renderer, "switch");
		var swSuppressed = HostPropNum(sw, "mauiSuppressedCount");
		_qtCtlChecks.Check($"Switch: native checked={HostProp(sw, "checked")} (IsToggled=true pushed)",
			HostProp(sw, "checked") == "true");

		var cb = CtlHost(renderer, "checkBox");
		_qtCtlChecks.Check($"CheckBox (F3 check box): native checked={HostProp(cb, "checked")} (IsChecked=true pushed)",
			HostProp(cb, "checked") == "true");

		var pk = CtlHost(renderer, "picker");
		var pkValue = HostProp(pk, "value");
		_qtCtlChecks.Check($"Picker: native currentIndex={HostProp(pk, "currentIndex")}==1 value='{pkValue}'=='banana' (items array + index pushed)",
			HostProp(pk, "currentIndex") == "1" && pkValue == "banana");

		var dp = CtlHost(renderer, "datePicker");
		// The value text uses MAUI's default Format ("d" / "t", current culture).
		var expectDate = new DateTime(2030, 1, 15).ToString("d", System.Globalization.CultureInfo.CurrentCulture);
		var expectTime = DateTime.Today.Add(new TimeSpan(14, 5, 0)).ToString("t", System.Globalization.CultureInfo.CurrentCulture);
		_qtCtlChecks.Check($"DatePicker: native value='{HostProp(dp, "value")}'=='{expectDate}' (local-midnight ms pushed into the dialog, MAUI Format)",
			HostProp(dp, "value") == expectDate && HostProp(dp, "mauiDateMs") != "0");

		var tp = CtlHost(renderer, "timePicker");
		_qtCtlChecks.Check($"TimePicker: native value='{HostProp(tp, "value")}'=='{expectTime}' (hour/minute pair pushed, MAUI Format)",
			HostProp(tp, "value") == expectTime && HostProp(tp, "mauiHour") == "14" && HostProp(tp, "mauiMinute") == "5");

		var ra = CtlHost(renderer, "radioA");
		var rb = CtlHost(renderer, "radioB");
		_qtCtlChecks.Check($"RadioButton group: native B checked={HostProp(rb, "checked")} A checked={HostProp(ra, "checked")} (MAUI exclusivity mirrored natively)",
			HostProp(rb, "checked") == "true" && HostProp(ra, "checked") == "false");

		var sb = CtlHost(renderer, "searchBar");
		_qtCtlChecks.Check($"SearchBar: native text='{HostProp(sb, "text")}'=='qs' (Text pushed into the SearchField)",
			HostProp(sb, "text") == "qs");

		// Container mutations read back natively.
		var density = SailfishDisplay.Density;
		var bd = CtlHost(renderer, "border");
		_qtCtlChecks.Check($"Border: native stroke={HostProp(bd, "mauiBorderColor")}=='#ff3cb371' width={HostProp(bd, "mauiBorderWidth")}≈{6 * density:F1}px radius={HostProp(bd, "mauiCornerRadius")}≈{24 * density:F1}px (Stroke/StrokeThickness/StrokeShape re-diffed)",
			HostProp(bd, "mauiBorderColor") == "#ff3cb371" &&
			Math.Abs(HostPropNum(bd, "mauiBorderWidth") - 6 * density) < 0.51 &&
			Math.Abs(HostPropNum(bd, "mauiCornerRadius") - 24 * density) < 0.51);

		var gr = CtlHost(renderer, "grid");
		_qtCtlChecks.Check($"Grid: native mauiBackground={HostProp(gr, "mauiBackground")}=='#3366aa66' (BackgroundColor pushed into the container fill)",
			HostProp(gr, "mauiBackground") == "#3366aa66");

		var st = CtlHost(renderer, "stack");
		_qtCtlChecks.Check($"StackLayout: native mauiSpacing={HostProp(st, "mauiSpacing")}≈{18 * density:F1}px mauiOrientation={HostProp(st, "mauiOrientation")}=='vertical' (Spacing pushed, positioner state mirrored)",
			Math.Abs(HostPropNum(st, "mauiSpacing") - 18 * density) < 0.51 &&
			HostProp(st, "mauiOrientation") == "vertical");

		var cv = CtlHost(renderer, "contentView");
		_qtCtlChecks.Check($"ContentView: native mauiBackground={HostProp(cv, "mauiBackground")}=='#3388aacc' (create-time fill intact through the reconcile polls)",
			HostProp(cv, "mauiBackground") == "#3388aacc");

		// Echo suppression: the managed pushes above must not come back as native events.
		_qtCtlChecks.Check($"suppression: switch mauiSuppressedCount={swSuppressed:F0}>0 — managed pushes did not echo back",
			swSuppressed > 0);

		ScheduleQtControlsWriteBack(renderer, dispatcher);
	}

	/// <summary>Leg C: unsuppressed native writes take the same adapter path as user input (mauiApplying=false → mauiEvent → write-back).
	/// Dialog accepts go through __diagFireEvent, since separate Silica windows can't be pointer-injected.</summary>
	private void ScheduleQtControlsWriteBack(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		Console.Error.WriteLine("[Sailfish] Qt controls diag: leg C — unsuppressed native writes + dialog accepts (native→managed write-backs)");
		var writes = 0;
		void Write(string key, string prop, string valueJson)
		{
			var host = CtlHost(renderer, key);
			if (host is null)
			{
				Console.Error.WriteLine($"[Sailfish] Qt controls diag: leg C — host '{key}' missing, write skipped");
				return;
			}
			var rc = QtHost.QtHostRuntime.SetProperty(host.NativeHandle, prop, valueJson);
			writes += rc >= 0 ? 1 : 0;
			if (rc < 0)
				Console.Error.WriteLine($"[Sailfish] Qt controls diag: leg C — set {prop} on '{key}' FAILED rc={rc}: {QtHost.QtHostRuntime.LastErrorText}");
		}
		Write("switch", "checked", "false");
		Write("slider", "value", "3.25");
		Write("picker", "currentIndex", "2");
		Write("radioA", "checked", "true");
		Write("checkBox", "checked", "false");
		Write("searchBar", "text", "\"native\"");

		void Fire(string key, string eventName, string payloadJs)
		{
			var host = CtlHost(renderer, key);
			if (host is null)
				return;
			var js = $"pageStack.currentPage.__diagFireEvent('{host.Id}','{eventName}',JSON.stringify({payloadJs}))";
			var result = QtHost.QtHostRuntime.Eval(js);
			Console.Error.WriteLine($"[Sailfish] Qt controls diag: leg C — __diagFireEvent('{key}','{eventName}') -> {result}");
		}
		var dpHost = CtlHost(renderer, "datePicker");
		var tpHost = CtlHost(renderer, "timePicker");
		var sbHost = CtlHost(renderer, "searchBar");
		if (dpHost is not null)
			Fire("datePicker", "date-selected", $"{{id:'{dpHost.Id}',y:2031,m:2,d:3}}");
		if (tpHost is not null)
			Fire("timePicker", "time-selected", $"{{id:'{tpHost.Id}',h:23,mi:59}}");
		if (sbHost is not null)
			Fire("searchBar", "completed", $"{{id:'{sbHost.Id}'}}");
		var ibHost = CtlHost(renderer, "imageButton");
		if (ibHost is not null)
			Fire("imageButton", "tap", $"{{id:'{ibHost.Id}'}}");   // image adapter tap → SendClicked

		Console.Error.WriteLine($"[Sailfish] Qt controls diag: leg C — {writes} native writes issued");
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900), () => VerifyQtControlsWriteBack(renderer, dispatcher));
	}

	private void VerifyQtControlsWriteBack(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var sw = (Switch)_qtCtl["switch"];
		_qtCtlChecks.Check($"Switch write-back: MAUI IsToggled={sw.IsToggled}==False (native checked=false event)",
			sw.IsToggled == false);

		var sld = (Slider)_qtCtl["slider"];
		_qtCtlChecks.Check($"Slider write-back: MAUI Value={sld.Value:F2}≈3.25 (native value-changed event)",
			Math.Abs(sld.Value - 3.25) < 1e-6);

		var pk = (Picker)_qtCtl["picker"];
		_qtCtlChecks.Check($"Picker write-back: MAUI SelectedIndex={pk.SelectedIndex}==2 (native ComboBox pick)",
			pk.SelectedIndex == 2);

		var ra = (RadioButton)_qtCtl["radioA"];
		var rb = (RadioButton)_qtCtl["radioB"];
		_qtCtlChecks.Check($"RadioButton write-back + group exclusivity: A={ra.IsChecked} B={rb.IsChecked} (native check A → MAUI unchecks B)",
			ra.IsChecked && !rb.IsChecked);

		var cb = (CheckBox)_qtCtl["checkBox"];
		_qtCtlChecks.Check($"CheckBox write-back: MAUI IsChecked={cb.IsChecked}==False (native toggled event)",
			cb.IsChecked == false);

		var sb = (SearchBar)_qtCtl["searchBar"];
		_qtCtlChecks.Check($"SearchBar write-back: MAUI Text='{sb.Text}'=='native' (native text-changed event)",
			sb.Text == "native");

		var dp = (DatePicker)_qtCtl["datePicker"];
		var dpDate = dp.Date ?? default;
		_qtCtlChecks.Check($"DatePicker dialog accept: MAUI Date={dpDate:yyyy-MM-dd}==2031-02-03 (date-selected event)",
			dpDate == new DateTime(2031, 2, 3));

		var tp = (TimePicker)_qtCtl["timePicker"];
		var tpTime = tp.Time ?? TimeSpan.Zero;
		_qtCtlChecks.Check($"TimePicker dialog accept: MAUI Time={tpTime:hh\\:mm}==23:59 (time-selected event)",
			tpTime == new TimeSpan(23, 59, 0));

		_qtCtlChecks.Check($"SearchBar accepted: SearchButtonPressed fired={_qtCtlSearches}>=1 (completed event → OnSearchButtonPressed)",
			_qtCtlSearches >= 1);

		_qtCtlChecks.Check($"ImageButton tap: Clicked fired={_qtCtlImageTaps}>=1 (image adapter 'tap' → SendClicked, README #8)",
			_qtCtlImageTaps >= 1);

		// Leg D: injected tap on the Silica Button. Press+release go back-to-back: with a spread, the page flickable's press-delay
		// grab swallows the release.
		var buttonHost = CtlHost(renderer, "button");
		if (buttonHost is not null && QtHost.QtHostRuntime.TryItemGeometry(buttonHost.NativeHandle, out var geo) && geo.Width > 0 && geo.Height > 0)
		{
			var cx = geo.X + geo.Width / 2;
			var cy = geo.Y + geo.Height / 2;
			Console.Error.WriteLine($"[Sailfish] Qt controls diag: leg D — injecting a real-Qt tap into the Silica Button at {cx:F0},{cy:F0}");
			DiagQml.Tap(cx, cy);
		}
		else
		{
			Console.Error.WriteLine("[Sailfish] Qt controls diag: leg D — button geometry unavailable, tap skipped");
		}
		// Same tap on the button inside the Border: the Border host covers the point but paints below and consumes nothing,
		// so the child Button must get it.
		var borderButtonHost = CtlHost(renderer, "borderButton");
		if (borderButtonHost is not null && QtHost.QtHostRuntime.TryItemGeometry(borderButtonHost.NativeHandle, out var bGeo) && bGeo.Width > 0 && bGeo.Height > 0)
		{
			var bx = bGeo.X + bGeo.Width / 2;
			var by = bGeo.Y + bGeo.Height / 2;
			Console.Error.WriteLine($"[Sailfish] Qt controls diag: leg D — injecting a real-Qt tap into the Button INSIDE the Border at {bx:F0},{by:F0}");
			DiagQml.Tap(bx, by);
		}
		else
		{
			Console.Error.WriteLine("[Sailfish] Qt controls diag: leg D — border button geometry unavailable, tap skipped");
		}
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(800), () => RunQtCtlScrollLeg(renderer, dispatcher));
	}

	/// <summary>Leg E: MAUI owns scroll state, so ScrollToAsync must reach the SilicaFlickable contentY; then a timed drag (60 ms
	/// spread, since a burst is swallowed by pressDelay) over the Grid fill must scroll natively and write ScrollY back.</summary>
	private void RunQtCtlScrollLeg(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		_qtCtlChecks.Check($"Button INSIDE the Border: Clicked fired={_qtCtlBorderTaps}>=1 (frontmost-host stacking — the Border paints below its children and consumes nothing)",
			_qtCtlBorderTaps >= 1);
		var scroll = renderer.ScrollContext;
		var gridHost = CtlHost(renderer, "grid");
		if (scroll is null || gridHost is null)
		{
			_qtCtlChecks.Check("leg E setup: page-level ScrollView tracked + the Grid drag surface hosted", false);
			FinishQtControlsDiagnostics(renderer, dispatcher);
			return;
		}
		// Bring the Grid near the viewport top so the drag lands on its fill. Keep the target below the native max scroll so it isn't
		// clamped and the drag keeps ≥250 dp of headroom.
		var gridY = gridHost.MauiLogicalBounds.Y;
		var scrollItem = PrimaryScrollItemJs(renderer);
		var contentH = DiagQml.EvalNum($"{scrollItem}.contentHeight");
		var pageH = DiagQml.EvalNum($"{scrollItem}.height");
		var maxScrollDp = contentH > pageH ? QtHost.QtHostUnits.ToLogical(contentH - pageH) : 0;
		_qtCtlScrollTarget = Math.Clamp(gridY - 150, 0, Math.Max(0, maxScrollDp - 250));
		Console.Error.WriteLine($"[Sailfish] Qt controls diag: leg E — ScrollView.ScrollToAsync(0, {_qtCtlScrollTarget:F0}) → setMauiScroll → native contentY (gridY={gridY:F0}dp maxScroll={maxScrollDp:F0}dp)");
		_ = scroll.ScrollToAsync(0, _qtCtlScrollTarget, animated: false);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(700), () => VerifyQtCtlScrollPush(renderer, dispatcher, scroll));
	}

	private void VerifyQtCtlScrollPush(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, ScrollView scroll)
	{
		// Managed ScrollY is the authority: native contentY must mirror it and equal the (unclamped) target.
		var nativeY = DiagQml.EvalNum($"{PrimaryScrollItemJs(renderer)}.contentY");
		var expectedQt = QtHost.QtHostUnits.ToQtUnits(scroll.ScrollY);
		_qtCtlChecks.Check($"ScrollToAsync push: native contentY={nativeY:F0}qt ≈ {expectedQt:F0}qt, MAUI ScrollY={scroll.ScrollY:F0}dp == target {_qtCtlScrollTarget:F0}dp (managed → SilicaFlickable)",
			Math.Abs(nativeY - expectedQt) <= 2 && Math.Abs(scroll.ScrollY - _qtCtlScrollTarget) <= 1);

		var gridHost = CtlHost(renderer, "grid");
		if (gridHost is null || !QtHost.QtHostRuntime.TryItemGeometry(gridHost.NativeHandle, out var geo) || geo.Width <= 0 || geo.Height <= 0)
		{
			_qtCtlChecks.Check("leg E drag surface: the Grid scene geometry available", false);
			FinishQtControlsDiagnostics(renderer, dispatcher);
			return;
		}
		var scrollBefore = scroll.ScrollY;
		var writeBacksBefore = renderer.ScrollWriteBacks;
		var cx = geo.X + geo.Width / 2;
		var cy = geo.Y + geo.Height / 2;
		Console.Error.WriteLine($"[Sailfish] Qt controls diag: leg E — injecting an upward drag over the Grid fill at {cx:F0},{cy:F0} (ScrollY before={scrollBefore:F0}dp)");
		QtHost.QtHostRuntime.InjectPointer(0, cx, cy);
		for (var i = 1; i <= 5; i++)
		{
			var moveY = cy - i * 30.0;
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(60 * i),
				() => QtHost.QtHostRuntime.InjectPointer(2, cx, moveY));
		}
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(420),
			() => QtHost.QtHostRuntime.InjectPointer(1, cx, cy - 150.0));
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1620),
			() => VerifyQtCtlScrollDrag(renderer, dispatcher, scroll, scrollBefore, writeBacksBefore));
	}

	private void VerifyQtCtlScrollDrag(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher,
	                                   ScrollView scroll, double scrollBefore, long writeBacksBefore)
	{
		var writeBacks = renderer.ScrollWriteBacks - writeBacksBefore;
		// Include the native flickable state for failure forensics.
		var scrollItem = PrimaryScrollItemJs(renderer);
		var flickState = QtHost.QtHostRuntime.Eval(
			$"(function(f){{return f?JSON.stringify({{ie:f.interactive,ch:f.contentHeight,fy:f.contentY}}):'no scroll host';}})({scrollItem})");
		_qtCtlChecks.Check($"gesture scroll: drag over the Grid fill → native flick → scroll-changed write-backs={writeBacks}>=1, ScrollY {scrollBefore:F0}→{scroll.ScrollY:F0}dp (> before), flick={flickState}",
			writeBacks >= 1 && scroll.ScrollY > scrollBefore + 10);
		RunQtCtlInteractionLegs(renderer, dispatcher);
	}

	// Legs F–I (Sailfish interaction surfaces): F long-press → ContextMenu → MenuFlyoutItem.Clicked, G/G2 pulley gestures → ToolbarItems,
	// H DockedPanel + Drawer synthetic hosts, I PushAlertAsync dialog accepted by an injected tap (same window, so injectable).
	private void RunQtCtlInteractionLegs(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		// Scroll the long-press target into view: Qt delivers no touch to content scrolled out of the viewport.
		if (renderer.ScrollContext is { } sc && CtlHost(renderer, "ctxLabel")?.Element is Element ctxTarget)
			_ = sc.ScrollToAsync(ctxTarget, ScrollToPosition.Center, false);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(600),
			() => RunQtCtlContextMenuLeg(renderer, dispatcher));
	}

	private void RunQtCtlContextMenuLeg(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var pull = renderer.CurrentHosts.FirstOrDefault(h => h.QmlUri == "pull-down-menu");
		var push = renderer.CurrentHosts.FirstOrDefault(h => h.QmlUri == "push-up-menu");
		var ctx = renderer.CurrentHosts.FirstOrDefault(h => h.QmlUri == "context-menu");
		var pullItems = QtHost.QtHostRuntime.Eval(
			"(function(){var h=pageStack.currentPage.__hosts;for(var k in h){if(h[k].uri==='pull-down-menu')return h[k].item.mauiItems;}return '[]';})()");
		_qtCtlChecks.Check($"Group E synthetic hosts reconciled (ToolbarItems/ContextFlyout → adapters): pull-down={pull is not null} push-up={push is not null} context-menu={ctx is not null}; pull mauiItems={pullItems}",
			pull is not null && push is not null && ctx is not null && pullItems.Contains("Pull one"));

		var ctxHost = CtlHost(renderer, "ctxLabel");
		if (ctxHost is null || !QtHost.QtHostRuntime.TryItemGeometry(ctxHost.NativeHandle, out var geo) || geo.Width <= 0)
		{
			_qtCtlChecks.Check("leg F setup: the long-press target host geometry available", false);
			FinishQtControlsDiagnostics(renderer, dispatcher);
			return;
		}
		var cx = geo.X + geo.Width / 2;
		var cy = geo.Y + geo.Height / 2;
		Console.Error.WriteLine($"[Sailfish] Qt controls diag: leg F — injecting a press-and-hold (long-press) on the context label at {cx:F0},{cy:F0}");
		QtHost.QtHostRuntime.InjectPointer(0, cx, cy);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(800), () =>
		{
			// Forensics during the hold: the menu fired at 600 ms and must be open while the finger is down.
			var dump = QtHost.QtHostRuntime.Eval(
				"(function(){var h=pageStack.currentPage.__hosts;for(var k in h){if(h[k].uri==='context-menu'){var m=h[k].item;return JSON.stringify({a:m.active,hc:m.hasContent,dh:m._displayHeight,ch:m._contentHeight,aa:m._activeAllowed,pg:m._page!==null,w:m.width,h:m.height,n:m.__items.length,par:m.parent!==null});}}return '{}';})()");
			Console.Error.WriteLine($"[Sailfish] Qt controls diag: leg F — context menu state during hold: {dump}");
		});
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1000), () =>
		{
			QtHost.QtHostRuntime.InjectPointer(1, cx, cy);
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(500),
				() => VerifyQtCtlContextMenu(renderer, dispatcher));
		});
	}

	private void VerifyQtCtlContextMenu(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var active = CtlSyntheticActive("context-menu");
		_qtCtlChecks.Check($"long-press → Silica ContextMenu open: active={active}==1 (router holds fired={_qtInputRouter?.LongPressFired ?? 0}>=1)",
			active == "1" && (_qtInputRouter?.LongPressFired ?? 0) >= 1);
		// Tap the first MenuItem (scene position read through the adapter).
		var pt = QtHost.QtHostRuntime.Eval(
			"(function(){var h=pageStack.currentPage.__hosts;for(var k in h){if(h[k].uri==='context-menu'){var m=h[k].item;if(!m.active||!m.__items.length)return '-1,-1';var it=m.__items[0];var p=it.mapToItem(pageStack.currentPage,it.width/2,it.height/2);return p.x+','+p.y;}}return '-1,-1';})()");
		if (CtlTryPoint(pt, out var ix, out var iy))
		{
			Console.Error.WriteLine($"[Sailfish] Qt controls diag: leg F — injecting a tap on the first ContextMenu MenuItem at {ix:F0},{iy:F0}");
			DiagQml.Tap(ix, iy);
		}
		else
		{
			Console.Error.WriteLine($"[Sailfish] Qt controls diag: leg F — menu item geometry unavailable ('{pt}'), tap skipped");
		}
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(800), () =>
		{
			var closed = CtlSyntheticActive("context-menu");
			_qtCtlChecks.Check($"ContextMenu pick → MAUI MenuFlyoutItem: Clicked fired={_qtCtlCtxClicks}>=1, activations={renderer.ContextMenuActivations}>=1, menu closed after the pick={closed}==0",
				_qtCtlCtxClicks >= 1 && renderer.ContextMenuActivations >= 1 && closed == "0");
			RunQtCtlPulleyLeg(renderer, dispatcher);
		});
	}

	private void RunQtCtlPulleyLeg(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		// A pull-down activates only at the top of the scroller; leg F scrolled away, so return first.
		if (renderer.ScrollContext is { } sc && sc.ScrollY > 0)
		{
			_ = sc.ScrollToAsync(0, 0, false);
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(600), () => RunQtCtlPulleyLeg(renderer, dispatcher));
			return;
		}
		var pageW = DiagQml.EvalNum("pageStack.currentPage.width");
		var inset = DiagQml.EvalNum("pageStack.currentPage.topInset");
		if (double.IsNaN(pageW) || double.IsNaN(inset))
		{
			_qtCtlChecks.Check("leg G setup: page geometry available", false);
			FinishQtControlsDiagnostics(renderer, dispatcher);
			return;
		}
		// Pull-down: press below the chrome and drag down with timed moves. The drag must exceed the expanded menu height
		// (≈260 px) or the pulley bounces back closed on release.
		var px = pageW / 2;
		var py = inset + 30;
		Console.Error.WriteLine($"[Sailfish] Qt controls diag: leg G — injecting a pull-down gesture at {px:F0},{py:F0}");
		QtHost.QtHostRuntime.InjectPointer(0, px, py);
		for (var i = 1; i <= 8; i++)
		{
			var moveY = py + i * 50.0;
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(60 * i),
				() => QtHost.QtHostRuntime.InjectPointer(2, px, moveY));
		}
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(540),
			() => QtHost.QtHostRuntime.InjectPointer(1, px, py + 400));
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1300),
			() => VerifyQtCtlPullDown(renderer, dispatcher));
	}

	private void VerifyQtCtlPullDown(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, int settle = 0)
	{
		var active = CtlSyntheticActive("pull-down-menu");
		// The pulley opens on its own animation clock after release; poll in a bounded settle window instead of one racy read.
		if (active != "1" && settle < 6)
		{
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(400),
				() => VerifyQtCtlPullDown(renderer, dispatcher, settle + 1));
			return;
		}
		if (active != "1")
		{
			// Forensics: attachment + pulley activation state of the adapter.
			var dump = QtHost.QtHostRuntime.Eval(
				"(function(){var p=pageStack.currentPage;var f=p.mauiFlickable();var m=null;for(var k in p.__hosts){if(p.__hosts[k].uri==='pull-down-menu')m=p.__hosts[k].item;}if(!m)return '{}';return JSON.stringify({fp:f.pullDownMenu===m,mf:m.flickable===f,par:m.parent===f,vis:m.visible,aip:m._atInitialPosition,ap:m._activationPermitted,ih:m._inactiveHeight,n:m.__items.length,cy:f.contentY,oy:f.originY,ie:f.interactive});})()");
			Console.Error.WriteLine($"[Sailfish] Qt controls diag: leg G — pull-down adapter state: {dump}");
		}
		_qtCtlChecks.Check($"pull-down gesture → Silica PullDownMenu active={active}==1 (native pulley over the page flickable)",
			active == "1");
		CtlTapFirstPulleyItem("pull-down-menu", dispatcher, () =>
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900), () =>
			{
				_qtCtlChecks.Check($"PullDownMenu pick → MAUI ToolbarItem: Clicked fired={_qtCtlPullClicks}>=1, toolbar activations={renderer.ToolbarActivations}>=1",
					_qtCtlPullClicks >= 1 && renderer.ToolbarActivations >= 1);
				RunQtCtlPushUpLeg(renderer, dispatcher);
			}));
	}

	private void RunQtCtlPushUpLeg(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		// Push-up activates at the bottom content edge; scroll there first (the push clamps at the native max).
		if (renderer.ScrollContext is { } sc)
			_ = sc.ScrollToAsync(0, 100000, false);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(700), () =>
		{
			var pageW = DiagQml.EvalNum("pageStack.currentPage.width");
			var pageH = DiagQml.EvalNum("pageStack.currentPage.height");
			if (double.IsNaN(pageW) || double.IsNaN(pageH))
			{
				_qtCtlChecks.Check("leg G2 setup: page geometry available", false);
				RunQtCtlPanelDrawerLeg(renderer, dispatcher);
				return;
			}
			var px = pageW / 2;
			var py = pageH - 20;
			Console.Error.WriteLine($"[Sailfish] Qt controls diag: leg G2 — injecting a push-up gesture at {px:F0},{py:F0}");
			QtHost.QtHostRuntime.InjectPointer(0, px, py);
			for (var i = 1; i <= 6; i++)
			{
				var moveY = py - i * 40.0;
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(60 * i),
					() => QtHost.QtHostRuntime.InjectPointer(2, px, moveY));
			}
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(420),
				() => QtHost.QtHostRuntime.InjectPointer(1, px, py - 240));
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200),
				() => VerifyQtCtlPushUp(renderer, dispatcher));
		});
	}

	private void VerifyQtCtlPushUp(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var active = CtlSyntheticActive("push-up-menu");
		_qtCtlChecks.Check($"push-up gesture → Silica PushUpMenu active={active}==1 (native pulley from the bottom edge)",
			active == "1");
		CtlTapFirstPulleyItem("push-up-menu", dispatcher, () =>
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900), () =>
			{
				_qtCtlChecks.Check($"PushUpMenu pick → MAUI ToolbarItem (Secondary): Clicked fired={_qtCtlPushClicks}>=1, toolbar activations={renderer.ToolbarActivations}>=2",
					_qtCtlPushClicks >= 1 && renderer.ToolbarActivations >= 2);
				RunQtCtlPanelDrawerLeg(renderer, dispatcher);
			}));
	}

	private void RunQtCtlPanelDrawerLeg(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var panelId = renderer.AddInteractionHost("docked-panel", new Dictionary<string, object?>
		{
			["mauiDock"] = "bottom", ["mauiOpen"] = true, ["mauiSize"] = 320.0, ["mauiText"] = "Group E docked panel",
		});
		var drawerId = renderer.AddInteractionHost("drawer", new Dictionary<string, object?>
		{
			["mauiDock"] = "left", ["mauiOpen"] = true, ["mauiSize"] = 420.0, ["mauiText"] = "Group E drawer",
		});
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1000), () =>
		{
			var state = CtlPanelDrawerState();
			_qtCtlChecks.Check($"DockedPanel + Drawer synthetic hosts open natively: open(panel+drawer)={state}==11",
				state == "11");
			var grabRc = QtHost.QtHostRuntime.GrabPng("/tmp/q11-groupE-surfaces.png");
			Console.Error.WriteLine($"[Sailfish] Qt controls diag: leg H — screenshot rc={grabRc} -> /tmp/q11-groupE-surfaces.png");
			// Managed close is a suppressed push (no event echo); the adapters mirror open back into mauiOpen.
			renderer.UpdateInteractionHost(panelId, new Dictionary<string, object?>
			{
				["mauiDock"] = "bottom", ["mauiOpen"] = false, ["mauiSize"] = 320.0, ["mauiText"] = "Group E docked panel",
			});
			renderer.UpdateInteractionHost(drawerId, new Dictionary<string, object?>
			{
				["mauiDock"] = "left", ["mauiOpen"] = false, ["mauiSize"] = 420.0, ["mauiText"] = "Group E drawer",
			});
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1000), () =>
			{
				var closed = CtlPanelDrawerState();
				_qtCtlChecks.Check($"managed close → native DockedPanel/Drawer open=false: state={closed}==00",
					closed == "00");
				renderer.RemoveInteractionHost(panelId);
				renderer.RemoveInteractionHost(drawerId);
				RunQtCtlDialogLeg(renderer, dispatcher);
			});
		});
	}

	private void RunQtCtlDialogLeg(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		// Stack depth here depends on the pages pushed before (root + startup nav + gallery), so measure it before the dialog push.
		var baseDepth = DiagQml.EvalNum("pageStack.depth");
		var dialogTask = renderer.PushAlertAsync("Q11 Dialog", "Group E Silica Dialog surface", "Accept", "Cancel");
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(800), () =>
		{
			var depth = DiagQml.EvalNum("pageStack.depth");
			var title = QtHost.QtHostRuntime.Eval("pageStack.currentPage.mauiTitle || ''");
			_qtCtlChecks.Check($"Silica Dialog pushed on the pageStack: depth={depth:F0}=={baseDepth + 1:F0}, title='{title}'=='Q11 Dialog'",
				depth == baseDepth + 1 && title == "Q11 Dialog");
			// The dialog lives in the same window (unlike the wheel dialogs), so its DialogHeader Accept is pointer-injectable.
			var pt = QtHost.QtHostRuntime.Eval(
				"(function(){var d=pageStack.currentPage;function F(o){if(o.acceptText!==undefined)return o;for(var i=0;i<o.children.length;++i){var r=F(o.children[i]);if(r)return r;}return null;}var h=F(d);if(!h)return '-1,-1';var p=h.mapToItem(d,h.width-40,h.height/2);return p.x+','+p.y;})()");
			if (CtlTryPoint(pt, out var ax, out var ay))
			{
				Console.Error.WriteLine($"[Sailfish] Qt controls diag: leg I — injecting a tap on the DialogHeader Accept at {ax:F0},{ay:F0}");
				DiagQml.Tap(ax, ay);
			}
			else
			{
				Console.Error.WriteLine($"[Sailfish] Qt controls diag: leg I — accept header geometry unavailable ('{pt}'), tap skipped");
			}
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200), () =>
			{
				var depth2 = DiagQml.EvalNum("pageStack.depth");
				var accepted = dialogTask.IsCompletedSuccessfully && dialogTask.Result;
				_qtCtlChecks.Check($"injected Accept tap → alert-accepted → task result={accepted}, dialog popped: depth={depth2:F0}=={baseDepth:F0}",
					accepted && depth2 == baseDepth);
				FinishQtControlsDiagnostics(renderer, dispatcher);
			});
		});
	}

	/// <summary>Native open/active state of a page-level synthetic adapter
	/// ("0"/"1") read straight from the QML host.</summary>
	private static string CtlSyntheticActive(string uri) =>
		QtHost.QtHostRuntime.Eval(
			$"(function(){{var h=pageStack.currentPage.__hosts;for(var k in h){{if(h[k].uri==='{uri}')return h[k].item.active?'1':'0';}}return '0';}})()");

	/// <summary>Open state pair of the leg-H panel+drawer ("11"/"10"/"00").</summary>
	private static string CtlPanelDrawerState() =>
		QtHost.QtHostRuntime.Eval(
			"(function(){var h=pageStack.currentPage.__hosts;var o={p:0,d:0};for(var k in h){if(h[k].uri==='docked-panel')o.p=h[k].item.open?1:0;if(h[k].uri==='drawer')o.d=h[k].item.open?1:0;}return ''+o.p+o.d;})()");

	/// <summary>Taps the first MenuItem of an active pulley, polling (and holding the menu open) until it's on-screen,
	/// then invokes <paramref name="onDone"/>.</summary>
	private void CtlTapFirstPulleyItem(string uri, SailfishDispatcher dispatcher, Action onDone) =>
		CtlTapPulleyItemAttempt(uri, dispatcher, 0, onDone);

	private void CtlTapPulleyItemAttempt(string uri, SailfishDispatcher dispatcher, int attempt, Action onDone)
	{
		var pt = QtHost.QtHostRuntime.Eval(
			$"(function(){{var h=pageStack.currentPage.__hosts;for(var k in h){{if(h[k].uri==='{uri}'){{var m=h[k].item;if(!m.active||!m.__items.length)return '-1,-1';var it=m.__items[0];var p=it.mapToItem(pageStack.currentPage,it.width/2,it.height/2);return p.x+','+p.y;}}}}return '-1,-1';}})()");
		if (CtlTryPulleyPoint(pt, out var x, out var y))
		{
			Console.Error.WriteLine($"[Sailfish] Qt controls diag: injecting a tap on the first {uri} MenuItem at {x:F0},{y:F0}");
			DiagQml.Tap(x, y);
			onDone();
			return;
		}

		if (attempt < 10)
		{
			// The pulley settle animation may still be running; re-assert active (the documented programmatic open) and re-poll.
			var held = QtHost.QtHostRuntime.Eval(
				$"(function(){{var h=pageStack.currentPage.__hosts;for(var k in h){{if(h[k].uri==='{uri}'){{var m=h[k].item;m.active=true;var f=m.flickable;return 'contentY='+(f?f.contentY:'?');}}}}return 'none';}})()");
			Console.Error.WriteLine($"[Sailfish] Qt controls diag: {uri} pick attempt {attempt + 1}: point '{pt}' not ready ({held}) — holding open, retrying");
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(300),
				() => CtlTapPulleyItemAttempt(uri, dispatcher, attempt + 1, onDone));
			return;
		}

		Console.Error.WriteLine($"[Sailfish] Qt controls diag: {uri} item geometry unavailable ('{pt}'), tap skipped");
		onDone();
	}

	/// <summary>Parses an "x,y" scene-coordinate eval result.</summary>
	private static bool CtlTryPoint(string text, out double x, out double y)
	{
		return DiagQml.TryPoint(text, out x, out y) && x >= 0 && y >= 0;
	}

	/// <summary>Parses a pulley item point. Unlike <see cref="CtlTryPoint"/>, off-screen coordinates are accepted: an open pulley can
	/// rest with items outside the page rect and scene hit-testing still reaches them. Only the '-1,-1' sentinel is rejected.</summary>
	private static bool CtlTryPulleyPoint(string text, out double x, out double y)
	{
		x = y = double.NaN;
		if (string.IsNullOrWhiteSpace(text) || text == "-1,-1")
			return false;
		return DiagQml.TryPoint(text, out x, out y);
	}

	/// <summary>JS reference to the QML item of the page's main ScrollView host, which has its own SilicaFlickable
	/// (contentY/contentHeight/interactive live there, not on the page flickable).</summary>
	private static string PrimaryScrollItemJs(QtHost.QtHostPageRenderer renderer)
	{
		var host = renderer.ScrollContext is { } sc
			? DiagQml.HostOf(renderer, sc)
			: null;
		return DiagQml.ItemJs(host);
	}

	private void FinishQtControlsDiagnostics(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		_qtCtlChecks.Check($"Button tap: Clicked fired={_qtCtlTaps}>=1 (injected real-Qt press/release → Silica Button → Q6 bridge)",
			_qtCtlTaps >= 1);
		var grabRc = QtHost.QtHostRuntime.GrabPng("/tmp/q11-controls-final.png");
		Console.Error.WriteLine($"[Sailfish] Qt controls diag: screenshot rc={grabRc} -> /tmp/q11-controls-final.png");
		var failed = _qtCtlChecks.Failed;
		Console.Error.WriteLine($"[Sailfish] Qt controls diag: bridge applied={renderer.BridgeApplied} failed={renderer.BridgeFailed} native events delivered={renderer.NativeEventsDelivered}");
		Console.Error.WriteLine($"[Sailfish] Qt controls diag: ARCH COUNTERS {renderer.ArchitectureCounters}");
		Console.Error.WriteLine($"[Sailfish] Qt controls diag: NAV-IDLE→render avg {renderer.AvgIdleToRenderMs:F0}ms ×{renderer.IdleToRenderCount}");
		Console.Error.WriteLine($"[Sailfish] Qt controls diag: ACCEPTANCE checks={_qtCtlChecks.Count} failed={failed} => " +
			(failed == 0
				? "OK — Q11 Group A/B/C/D/E controls, containers and Sailfish interaction surfaces work end-to-end through the Qt pipeline (PLAN Q11)"
				: "FAIL — see the CHECK lines above"));
		Console.Error.WriteLine("[Sailfish] Qt diag: controls diag done; auto-shutdown in 20s (compositor screenshot window)");
		dispatcher.DispatchDelayed(TimeSpan.FromSeconds(20), () => QtHost.QtHostRuntime.Shutdown());
	}

	// Nav diag: MAUI navigation maps onto the Silica pageStack with two-way sync; the startup push already created a second model page.
	// Legs: A PushAsync → native push, B native pop → MAUI follows, C/D modal push + Back pops it first, E PopToRoot collapses the stack,
	// F activation counters, G modal NavigationPage pops inner pages first, H injected push/pop faults roll back, I two pops in one poll.

	private void RunQtNavDiagnostics(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var nav = RootNav;
		if (nav is null)
		{
			Console.Error.WriteLine("[Sailfish] Qt nav diag: no NavigationPage — leg skipped; auto-shutdown");
			QtHost.QtHostRuntime.Shutdown();
			return;
		}
		_qtNavRootPage = nav.RootPage;
		_qtNavStartPage = nav.CurrentPage;
		_qtNavBaseDepth = renderer.NativePageIds.Count;
		var mauiDepth = nav.Navigation.NavigationStack.Count + nav.Navigation.ModalStack.Count;
		_qtNavChecks.Check($"baseline: native model pages [{string.Join(",", renderer.NativePageIds)}] mirror the MAUI depth (native={_qtNavBaseDepth} maui={mauiDepth}) — the startup push already mapped",
			_qtNavBaseDepth == mauiDepth && _qtNavBaseDepth >= 2);
		_qtNavChecks.Check($"activation bridge: window lifecycle events delivered to MAUI = {renderer.ActivationEvents} >= 1 (Created/Resumed/Activated)",
			renderer.ActivationEvents >= 1);

		// Leg A: managed PushAsync → native pageStack.push.
		var pageA = BuildNavLegPage("Q12 Nav A", "leg A — pushed through NavigationPage.PushAsync");
		var pushesBefore = renderer.NativePushes;
		Console.Error.WriteLine("[Sailfish] Qt nav diag: leg A — PushAsync 'Q12 Nav A' (MAUI drives the Silica pageStack)");
		_ = nav.PushAsync(pageA);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900),
			() => VerifyNavLegA(renderer, dispatcher, nav, pageA, pushesBefore));
	}


	private static ContentPage BuildNavLegPage(string title, string text) => new()
	{
		Title = title,
		Content = new VerticalStackLayout
		{
			Padding = new Thickness(24),
			Spacing = 12,
			Children =
			{
				new Label { Text = title, FontSize = 28, FontAttributes = FontAttributes.Bold },
				new Label { Text = text },
			},
		},
	};

	private void VerifyNavLegA(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher,
		NavigationPage nav, Page pageA, long pushesBefore)
	{
		var mirror = string.Join(",", renderer.NativePageIds);
		_qtNavChecks.Check($"leg A push: MAUI PushAsync → pageStack.push (pushes +{renderer.NativePushes - pushesBefore}, mirror [{mirror}] depth {renderer.NativePageIds.Count}=={_qtNavBaseDepth + 1}, PageStackAction.Immediate — no animatorPush)",
			renderer.NativePushes > pushesBefore && renderer.NativePageIds.Count == _qtNavBaseDepth + 1);
		_qtNavChecks.Check($"leg A current page: renderer.CurrentPage is the pushed page (got '{renderer.CurrentPage?.Title}')",
			ReferenceEquals(renderer.CurrentPage, pageA));
		var nativeTitle = QtHost.QtHostRuntime.Eval("pageStack.currentPage.pageTitle");
		var topIsModel = QtHost.QtHostRuntime.Eval("!!(pageStack.currentPage&&pageStack.currentPage.mauiPageId!==undefined)");
		_qtNavChecks.Check($"leg A native top: pageStack.currentPage is a MAUI model page (topIsModel={topIsModel}) titled '{nativeTitle}'",
			nativeTitle == "Q12 Nav A" && topIsModel == "true");
		_qtNavChecks.Check($"leg A lifecycle: SendAppearing={renderer.AppearingSent}>=1 SendDisappearing={renderer.DisappearingSent}>=1 (platform-owned appearing/disappearing semantics)",
			renderer.AppearingSent >= 1 && renderer.DisappearingSent >= 1);
		var rc = QtHost.QtHostRuntime.GrabPng("/tmp/q12-nav-a.png");
		Console.Error.WriteLine($"[Sailfish] Qt nav diag: screenshot rc={rc} -> /tmp/q12-nav-a.png");

		// Leg B: native-side pop (the Sailfish back gesture) → MAUI follows.
		var syncsBefore = renderer.NativePopSyncs;
		Console.Error.WriteLine("[Sailfish] Qt nav diag: leg B — simulating the Sailfish back gesture (pageStack.pop) — MAUI must follow");
		QtHost.QtHostRuntime.PopPage(immediate: true);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900),
			() => VerifyNavLegB(renderer, dispatcher, nav, syncsBefore));
	}

	private void VerifyNavLegB(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher,
		NavigationPage nav, long syncsBefore)
	{
		_qtNavChecks.Check($"leg B gesture pop: the native registry shrink was detected (popSyncs +{renderer.NativePopSyncs - syncsBefore}>=1) and MAUI popped to match (stack {nav.Navigation.NavigationStack.Count}=={_qtNavBaseDepth})",
			renderer.NativePopSyncs > syncsBefore && nav.Navigation.NavigationStack.Count == _qtNavBaseDepth);
		_qtNavChecks.Check($"leg B mirror: [{string.Join(",", renderer.NativePageIds)}] depth {renderer.NativePageIds.Count}=={_qtNavBaseDepth} and CurrentPage is the pre-push page '{_qtNavStartPage?.Title}' (got '{renderer.CurrentPage?.Title}')",
			renderer.NativePageIds.Count == _qtNavBaseDepth && ReferenceEquals(renderer.CurrentPage, _qtNavStartPage));

		// Leg C: modal push → its own model page on top.
		var pushesBefore = renderer.NativePushes;
		var modal = BuildNavLegPage("Q12 Nav M", "leg C — pushed through PushModalAsync");
		Console.Error.WriteLine("[Sailfish] Qt nav diag: leg C — PushModalAsync 'Q12 Nav M' (modals map onto extra model pages)");
		_ = nav.Navigation.PushModalAsync(modal);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900),
			() => VerifyNavLegC(renderer, dispatcher, nav, modal, pushesBefore));
	}

	private void VerifyNavLegC(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher,
		NavigationPage nav, Page modal, long pushesBefore)
	{
		var stackNow = nav.Navigation.NavigationStack.Count;
		_qtNavChecks.Check($"leg C modal push: ModalStack.Count==1 (got {nav.Navigation.ModalStack.Count}), native push +{renderer.NativePushes - pushesBefore}, mirror [{string.Join(",", renderer.NativePageIds)}] depth {renderer.NativePageIds.Count}=={stackNow + 1}",
			nav.Navigation.ModalStack.Count == 1 && renderer.NativePushes > pushesBefore &&
			renderer.NativePageIds.Count == stackNow + 1);
		var nativeTitle = QtHost.QtHostRuntime.Eval("pageStack.currentPage.pageTitle");
		_qtNavChecks.Check($"leg C native top: the modal's model page is on top ('{nativeTitle}'=='Q12 Nav M') and renderer.CurrentPage is the modal",
			nativeTitle == "Q12 Nav M" && ReferenceEquals(renderer.CurrentPage, modal));
		var rc = QtHost.QtHostRuntime.GrabPng("/tmp/q12-nav-modal.png");
		Console.Error.WriteLine($"[Sailfish] Qt nav diag: screenshot rc={rc} -> /tmp/q12-nav-modal.png");

		// Leg D: hardware Back → the MODAL pops first.
		var popsBefore = renderer.NativePops;
		Console.Error.WriteLine("[Sailfish] Qt nav diag: leg D — hardware Back (renderer.TryPop) must pop the modal first");
		var popped = renderer.TryPop();
		_qtNavChecks.Check("leg D TryPop: reported a poppable surface (the modal owns the topmost native page)", popped);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900),
			() => VerifyNavLegD(renderer, dispatcher, nav, popsBefore));
	}

	private void VerifyNavLegD(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher,
		NavigationPage nav, long popsBefore)
	{
		_qtNavChecks.Check($"leg D hardware Back: the modal popped first (ModalStack.Count==0, got {nav.Navigation.ModalStack.Count}), native pop +{renderer.NativePops - popsBefore}, mirror depth {renderer.NativePageIds.Count}=={nav.Navigation.NavigationStack.Count}",
			nav.Navigation.ModalStack.Count == 0 && renderer.NativePops > popsBefore &&
			renderer.NativePageIds.Count == nav.Navigation.NavigationStack.Count);
		_qtNavChecks.Check($"leg D current page: back on the pre-modal page '{_qtNavStartPage?.Title}' (got '{renderer.CurrentPage?.Title}')",
			ReferenceEquals(renderer.CurrentPage, _qtNavStartPage));

		// Leg E: two pushes then PopToRootAsync → the native stack collapses.
		Console.Error.WriteLine("[Sailfish] Qt nav diag: leg E — PushAsync 'Q12 Nav E1' then 'Q12 Nav E2', then PopToRootAsync");
		_ = nav.PushAsync(BuildNavLegPage("Q12 Nav E1", "leg E — first of two pushes"));
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(800), () =>
		{
			_ = nav.PushAsync(BuildNavLegPage("Q12 Nav E2", "leg E — second of two pushes"));
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900),
				() => VerifyNavLegE(renderer, dispatcher, nav));
		});
	}

	private void VerifyNavLegE(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, NavigationPage nav)
	{
		_qtNavChecks.Check($"leg E double push: MAUI stack {nav.Navigation.NavigationStack.Count}=={_qtNavBaseDepth + 2}, mirror [{string.Join(",", renderer.NativePageIds)}] depth {renderer.NativePageIds.Count}=={_qtNavBaseDepth + 2}",
			nav.Navigation.NavigationStack.Count == _qtNavBaseDepth + 2 &&
			renderer.NativePageIds.Count == _qtNavBaseDepth + 2);
		var popsBefore = renderer.NativePops;
		Console.Error.WriteLine("[Sailfish] Qt nav diag: leg E — PopToRootAsync (the native stack must collapse to the root model page)");
		_ = nav.PopToRootAsync();
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1400),
			() => FinishQtNavDiagnostics(renderer, dispatcher, nav, popsBefore));
	}

	private void FinishQtNavDiagnostics(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher,
		NavigationPage nav, long popsBefore)
	{
		var rootId = renderer.NativePageIds.Count > 0 ? renderer.NativePageIds[0] : "?";
		_qtNavChecks.Check($"leg E PopToRoot: MAUI stack collapsed to the root (got {nav.Navigation.NavigationStack.Count}==1), native pops +{renderer.NativePops - popsBefore}>=2, mirror [{string.Join(",", renderer.NativePageIds)}] depth {renderer.NativePageIds.Count}==1",
			nav.Navigation.NavigationStack.Count == 1 && renderer.NativePops - popsBefore >= 2 &&
			renderer.NativePageIds.Count == 1);
		_qtNavChecks.Check($"leg E current page: back on the root '{_qtNavRootPage?.Title}' (got '{renderer.CurrentPage?.Title}')",
			ReferenceEquals(renderer.CurrentPage, _qtNavRootPage));
		var topId = QtHost.QtHostRuntime.Eval("pageStack.currentPage.mauiPageId");
		_qtNavChecks.Check($"leg E native top: pageStack.currentPage is the ROOT model page (mauiPageId '{topId}'=='{rootId}')",
			topId == rootId);
		// The root's content is really there: every host live, and the root model page holds the QML objects (a restored
		// page cache entry with dead handles renders nothing while the checks above still pass).
		var content = renderer.CurrentHosts.Where(h => !h.Id.StartsWith("synth-", StringComparison.Ordinal)).ToList();
		var alive = content.Count(h => h.IsAttached && QtHost.QtHostRuntime.TryItemGeometry(h.NativeHandle, out _));
		var onRoot = DiagQml.EvalNum($"(function(){{var p=window.mauiPageById('{rootId}');var n=0;if(p)for(var k in p.__hosts)n++;return n;}})()");
		_qtNavChecks.Check($"leg E root content alive: {alive}=={content.Count}>0 hosts live, {onRoot}>={content.Count} QML objects on the root model page",
			content.Count > 0 && alive == content.Count && onRoot >= content.Count);
		var rc = QtHost.QtHostRuntime.GrabPng("/tmp/q12-nav-root.png");
		Console.Error.WriteLine($"[Sailfish] Qt nav diag: screenshot rc={rc} -> /tmp/q12-nav-root.png");

		// Leg G: a modal that is itself a NavigationPage.
		RunNavLegModalNav(renderer, dispatcher, nav);
	}

	// Leg G: the deepest active surface absorbs one Back. A modal NavigationPage maps to one native page per inner page, so Back pops
	// one inner page; ResolveBackTarget is the shared decision for hardware Back and native pops.

	private void RunNavLegModalNav(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, NavigationPage nav)
	{
		var modalNav = new NavigationPage(BuildNavLegPage("Q12 Nav G1", "leg G — modal NavigationPage, first inner page"));
		var pushesBefore = renderer.NativePushes;
		Console.Error.WriteLine("[Sailfish] Qt nav diag: leg G — PushModalAsync(new NavigationPage(...)), then an INNER PushAsync");
		_ = nav.Navigation.PushModalAsync(modalNav);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900),
			() => VerifyNavLegG1(renderer, dispatcher, nav, modalNav, pushesBefore));
	}

	private void VerifyNavLegG1(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher,
		NavigationPage nav, NavigationPage modalNav, long pushesBefore)
	{
		_qtNavChecks.Check($"leg G1 modal nav push: ModalStack.Count==1 (got {nav.Navigation.ModalStack.Count}), native push +{renderer.NativePushes - pushesBefore}>=1, mirror [{string.Join(",", renderer.NativePageIds)}] depth {renderer.NativePageIds.Count}==2 (root + one inner page)",
			nav.Navigation.ModalStack.Count == 1 && renderer.NativePushes > pushesBefore &&
			renderer.NativePageIds.Count == 2);
		_qtNavChecks.Check($"leg G1 current page: renderer.CurrentPage is the modal's inner page (got '{renderer.CurrentPage?.Title}'=='Q12 Nav G1')",
			renderer.CurrentPage?.Title == "Q12 Nav G1");

		// The covered root page keeps its list rows; its resync polls must not rebuild them (a row whose flattened root
		// layout was counted as a dead host looped create/destroy here, starving the leg).
		var bridge = renderer.Collection;
		var materialized = bridge.ItemsMaterialized;
		var destroyed = bridge.ItemsDestroyed;
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1500), () =>
		{
			_qtNavChecks.Check($"leg G1 covered root list idle: no row rebuilt under the modal (materialized {materialized}→{bridge.ItemsMaterialized}, destroyed {destroyed}→{bridge.ItemsDestroyed} over 1.5 s)",
				bridge.ItemsMaterialized == materialized && bridge.ItemsDestroyed == destroyed);
			// Across legs A–G1 a steady root list rebuilds no row for itself (0 on device); the flattened-root loop made 401.
			_qtNavChecks.Check($"list rows not rebuilt in a loop: SameRowRebuilds={bridge.SameRowRebuilds}<=5 (materialized {bridge.ItemsMaterialized}, destroyed {bridge.ItemsDestroyed})",
				bridge.SameRowRebuilds <= 5);
			PushNavLegG2(renderer, dispatcher, nav, modalNav);
		});
	}

	private void PushNavLegG2(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, NavigationPage nav, NavigationPage modalNav)
	{
		var pushesInner = renderer.NativePushes;
		var pageG2 = BuildNavLegPage("Q12 Nav G2", "leg G — second page INSIDE the modal NavigationPage");
		Console.Error.WriteLine("[Sailfish] Qt nav diag: leg G2 — PushAsync inside the modal NavigationPage (its inner stack owns a second model page)");
		_ = modalNav.PushAsync(pageG2);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900),
			() => VerifyNavLegG2(renderer, dispatcher, nav, modalNav, pageG2, pushesInner));
	}

	private void VerifyNavLegG2(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher,
		NavigationPage nav, NavigationPage modalNav, Page pageG2, long pushesInner)
	{
		_qtNavChecks.Check($"leg G2 inner push: the modal's inner stack grew to 2 (got {modalNav.Navigation.NavigationStack.Count}), native push +{renderer.NativePushes - pushesInner}>=1, mirror depth {renderer.NativePageIds.Count}==3",
			modalNav.Navigation.NavigationStack.Count == 2 && renderer.NativePushes > pushesInner &&
			renderer.NativePageIds.Count == 3);
		_qtNavChecks.Check($"leg G2 current page: renderer.CurrentPage is the inner pushed page (got '{renderer.CurrentPage?.Title}'=='Q12 Nav G2')",
			ReferenceEquals(renderer.CurrentPage, pageG2));

		Console.Error.WriteLine("[Sailfish] Qt nav diag: leg G3 — hardware Back INSIDE the modal NavigationPage must pop one inner page and KEEP the modal open");
		var popped = renderer.TryPop();
		_qtNavChecks.Check("leg G3 TryPop: reported a poppable surface (the modal nav has inner depth)", popped);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200),
			() => VerifyNavLegG3(renderer, dispatcher, nav, modalNav));
	}

	private void VerifyNavLegG3(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher,
		NavigationPage nav, NavigationPage modalNav)
	{
		// Regression guard: Back here must not close the whole modal and let the depth sync pop a second page.
		_qtNavChecks.Check($"leg G3 Back inside the modal nav: the modal is STILL OPEN (ModalStack.Count==1, got {nav.Navigation.ModalStack.Count}) and only ONE inner page was popped (inner stack {modalNav.Navigation.NavigationStack.Count}==1)",
			nav.Navigation.ModalStack.Count == 1 && modalNav.Navigation.NavigationStack.Count == 1);
		_qtNavChecks.Check($"leg G3 mirror: native depth {renderer.NativePageIds.Count}==2 and CurrentPage is the first inner page '{modalNav.RootPage?.Title}' (got '{renderer.CurrentPage?.Title}')",
			renderer.NativePageIds.Count == 2 && ReferenceEquals(renderer.CurrentPage, modalNav.RootPage));
		var rc = QtHost.QtHostRuntime.GrabPng("/tmp/q12-nav-modal-inner-back.png");
		Console.Error.WriteLine($"[Sailfish] Qt nav diag: screenshot rc={rc} -> /tmp/q12-nav-modal-inner-back.png");

		Console.Error.WriteLine("[Sailfish] Qt nav diag: leg G4 — hardware Back with the modal nav at depth 1 must CLOSE the modal");
		var popped = renderer.TryPop();
		_qtNavChecks.Check("leg G4 TryPop: reported a poppable surface (the modal itself)", popped);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200),
			() => VerifyNavLegG4(renderer, dispatcher, nav));
	}

	/// <summary>Leg G4: the second Back finds the modal nav at inner depth 1, so it closes the modal and the stack is back at the root.</summary>
	private void VerifyNavLegG4(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, NavigationPage nav)
	{
		_qtNavChecks.Check($"leg G4 Back at modal depth 1: the modal CLOSED (ModalStack.Count==0, got {nav.Navigation.ModalStack.Count}) and the native stack collapsed back to the root (mirror [{string.Join(",", renderer.NativePageIds)}] depth {renderer.NativePageIds.Count}==1)",
			nav.Navigation.ModalStack.Count == 0 && renderer.NativePageIds.Count == 1);
		_qtNavChecks.Check($"leg G4 current page: back on the root '{_qtNavRootPage?.Title}' (got '{renderer.CurrentPage?.Title}')",
			ReferenceEquals(renderer.CurrentPage, _qtNavRootPage));
		RunNavLegFault(renderer, dispatcher, nav);
	}

	// Leg H: inject pageStack rejections (FaultNextPush/FaultNextPop), since real shim rejections are never observed. A rejected push
	// must not commit the mirror and a rejected pop must roll it back; the depth sync retries both.

	private void RunNavLegFault(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, NavigationPage nav)
	{
		var failsBefore = renderer.NativeOpFailures;
		var pushesBefore = renderer.NativePushes;
		var pageH = BuildNavLegPage("Q12 Nav H", "leg H — push whose native side is injected as REJECTED");
		renderer.FaultNextPush = true;
		Console.Error.WriteLine("[Sailfish] Qt nav diag: leg H1 — injected push rejection: the mirror must NOT commit, the retry must land");
		_ = nav.PushAsync(pageH);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200),
			() => VerifyNavLegH1(renderer, dispatcher, nav, pageH, failsBefore, pushesBefore));
	}

	private void VerifyNavLegH1(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher,
		NavigationPage nav, Page pageH, long failsBefore, long pushesBefore)
	{
		// The retry lands within one poll, so the witness is the push count: a committed-then-retried push would make two native pages.
		_qtNavChecks.Check($"leg H1 injected push rejection: NativeOpFailures +{renderer.NativeOpFailures - failsBefore}>=1 and EXACTLY ONE native push survived two attempts (pushes +{renderer.NativePushes - pushesBefore}==1), mirror [{string.Join(",", renderer.NativePageIds)}] depth {renderer.NativePageIds.Count}==2 with CurrentPage '{renderer.CurrentPage?.Title}'",
			renderer.NativeOpFailures > failsBefore && renderer.NativePushes - pushesBefore == 1 &&
			renderer.NativePageIds.Count == 2 && ReferenceEquals(renderer.CurrentPage, pageH));

		var failsBeforePop = renderer.NativeOpFailures;
		var popsBefore = renderer.NativePops;
		renderer.FaultNextPop = true;
		Console.Error.WriteLine("[Sailfish] Qt nav diag: leg H2 — injected pop rejection: the mirror must roll back, the retry must pop");
		_ = nav.PopAsync();
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200),
			() => VerifyNavLegH2(renderer, dispatcher, nav, failsBeforePop, popsBefore));
	}

	private void VerifyNavLegH2(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher,
		NavigationPage nav, long failsBeforePop, long popsBefore)
	{
		// Without the rollback the rejected pop would commit and nothing would retry it (pops +0); exactly one pop proves the rollback ran.
		_qtNavChecks.Check($"leg H2 injected pop rejection: NativeOpFailures +{renderer.NativeOpFailures - failsBeforePop}>=1 and EXACTLY ONE native pop survived the rejected attempt (pops +{renderer.NativePops - popsBefore}==1), mirror [{string.Join(",", renderer.NativePageIds)}] depth {renderer.NativePageIds.Count}==1 with CurrentPage back on the root (got '{renderer.CurrentPage?.Title}')",
			renderer.NativeOpFailures > failsBeforePop && renderer.NativePops - popsBefore == 1 &&
			renderer.NativePageIds.Count == 1 && ReferenceEquals(renderer.CurrentPage, _qtNavRootPage));
		RunNavLegDoublePop(renderer, dispatcher, nav);
	}

	// Leg I: two immediate pops in one tick shrink the registry by 2 in one poll window; MAUI must drop two levels without re-pushing.

	private void RunNavLegDoublePop(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, NavigationPage nav)
	{
		Console.Error.WriteLine("[Sailfish] Qt nav diag: leg I — push two pages, then TWO immediate native pops in one tick");
		_ = nav.PushAsync(BuildNavLegPage("Q12 Nav I1", "leg I — first of two pages"));
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900), () =>
		{
			_ = nav.PushAsync(BuildNavLegPage("Q12 Nav I2", "leg I — second of two pages"));
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900),
				() => FireDoubleNativePop(renderer, dispatcher, nav));
		});
	}

	private void FireDoubleNativePop(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, NavigationPage nav)
	{
		_qtNavChecks.Check($"leg I setup: two pages pushed (mirror [{string.Join(",", renderer.NativePageIds)}] depth {renderer.NativePageIds.Count}==3, MAUI {nav.Navigation.NavigationStack.Count}==3)",
			renderer.NativePageIds.Count == 3 && nav.Navigation.NavigationStack.Count == 3);
		var pushesBefore = renderer.NativePushes;
		QtHost.QtHostRuntime.PopPage(immediate: true);
		QtHost.QtHostRuntime.PopPage(immediate: true);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1500),
			() => VerifyNavLegI(renderer, dispatcher, nav, pushesBefore));
	}

	private void VerifyNavLegI(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher,
		NavigationPage nav, long pushesBefore)
	{
		_qtNavChecks.Check($"leg I double native pop in one detection: MAUI dropped TWO levels (stack {nav.Navigation.NavigationStack.Count}==1), mirror [{string.Join(",", renderer.NativePageIds)}] depth {renderer.NativePageIds.Count}==1 and NO re-push of the backed-out pages (pushes +{renderer.NativePushes - pushesBefore}==0)",
			nav.Navigation.NavigationStack.Count == 1 && renderer.NativePageIds.Count == 1 &&
			renderer.NativePushes == pushesBefore);
		_qtNavChecks.Check($"leg I current page: back on the root '{_qtNavRootPage?.Title}' (got '{renderer.CurrentPage?.Title}')",
			ReferenceEquals(renderer.CurrentPage, _qtNavRootPage));
		ReportQtNavDiagnostics(renderer, dispatcher);
	}

	private void ReportQtNavDiagnostics(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		Console.Error.WriteLine($"[Sailfish] Qt nav diag: counters pushes={renderer.NativePushes} pops={renderer.NativePops} gesture-pop syncs={renderer.NativePopSyncs} " +
			$"appearing={renderer.AppearingSent} disappearing={renderer.DisappearingSent} activation={renderer.ActivationEvents}");
		// Non-zero 'rejected': the pageStack refused a mutation and the mirror rolled back. Non-zero 'pop-races blocked': the native-pop vs
		// managed-push race happened on this device and was stopped by state, not timing.
		Console.Error.WriteLine($"[Sailfish] Qt nav diag: nav-integrity native ops rejected={renderer.NativeOpFailures} pop-races blocked={renderer.NativePopRacesBlocked}");
		_qtNavChecks.Accept("OK — Q12 MAUI ⇄ Silica pageStack navigation mapping works end-to-end in both directions (PLAN Q12)");
		Console.Error.WriteLine("[Sailfish] Qt diag: nav diag done; auto-shutdown in 20s (compositor screenshot window)");
		dispatcher.DispatchDelayed(TimeSpan.FromSeconds(20), () => QtHost.QtHostRuntime.Shutdown());
	}

	// Popup diag: MAUI popups surface as native Silica dialogs via the AlertManager bridge and results flow back into the awaited tasks.
	// Legs: A alert + injected accept, B prompt (field focus, Maliit, injected keys), C action sheet (destructive first),
	// D BottomSheet → DockedPanel (Show, in-place Update, native close write-back).

	private void RunQtPopupDiagnostics(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var page = renderer.CurrentPage;
		var win = _context.Window as Microsoft.Maui.Controls.Window;
		if (page is null)
		{
			Console.Error.WriteLine("[Sailfish] Qt popup diag: no current page — legs skipped; auto-shutdown");
			QtHost.QtHostRuntime.Shutdown();
			return;
		}
		_qtPopupBaseDepth = renderer.NativePageIds.Count;
		_qtPopupChecks.Check($"baseline: current page '{page.Title}' at native depth {_qtPopupBaseDepth}>=1 — dialogs push onto the same Silica pageStack",
			_qtPopupBaseDepth >= 1);
		_qtPopupChecks.Check($"alert bridge armed: window handler attached={win?.Handler is not null}, page platform-enabled={page.IsPlatformEnabled} (AlertManager.Subscribe resolved the Qt-host IAlertManagerSubscription when the page handler attached)",
			win?.Handler is not null && page.IsPlatformEnabled);

		// Leg A: DisplayAlertAsync (the real MAUI API) → Silica AlertDialog.
		Console.Error.WriteLine("[Sailfish] Qt popup diag: leg A — Page.DisplayAlertAsync → dialogs/AlertDialog (accept via injected DialogHeader tap)");
		_qtPopupAlert = page.DisplayAlertAsync("Q13 Alert", "Silica Dialog surfaced through the MAUI AlertManager bridge", "Accept", "Cancel");
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900),
			() => VerifyPopupLegA(renderer, dispatcher, page));
	}

	private void VerifyPopupLegA(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, Page page)
	{
		var depth = DiagQml.EvalNum("pageStack.depth");
		var title = QtHost.QtHostRuntime.Eval("pageStack.currentPage.mauiTitle || ''");
		_qtPopupChecks.Check($"leg A alert surface: pageStack depth {depth:F0}=={_qtPopupBaseDepth + 1}, top dialog title '{title}'=='Q13 Alert' (DisplayAlertAsync pushed a native Silica Dialog)",
			depth == _qtPopupBaseDepth + 1 && title == "Q13 Alert");
		var grabRc = QtHost.QtHostRuntime.GrabPng("/tmp/q13-alert.png");
		Console.Error.WriteLine($"[Sailfish] Qt popup diag: leg A screenshot rc={grabRc} -> /tmp/q13-alert.png");
		PopupInjectDialogAccept("leg A", dispatcher, () =>
		{
			var alert = _qtPopupAlert!;
			_qtPopupChecks.Check($"leg A result: injected accept tap → alert-accepted → Task<bool> completed={alert.IsCompleted} result={(alert.IsCompletedSuccessfully ? alert.Result.ToString() : "?")}==True — the native result reached MAUI",
				alert.IsCompletedSuccessfully && alert.Result);
			var depth2 = DiagQml.EvalNum("pageStack.depth");
			_qtPopupChecks.Check($"leg A lifecycle: the dialog popped itself (depth {depth2:F0}=={_qtPopupBaseDepth})",
				depth2 == _qtPopupBaseDepth);

			// Leg B: DisplayPromptAsync → native TextField + Maliit keyboard.
			Console.Error.WriteLine("[Sailfish] Qt popup diag: leg B — Page.DisplayPromptAsync → dialogs/PromptDialog (native field focus, injected key taps)");
			_qtPopupPrompt = page.DisplayPromptAsync("Q13 Prompt", "Type into the native Silica field",
				accept: "Save", cancel: "Cancel", placeholder: "text");
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900),
				() => VerifyPopupLegB(renderer, dispatcher, page));
		});
	}

	/// <summary>Injects a tap on the top dialog's DialogHeader accept hotspot, then invokes <paramref name="after"/> once it has settled.</summary>
	private void PopupInjectDialogAccept(string leg, SailfishDispatcher dispatcher, Action after)
	{
		var pt = QtHost.QtHostRuntime.Eval(
			"(function(){var d=pageStack.currentPage;function F(o){if(o.acceptText!==undefined)return o;for(var i=0;i<o.children.length;++i){var r=F(o.children[i]);if(r)return r;}return null;}var h=F(d);if(!h)return '-1,-1';var p=h.mapToItem(d,h.width-40,h.height/2);return p.x+','+p.y;})()");
		if (CtlTryPoint(pt, out var ax, out var ay))
		{
			Console.Error.WriteLine($"[Sailfish] Qt popup diag: {leg} — injecting a tap on the DialogHeader Accept at {ax:F0},{ay:F0}");
			DiagQml.Tap(ax, ay);
		}
		else
		{
			Console.Error.WriteLine($"[Sailfish] Qt popup diag: {leg} — accept header geometry unavailable ('{pt}'), tap skipped");
		}
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200), after);
	}

	private void VerifyPopupLegB(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, Page page)
	{
		var title = QtHost.QtHostRuntime.Eval("pageStack.currentPage.mauiTitle || ''");
		var probe = QtHost.QtHostRuntime.Eval("pageStack.currentPage.mauiProbe || ''");
		var vkb = QtHost.QtHostRuntime.Eval("(function(){try{return Qt.inputMethod.visible?'1':'0';}catch(e){return '?';}})()");
		_qtPopupChecks.Check($"leg B prompt surface: title '{title}'=='Q13 Prompt', field probe '{probe}' — the native TextField took focus on dialog activation (activeFocus=true; the Maliit VKB follows: Qt.inputMethod.visible={vkb}, screenshot is the visual evidence)",
			title == "Q13 Prompt" && probe.StartsWith("true|", StringComparison.OrdinalIgnoreCase));
		var grabRc = QtHost.QtHostRuntime.GrabPng("/tmp/q13-prompt.png");
		Console.Error.WriteLine($"[Sailfish] Qt popup diag: leg B screenshot rc={grabRc} -> /tmp/q13-prompt.png");
		// Injected hardware keys route to Qt's focusObject — the dialog field.
		InjectQtKeyTap(QtHost.QtHostRuntime.QtKeyA + ('O' - 'A'), "O");
		InjectQtKeyTap(QtHost.QtHostRuntime.QtKeyA + ('K' - 'A'), "K");
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(700), () =>
		{
			var probe2 = QtHost.QtHostRuntime.Eval("pageStack.currentPage.mauiProbe || ''");
			_qtPopupChecks.Check($"leg B native keyboard input: injected key taps landed in the native field (probe '{probe2}' ends '|OK')",
				probe2.EndsWith("|OK", StringComparison.Ordinal));
			PopupInjectDialogAccept("leg B", dispatcher, () =>
			{
				var prompt = _qtPopupPrompt!;
				_qtPopupChecks.Check($"leg B result: accept tap → prompt-accepted → Task<string> completed={prompt.IsCompleted} result='{(prompt.IsCompletedSuccessfully ? prompt.Result ?? "<null>" : "?")}'=='OK' — the entered text reached MAUI",
					prompt.IsCompletedSuccessfully && prompt.Result == "OK");

				// Leg C: DisplayActionSheetAsync → ActionSheet.
				Console.Error.WriteLine("[Sailfish] Qt popup diag: leg C — Page.DisplayActionSheetAsync → dialogs/ActionSheet (destructive entry first; injected entry tap)");
				_qtPopupSheet = page.DisplayActionSheetAsync("Q13 Sheet", "Cancel", "Delete", "Share", "Archive");
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900),
					() => VerifyPopupLegC(renderer, dispatcher));
			});
		});
	}

	private void VerifyPopupLegC(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var title = QtHost.QtHostRuntime.Eval("pageStack.currentPage.mauiTitle || ''");
		var items = QtHost.QtHostRuntime.Eval(
			"(function(){var it=(pageStack.currentPage.__items||[]).slice();it.sort(function(a,b){return a.y-b.y;});var s=[];for(var i=0;i<it.length;i++)s.push(it[i].label+(it[i].visible?'':'(hidden)'));return s.join(',');})()");
		var cancel = QtHost.QtHostRuntime.Eval(
			"(function(){var d=pageStack.currentPage;function F(o){if(o.cancelText!==undefined)return o;for(var i=0;i<o.children.length;++i){var r=F(o.children[i]);if(r)return r;}return null;}var h=F(d);return h?h.cancelText:'?'})()");
		_qtPopupChecks.Check($"leg C sheet surface: title '{title}'=='Q13 Sheet', entries [{items}] with the destructive 'Delete' FIRST, cancel '{cancel}'=='Cancel' in the DialogHeader",
			title == "Q13 Sheet" && items.StartsWith("Delete,", StringComparison.Ordinal) &&
			items.Contains("Share", StringComparison.Ordinal) &&
			items.Contains("Archive", StringComparison.Ordinal) &&
			cancel == "Cancel");
		var grabRc = QtHost.QtHostRuntime.GrabPng("/tmp/q13-sheet.png");
		Console.Error.WriteLine($"[Sailfish] Qt popup diag: leg C screenshot rc={grabRc} -> /tmp/q13-sheet.png");
		// Tap the 'Share' entry: map its center out of the dialog page.
		var pt = QtHost.QtHostRuntime.Eval(
			"(function(){var it=pageStack.currentPage.__items||[];for(var i=0;i<it.length;i++){if(it[i].label==='Share'){var p=it[i].mapToItem(pageStack.currentPage,it[i].width/2,it[i].height/2);return p.x+','+p.y;}}return '-1,-1';})()");
		if (CtlTryPoint(pt, out var x, out var y))
		{
			Console.Error.WriteLine($"[Sailfish] Qt popup diag: leg C — injecting a tap on the 'Share' entry at {x:F0},{y:F0}");
			DiagQml.Tap(x, y);
		}
		else
		{
			Console.Error.WriteLine($"[Sailfish] Qt popup diag: leg C — 'Share' entry geometry unavailable ('{pt}'), tap skipped");
		}
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200), () =>
		{
			var sheet = _qtPopupSheet!;
			_qtPopupChecks.Check($"leg C result: entry tap → action-selected → Task<string> completed={sheet.IsCompleted} result='{(sheet.IsCompletedSuccessfully ? sheet.Result ?? "<null>" : "?")}'=='Share' — the picked entry text reached MAUI",
				sheet.IsCompletedSuccessfully && sheet.Result == "Share");
			// Leg D: SailfishBottomSheet → DockedPanel.
			RunPopupLegD(renderer, dispatcher);
		});
	}

	private void RunPopupLegD(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		Console.Error.WriteLine("[Sailfish] Qt popup diag: leg D — SailfishBottomSheet → interactions/DockedPanel (Show/Update/native write-back)");
		var sheet = new SailfishBottomSheet { Text = "Q13 bottom sheet", Size = 320 };
		sheet.OpenChanged += (_, open) => { if (!open) _qtPopupSheetCloses++; };
		_qtPopupBottomSheet = sheet;
		sheet.Show();
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900), () =>
		{
			var openState = CtlPanelDrawerState();   // "10" — the panel bit first
			_qtPopupChecks.Check($"leg D BottomSheet→DockedPanel: Show() opened the native panel (panel+drawer state '{openState}' starts with 1 — Silica slide-in animation)",
				openState.StartsWith('1'));
			var grabRc = QtHost.QtHostRuntime.GrabPng("/tmp/q13-bottomsheet.png");
			Console.Error.WriteLine($"[Sailfish] Qt popup diag: leg D screenshot rc={grabRc} -> /tmp/q13-bottomsheet.png");
			// In-place property update (no host re-creation).
			sheet.Text = "Q13 updated";
			sheet.Update();
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(700), () =>
			{
				var witness = QtHost.QtHostRuntime.Eval(
					"(function(){var h=pageStack.currentPage.__hosts;var n=0,t='';for(var k in h){if(h[k].uri==='docked-panel'){n++;t=h[k].item.mauiText;}}return n+'|'+t;})()");
				_qtPopupChecks.Check($"leg D property update: docked-panel hosts+witness '{witness}'=='1|Q13 updated' — content changed IN PLACE (one host, no re-creation)",
					witness == "1|Q13 updated");
				// Native-side close (the user drag): must write back into managed.
				QtHost.QtHostRuntime.Eval(
					"(function(){var h=pageStack.currentPage.__hosts;for(var k in h){if(h[k].uri==='docked-panel'){h[k].item.open=false;return '1';}}return '0';})()");
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(700), () =>
				{
					_qtPopupChecks.Check($"leg D native write-back: a native panel close (drag simulation) reached managed state (IsOpen={sheet.IsOpen}==False, OpenChanged close events={_qtPopupSheetCloses}>=1)",
						!sheet.IsOpen && _qtPopupSheetCloses >= 1);
					sheet.Close();
					FinishQtPopupDiagnostics(renderer, dispatcher);
				});
			});
		});
	}


	private void FinishQtPopupDiagnostics(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var grabRc = QtHost.QtHostRuntime.GrabPng("/tmp/q13-popups-final.png");
		Console.Error.WriteLine($"[Sailfish] Qt popup diag: screenshot rc={grabRc} -> /tmp/q13-popups-final.png");
		Console.Error.WriteLine($"[Sailfish] Qt popup diag: counters: dialogPushes={renderer.DialogPushes} dialogResults={renderer.DialogResults} panelOpenChanges={renderer.PanelOpenChanges} nativeSheetCloses={_qtPopupSheetCloses}");
		_qtPopupChecks.Check($"bridge counters: dialog pushes={renderer.DialogPushes}==3, dialog results={renderer.DialogResults}==3 (alert+prompt+sheet all completed through the bridge)",
			renderer.DialogPushes == 3 && renderer.DialogResults == 3);
		_qtPopupChecks.Accept("OK — Q13 MAUI popups (alert/prompt/action sheet) surface as native Silica dialogs with results flowing back into MAUI tasks, and the BottomSheet maps onto the DockedPanel (PLAN Q13)");
		Console.Error.WriteLine("[Sailfish] Qt diag: popup diag done; auto-shutdown in 20s (compositor screenshot window)");
		dispatcher.DispatchDelayed(TimeSpan.FromSeconds(20), () => QtHost.QtHostRuntime.Shutdown());
	}

	// Stress diag: open → close → reopen page, background/suspend/resume, dialog open/close, destroy. Witnesses: the QML live-host census
	// returns to baseline (leaks), shim lateCallbacks==0 and postsQueued==postsRun, managed GCHandle alloc==free, activation counters via
	// QPA-injected app state, a torn-down handle reads back DEAD (-3), and a clean rc=0 exit after the destroy leg.

	private void RunQtStressDiagnostics(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var nav = RootNav;
		if (nav is null)
		{
			Console.Error.WriteLine("[Sailfish] Qt stress diag: no NavigationPage — legs skipped; auto-shutdown");
			QtHost.QtHostRuntime.Shutdown();
			return;
		}
		// Take the leak baseline only once the startup page has settled: collection item hosts materialize after the 'rendered' report.
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(400),
			() => CaptureStressBaseline(renderer, dispatcher, nav, 0, -1));
	}

	/// <summary>Re-reads the live-host census until two reads agree (or the cap hits), records the baseline and starts leg A.</summary>
	private void CaptureStressBaseline(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher,
		NavigationPage nav, int attempt, long prevLive)
	{
		var qml0 = ParseStressQml(EvalStressQmlCounts());
		if (attempt < 4 && qml0.Live != prevLive)
		{
			Console.Error.WriteLine($"[Sailfish] Qt stress diag: baseline settling (live hosts {prevLive}→{qml0.Live}, re-read in 500ms)");
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(500),
				() => CaptureStressBaseline(renderer, dispatcher, nav, attempt + 1, qml0.Live));
			return;
		}
		_qtStressBaseDepth = renderer.NativePageIds.Count;
		var stats0 = ParseStressStats(QtHost.QtHostRuntime.DiagStats());
		_qtStressBaseRegistry = stats0.Registry;
		_qtStressBaseLive = qml0.Live;
		_qtStressChecks.Check($"baseline (settled after {attempt} re-reads): native depth {_qtStressBaseDepth}>=2 (root + startup-pushed page), QML model pages {qml0.Pages}=={_qtStressBaseDepth} with {qml0.Live}>0 live MAUI hosts (+{qml0.Parked} parked in the back cache, not counted; created/destroyed totals {qml0.Created}/{qml0.Destroyed}), shim registry={stats0.Registry}, lateCallbacks={stats0.Late}==0, posts queued/run {stats0.Queued}/{stats0.Run}",
			_qtStressBaseDepth >= 2 && qml0.Pages == _qtStressBaseDepth && qml0.Live > 0 && stats0.Late == 0);

		// Leg A: open page (managed PushAsync → native model-page push).
		var pushesA = renderer.NativePushes;
		var appearingA = renderer.AppearingSent;
		var pageA = BuildNavLegPage("Q17 Stress A", "leg A — opened by the Q17 stress cycle");
		Console.Error.WriteLine("[Sailfish] Qt stress diag: leg A — open page (PushAsync 'Q17 Stress A')");
		_ = nav.PushAsync(pageA);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900), () =>
		{
			var attached = renderer.CurrentHosts.Where(h => h.IsAttached).ToList();
			_qtStressDeadHandle = attached.Count > 0 ? attached[0].NativeHandle : 0;
			_qtStressChecks.Check($"leg A open page: native depth {renderer.NativePageIds.Count}=={_qtStressBaseDepth + 1}, pushes +{renderer.NativePushes - pushesA}==1, attached hosts={attached.Count}>0, SendAppearing +{renderer.AppearingSent - appearingA}>=1",
				renderer.NativePageIds.Count == _qtStressBaseDepth + 1 && renderer.NativePushes - pushesA == 1 &&
				attached.Count > 0 && renderer.AppearingSent > appearingA);
			RunStressLegB(renderer, dispatcher, nav);
		});
	}

	private void RunStressLegB(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, NavigationPage nav)
	{
		// Leg B: close page (PopAsync → destroy-before-pop + deleteLater cascade).
		var popsB = renderer.NativePops;
		var disappearingB = renderer.DisappearingSent;
		Console.Error.WriteLine("[Sailfish] Qt stress diag: leg B — close page (PopAsync; hosts deregistered + deleteLater'd)");
		_ = nav.PopAsync();
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1500), () =>
		{
			var statsB = ParseStressStats(QtHost.QtHostRuntime.DiagStats());
			_qtStressChecks.Check($"leg B close page: native depth {renderer.NativePageIds.Count}=={_qtStressBaseDepth}, pops +{renderer.NativePops - popsB}==1, SendDisappearing +{renderer.DisappearingSent - disappearingB}>=1",
				renderer.NativePageIds.Count == _qtStressBaseDepth && renderer.NativePops - popsB == 1 &&
				renderer.DisappearingSent > disappearingB);
			var qmlB = ParseStressQml(EvalStressQmlCounts());
			_qtStressChecks.Check($"leg B no leaked QML objects: live MAUI hosts across model pages {qmlB.Live}=={_qtStressBaseLive}, pages {qmlB.Pages}=={_qtStressBaseDepth} (the popped page's hosts were destroyed and Statistics' hosts recreated in place; created/destroyed totals {qmlB.Created}/{qmlB.Destroyed}, shim registry={statsB.Registry})",
				qmlB.Live == _qtStressBaseLive && qmlB.Pages == _qtStressBaseDepth);
			var deadReported = _qtStressDeadHandle != 0 &&
				!QtHost.QtHostRuntime.TryItemGeometry(_qtStressDeadHandle, out _);
			_qtStressChecks.Check($"leg B no use-after-free: the torn-down handle 0x{_qtStressDeadHandle:x} reads back DEAD through the QPointer registry (geometry lookup refused={deadReported}) — stale handles fail deterministically (-3), never dereference",
				deadReported);
			RunStressLegC(renderer, dispatcher, nav);
		});
	}

	private void RunStressLegC(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, NavigationPage nav)
	{
		// Leg C: open a second page instance.
		var pushesC = renderer.NativePushes;
		_qtStressPageC = BuildNavLegPage("Q17 Stress C", "leg C — re-opened; the background/resume and dialog legs run on top of it");
		Console.Error.WriteLine("[Sailfish] Qt stress diag: leg C — open page again (PushAsync 'Q17 Stress C')");
		_ = nav.PushAsync(_qtStressPageC);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900), () =>
		{
			_qtStressChecks.Check($"leg C re-open: native depth {renderer.NativePageIds.Count}=={_qtStressBaseDepth + 1}, pushes +{renderer.NativePushes - pushesC}==1, renderer current page is the new instance ('{renderer.CurrentPage?.Title}')",
				renderer.NativePageIds.Count == _qtStressBaseDepth + 1 && renderer.NativePushes - pushesC == 1 &&
				ReferenceEquals(renderer.CurrentPage, _qtStressPageC));
			RunStressLegD(renderer, dispatcher, nav);
		});
	}

	private void RunStressLegD(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, NavigationPage nav)
	{
		// Leg D: background → suspend → resume, injected at the QPA boundary (the channel lipstick drives in production). The app-state part
		// is deterministic; window activation needs the compositor to grant it, which an SSH-launched run never gets, so without an active
		// window the checks require no spurious activation events instead.
		var activeAtStart = renderer.LastWindowActive == true;
		var deact0 = renderer.DeactivatedSent;
		var stop0 = renderer.StoppedSent;
		var act0 = renderer.ActivatedSent;
		var res0 = renderer.ResumedSent;
		Console.Error.WriteLine($"[Sailfish] Qt stress diag: leg D — background app (QPA: deactivate window + ApplicationInactive; window.active at leg start={activeAtStart})");
		QtHost.QtHostRuntime.DiagSetAppState(2 /*Qt::ApplicationInactive*/, 0 /*deactivate*/);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(700), () =>
		{
			var stateQml = QtHost.QtHostRuntime.Eval("(typeof Qt!=='undefined'&&Qt.application?Qt.application.state:-1)");
			if (activeAtStart)
				_qtStressChecks.Check($"leg D background: Deactivated +{renderer.DeactivatedSent - deact0}>=1 (window.active mirror={renderer.LastWindowActive?.ToString() ?? "?"}==False), Qt.application.state='{stateQml}'=='2', Stopped not fired yet (+{renderer.StoppedSent - stop0}==0)",
					renderer.DeactivatedSent > deact0 && renderer.LastWindowActive == false &&
					stateQml == "2" && renderer.StoppedSent == stop0);
			else
				_qtStressChecks.Check($"leg D background: Qt.application.state='{stateQml}'=='2' via QPA injection, Stopped not fired yet (+{renderer.StoppedSent - stop0}==0); window.active was never granted by the compositor in this launch mode (mirror={renderer.LastWindowActive?.ToString() ?? "?"}) — no Deactivated can ride it and NONE fired spuriously (+{renderer.DeactivatedSent - deact0}==0)",
					stateQml == "2" && renderer.StoppedSent == stop0 &&
					renderer.DeactivatedSent == deact0 && renderer.LastWindowActive == false);
			Console.Error.WriteLine("[Sailfish] Qt stress diag: leg D — suspend (QPA: ApplicationSuspended)");
			QtHost.QtHostRuntime.DiagSetAppState(0 /*Qt::ApplicationSuspended*/, -1 /*activation unchanged*/);
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(500), () =>
			{
				_qtStressChecks.Check($"leg D suspend: Stopped +{renderer.StoppedSent - stop0}==1 (appState mirror={renderer.LastAppState}==0)",
					renderer.StoppedSent - stop0 == 1 && renderer.LastAppState == 0);
				Console.Error.WriteLine("[Sailfish] Qt stress diag: leg D — resume app (QPA: activate window + ApplicationActive)");
				QtHost.QtHostRuntime.DiagSetAppState(4 /*Qt::ApplicationActive*/, 1 /*activate*/);
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(700), () =>
				{
					var stateQml2 = QtHost.QtHostRuntime.Eval("(typeof Qt!=='undefined'&&Qt.application?Qt.application.state:-1)");
					if (activeAtStart)
						_qtStressChecks.Check($"leg D resume: Activated +{renderer.ActivatedSent - act0}>=1 AND Resumed +{renderer.ResumedSent - res0}>=1 (window.active mirror={renderer.LastWindowActive?.ToString() ?? "?"}==True, Qt.application.state='{stateQml2}'=='4')",
							renderer.ActivatedSent > act0 && renderer.ResumedSent > res0 &&
							renderer.LastWindowActive == true && stateQml2 == "4");
					else
						_qtStressChecks.Check($"leg D resume: Resumed +{renderer.ResumedSent - res0}>=1 on appState 0→4 (Qt.application.state='{stateQml2}'=='4'); Activated +{renderer.ActivatedSent - act0}==0 — window.active stays compositor-denied in this launch mode (mirror={renderer.LastWindowActive?.ToString() ?? "?"}), no spurious activation",
							renderer.ResumedSent > res0 && stateQml2 == "4" &&
							renderer.ActivatedSent == act0 && renderer.LastWindowActive == false);
					RunStressLegE(renderer, dispatcher, nav);
				});
			});
		});
	}

	private void RunStressLegE(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, NavigationPage nav)
	{
		var page = _qtStressPageC;
		if (page is null)
		{
			_qtStressChecks.Check("leg E dialog open: page C alive", false);
			FinishQtStressDiagnostics(renderer, dispatcher, nav);
			return;
		}
		var dialogs0 = renderer.DialogPushes;
		var results0 = renderer.DialogResults;
		Console.Error.WriteLine("[Sailfish] Qt stress diag: leg E — open dialog (DisplayAlertAsync → native Silica AlertDialog)");
		_qtStressAlert = page.DisplayAlertAsync("Q17 Stress", "dialog open/close leg of the Q17 stress cycle", "Accept", "Cancel");
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900), () =>
		{
			var title = QtHost.QtHostRuntime.Eval("pageStack.currentPage.mauiTitle || ''");
			var depth = DiagQml.EvalNum("pageStack.depth");
			_qtStressChecks.Check($"leg E dialog open: DialogPushes +{renderer.DialogPushes - dialogs0}==1, pageStack depth {depth:F0}=={_qtStressBaseDepth + 2}, top title '{title}'=='Q17 Stress'",
				renderer.DialogPushes - dialogs0 == 1 && depth == _qtStressBaseDepth + 2 && title == "Q17 Stress");
			// Leg F: close the dialog with an injected tap on the DialogHeader accept.
			Console.Error.WriteLine("[Sailfish] Qt stress diag: leg F — close dialog (injected accept tap)");
			PopupInjectDialogAccept("Q17 leg F", dispatcher, () =>
			{
				var alert = _qtStressAlert!;
				var depth2 = DiagQml.EvalNum("pageStack.depth");
				_qtStressChecks.Check($"leg F dialog close: Task<bool> completed={alert.IsCompleted} result={(alert.IsCompletedSuccessfully ? alert.Result.ToString() : "?")}==True, DialogResults +{renderer.DialogResults - results0}==1, the dialog popped itself (depth {depth2:F0}=={_qtStressBaseDepth + 1})",
					alert.IsCompletedSuccessfully && alert.Result &&
					renderer.DialogResults - results0 == 1 && depth2 == _qtStressBaseDepth + 1);
				FinishQtStressDiagnostics(renderer, dispatcher, nav);
			});
		});
	}

	private void FinishQtStressDiagnostics(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, NavigationPage nav)
	{
		// Leg G: close the re-opened page, sweep the invariants, then destroy; the run log proves the teardown was clean.
		var popsG = renderer.NativePops;
		Console.Error.WriteLine("[Sailfish] Qt stress diag: leg G — close page C, final invariant sweep, then destroy (Shutdown = the teardown leg)");
		_ = nav.PopAsync();
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1800), () =>
		{
			var stats = ParseStressStats(QtHost.QtHostRuntime.DiagStats());
			var qmlDepth = DiagQml.EvalNum("pageStack.depth");
			_qtStressChecks.Check($"leg G close page: native depth {renderer.NativePageIds.Count}=={_qtStressBaseDepth}, pops +{renderer.NativePops - popsG}==1, pageStack.depth {qmlDepth:F0}=={_qtStressBaseDepth}",
				renderer.NativePageIds.Count == _qtStressBaseDepth && renderer.NativePops - popsG == 1 &&
				qmlDepth == _qtStressBaseDepth);
			var qmlG = ParseStressQml(EvalStressQmlCounts());
			_qtStressChecks.Check($"no leaked QML objects: live MAUI hosts {qmlG.Live}=={_qtStressBaseLive}, model pages {qmlG.Pages}=={_qtStressBaseDepth} after 2×open/close + dialog + background/resume (QML created/destroyed totals {qmlG.Created}/{qmlG.Destroyed}; shim handle registry={stats.Registry}, baseline was {_qtStressBaseRegistry} — attach timing makes the registry advisory)",
				qmlG.Live == _qtStressBaseLive && qmlG.Pages == _qtStressBaseDepth);
			_qtStressChecks.Check($"no callback after disposal: shim lateCallbacks={stats.Late}==0 (pointer/key/tick/QML-event/post paths all guarded through the cycle)",
				stats.Late == 0);
			_qtStressChecks.Check($"no lost posts: shim postsQueued {stats.Queued}==postsRun {stats.Run} (rejected {stats.Rejected} — every accepted trampoline was delivered on the Qt thread)",
				stats.Queued == stats.Run);
			var mq = QtHost.QtHostRuntime.PostsQueued;
			var mr = QtHost.QtHostRuntime.PostsRun;
			var ma = QtHost.QtHostRuntime.HandlesAllocated;
			var mf = QtHost.QtHostRuntime.HandlesFreed;
			_qtStressChecks.Check($"no stale GCHandle: managed trampolines allocated {ma}==freed {mf}, posts queued {mq}==run {mr} (rejected {QtHost.QtHostRuntime.PostsRejected})",
				ma == mf && mq == mr);
			var failed = _qtStressChecks.Failed;
			Console.Error.WriteLine($"[Sailfish] QT STRESS DIAG: {_qtStressChecks.Count - failed}/{_qtStressChecks.Count} checks OK — " +
				$"open/close×2, background/suspend/resume, dialog open/close: qmlLive={qmlG.Live}/{_qtStressBaseLive} late={stats.Late} posts={stats.Queued}/{stats.Run} handles={ma}/{mf} — " +
				(failed == 0
					? "OK — Q17 lifecycle/threading invariants hold (PLAN Q17)"
					: $"{failed} FAILED — see the CHECK lines above"));
			Console.Error.WriteLine("[Sailfish] Qt stress diag: destroy leg — Shutdown(); the run log must now show 'shutdown: diag stats … lateCallbacks=0', a clean 'event loop exited rc=0' and the idempotent Shutdown re-call (no crash during teardown)");
			QtHost.QtHostRuntime.Shutdown();
		});
	}


	/// <summary>Parses the sailfish_host_diag_stats JSON (-1 = missing field).</summary>
	private static StressStats ParseStressStats(string json)
	{
		try
		{
			using var doc = System.Text.Json.JsonDocument.Parse(json);
			var root = doc.RootElement;
			long Get(string n) => root.TryGetProperty(n, out var v) && v.TryGetInt64(out var l) ? l : -1;
			return new StressStats(Get("registry"), Get("postsQueued"), Get("postsRun"), Get("postsRejected"), Get("lateCallbacks"));
		}
		catch
		{
			return new StressStats(-1, -1, -1, -1, -1);
		}
	}

	private readonly record struct StressStats(long Registry, long Queued, long Run, long Rejected, long Late);

	// QML-side leak census: each MauiModelPage's live __hosts plus created/destroyed totals. The shim handle registry can't be used
	// (managed attach trails host creation), and the page parked in the back cache is excluded as deliberate retention.
	// Hosts of parked pages (the page cache) count as parked, not live: deliberate retention, not a leak.
	private const string StressQmlCountsJs =
		"(function(parked){var set={};for(var j=0;j<parked.length;++j)set[parked[j]]=1;" +
		"var p=window.mauiPages||[];var live=0,cr=0,de=0,pk=0;" +
		"for(var i=0;i<p.length;++i){var h=p[i].__hosts||{};for(var k in h){if(set[k])pk++;else live++;}" +
		"cr+=p[i].createdTotal||0;de+=p[i].destroyedTotal||0;}" +
		"return JSON.stringify({pages:p.length,live:live,parked:pk,created:cr,destroyed:de});})";

	private static string EvalStressQmlCounts() =>
		QtHost.QtHostRuntime.Eval($"{StressQmlCountsJs}({QtHost.BridgeValue.Serialize(QtHost.QtHostPageRenderer.Current?.ParkedHostIds ?? Array.Empty<string>())})");

	/// <summary>Parses the StressQmlCountsJs JSON (-1 = missing field).</summary>
	private static StressQml ParseStressQml(string json)
	{
		try
		{
			using var doc = System.Text.Json.JsonDocument.Parse(json);
			var root = doc.RootElement;
			long Get(string n) => root.TryGetProperty(n, out var v) && v.TryGetInt64(out var l) ? l : -1;
			return new StressQml(Get("pages"), Get("live"), Get("parked"), Get("created"), Get("destroyed"));
		}
		catch
		{
			return new StressQml(-1, -1, -1, -1, -1);
		}
	}

	private readonly record struct StressQml(long Pages, long Live, long Parked, long Created, long Destroyed);

	// Collection diag: CollectionView on the native virtualized Silica ListView (QML owns delegates/flicking, MAUI owns content, selection
	// and scroll state). Legs: A row model + delegates, B injected tap → selection, C INCC add/remove in place, D ScrollTo + native flick
	// → Scrolled, E top pull inside a RefreshView → Refreshing.

	private void RunQtCollectionDiagnostics(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var page = renderer.CurrentPage;
		if (page?.Navigation is null)
		{
			Console.Error.WriteLine("[Sailfish] Qt collection diag: no current page/navigation — legs skipped; auto-shutdown");
			QtHost.QtHostRuntime.Shutdown();
			return;
		}

		_qtColItems = new System.Collections.ObjectModel.ObservableCollection<string>();
		for (var i = 0; i < _qtColRows; i++)
			_qtColItems.Add($"Q14 item {i:D2}");

		var list = new CollectionView
		{
			ItemsSource = _qtColItems,
			SelectionMode = SelectionMode.Single,
			Header = "Q14 collection header",
			Footer = "Q14 collection footer",
			ItemTemplate = new DataTemplate(() =>
			{
				var label = SelfTextLabel();
				label.FontSize = 18;
				label.TextColor = Colors.White;
				label.Margin = new Thickness(16, 10);
				return label;
			}),
		};
		list.SelectionChanged += (_, _) => _qtColSelections++;
		list.Scrolled += (_, e) => { _qtColScrolled++; _qtColLastScroll = e; };
		// The leg D scroll near the end must land inside the RemainingItemsThreshold window and fire it.
		list.RemainingItemsThreshold = 20;
		list.RemainingItemsThresholdReached += (_, _) =>
		{
			_qtColThresholds++;
			Console.Error.WriteLine("[Sailfish] Qt collection diag: RemainingItemsThresholdReached fired");
		};
		_qtColView = list;
		// The page has no ToolbarItems, so the hosted list owns the top overscroll and the leg E pull must reach RefreshView.Refreshing.
		_qtColRefresh = new RefreshView { Content = list };
		_qtColRefresh.Refreshing += (_, _) =>
		{
			_qtColRefreshes++;
			Console.Error.WriteLine("[Sailfish] Qt collection diag: RefreshView.Refreshing fired");
		};
		_qtColPage = new ContentPage
		{
			Title = "Q14 Collection",
			BackgroundColor = Color.FromArgb("#101820"),
			Content = _qtColRefresh,
		};

		_qtColChecks.Check($"baseline: pushing the diagnostics page 'Q14 Collection' (40 items, Single selection, Header/Footer slots) onto the MAUI stack — native model-page depth {renderer.NativePageIds.Count}>=1",
			renderer.NativePageIds.Count >= 1);

		Console.Error.WriteLine("[Sailfish] Qt collection diag: pushing 'Q14 Collection' — the native ListView must build rows and materialize delegates");
		_ = page.Navigation.PushAsync(_qtColPage);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(2200),
			() => VerifyColLegA(renderer, dispatcher));
	}


	// Adapter objects aren't JS identifiers in the Eval scope (objectName ≠ QML id), so find them by an objectName walk from the current page.
	private static double ColPropNum(string obj, string prop) =>
		DiagQml.EvalNum("(function(){function F(o){if(o.objectName==='" + obj + "')return o;var k=o.children;if(k)for(var i=0;i<k.length;++i){var r=F(k[i]);if(r)return r;}return null;}var t=F(pageStack.currentPage);return t?(t." + prop + "):-1;})()");

	/// <summary>"visible:emptyRows" for the list <paramref name="obj"/>: delegates inside the viewport, and the rows among
	/// them holding no MAUI host (a row the user sees blank).</summary>
	private static string ColEmptyVisibleRows(string obj) =>
		QtHost.QtHostRuntime.Eval("(function(){function F(o){if(o.objectName==='" + obj + "')return o;var k=o.children;if(k)for(var i=0;i<k.length;++i){var r=F(k[i]);if(r)return r;}return null;}" +
			"var l=F(pageStack.currentPage);if(!l||!l.contentItem)return '?';var c=l.contentItem.children,top=l.contentY,bot=top+l.height,miss=[],n=0;" +
			"for(var i=0;i<c.length;++i){var d=c[i];if(!d.objectName||d.objectName.indexOf('__r')<0||!d.visible||d.height<=0)continue;" +
			"if(d.y+d.height<=top||d.y>=bot)continue;n++;var k=d.children,has=false;" +
			"for(var j=0;j<k.length;++j)if(k[j].objectName&&k[j].objectName.indexOf('maui_e')===0){has=true;break;}" +
			"if(!has){var dup=0;for(var q=0;q<c.length;++q)if(c[q].objectName===d.objectName)dup++;miss.push(d.mauiRow+(dup>1?'x'+dup:''));}}return n+':'+miss.join(',');})()");

	private void CheckColVisibleRows(string? obj, string when)
	{
		var report = obj is null ? "?" : ColEmptyVisibleRows(obj);
		var parts = report.Split(':');
		var visible = parts.Length == 2 && int.TryParse(parts[0], out var v) ? v : -1;
		var empty = parts.Length == 2 ? parts[1] : "?";
		_qtColChecks.Check($"{when}: every row in the viewport holds its item content ({visible} visible delegates, empty rows [{empty}])",
			visible > 0 && empty.Length == 0);
	}

	private static string ColPropStr(string obj, string prop) =>
		QtHost.QtHostRuntime.Eval("(function(){function F(o){if(o.objectName==='" + obj + "')return o;var k=o.children;if(k)for(var i=0;i<k.length;++i){var r=F(k[i]);if(r)return r;}return null;}var t=F(pageStack.currentPage);return t?(''+t." + prop + "):'';})()");

	private bool _pulleyGestureDone;

	/// <summary>
	/// Injects a slow pull (press in the scroller's header band, where there are no tappable rows) so Silica opens the pulley
	/// natively and keeps it open; a faked menu state never paints.
	/// </summary>
	private void MaybeInjectPulleyGesture(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var spec = SailfishEnv.Get("MAUI_SAILFISH_PULL_GESTURE");
		if (_pulleyGestureDone || string.IsNullOrEmpty(spec))
			return;
		_pulleyGestureDone = true;
		if (!int.TryParse(spec, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var delayMs))
			return;
		Console.Error.WriteLine($"[Sailfish] Qt pulley gesture: armed delay={delayMs}ms");
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(delayMs), () =>
		{
			try
			{
				var pt = QtHost.QtHostRuntime.Eval(
					"(function(){var p=(typeof window!=='undefined'&&window.mauiModelPage?window.mauiModelPage:pageStack.currentPage);" +
					"if(!p||!p.mauiPulleySurface)return '';var f=p.mauiPulleySurface('pullDownMenu');if(!f)return '';" +
					"var o=f.mapToItem(null,0,0);return (f.width/2)+','+(o.y+40);})()");
				var parts = pt.Split(',');
				if (parts.Length != 2 ||
				    !double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var cx) ||
				    !double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var cy) ||
				    cx <= 0 || cy <= 0)
				{
					Console.Error.WriteLine($"[Sailfish] Qt pulley gesture: no scroller point from '{pt}'");
					return;
				}
				Console.Error.WriteLine($"[Sailfish] Qt pulley gesture: injecting a slow pull at {cx:F0},{cy:F0}");
				// Freeze managed ScrollY pushes during the gesture: a setMauiScroll mid-drag yanks contentY back to 0.
				QtHost.QtHostRuntime.Eval(
					"(function(){var p=(typeof window!=='undefined'&&window.mauiModelPage?window.mauiModelPage:pageStack.currentPage);if(p)p.mauiHoldScrollY=true;return 'ok';})()");
				QtHost.QtHostRuntime.InjectPointer(0, cx, cy);
				for (var step = 1; step <= 8; step++)
				{
					var dy = step * 45.0;
					dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(60 * step),
						() => QtHost.QtHostRuntime.InjectPointer(2, cx, cy + dy));
				}
				// Silica chooses snap-open vs bounce-back from the release velocity, so crawl the last pixels for a slow release past the item size.
				for (var creep = 1; creep <= 4; creep++)
				{
					var dy = 360.0 + creep * 4.0;
					dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(480 + 90 * creep),
						() => QtHost.QtHostRuntime.InjectPointer(2, cx, cy + dy));
				}
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1000),
					() => QtHost.QtHostRuntime.InjectPointer(1, cx, cy + 376.0));
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(950), () =>
				{
					var st = QtHost.QtHostRuntime.Eval(
						"(function(){var p=(typeof window!=='undefined'&&window.mauiModelPage?window.mauiModelPage:pageStack.currentPage);" +
						"if(!p||!p.mauiPulleySurface)return 'nosurf';var f=p.mauiPulleySurface('pullDownMenu');if(!f)return 'nomenu';" +
						"var m=f.pullDownMenu;return 'active='+m.active+' y='+f.contentY+' dragging='+f.dragging+' permitted='+m._activationPermitted+' inhibited='+m._activationInhibited;})()");
					Console.Error.WriteLine($"[Sailfish] Qt pulley gesture: mid-drag state {st}");
				});
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine($"[Sailfish] Qt pulley gesture: callback failed: {ex.Message}");
			}
		});
	}

	private void VerifyColLegA(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var bridge = renderer.Collection;
		var obj = bridge.FirstListObjectName;
		_qtColChecks.Check($"leg A page: renderer.CurrentPage is the diagnostics page (got '{renderer.CurrentPage?.Title}')",
			ReferenceEquals(renderer.CurrentPage, _qtColPage));
		// Virtualized: only the visible+cached window is materialized, so require half the rows built here; full coverage is the native count below.
		_qtColChecks.Check($"leg A adapter: list-view host attached (objectName '{obj ?? "<none>"}') and rows built by MAUI for the on-screen window (virtualized: RowsBuilt={bridge.RowsBuilt}>={bridge.TotalRows / 2}, LiveRows={bridge.LiveRows}>0, TotalRows={bridge.TotalRows}=={_qtColRows})",
			obj is not null && bridge.RowsBuilt >= bridge.TotalRows / 2 && bridge.LiveRows > 0 && bridge.TotalRows == _qtColRows);
		var count = obj is null ? double.NaN : ColPropNum(obj, "count");
		var probe = obj is null ? "" : ColPropStr(obj, "mauiProbe");
		_qtColChecks.Check($"leg A native model: ListView count {count:F0}=={_qtColRows} — the row JSON reached the QML ListModel (probe '{probe}')",
			count == _qtColRows);
		// Tree probe: are row delegates reachable under the current page, and does the shim's find_object see them?
		var treeProbe = QtHost.QtHostRuntime.Eval(
			"(function(){function N(o,a){var nm=o.objectName;if(nm&&(''+nm).indexOf('maui_')===0)a.push(''+nm);var k=o.children;if(k)for(var i=0;i<k.length;++i)N(k[i],a);return a;}" +
			"var n=N(pageStack.currentPage,[]);var r=[];for(var i=0;i<n.length;++i)if(n[i].indexOf('__r')>0)r.push(n[i]);" +
			"return 'pageNames='+n.length+' rowDelegates='+r.length+(r.length?' first='+r[0]:'');})()");
		Console.Error.WriteLine($"[Sailfish] Qt collection diag: tree probe: {treeProbe} | find_object(list)={QtHost.QtHostRuntime.FindObject(obj ?? "-")} find_object(row0)={QtHost.QtHostRuntime.FindObject((obj ?? "-") + "__r0")}");
		_qtColChecks.Check($"leg A materialization: ItemsMaterialized={bridge.ItemsMaterialized}>0 (MAUI item trees created inside row delegates) and ListEvents={bridge.ListEvents}>0 (list-item-attached/rebind events consumed)",
			bridge.ItemsMaterialized > 0 && bridge.ListEvents > 0);
		var headerH = obj is null ? double.NaN : ColPropNum(obj, "mauiHeaderH");
		var footerH = obj is null ? double.NaN : ColPropNum(obj, "mauiFooterH");
		_qtColChecks.Check($"leg A slots: header height {headerH:F0}>0 and footer height {footerH:F0}>0 (measured by MAUI, pushed into the ListView slot placeholders)",
			headerH > 0 && footerH > 0);
		var rc = QtHost.QtHostRuntime.GrabPng("/tmp/q14-collection-a.png");
		Console.Error.WriteLine($"[Sailfish] Qt collection diag: screenshot rc={rc} -> /tmp/q14-collection-a.png");

		// Leg B: an injected tap on row 2 must select its item through the bridge (MAUI is the selection authority).
		Console.Error.WriteLine("[Sailfish] Qt collection diag: leg B — injected real-Qt tap on row 2 → list-item-tapped → SelectedItem write-back");
		var tapped = false;
		if (bridge.TryGetRowPoint(2, out var tx, out var ty))
		{
			DiagQml.Tap(tx, ty);
			tapped = true;
		}
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900),
			() => VerifyColLegB(renderer, dispatcher, tapped));
	}

	private void VerifyColLegB(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, bool tapped)
	{
		var bridge = renderer.Collection;
		var obj = bridge.FirstListObjectName;
		_qtColChecks.Check($"leg B tap target: row 2 delegate resolved in the Qt scene={tapped} (real press/release injected at its center)",
			tapped);
		var sel = _qtColView?.SelectedItem as string;
		_qtColChecks.Check($"leg B selection round trip: SelectedItem '{sel}'=='Q14 item 02' (list-item-tapped → MAUI selection authority; SelectionChanged fires {_qtColSelections}>=1, SelectionsApplied={bridge.SelectionsApplied}>=1)",
			sel == "Q14 item 02" && _qtColSelections >= 1 && bridge.SelectionsApplied >= 1);
		var nativeSel = obj is null ? "" : ColPropStr(obj, "mauiSelectedRows");
		_qtColChecks.Check($"leg B highlight push: native mauiSelectedRows '{nativeSel}'=='2' (the Silica highlight follows the managed selection)",
			nativeSel == "2");
		var rc = QtHost.QtHostRuntime.GrabPng("/tmp/q14-collection-b.png");
		Console.Error.WriteLine($"[Sailfish] Qt collection diag: screenshot rc={rc} -> /tmp/q14-collection-b.png");

		// Leg C: INCC add + remove; the row model rebuilds and the same native ListView's count follows.
		Console.Error.WriteLine("[Sailfish] Qt collection diag: leg C — ObservableCollection add+remove → row-model rebuild → native count follows in place");
		var rowsBeforeLegC = bridge.RowsBuilt;   // virtualized rebuilds only re-materialize the visible window
		_qtColItems!.Add($"Q14 item {_qtColRows}");
		_qtColItems.RemoveAt(0);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900),
			() => VerifyColLegC(renderer, dispatcher, rowsBeforeLegC));
	}

	private void VerifyColLegC(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, long rowsBeforeLegC)
	{
		var bridge = renderer.Collection;
		var obj = bridge.FirstListObjectName;
		var count = obj is null ? double.NaN : ColPropNum(obj, "count");
		CheckColVisibleRows(obj, "leg C after add+remove (rows shifted and rebound)");
		// The INCC rebuild re-materializes only the visible window, so the proof is the RowsBuilt delta.
		_qtColChecks.Check($"leg C INCC: add+remove rebuilt the row model (TotalRows={bridge.TotalRows}=={_qtColRows}, RowsBuilt delta {bridge.RowsBuilt - rowsBeforeLegC}>={bridge.TotalRows / 2} from {rowsBeforeLegC} — the rebuild re-materialized the visible window) and the native count {count:F0}=={_qtColRows} followed on the SAME host '{obj}'",
			bridge.TotalRows == _qtColRows && bridge.RowsBuilt - rowsBeforeLegC >= bridge.TotalRows / 2 && count == _qtColRows);

		// Leg C2: an indexer set raises INCC Replace; the same row delegate must refresh in place (verified in VerifyColLegD).
		var rowsBuiltBeforeUpdate = bridge.RowsBuilt;
		_qtColItems![0] = "Q20 updated item";
		Console.Error.WriteLine("[Sailfish] Qt collection diag: leg C2 — in-place item update (INCC Replace) → same row delegate refreshed, no re-creation");

		// Leg D: managed ScrollTo jumps the native list; an injected flick reports list-scroll back into MAUI.
		var yBefore = obj is null ? double.NaN : ColPropNum(obj, "contentY");
		// Target near the end at every scale, so the RemainingItemsThreshold witness can fire.
		var scrollTarget = System.Math.Max(System.Math.Min(30, _qtColRows - 1), _qtColRows - 10);
		Console.Error.WriteLine($"[Sailfish] Qt collection diag: leg D — ScrollTo({scrollTarget}, Center) → mauiScrollRow/Pos/Tick → native contentY jump, then an injected flick → list-scroll → ItemsView.Scrolled");
		_qtColView!.ScrollTo(scrollTarget, -1, ScrollToPosition.Center, false);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900),
			() => VerifyColLegD(renderer, dispatcher, obj, yBefore, rowsBuiltBeforeUpdate));
	}

	private void VerifyColLegD(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, string? obj, double yBefore, long rowsBuiltBeforeUpdate)
	{
		// Leg C2 witness: row count unchanged and the visible window re-materialized with the new text.
		var bridgeNow = renderer.Collection;
		var countNow = obj is null ? double.NaN : ColPropNum(obj, "count");
		_qtColChecks.Check($"leg C2 update: INCC Replace propagated into the native list in place — row count stable {countNow:F0}=={_qtColRows} (TotalRows={bridgeNow.TotalRows}) and the visible window re-materialized with the new text (RowsBuilt {bridgeNow.RowsBuilt} > {rowsBuiltBeforeUpdate})",
			countNow == _qtColRows && bridgeNow.TotalRows == _qtColRows && bridgeNow.RowsBuilt > rowsBuiltBeforeUpdate);
		var yAfter = obj is null ? double.NaN : ColPropNum(obj, "contentY");
		var contentH = obj is null ? double.NaN : ColPropNum(obj, "contentHeight");
		var viewH = obj is null ? double.NaN : ColPropNum(obj, "height");
		var scrollable = !double.IsNaN(contentH) && !double.IsNaN(viewH) && contentH > viewH + 1;
		CheckColVisibleRows(obj, "leg D after ScrollTo near the end");
		_qtColChecks.Check(scrollable
			? $"leg D ScrollTo: managed ScrollTo(Center) jumped the native list (contentY {yBefore:F0} → {yAfter:F0}, mauiScrollTick applied)"
			: $"leg D ScrollTo: dataset fits the viewport at this scale (contentHeight {contentH:F0} <= height {viewH:F0}) — ScrollTo is a no-op by design, nothing to jump",
			scrollable ? !double.IsNaN(yAfter) && yAfter > yBefore : true);
		// Reposition near the top so the upward flick has room (StopAtBounds, and the centered target sits near the end).
		_qtColView!.ScrollTo(System.Math.Min(5, _qtColRows - 1), -1, ScrollToPosition.Start, false);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(500),
			() => InjectColFlick(renderer, dispatcher, obj));
	}

	private void InjectColFlick(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, string? obj)
	{
		var bridge = renderer.Collection;
		var scrollsBefore = bridge.ScrollsReported;
		var scrolledBefore = _qtColScrolled;
		var contentH = obj is null ? double.NaN : ColPropNum(obj, "contentHeight");
		var viewH = obj is null ? double.NaN : ColPropNum(obj, "height");
		var scrollable = !double.IsNaN(contentH) && !double.IsNaN(viewH) && contentH > viewH + 1;
		if (!scrollable)
		{
			// The dataset fits the viewport at this scale, so the flick/scroll checks are vacuous.
			_qtColChecks.Check($"leg D flick: skipped — dataset fits the viewport (contentHeight {contentH:F0} <= height {viewH:F0}); the scroll cells run at the scrollable scales", true);
			VerifyColLegE(renderer, dispatcher, obj, scrollsBefore, scrolledBefore, expectScroll: false);
			return;
		}
		if (obj is not null)
		{
			// Upward flick mid-viewport: QML owns the flick; the throttled list-scroll events must reach MAUI.
			var w = ColPropNum(obj, "width");
			var top = ColPropNum(obj, "mapToItem(null, 0, 0).y");
			var h = ColPropNum(obj, "height");
			var cx = w < 0 ? 200 : w / 2;
			var cy = (top < 0 ? 100 : top) + (h < 0 ? 300 : h / 2);
			Console.Error.WriteLine($"[Sailfish] Qt collection diag: leg D flick — injecting press/move/release at {cx:F0},{cy:F0} (w={w:F0} top={top:F0} h={h:F0})");
			QtHost.QtHostRuntime.InjectPointer(0, cx, cy);
			for (var step = 1; step <= 4; step++)
			{
				var dy = -step * 30.0;
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(40 * step),
					() => QtHost.QtHostRuntime.InjectPointer(2, cx, cy + dy));
			}
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(220),
				() => QtHost.QtHostRuntime.InjectPointer(1, cx, cy - 120.0));
		}
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900),
			() => VerifyColLegE(renderer, dispatcher, obj, scrollsBefore, scrolledBefore));
	}

	/// <summary>Leg E: the page has no pulley, so the list inside the RefreshView owns the top overscroll. A released pull past the Silica
	/// item size must set IsRefreshing and show the spinner; clearing it must hide the spinner.</summary>
	private void VerifyColLegE(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, string? obj,
		long scrollsBefore, int scrolledBefore, bool expectScroll = true)
	{
		if (_qtColRefresh is null || obj is null)
		{
			FinishQtCollectionDiagnostics(renderer, dispatcher, scrollsBefore, scrolledBefore, expectScroll);
			return;
		}
		_qtColView!.ScrollTo(0, -1, ScrollToPosition.Start, false);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(600), () =>
		{
			var w = ColPropNum(obj, "width");
			var top = ColPropNum(obj, "mapToItem(null, 0, 0).y");
			var cx = w < 0 ? 200 : w / 2;
			// Press in the header slot (no row MouseArea) or the row delegate steals the press; drag well past the refresh threshold.
			var cy = (top < 0 ? 100 : top) + 30;
			Console.Error.WriteLine($"[Sailfish] Qt collection diag: leg E pull-to-refresh — injecting a downward drag at {cx:F0},{cy:F0}");
			QtHost.QtHostRuntime.InjectPointer(0, cx, cy);
			for (var step = 1; step <= 8; step++)
			{
				var dy = step * 60.0;
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(40 * step),
					() => QtHost.QtHostRuntime.InjectPointer(2, cx, cy + dy));
			}
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(400),
				() => QtHost.QtHostRuntime.InjectPointer(1, cx, cy + 480.0));
		});
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1500), () =>
		{
			// QML bool reads back as "true"/"false" text, not a number.
			var refreshing = ColPropStr(obj, "mauiRefreshing");
			_qtColChecks.Check($"leg E pull-to-refresh: a released top overscroll set IsRefreshing (Refreshing fires {_qtColRefreshes}>=1) and the spinner push reached the list (mauiRefreshing {refreshing}==true)",
				_qtColRefreshes >= 1 && refreshing == "true");
			var rc = QtHost.QtHostRuntime.GrabPng("/tmp/q14-collection-e.png");
			Console.Error.WriteLine($"[Sailfish] Qt collection diag: leg E screenshot rc={rc} -> /tmp/q14-collection-e.png");
			// Clear while the return bounce is still in flight (as a fast cached refresh would); the adapter's episode latch must keep the
			// bounce's late movementEnded from requesting a second refresh.
			var refreshesAtRelease = _qtColRefreshes;
			_qtColRefresh!.IsRefreshing = false;
			// The false push hops MAUI→Qt thread; give the round trip room.
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200), () =>
			{
				var after = ColPropStr(obj, "mauiRefreshing");
				_qtColChecks.Check($"leg E spinner release: IsRefreshing=false cleared the native spinner (mauiRefreshing {after}==false)", after == "false");
				_qtColChecks.Check($"leg E one decision per gesture: the return bounce re-armed no second refresh-requested (Refreshing fires stayed {_qtColRefreshes}=={refreshesAtRelease} — a duplicate would re-run the command and re-show the spinner)",
					_qtColRefreshes == refreshesAtRelease);
				FinishQtCollectionDiagnostics(renderer, dispatcher, scrollsBefore, scrolledBefore, expectScroll);
			});
		});
	}

	private void FinishQtCollectionDiagnostics(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher,
		long scrollsBefore, int scrolledBefore, bool expectScroll, int settle = 0)
	{
		// At the viewport-fits scale the last delegate can materialize a tick late; poll live == total in a bounded settle window.
		if (!expectScroll && settle < 10 &&
		    renderer.Collection.LiveRows != renderer.Collection.TotalRows)
		{
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(200),
				() => FinishQtCollectionDiagnostics(renderer, dispatcher, scrollsBefore, scrolledBefore, expectScroll, settle + 1));
			return;
		}
		var bridge = renderer.Collection;
		var lastScroll = _qtColLastScroll;
		_qtColChecks.Check(expectScroll
			? $"leg D flick → scroll reporting: list-scroll events consumed +{bridge.ScrollsReported - scrollsBefore}>=1, ItemsView.Scrolled fires +{_qtColScrolled - scrolledBefore}>=1, last report first={lastScroll?.FirstVisibleItemIndex.ToString() ?? "?"} last={lastScroll?.LastVisibleItemIndex.ToString() ?? "?"} (indices past the top — MAUI scroll state follows the native viewport)"
			: "leg D flick → scroll reporting: vacuous at this scale (no scrollable extent) — marked OK by design",
			expectScroll
			? bridge.ScrollsReported > scrollsBefore && _qtColScrolled > scrolledBefore && (lastScroll?.LastVisibleItemIndex ?? 0) > 0
			: true);
		// The threshold must have fired: the leg D scroll lands near the end (at viewport-fits scale every item is visible, also a crossing).
		_qtColChecks.Check($"leg D threshold (README #7): RemainingItemsThresholdReached fired {_qtColThresholds}>=1 (threshold {_qtColView?.RemainingItemsThreshold} of {bridge.TotalRows} items, edge-triggered per crossing)",
			_qtColThresholds >= 1);
		var live = bridge.LiveRows;
		// At viewport-fits scale live == total holds only while nothing recycled; once a row recycles, one delegate is off-screen by design.
		var recycled = bridge.ItemsDestroyed > 0 || bridge.ScrollsReported > scrollsBefore;
		// Delegates live in the viewport plus the prefetch cacheBuffer each way: live < total needs content beyond that.
		var listHost = renderer.CurrentHosts.FirstOrDefault(h => h.QmlUri == "list-view" && h.IsAttached);
		double ListNum(string name) => listHost is null ? double.NaN
			: double.TryParse(QtHost.QtHostRuntime.GetProperty(listHost.NativeHandle, name), System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
		var content = ListNum("contentHeight");
		var window = ListNum("height") + 2 * ListNum("cacheBuffer");
		var beyondBuffer = !(content <= window);   // NaN (no host) keeps the strict rule
		expectScroll &= beyondBuffer;
		if (!beyondBuffer)
			Console.Error.WriteLine($"[Sailfish] Qt collection diag: content {content:F0}px fits viewport+2×cacheBuffer {window:F0}px — every row is prefetched by design");
		_qtColChecks.Check(expectScroll
			? $"virtualization: live materialized item trees {live} < {bridge.TotalRows} total rows — only visible/cached delegates hold MAUI content (the QML ListView owns recycling; no permanent per-row objects, PLAN Q14 rule)"
			: recycled
				? $"virtualization: recycling observed at the viewport-fits scale (destroyed={bridge.ItemsDestroyed}, scrolls +{bridge.ScrollsReported - scrollsBefore}) — live {live}>0 of {bridge.TotalRows} (live==total only holds while nothing recycles)"
				: $"virtualization: dataset fits the viewport — all {live} rows live == {bridge.TotalRows} total (correct virtualization at this scale; the strict live<total rule applies at scrollable scales)",
			live > 0 && (expectScroll ? live < bridge.TotalRows : (recycled || live == bridge.TotalRows)));
		CheckColVisibleRows(bridge.FirstListObjectName, "final");
		var rc = QtHost.QtHostRuntime.GrabPng("/tmp/q14-collection-d.png");
		Console.Error.WriteLine($"[Sailfish] Qt collection diag: screenshot rc={rc} -> /tmp/q14-collection-d.png");
		Console.Error.WriteLine($"[Sailfish] Qt collection diag: counters: rowsBuilt={bridge.RowsBuilt} materialized={bridge.ItemsMaterialized} destroyed={bridge.ItemsDestroyed} listEvents={bridge.ListEvents} selections={bridge.SelectionsApplied} scrolls={bridge.ScrollsReported}");
		// Idle stability: a settled list must not rebuild rows on its own. A row whose host set looks dead to the resync
		// (e.g. a flattened row root counted as a child) is re-materialized every poll: invisible, but it burns the GUI thread.
		var idleMaterialized = bridge.ItemsMaterialized;
		var idleDestroyed = bridge.ItemsDestroyed;
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1500), () =>
		{
			_qtColChecks.Check($"idle: no row rebuilt while nothing changed (materialized {idleMaterialized}→{bridge.ItemsMaterialized}, destroyed {idleDestroyed}→{bridge.ItemsDestroyed} over 1.5 s of resync polls)",
				bridge.ItemsMaterialized == idleMaterialized && bridge.ItemsDestroyed == idleDestroyed);
			_qtColChecks.Accept("OK — Q14 CollectionView runs on the native virtualized ListView: MAUI owns item content, selection authority and scroll state while QML owns delegates/flicking (PLAN Q14)");
			Console.Error.WriteLine("[Sailfish] Qt diag: collection diag done; auto-shutdown in 20s (compositor screenshot window)");
			dispatcher.DispatchDelayed(TimeSpan.FromSeconds(20), () => QtHost.QtHostRuntime.Shutdown());
		});
	}

	// Input diag: real Qt pointer events follow the event-ownership contract. The startup tap on a Silica button was QML-consumed;
	// here a MAUI gesture target that no native control consumes gets an injected tap and drag, which must fire SendTapped and
	// SendPan(Started/Running/Completed) through the router's pointer capture.

	private void RunQtInputDiagnostics(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, QtHost.QtHostInputRouter router)
	{
		Console.Error.WriteLine($"[Sailfish] Qt input diag: router counters before injection — {router.CounterSummary()}");
		// Focus leg: tapping the native Entry is QML-consumed (TextField takes activeFocus) and focus-changed must set Entry.IsFocused.
		renderer.Render();   // fresh geometry for the focus hit-test
		if (renderer.TryFindFocusTarget(out var focusHost, out var focusBounds) && focusHost is not null)
		{
			_qtInputFocusLeg = true;
			var fx = QtHost.QtHostUnits.ToQtUnits(focusBounds.X + focusBounds.Width / 2);
			var fy = QtHost.QtHostUnits.ToQtUnits(focusBounds.Y + focusBounds.Height / 2);
			Console.Error.WriteLine($"[Sailfish] Qt input diag: focus leg — injecting tap into {focusHost} at {fx:F0},{fy:F0}px (QML consumes; focus-changed must reach MAUI)");
			DiagQml.Tap(fx, fy);
		}
		// The gesture target and InputStatusLabel live on the root page, so pop back, but only after focus-changed drains: an event
		// arriving after teardown is dropped as an unknown host id and the focus leg reads 0.
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(500), () =>
		{
			if (renderer.TryPop())
			{
				Console.Error.WriteLine("[Sailfish] Qt input diag: popped back to the gesture-target page; injecting after settle");
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200), () => InjectQtInputDiagnostics(renderer, dispatcher, router));
				return;
			}
			InjectQtInputDiagnostics(renderer, dispatcher, router);
		});
	}

	private void InjectQtInputDiagnostics(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, QtHost.QtHostInputRouter router)
	{
		renderer.Render();   // fresh layout/geometry for the hit-test
		// Grab + QML dump before injecting: a page returning through a pop must look like its first render.
		QtHost.QtHostRuntime.GrabPng("/tmp/q8-postpop.png");
		Console.Error.WriteLine($"[Sailfish] Qt input diag: post-pop QML dump {QtHost.QtHostRuntime.Eval("pageStack.currentPage.__diagDump()")}");
		if (!renderer.TryFindTapTarget(out var host, out var bounds) || host is null)
		{
			Console.Error.WriteLine("[Sailfish] Qt input diag: FAIL — no MAUI gesture target on the current page");
			Console.Error.WriteLine("[Sailfish] Qt diag: input diag done; auto-shutdown requested");
			QtHost.QtHostRuntime.Shutdown();
			return;
		}
		// Root-space dp → Qt scene px, the same single conversion as geometry.
		var cx = QtHost.QtHostUnits.ToQtUnits(bounds.X + bounds.Width / 2);
		var cy = QtHost.QtHostUnits.ToQtUnits(bounds.Y + bounds.Height / 2);
		Console.Error.WriteLine($"[Sailfish] Qt input diag: gesture target {host} " +
			$"bounds=({bounds.X:F1},{bounds.Y:F1} {bounds.Width:F1}x{bounds.Height:F1})dp center=({cx:F0},{cy:F0})px");

		// Tap: press+release inside the slop → capture → SendTapped.
		DiagQml.Tap(cx, cy);

		// Horizontal drag beyond the slop → SendPan(Started/Running/Completed), plus SendSwiped where declared.
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(300), () =>
		{
			QtHost.QtHostRuntime.InjectPointer(0, cx, cy);
			for (var i = 1; i <= 5; i++)
				QtHost.QtHostRuntime.InjectPointer(2, cx + i * 20.0, cy);
			QtHost.QtHostRuntime.InjectPointer(1, cx + 100.0, cy);
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(600), () => VerifyQtInputDiagnostics(renderer, dispatcher, router));
		});
	}

	private void VerifyQtInputDiagnostics(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, QtHost.QtHostInputRouter router, int settle = 0)
	{
		// The focus transition can land a tick after the verification window; poll in a bounded settle window.
		if (_qtInputFocusLeg && renderer.FocusTransitions == 0 && settle < 10)
		{
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(300),
				() => VerifyQtInputDiagnostics(renderer, dispatcher, router, settle + 1));
			return;
		}
		renderer.Poll();   // push the gesture-handler label updates into QML for the screenshot
		// Post-pop evidence: each host's MAUI bounds vs Qt readback vs applied visibility.
		foreach (var host in renderer.CurrentHosts)
		{
			if (!host.IsAttached)
				continue;
			var maui = host.MauiLogicalBounds;
			var qtOk = QtHost.QtHostRuntime.TryItemGeometry(host.NativeHandle, out var scene);
			var native = qtOk ? QtHost.QtHostUnits.ToLogical(scene) : default;
			Console.Error.WriteLine($"[Sailfish] Qt input diag: post-pop host {host} maui=({maui.X:F1},{maui.Y:F1} {maui.Width:F1}x{maui.Height:F1})dp " +
				$"qt={(qtOk ? $"({native.X:F1},{native.Y:F1} {native.Width:F1}x{native.Height:F1})" : "DEAD")}dp visible={host.AppliedVisible}");
		}
		Console.Error.WriteLine($"[Sailfish] Qt input diag: router counters after injection — {router.CounterSummary()}");
		Console.Error.WriteLine($"[Sailfish] Qt input diag: focus transitions={renderer.FocusTransitions} (leg armed={_qtInputFocusLeg})");
		var focusOk = !_qtInputFocusLeg || renderer.FocusTransitions >= 1;
		var ok = router.TapsFired >= 1 && router.PanUpdatesFired >= 2 && router.NativeConsumed >= 1 && focusOk;
		Console.Error.WriteLine($"[Sailfish] Qt input diag: ACCEPTANCE taps={router.TapsFired}>=1 pans={router.PanUpdatesFired}>=2 qml-consumed={router.NativeConsumed}>=1 focus={renderer.FocusTransitions}{(_qtInputFocusLeg ? ">=1" : "(n/a)")} => " +
			(ok
				? "OK — native Silica button tap consumed by QML (bridge → SendClicked); MAUI gesture target received the tap+pan sequences"
				: "FAIL — pointer events did not reach the expected owners"));
		var grabRc = QtHost.QtHostRuntime.GrabPng("/tmp/q8-input.png");
		Console.Error.WriteLine($"[Sailfish] Qt input diag: screenshot rc={grabRc} -> /tmp/q8-input.png (InputStatusLabel shows taps/pan from the MAUI handlers)");
		// Keep the page up 20 s so a compositor screenshot (sf screenshot) can tell grabWindow-only artifacts from real repaints.
		Console.Error.WriteLine("[Sailfish] Qt diag: input diag done; auto-shutdown in 20s (compositor screenshot window)");
		dispatcher.DispatchDelayed(TimeSpan.FromSeconds(20), () => QtHost.QtHostRuntime.Shutdown());
	}

	private static void ParseRenderedCounters(string payload, out int createdTotal, out int destroyedTotal)
	{
		createdTotal = -1;
		destroyedTotal = -1;
		try
		{
			using var doc = System.Text.Json.JsonDocument.Parse(payload);
			if (doc.RootElement.TryGetProperty("createdTotal", out var c) && c.TryGetInt32(out var ci))
				createdTotal = ci;
			if (doc.RootElement.TryGetProperty("destroyedTotal", out var d) && d.TryGetInt32(out var di))
				destroyedTotal = di;
		}
		catch
		{
			// malformed payload — counters stay -1
		}
	}

	private static double ExtractRenderedNumber(string payload, string propertyName)
	{
		try
		{
			using var doc = System.Text.Json.JsonDocument.Parse(payload);
			if (doc.RootElement.TryGetProperty(propertyName, out var v) && v.TryGetDouble(out var d))
				return d;
		}
		catch
		{
			// malformed payload
		}
		return double.NaN;
	}

	// --- Performance diagnostics (MAUI_SAILFISH_QT_HOST_PERF_DIAG=1) ---
	// Phases: startup → idle → scroll (200-item list + flicks) → navigation (push/pop with in-place mutation) → summary; each boundary
	// samples sailfish_host_perf_stats plus GC allocations. Checks: no per-frame readback (grabs==0), no tree recreation on a property
	// change, one ops batch per reconcile, native virtualization, idle crossings bounded by the nav-state poll, device-calibrated budgets.

	/// <summary>One sailfish_host_perf_stats report; -1 = missing field.</summary>
	private readonly record struct PerfSnap(
		long UptimeMs, long FirstFrameMs, long Frames, long AvgFrameUs, long MaxFrameUs, long SlowFrames,
		long SyncCount, long AvgSyncUs, long MaxSyncUs,
		long CpuMs, long RssKb, long PeakRssKb,
		long QmlObjects, long QmlItems, string RenderLoop,
		long Registry, long Ticks,
		long Evals, long OpsEvals, long Drains, long PropertySets, long PropsBatches, long PropsApplied,
		long GeometryBatches, long GeometryEntries, long TextMeasures, long FindObjects,
		long Pushes, long Pops, long Grabs, long Injects, long Destroys, long Shutdown, long GeometryReads)
	{
		public static PerfSnap Parse(string json)
		{
			try
			{
				using var doc = System.Text.Json.JsonDocument.Parse(json);
				var r = doc.RootElement;
				long G(string n) => r.TryGetProperty(n, out var v) && v.TryGetInt64(out var l) ? l : -1;
				var loop = r.TryGetProperty("renderLoop", out var rl) ? rl.GetString() ?? "?" : "?";
				return new PerfSnap(G("uptimeMs"), G("firstFrameMs"), G("frames"), G("avgFrameUs"), G("maxFrameUs"),
					G("slowFrames"), G("syncCount"), G("avgSyncUs"), G("maxSyncUs"), G("cpuMs"), G("rssKb"),
					G("peakRssKb"), G("qmlObjects"), G("qmlItems"), loop, G("registry"), G("ticks"),
					G("evals"), G("opsEvals"), G("drains"), G("propertySets"), G("propsBatches"), G("propsApplied"),
					G("geometryBatches"), G("geometryEntries"), G("textMeasures"), G("findObjects"),
					G("pushes"), G("pops"), G("grabs"), G("injects"), G("destroys"), G("shutdown"), G("geometryReads"));
			}
			catch
			{
				return new PerfSnap(-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, "?", -1, -1,
					-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1);
			}
		}
	}

	private static PerfSnap Snap() => PerfSnap.Parse(QtHost.QtHostRuntime.PerfStats());

	/// <summary>QML-side ops census (mauiOpsBatches/mauiOpsApplied), the batched-update witness; eval scope is the ApplicationWindow root.</summary>
	private static (long Batches, long Applied) EvalOpsCensus()
	{
		try
		{
			using var doc = System.Text.Json.JsonDocument.Parse(
				QtHost.QtHostRuntime.Eval("JSON.stringify({b:mauiOpsBatches,a:mauiOpsApplied})"));
			var root = doc.RootElement;
			return (root.GetProperty("b").GetInt64(), root.GetProperty("a").GetInt64());
		}
		catch
		{
			return (-1, -1);
		}
	}

	/// <summary>Phase average from cumulative averages: avg = Σ/(n-1), so the
	/// phase delta is (avg1·(n1-1) − avg0·(n0-1)) / (Δn−1).</summary>
	private static double PhaseAvgUs(long avg0, long n0, long avg1, long n1)
	{
		var d = n1 - n0;
		if (d <= 1)
			return 0;
		var sum0 = avg0 * Math.Max(0, n0 - 1);
		var sum1 = avg1 * Math.Max(0, n1 - 1);
		return (sum1 - sum0) / (double)(d - 1);
	}

	/// <summary>Phase average where the cumulative avg = Σ/n (the sync counter,
	/// unlike frame INTERVALS which number n−1).</summary>
	private static double PhaseAvgUsN(long avg0, long n0, long avg1, long n1)
	{
		var d = n1 - n0;
		if (d <= 0)
			return 0;
		return (avg1 * Math.Max(0, n1) - avg0 * Math.Max(0, n0)) / (double)d;
	}


	/// <summary>
	/// Error leg: injects deterministic native errors, prints the QT ERROR DIAG report and requests teardown. Qt thread only;
	/// the 400 ms settle lets the startup hosts attach before the live-handle legs.
	/// </summary>
	private void RunQtErrorDiagnostics(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(400), () => RunQtErrorLegs(renderer));
	}

	private void RunQtErrorLegs(QtHost.QtHostPageRenderer renderer)
	{
		var baseErrors = QtHost.QtHostDiag.TotalErrors;
		var injected = 0;

		// Leg A, dead handle: SFHOST_E_DEAD_HANDLE (-3) and last_error names the handle.
		const long deadHandle = 987654321L;
		var rc = QtHost.QtHostRuntime.SetProperty(deadHandle, "text", "\"diag\"");
		var err = QtHost.QtHostRuntime.LastErrorText;
		QtHost.QtHostDiag.Error(QtHost.QtHostDiagChannel.QmlObject,
			$"QT ERROR DIAG leg A: injected dead-handle set_property rc={rc}: {err}");
		injected++;
		_qtErrorChecks.Check($"leg A dead handle: set_property rc={rc} == SFHOST_E_DEAD_HANDLE (-3), last_error names the handle ('{err}')",
			rc == QtHost.QtHostRuntime.SfhostEDeadHandle && err.Contains("987654321", StringComparison.Ordinal));

		// Leg B, bad args: SFHOST_E_ARGS (-1) and last_error names the API.
		rc = QtHost.QtHostRuntime.SetProperty(deadHandle, string.Empty, "\"diag\"");
		err = QtHost.QtHostRuntime.LastErrorText;
		QtHost.QtHostDiag.Error(QtHost.QtHostDiagChannel.QmlProperty,
			$"QT ERROR DIAG leg B: injected empty-name set_property rc={rc}: {err}");
		injected++;
		_qtErrorChecks.Check($"leg B invalid args: set_property(name='') rc={rc} == SFHOST_E_ARGS (-1), last_error names the API ('{err}')",
			rc == QtHost.QtHostRuntime.SfhostEArgs && err.Contains("sailfish_host_set_property", StringComparison.Ordinal));

		// Leg C, live handle: unknown property → SFHOST_E_PROPERTY (-2); malformed batch JSON → SFHOST_E_ARGS (-1).
		var live = renderer.CurrentHosts.FirstOrDefault(h => h.IsAttached);
		if (live is null)
		{
			_qtErrorChecks.Check("leg C live handle: no attached host found — property legs skipped", false);
		}
		else
		{
			rc = QtHost.QtHostRuntime.SetProperty(live.NativeHandle, "__qt_q19_no_such_property__", "1");
			err = QtHost.QtHostRuntime.LastErrorText;
			QtHost.QtHostDiag.Error(QtHost.QtHostDiagChannel.QmlProperty,
				$"QT ERROR DIAG leg C1: injected unknown-property set rc={rc} on {live}: {err}");
			injected++;
			_qtErrorChecks.Check($"leg C1 unknown property: set rc={rc} == SFHOST_E_PROPERTY (-2), last_error ('{err}')",
				rc == QtHost.QtHostRuntime.SfhostEProperty && err.Length > 0);

			rc = QtHost.QtHostRuntime.ApplyProperties(live.NativeHandle, "{not json");
			err = QtHost.QtHostRuntime.LastErrorText;
			QtHost.QtHostDiag.Error(QtHost.QtHostDiagChannel.QmlProperty,
				$"QT ERROR DIAG leg C2: injected malformed apply_props JSON rc={rc} on {live}: {err}");
			injected++;
			_qtErrorChecks.Check($"leg C2 malformed JSON: apply_props rc={rc} == SFHOST_E_ARGS (-1), last_error ('{err}')",
				rc == QtHost.QtHostRuntime.SfhostEArgs && err.Length > 0);
		}

		// Leg D: every channel must be observed; the touch pass fills unused ones, observedBefore shows the run's real spread.
		var observedBefore = QtHost.QtHostDiag.ObservedChannels;
		foreach (QtHost.QtHostDiagChannel ch in Enum.GetValues<QtHost.QtHostDiagChannel>())
			QtHost.QtHostDiag.Trace(ch, $"QT ERROR DIAG channel touch: {ch}");
		var observed = QtHost.QtHostDiag.ObservedChannels;
		_qtErrorChecks.Check($"leg D channels: observed {observed}/11 after the touch pass ({observedBefore}/11 from the run itself)",
			observed == 11);

		// Leg E: no unexpected errors; all channel errors come from the injected legs A–C.
		var totalErrors = QtHost.QtHostDiag.TotalErrors;
		_qtErrorChecks.Check($"leg E error budget: total channel errors {totalErrors} == baseline {baseErrors} + injected {injected} (no unexpected errors)",
			totalErrors == baseErrors + injected);

		var failed = _qtErrorChecks.Failed;
		Console.Error.WriteLine($"[Sailfish] QT ERROR DIAG: summary={QtHost.QtHostDiag.Summary()}");
		Console.Error.WriteLine($"[Sailfish] QT ERROR DIAG: ACCEPTANCE checks={_qtErrorChecks.Count} failed={failed} => " +
			$"{(failed == 0 ? "PASS" : "FAIL")}");
		QtHost.QtHostDiag.Trace(QtHost.QtHostDiagChannel.Lifecycle, "error diag done; auto-shutdown requested");
		QtHost.QtHostRuntime.Shutdown();
	}


	/// <summary>The app's root NavigationPage, which every navigating leg drives.</summary>
	private NavigationPage? RootNav =>
		(_context.Window as Microsoft.Maui.Controls.Window)?.Page as NavigationPage
		?? _context.Window?.Content as NavigationPage;

	/// <summary>Legs that push their own pages and run as separate diag cycles, in dispatch order.</summary>
	private (bool Enabled, Action<QtHost.QtHostPageRenderer, SailfishDispatcher, string> Run)[] PageOwningLegs() =>
	[
		(_qtPageDiag, (r, d, title) => { _qtPageReturnTitle = title; RunQtPageDiagnostics(r, d); }),
		(_qtControlsDiag, (r, d, _) => RunQtControlsDiagnostics(r, d)),
		(_qtNavDiag, (r, d, _) => RunQtNavDiagnostics(r, d)),
		(_qtPopupDiag, (r, d, _) => RunQtPopupDiagnostics(r, d)),
		(_qtCollectionDiag, (r, d, _) => RunQtCollectionDiagnostics(r, d)),
		(_qtShapesDiag, (r, d, title) => { _qtShapesReturnTitle = title; RunQtShapesDiagnostics(r, d); }),
		(_qtVisualDiag, (r, d, title) => { _qtVisualReturnTitle = title; RunQtVisualDiagnostics(r, d); }),
		(_qtStressDiag, (r, d, _) => RunQtStressDiagnostics(r, d)),
		(_qtPerfDiag, (r, d, _) => RunQtPerfDiagnostics(r, d)),
		(_qtErrorDiag, (r, d, _) => RunQtErrorDiagnostics(r, d)),
		(_qtTreeDiag, (r, d, _) => RunQtTreeDiagnostics(r, d)),
		(_qtShellDiag, (r, d, _) => RunQtShellDiagnostics(r, d)),
		(_qtContainersDiag, (r, d, _) => RunQtContainersDiagnostics(r, d)),
		(_qtPulleyDiag, (r, d, _) => RunQtPulleyDiagnostics(r, d)),
		(_qtTabPulleyDiag, (r, d, _) => RunQtTabPulleyDiagnostics(r, d)),
		(_qtSilicaDiag, (r, d, _) => RunQtSilicaDiagnostics(r, d)),
		(_qtApiDemo is not null, (r, d, _) => RunQtApiDemo(r, d)),
		(_qtF3Diag, (r, d, _) => RunQtF3Diagnostics(r, d)),
		(_qtF4Diag, (r, d, _) => RunQtF4Diagnostics(r, d)),
		(_qtFeaturesDiag, (r, d, _) => RunQtFeaturesDiagnostics(r, d)),
		(_qtNavBackDiag, (r, d, _) => RunQtNavBackDiagnostics(r, d)),
		(_qtAdapterBenchDiag, (r, d, _) => RunQtAdapterBenchDiagnostics(r, d)),
	];

	private void RunQtPerfDiagnostics(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var nav = RootNav;
		if (nav is null)
		{
			Console.Error.WriteLine("[Sailfish] Qt perf diag: no NavigationPage — phases skipped; auto-shutdown");
			QtHost.QtHostRuntime.Shutdown();
			return;
		}
		// The startup page reports 'rendered' before its collection hosts materialize; settle before baselining.
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(400),
			() => CapturePerfBaseline(renderer, dispatcher, nav, 0, -1));
	}

	private void CapturePerfBaseline(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher,
		NavigationPage nav, int attempt, long prevLive)
	{
		var qml0 = ParseStressQml(EvalStressQmlCounts());
		if (attempt < 4 && qml0.Live != prevLive)
		{
			Console.Error.WriteLine($"[Sailfish] Qt perf diag: baseline settling (live hosts {prevLive}→{qml0.Live}, re-read in 500ms)");
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(500),
				() => CapturePerfBaseline(renderer, dispatcher, nav, attempt + 1, qml0.Live));
			return;
		}
		_qtPerfBaseDepth = renderer.NativePageIds.Count;
		_qtPerfBaseLive = qml0.Live;
		_perfS0 = Snap();
		_perfA0 = GC.GetTotalAllocatedBytes(true);
		// Idle-churn attribution baselines.
		_perfLayoutPasses0 = renderer.LayoutPasses;
		_perfDirtyProp0 = renderer.LayoutRequests;
		_perfDirtyWb0 = renderer.LayoutDirtyFromWriteback;
		_perfGeoReads0 = _perfS0.GeometryReads;
		_perfRowsBuilt0 = renderer.Collection.RowsBuilt;
		var loopOk = _perfS0.RenderLoop is "QSGThreadedRenderLoop" or "QSGWindowsRenderLoop";
		Console.Error.WriteLine($"[Sailfish] QT PERF startup: firstFrameMs={_perfS0.FirstFrameMs} frames={_perfS0.Frames} renderLoop={_perfS0.RenderLoop} " +
			$"uptimeMs={_perfS0.UptimeMs} cpuMs={_perfS0.CpuMs} rssKb={_perfS0.RssKb} qmlObjects={_perfS0.QmlObjects} qmlItems={_perfS0.QmlItems} | " +
			$"evals={_perfS0.Evals} opsEvals={_perfS0.OpsEvals} propsBatches={_perfS0.PropsBatches} geometryBatches={_perfS0.GeometryBatches} " +
			$"textMeasures={_perfS0.TextMeasures} findObjects={_perfS0.FindObjects} grabs={_perfS0.Grabs} | " +
			$"settled after {attempt} re-reads; live hosts {qml0.Live}, model pages {qml0.Pages} (created/destroyed {qml0.Created}/{qml0.Destroyed})");
		_qtPerfChecks.Check($"startup: first frame {_perfS0.FirstFrameMs}ms after event-loop start (>0, ≤ uptime {_perfS0.UptimeMs}ms), {_perfS0.Frames} frames presented, scene-graph render loop identified as '{_perfS0.RenderLoop}'",
			_perfS0.FirstFrameMs > 0 && _perfS0.FirstFrameMs <= _perfS0.UptimeMs && _perfS0.Frames > 0 && loopOk);
		_qtPerfChecks.Check($"startup crossings bounded: evals {_perfS0.Evals}≤600, opsEvals {_perfS0.OpsEvals}, propsBatches {_perfS0.PropsBatches}, geometryBatches {_perfS0.GeometryBatches}, textMeasures {_perfS0.TextMeasures}, grabs {_perfS0.Grabs}==0 (no RGBA readback on the startup path)",
			_perfS0.Grabs == 0 && _perfS0.Evals >= 0 && _perfS0.Evals <= 600);
		Console.Error.WriteLine("[Sailfish] Qt perf diag: phase IDLE — 6 s warm-up drain (Q14 resync tail ≤25 polls + JIT tiering; logged, NOT asserted) + 3 s steady-state window (asserted)");
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(6000), () => MidPerfIdle(renderer, dispatcher, nav));
	}

	/// <summary>End of the idle warm-up. The first seconds carry the collection resync tail plus JIT tiering/GC, so the idle
	/// measurement window starts here: log the warm-up and rebaseline S0/A0 on the steady state.</summary>
	private void MidPerfIdle(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, NavigationPage nav)
	{
		var sWarm = Snap();
		var aWarm = GC.GetTotalAllocatedBytes(true);
		var warmMs = Math.Max(1, sWarm.UptimeMs - _perfS0.UptimeMs);
		Console.Error.WriteLine($"[Sailfish] QT PERF idle-warmup({warmMs}ms, not asserted): frames +{sWarm.Frames - _perfS0.Frames} " +
			$"cpuPct {100.0 * (sWarm.CpuMs - _perfS0.CpuMs) / warmMs:F1}% evals +{sWarm.Evals - _perfS0.Evals} textMeasures +{sWarm.TextMeasures - _perfS0.TextMeasures} " +
			$"geometryBatches +{sWarm.GeometryBatches - _perfS0.GeometryBatches} layoutPasses +{renderer.LayoutPasses - _perfLayoutPasses0} " +
			$"layoutRequests +{renderer.LayoutRequests - _perfDirtyProp0} allocKBps {(aWarm - _perfA0) / 1024.0 / (warmMs / 1000.0):F0}");
		// Rebaseline on the steady-state window (startup checks are already recorded).
		_perfS0 = sWarm;
		_perfA0 = aWarm;
		_perfLayoutPasses0 = renderer.LayoutPasses;
		_perfDirtyProp0 = renderer.LayoutRequests;
		_perfDirtyWb0 = renderer.LayoutDirtyFromWriteback;
		_perfGeoReads0 = sWarm.GeometryReads;
		_perfRowsBuilt0 = renderer.Collection.RowsBuilt;
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(3000), () => VerifyPerfIdle(renderer, dispatcher, nav));
	}

	private void VerifyPerfIdle(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, NavigationPage nav)
	{
		_perfS1 = Snap();
		_perfA1 = GC.GetTotalAllocatedBytes(true);
		var spanMs = Math.Max(1, _perfS1.UptimeMs - _perfS0.UptimeMs);
		var dFrames = _perfS1.Frames - _perfS0.Frames;
		var dCpu = _perfS1.CpuMs - _perfS0.CpuMs;
		var cpuPct = 100.0 * dCpu / spanMs;
		var allocKbPs = (_perfA1 - _perfA0) / 1024.0 / (spanMs / 1000.0);
		var dEvals = _perfS1.Evals - _perfS0.Evals;
		var dOps = _perfS1.OpsEvals - _perfS0.OpsEvals;
		var dSets = _perfS1.PropertySets - _perfS0.PropertySets;
		var dProps = _perfS1.PropsBatches - _perfS0.PropsBatches;
		var dGeo = _perfS1.GeometryBatches - _perfS0.GeometryBatches;
		var dText = _perfS1.TextMeasures - _perfS0.TextMeasures;
		var dGrabs = _perfS1.Grabs - _perfS0.Grabs;
		var dDrains = _perfS1.Drains - _perfS0.Drains;
		var dPasses = renderer.LayoutPasses - _perfLayoutPasses0;
		var dDirtyProp = renderer.LayoutRequests - _perfDirtyProp0;
		var dDirtyWb = renderer.LayoutDirtyFromWriteback - _perfDirtyWb0;
		var dGeoReads = _perfS1.GeometryReads - _perfGeoReads0;
		var dRows = renderer.Collection.RowsBuilt - _perfRowsBuilt0;
		Console.Error.WriteLine($"[Sailfish] QT PERF idle({spanMs}ms): frames +{dFrames} cpuPct {cpuPct:F1}% allocKBps {allocKbPs:F0} rssKb {_perfS1.RssKb} | " +
			$"crossings evals +{dEvals} opsEvals +{dOps} propertySets +{dSets} propsBatches +{dProps} geometryBatches +{dGeo} textMeasures +{dText} grabs +{dGrabs} drains +{dDrains} geometryReads +{dGeoReads} | " +
			$"reconcile avg {renderer.AvgReconcileMs:F2}ms ×{renderer.ReconcileCount} last {renderer.LastReconcileMs:F2}ms | " +
			$"churn: layoutPasses +{dPasses} layoutRequests +{dDirtyProp} dirtyFromWriteback +{dDirtyWb} rowsBuilt +{dRows}");
		_qtPerfChecks.Check($"idle boundary crossings bounded: evals +{dEvals}≤16 (the 4 Hz nav-state poll only), opsEvals +{dOps}==0, propertySets +{dSets}==0, propsBatches +{dProps}==0, geometryBatches +{dGeo}==0, textMeasures +{dText}==0, grabs +{dGrabs}==0 — no per-frame managed→Qt traffic at rest",
			dEvals is >= 0 and <= 16 && dOps == 0 && dSets == 0 && dProps == 0 && dGeo == 0 && dText == 0 && dGrabs == 0);
		_qtPerfChecks.Check($"idle on-demand rendering: frames +{dFrames} in {spanMs}ms — Qt Quick presents only changed frames (threshold calibrated on-device)",
			dFrames >= 0 && dFrames <= 90);   // calibrated: +0 measured (on-demand rendering)
		_qtPerfChecks.Check($"idle CPU: {cpuPct:F1}% of one core ≤ 13% (event-driven loop — ticks only when woken — + 4 Hz reconcile ~1–3 ms + .NET GC ~230 KB/s, TieredPGO off; 0 frames — render thread idle)",
			cpuPct is >= 0 and <= 13.0);      // calibrated: ~11% measured on device
		_qtPerfChecks.Check($"idle managed allocations: {allocKbPs:F0} KB/s ≤ 768 KB/s (the 250 ms diff-only reconcile walk + dispatcher)",
			allocKbPs is >= 0 and <= 768);    // calibrated: ~150 KB/s steady state
		StartPerfScroll(renderer, dispatcher, nav);
	}

	private void StartPerfScroll(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, NavigationPage nav)
	{
		var items = new System.Collections.ObjectModel.ObservableCollection<string>();
		for (var i = 0; i < 200; i++)
			items.Add($"Q18 perf row {i:D3} — the quick brown fox jumps over the lazy dog");
		var list = new CollectionView
		{
			ItemsSource = items,
			SelectionMode = SelectionMode.None,
			ItemTemplate = new DataTemplate(() =>
			{
				var label = SelfTextLabel();
				label.FontSize = 18;
				label.TextColor = Colors.White;
				label.Margin = new Thickness(16, 10);
				return label;
			}),
		};
		_qtPerfColPage = new ContentPage
		{
			Title = "Q18 Perf Scroll",
			BackgroundColor = Color.FromArgb("#101820"),
			Content = list,
		};
		Console.Error.WriteLine("[Sailfish] Qt perf diag: phase SCROLL — pushing the 200-item collection page, then 4 injected flicks");
		_perfPushesBefore = renderer.NativePushes;
		_ = nav.PushAsync(_qtPerfColPage);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(2500),
			() => VerifyPerfScrollSettle(renderer, dispatcher, nav));
	}

	private void VerifyPerfScrollSettle(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, NavigationPage nav)
	{
		_perfS2 = Snap();
		_perfA2 = GC.GetTotalAllocatedBytes(true);
		var qml2 = ParseStressQml(EvalStressQmlCounts());
		// Model pages are pushed via Eval(pageStack.push…), so the shim's push_page counter stays 0; NativePushes is the witness.
		var dPushes = renderer.NativePushes - _perfPushesBefore;
		var dOps = _perfS2.OpsEvals - _perfS1.OpsEvals;
		var dBuildFrames = _perfS2.Frames - _perfS1.Frames;
		var buildAllocKb = (_perfA2 - _perfA1) / 1024.0;
		var buildMs = renderer.LastNavToAppearingMs;
		Console.Error.WriteLine($"[Sailfish] QT PERF collection build: navToAppearing {buildMs:F0}ms frames +{dBuildFrames} alloc {buildAllocKb:F0}KB | " +
			$"liveHosts {qml2.Live} (200 items) qmlObjects {_perfS2.QmlObjects} qmlItems {_perfS2.QmlItems} pages {qml2.Pages} | " +
			$"pushes +{dPushes} opsEvals +{dOps} textMeasures +{_perfS2.TextMeasures - _perfS1.TextMeasures} grabs +{_perfS2.Grabs - _perfS1.Grabs}");
		_qtPerfChecks.Check($"native virtualization: a 200-item CollectionView materializes {qml2.Live} live MAUI hosts (<120 — the ListView viewport+cacheBuffer window, not the full dataset) across {qml2.Pages}=={_qtPerfBaseDepth + 1} model pages; QML items total {_perfS2.QmlItems}",
			qml2.Live > 0 && qml2.Live < 120 && qml2.Pages == _qtPerfBaseDepth + 1);
		_qtPerfChecks.Check($"collection build batched: pushes +{dPushes}==1, opsEvals +{dOps}≥1 (one applyMauiOps batch per reconcile change-set), grabs +{_perfS2.Grabs - _perfS1.Grabs}==0",
			dPushes == 1 && dOps >= 1 && _perfS2.Grabs - _perfS1.Grabs == 0);
		// Flick geometry from the live page (scene units == window px).
		var w = DiagQml.EvalNum("pageStack.currentPage.width");
		var h = DiagQml.EvalNum("pageStack.currentPage.height");
		if (double.IsNaN(w) || w < 100) w = 1080;
		if (double.IsNaN(h) || h < 100) h = 2160;
		_perfScrollWrites0 = renderer.ScrollWriteBacks;
		// Scroll witness: the collection page scrolls its own ListView, so page-level ScrollWriteBacks stay 0.
		_perfListObj = renderer.Collection.FirstListObjectName;
		_perfListY0 = ColPropNum(_perfListObj ?? "-", "contentY");
		_perfListEvents0 = renderer.Collection.ListEvents;
		RunPerfFlicks(renderer, dispatcher, nav, 4, w / 2, h * 0.72);
	}

	/// <summary>Injects one upward flick through the normal Qt event path and chains the next; verifies the phase after the last one settles.</summary>
	private void RunPerfFlicks(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher,
		NavigationPage nav, int remaining, double cx, double y0)
	{
		if (remaining <= 0)
		{
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1800),
				() => VerifyPerfScroll(renderer, dispatcher, nav));
			return;
		}
		Console.Error.WriteLine($"[Sailfish] Qt perf diag: injecting flick {5 - remaining}/4 at ({cx:F0},{y0:F0}) dy=-420");
		QtHost.QtHostRuntime.InjectPointer(0, cx, y0);
		for (var i = 1; i <= 4; i++)
		{
			var step = i;
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(45 * i),
				() => QtHost.QtHostRuntime.InjectPointer(2, cx, y0 - 105.0 * step));
		}
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(240), () =>
		{
			QtHost.QtHostRuntime.InjectPointer(1, cx, y0 - 420.0);
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(650),
				() => RunPerfFlicks(renderer, dispatcher, nav, remaining - 1, cx, y0));
		});
	}

	private void VerifyPerfScroll(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, NavigationPage nav)
	{
		_perfS3 = Snap();
		_perfA3 = GC.GetTotalAllocatedBytes(true);
		var dFrames = _perfS3.Frames - _perfS2.Frames;
		var avgUs = PhaseAvgUs(_perfS2.AvgFrameUs, _perfS2.Frames, _perfS3.AvgFrameUs, _perfS3.Frames);
		var dSlow = _perfS3.SlowFrames - _perfS2.SlowFrames;
		var dSync = _perfS3.SyncCount - _perfS2.SyncCount;
		var syncAvgUs = PhaseAvgUsN(_perfS2.AvgSyncUs, _perfS2.SyncCount, _perfS3.AvgSyncUs, _perfS3.SyncCount);
		var allocPerFrameKb = dFrames > 0 ? (_perfA3 - _perfA2) / 1024.0 / dFrames : 0;
		var dWrites = renderer.ScrollWriteBacks - _perfScrollWrites0;
		var listY1 = ColPropNum(_perfListObj ?? "-", "contentY");
		var dListEvents = renderer.Collection.ListEvents - _perfListEvents0;
		var listMoved = !double.IsNaN(listY1) && !double.IsNaN(_perfListY0) && listY1 - _perfListY0 >= 100;
		var dGrabs = _perfS3.Grabs - _perfS2.Grabs;
		var spanMs = Math.Max(1, _perfS3.UptimeMs - _perfS2.UptimeMs);
		var cpuPct = 100.0 * (_perfS3.CpuMs - _perfS2.CpuMs) / spanMs;
		Console.Error.WriteLine($"[Sailfish] QT PERF scroll({spanMs}ms): frames +{dFrames} avgFrame {avgUs / 1000.0:F1}ms maxFrame(cum) {_perfS3.MaxFrameUs / 1000.0:F1}ms slow +{dSlow} | " +
			$"sync +{dSync} avgSync {syncAvgUs / 1000.0:F2}ms maxSync(cum) {_perfS3.MaxSyncUs / 1000.0:F2}ms | allocPerFrame {allocPerFrameKb:F1}KB cpuPct {cpuPct:F1}% rssKb {_perfS3.RssKb} | " +
			$"scrollWriteBacks +{dWrites} grabs +{dGrabs} evals +{_perfS3.Evals - _perfS2.Evals}");
		_qtPerfChecks.Check($"scroll produced frames: +{dFrames} frames over {spanMs}ms (4 flicks + settle; ≥40 — the flick animations drove the scene graph)",
			dFrames >= 40);
		_qtPerfChecks.Check($"scroll frame time: phase avg {avgUs / 1000.0:F1}ms ≤ 33.0ms and slow frames (>32ms) +{dSlow} ≤ 30% of +{dFrames}",
			avgUs is > 0 and <= 33000 && dSlow * 10 <= dFrames * 3);   // calibrated: ~19 ms avg, ~2% slow
		_qtPerfChecks.Check($"scene-graph sync phase: +{dSync} syncs, avg {syncAvgUs / 1000.0:F2}ms ≤ 8.0ms — the MAUI↔Qt boundary does not dominate the frame budget",
			dSync > 0 && syncAvgUs <= 8000);   // calibrated: ~0.13 ms avg sync
		_qtPerfChecks.Check($"scroll witness: native ListView '{_perfListObj}' contentY {_perfListY0:F0}→{listY1:F0} (moved ≥100px: {listMoved}) and list events +{dListEvents}≥1 — the injected flicks scrolled the virtualized list natively (page ScrollWriteBacks +{dWrites} for reference)",
			listMoved && dListEvents >= 1);
		_qtPerfChecks.Check($"no per-frame readback: grabs +{dGrabs}==0 across the whole scroll phase — zero Qt→CPU RGBA copies; Qt Quick renders straight to Wayland/GPU",
			dGrabs == 0);
		_qtPerfChecks.Check($"scroll allocations: {allocPerFrameKb:F1} KB/frame ≤ 64 KB/frame managed (native virtualization keeps row churn off the GC)",
			allocPerFrameKb <= 64);   // calibrated: ~6 KB/frame
		Console.Error.WriteLine("[Sailfish] Qt perf diag: popping the collection page → phase NAVIGATION (3× push/pop with in-place property updates)");
		_ = nav.PopAsync();
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200), () => StartPerfNav(renderer, dispatcher, nav));
	}

	private void StartPerfNav(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, NavigationPage nav)
	{
		_perfNavA0 = GC.GetTotalAllocatedBytes(true);
		_qtPerfNavPushMs.Clear();
		_qtPerfNavLegs.Clear();
		PerfNavLeg(renderer, dispatcher, nav, 0);
	}

	/// <summary>One nav leg: push a text page, mutate a Label in place (typed property batch, no tree ops), pop, next leg.</summary>
	private void PerfNavLeg(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, NavigationPage nav, int leg)
	{
		if (leg >= 3)
		{
			FinishPerfNav(renderer, dispatcher, nav);
			return;
		}
		var mutate = new Label { Text = $"Q18 nav leg {leg} — original text", FontSize = 20, TextColor = Colors.White };
		_qtPerfMutateLabel = mutate;
		var page = new ContentPage
		{
			Title = $"Q18 Perf Nav {leg}",
			BackgroundColor = Color.FromArgb("#101820"),
			Content = new VerticalStackLayout
			{
				Padding = new Thickness(24),
				Spacing = 12,
				Children =
				{
					new Label { Text = $"Q18 Perf Nav {leg}", FontSize = 28, FontAttributes = FontAttributes.Bold, TextColor = Colors.White },
					mutate,
					new Label { Text = "The mutation below must reach QML IN PLACE (typed apply_props batch): no ops eval, no host recreation.", TextColor = Colors.LightGray },
				},
			},
		};
		var censusLeg = ParseStressQml(EvalStressQmlCounts());
		_ = nav.PushAsync(page);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1100), () =>
		{
			var sPush = Snap();
			var censusPush = ParseStressQml(EvalStressQmlCounts());
			var opsPush = EvalOpsCensus();
			_qtPerfNavPushMs.Add(renderer.LastNavToAppearingMs);
			// The immediate push path (whitelisted visual change → typed batch) must carry this with zero tree ops.
			mutate.Text = $"Q18 nav leg {leg} — MUTATED in place @ {DateTime.Now:HH:mm:ss.fff}";
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(700), () =>
			{
				var sMut = Snap();
				var censusMut = ParseStressQml(EvalStressQmlCounts());
				var opsMut = EvalOpsCensus();
				_qtPerfNavLegs.Add((
					sMut.OpsEvals - sPush.OpsEvals,
					censusMut.Created - censusPush.Created,
					sMut.PropsBatches - sPush.PropsBatches,
					sMut.PropertySets - sPush.PropertySets,
					opsMut.Batches - opsPush.Batches));
				Console.Error.WriteLine($"[Sailfish] QT PERF nav leg {leg}: push→appearing {renderer.LastNavToAppearingMs:F0}ms; " +
					$"mutation: opsEvals +{sMut.OpsEvals - sPush.OpsEvals} created +{censusMut.Created - censusPush.Created} " +
					$"opsBatches +{opsMut.Batches - opsPush.Batches} propsBatches +{sMut.PropsBatches - sPush.PropsBatches} propertySets +{sMut.PropertySets - sPush.PropertySets}; " +
					$"build: textMeasures +{sPush.TextMeasures - (leg == 0 ? _perfS3.TextMeasures : _qtPerfNavLegText0)} census {censusLeg.Live}→{censusPush.Live}");
				_qtPerfNavLegText0 = sMut.TextMeasures;
				_ = nav.PopAsync();
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1100),
					() => PerfNavLeg(renderer, dispatcher, nav, leg + 1));
			});
		});
	}

	private long _qtPerfNavLegText0;   // textMeasures at the previous leg boundary (build-cost logging)

	private void FinishPerfNav(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, NavigationPage nav)
	{
		var sF = Snap();
		var censusF = ParseStressQml(EvalStressQmlCounts());
		var allocNavKb = (GC.GetTotalAllocatedBytes(true) - _perfNavA0) / 1024.0;
		var dOpsEvals = _qtPerfNavLegs.Sum(t => t.OpsEvals);
		var dCreated = _qtPerfNavLegs.Sum(t => t.Created);
		var dProps = _qtPerfNavLegs.Sum(t => t.PropsBatches + t.PropertySets);
		var dOpsBatches = _qtPerfNavLegs.Sum(t => t.OpsBatches);
		var pushAvg = _qtPerfNavPushMs.Count > 0 ? _qtPerfNavPushMs.Average() : -1;
		Console.Error.WriteLine($"[Sailfish] QT PERF navigation: 3 legs; push→appearing [{string.Join(", ", _qtPerfNavPushMs.Select(ms => $"{ms:F0}ms"))}] avg {pushAvg:F0}ms | " +
			$"renderer NavTimings {renderer.NavTimings} avg {renderer.AvgNavToAppearingMs:F0}ms | in-place mutations: opsEvals +{dOpsEvals} created +{dCreated} opsBatches +{dOpsBatches} propsPushes +{dProps} | " +
			$"alloc {allocNavKb:F0}KB textMeasures→{sF.TextMeasures}");
		_qtPerfChecks.Check($"navigation timing: push→SendAppearing avg {pushAvg:F0}ms ≤ 1200ms over {_qtPerfNavPushMs.Count}==3 pushes (hosts created + geometry applied + appearing delivered; renderer measured {renderer.NavTimings} transitions)",
			_qtPerfNavPushMs.Count == 3 && pushAvg > 0 && pushAvg <= 1200);   // calibrated: ~32 ms avg
		_qtPerfChecks.Check($"no full-tree recreation: the 3 in-place Label.Text mutations produced opsEvals +{dOpsEvals}==0, QML created +{dCreated}==0, opsBatches +{dOpsBatches}==0 — updates rode typed property batches (+{dProps}≥3), the QML tree was untouched",
			dOpsEvals == 0 && dCreated == 0 && dOpsBatches == 0 && dProps >= 3);
		_qtPerfChecks.Check($"text rendering on the Qt path: textMeasures {_perfS0.TextMeasures}→{sF.TextMeasures} (+{sF.TextMeasures - _perfS0.TextMeasures}>0 — QFontMetrics through the shim, one metric for layout AND QML Text) and +{_perfS1.TextMeasures - _perfS0.TextMeasures}==0 during idle",
			sF.TextMeasures - _perfS0.TextMeasures > 0 && _perfS1.TextMeasures - _perfS0.TextMeasures == 0);
		_qtPerfChecks.Check($"census restored: live MAUI hosts {censusF.Live}=={_qtPerfBaseLive}, model pages {censusF.Pages}=={_qtPerfBaseDepth} after scroll+nav phases (no QML leak across the perf cycle)",
			censusF.Live == _qtPerfBaseLive && censusF.Pages == _qtPerfBaseDepth);
		StartPerfTree(renderer, dispatcher, nav);
	}

	// Tree phase (A3 of the architecture plan): the cost of one child added to a layout on a ~150-host page, applied by
	// the layout handler's subtree pass versus the full page reconcile (MAUI_SAILFISH_HANDLER_TREE=0 behaviour).
	private void StartPerfTree(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, NavigationPage nav)
	{
		var target = new VerticalStackLayout { Spacing = 4 };
		var rows = new VerticalStackLayout { Spacing = 2 };
		for (var r = 0; r < 30; r++)
		{
			var row = new HorizontalStackLayout { Spacing = 8 };
			for (var c = 0; c < 4; c++)
				row.Children.Add(new Label { Text = $"r{r}c{c}", TextColor = Colors.White });
			rows.Children.Add(row);
		}
		var page = new ContentPage
		{
			Title = "Q18 Perf Tree",
			BackgroundColor = Color.FromArgb("#101820"),
			Content = new VerticalStackLayout { Padding = new Thickness(24), Children = { target, rows } },
		};
		_ = nav.PushAsync(page);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1100), () => PerfTreeStep(renderer, dispatcher, nav, target,
			new List<double>(), new List<double>(), renderer.TreeFixups, renderer.SubtreeFallbacks, 0));
	}

	private const int PerfTreeSteps = 8;
	private readonly List<double> _perfTreeLayoutMs = new();   // the layout pass inside each subtree pass
	private readonly List<(double WalkDiffMs, double OpsEvalMs, double AttachMs)> _perfTreeSplits = new();
	private readonly List<(double BuildMs, double EvalMs, double NoteMs, int Chars)> _perfTreeOps = new();

	private void PerfTreeStep(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, NavigationPage nav,
	                          VerticalStackLayout target, List<double> subtreeMs, List<double> fullMs,
	                          long fixups0, long fallbacks0, int step)
	{
		if (step < PerfTreeSteps)
		{
			// Handler path: Layout.Add → SailfishStackHandler.Add → the stack's subtree on the next loop turn.
			var passes = renderer.SubtreeReconciles;
			target.Children.Add(new Label { Text = $"added {step}", TextColor = Colors.White });
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(120), () =>
			{
				if (renderer.SubtreeReconciles > passes)
				{
					subtreeMs.Add(renderer.LastSubtreeReconcileMs);
					_perfTreeLayoutMs.Add(renderer.LastSubtreeLayoutMs);
					_perfTreeSplits.Add(renderer.LastSubtreeSplit);
					_perfTreeOps.Add(renderer.LastApplyOpsSplit);
				}
				PerfTreeStep(renderer, dispatcher, nav, target, subtreeMs, fullMs, fixups0, fallbacks0, step + 1);
			});
			return;
		}
		if (step < 2 * PerfTreeSteps)
		{
			// Page path: the same change applied by a full reconcile (walk, diff, ops, layout of the whole page).
			var previous = QtHost.QtHostPageRenderer.HandlerTree;
			QtHost.QtHostPageRenderer.HandlerTree = false;
			try
			{
				target.Children.Add(new Label { Text = $"added {step}", TextColor = Colors.White });
				renderer.Render();
				fullMs.Add(renderer.LastReconcileMs);
			}
			finally
			{
				QtHost.QtHostPageRenderer.HandlerTree = previous;
			}
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(120),
				() => PerfTreeStep(renderer, dispatcher, nav, target, subtreeMs, fullMs, fixups0, fallbacks0, step + 1));
			return;
		}
		renderer.Render();
		var idleFullMs = renderer.LastReconcileMs;   // the walk of an unchanged page, for reference
		var hosts = renderer.CurrentHosts.Count;
		var natives = renderer.CurrentHosts.Count(h => h.Element is Label { Text: { } t } && t.StartsWith("added ", StringComparison.Ordinal) && h.IsAttached);
		static string Stats(List<double> ms) => ms.Count == 0 ? "n=0" :
			$"n={ms.Count} median {ms.OrderBy(x => x).ElementAt(ms.Count / 2):F2}ms avg {ms.Average():F2}ms max {ms.Max():F2}ms";
		static double Med(IEnumerable<double> xs) { var l = xs.OrderBy(x => x).ToList(); return l.Count == 0 ? -1 : l[l.Count / 2]; }
		// What an ops eval costs without creating anything: the eval itself (expression compile + page lookup).
		static double TimeEval(string js)
		{
			var samples = new List<double>();
			for (var k = 0; k < 8; k++)
			{
				var t = System.Diagnostics.Stopwatch.GetTimestamp();
				QtHost.QtHostRuntime.Eval(js);
				samples.Add((System.Diagnostics.Stopwatch.GetTimestamp() - t) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
			}
			return Med(samples);
		}
		var topPage = renderer.NativePageIds.Count > 0 ? renderer.NativePageIds[^1] : "";
		Console.Error.WriteLine($"[Sailfish] QT PERF tree eval cost (medians of 8): Eval(\"1\") {TimeEval("1"):F2}ms | " +
			$"empty ops batch on the top page {TimeEval($"(window.mauiPageById('{topPage}')).applyMauiOps('[]')"):F2}ms | " +
			$"page lookup only {TimeEval($"!!window.mauiPageById('{topPage}')"):F2}ms | " +
			$"pending re-parents {QtHost.QtHostRuntime.Eval($"(function(){{var p=window.mauiPageById('{topPage}');return p&&p.__pendingReparents?p.__pendingReparents.length:0;}})()")} " +
			$"resolve {TimeEval($"(window.mauiPageById('{topPage}')).__resolveReparents()"):F2}ms | " +
			$"one label create+destroy {TimeEval($"(function(){{var p=window.mauiPageById('{topPage}');p.applyMauiOps('[{{\"op\":\"create\",\"id\":\"probe1\",\"uri\":\"label\",\"src\":\"controls/Label.qml\",\"props\":{{\"text\":\"probe\"}}}}]');p.applyMauiOps('[{{\"op\":\"destroy\",\"id\":\"probe1\"}}]');}})()"):F2}ms");
		Console.Error.WriteLine($"[Sailfish] QT PERF tree subtree split (medians): walk+diff {Med(_perfTreeSplits.Select(t => t.WalkDiffMs)):F2}ms " +
			$"ops eval {Med(_perfTreeSplits.Select(t => t.OpsEvalMs)):F2}ms attach {Med(_perfTreeSplits.Select(t => t.AttachMs)):F2}ms layout {Med(_perfTreeLayoutMs):F2}ms");
		// Replay the last real batch with a probe id (then destroy it): does the expression itself cost the eval?
		var lastExpr = renderer.LastOpsExpression;
		var lastId = System.Text.RegularExpressions.Regex.Match(lastExpr, @"id\W+(e\d+)").Groups[1].Value;
		if (lastId.Length > 0)
		{
			var replays = new List<double>();
			for (var k = 0; k < 6; k++)
			{
				var probeId = $"replay{k}";
				var js = lastExpr.Replace(lastId, probeId);
				var t = System.Diagnostics.Stopwatch.GetTimestamp();
				QtHost.QtHostRuntime.Eval(js);
				replays.Add((System.Diagnostics.Stopwatch.GetTimestamp() - t) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
				QtHost.QtHostRuntime.Eval($"(window.mauiPageById('{topPage}')).applyMauiOps('[{{\"op\":\"destroy\",\"id\":\"{probeId}\"}}]')");
			}
			Console.Error.WriteLine($"[Sailfish] QT PERF tree replay of the last real batch ({lastExpr.Length} chars, id {lastId}): eval median {Med(replays):F2}ms [{string.Join(",", replays.Select(r => r.ToString("F1")))}]");
		}
		else
			Console.Error.WriteLine($"[Sailfish] QT PERF tree replay: no host id in the last batch ({lastExpr[..Math.Min(160, lastExpr.Length)]})");
		Console.Error.WriteLine($"[Sailfish] QT PERF tree ops batch split (medians): build {Med(_perfTreeOps.Select(t => t.BuildMs)):F2}ms " +
			$"eval {Med(_perfTreeOps.Select(t => t.EvalMs)):F2}ms note {Med(_perfTreeOps.Select(t => t.NoteMs)):F2}ms chars {Med(_perfTreeOps.Select(t => (double)t.Chars)):F0}");
		var split = renderer.LastLayoutSplit;
		Console.Error.WriteLine($"[Sailfish] QT PERF tree layout split (last pass): measure+arrange {split.MeasureArrangeMs:F2}ms collect {split.CollectMs:F2}ms flush {split.FlushMs:F2}ms | " +
			$"text cache {(QtHost.QtHostTextMetrics.CacheEnabled ? "on" : "off")} hits={QtHost.QtHostTextMetrics.CacheHits} shim measures={Snap().TextMeasures}");
		Console.Error.WriteLine($"[Sailfish] QT PERF tree change on {hosts} hosts: subtree pass {Stats(subtreeMs)} (of which layout {Stats(_perfTreeLayoutMs)}) | full reconcile {Stats(fullMs)} | " +
			$"unchanged-page walk {idleFullMs:F2}ms | fixups +{renderer.TreeFixups - fixups0} fallbacks +{renderer.SubtreeFallbacks - fallbacks0} " +
			$"handlerReleases={renderer.HandlerReleases}");
		_qtPerfChecks.Check($"handler tree: {PerfTreeSteps} Layout.Add went through {subtreeMs.Count}=={PerfTreeSteps} subtree passes, " +
			$"{natives}=={2 * PerfTreeSteps} added labels live natively, fixups +{renderer.TreeFixups - fixups0}==0",
			subtreeMs.Count == PerfTreeSteps && natives == 2 * PerfTreeSteps && renderer.TreeFixups == fixups0);
		if (subtreeMs.Count > 0 && fullMs.Count > 0)
			_qtPerfChecks.Check($"handler tree: subtree pass median {subtreeMs.OrderBy(x => x).ElementAt(subtreeMs.Count / 2):F2}ms < full reconcile median {fullMs.OrderBy(x => x).ElementAt(fullMs.Count / 2):F2}ms for the same change",
				subtreeMs.OrderBy(x => x).ElementAt(subtreeMs.Count / 2) < fullMs.OrderBy(x => x).ElementAt(fullMs.Count / 2));
		// The same replay after an idle gap each time, as the subtree pass runs 120 ms after the previous step: does
		// idle (CPU clock ramp, cold caches) cost the eval, not the batch?
		if (lastId.Length > 0)
		{
			var idleReplays = new List<double>();
			void IdleReplay(int k)
			{
				if (k >= 6)
				{
					Console.Error.WriteLine($"[Sailfish] QT PERF tree replay after a 120 ms idle gap: eval median {Med(idleReplays):F2}ms [{string.Join(",", idleReplays.Select(r => r.ToString("F1")))}]");
					_ = nav.PopAsync();
					dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1100), () => FinishQtPerfDiagnostics(renderer, Snap()));
					return;
				}
				var probeId = $"idle{k}";
				var t = System.Diagnostics.Stopwatch.GetTimestamp();
				QtHost.QtHostRuntime.Eval(lastExpr.Replace(lastId, probeId));
				idleReplays.Add((System.Diagnostics.Stopwatch.GetTimestamp() - t) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
				QtHost.QtHostRuntime.Eval($"(window.mauiPageById('{topPage}')).applyMauiOps('[{{\"op\":\"destroy\",\"id\":\"{probeId}\"}}]')");
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(120), () => IdleReplay(k + 1));
			}
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(120), () => IdleReplay(0));
			return;
		}
		_ = nav.PopAsync();
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1100), () => FinishQtPerfDiagnostics(renderer, Snap()));
	}

	private void FinishQtPerfDiagnostics(QtHost.QtHostPageRenderer renderer, PerfSnap sF)
	{
		var allocTotalKb = (GC.GetTotalAllocatedBytes(true) - _perfA0) / 1024.0;
		var failed = _qtPerfChecks.Failed;
		Console.Error.WriteLine($"[Sailfish] QT PERF SUMMARY: uptimeMs={sF.UptimeMs} firstFrameMs={sF.FirstFrameMs} renderLoop={sF.RenderLoop} | " +
			$"frames={sF.Frames} avgFrameUs={sF.AvgFrameUs} maxFrameUs={sF.MaxFrameUs} slowFrames={sF.SlowFrames} | " +
			$"sync={sF.SyncCount} avgSyncUs={sF.AvgSyncUs} maxSyncUs={sF.MaxSyncUs} | " +
			$"cpuMs={sF.CpuMs} rssKb={sF.RssKb} peakRssKb={sF.PeakRssKb} managedAllocTotalKb={allocTotalKb:F0} | " +
			$"qmlObjects={sF.QmlObjects} qmlItems={sF.QmlItems} registry={sF.Registry} | " +
			$"evals={sF.Evals} opsEvals={sF.OpsEvals} drains={sF.Drains} propertySets={sF.PropertySets} propsBatches={sF.PropsBatches} propsApplied={sF.PropsApplied} " +
			$"geometryBatches={sF.GeometryBatches} geometryEntries={sF.GeometryEntries} textMeasures={sF.TextMeasures} findObjects={sF.FindObjects} " +
			$"pushes={sF.Pushes} pops={sF.Pops} grabs={sF.Grabs} injects={sF.Injects} destroys={sF.Destroys} | " +
			$"reconcile avg {renderer.AvgReconcileMs:F2}ms ×{renderer.ReconcileCount} | nav avg {renderer.AvgNavToAppearingMs:F0}ms ×{renderer.NavTimings}");
		Console.Error.WriteLine($"[Sailfish] QT PERF ARCH COUNTERS: {renderer.ArchitectureCounters}");
		Console.Error.WriteLine($"[Sailfish] QT PERF NAV-IDLE→render avg {renderer.AvgIdleToRenderMs:F0}ms ×{renderer.IdleToRenderCount}");
		_qtPerfChecks.Check($"whole-run readback audit: grabs {sF.Grabs}==0 across the ENTIRE perf cycle — the pipeline never copies Qt frames back to the CPU (no SDL/readback path exists; screenshots are compositor-side only)",
			sF.Grabs == 0);
		Console.Error.WriteLine($"[Sailfish] QT PERF DIAG: {_qtPerfChecks.Count - failed}/{_qtPerfChecks.Count} checks OK — " +
			(failed == 0
				? "OK — Q18 performance rules hold on device (PLAN §19: measurement + rules + scene-graph/sync-boundary optimization)"
				: $"{failed} FAILED — see the CHECK lines above"));
		Console.Error.WriteLine("[Sailfish] Qt perf diag: Shutdown(); the run log must now show the shim's final 'shutdown: perf frames=…' line and a clean 'event loop exited rc=0'");
		QtHost.QtHostRuntime.Shutdown();
	}
}
