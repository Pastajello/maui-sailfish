using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.SailfishOS.Handlers;
using Microsoft.Maui.SailfishOS.Hosting;
using Microsoft.Maui.SailfishOS.Platform;
using Xunit;

namespace Linux.SailfishOS.Tests;

/// <summary>The window's service scope, as MAUI's MakeWindowScope builds it elsewhere: AddScoped lives per window,
/// singletons and the backend's own services stay the application's.</summary>
[Collection("renderer")]
public class WindowScopeTests
{
	private sealed class TestApp : Microsoft.Maui.Controls.Application
	{
	}

	private sealed class PerWindow
	{
	}

	private sealed class Shared
	{
	}

	[Fact]
	public void Scoped_services_live_per_window_and_the_backend_services_stay_shared()
	{
		var builder = MauiApp.CreateBuilder(useDefaults: false);
		builder.UseMauiAppSailfish<TestApp>();
		builder.Services.AddScoped<PerWindow>();
		builder.Services.AddSingleton<Shared>();
		using var app = builder.Build();
		var root = new SailfishServiceOverlay(app.Services);

		using var first = SailfishWindowScope.Create(root);
		using var second = SailfishWindowScope.Create(root);

		Assert.Same(first.GetService(typeof(PerWindow)), first.GetService(typeof(PerWindow)));
		Assert.NotSame(first.GetService(typeof(PerWindow)), second.GetService(typeof(PerWindow)));
		Assert.Same(first.GetService(typeof(Shared)), second.GetService(typeof(Shared)));
		Assert.IsType<SailfishDispatcherProvider>(first.GetService(typeof(IDispatcherProvider)));
		Assert.IsType<SailfishHandlersFactory>(first.Context.Handlers);
		Assert.IsType<SailfishFontManager>(first.GetService(typeof(IFontManager)));   // an overlay default nobody registered
		Assert.NotNull(first.GetService(typeof(Microsoft.Maui.Controls.Platform.IModalNavigationPlatformFactory)));
	}

	private sealed class AppAlerts : Microsoft.Maui.Controls.Platform.IAlertManagerSubscription
	{
		public void OnActionSheetRequested(Microsoft.Maui.Controls.Page sender, Microsoft.Maui.Controls.Internals.ActionSheetArguments arguments) { }
		public void OnAlertRequested(Microsoft.Maui.Controls.Page sender, Microsoft.Maui.Controls.Internals.AlertArguments arguments) { }
		public void OnPromptRequested(Microsoft.Maui.Controls.Page sender, Microsoft.Maui.Controls.Internals.PromptArguments arguments) { }
		public void OnPageBusy(Microsoft.Maui.Controls.Page sender, bool enabled) { }
	}

	// Dialogs and modals come from the overlay, with its render session, for the application and every window: the DI
	// registrations UseMauiAppSailfish made had no session and hid the overlay's (and an app's own registration made
	// before UseMauiAppSailfish lost to them).
	[Fact]
	public void Dialogs_and_modals_come_from_the_overlay_and_an_apps_own_registration_wins()
	{
		var builder = MauiApp.CreateBuilder(useDefaults: false);
		builder.UseMauiAppSailfish<TestApp>();
		using var app = builder.Build();
		var root = new SailfishServiceOverlay(app.Services);
		using var window = SailfishWindowScope.Create(root);

		Assert.IsType<Microsoft.Maui.SailfishOS.Platform.QtHost.QtHostAlertSubscription>(
			root.GetService(typeof(Microsoft.Maui.Controls.Platform.IAlertManagerSubscription)));
		Assert.Same(root.GetService(typeof(Microsoft.Maui.Controls.Platform.IAlertManagerSubscription)),
			window.GetService(typeof(Microsoft.Maui.Controls.Platform.IAlertManagerSubscription)));
		Assert.Same(root.GetService(typeof(Microsoft.Maui.Controls.Platform.IModalNavigationPlatformFactory)),
			window.GetService(typeof(Microsoft.Maui.Controls.Platform.IModalNavigationPlatformFactory)));

		var own = MauiApp.CreateBuilder(useDefaults: false);
		own.Services.AddSingleton<Microsoft.Maui.Controls.Platform.IAlertManagerSubscription, AppAlerts>();
		own.UseMauiAppSailfish<TestApp>();
		using var ownApp = own.Build();
		Assert.IsType<AppAlerts>(new SailfishServiceOverlay(ownApp.Services)
			.GetService(typeof(Microsoft.Maui.Controls.Platform.IAlertManagerSubscription)));
	}

	[Fact]
	public void The_overlay_decides_over_the_stock_registrations_in_the_window_scope_too()
	{
		// UseMauiApp registers MAUI's own FontManager; the window must still measure with the Sailfish one, the font Qt draws.
		SailfishDispatcherProvider.BindLoopThread();   // as Run does on the Qt thread
		var builder = MauiApp.CreateBuilder();
		builder.UseMauiApp<TestApp>();
		using var app = builder.Build();
		Assert.NotNull(app.Services.GetService(typeof(IFontManager)));
		Assert.IsNotType<SailfishFontManager>(app.Services.GetService(typeof(IFontManager)));
		var root = new SailfishServiceOverlay(app.Services);

		using var window = SailfishWindowScope.Create(root);

		Assert.Same(root.GetService(typeof(IFontManager)), window.GetService(typeof(IFontManager)));
		Assert.IsType<SailfishFontManager>(window.Context.Services.GetService(typeof(IFontManager)));
		Assert.Same(root.GetService(typeof(IDispatcherProvider)), window.GetService(typeof(IDispatcherProvider)));
		Assert.Same(root.GetService(typeof(Microsoft.Maui.Devices.IDeviceDisplay)), window.GetService(typeof(Microsoft.Maui.Devices.IDeviceDisplay)));
	}
}
