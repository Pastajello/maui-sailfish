namespace SailfishKitchen.Helpers;

/// <summary>
/// Collapses a burst of calls into one trailing call after a quiet period, so search does not fire (and race) a request per keystroke.
/// </summary>
public sealed class Debouncer : IDisposable
{
	private readonly object _gate = new();
	private CancellationTokenSource? _cts;
	private bool _disposed;

	/// <summary>Schedules <paramref name="action"/> after <paramref name="delay"/>, replacing any pending call.</summary>
	public void Schedule(TimeSpan delay, Action action)
	{
		ArgumentNullException.ThrowIfNull(action);

		CancellationToken token;
		lock (_gate)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);

			_cts?.Cancel();
			_cts?.Dispose();
			_cts = new CancellationTokenSource();
			token = _cts.Token;
		}

		_ = RunAsync(delay, action, token);
	}

	/// <summary>Drops a pending call without running it.</summary>
	public void Cancel()
	{
		lock (_gate)
		{
			_cts?.Cancel();
			_cts?.Dispose();
			_cts = null;
		}
	}

	private static async Task RunAsync(TimeSpan delay, Action action, CancellationToken token)
	{
		try
		{
			await Task.Delay(delay, token).ConfigureAwait(false);
			if (!token.IsCancellationRequested)
				action();
		}
		catch (OperationCanceledException)
		{
			// Superseded by a newer call.
		}
	}

	public void Dispose()
	{
		lock (_gate)
		{
			if (_disposed)
				return;

			_disposed = true;
			_cts?.Cancel();
			_cts?.Dispose();
			_cts = null;
		}
	}
}
