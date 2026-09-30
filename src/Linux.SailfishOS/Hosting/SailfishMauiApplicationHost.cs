using Microsoft.Maui.Hosting;

namespace Microsoft.Maui.SailfishOS.Hosting;

/// <summary>
/// Host used by the generated Main (SailfishGenerateMain), which hands it the app's CreateMauiApp factory.
/// </summary>
public sealed class SailfishMauiApplicationHost : Platform.SailfishMauiApplication
{
	private readonly Func<MauiApp> _createMauiApp;

	public SailfishMauiApplicationHost(Func<MauiApp> createMauiApp) =>
		_createMauiApp = createMauiApp ?? throw new ArgumentNullException(nameof(createMauiApp));

	protected override MauiApp CreateMauiApp() => _createMauiApp();
}
