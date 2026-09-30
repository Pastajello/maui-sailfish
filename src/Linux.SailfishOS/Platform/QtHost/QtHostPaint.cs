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
}
