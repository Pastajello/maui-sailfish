using System.Globalization;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Microsoft.Maui.SailfishOS.Platform.Text;

namespace Microsoft.Maui.SailfishOS.Handlers;

/// <summary>
/// Per-family measure methods called from each handler's <c>GetDesiredSize</c>.
/// Text uses Qt's QFontMetrics when the Qt host is running (the metrics QML renders with), else a font-size estimate.
/// </summary>
internal static class SailfishMeasure
{
	public const double DefaultFontSize = 14;

	/// <summary>
	/// Shared measure wrapper: applies Width/HeightRequest and explicit sizes, defers layouts to the
	/// cross-platform manager and runs <paramref name="self"/> for everything else.
	/// </summary>
	public static Size Frame(IView view, double widthConstraint, double heightConstraint,
	                         Func<IView, double, double, Size> self)
	{
		var requestedWidth = RequestedSize(view, horizontal: true);
		var requestedHeight = RequestedSize(view, horizontal: false);

		var wc = IsFinite(view.Width) ? view.Width : (IsFinite(requestedWidth) ? requestedWidth : widthConstraint);
		var hc = IsFinite(view.Height) ? view.Height : (IsFinite(requestedHeight) ? requestedHeight : heightConstraint);

		Size size = view is ILayout layout
			? layout.CrossPlatformMeasure(wc, hc)   // recurses into child GetDesiredSize
			: self(view, wc, hc);

		// Requests win over the intrinsic size...
		if (IsFinite(requestedWidth))
			size.Width = requestedWidth;
		if (IsFinite(requestedHeight))
			size.Height = requestedHeight;

		// ...and explicit requests always win.
		if (IsFinite(view.Width))
			size.Width = view.Width;
		if (IsFinite(view.Height))
			size.Height = view.Height;

		// Then the minimum/maximum, as MAUI's GetDesiredSizeFromHandler resolves them (WhatToEat's category tiles,
		// MinimumWidthRequest=150, came out as narrow as their text).
		size.Width = MinMax(size.Width, view.MinimumWidth, view.MaximumWidth);
		size.Height = MinMax(size.Height, view.MinimumHeight, view.MaximumHeight);
		return size;
	}

	private static double MinMax(double value, double min, double max)
	{
		if (!double.IsNaN(max) && value > max)
			value = max;
		if (IsFinite(min) && min > 0 && value < min)
			value = min;
		return value;
	}

	/// <summary>The catch-all leftovers for unmigrated view types.</summary>
	public static Size Generic(IView view, double wc, double hc)
	{
		switch (view)
		{
			case IStepper:
				return Constrain(180, 40, wc, hc);

			// Border/Frame must precede IContentView so padding and stroke are added around the content.
			case IBorderView borderView when borderView.PresentedContent is IView borderInner:
				return MeasureBoxed(borderInner,
					(borderView as IPadding)?.Padding ?? Thickness.Zero,
					borderView.StrokeThickness, wc, hc);

#pragma warning disable CS0618 // Frame is obsolete but must still measure.
			case Frame legacyFrameView when legacyFrameView.Content is IView frameInner:
				return MeasureBoxed(frameInner, legacyFrameView.Padding,
					legacyFrameView.BorderColor is not null ? 1 : 0, wc, hc);
#pragma warning restore CS0618

			case ScrollView scroll when ((IContentView)scroll).PresentedContent is IView scrolled:
			{
				// Content is measured unbounded along the scroll axes; the ScrollView itself fills the viewport.
				var pad = scroll.Padding;
				var horizontal = scroll.Orientation is ScrollOrientation.Horizontal or ScrollOrientation.Both;
				var vertical = scroll.Orientation is ScrollOrientation.Vertical or ScrollOrientation.Both;
				var innerW = horizontal || !IsFinite(wc) ? double.PositiveInfinity : Math.Max(0, wc - pad.HorizontalThickness);
				var innerH = vertical || !IsFinite(hc) ? double.PositiveInfinity : Math.Max(0, hc - pad.VerticalThickness);
				var s = scrolled.Measure(innerW, innerH);
				return new Size(IsFinite(wc) ? wc : s.Width + pad.HorizontalThickness,
					IsFinite(hc) ? hc : s.Height + pad.VerticalThickness);
			}

			case IContentView content when content.PresentedContent is IView inner:
				return MeasurePadded(content, inner, wc, hc);

			case IContentView contentView:
			{
				var s = contentView.CrossPlatformMeasure(wc, hc);
				return Constrain(s.Width, s.Height, wc, hc);
			}

			default:
				return Constrain(0, 0, wc, hc);
		}
	}

