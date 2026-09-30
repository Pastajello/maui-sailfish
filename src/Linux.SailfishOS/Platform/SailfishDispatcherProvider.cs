using System.Collections.Concurrent;
using Microsoft.Maui.Dispatching;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Dispatcher provider; the queue is drained by the Qt host loop.
/// </summary>
public class SailfishDispatcherProvider : IDispatcherProvider
{
	// Per thread, as a dispatcher belongs to its thread's loop (Android's Looper, iOS's main queue). AsyncLocal lost it
	// in every callback from the native Qt loop (a fresh execution context on the same thread), which then got a new
	// dispatcher no loop drains: work it was given (DispatchDelayed, timers) never ran.
	[ThreadStatic] private static SailfishDispatcher? t_dispatcher;

	public IDispatcher? GetForCurrentThread() => t_dispatcher ??= new SailfishDispatcher();

	internal static void SetCurrent(SailfishDispatcher dispatcher) => t_dispatcher = dispatcher;
}

/// <summary>
/// Dispatcher backed by a thread-safe queue drained on the Qt loop thread.
/// </summary>
public class SailfishDispatcher : IDispatcher
{
	private readonly ConcurrentQueue<Action> _queue = new();
	private readonly object _lock = new();
	private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;

	public bool IsDispatchRequired => Environment.CurrentManagedThreadId != _ownerThreadId;

	public IDispatcherTimer CreateTimer() => new SailfishDispatcherTimer(this);

	public bool Dispatch(Action action)
	{
		_queue.Enqueue(action);
		SailfishRuntime.RequestWake(0);   // wake the event-driven loop now
		return true;
	}

	/// <summary>Work is queued, so the loop re-arms the next tick immediately.</summary>
	internal bool HasPendingWork => !_queue.IsEmpty;

	public bool DispatchDelayed(TimeSpan delay, Action action)
	{
		var timer = new SailfishDispatcherTimer(this)
		{
			Interval = delay,
			IsRepeating = false,
		};
		timer.Tick += (_, _) =>
		{
			timer.Stop();
			var start = SailfishRuntime.SlowWorkMs > 0 ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
			action();
			if (start != 0)
				SailfishRuntime.ReportSlow(start, action);   // the delayed work itself, not this wrapper
		};
		timer.Start();
		return true;
	}

	/// <summary>Drains all queued work. Called by the runtime main loop.</summary>
	internal void DrainQueue()
	{
		while (_queue.TryDequeue(out var action))
		{
			var start = SailfishRuntime.SlowWorkMs > 0 ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
			try
			{
				action();
				if (start != 0)
					SailfishRuntime.ReportSlow(start, action);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[SailfishDispatcher] Unhandled dispatch exception: {ex}");
			}
		}
	}
}

/// <summary>
/// UI-thread SynchronizationContext: awaits resume on the Qt loop thread, as on Android/iOS.
/// Continuations on the thread pool would hit the single-threaded QML engine concurrently and corrupt its heap.
/// </summary>
public sealed class SailfishSynchronizationContext : SynchronizationContext
{
	private readonly SailfishDispatcher _dispatcher;

	public SailfishSynchronizationContext(SailfishDispatcher dispatcher)
	{
		ArgumentNullException.ThrowIfNull(dispatcher);
		_dispatcher = dispatcher;
	}

	public override void Post(SendOrPostCallback d, object? state) => _dispatcher.Dispatch(() => d(state));

	public override void Send(SendOrPostCallback d, object? state)
	{
		if (!_dispatcher.IsDispatchRequired)
		{
			d(state);
			return;
		}
		using var done = new ManualResetEventSlim();
		System.Runtime.ExceptionServices.ExceptionDispatchInfo? error = null;
		_dispatcher.Dispatch(() =>
		{
			try { d(state); }
			catch (Exception ex) { error = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex); }
			finally { done.Set(); }
		});
		done.Wait();
		error?.Throw();
	}

	public override SynchronizationContext CreateCopy() => this;
}

/// <summary>Timer driven by the Qt loop tick.</summary>
public class SailfishDispatcherTimer : IDispatcherTimer
{
	private readonly SailfishDispatcher _dispatcher;
	private DateTime _nextTick;
	private bool _isRunning;
	private readonly object _lock = new();

	internal SailfishDispatcherTimer(SailfishDispatcher dispatcher)
	{
		_dispatcher = dispatcher;
	}

	public TimeSpan Interval { get; set; } = TimeSpan.FromMilliseconds(16);
	public bool IsRepeating { get; set; } = true;
	public bool IsRunning => _isRunning;

	public event EventHandler? Tick;

	/// <summary>The Tick handlers (slow-work diagnostics name them).</summary>
	internal Delegate? TickSource => Tick;

	public void Start()
	{
		lock (_lock)
		{
			if (_isRunning)
				return;
			_isRunning = true;
			_nextTick = DateTime.UtcNow + Interval;
			SailfishRuntime.RegisterTimer(this);
		}
		SailfishRuntime.RequestWake((int)Math.Ceiling(Interval.TotalMilliseconds));
	}

	/// <summary>Milliseconds until the next due tick; -1 when stopped.</summary>
	internal int MillisecondsUntilDue(DateTime now) =>
		!_isRunning ? -1 : (int)Math.Max(0, Math.Ceiling((_nextTick - now).TotalMilliseconds));

	public void Stop()
	{
		lock (_lock)
		{
			if (!_isRunning)
				return;
			_isRunning = false;
			SailfishRuntime.UnregisterTimer(this);
		}
	}

	/// <summary>Called by the runtime loop; returns true if the timer should keep running.</summary>
	internal bool OnTick(DateTime now)
	{
		if (!_isRunning)
			return false;

		if (now >= _nextTick)
		{
			Tick?.Invoke(this, EventArgs.Empty);
			if (IsRepeating && _isRunning)
			{
				_nextTick = now + Interval;
				return true;
			}
			Stop();
			return false;
		}
		return true;
	}
}