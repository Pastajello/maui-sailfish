using System.Numerics;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Text;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// An <see cref="ICanvas"/> that records <c>IDrawable.Draw</c> calls as a command list, replayed by
/// qml/shapes/GraphicsView.qml in a Context2D (command names must match it). Coordinates stay in dp;
/// the adapter scales the context once by density, so app transforms compose normally.
/// </summary>
internal sealed class QtHostCanvasRecorder : ICanvas
{
	/// <summary>Per-pass cap so a runaway Draw loop cannot grow the bridge JSON without bound. 4096 cut real charts
	/// (a few thousand points, each a command); the list is one property push and one replay, so a larger cap is the
	/// whole fix (tracker S31).</summary>
	internal const int MaxCommands = 32768;

	private readonly List<object?> _commands = new();

	/// <summary>The recorded stream (bridge-friendly nested lists).</summary>
	public IReadOnlyList<object?> Commands => _commands;

	/// <summary>True when commands were dropped because of the cap.</summary>
	public bool Truncated { get; private set; }

	/* --- canvas state --- */

	/// <summary>The screen scale (dp → device px) MAUI reports to drawables.</summary>
	public float DisplayScale { get; set; } = (float)SailfishDisplay.Density;

	public bool Antialias { set => Add("aa", value ? 1 : 0); }   // the adapter turns the canvas's antialiasing off

	public float Alpha { set => Add("al", Num(value)); }

	public BlendMode BlendMode { set => Add("bm", (int)value); }

	public void SaveState() => Add("sv");

	/// <summary>MAUI's contract: false when there was no state left to restore.</summary>
	public bool RestoreState()
	{
		Add("rs");
		return true;
	}

	public void ResetState() => Add("reset");

	/* --- paint state --- */

	public Color FillColor { set => Add("fc", BridgeValue.ColorString(value)); }

	public Color StrokeColor { set => Add("sc", BridgeValue.ColorString(value)); }

	public float StrokeSize { set => Add("ss", Num(value)); }

	public float MiterLimit { set => Add("ml", Num(value)); }

	/* LineCap/LineJoin values map 1:1 onto the adapter's Context2D string tables. */
	public LineCap StrokeLineCap { set => Add("cap", (int)value); }

	public LineJoin StrokeLineJoin { set => Add("join", (int)value); }

	/// <summary>Qt 5.6's Context2D has no setLineDash, so the adapter cuts dashes in JS.</summary>
	public float[] StrokeDashPattern
	{
		set
		{
			var pattern = new List<object?>(value?.Length ?? 0);
			if (value is not null)
				foreach (var entry in value)
					pattern.Add(Num(entry));
			Add("dashp", pattern);
		}
	}

	public float StrokeDashOffset { set => Add("dasho", Num(value)); }

	public void SetFillPaint(Paint paint, RectF rectangle) =>
		Add("fpaint", QtHostShapes.PaintSpec(paint, rectangle) ?? new List<object?>());

	public void SetShadow(SizeF offset, float blur, Color color) =>
		Add("shadow", Num(offset.Width), Num(offset.Height), Num(blur), BridgeValue.ColorString(color));

	/// <summary>A ConfigureFonts alias resolves to its Qt family, as a Label's FontFamily does (tracker S30).</summary>
	public IFont Font
	{
		set => Add("fon", QtHostFonts.Resolve(value?.Name), value?.Weight ?? 400,
		           value is { StyleType: not FontStyleType.Normal } ? 1 : 0);
	}

	public Color FontColor { set => Add("foc", BridgeValue.ColorString(value)); }

	public float FontSize { set => Add("fos", Num(value)); }

	/* --- transforms --- */

	public void Translate(float dx, float dy) => Add("tr", Num(dx), Num(dy));

	public void Scale(float xFactor, float yFactor) => Add("sc2", Num(xFactor), Num(yFactor));

	public void Rotate(float degrees) => Add("ro", Num(degrees));

	public void Rotate(float degrees, float x, float y) => Add("ro2", Num(degrees), Num(x), Num(y));

	public void ConcatenateTransform(Matrix3x2 transform) =>
		Add("ct", Num(transform.M11), Num(transform.M12), Num(transform.M21), Num(transform.M22),
		    Num(transform.M31), Num(transform.M32));

	/* --- clipping --- */

	// Density 1.0: the stream stays in dp and the adapter's CTM scale converts it.
	public void ClipPath(PathF path, WindingMode windingMode = WindingMode.NonZero) =>
		Add("clipp", QtHostShapes.PathOps(path, 1.0), (int)windingMode);

	public void ClipRectangle(float x, float y, float width, float height) =>
		Add("clipr", Num(x), Num(y), Num(width), Num(height));

	/// <summary>Canvas2D has no clip subtraction; recorded so the adapter counts the degradation.</summary>
	public void SubtractFromClip(float x, float y, float width, float height) =>
		Add("subclipr", Num(x), Num(y), Num(width), Num(height));

	/* --- stroked geometry --- */

	public void DrawLine(float x1, float y1, float x2, float y2) =>
		Add("line", Num(x1), Num(y1), Num(x2), Num(y2));

