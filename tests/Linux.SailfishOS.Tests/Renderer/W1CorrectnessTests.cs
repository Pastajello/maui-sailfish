using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>Correctness defects from the second architecture review (docs/architecture-handoff.md W1), one test each.</summary>
[Collection("renderer")]
public sealed class W1CorrectnessTests
{
	// W1.1: content reconciled before its first arrange retries once in the same pass; the retry was a nested
	// Reconcile(), so its time and count went in twice (the outer stopwatch covered it already).
	[Fact]
	public void A_reconcile_with_an_unarranged_shape_retry_counts_once()
	{
		var nav = new NavigationPage(new ContentPage { Title = "Home", Content = new Label { Text = "home" } });
		using var h = new RendererHarness(nav);
		for (var i = 0; i < 4; i++)
			h.Poll();
		var count = h.Renderer.ReconcileCount;
		var retries = h.Renderer.UnarrangedRetries;

		// A shape sized only by its arrange: the push's walk skips it, the retry after the layout pass creates it.
		var ellipse = new Ellipse { Fill = Colors.Red, HeightRequest = 40 };
		_ = nav.PushAsync(new ContentPage { Title = "Shape", Content = new VerticalStackLayout { Children = { ellipse } } });
		h.Poll();

		Assert.Equal(retries + 1, h.Renderer.UnarrangedRetries);
		Assert.Contains(h.Renderer.CurrentHosts, x => ReferenceEquals(x.Element, ellipse));
		Assert.Equal(count + 1, h.Renderer.ReconcileCount);
	}

	// W1.2: a native pop while a dialog is open waits in SyncNativeNavigation; the reconcile must not render MAUI's
	// still-pushed page onto the revealed native page meanwhile. (The other order, a dialog opened while MAUI follows
	// a pop, needs MAUI's pop to stay in flight across polls, which it does on the device but not in this harness;
	// QtHostPageRenderer.MauiFollowPending covers it.)
	[Fact]
	public void A_native_pop_under_an_open_dialog_waits_for_MAUI_to_follow()
	{
		var nav = new NavigationPage(new ContentPage { Title = "Home", Content = new Label { Text = "home" } });
		using var h = new RendererHarness(nav);
		var detailLabel = new Label { Text = "detail" };
		_ = nav.PushAsync(new ContentPage { Title = "Detail", Content = detailLabel });
		for (var i = 0; i < 6; i++)
			h.Poll();
		var detail = nav.CurrentPage;
		_ = h.Renderer.PushAlertAsync("Q", "open while the stack pops", null, "OK");
		Assert.Equal(1, h.Shim.DialogsOpened);

		h.Shim.Pages.RemoveAt(h.Shim.Pages.Count - 1);   // the native stack pops under the dialog
		var reconciles = h.Renderer.ReconcileCount;
		detailLabel.Text = "detail changed";              // MAUI still shows Detail: a change asks for a pass
		for (var i = 0; i < 3; i++)
			h.Renderer.KickedPoll();
		Assert.Equal(reconciles, h.Renderer.ReconcileCount);
		Assert.Same(detail, nav.CurrentPage);
	}

	// W1.3: the posted layout and geometry passes checked the transition and creation gates only; with the native top
	// not followed yet they flushed MAUI's page geometry onto the hosts of the page being left (dying on the device:
	// HealIfDead, then a full-page reset).
	[Fact]
	public void A_posted_layout_pass_waits_while_the_native_top_is_not_followed()
	{
		var nav = new NavigationPage(new ContentPage { Title = "Home", Content = new Label { Text = "home" } });
		using var h = new RendererHarness(nav);
		var detailLabel = new Label { Text = "detail" };
		_ = nav.PushAsync(new ContentPage { Title = "Detail", Content = new VerticalStackLayout { WidthRequest = 300, Children = { detailLabel } } });
		for (var i = 0; i < 6; i++)
			h.Poll();
		_ = h.Renderer.PushAlertAsync("Q", "open while the stack pops", null, "OK");
		h.Shim.Pages.RemoveAt(h.Shim.Pages.Count - 1);   // native pops under the dialog: MAUI cannot follow yet
		h.Renderer.KickedPoll();
		var batches = h.Shim.GeometryBatches;

		detailLabel.Text = string.Join(' ', Enumerable.Repeat("a much longer detail text", 6));   // asks for a layout pass
		for (var i = 0; i < 3; i++)
			h.Poll();
		Assert.Equal(batches, h.Shim.GeometryBatches);
	}

	// W1.4: a MAUI pop reconciled from inside the navigation sync (to paint the returned-to page before it is
	// revealed), and the same poll then reconciled again.
	[Fact]
	public void A_poll_that_pops_runs_one_reconcile()
	{
		var nav = new NavigationPage(new ContentPage { Title = "Home", Content = new Label { Text = "home" } });
		using var h = new RendererHarness(nav);
		_ = nav.PushAsync(new ContentPage { Title = "Detail", Content = new Label { Text = "detail" } });
		for (var i = 0; i < 6; i++)
			h.Poll();

		_ = nav.PopAsync();
		var reconciles = h.Renderer.ReconcileCount;
		h.Renderer.KickedPoll();
		Assert.Equal(1, h.Renderer.ReconcileCount - reconciles);
		for (var i = 0; i < 4; i++)
			h.Poll();
		Assert.Single(h.Shim.Pages);
		Assert.Contains(h.Shim.ByUri("label"), l => l.Text("text") == "home" && !l.Destroyed);
	}

