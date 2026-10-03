using System.Numerics;
using System.Text.Json;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// MAUI sensors on QtSensors: one QML sensor per MAUI sensor, readings converted to MAUI's units here.
/// </summary>
internal abstract class SailfishSensor
{
	private readonly string _name;
	private readonly string _qmlType;
	private readonly string _readingJs;
	private bool _subscribed;

	protected SailfishSensor(string name, string qmlType, string readingJs)
	{
		_name = name;
		_qmlType = qmlType;
		_readingJs = readingJs;
	}

	private string Service => "sensor-" + _name;

	private string Qml => $$"""
		import QtQuick 2.6
		import QtSensors 5.2
		{{_qmlType}} {
		    active: false
		    alwaysOn: true
		    onReadingChanged: if (reading) window.mauiAppNotify("svc-sensor-{{_name}}", JSON.stringify({{_readingJs}}))
		}
		""";

	/// <summary>True when sensorfw has a sensor of this type.</summary>
	public bool IsSupported
	{
		get
		{
			if (!QtHostServices.Ensure(Service, Qml))
				return false;
			// The QML sensor connects on completion; connectToBackend() is not exposed.
			return QtHostServices.Eval(Service, "s.connectedToBackend") == "true";
		}
	}

	public bool IsMonitoring { get; private set; }

	public void Start(SensorSpeed sensorSpeed)
	{
		if (!IsSupported)
			throw new FeatureNotSupportedException($"{_qmlType} is not available on this device.");
		if (IsMonitoring)
			throw new InvalidOperationException($"{_qmlType} is already monitoring.");
		if (!_subscribed)
		{
			_subscribed = true;
			QtHostServices.Subscribe(ShellEvents.SensorPrefix + _name, OnReading);
		}
		var hz = sensorSpeed switch
		{
			SensorSpeed.Fastest => 100,
			SensorSpeed.Game => 50,
			SensorSpeed.UI => 16,
			_ => 5,
		};
		QtHostServices.Eval(Service, $"(function(){{s.dataRate={hz};s.active=true;return s.active;}})()");
		IsMonitoring = true;
	}

	public void Stop()
	{
		if (!IsMonitoring)
			return;
		QtHostServices.Eval(Service, "(function(){s.active=false;return s.active;})()");
		IsMonitoring = false;
	}

	private void OnReading(JsonElement e)
	{
		if (IsMonitoring)
			Readings++;
		if (IsMonitoring)
			Raise(e);
	}

	/// <summary>Readings delivered while monitoring (diagnostics).</summary>
	public int Readings { get; private set; }

	protected abstract void Raise(JsonElement reading);

	protected static float F(JsonElement e, string name) =>
		e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? (float)v.GetDouble() : 0f;
}

internal sealed class SailfishAccelerometer : SailfishSensor, IAccelerometer
{
	private const double StandardGravity = 9.80665;
	private DateTime _lastShake;

	public SailfishAccelerometer() : base("accelerometer", "Accelerometer", "{x:reading.x,y:reading.y,z:reading.z}")
	{
	}

	public event EventHandler<AccelerometerChangedEventArgs>? ReadingChanged;

	public event EventHandler? ShakeDetected;

	protected override void Raise(JsonElement r)
	{
		// QtSensors: m/s² → MAUI: G
		var v = new Vector3(F(r, "x"), F(r, "y"), F(r, "z")) / (float)StandardGravity;
		ReadingChanged?.Invoke(this, new AccelerometerChangedEventArgs(new AccelerometerData(v.X, v.Y, v.Z)));
		// MAUI's shake heuristic: a jolt well above 1 G, debounced.
		if (v.Length() > 2.7f && DateTime.UtcNow - _lastShake > TimeSpan.FromMilliseconds(500))
		{
			_lastShake = DateTime.UtcNow;
			ShakeDetected?.Invoke(this, EventArgs.Empty);
		}
	}
}

internal sealed class SailfishGyroscope : SailfishSensor, IGyroscope
{
	public SailfishGyroscope() : base("gyroscope", "Gyroscope", "{x:reading.x,y:reading.y,z:reading.z}")
	{
	}

	public event EventHandler<GyroscopeChangedEventArgs>? ReadingChanged;

	protected override void Raise(JsonElement r)
	{
		// QtSensors: °/s → MAUI: rad/s
		const float rad = MathF.PI / 180f;
		ReadingChanged?.Invoke(this, new GyroscopeChangedEventArgs(new GyroscopeData(F(r, "x") * rad, F(r, "y") * rad, F(r, "z") * rad)));
	}
}

