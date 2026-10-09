using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.SailfishOS.Handlers;
using Microsoft.Maui.SailfishOS.Platform;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>Tracker S14 (plan M15 steps 2, 5): tab badges (TabbedPage attached keys, Shell items), a title for a tab
/// without one, a scrolling row past four tabs, and tab or badge changes reaching the renderer without the heartbeat.</summary>
[Collection("renderer")]
public sealed class TabRowTests
{
	// A TabbedPage needs a dispatcher when it is built, before the harness exists.
	public TabRowTests()
	{
		if (Microsoft.Maui.Dispatching.DispatcherProvider.Current is not SailfishDispatcherProvider)
			Microsoft.Maui.Dispatching.DispatcherProvider.SetCurrent(new SailfishDispatcherProvider());
		SailfishDispatcherProvider.BindLoopThread();
	}

	private static ContentPage Page(string title) => new() { Title = title, Content = new Label { Text = title } };

	private static void Settle(RendererHarness h, int polls = 6)
	{
		for (var i = 0; i < polls; i++)
			h.Poll();
	}

	private static string TabsJson(RendererHarness h) =>
		System.Text.Json.JsonDocument.Parse(h.Shim.PageCalls.Last(c => c.Method == "setMauiTabs").Arg).RootElement.GetString()!;

	[Fact]
	public void A_tabbed_page_sends_each_tab_badge_and_a_change_re_pushes_it()
	{
		var inbox = Page("Inbox");
		TabbedPage.SetBadgeText(inbox, "3");
		TabbedPage.SetBadgeColor(inbox, Colors.Red);
		TabbedPage.SetBadgeTextColor(inbox, Colors.White);
		var tabbed = new TabbedPage { Children = { inbox, Page("Sent") } };
		using var h = new RendererHarness(tabbed);
		Settle(h);

		using (var doc = System.Text.Json.JsonDocument.Parse(TabsJson(h)))
		{
			var badges = doc.RootElement.GetProperty("badges");
			Assert.Equal(2, badges.GetArrayLength());
			Assert.Equal("3", badges[0].GetProperty("text").GetString());
			Assert.Equal("#FFFF0000", badges[0].GetProperty("bg").GetString());
			Assert.Equal("#FFFFFFFF", badges[0].GetProperty("fg").GetString());
			Assert.Equal(System.Text.Json.JsonValueKind.Null, badges[1].ValueKind);
		}

		var kicks = 0;
		h.Renderer.PollKick = () => kicks++;
		h.Renderer.KickedPoll();   // clears the latch
		TabbedPage.SetBadgeText(inbox, "4");
		Assert.Equal(1, kicks);
		Settle(h);
		Assert.Contains("\"text\":\"4\"", TabsJson(h));
	}

	[Fact]
	public void An_empty_badge_text_is_a_dot_and_no_badge_leaves_the_json_as_before()
	{
		var tabbed = new TabbedPage { Children = { Page("A"), Page("B") } };
		using var h = new RendererHarness(tabbed);
		Settle(h);
		Assert.DoesNotContain("badges", TabsJson(h));

		TabbedPage.SetBadgeText(tabbed.Children[1], "");
		Settle(h);
		Assert.Contains("\"badges\":[null,{\"text\":\"\",\"bg\":null,\"fg\":null}]", TabsJson(h));
	}

	[Fact]
	public void A_shell_section_badge_reaches_the_bottom_tabs()
	{
		var shell = new Shell();
		var alerts = new Tab { Title = "Alerts", BadgeText = "!", Items = { new ShellContent { Content = Page("Alerts") } } };
		shell.Items.Add(new TabBar { Items = { new Tab { Title = "Home", Items = { new ShellContent { Content = Page("Home") } } }, alerts } });
		using var h = new RendererHarness(shell);
		Settle(h);

		Assert.Contains("\"badges\":[null,{\"text\":\"!\",\"bg\":null,\"fg\":null}]", TabsJson(h));
	}

	private sealed class SettingsPage : ContentPage
	{
	}

	// A tab that relies on its icon: Silica's row is text only, so an empty title fell to an empty slot.
	[Fact]
	public void A_tab_without_a_title_falls_back_to_its_root_title_route_or_type()
	{
		var nav = new NavigationPage(Page("Feed"));
		var routed = new ContentPage { Content = new Label { Text = "x" } };
		Routing.SetRoute(routed, "profile");
		var plain = new SettingsPage();

		Assert.Equal("Feed", SailfishPageContainers.TabTitle(nav, nav.Title));
		Assert.Equal("profile", SailfishPageContainers.TabTitle(routed, routed.Title));
		Assert.Equal("SettingsPage", SailfishPageContainers.TabTitle(plain, plain.Title));
		Assert.Equal("Named", SailfishPageContainers.TabTitle(plain, "Named"));
	}

	[Fact]
	public void A_tab_added_at_runtime_shows_without_the_heartbeat()
	{
		var tabbed = new TabbedPage { Children = { Page("A"), Page("B") } };
		using var h = new RendererHarness(tabbed);
		Settle(h);

		var kicks = 0;
		h.Renderer.PollKick = () => kicks++;
		h.Renderer.KickedPoll();
		var added = Page("C");
		tabbed.Children.Add(added);
		Assert.Equal(1, kicks);
		Settle(h);
		Assert.Contains("\"titles\":[\"A\",\"B\",\"C\"]", TabsJson(h));

		// The new child is watched too.
		h.Renderer.KickedPoll();
		added.Title = "C2";
		Assert.Equal(2, kicks);
	}

	// Six tabs: the QML row flicks sideways; a horizontal drag on it must scroll the row, not switch the tab.
	[Fact]
	public void A_drag_on_a_scrolling_tab_row_is_not_a_tab_swipe()
	{
		var tabbed = new TabbedPage();
		for (var i = 1; i <= 6; i++)
			tabbed.Children.Add(Page("T" + i));
		using var h = new RendererHarness(tabbed);
		Settle(h);
		Assert.True(h.Renderer.TabRowScrolls);
		h.Renderer.ApplyWindowGeometry(
			$"{{\"pageWidth\":{QtHostUnits.ToQtUnits(360)},\"pageHeight\":{QtHostUnits.ToQtUnits(640)},\"headerHeight\":{QtHostUnits.ToQtUnits(140)},\"statusHeight\":0}}");
		Assert.InRange(h.Renderer.ChromeBottomDp, 139, 141);
		var loop = SailfishDispatcherProvider.BindLoopThread();
		var router = new QtHostInputRouter(h.Renderer, loop);

		void Drag(double yDp)
		{
			double X(double dp) => QtHostUnits.ToQtUnits(dp);
			router.OnPointer(0, X(250), X(yDp), 0, 0);
			for (var i = 1; i <= 10; i++)
				router.OnPointer(2, X(250 - 150.0 * i / 10), X(yDp), 0, 0);
			router.OnPointer(1, X(100), X(yDp), 0, 0);
			loop.DrainQueue();
		}

		Drag(120);   // on the tab row
		Assert.Equal(0, router.TabSwipes);
		Drag(400);   // on the page
		Assert.Equal(1, router.TabSwipes);
	}
}
