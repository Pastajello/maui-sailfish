namespace SailfishKitchen.Api;

/// <summary>
/// TheMealDB options, bound from the <c>MealDb</c> section. The public test key "1" is rate-limited and non-commercial.
/// </summary>
public sealed class MealDbOptions
{
	public const string SectionName = "MealDb";

	/// <summary>TheMealDB puts the API key in the path, not a header.</summary>
	public string BaseAddress { get; set; } = "https://www.themealdb.com/api/json/v1/";

	public string ApiKey { get; set; } = "1";

	public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(20);

	/// <summary>The free tier rate-limits per identity, so identify the app.</summary>
	public string UserAgent { get; set; } = "SailfishKitchen/1.0 (MAUI Sailfish OS sample; +https://github.com/dotnet/maui-labs)";

	/// <summary>Cap on concurrent thumb downloads, so a fling does not open dozens of sockets.</summary>
	public int MaxConcurrentImageDownloads { get; set; } = 4;
}

/// <summary>Thrown when TheMealDB answers with a non-success status or a body we cannot map.</summary>
public sealed class MealDbApiException : Exception
{
	public MealDbApiException(string message) : base(message)
	{
	}

	public MealDbApiException(string message, Exception inner) : base(message, inner)
	{
	}

	public int? StatusCode { get; init; }

	public string? RequestUri { get; init; }
}
