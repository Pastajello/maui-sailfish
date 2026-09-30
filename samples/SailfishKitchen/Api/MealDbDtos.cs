using System.Text.Json.Serialization;

namespace SailfishKitchen.Api;

// Wire contract for TheMealDB. List endpoints return JSON null, not an empty array, when nothing matched.
// Deserialized only through the source-generated context, since the app publishes trimmed.

[JsonSourceGenerationOptions(
	PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
	DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(CategoryListResponse))]
[JsonSerializable(typeof(CategoryNameListResponse))]
[JsonSerializable(typeof(AreaListResponse))]
[JsonSerializable(typeof(MealSummaryListResponse))]
[JsonSerializable(typeof(MealListResponse))]
internal sealed partial class MealDbJsonContext : JsonSerializerContext;

public sealed class CategoryListResponse
{
	[JsonPropertyName("categories")]
	public List<CategoryDto>? Categories { get; set; }
}

public sealed class CategoryDto
{
	[JsonPropertyName("idCategory")] public string? IdCategory { get; set; }
	[JsonPropertyName("strCategory")] public string? StrCategory { get; set; }
	[JsonPropertyName("strCategoryThumb")] public string? StrCategoryThumb { get; set; }
	[JsonPropertyName("strCategoryDescription")] public string? StrCategoryDescription { get; set; }
}

public sealed class CategoryNameListResponse
{
	[JsonPropertyName("meals")] public List<CategoryNameDto>? Meals { get; set; }
}

public sealed class CategoryNameDto
{
	[JsonPropertyName("strCategory")] public string? StrCategory { get; set; }
}

public sealed class AreaListResponse
{
	[JsonPropertyName("meals")] public List<AreaDto>? Meals { get; set; }
}

public sealed class AreaDto
{
	[JsonPropertyName("strArea")] public string? StrArea { get; set; }
	[JsonPropertyName("strCountry")] public string? StrCountry { get; set; }
}

/// <summary>The short row shape from <c>filter.php</c>; category/area are only present when filtering by area.</summary>
public sealed class MealSummaryListResponse
{
	[JsonPropertyName("meals")] public List<MealSummaryDto>? Meals { get; set; }
}

public sealed class MealSummaryDto
{
	[JsonPropertyName("idMeal")] public string? IdMeal { get; set; }
	[JsonPropertyName("strMeal")] public string? StrMeal { get; set; }
	[JsonPropertyName("strMealThumb")] public string? StrMealThumb { get; set; }
	[JsonPropertyName("strCategory")] public string? StrCategory { get; set; }
	[JsonPropertyName("strArea")] public string? StrArea { get; set; }
	[JsonPropertyName("strCountry")] public string? StrCountry { get; set; }
}

/// <summary>The full record shape returned by <c>lookup.php</c>, <c>search.php</c> and <c>random.php</c>.</summary>
public sealed class MealListResponse
{
	[JsonPropertyName("meals")] public List<MealDto>? Meals { get; set; }
}

public sealed class MealDto
{
	[JsonPropertyName("idMeal")] public string? IdMeal { get; set; }
	[JsonPropertyName("strMeal")] public string? StrMeal { get; set; }
	[JsonPropertyName("strMealAlternate")] public string? StrMealAlternate { get; set; }
	[JsonPropertyName("strCategory")] public string? StrCategory { get; set; }
	[JsonPropertyName("strArea")] public string? StrArea { get; set; }
	[JsonPropertyName("strCountry")] public string? StrCountry { get; set; }
	[JsonPropertyName("strInstructions")] public string? StrInstructions { get; set; }
	[JsonPropertyName("strMealThumb")] public string? StrMealThumb { get; set; }
	[JsonPropertyName("strTags")] public string? StrTags { get; set; }
	[JsonPropertyName("strYoutube")] public string? StrYoutube { get; set; }
	[JsonPropertyName("strSource")] public string? StrSource { get; set; }
	[JsonPropertyName("strImageSource")] public string? StrImageSource { get; set; }
	[JsonPropertyName("dateModified")] public string? DateModified { get; set; }

