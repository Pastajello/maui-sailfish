using System.Text.Json;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>What the bridge asks of one list: the hosts it owns, its adapter events, its geometry pass and the
/// diagnostics lookups. The bridge loops over its lists and calls these; the delegate and slot state stays here.</summary>
internal sealed partial class QtHostListAdapter
{
	/// <summary>Every host of the live delegates and slots (the renderer routes their native events by id).</summary>
	public IEnumerable<NativeElementHost> CellHosts
	{
		get
		{
			foreach (var dg in ByHandle.Values)
				foreach (var host in dg.Children)
					yield return host;
			foreach (var slot in Slots.Values)
				foreach (var host in slot.Children)
					yield return host;
		}
	}

	/// <summary>Whether <paramref name="host"/> is one of this list's cell hosts: they die with recycled delegates as
	/// normal virtualization, so the renderer's dead-host burst counter skips them.</summary>
	public bool OwnsCellHost(NativeElementHost host)
	{
		foreach (var dg in ByHandle.Values)
			if (dg.Children.Contains(host))
				return true;
		foreach (var slot in Slots.Values)
			if (slot.Children.Contains(host))
				return true;
		return false;
	}

	/// <summary>Live delegates (materialized rows).</summary>
	public int LiveRowCount => ByHandle.Count;

	/// <summary>Built rows of the model.</summary>
	public int RowCount => Rows.Count;

	/// <summary>Re-lays out every live delegate and slot subtree; geometry is delegate-relative, so this holds off
	/// screen (a parked page's late-attached rows get their size here).</summary>
	public void RefreshGeometry()
	{
		foreach (var dg in ByHandle.Values)
			UpdateDgGeometry(dg);
		foreach (var slot in Slots.Values)
			UpdateSlotGeometry(slot);
	}

	/// <summary>One event of this list's adapter (ListView.qml / CarouselView.qml), already routed by its host id.</summary>
	public void HandleEvent(string name, JsonElement root)
	{
		switch (name)
		{
			case "list-item-attached":
			case "list-item-rebind":
				RequestMaterialize((int)BridgeJson.Num(root, "row", -1), Text(root, "dg"));
				break;
			case "list-item-detached":
				if (Delegates.TryGetValue(Text(root, "dg"), out var dg))
					UnmaterializeDg(dg);
				break;
			case "list-item-released":
				ReleaseDg(Text(root, "dg"), Text(root, "to"));
				break;
			case "list-item-tapped":
				OnRowTapped((int)BridgeJson.Num(root, "row", -1), BridgeJson.Int(root, "cell", 0),
					BridgeJson.Num(root, "x", double.NaN), BridgeJson.Num(root, "y", double.NaN));
				break;
			case "list-item-pressed":
				OnRowPressed((int)BridgeJson.Num(root, "row", -1), BridgeJson.Int(root, "cell", 0),
					BridgeJson.Num(root, "x", double.NaN), BridgeJson.Num(root, "y", double.NaN));
				break;
			case "carousel-position":
				OnCarouselPosition((int)BridgeJson.Num(root, "index", -1));
				break;
			case "list-scroll":
				OnListScroll(BridgeJson.Num(root, "y", -1), (int)BridgeJson.Num(root, "first", -1), (int)BridgeJson.Num(root, "last", -1));
				break;
		}
	}

	// A required field: an event without it throws, and the bridge logs the event as failed.
	private static string Text(JsonElement root, string name) => root.GetProperty(name).GetString() ?? string.Empty;

	/* --- diagnostics and Sailfish APIs that address a row --- */

	/// <summary>The MAUI view of one cell of a built row, or null.</summary>
	public View? RowView(int rowIndex, int cell) =>
		rowIndex >= 0 && rowIndex < Rows.Count && cell >= 0 && cell < Rows[rowIndex].CellViews.Count
			? Rows[rowIndex].CellViews[cell]
			: null;

	/// <summary>The objectName of the live delegate whose cells hold <paramref name="element"/>, or null.</summary>
	public string? DelegateHolding(Element element)
	{
		foreach (var dg in ByHandle.Values)
			foreach (var (root, _) in dg.Cells)
				if (ElementTree.IsWithin(element, root))
					return dg.Obj;
		return null;
	}

	/// <summary>A scene point inside the materialized delegate of <paramref name="rowIndex"/> (an injected tap's
	/// target); false while the row has no live delegate.</summary>
	public bool TryGetRowPoint(int rowIndex, out double xQt, out double yQt)
	{
		xQt = yQt = -1;
		foreach (var dg in ByHandle.Values)
		{
			if (dg.Row?.Index != rowIndex)
				continue;
			if (dg.Handle == 0)
				dg.Handle = QtHostRuntime.FindScoped(dg.Obj, Host.NativeHandle);
			if (dg.Handle != 0 && QtHostRuntime.TryItemGeometry(dg.Handle, out var geo))
			{
				xQt = geo.X + geo.Width / 2;
				yQt = geo.Y + Math.Min(geo.Height / 2, 20);
				return true;
			}
		}
		return false;
	}

	/// <summary>One row's delegate, host and bounds state (diagnostics); null when the list has no such row.</summary>
	public string? DescribeRow(int rowIndex, bool parked)
	{
		if (rowIndex < 0 || rowIndex >= Rows.Count)
			return null;
		var row = Rows[rowIndex];
		var dg = ByHandle.Values.FirstOrDefault(d => d.Row?.Index == rowIndex);
		if (dg is null)
			return $"list {Host.Id} row {rowIndex}: no delegate";
		var cells = string.Join(";", dg.Cells.Select(c => $"{c.Root.GetType().Name} bounds={c.Root.Bounds}"));
		var hosts = string.Join(";", dg.Children.Take(3).Select(h => $"{h.Id} att={h.IsAttached} maui={h.MauiLogicalBounds}"));
		var rowCells = string.Join(";", row.CellViews.Select(v => v is null ? "null" : $"{v.GetType().Name} bounds={v.Bounds}"));
		var liveDg = QtHostRuntime.FindVisual(Host.NativeHandle, dg.Obj);
		var globalDg = QtHostRuntime.FindObject(dg.Obj);
		var rootHost = dg.Children.FirstOrDefault(h => h.Parent is null);
		var rootGeo = rootHost is not null && QtHostRuntime.TryItemGeometry(rootHost.NativeHandle, out var g) ? $"{g.Width:F0}x{g.Height:F0}@{g.X:F0},{g.Y:F0}" : "-";
		var scoped = rootHost is null ? 0 : QtHostRuntime.FindVisual(liveDg, $"maui_{rootHost.Id}");
		return $"list {Host.Id} page={PageId} parked={parked} row {rowIndex}: dg={dg.Obj} handle={dg.Handle} liveDg={liveDg} globalDg={globalDg} currentRow={ReferenceEquals(dg.Row, row)} " +
			$"root {rootHost?.Id} handle={rootHost?.NativeHandle} scene={rootGeo} rootInLiveDg={scoped} cells=[{cells}] modelCells=[{rowCells}] hosts=[{hosts}]";
	}
}