	// W1.6: an animated pop defers its hosts' native destroys until the slide-out ends; a pop the pageStack refused
	// never slides, and the queue (then process-wide) kept the handles until some later idle snapshot.
	[Fact]
	public void A_refused_pop_leaves_no_destroy_queued()
	{
		var nav = new NavigationPage(new ContentPage { Title = "Home", Content = new Label { Text = "home" } });
		using var h = new RendererHarness(nav);
		_ = nav.PushAsync(new ContentPage { Title = "Detail", Content = new Label { Text = "detail" } });
		for (var i = 0; i < 6; i++)
			h.Poll();

		h.Renderer.FaultNextPop = true;
		_ = nav.PopAsync();
		h.Renderer.KickedPoll();
		Assert.True(h.Renderer.NativeOpFailures > 0, "the injected pop failure did not happen");
		Assert.Equal(0, h.Renderer.DeferredNativeDestroys);
	}

	// W1.7a: a batch the shim rejected in part returned true, so callers counted it as pushed (handler pushes, the
	// reconcile diff); nothing is recorded as applied, and the next push diffs the whole batch again.
	[Fact]
	public void A_partly_rejected_batch_is_not_counted_as_pushed()
	{
		var label = new Label { Text = "before" };
		using var h = new RendererHarness(new ContentPage { Title = "T", Content = label });
		var host = h.Renderer.CurrentHosts.Single(x => ReferenceEquals(x.Element, label));

		h.Shim.RejectNextBatch = 1;
		var pushed = h.Renderer.PushBatch(host, new[] { ("text", "\"after\""), ("mauiPixelSize", "30") });
		Assert.False(pushed);
		Assert.False(host.IsApplied("text", "\"after\""));
	}

	// W1.11: the handler answers the legacy ScrollToRequested event; an app that hooks the RequestScrollTo command
	// through the CommandMapper must still see it (MAUI invokes the command too), and ScrollToAsync must complete.
	[Fact]
	public void ScrollToAsync_completes_and_reaches_an_apps_RequestScrollTo_mapping()
	{
		var invoked = 0;
		Microsoft.Maui.SailfishOS.Handlers.SailfishScrollViewHandler.CommandMapper.AppendToMapping(nameof(IScrollView.RequestScrollTo),
			(_, _, _) => invoked++);
		var scroll = new ScrollView { HeightRequest = 200, Content = new VerticalStackLayout { HeightRequest = 2000 } };
		using var h = new RendererHarness(new ContentPage { Title = "T", Content = scroll });
		var task = scroll.ScrollToAsync(0, 300, animated: false);
		h.Poll();
		Assert.True(task.IsCompleted, "ScrollToAsync did not complete");
		Assert.Equal(300, scroll.ScrollY);
		Assert.Equal(1, invoked);
	}

	// W1.11: a Label's FlowDirection went through the snapshot handler's MapSnapshotAndViewState, which skipped the
	// layout pass the generic mapper runs for that key: the mirror state arrived, the positions did not move.
	[Fact]
	public void A_labels_flow_direction_change_asks_for_a_layout_pass()
	{
		var label = new Label { Text = "right to left" };
		using var h = new RendererHarness(new ContentPage { Title = "T", Content = new VerticalStackLayout { Children = { label } } });
		var before = h.Renderer.ArchitectureCounters.LayoutRequests;
		label.FlowDirection = FlowDirection.RightToLeft;
		Assert.True(h.Renderer.ArchitectureCounters.LayoutRequests > before, "no layout pass requested for FlowDirection");
	}

	// W1.11: the container handlers subscribed in ConnectHandler only; a handler moved to another page kept listening
	// to the old one and never heard the new one.
	[Fact]
	public void A_tabbed_page_handler_moved_to_another_page_follows_it()
	{
		ContentPage Tab(string t) => new() { Title = t, Content = new Label { Text = t } };
		using var h = new RendererHarness(new ContentPage { Title = "Root", Content = new Label { Text = "root" } });
		// Pages outside the window: the handler's own subscription is the only way their tab change reaches the renderer.
		var (a1, a2, b1, b2) = (Tab("a1"), Tab("a2"), Tab("b1"), Tab("b2"));
		var first = new TabbedPage { Children = { a1, a2 } };
		var second = new TabbedPage { Children = { b1, b2 } };
		var handler = new Microsoft.Maui.SailfishOS.Handlers.SailfishTabbedPageHandler();
		handler.SetMauiContext(h.Renderer.MauiContext);
		handler.SetVirtualView(first);
		handler.SetVirtualView(second);

		var kicks = 0;
		h.Renderer.PollKick = () => kicks++;
		h.Renderer.KickedPoll();   // clears the latch
		first.CurrentPage = a2;
		Assert.Equal(0, kicks);
		second.CurrentPage = b2;
		Assert.Equal(1, kicks);
	}
}