	/* --- the migrated control families --- */

	public static Size Label(IView view, double wc, double hc)
	{
		if (view is not ILabel label)
			return Generic(view, wc, hc);

		var (spans, paragraph) = LabelTextMapper.Map(label);

		// Multi-font FormattedText is measured with the first span's font (approximation).
		if (QtHostTextMetrics.Enabled && spans.Count > 0)
		{
			var text = string.Concat(spans.Select(s => s.Text));
			if (text.Length == 0)
				return Constrain(0, 0, wc, hc);
			var first = spans[0];
			var wrapMode = paragraph.LineBreakMode switch
			{
				LineBreakMode.NoWrap or LineBreakMode.HeadTruncation or LineBreakMode.MiddleTruncation
					=> QtHostTextMetrics.NoWrap,
				LineBreakMode.CharacterWrap => QtHostTextMetrics.WrapAnywhere,
				_ => QtHostTextMetrics.WordWrap,
			};
			var maxLines = paragraph.MaxLines is > 0 and < int.MaxValue ? paragraph.MaxLines : 0;
			var pad = (view as Microsoft.Maui.Controls.Label)?.Padding ?? Thickness.Zero;
			var textWc = IsFinite(wc) ? Math.Max(1, wc - pad.HorizontalThickness) : 0;
			// An unset Label FontSize paints with Theme.fontSizeMedium (Label.qml), not MAUI's 14 dp default: measuring
			// at 14 dp wrapped "Y = " in an Auto column and cut its height. FormattedText spans carry their own size.
			var fontSize = view is Microsoft.Maui.Controls.Label { FormattedText: null } plain && !(plain.FontSize > 0)
				? (int)Math.Round(SilicaMediumFontDp())
				: first.FontSize;
			var (qw, qh) = QtHostTextMetrics.Measure(text, first.Family, first.Attributes, fontSize,
				textWc, wrapMode, paragraph.LineHeight, maxLines, first.CharacterSpacing);
			if (qh == 0)
				qh = fontSize;
			return Constrain(qw + pad.HorizontalThickness, Math.Max(qh, fontSize) + pad.VerticalThickness, wc, hc);
		}

		var est = EstimateTextSize(spans, paragraph, IsFinite(wc) ? wc : double.PositiveInfinity);
		return Constrain(est.Width, est.Height, wc, hc);
	}

	public static Size Button(IView view, double wc, double hc)
	{
		if (view is not IButton button)
			return Generic(view, wc, hc);
		var text = (button as ITextButton)?.Text ?? string.Empty;
		// An unset size paints with the Silica theme font, so measure that.
		var fs = view is Microsoft.Maui.Controls.Button mb
		         && !QtHostPageRenderer.HasAppFontSize(mb, Microsoft.Maui.Controls.Button.FontSizeProperty, mb.FontSize)
			? (int)Math.Round(SilicaMediumFontDp())
			: FontSizeOf(button);
		var attributes = (view as Microsoft.Maui.Controls.Button)?.FontAttributes ?? FontAttributes.None;
		var (tw, th) = MeasureText(text, (view as Microsoft.Maui.Controls.Button)?.FontFamily, attributes, fs,
			(view as Microsoft.Maui.Controls.Button)?.CharacterSpacing ?? 0);
		if (th == 0)
			th = fs + 6;
		var pad = view is Microsoft.Maui.Controls.Button { } b && b.IsSet(Microsoft.Maui.Controls.Button.PaddingProperty)
			? b.Padding
			: Thickness.Zero;
		// The image (Silica's Icon paints it at its native pixels) beside or above the text, per ContentLayout.
		if (view is Microsoft.Maui.Controls.Button { ImageSource: { } source } withImage &&
		    QtHostImages.PixelSize(source) is { } px)
		{
			var density = Platform.SailfishDisplay.Density > 0 ? Platform.SailfishDisplay.Density : 1;
			var (iw, ih) = (px.Width / density, px.Height / density);
			var layout = withImage.ContentLayout;
			var gap = text.Length > 0 ? layout.Spacing : 0;
			if (layout.Position is Microsoft.Maui.Controls.Button.ButtonContentLayout.ImagePosition.Top
			    or Microsoft.Maui.Controls.Button.ButtonContentLayout.ImagePosition.Bottom)
				return Constrain(Math.Max(tw, iw) + 48 + pad.HorizontalThickness,
					Math.Max(48, ih + gap + (text.Length > 0 ? th : 0) + 20 + pad.VerticalThickness), wc, hc);
			return Constrain(tw + iw + gap + 48 + pad.HorizontalThickness,
				Math.Max(48, Math.Max(th, ih) + 20 + pad.VerticalThickness), wc, hc);
		}
		return Constrain(tw + 48 + pad.HorizontalThickness, Math.Max(48, th + 20 + pad.VerticalThickness), wc, hc);
	}

