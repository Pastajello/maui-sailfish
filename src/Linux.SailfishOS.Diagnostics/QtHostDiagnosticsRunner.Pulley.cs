using Microsoft.Maui.Controls;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Pulley leg (MAUI_SAILFISH_QT_HOST_PULLEY_DIAG=1): pulley menus survive navigation across
/// a list page and a ScrollView page, via PopAsync and native back. Each step reads the QML
/// menu state and injects a real held pull under an SF-SHOT marker.
/// </summary>
internal sealed partial class QtHostDiagnosticsRunner
{
	private bool _qtPulleyDiag;
	private readonly DiagChecks _qtPulleyChecks = new("Qt pulley diag");


	/// <summary>One menu's QML state and live MenuItem texts ("!orphan" = unparented,
	/// "!outsidecolumn" = outside the menu's painted column).</summary>
	private sealed record MenuState(bool Found, bool Registered, List<string> Items, MenuState? CloneMenu);

	private const string PulleyStateJs = """
		(function(name){
		  var p=pageStack.currentPage; if(!p||!p.__mauiFindByName) return '';
		  var m=p.__mauiFindByName(name);
		  function st(menu){
		    if(!menu) return null;
		    var t=[];
		    for(var i=0;i<menu.__items.length;i++){
		      var it=menu.__items[i];
		      t.push(it ? (it.text+(!it.parent?'!orphan':(menu.mauiContentColumn&&it.parent!==menu.mauiContentColumn?'!outsidecolumn':''))) : 'null');
		    }
		    var f=menu.flickable;
		    var reg=!!f && (f.pullDownMenu===menu || f.pushUpMenu===menu);
		    return {registered:reg, items:t, clone: menu.__clone ? st(menu.__clone) : null};
		  }
		  return JSON.stringify(st(m));
		})
		""";

	/// <summary>While a pull is held: each drag surface of the page with its contentY and menu state.</summary>
	private const string HeldStateJs = """
		(function(){
		  var p=pageStack.currentPage; if(!p) return 'no page';
		  var primary=p.__mauiFindByName('maui_synth-pulldown');
		  function menu(m){ if(!m) return null;
		    var its=[]; var list=m.__items||[];
		    for(var i=0;i<list.length;i++){ var it=list[i]; var c=it.parent;
		      its.push({t:it.text, v:it.visible, o:it.opacity, y:Math.round(it.y), h:Math.round(it.height), w:Math.round(it.width),
		                cv:c?c.visible:null, co:c?c.opacity:null, cy:c?Math.round(c.y):null, ch:c?Math.round(c.height):null,
		                sy:Math.round(it.mapToItem(null,0,0).y)}); }
		    return {primary:m===primary, clone:!!(primary&&m===primary.__clone), active:m.active, visible:m.visible,
		            opacity:m.opacity, h:m.height, z:m.z, items:its}; }
		  var out=[];
		  var pf=p.mauiFlickable?p.mauiFlickable():null;
		  if(pf) out.push({surface:'page', interactive:pf.interactive, contentY:pf.contentY, dragging:pf.dragging, menu:menu(pf.pullDownMenu)});
		  var hosts=p.__hosts||{};
		  for(var id in hosts){ var h=hosts[id]; if(h.uri==='list-view'||h.uri==='scroll-view'){ var it=h.item;
		    out.push({surface:h.uri, interactive:it.interactive, contentY:it.contentY, dragging:it.dragging, menu:menu(it.pullDownMenu)}); } }
		  return JSON.stringify(out);
		})()
		""";

	/// <summary>The dragged surface's active menu has every item vertically inside the window.</summary>
	private static bool ItemsOnScreen(string heldJson) => ActiveItemYs(heldJson) is { Count: > 0 } ys && ys.All(y => y >= 0 && y < 2272);

	private static string OnScreenSummary(string heldJson) =>
		ActiveItemYs(heldJson) is { } ys ? "scene y [" + string.Join(",", ys) + "]" : "no active menu";

