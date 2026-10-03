using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

// MAUI navigation stack ⇄ Silica pageStack sync (one model page per MAUI page); the page cache is PageCache.cs.
internal sealed partial class QtHostPageRenderer : INativeStackOwner
{
	private Page? RootPage() =>
		_window.Page ?? (_window as Microsoft.Maui.IWindow)?.Content as Page;

	/// <summary>The root navigation surface as one stack, as the root page's container handler shows it
	/// (<see cref="Handlers.ISailfishPageContainer"/>: NavigationPage, Shell section, TabbedPage child, FlyoutPage
	/// Detail plus the presented Flyout, nested containers expanded). Pop is what one Back does (null = nothing).</summary>
	private (IReadOnlyList<Page> Pages, Func<Task>? Pop) ResolveRootStack() =>
		Handlers.SailfishPageContainers.StackOf(RootPage(), _mauiContext);

	private Page? ResolveCurrentPage()
	{
		var root = RootPage();
		// The topmost modal wins; NavigationPage-wrapped modals resolve to their current inner page.
		var modals = root?.Navigation?.ModalStack;
		if (modals is { Count: > 0 })
		{
			var modal = modals[modals.Count - 1];
			return modal is NavigationPage { CurrentPage: not null } mnav ? mnav.CurrentPage : modal;
		}
		var (pages, _) = ResolveRootStack();
		return pages.Count > 0 ? pages[pages.Count - 1] : root;
	}

	/// <summary>The page the reconcile renders. While a native pop is in flight MAUI's CurrentPage is still the
	/// popped page, so address the MAUI page the mirror top maps to (root stack, then modal levels, as
	/// <see cref="ExpectedNativeDepth"/> counts).</summary>
	private Page? ResolveReconcilePage()
	{
		var page = ResolveCurrentPage();
		if (page is null || _nativePageIds.Count == 0 || ExpectedNativeDepth() <= _nativePageIds.Count)
			return page;
		var index = _nativePageIds.Count - 1;
		var root = RootPage();
		var (rootPages, _) = ResolveRootStack();
		if (index < rootPages.Count)
			return rootPages[index];
		index -= Math.Max(1, rootPages.Count);
		foreach (var modal in root?.Navigation?.ModalStack ?? Array.Empty<Page>())
		{
			if (modal is NavigationPage mnav)
			{
				var mstack = mnav.Navigation.NavigationStack;
				if (index < mstack.Count)
					return mstack[index];
				index -= mstack.Count;
			}
			else if (index == 0)
				return modal;
			else
				index--;
		}
		return page;
	}

	/// <summary>The window-level modal stack (null when no navigation surface exists yet).</summary>
	private IReadOnlyList<Page>? ResolveModalStack()
	{
		var root = _window.Page ?? (_window as Microsoft.Maui.IWindow)?.Content as Page;
		return root?.Navigation?.ModalStack;
	}

	private Task PopModalTopAsync()
	{
		var root = _window.Page ?? (_window as Microsoft.Maui.IWindow)?.Content as Page;
		return root?.Navigation is { } navigation ? navigation.PopModalAsync() : Task.CompletedTask;
	}

	/// <summary>What one Back removes from the MAUI model.</summary>
	private enum BackTarget
	{
		/// <summary>Nothing is poppable; the caller must not touch any stack.</summary>
		None,
		/// <summary>The topmost modal is a single page: close the modal.</summary>
		Modal,
		/// <summary>The topmost modal is a NavigationPage with inner depth: pop one inner page.</summary>
		ModalNav,
		/// <summary>No modal is open: pop one page off the root NavigationPage.</summary>
		RootNav,
	}

