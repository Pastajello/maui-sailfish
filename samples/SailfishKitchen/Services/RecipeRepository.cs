using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SailfishKitchen.Api;
using SailfishKitchen.Models;

namespace SailfishKitchen.Services;

/// <summary>
/// The view models' recipe source, adding request coalescing, paging over an unpaged API and
/// UI-actionable failure classification on top of the raw client.
/// </summary>
public interface IRecipeRepository
{
	Task<Result<IReadOnlyList<Category>>> GetCategoriesAsync(CancellationToken ct = default);

	Task<Result<IReadOnlyList<(string Area, string Country)>>> GetCuisinesAsync(CancellationToken ct = default);

	/// <summary>Page <c>[skip, skip+take)</c> of <paramref name="query"/>, fetching more only when needed.</summary>
	Task<Result<PagedList<Meal>>> GetMealsAsync(MealQuery query, int skip, int take, CancellationToken ct = default);

	Task<Result<MealDetail>> GetMealAsync(string id, CancellationToken ct = default);

	Task<Result<MealDetail>> GetRandomMealAsync(CancellationToken ct = default);

	/// <summary>Several random recipes for the home page suggestion strip.</summary>
	Task<Result<IReadOnlyList<MealDetail>>> GetSuggestionsAsync(int count, CancellationToken ct = default);

	/// <summary>Drops the memoized result for a query so the next read goes to the network.</summary>
	void Invalidate(MealQuery query);

	/// <summary>Drops every memoized result and resets the catalog walk.</summary>
	void InvalidateAll();
}

public sealed class RecipeRepository : IRecipeRepository
{
	private const string Alphabet = "abcdefghijklmnopqrstuvwxyz";

	private readonly IMealDbClient _client;
	private readonly ILogger<RecipeRepository> _logger;

	// Lazy<> so a burst of identical requests issues exactly one HTTP call.
	private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<Meal>>>> _mealMemo = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, Lazy<Task<MealDetail>>> _detailMemo = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<Category>>>> _categoryMemo = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<(string, string)>>>> _cuisineMemo = new(StringComparer.Ordinal);

	// The catalog walk consumes letters in order and de-duplicates across them, so it is not a memo entry.
	private readonly SemaphoreSlim _catalogGate = new(1, 1);
	private readonly List<Meal> _catalogBuffer = [];
	private readonly HashSet<string> _catalogSeen = new(StringComparer.Ordinal);
	private int _catalogNextLetter;
	private bool _catalogExhausted;

	public RecipeRepository(IMealDbClient client, ILogger<RecipeRepository> logger)
	{
		_client = client;
		_logger = logger;
	}

	public Task<Result<IReadOnlyList<Category>>> GetCategoriesAsync(CancellationToken ct = default) =>
		GuardedAsync(
			"categories",
			_categoryMemo,
			_ => _client.GetCategoriesAsync(CancellationToken.None),
			ct);

	public Task<Result<IReadOnlyList<(string Area, string Country)>>> GetCuisinesAsync(CancellationToken ct = default) =>
		GuardedAsync(
			"cuisines",
			_cuisineMemo,
			_ => _client.GetAreasAsync(CancellationToken.None),
			ct);

	public async Task<Result<PagedList<Meal>>> GetMealsAsync(MealQuery query, int skip, int take, CancellationToken ct = default)
	{
		ArgumentNullException.ThrowIfNull(query);
		if (skip < 0) skip = 0;
		if (take <= 0) take = AppSettings.DefaultPageSize;

		// TheMealDB has no offset paging: the catalog streams by letter, other queries are fetched once and sliced.
		if (query is CatalogQuery)
			return await GetCatalogPageAsync(skip, take, ct).ConfigureAwait(false);

		var full = await FetchMealsAsync(query, ct).ConfigureAwait(false);
		if (!full.IsSuccess)
			return Result<PagedList<Meal>>.Fail(full.Failure!);

		var all = full.Value;
		if (skip >= all.Count)
			return Result<PagedList<Meal>>.Success(new PagedList<Meal>(Array.Empty<Meal>(), skip, take, all.Count));

		var slice = all.Skip(skip).Take(take).ToList();
		return Result<PagedList<Meal>>.Success(new PagedList<Meal>(slice, skip, take, all.Count));
	}

