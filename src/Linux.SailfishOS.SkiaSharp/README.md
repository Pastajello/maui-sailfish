# Microsoft.Maui.SailfishOS.SkiaSharp

Sailfish OS handlers for SkiaSharp's MAUI views. These are the counterpart of the per-platform handlers that
`SkiaSharp.Views.Maui.Core` ships for Android, iOS and Windows, built on the Sailfish backend's drawing surface.

Reference it in the Sailfish head next to SkiaSharp and its Linux native library, in the same version:

```xml
<ItemGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'sailfish'">
  <PackageReference Include="Microsoft.Maui.Platforms.SailfishOS.SkiaSharp" Version="0.1.0" />
  <PackageReference Include="SkiaSharp.NativeAssets.Linux" Version="3.119.4" />  <!-- = the app's SkiaSharp -->
</ItemGroup>
```

The app keeps calling `UseSkiaSharp()`, and no Sailfish code is needed: the package registers itself with the
backend.

- `SKCanvasView`: as on Android. Same `Info`/`RawInfo`, `IgnorePixelScaling`, coalesced `InvalidateSurface`, and
  touch with `Handled`.
- `SKGLView`: draws through the same raster path, so `GRContext` is null.
- Supports SkiaSharp 3.x.
