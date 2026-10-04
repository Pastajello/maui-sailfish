using CommunityToolkit.Maui;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Hosting;
using SailfishKitchen.Api;
using SailfishKitchen.Services;
using SailfishKitchen.ViewModels;
using SailfishKitchen.Views;

namespace SailfishKitchen;

/// <summary>
/// Composition root: pages and view models are resolved from the container, so nothing needs a static.
/// Environment variables (<c>./tools/sf run --env NAME=VALUE</c>) override settings for scripted runs:
///   KITCHEN_OFFLINE=1            serve the bundled seed catalog, never the network
///   KITCHEN_PREFER_OFFLINE=1     serve cached responses regardless of age
///   KITCHEN_PAGE_SIZE=24         rows per incremental page
///   KITCHEN_GRID_SPAN=3          catalog grid columns
///   KITCHEN_NO_IMAGES=1          skip thumbnail downloads (placeholder only)
///   KITCHEN_NO_ANIMATIONS=1      disable card animations
///   KITCHEN_API_BASE=…           point at another TheMealDB-compatible host
///   KITCHEN_LOG_LEVEL=Debug      file logger verbosity
///   KITCHEN_START_PAGE=…         home | catalog | search | favorites | settings
/// </summary>
public static class MauiProgram
{
	// On Sailfish the paths stay literal /tmp because tools/sf run tails them (TMPDIR would move them).
	// iOS sandboxes /tmp, so they go to the app cache dir: xcrun simctl get_app_container booted com.maui.sailfishkitchen data
#if IOS
	internal static readonly string LogFilePath = Path.Combine(CacheDirectory(), "kitchen.log");
	private static readonly string TraceFilePath = Path.Combine(CacheDirectory(), "maui_trace.log");

	private static string CacheDirectory()
	{
		try
		{
			var cache = Microsoft.Maui.Storage.FileSystem.CacheDirectory;
			if (!string.IsNullOrWhiteSpace(cache))
				return cache;
		}
		catch (Exception)
		{
			// NotAvailableException if the platform implementation is not up yet.
		}

		return Path.GetTempPath();
	}
#else
	internal const string LogFilePath = "/tmp/kitchen.log";
	private const string TraceFilePath = "/tmp/maui_trace.log";
#endif

	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();

		// The same chain on iOS (the look & feel comparison) and Sailfish: CTK registers its handlers on both, and the
		// pieces without a Silica counterpart (toast, snackbar, popup) are simply not used here. CA1416: CTK.Maui does
		// not declare Linux support.
#pragma warning disable CA1416
		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit();
#pragma warning restore CA1416

		builder.ConfigureFonts(fonts =>
		{
			// The host resolves Silica's own typefaces; no bundled fonts needed.
		});

		ConfigureLogging(builder);
		ConfigureServices(builder.Services);

