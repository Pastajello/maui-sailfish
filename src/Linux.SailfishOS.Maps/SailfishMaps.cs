using Microsoft.Maui.Maps.Handlers;
using Microsoft.Maui.SailfishOS.Handlers;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Maps;

/// <summary>
/// Registers the Sailfish map. The backend calls <see cref="Register"/> before <c>CreateMauiApp</c> (the package's
/// targets name it in the app assembly), so the app keeps calling <c>UseMauiMaps()</c> as on every platform; the plain
/// <c>net</c> <see cref="MapHandler"/>, which has no platform view, is served by <see cref="SailfishMapHandler"/>.
/// </summary>
public static class SailfishMaps
{
	private static bool _registered;

	public static void Register()
	{
		if (_registered)
			return;
		_registered = true;
		SailfishHandlersFactory.ReplaceLibraryHandler<MapHandler, SailfishMapHandler>();
		QtHostAdapters.Register(SailfishMapHandler.AdapterName, "SailfishMaps/Map.qml");
	}
}
