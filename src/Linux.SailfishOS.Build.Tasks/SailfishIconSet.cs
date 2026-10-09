// Moved from an inline RoslynCodeTaskFactory task in buildTransitive/Microsoft.Maui.Platforms.SailfishOS.targets.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Microsoft.Maui.SailfishOS.Build.Tasks;

/// <summary>
/// Builds the Sailfish hicolor icon set from the MauiIcon: one PNG per size (box-filtered from the source, which must
/// be a square, non-interlaced 8-bit PNG), written as hicolor/&lt;n&gt;x&lt;n&gt;/apps/&lt;Name&gt;.png.
/// </summary>
public class SailfishIconSet : Task
{
	[Required] public string Source { get; set; }
	[Required] public string HicolorDir { get; set; }
	[Required] public string Name { get; set; }
	[Required] public string Sizes { get; set; }
	[Output] public ITaskItem[] Files { get; set; }

	public override bool Execute()
	{
		int w, h;
		float[] rgba;   // premultiplied, 0..1
		try
		{
			rgba = Decode(File.ReadAllBytes(Source), out w, out h);
		}
		catch (Exception ex)
		{
			Log.LogError("MauiIcon " + Source + ": " + ex.Message +
						 " (the Sailfish icon set needs a non-interlaced 8-bit PNG; set -p:SailfishSkipIconCheck=true to copy it as the 108 px icon unchanged)");
			return false;
		}
		if (w != h)
		{
			Log.LogError("MauiIcon must be square for the Sailfish icon set (got " + w + "x" + h + " from " + Source + ")");
			return false;
		}
		var files = new List<ITaskItem>();
		foreach (var part in Sizes.Split(';'))
		{
			int size;
			if (!int.TryParse(part.Trim(), out size) || size <= 0)
				continue;
			var dir = Path.Combine(HicolorDir, size + "x" + size, "apps");
			Directory.CreateDirectory(dir);
			var dest = Path.Combine(dir, Name + ".png");
			if (size == w)
				File.Copy(Source, dest, true);
			else
			{
				if (size > w)
					Log.LogWarning("MauiIcon " + Source + " is " + w + "x" + w + " — upscaled to " + size + "x" + size +
								   "; ship a source of at least 172x172 for crisp Sailfish icons");
				File.WriteAllBytes(dest, Encode(Resize(rgba, w, h, size, size), size, size));
			}
			files.Add(new TaskItem("/usr/share/icons/hicolor/" + size + "x" + size + "/apps/" + Name + ".png"));
		}
		Files = files.ToArray();
		return !Log.HasLoggedErrors;
	}

