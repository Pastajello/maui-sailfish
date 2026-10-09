using Microsoft.Maui.Controls;

namespace Microsoft.Maui.SailfishOS.Handlers;

/// <summary>
/// Which font size a control paints with: the app's, or the Silica theme size when the app chose none. Snapshots
/// (what the adapter paints) and <see cref="SailfishMeasure"/> (what the layout reserves) both decide through here,
/// so text never measures at one size and paints at another.
/// </summary>
internal static class SailfishFontRules
{
	/// <summary>
	/// The font size the app chose, or null when it chose none and the adapter paints the Silica theme size. Once the
	/// handler attaches MAUI stores the font manager's default (<see cref="Platform.SailfishFontManager.DefaultSize"/>,
	/// 18) as a set value without PropertyChanged, so that value counts as unset; an explicit FontSize="18" therefore
	/// paints at the theme size too (docs/porting-existing-apps.md, "Platform behaviour that differs"). Measure and paint both decide through here.
	/// </summary>
	/// <remarks>With FontAutoScalingEnabled (MAUI's default) the size follows the phone's text-size setting, as Android
	/// scales sp with the system font scale (tracker S43).</remarks>
	public static double? AppFontSize(Microsoft.Maui.Controls.BindableObject element, Microsoft.Maui.Controls.BindableProperty property, double size) =>
		element.IsSet(property) && size > 0 && Math.Abs(size - Platform.SailfishFontManager.DefaultSize) > 0.01
			? size * ScaleFor(element)
			: null;

	/// <summary>
	/// The phone's text-size factor (Settings › Display › Text size: normal, large, huge, gigantic):
	/// Theme.fontSizeMedium over Theme.fontSizeMediumBase, which Silica's own text follows. 1 until the theme answers;
	/// read once per run, so a changed setting applies at the next start.
	/// </summary>
	public static double TextScale()
	{
		var scaled = SailfishMeasure.ThemeDp("Theme.fontSizeMedium", 0);
		var baseSize = SailfishMeasure.ThemeDp("Theme.fontSizeMediumBase", 0);
		return scaled > 0 && baseSize > 0 ? Math.Clamp(scaled / baseSize, 0.5, 4.0) : 1.0;
	}

	/// <summary>The factor an explicit size gets: <see cref="TextScale"/> unless the element turned FontAutoScalingEnabled off.</summary>
	public static double ScaleFor(Microsoft.Maui.Controls.BindableObject element) => AutoScales(element) ? TextScale() : 1.0;

	private static bool AutoScales(Microsoft.Maui.Controls.BindableObject element) => element switch
	{
		Microsoft.Maui.Controls.Span span => span.FontAutoScalingEnabled,
		Microsoft.Maui.ITextStyle style => style.Font.AutoScalingEnabled,
		_ => true,
	};

	/// <summary>
	/// A Label's size: the app's, else null and Label.qml paints Theme.fontSizeMedium, as every Silica label and every
	/// other control here (<see cref="AppFontSize"/>, owner decision 2026-10-03; until then an unset Label painted
	/// MAUI's 18). Snapshot and measure both read it.
	/// </summary>
	public static double? LabelFontSize(Microsoft.Maui.Controls.Label label) =>
		AppFontSize(label, Microsoft.Maui.Controls.Label.FontSizeProperty, label.FontSize);

	/// <summary>The size (dp) the adapter paints with: the app's, else the Silica theme size.</summary>
	public static double PaintFontSizeDp(Microsoft.Maui.Controls.BindableObject element, Microsoft.Maui.Controls.BindableProperty property, double size) =>
		AppFontSize(element, property, size) ?? SilicaMediumFontDp();

	/// <summary>Theme.fontSizeMedium in dp, what an unset Button, Entry or Label FontSize paints with; 25 dp until
	/// the theme answers.</summary>
	public static double SilicaMediumFontDp() => SailfishMeasure.ThemeDp("Theme.fontSizeMedium", 25);
}
