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
internal static class SailfishEssentials
{
	private static bool _installed;

	/// <summary>Registers every Sailfish Essentials implementation (SailfishEssentialsRegistry) unless the app registered
	/// its own. The container's singleton is the registry's default instance, the one the facades get too.</summary>
	internal static IServiceCollection AddSailfishEssentials(this IServiceCollection services)
	{
		foreach (var entry in SailfishEssentialsRegistry.Entries)
			services.TryAddSingleton(entry.Service, _ => SailfishEssentialsRegistry.DefaultFor(entry));
		return services;
	}

	/// <summary>The statics a MauiProgram reads while it builds the app (FileSystem paths for a log or database file,
	/// Preferences, AppInfo, DeviceInfo), installed before CreateMauiApp: their plain-net defaults throw
	/// (MoneyFox's Serilog setup died on FileSystem.AppDataDirectory). <see cref="Install"/> re-installs them from the
	/// app's services afterwards, so an app's own registration still wins. Only services that need neither the
	/// container nor the Qt loop belong here.</summary>
	public static void InstallEarly()
	{
		foreach (var entry in SailfishEssentialsRegistry.Entries.Where(e => e.Early))
			Hook(entry.Facade, entry.Hook, SailfishEssentialsRegistry.DefaultFor(entry));
	}

	public static void Install(IServiceProvider services)
	{
		if (_installed)
			return;
		_installed = true;
		foreach (var entry in SailfishEssentialsRegistry.Entries)
			Hook(entry.Facade, entry.Hook, services.GetService(entry.Service));
	}

