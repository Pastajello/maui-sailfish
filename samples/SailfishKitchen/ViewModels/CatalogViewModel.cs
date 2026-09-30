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

/// <summary>
/// The paged recipe list for a category, cuisine, letter or search, fetching the next page only
/// when the user scrolls near the end.
/// </summary>
public partial class CatalogViewModel : ViewModelBase
{
	private readonly IRecipeRepository _repository;
	private readonly ISettingsService _settings;
	private readonly IImageCache _images;
	private readonly IFavoritesService _favorites;
	private readonly INavigationService _navigation;
	private readonly Func<Meal, MealCardViewModel> _cardFactory;

	private MealQuery _query = MealQuery.WholeCatalog();

	public CatalogViewModel(
		IRecipeRepository repository,
		ISettingsService settings,
		IImageCache images,
		IFavoritesService favorites,
		INavigationService navigation,
		IDispatcher dispatcher,
		IDialogService dialogs,
		INotificationService notifications,
		IMessenger messenger,
		ILogger<CatalogViewModel> logger)
		: base(dialogs, notifications, dispatcher, logger, messenger)
	{
		_repository = repository;
		_settings = settings;
		_images = images;
		_favorites = favorites;
		_navigation = navigation;
		_cardFactory = meal => new MealCardViewModel(meal, images, favorites, dispatcher);

		Meals = new PagedCollection<MealCardViewModel>(dispatcher, FetchPageAsync);
		Meals.PageLoaded += OnPageLoaded;
		Meals.PageFailed += OnPageFailed;

		Title = _query.DisplayName;
		ApplySettings(settings.Current);
	}

	public PagedCollection<MealCardViewModel> Meals { get; }

	public override bool HasContent => Meals.Count > 0;

	/// <summary>Set by <see cref="INavigationService"/> before the page appears.</summary>
	public MealQuery Query
	{
		get => _query;
		set
		{
			var next = value ?? MealQuery.WholeCatalog();
			if (_query == next)
				return;

			_query = next;
			Ui(() =>
			{
				Title = next.DisplayName;
				StatusText = null;
				LoadedCount = 0;
				HasMore = true;
				Meals.ClearAll();
				ClearError();
			});

			OnPropertyChanged();
			OnPropertyChanged(nameof(Subtitle));
		}
	}

	public string Subtitle => Query is CatalogQuery
		? "browsing the whole catalog, one letter at a time"
		: "recipes matching " + Query.DisplayName;

	/// <summary>Two-way bound to RefreshView.IsRefreshing.</summary>
	[ObservableProperty]
	private bool _isRefreshing;

	[ObservableProperty]
	private bool _hasMore = true;

	[ObservableProperty]
	private int _loadedCount;

	/// <summary>0..1 when the source knows its total; the bar hides itself otherwise.</summary>
	[ObservableProperty]
	private double _loadProgress;

	[ObservableProperty]
	private bool _isProgressKnown;

	[ObservableProperty]
	private bool _isGridLayout = true;

	[ObservableProperty]
	private int _gridSpan = 2;

	[ObservableProperty]
	private bool _animationsEnabled = true;

	/// <summary>Two-way bound to CollectionView.SelectedItem; selecting navigates, then clears.</summary>
	[ObservableProperty]
	private MealCardViewModel? _selectedMeal;

	partial void OnSelectedMealChanged(MealCardViewModel? value)
	{
		if (value is null)
			return;

		// Clear immediately so tapping the same row twice navigates twice.
		SelectedMeal = null;
		OpenMealCommand.Execute(value);
	}

	partial void OnIsRefreshingChanged(bool value)
	{
		if (value)
			RefreshCommand.Execute(null);
	}

	/// <summary>Loads the first page. Idempotent, so OnAppearing and a retry can both call it.</summary>
	[RelayCommand]
	private Task LoadInitialAsync() =>
		HasContent || Meals.IsLoading ? Task.CompletedTask : Meals.InitializeAsync(Lifetime);

	/// <summary>
	/// Asks for the next page. Also called from the view's scroll signal in case RemainingItemsThreshold never fires.
	/// </summary>
	[RelayCommand]
	private Task LoadMoreAsync() => Meals.LoadMoreAsync(Lifetime);

