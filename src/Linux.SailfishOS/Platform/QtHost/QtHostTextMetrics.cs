using System.Globalization;
using System.Text;
using Microsoft.Maui;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// Measures text with the same QFontMetrics QML renders with, so layout matches the pixels.
/// Takes and returns dp; device pixels cross the bridge. Qt thread only.
/// </summary>
internal static class QtHostTextMetrics
{
	/// <summary>Wrap modes, matching QML Text.wrapMode.</summary>
	public const int NoWrap = 0;
	public const int WordWrap = 1;
	public const int WrapAnywhere = 2;

	/// <summary>True once <see cref="Enable"/> ran; otherwise NullViewHandler estimates from the font size.</summary>
	public static bool Enabled { get; internal set; }

	/// <summary>Enables Qt measurement; call before the first layout pass.</summary>
	public static void Enable() => Enabled = true;

	/// <summary>
	/// Measures <paramref name="text"/> in dp; a 0 or infinite max width and 0 max lines mean unlimited.
	/// Returns (0,0) for empty text or on failure.
	/// </summary>
	/// <param name="runs">Mixed-font text (FormattedText spans in order; their texts concatenate to <paramref name="text"/>):
	/// laid out with one format per run, so a line is as tall as its tallest run (tracker S43). Null for one font.</param>
	public static (double w, double h) Measure(string? text, string? family, FontAttributes attributes,
	                                           double fontSizeDp, double maxWidthDp, int wrap,
	                                           double lineHeight = 1.0, int maxLines = 0,
	                                           double letterSpacingDp = 0.0,
	                                           IReadOnlyList<Text.TextSpan>? runs = null)
	{
		if (string.IsNullOrEmpty(text))
			return (0, 0);

		var density = SailfishDisplay.Density;
		var sb = new StringBuilder(text.Length + 128);
		sb.Append("{\"text\":").Append(BridgeValue.Quote(text))
		  .Append(",\"family\":").Append(BridgeValue.Quote(QtHostFonts.Resolve(family)))
		  .Append(",\"px\":").Append(Num(fontSizeDp * density))
		  .Append(",\"bold\":").Append((attributes & FontAttributes.Bold) != 0 ? '1' : '0')
		  .Append(",\"italic\":").Append((attributes & FontAttributes.Italic) != 0 ? '1' : '0')
		  .Append(",\"ls\":").Append(Num(letterSpacingDp * density))
		  .Append(",\"lh\":").Append(Num(lineHeight > 0 ? lineHeight : 1.0))
		  .Append(",\"maxLines\":").Append(maxLines > 0 ? maxLines.ToString(CultureInfo.InvariantCulture) : "0")
		  .Append(",\"wrap\":").Append(wrap.ToString(CultureInfo.InvariantCulture))
		  .Append(",\"maxW\":").Append(Num(maxWidthDp > 0 && !double.IsInfinity(maxWidthDp) ? maxWidthDp * density : 0));
		if (runs is { Count: > 0 })
			AppendRuns(sb, runs, density);
		sb.Append('}');

		// The request is the whole input of QFontMetrics (text, resolved font, px size, width, wrapping), so a cached
		// answer cannot go stale; a layout pass re-measures every text leaf, as a native view would not.
		var request = sb.ToString();
		if (CacheEnabled && Cache.TryGetValue(request, out var cached))
		{
			CacheHits++;
			return cached;
		}
		if (!QtHostRuntime.TryMeasureText(request, out var wPx, out var hPx))
		{
			Console.Error.WriteLine($"[Sailfish] Qt text-metrics: measure failed: {QtHostRuntime.LastErrorText}");
			return (0, 0);
		}
		var result = (wPx / density, hPx / density);
		if (CacheEnabled)
		{
			if (Cache.Count >= CacheCapacity)
				Cache.Clear();   // simple bound; a page's texts refill it in one pass
			Cache[request] = result;
		}
		return result;
	}

	/// <summary>"runs":[{"s":start,"n":length,"family","px","bold","italic","ls"}], offsets in UTF-16 code units.</summary>
	private static void AppendRuns(StringBuilder sb, IReadOnlyList<Text.TextSpan> runs, double density)
	{
		sb.Append(",\"runs\":[");
		var start = 0;
		for (var i = 0; i < runs.Count; i++)
		{
			var run = runs[i];
			if (i > 0)
				sb.Append(',');
			sb.Append("{\"s\":").Append(start.ToString(CultureInfo.InvariantCulture))
			  .Append(",\"n\":").Append(run.Text.Length.ToString(CultureInfo.InvariantCulture))
			  .Append(",\"family\":").Append(BridgeValue.Quote(QtHostFonts.Resolve(run.Family)))
			  .Append(",\"px\":").Append(Num(run.FontSize * density))
			  .Append(",\"bold\":").Append((run.Attributes & FontAttributes.Bold) != 0 ? '1' : '0')
			  .Append(",\"italic\":").Append((run.Attributes & FontAttributes.Italic) != 0 ? '1' : '0')
			  .Append(",\"ls\":").Append(Num(run.CharacterSpacing * density))
			  .Append('}');
			start += run.Text.Length;
		}
		sb.Append(']');
	}

	/// <summary>MAUI_SAILFISH_TEXT_CACHE=0 measures every request through the shim (A/B).</summary>
	internal static bool CacheEnabled { get; set; } = SailfishEnv.Get("MAUI_SAILFISH_TEXT_CACHE") != "0";

	private const int CacheCapacity = 4096;
	private static readonly Dictionary<string, (double w, double h)> Cache = new(StringComparer.Ordinal);

	/// <summary>Measures answered from the cache (the shim's text-measure counter counts the rest).</summary>
	public static long CacheHits { get; private set; }

	/// <summary>Drops cached answers (a font registered later could change what a family resolves to).</summary>
	internal static void ClearCache() => Cache.Clear();

	/// <summary>Single-line measure in dp.</summary>
	public static (double w, double h) MeasureSingleLine(string? text, string? family, FontAttributes attributes, double fontSizeDp) =>
		Measure(text, family, attributes, fontSizeDp, 0, NoWrap);

	private static string Num(double value) =>
		double.IsNaN(value) || double.IsInfinity(value)
			? "0"
			: BridgeValue.Number(value);
}
