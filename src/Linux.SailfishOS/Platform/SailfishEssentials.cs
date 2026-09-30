using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Devices;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Microsoft.Maui.Storage;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Sailfish Essentials behind MAUI's statics (<c>Preferences.Default</c>, <c>DeviceInfo.Current</c>, ...), which
/// otherwise throw on net11.0. With <c>UseMauiAppSailfish</c> they are DI services that MAUI's own bridge installs
/// during Build (<see cref="AddSailfishEssentials"/>); <see cref="Install"/> covers plain <c>UseMauiApp</c> through
/// the internal SetDefault/SetCurrent hooks (reflection, kept by DynamicDependency). App-registered services win.
/// </summary>
public static class SailfishEssentials
{
	private static bool _installed;

	/// <summary>Registers every Sailfish Essentials implementation unless the app registered its own.</summary>
	internal static IServiceCollection AddSailfishEssentials(this IServiceCollection services)
	{
		services.TryAddSingleton<IClipboard, SailfishClipboard>();
		services.TryAddSingleton<IBrowser, SailfishBrowser>();
		services.TryAddSingleton<ILauncher, SailfishLauncher>();
		services.TryAddSingleton<IAppInfo, SailfishAppInfo>();
		services.TryAddSingleton<IPreferences, SailfishPreferences>();
		services.TryAddSingleton<ISecureStorage, SailfishSecureStorage>();
		services.TryAddSingleton<IFileSystem, SailfishFileSystem>();
		services.TryAddSingleton<IDeviceInfo, SailfishDeviceInfo>();
		services.TryAddSingleton<IDeviceDisplay, SailfishDeviceDisplay>();
		services.TryAddSingleton<IBattery, SailfishBattery>();
		services.TryAddSingleton<Microsoft.Maui.Networking.IConnectivity, SailfishConnectivity>();
		services.TryAddSingleton<IVibration, SailfishVibration>();
		services.TryAddSingleton<IHapticFeedback, SailfishHapticFeedback>();
		services.TryAddSingleton<Microsoft.Maui.Devices.Sensors.IAccelerometer, SailfishAccelerometer>();
		services.TryAddSingleton<Microsoft.Maui.Devices.Sensors.IGyroscope, SailfishGyroscope>();
		services.TryAddSingleton<Microsoft.Maui.Devices.Sensors.IMagnetometer, SailfishMagnetometer>();
		services.TryAddSingleton<Microsoft.Maui.Devices.Sensors.ICompass, SailfishCompass>();
		services.TryAddSingleton<Microsoft.Maui.Devices.Sensors.IBarometer, SailfishBarometer>();
		services.TryAddSingleton<Microsoft.Maui.Devices.Sensors.IOrientationSensor, SailfishOrientationSensor>();
		services.TryAddSingleton<Microsoft.Maui.Devices.Sensors.IGeolocation, SailfishGeolocation>();
		services.TryAddSingleton<IShare, SailfishShare>();
		services.TryAddSingleton<IPermissions, SailfishPermissions>();
		services.TryAddSingleton<Microsoft.Maui.Media.IScreenshot, SailfishScreenshot>();
		// One instance each serves several interfaces.
		services.TryAddSingleton<SailfishPickers>();
		services.TryAddSingleton<Microsoft.Maui.Media.IMediaPicker>(sp => sp.GetRequiredService<SailfishPickers>());
		services.TryAddSingleton<IFilePicker>(sp => sp.GetRequiredService<SailfishPickers>());
		services.TryAddSingleton<SailfishCommunication>();
		services.TryAddSingleton<Microsoft.Maui.ApplicationModel.Communication.IPhoneDialer>(sp => sp.GetRequiredService<SailfishCommunication>());
		services.TryAddSingleton<Microsoft.Maui.ApplicationModel.Communication.IEmail>(sp => sp.GetRequiredService<SailfishCommunication>());
		services.TryAddSingleton<Microsoft.Maui.ApplicationModel.Communication.ISms>(sp => sp.GetRequiredService<SailfishCommunication>());
		services.TryAddSingleton<IMap>(sp => sp.GetRequiredService<SailfishCommunication>());
		return services;
	}

