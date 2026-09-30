using Microsoft.Maui.Dispatching;
using Microsoft.Maui.SailfishOS.Platform;
using Xunit;

namespace Linux.SailfishOS.Tests;

/// <summary>A dispatcher belongs to its thread: code called back from the native Qt loop (a fresh execution context
/// on the loop thread) must reach the loop's dispatcher, not a new one no loop drains.</summary>
public class DispatcherTests
{
	// An async method's AsyncLocal writes do not flow back to its caller, as a native callback's context does not.
	private static async Task<IDispatcher?> ResolveInOwnContext(IDispatcherProvider provider)
	{
		await Task.CompletedTask;
		return provider.GetForCurrentThread();
	}

	[Fact]
	public void The_same_thread_gets_the_same_dispatcher_in_any_execution_context()
	{
		var provider = new SailfishDispatcherProvider();
		Exception? failure = null;
		var thread = new Thread(() =>
		{
			try
			{
				var inCallback = ResolveInOwnContext(provider).GetAwaiter().GetResult();
				Assert.Same(inCallback, provider.GetForCurrentThread());
			}
			catch (Exception ex)
			{
				failure = ex;
			}
		});
		thread.Start();
		thread.Join();
		Assert.Null(failure);
	}

	[Fact]
	public void Other_threads_get_their_own_dispatcher()
	{
		var provider = new SailfishDispatcherProvider();
		IDispatcher? other = null;
		var thread = new Thread(() => other = provider.GetForCurrentThread());
		thread.Start();
		thread.Join();
		Assert.NotNull(other);
		Assert.NotSame(other, provider.GetForCurrentThread());
	}
}
