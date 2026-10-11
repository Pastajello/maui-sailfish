using Microsoft.Maui.SailfishOS.Camera;
using Trailhead.Features.Recording;
using Trailhead.Features.Shared;

namespace Trailhead.Features.Camera;

/// <summary>
/// The camera (plan T13): a live preview (<see cref="SailfishCameraView"/>, QtMultimedia), the shutter, front/back,
/// flash and pinch zoom. A photo taken while recording joins the recording at the last fix (it shows on the map and in
/// the trip's grid afterwards); otherwise it goes to Pictures. Opened from the recording's pulley.
/// </summary>
public sealed class CameraPage : ContentPage
{
	public const string Route = "camera";

	private readonly RecordingService _recording;
	private readonly SailfishCameraView _camera = new();
	private readonly Label _status = new() { FontSize = 13, HorizontalTextAlignment = TextAlignment.Center };
	private readonly Image _last = new() { WidthRequest = 72, HeightRequest = 72, Aspect = Aspect.AspectFill };

	public CameraPage(RecordingService recording)
	{
		_recording = recording;
		Title = L.T("Camera");
		SemanticProperties.SetDescription(_camera, L.T("Camera preview; pinch to zoom"));
		SemanticProperties.SetDescription(_last, L.T("Last photo"));
		_status.SetDynamicResource(Label.TextColorProperty, ThemeResources.Secondary);

		var flip = new ToolbarItem { Text = L.T("Switch camera") };
		flip.Clicked += (_, _) => _camera.CameraPosition =
			_camera.CameraPosition == SailfishCameraPosition.Back ? SailfishCameraPosition.Front : SailfishCameraPosition.Back;
		var flash = new ToolbarItem { Text = L.T("Flash: auto") };
		flash.Clicked += (_, _) =>
		{
			_camera.FlashMode = _camera.FlashMode switch
			{
				SailfishFlashMode.Auto => SailfishFlashMode.On,
				SailfishFlashMode.On => SailfishFlashMode.Off,
				_ => SailfishFlashMode.Auto,
			};
			flash.Text = L.T($"Flash: {_camera.FlashMode.ToString().ToLowerInvariant()}");
		};
		ToolbarItems.Add(flip);
		ToolbarItems.Add(flash);

		// Pinch zooms the camera (digital zoom), not the view.
		var pinch = new PinchGestureRecognizer();
		pinch.PinchUpdated += (_, e) =>
		{
			if (e.Status == GestureStatus.Running)
				_camera.ZoomFactor *= e.Scale;
		};
		_camera.GestureRecognizers.Add(pinch);

		var shutter = new Button { Text = L.T("Take photo"), HorizontalOptions = LayoutOptions.Center };
		shutter.Clicked += async (_, _) => await TakeAsync();

		var bar = new Grid
		{
			Padding = new Thickness(16, 8, 16, 16),
			ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) },
		};
		bar.Add(_last);
		bar.Add(shutter, 1, 0);
		var root = new Grid { RowDefinitions = { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) } };
		root.Add(_camera);
		root.Add(_status, 0, 1);
		root.Add(bar, 0, 2);
		Content = root;
	}

	public SailfishCameraView Camera => _camera;

	/// <summary>The status line (tour checks).</summary>
	public string StatusText => _status.Text ?? "";

	/// <summary>The last photo's path (tour checks).</summary>
	public string? LastPhoto { get; private set; }

	protected override void OnAppearing()
	{
		base.OnAppearing();
		_camera.IsPreviewing = true;
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();
		_camera.IsPreviewing = false;   // the camera is released while the page is not shown
	}

	public async Task<string?> TakeAsync()
	{
		try
		{
			_status.Text = L.T("Taking the photo…");
			var path = _recording.IsActive
				? _recording.NewPhotoPath()
				: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), $"Trailhead-{DateTime.Now:yyyyMMdd-HHmmss}.jpg");
			path = await _camera.CaptureImageAsync(path);
			LastPhoto = path;
			_last.Source = ImageSource.FromFile(path);
			if (_recording.IsActive)
				_recording.AddPhoto(path);
			HapticFeedback.Default.Perform(HapticFeedbackType.Click);
			_status.Text = L.T(_recording.IsActive ? "Added to the recording" : "Saved to Pictures");
			return path;
		}
		catch (Exception ex) when (ex is InvalidOperationException or TaskCanceledException or TimeoutException or IOException)
		{
			_status.Text = ex.Message;
			return null;
		}
	}
}
