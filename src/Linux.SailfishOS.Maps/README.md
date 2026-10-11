# Microsoft.Maui.Platforms.SailfishOS.Maps

The Sailfish OS handler for [`Microsoft.Maui.Controls.Maps`](https://learn.microsoft.com/dotnet/maui/user-interface/controls/map):
the `Map` control on QtLocation with OpenStreetMap tiles.

```xml
<ItemGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'sailfish'">
  <PackageReference Include="Microsoft.Maui.Controls.Maps" Version="$(MauiVersion)" />
  <PackageReference Include="Microsoft.Maui.Platforms.SailfishOS.Maps" Version="$(PlatformMauiSailfishVersion)" />
</ItemGroup>
```

The app calls `UseMauiMaps()` as on every platform; the package registers its handler itself.

## What works

| MAUI | Sailfish |
|---|---|
| `MapType` | `Street`: OpenStreetMap; `Satellite`, `Hybrid`: OpenTopoMap (QtLocation's OSM plugin has no satellite imagery) |
| `IsScrollEnabled`, `IsZoomEnabled` | pan and pinch |
| `IsShowingUser`, `UserLocationChanged`, `LastUserLocation` | QtPositioning; the app needs the `Location` Sailjail permission |
| `Pins`, `Pin.MarkerClicked`, `Pin.InfoWindowClicked`, `ShowInfoWindow`/`HideInfoWindow` | a theme location icon; a tap shows the label and address |
| `IsClusteringEnabled`, `ClusterClicked` | pins in the same 64 dp screen cell form a counted bubble; an unhandled tap zooms in |
| `MapElements`: `Polyline`, `Polygon`, `Circle` | stroke colour and width, fill, `IsVisible`, `ZIndex`, `Clicked` |
| `MoveToRegion`, `VisibleRegion` | the region is reported back after the map settles |
| `MapClicked`, `MapLongClicked` | |
| `MapStyle` | not supported: the plugin's tile servers are fixed (a warning once) |
| `IsTrafficEnabled`, `ClusterImageSource` | ignored |

## Installing

QtLocation is not part of a default Sailfish OS install. The package makes the app's RPM require `qml(QtLocation)`
(provided by `qt5-qtdeclarative-import-location`) and `qt5-plugin-geoservices-osm` (`SailfishRpmRequires`), which
are in the `jolla` repository: the phone's package manager installs them with the app. Both, the `QtLocation 5.0`
import and `libQt5Location` are on the Jolla Store's allowed lists, so Harbour builds can use the map too.

OpenStreetMap's [tile usage policy](https://operations.osmfoundation.org/policies/tiles/) applies: the adapter
identifies itself with a User-Agent and shows "© OpenStreetMap contributors" on every map; no bulk downloads.
