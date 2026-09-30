using Microsoft.Maui.Controls;
using SailfishKitchen.Helpers;
using SailfishKitchen.Models;
using SailfishKitchen.ViewModels;

namespace SailfishKitchen.Views;

/// <summary>
/// The paged recipe list. Code-behind handles grid span changes, the incremental-load trigger and the
/// entrance animation.
/// </summary>
public partial class CatalogPage : ViewModelPage
{
	/// <summary>Rows before the end at which the next page is requested.</summary>
	private const int PrefetchDistance = 4;

	private readonly CatalogViewModel _viewModel;
	private bool _hasAppeared;

	public CatalogPage(CatalogViewModel viewModel, BannerViewModel banner)
		: base(viewModel)
	{
		_viewModel = viewModel;
		InitializeComponent();

		Banner.BindingContext = banner;
		banner.IsActive = true;

		BindingContext = viewModel;
		MealsList.RemainingItemsThreshold = PrefetchDistance;
		ApplySpan(viewModel.GridSpan);
	}

	/// <summary>Set by <see cref="Services.INavigationService"/> before the page appears.</summary>
	public MealQuery Query
	{
		get => _viewModel.Query;
		set => _viewModel.Query = value;
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		_viewModel.LoadInitialCommand.Execute(null);

		if (_hasAppeared)
			return;

		_hasAppeared = true;
		if (_viewModel.AnimationsEnabled)
			_ = MealsList.PlayEntranceAsync(lift: 14, fadeMs: 240, liftMs: 280);
	}

	/// <summary>
	/// Fires once per threshold crossing (evaluated by the Qt host bridge on each native scroll report)
	/// and re-arms when the list grows or the user scrolls back.
	/// </summary>
	private void OnMealsThresholdReached(object? sender, EventArgs e)
	{
		if (_viewModel.Meals.Count == 0 || !_viewModel.HasMore || _viewModel.Meals.IsLoading)
			return;

		_viewModel.LoadMoreCommand.Execute(null);
	}

	protected override void OnViewModelPropertyChanged(string? propertyName)
	{
		if (propertyName == nameof(CatalogViewModel.GridSpan))
			ApplySpan(_viewModel.GridSpan);
	}

	/// <summary>
	/// Changes the column count on the same CollectionView so the native Silica ListView keeps its scroll
	/// position and cached rows; list layout is Span=1.
	/// </summary>
	private void ApplySpan(int span) =>
		MealsList.ItemsLayout = new GridItemsLayout(Math.Clamp(span, 1, 4), ItemsLayoutOrientation.Vertical)
		{
			HorizontalItemSpacing = 12,
			VerticalItemSpacing = 12,
		};
}
