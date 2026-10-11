using System.Text.Json;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.SailfishOS.Handlers;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Camera;

/// <summary><see cref="SailfishCameraView"/> on QtMultimedia's Camera and VideoOutput (qml/SailfishCamera/Camera.qml).</summary>
public sealed class SailfishCameraViewHandler : SailfishSnapshotHandler
{
	public const string AdapterName = "sailfish-camera";

	private static readonly string[] Keys =
	{
		nameof(SailfishCameraView.CameraPosition), nameof(SailfishCameraView.FlashMode), nameof(SailfishCameraView.IsTorchOn),
		nameof(SailfishCameraView.ZoomFactor), nameof(SailfishCameraView.IsPreviewing),
	};

	public SailfishCameraViewHandler()
		: base(Keys, new CommandMapper<IView, SailfishCameraViewHandler>(SailfishViewMapper.CommandMapper)
		{
			[nameof(SailfishCameraView.CaptureImageAsync)] = (h, _, arg) =>
			{
				if (arg is not string path || !h.SendCommand("capture", new() { ["path"] = path }))
					(h.ConnectedView as SailfishCameraView)?.OnCaptureFailed("The camera is not ready");
			},
		})
	{
	}

	protected override string? AdapterUri => AdapterName;

	protected override Dictionary<string, object?>? Snapshot(IView view) =>
		view is SailfishCameraView camera
			? new()
			{
				["position"] = camera.CameraPosition == SailfishCameraPosition.Front ? "front" : "back",
				["flash"] = camera.FlashMode.ToString().ToLowerInvariant(),
				["torch"] = camera.IsTorchOn,
				["zoom"] = camera.ZoomFactor,
				["previewing"] = camera.IsPreviewing,
			}
			: null;

	protected override void OnAdapterEvent(string name, JsonElement payload)
	{
		if (ConnectedView is not SailfishCameraView camera)
			return;
		switch (name)
		{
			case "camera-state":
				camera.OnState(payload.GetProperty("available").GetBoolean(), payload.GetProperty("maxZoom").GetDouble());
				break;
			case "camera-captured":
				camera.OnCaptured(payload.GetProperty("path").GetString() ?? "");
				break;
			case "camera-failed":
				camera.OnCaptureFailed(payload.GetProperty("reason").GetString() ?? "capture failed");
				break;
		}
	}
}

public static class SailfishCameraExtensions
{
	/// <summary>Registers <see cref="SailfishCameraView"/> and its adapter.</summary>
	public static MauiAppBuilder UseSailfishCamera(this MauiAppBuilder builder)
	{
		QtHostAdapters.Register(SailfishCameraViewHandler.AdapterName, "SailfishCamera/Camera.qml");
		builder.ConfigureMauiHandlers(h => h.AddHandler<SailfishCameraView, SailfishCameraViewHandler>());
		return builder;
	}
}