	// ---- PNG decode (8-bit gray/gray+alpha/RGB/RGBA/palette, non-interlaced) ----
	static float[] Decode(byte[] png, out int w, out int h)
	{
		if (png.Length < 8 || png[0] != 0x89 || png[1] != (byte)'P' || png[2] != (byte)'N' || png[3] != (byte)'G')
			throw new InvalidDataException("not a PNG");
		int pos = 8, depth = 0, type = 0, interlace = 0;
		w = h = 0;
		byte[] palette = null, trns = null;
		var idat = new MemoryStream();
		while (pos + 8 <= png.Length)
		{
			int len = (png[pos] << 24) | (png[pos + 1] << 16) | (png[pos + 2] << 8) | png[pos + 3];
			string kind = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
			int data = pos + 8;
			if (kind == "IHDR")
			{
				w = (png[data] << 24) | (png[data + 1] << 16) | (png[data + 2] << 8) | png[data + 3];
				h = (png[data + 4] << 24) | (png[data + 5] << 16) | (png[data + 6] << 8) | png[data + 7];
				depth = png[data + 8]; type = png[data + 9]; interlace = png[data + 12];
			}
			else if (kind == "PLTE") { palette = new byte[len]; Array.Copy(png, data, palette, 0, len); }
			else if (kind == "tRNS") { trns = new byte[len]; Array.Copy(png, data, trns, 0, len); }
			else if (kind == "IDAT") idat.Write(png, data, len);
			else if (kind == "IEND") break;
			pos = data + len + 4;
		}
		if (w <= 0 || h <= 0) throw new InvalidDataException("missing IHDR");
		if (depth != 8) throw new InvalidDataException("bit depth " + depth + " is not supported");
		if (interlace != 0) throw new InvalidDataException("interlaced PNGs are not supported");
		int channels = type == 0 ? 1 : type == 2 ? 3 : type == 3 ? 1 : type == 4 ? 2 : type == 6 ? 4 : 0;
		if (channels == 0) throw new InvalidDataException("color type " + type + " is not supported");
		if (type == 3 && palette == null) throw new InvalidDataException("palette image without PLTE");
		int stride = w * channels;
		var raw = new byte[(stride + 1) * h];
		var zlib = idat.ToArray();
		using (var inflate = new DeflateStream(new MemoryStream(zlib, 2, zlib.Length - 2), CompressionMode.Decompress))
		{
			int read = 0, n;
			while (read < raw.Length && (n = inflate.Read(raw, read, raw.Length - read)) > 0)
				read += n;
			if (read != raw.Length) throw new InvalidDataException("truncated image data");
		}
		var pixels = new byte[stride * h];
		var prev = new byte[stride];
		for (int y = 0; y < h; y++)
		{
			int filter = raw[y * (stride + 1)];
			int src = y * (stride + 1) + 1, dst = y * stride;
			for (int x = 0; x < stride; x++)
			{
				int a = x >= channels ? pixels[dst + x - channels] : 0;
				int b = prev[x];
				int c = x >= channels ? prev[x - channels] : 0;
				int v = raw[src + x];
				switch (filter)
				{
					case 1: v += a; break;
					case 2: v += b; break;
					case 3: v += (a + b) >> 1; break;
					case 4:
						int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
						v += pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
						break;
				}
				pixels[dst + x] = (byte)v;
			}
			Array.Copy(pixels, dst, prev, 0, stride);
		}
		var outp = new float[w * h * 4];
		for (int i = 0; i < w * h; i++)
		{
			float r, g, bl, al = 1f;
			int o = i * channels;
			switch (type)
			{
				case 0: r = g = bl = pixels[o] / 255f; if (trns != null && trns.Length >= 2 && pixels[o] == trns[1]) al = 0f; break;
				case 2: r = pixels[o] / 255f; g = pixels[o + 1] / 255f; bl = pixels[o + 2] / 255f; break;
				case 3:
					int idx = pixels[o];
					r = palette[idx * 3] / 255f; g = palette[idx * 3 + 1] / 255f; bl = palette[idx * 3 + 2] / 255f;
					if (trns != null && idx < trns.Length) al = trns[idx] / 255f;
					break;
				case 4: r = g = bl = pixels[o] / 255f; al = pixels[o + 1] / 255f; break;
				default: r = pixels[o] / 255f; g = pixels[o + 1] / 255f; bl = pixels[o + 2] / 255f; al = pixels[o + 3] / 255f; break;
			}
			outp[i * 4] = r * al; outp[i * 4 + 1] = g * al; outp[i * 4 + 2] = bl * al; outp[i * 4 + 3] = al;
		}
		return outp;
	}

	// ---- separable tent-filter resampling (area-like when shrinking) ----
	static float[] Resize(float[] src, int sw, int sh, int dw, int dh)
	{
		var tmp = new float[dw * sh * 4];
		Pass(src, sw, sh, tmp, dw, true);
		var dst = new float[dw * dh * 4];
		Pass(tmp, dw, sh, dst, dh, false);
		return dst;
	}