	private static List<double>? ActiveItemYs(string heldJson)
	{
		try
		{
			using var doc = System.Text.Json.JsonDocument.Parse(heldJson);
			foreach (var surface in doc.RootElement.EnumerateArray())
			{
				if (!surface.TryGetProperty("menu", out var m) || m.ValueKind != System.Text.Json.JsonValueKind.Object || !m.GetProperty("active").GetBoolean())
					continue;
				return m.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("sy").GetDouble()).ToList();
			}
		}
		catch
		{
		}
		return null;
	}

	private static MenuState ReadMenu(string objectName)
	{
		var json = QtHost.QtHostRuntime.Eval($"{PulleyStateJs}('{objectName}')");
		if (string.IsNullOrEmpty(json) || json == "null")
			return new MenuState(false, false, new(), null);
		try
		{
			using var doc = System.Text.Json.JsonDocument.Parse(json);
			return Parse(doc.RootElement);
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[Sailfish] Qt pulley diag: menu state unreadable ({ex.Message}): {json}");
			return new MenuState(false, false, new(), null);
		}

		static MenuState Parse(System.Text.Json.JsonElement e) => new(
			true,
			e.GetProperty("registered").GetBoolean(),
			e.GetProperty("items").EnumerateArray().Select(i => i.GetString() ?? "").ToList(),
			e.TryGetProperty("clone", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.Object ? Parse(c) : null);
	}

	private static string Describe(MenuState m) =>
		!m.Found ? "none"
		: $"{(m.Registered ? "reg" : "UNREG")}[{string.Join(",", m.Items)}]" +
		  (m.CloneMenu is { } c ? $" clone:{(c.Registered ? "reg" : "UNREG")}[{string.Join(",", c.Items)}]" : "");

	/// <summary>The menu (and its clone, if any) is registered and shows exactly <paramref name="want"/>.</summary>
	private static bool Shows(MenuState m, IReadOnlyList<string> want, bool expectClone) =>
		m.Found && m.Registered && m.Items.SequenceEqual(want) &&
		(!expectClone || (m.CloneMenu is { } c && c.Registered && c.Items.SequenceEqual(want)));

	// Page A's list sits in a RefreshView: the pulley owns the overscroll, so the pulley carries a Refresh entry (tracker S02).
	private RefreshView? _pulleyRefresh;
	private int _pulleyRefreshes;

	private ContentPage BuildPulleyPageA()
	{
		var list = new CollectionView
		{
			ItemsSource = Enumerable.Range(1, 30).Select(i => $"pulley row {i}").ToList(),
			ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label { Padding = new Thickness(16, 12) };
				label.BindingContextChanged += (_, _) => label.Text = label.BindingContext as string ?? string.Empty;
				return label;
			}),
		};
		_pulleyRefresh = new RefreshView { Content = list };
		_pulleyRefresh.Refreshing += (_, _) => _pulleyRefreshes++;
		var page = new ContentPage
		{
			Title = "Pulley A",
			Content = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) },
				Children = { new Label { Text = "pulley page A", Margin = new Thickness(16) }, _pulleyRefresh } },
		};
		Grid.SetRow(_pulleyRefresh, 1);
		page.ToolbarItems.Add(new ToolbarItem { Text = "A pull 1" });
		page.ToolbarItems.Add(new ToolbarItem { Text = "A pull 2" });
		page.ToolbarItems.Add(new ToolbarItem { Text = "A push", Order = ToolbarItemOrder.Secondary });
		return page;
	}

	private static ContentPage BuildPulleyPageB()
	{
		var page = new ContentPage
		{
			Title = "Pulley B",
		};
		// The main ScrollView is its own flickable under a fixed header, as in MAUI.
		var rows = new VerticalStackLayout { Padding = new Thickness(16), Spacing = 12 };
		for (var i = 1; i <= 30; i++)
			rows.Children.Add(new Label { Text = $"B row {i}", FontSize = 20 });
		var grid = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) } };
		grid.Add(new Label { Text = "B fixed header", FontSize = 24, Padding = new Thickness(16), BackgroundColor = Color.FromArgb("#403060") }, 0, 0);
		grid.Add(new ScrollView { Content = rows }, 0, 1);
		page.Content = grid;
		page.ToolbarItems.Add(new ToolbarItem { Text = "B pull" });
		return page;
	}

	private static readonly string[] PullA = { "A pull 1", "A pull 2", QtHost.QtHostPageRenderer.PulleyRefreshText };
	private static readonly string[] PushA = { "A push" };
	private static readonly string[] PullB = { "B pull" };

	/// <summary>
	/// Injects a slow pull down, holds it for the screenshot (Silica paints pulley items only
	/// during an active drag), then drags back and releases at the start so nothing activates.
	/// </summary>
	private void PullAndGrab(SailfishDispatcher dispatcher, double yPx, double distancePx, string shot, Action next)
	{
		const double x = 516;
		const int steps = 40;
		QtHost.QtHostRuntime.InjectPointer(0, x, yPx);
		void Step(int i, int direction, Action done)
		{
			if (i > steps)
			{
				done();
				return;
			}
			var t = direction > 0 ? i : steps - i;
			QtHost.QtHostRuntime.InjectPointer(2, x, yPx + distancePx * t / steps);
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(16), () => Step(i + 1, direction, done));
		}
		Step(1, +1, () => dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(400), () =>
		{
			var held = QtHost.QtHostRuntime.Eval(HeldStateJs);
			Console.Error.WriteLine($"[Sailfish] Qt pulley diag: held {shot}: {held}");
			_qtPulleyChecks.Check($"{shot}: held pull shows the active menu's items ON SCREEN ({OnScreenSummary(held)})", ItemsOnScreen(held));
			Shot(dispatcher, shot, () => Step(1, -1, () =>
			{
				QtHost.QtHostRuntime.InjectPointer(1, x, yPx);
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900), next);
			}));
		}));
	}

	private void CheckPageA(string leg)
	{
		var pull = ReadMenu("maui_synth-pulldown");
		var push = ReadMenu("maui_synth-pushup");
		_qtPulleyChecks.Check($"{leg}: page A pull {Describe(pull)} == reg[{string.Join(",", PullA)}] (+clone on the list), push {Describe(push)} == reg[{string.Join(",", PushA)}]",
			Shows(pull, PullA, expectClone: true) && Shows(push, PushA, expectClone: false));
	}

	private void RunQtPulleyDiagnostics(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var nav = RootNav;
		if (nav is null)
		{
			Console.Error.WriteLine("[Sailfish] Qt pulley diag: no NavigationPage — leg skipped; auto-shutdown");
			QtHost.QtHostRuntime.Shutdown();
			return;
		}
		var pageA = BuildPulleyPageA();
		// Pull start points in window px: page A's hosted list, page B's main ScrollView.
		const double pullA = 700, pullB = 600, distance = 800;
		Console.Error.WriteLine("[Sailfish] Qt pulley diag: leg A — push page A (ToolbarItems + CollectionView)");
		_ = nav.PushAsync(pageA);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(2000), () =>
		{
			CheckPageA("leg A first visit");
			PullAndGrab(dispatcher, pullA, distance, "pulley-a1-first-visit", () => CheckPulleyRefresh(renderer, dispatcher, () =>
			{
				Console.Error.WriteLine("[Sailfish] Qt pulley diag: leg B — push page B over A");
				_ = nav.PushAsync(BuildPulleyPageB());
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(2000), () =>
				{
					var pullMenuB = ReadMenu("maui_synth-pulldown");
					_qtPulleyChecks.Check($"leg B: page B pull {Describe(pullMenuB)} == reg[{string.Join(",", PullB)}] (+clone on the main ScrollView)", Shows(pullMenuB, PullB, expectClone: true));
					PullAndGrab(dispatcher, pullB, distance, "pulley-b-pushed", () => ScrollUnderFixedHeader(renderer, dispatcher, () =>
					{
						Console.Error.WriteLine("[Sailfish] Qt pulley diag: leg C — MAUI PopAsync back to A");
						_ = nav.PopAsync();
						dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(2000), () =>
						{
							CheckPageA("leg C after PopAsync");
							PullAndGrab(dispatcher, pullA, distance, "pulley-a2-after-popasync", () =>
							{
								Console.Error.WriteLine("[Sailfish] Qt pulley diag: leg D — push B again, then the native back gesture");
								_ = nav.PushAsync(BuildPulleyPageB());
								dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(2000), () =>
								{
									QtHost.QtHostRuntime.PopPage(immediate: true);
									dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(2000), () =>
									{
										CheckPageA("leg D after the native back");
										PullAndGrab(dispatcher, pullA, distance, "pulley-a3-after-native-back", FinishPulleyDiag);
									});
								});
							});
						});
					}));
				});
			}));
		});
	}

	/// <summary>Leg A2: the pulley's Refresh entry starts page A's RefreshView (Refreshing fires once, the pulley bar
	/// pulses while IsRefreshing), as the pull gesture would on a page without a pulley.</summary>
	private void CheckPulleyRefresh(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, Action next)
	{
		var index = Array.IndexOf(PullA, QtHost.QtHostPageRenderer.PulleyRefreshText);
		var before = _pulleyRefreshes;
		Console.Error.WriteLine($"[Sailfish] Qt pulley diag: leg A2 — pulley pick 'Refresh' (toolbar-activated index {index})");
		renderer.HandleNativeEvent("toolbar-activated", "{\"menu\":\"pull\",\"index\":" + index + "}");
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(600), () =>
		{
			var busy = QtHost.QtHostRuntime.Eval("(function(){var p=pageStack.currentPage;var m=p&&p.__mauiFindByName?p.__mauiFindByName('maui_synth-pulldown'):null;return m?String(m.busy):'no menu';})()");
			_qtPulleyChecks.Check($"leg A2: pulley 'Refresh' → RefreshView.Refreshing fired {_pulleyRefreshes - before}==1, IsRefreshing={_pulleyRefresh?.IsRefreshing}, pulley busy={busy}",
				_pulleyRefreshes - before == 1 && _pulleyRefresh?.IsRefreshing == true && busy == "true");
			Shot(dispatcher, "pulley-a1b-refreshing", () =>
			{
				if (_pulleyRefresh is { } refresh)
					refresh.IsRefreshing = false;   // the app finished refreshing
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(600), next);
			});
		});
	}

	/// <summary>An upward drag over page B's ScrollView scrolls only its content; the header
	/// and page flickable stay put.</summary>
	private void ScrollUnderFixedHeader(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, Action next)
	{
		var header = renderer.CurrentHosts.FirstOrDefault(h => h.IsAttached && h.Element is Label { Text: "B fixed header" });
		var row = renderer.CurrentHosts.FirstOrDefault(h => h.IsAttached && h.Element is Label { Text: "B row 5" });
		var scrollHost = renderer.CurrentHosts.FirstOrDefault(h => h.IsAttached && h.QmlUri == "scroll-view");
		if (header is null || row is null || scrollHost is null
		    || !QtHost.QtHostRuntime.TryItemGeometry(header.NativeHandle, out var header0)
		    || !QtHost.QtHostRuntime.TryItemGeometry(row.NativeHandle, out var row0))
		{
			_qtPulleyChecks.Check($"leg B2 setup: header/row/scroll-view hosts on page B (header={header is not null} row={row is not null} scroll={scrollHost is not null})", false);
			next();
			return;
		}
		const double x = 516, y0 = 1600, travel = 600;
		const int steps = 12;
		Console.Error.WriteLine($"[Sailfish] Qt pulley diag: leg B2 — upward drag over the ScrollView content at {x},{y0}");
		QtHost.QtHostRuntime.InjectPointer(0, x, y0);
		void Step(int i)
		{
			if (i > steps)
			{
				QtHost.QtHostRuntime.InjectPointer(1, x, y0 - travel);
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200), Verify);
				return;
			}
			QtHost.QtHostRuntime.InjectPointer(2, x, y0 - travel * i / steps);
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(16), () => Step(i + 1));
		}
		void Verify()
		{
			QtHost.QtHostRuntime.TryItemGeometry(header.NativeHandle, out var header1);
			QtHost.QtHostRuntime.TryItemGeometry(row.NativeHandle, out var row1);
			var scrollY = QtHost.QtHostRuntime.GetProperty(scrollHost.NativeHandle, "contentY");
			var pageY = QtHost.QtHostRuntime.Eval("pageStack.currentPage.mauiFlickable().contentY");
			var sy = DiagQml.Num(scrollY, 0);
			var py = DiagQml.Num(pageY, 0);
			_qtPulleyChecks.Check($"leg B2: the drag scrolled ONLY the ScrollView — header scene y {header0.Y:F0}→{header1.Y:F0} (fixed), 'B row 5' {row0.Y:F0}→{row1.Y:F0} (up), ScrollView contentY={sy:F0}>100, page flickable contentY={py:F0}==0",
				Math.Abs(header1.Y - header0.Y) <= 1 && row1.Y < row0.Y - 100 && sy > 100 && Math.Abs(py) < 1);
			Shot(dispatcher, "pulley-b2-scrolled-header-fixed", next);
		}
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(16), () => Step(1));
	}

	private void FinishPulleyDiag()
	{
		_qtPulleyChecks.Accept("OK — pulley menus keep their items across push/pop (MAUI and native back), on the page and the list surface");
		QtHost.QtHostRuntime.Shutdown();
	}
}
