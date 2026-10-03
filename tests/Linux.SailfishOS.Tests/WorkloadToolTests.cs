using Microsoft.Maui.SailfishOS.Workload;
using Xunit;

namespace Linux.SailfishOS.Tests;

/// <summary>The sailfish-workload tool's SDK queries (what decides where the manifest lands).</summary>
public class WorkloadToolTests
{
	[Theory]
	[InlineData("11.0.100-rc.1.26425.128", "11.0.100-rc.1")]
	[InlineData("11.0.100", "11.0.100")]
	[InlineData("11.0.105", "11.0.100")]
	[InlineData("11.0.203-preview.3.1", "11.0.200-preview.3")]
	[InlineData("10.0.303", "10.0.300")]
	public void FeatureBand_matches_the_sdk_manifests_directory(string sdk, string band) =>
		Assert.Equal(band, Program.FeatureBand(sdk));

	[Fact]
	public void FeatureBand_rejects_garbage() => Assert.Null(Program.FeatureBand("not-a-version"));

	[Fact]
	public void SdkDirectory_picks_the_active_sdk_line()
	{
		const string list = """
			10.0.303 [/Users/me/Library/Application Support/dotnet/sdk]
			11.0.100-rc.1.26425.128 [/usr/share/dotnet/sdk]
			""";
		Assert.Equal("/usr/share/dotnet/sdk", Program.SdkDirectory(list, "11.0.100-rc.1.26425.128"));
		Assert.Equal("/Users/me/Library/Application Support/dotnet/sdk", Program.SdkDirectory(list, "10.0.303"));
		Assert.Null(Program.SdkDirectory(list, "11.0.100"));
	}

	[Fact]
	public void PackVersion_reads_the_backend_pack() =>
		Assert.Equal("9.8.7", Program.PackVersion("""{ "version": 1, "packs": { "microsoft.maui.sailfishos": { "kind": "library", "version": "9.8.7" } } }"""));

	// The manifest the tool embeds is written from data/*.in with the repo's version (SailfishVersionedFiles.targets).
	[Fact]
	public void The_embedded_manifest_carries_the_repo_version()
	{
		var props = File.ReadAllText(Path.Combine(Repo.Root, "Directory.Build.props"));
		var version = System.Text.RegularExpressions.Regex.Match(props, @"<PlatformMauiSailfishVersion[^>]*>([^<]+)<").Groups[1].Value;
		Assert.Equal(version, Program.EmbeddedVersion());
	}
}
