using System.Collections.ObjectModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Silica parity leg (MAUI_SAILFISH_QT_HOST_SILICA_DIAG=1): native Sailfish idioms a MAUI app gets without
/// Sailfish-specific code, each driven through its MAUI API and read back from the Silica object that renders it.
///   A. CollectionView scroll bar: the SilicaListView VerticalScrollDecorator (Default while moving, Always, Never).
///   B. A text EmptyView is Silica's ViewPlaceholder, shown only while the list is empty.
///   C. Page.IsBusy: the pull-down bar pulses (PullDownMenu.busy) on a page with a pulley, else a PageBusyIndicator.
///   D. ToolbarItem.IsEnabled / Text reach the pulley MenuItem in place.
///   E. TimePicker.IsOpen pushes the Silica TimePickerDialog; accepting it writes Time back and closes IsOpen.
///   F. Focusing an Entry at the end of a ScrollView keeps it above the virtual keyboard (Silica auto-scroll).
///   G. Rotation: a landscape window re-lays the MAUI page out landscape and DeviceDisplay follows; back to portrait.
///   H. SailfishRemorse: a RemorsePopup / a RemorseItem over a list row count down and run the action, a tap undoes it.
///   I. A horizontal GridItemsLayout scrolls along x with Span cells stacked in each column; a tap picks the cell.
///   J. MCE lifecycle state: display on, lock screen off and a known memory level while the app is in front.
///   K. Opening URLs: the installed .desktop declares the scheme and the D-Bus method (as native apps do), the D-Bus
///      activation file exists, and a call to the app's own openUrl reaches Application.OnAppLinkRequestReceived.
///   L. Dispatcher timers: DispatchDelayed fires on time when scheduled in bursts, from timer callbacks, from queued
///      work and right after an immediate modal pop (the case that once waited ~2 s for the heartbeat).
///   M. Native lifecycle events reach SailfishMauiApplication / AddSailfish handlers: the keyboard (F) and rotation (G)
///      raised theirs, an ambience report raises OnColorSchemeChanged, and minimizing to the home screen (Silica
///      window.deactivate) and back raises OnCoverStatusChanged and OnApplicationStateChanged.
///   N. Launcher.OpenAsync(OpenFileRequest): a missing file is false; an image opens in the system's viewer (this app
///      leaves the front), then the app comes back.
///   O. Two levels back: after pushing three pages, each pop reveals its page from the retention/page cache — no
///      row is rebuilt (the second pop used to rebuild the page, 847 ms) — with every visible list row painted.
/// </summary>
internal sealed partial class QtHostDiagnosticsRunner
{
	private bool _qtSilicaDiag;
	private readonly DiagChecks _qtSilicaChecks = new("Qt silica diag");

