using Microsoft.Maui.Controls;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>Silica's Orientation flags, combinable: the orientations a page may turn to.</summary>
[Flags]
public enum SailfishOrientations
{
	/// <summary>Not set: the app's own (<c>SailfishOrientation</c> in the project, Any by default).</summary>
	Default = 0,
	Portrait = 1,
	Landscape = 2,
	PortraitInverted = 4,
	LandscapeInverted = 8,
	PortraitMask = Portrait | PortraitInverted,
	LandscapeMask = Landscape | LandscapeInverted,
	All = PortraitMask | LandscapeMask,
}

/// <summary>
/// The first home of the per-page orientation setting; it forwards to the platform-specific API (decision D13, tracker
/// S47), so both spellings set the same value.
/// </summary>
[Obsolete("Use page.On<SailfishOS>().SetAllowedOrientations(…) (Microsoft.Maui.Controls.PlatformConfiguration.SailfishOSSpecific.Page).")]
public static class SailfishPage
{
	public static readonly BindableProperty AllowedOrientationsProperty =
		Microsoft.Maui.Controls.PlatformConfiguration.SailfishOSSpecific.Page.AllowedOrientationsProperty;

	public static SailfishOrientations GetAllowedOrientations(BindableObject page) =>
		Microsoft.Maui.Controls.PlatformConfiguration.SailfishOSSpecific.Page.GetAllowedOrientations(page);

	public static void SetAllowedOrientations(BindableObject page, SailfishOrientations value) =>
		Microsoft.Maui.Controls.PlatformConfiguration.SailfishOSSpecific.Page.SetAllowedOrientations(page, value);
}