	public void DrawPath(PathF path) => Add("path", QtHostShapes.PathOps(path, 1.0));

	public void DrawArc(float x, float y, float width, float height, float startAngle, float endAngle,
	                    bool clockwise, bool closed) =>
		Add("arc", Num(x), Num(y), Num(width), Num(height), Num(startAngle), Num(endAngle),
		    clockwise ? 1 : 0, closed ? 1 : 0);

	public void DrawRectangle(float x, float y, float width, float height) =>
		Add("rect", Num(x), Num(y), Num(width), Num(height));

	public void DrawRoundedRectangle(float x, float y, float width, float height, float cornerRadius) =>
		Add("rrect", Num(x), Num(y), Num(width), Num(height), Num(cornerRadius));

	public void DrawEllipse(float x, float y, float width, float height) =>
		Add("ell", Num(x), Num(y), Num(width), Num(height));

	/* --- filled geometry --- */

	public void FillPath(PathF path, WindingMode windingMode = WindingMode.NonZero) =>
		Add("fpath", QtHostShapes.PathOps(path, 1.0), (int)windingMode);

	public void FillArc(float x, float y, float width, float height, float startAngle, float endAngle,
	                    bool clockwise) =>
		Add("farc", Num(x), Num(y), Num(width), Num(height), Num(startAngle), Num(endAngle),
		    clockwise ? 1 : 0);

	public void FillRectangle(float x, float y, float width, float height) =>
		Add("frect", Num(x), Num(y), Num(width), Num(height));

	public void FillRoundedRectangle(float x, float y, float width, float height, float cornerRadius) =>
		Add("frrect", Num(x), Num(y), Num(width), Num(height), Num(cornerRadius));

	public void FillEllipse(float x, float y, float width, float height) =>
		Add("fell", Num(x), Num(y), Num(width), Num(height));

	/* --- images --- */

	/// <summary>IImage has no decoded pixels here, so this only records a skip. Fully qualified because
	/// Microsoft.Maui.IImage (the view contract) shadows the Graphics one.</summary>
	public void DrawImage(Microsoft.Maui.Graphics.IImage image, float x, float y, float width, float height) =>
		Add("img");

	/* --- text --- */

	public void DrawString(string value, float x, float y, HorizontalAlignment horizontalAlignment) =>
		Add("strp", value ?? string.Empty, Num(x), Num(y), (int)horizontalAlignment);

	public void DrawString(string value, float x, float y, float width, float height,
	                       HorizontalAlignment horizontalAlignment, VerticalAlignment verticalAlignment,
	                       TextFlow textFlow = TextFlow.ClipBounds, float lineSpacingAdjustment = 0) =>
		Add("str", value ?? string.Empty, Num(x), Num(y), Num(width), Num(height), (int)horizontalAlignment,
		    (int)verticalAlignment, (int)textFlow, Num(lineSpacingAdjustment));

	/// <summary>Flattened to plain text: Qt 5.6's Context2D cannot lay out per-run fonts and colors.</summary>
	public void DrawText(IAttributedText text, float x, float y, float width, float height) =>
		Add("txt", text?.Text ?? string.Empty, Num(x), Num(y), Num(width), Num(height));

	/* --- measurement --- */

	/// <summary>QFontMetrics measurement in dp, matching the QML Text items; a size estimate is the
	/// fallback when no Qt host runs.</summary>
	public SizeF GetStringSize(string value, IFont font, float textSize) => Measure(value, font, textSize);

	/// <summary>Alignment does not change the single-line size.</summary>
	public SizeF GetStringSize(string value, IFont font, float textSize,
	                           HorizontalAlignment horizontalAlignment, VerticalAlignment verticalAlignment) =>
		Measure(value, font, textSize);

	private static SizeF Measure(string value, IFont? font, float textSize)
	{
		var (w, h) = QtHostTextMetrics.MeasureSingleLine(value, font?.Name, FontAttributesOf(font), textSize);
		if (w <= 0 && !string.IsNullOrEmpty(value))
		{
			w = value.Length * textSize * 0.6;
			h = textSize * 1.2;
		}
		return new SizeF((float)w, (float)Math.Max(h, textSize));
	}

	private static FontAttributes FontAttributesOf(IFont? font)
	{
		var attributes = FontAttributes.None;
		if (font is null)
			return attributes;
		if (font.Weight >= 600)
			attributes |= FontAttributes.Bold;
		if (font.StyleType != FontStyleType.Normal)
			attributes |= FontAttributes.Italic;
		return attributes;
	}

	/* --- recording plumbing --- */

	private void Add(string kind, params object?[] args)
	{
		if (_commands.Count >= MaxCommands)
		{
			if (!Truncated)
			{
				Truncated = true;
				Console.Error.WriteLine($"[Sailfish] Qt canvas: drawable command stream truncated at {MaxCommands} commands");
			}
			return;
		}
		var command = new List<object?>(args.Length + 1) { kind };
		foreach (var arg in args)
			command.Add(arg);
		_commands.Add(command);
	}

	/// <summary>Milli-dp precision keeps the command JSON small.</summary>
	private static double Num(float v) => float.IsFinite(v) ? Math.Round(v, 3) : 0;
}