	/// <summary>Two Theme.itemSizeSmall tap targets side by side.</summary>
	public static Size Stepper(IView view, double wc, double hc)
	{
		var h = ValueBoxHeightDp();
		return Constrain(2 * h + 8, h, wc, hc);
	}

	public static Size Indicator(IView view, double wc, double hc)
	{
		if (view is not IndicatorView indicator)
			return Generic(view, wc, hc);
		var count = Math.Min(indicator.Count, Math.Max(0, indicator.MaximumVisible));
		if (count <= 0 || (indicator.HideSingle && indicator.Count <= 1))
			return Size.Zero;
		var size = indicator.IndicatorSize > 0 ? indicator.IndicatorSize : 6;
		return Constrain(count * size * 2 - size + 2 * size, Math.Max(size * 3, 24), wc, hc);
	}

	public static Size Radio(IView view, double wc, double hc)
	{
		// Templated (ControlTemplate): sized by the template tree, as a ContentView.
		if (view is IContentView { PresentedContent: IView } templated)
			return MeasureContent(templated, wc, hc);
		// Uses the Silica theme font size unless the app set one, like the adapter paints.
		var radio = view as Microsoft.Maui.Controls.RadioButton;
		var text = radio?.Content?.ToString() ?? string.Empty;
		var fs = radio is not null && QtHostPageRenderer.HasAppFontSize(radio, Microsoft.Maui.Controls.RadioButton.FontSizeProperty, radio.FontSize)
			? (int)Math.Round(radio.FontSize)
			: (int)Math.Round(SilicaMediumFontDp());
		var (tw, th) = MeasureText(text, radio?.FontFamily, radio?.FontAttributes ?? FontAttributes.None, fs, radio?.CharacterSpacing ?? 0);
		if (th == 0)
			th = fs + 6;
		var overhead = ThemeDp("2 * Theme.horizontalPageMargin + Theme.iconSizeSmall + Theme.paddingMedium", 64);
		return Constrain(tw + overhead + 1, Math.Max(44, th + 12), wc, hc);
	}

	public static Size ValueBox(IView view, double wc, double hc)
	{
		// Silica ComboBox/ValueButton floor their height at Theme.itemSizeSmall, which a geometry write
		// cannot shrink, so measure that; an app font size can make the line taller.
		var h = ValueBoxHeightDp();
		if (view is Microsoft.Maui.Controls.View styled && view is ITextStyle ts && AppFontSizeOf(styled) is { } size)
		{
			var (_, th) = MeasureText("Ag", ts.Font.Family, FontAttributes.None, (int)Math.Round(size));
			h = Math.Max(h, th + 2 * PaddingMediumDp());
		}
		return Constrain(IsFinite(wc) ? wc : 200, h, wc, hc);
	}

