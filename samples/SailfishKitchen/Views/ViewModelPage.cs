using System.ComponentModel;
using Microsoft.Maui.Controls;
using SailfishKitchen.ViewModels;

namespace SailfishKitchen.Views;

/// <summary>
/// Ties a page's visibility to its view model: messenger activation and a PropertyChanged hook while shown,
/// <see cref="ViewModelBase.Shutdown"/> when hidden. OnDisappearing also fires under a pushed page, so every
/// appear redoes what the last disappear undid.
/// </summary>
public abstract class ViewModelPage : ContentPage
{
	private readonly ViewModelBase _viewModel;

	protected ViewModelPage(ViewModelBase viewModel) => _viewModel = viewModel;

	protected override void OnAppearing()
	{
		base.OnAppearing();

		// -= first, so a repeated OnAppearing cannot double the handler.
		_viewModel.PropertyChanged -= HandleViewModelPropertyChanged;
		_viewModel.PropertyChanged += HandleViewModelPropertyChanged;
		_viewModel.IsActive = true;
	}

	protected override void OnDisappearing()
	{
		_viewModel.PropertyChanged -= HandleViewModelPropertyChanged;
		_viewModel.IsActive = false;
		_viewModel.Shutdown();
		base.OnDisappearing();
	}

	/// <summary>View-side reactions to view-model changes, called only while the page is shown.</summary>
	protected virtual void OnViewModelPropertyChanged(string? propertyName)
	{
	}

	private void HandleViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
		OnViewModelPropertyChanged(e.PropertyName);
}
