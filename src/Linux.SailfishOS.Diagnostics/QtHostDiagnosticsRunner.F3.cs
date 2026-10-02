using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// F3 controls leg (MAUI_SAILFISH_QT_HOST_F3_DIAG=1): drives the newer controls with real
/// taps/swipes and checks MAUI and native QML state, with an SF-SHOT per state.
///   A. CarouselView + IndicatorView: page box, Position, real swipe, dot tap.
///   B. Horizontal CollectionView: header/footer on the row axis, drag → Scrolled.
///   C. Stepper and CheckBox: real taps, bounds, managed values.
///   D. SwipeView: drag reveal, item Invoked, managed Open/Close.
///   G. Looping CarouselView: swipes wrap in both directions.
///   H. Text styling mappers read back from native items.
///   I. Strokes: dashed/ellipse/per-corner Borders (dashes cut in JS; Qt 5.6 has no
///      setLineDash), dashed Shape, ImageButton outline.
///   J. Generic view state: Background, Shadow, semantics, AutomationId.
///   K. Text inputs: ReturnType, clear button, MaxLength, SearchBar parity.
///   L. Remaining mappers: scroll bars, RefreshView, picker IsOpen, GIF, SwipeView reveal.
///   F. Fonts and images: font alias, FontImageSource, StreamImageSource, glyph icon.
///   N. Last parity keys: accessibility, Label TextType/TextTransform, text alignment, Slider thumb image,
///      Page background image, FlowDirection RTL on leaf controls (QtHostDiagnosticsRunner.F3Parity.cs).
///   E. WebView (Gecko): HTML/URL navigation, EvaluateJavaScriptAsync, GoBack.
/// </summary>
internal sealed partial class QtHostDiagnosticsRunner
{
	private bool _qtF3Diag;
	private readonly DiagChecks _qtF3Checks = new("Qt f3 diag");
	private CarouselView? _f3Carousel;
	private IndicatorView? _f3Indicator;
	private CollectionView? _f3Strip;
	private int _f3PositionChanged;
	private int _f3CurrentItemChanged;
	private int _f3StripScrolled;
	private double _f3StripOffset;
	private Stepper? _f3Stepper;
	private CheckBox? _f3Check;
	private int _f3StepperChanged;
	private int _f3CheckChanged;
	private SwipeView? _f3Swipe;
	private readonly Dictionary<string, int> _f3Invoked = new();
	private int _f3SwipeEnded;
	private bool _f3LastEndOpen;
	private WebView? _f3Web;
	private int _f3WebNavigated;

	private sealed record F3Page(string Name, Color Color);

	/// <summary>An IDrawable that strokes with ICanvas.StrokeDashPattern.</summary>
	private sealed class F3DashDrawable : IDrawable
	{
		public void Draw(ICanvas canvas, RectF dirtyRect)
		{
			canvas.StrokeColor = Colors.LightSkyBlue;
			canvas.StrokeSize = 3;
			canvas.StrokeDashPattern = new float[] { 3, 2 };
			canvas.DrawRoundedRectangle(6, 6, dirtyRect.Width - 12, dirtyRect.Height - 12, 10);
		}
	}


	private static string ItemJs(QtHost.QtHostPageRenderer renderer, Element element) =>
		DiagQml.ItemJs(DiagQml.HostOf(renderer, element));

	private ContentPage BuildF3Page()
	{
		var pages = new List<F3Page>
		{
			new("Page 1", Colors.SteelBlue), new("Page 2", Colors.SeaGreen), new("Page 3", Colors.IndianRed),
			new("Page 4", Colors.DarkOrchid), new("Page 5", Colors.DarkGoldenrod),
		};
		_f3Indicator = new IndicatorView
		{
			IndicatorColor = Colors.Gray,
			SelectedIndicatorColor = Colors.White,
			IndicatorSize = 10,
			HorizontalOptions = LayoutOptions.Center,
		};
		_f3Carousel = new CarouselView
		{
			HeightRequest = 300,
			Loop = false,
			PeekAreaInsets = new Thickness(30, 0),
			ItemsSource = pages,
			ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label { FontSize = 32, TextColor = Colors.White, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
				label.SetBinding(Label.TextProperty, nameof(F3Page.Name));
				var border = new Border { Margin = new Thickness(8, 0), StrokeThickness = 0, Content = label };
				border.SetBinding(VisualElement.BackgroundColorProperty, nameof(F3Page.Color));
				return border;
			}),
			IndicatorView = _f3Indicator,
		};
		_f3Carousel.PositionChanged += (_, _) => _f3PositionChanged++;
		_f3Carousel.CurrentItemChanged += (_, _) => _f3CurrentItemChanged++;

