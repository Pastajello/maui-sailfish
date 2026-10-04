using System.Reflection;
using System.Text.Json;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Linux.SailfishOS.Tests;

public static class Repo
{
	public static string Root { get; } =
		typeof(Repo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
			.First(a => a.Key == "RepoRoot").Value!;
}

/// <summary>Adapter library contract: the renderer only knows adapters.json's uri→src map and every
/// adapter implements mauiId/mauiProbe/mauiEvent; a broken entry is a silent blank control on device.</summary>
public class AdaptersContractTests
{
	private static readonly string AdaptersPath =
		Path.Combine(Repo.Root, "src/Linux.SailfishOS/Platform/QtHost/qml/adapters.json");

	private static JsonElement Adapters()
	{
		using var doc = JsonDocument.Parse(File.ReadAllText(AdaptersPath));
		return doc.RootElement.GetProperty("adapters").Clone();
	}

	[Fact]
	public void Every_adapter_source_exists_and_implements_the_contract()
	{
		var root = Adapters();
		var checkedCount = 0;
		foreach (var entry in root.EnumerateObject())
		{
			var src = Path.Combine(Repo.Root, "src/Linux.SailfishOS/Platform/QtHost/qml", entry.Value.GetString()!);
			Assert.True(File.Exists(src), $"adapter source missing: {src}");
			var text = WithLocalBase(src);
			Assert.Contains("mauiId", text);
			Assert.Contains("mauiProbe", text);
			Assert.Contains("mauiEvent", text);
			checkedCount++;
		}
		Assert.True(checkedCount >= 30, $"expected the full adapter map, got {checkedCount} entries");
	}

	// An adapter whose root type is another adapter in its directory (Grid.qml is a ContentView { … }) inherits that
	// file's contract properties, so the check reads both. Comments are dropped: they name the contract too.
	private static string WithLocalBase(string src)
	{
		var text = System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(src), @"//[^\n]*", "");
		var root = System.Text.RegularExpressions.Regex.Match(text, @"^([A-Z]\w*)\s*\{", System.Text.RegularExpressions.RegexOptions.Multiline);
		var local = Path.Combine(Path.GetDirectoryName(src)!, root.Groups[1].Value + ".qml");
		return root.Success && File.Exists(local) && local != src ? text + WithLocalBase(local) : text;
	}

	private static readonly string QmlRoot = Path.Combine(Repo.Root, "src/Linux.SailfishOS/Platform/QtHost/qml");

	private static IEnumerable<string> QmlSources() =>
		Directory.EnumerateFiles(QmlRoot, "*.qml", SearchOption.AllDirectories)
			.Concat(Directory.EnumerateFiles(Path.Combine(QmlRoot, "lib"), "*.js"));

