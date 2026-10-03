using Microsoft.Maui.Controls;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>The scheduler's latches: however many requests arrive, one pass reaches the loop, and the next request
/// after it ran queues another.</summary>
[Collection("renderer")]
public class RenderSchedulerTests
{
	private static (RenderScheduler Scheduler, Queue<Action> Posted, int[] Runs, TestStatics Statics) Create()
	{
		var statics = new TestStatics();
		var posted = new Queue<Action>();
		QtHostRuntime.TestShim = new FakeShim { Deferred = posted };
		var runs = new int[3];
		var scheduler = new RenderScheduler(() => runs[0]++, () => runs[1]++, () => runs[2]++);
		return (scheduler, posted, runs, statics);
	}

	[Fact]
	public void Parallel_layout_requests_post_one_pass()
	{
		var (scheduler, posted, runs, statics) = Create();
		using var _ = statics;

		Parallel.For(0, 100, _ => scheduler.RequestLayout());

		Assert.Single(posted);
		Assert.Equal(100, scheduler.LayoutRequests);
		posted.Dequeue()();
		Assert.Equal(1, runs[0]);
		scheduler.RequestLayout();   // the pass ran: a new request queues the next one
		Assert.Single(posted);
	}

	[Fact]
	public void Geometry_requests_post_one_pass()
	{
		var (scheduler, posted, runs, statics) = Create();
		using var _ = statics;

		scheduler.RequestGeometry();
		scheduler.RequestGeometry();

		Assert.Single(posted);
		posted.Dequeue()();
		Assert.Equal(1, runs[1]);
	}

	[Fact]
	public void Poll_requests_before_the_kick_runs_kick_once()
	{
		var (scheduler, _, _, statics) = Create();
		using var __ = statics;
		var kicks = 0;
		scheduler.RequestPoll();   // before the loop set the kick: not latched
		scheduler.Kick = () => kicks++;

		scheduler.RequestPoll();
		scheduler.RequestPoll();
		Assert.Equal(1, kicks);

		scheduler.PollStarted();
		scheduler.RequestPoll();
		Assert.Equal(2, kicks);
	}

	[Fact]
	public void Subtree_requests_collapse_per_element_into_one_pass()
	{
		var (scheduler, posted, _, statics) = Create();
		using var _ = statics;
		var a = new VerticalStackLayout();
		var b = new Grid();

		scheduler.QueueSubtree(a);
		scheduler.QueueSubtree(a);
		scheduler.QueueSubtree(b);

		Assert.Single(posted);
		Assert.Equal(new Element[] { a, b }, scheduler.TakeSubtrees());
		scheduler.QueueSubtree(a);
		Assert.True(scheduler.DropPendingSubtrees());
		Assert.Empty(scheduler.TakeSubtrees());
	}

	[Fact]
	public void A_navigation_request_is_taken_once()
	{
		var (scheduler, _, _, statics) = Create();
		using var _ = statics;

		scheduler.NoteNavigationRequest();
		Assert.NotEqual(0, scheduler.TakeNavigationRequestTs());
		Assert.Equal(0, scheduler.TakeNavigationRequestTs());
	}
}
