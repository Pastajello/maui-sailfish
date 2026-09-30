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
		ApplyLayout();
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
		if (propertyName is nameof(CatalogViewModel.GridSpan) or nameof(CatalogViewModel.IsGridLayout))
			ApplyLayout();
	}

	/// <summary>
	/// Changes the column count on the same CollectionView so the native Silica ListView keeps its scroll
	/// position and cached rows; list layout is Span=1.
	/// </summary>
	private void ApplyLayout()
	{
		// A settings change raises both IsGridLayout and GridSpan; the second call is a no-op.
		var span = _viewModel.IsGridLayout ? Math.Clamp(_viewModel.GridSpan, 1, 4) : 1;
		if (MealsList.ItemsLayout is GridItemsLayout { Span: var current } && current == span)
			return;
		MealsList.ItemsLayout = new GridItemsLayout(span, ItemsLayoutOrientation.Vertical)
		{
			HorizontalItemSpacing = 12,
			VerticalItemSpacing = 12,
		};
	}
}
