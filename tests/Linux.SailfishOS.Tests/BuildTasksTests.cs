using System.Collections;
using System.IO.Compression;
using Microsoft.Build.Framework;
using Microsoft.Maui.SailfishOS.Build.Tasks;
using Xunit;

namespace Linux.SailfishOS.Tests;

/// <summary>The packaging tasks of the targets, run on their own (they used to be inline code compiled per build).</summary>
public class BuildTasksTests : IDisposable
{
	private readonly string _dir = Path.Combine(Path.GetTempPath(), "sf-buildtasks-" + Guid.NewGuid().ToString("N"));

	public BuildTasksTests() => Directory.CreateDirectory(_dir);

	public void Dispose() => Directory.Delete(_dir, recursive: true);

	private sealed class Engine : IBuildEngine
	{
		public readonly List<string> Errors = new();
		public bool ContinueOnError => false;
		public int LineNumberOfTaskNode => 0;
		public int ColumnNumberOfTaskNode => 0;
		public string ProjectFileOfTaskNode => "test";
		public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs) => true;
		public void LogCustomEvent(CustomBuildEventArgs e) { }
		public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e.Message ?? string.Empty);
		public void LogMessageEvent(BuildMessageEventArgs e) { }
		public void LogWarningEvent(BuildWarningEventArgs e) { }
	}

	/// <summary>An 8-bit RGBA, non-interlaced PNG of one colour.</summary>
	private static byte[] Png(int width, int height)
	{
		using var raw = new MemoryStream();
		for (var y = 0; y < height; y++)
		{
			raw.WriteByte(0);   // filter: none
			for (var x = 0; x < width; x++)
				raw.Write(new byte[] { 0x20, 0x80, 0xE0, 0xFF });
		}
		using var idat = new MemoryStream();
		using (var z = new ZLibStream(idat, CompressionLevel.Fastest, leaveOpen: true))
			raw.WriteTo(z);
		using var png = new MemoryStream();
		png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
		var ihdr = new byte[13];
		BE(ihdr, 0, (uint)width);
		BE(ihdr, 4, (uint)height);
		ihdr[8] = 8;   // bit depth
		ihdr[9] = 6;   // RGBA
		Chunk(png, "IHDR", ihdr);
		Chunk(png, "IDAT", idat.ToArray());
		Chunk(png, "IEND", Array.Empty<byte>());
		return png.ToArray();
	}

	private static void BE(byte[] b, int at, uint v)
	{
		b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v;
	}

	private static void Chunk(Stream s, string kind, byte[] data)
	{
		var len = new byte[4];
		BE(len, 0, (uint)data.Length);
		s.Write(len);
		var head = System.Text.Encoding.ASCII.GetBytes(kind);
		s.Write(head);
		s.Write(data);
		var crc = new byte[4];
		BE(crc, 0, Crc(head.Concat(data)));
		s.Write(crc);
	}

	private static uint Crc(IEnumerable<byte> bytes)
	{
		var crc = 0xFFFFFFFFu;
		foreach (var b in bytes)
		{
			crc ^= b;
			for (var k = 0; k < 8; k++)
				crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
		}
		return crc ^ 0xFFFFFFFFu;
	}

	private static (int W, int H) PngSize(string path)
	{
		var b = File.ReadAllBytes(path);
		Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, b[..4]);
		return ((b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19], (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23]);
	}

	[Fact]
	public void The_icon_set_has_one_png_per_size()
	{
		var source = Path.Combine(_dir, "appicon.png");
		File.WriteAllBytes(source, Png(172, 172));
		var engine = new Engine();
		var task = new SailfishIconSet { BuildEngine = engine, Source = source, HicolorDir = Path.Combine(_dir, "hicolor"), Name = "harbour-x", Sizes = "86;108;128;172" };

		Assert.True(task.Execute(), string.Join("; ", engine.Errors));

		foreach (var size in new[] { 86, 108, 128, 172 })
			Assert.Equal((size, size), PngSize(Path.Combine(_dir, "hicolor", $"{size}x{size}", "apps", "harbour-x.png")));
		Assert.Equal(4, task.Files.Length);
	}

	[Fact]
	public void A_non_square_icon_is_refused()
	{
		var source = Path.Combine(_dir, "wide.png");
		File.WriteAllBytes(source, Png(40, 20));
		var engine = new Engine();
		var task = new SailfishIconSet { BuildEngine = engine, Source = source, HicolorDir = Path.Combine(_dir, "hicolor"), Name = "x", Sizes = "86" };

		Assert.False(task.Execute());
		Assert.Contains(engine.Errors, e => e.Contains("square", StringComparison.Ordinal));
	}

	[Fact]
	public void The_launcher_rpath_fills_the_placeholder_and_a_longer_one_is_refused()
	{
		var marker = "/__SAILFISH_LAUNCHER_RPATH__"u8.ToArray();
		var file = Path.Combine(_dir, "launcher");
		File.WriteAllBytes(file, [.. new byte[] { 1, 2, 3 }, .. marker, .. new byte[16], 9]);

		var engine = new Engine();
		Assert.True(new SailfishPatchLauncherRpath { BuildEngine = engine, File = file, Rpath = "$ORIGIN/lib" }.Execute());
		var patched = File.ReadAllBytes(file);
		Assert.Equal("$ORIGIN/lib"u8.ToArray(), patched[3..(3 + 11)]);
		Assert.Equal(0, patched[3 + 11]);
		Assert.Equal(9, patched[^1]);

		var tooLong = new string('x', marker.Length + 1);
		File.WriteAllBytes(file, [.. marker, 0]);
		Assert.False(new SailfishPatchLauncherRpath { BuildEngine = engine, File = file, Rpath = tooLong }.Execute());
		Assert.Contains(engine.Errors, e => e.Contains("longer than the launcher placeholder", StringComparison.Ordinal));
	}
}
