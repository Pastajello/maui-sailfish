using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using SailfishKitchen.Helpers;
using SailfishKitchen.Messaging;
using SailfishKitchen.Models;
using SailfishKitchen.Services;

namespace SailfishKitchen.ViewModels;

/// <summary>A cuisine row on the home screen.</summary>
public sealed record CuisineOption(string Area, string Country);

/// <summary>
/// The landing screen: category tiles, cuisines, random suggestions and entry points, loaded in one pass.
/// </summary>
public partial class HomeViewModel : ViewModelBase
{
	private readonly IRecipeRepository _repository;
	private readonly ISettingsService _settings;
	private readonly IImageCache _images;
	private readonly IFavoritesService _favorites;
	private readonly INavigationService _navigation;
	private readonly IConnectivityService _connectivity;

	/// <summary>Set once a load pass ran to the end without a Shutdown cancelling it.</summary>
	private bool _isLoaded;

	public HomeViewModel(
		IRecipeRepository repository,
		ISettingsService settings,
		IImageCache images,
		IFavoritesService favorites,
		INavigationService navigation,
		IConnectivityService connectivity,
		IDispatcher dispatcher,
		IDialogService dialogs,
		INotificationService notifications,
		IMessenger messenger,
		ILogger<HomeViewModel> logger)
		: base(dialogs, notifications, dispatcher, logger, messenger)
	{
		_repository = repository;
		_settings = settings;
		_images = images;
		_favorites = favorites;
		_navigation = navigation;
		_connectivity = connectivity;

		Title = "Sailfish Kitchen";
		FavoritesCount = favorites.Count;
		AnimationsEnabled = settings.Current.AnimationsEnabled;
	}

	public ObservableCollection<CategoryTileViewModel> Categories { get; } = [];

	public ObservableCollection<CuisineOption> Cuisines { get; } = [];

	public ObservableCollection<SuggestionViewModel> Suggestions { get; } = [];

	public override bool HasContent => Categories.Count > 0;
	public bool DoesntHaveContent => Categories.Count == 0;

	public string Subtitle => "recipes from TheMealDB, built for Sailfish OS";

	/// <summary>Footer credit; the free API asks for attribution.</summary>
	public string FooterNote => $"Data by TheMealDB · {_favorites.Count} saved · {SafeCacheNote()}";

	public bool HasSuggestions => Suggestions.Count > 0;

	[ObservableProperty] private bool _animationsEnabled = true;
	[ObservableProperty] private int _favoritesCount;
	[ObservableProperty] private bool _isOffline;
	[ObservableProperty] private string? _offlineBanner;

	[ObservableProperty] private CuisineOption? _selectedCuisine;

	/// <summary>Two-way bound to the category grid's SelectedItem; selecting navigates, then clears.</summary>
	[ObservableProperty] private CategoryTileViewModel? _selectedCategory;

	partial void OnSelectedCategoryChanged(CategoryTileViewModel? value)
	{
		if (value is null)
			return;

		SelectedCategory = null;
		_navigation.OpenCatalogAsync(MealQuery.ByCategory(value.Name));
	}

	partial void OnSelectedCuisineChanged(CuisineOption? value)
	{
		if (value is null)
			return;

		SelectedCuisine = null;
		_navigation.OpenCatalogAsync(MealQuery.ByCuisine(value.Area, value.Country));
	}

	private string SafeCacheNote()
	{
		try
		{
			return $"{_images.CachedCount} images cached";
		}
		catch (Exception ex)
		{
			// The cache directory may be cleared underneath us; the footer must never throw during layout.
			Logger.LogDebug(ex, "home: cache count unavailable");
			return "cache unavailable";
		}
	}