		return builder.Build();
	}

	private static void ConfigureLogging(MauiAppBuilder builder)
	{
		var level = ParseLogLevel(Environment.GetEnvironmentVariable("KITCHEN_LOG_LEVEL"));

		builder.Logging
			.SetMinimumLevel(level)
			.AddDebug()
			.AddProvider(new FileLoggerProvider(LogFilePath, level));
	}

	private static void ConfigureServices(IServiceCollection services)
	{
		// --- cross-cutting ----------------------------------------------------
		// IDispatcher comes from MAUI's own registration (the platform's UI-thread dispatcher, on Sailfish the Qt loop's).
		services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);

		// --- persistence ------------------------------------------------------
		services.AddSingleton<IJsonFileStore, JsonFileStore>();
		services.AddSingleton<ISettingsService>(provider =>
		{
			var settings = provider.GetRequiredService<SettingsService>();
			settings.Load();
			ApplyEnvironmentOverrides(settings);
			return settings;
		});
		services.AddSingleton<SettingsService>();
		services.AddSingleton<IFavoritesService, FavoritesService>();
		services.AddSingleton<ISearchHistoryService, SearchHistoryService>();

		// --- HTTP -------------------------------------------------------------
		services.Configure<MealDbOptions>(options =>
		{
			var apiBase = Environment.GetEnvironmentVariable("KITCHEN_API_BASE");
			if (!string.IsNullOrWhiteSpace(apiBase))
				options.BaseAddress = apiBase;
		});

		services.AddSingleton<HttpResponseCache>(provider =>
		{
			var cache = new HttpResponseCache(
				provider.GetRequiredService<IJsonFileStore>(),
				provider.GetRequiredService<ILogger<HttpResponseCache>>());

			// Prime from bundled recorded responses so a first offline run still shows a full catalog.
			var seedDirectory = Path.Combine(AppContext.BaseDirectory, "seed");
			cache.PrimeFromSeed(seedDirectory);
			return cache;
		});

		services.AddTransient<CachingHttpMessageHandler>();

		services
			.AddHttpClient<IMealDbClient, MealDbClient>((provider, http) =>
			{
				var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<MealDbOptions>>().Value;
				http.Timeout = options.RequestTimeout;
				http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", options.UserAgent);
			})
			// Cache outermost: a hit never enters the retry pipeline at all.
			.AddHttpMessageHandler<CachingHttpMessageHandler>()
			.AddStandardResilienceHandler(options =>
			{
				options.Retry.MaxRetryAttempts = 2;
				options.Retry.UseJitter = true;
				options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(20);
				options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(60);
				// The circuit breaker requires SamplingDuration >= 2 x AttemptTimeout; the 30 s default throws at startup.
				options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
			});

		services.AddSingleton<IImageCache, DiskImageCache>();
		services.AddSingleton<IRecipeRepository, RecipeRepository>();

		// --- UI services ------------------------------------------------------
		services.AddSingleton<IConnectivityService>(provider =>
		{
			var connectivity = provider.GetRequiredService<ConnectivityService>();
			connectivity.Start();
			return connectivity;
		});
		services.AddSingleton<ConnectivityService>();
		services.AddSingleton<IDialogService, DialogService>();
		services.AddSingleton<INavigationService, NavigationService>();
		services.AddSingleton<INotificationService, NotificationService>();

		// --- view models (transient: each page gets its own) -------------------
		// The banner is a singleton because one instance feeds the strip on every page.
		services.AddSingleton<BannerViewModel>();

		services.AddTransient<HomeViewModel>();
		services.AddTransient<CatalogViewModel>();
		services.AddTransient<MealDetailViewModel>();
		services.AddTransient<SearchViewModel>();
		services.AddTransient<FavoritesViewModel>();
		services.AddTransient<SettingsViewModel>();

		// --- views -------------------------------------------------------------
		services.AddTransient<HomePage>();
		services.AddTransient<CatalogPage>();
		services.AddTransient<MealDetailPage>();
		services.AddTransient<SearchPage>();
		services.AddTransient<FavoritesPage>();
		services.AddTransient<SettingsPage>();

		services.AddTransient<App>();
	}

	/// <summary>Environment wins over the persisted file for this run only, so scripted runs are reproducible.</summary>
	private static void ApplyEnvironmentOverrides(SettingsService settings)
	{
		var changed = false;

		if (HasFlag("KITCHEN_OFFLINE"))
		{
			settings.Override(s => s.OfflineMode = true, "env:KITCHEN_OFFLINE");
			changed = true;
		}

		if (HasFlag("KITCHEN_PREFER_OFFLINE"))
		{
			settings.Override(s => s.PreferOffline = true, "env:KITCHEN_PREFER_OFFLINE");
			changed = true;
		}

		if (HasFlag("KITCHEN_NO_IMAGES"))
		{
			settings.Override(s => s.CacheImages = false, "env:KITCHEN_NO_IMAGES");
			changed = true;
		}

		if (HasFlag("KITCHEN_NO_ANIMATIONS"))
		{
			settings.Override(s => s.AnimationsEnabled = false, "env:KITCHEN_NO_ANIMATIONS");
			changed = true;
		}

		if (TryReadInt("KITCHEN_PAGE_SIZE", out var pageSize))
		{
			settings.Override(s => s.PageSize = pageSize, "env:KITCHEN_PAGE_SIZE");
			changed = true;
		}

		if (TryReadInt("KITCHEN_GRID_SPAN", out var span))
		{
			settings.Override(s => s.GridSpan = span, "env:KITCHEN_GRID_SPAN");
			changed = true;
		}

		if (changed)
			Append("environment overrides applied to settings (session only)");
	}

	/// <summary>Which screen the app opens on, for scripted device runs and screenshots.</summary>
	internal static string? StartupPage => Normalize(Environment.GetEnvironmentVariable("KITCHEN_START_PAGE"));

	private static string? Normalize(string? value) =>
		string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

	private static bool HasFlag(string name)
	{
		var value = Environment.GetEnvironmentVariable(name);
		return value is "1" or "true" or "TRUE" or "yes";
	}

	private static bool TryReadInt(string name, out int value)
	{
		value = 0;
		var raw = Environment.GetEnvironmentVariable(name);
		return !string.IsNullOrWhiteSpace(raw)
			&& int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out value);
	}

	private static LogLevel ParseLogLevel(string? raw) => Normalize(raw) switch
	{
		"trace" => LogLevel.Trace,
		"debug" => LogLevel.Debug,
		"info" or "information" => LogLevel.Information,
		"warn" or "warning" => LogLevel.Warning,
		"error" => LogLevel.Error,
		"none" => LogLevel.None,
		_ => LogLevel.Information,
	};

	/// <summary>Writes to the backend's trace file, so startup failures show up before a logger exists.</summary>
	private static void Append(string message)
	{
		try
		{
			File.AppendAllText(TraceFilePath, $"[Kitchen] {message}{Environment.NewLine}");
		}
		catch (Exception)
		{
			// best effort
		}
	}
}
