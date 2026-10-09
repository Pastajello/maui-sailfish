using Microsoft.Maui;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.SailfishOS.Platform.Text;

/// <summary>
/// Maps an <see cref="ILabel"/> (text or FormattedText) onto <see cref="TextSpan"/>/<see cref="TextParagraphStyle"/> for the measure pass.
/// </summary>
internal static class LabelTextMapper
{
	/// <summary>Default label font size when neither label nor span sets one.</summary>
	private const double DefaultFontSize = Handlers.SailfishMeasure.DefaultFontSize;

	public static (List<TextSpan> spans, TextParagraphStyle paragraph) Map(ILabel label)
	{
		var spans = new List<TextSpan>();
		var baseColor = label.TextColor ?? Colors.White;
		var baseFont = label.Font;

		if (label is Microsoft.Maui.Controls.Label { FormattedText.Spans.Count: > 0 } formatted)
		{
			// What the rich-text label paints (AdapterSnapshots.BuildSpanHtml): a span without a size inherits the label's
			// painted size (its own, else Theme.fontSizeMedium), an explicit one follows FontAutoScalingEnabled, and the
			// text carries the span's TextTransform.
			var labelSize = Handlers.SailfishFontRules.LabelFontSize(formatted) ?? Handlers.SailfishFontRules.SilicaMediumFontDp();
			foreach (var span in formatted.FormattedText.Spans)
			{
				spans.Add(new TextSpan
				{
					Text = Microsoft.Maui.Controls.Internals.TextTransformUtilities.GetTransformedText(span.Text ?? string.Empty, span.TextTransform),
					Family = string.IsNullOrEmpty(span.FontFamily) ? baseFont.Family : span.FontFamily,
					Attributes = span.FontAttributes == FontAttributes.None ? BaseAttributes(label) : span.FontAttributes,
					FontSize = SpanFontSize(span) ?? labelSize,
					CharacterSpacing = span.CharacterSpacing,
					LineHeight = span.LineHeight > 0 ? span.LineHeight : 1.0,
					Decorations = span.TextDecorations,
					Color = span.TextColor ?? baseColor,
				});
			}
		}
		else
		{
			spans.Add(new TextSpan
			{
				Text = label.Text ?? string.Empty,
				Family = baseFont.Family,
				Attributes = BaseAttributes(label),
				FontSize = FontSize(baseFont.Size, baseFont),
				CharacterSpacing = label.CharacterSpacing,
				LineHeight = 1.0,
				Decorations = label.TextDecorations,
				Color = baseColor,
			});
		}

		// LineBreakMode/MaxLines live on the Controls Label, not on ILabel.
		var controlsLabel = label as Microsoft.Maui.Controls.Label;
		var paragraph = new TextParagraphStyle
		{
			LineBreakMode = controlsLabel?.LineBreakMode ?? LineBreakMode.WordWrap,
			MaxLines = controlsLabel is { MaxLines: > 0 } ? controlsLabel.MaxLines : int.MaxValue,
			HorizontalAlignment = label.HorizontalTextAlignment,
			VerticalAlignment = label.VerticalTextAlignment,
			LineHeight = label.LineHeight > 0 ? label.LineHeight : 1.0,
		};

		return (spans, paragraph);
	}

	/// <summary>The label's own font style (bold/italic), if it set one.</summary>
	private static FontAttributes BaseAttributes(ILabel label) =>
		label is Microsoft.Maui.Controls.Label controls ? controls.FontAttributes : FontAttributes.None;

	private static double FontSize(double spanSize, Font baseFont) =>
		spanSize > 0 ? spanSize : (baseFont.Size > 0 ? baseFont.Size : DefaultFontSize);

	/// <summary>A span's own size in dp (auto-scaled as painted), or null when it inherits the label's.</summary>
	internal static double? SpanFontSize(Microsoft.Maui.Controls.Span span) =>
		span.IsSet(Microsoft.Maui.Controls.Span.FontSizeProperty) && span.FontSize > 0
			? span.FontSize * Handlers.SailfishFontRules.ScaleFor(span)
			: null;
}
