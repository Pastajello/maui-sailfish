using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;
using SailfishKitchen.Models;
using SailfishKitchen.Views;

namespace SailfishKitchen.Services;

/// <summary>Navigation by intent ("open recipe 52768"), so view models never reference page types.</summary>
public interface INavigationService
{
	/// <summary>Set once by <see cref="App"/> when the window's root is known.</summary>
	void Attach(NavigationPage navigation);

	bool CanGoBack { get; }

	Task GoHomeAsync();

	Task OpenCatalogAsync(MealQuery query);

	Task OpenMealAsync(string mealId, string? title = null);

	Task OpenSearchAsync(string? initialQuery = null);

	Task OpenFavoritesAsync();

	Task OpenSettingsAsync();

	Task GoBackAsync();

	/// <summary>Clears the stack back to the home page.</summary>
	Task PopToRootAsync();
}

public sealed class NavigationService : INavigationService
{
	private readonly IServiceProvider _services;
	private readonly ILogger<NavigationService> _logger;
	private NavigationPage? _navigation;

	public NavigationService(IServiceProvider services, ILogger<NavigationService> logger)
	{
		_services = services;
		_logger = logger;
	}

	public bool CanGoBack => _navigation is not null && _navigation.Navigation.NavigationStack.Count > 1;

	public void Attach(NavigationPage navigation)
	{
		ArgumentNullException.ThrowIfNull(navigation);
		_navigation = navigation;
		_logger.LogInformation("navigation: attached to root NavigationPage");
	}

	public Task GoHomeAsync() => PopToRootAsync();

	public Task OpenCatalogAsync(MealQuery query)
	{
		ArgumentNullException.ThrowIfNull(query);
		var page = Resolve<CatalogPage>();
		page.Query = query;
		return PushAsync(page);
	}

	public Task OpenMealAsync(string mealId, string? title = null)
	{
		if (string.IsNullOrWhiteSpace(mealId))
			return Task.CompletedTask;

		var page = Resolve<MealDetailPage>();
		page.MealId = mealId;

		// A null Title resets the nav bar to default text, and late Title updates never reach it.
		if (!string.IsNullOrWhiteSpace(title))
			page.Title = title;

		return PushAsync(page);
	}

	public Task OpenSearchAsync(string? initialQuery = null)
	{
		var page = Resolve<SearchPage>();
		if (!string.IsNullOrWhiteSpace(initialQuery))
			page.InitialQuery = initialQuery;

		return PushAsync(page);
	}

	public Task OpenFavoritesAsync() => PushAsync(Resolve<FavoritesPage>());

	public Task OpenSettingsAsync() => PushAsync(Resolve<SettingsPage>());

	public Task GoBackAsync()
	{
		var navigation = _navigation;
		if (navigation is null || navigation.Navigation.NavigationStack.Count <= 1)
			return Task.CompletedTask;

		return navigation.PopAsync();
	}

	public Task PopToRootAsync()
	{
		var navigation = _navigation;
		if (navigation is null || navigation.Navigation.NavigationStack.Count <= 1)
			return Task.CompletedTask;

		return navigation.PopToRootAsync();
	}

	/// <summary>Pages are transient and constructor-injected, so they come from the container.</summary>
	private T Resolve<T>() where T : Page =>
		_services.GetRequiredService<T>();

	private async Task PushAsync(Page page)
	{
		var navigation = _navigation;
		if (navigation is null)
		{
			// Navigation before the window exists (e.g. during startup) is logged and dropped rather than crashing.
			_logger.LogWarning("navigation: no root attached yet, dropping push of {Page}", page.GetType().Name);
			return;
		}

		// KITCHEN_DIAG_PUSH_STACK=1 logs the caller of every push (phantom re-push after the native back gesture).
		if (Environment.GetEnvironmentVariable("KITCHEN_DIAG_PUSH_STACK") == "1")
			_logger.LogInformation("navigation: PUSH {Page} from:\n{Stack}",
				page.GetType().Name, Environment.StackTrace);

		try
		{
			await navigation.PushAsync(page).ConfigureAwait(true);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "navigation: push of {Page} failed", page.GetType().Name);
			throw;
		}
	}
}
