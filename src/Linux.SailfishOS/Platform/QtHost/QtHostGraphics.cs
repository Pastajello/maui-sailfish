using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// Records GraphicsView draw calls via <see cref="QtHostCanvasRecorder"/> for replay by the
/// GraphicsView.qml Context2D adapter. SailfishGraphicsHandler records on Invalidate(), a mapped property and a new
/// arranged size, and the reconcile walk reuses that recording.
/// </summary>
internal static class QtHostGraphics
{
	/// <summary>Adapter URI (qml/adapters.json).</summary>
	public const string AdapterUri = Handlers.SailfishKeys.Adapter.GraphicsView;

	/// <summary>Self-drawing library containers (SailfishDrawnViewHandler): the same replay under their children.</summary>
	public const string DrawnAdapterUri = Handlers.SailfishKeys.Adapter.DrawnView;

	private static readonly List<object?> Empty = new();
	private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, byte> FailedDrawables = new();

	public static Dictionary<string, object?> Props(GraphicsView view) =>
		DrawableProps(view.Drawable, view, view.BackgroundColor ?? Colors.Transparent);

	/// <summary>A self-drawing view: its own Draw, over its background.</summary>
	public static Dictionary<string, object?> DrawableProps(IDrawable drawable, VisualElement view) =>
		DrawableProps(drawable, view, QtHostPaint.Background(view) ?? Colors.Transparent);

	private static Dictionary<string, object?> DrawableProps(IDrawable? drawable, VisualElement view, Color background)
	{
		var width = view.Width > 0 ? (float)view.Width : 0f;
		var height = view.Height > 0 ? (float)view.Height : 0f;
		IReadOnlyList<object?> commands = Empty;
		if (drawable is not null && width > 0 && height > 0)
		{
			var recorder = new QtHostCanvasRecorder();
			try
			{
				drawable.Draw(recorder, new RectF(0, 0, width, height));
			}
			catch (Exception ex)
			{
				// One line per drawable type, not one per recording.
				if (FailedDrawables.TryAdd(drawable.GetType(), 0))
					QtHostDiag.Warn(QtHostDiagChannel.QmlObject,
						$"IDrawable.Draw ({drawable.GetType().FullName}) failed: {ex.GetType().Name}: {ex.Message} — drawn as far as it got");
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
			["mauiBackground"] = background,
			// Empty uses the Silica theme family, so canvas text matches Labels.
			["mauiFontFamily"] = string.Empty,
		};
	}
}
