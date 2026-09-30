using Microsoft.Maui.Controls;
using SailfishKitchen.ViewModels;

namespace SailfishKitchen.Views;

/// <summary>Saved recipes, served entirely from local storage so the screen works offline.</summary>
public partial class FavoritesPage : ViewModelPage
{
	private readonly FavoritesViewModel _viewModel;

	public FavoritesPage(FavoritesViewModel viewModel, BannerViewModel banner)
		: base(viewModel)
	{
		_viewModel = viewModel;
		InitializeComponent();

		Banner.BindingContext = banner;
		banner.IsActive = true;

		BindingContext = viewModel;
		ApplySpan(viewModel.GridSpan);
	}

	protected override void OnViewModelPropertyChanged(string? propertyName)
	{
		if (propertyName == nameof(FavoritesViewModel.GridSpan))
			ApplySpan(_viewModel.GridSpan);
	}

	private void ApplySpan(int span) =>
		FavoritesList.ItemsLayout = new GridItemsLayout(Math.Clamp(span, 1, 4), ItemsLayoutOrientation.Vertical)
		{
			HorizontalItemSpacing = 12,
			VerticalItemSpacing = 12,
		};

	protected override void OnAppearing()
	{
		base.OnAppearing();

		// Re-read on every appearance: a detail page pushed on top may have changed favourites.
		_viewModel.LoadCommand.Execute(null);
	}
}
