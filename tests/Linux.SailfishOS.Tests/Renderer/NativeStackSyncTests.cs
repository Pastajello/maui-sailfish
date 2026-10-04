using Microsoft.Maui.Controls;
using Microsoft.Maui.SailfishOS.Platform;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>The renderer's native stack sync (NativeStackCoordinator through the renderer): one operation at a time,
/// confirmed by the native pageStack, a resync when the stacks disagree in a way no operation explains.</summary>
[Collection("renderer")]
public class NativeStackSyncTests
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

	// Profitocracy: Settings (a Shell tab) pushes Profiles with Navigation.PushAsync; the back gesture's MAUI pop never
	// completed, so after the timeout the resync pushed Profiles back on screen and the tab row was gone.
	[Fact]
	public void A_back_gesture_pops_a_page_pushed_in_a_shell_tab()
	{
		var home = Page("Home");
		var settings = Page("Settings");
		var shell = new Shell();
		var bar = new TabBar();
		bar.Items.Add(new Tab { Title = "Home", Items = { new ShellContent { Content = home } } });
		bar.Items.Add(new Tab { Title = "Settings", Items = { new ShellContent { Content = settings } } });
		shell.Items.Add(bar);
		using var h = new RendererHarness(shell);
		shell.CurrentItem.CurrentItem = (ShellSection)bar.Items[1];
		Settle(h);
		var pushed = settings.Navigation.PushAsync(Page("Profiles"));
		Settle(h);
		Assert.True(pushed.IsCompleted);
		Assert.Equal(2, h.Shim.Pages.Count);

		var navigated = 0;
		shell.Navigated += (_, _) => navigated++;
		h.Shim.Pages.RemoveAt(h.Shim.Pages.Count - 1);   // Silica's back gesture
		Settle(h, 10);

		Assert.True(navigated > 0, "the shell never completed the pop");
		Assert.Single(shell.CurrentItem.CurrentItem.Navigation.NavigationStack);
		Assert.Equal(new[] { "mp1" }, h.Shim.Pages);
		Assert.Equal(0, h.Renderer.NavResyncs);
		Assert.Contains(h.Shim.ByUri("label"), o => o.Page == "mp1" && o.Text("text") == "Settings");
	}

	// WhatToEat: the results page's Loaded handler calls searchBar.SetSemanticFocus(). The plain-net Controls fire Loaded
	// as the page joins the window, before the renderer attached a handler, so it threw inside MAUI's push: no
	// DescendantAdded, no Navigated, and the page reached the screen only on the 2 s heartbeat.
	[Fact]
	public void A_shell_route_page_has_its_handlers_when_Loaded_fires()
	{
		Routing.RegisterRoute("loadeddetail", typeof(LoadedFocusPage));
		var shell = new Shell();
		// As AppShell.xaml's <TabBar><ShellContent/></TabBar>: an implicit section around the content.
		shell.Items.Add(new TabBar { Items = { (ShellSection)new ShellContent { Title = "Home", ContentTemplate = new DataTemplate(() => Page("Home")) } } });
		using var h = new RendererHarness(shell);
		Settle(h);
		var kicks = 0;
		var previous = h.Renderer.PollKick;
		var loop = SailfishDispatcherProvider.BindLoopThread();
		h.Renderer.PollKick = () => { kicks++; loop.Dispatch(h.Renderer.KickedPoll); };   // as the app loop
		try
		{
			var navigation = shell.GoToAsync("loadeddetail");
			for (var i = 0; i < 5; i++)
				loop.DrainQueue();   // loop turns only: no heartbeat poll

			var page = Assert.IsType<LoadedFocusPage>(shell.CurrentPage);
			Assert.Null(page.LoadedError);
			Assert.False(navigation.IsFaulted, navigation.Exception?.ToString());
			Assert.True(kicks > 0, "the route push did not request a sync");
			Assert.Equal(2, h.Shim.Pages.Count);
		}
		finally
		{
			h.Renderer.PollKick = previous;
			Routing.UnRegisterRoute("loadeddetail");
		}
	}

	// DeveloperBalance: a ShellContent whose page the services return marks it service-created and builds it again each
	// time its section comes back; the overlay returned every page type, so the dashboard was rebuilt on the flyout
	// switch, re-applied its SelectedItem binding and ran the app's navigation command with a stale selection.
	[Fact]
	public void A_templated_shell_page_is_kept_across_section_switches()
	{
		var shell = new Shell();
		shell.Items.Add(new FlyoutItem { Title = "Dashboard", Items = { (ShellSection)new ShellContent { ContentTemplate = new DataTemplate(typeof(TemplatedHomePage)) } } });
		shell.Items.Add(new FlyoutItem { Title = "Meta", Items = { (ShellSection)new ShellContent { ContentTemplate = new DataTemplate(typeof(TemplatedMetaPage)) } } });
		using var h = new RendererHarness(shell);
		Settle(h);
		var home = Assert.IsType<TemplatedHomePage>(shell.CurrentPage);

		shell.CurrentItem = shell.Items[1];
		Settle(h);
		Assert.IsType<TemplatedMetaPage>(shell.CurrentPage);
		shell.CurrentItem = shell.Items[0];
		Settle(h);

		Assert.Same(home, shell.CurrentPage);
	}

	private sealed class TemplatedHomePage : ContentPage
	{
		public TemplatedHomePage() => Content = new Label { Text = "Home" };
	}

	private sealed class TemplatedMetaPage : ContentPage
	{
		public TemplatedMetaPage() => Content = new Label { Text = "Meta" };
	}

	private sealed class LoadedFocusPage : ContentPage
	{
		public Exception? LoadedError { get; private set; }

		public LoadedFocusPage()
		{
			Title = "Home";   // the same title: no toolbar update to kick the renderer by accident
			var search = new SearchBar();
			Content = new VerticalStackLayout { Children = { search, new Label { Text = "Detail" } } };
			Loaded += (_, _) =>
			{
				try { search.SetSemanticFocus(); }
				catch (Exception ex) { LoadedError = ex; throw; }
			};
		}
	}

	// Profitocracy: a ContentTemplate tab page was dropped from the page cache on every push ("the app no longer holds
	// the page"), so each back gesture rebuilt all of Settings.
	[Fact]
	public void A_templated_shell_tab_page_comes_back_from_the_cache()
	{
		var shell = new Shell();
		var tab = new Tab { Title = "Settings", Items = { new ShellContent { ContentTemplate = new DataTemplate(() => Page("Settings")) } } };
		shell.Items.Add(new TabBar { Items = { tab } });
		using var h = new RendererHarness(shell);
		Settle(h);
		var before = h.Shim.ByUri("label").Single(o => o.Text("text") == "Settings").Handle;

		_ = tab.Navigation.PushAsync(Page("Profiles"));
		Settle(h);
		h.Shim.Pages.RemoveAt(h.Shim.Pages.Count - 1);   // Silica's back gesture
		Settle(h, 10);

		Assert.Single(tab.Navigation.NavigationStack);
		Assert.Equal(before, h.Shim.ByUri("label").Single(o => o.Text("text") == "Settings").Handle);
	}

	// Profitocracy: the Transactions tab holds two ShellContents (All, Recurring); with no top tabs Recurring was
	// unreachable.
	[Fact]
	public void A_shell_section_with_several_contents_gets_a_second_tab_row()
	{
		var shell = new Shell();
		var transactions = new Tab
		{
			Title = "Transactions",
			Items =
			{
				new ShellContent { Title = "All", Content = Page("All") },
				new ShellContent { Title = "Recurring", Content = Page("Recurring") },
			},
		};
		shell.Items.Add(new TabBar { Items = { new Tab { Title = "Home", Items = { new ShellContent { Content = Page("Home") } } }, transactions } });
		using var h = new RendererHarness(shell);
		shell.CurrentItem.CurrentItem = transactions;
		Settle(h);

		var tabs = h.Shim.PageCalls.Last(c => c.Method == "setMauiTabs").Arg;   // called on the page directly (invoke)
		Assert.Contains("\\\"sub\\\":{\\\"titles\\\":[\\\"All\\\",\\\"Recurring\\\"],\\\"index\\\":0}", tabs);

		h.Renderer.HandleNativeEvent("tab-selected", "{\"index\":1,\"level\":1}");
		Settle(h);
		Assert.Equal("Recurring", transactions.CurrentItem.Title);
		Assert.Equal(transactions, shell.CurrentItem.CurrentItem);
	}

	// Kitchen recipe detail: the data set while the push slid in reached QML as text, but measure and arrange waited
	// for the transition's end, so the title drew on one overflowing line for 0.4 s and then the page jumped.
	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void A_pushed_page_lays_out_while_it_slides_in(bool pushRenders)
	{
		var previous = QtHostPageRenderer.PushTransitionRenders;
		QtHostPageRenderer.PushTransitionRenders = pushRenders;
		try
		{
			var nav = new NavigationPage(Page("Home"));
			using var h = new RendererHarness(nav);
			var title = new Label { Text = "Detail" };
			_ = nav.PushAsync(new ContentPage { Title = "Detail", Content = new VerticalStackLayout { WidthRequest = 300, Children = { title } } });
			h.Poll();                                    // the native push and the page's first hosts
			h.Shim.StackBusy = true;                     // its slide-in
			h.Poll();
			var host = h.Shim.ByUri("label").Single(o => o.Text("text") == "Detail");
			var oneLine = host.Geometry.Height;

			title.Text = string.Join(' ', Enumerable.Repeat("a long recipe name", 8));   // the view model's data
			Settle(h, 3);
			Assert.Equal(pushRenders, host.Geometry.Height > oneLine * 2);

			h.Shim.StackBusy = false;
			Settle(h, 3);
			Assert.True(host.Geometry.Height > oneLine * 2);
		}
		finally
		{
			QtHostPageRenderer.PushTransitionRenders = previous;
		}
	}

	// Profitocracy, Settings → Theme → back: the revealed Settings (with its taller tab header) sent a window report
	// while the back transition still ran; that reconcile drew Theme onto its dying model page, the dead handles reset
	// the whole page and Settings flashed empty under the "Theme" title.
	[Fact]
	public void A_window_report_during_a_back_transition_leaves_the_revealed_page_alone()
	{
		var nav = new NavigationPage(Page("Settings"));
		using var h = new RendererHarness(nav);
		_ = nav.PushAsync(Page("Theme"));
		Settle(h);
		var settings = h.Shim.ByUri("label").Single(o => o.Text("text") == "Settings").Handle;
		var destroys = h.Shim.Destroys;

		h.Shim.StackBusy = true;                         // the back gesture's transition
		h.Shim.Pages.RemoveAt(h.Shim.Pages.Count - 1);
		h.Poll();
		var reconciles = h.Renderer.ReconcileCount;
		h.Renderer.HandleNativeEvent("window-geometry",
			"{\"pageWidth\":1080,\"pageHeight\":2160,\"headerHeight\":230,\"statusHeight\":40}");
		Assert.Equal(reconciles, h.Renderer.ReconcileCount);   // the report waits for the transition (one gate)

		h.Shim.StackBusy = false;
		Settle(h, 10);
		Assert.Single(nav.Navigation.NavigationStack);
		Assert.Equal(settings, h.Shim.ByUri("label").Single(o => o.Text("text") == "Settings").Handle);
		Assert.Equal(0, h.Renderer.NavResyncs);
		Assert.True(h.Shim.Destroys - destroys <= 1, $"destroys {h.Shim.Destroys - destroys}");   // Theme's own host at most
	}

	// Profitocracy: after Settings → Profiles → back, the stray sweep destroyed Home's hosts parked on the same model
	// page, and the next tap on the Home tab reset the whole page.
	[Fact]
	public void A_back_gesture_keeps_the_other_tabs_parked_hosts()
	{
		var home = new Tab { Title = "Home", Items = { new ShellContent { Content = Page("Home") } } };
		var settingsPage = Page("Settings");
		var settings = new Tab { Title = "Settings", Items = { new ShellContent { Content = settingsPage } } };
		var shell = new Shell();
		shell.Items.Add(new TabBar { Items = { home, settings } });
		using var h = new RendererHarness(shell);
		Settle(h);
		var homeLabel = h.Shim.ByUri("label").Single(o => o.Text("text") == "Home").Handle;
		shell.CurrentItem.CurrentItem = settings;
		Settle(h);
		_ = settingsPage.Navigation.PushAsync(Page("Profiles"));
		Settle(h);
		h.Shim.Pages.RemoveAt(h.Shim.Pages.Count - 1);   // Silica's back gesture
		Settle(h, 10);

		shell.CurrentItem.CurrentItem = home;
		Settle(h);
		Assert.Equal(homeLabel, h.Shim.ByUri("label").Single(o => o.Text("text") == "Home").Handle);
	}

	// Profitocracy: Home reloads in NavigatedTo; after adding a transaction, tapping the Home tab showed the old totals
	// because a tab switch set CurrentItem directly instead of navigating as the platform tab bars do.
	[Fact]
	public void A_tab_tap_navigates_so_the_page_gets_navigated_to()
	{
		var homePage = Page("Home");
		var navigatedTo = 0;
		homePage.NavigatedTo += (_, _) => navigatedTo++;
		var home = new Tab { Title = "Home", Items = { new ShellContent { Content = homePage } } };
		var settings = new Tab { Title = "Settings", Items = { new ShellContent { Content = Page("Settings") } } };
		var shell = new Shell();
		shell.Items.Add(new TabBar { Items = { home, settings } });
		var navigated = 0;
		shell.Navigated += (_, _) => navigated++;
		using var h = new RendererHarness(shell);
		Settle(h);

		h.Renderer.HandleNativeEvent("tab-selected", "{\"index\":1}");
		Settle(h);
		var before = (navigatedTo, navigated);
		h.Renderer.HandleNativeEvent("tab-selected", "{\"index\":0}");
		Settle(h);

		Assert.Same(home, shell.CurrentItem.CurrentItem);
		Assert.True(navigated > before.navigated, "Shell.Navigated");
		Assert.True(navigatedTo > before.navigatedTo, $"Page.NavigatedTo loaded={homePage.IsLoaded} window={homePage.Window is not null} current={shell.CurrentPage?.Title} nt={navigatedTo}");
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
