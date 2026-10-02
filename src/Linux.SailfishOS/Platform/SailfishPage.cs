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
/// Per-page Silica settings MAUI has no API for. <see cref="AllowedOrientationsProperty"/> is Silica's
/// <c>Page.allowedOrientations</c>: a video page can turn to landscape while the rest of the app stays portrait, as an
/// Android activity sets its own <c>screenOrientation</c>. It applies within the app-wide orientation and can sit on
/// the page or on a container around it (a NavigationPage, a Shell), the nearest one winning.
/// </summary>
/// <example><code>SailfishPage.SetAllowedOrientations(playerPage, SailfishOrientations.LandscapeMask);</code></example>
public static class SailfishPage
{
	public static readonly BindableProperty AllowedOrientationsProperty = BindableProperty.CreateAttached(
		"AllowedOrientations", typeof(SailfishOrientations), typeof(SailfishPage), SailfishOrientations.Default,
		propertyChanged: (_, _, _) => QtHostPageRenderer.RequestPoll());

	public static SailfishOrientations GetAllowedOrientations(BindableObject page) =>
		(SailfishOrientations)page.GetValue(AllowedOrientationsProperty);

	public static void SetAllowedOrientations(BindableObject page, SailfishOrientations value) =>
		page.SetValue(AllowedOrientationsProperty, value);

	/// <summary>The page's own setting, else the nearest container's; <see cref="SailfishOrientations.Default"/> when none.</summary>
	internal static SailfishOrientations Effective(Element page)
	{
		for (var e = page; e is not null; e = e.Parent)
			if (e is Page && GetAllowedOrientations(e) is var value && value != SailfishOrientations.Default)
				return value;
		return SailfishOrientations.Default;
	}
}