	/// <summary>
	/// The single Back decision, shared by <see cref="TryPop"/> and <see cref="PopMauiLevelsAsync"/>. The deepest
	/// active surface absorbs one level: a modal NavigationPage pops inside itself while it has inner depth,
	/// matching how <see cref="ExpectedNativeDepth"/> counts one native page per inner page.
	/// </summary>
	private (BackTarget Target, Func<Task>? Pop) ResolveBackTarget()
	{
		if (ResolveModalStack() is { Count: > 0 } modals)
		{
			if (modals[modals.Count - 1] is NavigationPage mnav && mnav.Navigation.NavigationStack.Count > 1)
				return (BackTarget.ModalNav, () => mnav.Navigation.PopAsync());
			return (BackTarget.Modal, null);
		}
		// Root stack: NavigationPage, Shell section, TabbedPage child, FlyoutPage detail / presented flyout.
		var (pages, pop) = ResolveRootStack();
		if (pop is not null && pages.Count > 1)
			return (BackTarget.RootNav, pop);
		return (BackTarget.None, null);
	}

	/// <summary>Native model-page depth for the MAUI navigation state: the root stack plus one per modal level
	/// (a modal NavigationPage contributes its whole inner stack).</summary>
	// --- Navigation completion ---
	// A push or pop is finished for MAUI when the Silica pageStack shows it, as UINavigationController's completion
	// or the fragment transaction is on the other platforms: not animating, at the depth MAUI expects, no native pop
	// still being followed. Waiters run on the Qt thread from the poll that observed it; a timeout never lets a stuck
	// transition hang PushAsync.

	private readonly List<(Action Done, long Deadline)> _settleWaiters = new();

	/// <summary>Whether the native stack shows exactly the MAUI navigation state.</summary>
	/// <remarks>A FollowNative operation does not block it: native already shows the target, and the MAUI pop it
	/// runs waits on exactly this (otherwise each back gesture would stall until the pop timeout).</remarks>
	internal bool NavigationSettled =>
		_navStateAdopted && !_navStackBusy && _navOp is null or { Kind: NavOpKind.FollowNative } &&
		_nativePageIds.Count == ExpectedNativeDepth();

	/// <summary>Runs <paramref name="done"/> once the native stack settled on the current MAUI state (or after
	/// <paramref name="timeoutMs"/>, logged), polling promptly meanwhile. Any thread.</summary>
	internal void WhenNavigationSettled(Action done, int timeoutMs = 3000)
	{
		void Register()
		{
			_settleWaiters.Add((done, Environment.TickCount64 + timeoutMs));
			KickSettlePolls();
		}
		QtHostRuntime.RunOnQtThread(Register);
	}

	/// <summary>While a waiter exists, polls every 50 ms instead of waiting for the heartbeat.</summary>
	private void KickSettlePolls() => _scheduler.KickSettlePolls(() => _settleWaiters.Count > 0);

	private void CompleteSettledNavigation()
	{
		if (_settleWaiters.Count == 0)
			return;
		var settled = NavigationSettled;
		var now = Environment.TickCount64;
		for (var i = _settleWaiters.Count - 1; i >= 0; i--)
		{
			var (done, deadline) = _settleWaiters[i];
			if (!settled && now < deadline)
				continue;
			_settleWaiters.RemoveAt(i);
			if (!settled)
				QtHostDiag.Warn(QtHostDiagChannel.Navigation,
					$"navigation not settled natively in time (busy={_navStackBusy}, native {_nativePageIds.Count} vs MAUI {ExpectedNativeDepth()}) — completing anyway");
			NavigationsSettled += settled ? 1 : 0;
			// Next loop turn: the awaiting app code (PushAsync continuations) must not run inside this poll and fuse
			// with its reconcile into one long frame.
			QtHostRuntime.Post(done);
		}
	}

	/// <summary>Navigations MAUI saw complete on a settled native stack (the rest timed out).</summary>
	public long NavigationsSettled { get; private set; }

	private int ExpectedNativeDepth()
	{
		var root = RootPage();
		var depth = root is null ? 0 : Math.Max(1, ResolveRootStack().Pages.Count);
		var modals = root?.Navigation?.ModalStack;
		if (modals is not null)
			foreach (var modal in modals)
				depth += modal is NavigationPage mnav ? Math.Max(1, mnav.Navigation.NavigationStack.Count) : 1;
		return depth;
	}

