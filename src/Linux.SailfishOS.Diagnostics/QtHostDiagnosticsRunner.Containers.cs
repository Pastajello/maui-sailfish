using Microsoft.Maui.Controls;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Containers leg (MAUI_SAILFISH_QT_HOST_CONTAINERS_DIAG=1): a FlyoutPage whose Detail is a TabbedPage with a
/// NavigationPage per tab. Checks that the container handlers drive the native stack (tabs, a push inside a tab, a
/// tab switch at depth 2, the flyout) and that the page cache keeps each page: a tab switched away from and back,
/// or revealed by a native back, shows the same QML objects, not a rebuild; a replaced Detail leaves the cache.
/// </summary>
internal sealed partial class QtHostDiagnosticsRunner
{
	private bool _qtContainersDiag;
	private readonly DiagChecks _qtContainersChecks = new("Qt containers diag");

	private const int ContainersStepMs = 1600;

	/// <summary>The native handle of the host showing <paramref name="body"/> (0 = none attached).</summary>
	private static long BodyHandle(QtHost.QtHostPageRenderer renderer, Label body) =>
		renderer.Cache.TryGet(body, out var host) && host is { IsAttached: true } ? host.NativeHandle : 0;

	private static bool HandleAlive(long handle) => handle != 0 && QtHost.QtHostRuntime.TryItemGeometry(handle, out _);

	/// <summary>Whether the page's content root is shown natively (a page restored from the cache was hidden).</summary>
	private static string RootVisible(QtHost.QtHostPageRenderer renderer, ContentPage page) =>
		page.Content is { } content && renderer.Cache.TryGet(content, out var root) && root is { IsAttached: true }
			? QtHost.QtHostRuntime.GetProperty(root.NativeHandle, "visible")
			: "?";

	private void RunQtContainersDiagnostics(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		if (_context.Window is not Microsoft.Maui.Controls.Window window)
		{
			Console.Error.WriteLine("[Sailfish] Qt containers diag: no Controls Window — leg skipped; auto-shutdown");
			QtHost.QtHostRuntime.Shutdown();
			return;
		}
		var oneBody = new Label { Text = "c one body" };
		var twoBody = new Label { Text = "c two body" };
		var one = new ContentPage { Title = "C One", Content = new VerticalStackLayout { Padding = new Thickness(16), Children = { oneBody, new Label { Text = "tab one, root of its stack" } } } };
		var two = new ContentPage { Title = "C Two", Content = new VerticalStackLayout { Padding = new Thickness(16), Children = { twoBody } } };
		var navOne = new NavigationPage(one) { Title = "C One" };
		var navTwo = new NavigationPage(two) { Title = "C Two" };
		var tabbed = new TabbedPage { Title = "Tabs", Children = { navOne, navTwo } };
		var flyout = new FlyoutPage { Flyout = SimplePage("Menu", "menu body"), Detail = tabbed };
		Console.Error.WriteLine("[Sailfish] Qt containers diag: leg A — window root → FlyoutPage { Detail = TabbedPage { NavigationPage × 2 } }");
		window.Page = flyout;

		long oneHandle = 0;
		var restores0 = 0L;
		void Step(int ms, Action next) => dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(ms), next);

