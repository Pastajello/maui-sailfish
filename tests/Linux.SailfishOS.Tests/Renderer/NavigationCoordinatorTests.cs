using Microsoft.Maui.Controls;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>The navigation coordinator: one operation at a time, confirmed by the native pageStack, a resync when
/// the stacks disagree in a way no operation explains.</summary>
[Collection("renderer")]
public class NavigationCoordinatorTests
{
	private static ContentPage Page(string text) => new() { Title = text, Content = new Label { Text = text } };

	private static void Settle(RendererHarness h, int polls = 6)
	{
		for (var i = 0; i < polls; i++)
			h.Poll();
	}

	[Fact]
	public void A_back_gesture_pops_MAUI_exactly_once()
	{
		var nav = new NavigationPage(Page("Root"));
		using var h = new RendererHarness(nav);
		_ = nav.PushAsync(Page("Second"));
		Settle(h);
		var completed = h.Renderer.NavOpsCompleted;

		h.Shim.Pages.RemoveAt(h.Shim.Pages.Count - 1);   // Silica's back gesture
		Settle(h);

		Assert.Single(nav.Navigation.NavigationStack);
		Assert.Equal(new[] { "mp1" }, h.Shim.Pages);
		Assert.True(h.Renderer.NavOpsCompleted > completed);
		Assert.Equal(0, h.Renderer.NavResyncs);
		Assert.Equal("Root", h.Shim.ByUri("label").Single(o => o.Page == "mp1").Text("text"));
	}

	[Fact]
	public void Back_to_back_pushes_run_one_after_another_in_order()
	{
		var nav = new NavigationPage(Page("Root"));
		using var h = new RendererHarness(nav);

		_ = nav.PushAsync(Page("Second"));
		_ = nav.PushAsync(Page("Third"));
		Settle(h, 8);

		Assert.Equal(3, nav.Navigation.NavigationStack.Count);
		Assert.Equal(3, h.Shim.Pages.Count);
		Assert.Equal(0, h.Renderer.NavResyncs);
		Assert.Equal(0, h.Renderer.NavOpsFailed);
		Assert.Equal("Third", h.Shim.ByUri("label").Single(o => o.Page == h.Shim.Pages[^1]).Text("text"));
	}

	[Fact]
	public void A_rejected_native_push_is_retried_without_a_resync()
	{
		var nav = new NavigationPage(Page("Root"));
		using var h = new RendererHarness(nav);
		var failures = h.Renderer.NativeOpFailures;

		h.Renderer.FaultNextPush = true;   // the pageStack refuses the next push once
		_ = nav.PushAsync(Page("Second"));
		Settle(h);

		Assert.Equal(failures + 1, h.Renderer.NativeOpFailures);
		Assert.Equal(2, h.Shim.Pages.Count);
		Assert.Equal(0, h.Renderer.NavResyncs);
		Assert.Equal("Second", h.Shim.ByUri("label").Single(o => o.Page == h.Shim.Pages[^1]).Text("text"));
	}

	[Fact]
	public void An_unexplained_native_stack_is_adopted_and_the_page_rendered_again()
	{
		var nav = new NavigationPage(Page("Root"));
		using var h = new RendererHarness(nav);
		_ = nav.PushAsync(Page("Second"));
		Settle(h);
		Assert.Equal(new[] { "mp1", "mp2" }, h.Shim.Pages);

		h.Shim.Pages[1] = "mpX";   // another model page instance at the same depth
		Settle(h);

		Assert.Equal(1, h.Renderer.NavResyncs);
		Assert.Equal(2, nav.Navigation.NavigationStack.Count);   // MAUI untouched: same depth
		Assert.Equal(new[] { "mp1", "mpX" }, h.Shim.Pages);
	}
}
