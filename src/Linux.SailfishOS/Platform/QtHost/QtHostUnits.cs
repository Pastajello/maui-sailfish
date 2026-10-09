using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// The single dp → Qt scene unit conversion point: scene units = dp × Density ÷ devicePixelRatio
/// (dpr is 1 on Sailfish). Values are converted once here; the shim and QML adapters must not scale again.
/// </summary>
internal static class QtHostUnits
{
	/// <summary>Qt devicePixelRatio reported by the host; 1.0 means no HiDPI scaling.</summary>
	public static double DevicePixelRatio { get; internal set; } = 1.0;

	/// <summary>Qt scene units per dp: the factor adapter snapshots scale sizes by (font pixels, stroke widths,
	/// spacing), so props and geometry use one conversion.</summary>
	public static double ScenePerDp => SailfishDisplay.Density / DevicePixelRatio;

	/// <summary>dp → Qt scene units.</summary>
	public static double ToQtUnits(double dp) => dp * ScenePerDp;

	/// <summary>Converts a dp rectangle (root space) to Qt scene units.</summary>
	public static Rect ToQtUnits(Rect dp) =>
		new Rect(ToQtUnits(dp.X), ToQtUnits(dp.Y), ToQtUnits(dp.Width), ToQtUnits(dp.Height));

	/// <summary>Qt scene units → dp (readback/diagnostics inverse of <see cref="ToQtUnits(double)"/>).</summary>
	public static double ToLogical(double qtUnits) => qtUnits * DevicePixelRatio / SailfishDisplay.Density;

	/// <summary>Converts scene geometry read from the shim back to dp (root space).</summary>
	public static Rect ToLogical(NativeGeometry scene) =>
		new Rect(ToLogical(scene.X), ToLogical(scene.Y), ToLogical(scene.Width), ToLogical(scene.Height));
}
