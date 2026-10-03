using Microsoft.Maui.Controls;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

// The page cache (back cache): the QML hosts of pages the app still holds but does not show stay alive, keyed by the
// MAUI page, so returning to one reveals it instead of rebuilding it. As on the other platforms each navigation
// container keeps its pages (a FragmentManager's back stack, a UINavigationController per tab): a page parked under a
// push waits below the pushed page; a tab, Shell section or FlyoutPage detail switched away from waits hidden on the
// same model page. The QML objects live on Silica model pages only the renderer knows, so the cache is here; the
// container handlers decide which pages are shown and which still belong to the app.
internal sealed partial class QtHostPageRenderer
{
	private sealed class ParkedPage
	{
		public required Page Page { get; init; }
		public required string ModelPageId { get; init; }
		public required List<NativeElementHost> Hosts { get; init; }
		public long LastUsed { get; set; }
		/// <summary>Parked on the page on screen (a tab/section/detail switch): its roots are hidden.</summary>
		public bool Hidden { get; set; }
	}

	private readonly List<ParkedPage> _parked = new();
	private readonly HashSet<NativeElementHost> _parkedHosts = new();
	private long _parkClock;

	/// <summary>Most pages kept at once (MAUI_SAILFISH_PAGE_CACHE, default 4); the least recently used is rebuilt
	/// on return instead.</summary>
	internal static int PageCacheLimit { get; set; } = Math.Max(1, SailfishEnv.Int("MAUI_SAILFISH_PAGE_CACHE") ?? 4);

	/// <summary>Whether the host belongs to a parked page.</summary>
	internal bool IsParked(NativeElementHost host) => _parkedHosts.Contains(host);

	/// <summary>Host ids of parked pages (diagnostics: deliberate retention, not a QML host leak).</summary>
	internal IReadOnlyCollection<string> ParkedHostIds => _parkedHosts.Select(h => h.Id).ToList();

	/// <summary>Pages parked now, and the cache's hits, parks and drops (diagnostics).</summary>
	public int ParkedPages => _parked.Count;
	public long PageCacheRestores { get; private set; }
	public long PageCacheParks { get; private set; }
	public long PageCacheDrops { get; private set; }

	/// <summary>Back-cache parking is safe only after the activation window: objects parked inside it die with
	/// the Silica rebuild and a restore would re-attach the mirror to dead handles.</summary>
	private bool RetentionArmed => _windowGeometryKnown && _navStateAdopted && ActivationSettled;

	/// <summary>Parks the rendered page's hosts under the outgoing model page before a push covers it.</summary>
	private void RetainOutgoingPage(string pageId)
	{
		// Synthetic surfaces (pulleys, menus, panels) are page-scoped and recreated on return: parking them would
		// leave the slot fields pointing at the wrong page's hosts.
		var synthetic = new List<NativeElementHost>();
		if (_pullHost is not null) synthetic.Add(_pullHost);
		if (_pushHost is not null) synthetic.Add(_pushHost);
		if (_ctxMenuHost is not null) synthetic.Add(_ctxMenuHost);
		synthetic.AddRange(_interactionHosts.Values);
		if (synthetic.Count > 0)
		{
			DestroyHosts(synthetic, pageId);
			_pullHost = null;
			_pushHost = null;
			_ctxMenuHost = null;
			_interactionHosts.Clear();
		}
		var hosts = _current.Where(h => !synthetic.Contains(h)).ToList();
		_current.Clear();
		_byId.Clear();
		if (_rendered is { } page && hosts.Count > 0)
			Park(page, pageId, hosts, hide: false);
		else if (hosts.Count > 0)
			DestroyHosts(hosts, pageId);
	}

