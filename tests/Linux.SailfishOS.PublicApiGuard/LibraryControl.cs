using System.Text.Json;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.SailfishOS.Handlers;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Linux.SailfishOS.PublicApiGuard;

/// <summary>A control from a third-party library (docs/custom-controls.md).</summary>
public class RatingView : View
{
	public static readonly BindableProperty ValueProperty =
		BindableProperty.Create(nameof(Value), typeof(int), typeof(RatingView), 0);

	public static readonly BindableProperty MaximumProperty =
		BindableProperty.Create(nameof(Maximum), typeof(int), typeof(RatingView), 5);

	public int Value
	{
		get => (int)GetValue(ValueProperty);
		set => SetValue(ValueProperty, value);
	}

	public int Maximum
	{
		get => (int)GetValue(MaximumProperty);
		set => SetValue(MaximumProperty, value);
	}
}

/// <summary>The library's Sailfish handler: its adapter, the snapshot it pushes, the events it hears.</summary>
public class RatingViewHandler : SailfishSnapshotHandler
{
	public RatingViewHandler()
		: base(new[] { nameof(RatingView.Value), nameof(RatingView.Maximum) },
			   (view, widthConstraint, heightConstraint) => new Size(Math.Min(widthConstraint, 240), 48))
	{
	}

	protected override string? AdapterUri => "rating-view";

	protected override Dictionary<string, object?>? Snapshot(IView view) =>
		view is RatingView rating
			? new() { ["value"] = rating.Value, ["maximum"] = rating.Maximum }
			: null;

	protected override void OnAdapterEvent(string name, JsonElement payload)
	{
		if (name == "rating-changed" && VirtualView is RatingView rating)
			rating.Value = payload.GetProperty("value").GetInt32();
	}
}

public static class RatingControlsExtensions
{
	/// <summary>The library's builder extension.</summary>
	public static MauiAppBuilder UseRatingControls(this MauiAppBuilder builder)
	{
		QtHostAdapters.Register("rating-view", "RatingControls/RatingView.qml");
		builder.ConfigureMauiHandlers(handlers => handlers.AddHandler<RatingView, RatingViewHandler>());
		return builder;
	}
}