	[DynamicDependency("SetDefault", typeof(Clipboard))]
	[DynamicDependency("SetDefault", typeof(Preferences))]
	[DynamicDependency("SetDefault", typeof(SecureStorage))]
	[DynamicDependency("SetCurrent", typeof(FileSystem))]
	[DynamicDependency("SetDefault", typeof(Browser))]
	[DynamicDependency("SetDefault", typeof(Launcher))]
	[DynamicDependency("SetCurrent", typeof(AppInfo))]
	[DynamicDependency("SetCurrent", typeof(DeviceInfo))]
	[DynamicDependency("SetCurrent", typeof(DeviceDisplay))]
	[DynamicDependency("SetDefault", typeof(Battery))]
	[DynamicDependency("SetCurrent", typeof(Microsoft.Maui.Networking.Connectivity))]
	[DynamicDependency("SetDefault", typeof(Vibration))]
	[DynamicDependency("SetDefault", typeof(HapticFeedback))]
	[DynamicDependency("SetDefault", typeof(Microsoft.Maui.Devices.Sensors.Accelerometer))]
	[DynamicDependency("SetDefault", typeof(Microsoft.Maui.Devices.Sensors.Gyroscope))]
	[DynamicDependency("SetDefault", typeof(Microsoft.Maui.Devices.Sensors.Magnetometer))]
	[DynamicDependency("SetDefault", typeof(Microsoft.Maui.Devices.Sensors.Compass))]
	[DynamicDependency("SetDefault", typeof(Microsoft.Maui.Devices.Sensors.Barometer))]
	[DynamicDependency("SetDefault", typeof(Microsoft.Maui.Devices.Sensors.OrientationSensor))]
	[DynamicDependency("SetDefault", typeof(Microsoft.Maui.Devices.Sensors.Geolocation))]
	[DynamicDependency("SetDefault", typeof(Share))]
	[DynamicDependency("SetDefault", typeof(Microsoft.Maui.Media.MediaPicker))]
	[DynamicDependency("SetDefault", typeof(FilePicker))]
	[DynamicDependency("SetCurrent", typeof(Permissions))]
	[DynamicDependency("SetDefault", typeof(Microsoft.Maui.ApplicationModel.Communication.PhoneDialer))]
	[DynamicDependency("SetDefault", typeof(Microsoft.Maui.ApplicationModel.Communication.Email))]
	[DynamicDependency("SetDefault", typeof(Microsoft.Maui.ApplicationModel.Communication.Sms))]
	[DynamicDependency("SetDefault", typeof(Map))]
	[DynamicDependency("SetDefault", typeof(Microsoft.Maui.Media.Screenshot))]
	[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The hooks are rooted by the DynamicDependency attributes above.")]
	public static void Install(IServiceProvider services)
	{
		if (_installed)
			return;
		_installed = true;
		Hook(typeof(Clipboard), "SetDefault", services.GetService(typeof(IClipboard)));
		Hook(typeof(Preferences), "SetDefault", services.GetService(typeof(IPreferences)));
		Hook(typeof(SecureStorage), "SetDefault", services.GetService(typeof(ISecureStorage)));
		Hook(typeof(FileSystem), "SetCurrent", services.GetService(typeof(IFileSystem)));
		Hook(typeof(Browser), "SetDefault", services.GetService(typeof(IBrowser)));
		Hook(typeof(Launcher), "SetDefault", services.GetService(typeof(ILauncher)));
		Hook(typeof(AppInfo), "SetCurrent", services.GetService(typeof(IAppInfo)));
		Hook(typeof(DeviceInfo), "SetCurrent", services.GetService(typeof(IDeviceInfo)));
		Hook(typeof(DeviceDisplay), "SetCurrent", services.GetService(typeof(IDeviceDisplay)));
		Hook(typeof(Battery), "SetDefault", services.GetService(typeof(IBattery)));
		Hook(typeof(Microsoft.Maui.Networking.Connectivity), "SetCurrent", services.GetService(typeof(Microsoft.Maui.Networking.IConnectivity)));
		Hook(typeof(Vibration), "SetDefault", services.GetService(typeof(IVibration)));
		Hook(typeof(HapticFeedback), "SetDefault", services.GetService(typeof(IHapticFeedback)));
		Hook(typeof(Microsoft.Maui.Devices.Sensors.Accelerometer), "SetDefault", services.GetService(typeof(Microsoft.Maui.Devices.Sensors.IAccelerometer)));
		Hook(typeof(Microsoft.Maui.Devices.Sensors.Gyroscope), "SetDefault", services.GetService(typeof(Microsoft.Maui.Devices.Sensors.IGyroscope)));
		Hook(typeof(Microsoft.Maui.Devices.Sensors.Magnetometer), "SetDefault", services.GetService(typeof(Microsoft.Maui.Devices.Sensors.IMagnetometer)));
		Hook(typeof(Microsoft.Maui.Devices.Sensors.Compass), "SetDefault", services.GetService(typeof(Microsoft.Maui.Devices.Sensors.ICompass)));
		Hook(typeof(Microsoft.Maui.Devices.Sensors.Barometer), "SetDefault", services.GetService(typeof(Microsoft.Maui.Devices.Sensors.IBarometer)));
		Hook(typeof(Microsoft.Maui.Devices.Sensors.OrientationSensor), "SetDefault", services.GetService(typeof(Microsoft.Maui.Devices.Sensors.IOrientationSensor)));
		Hook(typeof(Microsoft.Maui.Devices.Sensors.Geolocation), "SetDefault", services.GetService(typeof(Microsoft.Maui.Devices.Sensors.IGeolocation)));
		Hook(typeof(Share), "SetDefault", services.GetService(typeof(IShare)));
		Hook(typeof(Microsoft.Maui.Media.MediaPicker), "SetDefault", services.GetService(typeof(Microsoft.Maui.Media.IMediaPicker)));
		Hook(typeof(FilePicker), "SetDefault", services.GetService(typeof(IFilePicker)));
		Hook(typeof(Permissions), "SetCurrent", services.GetService(typeof(IPermissions)));
		Hook(typeof(Microsoft.Maui.ApplicationModel.Communication.PhoneDialer), "SetDefault", services.GetService(typeof(Microsoft.Maui.ApplicationModel.Communication.IPhoneDialer)));
		Hook(typeof(Microsoft.Maui.ApplicationModel.Communication.Email), "SetDefault", services.GetService(typeof(Microsoft.Maui.ApplicationModel.Communication.IEmail)));
		Hook(typeof(Microsoft.Maui.ApplicationModel.Communication.Sms), "SetDefault", services.GetService(typeof(Microsoft.Maui.ApplicationModel.Communication.ISms)));
		Hook(typeof(Map), "SetDefault", services.GetService(typeof(IMap)));
		Hook(typeof(Microsoft.Maui.Media.Screenshot), "SetDefault", services.GetService(typeof(Microsoft.Maui.Media.IScreenshot)));
	}

	/// <summary>Installs one more static.</summary>
	internal static void Hook(
		[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.NonPublicMethods)] Type facade, string method, object? implementation)
	{
		if (implementation is null)
			return;
		try
		{
			var hook = facade.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
			if (hook is null)
			{
				Console.Error.WriteLine($"[Sailfish] essentials: {facade.Name}.{method} not found — static stays on MAUI's default");
				return;
			}
			hook.Invoke(null, new[] { implementation });
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[Sailfish] essentials: {facade.Name}.{method} failed: {ex.GetBaseException().Message}");
		}
	}

