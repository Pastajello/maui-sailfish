namespace SailfishKitchen.Models;

/// <summary>One ingredient line: the name plus the measure the API pairs with it.</summary>
public sealed record Ingredient(string Name, string Measure)
{
	public string Display => string.IsNullOrWhiteSpace(Measure) ? Name : $"{Measure} · {Name}";
}
