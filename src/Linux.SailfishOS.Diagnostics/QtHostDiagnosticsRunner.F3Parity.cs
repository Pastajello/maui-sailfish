using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// F3 part N: the last mapper-parity keys read back from native state — accessibility (HeadingLevel,
/// IsInAccessibleTree, ExcludedWithChildren), Label TextType/TextTransform, VerticalTextAlignment of the text
/// fields, Picker text alignment, Slider ThumbImageSource, Page BackgroundImageSource and FlowDirection RTL on
/// leaf controls — then each managed change back.
/// </summary>
internal sealed partial class QtHostDiagnosticsRunner
{
	private void F3ParityN(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var nav = RootNav;
		var heading = new Label { Text = "Parity", FontSize = 26, Margin = new Thickness(16, 0) };
		SemanticProperties.SetHeadingLevel(heading, SemanticHeadingLevel.Level1);
		var hidden = new Button { Text = "Not in the tree", HorizontalOptions = LayoutOptions.Start, Margin = new Thickness(16, 0) };
		AutomationProperties.SetIsInAccessibleTree(hidden, false);
		var excludedChild = new Label { Text = "Excluded with its parent" };
		var excluded = new VerticalStackLayout { Margin = new Thickness(16, 0), Children = { excludedChild } };
		AutomationProperties.SetExcludedWithChildren(excluded, true);
		var html = new Label { Text = "<b>Bold</b> and <i>italic</i>", TextType = TextType.Html, Margin = new Thickness(16, 0) };
		var upper = new Label { Text = "shout", TextTransform = TextTransform.Uppercase, Margin = new Thickness(16, 0) };
		var entry = new Entry { Text = "bottom", HeightRequest = 120, VerticalTextAlignment = TextAlignment.End, Margin = new Thickness(16, 0) };
		var search = new SearchBar { Text = "top", HeightRequest = 140, VerticalTextAlignment = TextAlignment.Start };
		var picker = new Picker
		{
			Title = "Pick", ItemsSource = new List<string> { "One", "Two" }, SelectedIndex = 0, HeightRequest = 120,
			HorizontalTextAlignment = TextAlignment.End, VerticalTextAlignment = TextAlignment.End,
		};
		var thumbed = new Slider
		{
			Minimum = 0, Maximum = 1, Value = 0.5,
			ThumbImageSource = new FontImageSource { Glyph = "★", Size = 24, Color = Colors.Gold },
		};
		var page = new ContentPage
		{
			Title = "F3 parity",
			BackgroundImageSource = new FontImageSource { Glyph = "◆", Size = 64, Color = Color.FromArgb("#40306080") },
			Content = new VerticalStackLayout
			{
				Spacing = 10, Padding = new Thickness(0, 8),
				Children = { heading, hidden, excluded, html, upper, entry, search, picker, thumbed },
			},
		};
		_ = nav!.PushAsync(page);

		string Js(Element el) => ItemJs(renderer, el);
		string Prop(Element el, string name) =>
			NativeElementHostOf(renderer, el, out var h) ? QtHost.QtHostRuntime.GetProperty(h!.NativeHandle, name) : "(no host)";
		double Num(string js) => DiagQml.EvalNum(js);

		WaitFor(dispatcher, () => PageTitle(renderer) == "F3 parity" && NativeElementHostOf(renderer, thumbed, out _) && NativeElementHostOf(renderer, excludedChild, out _), 8000, () =>
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200), () =>
			{
				// Accessibility.
				var role = Prop(heading, "Accessible.role");
				_qtF3Checks.Check($"N HeadingLevel → Accessible.role '{role}'=='1044' (QAccessible::Heading)", role == "1044");
				var ignored = Prop(hidden, "Accessible.ignored");
				_qtF3Checks.Check($"N IsInAccessibleTree=false → Accessible.ignored '{ignored}'=='true'", ignored == "true");
				var childIgnored = Prop(excludedChild, "Accessible.ignored");
				_qtF3Checks.Check($"N ExcludedWithChildren on the parent → child Accessible.ignored '{childIgnored}'=='true'", childIgnored == "true");

				// Label text.
				var htmlState = QtHost.QtHostRuntime.Eval($"(function(l){{return l?JSON.stringify({{fmt:l.textFormat,text:l.text}}):'no host';}})({Js(html)})");
				_qtF3Checks.Check($"N Label TextType=Html → RichText with the app's markup ({htmlState})",
					htmlState.Contains("\"fmt\":1") && htmlState.Contains("<b>Bold</b>"));
				var upperText = Prop(upper, "text");
				_qtF3Checks.Check($"N Label TextTransform=Uppercase → text '{upperText}'=='SHOUT'", upperText == "SHOUT");

				// Vertical text alignment: where the editor sits inside the taller field.
				string EditorBox(Element el) => QtHost.QtHostRuntime.Eval(
					$"(function(r){{if(!r||!r._editor)return 'no editor';var p=r._editor.mapToItem(r,0,0);return JSON.stringify({{y:Math.round(p.y),h:Math.round(r._editor.height),H:Math.round(r.height),implicit:Math.round(r.implicitHeight),top:Math.round(r.textTopMargin)}});}})({Js(el)})");
				double Field(string box, string key) =>
					System.Text.RegularExpressions.Regex.Match(box, $"\"{key}\":(-?\\d+)") is { Success: true } m ? double.Parse(m.Groups[1].Value) : double.NaN;
				// A TextField keeps its label and underline below the text, so End puts the whole field content on the
				// bottom edge: the grown top margin makes the implicit height meet the host height.
				var entryBox = EditorBox(entry);
				_qtF3Checks.Check($"N Entry VerticalTextAlignment=End → field content on the bottom edge ({entryBox})",
					Math.Abs(Field(entryBox, "implicit") - Field(entryBox, "H")) <= 2 && Field(entryBox, "top") > Field(entryBox, "H") / 4);
				var searchBox = EditorBox(search);
				_qtF3Checks.Check($"N SearchBar VerticalTextAlignment=Start → editor in the upper half ({searchBox})",
					Field(searchBox, "y") + Field(searchBox, "h") / 2 < Field(searchBox, "H") / 2);
				var pickerState = QtHost.QtHostRuntime.Eval(
					$"(function(p){{if(!p||!p.__valueLabel)return 'no labels';var v=p.__valueLabel.mapToItem(p,0,0);return JSON.stringify({{x:Math.round(v.x),w:Math.round(p.__valueLabel.width),y:Math.round(v.y),W:Math.round(p.width),H:Math.round(p.height)}});}})({Js(picker)})");
				_qtF3Checks.Check($"N Picker HorizontalTextAlignment=End, VerticalTextAlignment=End → value at the lower right ({pickerState})",
					Field(pickerState, "w") > 0 && Field(pickerState, "x") + Field(pickerState, "w") <= Field(pickerState, "W") &&
					Field(pickerState, "x") + Field(pickerState, "w") / 2 > Field(pickerState, "W") / 2 &&
					Field(pickerState, "y") > Field(pickerState, "H") / 2);

				// Slider thumb image replaces the Silica handle, centred on it.
				var thumb = QtHost.QtHostRuntime.Eval(
					$"(function(s){{if(!s)return 'no host';var t=null;for(var i=0;i<s.children.length;i++)if(s.children[i].objectName==='mauiThumbImage')t=s.children[i];if(!t)return 'no image';" +
					"return JSON.stringify({handle:s.handleVisible,status:t.status,visible:t.visible,dx:Math.round(t.x+t.width/2-(s._highlightX+s._highlightItem.width/2))});})(" + Js(thumbed) + ")");
				_qtF3Checks.Check($"N Slider ThumbImageSource → image ready over the hidden handle ({thumb})",
					thumb.Contains("\"handle\":false") && thumb.Contains("\"status\":1") && thumb.Contains("\"visible\":true") && thumb.Contains("\"dx\":0"));

				// Page background image under the content.
				var bg = QtHost.QtHostRuntime.Eval("JSON.parse(pageStack.currentPage.__diagDump()).bgImage");
				_qtF3Checks.Check($"N Page BackgroundImageSource → background image status '{bg}'=='1' (Ready)", bg == "1");

				Shot(dispatcher, "f3-n1-parity", () =>
				{
					SemanticProperties.SetHeadingLevel(heading, SemanticHeadingLevel.None);
					AutomationProperties.SetIsInAccessibleTree(hidden, true);
					AutomationProperties.SetExcludedWithChildren(excluded, false);
					upper.TextTransform = TextTransform.None;
					thumbed.ThumbImageSource = null;
					dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900), () =>
					{
						var role2 = Prop(heading, "Accessible.role");
						var ignored2 = Prop(hidden, "Accessible.ignored");
						var child2 = Prop(excludedChild, "Accessible.ignored");
						var text2 = Prop(upper, "text");
						var handle2 = Prop(thumbed, "handleVisible");
						_qtF3Checks.Check($"N managed changes back → role '{role2}'!='1044', ignored '{ignored2}'/'{child2}'=='false', text '{text2}'=='shout', handleVisible '{handle2}'=='true'",
							role2 != "1044" && role2 != "(no host)" && ignored2 == "false" && child2 == "false" && text2 == "shout" && handle2 == "true");
						F3RtlN(renderer, dispatcher);
					});
				});
			}));
	}

	/// <summary>FlowDirection RTL: leaf controls mirror natively, Slider/ProgressBar grow from the right, and
	/// MAUI hosts below a mirrored control stop the inheritance.</summary>
	private void F3RtlN(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		var nav = RootNav;
		var slider = new Slider { Minimum = 0, Maximum = 1, Value = 0.2 };
		var progress = new ProgressBar { Progress = 0.2 };
		var label = new Label { Text = "Start-aligned in RTL" };
		var swipeLabel = new Label { Text = "Inside a mirrored SwipeView", VerticalOptions = LayoutOptions.Center };
		var swipe = new SwipeView { HeightRequest = 70, Content = new Grid { BackgroundColor = Color.FromArgb("#303048"), Children = { swipeLabel } } };
		var stepper = new Stepper { Minimum = 0, Maximum = 5, Value = 2, HorizontalOptions = LayoutOptions.Start };
		var rtl = new VerticalStackLayout
		{
			FlowDirection = FlowDirection.RightToLeft, Spacing = 14, Padding = new Thickness(16, 8),
			Children = { label, slider, progress, stepper, swipe },
		};
		_ = nav!.PushAsync(new ContentPage { Title = "F3 rtl", Content = rtl });

		string Prop(Element el, string name) =>
			NativeElementHostOf(renderer, el, out var h) ? QtHost.QtHostRuntime.GetProperty(h!.NativeHandle, name) : "(no host)";
		// Scene x of the filled end relative to the control's centre: > 0 means the value runs from the right.
		double FillEnd(Element el, string item) => DiagQml.EvalNum(
			$"(function(c){{if(!c)return NaN;var it={item};var p=it.mapToItem(null,it.width/2,0).x;var m=c.mapToItem(null,c.width/2,0).x;return p-m;}})({ItemJs(renderer, el)})");

		WaitFor(dispatcher, () => PageTitle(renderer) == "F3 rtl" && NativeElementHostOf(renderer, swipeLabel, out _), 8000, () =>
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200), () =>
			{
				var sliderMirror = Prop(slider, "LayoutMirroring.enabled");
				var sliderHandle = FillEnd(slider, "c._highlightItem");
				_qtF3Checks.Check($"N RTL Slider → LayoutMirroring '{sliderMirror}'=='true', handle at 20% sits right of centre ({sliderHandle:F0} px > 0)",
					sliderMirror == "true" && sliderHandle > 0);
				var progressFill = FillEnd(progress, "c.__glass[1]");
				_qtF3Checks.Check($"N RTL ProgressBar → the 20% fill sits right of centre ({progressFill:F0} px > 0)", progressFill > 0);
				var stepperMirror = Prop(stepper, "LayoutMirroring.enabled");
				var labelMirror = Prop(label, "LayoutMirroring.enabled");
				var innerMirror = Prop(swipeLabel, "LayoutMirroring.enabled");
				_qtF3Checks.Check($"N RTL leaf Stepper mirrored '{stepperMirror}'=='true'; Label '{labelMirror}' and a Label inside the mirrored SwipeView '{innerMirror}' stay 'false'",
					stepperMirror == "true" && labelMirror == "false" && innerMirror == "false");
				Shot(dispatcher, "f3-n2-rtl", () =>
				{
					rtl.FlowDirection = FlowDirection.LeftToRight;
					dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900), () =>
					{
						var sliderMirror2 = Prop(slider, "LayoutMirroring.enabled");
						var sliderHandle2 = FillEnd(slider, "c._highlightItem");
						_qtF3Checks.Check($"N back to LTR → Slider LayoutMirroring '{sliderMirror2}'=='false', handle left of centre ({sliderHandle2:F0} px < 0)",
							sliderMirror2 == "false" && sliderHandle2 < 0);
						F3LibraryO(renderer, dispatcher);
					});
				});
			}));
	}

	/// <summary>A library control (docs/custom-controls.md): its handler, its runtime-registered adapter.</summary>
	private sealed class DiagRatingView : View
	{
		public static readonly BindableProperty ValueProperty =
			BindableProperty.Create(nameof(Value), typeof(int), typeof(DiagRatingView), 0);

		public int Value
		{
			get => (int)GetValue(ValueProperty);
			set => SetValue(ValueProperty, value);
		}
	}

	private sealed class DiagRatingHandler : Handlers.SailfishSnapshotHandler
	{
		public DiagRatingHandler()
			: base(new[] { nameof(DiagRatingView.Value) }, (_, w, _) => new Size(Math.Min(w, 400), 80))
		{
		}

		protected override string? AdapterUri => "diag-rating";

		protected override Dictionary<string, object?>? Snapshot(IView view) =>
			view is DiagRatingView rating ? new Dictionary<string, object?> { ["value"] = rating.Value } : null;

		protected override void OnAdapterEvent(string name, System.Text.Json.JsonElement payload)
		{
			if (name == "rating-changed" && VirtualView is DiagRatingView rating)
				rating.Value = payload.GetProperty("value").GetInt32();
		}
	}

	/// <summary>
	/// F3 part O: a library control on its own QML adapter — registered at runtime, loaded from the app's qml/
	/// directory, updated through its handler's mapper, and its adapter event written back through the handler.
	/// </summary>
	private void F3LibraryO(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		QtHost.QtHostAdapters.Register("diag-rating", "diag/DiagRating.qml");
		var rating = new DiagRatingView { Value = 3, Margin = new Thickness(16, 0) };
		// An app registers the handler with AddHandler (covered on the host); the leg attaches it the same way the
		// factory would, since the diagnostics cannot add registrations to a built app.
		var handler = new DiagRatingHandler();
		handler.SetMauiContext(renderer.MauiContext);
		handler.SetVirtualView(rating);
		_ = RootNav!.PushAsync(new ContentPage
		{
			Title = "F3 library",
			Content = new VerticalStackLayout { Padding = new Thickness(0, 16), Children = { new Label { Text = "Library control", Margin = new Thickness(16, 0) }, rating } },
		});

		string Prop(string name) =>
			NativeElementHostOf(renderer, rating, out var h) ? QtHost.QtHostRuntime.GetProperty(h!.NativeHandle, name) : "(no host)";

		WaitFor(dispatcher, () => PageTitle(renderer) == "F3 library" && NativeElementHostOf(renderer, rating, out _), 8000, () =>
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900), () =>
			{
				NativeElementHostOf(renderer, rating, out var host);
				var witness = Prop("diagAdapter");
				_qtF3Checks.Check($"O library adapter: host uri '{host?.QmlUri}'=='diag-rating', the registered QML loaded (diagAdapter '{witness}'=='rating'), handler PlatformView is that host",
					host?.QmlUri == "diag-rating" && witness == "rating" && ReferenceEquals(((IElementHandler)handler).PlatformView, host));
				var initial = Prop("value");
				_qtF3Checks.Check($"O create state from the handler snapshot: value '{initial}'=='3'", initial == "3");
				rating.Value = 4;
				var pushed = Prop("value");
				_qtF3Checks.Check($"O Value=4 through the handler mapper → value '{pushed}'=='4'", pushed == "4");
				Shot(dispatcher, "f3-o-library", () =>
				{
					QtHost.QtHostRuntime.Eval($"(function(r){{if(r)r.diagRate(5);}})({ItemJs(renderer, rating)})");
					dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(600), () =>
					{
						_qtF3Checks.Check($"O adapter event rating-changed → handler OnAdapterEvent → Value {rating.Value}==5, pushed back as '{Prop("value")}'",
							rating.Value == 5 && Prop("value") == "5");
						F3WebE(renderer, dispatcher);
					});
				});
			}));
	}
}
