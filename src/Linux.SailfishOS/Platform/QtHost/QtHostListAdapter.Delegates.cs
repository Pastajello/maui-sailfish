using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using static Microsoft.Maui.SailfishOS.Platform.QtHost.QtHostCollectionBridge;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>The native ListView's delegates: which row each one shows, materializing a row's hosts in it (now, or deferred while it waits off screen), and releasing them when Qt recycles the delegate.</summary>
internal sealed partial class QtHostListAdapter
{
	/// <summary>Parks an unresolvable delegate attach for poll retries (deduped by delegate; newest row wins).</summary>
	internal void QueueAttachRetry(int rowIndex, string dgObj) => _bridge.ParkAttach(this, rowIndex, dgObj);

	internal void DropAttachRetries(string dgObj) => _bridge.CancelAttach(this, dgObj);

	/// <summary>Materializes the row delegates currently in the QML visual tree (idempotent). Needed because
	/// Qt 5.6 refills from its delegate cache without onCompleted or rebind events.</summary>
	internal void ResyncDelegates()
	{
		if (!Host.IsAttached)
			return;
		var prefix = DelegatePrefix;
		// Walk the list's own page: a back-cached page's list is not under currentPage.
		var pageJs = PageId.Length > 0
			? QmlPage.ByIdOr(PageId, "pageStack.currentPage")
			: "pageStack.currentPage";
		// Delegates are direct children of the list's contentItem (a PathView carousel: of the view itself), so
		// one level there is enough; the whole-page walk is only the fallback when the list host is not found.
		var js = "(function(){var p='" + prefix + "',pg=" + pageJs + ",a=[];" +
			"var h=pg&&pg.__hosts?pg.__hosts['" + Host.Id + "']:null,v=h&&h.item;" +
			"if(v){var k=(v.contentItem||v).children;" +
			"for(var i=0;i<k.length;++i){var nm=k[i].objectName;if(nm&&(''+nm).indexOf(p)===0)a.push(''+nm);}" +
			"return a.join(',');}" +
			"function N(o){var nm=o.objectName;" +
			"if(nm&&(''+nm).indexOf(p)===0)a.push(''+nm);" +
			"var c=o.children;if(c)for(var j=0;j<c.length;++j)N(c[j]);}" +
			"if(pg)N(pg);return a.join(',');})()";
		string names;
		try
		{
			names = QtHostRuntime.Eval(js);
		}
		catch (Exception ex)
		{
			QtHostDiag.Error(QtHostDiagChannel.QmlSignal, $"collection resync eval failed: {ex.Message}");
			return;
		}
		if (string.IsNullOrEmpty(names))
			return;
		foreach (var name in names.Split(','))
		{
			if (name.Length <= prefix.Length || !name.StartsWith(prefix, StringComparison.Ordinal))
				continue;
			if (!int.TryParse(name.AsSpan(prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var row))
				continue;
			RequestMaterialize(row, name);
		}
	}

	/// <summary>A delegate for <paramref name="rowIndex"/> appeared or rebound: materialize it now when it is on
	/// (or next to) the screen, otherwise queue it for the budgeted drain.</summary>
	internal void RequestMaterialize(int rowIndex, string dgObj)
	{
		if (!ShouldDefer(rowIndex) ||
		    (Delegates.TryGetValue(dgObj, out var known) && known.Row is { } r && rowIndex < Rows.Count &&
		     ReferenceEquals(r, Rows[rowIndex]) && known.Children.Count > 0))
		{
			MaterializeRow(rowIndex, dgObj);   // on screen, or an already built row that only needs placing
			return;
		}
		if (!DeferredRows.Contains(rowIndex))
			DeferredRows.Add(rowIndex);
		_bridge.SchedulePending();
	}

	private bool ShouldDefer(int rowIndex)
	{
		if (!DeferOffscreenRows || Horizontal || Carousel || rowIndex < 0 || rowIndex >= Rows.Count)
			return false;
		var viewport = Host.MauiLogicalBounds.Height;
		if (viewport <= 0)
			return false;
		var (top, bottom) = RowExtentDp(rowIndex);
		var offset = Math.Max(0, LastReportedYDp);
		var margin = viewport * DeferMarginViewports;
		return bottom < offset - margin || top > offset + viewport + margin;
	}

	private (double Top, double Bottom) RowExtentDp(int rowIndex)
	{
		if (_rowTopsDp is null || _rowTopsDp.Length != Rows.Count)
		{
			// The same arithmetic as ListView.qml's __reportScroll: rows start below the header slot.
			_rowTopsDp = new double[Rows.Count];
			var y = Slots.TryGetValue("header", out var header) ? header.ExtentDp : 0;
			for (var i = 0; i < Rows.Count; i++)
			{
				_rowTopsDp[i] = y;
				y += Rows[i].HeightDp + SpacingDp;
			}
		}
		return (_rowTopsDp[rowIndex], _rowTopsDp[rowIndex] + Rows[rowIndex].HeightDp);
	}

	/// <summary>Materializes queued off-screen rows nearest to the viewport first until <paramref name="budgetMs"/>
	/// is spent (at least one row per call). True when rows are left for a later turn.</summary>
	internal bool DrainDeferredRows(System.Diagnostics.Stopwatch clock, double budgetMs)
	{
		if (DeferredRows.Count == 0)
			return false;
		var prefix = DelegatePrefix;
		var viewTop = Math.Max(0, LastReportedYDp);
		var viewBottom = viewTop + Math.Max(0, Host.MauiLogicalBounds.Height);
		do
		{
			var best = 0;
			var bestDistance = double.MaxValue;
			for (var i = 0; i < DeferredRows.Count; i++)
			{
				var row = DeferredRows[i];
				if (row >= Rows.Count)
					continue;
				var (top, bottom) = RowExtentDp(row);
				var distance = bottom < viewTop ? viewTop - bottom : top > viewBottom ? top - viewBottom : 0;
				if (distance < bestDistance)
				{
					bestDistance = distance;
					best = i;
				}
			}
			var next = DeferredRows[best];
			DeferredRows.RemoveAt(best);
			// The delegate is addressed by row: a recycled one was renamed; a released one is simply gone.
			if (next < Rows.Count)
				MaterializeRow(next, prefix + next.ToString(CultureInfo.InvariantCulture), retryIfMissing: false);
		}
		while (DeferredRows.Count > 0 && clock.Elapsed.TotalMilliseconds < budgetMs);
		return DeferredRows.Count > 0;
	}

	internal void MaterializeRow(int rowIndex, string dgObj, bool retryIfMissing = true)
	{
		_bridge.MarkSceneDirty();
		if (rowIndex < 0 || rowIndex >= Rows.Count || dgObj.Length == 0)
			return;
		// The list's visual tree first: after a jump (ScrollTo) a released delegate can still carry the same name while
		// Qt deletes it later, and a global name search may return it; the content would die with it. Released
		// delegates are unparented from the list, so the scoped search finds the live one.
		var handle = QtHostRuntime.FindScoped(dgObj, Host.NativeHandle);   // a parked page's delegates: by name
		if (handle == 0 && !retryIfMissing)
			return;   // a deferred row whose delegate Qt released meanwhile: nothing to build
		if (handle == 0)
		{
			// Hidden-page delegates are parked unparented by Qt 5.6; retry from the poll loop.
			QueueAttachRetry(rowIndex, dgObj);
			return;
		}
		DropAttachRetries(dgObj);   // resolved: cancel any parked retry

		DgState dg;
		if (ByHandle.TryGetValue(handle, out var recycled))
		{
			dg = recycled;
			if (dg.Obj != dgObj)
			{
				// The recycled delegate was renamed for its new row: re-key the name index. Renames cascade on an
				// insert (r0→r1, r1→r2, …), so the old name may already be the next delegate's: drop it only while it
				// is still this one's. A delegate not renamed yet may still hold the new name; it re-keys itself on
				// its own rebind, and ByHandle keeps it meanwhile (the name index is only a lookup by name).
				if (Delegates.TryGetValue(dg.Obj, out var holder) && ReferenceEquals(holder, dg))
					Delegates.Remove(dg.Obj);
				dg.Obj = dgObj;
				Delegates[dgObj] = dg;
			}
			// Compare Row objects, not indices: after a rebuild a silently reused delegate must move to the new
			// cell views, or it paints the old ones' stale bounds.
			if (ReferenceEquals(dg.Row, Rows[rowIndex]) && dg.Children.Count > 0 && ChildrenAlive(dg))
			{
				QtHostDiag.Trace(QtHostDiagChannel.QmlObject,
					$"list row {rowIndex} dg '{dgObj}' early-return (children={dg.Children.Count})");
				UpdateDgGeometry(dg);   // re-attach of the same row: place only
				return;
			}
			QtHostDiag.Trace(QtHostDiagChannel.QmlObject,
				$"list row {rowIndex} dg '{dgObj}' rebind — clearing children={dg.Children.Count} prevRow={dg.Row?.Index.ToString() ?? "-"}");
			if (ReferenceEquals(dg.Row, Rows[rowIndex]) && dg.Children.Count > 0)
				_counters.SameRowRebuilds++;   // the same row built again: its hosts died, or it only looked dead
			ClearDg(dg);                       // rebind: drop the previous row's hosts
		}
		else
		{
			if (ByHandle.Count >= MaxDelegates)
			{
				QtHostDiag.Warn(QtHostDiagChannel.QmlObject, $"collection delegate cap ({MaxDelegates}) reached — row {rowIndex} not materialized");
				return;
			}
			// A different delegate may still hold the name: a dead one is dropped; a live one is only waiting for its
			// rename (an insert shifts every row) and keeps its content.
			if (Delegates.TryGetValue(dgObj, out var stale) && !QtHostRuntime.TryItemGeometry(stale.Handle, out _))
			{
				ByHandle.Remove(stale.Handle);
				ClearDg(stale);
			}
			dg = new DgState { Obj = dgObj, Handle = handle };
			Delegates[dgObj] = dg;
			ByHandle[handle] = dg;
			QtHostDiag.Trace(QtHostDiagChannel.QmlObject,
				$"list row {rowIndex} dg '{dgObj}' fresh delegate (registry={ByHandle.Count})");
		}

		var row = Rows[rowIndex];
		dg.Row = row;
		row.DgObj = dgObj;

		var desired = new List<NativeElementHost>();
		var props = new Dictionary<NativeElementHost, Dictionary<string, object?>>();
		for (var c = 0; c < row.CellViews.Count; c++)
		{
			var cellView = row.CellViews[c];
			if (cellView is null)
				continue;
			_renderer.MapItemSubtree(cellView, desired, props);
			_bridge.CollectHosts(cellView, dg.Children);
			dg.Cells.Add((cellView, row.CellX[c]));
		}
		if (!TryAdoptPooledRow(dg, desired, props))
			CreateAndAttachHosts(desired, props, dgObj, dg.Handle);
		dg.Own.Clear();
		dg.Own.AddRange(desired);
		PageId = ListPageId();
		QtHostDiag.Trace(QtHostDiagChannel.QmlObject,
			$"list row {rowIndex} dg '{dgObj}' materialized desired={desired.Count} children={dg.Children.Count} ids=[{string.Join(",", dg.Children.Select(h => h.Id))}]");
		_counters.ItemsMaterialized += row.CellViews.Count;
		UpdateDgGeometry(dg);
	}

	/// <summary>Creates the hosts as QML children of the placeholder and wires them like page hosts. The
	/// batch targets the list's own page, which for a back-cached page is not the top one.</summary>
	internal void CreateAndAttachHosts(List<NativeElementHost> desired,
	                                  Dictionary<NativeElementHost, Dictionary<string, object?>> props,
	                                  string parentObj, long placeholderHandle)
	{
		_bridge.MarkSceneDirty();
		if (desired.Count == 0)
			return;
		var ops = new List<Dictionary<string, object?>>(desired.Count);
		foreach (var host in desired)
			ops.Add(QtHostPageRenderer.CreateChildOp(host, props.TryGetValue(host, out var p) ? p : new(), parentObj));
		_renderer.ApplyOps(ops, PageTarget);
		foreach (var host in desired)
		{
			QtHostPageRenderer.AttachHost(host, props.TryGetValue(host, out var p) ? p : new(), placeholderHandle);
			_renderer.RegisterRoute(host.Id, host);
			// QML resolved parentObj by name, and a recycling ListView can briefly have two delegates with that
			// name, so pin the subtree root to the placeholder resolved by handle.
			if (host.Parent is null && host.IsAttached && placeholderHandle != 0 &&
			    !QtHostRuntime.SetParentItem(host.NativeHandle, placeholderHandle))
				QtHostDiag.Warn(QtHostDiagChannel.QmlObject,
					$"row root {host} could not be pinned to its placeholder '{parentObj}': {QtHostRuntime.LastErrorText}");
		}
	}

	/// <summary>Drops a delegate's row content but keeps its registry entry; the QML delegate stays alive.</summary>
	internal void ClearDg(DgState dg)
	{
		dg.Own.Clear();
		_bridge.DestroyHosts(dg.Children, PageId);
		dg.Cells.Clear();
		if (dg.Row is not null && dg.Row.DgObj == dg.Obj)
			dg.Row.DgObj = null;
		dg.Row = null;
	}

	/// <summary>Maps the row (or slot) holding <paramref name="element"/> again, e.g. when a flattened layout starts
	/// painting and needs its own host (flat rows). False when this list does not hold it.</summary>
	internal bool RemapRowContaining(Element element)
	{
		foreach (var dg in ByHandle.Values.ToList())
		{
			if (dg.Row is not { } row || !dg.Cells.Any(c => ElementTree.IsWithin(element, c.Root)))
				continue;
			var index = row.Index;
			ClearDg(dg);
			MaterializeRow(index, dg.Obj);
			return true;
		}
		foreach (var slot in Slots.Values)
			if (slot.Root is { } root && ElementTree.IsWithin(element, root))
			{
				MarkSlot(slot, remap: true);
				return true;
			}
		return false;
	}

	/// <summary>A delegate removed from the model handed its "__r" name to its replacement (ListView.qml
	/// onRemove): its content stays under the released name until the delegate's own detach.</summary>
	internal void ReleaseDg(string dgObj, string releasedObj)
	{
		if (releasedObj.Length == 0 || !Delegates.Remove(dgObj, out var dg))
			return;
		if (dg.Row is { } row && row.DgObj == dgObj)
			row.DgObj = null;
		dg.Obj = releasedObj;
		Delegates[releasedObj] = dg;
	}

	internal void UnmaterializeDg(DgState dg)
	{
		// The delegate is gone; its row subtree lives on (canvas-owned) and waits in the pool, or is destroyed.
		if (TryPoolRow(dg))
		{
			dg.Cells.Clear();
			if (dg.Row is not null && dg.Row.DgObj == dg.Obj)
				dg.Row.DgObj = null;
			dg.Row = null;
		}
		else
			ClearDg(dg);
		if (Delegates.TryGetValue(dg.Obj, out var holder) && ReferenceEquals(holder, dg))
			Delegates.Remove(dg.Obj);
		ByHandle.Remove(dg.Handle);
	}

	/// <summary>The model page a list's row hosts live on: the top page, except for a back-cached page's list.</summary>
	internal string ListPageId() => PageTarget ?? _bridge.MirrorTop();

	/// <summary>The model-page instance this list's row and slot ops go to (W3.2: one resolution): the page it was
	/// created on while that page is parked, else null = the top model page.</summary>
	internal string? PageTarget => _renderer.IsParked(Host) && PageId.Length > 0 ? PageId : null;

	internal void UpdateDgGeometry(DgState dg)
	{
		if (dg.Handle == 0 || dg.Children.Count == 0)
			return;
		var hosts = new HashSet<NativeElementHost>(dg.Children);
		foreach (var (root, cellX) in dg.Cells)
			_renderer.PushItemGeometry(root, cellX, hosts, crossAlongY: Horizontal);
	}
}
