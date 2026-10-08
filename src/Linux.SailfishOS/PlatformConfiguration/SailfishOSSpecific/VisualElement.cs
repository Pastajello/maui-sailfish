using Microsoft.Maui.Controls;

namespace Microsoft.Maui.Controls.PlatformConfiguration.SailfishOSSpecific;

/// <summary>
/// Sailfish OS specifics of a view (decision D13: the namespace Android, iOS and Windows use).
/// </summary>
public static class VisualElement
{
	/// <summary>
	/// Whether a drag the view's Pan, Swipe or Pinch recognizers capture stays theirs (default true): the page's back
	/// swipe and pull-down menu wait until the finger lifts, as a view that handles its own touches keeps them on Android
	/// and iOS. False lets Silica take the same drag as well (a horizontal pan also drags the page back, a vertical one at
	/// the top also opens the pulley).
	/// </summary>
	public static readonly BindableProperty KeepsDragProperty = BindableProperty.CreateAttached(
		"KeepsDrag", typeof(bool), typeof(VisualElement), true);

	public static bool GetKeepsDrag(BindableObject element) => (bool)element.GetValue(KeepsDragProperty);

	public static void SetKeepsDrag(BindableObject element, bool value) => element.SetValue(KeepsDragProperty, value);

	public static bool GetKeepsDrag(this IPlatformElementConfiguration<SailfishOS, Microsoft.Maui.Controls.VisualElement> config) =>
		GetKeepsDrag(config.Element);

	public static IPlatformElementConfiguration<SailfishOS, Microsoft.Maui.Controls.VisualElement> SetKeepsDrag(
		this IPlatformElementConfiguration<SailfishOS, Microsoft.Maui.Controls.VisualElement> config, bool value)
	{
		SetKeepsDrag(config.Element, value);
		return config;
	}
}
