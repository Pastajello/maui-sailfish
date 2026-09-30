using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using SailfishKitchen.Helpers;
using SailfishKitchen.Messaging;
using SailfishKitchen.Models;
using SailfishKitchen.Services;

namespace SailfishKitchen.ViewModels;

/// <summary>One numbered instruction step.</summary>
public sealed record StepViewModel(int Number, string Text);

/// <summary>
/// The recipe screen: hero, ingredients, steps, favourite, sharing and a related strip. It does the most
/// off-thread work, so its lifetime token is retired by <see cref="Shutdown"/>.
/// </summary>
public partial class MealDetailViewModel : ViewModelBase
{
	private readonly IRecipeRepository _repository;
	private readonly IFavoritesService _favorites;
	private readonly IImageCache _images;
	private readonly INavigationService _navigation;
	private readonly ISettingsService _settings;

	private MealDetail? _detail;
	private string _mealId = string.Empty;

	public MealDetailViewModel(
		IRecipeRepository repository,
		IFavoritesService favorites,
		IImageCache images,
		INavigationService navigation,
		ISettingsService settings,
		IDispatcher dispatcher,
		IDialogService dialogs,
		INotificationService notifications,
		IMessenger messenger,
		ILogger<MealDetailViewModel> logger)
		: base(dialogs, notifications, dispatcher, logger, messenger)
	{
		_repository = repository;
		_favorites = favorites;
		_images = images;
		_navigation = navigation;
		_settings = settings;

		AnimationsEnabled = settings.Current.AnimationsEnabled;

		// An Image inflated with Source=null never accepts a later source on this host.
		HeroImage = _images.Placeholder;
	}

	public ObservableCollection<StepViewModel> Steps { get; } = [];

	public ObservableCollection<Ingredient> Ingredients { get; } = [];

	public ObservableCollection<string> Tags { get; } = [];

	public ObservableCollection<MealCardViewModel> Related { get; } = [];

	public override bool HasContent => _detail is not null;

	/// <summary>Set by the navigation service before the page appears.</summary>
	public string MealId
	{
		get => _mealId;
		set
		{
			if (_mealId == value)
				return;

			_mealId = value ?? string.Empty;
			OnPropertyChanged();
		}
	}

	[ObservableProperty]
	private string _mealName = string.Empty;

	[ObservableProperty]
	private string _subtitle = string.Empty;

	[ObservableProperty]
	private ImageSource? _heroImage;