	/// <summary>Resumes after <paramref name="ms"/> on the Qt thread (the continuation runs inside the dispatcher callback).</summary>
	private static Task SilicaWait(SailfishDispatcher dispatcher, int ms)
	{
		var done = new TaskCompletionSource();
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(ms), () => done.SetResult());
		return done.Task;
	}

	/// <summary><see cref="Shot"/> awaited: the state is held for the screenshot before the leg moves on.</summary>
	private static Task SilicaShot(SailfishDispatcher dispatcher, string name)
	{
		var done = new TaskCompletionSource();
		Shot(dispatcher, name, () => done.SetResult());
		return done.Task;
	}

	/// <summary>A property of the first object named <paramref name="name"/> under the current page, as a string.</summary>
	private static string SilicaNamed(string name, string expr) => QtHost.QtHostRuntime.Eval(
		"(function(){function F(o){if(!o)return null;if(o.objectName==='" + name + "')return o;var k=o.children;if(k)for(var i=0;i<k.length;++i){var r=F(k[i]);if(r)return r;}return null;}" +
		"var o=F(pageStack.currentPage);return o?String(" + expr + "):'absent';})()");

	private static string SilicaHost(QtHost.QtHostPageRenderer renderer, Element element, string expr) =>
		QtHost.QtHostRuntime.Eval($"(function(){{var o={ItemJs(renderer, element)};return o?String({expr}):'absent';}})()");

	/// <summary>A slow finger drag in window pixels.</summary>
	private static async Task SilicaDrag(SailfishDispatcher dispatcher, double x, double y0, double y1, int steps = 16)
	{
		QtHost.QtHostRuntime.InjectPointer(0, x, y0);
		for (var i = 1; i <= steps; i++)
		{
			await SilicaWait(dispatcher, 16);
			QtHost.QtHostRuntime.InjectPointer(2, x, y0 + (y1 - y0) * i / steps);
		}
		QtHost.QtHostRuntime.InjectPointer(1, x, y1);
	}

	private void RunQtSilicaDiagnostics(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var nav = RootNav;
		if (nav is null)
		{
			Console.Error.WriteLine("[Sailfish] Qt silica diag: no NavigationPage — leg skipped; auto-shutdown");
			QtHost.QtHostRuntime.Shutdown();
			return;
		}
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200), async () =>
		{
			foreach (var (name, part) in new (string, Func<Task>)[]
			{
				("A scroll decorator", () => SilicaScrollBarA(renderer, dispatcher, nav)),
				("B placeholder", () => SilicaPlaceholderB(renderer, dispatcher, nav)),
				("C/D busy + menu items", () => SilicaBusyAndMenuCD(renderer, dispatcher, nav)),
				("E time dialog", () => SilicaTimeDialogE(renderer, dispatcher, nav)),
				("F keyboard", () => SilicaKeyboardF(renderer, dispatcher, nav)),
				("G rotation", () => SilicaRotationG(renderer, dispatcher, nav)),
				("H remorse", () => SilicaRemorseH(renderer, dispatcher, nav)),
				("I horizontal grid", () => SilicaHorizontalGridI(renderer, dispatcher, nav)),
				("J MCE state", () => SilicaMceJ(dispatcher)),
				("K open url", () => SilicaOpenUrlK(dispatcher)),
				("L dispatcher timers", () => SilicaTimersL(dispatcher, nav)),
				("M lifecycle events", () => SilicaLifecycleM(dispatcher)),
				("O two levels back", () => SilicaTwoLevelsBackO(renderer, dispatcher, nav)),
				("P insert, cover, back", () => SilicaInsertCoverBackP(renderer, dispatcher, nav)),
				// Last: the viewer it starts may come to the front seconds later and leave this app Inactive.
				("N open file", () => SilicaOpenFileN(dispatcher)),
			})
			{
				try
				{
					Console.Error.WriteLine($"[Sailfish] Qt silica diag: part {name}");
					await part();
				}
				catch (Exception ex)
				{
					_qtSilicaChecks.Check($"{name} threw {ex.GetType().Name}: {ex.Message}", false);
				}
			}
			_qtSilicaChecks.CheckNoOffThreadCalls();
			_qtSilicaChecks.Accept("OK — MAUI APIs land on the native Silica idioms (scroll decorator, placeholder, busy, pulley items, time dialog, keyboard, rotation, remorse)");
			QtHost.QtHostRuntime.Shutdown();
		});
	}

	private static ContentPage SilicaPage(string title, View content) => new() { Title = title, Content = content };

	private static CollectionView SilicaList(IEnumerable<string> items) => new()
	{
		ItemsSource = items,
		ItemTemplate = new DataTemplate(() =>
		{
			var row = new Label { Padding = new Thickness(16, 12) };
			row.BindingContextChanged += (_, _) => row.Text = row.BindingContext as string ?? string.Empty;
			return row;
		}),
	};

	private async Task SilicaScrollBarA(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, NavigationPage nav)
	{
		var list = SilicaList(Enumerable.Range(1, 60).Select(i => $"decorated row {i}").ToList());
		await nav.PushAsync(SilicaPage("Silica A", list));
		await SilicaWait(dispatcher, 1500);
		// "h=<opacity|hidden> v=<opacity|hidden>"
		string Bars() => SilicaHost(renderer, list, "o.mauiBars");
		var rest = Bars();
		_qtSilicaChecks.Check($"A Default: the list has a native VerticalScrollDecorator, transparent at rest ('{rest}' has v=0, no h)",
			rest.Contains("h=hidden") && rest.Contains("v=0"));
		// Silica shows the decorator while the list moves.
		var moving = "";
		QtHost.QtHostRuntime.InjectPointer(0, 516, 1700);
		for (var i = 1; i <= 16; i++)
		{
			await SilicaWait(dispatcher, 16);
			QtHost.QtHostRuntime.InjectPointer(2, 516, 1700 - 60 * i);
		}
		await SilicaWait(dispatcher, 120);
		moving = Bars();
		QtHost.QtHostRuntime.InjectPointer(1, 516, 740);
		var movingOpacity = DiagQml.Num(moving.Split("v=").LastOrDefault(), 0);
		_qtSilicaChecks.Check($"A Default: the decorator shows while the list moves ('{moving}', v>0)", movingOpacity > 0.05);
		list.VerticalScrollBarVisibility = ScrollBarVisibility.Always;
		await SilicaWait(dispatcher, 1600);
		_qtSilicaChecks.Check($"A Always: the decorator stays shown at rest ('{Bars()}' has v=1)", Bars().Contains("v=1"));
		list.VerticalScrollBarVisibility = ScrollBarVisibility.Never;
		await SilicaWait(dispatcher, 600);
		_qtSilicaChecks.Check($"A Never: no decorator ('{Bars()}' has v=hidden)", Bars().Contains("v=hidden"));
		await nav.PopAsync();
		await SilicaWait(dispatcher, 900);
	}

	private async Task SilicaPlaceholderB(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, NavigationPage nav)
	{
		var items = new ObservableCollection<string>();
		var list = SilicaList(items);
		list.EmptyView = "Nothing here yet";
		await nav.PushAsync(SilicaPage("Silica B", list));
		await SilicaWait(dispatcher, 1500);
		NativeElementHostOf(renderer, list, out var host);
		var name = $"maui_{host?.Id}__placeholder";
		string State() => SilicaNamed(name, "o.enabled+'|'+o.text+'|'+(o.visible&&o.opacity>0.5)");
		var empty = State();
		_qtSilicaChecks.Check($"B text EmptyView → Silica ViewPlaceholder enabled with the text, painted ('{empty}'=='true|Nothing here yet|true')",
			empty == "true|Nothing here yet|true");
		var slotHosts = renderer.CurrentHosts.Count(h => h.IsAttached && h.Element is Label { Text: "Nothing here yet" });
		_qtSilicaChecks.Check($"B no MAUI Label duplicates the placeholder ({slotHosts}==0)", slotHosts == 0);
		items.Add("first item");
		await SilicaWait(dispatcher, 1200);
		_qtSilicaChecks.Check($"B an item arrives → the placeholder switches off ('{State()}' starts with false)", State().StartsWith("false|", StringComparison.Ordinal));
		items.Clear();
		await SilicaWait(dispatcher, 1200);
		_qtSilicaChecks.Check($"B the list empties again → the placeholder is back ('{State()}' starts with true)", State().StartsWith("true|", StringComparison.Ordinal));
		await nav.PopAsync();
		await SilicaWait(dispatcher, 900);
	}

	private async Task SilicaBusyAndMenuCD(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, NavigationPage nav)
	{
		// C1/D: a page with a pulley.
		var refresh = new ToolbarItem { Text = "Refresh" };
		var later = new ToolbarItem { Text = "Later", IsEnabled = false };
		var pulleyPage = SilicaPage("Silica C", new Label { Text = "busy with a pulley", Margin = new Thickness(16) });
		pulleyPage.ToolbarItems.Add(refresh);
		pulleyPage.ToolbarItems.Add(later);
		await nav.PushAsync(pulleyPage);
		await SilicaWait(dispatcher, 1500);
		const string menu = "(function(){var p=pageStack.currentPage;return p&&p.__mauiFindByName?p.__mauiFindByName('maui_synth-pulldown'):null;})()";
		string Menu(string expr) => QtHost.QtHostRuntime.Eval($"(function(){{var m={menu};return m?String({expr}):'absent';}})()");
		string Items() => Menu("m.__items.map(function(i){return i.text+':'+i.enabled;}).join(',')");
		_qtSilicaChecks.Check($"D disabled ToolbarItem → disabled pulley MenuItem ('{Items()}'=='Refresh:true,Later:false')", Items() == "Refresh:true,Later:false");
		later.IsEnabled = true;
		later.Text = "Now";
		await SilicaWait(dispatcher, 700);
		_qtSilicaChecks.Check($"D IsEnabled/Text changes update the MenuItem ('{Items()}'=='Refresh:true,Now:true')", Items() == "Refresh:true,Now:true");

		_qtSilicaChecks.Check($"C at rest the pulley is not busy (busy={Menu("m.busy")})", Menu("m.busy") == "false");
		pulleyPage.IsBusy = true;
		await SilicaWait(dispatcher, 700);
		// The Silica busy timer flips the bar's _inactiveOpacity every 500 ms.
		const string bar = "(function(){var k=m.children;for(var i=0;i<k.length;++i)if(k[i].hasOwnProperty('_inactiveOpacity'))return k[i]._inactiveOpacity;return -1;})()";
		var samples = new List<string>();
		for (var i = 0; i < 4; i++)
		{
			samples.Add(Menu(bar));
			await SilicaWait(dispatcher, 260);
		}
		var pageBusy = SilicaNamed("mauiPageBusy", "o.running");
		_qtSilicaChecks.Check($"C Page.IsBusy with a pulley → PullDownMenu.busy={Menu("m.busy")}, the bar pulses ({string.Join(",", samples)}), no PageBusyIndicator (running={pageBusy})",
			Menu("m.busy") == "true" && samples.Distinct().Count() > 1 && pageBusy == "false");
		await SilicaShot(dispatcher, "silica-c-busy-pulley");
		pulleyPage.IsBusy = false;
		await SilicaWait(dispatcher, 900);
		_qtSilicaChecks.Check($"C IsBusy=false → the pulley stops (busy={Menu("m.busy")})", Menu("m.busy") == "false");
		await nav.PopAsync();
		await SilicaWait(dispatcher, 900);

		// C2: a page without a pulley.
		var plain = SilicaPage("Silica C2", new Label { Text = "busy without a pulley", Margin = new Thickness(16) });
		await nav.PushAsync(plain);
		await SilicaWait(dispatcher, 1500);
		plain.IsBusy = true;
		await SilicaWait(dispatcher, 900);
		var running = SilicaNamed("mauiPageBusy", "o.running+'|'+(o.visible&&o.opacity>0.5)");
		_qtSilicaChecks.Check($"C Page.IsBusy without a pulley → Silica PageBusyIndicator runs and paints ('{running}'=='true|true')", running == "true|true");
		await SilicaShot(dispatcher, "silica-c2-page-busy");
		plain.IsBusy = false;
		await SilicaWait(dispatcher, 900);
		_qtSilicaChecks.Check($"C IsBusy=false → the indicator stops (running={SilicaNamed("mauiPageBusy", "o.running")})", SilicaNamed("mauiPageBusy", "o.running") == "false");
		await nav.PopAsync();
		await SilicaWait(dispatcher, 900);
	}

	private async Task SilicaTimeDialogE(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, NavigationPage nav)
	{
		var picker = new TimePicker { Time = new TimeSpan(9, 15, 0), Margin = new Thickness(16) };
		await nav.PushAsync(SilicaPage("Silica E", new VerticalStackLayout { Children = { picker } }));
		await SilicaWait(dispatcher, 1500);
		var depth0 = DiagQml.EvalNum("pageStack.depth");
		picker.IsOpen = true;
		await SilicaWait(dispatcher, 1500);
		var depth1 = DiagQml.EvalNum("pageStack.depth");
		var dialog = QtHost.QtHostRuntime.Eval("(function(){var d=pageStack.currentPage;return d&&d.hour!==undefined&&d.accept?d.hour+':'+d.minute:'none';})()");
		_qtSilicaChecks.Check($"E TimePicker.IsOpen → the Silica TimePickerDialog is pushed (depth {depth0}→{depth1}) on the current time ('{dialog}'=='9:15')",
			depth1 == depth0 + 1 && dialog == "9:15");
		await SilicaShot(dispatcher, "silica-e-time-dialog");
		await SilicaWait(dispatcher, 300);
		// Turn the dialog's clock wheel (its inner TimePicker, which onDone copies back), then the dialog's own
		// accept path (what the DialogHeader accept does).
		var wheel = QtHost.QtHostRuntime.Eval("(function(){var d=pageStack.currentPage;function F(o){if(!o)return null;if(o!==d&&o.hour!==undefined&&o.minute!==undefined&&o._formatTime)return o;var k=o.children;if(k)for(var i=0;i<k.length;++i){var r=F(k[i]);if(r)return r;}return null;}" +
			"var t=F(d);if(!t)return 'no wheel';t.hour=13;t.minute=45;d.accept();return 'ok';})()");
		Console.Error.WriteLine($"[Sailfish] Qt silica diag: E wheel set + accept: {wheel}");
		await SilicaWait(dispatcher, 1600);
		_qtSilicaChecks.Check($"E accepting the dialog writes Time back ({picker.Time}==13:45:00), IsOpen {picker.IsOpen}==False, dialog popped (depth {DiagQml.EvalNum("pageStack.depth")}=={depth0})",
			picker.Time == new TimeSpan(13, 45, 0) && !picker.IsOpen && DiagQml.EvalNum("pageStack.depth") == depth0);
		await nav.PopAsync();
		await SilicaWait(dispatcher, 900);
	}

	private async Task SilicaKeyboardF(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, NavigationPage nav)
	{
		var rows = new VerticalStackLayout { Padding = new Thickness(16), Spacing = 12 };
		for (var i = 1; i <= 24; i++)
			rows.Children.Add(new Label { Text = $"filler {i}", FontSize = 20 });
		var entry = new Entry { Placeholder = "type here" };
		rows.Children.Add(entry);
		var scroll = new ScrollView { Content = rows };
		await nav.PushAsync(SilicaPage("Silica F", scroll));
		await SilicaWait(dispatcher, 1500);
		await scroll.ScrollToAsync(entry, ScrollToPosition.End, false);
		await SilicaWait(dispatcher, 800);
		entry.Focus();
		var shown = false;
		for (var i = 0; i < 20 && !shown; i++)
		{
			await SilicaWait(dispatcher, 200);
			shown = QtHost.QtHostRuntime.Eval("Qt.inputMethod.visible?'1':'0'") == "1";
		}
		await SilicaWait(dispatcher, 900);   // the keyboard slides in, Silica scrolls after it
		var kbTop = DiagQml.EvalNum("Qt.inputMethod.keyboardRectangle.y");
		var kbH = DiagQml.EvalNum("Qt.inputMethod.keyboardRectangle.height");
		NativeElementHostOf(renderer, entry, out var eh);
		var entryBottom = eh is not null && QtHost.QtHostRuntime.TryItemGeometry(eh.NativeHandle, out var g) ? g.Y + g.Height : double.NaN;
		Console.Error.WriteLine($"[Sailfish] Qt silica diag: F keyboard visible={shown} rect y={kbTop} h={kbH} entryBottom={entryBottom}");
		_qtSilicaChecks.Check($"F focusing the Entry raises the virtual keyboard (Qt.inputMethod.visible={shown}, height {kbH:F0})", shown && kbH > 100);
		// keyboardRectangle is in window pixels; the scene is the portrait window here.
		var top = kbTop > 0 ? kbTop : 2272 - kbH;
		_qtSilicaChecks.Check($"F the focused Entry stays above the keyboard (entry bottom {entryBottom:F0} ≤ keyboard top {top:F0})",
			shown && entryBottom <= top + 1);
		await SilicaShot(dispatcher, "silica-f-keyboard");
		await SilicaWait(dispatcher, 300);
		entry.Unfocus();
		await SilicaWait(dispatcher, 900);
		await nav.PopAsync();
		await SilicaWait(dispatcher, 900);
	}

	private async Task SilicaRotationG(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, NavigationPage nav)
	{
		var label = new Label { Text = "rotating page", Margin = new Thickness(16) };
		var page = SilicaPage("Silica G", new Grid { Children = { label } });
		await nav.PushAsync(page);
		await SilicaWait(dispatcher, 1500);
		var before = DeviceDisplay.Current.MainDisplayInfo.Orientation;
		// What the orientation sensor does natively: the window allows landscape only, Silica rotates the page.
		QtHost.QtHostRuntime.Eval("window.mauiOrientations = Orientation.Landscape");
		await SilicaWait(dispatcher, 2500);
		var winOrientation = QtHost.QtHostRuntime.Eval("String(window.orientation)");
		var info = DeviceDisplay.Current.MainDisplayInfo;
		_qtSilicaChecks.Check($"G landscape: Silica rotates the window (orientation {winOrientation}==2), DeviceDisplay reports {info.Orientation} (was {before}), the MAUI page lays out landscape ({page.Width:F0}x{page.Height:F0}, width > height)",
			winOrientation == "2" && info.Orientation == DisplayOrientation.Landscape && page.Width > page.Height);
		await SilicaShot(dispatcher, "silica-g-landscape");
		await SilicaWait(dispatcher, 300);
		QtHost.QtHostRuntime.Eval("window.mauiOrientations = Orientation.Portrait");
		await SilicaWait(dispatcher, 2500);
		info = DeviceDisplay.Current.MainDisplayInfo;
		_qtSilicaChecks.Check($"G back to portrait: window {QtHost.QtHostRuntime.Eval("String(window.orientation)")}==1, DeviceDisplay {info.Orientation}, page {page.Width:F0}x{page.Height:F0} (height > width)",
			QtHost.QtHostRuntime.Eval("String(window.orientation)") == "1" && info.Orientation == DisplayOrientation.Portrait && page.Height > page.Width);
		// Per page: the window allows every orientation, the page only landscape (SailfishPage.AllowedOrientations), and
		// Silica turns that page; Default hands it back to the window's default and it turns back.
		QtHost.QtHostRuntime.Eval("window.mauiOrientations = Orientation.All");
		SailfishPage.SetAllowedOrientations(page, SailfishOrientations.LandscapeMask);
		await SilicaWait(dispatcher, 2500);
		var pageMask = QtHost.QtHostRuntime.Eval("String(pageStack.currentPage.allowedOrientations)");
		var turned = QtHost.QtHostRuntime.Eval("String(window.orientation)");
		_qtSilicaChecks.Check($"G per page: SailfishPage.AllowedOrientations=LandscapeMask → page allowedOrientations {pageMask}==10, window {turned} is landscape, page {page.Width:F0}x{page.Height:F0}",
			pageMask == "10" && turned is "2" or "8" && page.Width > page.Height);
		await SilicaShot(dispatcher, "silica-g-page-landscape");
		SailfishPage.SetAllowedOrientations(page, SailfishOrientations.Default);
		QtHost.QtHostRuntime.Eval("window.mauiOrientations = Orientation.Portrait");
		await SilicaWait(dispatcher, 2500);
		_qtSilicaChecks.Check($"G per page reset: window {QtHost.QtHostRuntime.Eval("String(window.orientation)")}==1, page {page.Width:F0}x{page.Height:F0} (height > width)",
			QtHost.QtHostRuntime.Eval("String(window.orientation)") == "1" && page.Height > page.Width);
		await nav.PopAsync();
		await SilicaWait(dispatcher, 900);
	}

	/// <summary>The live Silica remorse objects under the current page: kind (item/popup), text, pending, scene rect,
	/// and for an item the objectName of what it covers.</summary>
	private const string RemorseJs = """
		(function(){
		  var out=[];
		  function walk(o){ if(!o) return;
		    if(o._countdown!==undefined && o.hasOwnProperty('_triggered')){
		      var p=o.mapToItem(null,0,0);
		      out.push({k:o._item!==undefined?'item':'popup', t:o.text, pending:!!o.pending, vis:o.visible&&o.opacity>0.5,
		        x:Math.round(p.x), y:Math.round(p.y), w:Math.round(o.width), h:Math.round(o.height),
		        over:o._item?o._item.objectName:''}); }
		    var k=o.children; if(k) for(var i=0;i<k.length;i++) walk(k[i]); }
		  walk(pageStack.currentPage);
		  return JSON.stringify(out);
		})()
		""";

	private static List<System.Text.Json.JsonElement> ReadRemorse()
	{
		try
		{
			using var doc = System.Text.Json.JsonDocument.Parse(QtHost.QtHostRuntime.Eval(RemorseJs));
			return doc.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
		}
		catch
		{
			return new();
		}
	}

	private async Task SilicaRemorseH(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, NavigationPage nav)
	{
		var items = new ObservableCollection<string>(Enumerable.Range(1, 10).Select(i => $"remorse row {i}"));
		var list = SilicaList(items);
		await nav.PushAsync(SilicaPage("Silica H", list));
		await SilicaWait(dispatcher, 1500);

		// H1: a page-wide action.
		var ran = 0;
		// Silica's own 4 s: the compositor screenshot lags a second or two behind the marker.
		var popup = SailfishRemorse.ExecuteAsync("Clearing", () => ran++);
		await SilicaWait(dispatcher, 500);
		var live = ReadRemorse().Where(r => r.GetProperty("k").GetString() == "popup").ToList();
		_qtSilicaChecks.Check($"H1 SailfishRemorse.ExecuteAsync(text) → a Silica RemorsePopup counts down at the top ({string.Join(";", live.Select(r => r.GetRawText()))})",
			live.Count == 1 && live[0].GetProperty("t").GetString() == "Clearing" && live[0].GetProperty("pending").GetBoolean() &&
			live[0].GetProperty("vis").GetBoolean() && live[0].GetProperty("y").GetDouble() < 400);
		await SilicaShot(dispatcher, "silica-h1-remorse-popup");
		await SilicaWait(dispatcher, SailfishRemorse.DefaultTimeoutMs);
		_qtSilicaChecks.Check($"H1 the countdown ends → the action ran ({ran}==1), the task is true ({(popup.IsCompleted ? popup.Result.ToString() : "pending")})",
			ran == 1 && popup.IsCompleted && popup.Result);

		// H2: tapping the popup undoes it (a real tap).
		var undone = SailfishRemorse.ExecuteAsync("Clearing", () => ran++, 4000);
		await SilicaWait(dispatcher, 600);
		var pop = ReadRemorse().FirstOrDefault(r => r.GetProperty("k").GetString() == "popup" && r.GetProperty("pending").GetBoolean());
		if (pop.ValueKind == System.Text.Json.JsonValueKind.Object)
			DiagQml.Tap(pop.GetProperty("x").GetDouble() + pop.GetProperty("w").GetDouble() / 2, pop.GetProperty("y").GetDouble() + pop.GetProperty("h").GetDouble() / 2);
		await SilicaWait(dispatcher, 900);
		_qtSilicaChecks.Check($"H2 a tap on the popup undoes it: the task is false ({(undone.IsCompleted ? undone.Result.ToString() : "pending")}), the action did not run ({ran}==1)",
			undone.IsCompleted && !undone.Result && ran == 1);
		await SilicaWait(dispatcher, 800);

		// H3: an action on one row — the RemorseItem covers the whole row.
		var row = renderer.Collection.RowView(2);
		var target = row is not null ? renderer.Collection.DelegateOf(row) : null;
		var gone = row is null
			? Task.FromResult(false)
			: SailfishRemorse.ExecuteAsync(row, "Deleting", () => items.RemoveAt(2), 1800);
		await SilicaWait(dispatcher, 500);
		var item = ReadRemorse().FirstOrDefault(r => r.GetProperty("k").GetString() == "item");
		var dgRect = target is null ? "" : SilicaNamed(target, "(function(){var p=o.mapToItem(null,0,0);return Math.round(p.y)+','+Math.round(o.height);})()");
		var itemRect = item.ValueKind == System.Text.Json.JsonValueKind.Object ? $"{item.GetProperty("y").GetDouble()},{item.GetProperty("h").GetDouble()}" : "none";
		_qtSilicaChecks.Check($"H3 ExecuteAsync(row view) → a Silica RemorseItem over the row's delegate '{target}' (covers '{(item.ValueKind == System.Text.Json.JsonValueKind.Object ? item.GetProperty("over").GetString() : "-")}', rect {itemRect} == delegate {dgRect}), 'Deleting' pending",
			target is not null && item.ValueKind == System.Text.Json.JsonValueKind.Object && item.GetProperty("over").GetString() == target &&
			itemRect == dgRect && item.GetProperty("t").GetString() == "Deleting" && item.GetProperty("pending").GetBoolean());
		await SilicaShot(dispatcher, "silica-h3-remorse-item");
		await SilicaWait(dispatcher, 2200);
		_qtSilicaChecks.Check($"H3 the countdown ends → the row's item is removed (count {items.Count}==9, 'remorse row 3' gone), the task is true",
			gone.IsCompleted && gone.Result && items.Count == 9 && !items.Contains("remorse row 3"));

		// H4: a tap on the row's remorse undoes it.
		await SilicaWait(dispatcher, 800);
		var row4 = renderer.Collection.RowView(3);
		var kept = row4 is null ? Task.FromResult(true) : SailfishRemorse.ExecuteAsync(row4, "Deleting", () => items.RemoveAt(3), 4000);
		await SilicaWait(dispatcher, 600);
		var cover = ReadRemorse().FirstOrDefault(r => r.GetProperty("k").GetString() == "item" && r.GetProperty("pending").GetBoolean());
		if (cover.ValueKind == System.Text.Json.JsonValueKind.Object)
			DiagQml.Tap(cover.GetProperty("x").GetDouble() + cover.GetProperty("w").GetDouble() / 2, cover.GetProperty("y").GetDouble() + cover.GetProperty("h").GetDouble() / 2);
		await SilicaWait(dispatcher, 900);
		_qtSilicaChecks.Check($"H4 a tap on the row's remorse undoes it: task false ({(kept.IsCompleted ? kept.Result.ToString() : "pending")}), count stays {items.Count}==9",
			kept.IsCompleted && !kept.Result && items.Count == 9);
		await nav.PopAsync();
		await SilicaWait(dispatcher, 900);
	}

	private async Task SilicaHorizontalGridI(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, NavigationPage nav)
	{
		var items = Enumerable.Range(1, 12).Select(i => $"cell {i}").ToList();
		var grid = new CollectionView
		{
			ItemsSource = items,
			SelectionMode = SelectionMode.Single,
			HeightRequest = 320,
			ItemsLayout = new GridItemsLayout(2, ItemsLayoutOrientation.Horizontal) { HorizontalItemSpacing = 12, VerticalItemSpacing = 8 },
			ItemTemplate = new DataTemplate(() =>
			{
				var cell = new Label { WidthRequest = 150, Padding = new Thickness(12), BackgroundColor = Color.FromArgb("#303a5a") };
				cell.BindingContextChanged += (_, _) => cell.Text = cell.BindingContext as string ?? string.Empty;
				return cell;
			}),
		};
		await nav.PushAsync(SilicaPage("Silica I", new VerticalStackLayout { Children = { grid } }));
		await SilicaWait(dispatcher, 1600);
		NativeElementHostOf(renderer, grid, out var listHost);
		var orientation = listHost is null ? "?" : QtHost.QtHostRuntime.GetProperty(listHost.NativeHandle, "mauiOrientation");
		var rows = listHost is null ? -1 : DiagQml.Num(QtHost.QtHostRuntime.GetProperty(listHost.NativeHandle, "count"), -1);
		// Scene rect of a row's cell (row = column of the horizontal grid).
		(double X, double Y, double W, double H) Cell(int row, int cell)
		{
			var view = renderer.Collection.RowView(row, cell);
			var id = view is null ? null : renderer.HostIdOf(view);
			if (id is null)
				return (double.NaN, double.NaN, 0, 0);
			var r = QtHost.QtHostRuntime.Eval($"(function(){{var h=pageStack.currentPage.__hosts['{id}'];if(!h||!h.item)return '';var i=h.item,p=i.mapToItem(null,0,0);return p.x+','+p.y+','+i.width+','+i.height;}})()").Split(',');
			return r.Length == 4 ? (DiagQml.Num(r[0]), DiagQml.Num(r[1]), DiagQml.Num(r[2]), DiagQml.Num(r[3])) : (double.NaN, double.NaN, 0, 0);
		}
		var a = Cell(0, 0);
		var b = Cell(0, 1);
		var c = Cell(1, 0);
		_qtSilicaChecks.Check($"I horizontal GridItemsLayout(2): the list scrolls along x ('{orientation}'=='horizontal') with 12/2 = {rows}==6 columns",
			orientation == "horizontal" && rows == 6);
		_qtSilicaChecks.Check($"I a column stacks its 2 cells ('cell 1' y{a.Y:F0} above 'cell 2' y{b.Y:F0}, same x {a.X:F0}/{b.X:F0}); the next column is to the right ('cell 3' x{c.X:F0} > {a.X + a.W:F0})",
			Math.Abs(a.X - b.X) < 1 && b.Y >= a.Y + a.H && c.X >= a.X + a.W);
		DiagQml.Tap(b.X + b.W / 2, b.Y + b.H / 2);
		await SilicaWait(dispatcher, 900);
		_qtSilicaChecks.Check($"I a tap on the lower cell selects it (SelectedItem '{grid.SelectedItem}'=='cell 2')", Equals(grid.SelectedItem, "cell 2"));
		await SilicaShot(dispatcher, "silica-i-horizontal-grid");
		await nav.PopAsync();
		await SilicaWait(dispatcher, 900);
	}

	private async Task SilicaMceJ(SailfishDispatcher dispatcher)
	{
		var app = IPlatformApplication.Current as SailfishMauiApplication;
		for (var i = 0; i < 30 && (app?.DisplayState is null || app.ScreenLocked is null || !app.MemoryLevelAnswered); i++)
			await SilicaWait(dispatcher, 100);
		_qtSilicaChecks.Check($"J MCE reports the display on ({app?.DisplayState}) and no lock screen ({app?.ScreenLocked}) while the app is in front",
			app?.DisplayState == SailfishDisplayState.On && app.ScreenLocked == false);
		_qtSilicaChecks.Check($"J MCE answered the memory level query ({app?.MemoryLevel}; Unknown where MCE does not track memory)",
			app is not null && app.MemoryLevelAnswered);
	}

	private async Task SilicaOpenUrlK(SailfishDispatcher dispatcher)
	{
		var package = Path.GetFileName(AppContext.BaseDirectory.TrimEnd('/'));
		var desktopPath = $"/usr/share/applications/{package}.desktop";
		var desktop = File.Exists(desktopPath) ? File.ReadAllText(desktopPath) : string.Empty;
		var service = System.Text.RegularExpressions.Regex.Match(desktop, @"X-Maemo-Service=(\S+)").Groups[1].Value;
		_qtSilicaChecks.Check($"K {desktopPath}: MimeType has x-scheme-handler/mauisample, Exec takes %U, X-Maemo-Service/Object-Path/Method name the app's D-Bus openUrl ('{service}')",
			desktop.Contains("x-scheme-handler/mauisample;") && desktop.Contains(" %U") && service.Length > 0 &&
			desktop.Contains("X-Maemo-Object-Path=/") && desktop.Contains("X-Maemo-Method=") && desktop.Contains(".openUrl"));
		var activation = $"/usr/share/dbus-1/services/{service}.service";
		_qtSilicaChecks.Check($"K the D-Bus activation file {activation} starts the app through the invoker",
			File.Exists(activation) && File.ReadAllText(activation).Contains($"/usr/bin/{package}"));

		Uri? got = null;
		void OnDelivered(Uri u) => got = u;
		SailfishOpenUrl.Delivered += OnDelivered;
		try
		{
			var rc = SailfishOpenUrl.CallSelf("mauisample://recipes/42?from=dbus");
			for (var i = 0; i < 20 && got is null; i++)
				await SilicaWait(dispatcher, 100);
			_qtSilicaChecks.Check($"K a D-Bus openUrl call ({rc}) reaches Application.OnAppLinkRequestReceived with the URI ('{got}')",
				got?.ToString() == "mauisample://recipes/42?from=dbus");
		}
		finally
		{
			SailfishOpenUrl.Delivered -= OnDelivered;
		}
		_qtSilicaChecks.Check($"K launch arguments: URLs and existing files become URIs, options and other text do not",
			SailfishOpenUrl.ToUri("mauisample://a")?.Scheme == "mauisample" && SailfishOpenUrl.ToUri(desktopPath)?.IsFile == true &&
			SailfishOpenUrl.ToUri("-prestart") is null && SailfishOpenUrl.ToUri("hello") is null);
	}

	private async Task SilicaTimersL(SailfishDispatcher dispatcher, NavigationPage nav)
	{
		var clock = System.Diagnostics.Stopwatch.StartNew();
		var late = new List<string>();
		var fired = 0;
		var expected = 0;
		void Schedule(int ms, string tag)
		{
			expected++;
			var due = clock.ElapsedMilliseconds + ms;
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(ms), () =>
			{
				fired++;
				var lag = clock.ElapsedMilliseconds - due;
				if (lag > 60)
					late.Add($"{tag}+{lag}ms");
			});
		}
		// A burst, mixed delays, scheduled in one go.
		foreach (var ms in new[] { 1, 5, 10, 20, 40, 60, 80, 120, 160, 200 })
			Schedule(ms, $"burst{ms}");
		// From inside timer callbacks (the next timer is armed while the loop is in its tick).
		for (var i = 0; i < 10; i++)
		{
			var delay = 15 + i * 12;
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(5 + i * 7), () => Schedule(delay, $"nested{delay}"));
		}
		// From queued work.
		for (var i = 0; i < 5; i++)
		{
			var delay = 30 + i * 25;
			dispatcher.Dispatch(() => Schedule(delay, $"queued{delay}"));
		}
		await SilicaWait(dispatcher, 700);
		// The reported case: a 60 ms timer scheduled right after an immediate modal close.
		await nav.Navigation.PushModalAsync(SilicaPage("Silica L modal", new Label { Text = "modal" }), false);
		await SilicaWait(dispatcher, 1200);
		await nav.Navigation.PopModalAsync(false);
		Schedule(60, "after-modal-pop");
		await SilicaWait(dispatcher, 1500);
		_qtSilicaChecks.Check($"L DispatchDelayed: {fired}/{expected} fired, none more than 60 ms late ([{string.Join(",", late)}])",
			fired == expected && late.Count == 0);
	}

	private async Task SilicaLifecycleM(SailfishDispatcher dispatcher)
	{
		static int Count(string name) => SailfishMauiApplication.NativeEventCounts.TryGetValue(name, out var n) ? n : 0;
		_qtSilicaChecks.Check($"M the keyboard raised OnInputMethodChanged ({Count("OnInputMethodChanged")}) and the rotation OnOrientationChanged ({Count("OnOrientationChanged")}×)",
			Count("OnInputMethodChanged") > 0 && Count("OnOrientationChanged") >= 2);
		var scheme0 = Count("OnColorSchemeChanged");
		var theme0 = SailfishTheme.Current;
		// The reports the shell sends on an ambience switch: to the other scheme, then back. OnColorSchemeChanged comes
		// after the theme service updated (AppInfo.RequestedTheme is already the new one there), once per change.
		QtHost.QtHostRuntime.Eval("window.mauiAppNotify('svc-theme-changed', JSON.stringify({ light: Theme.colorScheme !== Theme.DarkOnLight }))");
		await SilicaWait(dispatcher, 400);
		var switched = SailfishTheme.Current;
		QtHost.QtHostRuntime.Eval("window.mauiAppNotify('svc-theme-changed', JSON.stringify({ light: Theme.colorScheme === Theme.DarkOnLight }))");
		await SilicaWait(dispatcher, 400);
		_qtSilicaChecks.Check($"M an ambience switch and back raise OnColorSchemeChanged twice ({scheme0} → {Count("OnColorSchemeChanged")}), theme {theme0} → {switched} → {SailfishTheme.Current}",
			Count("OnColorSchemeChanged") == scheme0 + 2 && switched != theme0 && SailfishTheme.Current == theme0);
		var cover0 = Count("OnCoverStatusChanged");
		var state0 = Count("OnApplicationStateChanged");
		QtHost.QtHostRuntime.Eval("window.deactivate()");
		await SilicaWait(dispatcher, 2500);
		var coverShown = Count("OnCoverStatusChanged") - cover0;
		var stateDown = Count("OnApplicationStateChanged") - state0;
		QtHost.QtHostRuntime.Eval("window.activate()");
		for (var i = 0; i < 30 && QtHost.QtHostRuntime.Eval("String(Qt.application.state)") != "4"; i++)
			await SilicaWait(dispatcher, 100);
		await SilicaWait(dispatcher, 600);
		_qtSilicaChecks.Check($"M minimized to the home screen: OnCoverStatusChanged +{coverShown}, OnApplicationStateChanged +{stateDown}; back in front (state {QtHost.QtHostRuntime.Eval("String(Qt.application.state)")}==4)",
			coverShown > 0 && stateDown > 0 && QtHost.QtHostRuntime.Eval("String(Qt.application.state)") == "4");
	}

	private async Task SilicaOpenFileN(SailfishDispatcher dispatcher)
	{
		var missing = await Launcher.Default.OpenAsync(new OpenFileRequest("missing", new ReadOnlyFile("/nonexistent/x.png")));
		_qtSilicaChecks.Check($"N OpenAsync(OpenFileRequest) on a missing file is false ({missing})", !missing);
		var image = Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "images"), "*.png").FirstOrDefault();
		if (image is null)
		{
			_qtSilicaChecks.Check("N an image to open ships with the app", false);
			return;
		}
		var opened = await Launcher.Default.OpenAsync(new OpenFileRequest("image", new ReadOnlyFile(image)));
		var left = false;
		for (var i = 0; i < 50 && !left; i++)
		{
			await SilicaWait(dispatcher, 100);
			left = QtHost.QtHostRuntime.Eval("String(Qt.application.state)") != "4";
		}
		await SilicaShot(dispatcher, "silica-n-open-file");
		QtHost.QtHostRuntime.Eval("window.activate()");
		for (var i = 0; i < 40 && QtHost.QtHostRuntime.Eval("String(Qt.application.state)") != "4"; i++)
			await SilicaWait(dispatcher, 100);
		_qtSilicaChecks.Check($"N OpenAsync(OpenFileRequest '{Path.GetFileName(image)}') is true ({opened}) and hands the file to another app (this one left the front: {left}); back in front ({QtHost.QtHostRuntime.Eval("String(Qt.application.state)")}==4)",
			opened && left && QtHost.QtHostRuntime.Eval("String(Qt.application.state)") == "4");
	}

	private async Task SilicaTwoLevelsBackO(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, NavigationPage nav)
	{
		var list1 = SilicaList(Enumerable.Range(1, 40).Select(i => $"level one row {i}").ToList());
		var list2 = SilicaList(Enumerable.Range(1, 40).Select(i => $"level two row {i}").ToList());
		var p1 = SilicaPage("Silica O1", list1);
		var p2 = SilicaPage("Silica O2", list2);
		var p3 = SilicaPage("Silica O3", new Label { Text = "top", Margin = new Thickness(16) });
		foreach (var p in new[] { p1, p2, p3 })
		{
			await nav.PushAsync(p);
			await SilicaWait(dispatcher, 1300);
		}
		async Task Back(ContentPage revealed, CollectionView list, string step)
		{
			var materialized = renderer.Collection.ItemsMaterialized;
			var clock = System.Diagnostics.Stopwatch.StartNew();
			await nav.PopAsync();
			var popMs = clock.ElapsedMilliseconds;
			await SilicaWait(dispatcher, 500);
			var rows = NativeElementHostOf(renderer, list, out var host) && host is not null ? ColEmptyVisibleRows("maui_" + host.Id) : "?";
			var parts = rows.Split(':');
			var painted = parts.Length == 2 && int.TryParse(parts[0], out var visible) && visible > 0 && parts[1].Length == 0;
			var built = renderer.Collection.ItemsMaterialized - materialized;
			await SilicaShot(dispatcher, $"silica-o-{(revealed == p1 ? "level1" : "level2")}");
			// A rebuild (the old two-levels-down behaviour) re-materializes every visible row.
			_qtSilicaChecks.Check($"O {step}: '{revealed.Title}' comes back without rebuilding its rows ({built} materialized == 0) and " +
				$"painted (visible:empty rows {rows}; renderer page '{renderer.CurrentPage?.Title}', {renderer.CurrentHosts.Count} hosts, list attached: {renderer.CurrentHosts.Any(h => ReferenceEquals(h.Element, list))}); PopAsync took {popMs} ms",
				ReferenceEquals(renderer.CurrentPage, revealed) && built == 0 && painted);
		}
		await Back(p2, list2, "first pop");
		await Back(p1, list1, "second pop (two levels down)");
		await nav.PopAsync();
		await SilicaWait(dispatcher, 900);
	}
	/// <summary>An insert at the top renames every delegate (r0→r1, r1→r2, …); covering the page and coming back with
	/// the back swipe then sweeps QML hosts the renderer does not know. Every row must still paint its content (a
	/// cascading rename once dropped the shifted rows from the registry and the sweep destroyed their hosts).</summary>
	private async Task SilicaInsertCoverBackP(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, NavigationPage nav)
	{
		var items = new System.Collections.ObjectModel.ObservableCollection<string>(Enumerable.Range(1, 12).Select(i => $"insert row {i}"));
		var list = SilicaList(items);
		var page = SilicaPage("Silica P", list);
		await nav.PushAsync(page);
		await SilicaWait(dispatcher, 1300);
		items.Insert(0, "inserted at the top");
		await SilicaWait(dispatcher, 900);
		var covering = SilicaPage("Silica P cover", new Label { Text = "cover", Margin = new Thickness(16) });
		await nav.PushAsync(covering);
		await SilicaWait(dispatcher, 1300);
		var materialized = renderer.Collection.ItemsMaterialized;
		var swiped = new TaskCompletionSource();
		InjectBackSwipe(dispatcher, () => swiped.TrySetResult());
		await swiped.Task;
		await SilicaWait(dispatcher, 1500);
		var rows = NativeElementHostOf(renderer, list, out var host) && host is not null ? ColEmptyVisibleRows("maui_" + host.Id) : "?";
		var parts = rows.Split(':');
		var painted = parts.Length == 2 && int.TryParse(parts[0], out var visible) && visible > 0 && parts[1].Length == 0;
		var rebuilt = renderer.Collection.ItemsMaterialized - materialized;
		await SilicaShot(dispatcher, "silica-p-after-back");
		_qtSilicaChecks.Check($"P: after an insert at the top, a covering push and the back swipe every visible row paints " +
			$"(visible:empty rows {rows}) and none was rebuilt ({rebuilt} materialized == 0; page '{renderer.CurrentPage?.Title}')",
			ReferenceEquals(renderer.CurrentPage, page) && painted && rebuilt == 0);
		await nav.PopAsync();
		await SilicaWait(dispatcher, 900);
	}
}
