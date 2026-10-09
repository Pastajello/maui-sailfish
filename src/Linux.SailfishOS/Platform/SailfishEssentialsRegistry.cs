using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.Communication;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Authentication;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Media;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// One row per MAUI Essentials service: the interface, the static facade and its internal installer, and the Sailfish
/// implementation. <see cref="SailfishEssentials.AddSailfishEssentials"/> registers from it, <see cref="SailfishEssentials"/>
/// installs the facades from it and <see cref="SailfishServiceOverlay"/> falls back to it, so adding a service is one
/// row. Every consumer gets the same default instance of a row (<see cref="DefaultFor"/>): the facade installed before
/// CreateMauiApp, the container's singleton and the overlay's fallback are one object.
/// </summary>
/// <param name="Group">Rows of one implementation serving several interfaces (pickers, communication) share it.</param>
/// <param name="Early">Installed before CreateMauiApp (needs neither the container nor the Qt loop).</param>
internal sealed record EssentialsEntry(
	Type Service,
	[property: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.NonPublicMethods)] Type Facade,
	string Hook,
	Func<object> Create,
	string? Group = null,
	bool Early = false);

internal static class SailfishEssentialsRegistry
{
	private static readonly ConcurrentDictionary<string, Lazy<object>> Defaults = new(StringComparer.Ordinal);

	public static readonly IReadOnlyList<EssentialsEntry> Entries =
	[
		new(typeof(IFileSystem), typeof(FileSystem), "SetCurrent", () => new SailfishFileSystem(), Early: true),
		new(typeof(IPreferences), typeof(Preferences), "SetDefault", () => new SailfishPreferences(), Early: true),
		new(typeof(IAppInfo), typeof(AppInfo), "SetCurrent", () => new SailfishAppInfo(), Early: true),
		new(typeof(IDeviceInfo), typeof(DeviceInfo), "SetCurrent", () => new SailfishDeviceInfo(), Early: true),
		new(typeof(IClipboard), typeof(Clipboard), "SetDefault", () => new SailfishClipboard()),
		// Early: a MauiProgram that reads it before Build got MAUI's throwing default (tracker S12); neither needs Qt to exist.
		new(typeof(ISecureStorage), typeof(SecureStorage), "SetDefault", () => new SailfishSecureStorage(), Early: true),
		new(typeof(IBrowser), typeof(Browser), "SetDefault", () => new SailfishBrowser()),
		new(typeof(ILauncher), typeof(Launcher), "SetDefault", () => new SailfishLauncher()),
		new(typeof(IDeviceDisplay), typeof(DeviceDisplay), "SetCurrent", () => new SailfishDeviceDisplay(), Early: true),
		new(typeof(IBattery), typeof(Battery), "SetDefault", () => new SailfishBattery()),
		new(typeof(IConnectivity), typeof(Connectivity), "SetCurrent", () => new SailfishConnectivity()),
		new(typeof(IVibration), typeof(Vibration), "SetDefault", () => new SailfishVibration()),
		new(typeof(IHapticFeedback), typeof(HapticFeedback), "SetDefault", () => new SailfishHapticFeedback()),
		new(typeof(IAccelerometer), typeof(Accelerometer), "SetDefault", () => new SailfishAccelerometer()),
		new(typeof(IGyroscope), typeof(Gyroscope), "SetDefault", () => new SailfishGyroscope()),
		new(typeof(IMagnetometer), typeof(Magnetometer), "SetDefault", () => new SailfishMagnetometer()),
		new(typeof(ICompass), typeof(Compass), "SetDefault", () => new SailfishCompass()),
		new(typeof(IBarometer), typeof(Barometer), "SetDefault", () => new SailfishBarometer()),
		new(typeof(IOrientationSensor), typeof(OrientationSensor), "SetDefault", () => new SailfishOrientationSensor()),
		new(typeof(IGeolocation), typeof(Geolocation), "SetDefault", () => new SailfishGeolocation()),
		new(typeof(IShare), typeof(Share), "SetDefault", () => new SailfishShare()),
		new(typeof(IMediaPicker), typeof(MediaPicker), "SetDefault", () => new SailfishPickers(), Group: "pickers"),
		new(typeof(IFilePicker), typeof(FilePicker), "SetDefault", () => new SailfishPickers(), Group: "pickers"),
		new(typeof(IPermissions), typeof(Permissions), "SetCurrent", () => new SailfishPermissions()),
		new(typeof(IPhoneDialer), typeof(PhoneDialer), "SetDefault", () => new SailfishCommunication(), Group: "communication"),
		new(typeof(IEmail), typeof(Email), "SetDefault", () => new SailfishCommunication(), Group: "communication"),
		new(typeof(ISms), typeof(Sms), "SetDefault", () => new SailfishCommunication(), Group: "communication"),
		new(typeof(IMap), typeof(Map), "SetDefault", () => new SailfishCommunication(), Group: "communication"),
		new(typeof(IScreenshot), typeof(Screenshot), "SetDefault", () => new SailfishScreenshot()),
		new(typeof(IFlashlight), typeof(Flashlight), "SetDefault", () => new SailfishFlashlight()),
		new(typeof(ITextToSpeech), typeof(TextToSpeech), "SetDefault", () => new SailfishTextToSpeech()),
		new(typeof(IGeocoding), typeof(Geocoding), "SetCurrent", () => new SailfishGeocoding()),
		new(typeof(IPasskeys), typeof(Passkeys), "SetDefault", () => new SailfishPasskeys()),
		new(typeof(IAppActions), typeof(AppActions), "SetCurrent", () => new SailfishAppActions()),
		new(typeof(IWebAuthenticator), typeof(WebAuthenticator), "SetDefault", () => new SailfishWebAuthenticator()),
		new(typeof(IContacts), typeof(Contacts), "SetDefault", () => new SailfishContacts()),
		// Nothing in MAUI 11 registers a screen reader; its static default throws on Announce (tracker S12: was a special case).
		new(typeof(Microsoft.Maui.Accessibility.ISemanticScreenReader), typeof(Microsoft.Maui.Accessibility.SemanticScreenReader), "SetDefault",
			() => new SailfishSemanticScreenReader()),
		// MAUI's own implementation (internal, platform-neutral) over the Sailfish Preferences and AppInfo rows; without
		// a row IVersionTracking did not resolve from the services (tracker S09).
		new(typeof(IVersionTracking), typeof(VersionTracking), "SetDefault", CreateVersionTracking),
	];

