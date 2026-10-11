using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.SailfishOS.Platform;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Diagnostics;

/// <summary>
/// Drives an app's real UI for scripted tours and self-checks: synthetic pointer events that go through Qt, the
/// input router and Silica as a finger would (pulleys open, context menus pop, back swipes go back), element
/// geometry in scene pixels, QML read-back, and the <c>SF-SHOT</c> marker protocol of <c>tools/sf shots</c>.
/// Call it on the UI thread; awaits resume there.
/// </summary>
public static class SailfishUiDriver
{
	private const int StepMs = 16;

	private static (double X, double Y) _finger;

	/// <summary>The element's rectangle in scene (window) pixels, null while it is not rendered on the current page.</summary>
	public static Rect? SceneBounds(VisualElement element) =>
		HostOf(element) is { } host && QtHostRuntime.TryItemGeometry(host.NativeHandle, out var g)
			? new Rect(g.X, g.Y, g.Width, g.Height)
			: null;

	/// <summary>The element's live host: a page host, or one in a list's rows, header or footer (those are not on the
	/// page's host list).</summary>
	private static NativeElementHost? HostOf(VisualElement element)
	{
		if (SailfishRenderSession.OfApp?.Renderer is not { } renderer)
			return null;
		if (DiagQml.HostOf(renderer, element) is { IsAttached: true } host)
			return host;
		return renderer.HostIdOf(element) is { } id && renderer.TryGetHost(id, out var cached) &&
		       ReferenceEquals(cached.Element, element) && cached.IsAttached
			? cached
			: null;
	}

	/// <summary>The MAUI view of a built row of the current page's list (flat rows: group headers count; a grid row's
	/// <paramref name="cell"/>), null when that row is not materialized.</summary>
	public static View? CollectionRow(int row, int cell = 0) => SailfishRenderSession.OfApp?.Renderer.Collection.RowView(row, cell);

	/// <summary>A scene point inside a materialized row of the current page's list (a tap or press target).</summary>
	public static Point? CollectionRowPoint(int row) =>
		SailfishRenderSession.OfApp?.Renderer.Collection.TryGetRowPoint(row, out var x, out var y) == true ? new Point(x, y) : null;

	/// <summary>The window size in scene pixels.</summary>
	public static Size SceneSize =>
		new(DiagQml.EvalNum("pageStack.width", 0), DiagQml.EvalNum("pageStack.height", 0));

	/// <summary>A press and release at scene pixels.</summary>
	public static async Task TapAsync(double x, double y)
	{
		QtHostRuntime.InjectPointer(0, x, y);
		await Task.Delay(60);
		QtHostRuntime.InjectPointer(1, x, y);
		await Task.Delay(300);
	}

	/// <summary>A tap at the centre of <paramref name="element"/>; false when it is not rendered.</summary>
	public static async Task<bool> TapAsync(VisualElement element)
	{
		if (SceneBounds(element) is not { } r)
			return false;
		await TapAsync(r.Center.X, r.Center.Y);
		return true;
	}

	/// <summary>Two quick taps at scene pixels (inside the 300 ms multi-tap window).</summary>
	public static async Task DoubleTapAsync(double x, double y)
	{
		QtHostRuntime.InjectPointer(0, x, y);
		await Task.Delay(40);
		QtHostRuntime.InjectPointer(1, x, y);
		await Task.Delay(120);
		QtHostRuntime.InjectPointer(0, x, y);
		await Task.Delay(40);
		QtHostRuntime.InjectPointer(1, x, y);
		await Task.Delay(400);
	}

	/// <summary>Press and hold (Silica's context menus open after ~800 ms), then release.</summary>
	public static async Task LongPressAsync(double x, double y, int holdMs = 1200)
	{
		QtHostRuntime.InjectPointer(0, x, y);
		await Task.Delay(holdMs);
		QtHostRuntime.InjectPointer(1, x, y);
		await Task.Delay(400);
	}

	/// <summary>Puts a finger down at scene pixels (pair with <see cref="MoveToAsync"/> and <see cref="Release"/>).</summary>
	public static void Press(double x, double y)
	{
		_finger = (x, y);
		QtHostRuntime.InjectPointer(0, x, y);
	}

