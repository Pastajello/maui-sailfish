using System.Collections.ObjectModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;

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
}
