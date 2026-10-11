using SkiaSharp;

namespace Trailhead.Data;

/// <summary>The 320×240 list thumbnail of a camera photo: decoded at a reduced size, centre-cropped to 4:3.</summary>
public static class PhotoThumbnails
{
	public static void Make(string photo, string thumbnail)
	{
		using var codec = SKCodec.Create(photo);
		if (codec is null)
			return;
		// Decode at the smallest size the codec offers that still covers the thumbnail.
		var scale = Math.Max(PhotoPainter.ThumbWidth / (float)codec.Info.Width, PhotoPainter.ThumbHeight / (float)codec.Info.Height);
		var size = codec.GetScaledDimensions(Math.Min(1, scale * 2));
		using var bitmap = SKBitmap.Decode(codec, new SKImageInfo(size.Width, size.Height));
		if (bitmap is null)
			return;
		var crop = Math.Min(bitmap.Width / 4f, bitmap.Height / 3f);
		var source = SKRect.Create((bitmap.Width - crop * 4) / 2, (bitmap.Height - crop * 3) / 2, crop * 4, crop * 3);
		using var surface = SKSurface.Create(new SKImageInfo(PhotoPainter.ThumbWidth, PhotoPainter.ThumbHeight));
		using var image = SKImage.FromBitmap(bitmap);
		surface.Canvas.DrawImage(image, source, new SKRect(0, 0, PhotoPainter.ThumbWidth, PhotoPainter.ThumbHeight),
			new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
		using var thumb = surface.Snapshot();
		using var data = thumb.Encode(SKEncodedImageFormat.Jpeg, 80);
		Directory.CreateDirectory(Path.GetDirectoryName(thumbnail)!);
		using var file = File.Create(thumbnail);
		data.SaveTo(file);
	}
}
