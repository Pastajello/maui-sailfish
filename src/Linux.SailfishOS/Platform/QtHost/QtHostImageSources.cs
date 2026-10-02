using Microsoft.Maui.Controls;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// Image sources the backend does not know (a library's own <see cref="ImageSource"/> types, such as SkiaSharp's
/// <c>SKBitmapImageSource</c>). A resolver turns one into a URL Qt can load, usually a PNG it wrote to
/// <see cref="CacheDirectory"/>, and returns null for sources that are not its own. It is asked wherever MAUI takes
/// an <see cref="ImageSource"/> (Image, ImageButton, Button and toolbar icons). Qt thread.
/// </summary>
public static class QtHostImageSources
{
	private static readonly List<Func<ImageSource, string?>> Resolvers = new();

	/// <summary>Adds a resolver; the first one that returns a URL wins.</summary>
	public static void Register(Func<ImageSource, string?> resolver)
	{
		ArgumentNullException.ThrowIfNull(resolver);
		if (!Resolvers.Contains(resolver))
			Resolvers.Add(resolver);
	}

	/// <summary>The app's cache directory for generated images of <paramref name="kind"/> (created on demand).</summary>
	public static string CacheDirectory(string kind) => SailfishAppPaths.Cache(kind);

	internal static string? Resolve(ImageSource source)
	{
		foreach (var resolver in Resolvers)
		{
			try
			{
				if (resolver(source) is { } url)
					return url;
			}
			catch (Exception ex)
			{
				QtHostDiag.Warn(QtHostDiagChannel.QmlProperty, $"image source resolver failed for {source.GetType().Name}: {ex.Message}");
			}
		}
		return null;
	}
}
