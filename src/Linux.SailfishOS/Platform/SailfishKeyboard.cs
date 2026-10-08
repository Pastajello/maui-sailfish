using Microsoft.Maui.Controls;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// The on-screen keyboard (Maliit) from code, where MAUI's <c>SoftInputExtensions</c> (<c>view.ShowSoftInputAsync()</c>,
/// <c>HideSoftInputAsync</c>, <c>IsSoftInputShowing</c>) cannot reach on this target framework: they are compiled
/// for the platform heads only and throw on <c>net11.0</c> (an upstream seam, tracker S45).
/// </summary>
/// <example><code>SailfishKeyboard.Show(searchEntry); … if (SailfishKeyboard.IsShowing) SailfishKeyboard.Hide();</code></example>
public static class SailfishKeyboard
{
	/// <summary>Whether the keyboard is on screen (Qt.inputMethod.visible).</summary>
	public static bool IsShowing => QtThread.Run(() => QtHostRuntime.IsRunning && QtHostRuntime.Eval("Qt.inputMethod.visible") == "true");

	/// <summary>Focuses <paramref name="view"/> (an Entry, Editor or SearchBar) and opens the keyboard for it.</summary>
	public static void Show(View view)
	{
		ArgumentNullException.ThrowIfNull(view);
		QtThread.Run(() =>
		{
			view.Focus();
			if (QtHostRuntime.IsRunning)
				QtHostRuntime.Eval("Qt.inputMethod.show()");
			return true;
		});
	}

	/// <summary>Closes the keyboard; the focused text input loses focus, so the keyboard does not come straight back.</summary>
	public static void Hide()
	{
		QtThread.Run(() =>
		{
			if (Application.Current?.Windows.FirstOrDefault()?.Page is { } root)
				foreach (var input in Inputs(root))
					if (input.IsFocused)
						input.Unfocus();
			if (QtHostRuntime.IsRunning)
				QtHostRuntime.Eval("Qt.inputMethod.hide()");
			return true;
		});
	}

	private static IEnumerable<InputView> Inputs(Element element)
	{
		if (element is InputView input)
			yield return input;
		foreach (var child in ((IVisualTreeElement)element).GetVisualChildren().OfType<Element>())
			foreach (var nested in Inputs(child))
				yield return nested;
	}
}
