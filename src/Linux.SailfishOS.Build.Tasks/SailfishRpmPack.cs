using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Microsoft.Maui.SailfishOS.Build.Tasks;

/// <summary>
/// Writes the app's RPM (tracker S56): lead, signature header, main header and a gzip-compressed cpio (newc) payload,
/// the layout of tools/py/sf-rpmbuild.py (checked against releases.jolla.com packages and the phone's rpm 4.16), with
/// no rpmbuild, python3 or shell on the build host, so a Windows machine builds the package too. The metadata comes
/// from MSBuild instead of a spec file.
///
/// File modes do not come from the host (Windows has none): directories and ELF or "#!" files are 0755, everything
/// else 0644, the modes the targets used to set with chmod. Symlinks are declared (<see cref="Symlinks"/>), not read
/// from the disk.
/// </summary>
public class SailfishRpmPack : Task
{
	/// <summary>The staged root: <c>BuildRoot/usr/share/&lt;package&gt;/…</c>.</summary>
	[Required] public string BuildRoot { get; set; } = string.Empty;

	/// <summary>The .rpm to write.</summary>
	[Required] public string OutputFile { get; set; } = string.Empty;

	[Required] public string Name { get; set; } = string.Empty;
	[Required] public string Version { get; set; } = string.Empty;
	[Required] public string Release { get; set; } = string.Empty;

	/// <summary>The RPM arch (aarch64, armv7hl, x86_64).</summary>
	[Required] public string Arch { get; set; } = string.Empty;

