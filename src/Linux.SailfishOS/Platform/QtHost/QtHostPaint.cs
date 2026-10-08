using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>Reduces MAUI brushes to the plain colors the QML adapters paint.</summary>
internal static class QtHostPaint
{
	/// <summary>A solid brush's color; gradients, null and the unset Brush.Default (a solid brush with a null
	/// color) give null.</summary>
	public static Color? Solid(Brush? brush) => brush is SolidColorBrush { Color: { } color } ? color : null;

	/// <summary>The background paint: a solid Background brush wins over BackgroundColor; null when neither is set.</summary>
	public static Color? Background(VisualElement view) => Solid(view.Background) ?? view.BackgroundColor;

	/// <summary>A brush as one colour: a solid brush's, a gradient's stops averaged (iOS draws a gradient shadow this
	/// way, tracker S41); null for none.</summary>
	public static Color? Average(Brush? brush)
	{
		if (Solid(brush) is { } solid)
			return solid;
		if (brush is not GradientBrush { GradientStops.Count: > 0 } gradient)
			return null;
		float r = 0, g = 0, b = 0, a = 0;
		var n = 0;
		foreach (var stop in gradient.GradientStops)
		{
			if (stop.Color is not { } c)
				continue;
			r += c.Red;
			g += c.Green;
			b += c.Blue;
			a += c.Alpha;
			n++;
		}
		return n == 0 ? null : new Color(r / n, g / n, b / n, a / n);
	}

	/// <summary>A gradient Background as the shim's GradientFill spec (tracker S41, D10 a): points and radius relative
	/// to the element (MAUI's own units for these brushes), stops as [offset, "#AARRGGBB"]; "" for none.</summary>
	public static string GradientSpec(Brush? brush)
	{
		static List<object?> Stops(GradientBrush g) =>
			g.GradientStops.OrderBy(s => s.Offset).Select(s => (object?)new List<object?> { Math.Clamp(s.Offset, 0, 1), BridgeValue.ColorString(s.Color ?? Colors.Transparent) }).ToList();
		return brush switch
		{
			LinearGradientBrush { GradientStops.Count: > 0 } linear => BridgeValue.Serialize(new Dictionary<string, object?>
			{
				["t"] = "linear", ["x0"] = linear.StartPoint.X, ["y0"] = linear.StartPoint.Y,
				["x1"] = linear.EndPoint.X, ["y1"] = linear.EndPoint.Y, ["stops"] = Stops(linear),
			}),
			RadialGradientBrush { GradientStops.Count: > 0 } radial => BridgeValue.Serialize(new Dictionary<string, object?>
			{
				["t"] = "radial", ["x0"] = radial.Center.X, ["y0"] = radial.Center.Y, ["r"] = radial.Radius, ["stops"] = Stops(radial),
			}),
			_ => string.Empty,
		};
	}
}