	/// <summary>MAUI's VersionTrackingImplementation(IPreferences, IAppInfo): internal, so made by reflection (rooted for
	/// the trimmer), over the rows' default Preferences and AppInfo.</summary>
	[DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, "Microsoft.Maui.ApplicationModel.VersionTrackingImplementation", "Microsoft.Maui.Essentials")]
	[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The constructor is rooted by the DynamicDependency above.")]
	[UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "The constructor is rooted by the DynamicDependency above.")]
	private static object CreateVersionTracking()
	{
		var type = typeof(VersionTracking).Assembly.GetType("Microsoft.Maui.ApplicationModel.VersionTrackingImplementation", throwOnError: true)!;
		return Activator.CreateInstance(type, DefaultFor(ByService[typeof(IPreferences)]), DefaultFor(ByService[typeof(IAppInfo)]))!;
	}

	private static readonly Dictionary<Type, EssentialsEntry> ByService = Entries.ToDictionary(e => e.Service);

	/// <summary>The row of <paramref name="service"/>, or null for a service that is not an Essentials one.</summary>
	public static EssentialsEntry? Find(Type service) => ByService.GetValueOrDefault(service);

	/// <summary>The one default instance of a row (shared by its group): created on first use.</summary>
	public static object DefaultFor(EssentialsEntry entry) =>
		Defaults.GetOrAdd(entry.Group ?? entry.Service.FullName!, _ => new Lazy<object>(entry.Create)).Value;
}
