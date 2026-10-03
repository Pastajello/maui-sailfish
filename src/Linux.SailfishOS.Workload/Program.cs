using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Microsoft.Maui.SailfishOS.Workload;

/// <summary>
/// <c>sailfish-workload</c>: teaches the local .NET SDK the <c>net11.0-sailfish</c> TFM on a machine without a checkout of
/// the backend. The TFM check runs before restore, so no package a Sailfish project references can do it; a tool
/// package restores with a plain TFM. It copies the embedded WorkloadManifest.json/.targets into
/// <c>sdk-manifests/&lt;band&gt;/microsoft.maui.sailfishos</c>, which is what <c>dotnet workload install</c> does for a
/// manifest (the aggregate workload packs are not shipped).
/// </summary>
public static class Program
{
	internal const string ManifestId = "microsoft.maui.sailfishos";
	private static readonly string[] ManifestFiles = ["WorkloadManifest.json", "WorkloadManifest.targets"];

	private const string Usage = """
		Usage: sailfish-workload [install|uninstall|status] [--dotnet <path>] [--manifest-root <dir>]

		  install        copy the net11.0-sailfish workload manifest into the active SDK (default command)
		  uninstall      remove it; Sailfish projects then need the plain net11.0 TFM
		  status         print where the manifest is installed and its version

		  --dotnet <path>        the dotnet executable whose SDK to change (default: dotnet on PATH); the SDK is the
		                         one `dotnet --version` selects in the current directory (global.json applies)
		  --manifest-root <dir>  use <dir>/<band>/ instead of the SDK's sdk-manifests/<band>/, for an SDK you cannot
		                         write to; builds then need DOTNETSDK_WORKLOAD_MANIFEST_ROOTS=<dir>
		""";

	public static int Main(string[] args)
	{
		string command = "install", dotnet = "dotnet";
		string? manifestRoot = null;
		for (var i = 0; i < args.Length; i++)
		{
			switch (args[i])
			{
				case "install" or "uninstall" or "status":
					command = args[i];
					break;
				case "--dotnet" when i + 1 < args.Length:
					dotnet = args[++i];
					break;
				case "--manifest-root" when i + 1 < args.Length:
					manifestRoot = Path.GetFullPath(args[++i]);
					break;
				case "-h" or "--help":
					Console.WriteLine(Usage);
					return 0;
				default:
					Console.Error.WriteLine($"error: unexpected argument '{args[i]}'\n\n{Usage}");
					return 2;
			}
		}

		try
		{
			var target = Locate(dotnet, manifestRoot);
			return command switch
			{
				"uninstall" => Uninstall(target),
				"status" => Status(target),
				_ => Install(target),
			};
		}
		catch (ToolException ex)
		{
			Console.Error.WriteLine("error: " + ex.Message);
			return 1;
		}
	}

	private sealed record Target(string SdkVersion, string Band, string Directory, bool CustomRoot);

	private static Target Locate(string dotnet, string? manifestRoot)
	{
		var sdkVersion = Run(dotnet, "--version").Trim();
		var band = FeatureBand(sdkVersion)
			?? throw new ToolException($"cannot read the SDK feature band from '{sdkVersion}'");
		string root;
		if (manifestRoot is not null)
			root = manifestRoot;
		else
		{
			var sdkDir = SdkDirectory(Run(dotnet, "--list-sdks"), sdkVersion)
				?? throw new ToolException($"`{dotnet} --list-sdks` does not list the active SDK {sdkVersion}");
			// --list-sdks brackets the sdk/ directory itself; sdk-manifests/ is its sibling.
			root = Path.Combine(Path.GetDirectoryName(sdkDir.TrimEnd('/', '\\'))!, "sdk-manifests");
		}
		return new Target(sdkVersion, band, Path.Combine(root, band, ManifestId), manifestRoot is not null);
	}

	private static int Install(Target target)
	{
		var manifestBand = EmbeddedBand();
		if (!string.Equals(manifestBand, target.Band, StringComparison.OrdinalIgnoreCase))
			throw new ToolException($"this package carries the manifest for SDK band {manifestBand}, the active SDK " +
				$"{target.SdkVersion} is band {target.Band}. Use the Microsoft.Maui.SailfishOS.Workload version built for " +
				"that band, or select a matching SDK with global.json.");
		try
		{
			System.IO.Directory.CreateDirectory(target.Directory);
			foreach (var file in ManifestFiles)
			{
				using var source = Embedded(file);
				using var destination = File.Create(Path.Combine(target.Directory, file));
				source.CopyTo(destination);
			}
		}
		catch (UnauthorizedAccessException)
		{
			throw new ToolException($"no write access to {target.Directory}. Run it with sudo, or install into a directory " +
				"you own with --manifest-root <dir> and set DOTNETSDK_WORKLOAD_MANIFEST_ROOTS=<dir> for builds.");
		}
		Console.WriteLine($"installed the Sailfish workload manifest {EmbeddedVersion()} for SDK band {target.Band}");
		Console.WriteLine($"  {target.Directory}");
		if (target.CustomRoot)
			Console.WriteLine($"  builds need DOTNETSDK_WORKLOAD_MANIFEST_ROOTS={Path.GetDirectoryName(Path.GetDirectoryName(target.Directory))}");
		Console.WriteLine("  net11.0-sailfish is now a known TFM; `sailfish-workload uninstall` removes it");
		return 0;
	}

