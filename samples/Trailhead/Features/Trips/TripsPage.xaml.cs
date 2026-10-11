using Microsoft.Maui.SailfishOS.Platform;
using Microsoft.Maui.Storage;
using Trailhead.Features.Shared;

namespace Trailhead.Features.Trips;

public partial class TripsPage : ContentPage
{
	private readonly TripsViewModel _viewModel;

	public TripsPage(TripsViewModel viewModel)
	{
		_viewModel = viewModel;
		BindingContext = viewModel;
		InitializeComponent();
		// Page.IsBusy is obsolete in MAUI 11, but on Sailfish it is the pulsing pulley bar of a page with a pull-down
		// menu (Silica's PullDownMenu.busy), the platform's own "working" signal.
		viewModel.PropertyChanged += (_, e) =>
		{
			if (e.PropertyName == nameof(TripsViewModel.IsBusy))
#pragma warning disable CS0618
				IsBusy = viewModel.IsBusy;
#pragma warning restore CS0618
		};
	}

	private bool _offeredDraft;

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		_viewModel.UpdateCover();
		if (!_offeredDraft)
		{
			_offeredDraft = true;
			await _viewModel.OfferDraftAsync();
			await ShowHintOnceAsync();
		}
	}

	private const string HintKey = "hint-pulley";

	/// <summary>True while the first-launch hint runs (tour checks).</summary>
	public bool IsHinting => Hint.IsVisible;

	/// <summary>The pull-down hint, on the first launch only (TRAILHEAD_HINT=1 shows it again).</summary>
	public async Task ShowHintOnceAsync()
	{
		if (Preferences.Default.Get(HintKey, false) && Environment.GetEnvironmentVariable("TRAILHEAD_HINT") != "1")
			return;
		Preferences.Default.Set(HintKey, true);
		Hint.Opacity = 0;
		Hint.IsVisible = true;
		await Hint.FadeToAsync(1, 300);
		for (var i = 0; i < 3; i++)
		{
			HintDot.TranslationY = 40;
			HintDot.Opacity = 0;
			await Task.WhenAll(HintDot.FadeToAsync(0.9, 250), HintDot.TranslateToAsync(0, Height * 0.35, 1100, Easing.CubicInOut));
			await HintDot.FadeToAsync(0, 250);
		}
		await Hint.FadeToAsync(0, 400);
		Hint.IsVisible = false;
	}

	private async void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (e.CurrentSelection.FirstOrDefault() is not TripItem item)
			return;
		List.SelectedItem = null;
		await _viewModel.OpenAsync(item);
	}

	private void OnScrollToTop(object? sender, EventArgs e)
	{
		if (_viewModel.Groups.FirstOrDefault() is { Count: > 0 } first)
			List.ScrollTo(first[0], first, ScrollToPosition.Start, animate: true);
	}

	private static TripItem? ItemOf(object? sender, out View? row)
	{
		row = (sender as MenuFlyoutItem)?.CommandParameter as View;
		return row?.BindingContext as TripItem;
	}

	private async void OnShare(object? sender, EventArgs e)
	{
		if (ItemOf(sender, out _) is { } item)
			await _viewModel.ShareAsync(item);
	}

	private async void OnEdit(object? sender, EventArgs e)
	{
		if (ItemOf(sender, out _) is { } item)
			await _viewModel.EditAsync(item);
	}

	// Silica's way to confirm: a RemorseItem counts down over the row; a tap on it undoes the delete.
	private async void OnDelete(object? sender, EventArgs e)
	{
		if (ItemOf(sender, out var row) is { } item && row is not null)
			await SailfishRemorse.ExecuteAsync(row, L.T("Deleting"), () => _ = _viewModel.DeleteAsync(item));
	}
}
