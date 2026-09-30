using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Dispatching;
using SailfishKitchen.Helpers;
using SailfishKitchen.Messaging;
using SailfishKitchen.Models;
using SailfishKitchen.Services;

namespace SailfishKitchen.ViewModels;

/// <summary>The saved-recipes screen, backed entirely by local storage so it works offline.</summary>
public partial class FavoritesViewModel : ViewModelBase
{
	private const string AllCategoriesLabel = "All categories";

	private readonly IFavoritesService _favorites;
	private readonly IImageCache _images;
	private readonly ISettingsService _settings;
	private readonly INavigationService _navigation;

	public FavoritesViewModel(
		IFavoritesService favorites,
		IImageCache images,
		ISettingsService settings,
		INavigationService navigation,
		IDispatcher dispatcher,
		IDialogService dialogs,
		INotificationService notifications,
		IMessenger messenger,
		ILogger<FavoritesViewModel> logger)
		: base(dialogs, notifications, dispatcher, logger, messenger)
	{
		_favorites = favorites;
		_images = images;
		_settings = settings;
		_navigation = navigation;

		Title = "Favourites";
	}

	public ObservableCollection<MealCardViewModel> Meals { get; } = [];

	/// <summary>
	/// Filter options with an "all" entry at index 0, because a Picker with a null SelectedItem shows its Title.
	/// </summary>
	public ObservableCollection<string> FilterOptions { get; } = [AllCategoriesLabel];

	public override bool HasContent => Meals.Count > 0;

	public bool HasFilters => FilterOptions.Count > 1;

	public string Headline => Count == 1 ? "1 saved recipe" : $"{Count} saved recipes";

	[ObservableProperty]
	private int _count;

	[ObservableProperty]
	private bool _isGridLayout = true;

	[ObservableProperty]
	private int _gridSpan = 2;

	[ObservableProperty]
	private bool _animationsEnabled = true;

	[ObservableProperty]
	private string _selectedFilterOption = AllCategoriesLabel;

	private string? CategoryFilter =>
		SelectedFilterOption == AllCategoriesLabel ? null : SelectedFilterOption;

	[ObservableProperty]
	private MealCardViewModel? _selectedMeal;

	partial void OnSelectedMealChanged(MealCardViewModel? value)
	{
		if (value is null)
			return;

		SelectedMeal = null;
		_navigation.OpenMealAsync(value.Id, value.Name);
	}

	partial void OnSelectedFilterOptionChanged(string value)
	{
		// A Picker reports a null selection while rebinding; ignore it so the list is not emptied.
		if (string.IsNullOrEmpty(value))
			return;

		Rebuild();
	}

	[RelayCommand]
	private Task LoadAsync()
	{
		var settings = _settings.Current;
		Ui(() =>
		{
			IsGridLayout = settings.Layout == Models.ListLayout.Grid;
			GridSpan = settings.GridSpan;
			AnimationsEnabled = settings.AnimationsEnabled;
		});

		Rebuild();
		return Task.CompletedTask;
	}

	[RelayCommand]
	private async Task RemoveAsync(MealCardViewModel? card)
	{
		if (card is null)
			return;

		var removed = _favorites.Remove(card.Id);
		if (!removed)
			return;

		// The messenger round-trip removes the row from every open list, including this one.
		await Notifications.ShowAsync($"Removed “{card.Name}”", NotificationKind.Info).ConfigureAwait(false);
	}

	[RelayCommand]
	private Task BrowseAllAsync() => _navigation.OpenCatalogAsync(MealQuery.WholeCatalog());

	[RelayCommand]
	private async Task ClearAllAsync()
	{
		if (Count == 0)
			return;

		var confirmed = await Dialogs.ConfirmAsync(
			"Remove all favourites?",
			$"This deletes {Count} saved recipes from this device. It cannot be undone.",
			"Remove all", "Cancel").ConfigureAwait(false);

		if (!confirmed)
			return;

		_favorites.Clear();
		await Notifications.ShowAsync("Favourites cleared", NotificationKind.Success).ConfigureAwait(false);
	}

	private void Rebuild()
	{
		var all = _favorites.Snapshot();

		Ui(() =>
		{
			var selected = SelectedFilterOption;
			FilterOptions.ReplaceWith(all.Select(f => f.Category)
				.Where(c => !string.IsNullOrWhiteSpace(c))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
				.Prepend(AllCategoriesLabel));

			// Fall back to "all" if the selected category just lost its last recipe.
			SelectedFilterOption = FilterOptions.Contains(selected) ? selected : AllCategoriesLabel;
			OnPropertyChanged(nameof(HasFilters));
			OnPropertyChanged(nameof(Headline));

			var cards = all
				.Where(f => CategoryFilter is null || string.Equals(f.Category, CategoryFilter, StringComparison.OrdinalIgnoreCase))
				.Select(f => new MealCardViewModel(f.ToMeal(), _images, _favorites, Dispatcher))
				.ToList();
			foreach (var card in cards)
				card.SyncFavorite(true);

			Meals.ReplaceWith(cards);

			// Offline this resolves to the placeholder and clears the shimmer.
			var allowNetwork = _settings.Current.AllowNetwork;
			foreach (var card in cards)
				_ = card.LoadThumbnailAsync(allowNetwork, Lifetime);

			Count = Meals.Count;
			RaiseContentStateChanged();
			StatusText = Count == 0
				? null
				: CategoryFilter is null ? $"{Count} saved recipes" : $"{Count} saved in {CategoryFilter}";
		});
	}

	protected override void OnActivated()
	{
		Messenger.Register<FavoritesViewModel, FavoriteToggledMessage>(this, static (vm, _) => vm.Rebuild());
		Messenger.Register<FavoritesViewModel, FavoritesResetMessage>(this, static (vm, _) => vm.Rebuild());
		Messenger.Register<FavoritesViewModel, SettingsChangedMessage>(this, static (vm, message) =>
		{
			vm.IsGridLayout = message.Settings.Layout == Models.ListLayout.Grid;
			vm.GridSpan = message.Settings.GridSpan;
			vm.AnimationsEnabled = message.Settings.AnimationsEnabled;
		});
	}

	protected override void OnDeactivated()
	{
		Messenger.UnregisterAll(this);
	}

	/// <summary>Also cancels each card's thumbnail load.</summary>
	public override void Shutdown()
	{
		foreach (var card in Meals)
			card.CancelThumbnailLoad();

		base.Shutdown();
	}
}

internal static class FavoriteMealExtensions
{
	/// <summary>The stored favourite carries everything a card needs, so no network is involved.</summary>
	public static Meal ToMeal(this FavoriteMeal favorite) =>
		new(favorite.Id, favorite.Name, favorite.ThumbnailUrl, favorite.Category, favorite.Cuisine);
}
