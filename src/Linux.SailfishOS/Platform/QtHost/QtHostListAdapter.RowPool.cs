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
	internal static readonly bool RowPoolEnabled = Environment.GetEnvironmentVariable("MAUI_SAILFISH_ROW_POOL") != "0";

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
	/// property state seeds the diff, and the root moves into <paramref name="dg"/>'s delegate. False (nothing done)
	/// when no pooled row fits; the caller creates the hosts then.
	/// </summary>
	private bool TryAdoptPooledRow(DgState dg, List<NativeElementHost> desired,
	                               Dictionary<NativeElementHost, Dictionary<string, object?>> props)
	{
		var hosts = desired;
		if (_pooledRows == 0 || hosts.Count == 0 || hosts.Any(h => h.IsAttached) ||
		    Shape(hosts) is not { } shape || !_rowPool.TryGetValue(shape, out var stack))
			return false;
		while (stack.Count > 0)
		{
			var pooled = stack.Pop();
			_pooledRows--;
			if (!QtHostRuntime.TryItemGeometry(pooled[0].Handle, out _) || !Compatible(pooled, hosts, props))
			{
				DestroyPooled(pooled);
				continue;
			}
			var ops = new List<Dictionary<string, object?>>(hosts.Count);
			for (var i = 0; i < hosts.Count; i++)
				ops.Add(new Dictionary<string, object?>
				{
					["op"] = "rekey", ["from"] = pooled[i].Id, ["to"] = hosts[i].Id, ["parent"] = hosts[i].Parent?.Id ?? string.Empty,
				});
			_renderer.ApplyOps(ops, PageTarget());
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
		return false;
	}

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
		_renderer.IsParked(Host) && PageId.Length > 0 ? QmlPage.ById(PageId) : null;

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
