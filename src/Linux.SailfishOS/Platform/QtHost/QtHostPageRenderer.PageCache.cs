using Microsoft.Maui.Controls;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

// When pages go into and come back out of the page cache (PageCache.cs): parked under a push, swapped on a tab/section/
// detail switch, revealed by a pop. The page-scoped synthetic hosts never park.
internal sealed partial class QtHostPageRenderer : IPageCacheOwner
{
	private readonly PageCache _pageCache;

	/// <summary>Most pages kept at once (MAUI_SAILFISH_PAGE_CACHE, default 4); the least recently used is rebuilt
	/// on return instead.</summary>
	internal static int PageCacheLimit { get; set; } = Math.Max(1, SailfishEnv.Int("MAUI_SAILFISH_PAGE_CACHE") ?? 4);

	/// <summary>Whether the host belongs to a parked page.</summary>
	internal bool IsParked(NativeElementHost host) => _pageCache.IsParked(host);

	/// <summary>Host ids of parked pages (diagnostics: deliberate retention, not a QML host leak).</summary>
	internal IReadOnlyCollection<string> ParkedHostIds => _pageCache.ParkedHostIds;

	/// <summary>Pages parked now, and the cache's restores and drops (diagnostics).</summary>
	public int ParkedPages => _pageCache.ParkedPages;
	public long PageCacheRestores => _pageCache.Restores;
	public long PageCacheDrops => _pageCache.Drops;

	IReadOnlyList<string> IPageCacheOwner.NativePageIds => _nativePageIds;
	string? IPageCacheOwner.TopNativePageId => NativeTopPageId;
	bool IPageCacheOwner.PageHeld(Page page) => PageHeld(page);
	void IPageCacheOwner.ReleaseHosts(IReadOnlyList<NativeElementHost> hosts, string? pageId, bool sendOps) =>
		ReleaseHosts(hosts, pageId, sendOps);
	void IPageCacheOwner.TakeBackLive(IReadOnlyList<NativeElementHost> hosts) => _current.AddRange(hosts);
	QtHostCollectionBridge IPageCacheOwner.Collection => _collection;

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
		ReleaseHosts(synthetic, pageId, sendOps: true);   // also empties the slot fields
		var hosts = _current.Where(h => !synthetic.Contains(h)).ToList();
		_current.Clear();
		_byId.Clear();
		if (_rendered is { } page && hosts.Count > 0)
			_pageCache.Park(page, pageId, hosts, hide: false);
		else
			ReleaseHosts(hosts, pageId, sendOps: true);
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
		_pageCache.Touch(next, top);
		if (previous is not null && !ReferenceEquals(previous, next) && PageHeld(previous) &&
		    _current.Count > 0 && _current.Any(h => !IsSynthetic(h) && IsWithin(h.Element, previous)))
		{
			// Page-scoped synthetic hosts stay: the diff swaps them for the new page's.
			var hosts = _current.Where(h => !IsSynthetic(h)).ToList();
			_current.RemoveAll(h => !IsSynthetic(h));
			foreach (var host in hosts)
				_byId.Remove(host.Id);
			_pageCache.Park(previous, top, hosts, hide: true);
		}
		if (!_current.Any(h => !IsSynthetic(h)))
			_pageCache.Restore(next, top);
		// A switch can take pages out of the app (a replaced Detail, a new root): they leave the cache now.
		_pageCache.Prune();
	}

	/// <summary>A pop reveals <paramref name="pageId"/>: its page comes back from the cache when parked there; other
	/// pages parked on it stay parked, hidden (they were covered, now they would show).</summary>
	private bool RestoreRetention(string pageId)
	{
		var restored = ResolveReconcilePage() is { } target && _pageCache.Restore(target, pageId);
		_pageCache.HideShownOn(pageId);
		return restored;
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

	private static bool IsWithin(Element element, Page page) => ElementTree.IsWithin(element, page);

	private List<NativeElementHost> WithParked(List<NativeElementHost> hosts) => _pageCache.WithParked(hosts);

	private static bool IsSynthetic(NativeElementHost host) => IsSyntheticId(host.Id);

	/// <summary>Ids of the page-scoped synthetic hosts (pulleys, context menu, interaction surfaces): one prefix rule.</summary>
	internal const string SyntheticPrefix = "synth-";

	internal static bool IsSyntheticId(string? id) => id is not null && id.StartsWith(SyntheticPrefix, StringComparison.Ordinal);
}
