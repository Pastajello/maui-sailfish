using System.Text.Json;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.SailfishOS.Platform;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>Tracker S20 (plan M16 steps 3, 5): TitleView in the header, HasNavigationBar/NavBarIsVisible collapse the
/// header, Shell.TabBarIsVisible hides the sections' tab row.</summary>
[Collection("renderer")]
public sealed class HeaderChromeTests
{
	public HeaderChromeTests()
	{
		if (Microsoft.Maui.Dispatching.DispatcherProvider.Current is not SailfishDispatcherProvider)
			Microsoft.Maui.Dispatching.DispatcherProvider.SetCurrent(new SailfishDispatcherProvider());
		SailfishDispatcherProvider.BindLoopThread();
	}

	private static void Settle(RendererHarness h, int polls = 6)
	{
		for (var i = 0; i < polls; i++)
			h.Poll();
	}

	private static JsonElement LastHeader(RendererHarness h) =>
		h.Shim.Ops.Last(op => op.GetProperty("op").GetString() == "header");

	private static bool HeaderShown(RendererHarness h) =>
		!h.Shim.Ops.Any(op => op.GetProperty("op").GetString() == "header") || LastHeader(h).GetProperty("on").GetBoolean();

	private static List<string> TabTitles(RendererHarness h)
	{
		var call = h.Shim.PageCalls.Last(c => c.Method == "setMauiTabs");
		using var doc = JsonDocument.Parse(JsonSerializer.Deserialize<string>(call.Arg)!);
		return doc.RootElement.GetProperty("titles").EnumerateArray().Select(e => e.GetString()!).ToList();
	}

	private static ContentPage Page(string title) => new() { Title = title, Content = new Label { Text = title } };

	[Fact]
	public void HasNavigationBar_false_collapses_the_header_and_back()
	{
		var page = Page("Onboarding");
		NavigationPage.SetHasNavigationBar(page, false);
		using var h = new RendererHarness(new NavigationPage(page));
		Settle(h);
		Assert.False(HeaderShown(h));

		NavigationPage.SetHasNavigationBar(page, true);
		Settle(h);
		Assert.True(HeaderShown(h));
	}

	[Fact]
	public void A_tabbed_page_without_a_navigation_bar_hides_the_header_of_its_tabs()
	{
		var tabs = new TabbedPage { Children = { Page("A"), Page("B") } };
		NavigationPage.SetHasNavigationBar(tabs, false);
		using var h = new RendererHarness(new NavigationPage(tabs));
		Settle(h);
		Assert.False(HeaderShown(h));
	}

	[Fact]
	public void Shell_NavBarIsVisible_false_on_the_shell_hides_every_header_and_a_page_can_turn_it_back_on()
	{
		var home = Page("Home");
		var shell = new Shell();
		Shell.SetNavBarIsVisible(shell, false);
		shell.Items.Add(new ShellContent { Content = home });
		using var h = new RendererHarness(shell);
		Settle(h);
		Assert.False(HeaderShown(h));

		Shell.SetNavBarIsVisible(home, true);
		Settle(h);
		Assert.True(HeaderShown(h));
	}

	[Fact]
	public void A_title_view_is_hosted_in_the_header_band_instead_of_the_title_text()
	{
		var page = Page("Plain");
		var titleLabel = new Label { Text = "Custom title" };
		NavigationPage.SetTitleView(page, titleLabel);
		using var h = new RendererHarness(new NavigationPage(page));
		h.Renderer.HandleNativeEvent("window-geometry",
			"{\"pageWidth\":1080,\"pageHeight\":2160,\"headerHeight\":110,\"titleHeight\":110,\"statusHeight\":40}");
		Settle(h);

		Assert.False(LastHeader(h).GetProperty("title").GetBoolean());
		var host = h.Renderer.CurrentHosts.First(x => ReferenceEquals(x.Element, titleLabel));
		var content = h.Renderer.CurrentHosts.First(x => x.Element is Label { Text: "Plain" });
		// Laid out in the header band: under the status area, above the content.
		Assert.True(host.MauiLogicalBounds.Top > 0, $"title view at {host.MauiLogicalBounds}");
		Assert.True(host.MauiLogicalBounds.Bottom <= content.MauiLogicalBounds.Top + 0.5,
			$"title view {host.MauiLogicalBounds} vs content {content.MauiLogicalBounds}");

		// Removing it brings the title text back and drops its host.
		NavigationPage.SetTitleView(page, null);
		Settle(h);
		Assert.True(LastHeader(h).GetProperty("title").GetBoolean());
		Assert.DoesNotContain(h.Renderer.CurrentHosts, x => ReferenceEquals(x.Element, titleLabel));
	}

	[Fact]
	public void A_title_view_gets_one_host()
	{
		var page = Page("Plain");
		var titleLabel = new Label { Text = "Custom title" };
		NavigationPage.SetTitleView(page, titleLabel);
		using var h = new RendererHarness(new NavigationPage(page));
		Settle(h);

		Assert.Single(h.Renderer.CurrentHosts, x => ReferenceEquals(x.Element, titleLabel));
		Assert.Equal(new[] { "Custom title" }, h.Shim.Objects.Where(o => o.Text("text") == "Custom title").Select(o => o.Text("text")));

		// A change inside it updates the same native object.
		var id = h.Renderer.CurrentHosts.First(x => ReferenceEquals(x.Element, titleLabel)).Id;
		titleLabel.Text = "Renamed title";
		Settle(h);
		Assert.Equal("Renamed title", h.Shim.ById(id)?.Text("text"));
	}

	[Fact]
	public void A_shell_title_view_shows_on_its_page()
	{
		var home = Page("Home");
		var titleLabel = new Label { Text = "Shell title" };
		Shell.SetTitleView(home, titleLabel);
		var shell = new Shell();
		shell.Items.Add(new ShellContent { Content = home });
		using var h = new RendererHarness(shell);
		Settle(h);

		Assert.Contains(h.Renderer.CurrentHosts, x => ReferenceEquals(x.Element, titleLabel));
	}

	[Fact]
	public void A_hidden_header_shows_no_title_view()
	{
		var page = Page("Plain");
		var titleLabel = new Label { Text = "Custom title" };
		NavigationPage.SetTitleView(page, titleLabel);
		NavigationPage.SetHasNavigationBar(page, false);
		using var h = new RendererHarness(new NavigationPage(page));
		Settle(h);

		Assert.False(HeaderShown(h));
		Assert.DoesNotContain(h.Renderer.CurrentHosts, x => ReferenceEquals(x.Element, titleLabel));
	}

	[Fact]
	public void Shell_TabBarIsVisible_false_hides_the_sections_and_keeps_the_top_tabs()
	{
		var first = Page("First");
		var shell = new Shell();
		var item = new TabBar();
		var section = new Tab { Title = "One" };
		section.Items.Add(new ShellContent { Title = "First", Content = first });
		section.Items.Add(new ShellContent { Title = "Second", Content = Page("Second") });
		item.Items.Add(section);
		item.Items.Add(new Tab { Title = "Two", Items = { new ShellContent { Content = Page("Other") } } });
		shell.Items.Add(item);
		using var h = new RendererHarness(shell);
		Settle(h);
		Assert.Equal(new[] { "One", "Two" }, TabTitles(h));

		Shell.SetTabBarIsVisible(first, false);
		Settle(h);
		Assert.Equal(new[] { "First", "Second" }, TabTitles(h));

		Shell.SetTabBarIsVisible(first, true);
		Settle(h);
		Assert.Equal(new[] { "One", "Two" }, TabTitles(h));
	}
}
