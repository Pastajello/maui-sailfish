using System.Text.Json;
using Microsoft.Maui.Controls;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>Tracker S02 (plan M5): a RefreshView on a page with a Silica pulley. The pulley owns the overscroll, so the pull
/// gesture never reached the RefreshView; the pulley now carries a Refresh entry that starts it.</summary>
[Collection("renderer")]
public sealed class RefreshPulleyTests
{
	private static (ContentPage Page, RefreshView Refresh) PageWithRefresh(bool toolbar)
	{
		var refresh = new RefreshView { Content = new ScrollView { Content = new Label { Text = "body" } } };
		var page = new ContentPage { Title = "T", Content = refresh };
		if (toolbar)
			page.ToolbarItems.Add(new ToolbarItem { Text = "Add" });
		return (page, refresh);
	}

	private static List<string> PullTexts(RendererHarness h)
	{
		var menu = h.Shim.ByUri("pull-down-menu").Single(m => !m.Destroyed);
		using var doc = JsonDocument.Parse(menu.Text("mauiItems")!);
		return doc.RootElement.EnumerateArray().Select(e => e.GetProperty("text").GetString()!).ToList();
	}

	private static bool LastBusy(RendererHarness h) =>
		h.Shim.Ops.Where(o => o.TryGetProperty("op", out var op) && op.GetString() == "busy")
			.Select(o => o.GetProperty("on").GetBoolean()).LastOrDefault();

	[Fact]
	public void A_page_with_toolbar_items_gets_a_refresh_entry_nearest_the_content()
	{
		var (page, _) = PageWithRefresh(toolbar: true);
		using var h = new RendererHarness(page);
		h.Poll();
		Assert.Equal(new[] { "Add", QtHostPageRenderer.PulleyRefreshText }, PullTexts(h));
	}

	[Fact]
	public void The_refresh_entry_starts_the_refresh_and_pulses_the_pulley()
	{
		var (page, refresh) = PageWithRefresh(toolbar: true);
		var refreshed = 0;
		refresh.Refreshing += (_, _) => refreshed++;
		using var h = new RendererHarness(page);
		h.Poll();
		var index = PullTexts(h).IndexOf(QtHostPageRenderer.PulleyRefreshText);

		h.Renderer.HandleNativeEvent("toolbar-activated", $"{{\"menu\":\"pull\",\"index\":{index}}}");
		h.Poll();
		Assert.True(refresh.IsRefreshing);
		Assert.Equal(1, refreshed);
		Assert.True(LastBusy(h));

		refresh.IsRefreshing = false;   // the app finished
		h.Poll();
		Assert.False(LastBusy(h));
	}

	[Fact]
	public void A_page_without_a_pulley_keeps_the_pull_gesture_and_gets_no_entry()
	{
		var (page, _) = PageWithRefresh(toolbar: false);
		using var h = new RendererHarness(page);
		h.Poll();
		Assert.DoesNotContain(h.Shim.ByUri("pull-down-menu"), m => !m.Destroyed);
	}

	[Fact]
	public void A_disabled_refresh_view_shows_a_disabled_entry()
	{
		var (page, refresh) = PageWithRefresh(toolbar: true);
		refresh.IsRefreshEnabled = false;
		using var h = new RendererHarness(page);
		h.Poll();
		var menu = h.Shim.ByUri("pull-down-menu").Single(m => !m.Destroyed);
		using var doc = JsonDocument.Parse(menu.Text("mauiItems")!);
		Assert.False(doc.RootElement.EnumerateArray().Last().GetProperty("enabled").GetBoolean());
	}
}
