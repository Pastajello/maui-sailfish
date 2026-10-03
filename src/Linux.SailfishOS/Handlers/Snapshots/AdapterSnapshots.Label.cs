using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.SailfishOS.Platform;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Handlers;

// Snapshot builders of Label: text or span HTML, wrap/elide, alignment.
// MAUI control state → adapter properties (sizes in device px, Qt enums as ints); handlers call these from Snapshot().
internal static partial class AdapterSnapshots
{
	/// <summary>Label text crossing the bridge (FormattedText spans flattened).</summary>
	private static string LabelText(Label label) =>
		label.FormattedText is { Spans.Count: > 0 }
			? string.Concat(label.FormattedText.Spans.Select(s => s.Text))
			: label.Text ?? string.Empty;

	/// <summary>
	/// The full label snapshot (see qml/controls/Label.qml). Sizes cross in device px, Qt enums as plain ints,
	/// and FormattedText spans as escaped HTML rich text. "text" must precede "mauiTextFormat": the batch
	/// preserves insertion order.
	/// </summary>
	internal static Dictionary<string, object?> LabelProps(Label label)
	{
		var density = QtHostUnits.ScenePerDp;
		var spans = label.FormattedText is { Spans.Count: > 0 } ? label.FormattedText.Spans : null;
		var html = spans is null && label.TextType == TextType.Html;
		return new Dictionary<string, object?>
		{
			// "text" is the display string: escaped HTML with spans, the app's HTML with TextType.Html (both
			// RichText), plain text with TextTransform applied otherwise. HTML stays untransformed: upper-casing it
			// would break entities.
			["text"] = spans is not null ? BuildSpanHtml(spans, density)
				: html ? label.Text ?? string.Empty
				: Microsoft.Maui.Controls.Internals.TextTransformUtilities.GetTransformedText(label.Text ?? string.Empty, label.TextTransform),
			["mauiTextFormat"] = spans is not null || html ? 1 : 0,   // Text.RichText / Text.PlainText
			["mauiEmphasis"] = label.FontSize >= 24 ? "header" : "normal",
			// Transparent keeps the emphasis-derived theme color.
			["mauiColor"] = label.TextColor ?? Colors.Transparent,
			["mauiBackground"] = label.BackgroundColor ?? Colors.Transparent,
			["mauiPixelSize"] = SailfishMeasure.LabelFontSize(label) is { } size ? size * density : 0.0,
			["mauiFamily"] = QtHostFonts.Resolve(label.FontFamily),
			["mauiBold"] = (label.FontAttributes & FontAttributes.Bold) != 0,
			["mauiItalic"] = (label.FontAttributes & FontAttributes.Italic) != 0,
			["mauiUnderline"] = (label.TextDecorations & TextDecorations.Underline) != 0,
			["mauiStrike"] = (label.TextDecorations & TextDecorations.Strikethrough) != 0,
			["mauiLetterSpacing"] = label.CharacterSpacing * density,
			["mauiLineHeight"] = label.LineHeight > 0 ? label.LineHeight : 1.0,
			["mauiMaxLines"] = label.MaxLines > 0 ? label.MaxLines : 0,
			["mauiWrap"] = MapWrapMode(label.LineBreakMode),
			["mauiElide"] = MapElideMode(label.LineBreakMode),
			["mauiHAlign"] = MapHAlign(label.HorizontalTextAlignment, IsRightToLeft(label)),
			["mauiVAlign"] = MapVAlign(label.VerticalTextAlignment),
			// Label.Padding insets the text inside the background box.
			["mauiPadL"] = label.Padding.Left * density,
			["mauiPadT"] = label.Padding.Top * density,
			["mauiPadR"] = label.Padding.Right * density,
			["mauiPadB"] = label.Padding.Bottom * density,
		};
	}

	// Enum values are Qt 5.6 Qt Quick Text constants. Head/Middle truncation = elided single line; Tail = wrapped
	// with the last visible line elided.
	private static int MapWrapMode(LineBreakMode mode) => mode switch
	{
		LineBreakMode.NoWrap or LineBreakMode.HeadTruncation or LineBreakMode.MiddleTruncation => 0,
		LineBreakMode.CharacterWrap => 3,
		_ => 1,
	};

	private static int MapElideMode(LineBreakMode mode) => mode switch
	{
		LineBreakMode.HeadTruncation => 1,
		LineBreakMode.MiddleTruncation => 2,
		LineBreakMode.TailTruncation => 3,
		_ => 0,
	};

	private static int MapVAlign(TextAlignment alignment) => alignment switch
	{
		TextAlignment.Center => 128, // Text.AlignVCenter
		TextAlignment.End => 64,     // Text.AlignBottom
		_ => 32,                     // Text.AlignTop (Start/Fill)
	};

	/// <summary>
	/// FormattedText spans → escaped HTML rich text (colors as "#AARRGGBB", sizes in device px). Per-span
	/// CharacterSpacing/LineHeight are not supported; the label-level values apply.
	/// </summary>
	private static string BuildSpanHtml(IList<Span> spans, double density)
	{
		var sb = new StringBuilder();
		foreach (var span in spans)
		{
			var text = span.Text ?? string.Empty;
			if (text.Length == 0)
				continue;
			sb.Append("<span style=\"");
			if (span.TextColor is { } color)
				sb.Append("color:").Append(BridgeValue.ColorString(color)).Append(';');
			if ((span.FontAttributes & FontAttributes.Bold) != 0)
				sb.Append("font-weight:bold;");
			if ((span.FontAttributes & FontAttributes.Italic) != 0)
				sb.Append("font-style:italic;");
			if (!string.IsNullOrEmpty(span.FontFamily))
				sb.Append("font-family:'").Append(QtHostFonts.Resolve(span.FontFamily).Replace("\\", "\\\\").Replace("'", "\\'")).Append("';");
			if (span.FontSize > 0)
				sb.Append("font-size:").Append((span.FontSize * density).ToString("F0", CultureInfo.InvariantCulture)).Append("px;");
			var underline = (span.TextDecorations & TextDecorations.Underline) != 0;
			var strike = (span.TextDecorations & TextDecorations.Strikethrough) != 0;
			if (underline && strike)
				sb.Append("text-decoration:underline line-through;");
			else if (underline)
				sb.Append("text-decoration:underline;");
			else if (strike)
				sb.Append("text-decoration:line-through;");
			sb.Append("\">").Append(HtmlEscape(text)).Append("</span>");
		}
		return sb.ToString();
	}

	/// <summary>HTML-escapes span text (newlines become &lt;br/&gt;).</summary>
	private static string HtmlEscape(string text)
	{
		var sb = new StringBuilder(text.Length + 8);
		foreach (var c in text)
		{
			switch (c)
			{
				case '&': sb.Append("&amp;"); break;
				case '<': sb.Append("&lt;"); break;
				case '>': sb.Append("&gt;"); break;
				case '"': sb.Append("&quot;"); break;
				case '\n': sb.Append("<br/>"); break;
				case '\r': break;
				default: sb.Append(c); break;
			}
		}
		return sb.ToString();
	}
}
