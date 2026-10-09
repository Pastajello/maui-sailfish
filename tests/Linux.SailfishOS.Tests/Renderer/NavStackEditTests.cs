using Microsoft.Maui.Controls;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>Tracker S37 (plan M27 step 2): MAUI's animated flag reaches the native push/pop — PushAsync(page, false)
/// and the stack edits InsertPageBefore/RemovePage (which MAUI sends unanimated) do not slide the page.</summary>
[Collection("renderer")]
public sealed class NavStackEditTests
{
	private static ContentPage Page(string text) => new() { Title = text, Content = new Label { Text = text } };

	private static void Settle(RendererHarness h, int polls = 6)
	{
		for (var i = 0; i < polls; i++)
			h.Poll();
	}

	private static (RendererHarness H, NavigationPage Nav) Start()
	{
		var nav = new NavigationPage(Page("Root"));
		var h = new RendererHarness(nav);
		Settle(h);
		// The first push after start goes at once (pages are not kept in the back cache before activation settles).
		_ = nav.PushAsync(Page("Warm-up"));
		Settle(h);
		return (h, nav);
	}

	[Fact]
	public void An_animated_push_and_pop_slide_and_unanimated_ones_do_not()
	{
		var (h, nav) = Start();
		using var _h = h;

		_ = nav.PushAsync(Page("Slides"));
		Settle(h);
		Assert.Equal("PUSH Animated", h.Renderer.LastNativeNavStep);

		_ = nav.PushAsync(Page("Jumps"), animated: false);
		Settle(h);
		Assert.Equal("PUSH Immediate", h.Renderer.LastNativeNavStep);

		_ = nav.PopAsync(animated: false);
		Settle(h);
		Assert.Equal("POP Immediate", h.Renderer.LastNativeNavStep);

		// The flag is per request: the next animated push slides again.
		_ = nav.PushAsync(Page("Slides again"));
		Settle(h);
		Assert.Equal("PUSH Animated", h.Renderer.LastNativeNavStep);
	}

	[Fact]
	public void InsertPageBefore_and_RemovePage_change_the_stack_without_a_slide()
	{
		var (h, nav) = Start();
		using var _h = h;
		var top = Page("Top");
		_ = nav.PushAsync(top);
		Settle(h);

		nav.Navigation.InsertPageBefore(Page("Inserted"), top);
		Settle(h);
		Assert.Equal(4, h.Shim.Pages.Count);
		Assert.Equal("PUSH Immediate", h.Renderer.LastNativeNavStep);
		Assert.Equal("Top", h.Renderer.CurrentPage?.Title);

		nav.Navigation.RemovePage(nav.Navigation.NavigationStack[0]);
		Settle(h);
		Assert.Equal(3, h.Shim.Pages.Count);
		Assert.Equal("POP Immediate", h.Renderer.LastNativeNavStep);
		Assert.Equal("Top", h.Renderer.CurrentPage?.Title);
	}

	[Fact]
	public void Every_navigation_finishes_so_the_next_one_runs_after_stack_edits_and_a_pop_to_root()
	{
		var (h, nav) = Start();
		using var _h = h;
		var still = Page("Still");
		var pushes = new List<Task> { nav.PushAsync(still, false) };
		Settle(h);
		var slides = Page("Slides");
		pushes.Add(nav.PushAsync(slides));
		Settle(h);
		nav.Navigation.InsertPageBefore(Page("Inserted"), slides);
		Settle(h);
		nav.Navigation.RemovePage(still);
		Settle(h);
		var popped = nav.PopToRootAsync(false);
		Settle(h, 10);
		Assert.True(popped.IsCompleted, "PopToRootAsync never finished");
		Assert.All(pushes, t => Assert.True(t.IsCompleted));

		var next = nav.PushAsync(Page("Next"), false);
		Settle(h, 10);
		Assert.True(next.IsCompleted, "the push after the edits never ran");
		Assert.Equal("Next", h.Renderer.CurrentPage?.Title);
	}

	[Fact]
	public void The_root_pages_navigation_keeps_working_after_stack_edits_and_a_pop_to_root()
	{
		var root = Page("Root");
		var nav = new NavigationPage(root);
		using var h = new RendererHarness(nav);
		Settle(h);
		var navigation = root.Navigation;
		var still = Page("Still");
		_ = navigation.PushAsync(still, false);
		Settle(h);
		var slides = Page("Slides");
		_ = navigation.PushAsync(slides);
		Settle(h);
		navigation.InsertPageBefore(Page("Inserted"), slides);
		Settle(h);
		navigation.RemovePage(still);
		Settle(h);
		var popped = navigation.PopToRootAsync(false);
		Settle(h, 10);
		Assert.True(popped.IsCompleted);
		Assert.Equal(1, navigation.NavigationStack.Count);

		var next = navigation.PushAsync(Page("Next"), false);
		Settle(h, 10);
		Assert.True(next.IsCompleted);
		Assert.Equal(2, navigation.NavigationStack.Count);
		Assert.Equal("Next", h.Renderer.CurrentPage?.Title);
	}
}
