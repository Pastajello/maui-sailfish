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

/// <summary>Header, footer and empty view: each lives in its own placeholder of the native ListView, measured and materialized like a row, re-measured when its content asks.</summary>
internal sealed partial class QtHostListAdapter
{
	internal void MaterializeSlots(double widthDp)
	{
		if (!Host.IsAttached)
			return;
		var pending = false;
		// In a horizontal list header/footer extents are widths measured at the list height.
		pending |= MaterializeSlot("header", HeaderView, "mauiHeaderH", widthDp, Horizontal);
		pending |= MaterializeSlot("footer", FooterView, "mauiFooterH", widthDp, Horizontal);
		pending |= MaterializeSlot("empty", EmptySlotView, "mauiEmptyH", widthDp, false);
		SlotsDirty = pending;
		if (pending)
			_bridge.SchedulePending(ResyncIntervalMs);   // a placeholder not resolvable yet: retry shortly
	}

	/// <summary>Materializes one slot's content in its placeholder; true while the placeholder is not
	/// resolvable yet (retried on the list's own clock).</summary>
	internal bool MaterializeSlot(string slot, View? view, string heightProp, double widthDp, bool alongRows)
	{
		if (Slots.TryGetValue(slot, out var existing))
		{
			// A replaced view (a new Header, content for a slot that had none) is materialized like a remap.
			if ((existing.Remap && existing.Root is not null) || !ReferenceEquals(existing.Root, view))
			{
				UnwatchSlot(existing);
				_bridge.DestroyHosts(existing.Children, PageId);
				Slots.Remove(slot);   // materialized again below, in the same placeholder
			}
			else
			{
				var sizeChanged = alongRows
					? Math.Abs(Host.MauiLogicalBounds.Height - existing.CrossDp) > 0.5
					: Math.Abs(widthDp - existing.WidthDp) > 0.5;
				if (existing.Root is not null && (existing.Remeasure || sizeChanged))
				{
					existing.CrossDp = alongRows ? Host.MauiLogicalBounds.Height : existing.CrossDp;
					existing.WidthDp = widthDp;
					var extent = MeasureSlot(existing.Root, widthDp, alongRows);
					if (Math.Abs(extent - existing.ExtentDp) > 0.25)
					{
						existing.ExtentDp = extent;
						_rowTopsDp = null;   // rows start below the header
						Push(existing.HeightProp, QtHostUnits.ToQtUnits(extent));
						_bridge.MarkSceneDirty();
					}
				}
				existing.Remeasure = false;
				UpdateSlotGeometry(existing);
				return false;
			}
		}
		var objName = $"maui_{Host.Id}__{slot}";
		var handle = QtHostRuntime.FindObject(objName);
		if (handle == 0)
			handle = QtHostRuntime.FindVisual(Host.NativeHandle, objName);
		if (handle == 0)
		{
			if (view is null)
			{
				// No content: done at height 0, the placeholder stays invisible.
				Slots[slot] = new SlotState { Obj = objName, Handle = 0 };
				return false;
			}
			return true;   // placeholder still being created
		}

		var heightDp = 0.0;
		var slotState = new SlotState { Obj = objName, Handle = handle, HeightProp = heightProp };
		if (view is not null)
		{
			if (alongRows)
				slotState.CrossDp = Host.MauiLogicalBounds.Height;
			slotState.WidthDp = widthDp;
			heightDp = MeasureSlot(view, widthDp, alongRows);
			slotState.ExtentDp = heightDp;
			_rowTopsDp = null;
			var desired = new List<NativeElementHost>();
			var props = new Dictionary<NativeElementHost, Dictionary<string, object?>>();
			_bridge.SlotMapping = true;   // handler attach and templating raise tree/measure events of their own
			try
			{
				_renderer.MapItemSubtree(view, desired, props);
			}
			finally
			{
				_bridge.SlotMapping = false;
			}
			_bridge.CollectHosts(view, slotState.Children);
			slotState.Root = view;
			CreateAndAttachHosts(desired, props, objName, handle);
			PageId = ListPageId();
			WatchSlot(slotState, view);
		}
		Slots[slot] = slotState;
		Push(heightProp, QtHostUnits.ToQtUnits(heightDp));
		UpdateSlotGeometry(slotState);
		return false;
	}

	internal double MeasureSlot(View view, double widthDp, bool alongRows)
	{
		_bridge.SlotMapping = true;
		try
		{
			return alongRows ? MeasureItemExtent(view, widthDp) : _bridge.MeasureItemView(view, widthDp);
		}
		finally
		{
			_bridge.SlotMapping = false;
		}
	}

	// A header on iOS/Android grows with its content (a bound section appearing, rows added to a stack); here
	// the slot re-measures on MeasureInvalidated and re-creates its hosts when views come or go.
	internal void WatchSlot(SlotState slot, View view)
	{
		slot.MeasureHandler = (_, _) => MarkSlot(slot, remap: false);
		slot.TreeHandler = (_, _) => MarkSlot(slot, remap: true);
		view.MeasureInvalidated += slot.MeasureHandler;
		view.DescendantAdded += slot.TreeHandler;
		view.DescendantRemoved += slot.TreeHandler;
	}

	internal void MarkSlot(SlotState slot, bool remap)
	{
		if (!QtHostRuntime.IsQtThread)
		{
			QtHostRuntime.Post(() => MarkSlot(slot, remap));
			return;
		}
		if (_bridge.SlotMapping || slot.MeasureHandler is null)
			return;   // our own measure/map pass, or a retired slot
		slot.Remeasure = true;
		slot.Remap |= remap;
		SlotsDirty = true;
		_bridge.SchedulePending();
		_renderer.RequestPoll();
	}

	internal void UpdateSlotGeometry(SlotState slot)
	{
		if (slot.Handle == 0 || slot.Children.Count == 0 || slot.Root is null)
			return;
		_renderer.PushItemGeometry(slot.Root, 0, new HashSet<NativeElementHost>(slot.Children));
	}
}
