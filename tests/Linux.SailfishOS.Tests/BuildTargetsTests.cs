using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Linux.SailfishOS.Tests;

/// <summary>
/// Tracker S52 (plan M19 steps 2–3): the workload manifest and package targets, evaluated on a temp csproj with the
/// in-repo files (the installed workload is switched off, so the test sees these sources and not an older install).
/// </summary>
public sealed class BuildTargetsTests : IDisposable
{
	private readonly string _dir = Directory.CreateTempSubdirectory("sf-targets-").FullName;

	public void Dispose()
	{
		try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
	}

	private string Project(string outputType, string extra = "")
	{
		var manifest = Path.Combine(Repo.Root, "src/Linux.SailfishOS.WorkloadManifest/data/WorkloadManifest.targets.in");
		var targets = Path.Combine(Repo.Root, "src/Linux.SailfishOS/buildTransitive/Microsoft.Maui.Platforms.SailfishOS.targets");
		var path = Path.Combine(_dir, outputType + ".csproj");
		File.WriteAllText(path, $"""
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <TargetFramework>net11.0-sailfish</TargetFramework>
			    <OutputType>{outputType}</OutputType>
			    <SailfishImplicitPackageReference>false</SailfishImplicitPackageReference>
			    <CustomBeforeMicrosoftCommonTargets>{manifest}</CustomBeforeMicrosoftCommonTargets>
			    <CustomAfterMicrosoftCommonTargets>{targets}</CustomAfterMicrosoftCommonTargets>
			    {extra}
			  </PropertyGroup>
			</Project>
			""");
		return path;
	}

	private static (int Exit, string Output) Dotnet(string workDir, params string[] args)
	{
		var psi = new ProcessStartInfo("dotnet") { WorkingDirectory = workDir, RedirectStandardOutput = true, RedirectStandardError = true };
		foreach (var a in args)
			psi.ArgumentList.Add(a);
		// The test host's MSBuild variables would point the child at the test's own SDK resolution.
		foreach (var key in psi.Environment.Keys.Where(k => k.StartsWith("MSBuild", StringComparison.OrdinalIgnoreCase)).ToList())
			psi.Environment.Remove(key);
		psi.Environment["MSBuildEnableWorkloadResolver"] = "false";
		psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
		using var p = Process.Start(psi)!;
		var stdout = p.StandardOutput.ReadToEndAsync();
		var stderr = p.StandardError.ReadToEnd();
		p.WaitForExit();
		return (p.ExitCode, stdout.Result + stderr);
	}

	private static Dictionary<string, string> Properties(string project, params string[] names)
	{
		var (exit, output) = Dotnet(Path.GetDirectoryName(project)!, ["msbuild", project, .. names.Select(n => "-getProperty:" + n)]);
		Assert.True(exit == 0, output);
		if (names.Length == 1)   // one property prints its bare value, more print JSON
			return new() { [names[0]] = output.Trim() };
		using var json = JsonDocument.Parse(output);
		return json.RootElement.GetProperty("Properties").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
	}

	[Fact]
	public void An_app_head_is_a_self_contained_device_payload_with_an_rpm_and_a_generated_main()
	{
		var p = Properties(Project("Exe"), "RuntimeIdentifier", "SelfContained", "CreateSailfishRpm", "SailfishGenerateMain", "TargetPlatformSupported");

		Assert.Equal("linux-arm64", p["RuntimeIdentifier"]);
		Assert.Equal("true", p["SelfContained"]);
		Assert.Equal("true", p["CreateSailfishRpm"]);
		Assert.Equal("true", p["SailfishGenerateMain"]);
		Assert.Equal("true", p["TargetPlatformSupported"]);
	}

	[Fact]
	public void A_class_library_with_the_tfm_stays_rid_neutral()
	{
		var p = Properties(Project("Library"), "RuntimeIdentifier", "SelfContained", "CreateSailfishRpm", "SailfishGenerateMain");

		Assert.Equal("", p["RuntimeIdentifier"]);
		Assert.NotEqual("true", p["SelfContained"]);
		Assert.Equal("", p["CreateSailfishRpm"]);
		Assert.NotEqual("true", p["SailfishGenerateMain"]);
	}

	[Fact]
	public void SailfishRuntimeIdentifier_picks_the_heads_rid()
	{
		var p = Properties(Project("Exe", "<SailfishRuntimeIdentifier>linux-arm</SailfishRuntimeIdentifier>"), "RuntimeIdentifier");

		Assert.Equal("linux-arm", p["RuntimeIdentifier"]);
	}

	// Tracker S54: `dotnet run -f net11.0-sailfish` runs the device tools (DeployToDevice, then RunCommand).
	[Fact]
	public void Dotnet_run_streams_the_app_through_the_device_tools()
	{
		var project = Project("Exe", "<SailfishPackageName>harbour-probe</SailfishPackageName><SailfishToolsDir>/tools/</SailfishToolsDir>");
		var (exit, output) = Dotnet(_dir, "msbuild", project, "-t:ComputeRunArguments", "-getProperty:RunCommand", "-getProperty:RunArguments");

		Assert.True(exit == 0, output);
		using var json = JsonDocument.Parse(output);
		var props = json.RootElement.GetProperty("Properties");
		Assert.Equal("/usr/bin/env", props.GetProperty("RunCommand").GetString());
		var args = props.GetProperty("RunArguments").GetString()!;
		Assert.Contains("SF_PKG=\"harbour-probe\"", args);
		Assert.Contains("SF_RID=\"linux-arm64\"", args);
		Assert.EndsWith("bash \"/tools/sf\" run --follow", args);
	}

	[Fact]
	public void A_library_has_no_device_deploy()
	{
		var (exit, output) = Dotnet(_dir, "msbuild", Project("Library"), "-t:DeployToDevice");

		Assert.NotEqual(0, exit);
		Assert.Contains("MSB4057", output);   // the target does not exist
	}

	// The build fails on #error unless the platform defines exist, and the assembly carries the platform attributes.
	[Fact]
	public void A_library_builds_without_a_rid_with_the_platform_defines_and_attributes()
	{
		var project = Project("Library");
		File.WriteAllText(Path.Combine(_dir, "Probe.cs"), """
			#if !SAILFISH || !SAILFISH1_0 || !SAILFISH1_0_OR_GREATER
			#error platform defines missing
			#endif
			namespace Probe;
			public static class C { public static int X => 1; }
			""");

		var (exit, output) = Dotnet(_dir, "build", project, "-nologo", "-v:q");

		Assert.True(exit == 0, output);
		Assert.True(File.Exists(Path.Combine(_dir, "bin/Debug/net11.0-sailfish/Library.dll")), output);   // no RID folder
		var info = File.ReadAllText(Directory.GetFiles(Path.Combine(_dir, "obj"), "*.AssemblyInfo.cs", SearchOption.AllDirectories).Single());
		Assert.Contains("SupportedOSPlatformAttribute(\"sailfish1.0\")", info);
		Assert.Contains("TargetPlatformAttribute(\"sailfish1.0\")", info);
		// The app's own file; a library's copy would collide with it on publish (NETSDK1152).
		Assert.False(File.Exists(Path.Combine(_dir, "obj/maui-appmeta.json")));
	}
}
