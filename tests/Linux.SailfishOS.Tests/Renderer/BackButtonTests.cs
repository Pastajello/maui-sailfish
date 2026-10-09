using Microsoft.Maui.Controls;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>Tracker S05 (plan M2): hardware Back goes through IWindow.BackButtonClicked, so MAUI's OnBackButtonPressed
/// chain (a page's veto, Shell's BackButtonBehavior.Command, the modal pop) decides before anything pops.</summary>
[Collection("renderer")]
public sealed class BackButtonTests
{
	private sealed class VetoPage : ContentPage
	{
		public int Calls;
		public VetoPage() { Title = "Veto"; Content = new Label { Text = "veto" }; }
		protected override bool OnBackButtonPressed() { Calls++; return true; }
	}

	private static ContentPage Page(string text) => new() { Title = text, Content = new Label { Text = text } };

	private static void Settle(RendererHarness h, int polls = 6)
	{
		for (var i = 0; i < polls; i++)
			h.Poll();
	}

	[Fact]
	public void A_page_that_vetoes_Back_stays()
	{
		var nav = new NavigationPage(Page("Root"));
		using var h = new RendererHarness(nav);
		var veto = new VetoPage();
		_ = nav.PushAsync(veto);
		Settle(h);

		Assert.True(h.Renderer.HandleBack());
		Settle(h);

		Assert.Equal(1, veto.Calls);
		Assert.Equal(2, nav.Navigation.NavigationStack.Count);
		Assert.Equal(2, h.Shim.Pages.Count);
		Assert.Equal(1, h.Renderer.BackHandledByMaui);
		Assert.Equal(0, h.Renderer.BackFallbackPops);
	}

	[Fact]
	public void A_plain_page_is_popped_by_its_NavigationPage()
	{
		var nav = new NavigationPage(Page("Root"));
		using var h = new RendererHarness(nav);
		_ = nav.PushAsync(Page("Second"));
		Settle(h);

		Assert.True(h.Renderer.HandleBack());
		Settle(h);

		Assert.Single(nav.Navigation.NavigationStack);
		Assert.Single(h.Shim.Pages);
		Assert.Equal(1, h.Renderer.BackHandledByMaui);
	}

	[Fact]
	public void Shell_BackButtonBehavior_Command_runs_instead_of_the_pop()
	{
		var shell = new Shell { Items = { new ShellContent { Title = "Home", Content = Page("Home") } } };
		using var h = new RendererHarness(shell);
		var pushed = Page("Detail");
		var commands = 0;
		Shell.SetBackButtonBehavior(pushed, new BackButtonBehavior { Command = new Command(() => commands++) });
		_ = shell.Navigation.PushAsync(pushed);
		Settle(h);

		Assert.True(h.Renderer.HandleBack());
		Settle(h);

		Assert.Equal(1, commands);
		Assert.Equal(2, shell.CurrentItem.CurrentItem.Navigation.NavigationStack.Count);
	}

	[Fact]
	public void A_modal_is_closed_through_MAUI()
	{
		var nav = new NavigationPage(Page("Root"));
		using var h = new RendererHarness(nav);
		_ = nav.Navigation.PushModalAsync(Page("Modal"));
		Settle(h);

		Assert.True(h.Renderer.HandleBack());
		Settle(h);

		Assert.Empty(nav.Navigation.ModalStack);
		Assert.Equal(1, h.Renderer.BackHandledByMaui);
		Assert.Equal(0, h.Renderer.BackFallbackPops);
	}

	[Fact]
	public void Back_on_the_root_page_is_left_to_the_platform()
	{
		var nav = new NavigationPage(Page("Root"));
		using var h = new RendererHarness(nav);

		Assert.False(h.Renderer.HandleBack());
		Assert.Single(nav.Navigation.NavigationStack);
	}

	// On the phone Silica pops on the Back key and the back swipe before MAUI is asked (navback leg, 2026-10-06), so a
	// page that decides Back gets no Silica back navigation: Back reaches it only through MAUI.
	[Fact]
	public void Pages_that_decide_Back_turn_off_Silica_back_navigation()
	{
		Assert.False(Microsoft.Maui.SailfishOS.Platform.QtHost.QtHostPageRenderer.BackNavigationOf(new VetoPage()));
		var commanded = Page("Commanded");
		Shell.SetBackButtonBehavior(commanded, new BackButtonBehavior { Command = new Command(() => { }) });
		Assert.False(Microsoft.Maui.SailfishOS.Platform.QtHost.QtHostPageRenderer.BackNavigationOf(commanded));
		Assert.True(Microsoft.Maui.SailfishOS.Platform.QtHost.QtHostPageRenderer.BackNavigationOf(Page("Plain")));
		Assert.True(Microsoft.Maui.SailfishOS.Platform.QtHost.QtHostPageRenderer.BackNavigationOf(new NavigationPage(Page("Root"))));
	}
}