	public string Summary { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public string License { get; set; } = string.Empty;
	public string Vendor { get; set; } = string.Empty;
	public string Group { get; set; } = string.Empty;

	/// <summary>rpm's dependency syntax: "a, b >= 1.2".</summary>
	public string Requires { get; set; } = string.Empty;

	/// <summary>The %files entries: absolute package paths ("/usr/bin/x"); a directory takes everything below it.</summary>
	[Required] public ITaskItem[] Files { get; set; } = Array.Empty<ITaskItem>();

	/// <summary>Symlinks to package: Include = package path, <c>LinkTarget</c> metadata = the link text. A declared path
	/// needs no file in the buildroot.</summary>
	public ITaskItem[] Symlinks { get; set; } = Array.Empty<ITaskItem>();

	/// <summary>The payload size before compression (diagnostics).</summary>
	[Output] public long PayloadSize { get; set; }

	internal const int ModeDirectory = 0x4000 | 0x1ED;   // 040755
	internal const int ModeExecutable = 0x8000 | 0x1ED;  // 0100755
	internal const int ModeRegular = 0x8000 | 0x1A4;     // 0100644
	internal const int ModeSymlink = 0xA000 | 0x1FF;     // 0120777

	internal sealed class Entry
	{
		public string Path = string.Empty;        // "usr/bin/x": no leading slash, '/' separators
		public string? Source;                    // a regular file's path on disk
		public string LinkTarget = string.Empty;  // a symlink's text
		public int Mode;
		public long Size;
		public int MTime;
		public string Digest = string.Empty;
		public bool IsDirectory => (Mode & 0xF000) == 0x4000;
		public bool IsSymlink => (Mode & 0xF000) == 0xA000;
	}

	public override bool Execute()
	{
		try
		{
			var entries = Collect();
			Write(entries);
			Log.LogMessage(MessageImportance.Normal, $"SailfishRpm: {entries.Count} entries, payload {PayloadSize} bytes → {new FileInfo(OutputFile).Length} bytes");
			return true;
		}
		catch (RpmException ex)
		{
			Log.LogError(ex.Message);
			return false;
		}
	}

	private sealed class RpmException : Exception
	{
		public RpmException(string message) : base(message)
		{
		}
	}

	/* --- the file list --- */

	internal List<Entry> Collect()
	{
		var root = Path.GetFullPath(BuildRoot);
		if (!Directory.Exists(root))
			throw new RpmException($"SailfishRpm: the buildroot does not exist: {root}");
		var items = new SortedDictionary<string, Entry>(StringComparer.Ordinal);
		foreach (var link in Symlinks)
		{
			var path = Normalize(link.ItemSpec);
			var target = link.GetMetadata("LinkTarget");
			if (string.IsNullOrEmpty(target))
				throw new RpmException($"SailfishRpm: symlink /{path} has no LinkTarget");
			items[path] = new Entry { Path = path, LinkTarget = target, Mode = ModeSymlink, Size = Encoding.UTF8.GetByteCount(target), MTime = Now() };
		}
		foreach (var file in Files)
		{
			var path = Normalize(file.ItemSpec);
			if (path.Length == 0 || items.ContainsKey(path))
				continue;
			Add(items, root, path, explicitEntry: true);
		}
		if (items.Count == 0)
			throw new RpmException("SailfishRpm: the file list resolved to nothing");
		return items.Values.ToList();
	}

	private void Add(SortedDictionary<string, Entry> items, string root, string path, bool explicitEntry)
	{
		if (items.ContainsKey(path))
			return;
		var disk = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
		if (Directory.Exists(disk))
		{
			if ((File.GetAttributes(disk) & FileAttributes.ReparsePoint) != 0)
				throw new RpmException($"SailfishRpm: /{path} is a link on disk; declare it as a Symlinks item");
			items[path] = new Entry { Path = path, Mode = ModeDirectory, MTime = MTime(Directory.GetLastWriteTimeUtc(disk)) };
			foreach (var child in Directory.GetFileSystemEntries(disk).OrderBy(p => p, StringComparer.Ordinal))
				Add(items, root, path + "/" + Path.GetFileName(child), explicitEntry: false);
			return;
		}
		if (!File.Exists(disk))
		{
			if (explicitEntry)
				throw new RpmException($"SailfishRpm: %files entry missing from the buildroot: /{path}");
			return;
		}
		var info = new FileInfo(disk);
		if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
			throw new RpmException($"SailfishRpm: /{path} is a link on disk; declare it as a Symlinks item");
		items[path] = new Entry { Path = path, Source = disk, Mode = ModeFor(disk), Size = info.Length, MTime = MTime(info.LastWriteTimeUtc) };
	}

	/// <summary>ELF and "#!" files are executables (the apphost, native libraries, the launcher); the rest is data.</summary>
	internal static int ModeFor(string file)
	{
		var head = new byte[4];
		int read;
		using (var stream = File.OpenRead(file))
			read = stream.Read(head, 0, 4);
		var elf = read == 4 && head[0] == 0x7F && head[1] == (byte)'E' && head[2] == (byte)'L' && head[3] == (byte)'F';
		var script = read >= 2 && head[0] == (byte)'#' && head[1] == (byte)'!';
		return elf || script ? ModeExecutable : ModeRegular;
	}

	private static string Normalize(string path) => path.Replace('\\', '/').Trim().Trim('/');

	private static int MTime(DateTime utc) =>
		(int)Math.Max(0, Math.Min(int.MaxValue, (utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds));

	private static int Now() => MTime(DateTime.UtcNow);

	/* --- the package --- */

	private void Write(List<Entry> entries)
	{
		var payload = OutputFile + ".payload.tmp";
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(OutputFile))!);
		try
		{
			using (var file = File.Create(payload))
			using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
				PayloadSize = WriteCpio(gzip, entries);
			var main = MainHeader(entries, payload).Serialize();
			var sig = SignatureHeader(main, payload).Serialize();
			using var output = File.Create(OutputFile);
			output.Write(Lead(), 0, 96);
			output.Write(sig, 0, sig.Length);
			var pad = (8 - sig.Length % 8) % 8;
			output.Write(new byte[pad], 0, pad);
			output.Write(main, 0, main.Length);
			using var source = File.OpenRead(payload);
			source.CopyTo(output);
		}
		finally
		{
			try { File.Delete(payload); } catch (IOException) { }
		}
	}

