using Microsoft.Maui.Controls;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

// Handler-driven host tree: as a native container adds or removes its child
// views, a Sailfish container handler reports its own child changes (the layout commands Add/Insert/Remove/Update/
// UpdateZIndex/Clear, a Content or ControlTemplate swap) and only that subtree is diffed on the next loop turn: its
// hosts are created, destroyed, re-parented and ordered in one op batch, then laid out. The full page reconcile stays
// as the verifier (navigation, page switch, the safety-net poll); what it still changes on a steady page is counted
// as a fixup. Subtrees that need the page around them (lists, scroll views, refresh, context menus) fall back to it.
internal sealed partial class QtHostPageRenderer
{
	/// <summary>MAUI_SAILFISH_HANDLER_TREE=0 leaves handler tree changes to the full page reconcile (A/B).</summary>
	internal static bool HandlerTree { get; set; } = SailfishEnv.Get("MAUI_SAILFISH_HANDLER_TREE") != "0";

	private bool _subtreeRetry;

	/// <summary>Subtree passes that applied a handler's tree change.</summary>
	public long SubtreeReconciles { get; private set; }

	/// <summary>Total and last subtree pass time (ms), layout included.</summary>
	public double SubtreeReconcileTotalMs { get; private set; }
	public double LastSubtreeReconcileMs { get; private set; }

	/// <summary>The last subtree pass before its layout (ms): walk + diff, the ops eval (QML objects created), handle
	/// lookup and seeding of the created hosts.</summary>
	public (double WalkDiffMs, double OpsEvalMs, double AttachMs) LastSubtreeSplit { get; private set; }

	/// <summary>The layout pass (measure, arrange, geometry flush of the page) inside the last subtree pass (ms).</summary>
	public double LastSubtreeLayoutMs { get; private set; }

	/// <summary>Tree changes handed to the full reconcile (page context needed, host not live, navigation running).</summary>
	public long SubtreeFallbacks { get; private set; }

	/// <summary>Full reconciles on a steady page that still changed the host tree (create/destroy/reparent/order):
	/// changes no handler reported.</summary>
	public long TreeFixups { get; private set; }

	/// <summary>A container handler changed its children (any thread); the subtree is diffed on the next loop turn.</summary>
	internal void RequestSubtree(IView container)
	{
		if (!HandlerTree || container is not Element element)
		{
			RequestPoll();
			return;
		}
		_scheduler.QueueSubtree(element);
	}

	private void RunSubtreeReconciles()
	{
		var roots = _scheduler.TakeSubtrees();
		if (roots.Count == 0)
			return;
		var sw = System.Diagnostics.Stopwatch.StartNew();
		string? fallback;
		try
		{
			fallback = ReconcileSubtrees(roots);
		}
		catch (Exception ex)
		{
			fallback = "error: " + ex.Message;
			QtHostDiag.Error(QtHostDiagChannel.QtHost, $"subtree reconcile failed: {ex}");
		}
		sw.Stop();
		if (fallback is null)
		{
			SubtreeReconciles++;
			LastSubtreeReconcileMs = sw.Elapsed.TotalMilliseconds;
			SubtreeReconcileTotalMs += LastSubtreeReconcileMs;
			return;
		}
		SubtreeFallbacks++;
		QtHostDiag.Trace(QtHostDiagChannel.QtHost, $"subtree reconcile → full reconcile ({fallback})");
		RequestPoll();
	}

