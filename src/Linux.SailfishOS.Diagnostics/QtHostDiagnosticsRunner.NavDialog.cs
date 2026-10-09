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
					WaitFor(dispatcher, () => popped.IsCompleted, 4000, () => MixedFontRow(renderer, dispatcher, navigation));
				});
			});
		});
	}

	/// <summary>Tracker S43: a FormattedText row with a 14 dp and a 40 dp span, in an Auto grid row, gets the height its
	/// rich text needs (the QML label's contentHeight), so the tall span is not clipped and the next row does not
	/// overlap it.</summary>
	private void MixedFontRow(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, INavigation navigation)
	{
		var mixed = new Label
		{
			FormattedText = new FormattedString
			{
				Spans =
				{
					new Span { Text = "Total ", FontSize = 14 },
					new Span { Text = "1 234,56 zł", FontSize = 40, FontAttributes = FontAttributes.Bold },
					new Span { Text = " incl. VAT", FontSize = 14 },
				},
			},
		};
		var below = new Label { Text = "the row below", BackgroundColor = Microsoft.Maui.Graphics.Colors.DarkSlateGray };
		var grid = new Grid { Padding = 16, RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) } };
		grid.Add(mixed, 0, 0);
		grid.Add(below, 0, 1);
		var pushed = navigation.PushAsync(new ContentPage { Title = "ND fonts", Content = grid }, false);
		WaitFor(dispatcher, () => pushed.IsCompleted && mixed.Height > 0, 4000, () =>
		{
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(600), () =>
			{
				var js = DiagQml.ItemJs(DiagQml.HostOf(renderer, mixed));
				var raw = QtHost.QtHostRuntime.Eval($"(function(){{var t={js};return t?(t.contentHeight+','+t.contentWidth+','+t.height):'';}})()");
				var parts = raw.Split(',');
				var density = Microsoft.Maui.SailfishOS.Platform.SailfishDisplay.Density;
				var contentDp = parts.Length == 3 && double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ch) ? ch / density : -1;
				var scale = Microsoft.Maui.SailfishOS.Handlers.SailfishFontRules.TextScale();
				var themeSizes = QtHost.QtHostRuntime.Eval("Theme.fontSizeMedium + ',' + Theme.fontSizeMediumBase");
				_qtNavDialogChecks.Check($"mixed fonts S43: the 14/40 dp FormattedText row is {mixed.Height:F1} dp tall, its rich text needs {contentDp:F1} dp " +
					$"(QML contentHeight/contentWidth/height px '{raw}'); the next row starts at {below.Y:F1} ≥ {mixed.Y + mixed.Height:F1}; " +
					$"text scale {scale:F2} from Theme.fontSizeMedium,Base px '{themeSizes}'",
					contentDp > 0 && mixed.Height + 0.5 >= contentDp && below.Y + 0.5 >= mixed.Y + mixed.Height &&
					themeSizes.Split(',') is [var m, var b] && double.TryParse(m, System.Globalization.CultureInfo.InvariantCulture, out var mv) && mv > 0 &&
					double.TryParse(b, System.Globalization.CultureInfo.InvariantCulture, out var bv) && bv > 0);
				Shot(dispatcher, "navdialog-4b-fonts", () =>
				{
					var popped = navigation.PopAsync(false);
					WaitFor(dispatcher, () => popped.IsCompleted, 4000, () => KeyboardApi(renderer, dispatcher, navigation));
				});
			});
		});
	}

	/// <summary>Tracker S45: SailfishKeyboard.Show(entry) opens Maliit for it, Hide closes it and unfocuses it.</summary>
	private void KeyboardApi(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, INavigation navigation)
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
						var popped = navigation.PopAsync(false);
						WaitFor(dispatcher, () => popped.IsCompleted, 4000, () => KeyboardForm(renderer, dispatcher, navigation, scroll: true));
					});
				});
			});
		});
	}

	/// <summary>Tracker S44: the last Entry of a long form (in a ScrollView, then as a page's plain content) stays above the
	/// keyboard once it has focus. Window coordinates: the field's bottom against Qt.inputMethod.keyboardRectangle.</summary>
	private void KeyboardForm(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, INavigation navigation, bool scroll)
	{
		var fields = new VerticalStackLayout { Padding = 16, Spacing = 8 };
		Entry? last = null;
		// The ScrollView form is longer than the screen; the plain one fits, its last field low on the page (visible
		// until the keyboard opens), as a form without a ScrollView must to be usable at all.
		// The plain form fits the page in either orientation (8 fields in portrait, 3 in landscape).
		var landscape = Microsoft.Maui.SailfishOS.Platform.SailfishDisplay.Orientation is
			Microsoft.Maui.SailfishOS.Platform.SailfishOrientation.Landscape or Microsoft.Maui.SailfishOS.Platform.SailfishOrientation.LandscapeInverted;
		for (var i = 1; i <= (scroll ? 14 : landscape ? 3 : 8); i++)
		{
			fields.Add(new Label { Text = $"Field {i}" });
			fields.Add(last = new Entry { Placeholder = $"value {i}" });
		}
		var page = new ContentPage { Title = scroll ? "ND form" : "ND form plain", Content = scroll ? new ScrollView { Content = fields } : fields };
		var pushed = navigation.PushAsync(page, false);
		WaitFor(dispatcher, () => pushed.IsCompleted && last!.Height > 0, 4000, () =>
		{
			var before = QtHost.QtHostRuntime.Eval($"(function(){{var t={DiagQml.ItemJs(DiagQml.HostOf(renderer, last!))};if(!t)return '';var p=t.mapToItem(null,0,0);return p.y+','+(p.y+t.height);}})()");
			Console.Error.WriteLine($"[Sailfish] Qt navdialog diag: keyboard form S44 ({(scroll ? "ScrollView" : "plain content")}) before the keyboard: last Entry y '{before}'");
			Microsoft.Maui.SailfishOS.Platform.SailfishKeyboard.Show(last!);
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1800), () =>
			{
				// In the page's own coordinates, which turn with the orientation: Silica shrinks the page by the keyboard
				// (ApplicationWindow: height − panelSize), so the field is visible when it lies within 0..page.height.
				var js = DiagQml.ItemJs(DiagQml.HostOf(renderer, last!));
				var raw = QtHost.QtHostRuntime.Eval($"(function(){{var t={js};if(!t)return '';var pg=t;while(pg&&pg.mauiContentHeight===undefined)pg=pg.parent;if(!pg)return '';" +
					"var f=null;for(var i=0;i<pg.children.length;i++){var c=pg.children[i];if(c.contentHeight!==undefined&&c.flickableDirection!==undefined)f=c;}" +
					"var p=t.mapToItem(pg,0,0);return p.y+','+(p.y+t.height)+','+pg.height+','+Qt.inputMethod.visible+','+t.activeFocus+','+" +
					"(f?f.contentY:-1)+','+(f?f.contentHeight:-1)+','+pg.mauiContentHeight;})()");
				var parts = raw.Split(',');
				double N(int i) => parts.Length > i && double.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
				var (top, bottom, pageH) = (N(0), N(1), N(2));
				var shown = parts.Length > 3 && parts[3] == "true";
				var visible = shown && top >= -1 && bottom <= pageH + 1;
				_qtNavDialogChecks.Check($"keyboard form S44 ({(scroll ? "ScrollView" : "plain content")}): the focused last Entry spans y {top:F0}–{bottom:F0} px of the page, " +
					$"which the keyboard leaves {pageH:F0} px tall (shown {shown}; raw top,bottom,page,shown,focus,contentY,contentHeight,mauiContentHeight '{raw}')", visible);
				Shot(dispatcher, scroll ? "navdialog-6-form" : "navdialog-7-form-plain", () =>
				{
					Microsoft.Maui.SailfishOS.Platform.SailfishKeyboard.Hide();
					dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1500), () => AfterHide());
				});
			});

			// A plain page is panned like Android's adjustPan; it must come back when the keyboard goes, since its
			// flickable is not interactive and the user could not scroll it back.
			void AfterHide()
			{
				var after = QtHost.QtHostRuntime.Eval($"(function(){{var t={DiagQml.ItemJs(DiagQml.HostOf(renderer, last!))};if(!t)return '';var p=t.mapToItem(null,0,0);return p.y+','+(p.y+t.height);}})()");
				double Top(string v) => double.TryParse(v.Split(',')[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y) ? y : double.NaN;
				if (!scroll)
					_qtNavDialogChecks.Check($"keyboard form S44 (plain content): the page comes back once the keyboard is hidden (last Entry y '{before}' before, '{after}' after)",
						Math.Abs(Top(before) - Top(after)) <= 2);
				{
					var popped = navigation.PopAsync(false);
					WaitFor(dispatcher, () => popped.IsCompleted, 4000, () =>
					{
						if (scroll)
							KeyboardForm(renderer, dispatcher, navigation, scroll: false);
						else
							dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200), FinishNavDialog);
					});
				}
			}
		});
	}

	private void FinishNavDialog()
	{
		_qtNavDialogChecks.CheckNoOffThreadCalls();
		_qtNavDialogChecks.Accept("OK — chained and background alerts show in turn; unanimated pushes and stack edits go without a slide; a span tap fires");
		QtHost.QtHostRuntime.Shutdown();
	}
}