	private RpmHeader MainHeader(List<Entry> entries, string payload)
	{
		var h = new RpmHeader(RpmHeader.TagMainRegion);
		h.String(1000, Name);
		h.String(1001, Version);
		h.String(1002, Release);
		var summary = string.IsNullOrWhiteSpace(Summary) ? Name : Summary;
		h.I18n(1004, summary);
		h.I18n(1005, string.IsNullOrWhiteSpace(Description) ? summary : Description.Trim());
		h.Int32(1006, Now());
		h.String(1007, "localhost");
		var total = entries.Sum(e => e.Size);
		if (total > uint.MaxValue)
			h.Int64(1009, total);
		else
			h.Int32(1009, unchecked((int)(uint)total));
		if (!string.IsNullOrWhiteSpace(Vendor))
			h.String(1011, Vendor);
		if (!string.IsNullOrWhiteSpace(License))
			h.String(1014, License);
		h.I18n(1016, string.IsNullOrWhiteSpace(Group) ? "Unspecified" : Group);
		h.String(1021, "linux");
		h.String(1022, Arch);
		h.String(1064, "4.20.0");
		h.String(1124, "cpio");
		h.String(1125, "gzip");
		h.String(1126, "6");
		h.Int32(5011, 8);   // FILEDIGESTALGO: sha256
		var requires = ParseRequires(Requires);
		if (requires.Count > 0)
		{
			h.Int32(1048, requires.Select(r => r.Flags).ToArray());
			h.Strings(1049, requires.Select(r => r.Name).ToArray());
			h.Strings(1050, requires.Select(r => r.Version).ToArray());
		}

		var dirnames = new List<string>();
		var dirIndex = new Dictionary<string, int>(StringComparer.Ordinal);
		var dirindexes = new List<int>();
		var basenames = new List<string>();
		foreach (var entry in entries)
		{
			var slash = entry.Path.LastIndexOf('/');
			var dir = "/" + (slash < 0 ? string.Empty : entry.Path.Substring(0, slash + 1));
			if (!dirIndex.TryGetValue(dir, out var index))
			{
				index = dirIndex[dir] = dirnames.Count;
				dirnames.Add(dir);
			}
			dirindexes.Add(index);
			basenames.Add(slash < 0 ? entry.Path : entry.Path.Substring(slash + 1));
		}
		var n = entries.Count;
		h.Strings(1117, basenames.ToArray());
		h.Strings(1118, dirnames.ToArray());
		h.Int32(1116, dirindexes.ToArray());
		h.Int32(1028, entries.Select(e => unchecked((int)e.Size)).ToArray());
		h.Int16(1030, entries.Select(e => (short)e.Mode).ToArray());
		h.Int16(1033, new short[n]);
		h.Int32(1034, entries.Select(e => e.MTime).ToArray());
		h.Strings(1035, entries.Select(e => e.Digest).ToArray());
		h.Strings(1036, entries.Select(e => e.LinkTarget).ToArray());
		h.Int32(1037, new int[n]);
		h.Strings(1039, Enumerable.Repeat("root", n).ToArray());
		h.Strings(1040, Enumerable.Repeat("root", n).ToArray());
		h.Int32(1045, Enumerable.Repeat(-1, n).ToArray());
		h.Int32(1095, Enumerable.Repeat(1, n).ToArray());
		h.Int32(1096, Enumerable.Range(1, n).ToArray());
		h.Strings(1097, Enumerable.Repeat(string.Empty, n).ToArray());
		// rpm >= 4.14 checks a sha256 of the compressed payload.
		h.Strings(5092, new[] { Hex(Hash(SHA256.Create(), payload)) });
		h.Int32(5093, 8);
		return h;
	}

