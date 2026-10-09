using System.Collections;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.Maui.SailfishOS.Build.Tasks;
using Xunit;

namespace Linux.SailfishOS.Tests;

/// <summary>Tracker S56: the RPM writer task (no rpmbuild/python3, so Windows can package). The package is read back
/// here the way rpm reads it: lead, signature header (padded to 8), main header, gzip cpio payload.</summary>
public sealed class RpmPackTests : IDisposable
{
	private readonly string _dir = Path.Combine(Path.GetTempPath(), "sf-rpmpack-" + Guid.NewGuid().ToString("N"));

	public RpmPackTests() => Directory.CreateDirectory(_dir);

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

	private sealed record Header(Dictionary<int, object> Tags, int Length);

	/// <summary>Index entries → strings (string[]) / ints (long[]) / bytes, and the header's length.</summary>
	private static Header ReadHeader(byte[] rpm, int at)
	{
		static int Be(byte[] b, int o) => (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];
		Assert.Equal(new byte[] { 0x8E, 0xAD, 0xE8, 0x01 }, rpm[at..(at + 4)]);
		var n = Be(rpm, at + 8);
		var storeSize = Be(rpm, at + 12);
		var store = at + 16 + n * 16;
		var tags = new Dictionary<int, object>();
		var previous = -1;
		for (var i = 0; i < n; i++)
		{
			var e = at + 16 + i * 16;
			var (tag, type, offset, count) = (Be(rpm, e), Be(rpm, e + 4), Be(rpm, e + 8), Be(rpm, e + 12));
			if (i > 0)
			{
				Assert.True(tag > previous, $"index not sorted at tag {tag}");
				previous = tag;
			}
			var p = store + offset;
			tags[tag] = type switch
			{
				3 => Enumerable.Range(0, count).Select(k => (long)(ushort)((rpm[p + 2 * k] << 8) | rpm[p + 2 * k + 1])).ToArray(),
				4 => Enumerable.Range(0, count).Select(k => (long)Be(rpm, p + 4 * k)).ToArray(),
				6 or 8 or 9 => Strings(rpm, p, count),
				_ => rpm[p..(p + count)],
			};
			if (type == 4)
				Assert.Equal(0, offset % 4);
		}
		return new Header(tags, 16 + n * 16 + storeSize);
	}

	private static string[] Strings(byte[] b, int at, int count)
	{
		var result = new string[count];
		for (var i = 0; i < count; i++)
		{
			var end = Array.IndexOf(b, (byte)0, at);
			result[i] = Encoding.UTF8.GetString(b, at, end - at);
			at = end + 1;
		}
		return result;
	}

	private (Header Sig, Header Main, byte[] Payload, byte[] Rpm) Pack(SailfishRpmPack task)
	{
		var engine = new Engine();
		task.BuildEngine = engine;
		task.OutputFile = Path.Combine(_dir, "out", "app.rpm");
		Assert.True(task.Execute(), string.Join("; ", engine.Errors));
		var rpm = File.ReadAllBytes(task.OutputFile);
		Assert.Equal(new byte[] { 0xED, 0xAB, 0xEE, 0xDB }, rpm[..4]);
		var sig = ReadHeader(rpm, 96);
		var mainAt = 96 + sig.Length + (8 - sig.Length % 8) % 8;
		var main = ReadHeader(rpm, mainAt);
		var compressed = rpm[(mainAt + main.Length)..];
		using var gzip = new GZipStream(new MemoryStream(compressed), CompressionMode.Decompress);
		using var cpio = new MemoryStream();
		gzip.CopyTo(cpio);
		return (sig, main, cpio.ToArray(), rpm);
	}

	/// <summary>The cpio entries: name → (mode, data).</summary>
	private static Dictionary<string, (int Mode, byte[] Data)> ReadCpio(byte[] cpio)
	{
		var entries = new Dictionary<string, (int, byte[])>();
		var at = 0;
		while (true)
		{
			Assert.Equal("070701", Encoding.ASCII.GetString(cpio, at, 6));
			int Field(int i) => Convert.ToInt32(Encoding.ASCII.GetString(cpio, at + 6 + i * 8, 8), 16);
			var (mode, size, nameSize) = (Field(1), Field(6), Field(11));
			var name = Encoding.UTF8.GetString(cpio, at + 110, nameSize - 1);
			at += 110 + nameSize;
			at += (4 - at % 4) % 4;
			if (name == "TRAILER!!!")
				return entries;
			entries[name] = (mode, cpio[at..(at + size)]);
			at += size;
			at += (4 - at % 4) % 4;
		}
	}

	private string BuildRoot()
	{
		var root = Path.Combine(_dir, "root");
		var share = Path.Combine(root, "usr", "share", "harbour-x");
		Directory.CreateDirectory(Path.Combine(share, "sub"));
		File.WriteAllBytes(Path.Combine(share, "App"), [0x7F, (byte)'E', (byte)'L', (byte)'F', 2, 1]);
		File.WriteAllText(Path.Combine(share, "App.dll"), "managed");
		File.WriteAllText(Path.Combine(share, "sub", "run.sh"), "#!/bin/sh\necho hi\n");
		Directory.CreateDirectory(Path.Combine(root, "usr", "share", "applications"));
		File.WriteAllText(Path.Combine(root, "usr", "share", "applications", "harbour-x.desktop"), "[Desktop Entry]\n");
		File.WriteAllText(Path.Combine(root, "usr", "share", "not-listed.txt"), "left out");
		return root;
	}

