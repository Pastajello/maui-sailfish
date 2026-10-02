using System.Buffers.Binary;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>Reads an image's pixel size from its header (PNG, JPEG, GIF, BMP, WebP) without decoding it.</summary>
internal static class ImageHeader
{
	public static (int Width, int Height)? PixelSize(Stream stream)
	{
		Span<byte> h = stackalloc byte[30];
		var n = ReadAtMost(stream, h);
		if (n >= 24 && h[0] == 0x89 && h[1] == (byte)'P' && h[12] == (byte)'I' && h[15] == (byte)'R')
			return Positive(BinaryPrimitives.ReadInt32BigEndian(h[16..]), BinaryPrimitives.ReadInt32BigEndian(h[20..]));
		if (n >= 10 && h[0] == (byte)'G' && h[1] == (byte)'I' && h[2] == (byte)'F')
			return Positive(BinaryPrimitives.ReadUInt16LittleEndian(h[6..]), BinaryPrimitives.ReadUInt16LittleEndian(h[8..]));
		if (n >= 26 && h[0] == (byte)'B' && h[1] == (byte)'M')
			// A bottom-up BMP stores a negative height.
			return Positive(BinaryPrimitives.ReadInt32LittleEndian(h[18..]), Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(h[22..])));
		if (n >= 30 && h[0] == (byte)'R' && h[1] == (byte)'I' && h[8] == (byte)'W' && h[9] == (byte)'E')
			return WebP(h);
		if (n >= 2 && h[0] == 0xFF && h[1] == 0xD8)
			return Jpeg(stream, h[..n]);
		return null;
	}

	private static (int, int)? WebP(ReadOnlySpan<byte> h)
	{
		var chunk = System.Text.Encoding.ASCII.GetString(h.Slice(12, 4));
		return chunk switch
		{
			// Lossy: 14-bit dimensions after the frame tag and start code.
			"VP8 " => Positive(BinaryPrimitives.ReadUInt16LittleEndian(h[26..]) & 0x3FFF,
				BinaryPrimitives.ReadUInt16LittleEndian(h[28..]) & 0x3FFF),
			// Lossless: 14-bit width-1 and height-1 packed after the signature byte.
			"VP8L" => Positive(1 + (h[21] | ((h[22] & 0x3F) << 8)),
				1 + ((h[22] >> 6) | (h[23] << 2) | ((h[24] & 0x0F) << 10))),
			// Extended: 24-bit canvas width-1 and height-1.
			"VP8X" => Positive(1 + (h[24] | (h[25] << 8) | (h[26] << 16)), 1 + (h[27] | (h[28] << 8) | (h[29] << 16))),
			_ => null,
		};
	}

	/// <summary>Walks the JPEG segments to the first start-of-frame marker.</summary>
	private static (int, int)? Jpeg(Stream stream, ReadOnlySpan<byte> head)
	{
		using var buffer = new MemoryStream();
		buffer.Write(head);
		var chunk = new byte[4096];
		var pos = 2;
		while (true)
		{
			// Each segment: FF marker, then a big-endian length that counts itself.
			if (!Ensure(stream, buffer, chunk, pos + 9))
				return null;
			var data = buffer.GetBuffer();
			if (data[pos] != 0xFF)
				return null;
			var marker = data[pos + 1];
			if (marker == 0xFF)
			{
				pos++;   // fill byte
				continue;
			}
			if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
				return Positive(BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos + 7)),
					BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos + 5)));
			if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7))
			{
				pos += 2;   // markers without a length
				continue;
			}
			pos += 2 + BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos + 2));
			if (pos > 1 << 20)
				return null;   // no frame header in the first MiB: give up
		}
	}

	private static bool Ensure(Stream stream, MemoryStream buffer, byte[] chunk, int length)
	{
		while (buffer.Length < length)
		{
			var read = stream.Read(chunk, 0, chunk.Length);
			if (read <= 0)
				return false;
			buffer.Write(chunk, 0, read);
		}
		return true;
	}

	private static int ReadAtMost(Stream stream, Span<byte> into)
	{
		var total = 0;
		while (total < into.Length)
		{
			var read = stream.Read(into[total..]);
			if (read <= 0)
				break;
			total += read;
		}
		return total;
	}

	private static (int, int)? Positive(int width, int height) => width > 0 && height > 0 ? (width, height) : null;
}
