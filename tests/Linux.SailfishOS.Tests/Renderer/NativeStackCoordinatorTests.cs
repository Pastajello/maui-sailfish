using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>The MAUI ⇄ pageStack coordinator on its own: native snapshots in, operations and host-tree effects out,
/// recorded instead of rendered.</summary>
public class NativeStackCoordinatorTests
{
	private sealed class Owner : INativeStackOwner
	{
		public int Depth;
		public readonly List<string> Calls = new();
		public NavOperation? Followed;

		public int ExpectedNativeDepth() => Depth;
		public void PushModelPages(int levels) => Calls.Add($"push:{levels}");
		public void PopModelPages(int levels) => Calls.Add($"pop:{levels}");
		public void OnNativePopped(string? returnedTo) => Calls.Add($"popped:{returnedTo}");
		public void OnResynced(bool topGone) => Calls.Add($"resynced:{topGone}");
		public void FollowNative(NavOperation op, int levels)
		{
			Followed = op;
			Calls.Add($"follow:{levels}");
		}
		public void KickIn(long ms) { }
		public void RequestPoll() { }
		public void LogNavOp(string op, string source, string pageId, string nativeReport) { }
	}

	private static NativeStackCoordinator Create(Owner owner, params string[] mirror)
	{
		var stack = new NativeStackCoordinator(owner);
		stack.Mirror.AddRange(mirror);
		return stack;
	}

	[Fact]
	public void A_rejected_push_leaves_the_mirror_and_is_retried()
	{
		var owner = new Owner { Depth = 2 };
		var stack = Create(owner, "mp1");

		stack.Step(new() { "mp1" }, topModel: true, version: 1);
		Assert.Equal(new[] { "push:1" }, owner.Calls);
		Assert.Equal(NavOpKind.PushNative, stack.Operation?.Kind);

		// The pageStack refused: the mirror was not committed, the native stack shows it, the next step retries.
		stack.Step(new() { "mp1" }, topModel: true, version: 2);
		Assert.Equal(new[] { "mp1" }, stack.Mirror);
		Assert.Equal(1, stack.NavOpsCompleted);
		Assert.Equal(new[] { "push:1", "push:1" }, owner.Calls);
		Assert.DoesNotContain(owner.Calls, c => c.StartsWith("resynced", StringComparison.Ordinal));
	}

	[Fact]
	public void A_native_pop_that_maui_already_followed_starts_no_operation()
	{
		var owner = new Owner { Depth = 1 };   // the Back key popped MAUI while Silica popped natively
		var stack = Create(owner, "mp1", "mp2");

		stack.Step(new() { "mp1" }, topModel: true, version: 1);

		Assert.Equal(new[] { "popped:mp1" }, owner.Calls);
		Assert.Null(stack.Operation);
		Assert.False(stack.PopUnsynced);
		Assert.Equal(1, stack.NativePopSyncs);
		Assert.Equal(new[] { "mp1" }, stack.Mirror);
	}

	[Fact]
	public void A_back_gesture_makes_maui_follow_and_completes_when_it_did()
	{
		var owner = new Owner { Depth = 2 };
		var stack = Create(owner, "mp1", "mp2");

		stack.Step(new() { "mp1" }, topModel: true, version: 1);
		Assert.Equal(new[] { "popped:mp1", "follow:1" }, owner.Calls);
		Assert.True(stack.PopUnsynced);
		Assert.Equal(NavOpKind.FollowNative, stack.Operation?.Kind);

		// Still MAUI-ahead: no re-push of the page the user left.
		stack.Step(new() { "mp1" }, topModel: true, version: 2);
		Assert.Equal(1, stack.NativePopRacesBlocked);
		Assert.DoesNotContain("push:1", owner.Calls);

		owner.Depth = 1;
		owner.Followed!.MauiDone = true;
		stack.Step(new() { "mp1" }, topModel: true, version: 3);
		Assert.Null(stack.Operation);
		Assert.Equal(1, stack.NavOpsCompleted);
	}

	[Fact]
	public void A_stack_no_operation_explains_is_adopted()
	{
		var owner = new Owner { Depth = 2 };
		var stack = Create(owner, "mp1", "mp2");

		stack.Step(new() { "mp1", "mp3" }, topModel: true, version: 1);

		Assert.Equal(new[] { "resynced:True" }, owner.Calls);
		Assert.Equal(new[] { "mp1", "mp3" }, stack.Mirror);
		Assert.Equal(1, stack.NavResyncs);
	}
}
