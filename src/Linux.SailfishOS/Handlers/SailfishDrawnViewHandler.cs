using System.Text.Json;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Handlers;

/// <summary>
/// Fallback for a library view that draws itself (<see cref="IDrawable"/>) and lays out MAUI children, when its own
/// handler cannot run here: Syncfusion Toolkit's SfView (text input outline and hint, shimmer, segmented control)
/// registers a plain-net SfViewHandler that throws. The drawing is recorded like a GraphicsView's and painted under
/// the children; it is re-recorded on every reconcile, since the library's Invalidate goes to its own handler type.
/// </summary>
internal sealed class SailfishDrawnViewHandler : NullViewHandler, ISailfishAdapterHandler
{
	private Size _arranged;

	public string? AdapterUri => QtHostGraphics.DrawnAdapterUri;

	public Dictionary<string, object?>? AdapterState() =>
		VirtualView is IDrawable drawable && VirtualView is VisualElement view ? QtHostGraphics.DrawableProps(drawable, view) : null;

	public bool WalksChildren => true;

	public void OnAdapterEvent(string name, JsonElement payload)
	{
	}

	public override void PlatformArrange(Rect frame)
	{
		base.PlatformArrange(frame);
		// The drawing is recorded for the arranged size: a new size records it again.
		if (frame.Width > 0 && frame.Height > 0 && frame.Size != _arranged)
		{
			_arranged = frame.Size;
			SailfishHandlerCore.SessionOf(this)?.RequestPoll();
		}
	}
}
