using Microsoft.Maui.Controls;
using SailfishKitchen.ViewModels;

namespace SailfishKitchen.Views;

/// <summary>Search screen; the debounce lives in the view model so its window comes from settings.</summary>
public partial class SearchPage : ViewModelPage
{
	private const int PrefetchDistance = 4;

	private readonly SearchViewModel _viewModel;

	public SearchPage(SearchViewModel viewModel, BannerViewModel banner)
		: base(viewModel)
	{
		_viewModel = viewModel;
		InitializeComponent();

		Banner.BindingContext = banner;
		banner.IsActive = true;

		BindingContext = viewModel;
		ResultsList.RemainingItemsThreshold = PrefetchDistance;
	}

	/// <summary>Set by the navigation service to open the page with a query already running.</summary>
	public string? InitialQuery
	{
		set => _viewModel.SetInitialQuery(value);
	}

	/// <summary>Same threshold contract as <see cref="CatalogPage"/>: raised once per crossing.</summary>
	private void OnResultsThresholdReached(object? sender, EventArgs e)
	{
		var count = _viewModel.Meals.Count;
		if (count == 0 || _viewModel.Meals.IsLoading)
			return;

		_viewModel.LoadMoreCommand.Execute(null);
	}
}
