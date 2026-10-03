using System.Text.Json;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Networking;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Battery on Nemo.Mce; changes arrive on the service channel and readers get the last snapshot.
/// </summary>
internal sealed class SailfishBattery : IBattery
{
	private const string Service = "battery";
	private double _level = -1;
	private BatteryState _state = BatteryState.Unknown;
	private BatteryPowerSource _source = BatteryPowerSource.Unknown;
	private EnergySaverStatus _saver = EnergySaverStatus.Unknown;
	private bool _started;

	private const string Qml = """
		import QtQuick 2.6
		import Nemo.Mce 1.0
		Item {
		    // (no "state" names here: Item already has a state property)
		    property int batteryPercent: lvl.valid ? lvl.percent : -1
		    property int batteryState: bst.value
		    property int chargerType: chg.type
		    property bool cableConnected: cab.connected
		    property bool powerSaving: psm.active
		    MceBatteryLevel { id: lvl }
		    MceBatteryState { id: bst }
		    MceChargerType { id: chg }
		    MceCableState { id: cab }
		    McePowerSaveMode { id: psm }
		    function snapshot() {
		        return JSON.stringify({ percent: batteryPercent, state: batteryState,
		                                charger: chargerType, cable: cableConnected, saving: powerSaving });
		    }
		    function report() { window.mauiAppNotify("svc-battery-changed", snapshot()); }
		    onBatteryPercentChanged: report()
		    onBatteryStateChanged: report()
		    onChargerTypeChanged: report()
		    onCableConnectedChanged: report()
		    onPowerSavingChanged: report()
		}
		""";

	/// <summary>Started on first use. MCE answers asynchronously, so the first read comes from the kernel until it does.</summary>
	internal void Start() => QtThread.Run(() =>
	{
		if (_started)
			return;
		if (!QtHostServices.Ensure(Service, Qml))
		{
			// MCE missing while the host runs: the kernel's power_supply is the answer from now on.
			if (QtHostServices.IsUnavailable(Service))
			{
				_started = true;
				ReadKernel();
			}
			return;
		}
		_started = true;
		QtHostServices.Subscribe(ShellEvents.BatteryChanged, Apply);
		var snapshot = QtHostServices.Eval(Service, "s.snapshot()");
		if (snapshot.Length > 0)
		{
			using var doc = JsonDocument.Parse(snapshot);
			if (doc.RootElement.TryGetProperty("percent", out var p) && p.GetInt32() >= 0)
				Apply(doc.RootElement.Clone());
			else
				ReadKernel();
		}
	});

	/// <summary>Reads battery state and the online supply from /sys/class/power_supply.</summary>
	private void ReadKernel()
	{
		const string root = "/sys/class/power_supply";
		string? Read(string path)
		{
			try
			{
				return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
			}
			catch (IOException)
			{
				return null;
			}
			catch (UnauthorizedAccessException)
			{
				return null;
			}
		}
		if (int.TryParse(Read(Path.Combine(root, "battery", "capacity")), out var capacity))
			_level = Math.Clamp(capacity / 100.0, 0, 1);
		_state = Read(Path.Combine(root, "battery", "status")) switch
		{
			"Charging" => BatteryState.Charging,
			"Discharging" => BatteryState.Discharging,
			"Full" => BatteryState.Full,
			"Not charging" => BatteryState.NotCharging,
			_ => BatteryState.Unknown,
		};
		_source = BatteryPowerSource.Battery;
		try
		{
			foreach (var supply in Directory.GetDirectories(root))
			{
				if (Read(Path.Combine(supply, "online")) != "1")
					continue;
				var type = Read(Path.Combine(supply, "type"));
				if (type is "USB" or "USB_DCP" or "USB_CDP")
					_source = BatteryPowerSource.Usb;
				else if (type == "Mains")
					_source = BatteryPowerSource.AC;
				else if (type == "Wireless")
					_source = BatteryPowerSource.Wireless;
			}
		}
		catch (IOException)
		{
		}
		if (_state is BatteryState.Charging or BatteryState.Full && _source == BatteryPowerSource.Battery)
			_source = BatteryPowerSource.Usb;   // charging implies a charger even when the supply hides "online"
		_saver = EnergySaverStatus.Off;
	}

