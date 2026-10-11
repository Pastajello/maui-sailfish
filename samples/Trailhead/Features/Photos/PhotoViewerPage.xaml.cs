namespace Trailhead.Features.Photos;

public partial class PhotoViewerPage : ContentPage
{
	private readonly PhotoViewerViewModel _viewModel;

	public PhotoViewerPage(PhotoViewerViewModel viewModel)
	{
		_viewModel = viewModel;
		BindingContext = viewModel;
		InitializeComponent();
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await _viewModel.LoadAsync();
	}

	/// <summary>The zoom of the photo on screen (tour checks).</summary>
	public double CurrentImageScale =>
		Carousel.VisibleViews.OfType<ZoomableImage>().FirstOrDefault(v => v.BindingContext == _viewModel.Current)?.ImageScale ?? double.NaN;

	// A photo left zoomed in starts at 1× when the user comes back to it.
	private void OnPositionChanged(object? sender, PositionChangedEventArgs e)
	{
		foreach (var view in Carousel.VisibleViews)
			if (view is ZoomableImage image && image.BindingContext != _viewModel.Current)
				image.Reset();
	}

	private async void OnShare(object? sender, EventArgs e)
	{
		if (_viewModel.Current is { } photo)
			await Share.Default.RequestAsync(new ShareFileRequest(photo.Caption, new ShareFile(photo.FullPath, "image/jpeg")));
	}
}
