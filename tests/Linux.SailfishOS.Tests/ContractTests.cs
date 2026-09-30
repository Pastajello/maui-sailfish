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
		Assert.Equal("(function(){var p=window.mauiPageById('p3');if(p&&p.applyMauiOps)p.applyMauiOps(\"[]\");})()",
			QmlPage.Call(QmlPage.ById("p3"), "applyMauiOps", BridgeValue.Quote("[]")));

	[Fact]
	public void Call_without_argument() =>
		Assert.Equal("(function(){var p=pageStack.currentPage;if(p&&p.__destroyAllHosts)p.__destroyAllHosts();})()",
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
