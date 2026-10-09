using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.SailfishOS.Platform;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>Tracker S15 (plan M15 steps 5–6, M17 I7): Shell and Compatibility-layout changes reach the renderer at
/// once, ContentPage.HideSoftInputOnTapped, context menus with sub items and separators, icon-only ToolbarItems, and
/// the navigation hold an open context menu takes being released when it closes.</summary>
[Collection("renderer")]
public sealed class ChangeNotificationTests
{
	public ChangeNotificationTests()
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

	[Fact]
	public void A_flyout_item_added_to_a_shell_at_runtime_requests_the_sync_at_once()
	{
		var shell = new Shell();
		shell.Items.Add(new FlyoutItem { Title = "Home", Items = { new ShellContent { Content = Page("Home") } } });
		using var h = new RendererHarness(shell);
		Settle(h);

		var kicks = 0;
		h.Renderer.PollKick = () => kicks++;
		h.Renderer.KickedPoll();   // clears the latch
		shell.Items.Add(new FlyoutItem { Title = "Settings", Items = { new ShellContent { Content = Page("Settings") } } });
		Assert.True(kicks > 0, "a new flyout item waited for the heartbeat");

		Settle(h);
		var pull = h.Shim.ByUri("pull-down-menu").Last().Text("mauiItems");
		Assert.Contains("Settings", pull);
	}

	[Fact]
	public void Turning_the_shell_flyout_off_requests_the_sync_at_once()
	{
		var shell = new Shell();
		shell.Items.Add(new FlyoutItem { Title = "Home", Items = { new ShellContent { Content = Page("Home") } } });
		shell.Items.Add(new FlyoutItem { Title = "Other", Items = { new ShellContent { Content = Page("Other") } } });
		using var h = new RendererHarness(shell);
		Settle(h);

		var kicks = 0;
		h.Renderer.PollKick = () => kicks++;
		h.Renderer.KickedPoll();
		shell.FlyoutBehavior = FlyoutBehavior.Disabled;
		Assert.True(kicks > 0, "FlyoutBehavior waited for the heartbeat");
	}

#pragma warning disable CS0618   // Compatibility layouts are obsolete; apps still ship them
	[Fact]
	public void A_child_added_to_a_compatibility_layout_reconciles_its_subtree()
	{
		// As UseMauiCompatibility() does; the check is internal to Controls.
		var check = typeof(Shell).Assembly.GetType("Microsoft.Maui.Controls.Hosting.CompatibilityCheck", throwOnError: true)!;
		check.GetMethod("UseCompatibility", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, null);
		try
		{
			var stack = new Microsoft.Maui.Controls.Compatibility.StackLayout { Children = { new Label { Text = "A" } } };
			using var h = new RendererHarness(new ContentPage { Title = "Compat", Content = stack });
			Settle(h);

			var subtrees = h.Renderer.SubtreeReconciles;
			stack.Children.Add(new Label { Text = "B" });
			Assert.Equal(subtrees + 1, h.Renderer.SubtreeReconciles);
			Assert.Contains("B", h.Shim.ByUri("label").Select(o => o.Text("text")));
		}
		finally
		{
			check.GetMethod("ResetCompatibilityCheck", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, null);
		}
	}
#pragma warning restore CS0618

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void A_press_outside_the_focused_entry_hides_the_keyboard_only_when_the_page_asks(bool hide)
	{
		var entry = new Entry { Text = "x" };
		var box = new BoxView { HeightRequest = 300, Color = Colors.Blue };
		var page = new ContentPage { Title = "Input", HideSoftInputOnTapped = hide, Content = new VerticalStackLayout { Children = { entry, box } } };
		using var h = new RendererHarness(page);
		Settle(h);
		var entryHost = h.Renderer.CurrentHosts.First(x => ReferenceEquals(x.Element, entry));
		h.Renderer.HandleNativeEvent("focus-changed", $"{{\"id\":\"{entryHost.Id}\",\"focused\":true}}");
		Assert.True(entry.IsFocused);

		var loop = SailfishDispatcherProvider.BindLoopThread();
		var router = new QtHostInputRouter(h.Renderer, loop);
		var area = h.Renderer.CurrentHosts.First(x => ReferenceEquals(x.Element, box)).MauiLogicalBounds;
		router.OnPointer(0, QtHostUnits.ToQtUnits(area.Center.X), QtHostUnits.ToQtUnits(area.Center.Y), 0, 0);
		router.OnPointer(1, QtHostUnits.ToQtUnits(area.Center.X), QtHostUnits.ToQtUnits(area.Center.Y), 0, 0);
		loop.DrainQueue();

		Assert.Equal(!hide, entry.IsFocused);
		Assert.Equal(hide ? 1 : 0, router.SoftInputHides);
	}