	// The API models ingredients as 20 numbered pairs with "" for unused slots; see EnumerateIngredients.
	[JsonPropertyName("strIngredient1")] public string? Ingredient1 { get; set; }
	[JsonPropertyName("strMeasure1")] public string? Measure1 { get; set; }
	[JsonPropertyName("strIngredient2")] public string? Ingredient2 { get; set; }
	[JsonPropertyName("strMeasure2")] public string? Measure2 { get; set; }
	[JsonPropertyName("strIngredient3")] public string? Ingredient3 { get; set; }
	[JsonPropertyName("strMeasure3")] public string? Measure3 { get; set; }
	[JsonPropertyName("strIngredient4")] public string? Ingredient4 { get; set; }
	[JsonPropertyName("strMeasure4")] public string? Measure4 { get; set; }
	[JsonPropertyName("strIngredient5")] public string? Ingredient5 { get; set; }
	[JsonPropertyName("strMeasure5")] public string? Measure5 { get; set; }
	[JsonPropertyName("strIngredient6")] public string? Ingredient6 { get; set; }
	[JsonPropertyName("strMeasure6")] public string? Measure6 { get; set; }
	[JsonPropertyName("strIngredient7")] public string? Ingredient7 { get; set; }
	[JsonPropertyName("strMeasure7")] public string? Measure7 { get; set; }
	[JsonPropertyName("strIngredient8")] public string? Ingredient8 { get; set; }
	[JsonPropertyName("strMeasure8")] public string? Measure8 { get; set; }
	[JsonPropertyName("strIngredient9")] public string? Ingredient9 { get; set; }
	[JsonPropertyName("strMeasure9")] public string? Measure9 { get; set; }
	[JsonPropertyName("strIngredient10")] public string? Ingredient10 { get; set; }
	[JsonPropertyName("strMeasure10")] public string? Measure10 { get; set; }
	[JsonPropertyName("strIngredient11")] public string? Ingredient11 { get; set; }
	[JsonPropertyName("strMeasure11")] public string? Measure11 { get; set; }
	[JsonPropertyName("strIngredient12")] public string? Ingredient12 { get; set; }
	[JsonPropertyName("strMeasure12")] public string? Measure12 { get; set; }
	[JsonPropertyName("strIngredient13")] public string? Ingredient13 { get; set; }
	[JsonPropertyName("strMeasure13")] public string? Measure13 { get; set; }
	[JsonPropertyName("strIngredient14")] public string? Ingredient14 { get; set; }
	[JsonPropertyName("strMeasure14")] public string? Measure14 { get; set; }
	[JsonPropertyName("strIngredient15")] public string? Ingredient15 { get; set; }
	[JsonPropertyName("strMeasure15")] public string? Measure15 { get; set; }
	[JsonPropertyName("strIngredient16")] public string? Ingredient16 { get; set; }
	[JsonPropertyName("strMeasure16")] public string? Measure16 { get; set; }
	[JsonPropertyName("strIngredient17")] public string? Ingredient17 { get; set; }
	[JsonPropertyName("strMeasure17")] public string? Measure17 { get; set; }
	[JsonPropertyName("strIngredient18")] public string? Ingredient18 { get; set; }
	[JsonPropertyName("strMeasure18")] public string? Measure18 { get; set; }
	[JsonPropertyName("strIngredient19")] public string? Ingredient19 { get; set; }
	[JsonPropertyName("strMeasure19")] public string? Measure19 { get; set; }
	[JsonPropertyName("strIngredient20")] public string? Ingredient20 { get; set; }
	[JsonPropertyName("strMeasure20")] public string? Measure20 { get; set; }

	public IEnumerable<(string Ingredient, string Measure)> EnumerateIngredients()
	{
		foreach (var (ingredient, measure) in new[]
		{
			(Ingredient1, Measure1), (Ingredient2, Measure2), (Ingredient3, Measure3), (Ingredient4, Measure4),
			(Ingredient5, Measure5), (Ingredient6, Measure6), (Ingredient7, Measure7), (Ingredient8, Measure8),
			(Ingredient9, Measure9), (Ingredient10, Measure10), (Ingredient11, Measure11), (Ingredient12, Measure12),
			(Ingredient13, Measure13), (Ingredient14, Measure14), (Ingredient15, Measure15), (Ingredient16, Measure16),
			(Ingredient17, Measure17), (Ingredient18, Measure18), (Ingredient19, Measure19), (Ingredient20, Measure20),
		})
		{
			// Empty slots arrive as "" or as whitespace.
			if (string.IsNullOrWhiteSpace(ingredient))
				continue;

			yield return (ingredient.Trim(), string.IsNullOrWhiteSpace(measure) ? string.Empty : measure.Trim());
		}
	}
}