	[RelayCommand]
	private async Task RefreshAsync()
	{
		ClearError();
		_repository.Invalidate(Query);

		try
		{
			await Meals.RefreshAsync(Lifetime).ConfigureAwait(false);
		}
		finally
		{
			Ui(() => IsRefreshing = false);
		}
	}

	[RelayCommand]
	private Task RetryAsync()
	{
		ClearError();
		return HasContent ? LoadMoreAsync() : LoadInitialAsync();
	}

	[RelayCommand]
	private Task OpenMealAsync(MealCardViewModel? card) =>
		card is null ? Task.CompletedTask : _navigation.OpenMealAsync(card.Id, card.Name);

	[RelayCommand]
	private Task OpenSearchAsync() => _navigation.OpenSearchAsync();

	[RelayCommand]
	private async Task ToggleLayoutAsync()
	{
		var next = IsGridLayout ? Models.ListLayout.List : Models.ListLayout.Grid;
		_settings.Update(s => s.Layout = next, nameof(AppSettings.Layout));
		ApplySettings(_settings.Current);
		await Notifications.ShowAsync(next == Models.ListLayout.Grid ? "Grid layout" : "List layout").ConfigureAwait(false);
	}

	[RelayCommand]
	private async Task ClearImageCacheAsync()
	{
		var confirmed = await Dialogs.ConfirmAsync(
			"Clear cached images?",
			$"This removes {_images.CachedCount} downloaded thumbnails from the device. Recipes themselves stay cached.",
			"Clear", "Cancel").ConfigureAwait(false);

		if (!confirmed)
			return;

		await _images.ClearAsync().ConfigureAwait(false);
		await Notifications.ShowAsync("Image cache cleared", NotificationKind.Success).ConfigureAwait(false);
	}

	private async Task<Page<MealCardViewModel>?> FetchPageAsync(int skip, int take, CancellationToken ct)
	{
		var result = await _repository.GetMealsAsync(Query, skip, take, ct).ConfigureAwait(false);
		if (!result.IsSuccess)
		{
			var failure = result.Failure!;
			Ui(() => ApplyFailure(failure));
			return null;
		}

		Ui(ClearError);

		var page = result.Value;
		var cards = page.Items.Select(_cardFactory).ToList();

		// Fire and forget: each card resolves its own thumb, and the list must not wait for downloads.
		var allowNetwork = _settings.Current.AllowNetwork;
		foreach (var card in cards)
			_ = card.LoadThumbnailAsync(allowNetwork, Lifetime);

		return new Page<MealCardViewModel>(cards, page.HasMore, page.TotalCount);
	}

	private void OnPageLoaded(object? sender, PageLoadedEventArgs e)
	{
		LoadedCount = e.TotalCountLoaded;
		HasMore = e.HasMore;
		IsProgressKnown = e.KnownTotal.HasValue;
		LoadProgress = e.KnownTotal is { } total && total > 0
			? Math.Clamp((double)e.TotalCountLoaded / total, 0d, 1d)
			: 0d;

		StatusText = e.KnownTotal is { } known
			? $"{e.TotalCountLoaded} of {known} recipes"
			: $"{e.TotalCountLoaded} recipes loaded";

		RaiseContentStateChanged();
	}

	private void OnPageFailed(object? sender, PageFailedEventArgs e) =>
		ApplyFailure(ResultFailure.Unknown(e.Message, e.Exception));

	private void ApplySettings(AppSettings settings)
	{
		Ui(() =>
		{
			IsGridLayout = settings.Layout == Models.ListLayout.Grid;
			GridSpan = settings.GridSpan;
			AnimationsEnabled = settings.AnimationsEnabled;
			Meals.PageSize = settings.PageSize;
		});
	}

	protected override void OnActivated()
	{
		Messenger.Register<CatalogViewModel, SettingsChangedMessage>(this, static (vm, message) => vm.ApplySettings(message.Settings));
		Messenger.Register<CatalogViewModel, FavoriteToggledMessage>(this, static (vm, message) => vm.SyncFavorite(message));
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

	private void SyncFavorite(FavoriteToggledMessage message)
	{
		foreach (var card in Meals)
		{
			if (card.Id == message.MealId)
				card.SyncFavorite(message.IsFavorite);
		}
	}
}
