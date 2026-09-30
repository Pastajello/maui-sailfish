using Microsoft.Maui.Controls;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Tab pulley leg (MAUI_SAILFISH_QT_HOST_TABPULLEY_DIAG=1): a TabbedPage root whose first tab alone has
/// ToolbarItems, walked A → B → C → A as in the Clock app (only some TabItems carry a PullDownMenu). On A the
/// pulley is registered and a real held pull opens it; on B (short page) and C (a list, a second drag surface)
/// no surface keeps a menu, no resting bar paints and the same pull opens nothing. On A the held pull also
/// measures what moves: natively the resting bar, the header with its tabs and the content travel together.
/// </summary>
internal sealed partial class QtHostDiagnosticsRunner
{
	private bool _qtTabPulleyDiag;
	private readonly DiagChecks _qtTabPulleyChecks = new("Qt tab pulley diag");

	private static readonly string[] TabPullA = { "TA pull" };
	private static readonly string[] TabPushA = { "TA push" };
	private const string TabTitles = "Tab A,Tab B,Tab C";

	/// <summary>Every drag surface of the current page: which menus it carries, their items, and every pulley
	/// resting bar under the page with its effective opacity and scene y.</summary>
	private const string TabSurfacesJs = """
		(function(){
		  var p=pageStack.currentPage; if(!p||!p.mauiFlickable) return 'no page';
		  function texts(m){ var t=[]; var l=(m&&m.__items)||[]; for(var i=0;i<l.length;i++) t.push(l[i]?l[i].text:'null'); return t; }
		  function surf(kind,f){ return {s:kind, pull:f.pullDownMenu?texts(f.pullDownMenu):null, push:f.pushUpMenu?texts(f.pushUpMenu):null,
		    interactive:!!f.interactive, contentY:Math.round(f.contentY)}; }
		  var out={surfaces:[], bars:[]};
		  out.surfaces.push(surf('page',p.mauiFlickable()));
		  var hosts=p.__hosts||{};
		  for(var id in hosts){ var h=hosts[id]; if(h.uri==='list-view'||h.uri==='scroll-view') out.surfaces.push(surf(h.uri,h.item)); }
		  function op(it){ var o=1; for(var x=it;x;x=x.parent){ if(!x.visible) return 0; o*=x.opacity; } return o; }
		  function menuOf(it){ for(var x=it;x;x=x.parent) if(x.hasOwnProperty('mauiItems')) return x; return null; }
		  function why(it){ var r=[]; for(var x=it;x;x=x.parent){ if(!x.visible) { r.push('hidden:'+(x.objectName||x.toString().split('(')[0])); break; }
		      if(x.opacity<0.99) r.push((x.objectName||x.toString().split('(')[0])+'@'+Math.round(x.opacity*100)/100); } return r.join('>'); }
		  function walk(it){ if(!it) return; if(it.hasOwnProperty('_inactiveOpacity')){ var m=menuOf(it);
		      // A bar paints with its opacity and its colour's alpha (the adapter hides clone bars by colour too).
		      var o=op(it)*(it.color!==undefined&&it.color.a!==undefined?it.color.a:1);
		      out.bars.push({k:(m&&m.flickable&&m.flickable.pushUpMenu===m)?'push':'pull', o:Math.round(o*100)/100,
		        sy:Math.round(it.mapToItem(null,0,0).y), h:Math.round(it.height), io:it._inactiveOpacity,
		        clone:!!(m&&m.mauiForceFlick), active:!!(m&&m.active), why:why(it)}); }
		    var k=it.children; if(k) for(var i=0;i<k.length;i++) walk(k[i]); }
		  walk(p);
		  out.inset=Math.round(p.topInset||0);
		  return JSON.stringify(out);
		})()
		""";

	/// <summary>Scene y of the first object named <c>name</c> under the current page (NaN when absent).</summary>
	private static double TabSceneY(string name) => DiagQml.EvalNum(
		"(function(){function F(o){if(!o)return null;if(o.objectName==='" + name + "')return o;var k=o.children;if(k)for(var i=0;i<k.length;++i){var r=F(k[i]);if(r)return r;}return null;}" +
		"var t=F(pageStack.currentPage);return t?t.mapToItem(null,0,0).y:NaN;})()");

	private sealed record TabSurfaces(string Raw, List<(string Surface, List<string>? Pull, List<string>? Push)> Surfaces,
		List<(string Kind, double Opacity, double SceneY, double Height)> Bars, double TopInset)
	{
		/// <summary>Resting bars that paint inside the window (a push-up bar past the list end is off screen). The page
		/// flickable's pull-down bar sits mostly above the top edge; its visible sliver is the line at the top.</summary>
		public List<(string Kind, double Opacity, double SceneY, double Height)> OnScreen =>
			Bars.Where(b => b.Opacity > 0.02 && b.SceneY + b.Height > 0 && b.SceneY < 2272).ToList();

		public string Describe() => string.Join(",", Bars.Select(b => $"{b.Kind}@y{b.SceneY:F0}/o{b.Opacity}"));
	}