	/// <summary>
	/// A tab, Shell section or FlyoutPage detail switch on the same model page: the page switched away from is parked
	/// hidden (if the app still holds it) and the page switched to comes back from the cache when it is there.
	/// Called by the page reconcile at a page switch, before its diff.
	/// </summary>
	private void SwitchPageInPlace(Page? previous, Page next)
	{
		if (!RetentionArmed || NativeTopPageId is not { } top)
			return;
		// The page switched to is the most recent use: parking the previous one must not evict it.
		if (_parked.FirstOrDefault(e => ReferenceEquals(e.Page, next) && e.ModelPageId == top) is { } wanted)
			wanted.LastUsed = ++_parkClock;
		if (previous is not null && !ReferenceEquals(previous, next) && PageHeld(previous) &&
		    _current.Count > 0 && _current.Any(h => !IsSynthetic(h) && IsWithin(h.Element, previous)))
		{
			// Page-scoped synthetic hosts stay: the diff swaps them for the new page's.
			var hosts = _current.Where(h => !IsSynthetic(h)).ToList();
			_current.RemoveAll(h => !IsSynthetic(h));
			foreach (var host in hosts)
				_byId.Remove(host.Id);
			Park(previous, top, hosts, hide: true);
		}
		if (!_current.Any(h => !IsSynthetic(h)))
			RestoreParked(next, top);
		// A switch can take pages out of the app (a replaced Detail, a new root): they leave the cache now.
		PruneParked();
	}

	/// <summary>A pop reveals <paramref name="pageId"/>: its page comes back from the cache when parked there; other
	/// pages parked on it stay parked, hidden (they were covered, now they would show).</summary>
	private bool RestoreRetention(string pageId)
	{
		var restored = ResolveReconcilePage() is { } target && RestoreParked(target, pageId);
		foreach (var entry in _parked)
			if (entry.ModelPageId == pageId && !entry.Hidden)
				Hide(entry);
		return restored;
	}

	/// <summary>Whether the page on <paramref name="pageId"/> after a pop is parked there (an animated pop needs its
	/// returned-to page already painted).</summary>
	private bool IsParkedOn(Page? page, string pageId) =>
		page is not null && _parked.Any(e => ReferenceEquals(e.Page, page) && e.ModelPageId == pageId);

	private void Park(Page page, string modelPageId, List<NativeElementHost> hosts, bool hide)
	{
		DropParked(page, keep: hosts);   // one entry per page
		var entry = new ParkedPage { Page = page, ModelPageId = modelPageId, Hosts = hosts, LastUsed = ++_parkClock };
		_parked.Add(entry);
		foreach (var host in hosts)
			_parkedHosts.Add(host);
		if (hide)
			Hide(entry);
		PageCacheParks++;
		QtHostDiag.Trace(QtHostDiagChannel.Navigation,
			$"page cache: parked '{TitleOf(page)}' ({hosts.Count} hosts) on '{modelPageId}'{(hide ? " hidden" : string.Empty)}");
		PruneParked();
	}

	private bool RestoreParked(Page page, string modelPageId)
	{
		var entry = _parked.FirstOrDefault(e => ReferenceEquals(e.Page, page) && e.ModelPageId == modelPageId);
		if (entry is null)
			return false;
		_parked.Remove(entry);
		foreach (var host in entry.Hosts)
		{
			_parkedHosts.Remove(host);
			host.AppliedGeometrySet = false;   // the layout pass places (and, when hidden, shows) it again
		}
		_current.AddRange(entry.Hosts);
		_collection.OnPageRestored(entry.Hosts);
		PageCacheRestores++;
		QtHostDiag.Trace(QtHostDiagChannel.Navigation,
			$"page cache: restored '{TitleOf(page)}' ({entry.Hosts.Count} hosts) on '{modelPageId}'");
		return true;
	}

	/// <summary>Hides a parked page's roots (its descendants go with them); the restore's geometry push shows them.</summary>
	private static void Hide(ParkedPage entry)
	{
		entry.Hidden = true;
		foreach (var host in entry.Hosts)
		{
			if (host.Parent is not null || !host.IsAttached)
				continue;
			QtHostRuntime.SetProperty(host.NativeHandle, "visible", "false");
			host.AppliedVisible = false;
		}
	}

