namespace SailfishKitchen.Models;

/// <summary>A TheMealDB category ("Beef", "Dessert", …) used as the home grid tile.</summary>
public sealed record Category(
	string Id,
	string Name,
	string ThumbnailUrl,
	string Description)
{
	/// <summary>Short one-liner for the tile subtitle (the API descriptions are long).</summary>
	public string ShortDescription => Description.Length <= 110
		? Description
		: Description[..Description.LastIndexOf(' ', 110)] + "…";
}
