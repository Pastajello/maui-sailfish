using Microsoft.Maui.Controls;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Shell leg (MAUI_SAILFISH_QT_HOST_SHELL_DIAG=1): swaps the window root for a Shell, then
/// TabbedPage and FlyoutPage roots, and checks rendering, route pushes, native back, and
/// flyout/tab switching.
/// </summary>
internal sealed partial class QtHostDiagnosticsRunner
{
	private bool _qtShellDiag;
	private readonly DiagChecks _qtShellChecks = new("Qt shell diag");

	private const string ShellDetailRoute = "sfshelldetail";

	private sealed class ShellDetailPage : ContentPage
	{
		public ShellDetailPage()
		{
			Title = "Shell Detail";
			Content = new VerticalStackLayout { Padding = new Thickness(16), Children = { new Label { Text = "shell detail body" } } };
		}
	}


	private static ShellContent ShellSectionContent(string title, string body) => new()
	{
		Title = title,
		ContentTemplate = new DataTemplate(() => new ContentPage
		{
			Title = title,
			Content = new VerticalStackLayout { Padding = new Thickness(16), Children = { new Label { Text = body } } },
		}),
	};

	/// <summary>The rendered page's pulley entries (last applied mauiItems), or null without a pulley.</summary>
	private static List<string>? PulleyTexts(QtHost.QtHostPageRenderer renderer)
	{
		var host = renderer.CurrentHosts.FirstOrDefault(h => h.QmlUri == "pull-down-menu");
		if (host is null)
		{
			Console.Error.WriteLine("[Sailfish] Qt shell diag: no pull-down-menu host on the rendered page");
			return null;
		}
		if (!host.AppliedProperties.TryGetValue("mauiItems", out var json))
		{
			Console.Error.WriteLine($"[Sailfish] Qt shell diag: pulley host {host} has no applied mauiItems (keys: {string.Join(",", host.AppliedProperties.Keys)})");
			return null;
		}
		try
		{
			using var doc = System.Text.Json.JsonDocument.Parse(json);
			var root = doc.RootElement;
			if (root.ValueKind == System.Text.Json.JsonValueKind.String)
			{
				using var inner = System.Text.Json.JsonDocument.Parse(root.GetString()!);
				return inner.RootElement.EnumerateArray().Select(e => e.GetProperty("text").GetString() ?? "").ToList();
			}
			return root.EnumerateArray().Select(e => e.GetProperty("text").GetString() ?? "").ToList();
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[Sailfish] Qt shell diag: mauiItems unreadable ({ex.Message}): {json}");
			return null;
		}
	}

	private double _shellContentTopA = double.NaN;

	/// <summary>Top of the page's root content host in dp; moves down when a tab bar is shown.</summary>
	private static double ContentTop(QtHost.QtHostPageRenderer renderer) =>
		renderer.CurrentPage is ContentPage { Content: { } content } &&
		renderer.Cache.TryGet(content, out var host) && host is not null
			? host.MauiLogicalBounds.Y
			: double.NaN;

	private static string TabState() =>
		QtHost.QtHostRuntime.Eval("(function(){var p=pageStack.currentPage;return p&&p.mauiTabs!==undefined?(p.mauiTabs.join(',')+'@'+p.mauiTabIndex):'';})()");

	private static bool PageShows(QtHost.QtHostPageRenderer renderer, string title, string body) =>
		renderer.CurrentPage is ContentPage { Title: var t } page && t == title &&
		renderer.CurrentHosts.Any(h => h.IsAttached && h.Element is Label { Text: var text } && text == body);

