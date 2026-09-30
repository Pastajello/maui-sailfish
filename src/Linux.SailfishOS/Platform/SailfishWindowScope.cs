using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Hosting;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// The window's service scope, as MAUI's MakeWindowScope builds it on the other platforms (internal there): services
/// the app registered resolve from the scope, so an <c>AddScoped</c> service lives as long as the window; the Sailfish
/// defaults nobody registered, and the services the backend must own (the Qt-loop dispatcher, the handler factory),
/// come from the application's provider.
/// </summary>
internal sealed class SailfishWindowScope : IServiceProvider, IDisposable
{
	private readonly IServiceProvider _application;
	private readonly IServiceScope _scope;

	private SailfishWindowScope(IServiceProvider application, IServiceScope scope)
	{
		_application = application;
		_scope = scope;
		Context = new SailfishMauiContext(this);
	}

	/// <summary>The window's MAUI context (window handler, pages, renderer).</summary>
	public SailfishMauiContext Context { get; }

	/// <summary>Creates the scope and runs the window-scoped initializers (window dispatchers, animation tickers).</summary>
	public static SailfishWindowScope Create(IServiceProvider application)
	{
		var factory = (IServiceScopeFactory?)application.GetService(typeof(IServiceScopeFactory))
			?? throw new InvalidOperationException("the service provider has no IServiceScopeFactory");
		var windowScope = new SailfishWindowScope(application, factory.CreateScope());
		foreach (var initializer in windowScope._scope.ServiceProvider.GetServices<IMauiInitializeScopedService>())
			initializer.Initialize(windowScope);
		return windowScope;
	}

	public object? GetService(Type serviceType)
	{
		if (serviceType == typeof(IServiceProvider))
			return this;
		// The overlay decides over the scope's registrations (a stock FontManager registered by UseMauiApp must not
		// replace the Sailfish one: text would be measured with another font than Qt draws).
		if (_application is SailfishServiceOverlay overlay)
			return overlay.Resolve(serviceType, _scope.ServiceProvider);
		if (serviceType == typeof(IDispatcherProvider) || serviceType == typeof(IDispatcher) ||
		    serviceType == typeof(IMauiHandlersFactory))
			return _application.GetService(serviceType);
		return _scope.ServiceProvider.GetService(serviceType) ?? _application.GetService(serviceType);
	}

	public void Dispose() => _scope.Dispose();
}
