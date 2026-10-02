using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Authentication;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Media;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Flashlight on the system torch service (<c>org.sailfish.flashlight.provider</c>, the Top menu's toggle). The service
/// only toggles, so TurnOn/TurnOff read its state first. Sailjail has no permission for it: a sandboxed app cannot reach
/// it and gets <see cref="FeatureNotSupportedException"/>.
/// </summary>
public sealed class SailfishFlashlight : IFlashlight
{
	private const string Service = "flashlight";

	private const string Qml = """
		import QtQuick 2.6
		import Nemo.DBus 2.0
		DBusInterface {
		    bus: DBus.SessionBus
		    service: "org.sailfish.flashlight.provider"
		    path: "/org/sailfish/flashlight/provider"
		    iface: "org.sailfish.flashlight.provider"
		    // "on", "off" or "unavailable" (no service, or the sandbox does not let the call through)
		    function state() {
		        var on = getProperty("flashlightOn");
		        return on === true ? "on" : on === false ? "off" : "unavailable";
		    }
		    function setOn(want) {
		        var now = state();
		        if (now === "unavailable") return now;
		        if ((now === "on") === want) return "same";
		        call("toggleFlashlight", []);
		        return "toggled";
		    }
		}
		""";

	public Task<bool> IsSupportedAsync() =>
		SailfishMainThreadCall.Run(() => QtHostServices.Ensure(Service, Qml) && QtHostServices.Eval(Service, "s.state()") is "on" or "off");

	public Task TurnOnAsync() => Set(true);

	public Task TurnOffAsync() => Set(false);

	private static Task Set(bool on) => SailfishMainThreadCall.Run(() =>
	{
		var result = QtHostServices.Ensure(Service, Qml) ? QtHostServices.Eval(Service, $"s.setOn({(on ? "true" : "false")})") : "unavailable";
		if (result is not ("same" or "toggled"))
			throw new FeatureNotSupportedException(SailfishPermissions.IsSandboxed
				? "The flashlight service is not reachable from a Sailjail sandbox (there is no Sailjail permission for it)."
				: "The flashlight service (org.sailfish.flashlight.provider) is not available on this device.");
		return true;
	});
}

/// <summary>Sailfish OS ships no speech engine (no speech-dispatcher, espeak or similar), so there is nothing to speak
/// with: no locales, and SpeakAsync reports the feature as unsupported, as MAUI does on a device without a TTS engine.</summary>
public sealed class SailfishTextToSpeech : ITextToSpeech
{
	public Task<IEnumerable<Locale>> GetLocalesAsync() => Task.FromResult<IEnumerable<Locale>>(Array.Empty<Locale>());

	public Task SpeakAsync(string text, SpeechOptions? options = null, CancellationToken cancelToken = default) =>
		throw new FeatureNotSupportedException("Sailfish OS has no text-to-speech engine.");
}

/// <summary>Geocoding needs a geocoding provider; Sailfish OS has no Qt Location geoservices plugin and no system
/// geocoder, so both directions are unsupported (an app can call a web geocoder itself).</summary>
public sealed class SailfishGeocoding : IGeocoding
{
	public Task<IEnumerable<Placemark>> GetPlacemarksAsync(double latitude, double longitude) =>
		throw new FeatureNotSupportedException("Sailfish OS has no system geocoder.");

	public Task<IEnumerable<Location>> GetLocationsAsync(string address) =>
		throw new FeatureNotSupportedException("Sailfish OS has no system geocoder.");
}

/// <summary>Passkeys need a platform authenticator (WebAuthn/FIDO2); Sailfish OS has none.</summary>
public sealed class SailfishPasskeys : IPasskeys
{
	public bool IsSupported => false;

	public Task<PasskeyCreationResponse> CreateAsync(PasskeyCreationOptions options, CancellationToken cancellationToken = default) =>
		throw new FeatureNotSupportedException("Sailfish OS has no passkey (WebAuthn) authenticator.");

	public Task<PasskeyAssertionResponse> AssertAsync(PasskeyRequestOptions options, CancellationToken cancellationToken = default) =>
		throw new FeatureNotSupportedException("Sailfish OS has no passkey (WebAuthn) authenticator.");
}

/// <summary>Platform services talk to QML on the Qt (main) thread; a call from elsewhere hops there.</summary>
internal static class SailfishMainThreadCall
{
	public static Task<T> Run<T>(Func<T> work)
	{
		if (!QtHostRuntime.IsRunning || QtHostRuntime.IsQtThread)
		{
			try
			{
				return Task.FromResult(work());
			}
			catch (Exception ex)
			{
				return Task.FromException<T>(ex);
			}
		}
		return MainThread.InvokeOnMainThreadAsync(work);
	}
}
