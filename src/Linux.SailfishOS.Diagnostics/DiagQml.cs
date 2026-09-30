using System.Globalization;
using Microsoft.Maui.Controls;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>Reads QML state back through eval and drives real input, shared by every diagnostics leg.</summary>
internal static class DiagQml
{
	/// <summary>Invariant number from an eval result; <paramref name="fallback"/> when it isn't one.</summary>
	public static double Num(string? text, double fallback = double.NaN) =>
		double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;

	public static double EvalNum(string js, double fallback = double.NaN) => Num(QtHostRuntime.Eval(js), fallback);

	/// <summary>Parses an "x,y" eval result.</summary>
	public static bool TryPoint(string? text, out double x, out double y)
	{
		x = y = double.NaN;
		var parts = (text ?? string.Empty).Split(',');
		return parts.Length == 2 &&
			double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) &&
			double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y);
	}

	/// <summary>The live host rendering <paramref name="element"/> on the current page.</summary>
	public static NativeElementHost? HostOf(QtHostPageRenderer renderer, Element? element) =>
		renderer.CurrentHosts.FirstOrDefault(h => ReferenceEquals(h.Element, element));

	/// <summary>JS reference to a host's QML item on the current model page ("null" when unhosted).</summary>
	public static string ItemJs(NativeElementHost? host) =>
		host is null ? "null" : $"pageStack.currentPage.__hosts['{host.Id}'].item";

	/// <summary>A real press + release at scene coordinates.</summary>
	public static void Tap(double x, double y)
	{
		QtHostRuntime.InjectPointer(0, x, y);
		QtHostRuntime.InjectPointer(1, x, y);
	}
}