	static void Pass(float[] src, int sw, int sh, float[] dst, int dlen, bool horizontal)
	{
		int slen = horizontal ? sw : sh, lines = horizontal ? sh : sw;
		double scale = (double)slen / dlen, support = Math.Max(1.0, scale);
		for (int d = 0; d < dlen; d++)
		{
			double center = (d + 0.5) * scale;
			int lo = Math.Max(0, (int)Math.Floor(center - support)), hi = Math.Min(slen - 1, (int)Math.Ceiling(center + support));
			var weights = new double[hi - lo + 1];
			double total = 0;
			for (int s = lo; s <= hi; s++)
			{
				double wgt = Math.Max(0.0, 1.0 - Math.Abs(s + 0.5 - center) / support);
				weights[s - lo] = wgt; total += wgt;
			}
			for (int line = 0; line < lines; line++)
			{
				double r = 0, g = 0, b = 0, a = 0;
				for (int s = lo; s <= hi; s++)
				{
					int i = horizontal ? (line * sw + s) * 4 : (s * sw + line) * 4;
					double wgt = weights[s - lo];
					r += src[i] * wgt; g += src[i + 1] * wgt; b += src[i + 2] * wgt; a += src[i + 3] * wgt;
				}
				int o = horizontal ? (line * dlen + d) * 4 : (d * sw + line) * 4;
				dst[o] = (float)(r / total); dst[o + 1] = (float)(g / total); dst[o + 2] = (float)(b / total); dst[o + 3] = (float)(a / total);
			}
		}
	}

	// ---- PNG encode (RGBA8, filter None, zlib-wrapped deflate) ----
	static byte[] Encode(float[] px, int w, int h)
	{
		var raw = new byte[(w * 4 + 1) * h];
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				int i = (y * w + x) * 4, o = y * (w * 4 + 1) + 1 + x * 4;
				float a = px[i + 3];
				for (int c = 0; c < 3; c++)
					raw[o + c] = ToByte(a > 0 ? px[i + c] / a : 0f);
				raw[o + 3] = ToByte(a);
			}
		var z = new MemoryStream();
		z.WriteByte(0x78); z.WriteByte(0x9C);
		using (var deflate = new DeflateStream(z, CompressionLevel.Optimal, true))
			deflate.Write(raw, 0, raw.Length);
		uint s1 = 1, s2 = 0;
		foreach (var bt in raw) { s1 = (s1 + bt) % 65521; s2 = (s2 + s1) % 65521; }
		WriteBE(z, (s2 << 16) | s1);
		var png = new MemoryStream();
		png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
		var ihdr = new MemoryStream();
		WriteBE(ihdr, (uint)w); WriteBE(ihdr, (uint)h);
		ihdr.Write(new byte[] { 8, 6, 0, 0, 0 }, 0, 5);
		Chunk(png, "IHDR", ihdr.ToArray());
		Chunk(png, "IDAT", z.ToArray());
		Chunk(png, "IEND", new byte[0]);
		return png.ToArray();
	}

	static byte ToByte(float v) { return (byte)Math.Max(0, Math.Min(255, (int)Math.Round(v * 255f))); }

	static void WriteBE(Stream s, uint v)
	{
		s.WriteByte((byte)(v >> 24)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v);
	}

	static uint[] _crc;

	static void Chunk(Stream s, string kind, byte[] data)
	{
		if (_crc == null)
		{
			_crc = new uint[256];
			for (uint n = 0; n < 256; n++)
			{
				uint c = n;
				for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
				_crc[n] = c;
			}
		}
		WriteBE(s, (uint)data.Length);
		var head = System.Text.Encoding.ASCII.GetBytes(kind);
		s.Write(head, 0, 4); s.Write(data, 0, data.Length);
		uint crc = 0xFFFFFFFFu;
		foreach (var b in head) crc = _crc[(crc ^ b) & 0xFF] ^ (crc >> 8);
		foreach (var b in data) crc = _crc[(crc ^ b) & 0xFF] ^ (crc >> 8);
		WriteBE(s, crc ^ 0xFFFFFFFFu);
	}
}
