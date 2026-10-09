using System.Text.Json;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// The app-level ("svc-…") events the shell (MauiShell.qml), the inline QML services and the shim send, and the
/// payloads managed reads from them. Every C# subscriber names its event through these constants; the QML and C++
/// emitters keep their literals, and ContractTests checks that each one has a constant here.
/// </summary>
internal static class ShellEvents
{
	public const string Prefix = "svc-";

	// MauiShell.qml
	public const string AppState = "svc-app-state";
	public const string AppOrientation = "svc-app-orientation";
	public const string CoverStatus = "svc-cover-status";
	public const string CoverAction = "svc-cover-action";
	public const string InputMethod = "svc-input-method";
	public const string NavIdle = "svc-nav-idle";
	public const string NavDepth = "svc-nav-depth";
	public const string ClipboardChanged = "svc-clipboard-changed";

	// sailfish_host.cpp (synchronous: the loop is ending)
	public const string AppQuit = "svc-app-quit";

	// Inline QML services (SailfishMauiApplication's MCE service, Essentials)
	public const string Display = "svc-display";
	public const string ScreenLock = "svc-screen-lock";
	public const string MemoryLevel = "svc-memory-level";
	public const string ThemeChanged = "svc-theme-changed";
	public const string ThemePalette = "svc-theme-palette";
	public const string OpenUrl = "svc-open-url";
	public const string BatteryChanged = "svc-battery-changed";
	public const string ConnectivityChanged = "svc-connectivity-changed";
	public const string GeolocationFix = "svc-geolocation-fix";
	public const string GeolocationError = "svc-geolocation-error";
	public const string ContactsPopulated = "svc-contacts-populated";
	public const string ContactsPicked = "svc-contacts-picked";
	public const string PickersResult = "svc-pickers-result";

	/// <summary>A sensor's reading event: <see cref="SensorPrefix"/> + the sensor's service name.</summary>
	public const string SensorPrefix = "svc-sensor-";
}

/// <summary>{"state": Qt.application.state, "active": Qt.application.active} (MauiShell.qml). A payload without
/// "state" reads as Active, as Qt reports a window that never left the foreground.</summary>
internal readonly record struct AppStatePayload(SailfishApplicationState State, bool? Active)
{
	public static AppStatePayload Parse(JsonElement e) => new(
		(SailfishApplicationState)BridgeJson.Int(e, "state", (int)SailfishApplicationState.Active),
		e.ValueKind == JsonValueKind.Object && e.TryGetProperty("active", out var a) && a.ValueKind is JsonValueKind.True or JsonValueKind.False
			? a.ValueKind == JsonValueKind.True
			: null);
}

/// <summary>{"orientation": Silica Orientation flag} (MauiShell.qml).</summary>
internal readonly record struct AppOrientationPayload(SailfishOrientation Orientation)
{
	public static AppOrientationPayload Parse(JsonElement e) =>
		new((SailfishOrientation)BridgeJson.Int(e, "orientation", (int)SailfishOrientation.Portrait));
}

/// <summary>{"status": "active"|"activating"|"deactivating"|"inactive"} (MauiShell.qml cover).</summary>
internal readonly record struct CoverStatusPayload(SailfishCoverStatus Status)
{
	public static CoverStatusPayload Parse(JsonElement e) => new(Text(e, "status") switch
	{
		"active" => SailfishCoverStatus.Active,
		"activating" => SailfishCoverStatus.Activating,
		"deactivating" => SailfishCoverStatus.Deactivating,
		_ => SailfishCoverStatus.Inactive,
	});

	internal static string? Text(JsonElement e, string name) =>
		e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>{"index": cover action index} (MauiShell.qml cover).</summary>
internal readonly record struct CoverActionPayload(int Index)
{
	public static CoverActionPayload Parse(JsonElement e) => new(BridgeJson.Int(e, "index"));
}

/// <summary>{"light": bool} (SailfishTheme's inline service).</summary>
internal readonly record struct ThemeChangedPayload(SailfishColorScheme Scheme)
{
	public static ThemeChangedPayload Parse(JsonElement e) => new(
		e.ValueKind == JsonValueKind.Object && e.TryGetProperty("light", out var l) && l.ValueKind == JsonValueKind.True
			? SailfishColorScheme.DarkOnLight
			: SailfishColorScheme.LightOnDark);
}

/// <summary>{"state": MCE display state 0..2} (the MCE service).</summary>
internal readonly record struct DisplayPayload(SailfishDisplayState State)
{
	public static DisplayPayload Parse(JsonElement e) =>
		new((SailfishDisplayState)Math.Clamp(BridgeJson.Int(e, "state", 2), 0, 2));
}

/// <summary>{"locked": bool} (the MCE service).</summary>
internal readonly record struct ScreenLockPayload(bool Locked)
{
	public static ScreenLockPayload Parse(JsonElement e) =>
		new(e.ValueKind == JsonValueKind.Object && e.TryGetProperty("locked", out var l) && l.ValueKind == JsonValueKind.True);
}

/// <summary>{"level": "normal"|"warning"|"critical"} (the MCE service).</summary>
internal readonly record struct MemoryLevelPayload(SailfishMemoryLevel Level)
{
	public static MemoryLevelPayload Parse(JsonElement e) => new(CoverStatusPayload.Text(e, "level") switch
	{
		"normal" => SailfishMemoryLevel.Normal,
		"warning" => SailfishMemoryLevel.Warning,
		"critical" => SailfishMemoryLevel.Critical,
		_ => SailfishMemoryLevel.Unknown,
	});
}

/// <summary>{"visible": bool, "x", "y", "width", "height"} in scene px (MauiShell.qml input method).</summary>
internal readonly record struct InputMethodPayload(bool Visible, Rect Keyboard)
{
	public static InputMethodPayload Parse(JsonElement e) => new(
		e.ValueKind == JsonValueKind.Object && e.TryGetProperty("visible", out var v) && v.ValueKind == JsonValueKind.True,
		new Rect(BridgeJson.Num(e, "x"), BridgeJson.Num(e, "y"), BridgeJson.Num(e, "width"), BridgeJson.Num(e, "height")));
}
