using Microsoft.Maui.SailfishOS.Platform;

namespace Trailhead.Features.Shared;

/// <summary>
/// The ambience's colours as app resources (<c>{DynamicResource Highlight}</c> …), so text that is not a native Silica
/// control still follows the ambience, and changes with it while the app runs.
/// </summary>
public static class ThemeResources
{
	public const string Primary = nameof(Primary);
	public const string Secondary = nameof(Secondary);
	public const string Highlight = nameof(Highlight);
	public const string SecondaryHighlight = nameof(SecondaryHighlight);

	public static void Attach(Application app)
	{
		Apply(app);
		SailfishTheme.ColorsChanged += () => Apply(app);
	}

	/// <summary>The colours are known once the window is up; before that the fallbacks (Silica's dark-ambience
	/// defaults) keep the first frame readable.</summary>
	public static void Apply(Application app)
	{
		var r = app.Resources;
		r[Primary] = SailfishTheme.PrimaryColor ?? Colors.White;
		r[Secondary] = SailfishTheme.SecondaryColor ?? Color.FromRgba(255, 255, 255, 153);
		r[Highlight] = SailfishTheme.HighlightColor ?? Color.FromArgb("#87C3FF");
		r[SecondaryHighlight] = SailfishTheme.SecondaryHighlightColor ?? Color.FromArgb("#5A8FC4");
	}
}
