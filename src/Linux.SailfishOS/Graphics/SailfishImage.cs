using Microsoft.Maui.Graphics;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Graphics;

// Inside the namespace: Microsoft.Maui.IImage (the view contract) would win over a file-level alias.
using IImage = Microsoft.Maui.Graphics.IImage;

/// <summary>
/// MAUI's <see cref="IImage"/> on Sailfish (tracker S49, decision D6 a): encoded image bytes that QImage in the native
/// shim decodes, resizes and re-encodes. Pixels never cross into managed code; every operation hands bytes to the shim
/// and gets bytes back, so an instance holds no native handle. On the plain net11.0 target MAUI's own
/// <c>PlatformImage</c> only keeps the bytes (its Downsize and Resize throw); <see cref="FromStream"/>, the registered
/// <see cref="IImageLoadingService"/> or <see cref="From(IImage)"/> give the working one.
/// </summary>
public sealed class SailfishImage : IImage
{
	private byte[]? _data;

	private SailfishImage(byte[] data, ImageFormat format, int width, int height)
	{
		_data = data;
		Format = format;
		Width = width;
		Height = height;
	}

	/// <summary>Decoded width in pixels (EXIF orientation applied).</summary>
	public float Width { get; }

	/// <summary>Decoded height in pixels (EXIF orientation applied).</summary>
	public float Height { get; }

	/// <summary>The format of the encoded bytes (read from their header; the hint when the header is unknown).</summary>
	public ImageFormat Format { get; }

	/// <summary>The encoded bytes this image holds (the canvas draws from them).</summary>
	internal byte[] Data => _data ?? throw new ObjectDisposedException(nameof(SailfishImage));

	/// <summary>Reads encoded image bytes (PNG, JPEG, GIF, BMP, WebP, … whatever the phone's Qt image plugins read).</summary>
	/// <exception cref="InvalidDataException">The bytes do not decode.</exception>
	public static SailfishImage FromStream(Stream stream, ImageFormat format = ImageFormat.Png)
	{
		ArgumentNullException.ThrowIfNull(stream);
		using var copy = new MemoryStream();
		stream.CopyTo(copy);
		return FromBytes(copy.ToArray(), format);
	}

	/// <inheritdoc cref="FromStream"/>
	public static SailfishImage FromBytes(byte[] data, ImageFormat format = ImageFormat.Png)
	{
		ArgumentNullException.ThrowIfNull(data);
		if (data.Length == 0 || !QtHostRuntime.TryImageInfo(data, out var width, out var height))
			throw new InvalidDataException($"Not a decodable image ({data.Length} bytes): {QtHostRuntime.LastErrorText}");
		return new SailfishImage(data, Sniff(data) ?? format, width, height);
	}

	/// <summary>The Sailfish image for any <see cref="IImage"/>: itself, or MAUI's plain <c>PlatformImage</c> (or another
	/// image that can save itself) re-read from its bytes.</summary>
	public static SailfishImage From(IImage image)
	{
		ArgumentNullException.ThrowIfNull(image);
		if (image is SailfishImage sailfish)
			return sailfish;
		if (image is Microsoft.Maui.Graphics.Platform.PlatformImage { Bytes: { Length: > 0 } bytes })
			return FromBytes(bytes);
		using var stream = new MemoryStream();
		image.Save(stream);
		return FromBytes(stream.ToArray());
	}

	/// <summary>Scales down so neither side exceeds <paramref name="maxWidthOrHeight"/>, keeping the aspect ratio; an
	/// image already within it comes back unscaled.</summary>
	public IImage Downsize(float maxWidthOrHeight, bool disposeOriginal = false) =>
		Downsize(maxWidthOrHeight, maxWidthOrHeight, disposeOriginal);

	/// <summary>Scales down to fit within <paramref name="maxWidth"/> × <paramref name="maxHeight"/>, keeping the aspect
	/// ratio; an image already within it comes back unscaled.</summary>
	public IImage Downsize(float maxWidth, float maxHeight, bool disposeOriginal = false)
	{
		var scale = Math.Min(maxWidth / Width, maxHeight / Height);
		if (scale >= 1)
			return Result(new SailfishImage(Data, Format, (int)Width, (int)Height), disposeOriginal);
		var (w, h) = TargetSize(Width * scale, Height * scale);
		return Transform(w, h, "stretch", WritableFormat(Format), 100, disposeOriginal);
	}

	/// <summary>Resizes to <paramref name="width"/> × <paramref name="height"/>: <see cref="ResizeMode.Fit"/> letterboxes
	/// (transparent bars), <see cref="ResizeMode.Bleed"/> covers and crops the centre, <see cref="ResizeMode.Stretch"/>
	/// distorts.</summary>
	public IImage Resize(float width, float height, ResizeMode resizeMode = ResizeMode.Fit, bool disposeOriginal = false)
	{
		var (w, h) = TargetSize(width, height);
		var mode = resizeMode switch
		{
			ResizeMode.Bleed => "fill",
			ResizeMode.Stretch => "stretch",
			_ => "fit",
		};
		return Transform(w, h, mode, WritableFormat(Format), 100, disposeOriginal);
	}

