using System.Text.Json;
using Microsoft.Maui.SailfishOS.Handlers;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Trailhead.Controls;

/// <summary>The compass on its own QML adapter (qml/Trailhead/Compass.qml): every key re-sends the whole snapshot.</summary>
public sealed class CompassViewHandler : SailfishSnapshotHandler
{
	public const string AdapterName = "trailhead-compass";

	public CompassViewHandler()
		: base(new[] { nameof(CompassView.Heading), nameof(CompassView.TargetBearing), nameof(CompassView.IsCalibrated) },
			(_, w, h) =>
			{
				var side = Math.Min(double.IsInfinity(w) ? 120 : w, double.IsInfinity(h) ? 120 : h);
				return new Size(side, side);
			})
	{
	}

	protected override string? AdapterUri => AdapterName;

	protected override Dictionary<string, object?>? Snapshot(IView view) =>
		view is CompassView compass
			? new()
			{
				["heading"] = compass.Heading,
				["targetBearing"] = compass.TargetBearing ?? -1.0,
				["calibrated"] = compass.IsCalibrated,
			}
			: null;

	protected override void OnAdapterEvent(string name, JsonElement payload)
	{
		if (name == "compass-tapped" && ConnectedView is CompassView compass)
			compass.SendTapped();
	}

	/// <summary>Registers the adapter and the handler; MauiProgram calls it, as a library's UseXxx() would.</summary>
	public static void Register(IMauiHandlersCollection handlers)
	{
		QtHostAdapters.Register(AdapterName, "Trailhead/Compass.qml");
		handlers.AddHandler<CompassView, CompassViewHandler>();
	}
}
