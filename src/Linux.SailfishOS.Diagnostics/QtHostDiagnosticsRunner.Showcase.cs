using Microsoft.Maui.Controls;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Showcase tour (MAUI_SAILFISH_QT_HOST_SHOWCASE=1) for screen recordings: drives the sample
/// with real taps, flicks and back swipes. Not a test; it logs "SHOWCASE step" lines and leaves the app open.
/// </summary>
internal sealed partial class QtHostDiagnosticsRunner
{
	private bool _qtShowcase;

	private void RunQtShowcase(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var steps = new List<(int WaitMs, string Name, Action<Action> Run)>();
		void Step(int waitMs, string name, Action<Action> run) => steps.Add((waitMs, name, run));
		void Do(int waitMs, string name, Action run) => steps.Add((waitMs, name, next => { run(); next(); }));

		// --- the tour ---------------------------------------------------------
		// MAUI_SAILFISH_QT_HOST_SHOWCASE_TOUR=short|native|perf; perf visits each feature page twice (cold vs warm).
		var tour = SailfishEnv.Get("MAUI_SAILFISH_QT_HOST_SHOWCASE_TOUR");
		if (string.Equals(tour, "short", StringComparison.Ordinal))
		{
			// ~28 s tour for the README GIF.
			Do(1500, "home", () => { });
			Do(1200, "add a task", () => TapButton(renderer, "+ Add task"));
			Do(1700, "open Controls", () => TapButton(renderer, "Controls"));
			Do(900, "toggle a switch", () => TapFirst<Switch>(renderer));
			Do(900, "tick a checkbox", () => TapFirst<CheckBox>(renderer));
			Step(1100, "drag a slider", next => DragAcross<Slider>(renderer, dispatcher, next));
			Step(1100, "scroll Controls", next => Flick(dispatcher, 516, 1800, 516, 800, next));
			Step(1300, "back (swipe)", next => InjectBackSwipe(dispatcher, next));
			Do(1500, "open Features", () => TapButton(renderer, "Features"));
			Do(1800, "Visual", () => TapListRow(renderer, 6));
			Step(1100, "scroll Visual", next => Flick(dispatcher, 516, 1800, 516, 900, next));
			Step(1200, "back (swipe)", next => InjectBackSwipe(dispatcher, next));
			Do(1900, "Shapes & images", () => TapListRow(renderer, 9));
			Step(1200, "back (swipe)", next => InjectBackSwipe(dispatcher, next));
			Do(1600, "Pickers", () => TapListRow(renderer, 2));
			Step(1200, "back (swipe)", next => InjectBackSwipe(dispatcher, next));
			Do(1600, "Collections", () => TapListRow(renderer, 7));
			Step(1100, "scroll Collections", next => Flick(dispatcher, 516, 1800, 516, 700, next));
			Step(1200, "back (swipe)", next => InjectBackSwipe(dispatcher, next));
			Step(1500, "back home (swipe)", next => InjectBackSwipe(dispatcher, next));
		}
		else if (string.Equals(tour, "native", StringComparison.Ordinal))
		{
			// ~50 s: MAUI controls and navigation in the sample, then the Silica idioms MAUI code gets (pulley from
			// ToolbarItems, busy pulley from IsBusy, ViewPlaceholder from EmptyView) and the Sailfish APIs (remorse,
			// bottom sheet, notification) on a task list, driven by real pulls. Recorded 2× for the README.
			Do(1500, "home", () => { });
			Do(1200, "add a task", () => TapButton(renderer, "+ Add task"));
			Do(1700, "open Controls", () => TapButton(renderer, "Controls"));
			Do(900, "toggle a switch", () => TapFirst<Switch>(renderer));
			Do(900, "tick a checkbox", () => TapFirst<CheckBox>(renderer));
			Step(1100, "drag a slider", next => DragAcross<Slider>(renderer, dispatcher, next));
			Step(1100, "scroll Controls", next => Flick(dispatcher, 516, 1800, 516, 800, next));
			Step(1300, "back (swipe)", next => InjectBackSwipe(dispatcher, next));
			Do(1500, "open Features", () => TapButton(renderer, "Features"));
			Do(1800, "Visual", () => TapListRow(renderer, 6));
			Step(1100, "scroll Visual", next => Flick(dispatcher, 516, 1800, 516, 900, next));
			Step(1200, "back (swipe)", next => InjectBackSwipe(dispatcher, next));
			Do(1900, "Shapes & images", () => TapListRow(renderer, 9));
			Step(1200, "back (swipe)", next => InjectBackSwipe(dispatcher, next));
			Step(1500, "back home (swipe)", next => InjectBackSwipe(dispatcher, next));

			var items = new System.Collections.ObjectModel.ObservableCollection<string>(
				new[] { "Buy milk", "Call the bank", "Book train tickets", "Water the plants", "Pay the rent", "Renew passport" });
			ContentPage? tasks = null;
			Do(2000, "open the task list", () =>
			{
				var list = SilicaList(items);
				list.EmptyView = "No tasks yet";
				tasks = SilicaPage("Tasks", list);
				tasks.ToolbarItems.Add(new ToolbarItem("Clear all", null, () =>
				{
					Console.Error.WriteLine("[Sailfish] SHOWCASE pulley chose 'Clear all'");
					_ = SailfishRemorse.ExecuteAsync("Clearing all tasks", items.Clear, 3000);
				}));
				tasks.ToolbarItems.Add(new ToolbarItem("Sync now", null, () =>
				{
					Console.Error.WriteLine("[Sailfish] SHOWCASE pulley chose 'Sync now'");
					tasks.IsBusy = true;
					dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(2200), () =>
					{
						tasks.IsBusy = false;
						items.Insert(0, "Synced: 2 new tasks");
					});
				}));
				_ = RootNav?.PushAsync(tasks);
			});
			Step(3200, "pull: Sync now (busy pulley)", next => PullAndRelease(dispatcher, ShowcasePullNear, next));
			Step(3700, "remorse over a row", next =>
			{
				if (renderer.Collection.RowView(2) is { } row)
					_ = SailfishRemorse.ExecuteAsync(row, "Deleting", () => items.RemoveAt(2), 3000);
				next();
			});
			Do(2600, "bottom sheet", () =>
			{
				var sheet = new SailfishBottomSheet { Text = "3 tasks due today" };
				sheet.Show();
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1900), () => { sheet.Hide(); dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1000), sheet.Dispose); });
			});
			Do(3600, "notification", () =>
			{
				var id = SailfishNotifications.Show("Reminder", "Pay the rent today");
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(6000), () => SailfishNotifications.Close(id));
			});
			Step(5200, "pull: Clear all (remorse → placeholder)", next => PullAndRelease(dispatcher, ShowcasePullFar, next));
			Step(1600, "back home (swipe)", next => InjectBackSwipe(dispatcher, next));
		}
		else if (string.Equals(tour, "perf", StringComparison.Ordinal))
		{
			Do(2000, "home", () => { });
			Do(2500, "open Features", () => TapButton(renderer, "Features"));
			// MAUI_SAILFISH_QT_HOST_SHOWCASE_PROBE=1 dumps every shape host before leaving the page.
			var probe = SailfishEnv.Flag("MAUI_SAILFISH_QT_HOST_SHOWCASE_PROBE");
			foreach (var (row, title) in new[] { (0, "Text"), (6, "Visual"), (9, "Shapes & images"), (2, "Pickers"), (7, "Collections"), (3, "Controls") })
				for (var visit = 1; visit <= 2; visit++)
				{
					Do(probe ? 2500 : 3000, $"{title} #{visit}", () => TapListRow(renderer, row));
					if (probe)
						Do(500, "probe", () => ProbeShapes(renderer));
					Step(2000, "back (swipe)", next => InjectBackSwipe(dispatcher, next));
				}
			Do(2500, "open Controls (home)", () => { InjectBackSwipe(dispatcher, () => { }); });
			Do(2500, "Controls #1", () => TapButton(renderer, "Controls"));
			Step(2000, "back (swipe)", next => InjectBackSwipe(dispatcher, next));
			Do(2500, "Controls #2", () => TapButton(renderer, "Controls"));
			Step(2000, "back (swipe)", next => InjectBackSwipe(dispatcher, next));
		}
		else
		{
			Do(2500, "home", () => { });
			Do(2200, "add a task", () => TapButton(renderer, "+ Add task"));
			Do(2500, "open a task", () => TapListRow(renderer, 1));
			Step(1500, "back (swipe)", next => InjectBackSwipe(dispatcher, next));
			Do(2000, "open Controls", () => TapButton(renderer, "Controls"));
			Do(1200, "toggle a switch", () => TapFirst<Switch>(renderer));
			Do(1200, "tick a checkbox", () => TapFirst<CheckBox>(renderer));
			Step(1500, "drag a slider", next => DragAcross<Slider>(renderer, dispatcher, next));
			Step(1500, "scroll Controls", next => Flick(dispatcher, 516, 1800, 516, 800, next));
			Step(1800, "back (swipe)", next => InjectBackSwipe(dispatcher, next));
			Do(2500, "open Statistics", () => TapButton(renderer, "Statistics"));
			Step(1500, "back (swipe)", next => InjectBackSwipe(dispatcher, next));
			Do(2000, "open Features", () => TapButton(renderer, "Features"));
			Do(2200, "Text", () => TapListRow(renderer, 0));
			Step(1500, "scroll Text", next => Flick(dispatcher, 516, 1800, 516, 700, next));
			Step(1500, "back (swipe)", next => InjectBackSwipe(dispatcher, next));
			Do(2500, "Visual", () => TapListRow(renderer, 6));
			Step(1500, "scroll Visual", next => Flick(dispatcher, 516, 1800, 516, 900, next));
			Step(1500, "back (swipe)", next => InjectBackSwipe(dispatcher, next));
			Do(2800, "Shapes & images", () => TapListRow(renderer, 9));
			Step(1500, "back (swipe)", next => InjectBackSwipe(dispatcher, next));
			Do(2500, "Pickers", () => TapListRow(renderer, 2));
			Step(1500, "back (swipe)", next => InjectBackSwipe(dispatcher, next));
			Do(2200, "Collections", () => TapListRow(renderer, 7));
			Step(1200, "scroll Collections", next => Flick(dispatcher, 516, 1800, 516, 700, next));
			Step(1500, "back (swipe)", next => InjectBackSwipe(dispatcher, next));
			Step(2000, "back home (swipe)", next => InjectBackSwipe(dispatcher, next));
		}

		// MAUI_SAILFISH_QT_HOST_SHOWCASE_RECORD=<dir> records the app's own frames for the tour;
		// MAUI_SAILFISH_QT_HOST_SHOWCASE_SYNC=1 instead syncs with the screen recorder via /tmp/sfrec.*.
		var recordDir = SailfishEnv.Get("MAUI_SAILFISH_QT_HOST_SHOWCASE_RECORD");
		var sync = SailfishEnv.Flag("MAUI_SAILFISH_QT_HOST_SHOWCASE_SYNC");
		if (!string.IsNullOrEmpty(recordDir))
		{
			steps.Insert(0, (0, "start recording", next =>
			{
				var rc = QtHost.QtHostRuntime.RecordStart(recordDir);
				Console.Error.WriteLine($"[Sailfish] SHOWCASE recording to {recordDir} (rc {rc})");
				next();
			}));
			Do(0, "stop recording", () =>
			{
				var frames = QtHost.QtHostRuntime.RecordStop();
				Console.Error.WriteLine($"[Sailfish] SHOWCASE recorded {frames} frames");
			});
		}
		else if (sync)
		{
			var waited = 0;
			steps.Insert(0, (0, "wait for the recorder", next =>
			{
				void Poll()
				{
					if (File.Exists("/tmp/sfrec.recording") || waited > 30000)
					{
						next();
						return;
					}
					waited += 250;
					dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(250), Poll);
				}
				Poll();
			}));
			Do(0, "stop the recorder", () => File.WriteAllText("/tmp/sfrec.stop", "tour done\n"));
		}

		// Step times in the recorder's clock so frames can be lined up with taps.
		var clock = new System.Diagnostics.Stopwatch();
		if (!string.IsNullOrEmpty(recordDir))
			steps.Insert(1, (0, "clock", next => { clock.Restart(); next(); }));
		else
			clock.Start();
		var index = 0;
		void Next()
		{
			if (index >= steps.Count)
			{
				// The app stays open (closing looks like a crash); recorders wait for this line.
				Console.Error.WriteLine("[Sailfish] SHOWCASE done");
				return;
			}
			var (wait, name, run) = steps[index++];
			// CLOCK_MONOTONIC ms, the compositor recorder's frame clock.
			var mono = System.Diagnostics.Stopwatch.GetTimestamp() * 1000 / System.Diagnostics.Stopwatch.Frequency;
			Console.Error.WriteLine($"[Sailfish] SHOWCASE step {index}/{steps.Count} t={clock.ElapsedMilliseconds} mono={mono}: {name} (page '{PageTitle(renderer)}')");
			run(() => dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(wait), Next));
		}
		Next();
	}

	private static void ProbeShapes(QtHost.QtHostPageRenderer renderer)
	{
		foreach (var host in renderer.CurrentHosts.Where(h => h.IsAttached && h.QmlUri == QtHost.QtHostShapes.AdapterUri))
		{
			var js = $"(function(){{var it=pageStack.currentPage.__hosts['{host.Id}'];if(!it)return 'no host';it=it.item;var g=it.mapToItem(null,0,0);" +
			         "return JSON.stringify({kind:it.mauiKind,fast:it.mauiFast,rect:it.mauiRect,fill:it.mauiFillSpec,paints:it.mauiPaints," +
			         "vis:it.visible,op:it.opacity,x:Math.round(g.x),y:Math.round(g.y),w:it.width,h:it.height,scale:it.scale,z:it.z,avail:it.available})})()";
			Console.Error.WriteLine($"[Sailfish] SHOWCASE probe {host.Id} {host.Element?.GetType().Name}: {QtHost.QtHostRuntime.Eval(js)}");
		}
	}

	private static void TapButton(QtHost.QtHostPageRenderer renderer, string text)
	{
		var host = renderer.CurrentHosts.FirstOrDefault(h => h.IsAttached && h.Element is Button b &&
			(b.Text ?? string.Empty).Replace("…", "...").StartsWith(text, StringComparison.Ordinal));
		if (host is null)
			Console.Error.WriteLine($"[Sailfish] SHOWCASE: no button '{text}' on '{PageTitle(renderer)}'");
		else
			TapHost(host);
	}

	private static void TapFirst<T>(QtHost.QtHostPageRenderer renderer) where T : View
	{
		var host = renderer.CurrentHosts.FirstOrDefault(h => h.IsAttached && h.Element is T);
		if (host is not null)
			TapHost(host);
	}

	private static void TapHost(QtHost.NativeElementHost host)
	{
		if (!QtHost.QtHostRuntime.TryItemGeometry(host.NativeHandle, out var g))
			return;
		var x = g.X + g.Width / 2;
		var y = g.Y + g.Height / 2;
		DiagQml.Tap(x, y);
	}

	private static void TapListRow(QtHost.QtHostPageRenderer renderer, int row)
	{
		if (!renderer.Collection.TryGetRowPoint(row, out var x, out var y))
		{
			Console.Error.WriteLine($"[Sailfish] SHOWCASE: no row {row} on '{PageTitle(renderer)}'");
			return;
		}
		DiagQml.Tap(x, y);
	}

	/// <summary>A finger drag from one point to another (~350 ms, 16 ms steps).</summary>
	private static void Flick(SailfishDispatcher dispatcher, double x0, double y0, double x1, double y1, Action next)
	{
		const int steps = 22;
		var i = 0;
		QtHost.QtHostRuntime.InjectPointer(0, x0, y0);
		void Move()
		{
			i++;
			var x = x0 + (x1 - x0) * i / steps;
			var y = y0 + (y1 - y0) * i / steps;
			if (i >= steps)
			{
				QtHost.QtHostRuntime.InjectPointer(1, x, y);
				next();
				return;
			}
			QtHost.QtHostRuntime.InjectPointer(2, x, y);
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(16), Move);
		}
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(16), Move);
	}

	// Pull distances (px, scene) for the "native" tour's two-item pulley: the item next to the content, and the far one.
	private static readonly double ShowcasePullNear = SailfishEnv.Int("MAUI_SAILFISH_QT_HOST_SHOWCASE_PULL_NEAR") ?? 330;
	private static readonly double ShowcasePullFar = SailfishEnv.Int("MAUI_SAILFISH_QT_HOST_SHOWCASE_PULL_FAR") ?? 420;

	/// <summary>A slow pull down from mid-screen, held on the highlighted pulley item, then released onto it (how a
	/// Silica menu item is chosen).</summary>
	private static void PullAndRelease(SailfishDispatcher dispatcher, double distance, Action next)
	{
		const double x = 516, y0 = 700;
		const int steps = 40;
		var i = 0;
		QtHost.QtHostRuntime.InjectPointer(0, x, y0);
		void Move()
		{
			i++;
			var y = y0 + distance * Math.Min(i, steps) / steps;
			QtHost.QtHostRuntime.InjectPointer(2, x, y);
			if (i < steps)
			{
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(20), Move);
				return;
			}
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(600), () =>
			{
				Console.Error.WriteLine("[Sailfish] SHOWCASE pull held: " + QtHost.QtHostRuntime.Eval(
					"(function(){var h=pageStack.currentPage.__hosts;var r=[];for(var k in h){if(h[k].uri!=='pull-down-menu')continue;" +
					"var m=h[k].item;var f=m.flickable;r.push({id:k,active:m.active,busy:m.busy,items:m.__items.length," +
					"f:f?(f.model!==undefined?'list':'flick'):null,cy:f?Math.round(f.contentY):null,oy:f?Math.round(f.originY):null," +
					"inter:f?f.interactive:null,drag:f?f.dragging:null,clone:m.__clone?{active:m.__clone.active,cy:Math.round(m.__clone.flickable.contentY)}:null});}" +
					"return JSON.stringify(r);})()"));
				QtHost.QtHostRuntime.InjectPointer(1, x, y);
				next();
			});
		}
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(20), Move);
	}

	private static void DragAcross<T>(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, Action next) where T : View
	{
		var host = renderer.CurrentHosts.FirstOrDefault(h => h.IsAttached && h.Element is T);
		if (host is null || !QtHost.QtHostRuntime.TryItemGeometry(host.NativeHandle, out var g))
		{
			next();
			return;
		}
		var y = g.Y + g.Height / 2;
		Flick(dispatcher, g.X + g.Width * 0.2, y, g.X + g.Width * 0.85, y, next);
	}
}
