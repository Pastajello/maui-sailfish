using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Maui;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Controls;
using Microsoft.Maui.SailfishOS.Handlers;
using Microsoft.Maui.SailfishOS.Platform;

namespace Microsoft.Maui.SailfishOS.Hosting;

/// <summary>
/// Extension methods to configure a <see cref="MauiAppBuilder"/> for Sailfish OS.
/// </summary>
public static class AppHostBuilderExtensions
{
	/// <summary>
	/// Configures the MAUI application to run on Sailfish OS using the provided <typeparamref name="TApp"/>.
	/// </summary>
	public static MauiAppBuilder UseMauiAppSailfish<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TApp>(
		this MauiAppBuilder builder)
		where TApp : class, IApplication
	{
		builder.UseMauiApp<TApp>();
		builder.SetupSailfishDefaults();
		return builder;
	}

	static MauiAppBuilder SetupSailfishDefaults(this MauiAppBuilder builder)
	{
		builder.Services.AddSingleton<IDispatcherProvider>(svc => new SailfishDispatcherProvider());

		// Essentials in DI: MAUI's bridge installs them behind the statics (Battery.Default, …) during Build, and an
		// app registration wins (TryAdd). Plain UseMauiApp gets the same ones from SailfishServiceOverlay instead.
		builder.Services.AddSailfishEssentials();

		// ConfigureFonts registers here; the Qt host registers the files with Qt.
		builder.Services.AddSingleton<IFontRegistrar, SailfishFontRegistrar>();

		// IDispatcher is not registered here: SailfishServiceOverlay answers it from the Qt-loop dispatcher provider
		// (a registered factory would call DispatcherProvider.SetCurrent and swap the global provider back).

		// SailfishHandlersFactory resolves over this collection: app and library registrations win, stock MAUI
		// handlers give way to the Sailfish table (SailfishHandlersFactory.ViewHandlers).
		builder.ConfigureMauiHandlers(handlers =>
		{
			handlers.AddHandler<Application, SailfishApplicationHandler>();
			handlers.AddHandler<Microsoft.Maui.Controls.Window, SailfishWindowHandler>();
		});
		builder.Services.AddSingleton<IMauiHandlersFactory, SailfishHandlersFactory>();

		// DisplayAlertAsync/PromptAsync/ActionSheetAsync (Silica dialog panels) and modal pages (the Silica pageStack)
		// come from SailfishServiceOverlay with the window's render session; every MAUI lookup goes through it. An app
		// that registers its own IAlertManagerSubscription or IModalNavigationPlatformFactory still wins.

		return builder;
	}
}