using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using SailfishKitchen.Messaging;
using SailfishKitchen.Models;

namespace SailfishKitchen.Services;

/// <summary>
/// Persisted favourites as a guarded list handed out as snapshots; view models build their own observable
/// collections so mutations stay on the owning thread.
/// </summary>
public interface IFavoritesService
{
	int Count { get; }

	/// <summary>Snapshot ordered newest-first.</summary>
	IReadOnlyList<FavoriteMeal> Snapshot();

	bool IsFavorite(string mealId);

	FavoriteMeal? Find(string mealId);

	/// <summary>Adds or removes <paramref name="meal"/>; returns the resulting state.</summary>
	bool Toggle(Meal meal);

	bool Add(Meal meal);

	bool Remove(string mealId);

	void Clear();
}

public sealed class FavoritesService : IFavoritesService
{
	internal const string FileName = "favorites.json";

	private readonly IJsonFileStore _store;
	private readonly IMessenger _messenger;
	private readonly ILogger<FavoritesService> _logger;
	private readonly object _gate = new();
	private readonly Dictionary<string, FavoriteMeal> _byId = new(StringComparer.Ordinal);
	private bool _loaded;

	public FavoritesService(IJsonFileStore store, IMessenger messenger, ILogger<FavoritesService> logger)
	{
		_store = store;
		_messenger = messenger;
		_logger = logger;
	}

	public int Count
	{
		get { lock (_gate) { EnsureLoaded(); return _byId.Count; } }
	}

	public IReadOnlyList<FavoriteMeal> Snapshot()
	{
		lock (_gate)
		{
			EnsureLoaded();
			return _byId.Values
				.OrderByDescending(f => f.AddedAtUtc)
				.ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
				.ToList();
		}
	}

	public bool IsFavorite(string mealId)
	{
		lock (_gate) { EnsureLoaded(); return _byId.ContainsKey(mealId); }
	}

	public FavoriteMeal? Find(string mealId)
	{
		lock (_gate)
		{
			EnsureLoaded();
			return _byId.TryGetValue(mealId, out var found) ? found : null;
		}
	}

	public bool Toggle(Meal meal)
	{
		ArgumentNullException.ThrowIfNull(meal);

		// Returns the state after the toggle, which the star button binds to.
		if (Remove(meal.Id))
			return false;

		Add(meal);
		return true;
	}

	public bool Add(Meal meal)
	{
		ArgumentNullException.ThrowIfNull(meal);

		FavoriteMeal entry;
		lock (_gate)
		{
			EnsureLoaded();
			if (_byId.ContainsKey(meal.Id))
				return true;

			entry = new FavoriteMeal(
				meal.Id,
				meal.Name,
				meal.ThumbnailUrl,
				meal.Category ?? string.Empty,
				meal.Cuisine ?? string.Empty,
				DateTimeOffset.UtcNow);

			_byId[meal.Id] = entry;
			Persist();
		}

		_logger.LogInformation("favourites: + {Name} ({Id}), now {Count}", entry.Name, entry.Id, Count);
		_messenger.Send(FavoriteToggledMessage.Added(entry.Id, entry.Name));
		return true;
	}

	public bool Remove(string mealId)
	{
		string? name;
		lock (_gate)
		{
			EnsureLoaded();
			if (!_byId.TryGetValue(mealId, out var existing))
				return false;

			_byId.Remove(mealId);
			name = existing.Name;
			Persist();
		}

		_logger.LogInformation("favourites: - {Name} ({Id}), now {Count}", name, mealId, Count);
		_messenger.Send(FavoriteToggledMessage.Removed(mealId, name));
		return true;
	}

	public void Clear()
	{
		int removed;
		lock (_gate)
		{
			EnsureLoaded();
			removed = _byId.Count;
			_byId.Clear();
			Persist();
		}

		if (removed == 0)
			return;

		_logger.LogInformation("favourites: cleared {Count}", removed);
		_messenger.Send(new FavoritesResetMessage(0));
	}

	/// <summary>Reads the store on first touch; a missing or corrupt file yields an empty list.</summary>
	private void EnsureLoaded()
	{
		if (_loaded)
			return;

		_loaded = true;

		var file = _store.Load<FavoritesFile>(FileName);
		if (file?.Meals is null)
		{
			_logger.LogInformation("favourites: empty store");
			return;
		}

		if (file.Version != FavoritesFile.CurrentVersion)
		{
			// Unknown schema: leave the file on disk and start clean rather than guess at a migration.
			_logger.LogWarning("favourites: file version {Version} != {Current}; starting empty and preserving the old file",
				file.Version, FavoritesFile.CurrentVersion);
			return;
		}

		foreach (var meal in file.Meals)
		{
			if (string.IsNullOrWhiteSpace(meal?.Id))
				continue;

			_byId[meal.Id] = meal;
		}

		_logger.LogInformation("favourites: loaded {Count}", _byId.Count);
	}

	/// <summary>Caller holds <see cref="_gate"/>.</summary>
	private void Persist()
	{
		try
		{
			_store.Save(FileName, new FavoritesFile
			{
				Version = FavoritesFile.CurrentVersion,
				Meals = _byId.Values.OrderByDescending(f => f.AddedAtUtc).ToList(),
			});
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			_logger.LogError(ex, "favourites: could not persist");
			_messenger.Send(new ErrorRaisedMessage("favourites", "Could not save favourites to disk.", IsRetryable: true, ex));
		}
	}
}
