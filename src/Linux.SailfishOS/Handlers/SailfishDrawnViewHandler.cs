using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Handlers;

/// <summary>
/// Fallback for a library view that draws itself (<see cref="IDrawable"/>) and lays out MAUI children, when its own
/// handler cannot run here: Syncfusion Toolkit's SfView (text input outline and hint, shimmer, segmented control)
/// registers a plain-net SfViewHandler that throws. The drawing is recorded like a GraphicsView's and painted under
/// the children. The library's Invalidate goes to its own handler type, so the drawing is recorded again whenever a
/// property changes (as GraphicsView's every non-visual-state property), when the arranged size changes, and on every
/// reconcile.
/// </summary>
internal sealed class SailfishDrawnViewHandler : SailfishSnapshotHandler<IView>
{
	private static readonly string[] Keys = [];

	public static readonly PropertyMapper<IView, SailfishDrawnViewHandler> Mapper = SnapshotMapper<SailfishDrawnViewHandler>(Keys);

	public SailfishDrawnViewHandler() : base(Mapper, null)
	{
	}

	protected override string? AdapterUri => SailfishKeys.Adapter.DrawnView;

	public override bool OwnsProperty(string propertyName) => !QtHostVisualState.IsStateProperty(propertyName);

	/// <summary>The drawing is recorded for the arranged size.</summary>
	protected override bool SnapshotDependsOnSize => true;

	protected override Dictionary<string, object?>? Snapshot(IView view) =>
		view is IDrawable drawable && view is VisualElement visual ? QtHostGraphics.DrawableProps(drawable, visual) : null;
}
