using Microsoft.Maui.SailfishOS.Platform;
using Xunit;

namespace Linux.SailfishOS.Tests;

/// <summary>
/// Awaits started on the Qt thread must resume there via the dispatcher queue; a thread-pool
/// continuation touching the shim corrupts the QV4 heap.
/// </summary>
public class SynchronizationContextTests
{
	[Fact]
	public void Post_RunsOnlyWhenTheQueueIsDrained()
	{
		var dispatcher = new SailfishDispatcher();
		var context = new SailfishSynchronizationContext(dispatcher);
		var ran = false;

		context.Post(_ => ran = true, null);

		Assert.False(ran);
		dispatcher.DrainQueue();
		Assert.True(ran);
	}

	// W1.7: once the Qt loop ended nothing drains the queue; Dispatch reported the work as queued and a Send from
	// another thread waited forever.
	[Fact]
	public void After_the_loop_ended_dispatch_refuses_and_send_does_not_hang()
	{
		var dispatcher = new SailfishDispatcher();
		var context = new SailfishSynchronizationContext(dispatcher);
		dispatcher.Close();

		Assert.False(dispatcher.Dispatch(() => { }));
		var send = Task.Run(() => context.Send(_ => { }, null));
		Assert.True(((IAsyncResult)send).AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(5)), "Send hung after the loop ended");
		Assert.IsType<InvalidOperationException>(send.Exception?.GetBaseException());
	}

	[Fact]
	public void Send_OnTheOwnerThread_RunsInline()
	{
		var dispatcher = new SailfishDispatcher();
		var context = new SailfishSynchronizationContext(dispatcher);
		var ran = false;

		context.Send(_ => ran = true, null);

		Assert.True(ran);
	}

	[Fact]
	public void AwaitContinuation_ResumesOnTheOwnerThread()
	{
		var previous = SynchronizationContext.Current;
		var dispatcher = new SailfishDispatcher();
		SynchronizationContext.SetSynchronizationContext(new SailfishSynchronizationContext(dispatcher));
		try
		{
			var owner = Environment.CurrentManagedThreadId;
			var resumedOn = -1;
			async Task Work()
			{
				await Task.Delay(20);
				resumedOn = Environment.CurrentManagedThreadId;
			}

			var task = Work();
			// Pump like the Qt tick does until the continuation has run.
			var deadline = DateTime.UtcNow.AddSeconds(5);
			while (!task.IsCompleted && DateTime.UtcNow < deadline)
			{
				dispatcher.DrainQueue();
				Thread.Sleep(5);
			}

			Assert.True(task.IsCompleted, "the continuation never ran — it was not posted to the dispatcher");
			Assert.Equal(owner, resumedOn);
		}
		finally
		{
			SynchronizationContext.SetSynchronizationContext(previous);
		}
	}
}
