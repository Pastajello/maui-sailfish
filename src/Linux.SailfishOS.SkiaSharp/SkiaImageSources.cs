using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using SkiaSharp;
using SkiaSharp.Views.Maui.Controls;

namespace Microsoft.Maui.SailfishOS.SkiaSharp;

/// <summary>
/// SkiaSharp's image sources on Sailfish, the counterpart of SkiaSharp's Android <c>SKImageSourceService</c>
/// (which converts them to an Android Bitmap): each one is encoded once to a PNG in the app's image cache, which
/// Qt loads like any file. A new <c>Image</c>/<c>Bitmap</c>/<c>Pixmap</c>/<c>Picture</c> (or new
/// <c>Dimensions</c>) writes a new file; a picture is rasterized at its <c>Dimensions</c>, as on Android.
/// </summary>
internal static class SkiaImageSources
{
	private sealed class Entry
	{
		public object? Key;
		public string? Path;
	}

	private static readonly ConditionalWeakTable<ImageSource, Entry> Entries = new();

	/// <summary>Files written (diagnostics).</summary>
	internal static int Encoded { get; private set; }

	public static string? Resolve(ImageSource source) => source switch
	{
		SKImageImageSource { Image: { } image } => Url(source, image, () => image.Encode(SKEncodedImageFormat.Png, 100)),
		SKBitmapImageSource { Bitmap: { } bitmap } => Url(source, bitmap, () => bitmap.Encode(SKEncodedImageFormat.Png, 100)),
		SKPixmapImageSource { Pixmap: { } pixmap } => Url(source, pixmap, () => pixmap.Encode(SKEncodedImageFormat.Png, 100)),
		SKPictureImageSource { Picture: { } picture } pictureSource =>
			Url(source, (picture, pictureSource.Dimensions), () => Rasterize(picture, pictureSource.Dimensions)),
		_ => null,
	};

	// Drawn onto a raster surface the size of Dimensions (SKImage.FromPicture crashes natively in SkiaSharp 3.116).
	private static SKData? Rasterize(SKPicture picture, SKSizeI dimensions)
	{
		if (dimensions.Width <= 0 || dimensions.Height <= 0)
			return null;
		using var surface = SKSurface.Create(new SKImageInfo(dimensions.Width, dimensions.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
		surface.Canvas.Clear(SKColors.Transparent);
		surface.Canvas.DrawPicture(picture);
		using var image = surface.Snapshot();
		return image.Encode(SKEncodedImageFormat.Png, 100);
	}

	private static string? Url(ImageSource source, object key, Func<SKData?> encode)
	{
		var entry = Entries.GetOrCreateValue(source);
		if (entry.Path is not null && Equals(entry.Key, key) && File.Exists(entry.Path))
			return new Uri(entry.Path).AbsoluteUri;
		using var data = encode();
		if (data is null)
			return null;
		if (entry.Path is not null)
			TryDelete(entry.Path);
		var path = System.IO.Path.Combine(QtHostImageSources.CacheDirectory("skia"), Guid.NewGuid().ToString("N") + ".png");
		using (var file = File.Create(path))
			data.SaveTo(file);
		entry.Key = key;
		entry.Path = path;
		Encoded++;
		return new Uri(path).AbsoluteUri;
	}

	private static void TryDelete(string path)
	{
		try
		{
			File.Delete(path);
		}
		catch (IOException)
		{
		}
	}
}