	private void Apply(JsonElement e)
	{
		var oldSaver = _saver;
		var percent = e.TryGetProperty("percent", out var p) ? p.GetInt32() : -1;
		_level = percent < 0 ? -1 : Math.Clamp(percent / 100.0, 0, 1);
		_state = (e.TryGetProperty("state", out var s) ? s.GetInt32() : 0) switch
		{
			1 => BatteryState.Charging,
			2 => BatteryState.Discharging,
			3 => BatteryState.NotCharging,
			4 => BatteryState.Full,
			_ => BatteryState.Unknown,
		};
		var cable = e.TryGetProperty("cable", out var c) && c.ValueKind == JsonValueKind.True;
		_source = !cable ? BatteryPowerSource.Battery : (e.TryGetProperty("charger", out var t) ? t.GetInt32() : 0) switch
		{
			1 or 4 => BatteryPowerSource.Usb,               // USB / CDP
			2 or 3 => BatteryPowerSource.AC,                // wall chargers
			5 => BatteryPowerSource.Wireless,
			_ => BatteryPowerSource.Unknown,
		};
		_saver = e.TryGetProperty("saving", out var sv) && sv.ValueKind == JsonValueKind.True ? EnergySaverStatus.On : EnergySaverStatus.Off;
		BatteryInfoChanged?.Invoke(this, new BatteryInfoChangedEventArgs(_level, _state, _source));
		if (oldSaver != _saver && oldSaver != EnergySaverStatus.Unknown)
			EnergySaverStatusChanged?.Invoke(this, new EnergySaverStatusChangedEventArgs(_saver));
	}

	public double ChargeLevel { get { Start(); return _level; } }

	public BatteryState State { get { Start(); return _state; } }

	public BatteryPowerSource PowerSource { get { Start(); return _source; } }

	public EnergySaverStatus EnergySaverStatus { get { Start(); return _saver; } }

	public event EventHandler<BatteryInfoChangedEventArgs>? BatteryInfoChanged;

	public event EventHandler<EnergySaverStatusChangedEventArgs>? EnergySaverStatusChanged;
}

/// <summary>
/// Connectivity on Connman's NetworkManager: manager state plus the default route's technology.
/// </summary>
internal sealed class SailfishConnectivity : IConnectivity
{
	private const string Service = "connectivity";
	private NetworkAccess _access = NetworkAccess.Unknown;
	private ConnectionProfile[] _profiles = Array.Empty<ConnectionProfile>();
	private bool _started;

	private const string Qml = """
		import QtQuick 2.6
		import MeeGo.Connman 0.2
		NetworkManager {
		    id: manager
		    function snapshot() {
		        var route = manager.defaultRoute;
		        return JSON.stringify({ state: String(manager.state),
		                                type: route && route.type !== undefined ? String(route.type) : "" });
		    }
		    function report() { window.mauiAppNotify("svc-connectivity-changed", snapshot()); }
		    onStateChanged: report()
		    onDefaultRouteChanged: report()
		}
		""";

	/// <summary>Started on first use, since NetworkManager follows all Connman D-Bus traffic.</summary>
	internal void Start() => QtThread.Run(() =>
	{
		if (_started)
			return;
		if (!QtHostServices.Ensure(Service, Qml))
			return;
		_started = true;
		QtHostServices.Subscribe(ShellEvents.ConnectivityChanged, e => Apply(e, raise: true));
		var snapshot = QtHostServices.Eval(Service, "s.snapshot()");
		if (snapshot.Length > 0)
		{
			using var doc = JsonDocument.Parse(snapshot);
			Apply(doc.RootElement.Clone(), raise: false);
		}
		// Connman's first answer can say offline for seconds on an online phone, so kernel routes decide until it changes.
		if (_access != NetworkAccess.Internet)
			ReadInterfaces();
	});

