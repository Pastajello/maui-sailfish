using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SailfishKitchen.Services;

namespace SailfishKitchen.Services;

/// <summary>Persisted recent-search list, versioned like the other app files.</summary>
public sealed class SearchHistoryFile
{
	public const int CurrentVersion = 1;
	public const int MaxEntries = 12;

	[JsonPropertyName("version")] public int Version { get; set; } = CurrentVersion;

	[JsonPropertyName("terms")] public List<string> Terms { get; set; } = [];
}

/// <summary>Most-recent-first search terms, capped so the file cannot grow without bound.</summary>
public interface ISearchHistoryService
{
	IReadOnlyList<string> Recent { get; }

	void Record(string term);

	void Remove(string term);

	void Clear();
}

public sealed class SearchHistoryService : ISearchHistoryService
{
	internal const string FileName = "search-history.json";

	private readonly IJsonFileStore _store;
	private readonly ILogger<SearchHistoryService> _logger;
	private readonly object _gate = new();
	private readonly List<string> _terms = [];
	private bool _loaded;

	public SearchHistoryService(IJsonFileStore store, ILogger<SearchHistoryService> logger)
	{
		_store = store;
		_logger = logger;
	}

	public IReadOnlyList<string> Recent
	{
		get { lock (_gate) { EnsureLoaded(); return _terms.ToList(); } }
	}

	public void Record(string term)
	{
		var normalized = term?.Trim() ?? string.Empty;
		if (normalized.Length < 2)
			return;

		lock (_gate)
		{
			EnsureLoaded();

			// Case-insensitive de-dup, keeping the casing the user typed.
			var existing = _terms.FindIndex(t => string.Equals(t, normalized, StringComparison.OrdinalIgnoreCase));
			if (existing >= 0)
				_terms.RemoveAt(existing);

			_terms.Insert(0, normalized);
			if (_terms.Count > SearchHistoryFile.MaxEntries)
				_terms.RemoveRange(SearchHistoryFile.MaxEntries, _terms.Count - SearchHistoryFile.MaxEntries);

			Persist();
		}
	}

	public void Remove(string term)
	{
		lock (_gate)
		{
			EnsureLoaded();
			if (_terms.RemoveAll(t => string.Equals(t, term, StringComparison.OrdinalIgnoreCase)) == 0)
				return;

			Persist();
		}
	}

	public void Clear()
	{
		lock (_gate)
		{
			EnsureLoaded();
			if (_terms.Count == 0)
				return;

			_terms.Clear();
			Persist();
		}
	}

	private void EnsureLoaded()
	{
		if (_loaded)
			return;

		_loaded = true;

		var file = _store.Load<SearchHistoryFile>(FileName);
		if (file?.Terms is null || file.Version != SearchHistoryFile.CurrentVersion)
			return;

		foreach (var term in file.Terms.Where(t => !string.IsNullOrWhiteSpace(t)).Take(SearchHistoryFile.MaxEntries))
			_terms.Add(term.Trim());

		_logger.LogDebug("search history: loaded {Count}", _terms.Count);
	}

	private void Persist()
	{
		try
		{
			_store.Save(FileName, new SearchHistoryFile { Version = SearchHistoryFile.CurrentVersion, Terms = _terms.ToList() });
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			_logger.LogWarning(ex, "search history: could not persist");
		}
	}
}
