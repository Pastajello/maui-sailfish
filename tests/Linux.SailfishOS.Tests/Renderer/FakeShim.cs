using System.Globalization;
using System.Text.Json;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>
/// In-memory Qt host for renderer tests: models the QML objects, geometry and page stack the
/// renderer sees through the shim and answers its known evals. Text measures 0.5 em per character.
/// </summary>
internal sealed class FakeShim : IQtHostShim
{
	internal sealed class FakeObject
	{
		public required long Handle { get; init; }
		public required string Id { get; set; }
		public required string Uri { get; init; }
		public string Page { get; init; } = "mp1";
		public Dictionary<string, JsonElement> Props { get; } = new(StringComparer.Ordinal);
		public NativeGeometry Geometry { get; set; }
		public bool Visible { get; set; } = true;
		public bool Destroyed { get; set; }
		public long ParentHandle { get; set; }   // the last SetParentItem

		public string? Text(string prop) =>
			Props.TryGetValue(prop, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString()) : null;
	}

	private long _nextHandle = 1000;
	private readonly Dictionary<string, FakeObject> _byName = new(StringComparer.Ordinal);
	private readonly Dictionary<long, FakeObject> _byHandle = new();

	/// <summary>Model pages bottom → top (the shell pushes "mp1" at startup).</summary>
	public List<string> Pages { get; } = new() { "mp1" };

	/// <summary>pageStack.busy as the nav-state poll reads it (an animated transition in flight).</summary>
	public bool StackBusy { get; set; }

	public List<string> Evals { get; } = new();
	public List<JsonElement> Ops { get; } = new();

	/// <summary>Row hosts handed to another row by the row pool.</summary>
	public int Rekeys { get; private set; }
	public int PropertyBatches { get; private set; }
	public int GeometryBatches { get; private set; }
	public int Destroys { get; private set; }

	/// <summary>Live (not destroyed) objects.</summary>
	public IEnumerable<FakeObject> Objects => _byHandle.Values.Where(o => !o.Destroyed);

	public FakeObject? ById(string id) => _byName.TryGetValue("maui_" + id, out var o) && !o.Destroyed ? o : null;

	public IEnumerable<FakeObject> ByUri(string uri) => Objects.Where(o => o.Uri == uri);

	/// <summary>Answers an Eval before the built-in handling (platform service tests); null = not handled.</summary>
	public Func<string, string?>? EvalHook { get; set; }

	public string Eval(string expression)
	{
		Evals.Add(expression);
		if (EvalHook?.Invoke(expression) is { } hooked)
			return hooked;
		var ops = expression.IndexOf("applyMauiOps(", StringComparison.Ordinal);
		if (ops >= 0)
		{
			ApplyOps(ReadJsString(expression, ops + "applyMauiOps(".Length));
			return string.Empty;
		}
		if (expression.StartsWith("(typeof window!=='undefined'&&window.mauiPages", StringComparison.Ordinal))
		{
			return JsonSerializer.Serialize(new NavState(Pages, StackBusy, true, true, 4), NavStateContext.Default.NavState);
		}
		// The stray sweep (MauiModelPage.__destroyHostsNotIn): hosts on the top page that managed no longer knows.
		var sweep = expression.IndexOf("__destroyHostsNotIn(", StringComparison.Ordinal);
		if (sweep >= 0)
		{
			var known = JsonSerializer.Deserialize<string[]>(ReadJsString(expression, sweep + "__destroyHostsNotIn(".Length)) ?? [];
			foreach (var obj in Objects.Where(o => o.Page == Pages[^1] && !known.Contains(o.Id)).ToList())
			{
				obj.Destroyed = true;
				Destroys++;
			}
			return string.Empty;
		}
		var push = expression.IndexOf("pageStack.push(window.mauiPageUrl,{mauiPageId:'", StringComparison.Ordinal);
		if (push >= 0)
		{
			var start = push + "pageStack.push(window.mauiPageUrl,{mauiPageId:'".Length;
			var id = expression[start..expression.IndexOf('\'', start)];
			Pages.Add(id);
			return $"ok|depth={Pages.Count}|top=true";
		}
		return string.Empty;
	}

