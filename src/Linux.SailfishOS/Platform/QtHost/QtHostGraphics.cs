using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// Records GraphicsView draw calls via <see cref="QtHostCanvasRecorder"/> for replay by the
/// GraphicsView.qml Context2D adapter. There is no platform handler for Invalidate(), so the
/// drawable is re-recorded on every reconcile poll and the bridge diff skips unchanged output.
/// </summary>
internal static class QtHostGraphics
{
	/// <summary>Adapter URI (qml/adapters.json).</summary>
	public const string AdapterUri = "graphics-view";

	private static readonly List<object?> Empty = new();

	public static Dictionary<string, object?> Props(GraphicsView view)
	{
		var width = view.Width > 0 ? (float)view.Width : 0f;
		var height = view.Height > 0 ? (float)view.Height : 0f;
		IReadOnlyList<object?> commands = Empty;
		if (view.Drawable is { } drawable && width > 0 && height > 0)
		{
			var recorder = new QtHostCanvasRecorder();
			try
			{
				drawable.Draw(recorder, new RectF(0, 0, width, height));
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine($"[Sailfish] Qt canvas: IDrawable.Draw ({drawable.GetType().Name}) failed: {ex.Message}");
			}
			if (recorder.Truncated)
				Console.Error.WriteLine($"[Sailfish] Qt canvas: IDrawable.Draw ({drawable.GetType().Name}) exceeded the command cap; the tail was dropped (the adapter reports the executed count)");
			commands = recorder.Commands;
		}

		return new Dictionary<string, object?>
		{
			["mauiCommands"] = commands,
			// Commands stay in dp; the adapter scales the context once by this.
			["mauiScale"] = SailfishDisplay.Density,
			["mauiBackground"] = view.BackgroundColor ?? Colors.Transparent,
			// Empty uses the Silica theme family, so canvas text matches Labels.
			["mauiFontFamily"] = string.Empty,
		};
	}
}