	public async Task<Result<MealDetail>> GetMealAsync(string id, CancellationToken ct = default)
	{
		if (string.IsNullOrWhiteSpace(id))
			return Result<MealDetail>.Fail(ResultFailure.NotFound("No recipe id was supplied."));

		var result = await GuardedAsync(
			id,
			_detailMemo,
			_ => _client.GetMealDetailAsync(id, CancellationToken.None),
			ct).ConfigureAwait(false);

		if (!result.IsSuccess && result.Failure!.Kind is FailureKind.NotFound)
			_detailMemo.TryRemove(id, out _);

		return result;
	}

	public async Task<Result<MealDetail>> GetRandomMealAsync(CancellationToken ct = default)
	{
		try
		{
			var detail = await _client.GetRandomMealAsync(ct).ConfigureAwait(false);
			return Result<MealDetail>.Success(detail);
		}
		catch (Exception ex) when (IsCancellation(ex, ct))
		{
			return Result<MealDetail>.Fail(ResultFailure.Cancelled());
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "random recipe failed");
			return Result<MealDetail>.Fail(Classify(ex));
		}
	}

	public async Task<Result<IReadOnlyList<MealDetail>>> GetSuggestionsAsync(int count, CancellationToken ct = default)
	{
		count = Math.Clamp(count, 1, 12);
		var results = new List<MealDetail>(count);
		var seen = new HashSet<string>(StringComparer.Ordinal);

		// Serial on purpose: the free tier is rate-limited per identity.
		for (var i = 0; i < count && results.Count < count; i++)
		{
			var attempt = await GetRandomMealAsync(ct).ConfigureAwait(false);
			if (!attempt.IsSuccess)
			{
				// A partial strip beats none; only a first failure aborts.
				if (results.Count == 0)
					return Result<IReadOnlyList<MealDetail>>.Fail(attempt.Failure!);
				break;
			}

			if (seen.Add(attempt.Value.Id))
				results.Add(attempt.Value);
		}

		return Result<IReadOnlyList<MealDetail>>.Success(results);
	}

	public void Invalidate(MealQuery query)
	{
		ArgumentNullException.ThrowIfNull(query);
		_mealMemo.TryRemove(query.CacheKey, out _);
		if (query is CatalogQuery)
			ResetCatalogWalk();

		_logger.LogDebug("repository: invalidated {Key}", query.CacheKey);
	}

	public void InvalidateAll()
	{
		_mealMemo.Clear();
		_detailMemo.Clear();
		_categoryMemo.Clear();
		_cuisineMemo.Clear();
		ResetCatalogWalk();
		_logger.LogInformation("repository: all memoized results dropped");
	}

	private void ResetCatalogWalk()
	{
		_catalogGate.Wait();
		try
		{
			_catalogBuffer.Clear();
			_catalogSeen.Clear();
			_catalogNextLetter = 0;
			_catalogExhausted = false;
		}
		finally
		{
			_catalogGate.Release();
		}
	}

	/// <summary>
	/// Extends the buffer one letter per HTTP call until it covers the window, holding the gate for the whole
	/// walk so concurrent pages cannot fetch the same letter.
	/// </summary>
	private async Task<Result<PagedList<Meal>>> GetCatalogPageAsync(int skip, int take, CancellationToken ct)
	{
		await _catalogGate.WaitAsync(ct).ConfigureAwait(false);
		try
		{
			while (!_catalogExhausted && _catalogBuffer.Count < skip + take)
			{
				if (_catalogNextLetter >= Alphabet.Length)
				{
					_catalogExhausted = true;
					break;
				}

				var letter = Alphabet[_catalogNextLetter];
				IReadOnlyList<Meal> batch;
				try
				{
					// The walk is shared state, so one caller leaving must not abort it; the caller's ct is honoured at the edges.
					batch = await _client.GetMealsByFirstLetterAsync(letter, CancellationToken.None).ConfigureAwait(false);
				}
				catch (Exception ex) when (IsCancellation(ex, ct))
				{
					return Result<PagedList<Meal>>.Fail(ResultFailure.Cancelled());
				}
				catch (Exception ex)
				{
					_logger.LogWarning(ex, "catalog walk failed at letter '{Letter}'", letter);
					return Result<PagedList<Meal>>.Fail(Classify(ex));
				}

				foreach (var meal in batch)
				{
					if (_catalogSeen.Add(meal.Id))
						_catalogBuffer.Add(meal);
				}

				_catalogNextLetter++;
				ct.ThrowIfCancellationRequested();
				_logger.LogDebug("catalog: letter '{Letter}' +{Batch} = {Total}", letter, batch.Count, _catalogBuffer.Count);
			}

			if (skip >= _catalogBuffer.Count)
			{
				return Result<PagedList<Meal>>.Success(new PagedList<Meal>(
					Array.Empty<Meal>(), skip, take, _catalogExhausted ? _catalogBuffer.Count : null));
			}

			var slice = _catalogBuffer.Skip(skip).Take(take).ToList();
			return Result<PagedList<Meal>>.Success(new PagedList<Meal>(
				slice, skip, take, _catalogExhausted ? _catalogBuffer.Count : null));
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested)
		{
			return Result<PagedList<Meal>>.Fail(ResultFailure.Cancelled());
		}
		finally
		{
			_catalogGate.Release();
		}
	}

	private Task<Result<IReadOnlyList<Meal>>> FetchMealsAsync(MealQuery query, CancellationToken ct) =>
		GuardedAsync(query.CacheKey, _mealMemo, key => FetchMealsCoreAsync(query), ct);

	private Task<IReadOnlyList<Meal>> FetchMealsCoreAsync(MealQuery query) => query switch
	{
		CategoryQuery c => _client.GetMealsByCategoryAsync(c.Category, CancellationToken.None),
		CuisineQuery a => _client.GetMealsByAreaAsync(a.Area, CancellationToken.None),
		LetterQuery l => _client.GetMealsByFirstLetterAsync(l.Letter, CancellationToken.None),
		SearchQuery s => _client.SearchMealsAsync(s.Text, CancellationToken.None),
		CatalogQuery => throw new InvalidOperationException("The catalog query streams letter by letter and is not memoizable."),
		_ => throw new InvalidOperationException($"Unhandled meal query {query.GetType().Name}."),
	};

	/// <summary>
	/// Runs <paramref name="factory"/> once per key and shares the task; caller cancellation does not poison
	/// it, and a faulted entry is evicted so a retry can succeed.
	/// </summary>
	private async Task<Result<T>> GuardedAsync<T>(
		string key,
		ConcurrentDictionary<string, Lazy<Task<T>>> memo,
		Func<string, Task<T>> factory,
		CancellationToken ct)
	{
		var lazy = memo.GetOrAdd(key, k => new Lazy<Task<T>>(() => factory(k), LazyThreadSafetyMode.ExecutionAndPublication));

		try
		{
			// WaitAsync: the caller can leave while the shared fetch keeps running.
			var value = await lazy.Value.WaitAsync(ct).ConfigureAwait(false);
			return Result<T>.Success(value);
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested)
		{
			return Result<T>.Fail(ResultFailure.Cancelled());
		}
		catch (Exception ex)
		{
			// Evict so the failure is not sticky (cancellation is handled above and keeps the entry).
			memo.TryRemove(new KeyValuePair<string, Lazy<Task<T>>>(key, lazy));
			_logger.LogWarning(ex, "repository: {Key} failed", key);
			return Result<T>.Fail(Classify(ex));
		}
	}

	private static bool IsCancellation(Exception ex, CancellationToken ct) =>
		ex is OperationCanceledException && ct.IsCancellationRequested;

	/// <summary>Maps a transport exception to a failure the UI can act on.</summary>
	internal static ResultFailure Classify(Exception ex) => ex switch
	{
		MealDbApiException { StatusCode: >= 400 and < 500 } api =>
			ResultFailure.NotFound(api.Message.Length == 0 ? "The recipe is not available." : api.Message),

		MealDbApiException { StatusCode: >= 500 } api =>
			ResultFailure.Server(api.Message, api),

		MealDbApiException api =>
			ResultFailure.Malformed(api.Message, api),

		TaskCanceledException or TimeoutException =>
			ResultFailure.Network("TheMealDB did not answer in time. Check the connection and try again.", ex),

		HttpRequestException http =>
			ResultFailure.Network(http.Message.Length == 0 ? "Could not reach TheMealDB." : http.Message, http),

		_ => ResultFailure.Unknown(ex.Message, ex),
	};
}