	/// <summary>Pops the MAUI levels a native-side pop removed, via <see cref="ResolveBackTarget"/>. One detection
	/// can represent several native pops, and popping fewer makes the depth sync re-push the page the user left.
	/// The pops are awaited so the next reconcile doesn't render the popped page onto the returned-to one.</summary>
	private async Task PopMauiLevelsAsync(NavOperation op, int levels)
	{
		try
		{
			for (var i = 0; i < levels; i++)
			{
				var (target, nav) = ResolveBackTarget();
				if (target == BackTarget.Modal)
				{
					QtHostDiag.Trace(QtHostDiagChannel.Navigation, "native pop → MAUI PopModalAsync");
					await WithPopTimeout(PopModalTopAsync());
				}
				else if (nav is not null)
				{
					QtHostDiag.Trace(QtHostDiagChannel.Navigation, $"native pop → MAUI PopAsync ({target})");
					await WithPopTimeout(nav());
				}
				else
				{
					// Native shrank further than MAUI can follow (a real desync): stop, the depth sync re-pushes native back
					// up to the MAUI depth.
					QtHostDiag.Warn(QtHostDiagChannel.Navigation,
						$"native pop but MAUI has nothing to pop at level {i + 1}/{levels} (mauiDepth={ExpectedNativeDepth()} nativeMirror={_nativePageIds.Count}) — repairing native back to the MAUI depth");
					break;
				}
			}
		}
		finally
		{
			// Completion, not time or depth comparison, ends the operation: MAUI-ahead-of-native looks the same as a
			// legitimate app push. The operation deadline only nets a pop that never completes.
			op.MauiDone = true;
			RequestPoll();
		}
		// Sweep only returned-to-page QML hosts the mirror no longer knows (strays from misaddressed batches); a
		// blanket wipe would kill back-cache survivors. Runs in the next reconcile, once the routing table holds
		// the returned-to page.
		_strayScanPending = true;
		_layoutDirty = true;   // re-resolve the top page with the pops applied
		RequestPoll();
	}

	/// <summary>Per-level bound on awaiting a PopAsync. MAUI completes a pop through lifecycle events the reconcile
	/// sends, which mid multi-level pop address the wrong page, so an unbounded await deadlocks. The stack mutation
	/// is synchronous; a pop outliving this finishes in the background. A timeout, never the completion signal.</summary>
	private const int NavPopLevelTimeoutMs = 400;

	private static async Task WithPopTimeout(Task pop)
	{
		var finished = await Task.WhenAny(pop, Task.Delay(NavPopLevelTimeoutMs));
		if (finished == pop)
			await pop;   // propagate a real failure
	}

	// --- Native navigation mapping (MAUI ⇄ Silica pageStack) ---
	// Each poll reads one nav-state snapshot from the shell and: bridges activation into the MAUI Window
	// lifecycle, follows native-side pops (Silica back gesture) in MAUI, and drives the native stack to the
	// MAUI depth (destroy-before-pop, PageStackAction.Immediate).

	private const string NavStateJs =
		"(typeof window!=='undefined'&&window.mauiPages?JSON.stringify({" +
		// The pageStack itself, not the page registry: a popped page stays registered until its deferred destruction.
		"ids:(function(){var a=[];pageStack.find(function(p){if(p&&p.mauiPageId!==undefined)a.unshift(String(p.mauiPageId));return false});return a})()," +
		"ver:(window.mauiStackVersion||0)," +
		"busy:!!pageStack.busy," +
		"topModel:!!(pageStack.currentPage&&pageStack.currentPage.mauiPageId!==undefined)," +
		"active:!!window.active," +
		"appState:(typeof Qt!=='undefined'&&Qt.application?Qt.application.state:-1)}):'{}')";