	/// <summary>Writes the image as <paramref name="format"/>; <paramref name="quality"/> (0–1) applies to JPEG. The
	/// held bytes go out unchanged when they already are that format at full quality.</summary>
	public void Save(Stream stream, ImageFormat format = ImageFormat.Png, float quality = 1)
	{
		ArgumentNullException.ThrowIfNull(stream);
		stream.Write(Encode(format, quality));
	}

	/// <inheritdoc cref="Save"/>
	public async Task SaveAsync(Stream stream, ImageFormat format = ImageFormat.Png, float quality = 1)
	{
		ArgumentNullException.ThrowIfNull(stream);
		var bytes = await Task.Run(() => Encode(format, quality)).ConfigureAwait(false);
		await stream.WriteAsync(bytes).ConfigureAwait(false);
	}

	/// <summary>This image: the Sailfish image is the platform image.</summary>
	public IImage ToPlatformImage() => this;

	/// <summary>Draws the image over <paramref name="dirtyRect"/>.</summary>
	public void Draw(ICanvas canvas, RectF dirtyRect) =>
		canvas.DrawImage(this, dirtyRect.Left, dirtyRect.Top, dirtyRect.Width, dirtyRect.Height);

	public void Dispose() => _data = null;

	private byte[] Encode(ImageFormat format, float quality)
	{
		var data = Data;
		if (format == Format && (quality >= 1 || format != ImageFormat.Jpeg))
			return data;
		var op = Op((int)Width, (int)Height, "keep", format, Quality(quality));
		return QtHostRuntime.ImageTransform(data, op, (int)Width, (int)Height)
		       ?? throw new NotSupportedException($"Saving as {format}: {QtHostRuntime.LastErrorText}");
	}

	private SailfishImage Transform(int w, int h, string mode, ImageFormat format, int quality, bool disposeOriginal)
	{
		var bytes = QtHostRuntime.ImageTransform(Data, Op(w, h, mode, format, quality), w, h)
		            ?? throw new InvalidOperationException($"Image {mode} to {w}×{h}: {QtHostRuntime.LastErrorText}");
		return Result(new SailfishImage(bytes, format, w, h), disposeOriginal);
	}

	private SailfishImage Result(SailfishImage result, bool disposeOriginal)
	{
		if (disposeOriginal)
			Dispose();
		return result;
	}

	private static (int W, int H) TargetSize(float width, float height) =>
		(Math.Max(1, (int)MathF.Round(width)), Math.Max(1, (int)MathF.Round(height)));

	private static int Quality(float quality) => (int)MathF.Round(Math.Clamp(quality, 0, 1) * 100);

	/// <summary>Qt reads GIF but does not write it; a resized GIF comes out as PNG.</summary>
	private static ImageFormat WritableFormat(ImageFormat format) => format == ImageFormat.Gif ? ImageFormat.Png : format;

	internal static string Op(int w, int h, string mode, ImageFormat format, int quality) =>
		System.FormattableString.Invariant(
			$"{{\"w\":{w},\"h\":{h},\"mode\":\"{mode}\",\"format\":\"{FormatName(format)}\",\"quality\":{quality}}}");

	private static string FormatName(ImageFormat format) => format switch
	{
		ImageFormat.Jpeg => "jpg",
		ImageFormat.Gif => "gif",
		ImageFormat.Tiff => "tiff",
		ImageFormat.Bmp => "bmp",
		_ => "png",
	};

	/// <summary>The format from the bytes' header, null when it is none of MAUI's five.</summary>
	internal static ImageFormat? Sniff(ReadOnlySpan<byte> data) => data switch
	{
		[0x89, (byte)'P', (byte)'N', (byte)'G', ..] => ImageFormat.Png,
		[0xFF, 0xD8, ..] => ImageFormat.Jpeg,
		[(byte)'G', (byte)'I', (byte)'F', (byte)'8', ..] => ImageFormat.Gif,
		[(byte)'B', (byte)'M', ..] => ImageFormat.Bmp,
		[(byte)'I', (byte)'I', 0x2A, 0x00, ..] or [(byte)'M', (byte)'M', 0x00, 0x2A, ..] => ImageFormat.Tiff,
		_ => null,
	};
}

/// <summary>MAUI's image loading service on Sailfish: <see cref="SailfishImage"/>s. Resolved from the app's services
/// (<c>IImageLoadingService</c>) unless the app registers its own.</summary>
public sealed class SailfishImageLoadingService : IImageLoadingService
{
	public IImage FromStream(Stream stream, ImageFormat formatHint = ImageFormat.Png) => SailfishImage.FromStream(stream, formatHint);
}

/// <summary>A screenshot as an <see cref="IImage"/> (tracker S50): MAUI's <c>IScreenshotResult</c> only opens streams on
/// this target framework, so this reads the PNG into a <see cref="SailfishImage"/> to resize, save or draw.</summary>
public static class SailfishScreenshotExtensions
{
	public static async Task<SailfishImage> ToImageAsync(this Microsoft.Maui.Media.IScreenshotResult result)
	{
		ArgumentNullException.ThrowIfNull(result);
		if (result is Platform.SailfishScreenshot.ScreenshotImage sailfish)
			return SailfishImage.FromBytes(sailfish.Png);
		await using var stream = await result.OpenReadAsync().ConfigureAwait(false);
		return SailfishImage.FromStream(stream);
	}
}
