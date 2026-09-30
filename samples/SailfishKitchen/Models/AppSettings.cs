using System.Text.Json.Serialization;

namespace SailfishKitchen.Models;

/// <summary>Catalog presentation.</summary>
public enum ListLayout
{
	Grid,
	List,
}

/// <summary>
/// User settings, persisted as JSON in the app data directory. Durations are plain numbers so the file stays
/// hand-editable without a TimeSpan converter.
/// </summary>
public sealed class AppSettings
{
	public const int MinPageSize = 6;
	public const int MaxPageSize = 48;
	public const int DefaultPageSize = 12;

	/// <summary>Rows per incremental-loading page.</summary>
	public int PageSize { get; set; } = DefaultPageSize;

	public ListLayout Layout { get; set; } = ListLayout.Grid;

	/// <summary>Grid columns when <see cref="Layout"/> is Grid.</summary>
	public int GridSpan { get; set; } = 2;

	/// <summary>Serve any cached response without hitting the network first.</summary>
	public bool PreferOffline { get; set; }

	/// <summary>Never touch the network; use the cache and bundled seed catalog.</summary>
	public bool OfflineMode { get; set; }

	/// <summary>Keep a disk copy of thumbnails for offline use.</summary>
	public bool CacheImages { get; set; } = true;

	/// <summary>Play card entrance animations.</summary>
	public bool AnimationsEnabled { get; set; } = true;

	/// <summary>Accent colour key, resolved by <c>Palette.AccentFor</c>.</summary>
	public string Accent { get; set; } = "amber";

	/// <summary>How long a cached API response stays fresh.</summary>
	public int CacheTtlMinutes { get; set; } = 30;

	public int SearchDebounceMs { get; set; } = 450;

	/// <summary>False when either offline switch is on: thumbs come from the disk cache or the placeholder.</summary>
	[JsonIgnore]
	public bool AllowNetwork => !(OfflineMode || PreferOffline);

	public TimeSpan CacheTtl => TimeSpan.FromMinutes(CacheTtlMinutes);

	public TimeSpan SearchDebounce => TimeSpan.FromMilliseconds(SearchDebounceMs);

	public AppSettings Clone() => (AppSettings)MemberwiseClone();

	public static AppSettings GetDefault() => new();

	/// <summary>Clamps values, since the JSON file may be hand-edited.</summary>
	public AppSettings Sanitized()
	{
		PageSize = Math.Clamp(PageSize, MinPageSize, MaxPageSize);
		GridSpan = Math.Clamp(GridSpan, 1, 4);
		CacheTtlMinutes = Math.Clamp(CacheTtlMinutes, 0, 60 * 24 * 7);
		SearchDebounceMs = Math.Clamp(SearchDebounceMs, 0, 10_000);
		if (string.IsNullOrWhiteSpace(Accent))
			Accent = "amber";
		if (!Enum.IsDefined(Layout))
			Layout = ListLayout.Grid;
		return this;
	}
}