	private void SyncNativeNavigation()
	{
		if (!TryReadNavState(out var ids, out var busy, out var topModel, out var active, out var appState, out var version))
		{
			if (_navOp is not null && QtHostDiag.TraceEnabled)
				QtHostDiag.Trace(QtHostDiagChannel.Navigation, $"navigation {_navOp} in flight but the native state was unreadable");
			return;
		}
		// Poll skips the reconcile mid-transition: the dying page's hosts would fail the geometry probe and arm
		// the dead-host net against the revealed page. A push has no dying page: the incoming one is the top and
		// alive, so it keeps reconciling and laying out while it slides in (see PushTransitionRenders).
		_navStackBusy = busy;
		_pushTransition = busy && PushTransitionRenders && _idleNativeDepth >= 0 && ids.Count > _idleNativeDepth;
		if (!busy)
		{
			_idleNativeDepth = ids.Count;
			FlushDeferredNativeDestroys();   // a popped page's hosts, once its slide-out ended
		}

		// --- activation bridge (window focus + application foreground) ---
		if (_lastWindowActive is null)
		{
			if (!_appLifecycleStarted)
			{
				// No Created: MAUI startup already sent it (re-sending throws).
				_appLifecycleStarted = true;
				if (appState == 4)
					RaiseWindowLifecycle(w => w.Resumed(), "Resumed (Qt.application.state=ApplicationActive)", 3);
			}
			if (active)
				RaiseWindowLifecycle(w => w.Activated(), "Activated (window active at startup)", 1);
		}
		else if (active != _lastWindowActive.Value)
		{
			if (active) RaiseWindowLifecycle(w => w.Activated(), "Activated", 1);
			else RaiseWindowLifecycle(w => w.Deactivated(), "Deactivated", 2);
		}
		_lastWindowActive = active;

		if (_lastAppState != -1 && appState != _lastAppState)
		{
			// Qt.ApplicationState: Suspended=0, Hidden=1, Inactive=2, Active=4.
			if (appState == 4)
				RaiseWindowLifecycle(w => w.Resumed(), $"Resumed (appState {_lastAppState}→{appState})", 3);
			else if (appState == 0)
				RaiseWindowLifecycle(w => w.Stopped(), $"Stopped (appState {_lastAppState}→{appState})", 4);
		}
		_lastAppState = appState;
		// Host creation waits for a stable Active state: objects created in the activation rebuild die.
		if (appState != 4)
			_activeSinceMs = 0;
		else if (_activeSinceMs == 0)
			_activeSinceMs = Environment.TickCount64;

		// --- first registry read: adopt the shell-created root page(s) ---
		if (!_navStateAdopted)
		{
			if (ids.Count == 0)
				return;
			_navStateAdopted = true;
			_nativePageIds.AddRange(ids);
			_nativePageSeq = ids.Count;   // the shell root is "mp1"; the next push is "mp2"
			_topModelPageId = ids[ids.Count - 1];
			QtHostDiag.Trace(QtHostDiagChannel.Navigation, $"adopted native model pages [{string.Join(",", ids)}]");
			return;
		}

		// The top model page is the QML object the hosts live on; when it changes, re-arm the page-instance-scoped
		// render state or the new page keeps the default title, no background and hosts at 0,0.
		var topId = ids.Count > 0 ? ids[ids.Count - 1] : string.Empty;
		if (topId != _topModelPageId)
		{
			_topModelPageId = topId;
			ModelPageSwitches++;
			ResetModelPageScopedState($"top model page → '{topId}'");
		}

		// Never race a pageStack transition or an open dialog/flyout: those adapters are transient layers over the
		// model page.
		if (busy || _dialogTcs is not null || _openFlyout is not null)
		{
			if (_navOp is not null && QtHostDiag.TraceEnabled)
				QtHostDiag.Trace(QtHostDiagChannel.Navigation,
					$"navigation {_navOp} waits: busy={busy} dialog={_dialogTcs is not null} flyout={_openFlyout is not null}");
			_nativeTopUnfollowed = topId.Length > 0 && NativeTopPageId is { } waitingTop && topId != waitingTop;
			return;
		}
		_stack.Step(ids, topModel, version);
		_nativeTopUnfollowed = false;
	}

	/// <summary>
	/// Convergence net for a burst of dead hosts (Silica rebuilt the model page's visuals): per-host recreation
	/// races the rebuild, so wipe every host of the top page and rebuild it from MAUI state on the next reconcile.
	/// </summary>
	private void FullPageReset(string reason)
	{
		// The blanket destroy takes parked back-cache hosts too, so retire the mirror first.
		DropRetention();
		CallPage(null, "__destroyAllHosts");
		TearDownHosts(pageId: null, pageAlive: false);   // also retires every collection list
		ResetModelPageScopedState(reason);
		QtHostDiag.Warn(QtHostDiagChannel.Geometry,
			$"page reset: full page host reset ({reason}) — the next reconcile rebuilds from MAUI state");
	}

