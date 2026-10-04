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

// Snapshot builders shared by several control families: fonts, text style, horizontal alignment, flow direction.
// MAUI control state → adapter properties (sizes in device px, Qt enums as ints); handlers call these from Snapshot().
internal static partial class AdapterSnapshots
{
	/// <summary>Text styling for an adapter with one text item: only what the app set changes the Silica look.
	/// CharacterSpacing is applied by the shim on mauiTextItem (Qt 5.6 QML has no absolute tracking).</summary>
	internal static void AddTextStyle(Dictionary<string, object?> props, BindableObject element, BindableProperty fontSizeProperty,
	                                  double fontSize, string? family, FontAttributes attributes, double characterSpacing, Color? textColor)
	{
		AddFont(props, element, fontSizeProperty, fontSize, family, attributes, characterSpacing);
		props["mauiTextColor"] = textColor ?? Colors.Transparent;
	}

	/// <summary>Font props that cross only when the app set them (0 size = keep the Silica theme size).</summary>
	internal static void AddFont(Dictionary<string, object?> props, BindableObject element, BindableProperty fontSizeProperty,
	                             double fontSize, string? family, FontAttributes attributes, double characterSpacing)
	{
		var density = QtHostUnits.ScenePerDp;
		props["mauiPixelSize"] = SailfishFontRules.AppFontSize(element, fontSizeProperty, fontSize) is { } size ? size * density : 0.0;
		props["mauiFamily"] = QtHostFonts.Resolve(family);
		props["mauiBold"] = (attributes & FontAttributes.Bold) != 0;
		props["mauiItalic"] = (attributes & FontAttributes.Italic) != 0;
		props["mauiLetterSpacing"] = characterSpacing * density;
	}

	/// <summary>Start/End follow the effective FlowDirection; MAUI's layout already mirrors positions, text
	/// alignment is the adapter's part.</summary>
	private static int MapHAlign(TextAlignment alignment, bool rightToLeft = false) => alignment switch
	{
		TextAlignment.Center => 4,                        // Text.AlignHCenter
		TextAlignment.End => rightToLeft ? 1 : 2,         // Text.AlignLeft / AlignRight
		_ => rightToLeft ? 2 : 1,                         // Start/Fill
	};

	internal static bool IsRightToLeft(Element? element) => QtHostVisualState.IsRightToLeft(element);
}
