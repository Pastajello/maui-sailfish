namespace Microsoft.Maui.SailfishOS.Camera;

public enum SailfishCameraPosition
{
	Back,
	Front,
}

public enum SailfishFlashMode
{
	Off,
	On,
	Auto,
}

/// <summary>
/// A live camera preview that takes photos, on QtMultimedia (the camera Sailfish's own apps use). Shaped like
/// CommunityToolkit.Maui's <c>CameraView</c>, which cannot run here: it drives an internal platform manager that a
/// third-party platform has no way to provide. The app needs the <c>Camera</c> Sailjail permission.
/// </summary>
public class SailfishCameraView : View
{
	public static readonly BindableProperty CameraPositionProperty = BindableProperty.Create(nameof(CameraPosition),
		typeof(SailfishCameraPosition), typeof(SailfishCameraView), SailfishCameraPosition.Back);

	public static readonly BindableProperty FlashModeProperty = BindableProperty.Create(nameof(FlashMode),
		typeof(SailfishFlashMode), typeof(SailfishCameraView), SailfishFlashMode.Auto);

	public static readonly BindableProperty IsTorchOnProperty = BindableProperty.Create(nameof(IsTorchOn), typeof(bool),
		typeof(SailfishCameraView), false);

	public static readonly BindableProperty ZoomFactorProperty = BindableProperty.Create(nameof(ZoomFactor), typeof(double),
		typeof(SailfishCameraView), 1.0, coerceValue: (b, v) => Math.Clamp((double)v, 1, Math.Max(1, ((SailfishCameraView)b).MaximumZoomFactor)));

	public static readonly BindableProperty IsPreviewingProperty = BindableProperty.Create(nameof(IsPreviewing), typeof(bool),
		typeof(SailfishCameraView), true);

	private static readonly BindablePropertyKey IsAvailablePropertyKey = BindableProperty.CreateReadOnly(nameof(IsAvailable),
		typeof(bool), typeof(SailfishCameraView), false);

	public static readonly BindableProperty IsAvailableProperty = IsAvailablePropertyKey.BindableProperty;

	private static readonly BindablePropertyKey MaximumZoomFactorPropertyKey = BindableProperty.CreateReadOnly(nameof(MaximumZoomFactor),
		typeof(double), typeof(SailfishCameraView), 1.0);

	public static readonly BindableProperty MaximumZoomFactorProperty = MaximumZoomFactorPropertyKey.BindableProperty;

	private TaskCompletionSource<string>? _capture;

	public SailfishCameraPosition CameraPosition
	{
		get => (SailfishCameraPosition)GetValue(CameraPositionProperty);
		set => SetValue(CameraPositionProperty, value);
	}

	public SailfishFlashMode FlashMode
	{
		get => (SailfishFlashMode)GetValue(FlashModeProperty);
		set => SetValue(FlashModeProperty, value);
	}

	public bool IsTorchOn
	{
		get => (bool)GetValue(IsTorchOnProperty);
		set => SetValue(IsTorchOnProperty, value);
	}

	/// <summary>Digital zoom, 1 to <see cref="MaximumZoomFactor"/>.</summary>
	public double ZoomFactor
	{
		get => (double)GetValue(ZoomFactorProperty);
		set => SetValue(ZoomFactorProperty, value);
	}

	/// <summary>True while the preview runs (the camera is held only then); false releases the camera.</summary>
	public bool IsPreviewing
	{
		get => (bool)GetValue(IsPreviewingProperty);
		set => SetValue(IsPreviewingProperty, value);
	}

	/// <summary>True once the camera reports it is ready.</summary>
	public bool IsAvailable => (bool)GetValue(IsAvailableProperty);

	public double MaximumZoomFactor => (double)GetValue(MaximumZoomFactorProperty);

	/// <summary>Raised with the saved photo's path.</summary>
	public event EventHandler<string>? MediaCaptured;

	/// <summary>Raised with the camera's reason when a capture fails.</summary>
	public event EventHandler<string>? MediaCaptureFailed;

	/// <summary>Takes a photo into <paramref name="path"/> (a JPEG; null: the app's cache) and returns where it went.</summary>
	public Task<string> CaptureImageAsync(string? path = null, CancellationToken token = default)
	{
		if (Handler is not SailfishCameraViewHandler handler)
			throw new InvalidOperationException(Handler is null
				? "The camera view is not on screen"
				: "The camera view has no camera handler: call UseSailfishCamera() in CreateMauiApp");
		_capture?.TrySetCanceled();
		var capture = _capture = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
		token.Register(() => capture.TrySetCanceled(token));
		path ??= Path.Combine(Microsoft.Maui.Storage.FileSystem.CacheDirectory, $"capture-{DateTime.Now:yyyyMMdd-HHmmss-fff}.jpg");
		// The camera writes only into a directory that exists (CameraBin: "Error while writing to file").
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
		handler.Invoke(nameof(CaptureImageAsync), path);
		// A camera that never answers (a driver hang) must not leave the caller waiting for good.
		return capture.Task.WaitAsync(CaptureTimeout, token);
	}

	/// <summary>How long a capture may take before it fails with a <see cref="TimeoutException"/>.</summary>
	public static TimeSpan CaptureTimeout { get; set; } = TimeSpan.FromSeconds(20);

	/// <summary>Takes a photo and opens it as a stream (the toolkit's <c>CaptureImage</c>).</summary>
	public async Task<Stream> CaptureImage(CancellationToken token) => File.OpenRead(await CaptureImageAsync(null, token));

	internal void OnCaptured(string path)
	{
		_capture?.TrySetResult(path);
		_capture = null;
		MediaCaptured?.Invoke(this, path);
	}

	internal void OnCaptureFailed(string reason)
	{
		_capture?.TrySetException(new InvalidOperationException(reason));
		_capture = null;
		MediaCaptureFailed?.Invoke(this, reason);
	}

	internal void OnState(bool available, double maximumZoom)
	{
		SetValue(IsAvailablePropertyKey, available);
		SetValue(MaximumZoomFactorPropertyKey, Math.Max(1, maximumZoom));
	}
}