	private static TabSurfaces ReadTabSurfaces()
	{
		var raw = QtHost.QtHostRuntime.Eval(TabSurfacesJs);
		var surfaces = new List<(string, List<string>?, List<string>?)>();
		var bars = new List<(string, double, double, double)>();
		var inset = double.NaN;
		try
		{
			using var doc = System.Text.Json.JsonDocument.Parse(raw);
			static List<string>? Items(System.Text.Json.JsonElement e) =>
				e.ValueKind == System.Text.Json.JsonValueKind.Array ? e.EnumerateArray().Select(i => i.GetString() ?? "").ToList() : null;
			foreach (var s in doc.RootElement.GetProperty("surfaces").EnumerateArray())
				surfaces.Add((s.GetProperty("s").GetString() ?? "", Items(s.GetProperty("pull")), Items(s.GetProperty("push"))));
			foreach (var b in doc.RootElement.GetProperty("bars").EnumerateArray())
				bars.Add((b.GetProperty("k").GetString() ?? "?", b.GetProperty("o").GetDouble(), b.GetProperty("sy").GetDouble(), b.GetProperty("h").GetDouble()));
			inset = doc.RootElement.GetProperty("inset").GetDouble();
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[Sailfish] Qt tab pulley diag: surfaces unreadable ({ex.Message}): {raw}");
		}
		return new TabSurfaces(raw, surfaces, bars, inset);
	}

