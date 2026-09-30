namespace SailfishKitchen.Models;

/// <summary>A recipe as shown in a list; instructions and ingredients live in <see cref="MealDetail"/>.</summary>
public sealed record Meal(
	string Id,
	string Name,
	string ThumbnailUrl,
	string? Category = null,
	string? Cuisine = null)
{
	public static readonly IEqualityComparer<Meal> ById = new MealIdComparer();

	private sealed class MealIdComparer : IEqualityComparer<Meal>
	{
		public bool Equals(Meal? x, Meal? y) =>
			ReferenceEquals(x, y) || (x is not null && y is not null && x.Id == y.Id);

		public int GetHashCode(Meal obj) => obj.Id.GetHashCode();
	}
}
