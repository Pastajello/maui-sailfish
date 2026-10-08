using Microsoft.Maui.Controls;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Navigation and dialog leg (MAUI_SAILFISH_QT_HOST_NAVDIALOG_DIAG=1, tracker S37–S38): two chained alerts both show,
/// one after the other; an alert from a background thread shows; PushAsync(page, false) and the stack edits
/// (InsertPageBefore, RemovePage) go without a slide while an animated push slides; a tap on a FormattedText span fires
/// its recognizer (S42). An SF-SHOT per dialog.
/// </summary>
internal sealed partial class QtHostDiagnosticsRunner
{
	private bool _qtNavDialogDiag;
	private readonly DiagChecks _qtNavDialogChecks = new("Qt navdialog diag");

	private void RunQtNavDialogDiagnostics(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		if (renderer.CurrentPage is not { } page)
		{
			_qtNavDialogChecks.Check("navdialog: a current page", false);
			FinishNavDialog();
			return;
		}
		// Two alerts asked for back to back: the second used to complete at once with false.
		var first = page.DisplayAlertAsync("ND first", "the first of two", "Yes", "No");
		var second = page.DisplayAlertAsync("ND second", "the second of two", "Yes", "No");
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1500), () =>
		{
			var title1 = OpenDialogEval("d.mauiTitle");
			_qtNavDialogChecks.Check($"chained alerts S38: the first shows ('{title1}'=='ND first') and the second waits (completed {second.IsCompleted})",
				title1 == "ND first" && !second.IsCompleted);
			Shot(dispatcher, "navdialog-1-first-alert", () =>
			{
				InjectDialogAcceptTap("Qt navdialog diag");
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1800), () =>
				{
					var title2 = OpenDialogEval("d.mauiTitle");
					_qtNavDialogChecks.Check($"chained alerts S38: the first answered ({(first.IsCompleted ? first.Result.ToString() : "pending")}==True), then the second shows ('{title2}'=='ND second')",
						first.IsCompleted && first.Result && title2 == "ND second");
					Shot(dispatcher, "navdialog-2-second-alert", () =>
					{
						InjectDialogAcceptTap("Qt navdialog diag");
						dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1800), () =>
						{
							_qtNavDialogChecks.Check($"chained alerts S38: the second answered too ({(second.IsCompleted ? second.Result.ToString() : "pending")}==True)",
								second.IsCompleted && second.Result);
							BackgroundAlert(renderer, dispatcher, page);
						});
					});
				});
			});
		});
	}

	private void BackgroundAlert(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, Page page)
	{
		var fromPool = Task.Run(() => page.DisplayAlertAsync("ND background", "asked from a pool thread", "OK"));
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1800), () =>
		{
			var title = OpenDialogEval("d.mauiTitle");
			_qtNavDialogChecks.Check($"background alert S38: an alert asked from a pool thread shows ('{title}'=='ND background')", title == "ND background");
			Shot(dispatcher, "navdialog-3-background-alert", () =>
			{
				InjectDialogAcceptTap("Qt navdialog diag");
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1800), () =>
				{
					_qtNavDialogChecks.Check($"background alert S38: its answer comes back (completed {fromPool.IsCompleted})", fromPool.IsCompleted);
					NoSlideNavigation(renderer, dispatcher, page);
				});
			});
		});
	}

	private static void LogStacks(string when, Page page, INavigation navigation) =>
		Console.Error.WriteLine($"[Sailfish] Qt navdialog diag: stacks {when}: proxy {navigation.NavigationStack.Count}, " +
			$"page parent {page.Parent?.GetType().Name ?? "null"}, window nav {(page.Window?.Page as NavigationPage)?.Navigation.NavigationStack.Count.ToString() ?? "-"}");

	private void NoSlideNavigation(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, Page page)
	{
		// The NavigationPage's own navigation: the starting page may be a pushed one, and once PopToRoot removes it its
		// own Navigation proxy is empty (pushes through it go nowhere).
		var navigation = (page.Parent as NavigationPage)?.Navigation ?? page.Navigation;
		LogStacks("start", page, navigation);
		var still = new ContentPage { Title = "ND no slide", Content = new Label { Text = "pushed without an animation" } };
		_ = navigation.PushAsync(still, false);
		// An animated push keeps pageStack.busy for its ~300 ms slide; an immediate one is done at once.
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(120), () =>
		{
			var busy = QtHost.QtHostRuntime.Eval("pageStack.busy");
			var step = renderer.LastNativeNavStep;
			_qtNavDialogChecks.Check($"unanimated push S37: PushAsync(page, false) goes at once ({step}=='PUSH Immediate', pageStack.busy {busy}==false, top '{renderer.CurrentPage?.Title}')",
				step == "PUSH Immediate" && busy == "false" && ReferenceEquals(renderer.CurrentPage, still));
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(800), () =>
			{
				var slides = new ContentPage { Title = "ND slides", Content = new Label { Text = "pushed with the slide" } };
				_ = navigation.PushAsync(slides);
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(120), () =>
				{
					var animatedBusy = QtHost.QtHostRuntime.Eval("pageStack.busy");
					var animatedStep = renderer.LastNativeNavStep;
					_qtNavDialogChecks.Check($"unanimated push S37 (control): an animated push slides ({animatedStep}=='PUSH Animated', busy {animatedBusy}==true)",
						animatedStep == "PUSH Animated" && animatedBusy == "true");
					dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200), () =>
					{
						LogStacks("before insert", page, navigation);
						navigation.InsertPageBefore(new ContentPage { Title = "ND inserted", Content = new Label { Text = "inserted" } }, slides);
						dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(120), () =>
						{
							var insertStep = renderer.LastNativeNavStep;
							var insertBusy = QtHost.QtHostRuntime.Eval("pageStack.busy");
							_qtNavDialogChecks.Check($"stack edit S37: InsertPageBefore goes without a slide ({insertStep}=='PUSH Immediate', busy {insertBusy}==false), the top stays '{renderer.CurrentPage?.Title}'",
								insertStep == "PUSH Immediate" && insertBusy == "false" && ReferenceEquals(renderer.CurrentPage, slides));
							dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(800), () =>
							{
								LogStacks("before remove", page, navigation);
								navigation.RemovePage(still);
								dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(120), () =>
								{
									var removeStep = renderer.LastNativeNavStep;
									var removeBusy = QtHost.QtHostRuntime.Eval("pageStack.busy");
									_qtNavDialogChecks.Check($"stack edit S37: RemovePage goes without a slide ({removeStep}=='POP Immediate', busy {removeBusy}==false), the top stays '{renderer.CurrentPage?.Title}'",
										removeStep == "POP Immediate" && removeBusy == "false" && ReferenceEquals(renderer.CurrentPage, slides));
									LogStacks("before pop to root", page, navigation);
									var popped = navigation.PopToRootAsync(false);
									// MAUI refuses a push while a navigation is still running: the next step waits for it.
									WaitFor(dispatcher, () => popped.IsCompleted, 4000, () =>
									{
										Console.Error.WriteLine($"[Sailfish] Qt navdialog diag: pop to root completed {popped.IsCompleted} faulted {popped.IsFaulted}, stack {navigation.NavigationStack.Count}");
										SpanTap(renderer, dispatcher, navigation);
									});
								});
							});
						});
					});
				});
			});
		});
	}

	/// <summary>Tracker S42: a real tap on a FormattedText span with a TapGestureRecognizer fires it (the span is a
	/// "span:N" link; Text.linkAt finds where it is drawn).</summary>
	private void SpanTap(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, INavigation navigation)
	{
		var taps = 0;
		var link = new Span { Text = "these terms", TextColor = Microsoft.Maui.Graphics.Colors.Orange, TextDecorations = TextDecorations.Underline };
		var tap = new TapGestureRecognizer();
		tap.Tapped += (_, _) => taps++;
		link.GestureRecognizers.Add(tap);
		var label = new Label
		{
			FontSize = 24,
			Padding = 16,
			FormattedText = new FormattedString { Spans = { new Span { Text = "Please read " }, link, new Span { Text = " first." } } },
		};
		var pushed = navigation.PushAsync(new ContentPage { Title = "ND span", Content = label }, false);
		WaitFor(dispatcher, () => pushed.IsCompleted, 4000, () =>
		{
			Console.Error.WriteLine($"[Sailfish] Qt navdialog diag: span page push completed {pushed.IsCompleted} faulted {pushed.IsFaulted} " +
				$"({pushed.Exception?.GetBaseException().Message}), stack {navigation.NavigationStack.Count}, top '{renderer.CurrentPage?.Title}'");
			var js = DiagQml.ItemJs(DiagQml.HostOf(renderer, label));
			var point = QtHost.QtHostRuntime.Eval(
				$"(function(){{var t={js};if(!t)return '';for(var y=0;y<t.height;y+=4)for(var x=0;x<t.width;x+=4)if(t.linkAt(x,y)==='span:1'){{var p=t.mapToItem(null,x+6,y);return p.x+','+p.y;}}return '';}})()");
			if (DiagQml.TryPoint(point, out var x, out var y))
				DiagQml.Tap(x, y);
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(800), () =>
			{
				_qtNavDialogChecks.Check($"span tap S42: a real tap on a tappable span fires its recognizer (x{taps}==1, link found at '{point}')",
					taps == 1 && point.Length > 0);
				Shot(dispatcher, "navdialog-4-span", () =>
				{
					var popped = navigation.PopAsync(false);
					WaitFor(dispatcher, () => popped.IsCompleted, 4000, () => KeyboardApi(dispatcher, navigation));
				});
			});
		});
	}

	/// <summary>Tracker S45: SailfishKeyboard.Show(entry) opens Maliit for it, Hide closes it and unfocuses it.</summary>
	private void KeyboardApi(SailfishDispatcher dispatcher, INavigation navigation)
	{
		var entry = new Entry { Placeholder = "keyboard API" };
		var pushed = navigation.PushAsync(new ContentPage { Title = "ND keyboard", Content = new VerticalStackLayout { Padding = 16, Children = { entry } } }, false);
		WaitFor(dispatcher, () => pushed.IsCompleted, 4000, () =>
		{
			if (pushed.IsFaulted)
				Console.Error.WriteLine($"[Sailfish] Qt navdialog diag: keyboard page push failed: {pushed.Exception?.GetBaseException().Message}");
			Microsoft.Maui.SailfishOS.Platform.SailfishKeyboard.Show(entry);
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200), () =>
			{
				var shown = Microsoft.Maui.SailfishOS.Platform.SailfishKeyboard.IsShowing;
				var focused = entry.IsFocused;
				Shot(dispatcher, "navdialog-5-keyboard", () =>
				{
					Microsoft.Maui.SailfishOS.Platform.SailfishKeyboard.Hide();
					dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200), () =>
					{
						_qtNavDialogChecks.Check($"keyboard API S45: Show(entry) focuses it ({focused}) and opens the keyboard ({shown}); Hide closes it " +
							$"(showing {Microsoft.Maui.SailfishOS.Platform.SailfishKeyboard.IsShowing}) and unfocuses the entry ({entry.IsFocused})",
							shown && focused && !Microsoft.Maui.SailfishOS.Platform.SailfishKeyboard.IsShowing && !entry.IsFocused);
						_ = navigation.PopAsync(false);
						dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200), FinishNavDialog);
					});
				});
			});
		});
	}

	private void FinishNavDialog()
	{
		_qtNavDialogChecks.CheckNoOffThreadCalls();
		_qtNavDialogChecks.Accept("OK — chained and background alerts show in turn; unanimated pushes and stack edits go without a slide; a span tap fires");
		QtHost.QtHostRuntime.Shutdown();
	}
}
