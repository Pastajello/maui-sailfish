using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Linux.SailfishOS.Tests;

/// <summary>The device tool ships in the NuGet package as the sailfish tool's build output (buildTransitive/net11.0/
/// tools/sailfish, tracker S56) and the MSBuild targets call it; the bash scripts stay in the checkout (tools/), where
/// the template's .vscode files (SF_TOOLS_DIR) and the matrix use them. A file the targets call but the package lacks
/// only fails on a package user's machine, so the references are checked against the csproj's pack list.</summary>
public class ToolsPackagingTests
{
	private const string PackPrefix = "buildTransitive/net11.0/tools/";
	private static readonly string Tools = Path.Combine(Repo.Root, "tools");

	private static IEnumerable<(string Include, string PackagePath)> PackedItems() =>
		XDocument.Load(Path.Combine(Repo.Root, "src/Linux.SailfishOS/Linux.SailfishOS.csproj")).Descendants("None")
			.Where(i => (string?)i.Attribute("Pack") == "true")
			.Select(i => ((string)i.Attribute("Include")!, (string?)i.Attribute("PackagePath") ?? ""));

	/// <summary>Packed tool files as paths relative to the package's tools/ dir, e.g. "sailfish/sailfish.dll", with
	/// their source in the checkout.</summary>
	private static Dictionary<string, string> Packed()
	{
		var packed = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var (include, packagePath) in PackedItems().Where(i => i.PackagePath.StartsWith(PackPrefix, StringComparison.Ordinal)))
			foreach (var file in include.Split(';', StringSplitOptions.RemoveEmptyEntries))
				packed[packagePath[PackPrefix.Length..] + Path.GetFileName(file)] = Path.GetFullPath(Path.Combine(Repo.Root, "src/Linux.SailfishOS", file));
		return packed;
	}

	/// <summary>The dispatcher's command table: command name → file under tools/.</summary>
	private static Dictionary<string, string> Commands() =>
		Regex.Matches(File.ReadAllText(Path.Combine(Tools, "sf")), @"^([a-z-]+)\|(sh|py|cs)\|([^|]+)\|", RegexOptions.Multiline)
			.ToDictionary(m => m.Groups[1].Value, m => m.Groups[3].Value);

	/// <summary>NuGet reads a PackagePath without an extension as a folder; folder paths ending in "/" keep every file
	/// name as is.</summary>
	[Fact]
	public void Tool_pack_paths_are_folders()
	{
		var paths = PackedItems().Select(i => i.PackagePath).Where(p => p.StartsWith(PackPrefix, StringComparison.Ordinal)).ToList();
		Assert.NotEmpty(paths);
		Assert.All(paths, p => Assert.EndsWith("/", p));
	}

	/// <summary>The same NuGet rule for the native assets: the extension-less launcher packed as
	/// "native/sailfish-launcher" landed at native/sailfish-launcher/sailfish-launcher (tracker S06). A file without an
	/// extension needs a folder PackagePath.</summary>
	[Fact]
	public void Extension_less_native_assets_are_packed_into_a_folder()
	{
		var items = PackedItems()
			.Where(i => i.PackagePath.StartsWith("runtimes/", StringComparison.Ordinal) && Path.GetExtension(i.Include).Length == 0)
			.ToList();
		Assert.NotEmpty(items);   // the launchers
		Assert.All(items, i => Assert.EndsWith("/", i.PackagePath));
	}

	[Fact]
	public void The_package_carries_the_sailfish_tool_and_no_bash_or_python()
	{
		var packed = Packed();

		Assert.Equal(["sailfish/sailfish.deps.json", "sailfish/sailfish.dll", "sailfish/sailfish.runtimeconfig.json"], packed.Keys.Order());
		Assert.All(packed.Values, source => Assert.StartsWith(Path.Combine(Repo.Root, "artifacts", "sailfish-tool"), source));
		Assert.DoesNotContain(PackedItems(), i => i.Include.Contains("tools/", StringComparison.Ordinal) && !i.Include.Contains("sailfish-tool", StringComparison.Ordinal));
		Assert.All(packed.Values, source => Assert.True(File.Exists(source), $"packed but not built: {source}"));
	}

	[Fact]
	public void Every_dispatcher_command_has_its_file()
	{
		var commands = Commands();
		Assert.True(commands.Count > 20, "the command table did not parse");
		Assert.All(commands, c => Assert.True(File.Exists(Path.Combine(Tools, c.Value)), $"sf {c.Key}: missing tools/{c.Value}"));
	}

	[Fact]
	public void Msbuild_targets_call_the_packed_tool_and_the_template_the_checkouts_tools()
	{
		var packed = Packed();
		var targets = File.ReadAllText(Path.Combine(Repo.Root, "src/Linux.SailfishOS/buildTransitive/Microsoft.Maui.Platforms.SailfishOS.targets"));
		var package = Regex.Matches(targets, @"\$\(MSBuildThisFileDirectory\)tools/([A-Za-z0-9_./-]+)").Select(m => m.Groups[1].Value).ToList();
		Assert.NotEmpty(package);
		Assert.All(package, file => Assert.Contains(file, packed.Keys));
		Assert.DoesNotContain("bash ", targets);
		Assert.DoesNotContain("SailfishToolsDir", targets);

		var commands = Commands();
		foreach (var text in Directory.GetFiles(Path.Combine(Repo.Root, "templates/maui-sailfish-app/.vscode"), "*.json").Select(File.ReadAllText))
		{
			foreach (Match m in Regex.Matches(text, @"SF_TOOLS_DIR\}/sf ([a-z-]+)"))
				Assert.True(commands.ContainsKey(m.Groups[1].Value), $"unknown command: sf {m.Groups[1].Value}");
			foreach (Match m in Regex.Matches(text, @"SF_TOOLS_DIR\}/(sf-[a-z-]+\.sh)"))
				Assert.True(File.Exists(Path.Combine(Tools, m.Groups[1].Value)), $"template: missing tools/{m.Groups[1].Value}");
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
