using Microsoft.Maui;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// MAUI context carrying the service provider and handler factory.
/// </summary>
public class SailfishMauiContext : IMauiContext
{
	public SailfishMauiContext(IServiceProvider services)
	{
		Services = services ?? throw new ArgumentNullException(nameof(services));
		Handlers = services.GetService(typeof(IMauiHandlersFactory)) as IMauiHandlersFactory
			?? throw new InvalidOperationException("IMauiHandlersFactory is not registered.");
	}

	public IServiceProvider Services { get; }

	public IMauiHandlersFactory Handlers { get; }
}