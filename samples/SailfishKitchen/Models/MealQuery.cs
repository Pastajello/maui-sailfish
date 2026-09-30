namespace SailfishKitchen.Models;

/// <summary>What the catalog list shows; each variant carries its own cache key and display name.</summary>
public abstract record MealQuery(string CacheKey, string DisplayName)
{
	public static MealQuery ByCategory(string category) => new CategoryQuery(category);

	public static MealQuery ByCuisine(string area, string country) => new CuisineQuery(area, country);

	public static MealQuery ByFirstLetter(char letter) => new LetterQuery(letter);

	public static MealQuery Search(string text) => new SearchQuery(text.Trim());

	/// <summary>The whole catalog, walked one letter at a time since TheMealDB has no offset paging.</summary>
	public static MealQuery WholeCatalog() => CatalogQuery.Instance;
}

public sealed record CategoryQuery(string Category)
	: MealQuery("cat:" + Category, Category);

public sealed record CuisineQuery(string Area, string Country)
	: MealQuery("area:" + Area, Country);

public sealed record LetterQuery(char Letter)
	: MealQuery("letter:" + char.ToLowerInvariant(Letter), $"Names starting with “{char.ToUpperInvariant(Letter)}”");

public sealed record SearchQuery(string Text)
	: MealQuery("search:" + Text.ToLowerInvariant(), Text.Length == 0 ? "Search" : $"“{Text}”");

public sealed record CatalogQuery() : MealQuery("catalog:all", "All recipes")
{
	public static readonly CatalogQuery Instance = new();
}
