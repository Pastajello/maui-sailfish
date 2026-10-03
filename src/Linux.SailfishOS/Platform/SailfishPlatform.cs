using Microsoft.Maui.Devices;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>Sailfish OS as MAUI's device APIs name it, for shared code that branches per platform.</summary>
public static class SailfishPlatform
{
	/// <summary><c>DeviceInfo.Platform</c> on Sailfish OS: <c>DeviceInfo.Platform == SailfishPlatform.DevicePlatform</c>,
	/// as apps compare with <c>DevicePlatform.Android</c>.</summary>
	public static DevicePlatform DevicePlatform { get; } = DevicePlatform.Create("SailfishOS");
}