	/// <summary>
	/// Title, background, scroll push and geometry are diffed per model page instance; a stack change puts a
	/// fresh MauiModelPage under the same MAUI page, so re-arm them all.
	/// </summary>
	private void ResetModelPageScopedState(string reason)
	{
		_renderedTitle = string.Empty;
		_renderedBusy = string.Empty;
		_renderedBack = string.Empty;
		_renderedOrientations = string.Empty;
		_renderedScheme = string.Empty;
		_renderedBackground = string.Empty;
		_lastScrollPush = string.Empty;
		_layoutDirty = true;
		foreach (var host in _current)
			host.AppliedGeometrySet = false;
		QtHostDiag.Trace(QtHostDiagChannel.Navigation, $"model page switched ({reason}) — title/background/scroll/geometry re-armed");
	}

	private bool TryReadNavState(out List<string> ids, out bool busy, out bool topModel, out bool active, out int appState,
	                             out long version)
	{
		ids = new List<string>();
		version = -1;
		busy = false;
		topModel = false;
		active = false;
		appState = -1;
		string json;
		try
		{
			json = QtHostRuntime.Eval(NavStateJs);
		}
		catch
		{
			return false;
		}
		if (string.IsNullOrEmpty(json) || json is "{}" or "null")
			return false;
		try
		{
			using var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;
			if (root.ValueKind != JsonValueKind.Object)
				return false;
			if (root.TryGetProperty("ids", out var arr) && arr.ValueKind == JsonValueKind.Array)
				foreach (var e in arr.EnumerateArray())
					ids.Add(e.GetString() ?? string.Empty);
			busy = root.TryGetProperty("busy", out var b) && b.ValueKind == JsonValueKind.True;
			topModel = root.TryGetProperty("topModel", out var tm) && tm.ValueKind == JsonValueKind.True;
			active = root.TryGetProperty("active", out var a) && a.ValueKind == JsonValueKind.True;
			if (root.TryGetProperty("appState", out var s) && s.TryGetInt32(out var sv))
				appState = sv;
			if (root.TryGetProperty("ver", out var v) && v.TryGetInt64(out var vv))
				version = vv;
			return true;
		}
		catch
		{
			return false;
		}
	}

	// Kinds: 1=Activated 2=Deactivated 3=Resumed 4=Stopped.
	private void RaiseWindowLifecycle(Action<Microsoft.Maui.IWindow> send, string what, int kind)
	{
		try
		{
			send((Microsoft.Maui.IWindow)_window);
			ActivationEvents++;
			switch (kind)
			{
				case 1: ActivatedSent++; break;
				case 2: DeactivatedSent++; break;
				case 3: ResumedSent++; break;
				case 4: StoppedSent++; break;
			}
			QtHostDiag.Trace(QtHostDiagChannel.Lifecycle, $"window lifecycle → MAUI {what}");
		}
		catch (Exception ex)
		{
			QtHostDiag.Error(QtHostDiagChannel.Lifecycle, $"window lifecycle '{what}' failed: {ex.Message}");
		}
	}