	// One event shape (qml/lib/adapter.js): managed resolves the host from "id" and never parses a bare id, so an
	// adapter that emits anything else is a tap or a dialog result that silently goes nowhere.
	[Fact]
	public void Every_adapter_event_payload_is_an_object_with_id()
	{
		var offenders = new List<string>();
		foreach (var file in QmlSources())
		{
			var text = File.ReadAllText(file);
			foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, @"mauiEvent\(\s*""([a-z-]+)""\s*,\s*"))
			{
				var rest = text[m.Index..Math.Min(text.Length, m.Index + m.Length + 200)][m.Length..];
				if (!System.Text.RegularExpressions.Regex.IsMatch(rest, @"^JSON\.stringify\(\{\s*id\s*:"))
					offenders.Add($"{Path.GetRelativePath(QmlRoot, file)}:{text[..m.Index].Count(c => c == '\n') + 1} {m.Groups[1].Value}");
			}
		}
		Assert.True(offenders.Count == 0,
			"emit adapter events through Adapter.emit/guarded or with JSON.stringify({ id: … }): " + string.Join(", ", offenders));
	}

	// Page events (MauiModelPage.mauiNotify) name their page, so managed can tell them from a host's event of the
	// same name (the page flickable's "scroll-changed" against a ScrollView's).
	[Fact]
	public void Page_events_go_through_page_emit()
	{
		var page = File.ReadAllText(Path.Combine(QmlRoot, "MauiModelPage.qml"));
		var direct = System.Text.RegularExpressions.Regex.Matches(page, @"(?<![.\w])(?:page\.)?mauiNotify\(\s*""([a-z-]+)""")
			.Select(m => m.Groups[1].Value).ToList();
		// refresh-requested names the RefreshView host by "id" (an adapter event raised by the page on its behalf).
		Assert.True(direct.All(n => n == "refresh-requested"), "use Adapter.pageEmit for: " + string.Join(", ", direct));
	}

	private static readonly System.Text.RegularExpressions.Regex EmitRx = new(
		@"(?:mauiEvent|mauiNotify|mauiAppNotify)\(\s*""(?<n>[a-z0-9-]+)""|Adapter\.(?:emit|guarded|pageEmit)\(\s*\w+\s*,\s*""(?<n>[a-z0-9-]+)""");

	/// <summary>Event names the QML side emits (adapters, pages, the shell, the shared js libraries).</summary>
	private static HashSet<string> EmittedEvents() =>
		QmlSources().SelectMany(f => EmitRx.Matches(File.ReadAllText(f)).Select(m => m.Groups["n"].Value))
			.ToHashSet(StringComparer.Ordinal);

	/// <summary>Event names managed consumes: the renderer's switch, the collection bridge's, the handlers'
	/// OnAdapterEvent (`name == "…"`) and the shell services.</summary>
	private static HashSet<string> ConsumedEvents()
	{
		var core = Path.Combine(Repo.Root, "src/Linux.SailfishOS");
		var names = new HashSet<string>(StringComparer.Ordinal);
		foreach (var file in Directory.EnumerateFiles(core, "*.cs", SearchOption.AllDirectories)
			         .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
		{
			var text = File.ReadAllText(file);
			foreach (var rx in new[] { @"case ""([a-z0-9-]+)""\s*:", @"name == ""([a-z0-9-]+)""", @"Subscribe\(""([a-z0-9-]+)""" })
				names.UnionWith(System.Text.RegularExpressions.Regex.Matches(text, rx).Select(m => m.Groups[1].Value));
			// Handlers compare against SailfishKeys.Event constants.
			foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, @"name == SailfishKeys\.Event\.(\w+)"))
				if (typeof(Microsoft.Maui.SailfishOS.Handlers.SailfishKeys.Event).GetField(m.Groups[1].Value)?.GetRawConstantValue() is string value)
					names.Add(value);
		}
		return names;
	}

	/// <summary>Events the QML side emits only for the on-device diagnostics (QtHostDiagnosticsRunner reads them from
	/// the device log or through QmlEvent), and known gaps with the plan step that closes them.</summary>
	private static readonly Dictionary<string, string> UnconsumedOnPurpose = new(StringComparer.Ordinal)
	{
		["canvas-painted"] = "diagnostics: GraphicsView paint timing",
		["shape-painted"] = "diagnostics: Shape paint timing",
		["pulley-items"] = "diagnostics: pulley menu contents",
		["pulley-attached"] = "diagnostics: pulley attach report",
		["pulley-open-try"] = "diagnostics: scripted pulley open",
		["pulley-opened"] = "diagnostics: scripted pulley open",
	};

	// Both directions of the event contract: a case label without an emitter is dead code that hides a renamed event,
	// and an emitted event nobody consumes is a user action that silently goes nowhere.
	[Fact]
	public void Every_emitted_event_has_a_consumer_and_every_handled_event_an_emitter()
	{
		var emitted = EmittedEvents();
		var consumed = ConsumedEvents();
		var unconsumed = emitted.Where(e => !consumed.Contains(e) && !UnconsumedOnPurpose.ContainsKey(e)
		                                   && !e.StartsWith("svc-", StringComparison.Ordinal)).Order().ToList();
		Assert.True(unconsumed.Count == 0, "emitted by QML, consumed nowhere: " + string.Join(", ", unconsumed));

		// The renderer's and the collection bridge's own switches: every label must still be emitted somewhere.
		var switches = new[] { "Platform/QtHost/AdapterEventRouter.cs", "Platform/QtHost/QtHostCollectionBridge.cs" }
			.Select(f => File.ReadAllText(Path.Combine(Repo.Root, "src/Linux.SailfishOS", f)));
		var labels = switches.SelectMany(t => System.Text.RegularExpressions.Regex.Matches(t, @"case ""([a-z0-9-]+)""\s*:")
			.Select(m => m.Groups[1].Value)).ToHashSet(StringComparer.Ordinal);
		var deadLabels = labels.Where(l => !emitted.Contains(l)).Order().ToList();
		Assert.True(deadLabels.Count == 0, "handled but never emitted by QML: " + string.Join(", ", deadLabels));
		Assert.True(UnconsumedOnPurpose.Keys.All(emitted.Contains), "an allow-listed event is no longer emitted; drop it from the list");
	}

	// Every app-level event name, wherever it is emitted (shell QML, the shim, the inline QML services kept as C# raw
	// strings), is one of ShellEvents' constants, and C# subscribes only through those constants.
	[Fact]
	public void Every_shell_event_has_a_constant_and_is_subscribed_through_it()
	{
		var core = Path.Combine(Repo.Root, "src/Linux.SailfishOS");
		var shellEvents = File.ReadAllText(Path.Combine(core, "Platform/QtHost/ShellEvents.cs"));
		var constants = System.Text.RegularExpressions.Regex.Matches(shellEvents, @"const string \w+ = ""(svc-[a-z-]*)""")
			.Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
		const string sensorPrefix = "svc-sensor-";
		Assert.Contains(sensorPrefix, constants);

		var sources = Directory.EnumerateFiles(core, "*.*", SearchOption.AllDirectories)
			.Where(f => f.EndsWith(".cs") || f.EndsWith(".qml") || f.EndsWith(".js") || f.EndsWith(".cpp"))
			.Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
			.Where(f => !f.EndsWith("ShellEvents.cs"));
		var unknown = new SortedSet<string>(StringComparer.Ordinal);
		var literalSubscriptions = new List<string>();
		foreach (var file in sources)
		{
			var text = File.ReadAllText(file);
			foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, @"""(svc-[a-z-]+)"))
			{
				var name = m.Groups[1].Value;
				if (!constants.Contains(name) && !name.StartsWith(sensorPrefix, StringComparison.Ordinal))
					unknown.Add($"{Path.GetFileName(file)}: {name}");
			}
			if (file.EndsWith(".cs"))
				literalSubscriptions.AddRange(System.Text.RegularExpressions.Regex.Matches(text, @"(?:Subscribe\(|== )""svc-[a-z-]*""")
					.Select(m => $"{Path.GetFileName(file)}: {m.Value}"));
		}
		Assert.True(unknown.Count == 0, "svc-* event without a ShellEvents constant: " + string.Join(", ", unknown));
		Assert.True(literalSubscriptions.Count == 0, "subscribe through ShellEvents: " + string.Join(", ", literalSubscriptions));
	}

	// A uri nothing asks for is a dead adapter that still ships, warms up and has to be kept in step with the contract.
	[Fact]
	public void Adapter_map_has_no_unused_uris()
	{
		var csharp = string.Join('\n', Directory.EnumerateFiles(Path.Combine(Repo.Root, "src"), "*.cs", SearchOption.AllDirectories)
			.Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
			            && !f.EndsWith("QtHostAdapters.cs"))
			.Select(File.ReadAllText));
		var unused = Adapters().EnumerateObject().Select(e => e.Name)
			.Where(uri => !csharp.Contains($"\"{uri}\"", StringComparison.Ordinal)).ToList();
		Assert.True(unused.Count == 0, "adapters.json uris no C# code names: " + string.Join(", ", unused));
	}

	// One version source (D4 of docs/architecture-plan.md): every packed file takes it from Directory.Build.props, so a
	// second literal is a version that will not move with a bump. Docs show the current number on purpose.
	[Fact]
	public void The_package_version_is_written_down_once()
	{
		var props = File.ReadAllText(Path.Combine(Repo.Root, "Directory.Build.props"));
		var version = System.Text.RegularExpressions.Regex.Match(props, @"<PlatformMauiSailfishVersion[^>]*>([^<]+)<").Groups[1].Value;
		Assert.False(string.IsNullOrEmpty(version));
		var git = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", "ls-files")
			{ WorkingDirectory = Repo.Root, RedirectStandardOutput = true })!;
		var files = git.StandardOutput.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
		git.WaitForExit();
		var literal = new System.Text.RegularExpressions.Regex($@"(?<![\d.]){System.Text.RegularExpressions.Regex.Escape(version)}(?![\d.])");
		var extensions = new[] { ".cs", ".csproj", ".props", ".targets", ".json", ".in", ".sh", ".qml", ".xml", ".yaml", ".spec" };
		var offenders = files
			.Where(f => extensions.Contains(Path.GetExtension(f)) && f != "Directory.Build.props")
			.Where(f => File.Exists(Path.Combine(Repo.Root, f)) && literal.IsMatch(File.ReadAllText(Path.Combine(Repo.Root, f))))
			.ToList();
		Assert.True(offenders.Count == 0, $"{version} written outside Directory.Build.props: " + string.Join(", ", offenders));
	}

	[Fact]
	public void Adapter_map_covers_the_core_control_kinds()
	{
		var root = Adapters();
		foreach (var kind in new[] { "label", "button", "entry", "list-view", "graphics-view" })
			Assert.True(root.TryGetProperty(kind, out _), $"adapter map lost {kind}");
	}
}

