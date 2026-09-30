using Microsoft.Maui.Controls;

namespace SailfishKitchen.Helpers;

public static class AnimationExtensions
{
	/// <summary>
	/// One-shot fade-and-lift; each animated frame costs a Qt reconcile pass, so nothing loops. Opacity and
	/// TranslationY are honoured by the renderer; 3D rotation or non-uniform scale are ignored.
	/// </summary>
	public static async Task PlayEntranceAsync(this VisualElement view, double lift, uint fadeMs, uint liftMs)
	{
		try
		{
			view.Opacity = 0;
			view.TranslationY = lift;
			await Task.WhenAll(
				view.FadeToAsync(1, fadeMs, Easing.CubicOut),
				view.TranslateToAsync(0, 0, liftMs, Easing.CubicOut));
		}
		catch (Exception ex)
		{
			// An animation that cannot run must never take the page down.
			view.Opacity = 1;
			view.TranslationY = 0;
			System.Diagnostics.Debug.WriteLine($"entrance animation failed: {ex.Message}");
		}
	}
}
