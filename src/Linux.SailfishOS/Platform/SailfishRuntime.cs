using System.Collections.Concurrent;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Backend runtime services: the <see cref="SailfishDispatcherTimer"/> registry the Qt loop ticks, and opt-in trace logging.
/// </summary>
public static class SailfishRuntime
{
	private static readonly ConcurrentDictionary<SailfishDispatcherTimer, byte> _timers = new();

	/// <summary>Trace logging to /tmp/maui_trace.log; enable with MAUI_SAILFISH_TRACE=1.</summary>
	public static bool TraceEnabled { get; } =
		SailfishEnv.Flag("MAUI_SAILFISH_TRACE")
		|| string.Equals(SailfishEnv.Get("MAUI_SAILFISH_TRACE"), "true", StringComparison.OrdinalIgnoreCase);

	/// <summary>Registers a timer with the main loop.</summary>
	internal static void RegisterTimer(SailfishDispatcherTimer timer)
	{
		_timers.TryAdd(timer, 0);
	}

	/// <summary>Unregisters a timer from the main loop.</summary>
	internal static void UnregisterTimer(SailfishDispatcherTimer timer)
	{
		_timers.TryRemove(timer, out _);
	}

	/// <summary>
	/// Fires every due timer; called per pump tick on the Qt thread, so handlers need no hop.
	/// </summary>
	internal static void TickDueTimers(DateTime now)
	{
		foreach (var timer in _timers.Keys)
		{
			var start = SlowWorkMs > 0 ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
			timer.OnTick(now);
			if (start != 0)
				ReportSlow(start, timer.TickSource);
		}
	}

	/// <summary>MAUI_SAILFISH_SLOW_WORK_MS=N logs every UI-thread work item (dispatch, timer) that ran N ms or longer.</summary>
	internal static readonly int SlowWorkMs = SailfishEnv.Int("MAUI_SAILFISH_SLOW_WORK_MS") ?? 0;

	internal static void ReportSlow(long start, Delegate? work)
	{
		var ms = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
		if (ms < SlowWorkMs)
			return;
		var method = work?.Method;
		Console.Error.WriteLine($"[Sailfish][SLOW] {ms:F0} ms {method?.DeclaringType?.FullName}.{method?.Name} " +
		                        $"at {DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
	}

	/// <summary>The loop's thread-safe wake hook, set while the Qt loop runs.</summary>
	internal static Action<int>? WakeHook;

	internal static void RequestWake(int delayMs) => WakeHook?.Invoke(delayMs);

	/// <summary>The soonest running timer's delay in ms; -1 when none.</summary>
	internal static int NextTimerDelayMs(DateTime now)
	{
		var best = -1;
		foreach (var timer in _timers.Keys)
		{
			var ms = timer.MillisecondsUntilDue(now);
			if (ms >= 0 && (best < 0 || ms < best))
				best = ms;
		}
		return best;
	}

	/// <summary>Appends a timestamped line to the device trace log.</summary>
	internal static void Trace(string message)
	{
		if (!TraceEnabled)
			return;

		try
		{
			System.IO.File.AppendAllText("/tmp/maui_trace.log", $"[SailfishRuntime] {message} {DateTime.Now:HH:mm:ss.fff}\n");
		}
		catch
		{
			// Tracing must never crash the runtime.
		}
	}
}