	/// <summary>
	/// Loads categories, cuisines and suggestions. Idempotent; after a Shutdown under a pushed page it
	/// finishes only what that cancelled.
	/// </summary>
	[RelayCommand]
	private async Task LoadAsync()
	{
		// Read on the UI thread, before the first await; one token for the whole pass.
		var lifetime = Lifetime;
		var needCuisines = Cuisines.Count == 0;
		var needSuggestions = Suggestions.Count == 0;

		foreach (var tile in Categories)
			tile.LoadThumbnail(_settings.Current.OfflineMode, lifetime);
		foreach (var suggestion in Suggestions)
			suggestion.LoadThumbnail(_settings.Current.OfflineMode, lifetime);

		if (_isLoaded)
			return;

		if (!HasContent)
		{
			RefreshConnectivity();

			var categories = await RunBusyAsync(ct => _repository.GetCategoriesAsync(ct), lifetime).ConfigureAwait(false);
			if (categories is null)
				return;

			Ui(() =>
			{
				Categories.ReplaceWith(categories.Select(c =>
					new CategoryTileViewModel(c, _images, _settings.Current.OfflineMode, lifetime, Dispatcher)));
			});
		}

		// Cuisines and suggestions are best-effort and must not blank the categories above.
		if (needCuisines)
		{
			var cuisines = await _repository.GetCuisinesAsync(lifetime).ConfigureAwait(false);
			if (cuisines.IsSuccess)
			{
				Ui(() =>
				{
					Cuisines.ReplaceWith(cuisines.Value
						.Where(c => !string.IsNullOrWhiteSpace(c.Area))
						.Select(c => new CuisineOption(c.Area, c.Country)));
				});
			}
			else if (!cuisines.Failure!.IsSilent)
			{
				Logger.LogWarning("home: cuisines unavailable ({Kind})", cuisines.Failure.Kind);
			}
		}

		if (needSuggestions)
		{
			var suggestions = await _repository.GetSuggestionsAsync(3, lifetime).ConfigureAwait(false);
			if (suggestions.IsSuccess)
			{
				Ui(() =>
				{
					Suggestions.ReplaceWith(suggestions.Value.Select(m =>
						new SuggestionViewModel(m, _images, _settings.Current.OfflineMode, lifetime, Dispatcher)));

					// Computed properties have no backing field, so they are raised by hand.
					OnPropertyChanged(nameof(HasSuggestions));
					OnPropertyChanged(nameof(FooterNote));
				});
			}
		}

		Ui(() =>
		{
			OnPropertyChanged(nameof(HasContent));
			OnPropertyChanged(nameof(DoesntHaveContent));
		});

		// A failed section stays empty as before; only a cancelled pass is picked up on the next appear.
		if (!lifetime.IsCancellationRequested)
			_isLoaded = true;
	}

	[RelayCommand]
	private Task OpenCategoryAsync(CategoryTileViewModel? tile) =>
		tile is null ? Task.CompletedTask : _navigation.OpenCatalogAsync(MealQuery.ByCategory(tile.Name));

	[RelayCommand]
	private Task OpenSuggestionAsync(SuggestionViewModel? suggestion) =>
		suggestion is null ? Task.CompletedTask : _navigation.OpenMealAsync(suggestion.Id, suggestion.Name);

	[RelayCommand]
	private Task OpenSearchAsync() => _navigation.OpenSearchAsync();

	[RelayCommand]
	private Task OpenFavoritesAsync() => _navigation.OpenFavoritesAsync();

	[RelayCommand]
	private Task OpenSettingsAsync() => _navigation.OpenSettingsAsync();

	[RelayCommand]
	private Task BrowseAllAsync() => _navigation.OpenCatalogAsync(MealQuery.WholeCatalog());

	[RelayCommand]
	private async Task SurpriseMeAsync()
	{
		var result = await RunBusyAsync(ct => _repository.GetRandomMealAsync(ct), Lifetime).ConfigureAwait(false);
		if (result is null)
			return;

		await _navigation.OpenMealAsync(result.Id, result.Name).ConfigureAwait(false);
	}

	private void RefreshConnectivity()
	{
		var online = _connectivity.IsOnline;
		Ui(() =>
		{
			IsOffline = !online;
			OfflineBanner = _settings.Current.OfflineMode
				? "Offline mode — showing the bundled catalog"
				: online ? null : "No connection — showing cached recipes";
		});
	}

	protected override void OnActivated()
	{
		Messenger.Register<HomeViewModel, FavoritesResetMessage>(this, static (vm, _) => vm.FavoritesCount = vm._favorites.Count);
		Messenger.Register<HomeViewModel, FavoriteToggledMessage>(this, static (vm, _) => vm.FavoritesCount = vm._favorites.Count);
		Messenger.Register<HomeViewModel, ConnectivityChangedMessage>(this, static (vm, _) => vm.RefreshConnectivity());
		Messenger.Register<HomeViewModel, SettingsChangedMessage>(this, static (vm, _) => vm.RefreshConnectivity());
	}

	protected override void OnDeactivated()
	{
		Messenger.UnregisterAll(this);
	}
}

/// <summary>A category tile: name and description blurb over its thumb.</summary>
public sealed class CategoryTileViewModel : ThumbnailViewModel
{
	public CategoryTileViewModel(Category category, IImageCache images, bool offline, CancellationToken lifetime, IDispatcher dispatcher)
		: base(category.ThumbnailUrl, images, offline, lifetime, dispatcher)
	{
		Category = category;
		Name = category.Name;
		Description = category.ShortDescription;
	}

	public Category Category { get; }

	public string Name { get; }

	public string Description { get; }
}

/// <summary>A "what should I cook" card backed by a fully loaded recipe.</summary>
public sealed class SuggestionViewModel : ThumbnailViewModel
{
	public SuggestionViewModel(MealDetail meal, IImageCache images, bool offline, CancellationToken lifetime, IDispatcher dispatcher)
		: base(meal.ThumbnailUrl, images, offline, lifetime, dispatcher)
	{
		Meal = meal;
		Id = meal.Id;
		Name = meal.Name;
		Subtitle = $"{meal.Category} · {meal.Cuisine}";
		IngredientsSummary = meal.IngredientsSummary;
	}

	public MealDetail Meal { get; }

	public string Id { get; }

	public string Name { get; }

	public string Subtitle { get; }

	public string IngredientsSummary { get; }
}