	/// <summary>SHA1/SHA256 cover the main header only; MD5 and SIZE cover the main header plus the compressed payload;
	/// PAYLOADSIZE is the uncompressed cpio size (what the phone's rpm 4.16 computes).</summary>
	private RpmHeader SignatureHeader(byte[] main, string payload)
	{
		var sig = new RpmHeader(RpmHeader.TagSignatureRegion);
		var payloadLength = new FileInfo(payload).Length;
		sig.Int32(1000, unchecked((int)(main.Length + payloadLength)));
		using (var md5 = MD5.Create())
		{
			md5.TransformBlock(main, 0, main.Length, null, 0);
			using var stream = File.OpenRead(payload);
			var buffer = new byte[1 << 16];
			int read;
			while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
				md5.TransformBlock(buffer, 0, read, null, 0);
			md5.TransformFinalBlock(buffer, 0, 0);
			sig.Binary(1004, md5.Hash!);
		}
		sig.Int32(1007, unchecked((int)PayloadSize));
		using (var sha1 = SHA1.Create())
			sig.String(269, Hex(sha1.ComputeHash(main)));
		using (var sha256 = SHA256.Create())
			sig.String(273, Hex(sha256.ComputeHash(main)));
		return sig;
	}

	private byte[] Lead()
	{
		var lead = new byte[96];
		lead[0] = 0xED; lead[1] = 0xAB; lead[2] = 0xEE; lead[3] = 0xDB;
		lead[4] = 3;
		var nvr = Encoding.UTF8.GetBytes($"{Name}-{Version}-{Release}");
		Array.Copy(nvr, 0, lead, 10, Math.Min(nvr.Length, 65));
		lead[77] = 1;   // os: linux
		lead[79] = 5;   // signature type: header signatures
		return lead;
	}

	/* --- cpio (newc) --- */

	/// <summary>Writes the archive and fills each file's sha256 (read once for both); returns the archive size.</summary>
	internal static long WriteCpio(Stream sink, List<Entry> entries)
	{
		long size = 0;
		void Put(byte[] data, int count)
		{
			sink.Write(data, 0, count);
			size += count;
		}
		void Pad(long count)
		{
			var pad = (int)((4 - count % 4) % 4);
			if (pad > 0)
				Put(new byte[pad], pad);
		}
		void Head(int ino, int mode, int nlink, int mtime, long fileSize, string name)
		{
			var raw = Encoding.UTF8.GetBytes(name + "\0");
			var fields = new long[] { ino, mode, 0, 0, nlink, mtime, fileSize, 0, 0, 0, 0, raw.Length, 0 };
			var head = Encoding.ASCII.GetBytes("070701" + string.Concat(fields.Select(f => ((uint)f).ToString("X8"))));
			Put(head, head.Length);
			Put(raw, raw.Length);
			Pad(110 + raw.Length);
		}

		var ino = 0;
		var buffer = new byte[1 << 18];
		foreach (var entry in entries)
		{
			ino++;
			if (entry.IsDirectory)
			{
				Head(ino, entry.Mode, 2, entry.MTime, 0, entry.Path);
			}
			else if (entry.IsSymlink)
			{
				var target = Encoding.UTF8.GetBytes(entry.LinkTarget);
				Head(ino, entry.Mode, 1, entry.MTime, target.Length, entry.Path);
				Put(target, target.Length);
				Pad(target.Length);
			}
			else
			{
				Head(ino, entry.Mode, 1, entry.MTime, entry.Size, entry.Path);
				using var sha = SHA256.Create();
				using (var source = File.OpenRead(entry.Source!))
				{
					long copied = 0;
					int read;
					while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
					{
						sha.TransformBlock(buffer, 0, read, null, 0);
						Put(buffer, read);
						copied += read;
					}
					if (copied != entry.Size)
						throw new RpmException($"SailfishRpm: /{entry.Path} changed while packaging ({copied} of {entry.Size} bytes)");
				}
				sha.TransformFinalBlock(buffer, 0, 0);
				entry.Digest = Hex(sha.Hash!);
				Pad(entry.Size);
			}
		}
		Head(0, 0, 1, 0, 0, "TRAILER!!!");
		return size;
	}

	/* --- helpers --- */