internal sealed class SailfishMagnetometer : SailfishSensor, IMagnetometer
{
	public SailfishMagnetometer() : base("magnetometer", "Magnetometer", "{x:reading.x,y:reading.y,z:reading.z}")
	{
	}

	public event EventHandler<MagnetometerChangedEventArgs>? ReadingChanged;

	protected override void Raise(JsonElement r)
	{
		// QtSensors: Tesla → MAUI: µT
		const float micro = 1_000_000f;
		ReadingChanged?.Invoke(this, new MagnetometerChangedEventArgs(new MagnetometerData(F(r, "x") * micro, F(r, "y") * micro, F(r, "z") * micro)));
	}
}

internal sealed class SailfishCompass : SailfishSensor, ICompass
{
	public SailfishCompass() : base("compass", "Compass", "{azimuth:reading.azimuth}")
	{
	}

	public event EventHandler<CompassChangedEventArgs>? ReadingChanged;

	public void Start(SensorSpeed sensorSpeed, bool applyLowPassFilter) => Start(sensorSpeed);

	protected override void Raise(JsonElement r) =>
		ReadingChanged?.Invoke(this, new CompassChangedEventArgs(new CompassData(F(r, "azimuth"))));
}

internal sealed class SailfishBarometer : SailfishSensor, IBarometer
{
	public SailfishBarometer() : base("barometer", "PressureSensor", "{pressure:reading.pressure}")
	{
	}

	public event EventHandler<BarometerChangedEventArgs>? ReadingChanged;

	protected override void Raise(JsonElement r) =>
		// QtSensors: Pa → MAUI: hPa
		ReadingChanged?.Invoke(this, new BarometerChangedEventArgs(new BarometerData(F(r, "pressure") / 100.0)));
}

internal sealed class SailfishOrientationSensor : SailfishSensor, IOrientationSensor
{
	public SailfishOrientationSensor() : base("orientation", "RotationSensor", "{x:reading.x,y:reading.y,z:reading.z}")
	{
	}

	public event EventHandler<OrientationSensorChangedEventArgs>? ReadingChanged;

	protected override void Raise(JsonElement r)
	{
		// QRotationReading: Euler degrees (x pitch, y roll, z yaw) → quaternion.
		const float rad = MathF.PI / 180f;
		var q = Quaternion.CreateFromYawPitchRoll(F(r, "y") * rad, F(r, "x") * rad, F(r, "z") * rad);
		ReadingChanged?.Invoke(this, new OrientationSensorChangedEventArgs(new OrientationSensorData(q.X, q.Y, q.Z, q.W)));
	}
}

/// <summary>
/// Geolocation on QtPositioning: a request activates one PositionSource until a valid fix or the timeout.
/// A sandboxed app without the Location Sailjail permission gets a PermissionException, as MAUI does on Android/iOS.
/// </summary>
internal sealed class SailfishGeolocation : IGeolocation
{
	private const string Service = "geolocation";
	private Location? _last;
	private bool _subscribed;
	private readonly List<TaskCompletionSource<Location?>> _pending = new();
	private GeolocationListeningRequest? _listening;

	private const string Qml = """
		import QtQuick 2.6
		import QtPositioning 5.2
		PositionSource {
		    active: false
		    updateInterval: 1000
		    function snapshot() {
		        var p = position;
		        if (!p || !p.latitudeValid || !p.longitudeValid)
		            return "";
		        var c = p.coordinate;
		        return JSON.stringify({
		            lat: c.latitude, lon: c.longitude,
		            alt: p.altitudeValid ? c.altitude : null,
		            acc: p.horizontalAccuracyValid ? p.horizontalAccuracy : null,
		            vacc: p.verticalAccuracyValid ? p.verticalAccuracy : null,
		            speed: p.speedValid ? p.speed : null,
		            dir: p.directionValid ? p.direction : null,
		            t: p.timestamp ? p.timestamp.getTime() : Date.now() });
		    }
		    onPositionChanged: { var s = snapshot(); if (s.length > 0) window.mauiAppNotify("svc-geolocation-fix", s); }
		    onSourceErrorChanged: window.mauiAppNotify("svc-geolocation-error", JSON.stringify({ error: sourceError }))
		}
		""";

	private bool EnsureService()
	{
		if (!QtHostServices.Ensure(Service, Qml))
			return false;
		if (!_subscribed)
		{
			_subscribed = true;
			QtHostServices.Subscribe(ShellEvents.GeolocationFix, OnFix);
			QtHostServices.Subscribe(ShellEvents.GeolocationError, OnError);
		}
		return true;
	}

