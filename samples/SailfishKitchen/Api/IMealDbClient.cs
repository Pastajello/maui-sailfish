using SailfishKitchen.Models;

namespace SailfishKitchen.Api;

/// <summary>The seam between the app and TheMealDB, so the repository and view models can run against a fake.</summary>
public interface IMealDbClient
{
	Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken ct = default);

	Task<IReadOnlyList<string>> GetCategoryNamesAsync(CancellationToken ct = default);

	Task<IReadOnlyList<(string Area, string Country)>> GetAreasAsync(CancellationToken ct = default);

	Task<IReadOnlyList<Meal>> GetMealsByCategoryAsync(string category, CancellationToken ct = default);

	Task<IReadOnlyList<Meal>> GetMealsByAreaAsync(string area, CancellationToken ct = default);

	/// <summary>Every recipe whose name starts with <paramref name="firstLetter"/>; the paging unit for "browse all".</summary>
	Task<IReadOnlyList<Meal>> GetMealsByFirstLetterAsync(char firstLetter, CancellationToken ct = default);

	Task<IReadOnlyList<Meal>> SearchMealsAsync(string query, CancellationToken ct = default);

	Task<MealDetail> GetMealDetailAsync(string id, CancellationToken ct = default);

	Task<MealDetail> GetRandomMealAsync(CancellationToken ct = default);

	/// <summary>Downloads a thumb into <paramref name="destination"/>; returns false on a non-fatal failure.</summary>
	Task<bool> DownloadImageAsync(Uri url, string destination, CancellationToken ct = default);
}
