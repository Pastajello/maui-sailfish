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

/// <summary>
/// Search-as-you-type: debounced keystrokes, each new term cancels the one in flight, and terms go to a persisted recent list.
/// </summary>
public partial class SearchViewModel : ViewModelBase
{
	private readonly IRecipeRepository _repository;
	private readonly ISettingsService _settings;
	private readonly IImageCache _images;
	private readonly IFavoritesService _favorites;
	private readonly INavigationService _navigation;
	private readonly ISearchHistoryService _history;
	private readonly Debouncer _debouncer = new();

	private CancellationTokenSource? _search;

	public SearchViewModel(
		IRecipeRepository repository,
		ISettingsService settings,
		IImageCache images,
		IFavoritesService favorites,
		INavigationService navigation,
		ISearchHistoryService history,
		IDispatcher dispatcher,
		IDialogService dialogs,
		INotificationService notifications,
		IMessenger messenger,
		ILogger<SearchViewModel> logger)
		: base(dialogs, notifications, dispatcher, logger, messenger)
	{
		_repository = repository;
		_settings = settings;
		_images = images;
		_favorites = favorites;
		_navigation = navigation;
		_history = history;

		Title = "Search recipes";
		StatusText = "Search TheMealDB by name — try “chicken” or “soup”.";
		Meals = new PagedCollection<MealCardViewModel>(dispatcher, FetchPageAsync);
		Meals.PageFailed += (_, e) => ApplyFailure(ResultFailure.Unknown(e.Message, e.Exception));

		foreach (var term in _history.Recent)
			Recent.Add(term);
	}

	public PagedCollection<MealCardViewModel> Meals { get; }

	public ObservableCollection<string> Recent { get; } = [];

	public override bool HasContent => Meals.Count > 0;

	[ObservableProperty]
	private string _query = string.Empty;

	[ObservableProperty]
	private bool _isSearching;

	[ObservableProperty]
	private bool _hasSearched;

	public bool ShowIdle => !HasSearched && !IsSearching;

	partial void OnIsSearchingChanged(bool value)
	{
		OnPropertyChanged(nameof(ShowIdle));
		OnPropertyChanged(nameof(ShowEmpty));
	}

	partial void OnHasSearchedChanged(bool value)
	{
		OnPropertyChanged(nameof(ShowIdle));
		OnPropertyChanged(nameof(ShowEmpty));
	}

	[ObservableProperty]
	private bool _hasRecent;

	[ObservableProperty]
	private MealCardViewModel? _selectedMeal;

	[ObservableProperty]
	private bool _animationsEnabled = true;

	partial void OnQueryChanged(string value)
	{
		var text = value?.Trim() ?? string.Empty;

		if (text.Length == 0)
		{
			_debouncer.Cancel();
			_search?.Cancel();
			Ui(() =>
			{
				IsSearching = false;
				HasSearched = false;
				Meals.ClearAll();
				ClearError();
			});
			return;
		}

		// Too short to be worth a round trip; the API would return nearly everything.
		if (text.Length < 2)
			return;

		_debouncer.Schedule(_settings.Current.SearchDebounce, () => ExecuteSearch(text));
	}

	partial void OnSelectedMealChanged(MealCardViewModel? value)
	{
		if (value is null)
			return;

		SelectedMeal = null;
		_navigation.OpenMealAsync(value.Id, value.Name);
	}

	/// <summary>Runs the search now, bypassing the debounce (Enter key, recent-term tap).</summary>
	[RelayCommand]
	private void SearchNow()
	{
		var text = Query?.Trim() ?? string.Empty;
		if (text.Length < 2)
			return;

		_debouncer.Cancel();
		ExecuteSearch(text);
	}

	[RelayCommand]
	private void UseRecent(string? term)
	{
		if (string.IsNullOrWhiteSpace(term))
			return;

		_debouncer.Cancel();
		Query = term;
		ExecuteSearch(term);
	}

	[RelayCommand]
	private void ClearQuery()
	{
		_debouncer.Cancel();
		_search?.Cancel();
		Query = string.Empty;
	}

