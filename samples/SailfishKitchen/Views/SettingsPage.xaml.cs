using Microsoft.Maui.Controls;
using SailfishKitchen.ViewModels;

namespace SailfishKitchen.Views;

/// <summary>Settings and on-device diagnostics.</summary>
public partial class SettingsPage : ViewModelPage
{
	private readonly SettingsViewModel _viewModel;

	public SettingsPage(SettingsViewModel viewModel, BannerViewModel banner)
		: base(viewModel)
	{
		_viewModel = viewModel;
		InitializeComponent();

		Banner.BindingContext = banner;
		banner.IsActive = true;

		BindingContext = viewModel;
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();

		// Cache sizes and connection state change behind the page's back, so re-read on every appearance.
		_viewModel.Refresh();
	}
}
