using System.Runtime.CompilerServices;
using Microsoft.Maui.SailfishOS.Graphics;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// The file URLs <c>ICanvas.DrawImage</c> and <c>ImagePaint</c> hand to the Canvas adapter (tracker S50): Context2D
/// loads images by URL, so an image's encoded bytes go to a cache file named by their hash, once per image object.
/// Any <see cref="Microsoft.Maui.Graphics.IImage"/> works: a <see cref="SailfishImage"/> or MAUI's plain
/// <c>PlatformImage</c> give their bytes, any other image saves itself as PNG.
/// </summary>
internal static class QtHostDrawnImages
{
	private static readonly ConditionalWeakTable<object, string> Urls = new();
	private static int _swept;

	/// <summary>The image's file URL, null when it has no bytes (disposed, or it cannot save itself).</summary>
	public static string? Url(Microsoft.Maui.Graphics.IImage? image)
	{
		if (image is null)
			return null;
		if (Urls.TryGetValue(image, out var known))
			return known;
		byte[] bytes;
		try
		{
			bytes = Bytes(image);
		}
		catch (Exception ex) when (ex is ObjectDisposedException or NotSupportedException or PlatformNotSupportedException or InvalidOperationException or IOException)
		{
			QtHostDiag.Warn(QtHostDiagChannel.QmlProperty, $"DrawImage: {image.GetType().Name} has no bytes to draw ({ex.Message})");
			return null;
		}
		if (bytes.Length == 0)
			return null;
		var extension = SailfishImage.Sniff(bytes) switch
		{
			Microsoft.Maui.Graphics.ImageFormat.Jpeg => ".jpg",
			Microsoft.Maui.Graphics.ImageFormat.Gif => ".gif",
			Microsoft.Maui.Graphics.ImageFormat.Bmp => ".bmp",
			Microsoft.Maui.Graphics.ImageFormat.Tiff => ".tiff",
			_ => ".png",
		};
		var path = Path.Combine(Dir(), Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(bytes)) + extension);
		try
		{
			if (!File.Exists(path))
			{
				// Written whole, then renamed: the canvas never loads a half-written file.
				var temp = path + "." + Environment.CurrentManagedThreadId + ".tmp";
				File.WriteAllBytes(temp, bytes);
				File.Move(temp, path, overwrite: true);
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			QtHostDiag.Warn(QtHostDiagChannel.QmlProperty, $"DrawImage: cannot write {path}: {ex.Message}");
			return null;
		}
		var url = new Uri(path).AbsoluteUri;
		Urls.AddOrUpdate(image, url);
		return url;
	}

	private static byte[] Bytes(Microsoft.Maui.Graphics.IImage image)
	{
		switch (image)
		{
			case SailfishImage sailfish:
				return sailfish.Data;
			case Microsoft.Maui.Graphics.Platform.PlatformImage { Bytes: { } bytes }:
				return bytes;
			default:
				using (var stream = new MemoryStream())
				{
					image.Save(stream);
					return stream.ToArray();
				}
		}
	}

	/// <summary>The cache, emptied once per process (the files of an earlier run belong to images that are gone).
	/// Content-named files are shared by equal images, so nothing is deleted while the app runs.</summary>
	private static string Dir()
	{
		var dir = SailfishAppPaths.Cache("drawn");
		if (Interlocked.Exchange(ref _swept, 1) == 0)
		{
			try
			{
				foreach (var file in Directory.EnumerateFiles(dir))
					File.Delete(file);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
			}
		}
		return dir;
	}
}
