using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// A Border's children are sibling hosts on the flat canvas, so nothing clips them to its rounded shape.
/// For elements covering a Border corner this pushes the radius and a corner bitmask, and the adapter
/// masks itself; the spec is part of the reconcile snapshot so moving off a corner diffs back to zero.
/// </summary>
internal static class QtHostClip
{
	/// <summary>Corner bitmask, clockwise from top-left.</summary>
	public const int TopLeft = 1;
	public const int TopRight = 2;
	public const int BottomRight = 4;
	public const int BottomLeft = 8;

	/// <summary>Merges the clip spec into a host snapshot; always writes both
	/// keys so the reconcile diff can also push "no clip anymore".</summary>
	public static void Merge(Dictionary<string, object?> props, VisualElement? element, VisualElement? surroundFallback)
	{
		TryClipSpec(element, out var radius, out var corners);
		props["mauiClipRadius"] = radius;
		props["mauiClipCorners"] = corners;
		props["mauiClipColor"] = corners == 0 ? Colors.Transparent : SurroundColor(element!, surroundFallback);
		var border = corners == 0 ? null : ClippingBorder(element!);
		var stroke = border is null || border.StrokeThickness <= 0 ? null : QtHostPaint.Solid(border.Stroke);
		props["mauiClipStroke"] = stroke ?? Colors.Transparent;
		props["mauiClipStrokeWidth"] = stroke is null ? 0.0 : border!.StrokeThickness * SailfishDisplay.Density;
		if (QtHostDiag.TraceEnabled && corners != 0)
			QtHostDiag.Trace(QtHostDiagChannel.Geometry,
				$"clip spec: {element?.GetType().Name} radius={radius:F1} corners={corners} " +
				$"color={props["mauiClipColor"]} fallback={surroundFallback?.GetType().Name} " +
				$"fallbackBg={surroundFallback?.BackgroundColor}");
	}

	/// <summary>The first painted background behind the Border, shown through a cut corner (transparent
	/// disables the caps). Collection-row content is detached from the page, so the page is the fallback.</summary>
	private static Color SurroundColor(VisualElement element, VisualElement? fallback)
	{
		var clipping = true;
		for (var p = element.Parent as VisualElement; p is not null; p = p.Parent as VisualElement)
		{
			// The clipping Border paints inside the radius, not around it; a Border further out (a card around an
			// avatar) is what shows through the corner.
			if (p is Border && clipping)
			{
				clipping = false;
				continue;
			}
			var color = Effective(p);
			if (color is not null && color.Alpha > 0)
				return color;
		}
		var surround = fallback is null ? null : Effective(fallback);
		return surround is not null && surround.Alpha > 0 ? surround : Colors.Transparent;
	}

	// BackgroundColor first: the Background brush can hold a stale transparent solid next to it.
	private static Color? Effective(VisualElement view) =>
		view.BackgroundColor ?? QtHostPaint.Solid(view.Background);

	/// <summary>Radius in device px (0 when nothing clips) plus the bitmask of
	/// the ancestor Border's corners this element paints over.</summary>
	public static bool TryClipSpec(VisualElement? element, out double radiusDevicePx, out int corners)
	{
		radiusDevicePx = 0;
		corners = 0;
		if (element is null || element.Width <= 0 || element.Height <= 0)
			return false;

		var border = ClippingBorder(element);
		if (border is null || border.Width <= 0 || border.Height <= 0)
			return false;

		// MAUI arranges the content inside the stroke and clips it to the shape's inner edge.
		var inset = Math.Max(0, border.StrokeThickness);
		var radius = UniformRadius(border) - inset;
		if (radius <= 0)
			return false;

		// The element's rect in the Border's coordinate space.
		var (x, y) = OffsetIn(element, border);
		var left = x;
		var top = y;
		var right = x + element.Width;
		var bottom = y + element.Height;

		var eps = inset + 0.5;
		if (left <= eps && top <= eps)
			corners |= TopLeft;
		if (right >= border.Width - eps && top <= eps)
			corners |= TopRight;
		if (right >= border.Width - eps && bottom >= border.Height - eps)
			corners |= BottomRight;
		if (left <= eps && bottom >= border.Height - eps)
			corners |= BottomLeft;

		radiusDevicePx = radius * SailfishDisplay.Density;
		return true;
	}

	private static Border? ClippingBorder(VisualElement element)
	{
		for (var p = element.Parent as VisualElement; p is not null; p = p.Parent as VisualElement)
			if (p is Border border)
				return border;
		return null;
	}

	/// <summary>The StrokeShape's round radius (0 for other shapes); non-uniform radii use the largest corner
	/// because the mask draws a single radius. An Ellipse is a circle on a square Border (round avatars) and a
	/// stadium otherwise.</summary>
	private static double UniformRadius(Border border)
	{
		switch (border.StrokeShape)
		{
			case RoundRectangle round:
				var c = round.CornerRadius;
				return Math.Max(Math.Max(c.TopLeft, c.TopRight), Math.Max(c.BottomLeft, c.BottomRight));
			case Ellipse:
				return Math.Min(border.Width, border.Height) / 2;
			default:
				return 0;
		}
	}

	private static (double X, double Y) OffsetIn(VisualElement element, VisualElement ancestor)
	{
		double x = 0, y = 0;
		for (var v = element; v is not null && !ReferenceEquals(v, ancestor); v = v.Parent as VisualElement)
		{
			x += v.X;
			y += v.Y;
		}
		return (x, y);
	}
}
