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

	/// <summary>Decodes PNG bytes (8-bit gray/RGB/RGBA, non-interlaced).</summary>
	public static DiagPng FromBytes(byte[] png) => Decode(png);

	/// <summary>An 8-bit RGBA PNG of <paramref name="width"/> × <paramref name="height"/> with the pixels
	/// <paramref name="pixel"/> gives (unfiltered rows), to feed a decoder under test.</summary>
	public static byte[] Encode(int width, int height, Func<int, int, (byte R, byte G, byte B, byte A)> pixel)
	{
		var raw = new byte[(width * 4 + 1) * height];
		for (var y = 0; y < height; y++)
		for (var x = 0; x < width; x++)
		{
			var (r, g, b, a) = pixel(x, y);
			var o = y * (width * 4 + 1) + 1 + x * 4;
			(raw[o], raw[o + 1], raw[o + 2], raw[o + 3]) = (r, g, b, a);
		}
		using var compressed = new MemoryStream();
		using (var z = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
			z.Write(raw);
		var ihdr = new byte[13];
		BigEndian(ihdr, 0, width);
		BigEndian(ihdr, 4, height);
		(ihdr[8], ihdr[9]) = (8, 6);   // 8-bit RGBA; compression, filter, interlace 0
		using var png = new MemoryStream();
		png.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
		Chunk(png, "IHDR", ihdr);
		Chunk(png, "IDAT", compressed.ToArray());
		Chunk(png, "IEND", []);
		return png.ToArray();
	}

	private static void BigEndian(byte[] buffer, int at, int value)
	{
		buffer[at] = (byte)(value >> 24);
		buffer[at + 1] = (byte)(value >> 16);
		buffer[at + 2] = (byte)(value >> 8);
		buffer[at + 3] = (byte)value;
	}

	private static void Chunk(Stream png, string kind, byte[] data)
	{
		var head = new byte[8];
		BigEndian(head, 0, data.Length);
		System.Text.Encoding.ASCII.GetBytes(kind, 0, 4, head, 4);
		png.Write(head);
		png.Write(data);
		var crc = new byte[4];
		BigEndian(crc, 0, (int)Crc32(head.AsSpan(4), data));
		png.Write(crc);
	}

	private static uint Crc32(ReadOnlySpan<byte> kind, ReadOnlySpan<byte> data)
	{
		var crc = 0xFFFFFFFFu;
		foreach (var span in new[] { kind.ToArray(), data.ToArray() })
		foreach (var b in span)
		{
			crc ^= b;
			for (var k = 0; k < 8; k++)
				crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
		}
		return crc ^ 0xFFFFFFFFu;
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