/// <summary>Bridge serialization: the shim parses exactly this JSON dialect, so escaping and the
/// #AARRGGBB color form are contract.</summary>
public class BridgeValueTests
{
	[Theory]
	[InlineData("plain", "\"plain\"")]
	[InlineData("a\"b", "\"a\\\"b\"")]
	[InlineData("a\\b", "\"a\\\\b\"")]
	[InlineData("a\nb", "\"a\\nb\"")]
	[InlineData("a\tb", "\"a\\u0009b\"")]
	[InlineData("a\u2028b\u2029", "\"a\\u2028b\\u2029\"")]
	public void Quote_escapes_the_bridge_dialect(string input, string expected) =>
		Assert.Equal(expected, BridgeValue.Serialize(input));

	[Theory]
	[InlineData(0.1, "0.1")]
	[InlineData(-2.5, "-2.5")]
	[InlineData(1e21, "1E+21")]
	public void Numbers_are_invariant_round_trip(double value, string expected)
	{
		var culture = System.Globalization.CultureInfo.CurrentCulture;
		System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("pl-PL");
		try { Assert.Equal(expected, BridgeValue.Number(value)); }
		finally { System.Globalization.CultureInfo.CurrentCulture = culture; }
	}

	[Theory]
	[InlineData("{\"row\":3}", "row", 3.0)]
	[InlineData("{\"row\":\"x\"}", "row", -1.0)]
	[InlineData("{}", "row", -1.0)]
	public void Payload_numbers_fall_back_when_missing(string json, string name, double expected)
	{
		using var doc = JsonDocument.Parse(json);
		Assert.Equal(expected, BridgeJson.Num(doc.RootElement, name, -1));
	}