	[Fact]
	public void A_press_on_the_entry_itself_keeps_the_keyboard()
	{
		var entry = new Entry { Text = "x", HeightRequest = 80 };
		var page = new ContentPage { Title = "Input", HideSoftInputOnTapped = true, Content = new VerticalStackLayout { Children = { entry } } };
		using var h = new RendererHarness(page);
		Settle(h);
		var entryHost = h.Renderer.CurrentHosts.First(x => ReferenceEquals(x.Element, entry));
		h.Renderer.HandleNativeEvent("focus-changed", $"{{\"id\":\"{entryHost.Id}\",\"focused\":true}}");

		var loop = SailfishDispatcherProvider.BindLoopThread();
		var router = new QtHostInputRouter(h.Renderer, loop);
		var area = entryHost.MauiLogicalBounds;
		router.OnPointer(0, QtHostUnits.ToQtUnits(area.Center.X), QtHostUnits.ToQtUnits(area.Center.Y), 0, 0);
		router.OnPointer(1, QtHostUnits.ToQtUnits(area.Center.X), QtHostUnits.ToQtUnits(area.Center.Y), 0, 0);
		loop.DrainQueue();

		Assert.True(entry.IsFocused);
		Assert.Equal(0, router.SoftInputHides);
	}

	[Fact]
	public void A_sub_item_opens_as_a_label_row_with_its_items_and_separators_are_dropped()
	{
		var picked = new List<string>();
		MenuFlyoutItem Item(string text)
		{
			var item = new MenuFlyoutItem { Text = text };
			item.Clicked += (_, _) => picked.Add(text);
			return item;
		}
		MenuFlyoutSubItem Sub()
		{
			var sub = new MenuFlyoutSubItem { Text = "Share" };
			sub.Add(Item("Mail"));
			sub.Add(Item("Message"));
			return sub;
		}
		var flyout = new MenuFlyout
		{
			Item("Copy"),
			new MenuFlyoutSeparator(),
			Sub(),
		};

		var rows = QtHostPageRenderer.ContextEntries(flyout);
		Assert.Equal(new[] { "Copy", "Share", "Mail", "Message" }, rows.Select(r => r.Text));
		Assert.Null(rows[1].Item);
		Assert.False(rows[1].Enabled);

		var label = new Label { Text = "hold me" };
		FlyoutBase.SetContextFlyout(label, flyout);
		using var h = new RendererHarness(new ContentPage { Title = "Menu", Content = label });
		Settle(h);
		var host = h.Renderer.CurrentHosts.First(x => ReferenceEquals(x.Element, label));
		string? opened = null;
		h.Shim.EvalHook = js =>
		{
			if (!js.Contains("__openContextMenu", StringComparison.Ordinal))
				return null;
			opened = js;
			return "true";
		};
		h.Renderer.FireContextMenu(host.Id, flyout);
		Assert.Contains("{\\\"text\\\":\\\"Share\\\",\\\"enabled\\\":false,\\\"label\\\":true}", opened);
		h.Renderer.HandleNativeEvent("context-activated", "{\"id\":\"ctx\",\"index\":3}");
		Assert.Equal(new[] { "Message" }, picked);
	}

	// The open menu held the native stack sync and nothing released it: an "Edit" entry that pushes a page (the common
	// case) left the push waiting for good ("navigation … waits: flyout=True").
	[Fact]
	public void A_closed_context_menu_releases_the_navigation_it_held()
	{
		var nav = new NavigationPage(Page("Root"));
		var label = new Label { Text = "hold me" };
		var edit = new MenuFlyoutItem { Text = "Edit" };
		edit.Clicked += (_, _) => _ = nav.PushAsync(Page("Editor"));
		FlyoutBase.SetContextFlyout(label, new MenuFlyout { edit });
		((ContentPage)nav.RootPage).Content = label;
		using var h = new RendererHarness(nav);
		Settle(h);
		var host = h.Renderer.CurrentHosts.First(x => ReferenceEquals(x.Element, label));
		h.Shim.EvalHook = js => js.Contains("__openContextMenu", StringComparison.Ordinal) ? "true" : null;
		h.Renderer.FireContextMenu(host.Id, (MenuFlyout)FlyoutBase.GetContextFlyout(label));

		h.Renderer.HandleNativeEvent("context-closed", "{\"id\":\"ctx\"}");   // Silica: the menu closes, then the pick
		h.Renderer.HandleNativeEvent("context-activated", "{\"id\":\"ctx\",\"index\":0}");
		Settle(h, 10);

		Assert.Equal(2, nav.Navigation.NavigationStack.Count);
		Assert.Equal(2, h.Shim.Pages.Count);
	}

	[Fact]
	public void An_icon_only_toolbar_item_gets_a_text_for_the_pulley()
	{
		Assert.Equal("Save", QtHostPageRenderer.ToolbarText(new ToolbarItem { Text = "Save", AutomationId = "save" }));
		Assert.Equal("refresh", QtHostPageRenderer.ToolbarText(new ToolbarItem { AutomationId = "refresh", IconImageSource = "r.png" }));
		var described = new ToolbarItem { IconImageSource = "x.png" };
		SemanticProperties.SetDescription(described, "Delete all");
		Assert.Equal("Delete all", QtHostPageRenderer.ToolbarText(described));
		Assert.Equal("ic_add", QtHostPageRenderer.ToolbarText(new ToolbarItem { IconImageSource = "ic_add.png" }));
		Assert.Equal(string.Empty, QtHostPageRenderer.ToolbarText(new ToolbarItem()));
	}
}
