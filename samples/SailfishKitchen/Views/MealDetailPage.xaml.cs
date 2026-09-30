using Microsoft.Maui.Controls;
using SailfishKitchen.ViewModels;

namespace SailfishKitchen.Views;

/// <summary>The recipe screen; code-behind drives the hero reveal and the nav title.</summary>
public partial class MealDetailPage : ViewModelPage
{
	private readonly MealDetailViewModel _viewModel;
	private bool _heroRevealed;

	public MealDetailPage(MealDetailViewModel viewModel, BannerViewModel banner)
		: base(viewModel)
	{
		_viewModel = viewModel;
		InitializeComponent();

		Banner.BindingContext = banner;
		banner.IsActive = true;

		BindingContext = viewModel;

		// The nav bar ignores Title changes after inflation, so push the loaded name directly.
		_viewModel.PropertyChanged += (_, e) =>
		{
			if (e.PropertyName == nameof(MealDetailViewModel.MealName) && !string.IsNullOrEmpty(_viewModel.MealName))
				Title = _viewModel.MealName;
		};
	}

	/// <summary>Set by <see cref="Services.INavigationService"/> before the page appears.</summary>
	public string MealId
	{
		get => _viewModel.MealId;
		set => _viewModel.MealId = value;
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		_viewModel.LoadCommand.Execute(null);
	}

	protected override void OnViewModelPropertyChanged(string? propertyName)
	{
		// Reveal the hero once the download finishes, or the animation plays over the placeholder.
		if (propertyName == nameof(MealDetailViewModel.IsHeroLoading) && !_viewModel.IsHeroLoading)
			_ = RevealHeroAsync();
	}

	private async Task RevealHeroAsync()
	{
		if (_heroRevealed || !_viewModel.AnimationsEnabled)
			return;

		_heroRevealed = true;
		try
		{
			Hero.Opacity = 0;
			Hero.Scale = 1.06;
			await Task.WhenAll(
				Hero.FadeToAsync(1, 320, Easing.CubicOut),
				Hero.ScaleToAsync(1, 420, Easing.CubicOut));
		}
		catch (Exception)
		{
			Hero.Opacity = 1;
			Hero.Scale = 1;
		}
	}
}
