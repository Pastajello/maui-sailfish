using System.Text.RegularExpressions;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;

namespace Linux.SailfishOS.Tests;

/// <summary>The managed side and the native shim agree on the ABI: one version number, one set of error codes, and
/// every declared export defined once.</summary>
public class NativeContractTests
{
	private static readonly string NativeDir = Path.Combine(Repo.Root, "src", "Linux.SailfishOS", "Native");
	private static string Header => File.ReadAllText(Path.Combine(NativeDir, "sailfish_host.h"));

	/// <summary>The shim's sources (sailfish_host.cpp, the host_*.cpp families and host_internal.h) as one text.</summary>
	internal static string HostSources() => string.Join("\n", Directory.GetFiles(NativeDir)
		.Where(f => Path.GetFileName(f) is var n && (n == "sailfish_host.cpp" || n == "host_internal.h" ||
		                                             n.StartsWith("host_", StringComparison.Ordinal) && n.EndsWith(".cpp", StringComparison.Ordinal)))
		.Order(StringComparer.Ordinal).Select(File.ReadAllText));

	// W8.1: the shim is split by family; the build must compile every one of them (a file it misses links fine into a
	// library without those exports, which fails only at the first P/Invoke on the phone).
	[Fact]
	public void The_native_build_compiles_every_host_source()
	{
		var script = File.ReadAllText(Path.Combine(Repo.Root, "tools", "cmd", "native-build.sh"));
		var sources = Directory.GetFiles(NativeDir, "*.cpp").Select(Path.GetFileName)
			.Where(n => n == "sailfish_host.cpp" || n!.StartsWith("host_", StringComparison.Ordinal)).ToList();
		Assert.True(sources.Count > 1);
		Assert.All(sources, name => Assert.Contains($"\"$SRC/{name}\"", script));
	}

	[Fact]
	public void The_header_abi_version_is_the_managed_one()
	{
		var match = Regex.Match(Header, @"#define\s+SFHOST_ABI_VERSION\s+(\d+)");
		Assert.True(match.Success);
		Assert.Equal(QtHostNative.AbiVersion, int.Parse(match.Groups[1].Value));
	}

	[Fact]
	public void The_error_codes_match()
	{
		var codes = Regex.Matches(Header, @"(SFHOST_(?:OK|E_\w+))\s*=\s*(-?\d+)")
			.ToDictionary(m => m.Groups[1].Value, m => int.Parse(m.Groups[2].Value));
		Assert.Equal(QtHostRuntime.SfhostOk, codes["SFHOST_OK"]);
		Assert.Equal(QtHostRuntime.SfhostEArgs, codes["SFHOST_E_ARGS"]);
		Assert.Equal(QtHostRuntime.SfhostEProperty, codes["SFHOST_E_PROPERTY"]);
		Assert.Equal(QtHostRuntime.SfhostEDeadHandle, codes["SFHOST_E_DEAD_HANDLE"]);
		Assert.Equal(QtHostRuntime.SfhostEJs, codes["SFHOST_E_JS"]);
		Assert.Equal(QtHostRuntime.SfhostELoad, codes["SFHOST_E_LOAD"]);
		Assert.Equal(6, codes.Count);
	}

	[Fact]
	public void Every_declared_export_is_defined_once_and_imported()
	{
		var declared = Regex.Matches(Header, @"\b(sailfish_host_\w+)\s*\(").Select(m => m.Groups[1].Value).ToHashSet();
		var sources = Directory.GetFiles(NativeDir, "*.cpp").Select(File.ReadAllText).ToList();
		var imports = File.ReadAllText(Path.Combine(Repo.Root, "src", "Linux.SailfishOS", "Platform", "QtHost", "QtHostNative.cs"));
		var problems = new List<string>();
		foreach (var name in declared)
		{
			var definitions = sources.Sum(src => Regex.Matches(src, $@"^[\w\s\*]+\b{name}\s*\([^;]*\)\s*$", RegexOptions.Multiline).Count);
			if (definitions != 1)
				problems.Add($"{name}: {definitions} definitions");
		}
		foreach (Match m in Regex.Matches(imports, @"extern\s+[\w<>?\[\]]+\s+(sailfish_host_\w+)\s*\("))
			if (!declared.Contains(m.Groups[1].Value))
				problems.Add($"{m.Groups[1].Value}: imported but not in sailfish_host.h");
		Assert.Empty(problems);
	}

