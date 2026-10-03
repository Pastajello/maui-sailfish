using Microsoft.Maui;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Minimal IFontManager: Silica paints and Qt measures text, so only the default size is needed.
/// </summary>
internal sealed class SailfishFontManager : IFontManager
{
	/// <summary>The dp size MAUI resolves an unset FontSize to. Button/Entry treat this value as unset (keeping the Silica
	/// theme font), because MAUI marks the default as set once the handler attaches.</summary>
	internal const double DefaultSize = 18;

	public double DefaultFontSize => DefaultSize;
}
