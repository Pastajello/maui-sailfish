using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.Communication;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Media;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// F4 platform-services leg (MAUI_SAILFISH_QT_HOST_F4_DIAG=1): MAUI Essentials statics reach
/// the Sailfish implementations and the QML-backed services work on the device.
///   A. Stores and info: Preferences, SecureStorage, Clipboard, AppInfo, DeviceInfo, DeviceDisplay.
///   B. Theme: RequestedTheme follows the ambience and raises RequestedThemeChanged.
///   C. Device services: Battery, Connectivity, Vibration, HapticFeedback.
///   D. Sensors and location: accelerometer at rest, honest support, Geolocation.
///   E. Sharing and pickers: Share, MediaPicker, Permissions (Sailjail), launcher URIs, Screenshot.
///   F. The sample's Essentials page: no probe reports "unsupported".
///   G. SailfishNotifications: publish and close.
///   H. SailfishCover: content/actions reach the live cover.
/// </summary>
internal sealed partial class QtHostDiagnosticsRunner
{
	private bool _qtF4Diag;
	private readonly DiagChecks _qtF4Checks = new("Qt f4 diag");


	private void RunQtF4Diagnostics(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher)
	{
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1500), async () =>
		{
			try
			{
				await F4StaticsA();
			}
			catch (Exception ex)
			{
				_qtF4Checks.Check($"A statics threw: {ex.GetType().Name}: {ex.Message}", false);
			}
			F4ThemeB(dispatcher);
		});
	}

	private async Task F4StaticsA()
	{
		var stamp = DateTime.UtcNow.Ticks;
		Preferences.Default.Set("f4_int", 42);
		Preferences.Default.Set("f4_text", "sailfish");
		Preferences.Default.Set("f4_flag", true);
		var when = new DateTime(2030, 1, 15, 8, 30, 0, DateTimeKind.Utc);
		Preferences.Default.Set("f4_when", when);
		Preferences.Default.Set("f4_stamp", stamp);
		_qtF4Checks.Check($"A Preferences.Default typed round trip: {Preferences.Default.Get("f4_int", 0)}==42, '{Preferences.Default.Get("f4_text", "")}'=='sailfish', {Preferences.Default.Get("f4_flag", false)}, {Preferences.Default.Get("f4_when", DateTime.MinValue):O}",
			Preferences.Default.Get("f4_int", 0) == 42 && Preferences.Default.Get("f4_text", "") == "sailfish" &&
			Preferences.Default.Get("f4_flag", false) && Preferences.Default.Get("f4_when", DateTime.MinValue) == when);
		var fresh = new SailfishPreferences();
		_qtF4Checks.Check($"A Preferences persisted: a fresh store reads stamp {fresh.Get("f4_stamp", 0L)}=={stamp}", fresh.Get("f4_stamp", 0L) == stamp);

		await SecureStorage.Default.SetAsync("f4_secret", "s3cr3t");
		var secret = await SecureStorage.Default.GetAsync("f4_secret");
		_qtF4Checks.Check($"A SecureStorage.Default round trip '{secret}'=='s3cr3t'", secret == "s3cr3t");
		// Secrets semantics: overwrite in place, Unicode/'/' keys, Remove true once then null.
		const string oddKey = "f4 klucz/żółw 🔑";
		await SecureStorage.Default.SetAsync("f4_secret", "s3cr3t-2");
		await SecureStorage.Default.SetAsync(oddKey, "zażółć");
		var overwritten = await SecureStorage.Default.GetAsync("f4_secret");
		var odd = await SecureStorage.Default.GetAsync(oddKey);
		var removed = SecureStorage.Default.Remove(oddKey);
		var removedAgain = SecureStorage.Default.Remove(oddKey);
		var gone = await SecureStorage.Default.GetAsync(oddKey);
		_qtF4Checks.Check($"A SecureStorage overwrite '{overwritten}'=='s3cr3t-2', odd key '{odd}'=='zażółć', Remove {removed}/{removedAgain} (true/false), then {(gone is null ? "null" : "'" + gone + "'")}",
			overwritten == "s3cr3t-2" && odd == "zażółć" && removed && !removedAgain && gone is null);
		// With the Secrets daemon no key/data file may exist; otherwise (no daemon, or the app's device-lock collection
		// locked because the daemon never got the device lock code) the file store is used, with the reason.
		var daemonInstalled = File.Exists("/usr/bin/sailfishsecretsd");
		var storage = SecureStorage.Default as SailfishSecureStorage;
		var filesLeft = File.Exists(SailfishFileSecureStore.DataPath) || File.Exists(SailfishFileSecureStore.KeyPath);
		_qtF4Checks.Check($"A SecureStorage backend '{storage?.Backend}' (daemon installed: {daemonInstalled}; collection '{SailfishSecureStorage.CollectionName}'; " +
		        $"file store on disk: {filesLeft}; fallback: {storage?.FallbackReason ?? "-"})",
			storage is not null && (daemonInstalled && storage.Backend == "secrets"
				? !filesLeft
				: storage.Backend == "file" && !string.IsNullOrEmpty(storage.FallbackReason) &&
				  (!daemonInstalled || storage.FallbackReason.Contains("locked", StringComparison.Ordinal))));

		// The Essentials added 2026-10-02 (no torch flash and no picker here: those are checked by hand).
		// The user's address book is privileged (system apps only): a third-party app reads the non-privileged store, so
		// the check is that the model loads and answers, not a count.
		string contactsAnswer;
		bool contactsLoaded;
		try
		{
			contactsAnswer = $"{(await Contacts.Default.GetAllAsync().WaitAsync(TimeSpan.FromSeconds(10))).Count()} contacts (non-privileged store)";
			contactsLoaded = true;
		}
		catch (Exception ex)
		{
			contactsAnswer = $"{ex.GetType().Name}: {ex.Message}";
			contactsLoaded = false;
		}
		_qtF4Checks.Check($"A Contacts.GetAllAsync → {contactsAnswer}", contactsLoaded);
		var torch = await Flashlight.Default.IsSupportedAsync();
		_qtF4Checks.Check($"A Flashlight.IsSupportedAsync={torch} (the sample is not sandboxed)", torch);
		var coverBefore = SailfishCover.Actions;
		await AppActions.Current.SetAsync(new[] { new AppAction("f4a", "A", icon: "icon-cover-refresh"), new AppAction("f4b", "B") });
		var cover = SailfishCover.Actions;
		_qtF4Checks.Check($"A AppActions → {cover.Length} cover actions ({string.Join(", ", cover.Select(a => a.Icon))})",
			cover.Length == 2 && cover[0].Icon == "image://theme/icon-cover-refresh");
		SailfishCover.SetActions(coverBefore);
		var unsupported = 0;
		foreach (var call in new Func<Task>[]
		{
			() => TextToSpeech.Default.SpeakAsync("f4"),
			() => Geocoding.Default.GetLocationsAsync("Tampere"),
			() => Microsoft.Maui.Authentication.Passkeys.Default.AssertAsync(new Microsoft.Maui.Authentication.PasskeyRequestOptions("{}")),
		})
		{
			try { await call(); }
			catch (FeatureNotSupportedException) { unsupported++; }
		}
		_qtF4Checks.Check($"A TextToSpeech/Geocoding/Passkeys report FeatureNotSupported ({unsupported}/3)", unsupported == 3);

		await Clipboard.Default.SetTextAsync("f4-clip-" + stamp);
		var clip = await Clipboard.Default.GetTextAsync();
		_qtF4Checks.Check($"A Clipboard.Default round trip ('{clip}'), HasText={Clipboard.Default.HasText}", clip == "f4-clip-" + stamp && Clipboard.Default.HasText);

		_qtF4Checks.Check($"A AppInfo.Current: name '{AppInfo.Current.Name}', version {AppInfo.Current.VersionString}, theme {AppInfo.Current.RequestedTheme}",
			AppInfo.Current.Name.Length > 0 && AppInfo.Current.RequestedTheme != AppTheme.Unspecified);
		// Tracker S09: the identity the other heads report (ApplicationId, ApplicationDisplayVersion), and VersionTracking
		// over it, from the statics and from the services.
		var tracking = IPlatformApplication.Current?.Services.GetService(typeof(IVersionTracking)) as IVersionTracking;
		_qtF4Checks.Check($"A AppInfo identity: PackageName '{AppInfo.Current.PackageName}'=='com.maui.sailfish.sample' (ApplicationId), " +
			$"build '{AppInfo.Current.BuildString}', packaging {AppInfo.Current.PackagingModel}; VersionTracking.CurrentVersion '{VersionTracking.CurrentVersion}'==AppInfo '{AppInfo.Current.VersionString}', " +
			$"IVersionTracking from the services {(tracking is null ? "null" : "resolved")}, versions seen [{string.Join(",", VersionTracking.VersionHistory)}]",
			AppInfo.Current.PackageName == "com.maui.sailfish.sample" && AppInfo.Current.PackagingModel == AppPackagingModel.Packaged &&
			VersionTracking.CurrentVersion == AppInfo.Current.VersionString && tracking is not null);

		var info = DeviceInfo.Current;
		_qtF4Checks.Check($"A DeviceInfo.Current: {info.Manufacturer} '{info.Model}' ({info.Name}) {info.Platform} {info.VersionString} v{info.Version} {info.Idiom} {info.DeviceType}",
			info.Platform.ToString() == "SailfishOS" && info.VersionString.StartsWith("5.", StringComparison.Ordinal) &&
			info.Version.Major == 5 && info.Manufacturer.Length > 0 && info.Model.Length > 0 &&
			info.Idiom == DeviceIdiom.Phone && info.DeviceType == DeviceType.Physical);

		var display = DeviceDisplay.Current.MainDisplayInfo;
		_qtF4Checks.Check($"A DeviceDisplay.MainDisplayInfo {display.Width}x{display.Height} @{display.Density:F2} {display.Orientation} == window {SailfishDisplay.PixelWidth}x{SailfishDisplay.PixelHeight}",
			(int)display.Width == SailfishDisplay.PixelWidth && (int)display.Height == SailfishDisplay.PixelHeight && display.Density > 1 &&
			display.Orientation == DisplayOrientation.Portrait);
		DeviceDisplay.Current.KeepScreenOn = true;
		var on = SailfishDeviceDisplay.NativePreventBlanking;
		DeviceDisplay.Current.KeepScreenOn = false;
		var off = SailfishDeviceDisplay.NativePreventBlanking;
		_qtF4Checks.Check($"A KeepScreenOn → Nemo.KeepAlive DisplayBlanking.preventBlanking {on}/{off} (true/false)", on == "true" && off == "false");
	}

	private void F4ThemeB(SailfishDispatcher dispatcher)
	{
		var app = Application.Current!;
		var nativeLight = QtHost.QtHostRuntime.Eval("Theme.colorScheme === Theme.DarkOnLight") == "true";
		var expected = nativeLight ? AppTheme.Light : AppTheme.Dark;
		_qtF4Checks.Check($"B RequestedTheme {app.RequestedTheme} follows the ambience (colorScheme {(nativeLight ? "DarkOnLight" : "LightOnDark")} → {expected})",
			app.RequestedTheme == expected && SailfishTheme.Current == expected);
		var changed = 0;
		AppTheme? seen = null;
		app.RequestedThemeChanged += (_, e) => { changed++; seen = e.RequestedTheme; };
		// An ambience switch through the service channel (window queue → shim drain → subscriber).
		var flipped = !nativeLight;
		QtHost.QtHostRuntime.Eval($"window.mauiAppNotify('svc-theme-changed', JSON.stringify({{ light: {(flipped ? "true" : "false")} }}))");
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(800), () =>
		{
			var want = flipped ? AppTheme.Light : AppTheme.Dark;
			_qtF4Checks.Check($"B ambience change on the service channel → RequestedTheme {app.RequestedTheme}=={want}, RequestedThemeChanged {changed}>=1 ({seen})",
				app.RequestedTheme == want && changed >= 1 && seen == want);
			QtHost.QtHostRuntime.Eval($"window.mauiAppNotify('svc-theme-changed', JSON.stringify({{ light: {(nativeLight ? "true" : "false")} }}))");
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(800), () =>
			{
				_qtF4Checks.Check($"B restored → RequestedTheme {app.RequestedTheme}=={expected}", app.RequestedTheme == expected);
				F4DevicesC(dispatcher);
			});
		});
	}

	private void F4DevicesC(SailfishDispatcher dispatcher)
	{
		// First reads come from the kernel fallback; MCE / Connman take over once they report.
		var firstLevel = Battery.Default.ChargeLevel;
		var firstAccess = Connectivity.Current.NetworkAccess;
		_qtF4Checks.Check($"C first reads are immediate: ChargeLevel {firstLevel:F2}, NetworkAccess {firstAccess}",
			firstLevel >= 0 && firstAccess == NetworkAccess.Internet);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1500), () => F4DevicesCRun(dispatcher));
	}

	private void F4DevicesCRun(SailfishDispatcher dispatcher)
	{
		var battery = Battery.Default;
		var level = battery.ChargeLevel;
		var kernel = -1;
		try
		{
			kernel = int.Parse(System.IO.File.ReadAllText("/sys/class/power_supply/battery/capacity").Trim(), System.Globalization.CultureInfo.InvariantCulture);
		}
		catch (Exception)
		{
		}
		_qtF4Checks.Check($"C Battery.ChargeLevel {level:F2} vs kernel capacity {kernel}%, State {battery.State}, PowerSource {battery.PowerSource}, EnergySaver {battery.EnergySaverStatus}",
			level is >= 0 and <= 1 && (kernel < 0 || Math.Abs(level * 100 - kernel) <= 2) && battery.State != BatteryState.Unknown &&
			battery.PowerSource != BatteryPowerSource.Unknown && battery.EnergySaverStatus != EnergySaverStatus.Unknown);
		var events = 0;
		BatteryInfoChangedEventArgs? last = null;
		battery.BatteryInfoChanged += (_, e) => { events++; if (Math.Abs(e.ChargeLevel - 0.37) < 0.001) last = e; };
		QtHost.QtHostRuntime.Eval("window.mauiAppNotify('svc-battery-changed', JSON.stringify({ percent: 37, state: 1, charger: 1, cable: true, saving: false }))");

		var net = Connectivity.Current;
		_qtF4Checks.Check($"C Connectivity: NetworkAccess {net.NetworkAccess}, profiles [{string.Join(",", net.ConnectionProfiles)}] (the device is reached over Wi-Fi)",
			net.NetworkAccess is NetworkAccess.Internet or NetworkAccess.ConstrainedInternet or NetworkAccess.Local && net.ConnectionProfiles.Contains(ConnectionProfile.WiFi));

		var vibration = Vibration.Default;
		vibration.Vibrate(TimeSpan.FromMilliseconds(300));
		var running = SailfishVibration.NativeState;
		_qtF4Checks.Check($"C Vibration.Vibrate(300ms): supported {vibration.IsSupported}, native HapticsEffect state {running} (2 = Running)", vibration.IsSupported && running == "2");

		var haptics = HapticFeedback.Default;
		haptics.Perform(HapticFeedbackType.Click);
		var click = SailfishHapticFeedback.NativeEffect;
		haptics.Perform(HapticFeedbackType.LongPress);
		var longPress = SailfishHapticFeedback.NativeEffect;
		_qtF4Checks.Check($"C HapticFeedback: supported {haptics.IsSupported}, Click → effect {click} (Press 0), LongPress → {longPress} (PressStrong 4)",
			haptics.IsSupported && click == "0" && longPress == "4");

		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(800), () =>
		{
			_qtF4Checks.Check($"C battery change on the channel → BatteryInfoChanged {events}>=1 (level {last?.ChargeLevel:F2}==0.37, {last?.State}, {last?.PowerSource})",
				events >= 1 && last is { ChargeLevel: 0.37, State: BatteryState.Charging, PowerSource: BatteryPowerSource.Usb });
			// back to the real snapshot
			QtHost.QtHostRuntime.Eval("window.mauiServices['battery'].report()");
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(500), () =>
			{
				_qtF4Checks.Check($"C real battery state restored: {Battery.Default.ChargeLevel:F2}", Math.Abs(Battery.Default.ChargeLevel - level) < 0.02);
				F4SensorsD(dispatcher);
			});
		});
	}

	private void F4SensorsD(SailfishDispatcher dispatcher)
	{
		var acc = Accelerometer.Default;
		var readings = 0;
		double magnitude = 0;
		acc.ReadingChanged += (_, e) => { readings++; magnitude = e.Reading.Acceleration.Length(); };
		var supported = acc.IsSupported;
		if (supported)
			acc.Start(SensorSpeed.UI);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1500), () =>
		{
			_qtF4Checks.Check($"D Accelerometer: supported {supported}, {readings} readings in 1.5 s, |a| {magnitude:F2} G (≈1 at rest)",
				supported && readings >= 3 && magnitude is > 0.7 and < 1.3);
			acc.Stop();
			var stopped = readings;
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(600), async () =>
			{
				_qtF4Checks.Check($"D Accelerometer.Stop → readings hold ({stopped} → {readings}), IsMonitoring {acc.IsMonitoring}", readings - stopped <= 1 && !acc.IsMonitoring);
				var report = new List<string>();
				var honest = true;
				void Probe(string name, bool isSupported, Action start, Action stop)
				{
					if (isSupported)
					{
						start();
						stop();
						report.Add($"{name}=yes");
						return;
					}
					try
					{
						start();
						honest = false;   // an unsupported sensor must refuse to start
						report.Add($"{name}=started?!");
					}
					catch (FeatureNotSupportedException)
					{
						report.Add($"{name}=no");
					}
				}
				Probe("gyroscope", Gyroscope.Default.IsSupported, () => Gyroscope.Default.Start(SensorSpeed.UI), () => Gyroscope.Default.Stop());
				Probe("magnetometer", Magnetometer.Default.IsSupported, () => Magnetometer.Default.Start(SensorSpeed.UI), () => Magnetometer.Default.Stop());
				Probe("compass", Compass.Default.IsSupported, () => Compass.Default.Start(SensorSpeed.UI), () => Compass.Default.Stop());
				Probe("barometer", Barometer.Default.IsSupported, () => Barometer.Default.Start(SensorSpeed.UI), () => Barometer.Default.Stop());
				Probe("orientation", OrientationSensor.Default.IsSupported, () => OrientationSensor.Default.Start(SensorSpeed.UI), () => OrientationSensor.Default.Stop());
				_qtF4Checks.Check($"D other sensors report support honestly ({string.Join(", ", report)})", honest);

				var geo = Geolocation.Default;
				var enabled = geo.IsEnabled;   // creates the PositionSource
				var state = SailfishGeolocation.NativeState;
				Location? last = null;
				Location? fix = null;
				Exception? error = null;
				try
				{
					last = await geo.GetLastKnownLocationAsync();
					fix = await geo.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(8)));
				}
				catch (Exception ex)
				{
					error = ex;
				}
				Console.Error.WriteLine($"[Sailfish] Qt f4 diag: D geolocation fix: {(fix is null ? "none within 8 s (indoors?)" : $"{fix.Latitude:F4},{fix.Longitude:F4} ±{fix.Accuracy:F0} m")}, last known {(last is null ? "none" : "yes")}");
				_qtF4Checks.Check($"D Geolocation: backend {state}, enabled {enabled}, request completed without error ({error?.GetType().Name ?? "ok"})",
					enabled && error is null);
				F4ShareE(dispatcher);
			});
		});
	}

	private void F4ShareE(SailfishDispatcher dispatcher)
	{
		var (title, mime, resources) = SailfishShare.Describe(new ShareTextRequest { Text = "hello sailfish", Title = "Greeting" });
		var textConfig = SailfishShare.Prepare(title, mime, resources);
		var (utitle, umime, uresources) = SailfishShare.Describe(new ShareTextRequest { Uri = "https://sailfishos.org", Title = "Site" });
		var urlConfig = SailfishShare.Prepare(utitle, umime, uresources);
		var logo = System.IO.Path.Combine(AppContext.BaseDirectory, "images", "sailfish_logo.png");
		var fileConfig = SailfishShare.Prepare("Logo", "image/png", new object[] { logo });
		_qtF4Checks.Check($"E Share configures Sailfish.Share: text ({Trim(textConfig)}), url ({Trim(urlConfig)}), file ({Trim(fileConfig)})",
			textConfig.Contains("hello sailfish") && textConfig.Contains("text/plain") &&
			urlConfig.Contains("https://sailfishos.org") && urlConfig.Contains("text/x-url") &&
			fileConfig.Contains("sailfish_logo.png") && fileConfig.Contains("image/png"));

		// Unsandboxed grants everything; sandboxed grants only the desktop entry's Permissions= line.
		var desktop = System.IO.Path.Combine("/usr/share/applications", SailfishAppPaths.PackageName + ".desktop");
		var declared = System.IO.File.Exists(desktop)
			? System.IO.File.ReadAllLines(desktop).FirstOrDefault(l => l.StartsWith("Permissions=", StringComparison.Ordinal))
			: null;
		bool Declares(string p) => declared is null || declared["Permissions=".Length..].Split(';').Contains(p);
		PermissionStatus Want(string p) => Declares(p) ? PermissionStatus.Granted : PermissionStatus.Denied;
		var camera = Permissions.CheckStatusAsync<Permissions.Camera>().Result;
		var location = Permissions.RequestAsync<Permissions.LocationWhenInUse>().Result;
		var photos = Permissions.CheckStatusAsync<Permissions.Photos>().Result;
		var vibrate = Permissions.CheckStatusAsync<Permissions.Vibrate>().Result;
		_qtF4Checks.Check($"E Permissions ({declared ?? "unsandboxed"}): Camera {camera}, LocationWhenInUse {location}, Photos {photos}, Vibrate {vibrate}",
			camera == Want("Camera") && location == Want("Location") && photos == Want("Pictures") && vibrate == PermissionStatus.Granted);

		var dial = SailfishCommunication.DialUri("+48 600 100 200");
		var mail = SailfishCommunication.MailUri(new EmailMessage("Hi", "Body text", "a@example.com") { Cc = new List<string> { "c@example.com" } });
		var sms = SailfishCommunication.SmsUri(new SmsMessage("see you", "+48600100200"));
		var map = SailfishCommunication.GeoUri(60.1699, 24.9384, "Helsinki");
		_qtF4Checks.Check($"E URIs: {dial} | {mail} | {sms} | {map}",
			dial == "tel:%2B48%20600%20100%20200" && mail == "mailto:a@example.com?cc=c%40example.com&subject=Hi&body=Body%20text" &&
			sms == "sms:+48600100200?body=see%20you" && map == "geo:60.1699,24.9384?q=60.1699,24.9384(Helsinki)");

		var depth0 = DiagQml.EvalNum("pageStack.depth");
		var pick = MediaPicker.Default.PickPhotoAsync();
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1800), () =>
		{
			var depth1 = DiagQml.EvalNum("pageStack.depth");
			var page = QtHost.QtHostRuntime.Eval("String(pageStack.currentPage)");
			_qtF4Checks.Check($"E MediaPicker.PickPhotoAsync → Sailfish image picker pushed (depth {depth0} → {depth1}, {page.Split('(')[0]})",
				depth1 == depth0 + 1 && page.Contains("Picker", StringComparison.Ordinal));
			Shot(dispatcher, "f4-e1-image-picker", () =>
			{
				// A selection as the picker makes it (its selectedContentProperties).
				QtHost.QtHostRuntime.Eval($"(function(){{var p={SailfishPickers.CurrentPickerJs};p.selectedContentProperties={{filePath:{QtHost.QtHostServices.Js(logo)},fileName:'sailfish_logo.png',mimeType:'image/png'}};return 'ok';}})()");
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(600), () =>
				{
					_qtF4Checks.Check($"E selection → PickPhotoAsync resolved '{(pick.IsCompletedSuccessfully ? pick.Result?.FullPath : pick.Status.ToString())}'",
						pick.IsCompletedSuccessfully && pick.Result?.FullPath == logo && pick.Result.FileName == "sailfish_logo.png");
					QtHost.QtHostRuntime.Eval("pageStack.pop()");
					dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200), () =>
					{
						var cancel = FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Pick" });
						dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1800), () =>
						{
							var filePage = QtHost.QtHostRuntime.Eval("String(pageStack.currentPage)");
							QtHost.QtHostRuntime.Eval("pageStack.pop()");
							dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1500), async () =>
							{
								_qtF4Checks.Check($"E FilePicker.PickAsync opened ({filePage.Split('(')[0]}), closed without a pick → {(cancel.IsCompleted ? (cancel.Result is null ? "null" : cancel.Result.FullPath) : "pending")}",
									filePage.Contains("FilePicker", StringComparison.Ordinal) && cancel.IsCompletedSuccessfully && cancel.Result is null);
								try
								{
									var shot = await Screenshot.Default.CaptureAsync();
									await using var stream = await shot.OpenReadAsync();
									var magic = new byte[4];
									stream.ReadExactly(magic);
									_qtF4Checks.Check($"E Screenshot.CaptureAsync → {shot.Width}x{shot.Height} PNG (window {SailfishDisplay.PixelWidth}x{SailfishDisplay.PixelHeight})",
										shot.Width == SailfishDisplay.PixelWidth && shot.Height == SailfishDisplay.PixelHeight && magic[1] == (byte)'P' && magic[2] == (byte)'N');
								}
								catch (Exception ex)
								{
									_qtF4Checks.Check($"E Screenshot.CaptureAsync threw {ex.GetType().Name}: {ex.Message}", false);
								}
								await F4CaptureAndClipboard(dispatcher);
								F4EssentialsPageF(dispatcher);
							});
						});
					});
				});
			});
		});
	}

	private static string Trim(string s) => s.Length > 90 ? s[..90] + "…" : s;

	/// <summary>Tracker S11: view.CaptureAsync() (IViewScreenshot) of the shown page's content, the JPEG encoding, the
	/// display refresh rate, and Clipboard.ClipboardContentChanged for a change made outside the app's own SetTextAsync.</summary>
	private async Task F4CaptureAndClipboard(SailfishDispatcher dispatcher)
	{
		try
		{
			if ((RootNav?.CurrentPage as ContentPage)?.Content is not View view)
			{
				_qtF4Checks.Check("E view.CaptureAsync: a page content to capture", false);
				return;
			}
			var shot = await view.CaptureAsync();
			var expectedW = (int)Math.Round(view.Width * SailfishDisplay.Density);
			var expectedH = (int)Math.Round(view.Height * SailfishDisplay.Density);
			byte[] png = Array.Empty<byte>(), jpeg = Array.Empty<byte>();
			if (shot is not null)
			{
				await using (var s = await shot.OpenReadAsync())
				{
					png = new byte[4];
					s.ReadExactly(png);
				}
				await using (var s = await shot.OpenReadAsync(ScreenshotFormat.Jpeg, 80))
				{
					jpeg = new byte[2];
					s.ReadExactly(jpeg);
				}
			}
			_qtF4Checks.Check($"E view.CaptureAsync() → {(shot is null ? "null" : $"{shot.Width}x{shot.Height}")} ≈ the view {expectedW}x{expectedH} px, PNG, JPEG(80) starts FF D8 ({(jpeg.Length == 2 ? $"{jpeg[0]:X2} {jpeg[1]:X2}" : "-")})",
				shot is not null && Math.Abs(shot.Width - expectedW) <= 2 && Math.Abs(shot.Height - expectedH) <= 2 &&
				png[1] == (byte)'P' && jpeg[0] == 0xFF && jpeg[1] == 0xD8);
		}
		catch (Exception ex)
		{
			_qtF4Checks.Check($"E view.CaptureAsync threw {ex.GetType().Name}: {ex.Message}", false);
		}

		var rate = DeviceDisplay.Current.MainDisplayInfo.RefreshRate;
		_qtF4Checks.Check($"E DeviceDisplay RefreshRate {rate:F2} from the screen (QScreen::refreshRate), > 0", rate > 0);

		// Another app's copy, as Silica's Clipboard sees it: set from QML, not through Clipboard.Default.
		var changes = 0;
		void OnChanged(object? sender, EventArgs e) => changes++;
		Clipboard.Default.ClipboardContentChanged += OnChanged;
		await Clipboard.Default.SetTextAsync("f4-own-" + DateTime.UtcNow.Ticks);
		var tcs = new TaskCompletionSource();
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(600), () =>
		{
			var ownChanges = changes;
			QtHost.QtHostRuntime.Eval("Qt.createQmlObject('import QtQuick 2.6; import Sailfish.Silica 1.0; QtObject { Component.onCompleted: Clipboard.text = \"f4-external\" }', pageStack, 'f4clip')");
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(800), async () =>
			{
				var text = await Clipboard.Default.GetTextAsync();
				_qtF4Checks.Check($"E ClipboardContentChanged: own SetTextAsync once ({ownChanges}==1), an outside change once more ({changes}==2, text '{text}')",
					ownChanges == 1 && changes == 2 && text == "f4-external");
				Clipboard.Default.ClipboardContentChanged -= OnChanged;
				tcs.TrySetResult();
			});
		});
		await tcs.Task;
	}

	private void F4EssentialsPageF(SailfishDispatcher dispatcher)
	{
		var nav = RootNav;
		var page = CreateDiagnosticPage("essentials");
		if (nav is null || page is null)
		{
			_qtF4Checks.Check("F sample Essentials page registered and pushable", false);
			ReportF4();
			return;
		}
		_ = nav.PushAsync(page);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(2000), () =>
		{
			var texts = page.GetVisualTreeDescendants().OfType<Label>().Select(l => l.Text ?? string.Empty).ToList();
			var unsupported = texts.Where(t => t.Contains("unsupported", StringComparison.OrdinalIgnoreCase)).ToList();
			var state = texts.FirstOrDefault(t => t.StartsWith("state:", StringComparison.Ordinal)) ?? "(no state label)";
			var device = texts.FirstOrDefault(t => t.StartsWith("device:", StringComparison.Ordinal)) ?? "(no device label)";
			_qtF4Checks.Check($"F sample Essentials page: no 'unsupported' probes ({unsupported.Count}: {string.Join(" | ", unsupported)}); {device}; {state}",
				texts.Count > 0 && unsupported.Count == 0 && device.Contains("SailfishOS") && state.Contains("conn=Internet"));
			Shot(dispatcher, "f4-f1-essentials-page", () =>
			{
				// G. a Nemo notification: published, shown, then removed.
				var id = SailfishNotifications.Show("MAUI f4", "Notification from the diagnostics leg");
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1500), () =>
				{
					Shot(dispatcher, "f4-g1-notification", () =>
					{
						var closed = SailfishNotifications.Close(id);
						_qtF4Checks.Check($"G SailfishNotifications.Show → id {id} (>0), Close → {closed}", id > 0 && closed);
						F4CoverH(dispatcher);
					});
				});
			});
		});
	}

	/// <summary>H. Managed cover content/actions reach the live cover and an action tap runs its callback.</summary>
	private void F4CoverH(SailfishDispatcher dispatcher)
	{
		var hits = new int[2];
		SailfishCover.SetContent("MAUI f4 cover", "2 of 10 tasks done", "next: Harbour");
		SailfishCover.SetActions(
			new SailfishCoverAction("image://theme/icon-cover-new", () => hits[0]++),
			new SailfishCoverAction("image://theme/icon-cover-next", () => hits[1]++));
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1000), () =>
		{
			var state = QtHost.QtHostRuntime.Eval(
				"(function(){var w=window,c=w.__mauiCoverItem;return JSON.stringify({api:w.mauiCoverApi,item:!!c," +
				"title:c?c.coverTitle:'',lines:c?c.coverLines.length:-1,actions:c?c.coverActions.length:-1});})()");
			var ok = false;
			try
			{
				using var doc = System.Text.Json.JsonDocument.Parse(state);
				var r = doc.RootElement;
				ok = r.GetProperty("api").GetBoolean() && r.GetProperty("item").GetBoolean() &&
				     r.GetProperty("title").GetString() == "MAUI f4 cover" &&
				     r.GetProperty("lines").GetInt32() == 2 && r.GetProperty("actions").GetInt32() == 2;
			}
			catch (Exception)
			{
				// malformed → the check fails with the raw state in its label
			}
			_qtF4Checks.Check($"H SailfishCover content + actions reach the cover item ({state})", ok);
			QtHost.QtHostRuntime.Eval("(function(){var c=window.__mauiCoverItem;if(c)c.trigger(1);return 'ok';})()");
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(700), () =>
			{
				_qtF4Checks.Check($"H cover action tap → its managed callback (second {hits[1]}==1, first {hits[0]}==0)", hits[1] == 1 && hits[0] == 0);
				ReportF4();
			});
		});
	}

	private void ReportF4()
	{
		_qtF4Checks.CheckNoOffThreadCalls();
		_qtF4Checks.Accept("OK — F4 platform services work (Essentials statics, stores, device, display, theme)");
		QtHost.QtHostRuntime.Shutdown();
	}
}