	[RelayCommand]
	private void ClearHistory()
	{
		_history.Clear();
		Ui(() =>
		{
			Recent.Clear();
			HasRecent = false;
		});
	}

	[RelayCommand]
	private Task LoadMoreAsync() => Meals.LoadMoreAsync(_search?.Token ?? Lifetime);

	[RelayCommand]
	private void Retry()
	{
		ClearError();
		var text = Query?.Trim() ?? string.Empty;
		if (text.Length >= 2)
			ExecuteSearch(text);
	}

	private void ExecuteSearch(string text)
	{
		// A slow answer for "chic" must not overwrite the answer for "chicken". Cancelled, not disposed:
		// RunSearchAsync still holds the token.
		Interlocked.Exchange(ref _search, null)?.Cancel();
		var lifetime = Lifetime;
		var cts = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
		_search = cts;

		_history.Record(text);
		Ui(() =>
		{
			RefreshRecent();
			IsSearching = true;
			HasSearched = true;
			StatusText = $"searching “{text}”";
			ClearError();
		});

		_ = RunSearchAsync(text, cts.Token, lifetime);
	}

	private async Task RunSearchAsync(string text, CancellationToken ct, CancellationToken lifetime)
	{
		try
		{
			_repository.Invalidate(MealQuery.Search(text));
			await Meals.InitializeAsync(ct).ConfigureAwait(false);

			Ui(() => StatusText = Meals.Count == 0 ? $"nothing matches “{text}”" : $"{Meals.Count} recipes match “{text}”");
		}
		catch (OperationCanceledException)
		{
			// Superseded by a newer keystroke.
		}
		finally
		{
			// A superseded search leaves IsSearching to its successor; a shutdown still clears it.
			if (!ct.IsCancellationRequested || lifetime.IsCancellationRequested)
				Ui(() => IsSearching = false);
		}
	}

	private async Task<Page<MealCardViewModel>?> FetchPageAsync(int skip, int take, CancellationToken ct)
	{
		var text = Query?.Trim() ?? string.Empty;
		if (text.Length < 2)
			return new Page<MealCardViewModel>(Array.Empty<MealCardViewModel>(), HasMore: false, TotalCount: 0);

		var result = await _repository.GetMealsAsync(MealQuery.Search(text), skip, take, ct).ConfigureAwait(false);
		if (!result.IsSuccess)
		{
			var failure = result.Failure!;
			Ui(() => ApplyFailure(failure));
			return null;
		}

		Ui(ClearError);

		var cards = result.Value.Items
			.Select(m => new MealCardViewModel(m, _images, _favorites, Dispatcher))
			.ToList();

		var allowNetwork = _settings.Current.AllowNetwork;
		foreach (var card in cards)
			_ = card.LoadThumbnailAsync(allowNetwork, Lifetime);

		return new Page<MealCardViewModel>(cards, result.Value.HasMore, result.Value.TotalCount);
	}

	private void RefreshRecent()
	{
		Recent.ReplaceWith(_history.Recent);
		HasRecent = Recent.Count > 0;
	}

	/// <summary>Pre-fills the box when opened from another screen.</summary>
	public void SetInitialQuery(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
			return;

		Query = text;
		ExecuteSearch(text.Trim());
	}

	protected override void OnActivated()
	{
		Messenger.Register<SearchViewModel, SettingsChangedMessage>(this, static (vm, message) =>
			vm.AnimationsEnabled = message.Settings.AnimationsEnabled);
	}

	protected override void OnDeactivated()
	{
		Messenger.UnregisterAll(this);
	}

	/// <summary>Also cancels card thumbnails, the debounce and the search in flight.</summary>
	public override void Shutdown()
	{
		foreach (var card in Meals)
			card.CancelThumbnailLoad();

		// Cancel, not Dispose: the page comes back after a pop and the next keystroke schedules again.
		_debouncer.Cancel();
		Interlocked.Exchange(ref _search, null)?.Cancel();
		base.Shutdown();
	}
}