	private static double? AppFontSizeOf(Microsoft.Maui.Controls.View view) => view switch
	{
		Microsoft.Maui.Controls.Picker p when QtHostPageRenderer.HasAppFontSize(p, Microsoft.Maui.Controls.Picker.FontSizeProperty, p.FontSize) => p.FontSize,
		Microsoft.Maui.Controls.DatePicker d when QtHostPageRenderer.HasAppFontSize(d, Microsoft.Maui.Controls.DatePicker.FontSizeProperty, d.FontSize) => d.FontSize,
		Microsoft.Maui.Controls.TimePicker t when QtHostPageRenderer.HasAppFontSize(t, Microsoft.Maui.Controls.TimePicker.FontSizeProperty, t.FontSize) => t.FontSize,
		_ => null,
	};

	private static double PaddingMediumDp() => ThemeDp("Theme.paddingMedium", 8);

	private static readonly Dictionary<string, double> _themeDp = new(StringComparer.Ordinal);

	/// <summary>A Silica Theme expression (Qt px) in dp, queried once and cached; <paramref name="fallback"/> when headless.</summary>
	private static double ThemeDp(string expression, double fallback)
	{
		if (_themeDp.TryGetValue(expression, out var cached))
			return cached;
		if (!QtHostTextMetrics.Enabled || !QtHostRuntime.IsRunning)
			return fallback;
		var raw = QtHostRuntime.Eval(expression);
		if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var qt) || qt <= 0)
			return fallback;
		return _themeDp[expression] = QtHostUnits.ToLogical(qt);
	}

	public static Size Activity(IView view, double wc, double hc) =>
		Constrain(IsFinite(wc) ? wc : 48, 32, wc, hc);

	public static Size TextInput(IView view, double wc, double hc)
	{
		// Height must include the Silica field's own margins: TextField clips its editor to height minus
		// margins, so a shorter measure hides the typed text.
		// The line height comes from a fixed sample: measured on the text, an empty field was shorter than a filled
		// one and the form below jumped at the first typed character (WhatToEat New Recipe).
		var text = (view as ITextInput)?.Text ?? (view as IText)?.Text ?? string.Empty;
		var fs = TextInputFontDp(view);
		var family = (view as ITextStyle)?.Font.Family;
		var (tw, _) = MeasureText(text, family, FontAttributes.None, fs);
		var (_, th) = MeasureText("Ag", family, FontAttributes.None, fs);
		if (th == 0)
			th = fs + 6;
		// A borderless field without a placeholder hides Silica's label line (Entry.qml mauiBare).
		var bare = view is Microsoft.Maui.Controls.Entry entry && string.IsNullOrEmpty(entry.Placeholder) &&
		           (entry.IsSet(VisualElement.BackgroundColorProperty) || entry.IsSet(VisualElement.BackgroundProperty));
		var margins = TextInputMarginsDp(view is Microsoft.Maui.Controls.SearchBar ? "SearchField" : "TextField", bare);
		return Constrain(tw + 28, Math.Max(44, th + margins), wc, hc);
	}

	public static Size Editor(IView view, double wc, double hc)
	{
		// Fills the width, three lines plus the TextArea margins; AutoSize=TextChanges grows past three lines with the
		// wrapped text (MAUI re-measures an auto-sized Editor on every text change).
		var fs = TextInputFontDp(view);
		var family = (view as ITextStyle)?.Font.Family;
		var (_, th) = MeasureText("Ag", family, FontAttributes.None, fs);
		if (th == 0)
			th = fs + 6;
		var textHeight = 3.0 * th;
		if (view is Microsoft.Maui.Controls.Editor { AutoSize: Microsoft.Maui.Controls.EditorAutoSizeOption.TextChanges, Text: { Length: > 0 } text }
			&& IsFinite(wc) && QtHostTextMetrics.Enabled)
		{
			// Editor.qml's textMargin, on both sides.
			var margin = Math.Min(ThemeDp("Theme.horizontalPageMargin", 16), Math.Max(ThemeDp("Theme.paddingSmall", 6), wc / 8));
			var (_, wrapped) = QtHostTextMetrics.Measure(text, family, FontAttributes.None, fs, Math.Max(1, wc - 2 * margin),
				QtHostTextMetrics.WordWrap);
			textHeight = Math.Max(textHeight, wrapped);
		}
		return Constrain(wc, Math.Max(90, textHeight + TextInputMarginsDp("TextArea")), wc, hc);
	}

	/// <summary>The font size a text input paints with: the app's, or the Silica theme size when unset.</summary>
	private static int TextInputFontDp(IView view)
	{
		var size = view switch
		{
			Microsoft.Maui.Controls.Entry e when QtHostPageRenderer.HasAppFontSize(e, Microsoft.Maui.Controls.Entry.FontSizeProperty, e.FontSize) => e.FontSize,
			Microsoft.Maui.Controls.Editor e when QtHostPageRenderer.HasAppFontSize(e, Microsoft.Maui.Controls.Editor.FontSizeProperty, e.FontSize) => e.FontSize,
			Microsoft.Maui.Controls.SearchBar e when QtHostPageRenderer.HasAppFontSize(e, Microsoft.Maui.Controls.SearchBar.FontSizeProperty, e.FontSize) => e.FontSize,
			_ => SilicaMediumFontDp(),
		};
		return (int)Math.Round(size);
	}

	/// <summary>Vertical margins (dp) of a Silica text input, probed once per type with a hidden instance.</summary>
	private static double TextInputMarginsDp(string silicaType, bool noLabel = false)
	{
		var key = "margins:" + silicaType + (noLabel ? ":bare" : string.Empty);
		if (_themeDp.TryGetValue(key, out var cached))
			return cached;
		if (!QtHostTextMetrics.Enabled || !QtHostRuntime.IsRunning)
			return 18;
		var raw = QtHostRuntime.Eval(
			"(function(){var o=Qt.createQmlObject('import QtQuick 2.6; import Sailfish.Silica 1.0; " + silicaType +
			(noLabel ? " { visible: false; labelVisible: false }" : " { visible: false }") + "', pageStack, 'maui-measure-probe');var m=o.implicitHeight-(o._editor?o._editor.height:0);o.destroy();return m;})()");
		if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var qt) || qt <= 0)
		{
			QtHostDiag.Warn(QtHostDiagChannel.Geometry, $"{silicaType} margin probe failed ('{raw}') — 18dp estimate");
			return 18;
		}
		return _themeDp[key] = QtHostUnits.ToLogical(qt);
	}

	public static Size Check(IView view, double wc, double hc)
	{
		var h = ValueBoxHeightDp();
		return Constrain(h, h, wc, hc);
	}

	public static Size Switch(IView view, double wc, double hc) => Constrain(64, 36, wc, hc);

	/// <summary>A CollectionView fills what its layout gives it; unbounded along its scroll axis (a StackLayout or a
	/// ScrollView) it sizes to its rows, as RecyclerView/UICollectionView do, instead of collapsing to 0. A horizontal
	/// list unbounded across it (an Auto grid row) takes its tallest item, as a wrap_content RecyclerView does.</summary>
	public static Size Collection(IView view, double wc, double hc)
	{
		if (view.Handler is not SailfishListViewHandler { Adapter: { } adapter })
			return Constrain(0, 0, wc, hc);
		if (adapter.Horizontal)
		{
			var width = IsFinite(wc) ? 0 : adapter.ContentExtentDp;
			var height = IsFinite(hc) ? (IsFinite(wc) ? 0 : hc) : adapter.CrossExtentDp;
			return Constrain(width, height, wc, hc);
		}
		if (IsFinite(hc))
			return Constrain(0, 0, wc, hc);
		return new Size(IsFinite(wc) ? wc : 0, adapter.ContentExtentDp);
	}

	public static Size Slider(IView view, double wc, double hc) => Constrain(200, 44, wc, hc);

	public static Size Progress(IView view, double wc, double hc) => Constrain(200, 12, wc, hc);

	/// <summary>
	/// The source's own size (<see cref="QtHostImages.IntrinsicSize"/>), as Android's ImageView with adjustViewBounds:
	/// one requested side gives the other by the aspect ratio, and a constraint smaller than the image shrinks both.
	/// Nothing to show yet (no source, a stream being read, a remote image loading) measures 0 × 0.
	/// </summary>
	public static Size Image(IView view, double wc, double hc)
	{
		if (view is not IImageSourcePart part || QtHostImages.IntrinsicSize(part.Source as ImageSource) is not { } own ||
		    own.Width <= 0 || own.Height <= 0)
			return Constrain(0, 0, wc, hc);
		var pad = view is ImageButton button ? button.Padding : Thickness.Zero;
		var (w, h) = (own.Width, own.Height);
		var requestedW = RequestedSize(view, horizontal: true);
		var requestedH = RequestedSize(view, horizontal: false);
		if (IsFinite(requestedW) && !IsFinite(requestedH))
			return new Size(requestedW, Math.Max(0, requestedW - pad.HorizontalThickness) * h / w + pad.VerticalThickness);
		if (IsFinite(requestedH) && !IsFinite(requestedW))
			return new Size(Math.Max(0, requestedH - pad.VerticalThickness) * w / h + pad.HorizontalThickness, requestedH);
		var fit = Math.Min(1, Math.Min(
			IsFinite(wc) ? Math.Max(0, wc - pad.HorizontalThickness) / w : 1,
			IsFinite(hc) ? Math.Max(0, hc - pad.VerticalThickness) / h : 1));
		return new Size(w * fit + pad.HorizontalThickness, h * fit + pad.VerticalThickness);
	}

	public static Size Box(IView view, double wc, double hc) => Constrain(40, 40, wc, hc);

	public static Size Content(IView view, double wc, double hc) =>
		view is IContentView content ? MeasureContent(content, wc, hc) : Generic(view, wc, hc);

	private static Size MeasureContent(IContentView content, double wc, double hc)
	{
		if (content.PresentedContent is IView inner)
			return MeasurePadded(content, inner, wc, hc);
		var own = content.CrossPlatformMeasure(wc, hc);
		return Constrain(own.Width, own.Height, wc, hc);
	}

	/// <summary>Sizes to the content plus the view's Padding (as MAUI's MeasureContent), but fills a finite constraint.
	/// Without the padding an Auto row was the bare content's height and the padding ate into the content.</summary>
	private static Size MeasurePadded(IContentView content, IView inner, double wc, double hc)
	{
		var pad = content.Padding;
		var s = inner.Measure(IsFinite(wc) ? Math.Max(0, wc - pad.HorizontalThickness) : double.PositiveInfinity,
			IsFinite(hc) ? Math.Max(0, hc - pad.VerticalThickness) : double.PositiveInfinity);
		return new Size(IsFinite(wc) ? wc : s.Width + pad.HorizontalThickness,
			IsFinite(hc) ? hc : s.Height + pad.VerticalThickness);
	}

	public static Size Boxed(IView view, double wc, double hc) => view switch
	{
		IBorderView borderView when borderView.PresentedContent is IView borderInner =>
			MeasureBoxed(borderInner, (borderView as IPadding)?.Padding ?? Thickness.Zero,
				borderView.StrokeThickness, wc, hc),
#pragma warning disable CS0618
		Frame legacyFrame when legacyFrame.Content is IView frameInner =>
			MeasureBoxed(frameInner, legacyFrame.Padding, legacyFrame.BorderColor is not null ? 1 : 0, wc, hc),
#pragma warning restore CS0618
		_ => Generic(view, wc, hc),
	};

	/* --- shared helpers --- */

	/// <summary>WidthRequest/HeightRequest, or NaN when unset.</summary>
	public static double RequestedSize(IView view, bool horizontal)
	{
		if (view is not VisualElement visual)
			return double.NaN;

		var value = horizontal ? visual.WidthRequest : visual.HeightRequest;
		return value > 0 ? value : double.NaN;
	}

	public static int FontSizeOf(IView view) =>
		view is ITextStyle ts && ts.Font.Size > 0 ? (int)Math.Round(ts.Font.Size) : (int)DefaultFontSize;

	private static double _valueBoxHeightDp = double.NaN;

	/// <summary>
	/// Native minimum height (dp) of Silica value boxes (Theme.itemSizeSmall); measure must match it or
	/// following controls overlap. Queried on the Qt thread and cached; 44dp until available.
	/// </summary>
	public static double ValueBoxHeightDp()
	{
		if (!double.IsNaN(_valueBoxHeightDp))
			return _valueBoxHeightDp;
		if (!QtHostTextMetrics.Enabled || !QtHostRuntime.IsRunning)
			return 44;
		var raw = QtHostRuntime.Eval("Theme.itemSizeSmall");
		if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var qt) || qt <= 0)
			return 44;
		_valueBoxHeightDp = QtHostUnits.ToLogical(qt);
		return _valueBoxHeightDp;
	}

	private static double _silicaButtonFontDp = double.NaN;

	/// <summary>Theme.fontSizeMedium in dp, what an unset Button, Entry or Label FontSize paints with; 25dp until available.</summary>
	public static double SilicaMediumFontDp()
	{
		if (!double.IsNaN(_silicaButtonFontDp))
			return _silicaButtonFontDp;
		if (!QtHostTextMetrics.Enabled || !QtHostRuntime.IsRunning)
			return 25;
		var raw = QtHostRuntime.Eval("Theme.fontSizeMedium");
		if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var qt) || qt <= 0)
			return 25;
		_silicaButtonFontDp = QtHostUnits.ToLogical(qt);
		return _silicaButtonFontDp;
	}

	/// <summary>Single-line text size: Qt metrics when the host is running, else a font-size estimate.</summary>
	public static (int w, int h) MeasureText(string? text, string? family, FontAttributes attributes, int ptsize, double letterSpacingDp = 0)
	{
		if (QtHostTextMetrics.Enabled)
		{
			var (w, h) = QtHostTextMetrics.Measure(text, family, attributes, ptsize, 0, QtHostTextMetrics.NoWrap, letterSpacingDp: letterSpacingDp);
			return ((int)Math.Round(w), (int)Math.Round(h));
		}
		return ((int)Math.Round((text?.Length ?? 0) * ptsize * 0.55), (int)Math.Round(ptsize * 1.2));
	}

	/// <summary>Headless text estimate (0.55 × size per char, 1.2 × size per line); on device Qt metrics answer instead.</summary>
	public static Size EstimateTextSize(List<TextSpan> spans, TextParagraphStyle paragraph, double maxWidth)
	{
		if (spans.Count == 0)
			return new Size(0, 0);
		var text = string.Concat(spans.Select(s => s.Text));
		if (text.Length == 0)
			return new Size(0, 0);

		var fontSize = spans[0].FontSize;
		var charAdvance = fontSize * 0.55 + spans[0].CharacterSpacing;
		var lineAdvance = fontSize * 1.2 * (paragraph.LineHeight > 0 ? paragraph.LineHeight : 1.0);
		var naturalWidth = text.Length * charAdvance;

		var lines = 1;
		if (paragraph.LineBreakMode != LineBreakMode.NoWrap && IsFinite(maxWidth) && maxWidth > 0 && naturalWidth > maxWidth)
		{
			var perLine = Math.Max(1, (int)Math.Floor(maxWidth / charAdvance));
			lines = (text.Length + perLine - 1) / perLine;
		}
		if (paragraph.MaxLines > 0 && paragraph.MaxLines < int.MaxValue)
			lines = Math.Min(lines, paragraph.MaxLines);
		var width = IsFinite(maxWidth) && maxWidth > 0 ? Math.Min(naturalWidth, maxWidth) : naturalWidth;
		return new Size(width, lines * lineAdvance);
	}

	public static Size Constrain(double w, double h, double wc, double hc) =>
		new Size(IsFinite(wc) ? Math.Min(w, wc) : w, IsFinite(hc) ? Math.Min(h, hc) : h);

	/// <summary>Border/Frame measure: content inside the padded, stroked rect, then padding and stroke added back.</summary>
	public static Size MeasureBoxed(IView inner, Thickness pad, double stroke, double wc, double hc)
	{
		var innerW = IsFinite(wc) ? Math.Max(0, wc - pad.HorizontalThickness - 2 * stroke) : double.PositiveInfinity;
		var innerH = IsFinite(hc) ? Math.Max(0, hc - pad.VerticalThickness - 2 * stroke) : double.PositiveInfinity;
		var s = inner.Measure(innerW, innerH);
		return Constrain(
			s.Width + pad.HorizontalThickness + 2 * stroke,
			s.Height + pad.VerticalThickness + 2 * stroke,
			wc, hc);
	}

	public static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
}
