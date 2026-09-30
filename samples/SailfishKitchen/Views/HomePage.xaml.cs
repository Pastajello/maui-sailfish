using Microsoft.Maui.Controls;
using SailfishKitchen.Helpers;
using SailfishKitchen.ViewModels;

namespace SailfishKitchen.Views;

/// <summary>Landing screen; code-behind wires the shared banner and the entrance animation.</summary>
public partial class HomePage : ViewModelPage
{
	private readonly HomeViewModel _viewModel;
	private bool _hasAppeared;

	public HomePage(HomeViewModel viewModel, BannerViewModel banner)
		: base(viewModel)
	{
		_viewModel = viewModel;
		InitializeComponent();

		// The banner view model is a singleton; each page binds its own copy of the control to it.
		Banner.BindingContext = banner;
		banner.IsActive = true;

		BindingContext = viewModel;
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		_viewModel.LoadCommand.Execute(null);

		if (_hasAppeared)
			return;

		_hasAppeared = true;
		if (_viewModel.AnimationsEnabled)
			_ = CategoriesList.PlayEntranceAsync(lift: 18, fadeMs: 260, liftMs: 300);
	}
}