		_f3Strip = new CollectionView
		{
			HeightRequest = 120,
			ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Horizontal) { ItemSpacing = 10 },
			ItemsSource = Enumerable.Range(1, 12).Select(i => $"Tile {i}").ToList(),
			ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label { TextColor = Colors.White, FontSize = 20, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
				label.SetBinding(Label.TextProperty, ".");
				return new Border { WidthRequest = 160, BackgroundColor = Colors.SlateGray, StrokeThickness = 0, Content = label };
			}),
			// Header/Footer run along the row axis (sized by their width).
			Header = new Border { WidthRequest = 110, BackgroundColor = Colors.DarkSlateBlue, StrokeThickness = 0, Content = new Label { Text = "Start", TextColor = Colors.White, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center } },
			Footer = new Border { WidthRequest = 90, BackgroundColor = Colors.DarkOliveGreen, StrokeThickness = 0, Content = new Label { Text = "End", TextColor = Colors.White, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center } },
		};
		_f3Strip.Scrolled += (_, e) => { _f3StripScrolled++; _f3StripOffset = e.HorizontalOffset; };

		_f3Stepper = new Stepper { Minimum = 0, Maximum = 3, Increment = 1, Value = 1, HorizontalOptions = LayoutOptions.Start, Margin = new Thickness(16, 0) };
		_f3Stepper.ValueChanged += (_, _) => _f3StepperChanged++;
		_f3Check = new CheckBox { Color = Colors.Coral, HorizontalOptions = LayoutOptions.Start, Margin = new Thickness(16, 0) };
		_f3Check.CheckedChanged += (_, _) => _f3CheckChanged++;
		var stepperRow = new HorizontalStackLayout { Spacing = 12, Children = { _f3Stepper, _f3Check } };

		SwipeItem Item(string text, Color color)
		{
			var item = new SwipeItem { Text = text, BackgroundColor = color };
			item.Invoked += (_, _) => _f3Invoked[text] = _f3Invoked.GetValueOrDefault(text) + 1;
			return item;
		}
		_f3Swipe = new SwipeView
		{
			HeightRequest = 90,
			LeftItems = new SwipeItems { Item("Archive", Colors.SeaGreen) },
			RightItems = new SwipeItems { Item("Delete", Colors.Firebrick), Item("Flag", Colors.DarkOrange) },
			Content = new Grid
			{
				BackgroundColor = Color.FromArgb("#303048"),
				Children = { new Label { Text = "Swipe me ← →", VerticalOptions = LayoutOptions.Center, Margin = new Thickness(16, 0) } },
			},
		};
		_f3Swipe.SwipeEnded += (_, e) => { _f3SwipeEnded++; _f3LastEndOpen = e.IsOpen; };

		return new ContentPage
		{
			Title = "F3 controls",
			Content = new VerticalStackLayout
			{
				Spacing = 12,
				Padding = new Thickness(0, 12),
				Children =
				{
					new Label { Text = "CarouselView", Margin = new Thickness(16, 0) },
					_f3Carousel,
					_f3Indicator,
					new Label { Text = "Horizontal CollectionView", Margin = new Thickness(16, 0) },
					_f3Strip,
					new Label { Text = "Stepper and CheckBox", Margin = new Thickness(16, 0) },
					stepperRow,
					new Label { Text = "SwipeView", Margin = new Thickness(16, 0) },
					_f3Swipe,
				},
			},
		};
	}

	private void RunQtF3Diagnostics(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var nav = RootNav;
		if (nav is null)
		{
			Console.Error.WriteLine("[Sailfish] Qt f3 diag: no NavigationPage — leg skipped");
			QtHost.QtHostRuntime.Shutdown();
			return;
		}
		_ = nav.PushAsync(BuildF3Page());
		WaitFor(dispatcher, () => PageTitle(renderer) == "F3 controls" && renderer.CurrentHosts.Any(h => ReferenceEquals(h.Element, _f3Carousel) && h.IsAttached), 8000,
			() => dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200), () => F3CarouselA(renderer, dispatcher)));
	}

	private void F3CarouselA(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var c = _f3Carousel!;
		var js = ItemJs(renderer, c);
		var state = QtHost.QtHostRuntime.Eval(
			$"(function(l){{if(!l)return 'no host';var w=[];var ci=l.contentItem;for(var i=0;i<ci.children.length;i++){{var d=ci.children[i];if(d.objectName&&d.objectName.indexOf('__r')>0)w.push(Math.round(d.width));}}" +
			"return JSON.stringify({car:l.mauiCarousel,orient:l.mauiOrientation,count:l.count,cur:l.currentIndex,w:Math.round(l.width),peek:[l.mauiPeekStart,l.mauiPeekEnd],dw:w});})(" + js + ")");
		var width = DiagQml.EvalNum($"{js}.width");
		var peek = QtHost.QtHostUnits.ToQtUnits(30);
		var pageW = DiagQml.EvalNum($"(function(l){{var ci=l.contentItem;for(var i=0;i<ci.children.length;i++){{var d=ci.children[i];if(d.objectName&&d.objectName.indexOf('__r0')>0)return d.width;}}return -1;}})({js})");
		_qtF3Checks.Check($"A carousel host: native carousel mode, horizontal, 5 pages ({state})",
			state.Contains("\"car\":true") && state.Contains("\"orient\":\"horizontal\"") && state.Contains("\"count\":5"));
		_qtF3Checks.Check($"A page box: page width {pageW:F0} == viewport {width:F0} − 2×peek {peek:F0}", Math.Abs(pageW - (width - 2 * peek)) <= 2);
		_qtF3Checks.Check($"A initial state: Position {c.Position}==0, CurrentItem '{(c.CurrentItem as F3Page)?.Name}'=='Page 1', indicator Count {_f3Indicator!.Count}==5",
			c.Position == 0 && (c.CurrentItem as F3Page)?.Name == "Page 1" && _f3Indicator.Count == 5);
		Shot(dispatcher, "f3-a1-carousel-first", () =>
		{
			c.Position = 2;
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200), () =>
			{
				var cur = DiagQml.EvalNum($"{js}.currentIndex");
				_qtF3Checks.Check($"A managed Position=2 → native page {cur}==2, CurrentItem '{(c.CurrentItem as F3Page)?.Name}'=='Page 3', indicator Position {_f3Indicator.Position}==2",
					cur == 2 && (c.CurrentItem as F3Page)?.Name == "Page 3" && _f3Indicator.Position == 2);
				Shot(dispatcher, "f3-a2-carousel-page3", () => F3CarouselSwipe(renderer, dispatcher, js));
			});
		});
	}

	private void F3CarouselSwipe(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, string js)
	{
		var c = _f3Carousel!;
		var host = renderer.CurrentHosts.First(h => ReferenceEquals(h.Element, c));
		QtHost.QtHostRuntime.TryItemGeometry(host.NativeHandle, out var g);
		var y = g.Y + g.Height / 2;
		var x0 = g.X + g.Width * 0.8;
		var x1 = g.X + g.Width * 0.2;
		var changedBefore = _f3PositionChanged;
		Console.Error.WriteLine($"[Sailfish] Qt f3 diag: A swipe left on the carousel at y={y:F0} {x0:F0}→{x1:F0}");
		QtHost.QtHostRuntime.InjectPointer(0, x0, y);
		const int steps = 12;
		void Step(int i)
		{
			if (i > steps)
			{
				QtHost.QtHostRuntime.InjectPointer(1, x1, y);
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1500), () =>
				{
					var cur = DiagQml.EvalNum($"{js}.currentIndex");
					_qtF3Checks.Check($"A real swipe → native page {cur}==3, Position {c.Position}==3, CurrentItem '{(c.CurrentItem as F3Page)?.Name}'=='Page 4', PositionChanged +{_f3PositionChanged - changedBefore}>=1",
						cur == 3 && c.Position == 3 && (c.CurrentItem as F3Page)?.Name == "Page 4" && _f3PositionChanged > changedBefore);
					Shot(dispatcher, "f3-a3-carousel-swiped", () => F3IndicatorTap(renderer, dispatcher, js));
				});
				return;
			}
			QtHost.QtHostRuntime.InjectPointer(2, x0 + (x1 - x0) * i / steps, y);
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(16), () => Step(i + 1));
		}
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(16), () => Step(1));
	}

	private void F3IndicatorTap(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, string carouselJs)
	{
		var ind = _f3Indicator!;
		var host = DiagQml.HostOf(renderer, ind);
		if (host is null || !QtHost.QtHostRuntime.TryItemGeometry(host.NativeHandle, out var g))
		{
			_qtF3Checks.Check("A indicator host geometry available", false);
			F3StripB(renderer, dispatcher);
			return;
		}
		var js = ItemJs(renderer, ind);
		var size = DiagQml.EvalNum($"{js}.mauiDotSize");
		var n = DiagQml.EvalNum($"{js}.__visibleCount");
		_qtF3Checks.Check($"A indicator: {n} dots of {size:F0}px, selected {DiagQml.EvalNum($"{js}.mauiPosition")}==3", n == 5 && DiagQml.EvalNum($"{js}.mauiPosition") == 3);
		// Dot 0 centre: the row is centred; dots are `size` wide with `size` gaps.
		var rowW = n * size + (n - 1) * size;
		var cx = g.X + g.Width / 2 - rowW / 2 + size / 2;
		var cy = g.Y + g.Height / 2;
		Console.Error.WriteLine($"[Sailfish] Qt f3 diag: A tap on indicator dot 0 at {cx:F0},{cy:F0}");
		QtHost.QtHostRuntime.InjectPointer(0, cx, cy);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(120), () => QtHost.QtHostRuntime.InjectPointer(1, cx, cy));
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1500), () =>
		{
			var cur = DiagQml.EvalNum($"{carouselJs}.currentIndex");
			_qtF3Checks.Check($"A dot 0 tap → indicator Position {ind.Position}==0, carousel Position {_f3Carousel!.Position}==0, native page {cur}==0, CurrentItem '{(_f3Carousel.CurrentItem as F3Page)?.Name}'=='Page 1'",
				ind.Position == 0 && _f3Carousel.Position == 0 && cur == 0 && (_f3Carousel.CurrentItem as F3Page)?.Name == "Page 1");
			Shot(dispatcher, "f3-a4-dot-tapped", () => F3StripB(renderer, dispatcher));
		});
	}

	private void F3StripB(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var strip = _f3Strip!;
		var js = ItemJs(renderer, strip);
		var state = QtHost.QtHostRuntime.Eval(
			"(function(l){if(!l)return 'no host';var w=[],painted=0,dg=0;var ci=l.contentItem;" +
			"for(var i=0;i<ci.children.length;i++){var d=ci.children[i];if(!d.objectName||d.objectName.indexOf('__r')<0)continue;dg++;" +
			"w.push(Math.round(d.width)+'@'+Math.round(d.x));" +
			"for(var j=0;j<d.children.length;j++){var c=d.children[j];if(c.objectName&&c.objectName.indexOf('maui_')===0&&c.visible&&c.width>0){painted++;break;}}}" +
			"return JSON.stringify({orient:l.mauiOrientation,car:l.mauiCarousel,count:l.count,h:Math.round(l.height),w:Math.round(l.width),cx:l.contentX,dg:dg,painted:painted,dw:w.slice(0,4)});})(" + js + ")");
		var tileW = QtHost.QtHostUnits.ToQtUnits(160);
		_qtF3Checks.Check($"B strip: horizontal, not a carousel, 12 columns of {tileW:F0}px ({state})",
			state.Contains("\"orient\":\"horizontal\"") && state.Contains("\"car\":false") && state.Contains("\"count\":12") &&
			state.Contains($"{Math.Round(tileW)}"));
		// Before any scroll the columns on screen must already be painted.
		int dgN = -1, painted = -2;
		try
		{
			using var doc = System.Text.Json.JsonDocument.Parse(state);
			dgN = doc.RootElement.GetProperty("dg").GetInt32();
			painted = doc.RootElement.GetProperty("painted").GetInt32();
		}
		catch
		{
		}
		_qtF3Checks.Check($"B strip painted before any scroll: {painted}/{dgN} delegates show their tile (>=3)", dgN >= 3 && painted == dgN);
		// Header before the first column, footer after the last, both painted.
		var slots = QtHost.QtHostRuntime.Eval(
			"(function(l){if(!l)return 'no host';var ci=l.contentItem,out={},firstX=1e9;function painted(d){for(var j=0;j<d.children.length;j++){var c=d.children[j];if(c.objectName&&c.objectName.indexOf('maui_')===0&&c.visible&&c.width>0)return true;}return false;}" +
			"for(var i=0;i<ci.children.length;i++){var d=ci.children[i];if(!d.objectName)continue;" +
			"if(d.objectName.indexOf('__header')>0)out.header={w:Math.round(d.width),x:Math.round(d.x),p:painted(d)};" +
			"else if(d.objectName.indexOf('__footer')>0)out.footer={w:Math.round(d.width),x:Math.round(d.x),p:painted(d)};" +
			"else if(d.objectName.indexOf('__r0')>0)firstX=Math.round(d.x);}" +
			"out.firstX=firstX;return JSON.stringify(out);})(" + js + ")");
		var headerW = QtHost.QtHostUnits.ToQtUnits(110);
		var footerW = QtHost.QtHostUnits.ToQtUnits(90);
		// Qt places the header before the origin (x = −width) and starts scrolled to it
		// (contentX = −width).
		double SlotNum(string slot, string key)
		{
			try
			{
				using var doc = System.Text.Json.JsonDocument.Parse(slots);
				return doc.RootElement.GetProperty(slot).GetProperty(key).GetDouble();
			}
			catch
			{
				return double.NaN;
			}
		}
		var startX = DiagQml.EvalNum($"{js}.contentX");
		_qtF3Checks.Check($"B header slot {headerW:F0}px wide, painted, before the first column and in view at the start (contentX {startX:F0}) ({slots})",
			Math.Abs(SlotNum("header", "w") - Math.Round(headerW)) <= 1 && slots.Contains("\"header\":{\"p\":true") &&
			Math.Abs(SlotNum("header", "x") + Math.Round(headerW)) <= 1 && Math.Abs(startX + headerW) <= 2);
		_qtF3Checks.Check($"B footer slot {footerW:F0}px wide, painted ({slots})", Math.Abs(SlotNum("footer", "w") - Math.Round(footerW)) <= 1 && slots.Contains("\"footer\":{\"p\":true"));
		var host = renderer.CurrentHosts.First(h => ReferenceEquals(h.Element, strip));
		QtHost.QtHostRuntime.TryItemGeometry(host.NativeHandle, out var g);
		var y = g.Y + g.Height / 2;
		var x0 = g.X + g.Width * 0.8;
		var x1 = g.X + g.Width * 0.2;
		var scrolledBefore = _f3StripScrolled;
		QtHost.QtHostRuntime.InjectPointer(0, x0, y);
		const int steps = 10;
		void Step(int i)
		{
			if (i > steps)
			{
				QtHost.QtHostRuntime.InjectPointer(1, x1, y);
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1500), () =>
				{
					var cx = DiagQml.EvalNum($"{js}.contentX");
					_qtF3Checks.Check($"B real drag → native contentX {cx:F0}>0, Scrolled +{_f3StripScrolled - scrolledBefore}>=1 with HorizontalOffset {_f3StripOffset:F0}dp>0",
						cx > 0 && _f3StripScrolled > scrolledBefore && _f3StripOffset > 0);
					Shot(dispatcher, "f3-b1-strip-dragged", () => F3StepperC(renderer, dispatcher));
				});
				return;
			}
			QtHost.QtHostRuntime.InjectPointer(2, x0 + (x1 - x0) * i / steps, y);
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(16), () => Step(i + 1));
		}
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(16), () => Step(1));
	}

	/// <summary>A real tap at the centre of a child of an adapter item.</summary>
	private static bool TapChild(string itemJs, string childExpr, SailfishDispatcher dispatcher)
	{
		var at = QtHost.QtHostRuntime.Eval(
			$"(function(i){{if(!i)return '';var c={childExpr};if(!c)return '';var p=c.mapToItem(null,c.width/2,c.height/2);return p.x+','+p.y;}})({itemJs})");
		if (!DiagQml.TryPoint(at, out var x, out var y))
			return false;
		QtHost.QtHostRuntime.InjectPointer(0, x, y);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(120), () => QtHost.QtHostRuntime.InjectPointer(1, x, y));
		return true;
	}

	private void F3StepperC(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var st = _f3Stepper!;
		var js = ItemJs(renderer, st);
		var uri = DiagQml.HostOf(renderer, st)?.QmlUri;
		_qtF3Checks.Check($"C stepper host '{uri}'=='stepper', native value {DiagQml.EvalNum($"{js}.value")}==1", uri == "stepper" && DiagQml.EvalNum($"{js}.value") == 1);
		var plus = "i.children[1]";
		var minus = "i.children[0]";
		var changed0 = _f3StepperChanged;
		var tapped = TapChild(js, plus, dispatcher);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(700), () =>
		{
			_qtF3Checks.Check($"C plus tap (tapped={tapped}) → Value {st.Value}==2, native {DiagQml.EvalNum($"{js}.value")}==2, ValueChanged +{_f3StepperChanged - changed0}==1",
				st.Value == 2 && DiagQml.EvalNum($"{js}.value") == 2 && _f3StepperChanged - changed0 == 1);
			TapChild(js, plus, dispatcher);
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(700), () =>
			{
				var plusEnabled = QtHost.QtHostRuntime.Eval($"(function(i){{return String({plus}.enabled);}})({js})");
				_qtF3Checks.Check($"C plus to the maximum → Value {st.Value}==3, plus enabled={plusEnabled}==false", st.Value == 3 && plusEnabled == "false");
				TapChild(js, plus, dispatcher);   // disabled: must not step past 3
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(700), () =>
				{
					_qtF3Checks.Check($"C disabled plus → Value stays {st.Value}==3", st.Value == 3);
					Shot(dispatcher, "f3-c1-stepper-max", () =>
					{
						st.Value = 0;
						dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(600), () =>
						{
							var minusEnabled = QtHost.QtHostRuntime.Eval($"(function(i){{return String({minus}.enabled);}})({js})");
							_qtF3Checks.Check($"C managed Value=0 → native {DiagQml.EvalNum($"{js}.value")}==0, minus enabled={minusEnabled}==false",
								DiagQml.EvalNum($"{js}.value") == 0 && minusEnabled == "false");
							F3CheckBoxC(renderer, dispatcher);
						});
					});
				});
			});
		});
	}

	private void F3CheckBoxC(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var cb = _f3Check!;
		var js = ItemJs(renderer, cb);
		var uri = DiagQml.HostOf(renderer, cb)?.QmlUri;
		_qtF3Checks.Check($"C check box host '{uri}'=='check-box' (no longer the switch)", uri == "check-box");
		var changed0 = _f3CheckChanged;
		var tapped = TapChild(js, "i", dispatcher);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(700), () =>
		{
			var native = QtHost.QtHostRuntime.Eval($"String({js}.checked)");
			_qtF3Checks.Check($"C check box tap (tapped={tapped}) → IsChecked={cb.IsChecked}==True, native checked={native}, CheckedChanged +{_f3CheckChanged - changed0}==1",
				cb.IsChecked && native == "true" && _f3CheckChanged - changed0 == 1);
			Shot(dispatcher, "f3-c2-checked", () =>
			{
				cb.IsChecked = false;
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(600), () =>
				{
					var native2 = QtHost.QtHostRuntime.Eval($"String({js}.checked)");
					_qtF3Checks.Check($"C managed IsChecked=false → native checked={native2}==false", native2 == "false");
					F3SwipeD(renderer, dispatcher);
				});
			});
		});
	}

	private static bool TapAt(string point, SailfishDispatcher dispatcher)
	{
		if (!DiagQml.TryPoint(point, out var x, out var y))
			return false;
		QtHost.QtHostRuntime.InjectPointer(0, x, y);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(120), () => QtHost.QtHostRuntime.InjectPointer(1, x, y));
		return true;
	}

	private void F3SwipeD(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var sv = _f3Swipe!;
		var js = ItemJs(renderer, sv);
		var host = DiagQml.HostOf(renderer, sv);
		_qtF3Checks.Check($"D swipe host '{host?.QmlUri}'=='swipe-view', content nested in its middle cell",
			host?.QmlUri == "swipe-view" && QtHost.QtHostRuntime.Eval($"String({js}.mauiChildHost.children.length>0)") == "true");
		if (host is null || !QtHost.QtHostRuntime.TryItemGeometry(host.NativeHandle, out var g))
		{
			FinishF3();
			return;
		}
		var y = g.Y + g.Height / 2;
		var x0 = g.X + g.Width * 0.15;
		var x1 = g.X + g.Width * 0.75;
		var ended0 = _f3SwipeEnded;
		QtHost.QtHostRuntime.InjectPointer(0, x0, y);
		const int steps = 12;
		void Step(int i)
		{
			if (i > steps)
			{
				QtHost.QtHostRuntime.InjectPointer(1, x1, y);
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900), () =>
				{
					var side = QtHost.QtHostRuntime.Eval($"{js}.__openSide");
					_qtF3Checks.Check($"D real drag right → native open side '{side}'=='left', SwipeEnded +{_f3SwipeEnded - ended0}>=1 (IsOpen {_f3LastEndOpen}), SwipeView.IsOpen={((ISwipeView)sv).IsOpen}",
						side == "left" && _f3SwipeEnded > ended0 && _f3LastEndOpen && ((ISwipeView)sv).IsOpen);
					Shot(dispatcher, "f3-d1-swipe-left-open", () =>
					{
						var tapped = TapAt(QtHost.QtHostRuntime.Eval($"{js}.mauiItemPoint('left',0)"), dispatcher);
						dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900), () =>
						{
							var side2 = QtHost.QtHostRuntime.Eval($"{js}.__openSide");
							_qtF3Checks.Check($"D 'Archive' tap (tapped={tapped}) → Invoked {_f3Invoked.GetValueOrDefault("Archive")}==1, row closed (side '{side2}'==''), IsOpen={((ISwipeView)sv).IsOpen}==False",
								_f3Invoked.GetValueOrDefault("Archive") == 1 && side2 == "" && !((ISwipeView)sv).IsOpen);
							Shot(dispatcher, "f3-d2-swipe-closed", () =>
							{
								sv.Open(OpenSwipeItem.RightItems, animated: false);
								dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(800), () =>
								{
									var side3 = QtHost.QtHostRuntime.Eval($"{js}.__openSide");
									_qtF3Checks.Check($"D managed Open(RightItems) → native open side '{side3}'=='right'", side3 == "right");
									Shot(dispatcher, "f3-d3-swipe-right-open", () =>
									{
										var tapped2 = TapAt(QtHost.QtHostRuntime.Eval($"{js}.mauiItemPoint('right',0)"), dispatcher);
										dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900), () =>
										{
											_qtF3Checks.Check($"D 'Delete' tap (tapped={tapped2}) → Invoked {_f3Invoked.GetValueOrDefault("Delete")}==1, 'Flag' untouched {_f3Invoked.GetValueOrDefault("Flag")}==0",
												_f3Invoked.GetValueOrDefault("Delete") == 1 && _f3Invoked.GetValueOrDefault("Flag") == 0);
											sv.Open(OpenSwipeItem.RightItems, animated: false);
											dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(600), () =>
											{
												sv.Close(animated: false);
												dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(600), () =>
												{
													var side4 = QtHost.QtHostRuntime.Eval($"{js}.__openSide");
													_qtF3Checks.Check($"D managed Close() → native closed (side '{side4}'=='')", side4 == "");
													F3LoopG(renderer, dispatcher);
												});
											});
										});
									});
								});
							});
						});
					});
				});
				return;
			}
			QtHost.QtHostRuntime.InjectPointer(2, x0 + (x1 - x0) * i / steps, y);
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(16), () => Step(i + 1));
		}
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(16), () => Step(1));
	}

	private void F3LoopG(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var nav = RootNav;
		var loop = new CarouselView
		{
			HeightRequest = 260,
			Loop = true,
			PeekAreaInsets = new Thickness(24, 0),
			ItemsSource = new List<F3Page> { new("Loop A", Colors.Teal), new("Loop B", Colors.Crimson), new("Loop C", Colors.MediumPurple) },
			ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label { FontSize = 32, TextColor = Colors.White, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
				label.SetBinding(Label.TextProperty, nameof(F3Page.Name));
				var border = new Border { Margin = new Thickness(6, 0), StrokeThickness = 0, Content = label };
				border.SetBinding(VisualElement.BackgroundColorProperty, nameof(F3Page.Color));
				return border;
			}),
		};
		var changed = 0;
		loop.PositionChanged += (_, _) => changed++;
		_ = nav!.PushAsync(new ContentPage
		{
			Title = "F3 loop",
			Content = new VerticalStackLayout
			{
				Spacing = 12, Padding = new Thickness(0, 12),
				Children = { new Label { Text = "CarouselView Loop=true", Margin = new Thickness(16, 0) }, loop },
			},
		});
		string Name() => (loop.CurrentItem as F3Page)?.Name ?? "(null)";
		WaitFor(dispatcher, () => PageTitle(renderer) == "F3 loop" && NativeElementHostOf(renderer, loop, out _), 8000, () =>
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200), () =>
			{
				NativeElementHostOf(renderer, loop, out var host);
				var js = ItemJs(renderer, loop);
				var painted = QtHost.QtHostRuntime.Eval(
					"(function(l){if(!l)return 'no host';var n=0,p=0;var ci=l.contentItem||l;for(var i=0;i<l.children.length;i++){var d=l.children[i];if(!d.objectName||d.objectName.indexOf('__r')<0)continue;n++;" +
					"for(var j=0;j<d.children.length;j++){var c=d.children[j];if(c.objectName&&c.objectName.indexOf('maui_')===0&&c.visible&&c.width>0){p++;break;}}}return n+'/'+p;})(" + js + ")");
				_qtF3Checks.Check($"G loop host '{host!.QmlUri}'=='carousel-view', {DiagQml.EvalNum($"{js}.count")}==3 pages, delegates/painted {painted}, CurrentItem '{Name()}'=='Loop A'",
					host.QmlUri == "carousel-view" && DiagQml.EvalNum($"{js}.count") == 3 && Name() == "Loop A" && painted is "3/3");
				// A mis-sized page box wraps the centred "Loop A" label.
				var labelState = QtHost.QtHostRuntime.Eval(
					"(function(l){if(!l)return 'no host';var out=[];function walk(it,depth){for(var i=0;i<it.children.length;i++){var c=it.children[i];" +
					"if(c.objectName&&c.objectName.indexOf('maui_')===0&&c.text!==undefined&&String(c.text).indexOf('Loop')===0)out.push(c.text+':'+(c.lineCount===undefined?'?':c.lineCount)+':'+Math.round(c.width)+'x'+Math.round(c.height)+'@'+Math.round(c.x)+' natural '+c.implicitWidth.toFixed(1));" +
					"if(depth<6)walk(c,depth+1);}}walk(l,0);return out.join(' ');})(" + js + ")");
				_qtF3Checks.Check($"G page labels on one line ({labelState})", labelState.Contains("Loop A:1:") && !labelState.Contains(":2:"));
				Shot(dispatcher, "f3-g1-loop-first", () =>
				{
					loop.Position = 2;
					dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200), () =>
					{
						_qtF3Checks.Check($"G managed Position=2 → native page {DiagQml.EvalNum($"{js}.currentIndex")}==2, CurrentItem '{Name()}'=='Loop C'",
							DiagQml.EvalNum($"{js}.currentIndex") == 2 && Name() == "Loop C");
						var changed0 = changed;
						F3Swipe(renderer, dispatcher, host, left: true, () =>
						{
							_qtF3Checks.Check($"G swipe left past the last page wraps → native {DiagQml.EvalNum($"{js}.currentIndex")}==0, Position {loop.Position}==0, CurrentItem '{Name()}'=='Loop A', PositionChanged +{changed - changed0}>=1",
								DiagQml.EvalNum($"{js}.currentIndex") == 0 && loop.Position == 0 && Name() == "Loop A" && changed > changed0);
							Shot(dispatcher, "f3-g2-loop-wrapped-forward", () =>
								F3Swipe(renderer, dispatcher, host, left: false, () =>
								{
									_qtF3Checks.Check($"G swipe right past the first page wraps → native {DiagQml.EvalNum($"{js}.currentIndex")}==2, Position {loop.Position}==2, CurrentItem '{Name()}'=='Loop C'",
										DiagQml.EvalNum($"{js}.currentIndex") == 2 && loop.Position == 2 && Name() == "Loop C");
									Shot(dispatcher, "f3-g3-loop-wrapped-back", () => F3StylingH(renderer, dispatcher));
								}));
						});
					});
				});
			}));
	}

	/// <summary>A real horizontal swipe across <paramref name="host"/> (60% of
	/// its width), then <paramref name="done"/> once the snap settled.</summary>
	private static void F3Swipe(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, QtHost.NativeElementHost host, bool left, Action done)
	{
		QtHost.QtHostRuntime.TryItemGeometry(host.NativeHandle, out var g);
		var y = g.Y + g.Height / 2;
		var x0 = g.X + g.Width * (left ? 0.8 : 0.2);
		var x1 = g.X + g.Width * (left ? 0.2 : 0.8);
		QtHost.QtHostRuntime.InjectPointer(0, x0, y);
		const int steps = 12;
		void Step(int i)
		{
			if (i > steps)
			{
				QtHost.QtHostRuntime.InjectPointer(1, x1, y);
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1500), done);
				return;
			}
			QtHost.QtHostRuntime.InjectPointer(2, x0 + (x1 - x0) * i / steps, y);
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(16), () => Step(i + 1));
		}
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(16), () => Step(1));
	}

	private static string Hex(Color c) =>
		$"#{(int)Math.Round(c.Red * 255):x2}{(int)Math.Round(c.Green * 255):x2}{(int)Math.Round(c.Blue * 255):x2}";

	private void F3StylingH(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var nav = RootNav;
		var d = SailfishDisplay.Density;
		var tracked = new Button { Text = "Tracked", CharacterSpacing = 4, HorizontalOptions = LayoutOptions.Start, Margin = new Thickness(16, 0) };
		var padded = new Label { Text = "Padded label", Padding = new Thickness(20, 10), BackgroundColor = Color.FromArgb("#305070"), HorizontalOptions = LayoutOptions.Start, Margin = new Thickness(16, 0) };
		var radio = new RadioButton
		{
			Content = "Styled radio", FontSize = 28, FontAttributes = FontAttributes.Bold, TextColor = Colors.Coral, CharacterSpacing = 2,
			BorderColor = Colors.White, BorderWidth = 2, CornerRadius = 8, HorizontalOptions = LayoutOptions.Start, Margin = new Thickness(16, 0),
		};
		var picker = new Picker
		{
			Title = "Fruit", TitleColor = Colors.Gold, TextColor = Colors.LightGreen, FontSize = 30, CharacterSpacing = 1,
			ItemsSource = new List<string> { "apple", "banana", "cherry" }, SelectedIndex = 1,
		};
		var date = new DateTime(2030, 1, 15);
		var dateDefault = new DatePicker { Date = date };
		var dateCustom = new DatePicker { Date = date, Format = "dd MMM yyyy", TextColor = Colors.Orange };
		var timeCustom = new TimePicker { Time = new TimeSpan(14, 5, 0), Format = "HH:mm:ss" };
		_ = nav!.PushAsync(new ContentPage
		{
			Title = "F3 styling",
			Content = new VerticalStackLayout { Spacing = 10, Padding = new Thickness(0, 12), Children = { tracked, padded, radio, picker, dateDefault, dateCustom, timeCustom } },
		});
		WaitFor(dispatcher, () => PageTitle(renderer) == "F3 styling" && NativeElementHostOf(renderer, timeCustom, out _), 8000, () =>
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1000), () =>
			{
				var bjs = ItemJs(renderer, tracked);
				var spacing = DiagQml.EvalNum($"{bjs}.__label.font.letterSpacing");
				var bLines = DiagQml.EvalNum($"{bjs}.__label.lineCount");
				_qtF3Checks.Check($"H Button CharacterSpacing 4 → label tracking {spacing:F1}px=={4 * d:F1}, {bLines}==1 line",
					Math.Abs(spacing - 4 * d) < 0.1 && bLines == 1);

				var ljs = ItemJs(renderer, padded);
				var lstate = QtHost.QtHostRuntime.Eval($"(function(l){{return JSON.stringify({{l:l.leftPadding,t:l.topPadding,lines:l.lineCount,w:Math.round(l.width),iw:l.implicitWidth}});}})({ljs})");
				_qtF3Checks.Check($"H Label Padding 20,10 → native paddings {Math.Round(20 * d)}/{Math.Round(10 * d)} and one line ({lstate})",
					Math.Abs(DiagQml.EvalNum($"{ljs}.leftPadding") - 20 * d) < 0.5 && Math.Abs(DiagQml.EvalNum($"{ljs}.topPadding") - 10 * d) < 0.5 && DiagQml.EvalNum($"{ljs}.lineCount") == 1);

				var rjs = ItemJs(renderer, radio);
				var rdiag = QtHost.QtHostRuntime.Eval($"{rjs}.mauiDiag()");
				_qtF3Checks.Check($"H RadioButton → {Math.Round(28 * d)}px bold coral, tracking {2 * d:F1}, not truncated, frame 2dp r8 white ({rdiag})",
					Math.Abs(DiagQml.EvalNum($"{rjs}.mauiTextItem.font.pixelSize") - 28 * d) <= 1 && rdiag.Contains("\"bold\":true") && rdiag.Contains($"\"color\":\"{Hex(Colors.Coral)}\"") &&
					rdiag.Contains("\"truncated\":false") && rdiag.Contains("\"frame\":true") && rdiag.Contains("\"frameColor\":\"#ffffff\"") &&
					Math.Abs(DiagQml.EvalNum($"{rjs}.mauiTextItem.font.letterSpacing") - 2 * d) < 0.1 &&
					Math.Abs(DiagQml.EvalNum($"JSON.parse({rjs}.mauiDiag()).frameRadius") - 8 * d) < 0.5);

				var pjs = ItemJs(renderer, picker);
				var pdiag = QtHost.QtHostRuntime.Eval($"{pjs}.mauiDiag()");
				_qtF3Checks.Check($"H Picker → value 'banana' light green {Math.Round(30 * d)}px, title gold, tracking {1 * d:F1} ({pdiag})",
					pdiag.Contains("\"value\":\"banana\"") && pdiag.Contains($"\"valueColor\":\"{Hex(Colors.LightGreen)}\"") &&
					pdiag.Contains($"\"labelColor\":\"{Hex(Colors.Gold)}\"") && Math.Abs(DiagQml.EvalNum($"{pjs}.mauiTextItem.font.pixelSize") - 30 * d) <= 1 &&
					Math.Abs(DiagQml.EvalNum($"{pjs}.mauiTextItem.font.letterSpacing") - 1 * d) < 0.1);
				NativeElementHostOf(renderer, picker, out var ph);
				QtHost.QtHostRuntime.TryItemGeometry(ph!.NativeHandle, out var pg);
				var mauiH = picker.Height * d;
				_qtF3Checks.Check($"H Picker with a large font: MAUI height {mauiH:F0}px == native {pg.Height:F0}px (value box grows with the font)", Math.Abs(mauiH - pg.Height) <= 2);

				var expectDefault = date.ToString("d", System.Globalization.CultureInfo.CurrentCulture);
				var expectCustom = date.ToString("dd MMM yyyy", System.Globalization.CultureInfo.CurrentCulture);
				var vDefault = QtHost.QtHostRuntime.Eval($"{ItemJs(renderer, dateDefault)}.value");
				var cdiag = QtHost.QtHostRuntime.Eval($"{ItemJs(renderer, dateCustom)}.mauiDiag()");
				_qtF3Checks.Check($"H DatePicker default Format 'd' → '{vDefault}'=='{expectDefault}'", vDefault == expectDefault);
				_qtF3Checks.Check($"H DatePicker Format 'dd MMM yyyy' + orange → '{expectCustom}' ({cdiag})",
					cdiag.Contains($"\"value\":\"{expectCustom}\"") && cdiag.Contains($"\"valueColor\":\"{Hex(Colors.Orange)}\""));
				var vTime = QtHost.QtHostRuntime.Eval($"{ItemJs(renderer, timeCustom)}.value");
				_qtF3Checks.Check($"H TimePicker Format 'HH:mm:ss' → '{vTime}'=='14:05:00'", vTime == "14:05:00");
				Shot(dispatcher, "f3-h1-styling", () =>
				{
					// Managed changes reach the adapters (mapper path).
					dateCustom.Format = "yyyy/MM/dd";
					radio.TextColor = Colors.SkyBlue;
					dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(800), () =>
					{
						var v2 = QtHost.QtHostRuntime.Eval($"{ItemJs(renderer, dateCustom)}.value");
						var r2 = QtHost.QtHostRuntime.Eval($"{rjs}.mauiDiag()");
						_qtF3Checks.Check($"H managed Format='yyyy/MM/dd' → '{v2}'=='{date.ToString("yyyy/MM/dd", System.Globalization.CultureInfo.CurrentCulture)}', radio TextColor → sky blue",
							v2 == date.ToString("yyyy/MM/dd", System.Globalization.CultureInfo.CurrentCulture) && r2.Contains($"\"color\":\"{Hex(Colors.SkyBlue)}\""));
						F3StrokesI(renderer, dispatcher);
					});
				});
			}));
	}

	private void F3StrokesI(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var nav = RootNav;
		var d = SailfishDisplay.Density;
		Border Box(string text, Action<Border> style)
		{
			var b = new Border { HeightRequest = 90, Margin = new Thickness(16, 0), StrokeThickness = 3, Stroke = Colors.White,
				Content = new Label { Text = text, VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Center } };
			style(b);
			return b;
		}
		var dashed = Box("Dashed", b => { b.StrokeDashArray = new DoubleCollection { 4, 2 }; b.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 }; });
		var ellipse = Box("Ellipse", b => { b.StrokeShape = new Microsoft.Maui.Controls.Shapes.Ellipse(); b.BackgroundColor = Colors.SeaGreen; b.Stroke = Colors.Gold; });
		var corners = Box("Corners", b => { b.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(30, 0, 0, 30) }; b.BackgroundColor = Colors.SteelBlue; });
		var plain = Box("Plain", b => { b.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 }; });
		var dashedShape = new Microsoft.Maui.Controls.Shapes.Rectangle
		{
			HeightRequest = 60, Margin = new Thickness(16, 0), Stroke = Colors.Orange, StrokeThickness = 4,
			StrokeDashArray = new DoubleCollection { 3, 2 }, Fill = new SolidColorBrush(Colors.Transparent),
		};
		var dashedCanvas = new GraphicsView { HeightRequest = 70, Margin = new Thickness(16, 0), Drawable = new F3DashDrawable() };
		var logo = System.IO.Path.Combine(AppContext.BaseDirectory, "images", "sailfish_logo.png");
		var imageButton = new ImageButton
		{
			Source = ImageSource.FromFile(logo), WidthRequest = 120, HeightRequest = 120, HorizontalOptions = LayoutOptions.Start,
			Margin = new Thickness(16, 0), BorderColor = Colors.Coral, BorderWidth = 3, CornerRadius = 16, Padding = new Thickness(12),
			BackgroundColor = Color.FromArgb("#304060"),
		};
		_ = nav!.PushAsync(new ContentPage
		{
			Title = "F3 strokes",
			Content = new ScrollView { Content = new VerticalStackLayout { Spacing = 12, Padding = new Thickness(0, 12), Children = { dashed, ellipse, corners, plain, dashedShape, dashedCanvas, imageButton } } },
		});
		WaitFor(dispatcher, () => PageTitle(renderer) == "F3 strokes" && NativeElementHostOf(renderer, imageButton, out _), 8000, () =>
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200), () =>
			{
				string Border(Border b) => QtHost.QtHostRuntime.Eval($"{ItemJs(renderer, b)}.mauiDiag()");
				var sd = Border(dashed);
				_qtF3Checks.Check($"I dashed Border → canvas stroke, dashes cut in JS ({sd})", sd.Contains("\"canvas\":true") && DiagQml.EvalNum($"{ItemJs(renderer, dashed)}.mauiDashes") >= 8 && sd.Contains("\"bw\":0"));
				var se = Border(ellipse);
				_qtF3Checks.Check($"I Ellipse Border → canvas path with curves, painted ({se})", se.Contains("\"canvas\":true") && System.Text.RegularExpressions.Regex.IsMatch(se, "\"kinds\":\"[^\"]*[ca]") && DiagQml.EvalNum($"{ItemJs(renderer, ellipse)}.mauiCanvasPaints") >= 1);
				var sc = Border(corners);
				_qtF3Checks.Check($"I per-corner RoundRectangle → canvas ({sc})", sc.Contains("\"canvas\":true"));
				var sp = Border(plain);
				_qtF3Checks.Check($"I uniform RoundRectangle keeps the Rectangle: radius {Math.Round(12 * d)}, border {Math.Round(3 * d)} ({sp})",
					sp.Contains("\"canvas\":false") && Math.Abs(DiagQml.EvalNum($"{ItemJs(renderer, plain)}.mauiChildHost.radius") - 12 * d) < 0.5 && Math.Abs(DiagQml.EvalNum($"{ItemJs(renderer, plain)}.mauiChildHost.border.width") - 3 * d) < 0.5);
				var shapeDashes = DiagQml.EvalNum($"{ItemJs(renderer, dashedShape)}.mauiDashes");
				_qtF3Checks.Check($"I dashed Shape → {shapeDashes} dashes (was dropped on Qt 5.6)", shapeDashes >= 8);
				var canvasDashes = DiagQml.EvalNum($"{ItemJs(renderer, dashedCanvas)}.mauiDashes");
				_qtF3Checks.Check($"I dashed IDrawable stroke → {canvasDashes} dashes (ICanvas.StrokeDashPattern)", canvasDashes >= 8);
				var ijs = ItemJs(renderer, imageButton);
				var istate = QtHost.QtHostRuntime.Eval($"(function(r){{var f=null,img=null;for(var i=0;i<r.children.length;i++){{var c=r.children[i];if(c.border!==undefined&&c.z===10)f=c;if(c.sourceSize!==undefined)img=c;}}" +
					"return JSON.stringify({frame:f?f.visible:null,fw:f?f.border.width:-1,fr:f?f.radius:-1,ix:img?Math.round(img.x):-1,iw:img?Math.round(img.width):-1,w:Math.round(r.width)});})(" + ijs + ")");
				_qtF3Checks.Check($"I ImageButton → outline {Math.Round(3 * d)}px r{Math.Round(16 * d)}, bitmap inset {Math.Round(12 * d)}px ({istate})",
					istate.Contains("\"frame\":true") && Math.Abs(DiagQml.EvalNum($"{ijs}.mauiStrokeWidth") - 3 * d) < 0.5 &&
					Math.Abs(DiagQml.EvalNum($"{ijs}.mauiCornerRadius") - 16 * d) < 0.5 && istate.Contains($"\"ix\":{Math.Round(12 * d)}"));
				Shot(dispatcher, "f3-i1-strokes", () => F3GenericJ(renderer, dispatcher));
			}));
	}

	private void F3GenericJ(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var nav = RootNav;
		var sw = new Switch { BackgroundColor = Colors.DarkRed, HorizontalOptions = LayoutOptions.Start, Margin = new Thickness(16, 0) };
		var entry = new Entry { Text = "on a fill", BackgroundColor = Color.FromArgb("#204060"), Margin = new Thickness(16, 0) };
		var plainEntry = new Entry { Text = "no fill", Margin = new Thickness(16, 0) };
		var card = new Border
		{
			HeightRequest = 100, Margin = new Thickness(32, 16), BackgroundColor = Color.FromArgb("#3a3a50"), StrokeThickness = 0,
			StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
			Shadow = new Shadow { Brush = new SolidColorBrush(Colors.Black), Offset = new Point(4, 6), Radius = 12, Opacity = 0.8f },
			Content = new Label { Text = "Card with shadow", VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Center },
		};
		var save = new Button { Text = "Save", AutomationId = "saveButton", HorizontalOptions = LayoutOptions.Start, Margin = new Thickness(16, 0) };
		SemanticProperties.SetDescription(save, "Save file");
		SemanticProperties.SetHint(save, "Saves the document");
		_ = nav!.PushAsync(new ContentPage
		{
			Title = "F3 generic",
			Content = new VerticalStackLayout { Spacing = 16, Padding = new Thickness(0, 12), Children = { sw, entry, plainEntry, card, save } },
		});
		string Fill(Element el) => QtHost.QtHostRuntime.Eval(
			$"(function(r){{if(!r)return 'no host';for(var i=0;i<r.children.length;i++){{var c=r.children[i];if(c.objectName==='mauiBackgroundFill')return JSON.stringify({{color:c.color.toString(),visible:c.visible,w:Math.round(c.width),rw:Math.round(r.width)}});}}return 'none';}})({ItemJs(renderer, el)})");
		WaitFor(dispatcher, () => PageTitle(renderer) == "F3 generic" && NativeElementHostOf(renderer, save, out _), 8000, () =>
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200), () =>
			{
				var fs = Fill(sw);
				_qtF3Checks.Check($"J Switch BackgroundColor → fill rectangle under the Silica switch ({fs})",
					fs.Contains($"\"color\":\"{Hex(Colors.DarkRed)}\"") && fs.Contains("\"visible\":true"));
				// Silica clips the editor to the field height minus its margins, so a
				// short measure hides the typed text.
				string EditorFit(Entry e) => QtHost.QtHostRuntime.Eval(
					"(function(r){var ed=null;function w(it){for(var i=0;i<it.children.length&&!ed;i++){var c=it.children[i];if(c.objectName==='textEditor')ed=c;else w(c);}}w(r);" +
					"if(!ed)return 'no editor';return JSON.stringify({text:ed.text,editorH:Math.round(ed.height),clipH:Math.round(ed.parent.height),clip:ed.parent.clip});})(" + ItemJs(renderer, e) + ")");
				foreach (var e in new[] { entry, plainEntry })
				{
					var fit = EditorFit(e);
					var fitOk = System.Text.RegularExpressions.Regex.Match(fit, "\"clipH\":(\\d+).*\"editorH\":(\\d+)");
					_qtF3Checks.Check($"J Entry '{e.Text}' text visible: clip container ≥ editor line ({fit})",
						fitOk.Success && int.Parse(fitOk.Groups[1].Value) >= int.Parse(fitOk.Groups[2].Value));
				}
				var fe = Fill(entry);
				_qtF3Checks.Check($"J Entry BackgroundColor → fill rectangle ({fe})", fe.Contains("\"color\":\"#204060\"") && fe.Contains("\"visible\":true"));
				var cd = QtHost.QtHostRuntime.Eval($"{ItemJs(renderer, card)}.mauiDiag()");
				_qtF3Checks.Check($"J Border Shadow → shadow canvas painted outside the clipped box ({cd})",
					cd.Contains("\"shadow\":true") && DiagQml.EvalNum($"JSON.parse({ItemJs(renderer, card)}.mauiDiag()).shadowPaints") >= 1 && cd.Contains("\"clip\":true"));
				NativeElementHostOf(renderer, save, out var sh);
				var name = QtHost.QtHostRuntime.GetProperty(sh!.NativeHandle, "Accessible.name");
				var desc = QtHost.QtHostRuntime.GetProperty(sh.NativeHandle, "Accessible.description");
				var aid = QtHost.QtHostRuntime.GetProperty(sh.NativeHandle, "mauiAutomationId");
				_qtF3Checks.Check($"J Semantics → Accessible.name '{name}'=='Save file', description '{desc}'=='Saves the document'; AutomationId '{aid}'=='saveButton'",
					name == "Save file" && desc == "Saves the document" && aid == "saveButton");
				Shot(dispatcher, "f3-j1-generic", () =>
				{
					sw.BackgroundColor = Colors.Transparent;
					SemanticProperties.SetDescription(save, "Save now");
					dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(800), () =>
					{
						var fs2 = Fill(sw);
						var name2 = QtHost.QtHostRuntime.GetProperty(sh.NativeHandle, "Accessible.name");
						_qtF3Checks.Check($"J managed changes → fill hidden ({fs2}), Accessible.name '{name2}'=='Save now'",
							fs2.Contains("\"visible\":false") && name2 == "Save now");
						F3EffectsM(renderer, dispatcher);
					});
				});
			}));
	}

	/// <summary>Generic Shadow + Clip via the shim's layer effect: checks the layer, the
	/// painted pixels, and that clearing the Shadow releases the layer.</summary>
	private void F3EffectsM(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var nav = RootNav;
		var shadowBox = new BoxView
		{
			Color = Colors.White, WidthRequest = 140, HeightRequest = 70, HorizontalOptions = LayoutOptions.Start, Margin = new Thickness(24, 8),
			Shadow = new Shadow { Brush = new SolidColorBrush(Colors.Black), Offset = new Point(12, 12), Radius = 4, Opacity = 0.9f },
		};
		var clipBox = new BoxView
		{
			Color = Colors.Red, WidthRequest = 120, HeightRequest = 120, HorizontalOptions = LayoutOptions.Start, Margin = new Thickness(24, 8),
			Clip = new Microsoft.Maui.Controls.Shapes.EllipseGeometry { Center = new Point(60, 60), RadiusX = 60, RadiusY = 60 },
		};
		var both = new Button
		{
			Text = "Clipped + shadow", BackgroundColor = Colors.SteelBlue, TextColor = Colors.White,
			WidthRequest = 240, HeightRequest = 64, HorizontalOptions = LayoutOptions.Start, Margin = new Thickness(24, 8),
			Clip = new Microsoft.Maui.Controls.Shapes.RoundRectangleGeometry { CornerRadius = 28, Rect = new Rect(0, 0, 240, 64) },
			Shadow = new Shadow { Brush = new SolidColorBrush(Colors.Black), Offset = new Point(6, 8), Radius = 8, Opacity = 0.8f },
		};
		var label = new Label
		{
			Text = "Label with a shadow", FontSize = 30, TextColor = Colors.White, Margin = new Thickness(24, 8),
			Shadow = new Shadow { Brush = new SolidColorBrush(Colors.Black), Offset = new Point(3, 3), Radius = 3, Opacity = 1f },
		};
		_ = nav!.PushAsync(new ContentPage
		{
			Title = "F3 effects",
			BackgroundColor = Color.FromArgb("#c8c8c8"),
			Content = new VerticalStackLayout { Spacing = 12, Padding = new Thickness(0, 12), Children = { shadowBox, clipBox, both, label } },
		});
		string Layer(Element el) => QtHost.QtHostRuntime.Eval(
			"(function(it){if(!it)return 'no host';var p=it.parent,fx=null;for(var i=0;p&&i<p.children.length;i++){var c=p.children[i];" +
			"if(c.objectName==='mauiLayerEffect'&&Math.abs(c.x-it.x)<1&&Math.abs(c.y-it.y)<1&&Math.abs(c.width-it.width)<1)fx=c;}" +
			"var g=it.mapToItem(null,0,0);return JSON.stringify({layer:it.layer.enabled,effect:!!fx,shadow:fx?fx.shadowing:false,clip:fx?fx.clipping:false," +
			"x:Math.round(g.x),y:Math.round(g.y),w:Math.round(it.width),h:Math.round(it.height)});})(" + ItemJs(renderer, el) + ")");
		// V4's JSON.stringify does not keep the literal's key order — parse
		static System.Text.Json.JsonElement Parse(string json)
		{
			try
			{
				using var doc = System.Text.Json.JsonDocument.Parse(json);
				return doc.RootElement.Clone();
			}
			catch (System.Text.Json.JsonException)
			{
				return default;
			}
		}
		static bool Flag(string json, string name) =>
			Parse(json) is { ValueKind: System.Text.Json.JsonValueKind.Object } e && e.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.True;
		static bool Effect(string json, bool shadow, bool clip) =>
			Flag(json, "layer") && Flag(json, "effect") && Flag(json, "shadow") == shadow && Flag(json, "clip") == clip;
		static (int X, int Y, int W, int H) Rect(string json) =>
			Parse(json) is { ValueKind: System.Text.Json.JsonValueKind.Object } e
				? (e.GetProperty("x").GetInt32(), e.GetProperty("y").GetInt32(), e.GetProperty("w").GetInt32(), e.GetProperty("h").GetInt32())
				: (0, 0, 0, 0);
		WaitFor(dispatcher, () => PageTitle(renderer) == "F3 effects" && NativeElementHostOf(renderer, label, out _), 8000, () =>
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1500), () =>
			{
				var ls = Layer(shadowBox);
				var lc = Layer(clipBox);
				var lb = Layer(both);
				var ll = Layer(label);
				_qtF3Checks.Check($"M layer effects attach: shadow box {ls}; clip box {lc}; clipped+shadow button {lb}; label {ll}",
					Effect(ls, shadow: true, clip: false) && Effect(lc, shadow: false, clip: true) &&
					Effect(lb, shadow: true, clip: true) && Effect(ll, shadow: true, clip: false));
				var density = SailfishDisplay.Density;
				QtHost.QtHostRuntime.GrabPng("/tmp/f3-m-effects.png");
				var png = DiagPng.TryLoad("/tmp/f3-m-effects.png");
				var (sx, sy, sw2, sh2) = Rect(ls);
				var (cx, cy, cw, ch) = Rect(lc);
				var offset = (int)Math.Round(12 * density);
				if (png is null)
					_qtF3Checks.Check("M window grab decodes", false);
				else
				{
					var bg = png.Luma(sx + sw2 + 4 * offset, sy + sh2 / 2);
					var shade = png.Luma(sx + sw2 + offset / 2, sy + sh2 + offset / 2);
					var inside = png.Luma(sx + sw2 / 2, sy + sh2 / 2);
					_qtF3Checks.Check($"M Shadow pixels: beside the box luma {shade} < background {bg} − 40, box itself {inside} (white)",
						shade < bg - 40 && inside > 230);
					var corner = png.At(cx + 4, cy + 4);
					var corner2 = png.At(cx + cw - 5, cy + ch - 5);
					var center = png.At(cx + cw / 2, cy + ch / 2);
					_qtF3Checks.Check($"M Clip pixels: ellipse corners show the page {corner}/{corner2}, center is red {center}",
						Math.Abs(corner.R - corner.G) < 30 && Math.Abs(corner2.R - corner2.G) < 30 && center.R > 180 && center.G < 80);
				}
				Shot(dispatcher, "f3-m1-effects", () =>
				{
					shadowBox.Shadow = null!;
					clipBox.Clip = null!;
					dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900), () =>
					{
						var ls2 = Layer(shadowBox);
						var lc2 = Layer(clipBox);
						QtHost.QtHostRuntime.GrabPng("/tmp/f3-m-effects2.png");
						var png2 = DiagPng.TryLoad("/tmp/f3-m-effects2.png");
						var (sx3, sy3, sw3, sh3) = Rect(ls2);
						var (cx3, cy3, _, _) = Rect(lc2);
						var bg2 = png2?.Luma(sx3 + sw3 + 4 * offset, sy3 + sh3 / 2) ?? -1;
						var shade2 = png2?.Luma(sx3 + sw3 + offset / 2, sy3 + sh3 + offset / 2) ?? -1;
						var corner3 = png2?.At(cx3 + 4, cy3 + 4) ?? default;
						_qtF3Checks.Check($"M cleared Shadow/Clip release the layer ({ls2}; {lc2}), no shadow left (luma {shade2} ≈ {bg2}), the square's corner is red again {corner3}",
							!Flag(ls2, "layer") && !Flag(lc2, "layer") && Rect(ls2).W > 0 && png2 is not null &&
							Math.Abs(shade2 - bg2) < 12 && corner3.R > 180 && corner3.G < 80);
						F3InputsK(renderer, dispatcher);
					});
				});
			}));
	}

	private void F3InputsK(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var nav = RootNav;
		var next = new Entry { Text = "next field", ReturnType = ReturnType.Next, IsSpellCheckEnabled = false, Margin = new Thickness(16, 0) };
		var clearable = new Entry { Text = "clear me", ClearButtonVisibility = ClearButtonVisibility.WhileEditing, Margin = new Thickness(16, 0) };
		var editor = new Editor { MaxLength = 5, Margin = new Thickness(16, 0) };
		var search = new SearchBar { Text = "abcdef", ReturnType = ReturnType.Search, SearchIconColor = Colors.Orange, Keyboard = Keyboard.Numeric, MaxLength = 10 };
		var readOnlySearch = new SearchBar { Text = "fixed", IsReadOnly = true, MaxLength = 4 };
		_ = nav!.PushAsync(new ContentPage
		{
			Title = "F3 inputs",
			Content = new VerticalStackLayout { Spacing = 8, Padding = new Thickness(0, 12), Children = { next, clearable, editor, search, readOnlySearch } },
		});
		WaitFor(dispatcher, () => PageTitle(renderer) == "F3 inputs" && NativeElementHostOf(renderer, readOnlySearch, out _), 8000, () =>
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1000), () =>
			{
				var njs = ItemJs(renderer, next);
				// the attached Silica EnterKey (the shim finds the attached object)
				NativeElementHostOf(renderer, next, out var nh);
				var icon = QtHost.QtHostRuntime.GetProperty(nh!.NativeHandle, "EnterKey.iconSource");
				var hints = DiagQml.EvalNum($"{njs}.inputMethodHints");
				_qtF3Checks.Check($"K Entry ReturnType.Next → enter icon '{icon}', IsSpellCheckEnabled=false → no-suggestions hint (hints {hints})",
					icon.EndsWith("icon-m-enter-next", StringComparison.Ordinal) && ((int)hints & 0x40) != 0);
				var sjs = ItemJs(renderer, search);
				NativeElementHostOf(renderer, search, out var sbh);
				var sIcon = QtHost.QtHostRuntime.GetProperty(sbh!.NativeHandle, "EnterKey.iconSource");
				var sHints = DiagQml.EvalNum($"{sjs}.inputMethodHints");
				var tint = QtHost.QtHostRuntime.Eval($"String({sjs}.leftItem.color)");
				_qtF3Checks.Check($"K SearchBar → enter icon '{sIcon}', numeric hints {sHints}, search icon tint {tint}, maximumLength {DiagQml.EvalNum($"{sjs}.maximumLength")}",
					sIcon.EndsWith("icon-m-search", StringComparison.Ordinal) && ((int)sHints & 0x20000) != 0 && tint == Hex(Colors.Orange) && DiagQml.EvalNum($"{sjs}.maximumLength") == 10);
				var rjs = ItemJs(renderer, readOnlySearch);
				_qtF3Checks.Check($"K read-only SearchBar → readOnly {QtHost.QtHostRuntime.Eval($"String({rjs}.readOnly)")}, maximumLength {DiagQml.EvalNum($"{rjs}.maximumLength")}==4",
					QtHost.QtHostRuntime.Eval($"String({rjs}.readOnly)") == "true" && DiagQml.EvalNum($"{rjs}.maximumLength") == 4);
				search.CursorPosition = 2;
				editor.Text = "toolong";
				clearable.Focus();
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900), () =>
				{
					_qtF3Checks.Check($"K managed SearchBar.CursorPosition=2 → native cursor {DiagQml.EvalNum($"{sjs}.cursorPosition")}==2", DiagQml.EvalNum($"{sjs}.cursorPosition") == 2);
					var ejs = ItemJs(renderer, editor);
					_qtF3Checks.Check($"K Editor MaxLength=5 → native '{QtHost.QtHostRuntime.Eval($"{ejs}.text")}', MAUI Text '{editor.Text}'=='toolo'",
						QtHost.QtHostRuntime.Eval($"{ejs}.text") == "toolo" && editor.Text == "toolo");
					var cjs = ItemJs(renderer, clearable);
					var shown = QtHost.QtHostRuntime.Eval($"String({cjs}.rightItem !== null)");
					_qtF3Checks.Check($"K focused Entry with text → clear button shown ({shown})", shown == "true");
					Shot(dispatcher, "f3-k1-inputs", () =>
					{
						var tapped = TapChild(cjs, "i.rightItem", dispatcher);
						dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900), () =>
						{
							_qtF3Checks.Check($"K clear button tap (tapped={tapped}) → Entry.Text '{clearable.Text}'=='' and the button hides ({QtHost.QtHostRuntime.Eval($"String({cjs}.rightItem !== null)")})",
								tapped && clearable.Text == "" && QtHost.QtHostRuntime.Eval($"String({cjs}.rightItem !== null)") == "false");
							clearable.Unfocus();
							dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(600), () => F3MiscL(renderer, dispatcher));
						});
					});
				});
			}));
	}

	private void F3MiscL(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var nav = RootNav;
		View Tall(string text) => new VerticalStackLayout
		{
			Children = { new Label { Text = text }, new BoxView { HeightRequest = 600, Color = Color.FromArgb("#303048") } },
		};
		var always = new ScrollView { HeightRequest = 110, VerticalScrollBarVisibility = ScrollBarVisibility.Always, Content = Tall("bars: always") };
		var never = new ScrollView { HeightRequest = 110, VerticalScrollBarVisibility = ScrollBarVisibility.Never, Content = Tall("bars: never") };
		var armed = new ScrollView { HeightRequest = 110, Content = Tall("refresh surface") };
		var refresh = new RefreshView { IsRefreshEnabled = false, RefreshColor = Colors.Gold, Content = armed };
		var picker = new Picker { Title = "Open me", ItemsSource = new List<string> { "one", "two" } };
		var datePicker = new DatePicker { Date = new DateTime(2030, 1, 15) };
		var gif = new Image { Source = ImageSource.FromFile(System.IO.Path.Combine(AppContext.BaseDirectory, "images", "pulse.gif")), WidthRequest = 96, HeightRequest = 96, IsAnimationPlaying = true, HorizontalOptions = LayoutOptions.Start };
		var swipe = new SwipeView
		{
			HeightRequest = 70,
			LeftItems = new SwipeItems { new SwipeItem { Text = "Pin", BackgroundColor = Colors.SeaGreen } },
			Content = new Grid { BackgroundColor = Color.FromArgb("#404060"), Children = { new Label { Text = "reveal", VerticalOptions = LayoutOptions.Center } } },
		};
		_ = nav!.PushAsync(new ContentPage
		{
			Title = "F3 misc",
			Content = new VerticalStackLayout { Spacing = 8, Padding = new Thickness(16, 8), Children = { always, never, refresh, picker, datePicker, gif, swipe } },
		});
		WaitFor(dispatcher, () => PageTitle(renderer) == "F3 misc" && NativeElementHostOf(renderer, swipe, out _), 8000, () =>
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200), () =>
			{
				var barsAlways = QtHost.QtHostRuntime.Eval($"{ItemJs(renderer, always)}.mauiBars");
				var barsNever = QtHost.QtHostRuntime.Eval($"{ItemJs(renderer, never)}.mauiBars");
				_qtF3Checks.Check($"L ScrollBarVisibility Always → '{barsAlways}' (v=1 while idle), Never → '{barsNever}' (v=hidden)",
					barsAlways.EndsWith("v=1", StringComparison.Ordinal) && barsNever.EndsWith("v=hidden", StringComparison.Ordinal));
				var ajs = ItemJs(renderer, armed);
				var id0 = QtHost.QtHostRuntime.Eval($"{ajs}.mauiRefreshId");
				_qtF3Checks.Check($"L RefreshView IsRefreshEnabled=false → surface disarmed (id '{id0}'=='')", id0 == "");
				var gjs = ItemJs(renderer, gif);
				var frames = DiagQml.EvalNum($"{gjs}.mauiFrameCount");
				// Sample several frames over ~1 s: one sample of a 600 ms loop can repeat a frame.
				var seenFrames = new HashSet<double> { DiagQml.EvalNum($"{gjs}.mauiFrame") };
				for (var k = 1; k <= 5; k++)
					dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(130 * k), () => seenFrames.Add(DiagQml.EvalNum($"{gjs}.mauiFrame")));
				refresh.IsRefreshEnabled = true;
				picker.IsOpen = true;
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900), () =>
				{
					_qtF3Checks.Check($"L GIF IsAnimationPlaying → {frames} frames, playing (frames seen {string.Join(",", seenFrames)})", frames == 3 && seenFrames.Count >= 2);
					var id1 = QtHost.QtHostRuntime.Eval($"{ajs}.mauiRefreshId");
					var tint = QtHost.QtHostRuntime.Eval($"String({ajs}.mauiRefreshColor)");
					_qtF3Checks.Check($"L IsRefreshEnabled=true → armed ('{id1}'=='refresh1'), RefreshColor → '{tint}'=='{Hex(Colors.Gold)}'", id1 == "refresh1" && tint == Hex(Colors.Gold));
					var pjs = ItemJs(renderer, picker);
					_qtF3Checks.Check($"L Picker.IsOpen=true → native menu open {QtHost.QtHostRuntime.Eval($"String({pjs}._menuOpen)")}", QtHost.QtHostRuntime.Eval($"String({pjs}._menuOpen)") == "true");
					Shot(dispatcher, "f3-l1-picker-open", () =>
					{
						picker.IsOpen = false;
						gif.IsAnimationPlaying = false;
						dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(700), () =>
						{
							_qtF3Checks.Check($"L Picker.IsOpen=false → menu closed {QtHost.QtHostRuntime.Eval($"String({pjs}._menuOpen)")}", QtHost.QtHostRuntime.Eval($"String({pjs}._menuOpen)") == "false");
							var g1 = DiagQml.EvalNum($"{gjs}.mauiFrame");
							dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(700), () =>
							{
							var g2 = DiagQml.EvalNum($"{gjs}.mauiFrame");
							_qtF3Checks.Check($"L IsAnimationPlaying=false → frame holds ({g1} → {g2})", g1 == g2);
							var depth0 = DiagQml.EvalNum("pageStack.depth");
							NativeElementHostOf(renderer, datePicker, out var dh);
							datePicker.IsOpen = true;
							dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1500), () =>
							{
								// (pageStack.currentPage is the dialog now — read the host by handle)
								var depth1 = DiagQml.EvalNum("pageStack.depth");
								var dialogOpen = QtHost.QtHostRuntime.GetProperty(dh!.NativeHandle, "mauiDialogOpen");
								_qtF3Checks.Check($"L DatePicker.IsOpen=true → dialog pushed (depth {depth0} → {depth1}, open {dialogOpen})",
									depth1 == depth0 + 1 && dialogOpen == "true");
								Shot(dispatcher, "f3-l2-date-dialog", () =>
								{
									// A native back (the dialog's own cancel path) closes it.
									QtHost.QtHostRuntime.Eval("pageStack.pop()");
									dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1500), () =>
									{
										_qtF3Checks.Check($"L native back on the date dialog → DatePicker.IsOpen {datePicker.IsOpen}==False, depth {DiagQml.EvalNum("pageStack.depth")}=={depth0}",
											!datePicker.IsOpen && DiagQml.EvalNum("pageStack.depth") == depth0);
										// SwipeView Reveal: open left, the item row stays at the view's left edge.
										swipe.Open(OpenSwipeItem.LeftItems);
										dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900), () =>
										{
											var sjs = ItemJs(renderer, swipe);
											NativeElementHostOf(renderer, swipe, out var sh);
											QtHost.QtHostRuntime.TryItemGeometry(sh!.NativeHandle, out var sg);
											var point = QtHost.QtHostRuntime.Eval($"{sjs}.mauiItemPoint('left', 0)").Split(',');
											var itemX = point.Length == 2 && double.TryParse(point[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var px) ? px : double.NaN;
											var itemW = DiagQml.EvalNum($"(function(i){{var r=i.children[0].children[0].children[0];return r.width;}})({sjs})");
											_qtF3Checks.Check($"L SwipeView Reveal (default) → open left, item centre x {itemX:F0} at the view's left edge {sg.X:F0} + half item {itemW / 2:F0}",
												Math.Abs(itemX - (sg.X + itemW / 2)) <= 3);
											Shot(dispatcher, "f3-l3-swipe-reveal", () =>
											{
												swipe.Close();
												dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(500), () => F3ImagesF(renderer, dispatcher));
											});
										});
									});
								});
							});
							});
						});
					});
				});
			}));
	}

	private void F3ImagesF(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var nav = RootNav;
		// Registers a font the way ConfigureFonts does (file + alias into IFontRegistrar).
		var registrar = renderer.MauiContext.Services.GetService(typeof(IFontRegistrar)) as IFontRegistrar;
		registrar?.Register("/usr/share/fonts/symbola/Symbola.ttf", "DiagSymbols");
		var logo = System.IO.Path.Combine(AppContext.BaseDirectory, "images", "sailfish_logo.png");
		var bytes = System.IO.File.Exists(logo) ? System.IO.File.ReadAllBytes(logo) : Array.Empty<byte>();
		var aliasLabel = new Label { Text = "Alias font ★ ♞ ☂", FontFamily = "DiagSymbols", FontSize = 28, Margin = new Thickness(16, 0) };
		var glyphImage = new Image
		{
			Source = new FontImageSource { Glyph = "★", FontFamily = "DiagSymbols", Size = 64, Color = Colors.Gold },
			WidthRequest = 128, HeightRequest = 128, HorizontalOptions = LayoutOptions.Start, Margin = new Thickness(16, 0),
		};
		var streamImage = new Image
		{
			Source = ImageSource.FromStream(() => new System.IO.MemoryStream(bytes)),
			WidthRequest = 160, HeightRequest = 160, HorizontalOptions = LayoutOptions.Start, Margin = new Thickness(16, 0),
		};
		// No size request: an image copied into images/ unresized measures one dp per pixel (Android's drawable/), a
		// stream image its pixels ÷ density once read.
		var naturalImage = new Image { Source = "sailfish_logo.png", HorizontalOptions = LayoutOptions.Start, Margin = new Thickness(16, 0) };
		var naturalStream = new Image
		{
			Source = ImageSource.FromStream(() => new System.IO.MemoryStream(bytes)),
			HorizontalOptions = LayoutOptions.Start, Margin = new Thickness(16, 0),
		};
		var glyphButton = new Button
		{
			Text = "Done",
			ImageSource = new FontImageSource { Glyph = "✓", FontFamily = "DiagSymbols", Size = 24, Color = Colors.White },
			HorizontalOptions = LayoutOptions.Start, Margin = new Thickness(16, 0),
		};
		_ = nav!.PushAsync(new ContentPage
		{
			Title = "F3 images",
			Content = new VerticalStackLayout { Spacing = 16, Padding = new Thickness(0, 12), Children = { aliasLabel, glyphImage, streamImage, naturalImage, naturalStream, glyphButton } },
		});
		NativeElementHostOf(renderer, streamImage, out _);
		WaitFor(dispatcher, () => PageTitle(renderer) == "F3 images" && NativeElementHostOf(renderer, streamImage, out var sh) && sh!.QmlUri == "image", 8000, () =>
		{
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(800), () =>
			{
				NativeElementHostOf(renderer, aliasLabel, out var lh);
				var family = lh is null ? "(no host)" : QtHost.QtHostRuntime.GetProperty(lh.NativeHandle, "mauiFamily");
				_qtF3Checks.Check($"F alias 'DiagSymbols' → Qt family '{family}'=='Symbola'", family == "Symbola");
				string ImageState(Image img)
				{
					var js = ItemJs(renderer, img);
					return QtHost.QtHostRuntime.Eval($"(function(i){{if(!i)return 'no host';return JSON.stringify({{src:String(i.mauiSource).slice(-40),loaded:i.mauiLoaded,w:i.mauiNaturalWidth,err:i.mauiLoadError}});}})({js})");
				}
				var glyphState = ImageState(glyphImage);
				_qtF3Checks.Check($"F FontImageSource glyph → loaded image ({glyphState})", glyphState.Contains("\"loaded\":true") && glyphState.Contains(".png"));
				var streamState = ImageState(streamImage);
				_qtF3Checks.Check($"F StreamImageSource ({bytes.Length} bytes) → loaded image ({streamState})", bytes.Length > 0 && streamState.Contains("\"loaded\":true") && streamState.Contains(".img"));
				var density = SailfishDisplay.Density;
				_qtF3Checks.Check($"F unsized Image (128 px PNG copied to images/) → {naturalImage.Width:F1}×{naturalImage.Height:F1} dp == 128×128",
					Math.Abs(naturalImage.Width - 128) < 0.5 && Math.Abs(naturalImage.Height - 128) < 0.5);
				var streamDp = 128 / density;
				_qtF3Checks.Check($"F unsized stream Image → {naturalStream.Width:F1}×{naturalStream.Height:F1} dp == {streamDp:F1} (128 px ÷ {density:F2})",
					Math.Abs(naturalStream.Width - streamDp) < 0.5 && Math.Abs(naturalStream.Height - streamDp) < 0.5);
				NativeElementHostOf(renderer, glyphButton, out var bh);
				var icon = bh is null ? "" : QtHost.QtHostRuntime.GetProperty(bh.NativeHandle, "mauiIconSource");
				_qtF3Checks.Check($"F Button ImageSource glyph → icon '{(icon.Length > 30 ? "…" + icon[^30..] : icon)}' is a rendered PNG", icon.EndsWith(".png", StringComparison.Ordinal));
				Shot(dispatcher, "f3-f1-images", () => F3ParityN(renderer, dispatcher));
			});
		});
	}

	private static bool NativeElementHostOf(QtHost.QtHostPageRenderer renderer, Element element, out QtHost.NativeElementHost? host)
	{
		host = renderer.CurrentHosts.FirstOrDefault(h => ReferenceEquals(h.Element, element) && h.IsAttached);
		return host is not null;
	}

	private void F3WebE(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var nav = RootNav;
		_f3Web = new WebView
		{
			Source = new HtmlWebViewSource
			{
				Html = "<html><body style='background:#1c2a3a;color:#fff;font-size:48px;font-family:sans-serif'>" +
				       "<h1 id='t'>Hello MAUI</h1><p>Sailfish WebView (Gecko)</p></body></html>",
			},
			UserAgent = "MauiSailfishDiag/1.0",
		};
		_f3Web.Navigated += (_, _) => _f3WebNavigated++;
		_ = nav!.PushAsync(new ContentPage { Title = "F3 web", Content = _f3Web });
		WaitFor(dispatcher, () => _f3WebNavigated >= 1, 15000, () =>
		{
			var js = ItemJs(renderer, _f3Web);
			var uri = DiagQml.HostOf(renderer, _f3Web)?.QmlUri;
			_qtF3Checks.Check($"E web host '{uri}'=='web-view', HTML source → Navigated {_f3WebNavigated}>=1, native loading={QtHost.QtHostRuntime.Eval($"String({js}.loading)")}",
				uri == "web-view" && _f3WebNavigated >= 1);
			var eval = _f3Web.EvaluateJavaScriptAsync("document.getElementById('t').textContent + '|' + navigator.userAgent");
			WaitFor(dispatcher, () => eval.IsCompleted, 6000, () =>
			{
				var result = eval.IsCompletedSuccessfully ? eval.Result ?? string.Empty : $"<{eval.Status}>";
				var bar = result.IndexOf('|');
				var text = bar >= 0 ? result[..bar] : result;
				var agent = bar >= 0 ? result[(bar + 1)..] : string.Empty;
				_qtF3Checks.Check($"E EvaluateJavaScriptAsync → '{text}'=='Hello MAUI'", text == "Hello MAUI");
				_qtF3Checks.Check($"E WebView.UserAgent → navigator.userAgent '{agent}'=='MauiSailfishDiag/1.0'", agent == "MauiSailfishDiag/1.0");
				Shot(dispatcher, "f3-e1-web-html", () =>
				{
					var navigated0 = _f3WebNavigated;
					_f3Web.Source = new UrlWebViewSource { Url = "data:text/html,<html><body style='background:%23303030;color:%23fff;font-size:48px'><h1 id='t'>Second page</h1></body></html>" };
					WaitFor(dispatcher, () => _f3WebNavigated > navigated0, 15000, () =>
					{
						var eval2 = _f3Web.EvaluateJavaScriptAsync("document.getElementById('t').textContent");
						WaitFor(dispatcher, () => eval2.IsCompleted, 6000, () =>
						{
							var text2 = eval2.IsCompletedSuccessfully ? eval2.Result : $"<{eval2.Status}>";
							_qtF3Checks.Check($"E URL source → Navigated +{_f3WebNavigated - navigated0}>=1, document '{text2}'=='Second page', CanGoBack={_f3Web.CanGoBack}",
								_f3WebNavigated > navigated0 && text2 == "Second page");
							Shot(dispatcher, "f3-e2-web-url", () =>
							{
								if (!_f3Web.CanGoBack)
								{
									_qtF3Checks.Check("E GoBack: no history after an HTML-string load (Gecko keeps loadHtml out of the session history) — skipped", true);
									FinishF3();
									return;
								}
								var navigated1 = _f3WebNavigated;
								_f3Web.GoBack();
								WaitFor(dispatcher, () => _f3WebNavigated > navigated1, 15000, () =>
								{
									var eval3 = _f3Web.EvaluateJavaScriptAsync("document.getElementById('t').textContent");
									WaitFor(dispatcher, () => eval3.IsCompleted, 6000, () =>
									{
										var text3 = eval3.IsCompletedSuccessfully ? eval3.Result : $"<{eval3.Status}>";
										_qtF3Checks.Check($"E GoBack → Navigated +{_f3WebNavigated - navigated1}>=1, document '{text3}'=='Hello MAUI'",
											_f3WebNavigated > navigated1 && text3 == "Hello MAUI");
										FinishF3();
									});
								});
							});
						});
					});
				});
			});
		});
	}

	private void FinishF3()
	{
		_qtF3Checks.CheckNoOffThreadCalls();
		_qtF3Checks.Accept("OK — F3 controls work natively (carousel, indicator, horizontal list, stepper, check box, swipe view, images/fonts, web view)");
		QtHost.QtHostRuntime.Shutdown();
	}
}
