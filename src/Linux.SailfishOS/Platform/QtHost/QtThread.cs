namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// The one way platform services reach the Qt thread (HOP, docs/sailfish-apis.md: every Sailfish API is safe from any
/// thread). On the Qt thread, before the host runs (startup code on the would-be loop thread) and under the test shim
/// the work runs inline; from any other thread it is posted to the Qt loop and the caller waits for it (Run) or awaits
/// it (RunAsync). Qt objects, the QML engine and QtDBus are only ever touched on their own thread this way.
/// </summary>
internal static class QtThread
{
	/// <summary>True when work can run inline: the Qt thread, no host yet, or the test shim.</summary>
	public static bool IsCurrent => QtHostRuntime.TestShim is not null || !QtHostRuntime.IsRunning || QtHostRuntime.IsQtThread;

	/// <summary>Runs <paramref name="work"/> on the Qt thread and returns its result; blocks an off-thread caller.</summary>
	public static T Run<T>(Func<T> work) => IsCurrent ? work() : RunAsync(work).GetAwaiter().GetResult();

	/// <summary>Runs <paramref name="work"/> on the Qt thread; blocks an off-thread caller until it ran.</summary>
	public static void Run(Action work)
	{
		if (IsCurrent)
			work();
		else
			RunAsync(() => { work(); return true; }).GetAwaiter().GetResult();
	}

	/// <summary>Runs <paramref name="work"/> on the Qt thread; the task completes with its result or exception.</summary>
	public static Task<T> RunAsync<T>(Func<T> work)
	{
		if (IsCurrent)
		{
			try
			{
				return Task.FromResult(work());
			}
			catch (Exception ex)
			{
				return Task.FromException<T>(ex);
			}
		}
		var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
		QtHostRuntime.Post(() =>
		{
			try
			{
				done.SetResult(work());
			}
			catch (Exception ex)
			{
				done.SetException(ex);
			}
		});
		return done.Task;
	}

	/// <summary>Queues <paramref name="work"/> on the Qt thread without waiting (inline when already there).</summary>
	public static void Post(Action work) => QtHostRuntime.RunOnQtThread(work);
}