	private static ContentPage BuildTabPage(string title, string body, bool list, bool menus)
	{
		var label = new Label { Text = body, Margin = new Thickness(16) };
		View content = label;
		if (list)
		{
			var rows = new CollectionView
			{
				ItemsSource = Enumerable.Range(1, 30).Select(i => $"{title} row {i}").ToList(),
				ItemTemplate = new DataTemplate(() =>
				{
					var row = new Label { Padding = new Thickness(16, 12) };
					row.BindingContextChanged += (_, _) => row.Text = row.BindingContext as string ?? string.Empty;
					return row;
				}),
			};
			var grid = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) } };
			grid.Add(label, 0, 0);
			grid.Add(rows, 0, 1);
			content = grid;
		}
		var page = new ContentPage { Title = title, Content = content };
		if (menus)
		{
			page.ToolbarItems.Add(new ToolbarItem { Text = TabPullA[0] });
			page.ToolbarItems.Add(new ToolbarItem { Text = TabPushA[0], Order = ToolbarItemOrder.Secondary });
		}
		return page;
	}

	private void RunQtTabPulleyDiagnostics(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		if (_context.Window is not Microsoft.Maui.Controls.Window window)
		{
			Console.Error.WriteLine("[Sailfish] Qt tab pulley diag: no Controls Window — leg skipped; auto-shutdown");
			QtHost.QtHostRuntime.Shutdown();
			return;
		}
		var tabbed = new TabbedPage
		{
			Children =
			{
				BuildTabPage("Tab A", "tab a body", list: true, menus: true),
				BuildTabPage("Tab B", "tab b body", list: false, menus: false),
				BuildTabPage("Tab C", "tab c body", list: true, menus: false),
			},
		};
		Console.Error.WriteLine("[Sailfish] Qt tab pulley diag: window root → TabbedPage { A (pull+push, list), B (none), C (none, list) }");
		window.Page = tabbed;
		WhenStackIdle(dispatcher, () =>
			VisitTab(renderer, dispatcher, "a1", 0, () =>
			SwitchTab(renderer, dispatcher, 1, () => VisitTab(renderer, dispatcher, "b", 1, () =>
			SwitchTab(renderer, dispatcher, 2, () => VisitTab(renderer, dispatcher, "c", 2, () =>
			SwitchTab(renderer, dispatcher, 0, () => VisitTab(renderer, dispatcher, "a2", 0, FinishTabPulleyDiag))))))));
	}

	/// <summary>Waits out the root swap: a pull injected while the pageStack still animates never reaches the list.</summary>
	private static void WhenStackIdle(SailfishDispatcher dispatcher, Action next, int waitedMs = 0)
	{
		var busy = QtHost.QtHostRuntime.Eval("pageStack.busy?'1':'0'") != "0";
		if ((busy || waitedMs < 2200) && waitedMs < 8000)
		{
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(200), () => WhenStackIdle(dispatcher, next, waitedMs + 200));
			return;
		}
		Console.Error.WriteLine($"[Sailfish] Qt tab pulley diag: page stack idle after {waitedMs} ms (busy={busy})");
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(600), next);
	}

	/// <summary>A tap in the model page's tab bar, as the QML tab row sends it.</summary>
	private static void SwitchTab(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, int index, Action next)
	{
		Console.Error.WriteLine($"[Sailfish] Qt tab pulley diag: tab-selected {index}");
		renderer.HandleNativeEvent("tab-selected", $"{{\"index\":{index}}}");
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1800), next);
	}

	private void VisitTab(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, string step, int index, Action next)
	{
		var title = index switch { 0 => "Tab A", 1 => "Tab B", _ => "Tab C" };
		var body = index switch { 0 => "tab a body", 1 => "tab b body", _ => "tab c body" };
		var withMenus = index == 0;
		var tabs = TabState();
		_qtTabPulleyChecks.Check($"{step}: tab bar '{tabs}'=='{TabTitles}@{index}', '{title}' rendered",
			tabs == $"{TabTitles}@{index}" && PageShows(renderer, title, body));

		var rest = ReadTabSurfaces();
		Console.Error.WriteLine($"[Sailfish] Qt tab pulley diag: {step} surfaces: {rest.Raw}");
		var pulls = rest.Surfaces.Where(s => s.Pull is not null).ToList();
		var pushes = rest.Surfaces.Where(s => s.Push is not null).ToList();
		string Show(IEnumerable<(string Surface, List<string>? Items)> s) =>
			"[" + string.Join(";", s.Select(x => $"{x.Surface}:{string.Join(",", x.Items!)}")) + "]";
		var pullShown = Show(pulls.Select(s => (s.Surface, s.Pull)));
		var pushShown = Show(pushes.Select(s => (s.Surface, s.Push)));
		if (withMenus)
		{
			_qtTabPulleyChecks.Check($"{step}: pull-down registered on the drag surfaces {pullShown}, each [{string.Join(",", TabPullA)}]; push-up {pushShown} == [{string.Join(",", TabPushA)}]",
				pulls.Count > 0 && pulls.All(s => s.Pull!.SequenceEqual(TabPullA)) && pushes.Count > 0 && pushes.All(s => s.Push!.SequenceEqual(TabPushA)));
			_qtTabPulleyChecks.Check($"{step}: exactly one resting pull-down bar paints on screen ({rest.Describe()})",
				rest.OnScreen.Count(b => b.Kind == "pull") == 1);
			// Both drag surfaces carry the menus (the page flickable and, as a clone, the list), on every visit.
			var listMenus = rest.Surfaces.Any(x => x.Surface == "list-view" && x.Pull is not null && x.Push is not null);
			_qtTabPulleyChecks.Check($"{step}: Tab A's list carries both menus too (pull {pullShown}, push {pushShown})", listMenus);
			// Natively a list's push-up bar sits at the end of its content: the page-bottom bar stays unpainted then.
			var bottomBars = rest.OnScreen.Count(b => b.Kind == "push" && b.SceneY > 2272 - b.Height);
			_qtTabPulleyChecks.Check($"{step}: no push-up bar paints at the page bottom while the list ends elsewhere ({bottomBars} there: {rest.Describe()})",
				bottomBars == 0);
		}
		else
		{
			_qtTabPulleyChecks.Check($"{step}: no drag surface keeps a menu (pull {pullShown}, push {pushShown}) — the mechanism stayed with Tab A",
				pulls.Count == 0 && pushes.Count == 0);
			_qtTabPulleyChecks.Check($"{step}: no resting pulley bar paints ({rest.Describe()})", rest.OnScreen.Count == 0);
		}

		// The motion probes: a content label, the tab row (header chrome) and the resting bar.
		var contentHost = renderer.CurrentHosts.FirstOrDefault(h => h.IsAttached && h.Element is Label { Text: var t } && t == body);
		double ContentY() => contentHost is not null && QtHost.QtHostRuntime.TryItemGeometry(contentHost.NativeHandle, out var g) ? g.Y : double.NaN;
		var content0 = ContentY();
		var tab0 = TabSceneY("mauiTab_0");
		var pullBar0 = rest.OnScreen.Where(b => b.Kind == "pull").Select(b => b.SceneY).DefaultIfEmpty(double.NaN).First();

		// A slow pull from over the content (the list on A and C), held for the reading and the screenshot.
		const double x = 516, y0 = 700, distance = 800;
		const int steps = 40;
		QtHost.QtHostRuntime.InjectPointer(0, x, y0);
		void Step(int i, int direction, Action done)
		{
			if (i > steps)
			{
				done();
				return;
			}
			var k = direction > 0 ? i : steps - i;
			QtHost.QtHostRuntime.InjectPointer(2, x, y0 + distance * k / steps);
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(16), () => Step(i + 1, direction, done));
		}
		Step(1, +1, () => dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(400), () =>
		{
			var held = QtHost.QtHostRuntime.Eval(HeldStateJs);
			Console.Error.WriteLine($"[Sailfish] Qt tab pulley diag: {step} held: {held}");
			var activeItems = ActiveItemYs(held);
			if (withMenus)
			{
				_qtTabPulleyChecks.Check($"{step}: the held pull opens the menu with its items ON SCREEN ({OnScreenSummary(held)})", ItemsOnScreen(held));
				var dContent = ContentY() - content0;
				var dTab = TabSceneY("mauiTab_0") - tab0;
				var pulled = ReadTabSurfaces();
				Console.Error.WriteLine($"[Sailfish] Qt tab pulley diag: {step} pulled bars: {pulled.Raw}");
				_qtTabPulleyChecks.Check($"{step}: the pull moves the content (Δ{dContent:F0} px > 100)", dContent > 100);
				_qtTabPulleyChecks.Check($"{step}: the header with its tabs travels with the content, as natively (Δtabs {dTab:F0} ≈ Δcontent {dContent:F0})",
					!double.IsNaN(dTab) && Math.Abs(dTab - dContent) <= 4);
				// The header (title + tab row) spans topInset above the tab row's bottom edge, wherever it now sits.
				var tabBottom = TabSceneY("mauiTab_0") + DiagQml.EvalNum("(function(){function F(o){if(!o)return null;if(o.objectName==='mauiTab_0')return o;var k=o.children;if(k)for(var i=0;i<k.length;++i){var r=F(k[i]);if(r)return r;}return null;}var t=F(pageStack.currentPage);return t?t.height:NaN;})()");
				var headerTop = tabBottom - rest.TopInset;
				var under = (activeItems ?? new List<double>()).Where(y => y > headerTop - 1 && y < tabBottom - 1).ToList();
				_qtTabPulleyChecks.Check($"{step}: no open menu item paints under the header/tabs (header scene y {headerTop:F0}..{tabBottom:F0}, items under it [{string.Join(",", under)}])",
					activeItems is { Count: > 0 } && under.Count == 0);
				// The bar is Silica's highlight: it tracks the drag edge rather than riding the content rigidly, so it
				// must leave the top edge with the page and stay between the menu and the (moved) header.
				var pageBar = pulled.Bars.Where(b => b.Kind == "pull").OrderBy(b => Math.Abs(b.SceneY - pullBar0)).FirstOrDefault();
				var dBar = double.IsNaN(pullBar0) || pageBar.Kind is null ? double.NaN : pageBar.SceneY - pullBar0;
				_qtTabPulleyChecks.Check($"{step}: the pull-down bar leaves the top edge with the page (Δbar {dBar:F0} > 100) and stays above the header (bar bottom {pageBar.SceneY + pageBar.Height:F0} ≤ header top {headerTop:F0})",
					dBar > 100 && pageBar.SceneY + pageBar.Height <= headerTop + 2);
			}
			else
			{
				_qtTabPulleyChecks.Check($"{step}: the same held pull opens no menu (active items: {(activeItems is null ? "none" : string.Join(",", activeItems))})",
					activeItems is null);
			}
			Shot(dispatcher, $"tabpulley-{step}-held", () => Step(1, -1, () =>
			{
				QtHost.QtHostRuntime.InjectPointer(1, x, y0);
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900), () =>
				{
					var after = ReadTabSurfaces();
					var moved = after.Surfaces
						.Where(s => Math.Abs(DiagQml.Num(SurfaceY(after.Raw, s.Surface), 0) - DiagQml.Num(SurfaceY(rest.Raw, s.Surface), 0)) > 1)
						.Select(s => s.Surface).ToList();
					_qtTabPulleyChecks.Check($"{step}: released at the start, every surface is back where it rested (moved: [{string.Join(",", moved)}])", moved.Count == 0);
					next();
				});
			}));
		}));
	}

	/// <summary>contentY of the first surface of kind <paramref name="surface"/> in a <see cref="TabSurfacesJs"/> reading.</summary>
	private static string SurfaceY(string raw, string surface)
	{
		try
		{
			using var doc = System.Text.Json.JsonDocument.Parse(raw);
			foreach (var s in doc.RootElement.GetProperty("surfaces").EnumerateArray())
				if (s.GetProperty("s").GetString() == surface)
					return s.GetProperty("contentY").GetRawText();
		}
		catch
		{
		}
		return "0";
	}

	private void FinishTabPulleyDiag()
	{
		_qtTabPulleyChecks.CheckNoOffThreadCalls();
		_qtTabPulleyChecks.Accept("OK — pulley menus follow the selected tab: only Tab A carries them, B and C keep none, A has them again");
		QtHost.QtHostRuntime.Shutdown();
	}
}