	private void RunQtShellDiagnostics(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		if (_context.Window is not Microsoft.Maui.Controls.Window window)
		{
			Console.Error.WriteLine("[Sailfish] Qt shell diag: no Controls Window — leg skipped; auto-shutdown");
			QtHost.QtHostRuntime.Shutdown();
			return;
		}
		Routing.RegisterRoute(ShellDetailRoute, typeof(ShellDetailPage));
		var shell = new Shell
		{
			Items =
			{
				new FlyoutItem { Title = "Alpha", Items = { ShellSectionContent("Shell Alpha", "alpha body") } },
				new FlyoutItem { Title = "Beta", Items = { ShellSectionContent("Shell Beta", "beta body") } },
				new TabBar
				{
					Title = "Tabs",
					Items =
					{
						new Tab { Title = "One", Items = { ShellSectionContent("Tab One", "tab one body") } },
						new Tab { Title = "Two", Items = { ShellSectionContent("Tab Two", "tab two body") } },
					},
				},
			},
		};
		Console.Error.WriteLine("[Sailfish] Qt shell diag: leg A — window root → Shell (2 flyout sections)");
		window.Page = shell;
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1800), () => VerifyShellLegA(renderer, dispatcher, shell));
	}

	private void VerifyShellLegA(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, Shell shell)
	{
		_qtShellChecks.Check($"leg A root: '{(renderer.CurrentPage as ContentPage)?.Title}'=='Shell Alpha' rendered with its body label, native depth {renderer.NativePageIds.Count}==1",
			PageShows(renderer, "Shell Alpha", "alpha body") && renderer.NativePageIds.Count == 1);
		_shellContentTopA = ContentTop(renderer);
		Console.Error.WriteLine($"[Sailfish] Qt shell diag: leg B — GoToAsync('{ShellDetailRoute}')");
		_ = shell.GoToAsync(ShellDetailRoute);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1800), () => VerifyShellLegB(renderer, dispatcher, shell));
	}

	private void VerifyShellLegB(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, Shell shell)
	{
		var sectionDepth = shell.CurrentItem?.CurrentItem?.Navigation.NavigationStack.Count ?? -1;
		_qtShellChecks.Check($"leg B route push: '{(renderer.CurrentPage as ContentPage)?.Title}'=='Shell Detail' rendered, section stack {sectionDepth}==2, native depth {renderer.NativePageIds.Count}==2",
			PageShows(renderer, "Shell Detail", "shell detail body") && sectionDepth == 2 && renderer.NativePageIds.Count == 2);
		var detailPulley = PulleyTexts(renderer);
		_qtShellChecks.Check($"leg B no flyout below the section root: pulley entries [{string.Join(",", detailPulley ?? new())}] carry no 'Alpha'/'Beta'",
			detailPulley is null || (!detailPulley.Contains("Alpha") && !detailPulley.Contains("Beta")));
		Console.Error.WriteLine("[Sailfish] Qt shell diag: leg C — native back gesture (pageStack.pop) — the section must pop");
		QtHost.QtHostRuntime.PopPage(immediate: true);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1800), () => VerifyShellLegC(renderer, dispatcher, shell));
	}

	private void VerifyShellLegC(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, Shell shell)
	{
		var sectionDepth = shell.CurrentItem?.CurrentItem?.Navigation.NavigationStack.Count ?? -1;
		_qtShellChecks.Check($"leg C native pop: section stack {sectionDepth}==1, '{(renderer.CurrentPage as ContentPage)?.Title}'=='Shell Alpha' rendered again, native depth {renderer.NativePageIds.Count}==1",
			sectionDepth == 1 && PageShows(renderer, "Shell Alpha", "alpha body") && renderer.NativePageIds.Count == 1);
		Console.Error.WriteLine("[Sailfish] Qt shell diag: leg D — Shell.CurrentItem → Beta");
		shell.CurrentItem = shell.Items[1];
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1800), () => VerifyShellLegD(renderer, shell));
	}

	private void VerifyShellLegD(QtHost.QtHostPageRenderer renderer, Shell shell)
	{
		_qtShellChecks.Check($"leg D section switch: '{(renderer.CurrentPage as ContentPage)?.Title}'=='Shell Beta' rendered with its body, native depth {renderer.NativePageIds.Count}==1",
			PageShows(renderer, "Shell Beta", "beta body") && renderer.NativePageIds.Count == 1);
		// Leg E: the section root shows the flyout as its pulley; a pick switches the section.
		var pulley = PulleyTexts(renderer);
		_qtShellChecks.Check($"leg E flyout pulley: entries [{string.Join(",", pulley ?? new())}] start with Alpha,Beta",
			pulley is { Count: >= 2 } && pulley[0] == "Alpha" && pulley[1] == "Beta");
		// Leg E2 (tracker S15): a flyout item added at runtime reaches the pulley at once, not at the 2 s heartbeat.
		var gamma = new FlyoutItem { Title = "Gamma", Items = { ShellSectionContent("Shell Gamma", "gamma body") } };
		Console.Error.WriteLine("[Sailfish] Qt shell diag: leg E2 — Shell.Items.Add('Gamma') at runtime");
		shell.Items.Add(gamma);
		_context.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(500), () =>
		{
			var added = PulleyTexts(renderer);
			_qtShellChecks.Check($"leg E2 runtime flyout item: pulley [{string.Join(",", added ?? new())}] has 'Gamma' 500 ms after Items.Add",
				added?.Contains("Gamma") == true);
			shell.Items.Remove(gamma);
			_context.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(500), () =>
			{
				var removed = PulleyTexts(renderer);
				_qtShellChecks.Check($"leg E2 removed again: pulley [{string.Join(",", removed ?? new())}] without 'Gamma' 500 ms after Items.Remove",
					removed is not null && !removed.Contains("Gamma"));
				Console.Error.WriteLine("[Sailfish] Qt shell diag: leg E — pulley pick 'Alpha' (toolbar-activated index 0)");
				renderer.HandleNativeEvent("toolbar-activated", "{\"menu\":\"pull\",\"index\":0}");
				_context.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1800), () => VerifyShellLegE(renderer, shell));
			});
		});
	}

	private void VerifyShellLegE(QtHost.QtHostPageRenderer renderer, Shell shell)
	{
		_qtShellChecks.Check($"leg E pulley pick: CurrentItem '{shell.CurrentItem?.Title}'=='Alpha', '{(renderer.CurrentPage as ContentPage)?.Title}'=='Shell Alpha' rendered",
			shell.CurrentItem?.Title == "Alpha" && PageShows(renderer, "Shell Alpha", "alpha body"));
		_qtShellChecks.Check($"leg E no tab bar on a single-section item: tabs '{TabState()}' == '@0'", TabState() == "@0");
		// Leg F: a TabBar item shows the tab bar; a tab tap switches.
		Console.Error.WriteLine("[Sailfish] Qt shell diag: leg F — Shell.CurrentItem → TabBar (One/Two)");
		shell.CurrentItem = shell.Items[2];
		_context.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1800), () => VerifyShellLegF(renderer, shell));
	}

	private void VerifyShellLegF(QtHost.QtHostPageRenderer renderer, Shell shell)
	{
		var top = ContentTop(renderer);
		_qtShellChecks.Check($"leg F tab bar: tabs '{TabState()}'=='One,Two@0', 'Tab One' rendered, content top {top:F1} > {_shellContentTopA:F1} (the bar joined the chrome)",
			TabState() == "One,Two@0" && PageShows(renderer, "Tab One", "tab one body") && top > _shellContentTopA + 10);
		Console.Error.WriteLine("[Sailfish] Qt shell diag: leg F — tab tap 'Two' (tab-selected index 1)");
		renderer.HandleNativeEvent("tab-selected", "{\"index\":1}");
		_context.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1800), () => VerifyShellLegF2(renderer));
	}

	private static ContentPage SimplePage(string title, string body) => new()
	{
		Title = title,
		Content = new VerticalStackLayout { Padding = new Thickness(16), Children = { new Label { Text = body } } },
	};

	private void VerifyShellLegF2(QtHost.QtHostPageRenderer renderer)
	{
		_qtShellChecks.Check($"leg F tab switch: tabs '{TabState()}'=='One,Two@1', 'Tab Two' rendered",
			TabState() == "One,Two@1" && PageShows(renderer, "Tab Two", "tab two body"));
		// Leg F3: a right swipe mid-screen (clear of system edge gestures) goes to the previous tab.
		Console.Error.WriteLine("[Sailfish] Qt shell diag: leg F3 — injected swipe right across the page");
		const double y = 1100;
		QtHost.QtHostRuntime.InjectPointer(0, 250, y);
		for (var x = 300; x <= 600; x += 50)
			QtHost.QtHostRuntime.InjectPointer(2, x, y);
		// Mid-swipe the page follows the finger, the previous tab's title beside it.
		_context.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(400), () =>
		{
			var drag = DiagQml.Num(QtHost.QtHostRuntime.Eval("String(pageStack.currentPage.mauiTabDrag)"));
			_qtShellChecks.Check($"leg F3 drag: the page follows the finger (mauiTabDrag={drag:F0} px, finger moved 350 px)",
				drag > 250 && drag < 400);
			Shot(_context.Dispatcher, "shell-tab-drag", () =>
			{
				for (var x = 650; x <= 800; x += 50)
					QtHost.QtHostRuntime.InjectPointer(2, x, y);
				QtHost.QtHostRuntime.InjectPointer(1, 820, y);
				_context.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1800), VerifyShellLegF3);
			});
		});

		void VerifyShellLegF3()
		{
			_qtShellChecks.Check($"leg F3 swipe: tabs '{TabState()}'=='One,Two@0', 'Tab One' rendered (router tab swipes {QtHost.QtHostInputRouter.Active?.TabSwipes})",
				TabState() == "One,Two@0" && PageShows(renderer, "Tab One", "tab one body"));
			var settled = DiagQml.Num(QtHost.QtHostRuntime.Eval("String(pageStack.currentPage.mauiTabDrag)"));
			_qtShellChecks.Check($"leg F3 slide: the new tab slid in and rests (mauiTabDrag={settled:F0})", Math.Abs(settled) < 1);
			RunTabbedPageLeg(renderer);
		}
	}

	private void RunTabbedPageLeg(QtHost.QtHostPageRenderer renderer)
	{
		// Leg G: a TabbedPage root; a NavigationPage child owns its stack while the bar stays top-level.
		var tabNav = new NavigationPage(SimplePage("TP One", "tp one body")) { Title = "TP One" };
		var tabbed = new TabbedPage { Children = { tabNav, SimplePage("TP Two", "tp two body") } };
		Console.Error.WriteLine("[Sailfish] Qt shell diag: leg G — window root → TabbedPage (NavigationPage child + page)");
		((Microsoft.Maui.Controls.Window)_context.Window).Page = tabbed;
		_context.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1800), () => VerifyTabbedA(renderer, tabbed, tabNav));
	}

	private void VerifyTabbedA(QtHost.QtHostPageRenderer renderer, TabbedPage tabbed, NavigationPage tabNav)
	{
		_qtShellChecks.Check($"leg G tabbed root: tabs '{TabState()}'=='TP One,TP Two@0', 'TP One' rendered, native depth {renderer.NativePageIds.Count}==1",
			TabState() == "TP One,TP Two@0" && PageShows(renderer, "TP One", "tp one body") && renderer.NativePageIds.Count == 1);
		_ = tabNav.PushAsync(SimplePage("TP Pushed", "tp pushed body"));
		_context.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1800), () =>
		{
			_qtShellChecks.Check($"leg G push inside the tab: 'TP Pushed' rendered, native depth {renderer.NativePageIds.Count}==2, no tab bar ('{TabState()}' starts with '@')",
				PageShows(renderer, "TP Pushed", "tp pushed body") && renderer.NativePageIds.Count == 2 && TabState().StartsWith("@", StringComparison.Ordinal));
			QtHost.QtHostRuntime.PopPage(immediate: true);
			_context.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1800), () =>
			{
				_qtShellChecks.Check($"leg G native pop: tab stack {tabNav.Navigation.NavigationStack.Count}==1, 'TP One' rendered, tabs back '{TabState()}'",
					tabNav.Navigation.NavigationStack.Count == 1 && PageShows(renderer, "TP One", "tp one body") && TabState() == "TP One,TP Two@0");
				renderer.HandleNativeEvent("tab-selected", "{\"index\":1}");
				_context.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1800), () =>
				{
					_qtShellChecks.Check($"leg G tab switch: CurrentPage '{tabbed.CurrentPage?.Title}'=='TP Two' rendered",
						PageShows(renderer, "TP Two", "tp two body"));
					RunFlyoutPageLeg(renderer);
				});
			});
		});
	}

	private void RunFlyoutPageLeg(QtHost.QtHostPageRenderer renderer)
	{
		// Leg H: a FlyoutPage root; the pulley opens the flyout as a native push, Back dismisses it.
		var flyout = new FlyoutPage
		{
			Flyout = SimplePage("Menu", "menu body"),
			Detail = new NavigationPage(SimplePage("FP Detail", "fp detail body")),
		};
		Console.Error.WriteLine("[Sailfish] Qt shell diag: leg H — window root → FlyoutPage");
		((Microsoft.Maui.Controls.Window)_context.Window).Page = flyout;
		_context.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1800), () =>
		{
			var pulley = PulleyTexts(renderer);
			_qtShellChecks.Check($"leg H flyout root: 'FP Detail' rendered, pulley [{string.Join(",", pulley ?? new())}] == [Menu]",
				PageShows(renderer, "FP Detail", "fp detail body") && pulley is { Count: 1 } && pulley[0] == "Menu");
			renderer.HandleNativeEvent("toolbar-activated", "{\"menu\":\"pull\",\"index\":0}");
			_context.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1800), () =>
			{
				_qtShellChecks.Check($"leg H present: IsPresented={flyout.IsPresented}, 'Menu' rendered on a pushed native page (depth {renderer.NativePageIds.Count}==2)",
					flyout.IsPresented && PageShows(renderer, "Menu", "menu body") && renderer.NativePageIds.Count == 2);
				QtHost.QtHostRuntime.PopPage(immediate: true);
				_context.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1800), () =>
				{
					_qtShellChecks.Check($"leg H native back: IsPresented={flyout.IsPresented}==False, 'FP Detail' rendered, depth {renderer.NativePageIds.Count}==1",
						!flyout.IsPresented && PageShows(renderer, "FP Detail", "fp detail body") && renderer.NativePageIds.Count == 1);
					FinishShellDiag();
				});
			});
		});
	}

	private void FinishShellDiag()
	{
		_qtShellChecks.Accept("OK — F2 navigation: Shell sections/routes/flyout pulley/tab bar, TabbedPage and FlyoutPage ride the native pageStack");
		// MAUI_SAILFISH_QT_HOST_SHELL_HOLD=1 stays on the last state for a screenshot.
		if (SailfishEnv.Flag("MAUI_SAILFISH_QT_HOST_SHELL_HOLD"))
		{
			Console.Error.WriteLine("[Sailfish] Qt shell diag: HOLD — staying on the tab page for a screenshot (no auto-shutdown)");
			return;
		}
		QtHost.QtHostRuntime.Shutdown();
	}
}