	private void ApplyOps(string json)
	{
		using var doc = JsonDocument.Parse(json);
		foreach (var op in doc.RootElement.EnumerateArray())
		{
			Ops.Add(op.Clone());
			var kind = op.TryGetProperty("op", out var k) ? k.GetString() : null;
			switch (kind)
			{
				case "create":
				{
					var id = op.GetProperty("id").GetString()!;
					var obj = new FakeObject
					{
						Handle = ++_nextHandle,
						Id = id,
						Uri = op.TryGetProperty("uri", out var u) ? u.GetString() ?? string.Empty : string.Empty,
						Page = Pages[^1],
					};
					if (op.TryGetProperty("props", out var props) && props.ValueKind == JsonValueKind.Object)
						foreach (var p in props.EnumerateObject())
							obj.Props[p.Name] = p.Value.Clone();
					if (_byName.TryGetValue("maui_" + id, out var old))
						old.Destroyed = true;
					_byName["maui_" + id] = obj;
					_byHandle[obj.Handle] = obj;
					break;
				}
				case "rekey":
				{
					// A pooled row host takes the new element's id (MauiModelPage.applyMauiOps).
					var from = "maui_" + op.GetProperty("from").GetString();
					var to = op.GetProperty("to").GetString()!;
					if (_byName.Remove(from, out var moved))
					{
						moved.Id = to;
						_byName["maui_" + to] = moved;
						Rekeys++;
					}
					break;
				}
				case "destroy":
				{
					if (op.TryGetProperty("id", out var idEl) && _byName.TryGetValue("maui_" + idEl.GetString(), out var obj))
					{
						obj.Destroyed = true;
						Destroys++;
					}
					break;
				}
			}
		}
	}

	/// <summary>Reads the JS string literal (ToJsString output) at <paramref name="at"/>.</summary>
	private static string ReadJsString(string s, int at)
	{
		var end = at + 1;
		while (end < s.Length && !(s[end] == '"' && s[end - 1] != '\\'))
			end++;
		// a trailing backslash pair ("\\") before the quote ends a literal too
		while (end < s.Length && s[end] == '"' && CountBackslashes(s, end) % 2 == 1)
		{
			end++;
			while (end < s.Length && s[end] != '"')
				end++;
		}
		return JsonSerializer.Deserialize(s[at..(end + 1)], NavStateContext.Default.String)!;
	}

	private static int CountBackslashes(string s, int quote)
	{
		var n = 0;
		for (var i = quote - 1; i >= 0 && s[i] == '\\'; i--)
			n++;
		return n;
	}

	/// <summary>A QML object the shell made by itself (a ListView delegate placeholder), findable by objectName.</summary>
	public FakeObject AddNative(string objectName, string uri = "delegate")
	{
		var o = new FakeObject { Handle = ++_nextHandle, Id = objectName, Uri = uri };
		_byName[objectName] = o;
		_byHandle[o.Handle] = o;
		return o;
	}

	public long FindObject(string objectName) =>
		_byName.TryGetValue(objectName, out var o) && !o.Destroyed ? o.Handle : 0;

	public int SetProperty(long handle, string name, string? valueJson)
	{
		if (!_byHandle.TryGetValue(handle, out var o) || o.Destroyed)
			return -3;
		using var doc = JsonDocument.Parse(valueJson ?? "null");
		o.Props[name] = doc.RootElement.Clone();
		return 0;
	}

	public int ApplyProperties(long handle, string propsJson)
	{
		if (!_byHandle.TryGetValue(handle, out var o) || o.Destroyed)
			return -3;
		PropertyBatches++;
		using var doc = JsonDocument.Parse(propsJson);
		foreach (var entry in doc.RootElement.EnumerateArray())
			o.Props[entry.GetProperty("name").GetString()!] = entry.GetProperty("value").Clone();
		EmulateFocus(o);
		return 0;
	}

	public string GetProperty(long handle, string name) =>
		_byHandle.TryGetValue(handle, out var o) && !o.Destroyed && o.Props.TryGetValue(name, out var v)
			? v.ValueKind switch
			{
				JsonValueKind.String => v.GetString() ?? string.Empty,
				JsonValueKind.True => "true",    // as QVariant(bool) reads back from the real shim
				JsonValueKind.False => "false",
				_ => v.ToString(),
			}
			: string.Empty;

	/// <summary>A text adapter takes active focus on mauiFocus unless it is disabled (Qt refuses).</summary>
	private static void EmulateFocus(FakeObject o)
	{
		if (!o.Props.TryGetValue("mauiFocus", out var focus))
			return;
		var enabled = !o.Props.TryGetValue("enabled", out var e) || e.ValueKind != JsonValueKind.False;
		o.Props["activeFocus"] = JsonSerializer.SerializeToElement(focus.ValueKind == JsonValueKind.True && enabled);
	}