	/// <summary>Installs one more static.</summary>
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
	[DynamicDependency("SetDefault", typeof(Microsoft.Maui.Accessibility.SemanticScreenReader))]
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
	[DynamicDependency("SetDefault", typeof(Flashlight))]
	[DynamicDependency("SetDefault", typeof(Microsoft.Maui.Media.TextToSpeech))]
	[DynamicDependency("SetCurrent", typeof(Microsoft.Maui.Devices.Sensors.Geocoding))]
	[DynamicDependency("SetDefault", typeof(Microsoft.Maui.Authentication.Passkeys))]
	[DynamicDependency("SetCurrent", typeof(AppActions))]
	[DynamicDependency("SetDefault", typeof(Microsoft.Maui.Authentication.WebAuthenticator))]
	[DynamicDependency("SetDefault", typeof(Microsoft.Maui.ApplicationModel.Communication.Contacts))]
	[DynamicDependency("SetDefault", typeof(VersionTracking))]
	[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The hooks are rooted by the DynamicDependency attributes above.")]
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

	/// <summary>The host runs and had its first tick: services may talk to it now.</summary>
	internal static bool IsHostUp => QtHostRuntime.IsRunning && HostReady.Task.IsCompleted;

	/// <summary>First Qt tick: starts the services that need the QML host.</summary>
	internal static void OnHostReady()
	{
		HostReady.TrySetResult();
		SailfishTheme.Start();
		SailfishLayoutDirection.OnHostReady();
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
	private static Task<AppTheme>? _seed;

	/// <summary>The ambience's theme. Before the Qt side is up (the app's constructor, CreateWindow) it is the
	/// ambience's colour scheme as dconf stores it (<see cref="Seed"/>), so code that reads RequestedTheme once at
	/// start gets the real one; Silica's Theme.colorScheme confirms or corrects it when the theme service starts.</summary>
	public static AppTheme Current
	{
		get
		{
			if (_current == AppTheme.Unspecified && _seed is { } seed)
			{
				_seed = null;
				if (seed.Wait(TimeSpan.FromMilliseconds(200)) && seed.Result != AppTheme.Unspecified)
					_current = seed.Result;
			}
			return _current;
		}
	}

	/// <summary>dconf key of the ambience's colour scheme (0 light text on dark, 1 dark text on light), what
	/// Silica's Theme.colorScheme follows.</summary>
	private const string ColorSchemeKey = "/desktop/jolla/theme/color_scheme";

	/// <summary>Starts reading the ambience's colour scheme off the main thread at the very start of the app, while
	/// MAUI builds the app (a dconf read takes about a millisecond on the phone).</summary>
	internal static void Seed() => _seed ??= Task.Run(ReadColorScheme);

	private static AppTheme ReadColorScheme()
	{
		var clock = System.Diagnostics.Stopwatch.StartNew();
		var theme = ReadColorSchemeCore();
		Console.Error.WriteLine($"[Sailfish] theme: seeded {theme} from dconf in {clock.ElapsedMilliseconds} ms");
		return theme;
	}

	private static AppTheme ReadColorSchemeCore()
	{
		try
		{
			var start = new System.Diagnostics.ProcessStartInfo("dconf", "read " + ColorSchemeKey)
			{
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
			};
			using var process = System.Diagnostics.Process.Start(start);
			if (process is null)
				return AppTheme.Unspecified;
			var output = process.StandardOutput.ReadToEnd();
			if (!process.WaitForExit(1000))
				return AppTheme.Unspecified;
			return ParseColorScheme(output);
		}
		catch (Exception)
		{
			return AppTheme.Unspecified;   // no dconf (desktop host, a sandbox without it): the theme service answers
		}
	}

	/// <summary>dconf's answer for the colour scheme key ("0", "1", or empty when unset: Silica's default is light on
	/// dark).</summary>
	internal static AppTheme ParseColorScheme(string? dconf) => (dconf ?? string.Empty).Trim() switch
	{
		"1" => AppTheme.Light,
		"0" or "" => AppTheme.Dark,
		_ => AppTheme.Unspecified,
	};

	/// <summary>Tests: the action puts the current theme back.</summary>
	internal static Action CaptureForTests()
	{
		var current = _current;
		var seed = _seed;
		var colors = (HighlightColor, PrimaryColor, SecondaryColor, SecondaryHighlightColor);
		return () =>
		{
			_current = current;
			_seed = seed;
			(HighlightColor, PrimaryColor, SecondaryColor, SecondaryHighlightColor) = colors;
		};
	}

	/// <summary>Tests: a seed that answers <paramref name="theme"/>.</summary>
	internal static void SeedForTests(AppTheme theme)
	{
		_current = AppTheme.Unspecified;
		_seed = Task.FromResult(theme);
	}

	internal static void Start() =>
		QtHostServices.Ensure(Service, """
			import QtQuick 2.6
			import Sailfish.Silica 1.0
			QtObject {
			    property bool light: Theme.colorScheme === Theme.DarkOnLight
			    onLightChanged: window.mauiAppNotify("svc-theme-changed", JSON.stringify({ light: light }))
			    property string palette: Theme.highlightColor + "|" + Theme.primaryColor + "|" + Theme.secondaryColor + "|" + Theme.secondaryHighlightColor
			    onPaletteChanged: window.mauiAppNotify("svc-theme-palette", JSON.stringify({ palette: palette }))
			}
			""",
			() =>
			{
				ApplyPalette(QtHostServices.Eval(Service, "s.palette"));
				Apply(QtHostServices.Eval(Service, "s.light") == "true" ? AppTheme.Light : AppTheme.Dark);
			},
			(ShellEvents.ThemeChanged, e => Apply(ThemeOf(ThemeChangedPayload.Parse(e).Scheme))),
			(ShellEvents.ThemePalette, e => ApplyPalette(
				e.ValueKind == JsonValueKind.Object && e.TryGetProperty("palette", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null)));

	/// <summary>The ambience's highlight colour (Silica Theme.highlightColor): what Silica controls use for selection,
	/// focus and pressed states. Null until the theme service started.</summary>
	public static Color? HighlightColor { get; private set; }

	/// <summary>The ambience's primary text colour (Theme.primaryColor).</summary>
	public static Color? PrimaryColor { get; private set; }

	/// <summary>The ambience's secondary text colour (Theme.secondaryColor).</summary>
	public static Color? SecondaryColor { get; private set; }

	/// <summary>The ambience's secondary highlight colour (Theme.secondaryHighlightColor).</summary>
	public static Color? SecondaryHighlightColor { get; private set; }

	/// <summary>Raised (main thread) when the ambience's colours changed: another ambience, even one with the same
	/// light/dark scheme (which <see cref="IApplication.ThemeChanged"/> would not report).</summary>
	public static event Action? ColorsChanged;

	/// <summary>"highlight|primary|secondary|secondaryHighlight", Qt colour strings.</summary>
	internal static void ApplyPalette(string? palette)
	{
		var parts = (palette ?? string.Empty).Split('|');
		if (parts.Length != 4)
			return;
		Color? Parse(string text) => text.StartsWith('#') ? Color.FromArgb(text) : null;
		var (highlight, primary, secondary, secondaryHighlight) = (Parse(parts[0]), Parse(parts[1]), Parse(parts[2]), Parse(parts[3]));
		if (Equals(highlight, HighlightColor) && Equals(primary, PrimaryColor) && Equals(secondary, SecondaryColor) &&
		    Equals(secondaryHighlight, SecondaryHighlightColor))
			return;
		(HighlightColor, PrimaryColor, SecondaryColor, SecondaryHighlightColor) = (highlight, primary, secondary, secondaryHighlight);
		ColorsChanged?.Invoke();
	}

	/// <summary>The MAUI theme of a Silica colour scheme: dark text on light is Light.</summary>
	internal static AppTheme ThemeOf(SailfishColorScheme scheme) =>
		scheme == SailfishColorScheme.DarkOnLight ? AppTheme.Light : AppTheme.Dark;

	/// <summary>Raised (Qt thread) after <see cref="Current"/> changed, the first read included: the application's
	/// OnColorSchemeChanged rides this, so AppInfo.RequestedTheme is already the new one there (W1.9).</summary>
	internal static event Action<AppTheme>? Changed;

	internal static void Apply(AppTheme theme)
	{
		if (theme == _current)
			return;
		_current = theme;
		Console.Error.WriteLine($"[Sailfish] theme: ambience → {theme}");
		(Microsoft.Maui.Controls.Application.Current as IApplication)?.ThemeChanged();
		Changed?.Invoke(theme);
	}
}

/// <summary>AppInfo.RequestedLayoutDirection: Qt's application layout direction (from the locale), read once the host
/// runs; the current UI culture before.</summary>
internal static class SailfishLayoutDirection
{
	private static LayoutDirection? _qt;

	internal static LayoutDirection Current =>
		_qt ?? (CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft ? LayoutDirection.RightToLeft : LayoutDirection.LeftToRight);

	internal static void OnHostReady() => _qt = Parse(QtHostRuntime.Eval("Qt.application.layoutDirection"));

	/// <summary>Qt.LeftToRight is 0, Qt.RightToLeft 1; anything else (no answer) leaves the culture's.</summary>
	internal static LayoutDirection? Parse(string? qt) => qt?.Trim() switch
	{
		"1" => LayoutDirection.RightToLeft,
		"0" => LayoutDirection.LeftToRight,
		_ => null,
	};

	/// <summary>Tests: the action puts the Qt answer back.</summary>
	internal static Action CaptureForTests()
	{
		var qt = _qt;
		return () => _qt = qt;
	}

	internal static void SetForTests(LayoutDirection? qt) => _qt = qt;
}

/// <summary>Device info from /etc/hw-release and /etc/sailfish-release.</summary>
internal sealed class SailfishDeviceInfo : IDeviceInfo
{
	public static DevicePlatform SailfishOS => SailfishPlatform.DevicePlatform;

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
internal sealed class SailfishDeviceDisplay : IDeviceDisplay
{
	private const string Service = "display";
	private bool _keepScreenOn;

	public SailfishDeviceDisplay() =>
		SailfishDisplay.Changed += () => MainDisplayInfoChanged?.Invoke(this, new DisplayInfoChangedEventArgs(MainDisplayInfo));

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
			return new DisplayInfo(w, h, SailfishDisplay.Density, orientation, rotation, RefreshRate());
		}
	}

	private static float s_refreshRate;

	/// <summary>The screen's refresh rate (QScreen::refreshRate through the shim's screen info), read once; 60 until
	/// the shim answers.</summary>
	internal static float RefreshRate()
	{
		if (s_refreshRate > 0)
			return s_refreshRate;
		var rate = ParseRefreshRate(QtHostRuntime.IsRunning ? QtThread.Run(QtHostRuntime.ScreenInfo) : string.Empty);
		if (rate > 0)
			s_refreshRate = rate;
		return rate > 0 ? rate : 60f;
	}

	internal static float ParseRefreshRate(string screenInfo)
	{
		if (string.IsNullOrEmpty(screenInfo))
			return 0;
		try
		{
			using var doc = System.Text.Json.JsonDocument.Parse(screenInfo);
			return doc.RootElement.TryGetProperty("screen", out var screen) &&
			       screen.TryGetProperty("refreshRate", out var rate) && rate.TryGetDouble(out var hz) && hz > 0
				? (float)hz
				: 0;
		}
		catch (System.Text.Json.JsonException)
		{
			return 0;
		}
	}

	public event EventHandler<DisplayInfoChangedEventArgs>? MainDisplayInfoChanged;
}