	private SailfishRpmPack Task(string root) => new()
	{
		BuildRoot = root,
		Name = "harbour-x",
		Version = "1.2",
		Release = "7",
		Arch = "aarch64",
		Summary = "X",
		License = "MIT",
		Requires = "sailfishsecretsdaemon, libfoo >= 2.1",
		Files = [new TaskItem("/usr/share/harbour-x"), new TaskItem("/usr/share/applications/harbour-x.desktop")],
		Symlinks = [new TaskItem("/usr/bin/harbour-x", new Dictionary<string, string> { ["LinkTarget"] = "../share/harbour-x/App" })],
	};

	[Fact]
	public void The_package_holds_the_listed_files_with_modes_digests_and_the_declared_link()
	{
		var (_, main, payload, _) = Pack(Task(BuildRoot()));
		var t = main.Tags;

		Assert.Equal(new[] { "harbour-x" }, t[1000]);
		Assert.Equal(new[] { "aarch64" }, t[1022]);
		Assert.Equal(new[] { "gzip" }, t[1125]);
		var dirs = (string[])t[1118];
		var names = ((long[])t[1116]).Zip((string[])t[1117], (d, b) => dirs[d] + b).ToArray();
		Assert.Equal(new[]
		{
			"/usr/bin/harbour-x", "/usr/share/applications/harbour-x.desktop", "/usr/share/harbour-x", "/usr/share/harbour-x/App",
			"/usr/share/harbour-x/App.dll", "/usr/share/harbour-x/sub", "/usr/share/harbour-x/sub/run.sh",
		}, names);
		var modes = ((long[])t[1030]).Select(m => Convert.ToString(m, 8)).ToArray();
		Assert.Equal(new[] { "120777", "100644", "40755", "100755", "100644", "40755", "100755" }, modes);
		Assert.Equal("../share/harbour-x/App", ((string[])t[1036])[0]);
		Assert.Equal(Convert.ToHexString(SHA256.HashData("managed"u8)).ToLowerInvariant(), ((string[])t[1035])[4]);
		Assert.Equal(new[] { "sailfishsecretsdaemon", "libfoo" }, t[1049]);
		Assert.Equal(new long[] { 0, 0x0C }, t[1048]);

		var cpio = ReadCpio(payload);
		Assert.Equal(names.Select(n => n.TrimStart('/')), cpio.Keys);
		Assert.Equal("../share/harbour-x/App", Encoding.UTF8.GetString(cpio["usr/bin/harbour-x"].Data));
		Assert.Equal("managed", Encoding.UTF8.GetString(cpio["usr/share/harbour-x/App.dll"].Data));
	}

	[Fact]
	public void The_signature_covers_the_main_header_and_the_payload_as_rpm_checks_it()
	{
		var (sig, main, payload, rpm) = Pack(Task(BuildRoot()));
		var mainAt = 96 + sig.Length + (8 - sig.Length % 8) % 8;
		var mainBytes = rpm[mainAt..(mainAt + main.Length)];
		var compressed = rpm[(mainAt + main.Length)..];

		Assert.Equal(Convert.ToHexString(SHA256.HashData(mainBytes)).ToLowerInvariant(), ((string[])sig.Tags[273])[0]);
		Assert.Equal(Convert.ToHexString(SHA1.HashData(mainBytes)).ToLowerInvariant(), ((string[])sig.Tags[269])[0]);
		Assert.Equal(MD5.HashData([.. mainBytes, .. compressed]), (byte[])sig.Tags[1004]);
		Assert.Equal(mainBytes.Length + compressed.Length, ((long[])sig.Tags[1000])[0]);
		Assert.Equal(payload.Length, ((long[])sig.Tags[1007])[0]);
		Assert.Equal(Convert.ToHexString(SHA256.HashData(compressed)).ToLowerInvariant(), ((string[])main.Tags[5092])[0]);
	}

	[Fact]
	public void Packaging_runs_no_unix_program_unless_host_rpmbuild_is_asked_for()
	{
		var targets = File.ReadAllText(Path.Combine(Repo.Root, "src/Linux.SailfishOS/buildTransitive/Microsoft.Maui.Platforms.SailfishOS.targets"));

		// Whole elements: an Exec's Condition may sit on its next line.
		var execs = System.Text.RegularExpressions.Regex.Matches(targets, @"<Exec\b[^>]*>").Select(m => m.Value).ToList();
		Assert.DoesNotContain(execs, e => e.Contains("python3", StringComparison.Ordinal) || e.Contains("command -v", StringComparison.Ordinal));
		var unix = execs
			.Where(e => e.Contains("chmod", StringComparison.Ordinal) || e.Contains("ln -sf", StringComparison.Ordinal) || e.Contains("find '", StringComparison.Ordinal)).ToList();
		Assert.NotEmpty(unix);
		Assert.All(unix, l => Assert.Contains("'$(SailfishRpmBuilder)' == 'rpmbuild'", l));
	}

	[Fact]
	public void A_listed_path_missing_from_the_buildroot_fails_the_build()
	{
		var task = Task(BuildRoot());
		task.Files = [.. task.Files, new TaskItem("/usr/share/icons/missing.png")];
		var engine = new Engine();
		task.BuildEngine = engine;
		task.OutputFile = Path.Combine(_dir, "out", "app.rpm");

		Assert.False(task.Execute());
		Assert.Contains("/usr/share/icons/missing.png", engine.Errors.Single());
	}
}