	/// <summary>Diffs the hosts under <paramref name="containers"/> against their MAUI children and applies the change
	/// as one op batch plus a layout pass. Returns null when done, else why the full reconcile must run instead.</summary>
	private string? ReconcileSubtrees(List<Element> containers)
	{
		if (_navStackBusy || _stack.PopUnsynced || _navOp is not null || _fullResetPending || _resetHoldUntilMs > Environment.TickCount64 ||
		    !_windowGeometryKnown || CreationDeferred || _pendingAppearing is not null)
			return "navigation or page creation in progress";
		if (_rendered is not { } page || !ReferenceEquals(ResolveReconcilePage(), page))
			return "page switch pending";

		// Roots whose host is live on the rendered page, outermost only (a nested root is inside its ancestor's walk).
		var liveHosts = new HashSet<NativeElementHost>(_current);
		var roots = new List<(Element Element, NativeElementHost Host)>();
		foreach (var container in containers)
		{
			if (!_cache.TryGet(container, out var host) || host is null || !liveHosts.Contains(host) || !host.IsAttached)
				return $"{container.GetType().Name} has no live host";   // another page, a list row, not created yet
			if (container.Handler is Handlers.ISailfishAdapterHandler { WalksChildren: false })
				continue;   // the adapter draws its children itself
			if (NeedsPageContext(container, includeSelf: false))
				return $"{container.GetType().Name} holds a list, scroll, refresh or context-menu subtree";
			roots.Add((container, host));
		}
		roots.RemoveAll(r => roots.Any(o => !ReferenceEquals(o.Element, r.Element) && IsAncestor(o.Element, r.Element)));
		if (roots.Count == 0)
			return null;

		// The hosts the subtrees have now: pre-order after their root, linked by Parent.
		var oldHosts = new HashSet<NativeElementHost>();
		foreach (var (_, rootHost) in roots)
		{
			var inside = new HashSet<NativeElementHost> { rootHost };
			foreach (var host in _current)
				if (host.Parent is { } parent && inside.Contains(parent) && inside.Add(host))
					oldHosts.Add(host);
		}
		foreach (var host in oldHosts)
			if (NeedsPageContext(host.Element, includeSelf: true))
				return $"old subtree holds {host.Element.GetType().Name}";

		// What they should have: the same walk the page reconcile runs, rooted at each container.
		var walkStart = System.Diagnostics.Stopwatch.GetTimestamp();
		_skippedUnarranged = false;
		var desiredByRoot = new List<(NativeElementHost Root, List<NativeElementHost> Hosts)>();
		var props = new Dictionary<NativeElementHost, Dictionary<string, object?>>();
		var newHosts = new HashSet<NativeElementHost>();
		foreach (var (element, rootHost) in roots)
		{
			var desired = new List<NativeElementHost>();
			Walk((IVisualTreeElement)element, desired, props, rootHost);
			desiredByRoot.Add((rootHost, desired));
			foreach (var host in desired)
			{
				// A host that lives elsewhere on the page moved in from a container that reported nothing; a parked one
				// must be reclaimed from the page cache first (the page reconcile does).
				if (!newHosts.Add(host) || (liveHosts.Contains(host) && !oldHosts.Contains(host)) || IsParked(host))
					return $"{host} moved in from outside the subtree";
				if (host.Element is VisualElement visualState && props.TryGetValue(host, out var hostProps))
					QtHostVisualState.Merge(hostProps, visualState);
				if (host.QmlUri == "image" && props.TryGetValue(host, out var clipProps))
					QtHostClip.Merge(clipProps, host.Element as VisualElement, page);
			}
		}

		var diff = new HostTreeDiff(_current);
		// Destroy descendants before ancestors.
		var destroyed = _current.Where(h => oldHosts.Contains(h) && !newHosts.Contains(h)).Reverse().ToList();
		foreach (var host in destroyed)
		{
			diff.Destroy(host);
			ReleaseHost(host);
			_byId.Remove(host.Id);
		}
		// In pre-order per root: a survivor is re-attached where it moved and updated in place, a new host is created.
		var updates = 0;
		foreach (var (_, desired) in desiredByRoot)
		{
			foreach (var host in desired)
			{
				if (oldHosts.Contains(host))
				{
					diff.Reparent(host);
					updates += ApplyUpdates(host, props.TryGetValue(host, out var p) ? p : EmptyProps, "subtree-diff");
					continue;
				}
				diff.Create(host, props.TryGetValue(host, out var cp) ? cp : EmptyProps, CreateOp);
				_awaitingArrange.Remove(host.Element);
			}
		}
		// Child order (stacking order) of the roots and every host under them.
		foreach (var (_, desired) in desiredByRoot)
			diff.Order(desired);
		var ops = diff.Ops;
		var created = diff.Created;

		// _current keeps the page pre-order: each root's new subtree follows it.
		_current.RemoveAll(oldHosts.Contains);
		foreach (var (rootHost, desired) in desiredByRoot)
		{
			var at = _current.IndexOf(rootHost);
			_current.InsertRange(at + 1, desired);
			foreach (var host in desired)
				_byId[host.Id] = host;
		}

		var opsStart = System.Diagnostics.Stopwatch.GetTimestamp();
		var attachStart = opsStart;
		if (ops.Count > 0)
		{
			ApplyOps(ops);
			attachStart = System.Diagnostics.Stopwatch.GetTimestamp();
			foreach (var host in created)
				AttachNative(host, props.TryGetValue(host, out var p) ? p : EmptyProps);
			_collection.OnHostsCreated(created);
		}
		var attachEnd = System.Diagnostics.Stopwatch.GetTimestamp();
		static double Ms(long a, long b) => (b - a) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
		LastSubtreeSplit = (Ms(walkStart, opsStart), Ms(opsStart, attachStart), Ms(attachStart, attachEnd));
		QtHostDiag.Trace(QtHostDiagChannel.QtHost,
			$"'{TitleOf(page)}' subtree reconcile [{string.Join(",", roots.Select(r => r.Host.Id))}] create={created.Count} " +
			$"destroy={destroyed.Count} update={updates} ops={ops.Count} hosts={_current.Count}");
		if (ops.Count == 0 && updates == 0)
			return null;

		_layoutDirty = true;
		var layoutStart = System.Diagnostics.Stopwatch.GetTimestamp();
		RunLayoutPass(page);
		LastSubtreeLayoutMs = (System.Diagnostics.Stopwatch.GetTimestamp() - layoutStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
		// Shapes created before their first arrange were skipped: create them in the same frame, once.
		if (_skippedUnarranged && !_subtreeRetry)
		{
			_subtreeRetry = true;
			try { return ReconcileSubtrees(roots.Select(r => r.Element).ToList()); }
			finally { _subtreeRetry = false; }
		}
		return null;
	}

	/// <summary>Elements whose hosting depends on the page around them (the page reconcile's walk state).</summary>
	private static bool NeedsPageContext(Element element, bool includeSelf)
	{
		if (includeSelf && (element is ItemsView or ScrollView or RefreshView ||
		                    element is View view && FlyoutBase.GetContextFlyout(view) is not null))
			return true;
		if (element is not IVisualTreeElement tree || element is ItemsView)
			return false;   // item views are the collection bridge's
		foreach (var child in tree.GetVisualChildren())
			if (child is Element childElement && NeedsPageContext(childElement, includeSelf: true))
				return true;
		return false;
	}

	private static bool IsAncestor(Element ancestor, Element element)
	{
		for (var e = element.Parent; e is not null; e = e.Parent)
			if (ReferenceEquals(e, ancestor))
				return true;
		return false;
	}

	/// <summary>
	/// A Sailfish handler disconnected (DisconnectHandler, e.g. after a pop or an explicit disconnect). If its element
	/// left the rendered page but its host is still live, the host and those under it are destroyed now rather than at
	/// the next reconcile. Parked pages (the back cache) are left to their retention.
	/// </summary>
	internal void OnHandlerDisconnected(NativeElementHost host)
	{
		void Release()
		{
			if (!host.IsAttached || _rendered is not { } page || !_current.Contains(host) ||
			    IsAncestor(page, host.Element))
				return;
			var inside = new HashSet<NativeElementHost> { host };
			foreach (var h in _current)
				if (h.Parent is { } parent && inside.Contains(parent))
					inside.Add(h);
			var doomed = _current.Where(inside.Contains).Reverse().ToList();
			DestroyHosts(doomed, pageId: null, unroute: true);
			foreach (var h in doomed)
				h.AppliedParentId = null;
			_current.RemoveAll(inside.Contains);
			_collection.SyncDesired(_parkedHosts.Count == 0 ? _current : _current.Concat(_parkedHosts).ToList());
			HandlerReleases++;
			RequestLayout();
		}
		QtHostRuntime.RunOnQtThread(Release);
	}

	/// <summary>Hosts destroyed by a handler disconnect before any reconcile saw their element leave.</summary>
	public long HandlerReleases { get; private set; }

	/// <summary>The full reconcile takes over the reported changes still queued (it applies them itself).</summary>
	private bool TakePendingSubtrees()
	{
		return _scheduler.DropPendingSubtrees();
	}

	/// <summary>Shapes the walk skipped until their first arrange; their later create is expected, not a fixup.</summary>
	private readonly HashSet<Element> _awaitingArrange = new();

	/// <summary>Counts the full reconcile's tree ops on a steady page (see <see cref="TreeFixups"/>). Not fixups:
	/// changes a handler reported that this pass applied first, page-level synthetic surfaces (menus, panels), and
	/// shapes created once arranged, with the order ops that place them.</summary>
	private void NoteTreeFixup(List<Dictionary<string, object?>> ops, bool pageChanged, bool handlerReported)
	{
		if (pageChanged || handlerReported || !HandlerTree)
			return;
		var tree = new List<Dictionary<string, object?>>();
		var expected = false;
		foreach (var op in ops)
		{
			var kind = op.GetValueOrDefault("op") as string;
			if (kind is not ("create" or "destroy" or "reparent" or "order"))
				continue;
			var id = op.GetValueOrDefault("id") as string;
			if (id is not null && id.StartsWith("synth-", StringComparison.Ordinal))
				continue;
			if (kind == "create" && id is not null && _byId.TryGetValue(id, out var host) && _awaitingArrange.Contains(host.Element))
			{
				expected = true;
				continue;
			}
			tree.Add(op);
		}
		if (tree.Count == 0 || (expected && tree.All(o => o.GetValueOrDefault("op") is "order")))
			return;
		TreeFixups++;
		if (QtHostDiag.TraceEnabled)
			QtHostDiag.Trace(QtHostDiagChannel.QtHost,
				$"tree fixup on '{(_rendered is null ? "-" : TitleOf(_rendered))}': " +
				string.Join(",", tree.Take(8).Select(o =>
					$"{o.GetValueOrDefault("op")}:{o.GetValueOrDefault("uri") ?? string.Empty}:{o.GetValueOrDefault("id") ?? o.GetValueOrDefault("parent")}")) +
				(tree.Count > 8 ? $",+{tree.Count - 8}" : string.Empty));
	}
}
