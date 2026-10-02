using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using SailfishKitchen.Helpers;
using SailfishKitchen.Models;
using SailfishKitchen.Services;

namespace SailfishKitchen.ViewModels;

/// <summary>
/// One card in a recipe grid/list, owning its thumbnail and favourite state so the collection is never rebuilt.
/// Not a messenger recipient (300 rows would mean 300 subscriptions); the owning list calls <see cref="SyncFavorite"/>.
/// </summary>
public partial class MealCardViewModel : ObservableObject, IEquatable<MealCardViewModel>
{
	private readonly IImageCache _images;
	private readonly IFavoritesService _favorites;
	private readonly IDispatcher _dispatcher;
	private readonly ImageSource _placeholder;
	private CancellationTokenSource? _thumbCts;
	private bool _thumbSettled;   // the thumb (or the deliberate placeholder) is in; a cancelled load leaves it false

	public MealCardViewModel(Meal meal, IImageCache images, IFavoritesService favorites, IDispatcher dispatcher)
	{
		Meal = meal ?? throw new ArgumentNullException(nameof(meal));
		_images = images ?? throw new ArgumentNullException(nameof(images));
		_favorites = favorites ?? throw new ArgumentNullException(nameof(favorites));
		_dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
		_placeholder = images.Placeholder;

		// Must be non-null at template inflation: on this backend an Image created with Source=null
		// never picks up a later source.
		_thumbnail = _placeholder;

		_isFavorite = favorites.IsFavorite(meal.Id);
	}

	public Meal Meal { get; }

	public string Id => Meal.Id;

	public string Name => Meal.Name;

	public string Subtitle => (string.IsNullOrWhiteSpace(Meal.Category), string.IsNullOrWhiteSpace(Meal.Cuisine)) switch
	{
		(false, false) => $"{Meal.Category} · {Meal.Cuisine}",
		(false, true) => Meal.Category!,
		(true, false) => Meal.Cuisine!,
		_ => "Recipe",
	};

	public string ThumbnailUrl => Meal.ThumbnailUrl;

	/// <summary>Whether the thumb still has to load: never started, or cancelled (page covered, cell recycled).
	/// <see cref="Thumbnail"/> cannot tell, since it holds the placeholder from the start.</summary>
	public bool NeedsThumbnail => !_thumbSettled;

	/// <summary>Bundled art shown while the thumb is in flight, or if it never arrives.</summary>
	public ImageSource Placeholder => _placeholder;

	[ObservableProperty]
	private ImageSource? _thumbnail;

	[ObservableProperty]
	private bool _isImageLoading = true;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(FavoriteGlyph))]
	private bool _isFavorite;

	/// <summary>A text glyph because this backend has no ImageButton adapter.</summary>
	public string FavoriteGlyph => IsFavorite ? "★" : "☆";

	/// <summary>
	/// Resolves the thumb from the disk cache or network; a new call retires the previous token so a fling
	/// does not pile up downloads.
	/// </summary>
	public async Task LoadThumbnailAsync(bool allowNetwork, CancellationToken external = default)
	{
		if (_thumbSettled)
			return;

		_thumbCts?.Cancel();
		using var cts = CancellationTokenSource.CreateLinkedTokenSource(external);
		_thumbCts = cts;

		try
		{
			// ResolveAsync decides: online it returns the remote URI (a file:// source paints nothing on this
			// backend while online) and caches a copy; offline it returns the cached file or placeholder.
			if (string.IsNullOrWhiteSpace(ThumbnailUrl)
				|| (!allowNetwork && !_images.HasCached(ThumbnailUrl)))
			{
				_thumbSettled = true;
				_dispatcher.RunOnUi(() => Thumbnail = _placeholder);
				return;
			}

			var resolved = await _images.ResolveAsync(ThumbnailUrl, cts.Token).ConfigureAwait(false);
			_thumbSettled = true;
			_dispatcher.RunOnUi(() => Thumbnail = resolved);
		}
		catch (OperationCanceledException)
		{
			// Cell scrolled away or page closed; keep the placeholder.
		}
		finally
		{
			_dispatcher.RunOnUi(() => IsImageLoading = false);
			if (ReferenceEquals(_thumbCts, cts))
				_thumbCts = null;
		}
	}

	public void CancelThumbnailLoad()
	{
		_thumbCts?.Cancel();
		_thumbCts = null;
	}

	[RelayCommand]
	private void ToggleFavorite() => IsFavorite = _favorites.Toggle(Meal);

	public void SyncFavorite(bool isFavorite) => IsFavorite = isFavorite;

	public bool Equals(MealCardViewModel? other) => other is not null && other.Id == Id;

	public override bool Equals(object? obj) => Equals(obj as MealCardViewModel);

	public override int GetHashCode() => Id.GetHashCode();
}
