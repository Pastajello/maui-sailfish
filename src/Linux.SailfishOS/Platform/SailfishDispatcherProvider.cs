using System.Collections.Concurrent;
using Microsoft.Maui.Dispatching;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Dispatcher provider; the queue is drained by the Qt host loop.
/// </summary>
internal class SailfishDispatcherProvider : IDispatcherProvider
{
	// Per thread, as a dispatcher belongs to its thread's loop (Android's Looper, iOS's main queue). AsyncLocal lost it
	// in every callback from the native Qt loop (a fresh execution context on the same thread), which then got a new
	// dispatcher no loop drains: work it was given (DispatchDelayed, timers) never ran.
	[ThreadStatic] private static SailfishDispatcher? t_dispatcher;
	[ThreadStatic] private static bool t_loopThread;

	// Only a thread bound to a loop (the Qt thread) has a dispatcher; any other gets null, as an Android thread without a
	// Looper does. A dispatcher made for a pool thread is a queue nothing drains, and an element built there kept it:
	// Profitocracy's AppShell, created after an await off the Qt thread, never completed a Shell pop (each back gesture
	// re-pushed the page). With null, MAUI finds the element's or the application's dispatcher instead.
	public IDispatcher? GetForCurrentThread() =>
		t_dispatcher ?? (t_loopThread ? t_dispatcher = new SailfishDispatcher() : null);

	/// <summary>Makes the calling thread a loop thread (the Qt thread; a test harness) and returns its dispatcher.</summary>
	internal static SailfishDispatcher BindLoopThread()
	{
		t_loopThread = true;
		return t_dispatcher ??= new SailfishDispatcher();
	}

	internal static void SetCurrent(SailfishDispatcher dispatcher)
	{
		t_loopThread = true;
		t_dispatcher = dispatcher;
	}
}

/// <summary>
/// Dispatcher backed by a thread-safe queue drained on the Qt loop thread.
/// </summary>
internal class SailfishDispatcher : IDispatcher
{
	private readonly ConcurrentQueue<Action> _queue = new();
	private readonly object _lock = new();
	private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;

	public bool IsDispatchRequired => Environment.CurrentManagedThreadId != _ownerThreadId;

	public IDispatcherTimer CreateTimer() => new SailfishDispatcherTimer(this);

	private volatile bool _closed;

	/// <summary>The loop that drains this queue has ended (QtHostRuntime.Run returned): nothing queued from now on
	/// would ever run, so Dispatch refuses it and a cross-thread Send fails instead of waiting forever (W1.7).</summary>
	internal void Close() => _closed = true;

	internal bool IsClosed => _closed;

	public bool Dispatch(Action action)
	{
		if (_closed)
			return false;
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
				// Where an async void handler's exception lands (its continuation is posted here): it ends the app, as on
				// Android, unless SailfishExceptions.Unhandled handles it (D11; the log alone hid WhatToEat's aborted push).
				SailfishExceptions.Report(ex, "dispatched work");
			}
		}
	}
}

/// <summary>
/// UI-thread SynchronizationContext: awaits resume on the Qt loop thread, as on Android/iOS.
/// Continuations on the thread pool would hit the single-threaded QML engine concurrently and corrupt its heap.
/// </summary>
internal sealed class SailfishSynchronizationContext : SynchronizationContext
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
		var queued = _dispatcher.Dispatch(() =>
		{
			try { d(state); }
			catch (Exception ex) { error = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex); }
			finally { done.Set(); }
		});
		// Queued just before the loop ended, it never runs: the wait checks for that instead of blocking for good.
		while (!queued || !done.Wait(250))
			if (!queued || _dispatcher.IsClosed)
				throw new InvalidOperationException("The Qt loop has ended; work sent to its thread can no longer run.");
		error?.Throw();
	}

	public override SynchronizationContext CreateCopy() => this;
}

/// <summary>Timer driven by the Qt loop tick.</summary>
internal class SailfishDispatcherTimer : IDispatcherTimer
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
			// The next tick is scheduled before Tick runs: a handler that throws neither re-fires every pump nor
			// leaves a one-shot timer running.
			var repeating = IsRepeating;
			if (repeating)
				_nextTick = now + Interval;
			else
				Stop();
			Tick?.Invoke(this, EventArgs.Empty);
			if (repeating && _isRunning && IsRepeating)
				return true;
			if (_isRunning && !IsRepeating)
				Stop();
			return _isRunning;
		}
		return true;
	}
}
