using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Header leg (MAUI_SAILFISH_QT_HOST_HEADER_DIAG=1, tracker S20): a NavigationPage root whose page has a TitleView (a
/// label and a tappable button in the header band), a pushed page with HasNavigationBar=false over Canvas-painted shapes
/// (the collapsed PageHeader stays alive at 0 height), the bar turned back on at runtime, and a Shell page with
/// TabBarIsVisible=false (the sections' row gives way to the section's own contents), and a Shell page with a SearchHandler
/// (tracker S21: typed keys write Query, the enter key runs Command). An SF-SHOT per state.
/// </summary>
internal sealed partial class QtHostDiagnosticsRunner
{
	private bool _qtHeaderDiag;
	private readonly DiagChecks _qtHeaderChecks = new("Qt header diag");
	private int _headerTitleTaps;

	/// <summary>The current model page's header: {box: height of mauiHeaderBox, title: the PageHeader's text, status}.</summary>
	private const string HeaderStateJs = """
		(function(){
		  var p=pageStack.currentPage; if(!p) return '{}';
		  function F(o){ if(!o) return null; if(o.objectName==='mauiHeaderBox') return o; var k=o.children; if(k) for(var i=0;i<k.length;i++){ var r=F(k[i]); if(r) return r; } return null; }
		  var b=F(p); var h=b&&b.children.length?b.children[0]:null;
		  return JSON.stringify({box:b?Math.round(b.height):-1, title:h&&h.title!==undefined?h.title:null,
		    alive:!!(h&&h.visible), status:Math.round(p.statusHeight||0), inset:Math.round(p.topInset||0)});
		})()
		""";

	private sealed record HeaderState(double Box, string? Title, bool Alive, double Status, double Inset, string Raw);

	private static HeaderState ReadHeader()
	{
		var raw = QtHost.QtHostRuntime.Eval(HeaderStateJs);
		try
		{
			using var doc = System.Text.Json.JsonDocument.Parse(raw);
			var r = doc.RootElement;
			return new HeaderState(r.GetProperty("box").GetDouble(),
				r.GetProperty("title").ValueKind == System.Text.Json.JsonValueKind.String ? r.GetProperty("title").GetString() : null,
				r.GetProperty("alive").GetBoolean(), r.GetProperty("status").GetDouble(), r.GetProperty("inset").GetDouble(), raw);
		}
		catch
		{
			return new HeaderState(-1, null, false, 0, 0, raw);
		}
	}

	private void RunQtHeaderDiagnostics(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		if (_context.Window is not Microsoft.Maui.Controls.Window window)
		{
			Console.Error.WriteLine("[Sailfish] Qt header diag: no Controls Window — leg skipped; auto-shutdown");
			QtHost.QtHostRuntime.Shutdown();
			return;
		}
		var titleButton = new Button { Text = "HD tap" };
		titleButton.Clicked += (_, _) => _headerTitleTaps++;
		var root = new ContentPage
		{
			Title = "HD plain title",
			Content = new VerticalStackLayout { Padding = 16, Children = { new Label { Text = "header a body" } } },
		};
		NavigationPage.SetTitleView(root, new HorizontalStackLayout
		{
			Spacing = 16,
			Padding = new Thickness(16, 0),
			VerticalOptions = LayoutOptions.Center,
			Children = { new Label { Text = "HD title view", FontSize = 22, VerticalOptions = LayoutOptions.Center }, titleButton },
		});
		Console.Error.WriteLine("[Sailfish] Qt header diag: window root → NavigationPage { page with a TitleView }");
		window.Page = new NavigationPage(root);
		WhenStackIdle(dispatcher, () => HeaderTitleView(renderer, dispatcher, window, titleButton));
	}

	private void HeaderTitleView(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, Microsoft.Maui.Controls.Window window,
		Button titleButton)
	{
		var header = ReadHeader();
		Console.Error.WriteLine($"[Sailfish] Qt header diag: title view header {header.Raw}");
		_qtHeaderChecks.Check($"title view: header shown ({header.Box}>0), its title text cleared ('{header.Title}'=='')",
			header.Box > 0 && header.Title == string.Empty);
		var label = renderer.CurrentHosts.FirstOrDefault(h => h.IsAttached && h.Element is Label { Text: "HD title view" });
		var button = renderer.CurrentHosts.FirstOrDefault(h => h.IsAttached && ReferenceEquals(h.Element, titleButton));
		var inBand = label is not null && QtHost.QtHostRuntime.TryItemGeometry(label.NativeHandle, out var scene) &&
		             scene.Y >= header.Status - 1 && scene.Y + scene.Height <= header.Status + header.Box + 1;
		_qtHeaderChecks.Check($"title view: label and button hosted ({label is not null},{button is not null}), the label in the header band",
			label is not null && button is not null && inBand);
		Shot(dispatcher, "header-1-titleview", () =>
		{
			if (button is not null)
				InjectQtTapAtHost(button);
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(700), () =>
			{
				_qtHeaderChecks.Check($"title view: a tap on its button clicks it ({_headerTitleTaps}==1)", _headerTitleTaps == 1);
				HeaderPushHidden(renderer, dispatcher, window);
			});
		});
	}

	private void HeaderPushHidden(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, Microsoft.Maui.Controls.Window window)
	{
		var hidden = new ContentPage
		{
			Title = "HD hidden",
			Content = new VerticalStackLayout
			{
				Spacing = 12,
				Children =
				{
					new Label { Text = "header b body" },
					new Ellipse { Fill = Colors.OrangeRed, WidthRequest = 140, HeightRequest = 140, HorizontalOptions = LayoutOptions.Start },
					new RoundRectangle { CornerRadius = 18, Fill = Colors.SteelBlue, WidthRequest = 220, HeightRequest = 70, HorizontalOptions = LayoutOptions.Start },
					new BoxView { Color = Colors.SeaGreen, WidthRequest = 220, HeightRequest = 40, HorizontalOptions = LayoutOptions.Start },
				},
			},
		};
		NavigationPage.SetHasNavigationBar(hidden, false);
		Console.Error.WriteLine("[Sailfish] Qt header diag: push a page with HasNavigationBar=false over shapes");
		_ = ((NavigationPage)window.Page!).PushAsync(hidden);
		WhenStackIdle(dispatcher, () =>
		{
			var header = ReadHeader();
			Console.Error.WriteLine($"[Sailfish] Qt header diag: hidden header {header.Raw}");
			_qtHeaderChecks.Check($"no navigation bar: header 0 high ({header.Box}==0), the PageHeader kept alive ({header.Alive})",
				header.Box == 0 && header.Alive);
			var body = renderer.CurrentHosts.FirstOrDefault(h => h.IsAttached && h.Element is Label { Text: "header b body" });
			var top = body is not null && QtHost.QtHostRuntime.TryItemGeometry(body.NativeHandle, out var scene) ? scene.Y : double.NaN;
			_qtHeaderChecks.Check($"no navigation bar: content starts under the status area (y {top:F0} ≈ inset {header.Inset})",
				Math.Abs(top - header.Inset) <= 2);
			var shapes = renderer.CurrentHosts.Count(h => h.IsAttached && h.Element is Shape or BoxView);
			_qtHeaderChecks.Check($"no navigation bar: the shapes are hosted ({shapes}==3)", shapes == 3);
			Shot(dispatcher, "header-2-hidden", () =>
			{
				NavigationPage.SetHasNavigationBar(hidden, true);
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900), () =>
				{
					var back = ReadHeader();
					var bodyTop = body is not null && QtHost.QtHostRuntime.TryItemGeometry(body.NativeHandle, out var s2) ? s2.Y : double.NaN;
					_qtHeaderChecks.Check($"bar back on at runtime: header {back.Box}>0, title '{back.Title}', content moved to {bodyTop:F0}≈{back.Inset}",
						back.Box > 0 && back.Title == "HD hidden" && Math.Abs(bodyTop - back.Inset) <= 2);
					Shot(dispatcher, "header-3-shown", () => HeaderShellTabs(renderer, dispatcher, window));
				});
			});
		});
	}

	private void HeaderShellTabs(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, Microsoft.Maui.Controls.Window window)
	{
		var first = new ContentPage { Title = "HD first", Content = new Label { Text = "header shell body" } };
		var section = new Tab { Title = "HD one" };
		section.Items.Add(new ShellContent { Title = "HD first", Content = first });
		section.Items.Add(new ShellContent { Title = "HD second", Content = new ContentPage { Title = "HD second", Content = new Label { Text = "second" } } });
		var bar = new TabBar();
		bar.Items.Add(section);
		bar.Items.Add(new Tab { Title = "HD two", Items = { new ShellContent { Content = new ContentPage { Title = "HD two", Content = new Label { Text = "two" } } } } });
		var shell = new Shell();
		shell.Items.Add(bar);
		Shell.SetTabBarIsVisible(first, false);
		Console.Error.WriteLine("[Sailfish] Qt header diag: window root → Shell { HD one (first, second), HD two }, first page TabBarIsVisible=false");
		window.Page = shell;
		WhenStackIdle(dispatcher, () =>
		{
			var tabs = TabState();
			var sub = QtHost.QtHostRuntime.Eval("(function(){var p=pageStack.currentPage;return p&&p.mauiSubTabs!==undefined?p.mauiSubTabs.join(','):'';})()");
			_qtHeaderChecks.Check($"TabBarIsVisible=false: the sections' row gives way to the contents ('{tabs}'=='HD first,HD second@0', sub '{sub}'=='')",
				tabs == "HD first,HD second@0" && sub == string.Empty);
			Shot(dispatcher, "header-4-tabbar-hidden", () =>
			{
				Shell.SetTabBarIsVisible(first, true);
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900), () =>
				{
					var again = TabState();
					var subAgain = QtHost.QtHostRuntime.Eval("(function(){var p=pageStack.currentPage;return p&&p.mauiSubTabs!==undefined?p.mauiSubTabs.join(','):'';})()");
					_qtHeaderChecks.Check($"TabBarIsVisible back on: sections '{again}'=='HD one,HD two@0', contents under them '{subAgain}'=='HD first,HD second'",
						again == "HD one,HD two@0" && subAgain == "HD first,HD second");
					Shot(dispatcher, "header-5-tabbar-shown", () => HeaderSearch(renderer, dispatcher, window));
				});
			});
		});
	}

	/// <summary>The search field: {box: height of mauiSearchBox, placeholder, text, focus, cx/cy: its centre in scene units}.</summary>
	private const string SearchStateJs = """
		(function(){
		  var p=pageStack.currentPage; if(!p) return '{}';
		  function F(o,n){ if(!o) return null; if(o.objectName===n) return o; var k=o.children; if(k) for(var i=0;i<k.length;i++){ var r=F(k[i],n); if(r) return r; } return null; }
		  var b=F(p,'mauiSearchBox'), f=F(p,'mauiSearchField'); var c=f?f.mapToItem(null,f.width/2,f.height/2):{x:-1,y:-1};
		  return JSON.stringify({box:b?Math.round(b.visible?b.height:0):-1, placeholder:f?f.placeholderText:null, text:f?f.text:null,
		    focus:!!(f&&f.activeFocus), cx:c.x, cy:c.y, inset:Math.round(p.topInset||0)});
		})()
		""";

	private static System.Text.Json.JsonElement ReadSearch()
	{
		try
		{
			using var doc = System.Text.Json.JsonDocument.Parse(QtHost.QtHostRuntime.Eval(SearchStateJs));
			return doc.RootElement.Clone();
		}
		catch
		{
			using var empty = System.Text.Json.JsonDocument.Parse("{}");
			return empty.RootElement.Clone();
		}
	}

	private static string Str(System.Text.Json.JsonElement e, string name) =>
		e.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() ?? "" : "";

	private static double Dbl(System.Text.Json.JsonElement e, string name) =>
		e.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number ? v.GetDouble() : double.NaN;

	/// <summary>Shell.SearchHandler (tracker S21): the field under the header; a real tap and typed keys write Query, the
	/// enter key runs Command, the app's own Query reaches the field.</summary>
	private void HeaderSearch(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, Microsoft.Maui.Controls.Window window)
	{
		var confirmed = new List<string>();
		var picked = new List<object>();
		var fruit = new[] { "apple", "apricot", "kiwi", "peach", "pear", "plum" };
		var handler = new HeaderSearchHandler(picked) { Placeholder = "HD find a fruit", ShowsResults = true };
		handler.Command = new Command(() => confirmed.Add(handler.Query ?? ""));
		// Filters as an app's OnQueryChanged would (tracker S22).
		handler.PropertyChanged += (_, e) =>
		{
			if (e.PropertyName == nameof(SearchHandler.Query))
				handler.ItemsSource = string.IsNullOrEmpty(handler.Query) ? null : fruit.Where(f => f.Contains(handler.Query, StringComparison.Ordinal)).ToList();
		};
		var page = new ContentPage { Title = "HD search", Content = new Label { Text = "header search body" } };
		Shell.SetSearchHandler(page, handler);
		var shell = new Shell();
		shell.Items.Add(new ShellContent { Content = page });
		Console.Error.WriteLine("[Sailfish] Qt header diag: window root → Shell { page with a SearchHandler }");
		window.Page = shell;
		WhenStackIdle(dispatcher, () =>
		{
			var state = ReadSearch();
			Console.Error.WriteLine($"[Sailfish] Qt header diag: search field {state}");
			var body = renderer.CurrentHosts.FirstOrDefault(h => h.IsAttached && h.Element is Label { Text: "header search body" });
			var top = body is not null && QtHost.QtHostRuntime.TryItemGeometry(body.NativeHandle, out var scene) ? scene.Y : double.NaN;
			_qtHeaderChecks.Check($"search: the field under the header ({Dbl(state, "box")}>0, placeholder '{Str(state, "placeholder")}'), the content below it (y {top:F0} ≈ inset {Dbl(state, "inset")})",
				Dbl(state, "box") > 0 && Str(state, "placeholder") == "HD find a fruit" && Math.Abs(top - Dbl(state, "inset")) <= 2);
			DiagQml.Tap(Dbl(state, "cx"), Dbl(state, "cy"));
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(800), () =>
			{
				foreach (var c in "KIWI")
					InjectQtKeyTap(QtHost.QtHostRuntime.QtKeyA + (c - 'A'), char.ToLowerInvariant(c).ToString());
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(700), () =>
				{
					var typed = ReadSearch();
					var focused = typed.TryGetProperty("focus", out var fv) && fv.ValueKind == System.Text.Json.JsonValueKind.True;
					_qtHeaderChecks.Check($"search: a tap focuses the field ({focused}), typed keys reach Query ('{handler.Query}'=='kiwi', field '{Str(typed, "text")}')",
						focused && handler.Query == "kiwi" && Str(typed, "text") == "kiwi");
					_qtHeaderChecks.Check($"results: the filtered list shows over the content ({ResultsRows(renderer)}==1)", ResultsRows(renderer) == 1);
					Shot(dispatcher, "header-6-search", () =>
					{
						InjectQtKeyTap(QtHost.QtHostRuntime.QtKeyReturn, "\r");
						dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(600), () =>
						{
							_qtHeaderChecks.Check($"search: the enter key runs Command with the query ([{string.Join(",", confirmed)}]==[kiwi]), the results close ({ResultsRows(renderer)}==-1)",
								confirmed.SequenceEqual(new[] { "kiwi" }) && ResultsRows(renderer) == -1);
							handler.Query = "p";
							dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900), () =>
							{
								var set = ReadSearch();
								_qtHeaderChecks.Check($"search: the app's Query reaches the field ('{Str(set, "text")}'=='p'), its results show ({ResultsRows(renderer)}==5)",
									Str(set, "text") == "p" && ResultsRows(renderer) == 5);
								Shot(dispatcher, "header-7-search-results", () =>
								{
									// Row 3 of [apple, apricot, peach, pear, plum].
									if (renderer.Collection.TryGetRowPoint(3, out var rx, out var ry))
										DiagQml.Tap(rx, ry);
									dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900), () =>
									{
										_qtHeaderChecks.Check($"results: a tapped row selects its item ([{string.Join(",", picked)}]==[pear], SelectedItem '{handler.SelectedItem}'), the list closes ({ResultsRows(renderer)}==-1)",
											picked.SequenceEqual(new object[] { "pear" }) && Equals(handler.SelectedItem, "pear") && ResultsRows(renderer) == -1);
										Shot(dispatcher, "header-8-search-picked", () => HeaderFlyoutPresented(renderer, dispatcher, window));
									});
								});
							});
						});
					});
				});
			});
		});
	}

	/// <summary>The open context menu's entries: {active, items: [{text, cx, cy}]} (centres in scene units).</summary>
	private const string MenuStateJs = """
		(function(){
		  var p=pageStack.currentPage; if(!p) return '{}';
		  var a=p.__ctxAnchor, m=a?a.menu:null; var out={active:!!(m&&m.active), items:[]};
		  function W(o){ if(!o) return; if(o.text!==undefined && o.clicked!==undefined && o.visible){ var c=o.mapToItem(null,o.width/2,o.height/2); out.items.push({text:o.text,cx:c.x,cy:c.y}); }
		    var k=o.children; if(k) for(var i=0;i<k.length;i++) W(k[i]); }
		  if(m) W(m);
		  return JSON.stringify(out);
		})()
		""";

	/// <summary>Shell.FlyoutIsPresented from a button (tracker S23): a real tap opens the flyout entries as a Silica
	/// ContextMenu, a real tap on an entry switches the item and writes FlyoutIsPresented back to false.</summary>
	private void HeaderFlyoutPresented(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, Microsoft.Maui.Controls.Window window)
	{
		var shell = new Shell();
		var menuButton = new Button { Text = "HD menu" };
		menuButton.Clicked += (_, _) => shell.FlyoutIsPresented = true;
		shell.Items.Add(new FlyoutItem { Title = "HD flyout one", Items = { new ShellContent { Content = new ContentPage
		{
			Title = "HD flyout one",
			Content = new VerticalStackLayout { Padding = 16, Children = { new Label { Text = "header flyout body" }, menuButton } },
		} } } });
		shell.Items.Add(new FlyoutItem { Title = "HD flyout two", Items = { new ShellContent { Content = new ContentPage
		{
			Title = "HD flyout two", Content = new Label { Text = "header flyout two" },
		} } } });
		Console.Error.WriteLine("[Sailfish] Qt header diag: window root → Shell { two flyout items, a menu button }");
		window.Page = shell;
		WhenStackIdle(dispatcher, () =>
		{
			var button = renderer.CurrentHosts.FirstOrDefault(h => h.IsAttached && ReferenceEquals(h.Element, menuButton));
			if (button is not null)
				InjectQtTapAtHost(button);
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200), () =>
			{
				var raw = QtHost.QtHostRuntime.Eval(MenuStateJs);
				Console.Error.WriteLine($"[Sailfish] Qt header diag: flyout menu {raw}");
				using var doc = System.Text.Json.JsonDocument.Parse(raw);
				var active = doc.RootElement.TryGetProperty("active", out var a) && a.ValueKind == System.Text.Json.JsonValueKind.True;
				var items = doc.RootElement.TryGetProperty("items", out var it) ? it.EnumerateArray().Select(e => e.Clone()).ToList() : new();
				var texts = string.Join(",", items.Select(e => Str(e, "text")));
				_qtHeaderChecks.Check($"FlyoutIsPresented: the button opens the flyout entries as a ContextMenu (active {active}, [{texts}] has both items, IsPresented {shell.FlyoutIsPresented})",
					active && texts.Contains("HD flyout one", StringComparison.Ordinal) && texts.Contains("HD flyout two", StringComparison.Ordinal) && shell.FlyoutIsPresented);
				Shot(dispatcher, "header-9-flyout-menu", () =>
				{
					var two = items.FirstOrDefault(e => Str(e, "text") == "HD flyout two");
					if (two.ValueKind == System.Text.Json.JsonValueKind.Object)
						DiagQml.Tap(Dbl(two, "cx"), Dbl(two, "cy"));
					dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1500), () =>
					{
						_qtHeaderChecks.Check($"FlyoutIsPresented: a tapped entry switches the item ('{shell.CurrentItem?.Title}'=='HD flyout two') and writes back false ({shell.FlyoutIsPresented})",
							shell.CurrentItem?.Title == "HD flyout two" && !shell.FlyoutIsPresented);
						Shot(dispatcher, "header-10-flyout-picked", FinishHeaderDiag);
					});
				});
			});
		});
	}

	/// <summary>Rows of the search results list on the page, -1 when none shows.</summary>
	private static int ResultsRows(QtHost.QtHostPageRenderer renderer)
	{
		var list = renderer.CurrentHosts.FirstOrDefault(h => h.IsAttached && h.Element is CollectionView);
		return list?.Element is CollectionView { ItemsSource: System.Collections.IEnumerable items } ? items.Cast<object>().Count() : -1;
	}

	private sealed class HeaderSearchHandler(List<object> picked) : SearchHandler
	{
		protected override void OnItemSelected(object item) => picked.Add(item);
	}

	private void FinishHeaderDiag()
	{
		_qtHeaderChecks.CheckNoOffThreadCalls();
		_qtHeaderChecks.Accept("OK — TitleView in the header band, HasNavigationBar=false collapses the header over painting shapes, Shell.TabBarIsVisible hides the sections' row, Shell.SearchHandler's field writes Query and confirms it, its results list picks an item, FlyoutIsPresented opens the flyout entries");
		QtHost.QtHostRuntime.Shutdown();
	}
}