	internal static List<(string Name, int Flags, string Version)> ParseRequires(string value)
	{
		var sense = new Dictionary<string, int> { ["<"] = 0x02, [">"] = 0x04, ["="] = 0x08, ["<="] = 0x0A, [">="] = 0x0C };
		var tokens = (value ?? string.Empty).Replace(",", " ").Split(new[] { ' ', '\t', '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries);
		var deps = new List<(string, int, string)>();
		for (var i = 0; i < tokens.Length;)
		{
			if (i + 2 < tokens.Length && sense.TryGetValue(tokens[i + 1], out var flags))
			{
				deps.Add((tokens[i], flags, tokens[i + 2]));
				i += 3;
			}
			else
			{
				deps.Add((tokens[i], 0, string.Empty));
				i++;
			}
		}
		return deps;
	}

	private static byte[] Hash(HashAlgorithm algorithm, string file)
	{
		using (algorithm)
		using (var stream = File.OpenRead(file))
			return algorithm.ComputeHash(stream);
	}

	internal static string Hex(byte[] bytes) => string.Concat(bytes.Select(b => b.ToString("x2")));
}

/// <summary>An RPM header: tag-sorted index entries over a type-aligned data store, closed by the immutable region
/// trailer (-(nindex·16) as a signed int32 at the end of the store).</summary>
internal sealed class RpmHeader
{
	internal const int TagSignatureRegion = 62;
	internal const int TagMainRegion = 63;
	private const int TypeInt16 = 3, TypeInt32 = 4, TypeInt64 = 5, TypeString = 6, TypeBinary = 7, TypeStringArray = 8, TypeI18n = 9;

	private readonly int _region;
	private readonly SortedDictionary<int, (int Type, byte[] Data, int Count)> _entries = new();

	public RpmHeader(int region) => _region = region;

	private void Put(int tag, int type, byte[] data, int count)
	{
		if (_entries.ContainsKey(tag))
			throw new InvalidOperationException($"duplicate header tag {tag}");
		_entries[tag] = (type, data, count);
	}

	private static byte[] Strings0(IEnumerable<string> values) =>
		values.SelectMany(v => Encoding.UTF8.GetBytes(v ?? string.Empty).Concat(new byte[] { 0 })).ToArray();

	public void String(int tag, string value) => Put(tag, TypeString, Strings0(new[] { value }), 1);
	public void I18n(int tag, string value) => Put(tag, TypeI18n, Strings0(new[] { value }), 1);
	public void Strings(int tag, string[] values) => Put(tag, TypeStringArray, Strings0(values), values.Length);
	public void Binary(int tag, byte[] value) => Put(tag, TypeBinary, value, value.Length);

	public void Int16(int tag, short[] values) =>
		Put(tag, TypeInt16, values.SelectMany(v => new[] { (byte)(v >> 8), (byte)v }).ToArray(), values.Length);

	public void Int32(int tag, params int[] values) => Put(tag, TypeInt32, values.SelectMany(Be32).ToArray(), values.Length);

	public void Int64(int tag, long value) =>
		Put(tag, TypeInt64, Be32((int)(value >> 32)).Concat(Be32((int)value)).ToArray(), 1);

	private static byte[] Be32(int v) => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };

	public byte[] Serialize()
	{
		var store = new List<byte>();
		var index = new List<(int Tag, int Type, int Offset, int Count)>();
		foreach (var pair in _entries)
		{
			var (type, data, count) = pair.Value;
			var align = type switch { TypeInt16 => 2, TypeInt32 => 4, TypeInt64 => 8, _ => 1 };
			while (store.Count % align != 0)
				store.Add(0);
			index.Add((pair.Key, type, store.Count, count));
			store.AddRange(data);
		}
		var nindex = index.Count + 1;
		var trailerOffset = store.Count;
		store.AddRange(Be32(_region));
		store.AddRange(Be32(TypeBinary));
		store.AddRange(Be32(-(nindex * 16)));
		store.AddRange(Be32(16));
		var output = new List<byte> { 0x8E, 0xAD, 0xE8, 0x01, 0, 0, 0, 0 };
		output.AddRange(Be32(nindex));
		output.AddRange(Be32(store.Count));
		foreach (var (tag, type, offset, count) in new[] { (_region, TypeBinary, trailerOffset, 16) }.Concat(index))
		{
			output.AddRange(Be32(tag));
			output.AddRange(Be32(type));
			output.AddRange(Be32(offset));
			output.AddRange(Be32(count));
		}
		output.AddRange(store);
		return output.ToArray();
	}
}
