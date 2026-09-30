namespace SailfishKitchen.Models;

/// <summary>A fully loaded recipe from <c>lookup.php?i=</c>.</summary>
public sealed record MealDetail(
	string Id,
	string Name,
	string Category,
	string Cuisine,
	string? Country,
	string ThumbnailUrl,
	string Instructions,
	IReadOnlyList<Ingredient> Ingredients,
	IReadOnlyList<string> Tags,
	string? YoutubeUrl,
	string? SourceUrl)
{
	/// <summary>Instructions arrive as one CRLF-separated blob; split into numbered steps.</summary>
	public IReadOnlyList<string> Steps { get; } = SplitSteps(Instructions);

	public string IngredientsSummary => Ingredients.Count switch
	{
		0 => "no ingredients listed",
		1 => Ingredients[0].Name,
		_ => $"{Ingredients.Count} ingredients · {Ingredients[0].Name}, {Ingredients[1].Name}" +
			 (Ingredients.Count > 2 ? ", …" : string.Empty),
	};

	/// <summary>Projects back down to the list shape, for favourites and related rows.</summary>
	public Meal ToSummary() => new(Id, Name, ThumbnailUrl, Category, Cuisine);

	private static IReadOnlyList<string> SplitSteps(string instructions)
	{
		if (string.IsNullOrWhiteSpace(instructions))
			return Array.Empty<string>();

		var steps = new List<string>();
		foreach (var raw in instructions.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
		{
			var line = raw.Trim();
			if (line.Length == 0)
				continue;
			steps.Add(line);
		}

		return steps;
	}
}
