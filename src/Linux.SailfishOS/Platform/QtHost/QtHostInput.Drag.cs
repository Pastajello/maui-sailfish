using System.Globalization;
using System.Text;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.SailfishOS.Platform;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// Drag &amp; drop on top of the router (tracker S39, plan M28). A press held still on a view with a
/// <see cref="DragGestureRecognizer"/> starts a drag, as Android starts one on a long press: <c>SendDragStarting</c>
/// (Cancel or Handled leave the press to the other gestures), a ghost of the source follows the finger, the page's
/// back swipe and flickables wait (<c>mauiHoldDrag</c>), the <see cref="DropGestureRecognizer"/> under the finger gets
/// DragOver (and DragLeave when the finger moves on), and the release over an accepting target awaits <c>SendDrop</c>
/// (MAUI's default copies the text or image) before <c>SendDropCompleted</c> on the source. A release elsewhere, a
/// second finger or leaving the page completes the drag without a drop, as MAUI has no cancel event.
/// </summary>
internal sealed partial class QtHostInputRouter
{
	/// <summary>Hold time before a drag starts (Android's long-press timeout).</summary>
	internal const long DragStartMs = 500;

	private View? _dragOwner;                          // the source view
	private List<DragGestureRecognizer>? _dragSources;  // its recognizers (CanDrag)
	private string? _dragHostId;                        // the source's host, for the ghost
	private int _dragSeq;                               // invalidates a pending start
	private bool _dragArmed;                            // the finger is down on a source, the start timer runs
	private DragStartingEventArgs? _dragStart;          // the active drag's DataPackage (SendDragStarting's answer)
	private List<DragGestureRecognizer>? _dragStarted;  // recognizers whose drag is active (SendDropCompleted)
	private View? _dropOwner;                           // the drop target under the finger
	private List<DropGestureRecognizer>? _dropTargets;
	private bool _dropAccepted;                         // its last DragOver accepted the data
	private double _dragPressDpX, _dragPressDpY;
	private Page? _dragPage;                            // the page the drag started on
	private Action<bool>? _dragRowHold;                 // a drag from a list row: keeps the ListView from flicking
	private string? _dragGhostName;                     // a row's delegate, for the ghost (rows have no page host)
	private Action<bool>? _dragRowMark;                 // marks the row as dragged (its press highlight goes)

	/// <summary>Drags started (diagnostics).</summary>
	public long DragsStarted { get; private set; }

	/// <summary>Drops delivered to a target (diagnostics).</summary>
	public long Drops { get; private set; }

	/// <summary>Whether a drag is in progress (the finger carries a DataPackage).</summary>
	internal bool DragActive => _dragStart is not null;

	/// <summary>A press on <paramref name="element"/>: arms the drag start if it or an ancestor can be dragged.</summary>
	private void ArmDrag(VisualElement element, NativeElementHost host, double dpX, double dpY)
	{
		ClearDrag();
		if (!TryFindDragSource(element, out var owner, out var sources))
			return;
		_dragHostId = HostIdOf(owner) ?? host.Id;
		Arm(owner, sources, dpX, dpY);
	}

	/// <summary>A press on a list row (the ListView took it, CaptureRow): arms a drag from the row's template, the
	/// source looked up between the element under the finger and the cell root.</summary>
	private bool ArmRowDrag(View hit, View cellRoot, Action<bool> hold, string? ghostName, Action<bool>? dragging)
	{
		if (_dragArmed || _dragStart is not null)
			return false;
		ClearDrag();
		if (!TryFindDragSource(hit, out var owner, out var sources, stopAt: cellRoot))
			return false;
		_dragRowHold = hold;
		_dragGhostName = ghostName;
		_dragRowMark = dragging;
		Arm(owner, sources, _lastPressDpX, _lastPressDpY);
		return true;
	}

