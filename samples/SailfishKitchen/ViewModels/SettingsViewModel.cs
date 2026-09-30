using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Dispatching;
using SailfishKitchen.Api;
using SailfishKitchen.Models;
using SailfishKitchen.Services;

namespace SailfishKitchen.ViewModels;

/// <summary>
/// Settings plus cache/storage/device diagnostics. Slider-backed values are doubles because MAUI does not
/// narrow <c>Slider.Value</c> to int; <see cref="SyncFrom"/> reads the stored (sanitized) value back.
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
	private readonly ISettingsService _settings;
	private readonly IImageCache _imageCache;
	private readonly HttpResponseCache _httpCache;
	private readonly IFavoritesService _favorites;
	private readonly IRecipeRepository _repository;
	private readonly IJsonFileStore _store;
	private readonly IConnectivityService _connectivity;
	private bool _suppressWriteBack;

	public SettingsViewModel(
		ISettingsService settings,
		IImageCache imageCache,
		HttpResponseCache httpCache,
		IFavoritesService favorites,
		IRecipeRepository repository,
		IJsonFileStore store,
		IConnectivityService connectivity,
		IDispatcher dispatcher,
		IDialogService dialogs,
		INotificationService notifications,
		ILogger<SettingsViewModel> logger)
		: base(dialogs, notifications, dispatcher, logger)
	{
		_settings = settings;
		_imageCache = imageCache;
		_httpCache = httpCache;
		_favorites = favorites;
		_repository = repository;
		_store = store;
		_connectivity = connectivity;

		Title = "Settings";
		SyncFrom(settings.Current);
		RefreshDiagnostics();
	}

	// --- browsing -------------------------------------------------------------

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(PageSizeSummary))]
	private double _pageSizeValue = AppSettings.DefaultPageSize;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(GridSpanSummary))]
	private double _gridSpanValue = 2;

	[ObservableProperty]
	private bool _isGridLayout = true;

	[ObservableProperty]
	private bool _animationsEnabled = true;

	// --- data -----------------------------------------------------------------

	[ObservableProperty]
	private bool _preferOffline;

	[ObservableProperty]
	private bool _offlineMode;

	[ObservableProperty]
	private bool _cacheImages = true;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(CacheTtlSummary))]
	private double _cacheTtlValue = 30;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(DebounceSummary))]
	private double _debounceValue = 450;

	// --- diagnostics ----------------------------------------------------------

	[ObservableProperty]
	private string _cachedImagesSummary = "—";

	[ObservableProperty]
	private string _cachedResponsesSummary = "—";

	[ObservableProperty]
	private string _favoritesSummary = "—";

	[ObservableProperty]
	private string _dataDirectory = "—";

	[ObservableProperty]
	private string _deviceSummary = "—";

	[ObservableProperty]
	private string _connectionSummary = "—";

	public string PageSizeSummary => $"{(int)PageSizeValue} rows per page";

	public string GridSpanSummary => $"{(int)GridSpanValue} column(s)";

	public string CacheTtlSummary => CacheTtlValue <= 0 ? "never expires" : $"{(int)CacheTtlValue} min";

	public string DebounceSummary => $"{(int)DebounceValue} ms";

	partial void OnPageSizeValueChanged(double value) =>
		Apply(s => s.PageSize = (int)value, nameof(AppSettings.PageSize));

	partial void OnGridSpanValueChanged(double value) =>
		Apply(s => s.GridSpan = (int)value, nameof(AppSettings.GridSpan));

	partial void OnIsGridLayoutChanged(bool value) =>
		Apply(s => s.Layout = value ? Models.ListLayout.Grid : Models.ListLayout.List, nameof(AppSettings.Layout));

	partial void OnAnimationsEnabledChanged(bool value) =>
		Apply(s => s.AnimationsEnabled = value, nameof(AppSettings.AnimationsEnabled));

	partial void OnPreferOfflineChanged(bool value) =>
		Apply(s => s.PreferOffline = value, nameof(AppSettings.PreferOffline));

	partial void OnOfflineModeChanged(bool value) =>
		Apply(s => s.OfflineMode = value, nameof(AppSettings.OfflineMode));

	partial void OnCacheImagesChanged(bool value) =>
		Apply(s => s.CacheImages = value, nameof(AppSettings.CacheImages));

	partial void OnCacheTtlValueChanged(double value) =>
		Apply(s => s.CacheTtlMinutes = (int)value, nameof(AppSettings.CacheTtlMinutes));

	partial void OnDebounceValueChanged(double value) =>
		Apply(s => s.SearchDebounceMs = (int)value, nameof(AppSettings.SearchDebounceMs));

	[RelayCommand]
	private void Reset()
	{
		SyncFrom(_settings.Reset());
		RefreshDiagnostics();
	}

	[RelayCommand]
	private async Task ClearImageCacheAsync()
	{
		await _imageCache.ClearAsync().ConfigureAwait(false);
		Ui(RefreshDiagnostics);
		await Notifications.ShowAsync("Image cache cleared", NotificationKind.Success).ConfigureAwait(false);
	}

	[RelayCommand]
	private async Task ClearCatalogCacheAsync()
	{
		var confirmed = await Dialogs.ConfirmAsync(
			"Clear the recipe cache?",
			"The stored catalog responses are removed, so the next browse has to reach TheMealDB again. Favourites and images are kept.",
			"Clear", "Cancel").ConfigureAwait(false);

		if (!confirmed)
			return;

		_httpCache.Clear();
		_repository.InvalidateAll();
		Ui(RefreshDiagnostics);
		await Notifications.ShowAsync("Recipe cache cleared", NotificationKind.Success).ConfigureAwait(false);
	}

	[RelayCommand]
	private async Task ClearFavoritesAsync()
	{
		if (_favorites.Count == 0)
		{
			await Notifications.ShowAsync("No favourites to remove").ConfigureAwait(false);
			return;
		}

		var confirmed = await Dialogs.ConfirmAsync(
			"Remove all favourites?",
			$"{_favorites.Count} saved recipes will be deleted from this device.",
			"Remove all", "Cancel").ConfigureAwait(false);

		if (!confirmed)
			return;

		_favorites.Clear();
		Ui(RefreshDiagnostics);
		await Notifications.ShowAsync("Favourites cleared", NotificationKind.Success).ConfigureAwait(false);
	}

	[RelayCommand]
	private async Task PruneImageCacheAsync()
	{
		await _imageCache.PruneAsync(maxBytes: 8 * 1024 * 1024).ConfigureAwait(false);
		Ui(RefreshDiagnostics);
		await Notifications.ShowAsync("Image cache trimmed to 8 MB", NotificationKind.Success).ConfigureAwait(false);
	}

	/// <summary>Called by the page when it appears, so the numbers are current.</summary>
	public void Refresh() => RefreshDiagnostics();

	private void RefreshDiagnostics()
	{
		CachedImagesSummary = $"{_imageCache.CachedCount} thumbnails · {FormatBytes(_imageCache.TotalBytes)}";
		CachedResponsesSummary = $"{_httpCache.EntryCount} responses · {FormatBytes(_httpCache.TotalBytes)}";
		FavoritesSummary = $"{_favorites.Count} saved recipes";
		DataDirectory = _store.RootDirectory;
		DeviceSummary = DescribeDevice();
		ConnectionSummary = _connectivity.IsOnline ? "online" : "offline";
	}

	private void Apply(Action<AppSettings> mutate, string propertyName)
	{
		// SyncFrom re-enters these handlers; without the guard that loops.
		if (_suppressWriteBack)
			return;

		// Sanitizing may clamp the input; read it back so the control shows what the app uses.
		var stored = _settings.Update(mutate, propertyName);
		SyncFrom(stored);
		RefreshDiagnostics();
	}

	private void SyncFrom(AppSettings settings)
	{
		_suppressWriteBack = true;
		try
		{
			Ui(() =>
			{
				PageSizeValue = settings.PageSize;
				GridSpanValue = settings.GridSpan;
				IsGridLayout = settings.Layout == Models.ListLayout.Grid;
				AnimationsEnabled = settings.AnimationsEnabled;
				PreferOffline = settings.PreferOffline;
				OfflineMode = settings.OfflineMode;
				CacheImages = settings.CacheImages;
				CacheTtlValue = settings.CacheTtlMinutes;
				DebounceValue = settings.SearchDebounceMs;
			});
		}
		finally
		{
			_suppressWriteBack = false;
		}
	}

	private string DescribeDevice()
	{
		try
		{
			var info = Microsoft.Maui.Devices.DeviceInfo.Current;
			return $"{info.Manufacturer} {info.Model} · {info.Platform} {info.VersionString} · {info.Idiom}";
		}
		catch (Exception ex)
		{
			// Essentials is not guaranteed on every host; degrade rather than fail the page.
			Logger.LogDebug(ex, "device info unavailable");
			return $"{Environment.OSVersion} · {(Environment.Is64BitProcess ? "64-bit" : "32-bit")} process";
		}
	}

	private static string FormatBytes(long bytes) => bytes switch
	{
		<= 0 => "0 B",
		< 1024 => $"{bytes} B",
		< 1024 * 1024 => $"{bytes / 1024d:F1} kB",
		_ => $"{bytes / (1024d * 1024d):F1} MB",
	};
}