	public bool TryItemGeometry(long handle, out NativeGeometry geometry)
	{
		if (_byHandle.TryGetValue(handle, out var o) && !o.Destroyed)
		{
			geometry = o.Geometry;
			return true;
		}
		geometry = default;
		return false;
	}

	public bool SetParentItem(long handle, long parent)
	{
		if (!_byHandle.TryGetValue(handle, out var o))
			return false;
		o.ParentHandle = parent;
		return true;
	}

	public int ApplyGeometry(string geoJson)
	{
		GeometryBatches++;
		using var doc = JsonDocument.Parse(geoJson);
		var failed = 0;
		foreach (var e in doc.RootElement.EnumerateArray())
		{
			var handle = long.Parse(e.GetProperty("handle").GetString()!, CultureInfo.InvariantCulture);
			if (!_byHandle.TryGetValue(handle, out var o) || o.Destroyed)
			{
				failed++;
				continue;
			}
			double N(string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
			o.Geometry = new NativeGeometry(N("x"), N("y"), N("w"), N("h"));
			if (e.TryGetProperty("vis", out var vis))
				o.Visible = vis.ValueKind == JsonValueKind.Number ? vis.GetInt32() != 0 : vis.ValueKind == JsonValueKind.True;
		}
		return failed;
	}

	/// <summary>Text measures that reached the shim.</summary>
	public int TextMeasures { get; private set; }

	public bool TryMeasureText(string json, out double widthPx, out double heightPx)
	{
		TextMeasures++;
		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;
		var text = root.GetProperty("text").GetString() ?? string.Empty;
		var px = root.TryGetProperty("px", out var p) ? p.GetDouble() : 16;
		var maxW = root.TryGetProperty("maxW", out var m) ? m.GetDouble() : 0;
		var width = text.Length * px * 0.5;
		var lines = maxW > 0 && width > maxW ? Math.Ceiling(width / maxW) : 1;
		widthPx = maxW > 0 ? Math.Min(width, maxW) : width;
		heightPx = lines * px * 1.2;
		return true;
	}

	public string ScreenInfo() =>
		"{\"window\":{\"x\":0,\"y\":0,\"width\":1080,\"height\":2160,\"dpr\":1},\"screen\":{\"name\":\"fake\",\"orientation\":\"Portrait\"}}";

	public void DestroyObject(long handle)
	{
		if (_byHandle.TryGetValue(handle, out var o))
		{
			o.Destroyed = true;
			Destroys++;
		}
	}

	public int PushPage(string qmlPath, string? propsJson) => 0;

	public int PopPage()
	{
		if (Pages.Count > 1)
			Pages.RemoveAt(Pages.Count - 1);
		return 0;
	}

	/// <summary>Posts run inline (the test thread is the "Qt thread").</summary>
	public void Post(Action action) => action();

	/* --- Drawing surfaces --- */

	/// <summary>One surface commit; Pixels is a copy (empty when the surface was released).</summary>
	internal sealed record SurfaceCommitRecord(long Handle, int Width, int Height, byte[] Pixels);

	public List<SurfaceCommitRecord> SurfaceCommits { get; } = new();
	public int FrameRequests { get; private set; }
	public Dictionary<long, bool> SurfaceTouch { get; } = new();

	public int SurfaceCommit(long handle, IntPtr pixels, int width, int height, int stride)
	{
		var copy = new byte[width > 0 && height > 0 && pixels != IntPtr.Zero ? width * height * 4 : 0];
		for (var y = 0; copy.Length > 0 && y < height; y++)
			System.Runtime.InteropServices.Marshal.Copy(pixels + y * stride, copy, y * width * 4, width * 4);
		SurfaceCommits.Add(new SurfaceCommitRecord(handle, width, height, copy));
		return _byHandle.TryGetValue(handle, out var o) && !o.Destroyed ? QtHostRuntime.SfhostOk : QtHostRuntime.SfhostEDeadHandle;
	}

	public void RequestFrame() => FrameRequests++;

	public int SurfaceSetTouch(long handle, bool enabled)
	{
		SurfaceTouch[handle] = enabled;
		return QtHostRuntime.SfhostOk;
	}
}

internal sealed record NavState(List<string> ids, bool busy, bool topModel, bool active, int appState);

[System.Text.Json.Serialization.JsonSerializable(typeof(NavState))]
[System.Text.Json.Serialization.JsonSerializable(typeof(string))]
internal sealed partial class NavStateContext : System.Text.Json.Serialization.JsonSerializerContext
{
}