	/// <summary>Drops entries of model pages that are gone (their objects died with them), of pages the app no longer
	/// holds (popped, a replaced Detail), and the least recently used beyond <see cref="PageCacheLimit"/>.</summary>
	private void PruneParked()
	{
		foreach (var entry in _parked.ToList())
		{
			if (!_nativePageIds.Contains(entry.ModelPageId))
				Drop(entry, pageAlive: false, "its model page is gone");
			else if (!PageHeld(entry.Page))
				Drop(entry, pageAlive: true, "the app no longer holds the page");
		}
		while (_parked.Count > PageCacheLimit)
			Drop(_parked.MinBy(e => e.LastUsed)!, pageAlive: true, $"over the cache limit ({PageCacheLimit})");
	}

	private void DropParked(Page page, List<NativeElementHost> keep)
	{
		if (_parked.FirstOrDefault(e => ReferenceEquals(e.Page, page)) is not { } entry)
			return;
		// Hosts are keyed by element: the page's hosts being parked now may be the same objects.
		entry.Hosts.RemoveAll(keep.Contains);
		Drop(entry, pageAlive: _nativePageIds.Contains(entry.ModelPageId), "parked again");
	}

	/// <summary>
	/// A host is on screen or parked, never both: hosts are keyed by element, so a parked page rendered again would get
	/// a second QML object under the same id, and dropping the old entry would destroy the new one (an empty page).
	/// Before the reconcile diffs, every desired host is taken back from the cache: restored when its entry lives on
	/// the model page on screen, else dropped there (its objects are destroyed on that page) so the diff creates it here.
	/// </summary>
	private void ReclaimParked(List<NativeElementHost> desired)
	{
		if (_parkedHosts.Count == 0)
			return;
		HashSet<NativeElementHost>? wanted = null;
		foreach (var entry in _parked.ToList())
		{
			wanted ??= new HashSet<NativeElementHost>(desired);
			if (!entry.Hosts.Any(wanted.Contains))
				continue;
			if (entry.ModelPageId == NativeTopPageId)
				RestoreParked(entry.Page, entry.ModelPageId);
			else
				Drop(entry, pageAlive: _nativePageIds.Contains(entry.ModelPageId), "its page is rendered again");
		}
	}

	/// <summary>Whether the app still holds <paramref name="page"/>: the window's root or a page its container
	/// handlers hold, or a modal (or a page of a modal container).</summary>
	private bool PageHeld(Page page)
	{
		if (Handlers.SailfishPageContainers.Holds(RootPage(), page, _mauiContext))
			return true;
		foreach (var modal in ResolveModalStack() ?? Array.Empty<Page>())
			if (Handlers.SailfishPageContainers.Holds(modal, page, _mauiContext))
				return true;
		return false;
	}

	/// <summary>Drops the whole cache (a full page reset, a rebuilt stack).</summary>
	private void DropRetention()
	{
		foreach (var entry in _parked.ToList())
			Drop(entry, pageAlive: _nativePageIds.Contains(entry.ModelPageId), "cache reset");
	}

	private void Drop(ParkedPage entry, bool pageAlive, string reason)
	{
		_parked.Remove(entry);
		// Lists first, while their hosts still read as parked: the row pool's destroys then address the parked page
		// (QtHostListAdapter.PageTarget), as ClearDg does, instead of the top page where the ids are unknown.
		_collection.TearDownListsForHosts(entry.Hosts.Select(h => h.Id).ToHashSet());
		foreach (var host in entry.Hosts)
			_parkedHosts.Remove(host);
		if (pageAlive)
			DestroyHosts(entry.Hosts, entry.ModelPageId);
		else
			foreach (var host in entry.Hosts)
				ReleaseHost(host);
		PageCacheDrops++;
		QtHostDiag.Trace(QtHostDiagChannel.Navigation,
			$"page cache: dropped '{TitleOf(entry.Page)}' from '{entry.ModelPageId}' ({entry.Hosts.Count} hosts) — {reason}");
	}

	private static bool IsWithin(Element element, Page page)
	{
		for (var e = element; e is not null; e = e.Parent)
			if (ReferenceEquals(e, page))
				return true;
		return false;
	}

	private static bool IsSynthetic(NativeElementHost host) => host.Id.StartsWith("synth-", StringComparison.Ordinal);
}
