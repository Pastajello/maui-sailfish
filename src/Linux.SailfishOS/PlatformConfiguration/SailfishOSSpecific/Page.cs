using Microsoft.Maui.SailfishOS.Platform;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using FormsElement = Microsoft.Maui.Controls.Page;

namespace Microsoft.Maui.Controls.PlatformConfiguration.SailfishOSSpecific;

/// <summary>
/// Sailfish OS specifics of a page (decision D13). <see cref="AllowedOrientationsProperty"/> is Silica's
/// <c>Page.allowedOrientations</c>: a video page can turn to landscape while the rest of the app stays portrait, as an
/// Android activity sets its own <c>screenOrientation</c>. It applies within the app-wide orientation and can sit on
/// the page or on a container around it (a NavigationPage, a Shell), the nearest one winning.
/// </summary>
/// <example><code>playerPage.On&lt;SailfishOS&gt;().SetAllowedOrientations(SailfishOrientations.LandscapeMask);</code></example>
public static class Page
{
	public static readonly BindableProperty AllowedOrientationsProperty = BindableProperty.CreateAttached(
		"AllowedOrientations", typeof(SailfishOrientations), typeof(Page), SailfishOrientations.Default,
		propertyChanged: (bindable, _, _) => SailfishRenderSession.OfElement(bindable as Element)?.RequestPoll());

	public static SailfishOrientations GetAllowedOrientations(BindableObject element) =>
		(SailfishOrientations)element.GetValue(AllowedOrientationsProperty);

	public static void SetAllowedOrientations(BindableObject element, SailfishOrientations value) =>
		element.SetValue(AllowedOrientationsProperty, value);

	public static SailfishOrientations GetAllowedOrientations(this IPlatformElementConfiguration<SailfishOS, FormsElement> config) =>
		GetAllowedOrientations(config.Element);

	public static IPlatformElementConfiguration<SailfishOS, FormsElement> SetAllowedOrientations(
		this IPlatformElementConfiguration<SailfishOS, FormsElement> config, SailfishOrientations value)
	{
		SetAllowedOrientations(config.Element, value);
		return config;
	}

	/// <summary>The page's own setting, else the nearest container's; <see cref="SailfishOrientations.Default"/> when none.</summary>
	internal static SailfishOrientations Effective(Element page)
	{
		for (var e = page; e is not null; e = e.Parent)
			if (e is FormsElement && GetAllowedOrientations(e) is var value && value != SailfishOrientations.Default)
				return value;
		return SailfishOrientations.Default;
	}
}
