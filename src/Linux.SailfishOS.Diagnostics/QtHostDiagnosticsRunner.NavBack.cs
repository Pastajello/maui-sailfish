using Microsoft.Maui.Controls;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Navigation-after-back leg (MAUI_SAILFISH_QT_HOST_NAVBACK_DIAG=1): real back swipes and
/// taps on the sample, checking that forward navigation keeps working after a back and
/// that the returned-to page never shows the popped page's title/background, even for one tick.
/// </summary>
internal sealed partial class QtHostDiagnosticsRunner
{
	private bool _qtNavBackDiag;
	private readonly DiagChecks _qtNavBackChecks = new("Qt navback diag");


	private static string PageTitle(QtHost.QtHostPageRenderer renderer) =>
		renderer.CurrentPage?.Title ?? renderer.CurrentPage?.GetType().Name ?? "-";

	/// <summary>A real back swipe from the left screen edge; <paramref name="midHold"/> runs
	/// with the finger held most of the way and gets the continuation.</summary>
	private static void InjectBackSwipe(SailfishDispatcher dispatcher, Action next, Action? onRelease = null, Action<Action>? midHold = null)
	{
		// Silica ignores the gesture mid-transition (pageStack.busy), so wait for it,
		// bounded so a stuck busy still swipes.
		var waited = 0;
		void Start()
		{
			if (waited < 2000 && QtHost.QtHostRuntime.Eval("pageStack.busy") == "true")
			{
				waited += 50;
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(50), Start);
				return;
			}
			Swipe();
		}
		void Swipe() => InjectBackSwipeNow(dispatcher, next, onRelease, midHold);
		Start();
	}

	private static void InjectBackSwipeNow(SailfishDispatcher dispatcher, Action next, Action? onRelease, Action<Action>? midHold)
	{
		const double y = 1200;
		QtHost.QtHostRuntime.InjectPointer(0, 6, y);
		var x = 40.0;
		// A held swipe runs further so the mid-swipe shot shows the page underneath.
		var midHoldArmed = midHold is not null;
		void Step()
		{
			if (midHold is not null && x > 840)
			{
				var hold = midHold;
				midHold = null;
				hold(() => dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(16), Step));
				return;
			}
			if (x > (midHoldArmed ? 900 : 520))
			{
				QtHost.QtHostRuntime.InjectPointer(1, x, y);
				onRelease?.Invoke();
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(300), next);   // callers WaitFor the pop sync
				return;
			}
			QtHost.QtHostRuntime.InjectPointer(2, x, y);
			x += 40;
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(16), Step);
		}
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(16), Step);
	}

	/// <summary>Sorted registered host ids of a page; equal sets across a round trip mean nothing was rebuilt.</summary>
	private static string PageHostIds(string pageId) => QtHost.QtHostRuntime.Eval(
		$"(function(){{var p=window.mauiPageById('{pageId}');return p?Object.keys(p.__hosts).sort().join(','):'-';}})()");

	private static int IdCount(string ids) => ids is "" or "-" ? 0 : ids.Count(c => c == ',') + 1;

	/// <summary>Runs <paramref name="next"/> once <paramref name="done"/> holds or after
	/// <paramref name="timeoutMs"/>; filmstrip grabs slow ticks, so a fixed delay is unreliable.</summary>
	private static void WaitFor(SailfishDispatcher dispatcher, Func<bool> done, int timeoutMs, Action next)
	{
		var sw = System.Diagnostics.Stopwatch.StartNew();
		void Poll()
		{
			if (done() || sw.ElapsedMilliseconds > timeoutMs)
			{
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(600), next);
				return;
			}
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(50), Poll);
		}
		Poll();
	}

	/// <summary>Arms the page's chrome history and returns its title/background history lengths.</summary>
	private static (int T, int B) ChromeMark(string pageId)
	{
		var r = QtHost.QtHostRuntime.Eval(
			$"(function(){{var p=window.mauiPageById('{pageId}');if(!p)return '-1,-1';p.mauiRecordChrome=true;" +
			"return p.mauiTitleHistory.length+','+p.mauiBackgroundHistory.length;})()").Split(',');
		return r.Length == 2 && int.TryParse(r[0], out var t) && int.TryParse(r[1], out var b) ? (t, b) : (-1, -1);
	}

	/// <summary>No-flash check: every title/background since <paramref name="mark"/> must be the final one.</summary>
	private void CheckNoChromeFlash(string what, string pageId, (int T, int B) mark)
	{
		var json = QtHost.QtHostRuntime.Eval(
			$"(function(){{var p=window.mauiPageById('{pageId}');if(!p)return '{{}}';" +
			$"return JSON.stringify({{t:p.pageTitle,b:String(p.mauiBackground),ts:p.mauiTitleHistory.slice({mark.T}),bs:p.mauiBackgroundHistory.slice({mark.B})}});}})()");
		string nowT = "?", nowB = "?";
		var titles = new List<string>();
		var backgrounds = new List<string>();
		try
		{
			using var doc = System.Text.Json.JsonDocument.Parse(json);
			nowT = doc.RootElement.GetProperty("t").GetString() ?? "?";
			nowB = doc.RootElement.GetProperty("b").GetString() ?? "?";
			foreach (var e in doc.RootElement.GetProperty("ts").EnumerateArray())
				titles.Add(e.GetString() ?? "");
			foreach (var e in doc.RootElement.GetProperty("bs").EnumerateArray())
				backgrounds.Add(e.GetString() ?? "");
		}
		catch (Exception ex)
		{
			titles.Add("unreadable: " + ex.Message);
		}
		// Checked separately: one op batch sets them in sequence and that step never reaches a frame.
		var flashes = titles.Where(t => t != nowT).Select(t => $"title '{t}'")
			.Concat(backgrounds.Where(b => b != nowB).Select(b => $"background '{b}'")).Distinct().ToList();
		_qtNavBackChecks.Check($"{what}: '{pageId}' never showed foreign chrome (now '{nowT}' {nowB}, {titles.Count + backgrounds.Count} change(s) since the pop" +
			(flashes.Count > 0 ? $", FLASHED: {string.Join(" ; ", flashes)})" : ")"),
			flashes.Count == 0);
	}

	/// <summary>Opt-in filmstrip (MAUI_SAILFISH_NAVBACK_FILM=1): an in-app scene grab after every
	/// tick for <paramref name="ms"/>. Off by default because a synchronous grabWindow() mid-transition
	/// polishes items re-entrantly and can crash QV4.</summary>
	private static readonly bool FilmEnabled = SailfishEnv.Flag("MAUI_SAILFISH_NAVBACK_FILM");

	private static void Film(SailfishDispatcher dispatcher, string prefix, int ms)
	{
		if (!FilmEnabled)
			return;
		var sw = System.Diagnostics.Stopwatch.StartNew();
		var frame = 0;
		void Grab()
		{
			if (sw.ElapsedMilliseconds > ms || frame >= 60)
			{
				Console.Error.WriteLine($"[Sailfish] Qt navback diag: film {prefix}: {frame} frame(s) in {sw.ElapsedMilliseconds} ms");
				return;
			}
			QtHost.QtHostRuntime.GrabPng($"/tmp/sf-film/{prefix}-{frame++:D2}-{sw.ElapsedMilliseconds:D4}ms.png");
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1), Grab);
		}
		Grab();
	}

	/// <summary>A real tap on the button with this text (false if none is attached); press and
	/// release are 150 ms apart because QPA injection is queued.</summary>
	private bool TapButton(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, string text)
	{
		var host = renderer.CurrentHosts.FirstOrDefault(h => h.IsAttached && h.Element is Button { Text: var t } && t == text);
		if (host is null || !QtHost.QtHostRuntime.TryItemGeometry(host.NativeHandle, out var g))
			return false;
		// Guards against the stray sweep (__destroyHostsNotIn) unregistering the returned-to
		// page's hosts after a native pop, which leaves buttons painted but dead.
		var registered = QtHost.QtHostRuntime.Eval(
			"(function(){var pg=pageStack.currentPage;return pg&&pg.__hosts&&pg.__hosts['" + host.Id + "']!==undefined?'yes':'no';})()");
		_qtNavBackChecks.Check($"'{text}' host {host.Id} still registered on the page (its tap reaches the event queue): {registered}", registered == "yes");
		var cx = g.X + g.Width / 2;
		var cy = g.Y + g.Height / 2;
		Console.Error.WriteLine($"[Sailfish] Qt navback diag: tap '{text}' at {cx:F0},{cy:F0}");
		QtHost.QtHostRuntime.InjectPointer(0, cx, cy);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(150), () => QtHost.QtHostRuntime.InjectPointer(1, cx, cy));
		return true;
	}

	/// <summary>Every button on the page first showed at its final font size (no Silica-default flash).</summary>
	private void CheckButtonsShowFinalFont(string where)
	{
		var json = QtHost.QtHostRuntime.Eval("""
			(function(){ var p=pageStack.currentPage, out=[];
			  for(var k in p.__hosts){ var h=p.__hosts[k]; if(h.uri!=='button') continue; var b=h.item;
			    out.push({t:b.text, first:b.mauiFirstVisiblePx, now:b.__label?b.__label.font.pixelSize:-1}); }
			  return JSON.stringify(out); })()
			""");
		var bad = new List<string>();
		var n = 0;
		try
		{
			using var doc = System.Text.Json.JsonDocument.Parse(json);
			foreach (var b in doc.RootElement.EnumerateArray())
			{
				n++;
				var first = b.GetProperty("first").GetDouble();
				var now = b.GetProperty("now").GetDouble();
				if (Math.Abs(first - now) > 0.5)
					bad.Add($"'{b.GetProperty("t").GetString()}' first {first:F0}px → {now:F0}px");
			}
		}
		catch (Exception ex)
		{
			bad.Add("unreadable: " + ex.Message);
		}
		_qtNavBackChecks.Check($"{where}: {n} button(s) first painted at their final font size" + (bad.Count > 0 ? $" — CHANGED: {string.Join("; ", bad)}" : ""),
			n > 0 && bad.Count == 0);
	}

	private void RunQtNavBackDiagnostics(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var nav = RootNav;
		var rootTitle = nav?.RootPage?.Title ?? "Sailfish Tasks";
		var rootPageId = renderer.NativePageIds.Count > 0 ? renderer.NativePageIds[0] : "mp1";
		System.IO.Directory.CreateDirectory("/tmp/sf-film");
		foreach (var old in System.IO.Directory.GetFiles("/tmp/sf-film"))
			System.IO.File.Delete(old);
		_qtNavBackChecks.Check($"start: startup tap opened '{PageTitle(renderer)}'=='Statistics', native depth {renderer.NativePageIds.Count}==2",
			PageTitle(renderer) == "Statistics" && renderer.NativePageIds.Count == 2);
		// Page-load guard (push → Appearing, the page's hosts and layout done). Budgets are about twice what the Jolla
		// phone takes (2026-10-02: first Statistics 162–183 ms, Controls 44–61 ms, Statistics again 47–54 ms).
		void LoadBudget(string what, double budgetMs) =>
			_qtNavBackChecks.Check($"page load: {what} push→Appearing {renderer.LastNavToAppearingMs:F0} ms ≤ {budgetMs:F0} ms",
				renderer.LastNavToAppearingMs > 0 && renderer.LastNavToAppearingMs <= budgetMs);
		LoadBudget("first Statistics (adapters cold)", 300);
		bool BackOnRoot() => PageTitle(renderer) == rootTitle && renderer.NativePageIds.Count == 1;
		void Finish()
		{
			// Pop-sync continuations on the thread pool calling the shim crash QV4.
			_qtNavBackChecks.CheckNoOffThreadCalls();
			_qtNavBackChecks.Accept("OK — back returns cleanly (no flash) and forward navigation keeps working");
			QtHost.QtHostRuntime.Shutdown();
		}
		// A MAUI-driven pop (app code / hardware Back) must not flash either.
		void MauiPopRound()
		{
			var mark = ChromeMark(rootPageId);
			Console.Error.WriteLine("[Sailfish] Qt navback diag: MAUI PopAsync");
			Film(dispatcher, "r3-maui-pop", 3000);
			_ = nav?.PopAsync();
			WaitFor(dispatcher, BackOnRoot, 6000, () =>
			{
				_qtNavBackChecks.Check($"MAUI pop: '{PageTitle(renderer)}'=='{rootTitle}', native depth {renderer.NativePageIds.Count}==1",
					PageTitle(renderer) == rootTitle && renderer.NativePageIds.Count == 1);
				CheckNoChromeFlash("MAUI pop", rootPageId, mark);
				Shot(dispatcher, "navback-5-main-after-maui-pop", Finish);
			});
		}
		Shot(dispatcher, "navback-1-stats", () =>
		{
			var mark1 = ChromeMark(rootPageId);
			Console.Error.WriteLine("[Sailfish] Qt navback diag: back swipe from the left edge");
			InjectBackSwipe(dispatcher, () => WaitFor(dispatcher, BackOnRoot, 6000, () =>
			{
				_qtNavBackChecks.Check($"back: '{PageTitle(renderer)}'=='{rootTitle}', native depth {renderer.NativePageIds.Count}==1",
					PageTitle(renderer) == rootTitle && renderer.NativePageIds.Count == 1);
				CheckNoChromeFlash("back swipe from Statistics", rootPageId, mark1);
				CheckButtonsShowFinalFont("main page");
				Shot(dispatcher, "navback-2-main-after-back", () =>
				{
					var idsOnA = PageHostIds(rootPageId);
					var tapped = TapButton(renderer, dispatcher, "Controls");
					dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(2000), () =>
					{
						_qtNavBackChecks.Check($"forward after back: tapped={tapped}, '{PageTitle(renderer)}' != '{rootTitle}' (Controls pushed), native depth {renderer.NativePageIds.Count}==2",
							tapped && PageTitle(renderer) != rootTitle && renderer.NativePageIds.Count == 2);
						CheckButtonsShowFinalFont("pushed Controls page");
						LoadBudget("Controls", 150);
						Shot(dispatcher, "navback-3-controls", () =>
						{
							var mark2 = ChromeMark(rootPageId);
							InjectBackSwipe(dispatcher, () => WaitFor(dispatcher, BackOnRoot, 6000, () =>
							{
								CheckNoChromeFlash("back swipe from Controls", rootPageId, mark2);
								var idsBack = PageHostIds(rootPageId);
								_qtNavBackChecks.Check($"back from Controls: '{rootPageId}' kept every host incl. list rows — nothing rebuilt ({IdCount(idsBack)} ids == {IdCount(idsOnA)} before the push, same set)",
									idsBack == idsOnA && IdCount(idsOnA) > 0);
								var tapped2 = TapButton(renderer, dispatcher, "Statistics");
								dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(2000), () =>
								{
									_qtNavBackChecks.Check($"second round: tapped={tapped2}, '{PageTitle(renderer)}'=='Statistics', native depth {renderer.NativePageIds.Count}==2",
										tapped2 && PageTitle(renderer) == "Statistics" && renderer.NativePageIds.Count == 2);
									LoadBudget("Statistics again (warm)", 120);
									Shot(dispatcher, "navback-4-stats-again", MauiPopRound);
								});
							}), () => Film(dispatcher, "r2-swipe-from-controls", 3000),
							k =>
							{
								// Mid-swipe the page underneath keeps its list rows (the back cache keeps them warm).
								var idsUnder = PageHostIds(rootPageId);
								_qtNavBackChecks.Check($"mid back swipe: '{rootPageId}' under Controls still has all {IdCount(idsOnA)} hosts incl. list rows (has {IdCount(idsUnder)}, same set)",
									idsUnder == idsOnA);
								Shot(dispatcher, "navback-3b-mid-swipe-from-controls", k);
							});
						});
					});
				});
			}), () => Film(dispatcher, "r1-swipe-from-stats", 3000),
			k => Shot(dispatcher, "navback-1b-mid-swipe-from-stats", k));
		});
	}
}
