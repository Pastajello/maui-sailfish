using Microsoft.Maui;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.SailfishOS.Platform.Text;

// Backend-neutral text model used by the measure pass (LabelTextMapper → QtHostTextMetrics).

/// <summary>One styled run of text (a label's text or a single formatted span).</summary>
internal sealed class TextSpan
{
	public string Text { get; init; } = string.Empty;

	public string? Family { get; init; }

	public FontAttributes Attributes { get; init; }

	public int FontSize { get; init; } = (int)Handlers.SailfishMeasure.DefaultFontSize;

	/// <summary>Extra tracking between characters, in device units.</summary>
	public double CharacterSpacing { get; init; }

	/// <summary>Span-level line height multiplier.</summary>
	public double LineHeight { get; init; } = 1.0;

	public TextDecorations Decorations { get; init; }

	public Color Color { get; init; } = Colors.White;
}

/// <summary>Paragraph-level layout options for a block of text.</summary>
internal sealed class TextParagraphStyle
{
	public LineBreakMode LineBreakMode { get; init; } = LineBreakMode.WordWrap;

	/// <summary>Maximum rendered lines; extra lines are cut with an ellipsis.</summary>
	public int MaxLines { get; init; } = int.MaxValue;

	public TextAlignment HorizontalAlignment { get; init; } = TextAlignment.Start;

	public TextAlignment VerticalAlignment { get; init; } = TextAlignment.Start;

	/// <summary>Paragraph-level line height multiplier.</summary>
	public double LineHeight { get; init; } = 1.0;
}
