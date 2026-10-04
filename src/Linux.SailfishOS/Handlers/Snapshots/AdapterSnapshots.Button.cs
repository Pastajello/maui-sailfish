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

// Snapshot builders of Button: text, font, colours with an explicit "set" flag (unset gives Silica's back).
// MAUI control state → adapter properties (sizes in device px, Qt enums as ints); handlers call these from Snapshot().
internal static partial class AdapterSnapshots
{
	/// <summary>True when the app set a background, even a transparent one. Unset keeps the Silica default; an
	/// explicit transparent plate must cross, or Silica's highlight plate paints over it. An unset Background is
	/// Brush.Default (an empty brush, not null): counted as set, every plain button lost its Silica plate.</summary>
	private static bool HasExplicitBackground(VisualElement view) =>
		!Brush.IsNullOrEmpty(view.Background) || view.BackgroundColor is not null;

	/// <summary>The Button snapshot, shared by the reconcile and SailfishButtonHandler so both push one spec.</summary>
	internal static Dictionary<string, object?> ButtonProps(Button button)
	{
		var buttonProps = new Dictionary<string, object?> { ["text"] = button.Text ?? string.Empty };
		// The label colour and the plate tint, with whether the app set them: unset hands Silica's own colours back,
		// so clearing a colour (TextColor = null, a VisualState setter that ends) restores the theme. An explicit
		// transparent plate is set too: the app is asking for no plate.
		buttonProps["mauiTextColor"] = button.TextColor ?? Colors.Transparent;
		buttonProps["mauiTextColorSet"] = button.TextColor is not null;
		var plateSet = HasExplicitBackground(button);
		buttonProps["mauiPlateColor"] = plateSet ? EffectiveBackground(button) : Colors.Transparent;
		buttonProps["mauiPlateSet"] = plateSet;
		// Other styling crosses only when set, so the Silica look stays (MAUI's default FontSize would otherwise
		// override Theme.fontSizeMedium).
		AddFont(buttonProps, button, Button.FontSizeProperty, button.FontSize, button.FontFamily,
			button.FontAttributes, button.CharacterSpacing);
		var density = QtHostUnits.ScenePerDp;
		buttonProps["mauiCornerRadius"] = button.CornerRadius >= 0 ? button.CornerRadius * density : -1.0;
		buttonProps["mauiStrokeColor"] = button.BorderColor ?? Colors.Transparent;
		buttonProps["mauiStrokeWidth"] = button.BorderWidth >= 0 ? button.BorderWidth * density : -1.0;
		buttonProps["mauiIconSource"] = QtHostImages.Resolve(button.ImageSource) ?? string.Empty;
		// Button.ContentLayout: where the image sits against the text, and the gap (px).
		buttonProps["mauiIconPosition"] = button.ContentLayout.Position switch
		{
			Button.ButtonContentLayout.ImagePosition.Top => "top",
			Button.ButtonContentLayout.ImagePosition.Bottom => "bottom",
			Button.ButtonContentLayout.ImagePosition.Right => "right",
			_ => "left",
		};
		buttonProps["mauiIconSpacing"] = button.ContentLayout.Spacing * density;
		// The plate (MAUI's background) covers the whole frame when the app sized the button or stacks its image;
		// otherwise it keeps Silica's itemSizeExtraSmall height inside the frame.
		buttonProps["mauiFillPlate"] = button.HeightRequest > 0 || button.MinimumHeightRequest > 0 ||
			(button.ImageSource is not null && button.ContentLayout.Position is
				Button.ButtonContentLayout.ImagePosition.Top or Button.ButtonContentLayout.ImagePosition.Bottom);
		// An unset FontSize paints at Theme.fontSizeMedium, larger than the 14 dp MAUI layouts are made for: let the
		// label shrink to fit a narrower button (a fixed 100 dp column) down to that 14 dp instead of fading out.
		buttonProps["mauiFitPixelSize"] = SailfishFontRules.AppFontSize(button, Button.FontSizeProperty, button.FontSize) is not null
			? 0.0
			: Handlers.SailfishMeasure.DefaultFontSize * density;
		return buttonProps;
	}
}