	[Fact]
	public void Colors_cross_as_AARRGGBB_for_QColor() =>
		Assert.Equal("\"#FFFF0000\"", BridgeValue.Serialize(Colors.Red));

	[Fact]
	public void Primitives_and_null_keep_JSON_forms()
	{
		Assert.Equal("null", BridgeValue.Serialize(null));
		Assert.Equal("true", BridgeValue.Serialize(true));
		Assert.Equal("42", BridgeValue.Serialize(42));
	}

	[Fact]
	public void Maps_and_collections_cross_as_objects_and_arrays()
	{
		Assert.Equal("{\"op\":\"title\"}",
			BridgeValue.Serialize(new Dictionary<string, object?> { ["op"] = "title" }));
		Assert.Equal("[\"a\",\"b\"]", BridgeValue.Serialize(new[] { "a", "b" }));
	}

	[Fact]
	public void Rects_cross_as_xywh_objects() =>
		Assert.Equal("{\"x\":1,\"y\":2,\"width\":3,\"height\":4}",
			BridgeValue.Serialize(new Rect(1, 2, 3, 4)));
}

/// <summary>Model-page addressing is contract with MauiModelPage.qml/MauiShell.qml; a wrong target leaks hosts.</summary>
public class QmlPageTests
{
	[Fact]
	public void Call_guards_page_and_function() =>
		Assert.Equal("(function(){var p=window.mauiPageById('p3');if(p&&p.applyMauiOps)return p.applyMauiOps(\"[]\");return '';})()",
			QmlPage.Call(QmlPage.ById("p3"), "applyMauiOps", BridgeValue.Quote("[]")));

	[Fact]
	public void Call_without_argument() =>
		Assert.Equal("(function(){var p=pageStack.currentPage;if(p&&p.__destroyAllHosts)return p.__destroyAllHosts();return '';})()",
			QmlPage.Call("pageStack.currentPage", "__destroyAllHosts"));

	[Fact]
	public void ById_falls_back_when_the_registry_lost_the_page() =>
		Assert.Equal("(window.mauiPageById('p1')||pageStack.currentPage)",
			QmlPage.ByIdOr("p1", "pageStack.currentPage"));

	[Fact]
	public void Model_page_prefers_the_shell_pointer_over_currentPage() =>
		Assert.StartsWith("(typeof window!=='undefined'&&window.mauiModelPage?", QmlPage.Model);
}

/// <summary>The shim parses property batches as a JSON array; the mauiApplying envelope is optional.</summary>
public class BridgeBatchTests
{
	private static readonly (string, string)[] Props = { ("text", "\"a\""), ("value", "2") };

	[Fact]
	public void Suppressed_batch_is_wrapped_in_the_applying_envelope() =>
		Assert.Equal("[{\"name\":\"mauiApplying\",\"value\":true},{\"name\":\"text\",\"value\":\"a\"}," +
		             "{\"name\":\"value\",\"value\":2},{\"name\":\"mauiApplying\",\"value\":false}]",
			QtHostBridge.BuildBatch(Props));

	[Fact]
	public void Unsuppressed_batch_is_valid_json()
	{
		var json = QtHostBridge.BuildBatch(Props, suppress: false);
		Assert.Equal("[{\"name\":\"text\",\"value\":\"a\"},{\"name\":\"value\",\"value\":2}]", json);
		using var doc = JsonDocument.Parse(json);
		Assert.Equal(2, doc.RootElement.GetArrayLength());
	}

	[Fact]
	public void Empty_unsuppressed_batch_is_an_empty_array() =>
		Assert.Equal("[]", QtHostBridge.BuildBatch(Array.Empty<(string, string)>(), suppress: false));
}
