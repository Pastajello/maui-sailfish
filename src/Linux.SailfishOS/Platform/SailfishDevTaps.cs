using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Developer aid for driving an app on the phone without touching it, through the same QPA path as real input. Off
/// unless MAUI_SAILFISH_TAPS is set to ';'-separated entries, each at ms after launch, in window pixels (the
/// coordinates of a tools/sf screenshot):
/// <c>ms:x,y</c> taps, <c>ms:x,y&gt;x2,y2</c> drags (a swipe), <c>ms:"text"</c> types into the focused input.
/// </summary>
internal static class SailfishDevTaps
{
	internal enum Kind { Tap, Drag, Text }

	internal readonly record struct DevInput(int AtMs, Kind Kind, double X = 0, double Y = 0, double X2 = 0, double Y2 = 0, string Text = "");

	private const int DragSteps = 12;
	private const int DragStepMs = 25;

	/// <summary>Parses the entries; malformed ones are skipped.</summary>
	internal static List<DevInput> Parse(string? spec)
	{
		var inputs = new List<DevInput>();
		if (string.IsNullOrWhiteSpace(spec))
			return inputs;
		foreach (var entry in spec.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			var timeAndAction = entry.Split(':', 2);
			if (timeAndAction.Length != 2 ||
			    !int.TryParse(timeAndAction[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var at))
				continue;
			var action = timeAndAction[1].Trim();
			if (action.Length >= 2 && action[0] == '"' && action[^1] == '"')
			{
				inputs.Add(new DevInput(at, Kind.Text, Text: action[1..^1]));
				continue;
			}
			var ends = action.Split('>', 2);
			if (!TryPoint(ends[0], out var x, out var y))
				continue;
			if (ends.Length == 1)
				inputs.Add(new DevInput(at, Kind.Tap, x, y));
			else if (TryPoint(ends[1], out var x2, out var y2))
				inputs.Add(new DevInput(at, Kind.Drag, x, y, x2, y2));
		}
		return inputs;
	}

	private static bool TryPoint(string text, out double x, out double y)
	{
		var point = text.Split(',', 2);
		x = y = 0;
		return point.Length == 2 &&
		       double.TryParse(point[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) &&
		       double.TryParse(point[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y);
	}

	/// <summary>Schedules the inputs on the Qt loop's dispatcher (call on the loop thread).</summary>
	public static void Schedule(IDispatcher loop)
	{
		var inputs = Parse(SailfishEnv.Get("MAUI_SAILFISH_TAPS"));
		for (var i = 0; i < inputs.Count; i++)
		{
			var input = inputs[i];
			var n = i + 1;
			loop.DispatchDelayed(TimeSpan.FromMilliseconds(input.AtMs), () => Run(loop, input, n));
		}
	}

	private static void Run(IDispatcher loop, DevInput input, int n)
	{
		var at = $"{input.X.ToString(CultureInfo.InvariantCulture)},{input.Y.ToString(CultureInfo.InvariantCulture)}";
		switch (input.Kind)
		{
			case Kind.Tap:
				Console.Error.WriteLine($"[Sailfish] dev tap #{n} at {at}");
				QtHostRuntime.InjectPointer(0, input.X, input.Y);
				loop.DispatchDelayed(TimeSpan.FromMilliseconds(60), () => QtHostRuntime.InjectPointer(1, input.X, input.Y));
				break;
			case Kind.Drag:
				Console.Error.WriteLine($"[Sailfish] dev tap #{n} drag {at} > " +
					$"{input.X2.ToString(CultureInfo.InvariantCulture)},{input.Y2.ToString(CultureInfo.InvariantCulture)}");
				QtHostRuntime.InjectPointer(0, input.X, input.Y);
				for (var step = 1; step <= DragSteps; step++)
				{
					var t = step / (double)DragSteps;
					var x = input.X + (input.X2 - input.X) * t;
					var y = input.Y + (input.Y2 - input.Y) * t;
					loop.DispatchDelayed(TimeSpan.FromMilliseconds(step * DragStepMs), () => QtHostRuntime.InjectPointer(2, x, y));
				}
				loop.DispatchDelayed(TimeSpan.FromMilliseconds((DragSteps + 1) * DragStepMs),
					() => QtHostRuntime.InjectPointer(1, input.X2, input.Y2));
				break;
			case Kind.Text:
				Console.Error.WriteLine($"[Sailfish] dev tap #{n} types {input.Text.Length} chars");
				foreach (var ch in input.Text)
				{
					// Key_unknown with text: QQuickTextInput inserts the text of a key it does not handle itself.
					var text = ch.ToString();
					QtHostRuntime.InjectKey(0, 0x01ffffff, 0, text);
					QtHostRuntime.InjectKey(1, 0x01ffffff, 0, text);
				}
				break;
		}
	}
}
