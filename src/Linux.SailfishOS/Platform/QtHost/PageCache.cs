using Microsoft.Maui.Controls;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>What the page cache needs from the renderer (C4, W3.7b): the native stack, the release path, which pages
/// the app still holds, the live host set and the lists.</summary>
internal interface IPageCacheOwner
{
	IReadOnlyList<string> NativePageIds { get; }
	string? TopNativePageId { get; }
	bool PageHeld(Page page);
	void ReleaseHosts(IReadOnlyList<NativeElementHost> hosts, string? pageId, bool sendOps);
	/// <summary>A restored page's hosts join the live set.</summary>
	void TakeBackLive(IReadOnlyList<NativeElementHost> hosts);
	QtHostCollectionBridge Collection { get; }
}

/// <summary>
/// The page cache (back cache): the QML hosts of pages the app still holds but does not show stay alive, keyed by the
/// MAUI page, so returning to one reveals it instead of rebuilding it. As on the other platforms each navigation
/// container keeps its pages (a FragmentManager's back stack, a UINavigationController per tab): a page parked under a
/// push waits below the pushed page; a tab, Shell section or FlyoutPage detail switched away from waits hidden on the
/// same model page. The QML objects live on Silica model pages only the renderer knows, so the renderer decides when
/// to park and restore (QtHostPageRenderer.PageCache.cs); the container handlers decide which pages are shown and which
/// still belong to the app. Extracted from the renderer behind <see cref="IPageCacheOwner"/> (W3.7b).
/// </summary>
internal sealed class PageCache(IPageCacheOwner owner)
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

	/// <summary>Whether the host belongs to a parked page.</summary>
	public bool IsParked(NativeElementHost host) => _parkedHosts.Contains(host);

	/// <summary>Host ids of parked pages (diagnostics: deliberate retention, not a QML host leak).</summary>
	public IReadOnlyCollection<string> ParkedHostIds => _parkedHosts.Select(h => h.Id).ToList();

	/// <summary>Pages parked now, and the cache's restores and drops (diagnostics).</summary>
	public int ParkedPages => _parked.Count;
	public long Restores { get; private set; }
	public long Drops { get; private set; }

	/// <summary>The live hosts plus the parked ones, which keep their lists (the collection bridge's desired set).</summary>
	public List<NativeElementHost> WithParked(List<NativeElementHost> hosts) =>
		_parkedHosts.Count == 0 ? hosts : hosts.Concat(_parkedHosts).ToList();

	/// <summary>Moves the most recent use to the page parked as (<paramref name="page"/>, <paramref name="modelPageId"/>),
	/// so parking another page does not evict it.</summary>
	public void Touch(Page page, string modelPageId)
	{
		if (_parked.FirstOrDefault(e => ReferenceEquals(e.Page, page) && e.ModelPageId == modelPageId) is { } entry)
			entry.LastUsed = ++_parkClock;
	}

	/// <summary>Hides the pages parked on <paramref name="modelPageId"/> that still show (a pop revealed them).</summary>
	public void HideShownOn(string modelPageId)
	{
		foreach (var entry in _parked)
			if (entry.ModelPageId == modelPageId && !entry.Hidden)
				Hide(entry);
	}

	/// <summary>Whether the page on <paramref name="pageId"/> after a pop is parked there (an animated pop needs its
	/// returned-to page already painted).</summary>
	public bool IsParkedOn(Page? page, string pageId) =>
		page is not null && _parked.Any(e => ReferenceEquals(e.Page, page) && e.ModelPageId == pageId);

	public void Park(Page page, string modelPageId, List<NativeElementHost> hosts, bool hide)
	{
		DropParked(page, keep: hosts);   // one entry per page
		var entry = new ParkedPage { Page = page, ModelPageId = modelPageId, Hosts = hosts, LastUsed = ++_parkClock };
		_parked.Add(entry);
		foreach (var host in hosts)
			_parkedHosts.Add(host);
		if (hide)
			Hide(entry);
		QtHostDiag.Trace(QtHostDiagChannel.Navigation,
			$"page cache: parked '{QtHostPageRenderer.TitleOf(page)}' ({hosts.Count} hosts) on '{modelPageId}'{(hide ? " hidden" : string.Empty)}");
		Prune();
	}

	public bool Restore(Page page, string modelPageId)
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
		owner.TakeBackLive(entry.Hosts);
		owner.Collection.OnPageRestored(entry.Hosts);
		Restores++;
		QtHostDiag.Trace(QtHostDiagChannel.Navigation,
			$"page cache: restored '{QtHostPageRenderer.TitleOf(page)}' ({entry.Hosts.Count} hosts) on '{modelPageId}'");
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
	/// holds (popped, a replaced Detail), and the least recently used beyond <see cref="QtHostPageRenderer.PageCacheLimit"/>.</summary>
	public void Prune()
	{
		foreach (var entry in _parked.ToList())
		{
			if (!owner.NativePageIds.Contains(entry.ModelPageId))
				Drop(entry, pageAlive: false, "its model page is gone");
			else if (!owner.PageHeld(entry.Page))
				Drop(entry, pageAlive: true, "the app no longer holds the page");
		}
		while (_parked.Count > QtHostPageRenderer.PageCacheLimit)
			Drop(_parked.MinBy(e => e.LastUsed)!, pageAlive: true, $"over the cache limit ({QtHostPageRenderer.PageCacheLimit})");
	}

	private void DropParked(Page page, List<NativeElementHost> keep)
	{
		if (_parked.FirstOrDefault(e => ReferenceEquals(e.Page, page)) is not { } entry)
			return;
		// Hosts are keyed by element: the page's hosts being parked now may be the same objects.
		entry.Hosts.RemoveAll(keep.Contains);
		Drop(entry, pageAlive: owner.NativePageIds.Contains(entry.ModelPageId), "parked again");
	}

	/// <summary>
	/// A host is on screen or parked, never both: hosts are keyed by element, so a parked page rendered again would get
	/// a second QML object under the same id, and dropping the old entry would destroy the new one (an empty page).
	/// Before the reconcile diffs, every desired host is taken back from the cache: restored when its entry lives on
	/// the model page on screen, else dropped there (its objects are destroyed on that page) so the diff creates it here.
	/// </summary>
	public void Reclaim(List<NativeElementHost> desired)
	{
		if (_parkedHosts.Count == 0)
			return;
		HashSet<NativeElementHost>? wanted = null;
		foreach (var entry in _parked.ToList())
		{
			wanted ??= new HashSet<NativeElementHost>(desired);
			if (!entry.Hosts.Any(wanted.Contains))
				continue;
			if (entry.ModelPageId == owner.TopNativePageId)
				Restore(entry.Page, entry.ModelPageId);
			else
				Drop(entry, pageAlive: owner.NativePageIds.Contains(entry.ModelPageId), "its page is rendered again");
		}
	}

	/// <summary>Drops the whole cache (a full page reset, a rebuilt stack).</summary>
	public void DropAll()
	{
		foreach (var entry in _parked.ToList())
			Drop(entry, pageAlive: owner.NativePageIds.Contains(entry.ModelPageId), "cache reset");
	}

	private void Drop(ParkedPage entry, bool pageAlive, string reason)
	{
		_parked.Remove(entry);
		// Lists first, while their hosts still read as parked: the row pool's destroys then address the parked page
		// (QtHostListAdapter.PageTarget), as ClearDg does, instead of the top page where the ids are unknown.
		owner.Collection.TearDownListsForHosts(entry.Hosts.Select(h => h.Id).ToHashSet());
		foreach (var host in entry.Hosts)
			_parkedHosts.Remove(host);
		owner.ReleaseHosts(entry.Hosts, entry.ModelPageId, sendOps: pageAlive);
		Drops++;
		QtHostDiag.Trace(QtHostDiagChannel.Navigation,
			$"page cache: dropped '{QtHostPageRenderer.TitleOf(entry.Page)}' from '{entry.ModelPageId}' ({entry.Hosts.Count} hosts) — {reason}");
	}
}
