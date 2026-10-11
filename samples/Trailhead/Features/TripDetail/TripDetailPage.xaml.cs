using Microsoft.Maui.SailfishOS.Platform;
using Trailhead.Features.Shared;

namespace Trailhead.Features.TripDetail;

public partial class TripDetailPage : ContentPage
{
	private readonly TripDetailViewModel _viewModel;

	public TripDetailPage(TripDetailViewModel viewModel)
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

	private async void OnPhotoSelected(object? sender, SelectionChangedEventArgs e)
	{
		if (e.CurrentSelection.FirstOrDefault() is not PhotoItem photo)
			return;
		Grid.SelectedItem = null;
		await Shell.Current.GoToAsync($"{AppShell.PhotoRoute}?id={_viewModel.Id}&index={photo.Index}");
	}

	private void OnProfileScrubbed(object? sender, Data.TrackPoint? point) =>
		_viewModel.ScrubText = point is { } p ? $"{p.Time:HH:mm} · {Format.Height(p.Ele)}" : L.T("Touch the profile to read it");

	/// <summary>The elevation profile (tour checks).</summary>
	public Charts.ElevationChart ElevationProfile => Profile;

	private async void OnEdit(object? sender, EventArgs e) =>
		await Shell.Current.GoToAsync($"{AppShell.TripEditRoute}?id={_viewModel.Id}");

	private async void OnMap(object? sender, EventArgs e) =>
		await Shell.Current.GoToAsync($"{AppShell.TripMapRoute}?id={_viewModel.Id}");

	private async void OnShare(object? sender, EventArgs e)
	{
		if (_viewModel.Trip is { } trip)
			await TripSharing.ShareGpxAsync(trip);
	}

	/// <summary>Silica's multi-image picker (MediaPicker on Sailfish), at most ten photos a time; the grid and its
	/// header show what came in.</summary>
	private async void OnImportPhotos(object? sender, EventArgs e)
	{
		var picked = await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions { Title = L.T("Import photos"), SelectionLimit = 10 });
		await _viewModel.ImportPhotosAsync(picked.Select(f => f.FullPath).ToList());
	}

	// A page-wide RemorsePopup: the trip goes after the countdown unless the user taps it.
	private async void OnDelete(object? sender, EventArgs e)
	{
		if (await SailfishRemorse.ExecuteAsync(L.T("Deleting trip"), () => { }))
		{
			await _viewModel.DeleteAsync();
			await Shell.Current.GoToAsync("..");
		}
	}
}
