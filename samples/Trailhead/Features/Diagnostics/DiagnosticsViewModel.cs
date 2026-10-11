using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;

namespace Trailhead.Features.Diagnostics;

public sealed record DiagnosticsRow(string Name, string Value);

/// <summary>
/// What this build is and what the phone grants it. T04 adds the sensor census, T18 the runtime metrics.
/// </summary>
public partial class DiagnosticsViewModel : ObservableObject
{
	public ObservableCollection<DiagnosticsRow> Build { get; } = new();

	public ObservableCollection<DiagnosticsRow> Permissions { get; } = new();

	/// <summary>Which sensors this phone has and a reading from each (plan T04: the barometer decides where the
	/// elevation comes from).</summary>
	public ObservableCollection<DiagnosticsRow> Sensors { get; } = new();

	public async Task LoadAsync()
	{
		Build.Clear();
		Build.Add(new("Flavor", AppFlavor.Name));
		Build.Add(new("Version", $"{AppInfo.VersionString} ({AppInfo.BuildString})"));
		Build.Add(new("Package", AppInfo.PackageName));
		Build.Add(new("Device", $"{DeviceInfo.Manufacturer} {DeviceInfo.Model}"));
		Build.Add(new("OS", $"{DeviceInfo.Platform} {DeviceInfo.VersionString}"));
		var display = DeviceDisplay.MainDisplayInfo;
		Build.Add(new("Display", $"{display.Width}×{display.Height} px, density {display.Density:0.##}"));

		Permissions.Clear();
		Permissions.Add(new("Location", await StatusAsync<Microsoft.Maui.ApplicationModel.Permissions.LocationWhenInUse>()));
		Permissions.Add(new("Camera", await StatusAsync<Microsoft.Maui.ApplicationModel.Permissions.Camera>()));
		Permissions.Add(new("Photos", await StatusAsync<Microsoft.Maui.ApplicationModel.Permissions.Photos>()));
		Permissions.Add(new("Sensors", await StatusAsync<Microsoft.Maui.ApplicationModel.Permissions.Sensors>()));
		Permissions.Add(new("Notifications", await StatusAsync<Microsoft.Maui.ApplicationModel.Permissions.PostNotifications>()));
	}

	/// <summary>Listens to every sensor for a moment and keeps its last reading.</summary>
	public async Task LoadSensorsAsync()
	{
		Sensors.Clear();
		var readings = new Dictionary<string, string>();
		var stops = new List<Action>();

		void Try(string name, bool supported, Action start, Action stop)
		{
			if (!supported)
			{
				readings[name] = "not on this phone";
				return;
			}
			readings[name] = "no reading";
			try
			{
				start();
				stops.Add(stop);
			}
			catch (Exception ex) when (ex is FeatureNotSupportedException or PermissionException or InvalidOperationException)
			{
				readings[name] = ex.GetType().Name;
			}
		}

		void OnAcc(object? s, AccelerometerChangedEventArgs e) => readings["Accelerometer"] = V(e.Reading.Acceleration) + " g";
		void OnGyro(object? s, GyroscopeChangedEventArgs e) => readings["Gyroscope"] = V(e.Reading.AngularVelocity) + " rad/s";
		void OnMag(object? s, MagnetometerChangedEventArgs e) => readings["Magnetometer"] = V(e.Reading.MagneticField) + " µT";
		void OnCompass(object? s, CompassChangedEventArgs e) => readings["Compass"] = $"{e.Reading.HeadingMagneticNorth:0}°";
		void OnOrientation(object? s, OrientationSensorChangedEventArgs e) =>
			readings["Orientation"] = $"{e.Reading.Orientation.X:0.00} {e.Reading.Orientation.Y:0.00} {e.Reading.Orientation.Z:0.00} {e.Reading.Orientation.W:0.00}";
		void OnBaro(object? s, BarometerChangedEventArgs e) => readings["Barometer"] = $"{e.Reading.PressureInHectopascals:0.0} hPa";

		Try("Accelerometer", Accelerometer.Default.IsSupported,
			() => { Accelerometer.Default.ReadingChanged += OnAcc; Accelerometer.Default.Start(SensorSpeed.UI); },
			() => { Accelerometer.Default.Stop(); Accelerometer.Default.ReadingChanged -= OnAcc; });
		Try("Gyroscope", Gyroscope.Default.IsSupported,
			() => { Gyroscope.Default.ReadingChanged += OnGyro; Gyroscope.Default.Start(SensorSpeed.UI); },
			() => { Gyroscope.Default.Stop(); Gyroscope.Default.ReadingChanged -= OnGyro; });
		Try("Magnetometer", Magnetometer.Default.IsSupported,
			() => { Magnetometer.Default.ReadingChanged += OnMag; Magnetometer.Default.Start(SensorSpeed.UI); },
			() => { Magnetometer.Default.Stop(); Magnetometer.Default.ReadingChanged -= OnMag; });
		Try("Compass", Compass.Default.IsSupported,
			() => { Compass.Default.ReadingChanged += OnCompass; Compass.Default.Start(SensorSpeed.UI); },
			() => { Compass.Default.Stop(); Compass.Default.ReadingChanged -= OnCompass; });
		Try("Orientation", OrientationSensor.Default.IsSupported,
			() => { OrientationSensor.Default.ReadingChanged += OnOrientation; OrientationSensor.Default.Start(SensorSpeed.UI); },
			() => { OrientationSensor.Default.Stop(); OrientationSensor.Default.ReadingChanged -= OnOrientation; });
		Try("Barometer", Barometer.Default.IsSupported,
			() => { Barometer.Default.ReadingChanged += OnBaro; Barometer.Default.Start(SensorSpeed.UI); },
			() => { Barometer.Default.Stop(); Barometer.Default.ReadingChanged -= OnBaro; });
		readings["Step counter"] = "no MAUI API";

		await Task.Delay(1500);
		foreach (var stop in stops)
			stop();
		foreach (var (name, value) in readings)
			Sensors.Add(new(name, value));
	}

	private static string V(System.Numerics.Vector3 v) => $"{v.X:0.00} {v.Y:0.00} {v.Z:0.00}";

	private static async Task<string> StatusAsync<T>() where T : Microsoft.Maui.ApplicationModel.Permissions.BasePermission, new()
	{
		try
		{
			return (await Microsoft.Maui.ApplicationModel.Permissions.CheckStatusAsync<T>()).ToString();
		}
		catch (Exception ex)
		{
			return ex.GetType().Name;
		}
	}
}
