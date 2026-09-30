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
		ApplyLayout();
	}

	protected override void OnViewModelPropertyChanged(string? propertyName)
	{
		if (propertyName is nameof(FavoritesViewModel.GridSpan) or nameof(FavoritesViewModel.IsGridLayout))
			ApplyLayout();
	}

	private void ApplyLayout()
	{
		// A settings change raises both IsGridLayout and GridSpan; the second call is a no-op.
		var span = _viewModel.IsGridLayout ? Math.Clamp(_viewModel.GridSpan, 1, 4) : 1;
		if (FavoritesList.ItemsLayout is GridItemsLayout { Span: var current } && current == span)
			return;
		FavoritesList.ItemsLayout = new GridItemsLayout(span, ItemsLayoutOrientation.Vertical)
		{
			HorizontalItemSpacing = 12,
			VerticalItemSpacing = 12,
		};
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();

		// Re-read on every appearance: a detail page pushed on top may have changed favourites.
		_viewModel.LoadCommand.Execute(null);
	}
}