	private void PushModelPages(int count)
	{
		// Navigation timing starts at the first push and closes at the new page's SendAppearing.
		_navStopwatch ??= System.Diagnostics.Stopwatch.StartNew();
		_tlStart = 0;
		TimelineStart("push");
		for (var i = 0; i < count; i++)
		{
			// Park the outgoing page's hosts in the back cache so a pop-back restores them. Only safe after the
			// activation window (objects parked inside it die with the rebuild); earlier pushes tear down instead.
			if (_nativePageIds.Count > 0)
			{
				if (RetentionArmed)
					RetainOutgoingPage(_nativePageIds[^1]);
				else
					TearDownHosts(_nativePageIds[^1], pageAlive: true);
			}
			var id = $"mp{++_nativePageSeq}";
			// Transactional push: the synchronous eval pushes and reads back the confirmed state, and the mirror is
			// committed only if the pageStack took the page. Single-level pushes past activation animate like Silica;
			// this poll reconciles the new page before the first frame, so it slides in complete.
			var action = count == 1 && RetentionArmed && NavAnimation ? "Animated" : "Immediate";
			string rc;
			if (FaultNextPush)
			{
				FaultNextPush = false;   // one-shot injection
				rc = "fail|injected";
			}
			else
			{
				// The top page slides in with its real header; pages under it are covered at once.
				var title = BridgeValue.Quote(i == count - 1 ? CurrentTitle : string.Empty);
				rc = QtHostRuntime.Eval(
					$"(function(){{var p=pageStack.push(window.mauiPageUrl,{{mauiPageId:'{id}',pageTitle:{title}}},PageStackAction.{action});" +
					$"return (p&&p.mauiPageId==='{id}'?'ok':'fail')+'|depth='+pageStack.depth+'|top='+(pageStack.currentPage===p)}})()");
			}
			if (!rc.StartsWith("ok|", StringComparison.Ordinal))
			{
				NativeOpFailures++;
				QtHostDiag.Error(QtHostDiagChannel.Navigation,
					$"pageStack.push '{id}' REJECTED (native reported '{rc}') — mirror NOT committed " +
					$"(mauiDepth={ExpectedNativeDepth()} nativeMirror={_nativePageIds.Count}); the depth sync re-attempts it shortly");
				KickIn(250);
				// The outgoing page is still on top: un-park its hosts so the reconcile diffs against live objects.
				if (_nativePageIds.Count > 0)
					RestoreRetention(_nativePageIds[^1]);
				_layoutDirty = true;
				return;   // native depth unchanged: the rest of the batch is moot
			}
			_nativePageIds.Add(id);
			NativePushes++;
			LogNavOp("PUSH", "MAUI", id, $"{rc}|{action}");
		}
	}

	private void PopModelPages(int count)
	{
		if (count <= 0 || _nativePageIds.Count == 0)
			return;
		_navStopwatch ??= System.Diagnostics.Stopwatch.StartNew();
		_tlStart = 0;
		TimelineStart("pop");
		for (var i = 0; i < count; i++)
		{
			if (_nativePageIds.Count == 0)
				return;
			var id = _nativePageIds[^1];
			// Animated back like the Silica gesture: the popped page keeps its QML hosts while sliding out and the
			// mirror drops them without an eval. Requires the returned-to page to be its restored back-cache self.
			var animated = count == 1 && NavAnimation && RetentionArmed &&
			               _nativePageIds.Count > 1 && IsParkedOn(ResolveCurrentPage(), _nativePageIds[_nativePageIds.Count - 2]);
			if (animated)
			{
				// The page slides out with its content (Silica keeps a popped page painted until the transition ends).
				_deferNativeDestroy = true;
				try { TearDownHosts(pageId: null, pageAlive: false); }
				finally { _deferNativeDestroy = false; }
			}
			else
				// Destroy-before-pop through the still-alive page's own op queue.
				TearDownHosts(id, pageAlive: true);
			_nativePageIds.RemoveAt(_nativePageIds.Count - 1);
			if (_nativePageIds.Count > 0)
				RestoreRetention(_nativePageIds[^1]);
			// Same disarm as the native-pop path: dead handles from the dying page must not reset the revealed one.
			_fullResetPending = false;
			_healedSinceReconcile = 0;
			// Reconcile while the popped page still covers the stack, so the returned-to page is painted when revealed —
			// at the last level only: an intermediate model page is popped next, and MAUI (already at the target) would
			// render the target onto it with the very hosts the page cache holds for the target's own model page.
			if (i == count - 1)
				Reconcile();
			// Transactional pop: a negative rc means the pageStack refused, and the mirror must not stay committed (the
			// registry would then agree with it and nothing would re-detect the stranded page).
			int rc;
			if (FaultNextPop)
			{
				FaultNextPop = false;   // one-shot injection
				rc = -1;
			}
			else
			{
				rc = QtHostRuntime.PopPage(immediate: !animated);
			}
			if (rc < 0)
			{
				NativeOpFailures++;
				QtHostDiag.Error(QtHostDiagChannel.Navigation,
					$"pageStack.pop '{id}' FAILED (native rc={rc}) — mirror rolled back, retried shortly " +
					$"(mauiDepth={ExpectedNativeDepth()} nativeMirror={_nativePageIds.Count + 1})");
				KickIn(250);
				_nativePageIds.Add(id);   // the page never left the stack
				// Undo the restore: re-park the returned-to page's hosts under their own id instead of leaving them live on
				// the wrong page. The popped page's hosts are already gone; the retry rebuilds them.
				if (_nativePageIds.Count > 1)
					RetainOutgoingPage(_nativePageIds[_nativePageIds.Count - 2]);
				_layoutDirty = true;
				return;   // native depth unchanged: the rest of the batch is moot
			}
			NativePops++;
			PruneParked();   // pages parked on the popped model page died with it
			LogNavOp("POP", "MAUI", id, $"rc={rc}|{(animated ? "Animated" : "Immediate")}");
		}
	}

