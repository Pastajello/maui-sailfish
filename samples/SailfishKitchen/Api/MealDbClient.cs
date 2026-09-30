using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SailfishKitchen.Models;

namespace SailfishKitchen.Api;

/// <summary>
/// Typed <see cref="HttpClient"/> for TheMealDB; the resilience pipeline is composed by <c>AddHttpClient</c> in MauiProgram.
/// </summary>
public sealed class MealDbClient : IMealDbClient
{
	private readonly HttpClient _http;
	private readonly MealDbOptions _options;
	private readonly ILogger<MealDbClient> _logger;

	public MealDbClient(HttpClient http, IOptions<MealDbOptions> options, ILogger<MealDbClient> logger)
	{
		_http = http;
		_options = options.Value;
		_logger = logger;

		// The UA is set per request because pooled typed clients share DefaultRequestHeaders.
		_http.Timeout = _options.RequestTimeout;
	}

	public async Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken ct = default)
	{
		var response = await GetJsonAsync("categories.php", null, MealDbJsonContext.Default.CategoryListResponse, ct).ConfigureAwait(false);
		var source = response?.Categories;
		if (source is null)
			return Array.Empty<Category>();

		var result = new List<Category>(source.Count);
		foreach (var dto in source)
		{
			var name = dto?.StrCategory;
			if (string.IsNullOrWhiteSpace(name))
				continue;

			result.Add(new Category(
				dto!.IdCategory ?? name,
				name,
				dto.StrCategoryThumb ?? string.Empty,
				dto.StrCategoryDescription ?? string.Empty));
		}

		_logger.LogDebug("categories: {Count} parsed", result.Count);
		return result;
	}

	public async Task<IReadOnlyList<string>> GetCategoryNamesAsync(CancellationToken ct = default)
	{
		var response = await GetJsonAsync("list.php", new() { ["c"] = "list" }, MealDbJsonContext.Default.CategoryNameListResponse, ct).ConfigureAwait(false);
		var names = response?.Meals?
			.Where(m => !string.IsNullOrWhiteSpace(m?.StrCategory))
			.Select(m => m!.StrCategory!)
			.ToList();
		return names ?? (IReadOnlyList<string>)Array.Empty<string>();
	}

	public async Task<IReadOnlyList<(string Area, string Country)>> GetAreasAsync(CancellationToken ct = default)
	{
		var response = await GetJsonAsync("list.php", new() { ["a"] = "list" }, MealDbJsonContext.Default.AreaListResponse, ct).ConfigureAwait(false);
		var areas = response?.Meals?
			.Where(m => !string.IsNullOrWhiteSpace(m?.StrArea))
			.Select(m => (m!.StrArea!, m.StrCountry ?? m.StrArea!))
			.ToList();
		return areas ?? (IReadOnlyList<(string, string)>)Array.Empty<(string, string)>();
	}

	public Task<IReadOnlyList<Meal>> GetMealsByCategoryAsync(string category, CancellationToken ct = default) =>
		FilterAsync("c", category, ct);

	public Task<IReadOnlyList<Meal>> GetMealsByAreaAsync(string area, CancellationToken ct = default) =>
		FilterAsync("a", area, ct);

	public Task<IReadOnlyList<Meal>> GetMealsByFirstLetterAsync(char firstLetter, CancellationToken ct = default)
	{
		if (!char.IsAsciiLetter(firstLetter))
			throw new ArgumentOutOfRangeException(nameof(firstLetter), firstLetter, "TheMealDB only pages the catalog by ASCII letter.");

		return SearchAsync("f", char.ToLowerInvariant(firstLetter).ToString(), ct);
	}

	public Task<IReadOnlyList<Meal>> SearchMealsAsync(string query, CancellationToken ct = default)
	{
		if (string.IsNullOrWhiteSpace(query))
			return Task.FromResult<IReadOnlyList<Meal>>(Array.Empty<Meal>());

		return SearchAsync("s", query.Trim(), ct);
	}

	public async Task<MealDetail> GetMealDetailAsync(string id, CancellationToken ct = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(id);

		var response = await GetJsonAsync("lookup.php", new() { ["i"] = id }, MealDbJsonContext.Default.MealListResponse, ct).ConfigureAwait(false);
		var dto = response?.Meals?.FirstOrDefault();
		return dto is null
			? throw new MealDbApiException($"TheMealDB returned no recipe for id '{id}'.") { RequestUri = $"lookup.php?i={id}" }
			: dto.ToDetail();
	}

	public async Task<MealDetail> GetRandomMealAsync(CancellationToken ct = default)
	{
		var response = await GetJsonAsync("random.php", null, MealDbJsonContext.Default.MealListResponse, ct).ConfigureAwait(false);
		var dto = response?.Meals?.FirstOrDefault();
		return dto is null
			? throw new MealDbApiException("TheMealDB returned no random recipe.") { RequestUri = "random.php" }
			: dto.ToDetail();
	}

	public async Task<bool> DownloadImageAsync(Uri url, string destination, CancellationToken ct = default)
	{
		try
		{
			using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
			if (!response.IsSuccessStatusCode)
			{
				_logger.LogWarning("thumb {Url} -> HTTP {Status}", url, (int)response.StatusCode);
				return false;
			}

			// Write to a temp file and move into place so a cancelled download never leaves a half-written cache entry.
			var temp = destination + ".part";
			await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
			await using (var target = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
			{
				await source.CopyToAsync(target, ct).ConfigureAwait(false);
			}

			File.Move(temp, destination, overwrite: true);
			return true;
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			// A missing thumb degrades the tile; it must not take down the list.
			_logger.LogWarning(ex, "thumb download failed for {Url}", url);
			TryDelete(destination + ".part");
			return false;
		}
	}

	/// <summary><c>filter.php</c> returns the short row shape.</summary>
	private async Task<IReadOnlyList<Meal>> FilterAsync(string key, string value, CancellationToken ct)
	{
		var response = await GetJsonAsync("filter.php", new() { [key] = value }, MealDbJsonContext.Default.MealSummaryListResponse, ct).ConfigureAwait(false);
		var source = response?.Meals;
		if (source is null)
			return Array.Empty<Meal>();

		var result = new List<Meal>(source.Count);
		foreach (var dto in source)
		{
			if (string.IsNullOrWhiteSpace(dto?.IdMeal) || string.IsNullOrWhiteSpace(dto!.StrMeal))
				continue;

			result.Add(new Meal(
				dto.IdMeal,
				dto.StrMeal,
				dto.StrMealThumb ?? string.Empty,
				dto.StrCategory ?? (key == "c" ? value : null),
				dto.StrArea ?? (key == "a" ? value : null)));
		}

		return result;
	}

	/// <summary><c>search.php</c> returns the full record shape, projected down to the same <see cref="Meal"/>.</summary>
	private async Task<IReadOnlyList<Meal>> SearchAsync(string key, string value, CancellationToken ct)
	{
		var response = await GetJsonAsync("search.php", new() { [key] = value }, MealDbJsonContext.Default.MealListResponse, ct).ConfigureAwait(false);
		var source = response?.Meals;
		if (source is null)
			return Array.Empty<Meal>();

		var result = new List<Meal>(source.Count);
		foreach (var dto in source)
		{
			if (string.IsNullOrWhiteSpace(dto?.IdMeal) || string.IsNullOrWhiteSpace(dto!.StrMeal))
				continue;

			result.Add(new Meal(dto.IdMeal, dto.StrMeal, dto.StrMealThumb ?? string.Empty, dto.StrCategory, dto.StrArea));
		}

		return result;
	}

	private async Task<T?> GetJsonAsync<T>(
		string endpoint,
		Dictionary<string, string>? query,
		JsonTypeInfo<T> typeInfo,
		CancellationToken ct)
	{
		var url = BuildUri(endpoint, query);
		_logger.LogDebug("GET {Url}", url);

		HttpResponseMessage response;
		try
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, url);
			request.Headers.TryAddWithoutValidation("User-Agent", _options.UserAgent);
			response = await _http.SendAsync(request, ct).ConfigureAwait(false);
		}
		catch (HttpRequestException ex)
		{
			throw new MealDbApiException($"Network failure reaching {endpoint}: {ex.Message}", ex) { RequestUri = url.ToString() };
		}

		using (response)
		{
			if (!response.IsSuccessStatusCode)
			{
				throw new MealDbApiException($"TheMealDB returned HTTP {(int)response.StatusCode} for {endpoint}.")
				{
					StatusCode = (int)response.StatusCode,
					RequestUri = url.ToString(),
				};
			}

			await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
			try
			{
				return await JsonSerializer.DeserializeAsync(stream, typeInfo, ct).ConfigureAwait(false);
			}
			catch (JsonException ex)
			{
				throw new MealDbApiException($"Unexpected JSON shape from {endpoint}: {ex.Message}", ex) { RequestUri = url.ToString() };
			}
		}
	}

	private Uri BuildUri(string endpoint, Dictionary<string, string>? query)
	{
		var basePath = _options.BaseAddress.TrimEnd('/');
		var text = $"{basePath}/{Uri.EscapeDataString(_options.ApiKey)}/{endpoint}";
		if (query is { Count: > 0 })
		{
			text += "?" + string.Join('&', query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
		}

		return new Uri(text);
	}

	private static void TryDelete(string path)
	{
		try { if (File.Exists(path)) File.Delete(path); }
		catch (IOException) { /* a stale .part is harmless; the next write overwrites it */ }
		catch (UnauthorizedAccessException) { }
	}
}

internal static class MealDtoMapping
{
	public static MealDetail ToDetail(this MealDto dto)
	{
		var ingredients = dto.EnumerateIngredients()
			.Select(pair => new Ingredient(pair.Ingredient, pair.Measure))
			.ToList();

		var tags = string.IsNullOrWhiteSpace(dto.StrTags)
			? new List<string>()
			: dto.StrTags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

		return new MealDetail(
			dto.IdMeal ?? string.Empty,
			dto.StrMeal ?? "(untitled)",
			dto.StrCategory ?? "Uncategorised",
			dto.StrArea ?? dto.StrCountry ?? "Unknown",
			dto.StrCountry,
			dto.StrMealThumb ?? string.Empty,
			dto.StrInstructions ?? string.Empty,
			ingredients,
			tags,
			NullIfEmpty(dto.StrYoutube),
			NullIfEmpty(dto.StrSource));
	}

	private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
