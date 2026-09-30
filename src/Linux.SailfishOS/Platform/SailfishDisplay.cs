namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// dp to pixel mapping. Density is pixelWidth / 540, like Silica's own scaling, so MAUI dp match native control sizes.
/// Override with MAUI_SAILFISH_DENSITY (e.g. "1.0" for raw px).
/// </summary>
public static class SailfishDisplay
{
	/// <summary>Reference logical screen width of Sailfish Silica UIs, in dp.</summary>
	private const double ReferenceLogicalWidth = 540.0;

	private static readonly double? DensityOverride = ReadDensityOverride();

	/// <summary>Physical pixels per dp (1.0 until <see cref="Update"/> runs).</summary>
	public static double Density { get; private set; } = 1.0;

	/// <summary>Window surface width in physical pixels.</summary>
	public static int PixelWidth { get; private set; }

	/// <summary>Window surface height in physical pixels.</summary>
	public static int PixelHeight { get; private set; }

	/// <summary>Window width in dp (MAUI layout units).</summary>
	public static int LogicalWidth { get; private set; }

	/// <summary>Window height in dp (MAUI layout units).</summary>
	public static int LogicalHeight { get; private set; }

	/// <summary>Recomputes density and dp sizes from the surface size in pixels (on creation and every resize).</summary>
	public static void Update(int pixelWidth, int pixelHeight)
	{
		if (pixelWidth <= 0 || pixelHeight <= 0)
			return;

		var changed = pixelWidth != PixelWidth || pixelHeight != PixelHeight;
		PixelWidth = pixelWidth;
		PixelHeight = pixelHeight;
		Density = DensityOverride ?? Math.Clamp(pixelWidth / ReferenceLogicalWidth, 1.0, 4.0);
		LogicalWidth = Math.Max(1, (int)Math.Round(pixelWidth / Density));
		LogicalHeight = Math.Max(1, (int)Math.Round(pixelHeight / Density));
		if (changed)
			Changed?.Invoke();
	}

	/// <summary>Silica's window orientation. The Qt surface stays portrait while Silica rotates its content, so
	/// the sizes above are portrait ones and DeviceDisplay swaps them for a landscape window.</summary>
	public static SailfishOrientation Orientation { get; private set; } = SailfishOrientation.Portrait;

	internal static void SetOrientation(SailfishOrientation orientation)
	{
		if (orientation == Orientation)
			return;
		Orientation = orientation;
		Changed?.Invoke();
	}

	/// <summary>The surface size or the orientation changed (drives DeviceDisplay.MainDisplayInfoChanged).</summary>
	public static event Action? Changed;

	/// <summary>Scales a dp size to physical pixels (min 1 px).</summary>
	public static int ToPixels(double dp) => Math.Max(1, (int)Math.Round(dp * Density));

	/// <summary>Converts a physical pixel size to dp.</summary>
	public static int ToLogical(int px) => (int)Math.Round(px / Density);

	private static double? ReadDensityOverride()
	{
		var raw = SailfishEnv.Get("MAUI_SAILFISH_DENSITY");
		if (double.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var value) && value > 0)
			return value;
		return null;
	}
}