	/// <summary>
	/// One structured line per navigation operation: op id, source, both depths and the native confirmation,
	/// enough to tell MAUI, the pageStack and the sync layer apart in a device log.
	/// </summary>
	private void LogNavOp(string op, string source, string pageId, string nativeReport)
	{
		QtHostDiag.Trace(QtHostDiagChannel.Navigation,
			$"[nav#{++_navOpSeq}] {op} '{pageId}' src={source} mauiDepth={ExpectedNativeDepth()} " +
			$"nativeMirror={_nativePageIds.Count} top='{(NativeTopPageId ?? "-")}' native[{nativeReport}]");
	}

	/// <summary>Destroys every live host of the outgoing page (a QML destroy batch addressed by registry id when
	/// the page is alive, managed detach in both cases) and clears the synthetic-host slots.</summary>
	private void TearDownHosts(string? pageId, bool pageAlive)
	{
		// Item/slot children die with this page's list adapters; a parked page keeps its own warm.
		_collection.TearDownListsForHosts(_current.Select(h => h.Id).ToHashSet());
		if (_current.Count == 0)
			return;
		var hosts = _current.ToList();
		if (pageAlive && pageId is not null)
			DestroyHosts(hosts, pageId);
		else
			foreach (var host in hosts)
				ReleaseHost(host);
		foreach (var host in hosts)
			ReleaseSyntheticSlot(host);
		_current.Clear();
		_byId.Clear();
	}

	// --- INativeStackOwner: what the coordinator asks of the renderer ---

	int INativeStackOwner.ExpectedNativeDepth() => ExpectedNativeDepth();

	void INativeStackOwner.PushModelPages(int levels) => PushModelPages(levels);

	void INativeStackOwner.PopModelPages(int levels) => PopModelPages(levels);

	void INativeStackOwner.OnNativePopped(string? returnedTo)
	{
		// The popped page took its QML hosts with it: drop the mirror without an eval (dead handles resolve to null).
		TearDownHosts(pageId: null, pageAlive: false);
		PruneParked();
		// The returned-to page is already visible under the dying one: restore its parked hosts now.
		if (returnedTo is not null)
		{
			_tlStart = 0;
			TimelineStart("gesture-pop");
			RestoreRetention(returnedTo);
		}
		// Dead handles counted mid-transition belonged to the dying page; don't let them wipe the revealed page.
		_fullResetPending = false;
		_healedSinceReconcile = 0;
	}

	void INativeStackOwner.OnResynced(bool topGone)
	{
		if (topGone)
			TearDownHosts(pageId: null, pageAlive: false);
		PruneParked();
		_layoutDirty = true;
	}

	void INativeStackOwner.FollowNative(NavOperation op, int levels) =>
		Observe(PopMauiLevelsAsync(op, levels), "MAUI pop after a native pop");

	void INativeStackOwner.KickIn(long ms) => KickIn(ms);

	void INativeStackOwner.RequestPoll() => RequestPoll();

	void INativeStackOwner.LogNavOp(string op, string source, string pageId, string nativeReport) =>
		LogNavOp(op, source, pageId, nativeReport);
}
