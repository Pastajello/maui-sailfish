using System.Text.Json;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.SailfishOS.Handlers;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>Tracker S19 (plan M16 steps 1–2, D16 a): MAUI's toolbar gets a Sailfish handler and drives the pulley
/// (Priority order, the Shell's items, a change at runtime).</summary>
[Collection("renderer")]
public sealed class ToolbarHandlerTests
{
	private static void Settle(RendererHarness h, int polls = 6)
	{
		for (var i = 0; i < polls; i++)
			h.Poll();
	}

	private static List<string> PullTexts(RendererHarness h)
	{
		var menu = h.Shim.ByUri("pull-down-menu").Last(m => !m.Destroyed);
		using var doc = JsonDocument.Parse(menu.Text("mauiItems")!);
		return doc.RootElement.EnumerateArray().Select(e => e.GetProperty("text").GetString()!).ToList();
	}

	[Fact]
	public void A_navigation_page_toolbar_gets_the_sailfish_handler()
	{
		var nav = new NavigationPage(new ContentPage { Title = "Root", Content = new Label { Text = "root" } });
		using var h = new RendererHarness(nav);
		Settle(h);

		var toolbar = ((IToolbarElement)h.Window).Toolbar;
		Assert.NotNull(toolbar);
		Assert.IsType<SailfishToolbarHandler>(toolbar!.Handler);
	}

	[Fact]
	public void Pulley_entries_follow_priority_as_on_the_other_platforms()
	{
		var page = new ContentPage { Title = "Root", Content = new Label { Text = "root" } };
		page.ToolbarItems.Add(new ToolbarItem { Text = "Later", Priority = 2 });
		page.ToolbarItems.Add(new ToolbarItem { Text = "First", Priority = 0 });
		page.ToolbarItems.Add(new ToolbarItem { Text = "Middle", Priority = 1 });
		using var h = new RendererHarness(new NavigationPage(page));
		Settle(h);

		Assert.Equal(new[] { "First", "Middle", "Later" }, PullTexts(h));
	}

	[Fact]
	public void A_shell_toolbar_item_shows_on_its_pages()
	{
		var page = new ContentPage { Title = "Home", Content = new Label { Text = "home" } };
		page.ToolbarItems.Add(new ToolbarItem { Text = "Local" });
		var shell = new Shell();
		shell.ToolbarItems.Add(new ToolbarItem { Text = "Global" });
		shell.Items.Add(new ShellContent { Content = page });
		using var h = new RendererHarness(shell);
		Settle(h);

		Assert.IsType<SailfishToolbarHandler>(((IToolbarElement)shell).Toolbar?.Handler);
		var texts = PullTexts(h);
		Assert.Contains("Local", texts);
		Assert.Contains("Global", texts);
	}

	[Fact]
	public void An_item_added_at_runtime_reaches_the_pulley()
	{
		var page = new ContentPage { Title = "Root", Content = new Label { Text = "root" } };
		page.ToolbarItems.Add(new ToolbarItem { Text = "One" });
		using var h = new RendererHarness(new NavigationPage(page));
		Settle(h);

		page.ToolbarItems.Add(new ToolbarItem { Text = "Two", Priority = -1 });
		Settle(h);

		Assert.Equal(new[] { "Two", "One" }, PullTexts(h));
	}
}