	/// <summary>Fallback: an up interface with a gateway means Internet; its name gives the technology.</summary>
	private void ReadInterfaces()
	{
		try
		{
			var profiles = new List<ConnectionProfile>();
			var routed = false;
			foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
			{
				if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up ||
				    nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
					continue;
				var gateway = nic.GetIPProperties().GatewayAddresses.Count > 0;
				routed |= gateway;
				var name = nic.Name;
				var profile = name.StartsWith("wlan", StringComparison.Ordinal) ? ConnectionProfile.WiFi
					: name.StartsWith("rmnet", StringComparison.Ordinal) || name.StartsWith("ccmni", StringComparison.Ordinal) || name.StartsWith("wwan", StringComparison.Ordinal) ? ConnectionProfile.Cellular
					: name.StartsWith("eth", StringComparison.Ordinal) ? ConnectionProfile.Ethernet
					: name.StartsWith("bnep", StringComparison.Ordinal) ? ConnectionProfile.Bluetooth
					: ConnectionProfile.Unknown;
				if (gateway && profile != ConnectionProfile.Unknown && !profiles.Contains(profile))
					profiles.Add(profile);
			}
			_access = routed ? NetworkAccess.Internet : NetworkAccess.None;
			_profiles = profiles.ToArray();
		}
		catch (System.Net.NetworkInformation.NetworkInformationException)
		{
		}
	}

	private void Apply(JsonElement e, bool raise)
	{
		var state = e.TryGetProperty("state", out var s) ? s.GetString() ?? string.Empty : string.Empty;
		var type = e.TryGetProperty("type", out var t) ? t.GetString() ?? string.Empty : string.Empty;
		var access = state switch
		{
			"online" => NetworkAccess.Internet,
			// Connected but Connman's online check has not passed yet.
			"ready" => NetworkAccess.ConstrainedInternet,
			"idle" or "offline" or "failure" or "disconnect" => NetworkAccess.None,
			_ => NetworkAccess.Unknown,
		};
		var profile = type switch
		{
			"wifi" => ConnectionProfile.WiFi,
			"cellular" => ConnectionProfile.Cellular,
			"ethernet" => ConnectionProfile.Ethernet,
			"bluetooth" => ConnectionProfile.Bluetooth,
			"" => ConnectionProfile.Unknown,
			_ => ConnectionProfile.Unknown,
		};
		var profiles = access is NetworkAccess.Internet or NetworkAccess.ConstrainedInternet or NetworkAccess.Local && profile != ConnectionProfile.Unknown
			? new[] { profile }
			: Array.Empty<ConnectionProfile>();
		var changed = access != _access || !profiles.SequenceEqual(_profiles);
		_access = access;
		_profiles = profiles;
		if (raise && changed)
			ConnectivityChanged?.Invoke(this, new ConnectivityChangedEventArgs(_access, _profiles));
	}

	// Off the Qt thread Start() cannot create the watcher, so kernel routes answer instead of Unknown.
	public NetworkAccess NetworkAccess { get { Start(); if (_access == NetworkAccess.Unknown) ReadInterfaces(); return _access; } }

	public IEnumerable<ConnectionProfile> ConnectionProfiles { get { Start(); if (_access == NetworkAccess.Unknown) ReadInterfaces(); return _profiles; } }

	public event EventHandler<ConnectivityChangedEventArgs>? ConnectivityChanged;
}

/// <summary>Vibration on QtFeedback's HapticsEffect.</summary>
internal sealed class SailfishVibration : IVibration
{
	private const string Service = "vibration";

	private const string Qml = """
		import QtQuick 2.6
		import QtFeedback 5.0
		HapticsEffect { intensity: 1.0; duration: 500 }
		""";

	public bool IsSupported => QtHostServices.Ensure(Service, Qml);

	public void Vibrate() => Vibrate(TimeSpan.FromMilliseconds(500));