	private static int Uninstall(Target target)
	{
		if (!System.IO.Directory.Exists(target.Directory))
		{
			Console.WriteLine($"not installed for SDK band {target.Band} ({target.Directory})");
			return 0;
		}
		try
		{
			System.IO.Directory.Delete(target.Directory, recursive: true);
		}
		catch (UnauthorizedAccessException)
		{
			throw new ToolException($"no write access to {target.Directory}; run it with sudo");
		}
		Console.WriteLine($"removed the Sailfish workload manifest for SDK band {target.Band}");
		return 0;
	}

	private static int Status(Target target)
	{
		var json = Path.Combine(target.Directory, "WorkloadManifest.json");
		if (!File.Exists(json))
		{
			Console.WriteLine($"not installed for SDK {target.SdkVersion} (band {target.Band}); expected at {target.Directory}");
			return 3;
		}
		var installed = PackVersion(File.ReadAllText(json)) ?? "?";
		Console.WriteLine($"installed {installed} for SDK {target.SdkVersion} (band {target.Band}) at {target.Directory}");
		var embedded = EmbeddedVersion();
		if (installed != embedded)
			Console.WriteLine($"  this tool carries {embedded}; `sailfish-workload install` replaces it");
		return 0;
	}

	/// <summary>
	/// The SDK feature band of an SDK version, the directory name under sdk-manifests/: the patch rounded down to its
	/// hundred and the first two prerelease labels ("11.0.105" → "11.0.100", "11.0.100-rc.1.26425.128" →
	/// "11.0.100-rc.1").
	/// </summary>
	internal static string? FeatureBand(string sdkVersion)
	{
		var m = Regex.Match(sdkVersion, @"^(\d+)\.(\d+)\.(\d+)(-[0-9A-Za-z]+\.[0-9A-Za-z]+)?");
		if (!m.Success)
			return null;
		var patch = int.Parse(m.Groups[3].Value) / 100 * 100;
		return $"{m.Groups[1].Value}.{m.Groups[2].Value}.{patch}{m.Groups[4].Value}";
	}

	/// <summary>The sdk/ directory of one version from <c>dotnet --list-sdks</c> output ("11.0.100 [/usr/share/dotnet/sdk]").</summary>
	internal static string? SdkDirectory(string listSdks, string sdkVersion)
	{
		foreach (var line in listSdks.Split('\n'))
		{
			var m = Regex.Match(line.Trim(), @"^(\S+) \[(.+)\]$");
			if (m.Success && m.Groups[1].Value == sdkVersion)
				return m.Groups[2].Value;
		}
		return null;
	}

	/// <summary>The microsoft.maui.sailfishos pack version in a WorkloadManifest.json.</summary>
	internal static string? PackVersion(string manifestJson)
	{
		using var doc = System.Text.Json.JsonDocument.Parse(manifestJson);
		return doc.RootElement.TryGetProperty("packs", out var packs) &&
		       packs.TryGetProperty(ManifestId, out var pack) &&
		       pack.TryGetProperty("version", out var version)
			? version.GetString()
			: null;
	}

	private static string EmbeddedBand() => Metadata("SailfishWorkloadBand");

	internal static string EmbeddedVersion()
	{
		using var reader = new StreamReader(Embedded("WorkloadManifest.json"));
		return PackVersion(reader.ReadToEnd()) ?? "?";
	}

	private static string Metadata(string key) =>
		typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value
		?? throw new ToolException($"the tool was built without {key}");

	private static Stream Embedded(string file) =>
		typeof(Program).Assembly.GetManifestResourceStream(file)
		?? throw new ToolException($"the tool was built without {file}");

	private static string Run(string exe, string arguments)
	{
		var start = new ProcessStartInfo(exe, arguments)
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
		};
		try
		{
			using var process = Process.Start(start) ?? throw new ToolException($"cannot start {exe}");
			var output = process.StandardOutput.ReadToEnd();
			process.WaitForExit();
			if (process.ExitCode != 0)
				throw new ToolException($"`{exe} {arguments}` failed: {process.StandardError.ReadToEnd().Trim()}");
			return output;
		}
		catch (System.ComponentModel.Win32Exception)
		{
			throw new ToolException($"cannot run '{exe}'; pass the dotnet executable with --dotnet <path>");
		}
	}

	private sealed class ToolException(string message) : Exception(message);
}
