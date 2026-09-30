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

	[Fact]
	public void The_overlay_decides_over_the_stock_registrations_in_the_window_scope_too()
	{
		// UseMauiApp registers MAUI's own FontManager; the window must still measure with the Sailfish one, the font Qt draws.
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
