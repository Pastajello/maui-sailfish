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

	/// <summary>Dialog panels opened through MauiModelPage.__pushDialog.</summary>
	public int DialogsOpened { get; private set; }

	public List<string> Evals { get; } = new();
	public List<JsonElement> Ops { get; } = new();

	/// <summary>
	/// Strict mode (default): an eval this fake neither models nor lists in <see cref="AllowedUnanswered"/> is
	/// recorded in <see cref="UnhandledEvals"/>, and <see cref="RendererHarness"/> fails the test on dispose. It is
	/// not thrown from <see cref="Eval"/>, because the renderer catches and logs eval failures: a throw would change
	/// the code path under test and still pass. A test that drives an eval on purpose sets <see cref="EvalHook"/>.
	/// </summary>
	public bool Strict { get; set; } = true;

	/// <summary>Evals that reached the fake unanswered (see <see cref="Strict"/>).</summary>
	public List<string> UnhandledEvals { get; } = new();

	/// <summary>
	/// Evals whose empty answer is the intended fake behaviour, by prefix. Each one is a probe whose caller falls back
	/// to a default when the answer is empty; anything new must be modelled or added here with its reason.
	/// </summary>
	public static readonly IReadOnlyList<(string Prefix, string Why)> AllowedUnanswered =
	[
		("Theme.", "SailfishMeasure theme probes (fontSizeMedium, paddingSmall, …): empty → the dp defaults"),
		("2 * Theme.", "SailfishMeasure composed theme probe: empty → the dp default"),
		("(function(){var o=Qt.createQmlObject(", "SailfishMeasure.TextInputMarginsDp probe of a Silica text field: empty → defaults"),
		("(function(){var p='maui_", "list delegate resync (QtHostListAdapter): empty → no stale delegate found"),
		("window.mauiPreloadAdapters(", "adapter warm-up (fire and forget)"),
		("window.mauiOpsTiming=", "diagnostic flag (fire and forget)"),
		("window.mauiListPrefetch=", "list prefetch flag (fire and forget)"),
		("window.mauiImageTrace=", "diagnostic flag (fire and forget)"),
		("typeof window!=='undefined'&&window?window.orientation", "first orientation read: empty → portrait"),
	];

	/// <summary>A call of a MauiModelPage entry point (QmlPage.Call or a direct page call).</summary>
	internal sealed record PageCall(string Page, string Method, string Arg);

	/// <summary>Page entry points the renderer called, in order (applyMauiOps and the stray sweep excluded).</summary>
	public List<PageCall> PageCalls { get; } = new();

	/// <summary>MauiModelPage functions that take an argument and return nothing the renderer reads.</summary>
	private static readonly HashSet<string> PageMethods = new(StringComparer.Ordinal)
	{
		"setMauiScroll", "setMauiTabs", "setMauiRefresh", "mauiReattachPulleys", "mauiSetTabDrag", "mauiEndTabDrag",
		"mauiRemorse", "mauiRemorseCancel", "__destroyAllHosts", "mauiHoldDrag",
	};

	private static readonly System.Text.RegularExpressions.Regex PageIdRx =
		new(@"mauiPageById\('([^']*)'\)", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

	private static readonly System.Text.RegularExpressions.Regex CallRx =
		new(@"(?:if\(p&&p\.(?<m>[A-Za-z_]+)\)return p\.\k<m>\((?<a>.*)\);return '';\}\)\(\)$)|(?:\)\.(?<m>[A-Za-z_]+)\((?<a>.*)\)$)",
			System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Singleline);

	/// <summary>Records a page entry point; false when <paramref name="expression"/> is not one.</summary>
	private bool TryRecordPageCall(string expression)
	{
		var call = CallRx.Match(expression);
		if (!call.Success || !PageMethods.Contains(call.Groups["m"].Value))
			return false;
		var page = PageIdRx.Match(expression) is { Success: true } id ? id.Groups[1].Value : Pages[^1];
		PageCalls.Add(new PageCall(page, call.Groups["m"].Value, call.Groups["a"].Value));
		return true;
	}

	/// <summary>Row hosts handed to another row by the row pool.</summary>
	public int Rekeys { get; private set; }

	/// <summary>Rekey ops MauiModelPage would refuse (the target id taken).</summary>
	public int RekeysRefused { get; private set; }

	/// <summary>Refuses every rekey, as the page does for a target id held by an object managed does not know.</summary>
	public bool RefuseRekeys { get; set; }

	private int _unknownOps;   // per batch, as MauiModelPage counts them
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
			_unknownOps = 0;
			var (created, destroyed) = ApplyOps(ReadJsString(expression, ops + "applyMauiOps(".Length));
			// MauiModelPage.applyMauiOps's answer, which reaches the caller only when the expression returns it (the
			// QmlPage.Call wrapper used to drop it, and every pooled row then read as refused on the device).
			return expression.Contains("return p.applyMauiOps(", StringComparison.Ordinal)
				? $"{created}:{destroyed}:{_unknownOps}"
				: string.Empty;
		}
		if (expression.StartsWith("(typeof window!=='undefined'&&window.mauiPages", StringComparison.Ordinal))
		{
			return JsonSerializer.Serialize(new NavState(Pages, StackBusy, true, true, 4), NavStateContext.Default.NavState);
		}
		// The stray sweep (MauiModelPage.__destroyHostsNotIn): hosts on the top page that managed no longer knows.
		var sweep = expression.IndexOf("__destroyHostsNotIn(", StringComparison.Ordinal);
		if (sweep >= 0)
		{
			SweepStrays(Pages[^1], ReadJsString(expression, sweep + "__destroyHostsNotIn(".Length));
			return string.Empty;
		}
		// A dialog panel opened over the page (MauiModelPage.__pushDialog); it stays open until a test raises its event.
		if (expression.Contains(".__pushDialog(", StringComparison.Ordinal))
		{
			DialogsOpened++;
			return "ok";
		}
		var push = expression.IndexOf("pageStack.push(window.mauiPageUrl,{mauiPageId:'", StringComparison.Ordinal);
		if (push >= 0)
		{
			var start = push + "pageStack.push(window.mauiPageUrl,{mauiPageId:'".Length;
			var id = expression[start..expression.IndexOf('\'', start)];
			Pages.Add(id);
			return $"ok|depth={Pages.Count}|top=true";
		}
		if (TryRecordPageCall(expression))
			return string.Empty;
		if (Strict && !AllowedUnanswered.Any(a => expression.StartsWith(a.Prefix, StringComparison.Ordinal)))
			UnhandledEvals.Add(expression);
		return string.Empty;
	}

	private int SweepStrays(string page, string knownJson)
	{
		var known = JsonSerializer.Deserialize<string[]>(knownJson) ?? [];
		var strays = Objects.Where(o => o.Page == page && o.Uri != "model-page" && !known.Contains(o.Id)).ToList();
		foreach (var obj in strays)
		{
			obj.Destroyed = true;
			Destroys++;
		}
		return strays.Count;
	}

	private (int Created, int Destroyed) ApplyOps(string json, string? page = null)
	{
		int created = 0, destroyed = 0;
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
						Page = page ?? Pages[^1],
					};
					if (op.TryGetProperty("props", out var props) && props.ValueKind == JsonValueKind.Object)
						foreach (var p in props.EnumerateObject())
							obj.Props[p.Name] = p.Value.Clone();
					if (_byName.TryGetValue("maui_" + id, out var old))
						old.Destroyed = true;
					_byName["maui_" + id] = obj;
					_byHandle[obj.Handle] = obj;
					created++;
					break;
				}
				case "rekey":
				{
					// A pooled row host takes the new element's id (MauiModelPage.applyMauiOps).
					// MauiModelPage refuses when the target id is taken (a live host, or the same id).
					var from = "maui_" + op.GetProperty("from").GetString();
					var to = op.GetProperty("to").GetString()!;
					if (RefuseRekeys || (_byName.TryGetValue("maui_" + to, out var taken) && !taken.Destroyed))
					{
						RekeysRefused++;
						_unknownOps++;
					}
					else if (_byName.Remove(from, out var moved))
					{
						moved.Id = to;
						_byName["maui_" + to] = moved;
						Rekeys++;
					}
					else
						_unknownOps++;
					break;
				}
				case "destroy":
				{
					if (op.TryGetProperty("id", out var idEl) && _byName.TryGetValue("maui_" + idEl.GetString(), out var obj))
					{
						obj.Destroyed = true;
						Destroys++;
						destroyed++;
					}
					else
						_unknownOps++;
					break;
				}
			}
		}
		return (created, destroyed);
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

	public long FindObject(string objectName)
	{
		// A model page is found by its objectName ("mauiPage_<id>") while it is on the stack.
		if (objectName.StartsWith(PagePrefix, StringComparison.Ordinal) && Pages.Contains(objectName[PagePrefix.Length..]) &&
		    !_byName.ContainsKey(objectName))
			AddNative(objectName, "model-page");
		return _byName.TryGetValue(objectName, out var o) && !o.Destroyed ? o.Handle : 0;
	}

	private const string PagePrefix = "mauiPage_";

	/// <summary>Direct page calls (sailfish_host_invoke): page id, method, raw argument.</summary>
	public List<(string Page, string Method, string? Arg)> Invokes { get; } = new();

	/// <summary>sailfish_host_invoke on a model page: the same functions the eval path models.</summary>
	/// <summary>Adapter commands (mauiCommand) sent to objects: object id, command JSON.</summary>
	public List<(string Id, string Json)> Commands { get; } = new();

	public string? Invoke(long handle, string method, string? arg, out int rc)
	{
		if (_byHandle.TryGetValue(handle, out var target) && !target.Destroyed && target.Uri != "model-page")
		{
			// An adapter: commands only (the adapters' mauiCommand).
			rc = method == "mauiCommand" ? 0 : QtHostRuntime.SfhostEProperty;
			if (rc == 0)
				Commands.Add((target.Id, arg ?? string.Empty));
			return rc == 0 ? string.Empty : null;
		}
		if (!_byHandle.TryGetValue(handle, out var o) || o.Destroyed || o.Uri != "model-page" || !Pages.Contains(o.Id[PagePrefix.Length..]))
		{
			if (o is { Uri: "model-page" })
				o.Destroyed = true;   // the page left the stack: its handle is dead
			rc = QtHostRuntime.SfhostEDeadHandle;
			return null;
		}
		var page = o.Id[PagePrefix.Length..];
		Invokes.Add((page, method, arg));
		rc = 0;
		switch (method)
		{
			case "applyMauiOps":
			{
				_unknownOps = 0;
				var (created, destroyed) = ApplyOps(arg ?? "[]", page);
				return $"{created}:{destroyed}:{_unknownOps}";
			}
			case "__destroyHostsNotIn":
				return SweepStrays(page, arg ?? "[]").ToString(System.Globalization.CultureInfo.InvariantCulture);
			default:
				if (!PageMethods.Contains(method))
				{
					rc = QtHostRuntime.SfhostEProperty;   // no such function on the page
					return null;
				}
				PageCalls.Add(new PageCall(page, method, arg is null ? string.Empty : BridgeValue.Quote(arg)));
				return string.Empty;
		}
	}

	/// <summary>Every property written to an object, single or in a batch: (object id, property name).</summary>
	public List<(string Id, string Name)> PropertyWrites { get; } = new();

	public int SetProperty(long handle, string name, string? valueJson)
	{
		if (!_byHandle.TryGetValue(handle, out var o) || o.Destroyed)
			return -3;
		PropertyWrites.Add((o.Id, name));
		using var doc = JsonDocument.Parse(valueJson ?? "null");
		o.Props[name] = doc.RootElement.Clone();
		if (name is "mauiFocus" or "enabled")
			EmulateFocus(o);   // QML reacts to a single set as to a batch
		return 0;
	}

	/// <summary>The next property batch reports this many rejected properties (a partial failure); 0 = none.</summary>
	public int RejectNextBatch { get; set; }

	public int ApplyProperties(long handle, string propsJson)
	{
		if (!_byHandle.TryGetValue(handle, out var o) || o.Destroyed)
			return -3;
		PropertyBatches++;
		if (RejectNextBatch > 0)
		{
			// The real shim applies what it can and returns how many properties it rejected.
			var rejected = RejectNextBatch;
			RejectNextBatch = 0;
			return rejected;
		}
		using var doc = JsonDocument.Parse(propsJson);
		foreach (var entry in doc.RootElement.EnumerateArray())
		{
			o.Props[entry.GetProperty("name").GetString()!] = entry.GetProperty("value").Clone();
			PropertyWrites.Add((o.Id, entry.GetProperty("name").GetString()!));
		}
		EmulateFocus(o);
		return 0;
	}

	public string GetProperty(long handle, string name) =>
		name == "objectName" && _byHandle.TryGetValue(handle, out var named) && !named.Destroyed ? "maui_" + named.Id
		: _byHandle.TryGetValue(handle, out var o) && !o.Destroyed && o.Props.TryGetValue(name, out var v)
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
	/// <summary>When set, posts queue here instead of running inline (tests of the scheduler's latches run them).</summary>
	public Queue<Action>? Deferred { get; set; }

	public void Post(Action action)
	{
		if (Deferred is { } queue)
			queue.Enqueue(action);
		else
			action();
	}

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