	/// <summary>Completes on the first Qt tick, for services that need QCoreApplication (QtDBus).</summary>
	internal static readonly TaskCompletionSource HostReady = new(TaskCreationOptions.RunContinuationsAsynchronously);

	/// <summary>First Qt tick: starts the services that need the QML host.</summary>
	internal static void OnHostReady()
	{
		HostReady.TrySetResult();
		SailfishTheme.Start();
		SailfishDeviceDisplay.Start();
		SailfishCover.OnHostReady();
		(IPlatformApplication.Current as SailfishMauiApplication)?.StartSystemService();
		SailfishOpenUrl.OnHostReady();
		// Battery/Connectivity start on first use.
	}
}

/// <summary>System.Text.Json source generation; the trimmed app disables reflection-based serialization.</summary>
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(float))]
internal sealed partial class SailfishJsonContext : JsonSerializerContext
{
}

/// <summary>
/// AppTheme from the Silica ambience (LightOnDark is Dark); ambience switches reach MAUI through
/// <see cref="IApplication.ThemeChanged"/>.
/// </summary>
public static class SailfishTheme
{
	private const string Service = "theme";
	private static AppTheme _current = AppTheme.Unspecified;

	public static AppTheme Current => _current;

	/// <summary>Theme changes applied (diagnostics).</summary>
	public static int Changes { get; private set; }

