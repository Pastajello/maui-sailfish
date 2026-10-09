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

	private static Dictionary<string, string> Properties(string project, params string[] names) =>
		Properties(project, [], names);

	// globals: -p: arguments, as `dotnet publish -c Release` passes Configuration (the SDK props derive Optimize from it).
	private static Dictionary<string, string> Properties(string project, string[] globals, params string[] names)
	{
		var (exit, output) = Dotnet(Path.GetDirectoryName(project)!, ["msbuild", project, .. globals, .. names.Select(n => "-getProperty:" + n)]);
		Assert.True(exit == 0, output);
		if (names.Length == 1)   // one property prints its bare value, more print JSON
			return new() { [names[0]] = output.Trim() };
		using var json = JsonDocument.Parse(output);
		return json.RootElement.GetProperty("Properties").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
	}

	// Tracker S57: restore never sees the package's targets (ExcludeRestorePackageImports), so the manifest puts the
	// ReadyToRun and trimming packs into the restore; a Release publish on a clean NuGet cache failed with NETSDK1094.
	[Fact]
	public void The_restore_brings_the_ready_to_run_and_trimming_packs_and_the_build_decides_per_configuration()
	{
		var project = Project("Exe");
		var restore = Properties(project, ["-p:ExcludeRestorePackageImports=true", "-p:Configuration=Debug"], "PublishReadyToRun", "PublishTrimmed");
		var debug = Properties(project, ["-p:Configuration=Debug"], "PublishReadyToRun", "PublishTrimmed");
		var optedOut = Properties(project, ["-p:ExcludeRestorePackageImports=true", "-p:SailfishReadyToRun=false", "-p:SailfishTrim=false"], "PublishReadyToRun", "PublishTrimmed");

		Assert.Equal(("true", "true"), (restore["PublishReadyToRun"], restore["PublishTrimmed"]));
		Assert.Equal((string.Empty, string.Empty), (debug["PublishReadyToRun"], debug["PublishTrimmed"]));
		Assert.Equal((string.Empty, string.Empty), (optedOut["PublishReadyToRun"], optedOut["PublishTrimmed"]));
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

	// Tracker S54/S56: `dotnet run -f net11.0-sailfish` runs the sailfish tool (DeployToDevice, then RunCommand) on the
	// build's own dotnet: no /usr/bin/env or bash, so Windows runs it too.
	[Fact]
	public void Dotnet_run_streams_the_app_through_the_sailfish_tool()
	{
		var tool = Path.Combine(Repo.Root, "artifacts", "sailfish-tool", "sailfish.dll");
		var project = Project("Exe", $"<SailfishPackageName>harbour-probe</SailfishPackageName><SailfishToolDll>{tool}</SailfishToolDll>");
		var (exit, output) = Dotnet(_dir, "msbuild", project, "-t:ComputeRunArguments", "-getProperty:RunCommand", "-getProperty:RunArguments");

		Assert.True(exit == 0, output);
		using var json = JsonDocument.Parse(output);
		var props = json.RootElement.GetProperty("Properties");
		Assert.Matches(@"(^|[/\\])dotnet(\.exe)?$", props.GetProperty("RunCommand").GetString());
		var args = props.GetProperty("RunArguments").GetString()!;
		Assert.StartsWith($"\"{tool}\" run --follow --project ", args);
		Assert.Contains("--package harbour-probe", args);
		Assert.EndsWith("--rid linux-arm64", args);
	}

	// Tracker S53: the generated Main's CreateMauiApp() is rooted in the app assembly only, not in every assembly.
	[Fact]
	public void The_trimmer_descriptor_roots_mauiprogram_in_the_app_assembly_only()
	{
		var project = Project("Exe", "<AssemblyName>Probe.App</AssemblyName>");
		var (exit, output) = Dotnet(_dir, "msbuild", project, "-t:_SailfishTrimmerDescriptor", "-getItem:TrimmerRootDescriptor");

		Assert.True(exit == 0, output);
		var descriptor = Assert.Single(Directory.GetFiles(Path.Combine(_dir, "obj"), "sailfish-trimmer.xml", SearchOption.AllDirectories));
		var xml = System.Xml.Linq.XDocument.Load(descriptor);
		var assembly = Assert.Single(xml.Root!.Elements("assembly"));
		Assert.Equal("Probe.App", (string?)assembly.Attribute("fullname"));
		Assert.Equal("*MauiProgram", (string?)Assert.Single(assembly.Elements("type")).Attribute("fullname"));
		Assert.Contains("sailfish-trimmer.xml", output);
	}

	// Tracker S53: a trimmed Release keeps reflection-based System.Text.Json and [DefaultValue] (as on Android), and drops
	// the development-time DI check, startup hooks and HTTP activity propagation.
	[Fact]
	public void A_trimmed_release_sets_the_android_feature_switches()
	{
		var p = Properties(Project("Exe"), ["-p:Configuration=Release"],
			"PublishTrimmed", "TrimMode", "JsonSerializerIsReflectionEnabledByDefault", "_DefaultValueAttributeSupport",
			"VerifyDependencyInjectionOpenGenericServiceTrimmability", "StartupHookSupport", "HttpActivityPropagationSupport",
			"EventSourceSupport", "UseSystemResourceKeys");

		Assert.Equal("true", p["PublishTrimmed"]);
		Assert.Equal("partial", p["TrimMode"]);
		Assert.Equal("true", p["JsonSerializerIsReflectionEnabledByDefault"]);
		Assert.Equal("true", p["_DefaultValueAttributeSupport"]);
		Assert.Equal("false", p["VerifyDependencyInjectionOpenGenericServiceTrimmability"]);
		Assert.Equal("false", p["StartupHookSupport"]);
		Assert.Equal("false", p["HttpActivityPropagationSupport"]);
		Assert.NotEqual("false", p["EventSourceSupport"]);      // dotnet-trace on Release builds
		Assert.NotEqual("true", p["UseSystemResourceKeys"]);    // readable exception messages in the device log
	}

	// Tracker S51: images, fonts and assets go through MAUI's external-backend contract on the app head; a library's items
	// reach the app through its ProjectReference, as on the in-box heads.
	[Fact]
	public void The_app_head_opts_into_the_resizetizer_external_backend_contract()
	{
		string[] names = ["ResizetizerPlatformType", "ResizetizeBeforeTargets", "ProcessMauiFontsBeforeTargets",
			"ResizetizerAfterImageProcessingTargets", "ResizetizerAfterFontProcessingTargets", "ResizetizerAfterAssetProcessingTargets"];
		var app = Properties(Project("Exe"), names);
		var library = Properties(Project("Library"), names);

		Assert.Equal("wpf", app["ResizetizerPlatformType"]);
		Assert.Contains("AssignTargetPaths", app["ResizetizeBeforeTargets"]);
		Assert.Contains("AssignTargetPaths", app["ProcessMauiFontsBeforeTargets"]);
		Assert.Contains("_SailfishMauiProcessedImages", app["ResizetizerAfterImageProcessingTargets"]);
		Assert.Contains("_SailfishMauiProcessedFonts", app["ResizetizerAfterFontProcessingTargets"]);
		Assert.Contains("_SailfishMauiProcessedAssets", app["ResizetizerAfterAssetProcessingTargets"]);
		Assert.Equal("", library["ResizetizerPlatformType"]);
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
