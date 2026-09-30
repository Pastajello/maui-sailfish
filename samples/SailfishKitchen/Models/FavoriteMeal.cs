namespace SailfishKitchen.Models;

/// <summary>A persisted favourite, storing name and thumbnail URL so the favourites page needs no network.</summary>
public sealed record FavoriteMeal(
	string Id,
	string Name,
	string ThumbnailUrl,
	string Category,
	string Cuisine,
	DateTimeOffset AddedAtUtc);
