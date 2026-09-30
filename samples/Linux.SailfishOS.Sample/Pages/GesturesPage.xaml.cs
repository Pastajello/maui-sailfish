using System.Collections.ObjectModel;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.SailfishOS.Sample.Pages;

/// <summary>
/// Gallery of gesture recognizers and interactive containers (scrolling, RefreshView, SwipeView).
/// </summary>
public partial class GesturesPage : ContentPage
{
	private readonly ObservableCollection<string> _items = new() { "alpha", "beta", "gamma" };

	private int _taps;
	private double _panX, _panY, _pinch = 1.0;
	private int _refreshCount;
	private string _swipe = "-", _swipeView = "-";

	public GesturesPage()
	{
		InitializeComponent();
		RefreshList.ItemsSource = _items;
	}

	private void UpdateStatus() =>
		StatusLabel.Text =
			$"taps: {_taps} | pan: {_panX:0},{_panY:0} | swipe: {_swipe} | " +
			$"pinch: {_pinch:0.00} | refresh: {_refreshCount} | swipeview: {_swipeView}";

	private void OnTapped(object? sender, TappedEventArgs e)
	{
		_taps++;
		UpdateStatus();
	}

	private void OnPanned(object? sender, PanUpdatedEventArgs e)
	{
		if (e.StatusType == GestureStatus.Running)
		{
			_panX = e.TotalX;
			_panY = e.TotalY;
			UpdateStatus();
		}
	}

	private void OnSwiped(object? sender, SwipedEventArgs e)
	{
		_swipe = e.Direction.ToString();
		UpdateStatus();
	}

	private void OnPinched(object? sender, PinchGestureUpdatedEventArgs e)
	{
		if (e.Status == GestureStatus.Running)
		{
			_pinch = Math.Clamp(_pinch * e.Scale, 0.25, 4.0);
			GestureTarget.Scale = _pinch;
			UpdateStatus();
		}
	}

	private void OnRefreshing(object? sender, EventArgs e)
	{
		_refreshCount++;
		if (_items.Count < 9)
			_items.Add($"refreshed #{_refreshCount}");

		Refresh.IsRefreshing = false;
		UpdateStatus();
	}

	private void OnDeleteInvoked(object? sender, EventArgs e)
	{
		_swipeView = "delete";
		UpdateStatus();
	}

	private void OnArchiveInvoked(object? sender, EventArgs e)
	{
		_swipeView = "archive";
		UpdateStatus();
	}
}