	public void Vibrate(TimeSpan duration)
	{
		if (!IsSupported)
			return;
		var ms = (int)Math.Clamp(duration.TotalMilliseconds, 1, 5000);
		QtHostServices.Eval(Service, $"(function(){{s.stop();s.duration={ms};s.start();return s.state;}})()");
		Vibrations++;
	}

	public void Cancel()
	{
		if (IsSupported)
			QtHostServices.Eval(Service, "(function(){s.stop();return s.state;})()");
	}

	/// <summary>Native effect state (diagnostics: QFeedbackEffect Running = 2).</summary>
	public static string NativeState => QtHostServices.Eval(Service, "s.state");

	/// <summary>Diagnostics: vibrations started.</summary>
	public static int Vibrations { get; private set; }
}

/// <summary>HapticFeedback on QtFeedback theme effects: Click is Press, LongPress is PressStrong.</summary>
internal sealed class SailfishHapticFeedback : IHapticFeedback
{
	private const string Service = "haptics";

	private const string Qml = """
		import QtQuick 2.6
		import QtFeedback 5.0
		ThemeEffect { effect: ThemeEffect.Press }
		""";

	public bool IsSupported => QtHostServices.Ensure(Service, Qml);

	public void Perform(HapticFeedbackType type)
	{
		if (!IsSupported)
			return;
		// ThemeEffect values (the enum is not in scope of the eval): Press 0, PressStrong 4.
		var effect = type == HapticFeedbackType.LongPress ? "4" : "0";
		QtHostServices.Eval(Service, $"(function(){{s.effect={effect};s.play();return s.effect;}})()");
		Performed++;
	}

	/// <summary>The native effect of the last Perform (diagnostics).</summary>
	public static string NativeEffect => QtHostServices.Eval(Service, "s.effect");

	/// <summary>Diagnostics: feedback effects played.</summary>
	public static int Performed { get; private set; }
}

/// <summary>
/// Local notifications on Nemo.Notifications (MAUI has no API of its own). <see cref="Show"/> returns an id for <see cref="Close"/>.
/// </summary>
public static class SailfishNotifications
{
	private const string Service = "notifications";

	private const string Qml = """
		import QtQuick 2.6
		import Nemo.Notifications 1.0
		Item {
		    Component { id: factory; Notification {} }
		    property var live: ({})
		    function show(json) {
		        var o = JSON.parse(json);
		        var n = factory.createObject(this, {
		            appName: o.appName, appIcon: o.icon, summary: o.summary, body: o.body,
		            previewSummary: o.preview ? o.summary : "", previewBody: o.preview ? o.body : "" });
		        n.publish();
		        live[n.replacesId] = n;
		        return n.replacesId;
		    }
		    function close(id) {
		        var n = live[id];
		        if (!n) return "missing";
		        n.close();
		        delete live[id];
		        return "closed";
		    }
		}
		""";

	/// <summary>Publishes a notification (with a banner when
	/// <paramref name="preview"/>); returns its id, 0 when unavailable.</summary>
	public static uint Show(string summary, string body, bool preview = true, string? icon = null)
	{
		if (!QtHostServices.Ensure(Service, Qml))
			return 0;
		var app = Microsoft.Maui.ApplicationModel.AppInfo.Current.Name;
		var json = "{\"appName\":" + QtHostServices.Js(app) + ",\"icon\":" + QtHostServices.Js(icon ?? "icon-lock-information") +
		           ",\"summary\":" + QtHostServices.Js(summary) + ",\"body\":" + QtHostServices.Js(body) +
		           ",\"preview\":" + (preview ? "true" : "false") + "}";
		return uint.TryParse(QtHostServices.Eval(Service, $"s.show({QtHostServices.Js(json)})"), out var id) ? id : 0;
	}

	/// <summary>Removes a published notification; false when unknown.</summary>
	public static bool Close(uint id) =>
		QtHostServices.Eval(Service, $"s.close({id})") == "closed";
}
