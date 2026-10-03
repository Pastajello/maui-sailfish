using static Microsoft.Maui.SailfishOS.Platform.QtHost.QtHostCollectionBridge;


namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// The row pool, the recycling Android's RecyclerView does with view holders: Qt 5.6's ListView destroys a delegate that
/// leaves its cache buffer, and the row's QML subtree used to die with it, so every row that scrolled in created all its
/// hosts again (~1 ms each). A row root is owned by the page canvas instead (MauiModelPage.__createHost), so its
/// subtree outlives the delegate; a detached row of plain adapters waits here, and the next row with the same shape
/// (adapter per host, parent per host) takes it over: each host gets the new element's id ("rekey"), the old property
/// state seeds the diff and only what differs is pushed. MAUI_SAILFISH_ROW_POOL=0 turns it off (A/B).
/// </summary>
internal sealed partial class QtHostListAdapter
{
	internal static bool RowPoolEnabled { get; set; } = SailfishEnv.Get("MAUI_SAILFISH_ROW_POOL") != "0";

	/// <summary>Detached rows kept per list; more are destroyed. Two viewports of catalog cards.</summary>
	private const int RowPoolCap = 48;

	/// <summary>Adapters whose whole state is their properties and geometry, so a host can change rows. Text input,
	/// lists, scrollers, surfaces and anything with native-side state are rebuilt instead.</summary>
	private static readonly HashSet<string> PoolableUris = new(StringComparer.Ordinal)
	{
		"label", "image", "border", "grid", "stack-layout", "content-view", "button", "shape", "activity-indicator",
	};

	/// <summary>Properties pushed after the create (transform, decode size, letter spacing): a pooled host may carry them
	/// while the next row's snapshot does not, and their own passes reset them.</summary>
	private static readonly HashSet<string> LateKeys = new(StringComparer.Ordinal)
	{
		"rotation", "scale", "transformOrigin", "mauiMatrix", "mauiDecodeW", "mauiDecodeH", "mauiLetterSpacing",
	};

	private sealed class PooledHost
	{
		public required string Id;
		public required long Handle;
		public required Dictionary<string, string> Applied;
	}

	private readonly Dictionary<string, Stack<List<PooledHost>>> _rowPool = new(StringComparer.Ordinal);
	private int _pooledRows;

	/// <summary>Host ids held by the pool: still registered in the page's QML, so the stray sweep must keep them.</summary>
	internal IEnumerable<string> PooledHostIds =>
		_rowPool.Values.SelectMany(stack => stack).SelectMany(row => row).Select(h => h.Id);

	/// <summary>The row's shape: adapter and parent position of each host, in pre-order. Null when a host may not move.</summary>
	private static string? Shape(IReadOnlyList<NativeElementHost> hosts)
	{
		if (hosts.Count == 0)
			return null;
		var index = new Dictionary<NativeElementHost, int>(hosts.Count, ReferenceEqualityComparer.Instance);
		var sb = new System.Text.StringBuilder();
		for (var i = 0; i < hosts.Count; i++)
		{
			var host = hosts[i];
			if (!PoolableUris.Contains(host.QmlUri))
				return null;
			var parent = host.Parent is { } p && index.TryGetValue(p, out var at) ? at : -1;
			if (host.Parent is not null && parent < 0 && i > 0)
				return null;   // a parent outside the row
			index[host] = i;
			sb.Append(host.QmlUri).Append(':').Append(parent).Append('|');
		}
		return sb.ToString();
	}

	/// <summary>Keeps a detached row's live subtree for the next row of its shape; false when it has to be destroyed
	/// (pool off or full, a host that may not move, the subtree already dead).</summary>
	private bool TryPoolRow(DgState dg)
	{
		if (!RowPoolEnabled || _pooledRows >= RowPoolCap || dg.Own.Count == 0 || _renderer.IsParked(Host))
			return false;
		var hosts = dg.Own;
		if (Shape(hosts) is not { } shape || !hosts.All(h => h.IsAttached) ||
		    !QtHostRuntime.TryItemGeometry(hosts[0].NativeHandle, out _))
			return false;
		var pooled = new List<PooledHost>(hosts.Count);
		foreach (var host in hosts)
		{
			pooled.Add(new PooledHost
			{
				Id = host.Id,
				Handle = host.NativeHandle,
				Applied = new Dictionary<string, string>(host.AppliedProperties, StringComparer.Ordinal),
			});
			_renderer.ReleaseToPool(host);
		}
		if (!_rowPool.TryGetValue(shape, out var stack))
			_rowPool[shape] = stack = new Stack<List<PooledHost>>();
		stack.Push(pooled);
		_pooledRows++;
		_bridge.RowsPooled++;
		// What the row only showed (a shared stroke shape) is released as before; its own hosts left with the pool.
		var shown = dg.Children.Where(h => !hosts.Contains(h)).ToList();
		hosts.Clear();
		dg.Children.Clear();
		_bridge.DestroyHosts(shown, PageId);
		return true;
	}