	// sailfish_host_post/wake/quit run on any thread: the state they read is atomic, and the last error is written
	// under its mutex only (set_error), never assigned directly.
	[Fact]
	public void Cross_thread_state_is_atomic_and_the_error_text_is_locked()
	{
		var source = HostSources();
		// The one direct write is set_error's own, under the lock (and set_error must not call itself).
		var writes = Regex.Matches(source, @"\bg\.error\s*=(?!=)");
		Assert.Single(writes);
		var setter = Regex.Match(source, @"void set_error\(std::string text\)\s*\{(?<body>[^}]*)\}");
		Assert.True(setter.Success);
		Assert.Contains("lock_guard", setter.Groups["body"].Value);
		Assert.Contains("g.error = std::move(text);", setter.Groups["body"].Value);
		Assert.DoesNotContain("set_error(", setter.Groups["body"].Value);
		Assert.Matches(@"std::atomic<QGuiApplication \*> app\{", source);
		Assert.Matches(@"std::atomic<QObject \*> receiver\{", source);
		Assert.Matches(@"std::atomic<bool> shutdown\{", source);
	}
}

/// <summary>The op batch contract between the renderer and MauiModelPage.applyMauiOps: every op kind C# emits has a
/// branch in the page (an unknown op is counted and dropped there, silently on the device).</summary>
public class OpContractTests
{
	[Fact]
	public void Every_emitted_op_kind_is_handled_by_the_model_page()
	{
		var src = Path.Combine(Repo.Root, "src", "Linux.SailfishOS");
		var emitted = Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories)
			.Where(f => !f.Contains("/obj/") && !f.Contains("/bin/"))
			.SelectMany(f => Regex.Matches(File.ReadAllText(f), @"\[""op""\]\s*=\s*""(\w+)""").Select(m => m.Groups[1].Value))
			.ToHashSet();
		var page = File.ReadAllText(Path.Combine(src, "Platform", "QtHost", "qml", "MauiModelPage.qml"));
		var handled = Regex.Matches(page, @"o\.op === ""(\w+)""").Select(m => m.Groups[1].Value).ToHashSet();

		Assert.NotEmpty(emitted);
		Assert.Empty(emitted.Except(handled));
		Assert.Empty(handled.Except(emitted));   // a page branch nothing sends is dead code
	}
}

/// <summary>Adapter QML hygiene that the QML engine only reports at load time on the device.</summary>
public class AdapterQmlTests
{
	private static IEnumerable<string> Adapters() =>
		Directory.GetFiles(Path.Combine(Repo.Root, "src", "Linux.SailfishOS", "Platform", "QtHost", "qml"), "*.qml", SearchOption.AllDirectories);

	// A handler for a property that is not declared fails to load the whole adapter on the device ("Cannot assign to
	// non-existent property"); a rename that misses the capitalised handler does exactly that.
	[Fact]
	public void Every_maui_change_handler_has_its_property()
	{
		var problems = new List<string>();
		foreach (var file in Adapters())
		{
			var qml = File.ReadAllText(file);
			var declared = Regex.Matches(qml, @"property\s+[\w<>]+\s+(maui\w+)").Select(m => m.Groups[1].Value).ToHashSet();
			foreach (Match m in Regex.Matches(qml, @"\bonMaui(\w+)Changed\s*:"))
			{
				var property = "maui" + m.Groups[1].Value;
				if (!declared.Contains(property))
					problems.Add($"{Path.GetFileName(file)}: on{char.ToUpperInvariant(property[0])}{property[1..]}Changed without '{property}'");
			}
		}
		Assert.Empty(problems);
	}

	// Actions are mauiCommand calls (AdapterCommands), not a property plus a counter that re-fires equal values.
	[Fact]
	public void No_adapter_drives_an_action_with_a_tick_counter()
	{
		var ticks = Adapters().SelectMany(f => Regex.Matches(File.ReadAllText(f), @"property\s+int\s+(maui\w*Tick)\b")
			.Select(m => $"{Path.GetFileName(f)}: {m.Groups[1].Value}")).ToList();
		Assert.Empty(ticks);
	}
}

/// <summary>The QML → managed event channel: one drain per tick covers every page.</summary>
public class EventDrainContractTests
{
	[Fact]
	public void The_drain_reads_every_model_page_and_the_app_queue()
	{
		var shell = File.ReadAllText(Path.Combine(Repo.Root, "src", "Linux.SailfishOS", "Platform", "QtHost", "qml", "MauiShell.qml"));
		var drainAll = Regex.Match(shell, @"function __mauiDrainAll\(\) \{(?<body>.*?)\n    \}", RegexOptions.Singleline);
		Assert.True(drainAll.Success);
		Assert.Contains("mauiPages", drainAll.Groups["body"].Value);
		Assert.Contains("__mauiAppDrain()", drainAll.Groups["body"].Value);
		Assert.Contains("return __mauiDrainAll();", NativeContractTests.HostSources());
	}
}
