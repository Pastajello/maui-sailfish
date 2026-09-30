using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using SailfishKitchen.Services;
using SailfishKitchen.Views;

namespace SailfishKitchen;

/// <summary>
/// Application root. Uses a <see cref="NavigationPage"/> because the Silica page stack maps onto it
/// directly; Shell's flyout/tab chrome has no counterpart here.
/// </summary>
public class App : Application
{
	private readonly IServiceProvider _services;
	private readonly ILogger<App> _logger;

	public App(IServiceProvider services, ILogger<App> logger)
	{
		_services = services;
		_logger = logger;

		// Installed before the first page so implicit styles apply on its first measure.
		SailfishKitchen.Resources.AppStyles.Populate(Resources);
		_logger.LogInformation("application resources installed ({Count} entries)", Resources.Count);
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var home = _services.GetRequiredService<HomePage>();
		var navigation = new NavigationPage(home)
		{
			BarBackgroundColor = Palette.Surface,
			BarTextColor = Palette.TextPrimary,
			BackgroundColor = Palette.Background,
		};

		// Attached here so the navigation service is usable from any view model constructor onward.
		_services.GetRequiredService<INavigationService>().Attach(navigation);

		var window = new Window(navigation) { Title = "Sailfish Kitchen" };
		window.Created += OnWindowCreated;

		_logger.LogInformation("window created; root = {Root}", home.GetType().Name);
		return window;
	}

	private static async Task WaitForFirstLayoutAsync(Window window)
	{
		for (var i = 0; i < 60; i++)
		{
			if (window.Width > 1 && window.Height > 1)
				return;

			await Task.Delay(50).ConfigureAwait(true);
		}
	}

	/// <summary>Honours <c>KITCHEN_START_PAGE</c> so scripted runs land directly on a screen.</summary>
	private void OnWindowCreated(object? sender, EventArgs e)
	{
		if (sender is not Window created)
			return;

		created.Created -= OnWindowCreated;
		var window = created;

		if (Environment.GetEnvironmentVariable("KITCHEN_TOUR") is "beef" or "home")
		{
			var tourNavigation = _services.GetRequiredService<INavigationService>();
			_services.GetRequiredService<IDispatcher>().Dispatch(async () =>
			{
				await WaitForFirstLayoutAsync(window);
				if (Environment.GetEnvironmentVariable("KITCHEN_TOUR") is "home")
					await Helpers.DemoTour.RunHomeAsync(window);
				else
					await Helpers.DemoTour.RunAsync(tourNavigation, window);
			});
			return;
		}

		var target = MauiProgram.StartupPage;
		if (target is null)
			return;

		var navigation = _services.GetRequiredService<INavigationService>();
		var dispatcher = _services.GetRequiredService<IDispatcher>();

		dispatcher.Dispatch(async () =>
		{
			// A page pushed before the window has a real size is measured against a stale canvas and never
			// re-measured. Activated never fires on this host, so poll the geometry.
			// KITCHEN_STARTUP_NOWAIT=1 skips the wait, to test whether the backend self-heals.
			if (Environment.GetEnvironmentVariable("KITCHEN_STARTUP_NOWAIT") != "1")
				await WaitForFirstLayoutAsync(window);

			try
			{
				switch (target)
				{
					case "catalog":
						await navigation.OpenCatalogAsync(Models.MealQuery.WholeCatalog());
						break;
					case "meal":
						// KITCHEN_START_MEAL picks the recipe for detail-page screenshots.
						var mealId = Environment.GetEnvironmentVariable("KITCHEN_START_MEAL") ?? "52772";
						await navigation.OpenMealAsync(mealId);
						break;
					case "search":
						await navigation.OpenSearchAsync();
						break;
					case "favorites":
						await navigation.OpenFavoritesAsync();
						break;
					case "settings":
						await navigation.OpenSettingsAsync();
						break;
					default:
						_logger.LogWarning("unknown KITCHEN_START_PAGE '{Target}'", target);
						break;
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "startup navigation to '{Target}' failed", target);
			}

			// KITCHEN_DIAG_POP=1: pop after a beat to exercise push/pop (pulley menus coming back blank).
			if (Environment.GetEnvironmentVariable("KITCHEN_DIAG_POP") == "1")
			{
				await Task.Delay(3000);
				await navigation.GoBackAsync();
			}

			// KITCHEN_DIAG_CYCLES=N: soak N catalog push/pop cycles (the loop that crashed the Qt 5.6 QML engine).
			if (int.TryParse(Environment.GetEnvironmentVariable("KITCHEN_DIAG_CYCLES"), out var cycles))
			{
				for (var i = 0; i < cycles; i++)
				{
					await Task.Delay(2500);
					await navigation.OpenCatalogAsync(Models.MealQuery.WholeCatalog());
					await Task.Delay(2500);
					await navigation.GoBackAsync();
				}
			}
		});
	}
}

/// <summary>The app's named colours; dark because Silica's ambient background is dark.</summary>
public static class Palette
{
	public static readonly Color Background = Color.FromArgb("#0E1116");
	public static readonly Color Surface = Color.FromArgb("#171B22");
	public static readonly Color SurfaceRaised = Color.FromArgb("#1F242D");
	public static readonly Color Border = Color.FromArgb("#2C333D");
	public static readonly Color TextPrimary = Color.FromArgb("#F2F5F9");
	public static readonly Color TextSecondary = Color.FromArgb("#9AA6B4");
	public static readonly Color TextMuted = Color.FromArgb("#6B7684");
	public static readonly Color Accent = Color.FromArgb("#FFB13D");
	public static readonly Color AccentDim = Color.FromArgb("#8A6224");
	public static readonly Color Success = Color.FromArgb("#5BD07E");
	public static readonly Color Warning = Color.FromArgb("#FFC24B");
	public static readonly Color Error = Color.FromArgb("#FF6B5E");
	public static readonly Color Skeleton = Color.FromArgb("#232932");

	/// <summary>Accent choices offered on the settings page, keyed by <see cref="AppSettings.Accent"/>.</summary>
	public static IReadOnlyDictionary<string, Color> Accents { get; } =
		new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
		{
			["amber"] = Color.FromArgb("#FFB13D"),
			["sailfish"] = Color.FromArgb("#4FC3F7"),
			["herb"] = Color.FromArgb("#7BD389"),
			["chilli"] = Color.FromArgb("#FF6B5E"),
			["plum"] = Color.FromArgb("#C792EA"),
		};

	public static Color AccentFor(string? key) =>
		key is not null && Accents.TryGetValue(key, out var accent) ? accent : Accent;
}
