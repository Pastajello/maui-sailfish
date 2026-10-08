using System.Text.RegularExpressions;
using Xunit;

namespace Linux.SailfishOS.Tests;

/// <summary>
/// Tracker S54 (plan M21 step 4): the <c>MAUI_SAILFISH_*</c> tables in <c>docs/tools.md</c> list every switch the
/// sources read (C#, the shim, QML), and nothing that is no longer read. A table row may shorten the
/// <c>MAUI_SAILFISH_QT_HOST</c> prefix to <c>…</c>, as the diagnostics table does.
/// </summary>
public sealed partial class EnvSwitchTableTests
{
	[GeneratedRegex("MAUI_SAILFISH_[A-Z0-9_]*[A-Z0-9]")]
	private static partial Regex FullName();

	// "…_PAGE_DIAG", and the "…_SHOWCASE_PULL_NEAR/FAR" shorthand for two names.
	[GeneratedRegex("…(_[A-Z0-9_]*[A-Z0-9])(?:/([A-Z0-9]+))?")]
	private static partial Regex ShortName();

	private static readonly string[] SourceExtensions = [".cs", ".cpp", ".h", ".qml", ".js"];

	private static SortedSet<string> SourceNames()
	{
		var names = new SortedSet<string>(StringComparer.Ordinal);
		var src = Path.Combine(Repo.Root, "src");
		foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
		{
			var rel = Path.GetRelativePath(src, file).Replace('\\', '/');
			if (rel.Contains("/bin/", StringComparison.Ordinal) || rel.Contains("/obj/", StringComparison.Ordinal)
			    || !SourceExtensions.Contains(Path.GetExtension(file)))
				continue;
			foreach (Match m in FullName().Matches(File.ReadAllText(file)))
				names.Add(m.Value);
		}
		return names;
	}

	private static SortedSet<string> DocumentedNames()
	{
		var doc = File.ReadAllText(Path.Combine(Repo.Root, "docs", "tools.md"));
		var names = new SortedSet<string>(FullName().Matches(doc).Select(m => m.Value), StringComparer.Ordinal);
		foreach (Match m in ShortName().Matches(doc))
		{
			var name = "MAUI_SAILFISH_QT_HOST" + m.Groups[1].Value;
			names.Add(name);
			if (m.Groups[2].Success)   // …_PULL_NEAR/FAR → …_PULL_FAR
				names.Add(name[..(name.LastIndexOf('_') + 1)] + m.Groups[2].Value);
		}
		return names;
	}

	[Fact]
	public void Every_switch_the_sources_read_is_in_tools_md() =>
		Assert.Empty(SourceNames().Except(DocumentedNames()));

	[Fact]
	public void Tools_md_lists_no_switch_the_sources_no_longer_read() =>
		Assert.Empty(DocumentedNames().Except(SourceNames()));
}