		Step(ContainersStepMs, () =>
		{
			oneHandle = BodyHandle(renderer, oneBody);
			_qtContainersChecks.Check($"leg A: handlers flyout={flyout.Handler?.GetType().Name} tabbed={tabbed.Handler?.GetType().Name} nav={navOne.Handler?.GetType().Name}; " +
				$"tabs '{TabState()}'=='C One,C Two@0'; 'C One' rendered (handle {oneHandle:x})",
				flyout.Handler is Handlers.SailfishFlyoutPageHandler && tabbed.Handler is Handlers.SailfishTabbedPageHandler &&
				navOne.Handler is Handlers.SailfishNavigationViewHandler && TabState() == "C One,C Two@0" &&
				PageShows(renderer, "C One", "c one body") && oneHandle != 0);
			restores0 = renderer.PageCacheRestores;
			renderer.HandleNativeEvent("tab-selected", "{\"index\":1}");
			Step(ContainersStepMs, () =>
			{
				var oneRoot = one.Content is { } content && renderer.Cache.TryGet(content, out var rootHost) && rootHost is not null ? rootHost.NativeHandle : 0;
				var oneVisible = oneRoot == 0 ? "?" : QtHost.QtHostRuntime.GetProperty(oneRoot, "visible");
				_qtContainersChecks.Check($"leg B tab switch: 'C Two' rendered, 'C One' parked (pages {renderer.ParkedPages}>=1), its objects alive ({HandleAlive(oneHandle)}) and hidden (visible={oneVisible})",
					PageShows(renderer, "C Two", "c two body") && renderer.ParkedPages >= 1 && HandleAlive(oneHandle) && oneVisible == "false");
				Shot(dispatcher, "containers-b-tab-two", () =>
				{
					renderer.HandleNativeEvent("tab-selected", "{\"index\":0}");
					Step(ContainersStepMs, () =>
					{
						var back = BodyHandle(renderer, oneBody);
						var shown = RootVisible(renderer, one);
						_qtContainersChecks.Check($"leg C back to tab one: 'C One' rendered by the same QML object ({back:x}=={oneHandle:x}), shown again (visible={shown}), cache restores +{renderer.PageCacheRestores - restores0}>=1",
							PageShows(renderer, "C One", "c one body") && back == oneHandle && shown == "true" && renderer.PageCacheRestores > restores0);
						Shot(dispatcher, "containers-c-tab-one-again", () => PushInTab(renderer, dispatcher, tabbed, navOne, oneBody, oneHandle, flyout, window));
					});
				});
			});
		});
	}

	private void PushInTab(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, TabbedPage tabbed, NavigationPage navOne,
	                       Label oneBody, long oneHandle, FlyoutPage flyout, Microsoft.Maui.Controls.Window window)
	{
		void Step(int ms, Action next) => dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(ms), next);
		_ = navOne.PushAsync(SimplePage("C Pushed", "c pushed body"));
		Step(ContainersStepMs, () =>
		{
			_qtContainersChecks.Check($"leg D push in tab one: 'C Pushed' rendered, native depth {renderer.NativePageIds.Count}==2, no tab bar ('{TabState()}')",
				PageShows(renderer, "C Pushed", "c pushed body") && renderer.NativePageIds.Count == 2 && TabState().StartsWith("@", StringComparison.Ordinal));
			tabbed.CurrentPage = tabbed.Children[1];
			Step(ContainersStepMs, () =>
			{
				var twoShown = tabbed.Children[1] is NavigationPage { RootPage: ContentPage twoPage } ? RootVisible(renderer, twoPage) : "?";
				_qtContainersChecks.Check($"leg E tab switch at depth 2: 'C Two' rendered and shown (visible={twoShown}), native depth {renderer.NativePageIds.Count}==1, tab one's stack kept by MAUI ({navOne.Navigation.NavigationStack.Count}==2)",
					PageShows(renderer, "C Two", "c two body") && twoShown == "true" && renderer.NativePageIds.Count == 1 && navOne.Navigation.NavigationStack.Count == 2);
				tabbed.CurrentPage = tabbed.Children[0];
				Step(ContainersStepMs, () =>
				{
					_qtContainersChecks.Check($"leg F back to tab one: its stack top 'C Pushed' rendered, native depth {renderer.NativePageIds.Count}==2",
						PageShows(renderer, "C Pushed", "c pushed body") && renderer.NativePageIds.Count == 2);
					QtHost.QtHostRuntime.PopPage(immediate: true);
					Step(ContainersStepMs, () =>
					{
						var back = BodyHandle(renderer, oneBody);
						var oneShown = navOne.RootPage is ContentPage onePage ? RootVisible(renderer, onePage) : "?";
						_qtContainersChecks.Check($"leg G native back: 'C One' rendered from the cache (same QML object {back:x}=={oneHandle:x}), shown (visible={oneShown}), tab stack {navOne.Navigation.NavigationStack.Count}==1, tabs '{TabState()}'",
							PageShows(renderer, "C One", "c one body") && back == oneHandle && oneShown == "true" &&
							navOne.Navigation.NavigationStack.Count == 1 && TabState() == "C One,C Two@0");
						Shot(dispatcher, "containers-g-after-back", () => PresentFlyout(renderer, dispatcher, oneBody, oneHandle, flyout, window));
					});
				});
			});
		});
	}

	private void PresentFlyout(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, Label oneBody, long oneHandle,
	                           FlyoutPage flyout, Microsoft.Maui.Controls.Window window)
	{
		void Step(int ms, Action next) => dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(ms), next);
		var pulley = PulleyTexts(renderer);
		_qtContainersChecks.Check($"leg H pulley on the detail's root: [{string.Join(",", pulley ?? new())}] == [Menu]",
			pulley is { Count: 1 } && pulley[0] == "Menu");
		flyout.IsPresented = true;
		Step(ContainersStepMs, () =>
		{
			_qtContainersChecks.Check($"leg H flyout presented: 'Menu' on a pushed native page (depth {renderer.NativePageIds.Count}==2)",
				PageShows(renderer, "Menu", "menu body") && renderer.NativePageIds.Count == 2);
			QtHost.QtHostRuntime.PopPage(immediate: true);
			Step(ContainersStepMs, () =>
			{
				var back = BodyHandle(renderer, oneBody);
				_qtContainersChecks.Check($"leg H back from the flyout: IsPresented={flyout.IsPresented}==False, 'C One' from the cache ({back:x}=={oneHandle:x})",
					!flyout.IsPresented && PageShows(renderer, "C One", "c one body") && back == oneHandle);
				var drops0 = renderer.PageCacheDrops;
				flyout.Detail = new NavigationPage(SimplePage("C Other", "c other body"));
				Step(ContainersStepMs, () =>
				{
					var census = ParseStressQml(EvalStressQmlCounts());
					_qtContainersChecks.Check($"leg I replaced Detail: 'C Other' rendered, the old tabs left the cache (pages {renderer.ParkedPages}==0, drops +{renderer.PageCacheDrops - drops0}>=1), " +
						$"no parked QML hosts left ({census.Parked}==0)",
						PageShows(renderer, "C Other", "c other body") && renderer.ParkedPages == 0 &&
						renderer.PageCacheDrops > drops0 && census.Parked == 0);
					BadgedTabs(renderer, dispatcher, window);
				});
			});
		});
	}

	/// <summary>Scene geometry of the first object named <paramref name="name"/> under the current page as
	/// "x,width,visible" (MAUI 11 tab badges, the scrolling tab row); empty when absent.</summary>
	private static string TabObject(string name) => QtHost.QtHostRuntime.Eval(
		"(function(){function F(o){if(!o)return null;if(o.objectName==='" + name + "')return o;var k=o.children;if(k)for(var i=0;i<k.length;++i){var r=F(k[i]);if(r)return r;}return null;}" +
		"var t=F(pageStack.currentPage);if(!t)return '';var v=true;for(var x=t;x;x=x.parent)if(!x.visible){v=false;break;}" +
		"return Math.round(t.mapToItem(null,0,0).x)+','+Math.round(t.width)+','+v+(t.children&&t.children.length&&t.children[0].text!==undefined?','+t.children[0].text:'');})()");

	/// <summary>Leg J (tracker S14): six tabs, one with a count badge and one with a dot. The badges paint, a badge
	/// change at runtime re-pushes, and the row scrolls so the last tab comes into view when selected.</summary>
	private void BadgedTabs(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, Microsoft.Maui.Controls.Window window)
	{
		void Step(int ms, Action next) => dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(ms), next);
		var tabbed = new TabbedPage { Title = "Badges" };
		for (var i = 1; i <= 6; i++)
			tabbed.Children.Add(SimplePage("Tab " + i, "badge tab " + i + " body"));
		TabbedPage.SetBadgeText(tabbed.Children[0], "3");
		TabbedPage.SetBadgeColor(tabbed.Children[0], Microsoft.Maui.Graphics.Colors.Red);
		TabbedPage.SetBadgeTextColor(tabbed.Children[0], Microsoft.Maui.Graphics.Colors.White);
		TabbedPage.SetBadgeText(tabbed.Children[1], "");
		window.Page = tabbed;
		Step(ContainersStepMs, () =>
		{
			var count = TabObject("mauiTab_badge_0");
			var dot = TabObject("mauiTab_badge_1");
			var none = TabObject("mauiTab_badge_2");
			var last = TabObject("mauiTab_5");
			var pageWidth = DiagQml.EvalNum("pageStack.currentPage?pageStack.currentPage.width:0");
			var lastX = DiagQml.Num(last.Split(',')[0], 0);
			_qtContainersChecks.Check($"leg J badges: tabs '{TabState()}', count badge [{count}] visible with '3', dot [{dot}] visible, tab 3 none [{none}], " +
				$"row scrolls (tab 6 at x {lastX:F0} ≥ page width {pageWidth:F0})",
				TabState() == "Tab 1,Tab 2,Tab 3,Tab 4,Tab 5,Tab 6@0" && count.EndsWith(",true,3", StringComparison.Ordinal) &&
				dot.Contains(",true", StringComparison.Ordinal) && none.Contains(",false", StringComparison.Ordinal) && lastX >= pageWidth - 1);
			Shot(dispatcher, "containers-j-badges", () =>
			{
				TabbedPage.SetBadgeText(tabbed.Children[0], "12");
				tabbed.CurrentPage = tabbed.Children[5];
				Step(ContainersStepMs, () =>
				{
					var updated = TabObject("mauiTab_badge_0");
					var shownLast = TabObject("mauiTab_5").Split(',');
					var x = DiagQml.Num(shownLast[0], -1);
					var w = shownLast.Length > 1 ? DiagQml.Num(shownLast[1], 0) : 0;
					_qtContainersChecks.Check($"leg J runtime: badge now [{updated}] ends with '12'; tab 6 selected ('{TabState()}'), in view (x {x:F0}, width {w:F0}, page {pageWidth:F0}); 'Tab 6' rendered",
						updated.EndsWith(",12", StringComparison.Ordinal) && TabState().EndsWith("@5", StringComparison.Ordinal) &&
						x >= 0 && x + w <= pageWidth + 1 && PageShows(renderer, "Tab 6", "badge tab 6 body"));
					Shot(dispatcher, "containers-j-last-tab", () =>
					{
						_qtContainersChecks.Accept("OK — Shell/Tabbed/Flyout container handlers drive the native stack and the page cache keeps each container's pages; tab badges and the scrolling tab row");
						QtHost.QtHostRuntime.Shutdown();
					});
				});
			});
		});
	}
}