	/// <summary>
	/// Hands a pooled subtree of the same shape to a row being materialized: the hosts take the new ids, the old
	/// property state seeds the diff, and the root moves into <paramref name="dg"/>'s delegate. False (nothing adopted)
	/// when no pooled row fits; the caller creates the hosts then.
	/// </summary>
	private bool TryAdoptPooledRow(DgState dg, List<NativeElementHost> desired,
	                               Dictionary<NativeElementHost, Dictionary<string, object?>> props)
	{
		var hosts = desired;
		if (_pooledRows == 0 || hosts.Count == 0 || hosts.Any(h => h.IsAttached) || Shape(hosts) is not { } shape)
			return false;
		// A row that scrolled out and back finds its own former subtree in the pool, still registered under its ids:
		// it is adopted as it is. Any other pooled row holding one of those ids would make MauiModelPage refuse the
		// rekey onto them, so it goes first.
		if (TakePooledHolding(hosts.Select(h => h.Id).ToList()) is { } own)
		{
			if (Shape(hosts) == shape && IsAlive(own) && Compatible(own, hosts, props))
				return Adopt(dg, hosts, own, props, rekey: false);
			DestroyPooled(own);
		}
		if (!_rowPool.TryGetValue(shape, out var stack))
			return false;
		while (stack.Count > 0)
		{
			var pooled = stack.Pop();
			_pooledRows--;
			if (!IsAlive(pooled) || !Compatible(pooled, hosts, props))
			{
				DestroyPooled(pooled);
				continue;
			}
			return Adopt(dg, hosts, pooled, props, rekey: true);
		}
		return false;
	}

	private bool Adopt(DgState dg, List<NativeElementHost> hosts, List<PooledHost> pooled,
	                   Dictionary<NativeElementHost, Dictionary<string, object?>> props, bool rekey)
	{
		if (rekey)
		{
			var ops = new List<Dictionary<string, object?>>(hosts.Count);
			for (var i = 0; i < hosts.Count; i++)
				ops.Add(new Dictionary<string, object?>
				{
					["op"] = "rekey", ["from"] = pooled[i].Id, ["to"] = hosts[i].Id, ["parent"] = hosts[i].Parent?.Id ?? string.Empty,
				});
			var refused = _renderer.ApplyOps(ops, PageTarget());
			if (refused != 0)
			{
				// Some objects kept their old id: binding them would drive objects the page knows by another name. Both
				// names go, and the caller creates the row.
				QtHostDiag.Warn(QtHostDiagChannel.QmlObject,
					$"pooled row rekey refused ({refused} of {hosts.Count}) for [{string.Join(",", hosts.Select(h => h.Id))}] — creating the row");
				_renderer.ApplyOps(hosts.Select(h => BridgeOps.Destroy(h.Id)).ToList(), PageTarget());
				DestroyPooled(pooled);
				_bridge.RowRekeysRefused++;
				return false;
			}
		}
		for (var i = 0; i < hosts.Count; i++)
		{
			_renderer.AdoptPooledHost(hosts[i], pooled[i].Handle, pooled[i].Applied,
				props.TryGetValue(hosts[i], out var p) ? p : new());
			_renderer.RegisterRoute(hosts[i].Id, hosts[i]);
		}
		// Every cell root of the row (a grid row holds several) joins the delegate; nested hosts ride their parents.
		foreach (var root in hosts.Where(h => h.Parent is null))
			if (!QtHostRuntime.SetParentItem(root.NativeHandle, dg.Handle))
				QtHostDiag.Warn(QtHostDiagChannel.QmlObject, $"pooled row root {root} could not join '{dg.Obj}': {QtHostRuntime.LastErrorText}");
		_bridge.RowsAdopted++;
		return true;
	}

	/// <summary>Takes every pooled row holding one of <paramref name="ids"/> out of the pool: returns the one holding
	/// exactly them, in order (the row's own former subtree), and destroys the others.</summary>
	private List<PooledHost>? TakePooledHolding(IReadOnlyList<string> ids)
	{
		var wanted = new HashSet<string>(ids, StringComparer.Ordinal);
		List<PooledHost>? own = null;
		foreach (var (shape, stack) in _rowPool.ToList())
		{
			if (!stack.Any(row => row.Any(h => wanted.Contains(h.Id))))
				continue;
			var keep = new List<List<PooledHost>>();
			foreach (var row in stack.Reverse())   // bottom → top, so the rebuilt stack keeps its order
			{
				if (!row.Any(h => wanted.Contains(h.Id)))
				{
					keep.Add(row);
					continue;
				}
				_pooledRows--;
				if (own is null && row.Select(h => h.Id).SequenceEqual(ids, StringComparer.Ordinal))
					own = row;
				else
					DestroyPooled(row);
			}
			_rowPool[shape] = new Stack<List<PooledHost>>(keep);
		}
		return own;
	}

	/// <summary>The pooled row's root object is alive and still the one its id names (a handle can outlive its object
	/// and be reused by Qt for another).</summary>
	private static bool IsAlive(List<PooledHost> pooled) =>
		QtHostRuntime.TryItemGeometry(pooled[0].Handle, out _) &&
		QtHostRuntime.GetProperty(pooled[0].Handle, "objectName") == "maui_" + pooled[0].Id;

	/// <summary>The pooled host state must cover the new snapshot's keys and hold nothing else but late keys.</summary>
	private static bool Compatible(List<PooledHost> pooled, List<NativeElementHost> hosts,
	                               Dictionary<NativeElementHost, Dictionary<string, object?>> props)
	{
		for (var i = 0; i < hosts.Count; i++)
		{
			if (!props.TryGetValue(hosts[i], out var p))
				return false;
			foreach (var key in pooled[i].Applied.Keys)
				if (!p.ContainsKey(key) && !LateKeys.Contains(key))
					return false;
		}
		return true;
	}

	private string? PageTarget() =>
		_renderer.IsParked(Host) && PageId.Length > 0 ? PageId : null;

	private void DestroyPooled(List<PooledHost> pooled)
	{
		_renderer.DestroyPooledHosts(pooled.Select(h => (h.Id, h.Handle)).Reverse().ToList(), PageTarget());
	}

	/// <summary>Destroys every pooled row (the list is retired).</summary>
	private void DrainRowPool()
	{
		foreach (var stack in _rowPool.Values)
			while (stack.Count > 0)
				DestroyPooled(stack.Pop());
		_rowPool.Clear();
		_pooledRows = 0;
	}
}