	private void Arm(View owner, List<DragGestureRecognizer> sources, double dpX, double dpY)
	{
		_dragOwner = owner;
		_dragSources = sources;
		_dragPressDpX = dpX;
		_dragPressDpY = dpY;
		_dragArmed = true;
		var seq = ++_dragSeq;
		_dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(DragStartMs), () =>
		{
			if (seq == _dragSeq && _dragArmed && _fingerDown)
				StartDrag();
		});
		if (_trace)
			QtHostDiag.Trace(QtHostDiagChannel.Input, $"drag armed on {owner.GetType().Name} (starts after {DragStartMs} ms held)");
	}

	/// <summary>The nearest view (element or ancestor) with a DragGestureRecognizer that can drag.</summary>
	private static bool TryFindDragSource(VisualElement element, out View owner, out List<DragGestureRecognizer> sources, View? stopAt = null)
	{
		for (var v = element as View; v is not null; v = ReferenceEquals(v, stopAt) ? null : v.Parent as View)
		{
			List<DragGestureRecognizer>? found = null;
			foreach (var recognizer in v.GestureRecognizers)
				if (recognizer is DragGestureRecognizer { CanDrag: true } drag)
					(found ??= new List<DragGestureRecognizer>()).Add(drag);
			if (found is not null)
			{
				owner = v;
				sources = found;
				return true;
			}
		}
		owner = null!;
		sources = null!;
		return false;
	}

	/// <summary>The nearest view at the root-space point that accepts drops.</summary>
	private bool TryFindDropTarget(double dpX, double dpY, out View owner, out List<DropGestureRecognizer> targets)
	{
		if (_renderer.TryHitTest(dpX, dpY, out var host) && host?.Element is Element hit)
		{
			for (var e = hit; e is not null and not Page; e = e.Parent)
			{
				if (e is not View v)
					continue;
				List<DropGestureRecognizer>? found = null;
				foreach (var recognizer in v.GestureRecognizers)
					if (recognizer is DropGestureRecognizer { AllowDrop: true } drop)
						(found ??= new List<DropGestureRecognizer>()).Add(drop);
				if (found is not null)
				{
					owner = v;
					targets = found;
					return true;
				}
			}
		}
		owner = null!;
		targets = null!;
		return false;
	}

	private string? HostIdOf(View view) =>
		_renderer.CurrentHosts.FirstOrDefault(h => h.IsAttached && ReferenceEquals(h.Element, view))?.Id;

	private void StartDrag()
	{
		_dragArmed = false;
		var owner = _dragOwner;
		var sources = _dragSources;
		if (owner is null || sources is null)
			return;
		var getPosition = PositionOf(new Point(_dragPressDpX, _dragPressDpY));
		DragStartingEventArgs? started = null;
		List<DragGestureRecognizer>? active = null;
		foreach (var source in sources)
		{
			try
			{
				var args = source.SendDragStarting(owner, getPosition);
				if (args.Cancel || args.Handled)
					continue;
				started ??= args;
				(active ??= new List<DragGestureRecognizer>()).Add(source);
			}
			catch (Exception ex)
			{
				SailfishExceptions.Report(ex, "a DragStarting handler");
			}
		}
		if (started is null || active is null)
		{
			ClearDrag();   // canceled or handled: the press stays the other gestures'
			return;
		}
		_dragStart = started;
		_dragStarted = active;
		_dragPage = _renderer.CurrentPage;
		DragsStarted++;
		// The drag owns the finger now: no long press, tap, pan or tab swipe follows from it.
		CancelLongPress();
		_holdFlyout = null;
		_tabSwipeArmed = false;
		_renderer.HoldPageDrag(true);
		if (_dragRowHold is { } rowHold)
		{
			rowHold(true);             // the ListView must not flick under the drag
			_rowGestureTook = true;    // nor report the release as a row tap
			_dragRowMark?.Invoke(true);
		}
		Ghost(show: true, _dragPressDpX, _dragPressDpY, allowed: false, source: _dragHostId, sourceName: _dragGhostName);
		if (_trace)
			QtHostDiag.Trace(QtHostDiagChannel.Input, $"drag started on {owner.GetType().Name} (text '{started.Data.Text}')");
		UpdateDropTarget(_dragPressDpX, _dragPressDpY);
	}

	/// <summary>A move during an armed or active drag. True when the drag took it.</summary>
	private bool DragMove(double x, double y)
	{
		var dpX = QtHostUnits.ToLogical(x);
		var dpY = QtHostUnits.ToLogical(y);
		if (_dragArmed)
		{
			// Moving before the hold time: a scroll or pan, not a drag.
			if (Math.Max(Math.Abs(dpX - _dragPressDpX), Math.Abs(dpY - _dragPressDpY)) > TapSlopDp)
				ClearDrag();
			return false;
		}
		if (_dragStart is null)
			return false;
		if (!ReferenceEquals(_renderer.CurrentPage, _dragPage))
		{
			CancelDrag();   // the page went away under the finger
			return true;
		}
		UpdateDropTarget(dpX, dpY);
		Ghost(show: true, dpX, dpY, _dropAccepted, source: null);
		return true;
	}

	private void UpdateDropTarget(double dpX, double dpY)
	{
		var package = _dragStart!.Data;
		TryFindDropTarget(dpX, dpY, out var owner, out var targets);
		var getPosition = PositionOf(new Point(dpX, dpY));
		if (_dropOwner is not null && !ReferenceEquals(_dropOwner, owner))
		{
			foreach (var target in _dropTargets!)
				Guard(() => target.SendDragLeave(new SailfishDragEventArgs(package, getPosition)), "a DragLeave handler");
			_dropOwner = null;
			_dropTargets = null;
			_dropAccepted = false;
		}
		if (owner is null)
			return;
		_dropOwner = owner;
		_dropTargets = targets;
		var accepted = false;
		foreach (var target in targets)
		{
			var args = new SailfishDragEventArgs(package, getPosition);
			Guard(() => target.SendDragOver(args), "a DragOver handler");
			accepted |= args.AcceptedOperation != DataPackageOperation.None;
		}
		_dropAccepted = accepted;
	}

	/// <summary>The release of an armed or active drag. True when the drag took it (no tap or pan follows).</summary>
	private bool DragRelease(double x, double y)
	{
		if (_dragArmed)
		{
			ClearDrag();
			return false;
		}
		if (_dragStart is null)
			return false;
		if (!ReferenceEquals(_renderer.CurrentPage, _dragPage))
		{
			CancelDrag();
			return true;
		}
		var dpX = QtHostUnits.ToLogical(x);
		var dpY = QtHostUnits.ToLogical(y);
		UpdateDropTarget(dpX, dpY);
		var package = _dragStart.Data;
		var targets = _dropAccepted ? _dropTargets : null;
		var sources = _dragStarted!;
		var getPosition = PositionOf(new Point(dpX, dpY));
		EndDragVisuals();
		ClearDrag();
		_ = DeliverDrop(package, targets, sources, getPosition);
		return true;
	}

	/// <summary>Drop on the accepting target (awaited: MAUI's default transfer reads the package asynchronously), then
	/// DropCompleted on the source, also when nothing took the drop.</summary>
	private async Task DeliverDrop(DataPackage package, List<DropGestureRecognizer>? targets,
		List<DragGestureRecognizer> sources, Func<IElement?, Point?> getPosition)
	{
		if (targets is not null)
		{
			foreach (var target in targets)
			{
				try
				{
					await target.SendDrop(new SailfishDropEventArgs(package.View, getPosition));
					Drops++;
				}
				catch (Exception ex)
				{
					SailfishExceptions.Report(ex, "a Drop handler");
				}
			}
		}
		foreach (var source in sources)
			Guard(() => source.SendDropCompleted(new DropCompletedEventArgs()), "a DropCompleted handler");
	}

	/// <summary>A second finger or the page going away: the drag ends without a drop.</summary>
	internal void CancelDrag()
	{
		if (_dragArmed)
		{
			ClearDrag();
			return;
		}
		if (_dragStart is null)
			return;
		var package = _dragStart.Data;
		var getPosition = PositionOf(new Point(_dragPressDpX, _dragPressDpY));
		if (_dropTargets is { } targets)
			foreach (var target in targets)
				Guard(() => target.SendDragLeave(new SailfishDragEventArgs(package, getPosition)), "a DragLeave handler");
		var sources = _dragStarted!;
		EndDragVisuals();
		ClearDrag();
		foreach (var source in sources)
			Guard(() => source.SendDropCompleted(new DropCompletedEventArgs()), "a DropCompleted handler");
	}

	private void EndDragVisuals()
	{
		Ghost(show: false, 0, 0, allowed: false, source: null);
		_renderer.HoldPageDrag(false);
		_dragRowHold?.Invoke(false);
		_dragRowMark?.Invoke(false);
	}

	private void ClearDrag()
	{
		_dragSeq++;
		_dragArmed = false;
		_dragOwner = null;
		_dragSources = null;
		_dragHostId = null;
		_dragStart = null;
		_dragStarted = null;
		_dropOwner = null;
		_dropTargets = null;
		_dropAccepted = false;
		_dragPage = null;
		_dragRowHold = null;
		_dragGhostName = null;
		_dragRowMark = null;
	}

	/// <summary>Shows, moves or hides the page's drag ghost (MauiModelPage.mauiDragGhost): a snapshot of the source host
	/// under the finger, dimmed while no target accepts the drop. Positions in Qt units of the page's root space.</summary>
	private void Ghost(bool show, double dpX, double dpY, bool allowed, string? source, string? sourceName = null)
	{
		var sb = new StringBuilder("{\"show\":").Append(show ? "true" : "false");
		if (show)
		{
			sb.Append(",\"x\":").Append(QtHostUnits.ToQtUnits(dpX).ToString("R", CultureInfo.InvariantCulture))
			  .Append(",\"y\":").Append(QtHostUnits.ToQtUnits(dpY).ToString("R", CultureInfo.InvariantCulture))
			  .Append(",\"allowed\":").Append(allowed ? "true" : "false");
			if (source is not null)
				sb.Append(",\"id\":").Append(BridgeValue.Quote(source));
			else if (sourceName is not null)
				sb.Append(",\"name\":").Append(BridgeValue.Quote(sourceName));
		}
		_renderer.CallPage(null, "mauiDragGhost", sb.Append('}').ToString());
	}

	private static void Guard(Action call, string what)
	{
		try
		{
			call();
		}
		catch (Exception ex)
		{
			SailfishExceptions.Report(ex, what);
		}
	}
}

/// <summary>DragEventArgs with the finger's position (MAUI's position constructor is internal; GetPosition is virtual).</summary>
internal sealed class SailfishDragEventArgs(DataPackage package, Func<IElement?, Point?> getPosition) : DragEventArgs(package)
{
	public override Point? GetPosition(Element? relativeTo) => getPosition(relativeTo);
}

/// <summary>DropEventArgs with the finger's position.</summary>
internal sealed class SailfishDropEventArgs(DataPackageView view, Func<IElement?, Point?> getPosition) : DropEventArgs(view)
{
	public override Point? GetPosition(Element? relativeTo) => getPosition(relativeTo);
}