	/// <summary>Moves the finger that is down to (x, y) in <paramref name="durationMs"/>, a move event every 16 ms.</summary>
	public static async Task MoveToAsync(double x, double y, int durationMs = 240)
	{
		var (x0, y0) = _finger;
		var steps = Math.Max(2, durationMs / StepMs);
		for (var i = 1; i <= steps; i++)
		{
			await Task.Delay(StepMs);
			QtHostRuntime.InjectPointer(2, x0 + (x - x0) * i / steps, y0 + (y - y0) * i / steps);
		}
		_finger = (x, y);
	}

	/// <summary>Lifts the finger where it is.</summary>
	public static void Release() => QtHostRuntime.InjectPointer(1, _finger.X, _finger.Y);

	/// <summary>
	/// A finger drag from (x0, y0) to (x1, y1) in <paramref name="durationMs"/>. <paramref name="whileHeld"/> runs at the end
	/// with the finger still down (a screenshot of an open pulley; it may move the finger on), then the finger lifts.
	/// </summary>
	public static async Task DragAsync(double x0, double y0, double x1, double y1, int durationMs = 240,
		Func<Task>? whileHeld = null)
	{
		Press(x0, y0);
		await MoveToAsync(x1, y1, durationMs);
		if (whileHeld is not null)
			await whileHeld();
		Release();
		await Task.Delay(600);
	}

	/// <summary>The adapter's own diagnostic line (<c>mauiProbe</c>) of the element's native item, or null when the
	/// element is not rendered on the current page.</summary>
	public static string? Probe(VisualElement element) =>
		HostOf(element) is { } host ? QtHostRuntime.Eval($"{DiagQml.ItemJs(host)}.mauiProbe") : null;

	/// <summary>Evaluates <paramref name="body"/> as a function of <c>item</c>, the element's native QML item (an experiment
	/// on a live adapter); null when the element is not rendered.</summary>
	public static string? EvalOn(VisualElement element, string body) =>
		HostOf(element) is { } host ? QtHostRuntime.Eval($"(function(item){{{body}}})({DiagQml.ItemJs(host)})") : null;

	/// <summary>Reads a property of the element's native item through the shim, attached ones included
	/// (<c>Accessible.name</c>, where SemanticProperties.Description lands); null when the element is not rendered.</summary>
	public static string? NativeProperty(VisualElement element, string name) =>
		HostOf(element) is { } host ? QtHostRuntime.GetProperty(host.NativeHandle, name) : null;

	/// <summary>Sets a string property of the element's native item through the shim's property path (the one handlers
	/// use), JSON-encoded as they send it; the shim's return code (0 = set).</summary>
	public static int SetAdapterString(VisualElement element, string name, string value) =>
		HostOf(element) is { } host
			? QtHostRuntime.SetProperty(host.NativeHandle, name, System.Text.Json.JsonSerializer.Serialize(value))
			: -3;

	/// <summary>Evaluates a QML/JS expression in the host's root context (read-back for checks).</summary>
	public static string Eval(string js) => QtHostRuntime.Eval(js);

	/// <summary>
	/// Logs <c>SF-SHOT name</c>. Under <c>tools/sf shots</c> (MAUI_SAILFISH_SHOT_SYNC=1) it waits until the screenshot is
	/// acknowledged (/tmp/sf-shot-ack/name, at most 20 s); otherwise it holds MAUI_SAILFISH_SHOT_HOLD_MS (at least 200 ms).
	/// </summary>
	public static async Task ShotAsync(string name)
	{
		var hold = SailfishEnv.Int("MAUI_SAILFISH_SHOT_HOLD_MS") ?? 0;
		Console.Error.WriteLine($"[Sailfish] SF-SHOT {name}");
		if (!SailfishEnv.Flag("MAUI_SAILFISH_SHOT_SYNC"))
		{
			await Task.Delay(Math.Max(200, hold));
			return;
		}
		var ack = Path.Combine("/tmp/sf-shot-ack", name);
		for (var waited = 0; !File.Exists(ack); waited += 100)
		{
			if (waited >= 20000)
			{
				Console.Error.WriteLine($"[Sailfish] SF-SHOT {name}: no acknowledgement after 20 s, moving on");
				break;
			}
			await Task.Delay(100);
		}
		await Task.Delay(hold);
	}
}
