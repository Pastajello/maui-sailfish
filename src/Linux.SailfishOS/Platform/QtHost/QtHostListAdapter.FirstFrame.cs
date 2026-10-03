using System.Collections;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// A page with a long list opens in two turns: the page and its header paint first, the rows come a frame later.
/// Building the row model (templating and measuring every item) ran in the same turn as the page itself, so the
/// whole push stalled the UI thread for both (Kitchen's Beef catalog: 161 ms in one block, 33 ms of it the rows).
/// Only for lists that will be taller than their viewport: a short list is cheap, and an empty first frame of a list
/// that fits would only flicker. Owner decision 2026-10-03 (architecture-handoff.md W10, 3c).
/// </summary>
internal sealed partial class QtHostListAdapter
{
	internal static bool FirstBuildWaitsFrame { get; set; } = SailfishEnv.Get("MAUI_SAILFISH_LIST_FIRST_FRAME") != "0";

	/// <summary>The longest a first build waits for its frame: a hidden window renders no frames.</summary>
	internal const int FirstFrameHoldMs = 200;

	/// <summary>A row of a one-column list is at least a Silica small item (dp) for the estimate.</summary>
	private const double MinRowEstimateDp = 40;

	/// <summary>Items counted at most for the estimate; past this the list is long anyway.</summary>
	private const int EstimateItemCap = 256;

	/// <summary>Diagnostics: first builds that waited for a frame.</summary>
	internal static long FirstBuildsHeld { get; private set; }

	private bool _everBuilt;
	private long _firstBuildHoldUntil;   // 0 = not decided yet, -1 = no hold (or released), else the deadline

	/// <summary>True while the list's first row build waits for the page's first frame; asks for that frame once.</summary>
	private bool HoldsFirstBuild(double widthDp)
	{
		if (_everBuilt || _firstBuildHoldUntil < 0 || !FirstBuildWaitsFrame)
			return false;
		if (_firstBuildHoldUntil == 0)
		{
			if (!LikelyTallerThanViewport(widthDp))
			{
				_firstBuildHoldUntil = -1;
				return false;
			}
			_firstBuildHoldUntil = Environment.TickCount64 + FirstFrameHoldMs;
			FirstBuildsHeld++;
			QtHostSurface.RequestFrame(ReleaseFirstBuild);
			_bridge.SchedulePending(FirstFrameHoldMs);
			QtHostDiag.Trace(QtHostDiagChannel.QmlObject, $"collection '{Host}' first rows wait for the page's first frame");
			return true;
		}
		if (Environment.TickCount64 < _firstBuildHoldUntil)
			return true;
		_firstBuildHoldUntil = -1;   // no frame came in time (hidden window): build now
		return false;
	}

	private void ReleaseFirstBuild()
	{
		if (_firstBuildHoldUntil <= 0)
			return;
		_firstBuildHoldUntil = -1;
		_bridge.SchedulePending();
	}

	/// <summary>
	/// Whether the rows will likely overflow the list's viewport, from what is known before any item is templated:
	/// the item count, the span, and a row height of at least <see cref="MinRowEstimateDp"/> (a grid cell's width in
	/// a multi-column grid, cards being about as tall as wide).
	/// </summary>
	private bool LikelyTallerThanViewport(double widthDp)
	{
		ReadLayout();
		if (Horizontal || Carousel)
			return false;
		var viewportDp = Host.MauiLogicalBounds.Height;
		if (viewportDp <= 0)
			return false;
		var count = CountItems(View.ItemsSource);
		if (count == 0)
			return false;
		var span = Math.Max(1, Span);
		var rows = (count + span - 1) / span;
		var rowDp = span > 1 ? Math.Max(MinRowEstimateDp, (widthDp - (span - 1) * HSpacingDp) / span) : MinRowEstimateDp;
		return rows * (rowDp + SpacingDp) > viewportDp;
	}

	private static int CountItems(IEnumerable? items)
	{
		switch (items)
		{
			case null:
				return 0;
			case ICollection collection:
				return collection.Count;
		}
		var count = 0;
		foreach (var _ in items)
			if (++count >= EstimateItemCap)
				break;
		return count;
	}
}