	/// <summary>Drives the hero fade/scale-in.</summary>
	[ObservableProperty]
	private bool _isHeroLoading = true;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(FavoriteGlyph))]
	private bool _isFavorite;

	/// <summary>A text glyph because this backend has no ImageButton adapter.</summary>
	public string FavoriteGlyph => IsFavorite ? "★" : "☆";

	[ObservableProperty]
	private bool _hasSteps;

	[ObservableProperty]
	private bool _hasIngredients;

	[ObservableProperty]
	private bool _hasRelated;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasYoutube))]
	private string? _youtubeUrl;

	public bool HasYoutube => !string.IsNullOrWhiteSpace(YoutubeUrl);

	[ObservableProperty]
	private string? _sourceUrl;

	[ObservableProperty]
	private bool _animationsEnabled;

	[RelayCommand]
	private async Task LoadAsync()
	{
		if (string.IsNullOrWhiteSpace(MealId))
		{
			Ui(() => ApplyFailure(ResultFailure.NotFound("No recipe was selected.")));
			return;
		}

		var detail = await RunBusyAsync(ct => _repository.GetMealAsync(MealId, ct), Lifetime).ConfigureAwait(false);
		if (detail is null)
			return;

		_detail = detail;

		Ui(() =>
		{
			MealName = detail.Name;
			Title = detail.Name;
			Subtitle = $"{detail.Category} · {detail.Cuisine}";
			IsFavorite = _favorites.IsFavorite(detail.Id);
			YoutubeUrl = detail.YoutubeUrl;
			SourceUrl = detail.SourceUrl;

			Steps.ReplaceWith(detail.Steps.Select((text, i) => new StepViewModel(i + 1, text)));
			HasSteps = Steps.Count > 0;

			Ingredients.ReplaceWith(detail.Ingredients);
			HasIngredients = Ingredients.Count > 0;

			Tags.ReplaceWith(detail.Tags);
		});

		HeroImage = _images.Placeholder;
		await ResolveHeroAsync(detail.ThumbnailUrl).ConfigureAwait(false);

		// Related recipes are optional; a failure must not disturb the page.
		await LoadRelatedAsync(detail).ConfigureAwait(false);
	}

	[RelayCommand]
	private async Task ToggleFavoriteAsync()
	{
		if (_detail is null)
			return;

		var nowFavorite = _favorites.Toggle(_detail.ToSummary());
		Ui(() => IsFavorite = nowFavorite);
		await Notifications.ShowAsync(
			nowFavorite ? $"“{_detail.Name}” saved to favourites" : $"“{_detail.Name}” removed from favourites",
			nowFavorite ? NotificationKind.Success : NotificationKind.Info).ConfigureAwait(false);
	}

	[RelayCommand]
	private async Task ShareAsync()
	{
		if (_detail is null)
			return;

		var text = $"{_detail.Name} ({_detail.Category}, {_detail.Cuisine})\n\n" +
				   $"Ingredients: {string.Join(", ", _detail.Ingredients.Select(i => i.Name))}\n\n" +
				   _detail.Instructions +
				   (_detail.SourceUrl is null ? string.Empty : $"\n\nSource: {_detail.SourceUrl}");

		try
		{
			await Share.Default.RequestAsync(new ShareTextRequest { Title = "Share recipe", Text = text }).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is NotImplementedException or NotSupportedException or PlatformNotSupportedException)
		{
			// No share sheet on this host; fall back to the clipboard.
			await CopyAsync().ConfigureAwait(false);
			Logger.LogInformation(ex, "share unavailable, copied to clipboard instead");
		}
	}

	[RelayCommand]
	private async Task CopyAsync()
	{
		if (_detail is null)
			return;

		try
		{
			await Clipboard.Default.SetTextAsync(_detail.Instructions).ConfigureAwait(false);
			await Notifications.ShowAsync("Method copied to the clipboard", NotificationKind.Success).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is NotImplementedException or NotSupportedException or PlatformNotSupportedException)
		{
			Logger.LogWarning(ex, "clipboard unavailable on this host");
			await Notifications.ShowAsync("This device has no clipboard access", NotificationKind.Warning).ConfigureAwait(false);
		}
	}

	[RelayCommand]
	private async Task OpenYoutubeAsync()
	{
		if (string.IsNullOrWhiteSpace(YoutubeUrl))
			return;

		try
		{
			await Launcher.Default.OpenAsync(YoutubeUrl).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			Logger.LogWarning(ex, "could not open {Url}", YoutubeUrl);
			await Dialogs.AlertAsync("Cannot open the link", $"No browser could be launched for:\n{YoutubeUrl}").ConfigureAwait(false);
		}
	}

	[RelayCommand]
	private Task OpenRelatedAsync(MealCardViewModel? card) =>
		card is null ? Task.CompletedTask : _navigation.OpenMealAsync(card.Id, card.Name);

	[RelayCommand]
	private Task OpenCategoryAsync() =>
		_detail is null ? Task.CompletedTask : _navigation.OpenCatalogAsync(MealQuery.ByCategory(_detail.Category));

	private async Task ResolveHeroAsync(string url)
	{
		Ui(() => IsHeroLoading = true);

		if (string.IsNullOrWhiteSpace(url) || _settings.Current.OfflineMode)
		{
			Ui(() => IsHeroLoading = false);
			return;
		}

		try
		{
			var source = await _images.ResolveAsync(url, Lifetime).ConfigureAwait(false);
			Ui(() => HeroImage = source);
		}
		catch (OperationCanceledException)
		{
			// Popped mid-download.
		}
		finally
		{
			Ui(() => IsHeroLoading = false);
		}
	}

	private async Task LoadRelatedAsync(MealDetail detail)
	{
		var related = await _repository.GetMealsAsync(MealQuery.ByCategory(detail.Category), 0, 6, Lifetime).ConfigureAwait(false);
		if (!related.IsSuccess || related.Value.Items.Count == 0)
		{
			Ui(() => HasRelated = false);
			return;
		}

		var allowNetwork = _settings.Current.AllowNetwork;
		var cards = related.Value.Items
			.Where(m => m.Id != detail.Id)
			.Select(m => new MealCardViewModel(m, _images, _favorites, Dispatcher))
			.ToList();

		Ui(() =>
		{
			Related.ReplaceWith(cards);
			HasRelated = Related.Count > 0;
		});

		foreach (var card in cards)
			_ = card.LoadThumbnailAsync(allowNetwork, Lifetime);
	}

	protected override void OnActivated()
	{
		Messenger.Register<MealDetailViewModel, FavoriteToggledMessage>(this, static (vm, message) =>
		{
			if (message.MealId == vm.MealId)
				vm.IsFavorite = message.IsFavorite;
		});

		Messenger.Register<MealDetailViewModel, SettingsChangedMessage>(this, static (vm, message) =>
			vm.AnimationsEnabled = message.Settings.AnimationsEnabled);
	}

	protected override void OnDeactivated()
	{
		Messenger.UnregisterAll(this);
	}

	/// <summary>Also cancels each card's thumbnail load.</summary>
	public override void Shutdown()
	{
		foreach (var card in Related)
			card.CancelThumbnailLoad();

		base.Shutdown();
	}
}
