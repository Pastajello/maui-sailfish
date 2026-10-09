using System.Diagnostics;
using System.Text.Json;

namespace Microsoft.Maui.SailfishOS.Tools;

/// <summary>
/// The app being deployed: its project file, package and binary names (asked from MSBuild, as tools/sf does), the
/// Sailfish target framework and the publish that builds its RPM.
/// </summary>
internal sealed class AppProject
{
	public string ProjectFile { get; private init; } = string.Empty;
	public string Directory => Path.GetDirectoryName(ProjectFile)!;
	public string PackageName { get; private init; } = string.Empty;
	public string AssemblyName { get; private init; } = string.Empty;

	/// <summary>net11.0-sailfish when the project lists it, else net11.0 (the RID then goes in with -r).</summary>
	public string TargetFramework { get; private init; } = "net11.0";

	public bool SailfishTfm => TargetFramework.EndsWith("-sailfish", StringComparison.Ordinal);

	/// <summary>The project file: <paramref name="path"/> (a file or a directory), else the only one in the current directory.</summary>
	public static string Find(string? path)
	{
		var start = path ?? System.IO.Directory.GetCurrentDirectory();
		if (File.Exists(start))
			return Path.GetFullPath(start);
		if (!System.IO.Directory.Exists(start))
			throw new ToolException($"no project at {start}");
		var projects = System.IO.Directory.GetFiles(start, "*.csproj");
		return projects.Length switch
		{
			1 => Path.GetFullPath(projects[0]),
			0 => throw new ToolException($"no .csproj in {Path.GetFullPath(start)} — run in the app's folder or pass --project"),
			_ => throw new ToolException($"several projects in {Path.GetFullPath(start)} — pass --project <file>"),
		};
	}

	public static AppProject Load(string projectFile, string configuration, string rid, string? framework = null, string? packageOverride = null)
	{
		var text = File.ReadAllText(projectFile);
		var tfm = framework ?? (text.Contains("net11.0-sailfish", StringComparison.Ordinal) ? "net11.0-sailfish" : "net11.0");
		var args = new List<string> { "msbuild", projectFile, "-getProperty:SailfishPackageName", "-getProperty:AssemblyName",
			"-p:TargetFramework=" + tfm, "-p:RuntimeIdentifier=" + rid, "-p:Configuration=" + configuration, "-p:CreateSailfishRpm=true" };
		var (rc, output) = Dotnet.Capture(args, Path.GetDirectoryName(projectFile)!);
		string package = string.Empty, assembly = Path.GetFileNameWithoutExtension(projectFile);
		if (rc == 0 && output.IndexOf('{') is var at and >= 0)
		{
			using var json = JsonDocument.Parse(output[at..]);
			if (json.RootElement.TryGetProperty("Properties", out var props))
			{
				package = props.TryGetProperty("SailfishPackageName", out var p) ? p.GetString() ?? string.Empty : string.Empty;
				assembly = props.TryGetProperty("AssemblyName", out var a) && a.GetString() is { Length: > 0 } name ? name : assembly;
			}
		}
		if (packageOverride is { Length: > 0 })
			package = packageOverride;
		else
		{
			if (package.Length == 0)
				package = "harbour-" + Path.GetFileNameWithoutExtension(projectFile).ToLowerInvariant().Replace('.', '-');
			// As tools/sf: a Debug build installs as its own package next to the Release one.
			if (string.Equals(configuration, "Debug", StringComparison.OrdinalIgnoreCase))
				package += "-debug";
		}
		return new AppProject { ProjectFile = projectFile, PackageName = package, AssemblyName = assembly, TargetFramework = tfm };
	}

	/// <summary>The RID from the phone's <c>uname -m</c>.</summary>
	public static string RidFor(string machine) => machine.Trim() switch
	{
		"aarch64" or "arm64" => "linux-arm64",
		var m when m.StartsWith("armv7", StringComparison.Ordinal) || m == "arm" => "linux-arm",
		"x86_64" => "linux-x64",
		var m => throw new ToolException($"the phone reports machine '{m}', which has no Sailfish RID"),
	};

	public static string RpmArch(string rid) => rid switch
	{
		"linux-arm" => "armv7hl",
		"linux-x64" => "x86_64",
		_ => "aarch64",
	};

	/// <summary>dotnet publish arguments that build the RPM (tools/lib/sf-lib.sh sf_publish_rpm).</summary>
	public List<string> PublishArguments(string configuration, string rid, string release, IEnumerable<string> extra)
	{
		var args = new List<string> { "publish", ProjectFile, "-c", configuration, "-f", TargetFramework };
		if (SailfishTfm)
			args.Add("-p:SailfishRuntimeIdentifier=" + rid);
		else
			args.AddRange(["-r", rid]);
		args.AddRange(["-p:SelfContained=true", "-p:CreateSailfishRpm=true", "-p:SailfishRelease=" + release,
			"-p:SailfishPackageName=" + PackageName, "-nologo"]);
		args.AddRange(extra);
		return args;
	}

	public string RpmDirectory => Path.Combine(Directory, "bin", "SailfishRpm");
}

internal static class Dotnet
{
	public static (int Rc, string Output) Capture(IEnumerable<string> args, string directory)
	{
		var info = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = directory };
		foreach (var arg in args)
			info.ArgumentList.Add(arg);
		using var process = Process.Start(info)!;
		var output = process.StandardOutput.ReadToEndAsync();
		var errors = process.StandardError.ReadToEndAsync();
		process.WaitForExit();
		return (process.ExitCode, output.Result + errors.Result);
	}

	/// <summary>Runs with the output on the console.</summary>
	public static int Run(IEnumerable<string> args, string directory)
	{
		var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, WorkingDirectory = directory };
		foreach (var arg in args)
			info.ArgumentList.Add(arg);
		using var process = Process.Start(info)!;
		process.WaitForExit();
		return process.ExitCode;
	}
}
