using Microsoft.Maui.Controls;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>Tracker S23 (plan M16 step 6, D15 a): Shell.FlyoutIsPresented = true opens the flyout entries as a Silica
/// ContextMenu; a pick runs its entry, closing the menu writes FlyoutIsPresented back to false.</summary>
[Collection("renderer")]
public sealed class ShellFlyoutPresentedTests
{
	private static void Settle(RendererHarness h, int polls = 6)
	{
		for (var i = 0; i < polls; i++)
			h.Poll();
	}

	private static ContentPage Page(string title) => new() { Title = title, Content = new Label { Text = title } };

	private static (RendererHarness Harness, Shell Shell) TwoItemShell()
	{
		var shell = new Shell();
		shell.Items.Add(new FlyoutItem { Title = "Home", Items = { new ShellContent { Content = Page("Home") } } });
		shell.Items.Add(new FlyoutItem { Title = "Settings", Items = { new ShellContent { Content = Page("Settings") } } });
		var h = new RendererHarness(shell);
		h.Shim.EvalHook = js => js.Contains("__openFlyoutMenu", StringComparison.Ordinal) ? "true"
			: js.Contains("__closeFlyoutMenu", StringComparison.Ordinal) ? "" : null;
		Settle(h);
		return (h, shell);
	}

	private static string? OpenCall(RendererHarness h) =>
		h.Shim.Evals.LastOrDefault(e => e.Contains("__openFlyoutMenu", StringComparison.Ordinal));

	[Fact]
	public void Presenting_the_flyout_opens_its_entries_and_a_pick_switches_the_item()
	{
		var (h, shell) = TwoItemShell();
		using var _h = h;
		Assert.Null(OpenCall(h));

		shell.FlyoutIsPresented = true;
		Settle(h);

		var open = OpenCall(h);
		Assert.NotNull(open);
		Assert.Contains("Home", open);
		Assert.Contains("Settings", open);
		Assert.Single(h.Shim.Evals, e => e.Contains("__openFlyoutMenu", StringComparison.Ordinal));   // opened once
		Assert.Contains(h.Shim.ByUri("context-menu"), m => !m.Destroyed);

		h.Renderer.HandleNativeEvent("context-activated", "{\"index\":1}");
		h.Renderer.HandleNativeEvent("context-closed", "{}");
		Settle(h);

		Assert.Equal("Settings", shell.CurrentItem?.Title);
		Assert.False(shell.FlyoutIsPresented);
	}

	[Fact]
	public void Dismissing_the_menu_writes_FlyoutIsPresented_back()
	{
		var (h, shell) = TwoItemShell();
		using var _h = h;
		shell.FlyoutIsPresented = true;
		Settle(h);

		h.Renderer.HandleNativeEvent("context-closed", "{}");
		Settle(h);

		Assert.False(shell.FlyoutIsPresented);
		Assert.Equal("Home", shell.CurrentItem?.Title);
	}

	[Fact]
	public void FlyoutIsPresented_false_from_code_closes_the_menu()
	{
		var (h, shell) = TwoItemShell();
		using var _h = h;
		shell.FlyoutIsPresented = true;
		Settle(h);

		shell.FlyoutIsPresented = false;
		Settle(h);

		Assert.Single(h.Shim.Evals, e => e.Contains("__closeFlyoutMenu", StringComparison.Ordinal));   // asked once

		// The menu reports its close; the next presentation opens it again.
		h.Renderer.HandleNativeEvent("context-closed", "{}");
		shell.FlyoutIsPresented = true;
		Settle(h);
		Assert.Equal(2, h.Shim.Evals.Count(e => e.Contains("__openFlyoutMenu", StringComparison.Ordinal)));
	}

	[Fact]
	public void A_disabled_flyout_is_not_presented()
	{
		var (h, shell) = TwoItemShell();
		using var _h = h;
		shell.FlyoutBehavior = FlyoutBehavior.Disabled;
		Settle(h);

		shell.FlyoutIsPresented = true;
		Settle(h);

		Assert.Null(OpenCall(h));
		Assert.False(shell.FlyoutIsPresented);
	}
}