	internal static void Start()
	{
		if (!QtHostServices.Ensure(Service, """
			import QtQuick 2.6
			import Sailfish.Silica 1.0
			QtObject {
			    property bool light: Theme.colorScheme === Theme.DarkOnLight
			    onLightChanged: window.mauiAppNotify("svc-theme-changed", JSON.stringify({ light: light }))
			}
			"""))
			return;
		QtHostServices.Subscribe("svc-theme-changed", e =>
			Apply(e.TryGetProperty("light", out var light) && light.ValueKind == JsonValueKind.True ? AppTheme.Light : AppTheme.Dark));
		Apply(QtHostServices.Eval(Service, "s.light") == "true" ? AppTheme.Light : AppTheme.Dark);
	}

	internal static void Apply(AppTheme theme)
	{
		if (theme == _current)
			return;
		_current = theme;
		Changes++;
		Console.Error.WriteLine($"[Sailfish] theme: ambience → {theme}");
		(Microsoft.Maui.Controls.Application.Current as IApplication)?.ThemeChanged();
	}
}

/// <summary>Device info from /etc/hw-release and /etc/sailfish-release.</summary>
public sealed class SailfishDeviceInfo : IDeviceInfo
{
	public static readonly DevicePlatform SailfishOS = DevicePlatform.Create("SailfishOS");

	private static readonly Lazy<Dictionary<string, string>> Hw = new(() => ReadRelease("/etc/hw-release"));
	private static readonly Lazy<Dictionary<string, string>> Os = new(() => ReadRelease("/etc/sailfish-release"));

