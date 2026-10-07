using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Linux.SailfishOS.Tests;

/// <summary>The device tools ship in the NuGet package (buildTransitive/net11.0/tools) and are called by the
/// MSBuild targets and the template's .vscode files. A tool that is referenced but not packed only fails on a
/// package user's machine (that is how sf-preflight once called a missing sf-detect.sh), so the references are
/// checked here against the csproj's pack list.</summary>
public class ToolsPackagingTests
{
	private const string PackPrefix = "buildTransitive/net11.0/tools/";
	private static readonly string Tools = Path.Combine(Repo.Root, "tools");

	/// <summary>Packed tool files as paths relative to the package's tools/ dir, e.g. "cmd/run.sh".</summary>
	private static HashSet<string> Packed()
	{
		var csproj = Path.Combine(Repo.Root, "src/Linux.SailfishOS/Linux.SailfishOS.csproj");
		var packed = new HashSet<string>(StringComparer.Ordinal);
		foreach (var item in XDocument.Load(csproj).Descendants("None"))
		{
			var packagePath = (string?)item.Attribute("PackagePath") ?? "";
			if ((string?)item.Attribute("Pack") != "true" || !packagePath.StartsWith(PackPrefix, StringComparison.Ordinal))
				continue;
			var dir = packagePath[PackPrefix.Length..].Replace("%(Filename)%(Extension)", "");
			foreach (var include in ((string)item.Attribute("Include")!).Split(';', StringSplitOptions.RemoveEmptyEntries))
				packed.Add(dir.Contains('.') ? dir : dir + Path.GetFileName(include));
		}
		return packed;
	}

	/// <summary>The dispatcher's command table: command name → file under tools/.</summary>
	private static Dictionary<string, string> Commands() =>
		Regex.Matches(File.ReadAllText(Path.Combine(Tools, "sf")), @"^([a-z-]+)\|(sh|py|cs)\|([^|]+)\|", RegexOptions.Multiline)
			.ToDictionary(m => m.Groups[1].Value, m => m.Groups[3].Value);

	/// <summary>NuGet reads a PackagePath without an extension as a folder, so "tools/%(Filename)" turned the
	/// extension-less dispatcher into tools/sf/sf; folder paths ending in "/" keep every file name as is.</summary>
	[Fact]
	public void Tool_pack_paths_are_folders()
	{
		var csproj = Path.Combine(Repo.Root, "src/Linux.SailfishOS/Linux.SailfishOS.csproj");
		var paths = XDocument.Load(csproj).Descendants("None")
			.Select(i => (string?)i.Attribute("PackagePath") ?? "")
			.Where(p => p.StartsWith(PackPrefix, StringComparison.Ordinal))
			.ToList();
		Assert.NotEmpty(paths);
		Assert.All(paths, p => Assert.EndsWith("/", p));
	}

	/// <summary>The same NuGet rule for the native assets: the extension-less launcher packed as
	/// "native/sailfish-launcher" landed at native/sailfish-launcher/sailfish-launcher (tracker S06). A file without an
	/// extension needs a folder PackagePath.</summary>
	[Fact]
	public void Extension_less_native_assets_are_packed_into_a_folder()
	{
		var csproj = Path.Combine(Repo.Root, "src/Linux.SailfishOS/Linux.SailfishOS.csproj");
		var items = XDocument.Load(csproj).Descendants("None")
			.Select(i => (Include: (string?)i.Attribute("Include") ?? "", Path: (string?)i.Attribute("PackagePath") ?? ""))
			.Where(i => i.Path.StartsWith("runtimes/", StringComparison.Ordinal) && System.IO.Path.GetExtension(i.Include).Length == 0)
			.ToList();
		Assert.NotEmpty(items);   // the launchers
		Assert.All(items, i => Assert.EndsWith("/", i.Path));
	}

	[Fact]
	public void Every_packed_tool_exists_in_the_checkout()
	{
		var packed = Packed();
		Assert.Contains("sf", packed);
		Assert.All(packed, rel => Assert.True(File.Exists(Path.Combine(Tools, rel)), $"packed but missing: tools/{rel}"));
	}

	[Fact]
	public void Every_dispatcher_command_has_its_file()
	{
		var commands = Commands();
		Assert.True(commands.Count > 20, "the command table did not parse");
		Assert.All(commands, c => Assert.True(File.Exists(Path.Combine(Tools, c.Value)), $"sf {c.Key}: missing tools/{c.Value}"));
	}

