using SailfishKitchen.Models;

namespace SailfishKitchen.Messaging;

// Cross-view-model messages, sent through WeakReferenceMessenger so a popped page never leaks its view model.

/// <summary>A recipe was added to or removed from favourites.</summary>
public sealed record FavoriteToggledMessage(string MealId, bool IsFavorite, string Name)
{
	/// <summary>Sent by <see cref="Services.FavoritesService"/> after the store is saved.</summary>
	public static FavoriteToggledMessage Added(string id, string name) => new(id, true, name);

	public static FavoriteToggledMessage Removed(string id, string name) => new(id, false, name);
}

/// <summary>The whole favourites collection changed (clear, import, migration).</summary>
public sealed record FavoritesResetMessage(int Count);

/// <summary>Settings were persisted; views re-read layout, animation and paging.</summary>
public sealed record SettingsChangedMessage(AppSettings Settings, string? ChangedProperty);

/// <summary>A catalog page finished loading a batch, for a status bar that owns no view model.</summary>
public sealed record LoadingProgressMessage(string Source, bool IsBusy, int LoadedCount, int? TotalCount, string? StatusText);

/// <summary>Connectivity changed.</summary>
public sealed record ConnectivityChangedMessage(bool IsOnline);

/// <summary>Request navigation to a recipe without holding a reference to the navigator.</summary>
public sealed record OpenMealMessage(string MealId, string? Title);

/// <summary>An operation failed and should be surfaced to the user, not just logged.</summary>
public sealed record ErrorRaisedMessage(string Source, string Message, bool IsRetryable, Exception? Exception);

/// <summary>
/// Published for every notification, so the banner shows feedback even where the toast handler is unavailable.
/// </summary>
public sealed record NotificationMessage(string Text, Services.NotificationKind Kind);
