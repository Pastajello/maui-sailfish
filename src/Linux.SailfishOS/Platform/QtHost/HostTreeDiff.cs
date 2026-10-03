namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// One op batch that brings the native host tree to the managed one, shared by the page reconcile and a container's
/// subtree reconcile. It keeps the QML child order every host had before the batch (the basis of "order" ops), emits
/// the destroy / reparent / create / order ops and records where each host now sits natively
/// (<see cref="NativeElementHost.AppliedParentId"/>). Each caller decides the sequence (the page reconcile reparents
/// every survivor before it creates; a subtree interleaves them in pre-order) and does its own registry work
/// (releasing handles, the id routes, the page's host list) with <see cref="Destroyed"/> and <see cref="Created"/>.
/// </summary>
internal sealed class HostTreeDiff
{
	// QML child ids per parent id ("" = the page canvas), as the native tree holds them before this batch.
	private readonly Dictionary<string, List<string>> _children = new(StringComparer.Ordinal);

	/// <param name="current">The hosts live natively, in QML (pre-)order.</param>
	public HostTreeDiff(IEnumerable<NativeElementHost> current)
	{
		foreach (var host in current)
			Siblings(host.AppliedParentId ?? string.Empty).Add(host.Id);
	}

	/// <summary>The ops in the order they were added.</summary>
	public List<Dictionary<string, object?>> Ops { get; } = new();

	/// <summary>Hosts destroyed in this batch, in op order.</summary>
	public List<NativeElementHost> Destroyed { get; } = new();

	/// <summary>Hosts created in this batch, in op order.</summary>
	public List<NativeElementHost> Created { get; } = new();

	/// <summary>The parent id a host belongs under: its nearest hosted ancestor, "" for the page canvas.</summary>
	public static string ParentKey(NativeElementHost? parent) => parent?.Id ?? string.Empty;

	/// <summary>Destroys <paramref name="host"/>. Callers pass descendants before ancestors, so no op addresses an item
	/// its parent's teardown already took.</summary>
	public void Destroy(NativeElementHost host)
	{
		Ops.Add(BridgeOps.Destroy(host.Id));
		if (_children.TryGetValue(host.AppliedParentId ?? string.Empty, out var siblings))
			siblings.Remove(host.Id);
		host.AppliedParentId = null;
		Destroyed.Add(host);
	}

	/// <summary>Re-attaches a surviving host under the parent it now belongs to; its QML object (and its focus,
	/// scroll and animation state) is kept. False when it already sits there.</summary>
	public bool Reparent(NativeElementHost host)
	{
		var want = ParentKey(host.Parent);
		var have = host.AppliedParentId ?? string.Empty;
		if (want == have)
			return false;
		Ops.Add(BridgeOps.Reparent(host.Id, want));
		if (_children.TryGetValue(have, out var oldSiblings))
			oldSiblings.Remove(host.Id);
		Siblings(want).Add(host.Id);
		host.AppliedParentId = want;
		return true;
	}

	/// <summary>Creates <paramref name="host"/> as the last child of its parent.</summary>
	/// <param name="createOp">Builds the create op from the host, its property snapshot and its parent id.</param>
	public void Create(NativeElementHost host, Dictionary<string, object?> props,
		Func<NativeElementHost, Dictionary<string, object?>, string, Dictionary<string, object?>> createOp)
	{
		var parent = ParentKey(host.Parent);
		Ops.Add(createOp(host, props, parent));
		host.AppliedParentId = parent;
		Siblings(parent).Add(host.Id);
		Created.Add(host);
	}

	/// <summary>
	/// Where appending could not reproduce a parent's desired child order, an "order" op re-attaches the same QObjects
	/// in sequence (child order is the stacking order among siblings). <paramref name="desired"/> is in pre-order.
	/// True when any order op was added.
	/// </summary>
	public bool Order(IEnumerable<NativeElementHost> desired)
	{
		var reordered = false;
		foreach (var group in desired.GroupBy(h => ParentKey(h.Parent)))
		{
			var want = group.Select(h => h.Id).ToList();
			var have = _children.TryGetValue(group.Key, out var list) ? list : new List<string>();
			if (have.SequenceEqual(want))
				continue;
			Ops.Add(BridgeOps.Order(group.Key, want));
			reordered = true;
		}
		return reordered;
	}

	private List<string> Siblings(string parent)
	{
		if (!_children.TryGetValue(parent, out var list))
			_children[parent] = list = new List<string>();
		return list;
	}
}