	/// <summary>Calls a packed script makes only in a checkout (behind its IN_CHECKOUT test).</summary>
	private static readonly HashSet<string> CheckoutOnlyCalls = new(StringComparer.Ordinal)
	{
		"cmd/doctor.sh -> cmd/sysroot.sh",        // doctor --fix builds the sysroot
		"cmd/doctor.sh -> cmd/native-build.sh",   // doctor --fix builds the shim
	};

	[Fact]
	public void Packed_scripts_only_call_packed_files()
	{
		var packed = Packed();
		var commands = Commands();
		var missing = new List<string>();
		foreach (var rel in packed.Where(p => p.EndsWith(".sh", StringComparison.Ordinal) || p == "sf"))
		{
			var text = File.ReadAllText(Path.Combine(Tools, rel));
			var dir = Path.GetDirectoryName(rel)!.Replace('\\', '/');
			var calls = new List<string>();
			// siblings and neighbours of a script in cmd/: "$SCRIPT_DIR/run.sh", "$SCRIPT_DIR/../remote/x.sh"
			foreach (Match m in Regex.Matches(text, @"\$SCRIPT_DIR/(\.\./)?([A-Za-z0-9_./-]+\.(?:sh|py|cs))"))
				calls.Add(m.Groups[1].Success ? m.Groups[2].Value : (dir.Length > 0 ? dir + "/" : "") + m.Groups[2].Value);
			// the library's helpers: "$_SF_TOOLS_DIR/remote/sf-kill-remote.sh"
			foreach (Match m in Regex.Matches(text, @"\$_SF_TOOLS_DIR/([A-Za-z0-9_./-]+\.(?:sh|py))"))
				calls.Add(m.Groups[1].Value);
			// shims: exec bash ".../sf" <command>
			foreach (Match m in Regex.Matches(text, @"/sf"" ([a-z-]+)"))
				calls.Add(commands.TryGetValue(m.Groups[1].Value, out var file) ? file : "sf " + m.Groups[1].Value);
			missing.AddRange(calls.Where(c => !packed.Contains(c) && !CheckoutOnlyCalls.Contains($"{rel} -> {c}"))
				.Select(c => $"tools/{rel} -> tools/{c}"));
		}
		Assert.Empty(missing);
	}

	[Fact]
	public void Msbuild_targets_and_template_call_packed_tools()
	{
		var packed = Packed();
		var commands = Commands();
		var targets = File.ReadAllText(Path.Combine(Repo.Root, "src/Linux.SailfishOS/buildTransitive/Microsoft.Maui.SailfishOS.targets"));
		foreach (Match m in Regex.Matches(targets, @"\$\(MSBuildThisFileDirectory\)tools/([A-Za-z0-9_./-]+)"))
			Assert.Contains(m.Groups[1].Value, packed);
		foreach (Match m in Regex.Matches(targets, @"\$\(MSBuildThisFileDirectory\)\.\./\.\./\.\./tools/([A-Za-z0-9_./-]+)"))
			Assert.True(File.Exists(Path.Combine(Tools, m.Groups[1].Value)), $"targets: missing tools/{m.Groups[1].Value}");
		var vscode = Directory.GetFiles(Path.Combine(Repo.Root, "templates/maui-sailfish-app/.vscode"), "*.json")
			.Select(File.ReadAllText).Concat(new[] { targets });
		foreach (var text in vscode)
		{
			foreach (Match m in Regex.Matches(text, @"(?:SF_TOOLS_DIR\}/|SailfishToolsDir\)|&quot;)sf(?:&quot;)? ([a-z-]+)"))
			{
				Assert.True(commands.TryGetValue(m.Groups[1].Value, out var file), $"unknown command: sf {m.Groups[1].Value}");
				Assert.Contains(file, packed);
			}
			foreach (Match m in Regex.Matches(text, @"SF_TOOLS_DIR\}/(sf-[a-z-]+\.sh)"))
				Assert.Contains(m.Groups[1].Value, packed);
		}
	}
}

/// <summary>A failed start reaches the launcher: every Sailfish entry point returns Run's exit code.</summary>
public class EntryPointExitCodeTests
{
	[Theory]
	[InlineData("src/Linux.SailfishOS/scaffold/SailfishGeneratedEntryPoint.cs")]
	[InlineData("templates/maui-sailfish-app/Platforms/SailfishOS/Program.cs")]
	[InlineData("samples/Linux.SailfishOS.Sample/Program.cs")]
	[InlineData("samples/SkiaSharpProbe/Platforms/SailfishOS/Program.cs")]
	public void The_entry_point_returns_the_exit_code(string path)
	{
		var source = File.ReadAllText(Path.Combine(Repo.Root, path));
		Assert.Matches(@"static int Main\(string\[\] args\)", source);
		Assert.DoesNotMatch(@"static void Main\(", source);
	}
}