	/// <summary>A positioning backend is present (diagnostics).</summary>
	public static string NativeState => QtHostServices.Eval(Service, "JSON.stringify({valid:s.valid,error:s.sourceError,name:s.name,methods:s.supportedPositioningMethods})");

	private static PermissionException? Missing() => SailfishPermissions.Missing(typeof(Permissions.LocationWhenInUse), "Geolocation");

	public Task<Location?> GetLastKnownLocationAsync()
	{
		if (Missing() is { } missing)
			return Task.FromException<Location?>(missing);
		if (_last is null && EnsureService())
		{
			var snapshot = QtHostServices.Eval(Service, "s.snapshot()");
			if (snapshot.Length > 0)
			{
				using var doc = JsonDocument.Parse(snapshot);
				_last = ToLocation(doc.RootElement);
			}
		}
		return Task.FromResult(_last);
	}

	public async Task<Location?> GetLocationAsync(GeolocationRequest request, CancellationToken cancelToken)
	{
		if (Missing() is { } missing)
			throw missing;
		if (!EnsureService())
			throw new FeatureNotSupportedException("Positioning is not available.");
		var tcs = new TaskCompletionSource<Location?>(TaskCreationOptions.RunContinuationsAsynchronously);
		lock (_pending)
			_pending.Add(tcs);
		QtHostServices.Eval(Service, "(function(){s.active=true;s.update();return s.active;})()");
		var timeout = request.Timeout > TimeSpan.Zero ? request.Timeout : TimeSpan.FromSeconds(30);
		using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancelToken);
		cts.CancelAfter(timeout);
		using (cts.Token.Register(() => tcs.TrySetResult(null)))
		{
			var location = await tcs.Task.ConfigureAwait(true);
			bool idle;
			lock (_pending)
			{
				_pending.Remove(tcs);
				idle = _pending.Count == 0;
			}
			if (_listening is null && idle)
				QtHostServices.Eval(Service, "(function(){s.active=false;return s.active;})()");
			return location;
		}
	}

	public bool IsListeningForeground => _listening is not null;

	public bool IsEnabled => EnsureService() && QtHostServices.Eval(Service, "s.valid") == "true";

	public event EventHandler<GeolocationLocationChangedEventArgs>? LocationChanged;

	public event EventHandler<GeolocationListeningFailedEventArgs>? ListeningFailed;

	public Task<bool> StartListeningForegroundAsync(GeolocationListeningRequest request)
	{
		if (Missing() is { } missing)
			return Task.FromException<bool>(missing);
		if (!EnsureService())
			return Task.FromResult(false);
		_listening = request;
		var ms = (int)Math.Clamp(request.MinimumTime.TotalMilliseconds, 100, 60_000);
		QtHostServices.Eval(Service, $"(function(){{s.updateInterval={ms};s.active=true;return s.active;}})()");
		return Task.FromResult(true);
	}

	public void StopListeningForeground()
	{
		_listening = null;
		bool idle;
		lock (_pending)
			idle = _pending.Count == 0;
		if (idle)
			QtHostServices.Eval(Service, "(function(){s.active=false;return s.active;})()");
	}

	private void OnFix(JsonElement e)
	{
		var location = ToLocation(e);
		_last = location;
		TaskCompletionSource<Location?>[] pending;
		lock (_pending)
			pending = _pending.ToArray();
		foreach (var tcs in pending)
			tcs.TrySetResult(location);
		if (_listening is not null)
			LocationChanged?.Invoke(this, new GeolocationLocationChangedEventArgs(location));
	}

	private void OnError(JsonElement e)
	{
		var code = e.TryGetProperty("error", out var c) ? c.GetInt32() : 0;
		if (code == 0)
			return;   // NoError
		// PositionSource.SourceError: 1 AccessError, 2 ClosedError, 3 UnknownSourceError
		if (_listening is not null)
			ListeningFailed?.Invoke(this, new GeolocationListeningFailedEventArgs(code == 1 ? GeolocationError.Unauthorized : GeolocationError.PositionUnavailable));
	}

	private static Location ToLocation(JsonElement e)
	{
		double? Num(string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
		var location = new Location(Num("lat") ?? 0, Num("lon") ?? 0, DateTimeOffset.FromUnixTimeMilliseconds((long)(Num("t") ?? 0)))
		{
			Altitude = Num("alt"),
			Accuracy = Num("acc"),
			VerticalAccuracy = Num("vacc"),
			Speed = Num("speed"),
			Course = Num("dir"),
		};
		return location;
	}
}