	internal static Dictionary<string, string> ReadRelease(string path)
	{
		var map = new Dictionary<string, string>(StringComparer.Ordinal);
		try
		{
			foreach (var raw in File.ReadAllLines(path))
			{
				var line = raw.Trim();
				var eq = line.IndexOf('=');
				if (line.StartsWith('#') || eq <= 0)
					continue;
				map[line[..eq]] = line[(eq + 1)..].Trim('"');
			}
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
		return map;
	}

	public string Model => Hw.Value.GetValueOrDefault("NAME") ?? Hw.Value.GetValueOrDefault("MER_HA_DEVICE") ?? "Sailfish device";

	public string Manufacturer
	{
		get
		{
			var vendor = Hw.Value.GetValueOrDefault("MER_HA_VENDOR") ?? string.Empty;
			return vendor.Length == 0 ? "Unknown" : char.ToUpperInvariant(vendor[0]) + vendor[1..];
		}
	}

	public string Name
	{
		get
		{
			try
			{
				var host = File.ReadAllText("/etc/hostname").Trim();
				if (host.Length > 0)
					return host;
			}
			catch (IOException)
			{
			}
			catch (UnauthorizedAccessException)
			{
			}
			return Model;
		}
	}

	public string VersionString => Os.Value.GetValueOrDefault("VERSION_ID") ?? Environment.OSVersion.VersionString;

	public Version Version
	{
		get
		{
			var parts = VersionString.Split('.');
			var numbers = parts.Take(4).Select(p => int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0).ToArray();
			return numbers.Length switch
			{
				>= 4 => new Version(numbers[0], numbers[1], numbers[2], numbers[3]),
				3 => new Version(numbers[0], numbers[1], numbers[2]),
				2 => new Version(numbers[0], numbers[1]),
				_ => new Version(0, 0),
			};
		}
	}

	public DevicePlatform Platform => SailfishOS;

	/// <summary>A short side over 600 dp is a tablet (the Android/iOS convention).</summary>
	public DeviceIdiom Idiom =>
		Math.Min(SailfishDisplay.LogicalWidth, SailfishDisplay.LogicalHeight) >= 600 ? DeviceIdiom.Tablet : DeviceIdiom.Phone;

	/// <summary>The Sailfish SDK emulator reports an "sbj"/"emulator" HA device.</summary>
	public DeviceType DeviceType
	{
		get
		{
			var device = Hw.Value.GetValueOrDefault("MER_HA_DEVICE") ?? string.Empty;
			return device.Contains("emul", StringComparison.OrdinalIgnoreCase) || device.Contains("sbj", StringComparison.OrdinalIgnoreCase)
				? DeviceType.Virtual
				: DeviceType.Physical;
		}
	}
}

/// <summary>Display info from the Qt window; KeepScreenOn via Nemo.KeepAlive (allowed under Sailjail).</summary>
public sealed class SailfishDeviceDisplay : IDeviceDisplay
{
	private const string Service = "display";
	private static SailfishDeviceDisplay? _instance;
	private bool _keepScreenOn;

	public SailfishDeviceDisplay() => _instance ??= this;

	public bool KeepScreenOn
	{
		get => _keepScreenOn;
		set
		{
			_keepScreenOn = value;
			if (QtHostServices.Ensure(Service, DisplayQml))
				QtHostServices.Eval(Service, $"s.preventBlanking = {(value ? "true" : "false")}");
		}
	}

	/// <summary>The native DisplayBlanking state (diagnostics).</summary>
	public static string NativePreventBlanking => QtHostServices.Eval(Service, "s.preventBlanking");

	private const string DisplayQml = """
		import QtQuick 2.6
		import Nemo.KeepAlive 1.2
		DisplayBlanking { preventBlanking: false }
		""";

	public DisplayInfo MainDisplayInfo
	{
		get
		{
			var (rotation, landscape) = SailfishDisplay.Orientation switch
			{
				SailfishOrientation.Landscape => (DisplayRotation.Rotation90, true),
				SailfishOrientation.PortraitInverted => (DisplayRotation.Rotation180, false),
				SailfishOrientation.LandscapeInverted => (DisplayRotation.Rotation270, true),
				_ => (DisplayRotation.Rotation0, false),
			};
			var w = landscape ? SailfishDisplay.PixelHeight : SailfishDisplay.PixelWidth;
			var h = landscape ? SailfishDisplay.PixelWidth : SailfishDisplay.PixelHeight;
			var orientation = w > h ? DisplayOrientation.Landscape : DisplayOrientation.Portrait;
			return new DisplayInfo(w, h, SailfishDisplay.Density, orientation, rotation, 60);
		}
	}

	public event EventHandler<DisplayInfoChangedEventArgs>? MainDisplayInfoChanged;

	internal static void Start() => SailfishDisplay.Changed += () =>
		_instance?.MainDisplayInfoChanged?.Invoke(_instance, new DisplayInfoChangedEventArgs(_instance.MainDisplayInfo));
}
