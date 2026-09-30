using System.IO.Compression;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Decodes <c>QtHostRuntime.GrabPng</c> output (8-bit gray/RGB/RGBA, non-interlaced) so checks
/// can assert on actual window pixels rather than QML properties.
/// </summary>
internal sealed class DiagPng
{
	private readonly byte[] _pixels;
	private readonly int _channels;

	public int Width { get; }

	public int Height { get; }

	private DiagPng(int width, int height, int channels, byte[] pixels)
	{
		Width = width;
		Height = height;
		_channels = channels;
		_pixels = pixels;
	}

	/// <summary>(r, g, b, a) at a pixel, clamped to the image.</summary>
	public (int R, int G, int B, int A) At(int x, int y)
	{
		x = Math.Clamp(x, 0, Width - 1);
		y = Math.Clamp(y, 0, Height - 1);
		var o = (y * Width + x) * _channels;
		return _channels switch
		{
			1 => (_pixels[o], _pixels[o], _pixels[o], 255),
			2 => (_pixels[o], _pixels[o], _pixels[o], _pixels[o + 1]),
			3 => (_pixels[o], _pixels[o + 1], _pixels[o + 2], 255),
			_ => (_pixels[o], _pixels[o + 1], _pixels[o + 2], _pixels[o + 3]),
		};
	}

	/// <summary>Perceived brightness 0..255 of a pixel.</summary>
	public int Luma(int x, int y)
	{
		var (r, g, b, _) = At(x, y);
		return (r * 299 + g * 587 + b * 114) / 1000;
	}

	public static DiagPng? TryLoad(string path)
	{
		try
		{
			return Decode(File.ReadAllBytes(path));
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[Sailfish] DiagPng: {path}: {ex.Message}");
			return null;
		}
	}

	private static DiagPng Decode(byte[] png)
	{
		if (png.Length < 8 || png[1] != 'P' || png[2] != 'N' || png[3] != 'G')
			throw new InvalidDataException("not a PNG");
		int pos = 8, w = 0, h = 0, depth = 0, type = 0, interlace = 0;
		var idat = new MemoryStream();
		while (pos + 8 <= png.Length)
		{
			var len = (png[pos] << 24) | (png[pos + 1] << 16) | (png[pos + 2] << 8) | png[pos + 3];
			var kind = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
			var data = pos + 8;
			if (kind == "IHDR")
			{
				w = (png[data] << 24) | (png[data + 1] << 16) | (png[data + 2] << 8) | png[data + 3];
				h = (png[data + 4] << 24) | (png[data + 5] << 16) | (png[data + 6] << 8) | png[data + 7];
				depth = png[data + 8];
				type = png[data + 9];
				interlace = png[data + 12];
			}
			else if (kind == "IDAT")
				idat.Write(png, data, len);
			else if (kind == "IEND")
				break;
			pos = data + len + 4;
		}
		var channels = type switch { 0 => 1, 2 => 3, 4 => 2, 6 => 4, _ => 0 };
		if (depth != 8 || interlace != 0 || channels == 0)
			throw new InvalidDataException($"unsupported PNG (depth {depth}, type {type}, interlace {interlace})");
		var stride = w * channels;
		var raw = new byte[(stride + 1) * h];
		idat.Position = 0;
		using (var z = new ZLibStream(idat, CompressionMode.Decompress))
			z.ReadExactly(raw);
		var pixels = new byte[stride * h];
		for (var y = 0; y < h; y++)
		{
			var filter = raw[y * (stride + 1)];
			var src = y * (stride + 1) + 1;
			var dst = y * stride;
			for (var x = 0; x < stride; x++)
			{
				var a = x >= channels ? pixels[dst + x - channels] : 0;
				var b = y > 0 ? pixels[dst - stride + x] : 0;
				var c = x >= channels && y > 0 ? pixels[dst - stride + x - channels] : 0;
				var v = raw[src + x];
				pixels[dst + x] = filter switch
				{
					1 => (byte)(v + a),
					2 => (byte)(v + b),
					3 => (byte)(v + ((a + b) >> 1)),
					4 => (byte)(v + Paeth(a, b, c)),
					_ => v,
				};
			}
		}
		return new DiagPng(w, h, channels, pixels);
	}

	private static int Paeth(int a, int b, int c)
	{
		int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
		return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
	}
}
