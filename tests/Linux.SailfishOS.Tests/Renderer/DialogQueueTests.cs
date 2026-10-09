using Microsoft.Maui.Controls;
using Microsoft.Maui.SailfishOS.Handlers;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>Tracker S38 (plan M27 steps 4–5): dialogs wait their turn instead of completing at once, follow the page's
/// FlowDirection and the prompt's keyboard; a replaced FlyoutPage.Detail lets go of its handlers.</summary>
[Collection("renderer")]
public sealed class DialogQueueTests
{
	private static RendererHarness Show(Page page)
	{
		var h = new RendererHarness(page);
		for (var i = 0; i < 4; i++)
			h.Poll();
		return h;
	}

	private static string LastDialogEval(RendererHarness h) => h.Shim.Evals.Last(e => e.Contains(".__pushDialog(", StringComparison.Ordinal));

	[Fact]
	public async Task A_second_alert_waits_for_the_first_and_both_return_their_answers()
	{
		using var h = Show(new ContentPage { Title = "T", Content = new Label { Text = "x" } });

		var first = h.Renderer.PushAlertAsync("One", "first", "Yes", "No");
		var second = h.Renderer.PushAlertAsync("Two", "second", "Yes", "No");
		Assert.Equal(1, h.Shim.DialogsOpened);
		Assert.False(second.IsCompleted);

		h.Renderer.HandleNativeEvent("alert-accepted", "{}");
		Assert.True(await first);
		Assert.Equal(2, h.Shim.DialogsOpened);   // the queued one opened when the first closed
		Assert.Contains("Two", LastDialogEval(h));

		h.Renderer.HandleNativeEvent("alert-rejected", "{}");
		Assert.False(await second);
	}

	[Fact]
	public void A_dialog_on_a_right_to_left_page_is_mirrored()
	{
		using var h = Show(new ContentPage { Title = "T", FlowDirection = FlowDirection.RightToLeft, Content = new Label { Text = "x" } });

		_ = h.Renderer.PushAlertAsync("RTL", "mirrored", "OK", "Cancel");

		Assert.Contains("\\\"mauiMirrored\\\":true", LastDialogEval(h));
	}

	[Fact]
	public void A_prompt_for_an_email_address_asks_for_the_email_keyboard()
	{
		using var h = Show(new ContentPage { Title = "T", Content = new Label { Text = "x" } });

		_ = h.Renderer.PushPromptAsync("Mail", "address", "OK", "Cancel", "", "", -1, numeric: false, hints: 0x200000);

		Assert.Contains("\\\"mauiHints\\\":2097152", LastDialogEval(h));
	}

	[Fact]
	public void A_replaced_detail_lets_go_of_its_handlers()
	{
		var oldDetail = new ContentPage { Title = "Old", Content = new Label { Text = "old" } };
		var flyout = new FlyoutPage
		{
			Flyout = new ContentPage { Title = "Menu", Content = new Label { Text = "menu" } },
			Detail = new NavigationPage(oldDetail),
		};
		using var h = Show(flyout);
		var oldNav = flyout.Detail;
		Assert.NotNull(oldNav.Handler);

		flyout.Detail = new NavigationPage(new ContentPage { Title = "New", Content = new Label { Text = "new" } });
		for (var i = 0; i < 4; i++)
			h.Poll();

		Assert.Null(oldNav.Handler);
		Assert.IsType<SailfishFlyoutPageHandler>(flyout.Handler);
	}
}
