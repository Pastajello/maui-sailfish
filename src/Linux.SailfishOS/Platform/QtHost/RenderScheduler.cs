using System.Diagnostics;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// When the renderer works. Every request (a poll, a layout or geometry pass, a handler's subtree change, a deadline,
/// the heartbeat) is latched here and reaches the Qt loop once; the renderer only runs the passes and keeps the
/// "dirty" state they read. One per renderer, so nothing is process-wide: a test's renderer and the app's never share
/// a latch. Requests are safe from any thread; the passes run on the Qt thread (<see cref="QtHostRuntime.Post"/>, which
/// the test shim captures).
/// </summary>
internal sealed class RenderScheduler
{
	private readonly Action _runLayout;
	private readonly Action _runGeometry;
	private readonly Action _runSubtrees;

	private int _pollPending;       // a kicked poll is queued
	private int _layoutPosted;      // a layout pass is queued
	private int _geometryPosted;    // a geometry pass is queued
	private long _layoutRequests;
	private long _navRequestTs;     // Stopwatch timestamp of the last MAUI navigation request (0 = none)
	private long _kickAtMs;         // the earliest scheduled deadline kick (0 = none); Qt thread
	private bool _settleKickArmed;  // Qt thread

	private readonly object _subtreeSync = new();
	private readonly List<Element> _dirtySubtrees = new();
	private bool _subtreePosted;

	internal RenderScheduler(Action runLayout, Action runGeometry, Action runSubtrees)
	{
		_runLayout = runLayout;
		_runGeometry = runGeometry;
		_runSubtrees = runSubtrees;
	}

	/// <summary>Queues one kicked poll on the loop; set by the host (tests set their own or drive polls directly).
	/// Null until the loop runs: requests before it are not latched.</summary>
	internal Action? Kick { get; set; }

	/// <summary>Runs the navigation sync + reconcile on the next loop turn instead of at the next heartbeat; requests
	/// before it runs collapse into one.</summary>
	internal void RequestPoll()
	{
		if (Kick is { } kick && Interlocked.Exchange(ref _pollPending, 1) == 0)
			kick();
	}

	/// <summary>The kicked poll is running: the next request queues another.</summary>
	internal void PollStarted() => Interlocked.Exchange(ref _pollPending, 0);

	/// <summary>A MAUI push/pop was requested (any thread): starts the navigation timeline and kicks a poll.</summary>
	internal void NoteNavigationRequest()
	{
		Interlocked.Exchange(ref _navRequestTs, Stopwatch.GetTimestamp());
		RequestPoll();
	}

	/// <summary>The timestamp of the pending navigation request (0 = none), cleared.</summary>
	internal long TakeNavigationRequestTs() => Interlocked.Exchange(ref _navRequestTs, 0);

	/// <summary>Runs a kicked poll in <paramref name="ms"/> (a deadline the renderer waits for: the activation gate,
	/// a navigation operation's timeout). Earlier requests win; without a dispatcher (tests) the caller polls itself.</summary>
	internal void KickIn(long ms)
	{
		var now = Environment.TickCount64;
		var at = now + Math.Max(1, ms);
		// An earlier kick still pending covers this one; one whose time passed has run (or was lost) and covers nothing.
		if (_kickAtMs > now && _kickAtMs <= at)
		{
			if (QtHostDiag.TraceEnabled)
				QtHostDiag.Trace(QtHostDiagChannel.QtHost, $"kick in {ms} ms covered by the one due in {_kickAtMs - now} ms");
			return;
		}
		if (Dispatcher.GetForCurrentThread() is not { } dispatcher)
		{
			if (QtHostDiag.TraceEnabled)
				QtHostDiag.Trace(QtHostDiagChannel.QtHost, $"kick in {ms} ms dropped: no dispatcher on this thread " +
					$"(provider {DispatcherProvider.Current?.GetType().FullName ?? "null"}, thread {Environment.CurrentManagedThreadId})");
			return;
		}
		_kickAtMs = at;
		if (QtHostDiag.TraceEnabled)
			QtHostDiag.Trace(QtHostDiagChannel.QtHost, $"kick scheduled in {ms} ms");
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(Math.Max(1, ms)), () =>
		{
			if (_kickAtMs == at)
				_kickAtMs = 0;
			if (QtHostDiag.TraceEnabled)
				QtHostDiag.Trace(QtHostDiagChannel.QtHost, $"kick fired ({Environment.TickCount64 - at:+0;-0} ms vs due)");
			RequestPoll();
		});
	}

	/// <summary>While <paramref name="hasWaiters"/> holds, polls every 50 ms instead of waiting for the heartbeat (a
	/// navigation-settled waiter). Without a dispatcher (tests) the caller polls itself.</summary>
	internal void KickSettlePolls(Func<bool> hasWaiters)
	{
		if (_settleKickArmed || !hasWaiters())
			return;
		_settleKickArmed = true;
		if (Dispatcher.GetForCurrentThread() is { } dispatcher)
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(50), () =>
			{
				_settleKickArmed = false;
				RequestPoll();
				KickSettlePolls(hasWaiters);
			});
		else
			_settleKickArmed = false;
	}

	/// <summary>Layout passes requested (MAUI's InvalidateMeasure, the geometry keys); 0 at rest.</summary>
	internal long LayoutRequests => Interlocked.Read(ref _layoutRequests);

	/// <summary>Queues one layout pass however many requests arrive before it runs.</summary>
	internal void RequestLayout()
	{
		Interlocked.Increment(ref _layoutRequests);
		if (Interlocked.Exchange(ref _layoutPosted, 1) == 0)
			QtHostRuntime.Post(() =>
			{
				Volatile.Write(ref _layoutPosted, 0);
				_runLayout();
			});
	}

	/// <summary>Queues one geometry pass (root rects only, no measure/arrange) however many scroll reports arrive.</summary>
	internal void RequestGeometry()
	{
		if (Interlocked.Exchange(ref _geometryPosted, 1) == 0)
			QtHostRuntime.Post(() =>
			{
				Volatile.Write(ref _geometryPosted, 0);
				_runGeometry();
			});
	}

	/// <summary>A container handler changed its children (any thread): the subtree is diffed on the next loop turn.</summary>
	internal void QueueSubtree(Element element)
	{
		lock (_subtreeSync)
		{
			if (!_dirtySubtrees.Contains(element))
				_dirtySubtrees.Add(element);
			if (_subtreePosted)
				return;
			_subtreePosted = true;
		}
		QtHostRuntime.Post(_runSubtrees);
	}

	/// <summary>The queued subtree roots, cleared (the subtree pass takes them).</summary>
	internal List<Element> TakeSubtrees()
	{
		lock (_subtreeSync)
		{
			_subtreePosted = false;
			var roots = new List<Element>(_dirtySubtrees);
			_dirtySubtrees.Clear();
			return roots;
		}
	}

	/// <summary>The full reconcile takes over the reported changes still queued (it applies them itself).</summary>
	internal bool DropPendingSubtrees()
	{
		lock (_subtreeSync)
		{
			var any = _dirtySubtrees.Count > 0;
			_dirtySubtrees.Clear();
			return any;
		}
	}

	/// <summary>The slow verifying heartbeat: <paramref name="poll"/> every <paramref name="ms"/> (the first after at
	/// most 500 ms). Work it finds is work no event announced (counted as timerWithWork).</summary>
	internal static void StartHeartbeat(IDispatcher dispatcher, int ms, Action poll)
	{
		if (ms <= 0)
			return;
		Action? beat = null;
		beat = () =>
		{
			poll();
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(ms), beat!);
		};
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(Math.Min(ms, 500)), beat);
	}
}
