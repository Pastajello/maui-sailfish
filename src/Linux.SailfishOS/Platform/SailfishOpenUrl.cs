using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// URLs and files the system hands to the app (<c>SailfishUrlSchemes</c> / <c>SailfishMimeTypes</c> in the project).
/// The first launch gets them as arguments (<c>Exec=… %U</c>); a running instance gets them through its D-Bus
/// <c>openUrl(as)</c> method (the .desktop <c>X-Maemo-*</c> entries, as native Sailfish apps declare it). Either way they
/// reach MAUI's <c>Application.OnAppLinkRequestReceived(Uri)</c>, as deep links do on Android and iOS.
/// </summary>
internal static class SailfishOpenUrl
{
	private static string? _service, _path, _iface;
	private static readonly List<Uri> PendingLaunch = new();
	private static bool _ready;

	/// <summary>Diagnostics: every URI delivered to MAUI.</summary>
	internal static event Action<Uri>? Delivered;

	/// <summary>Takes a URI before the app sees it (a pending WebAuthenticator sign-in); true = consumed.</summary>
	internal static Func<Uri, bool>? Intercept;

	/// <summary>The D-Bus names baked into maui-appmeta.json; absent when the app declares no schemes or types.</summary>
	internal static void Configure(string? service, string? path, string? iface)
	{
		_service = string.IsNullOrEmpty(service) ? null : service;
		_path = path;
		_iface = iface;
	}

	internal static bool Enabled => _service is not null;

	/// <summary>Launch arguments that are URLs or existing files (the %U of the .desktop Exec line).</summary>
	internal static void QueueLaunchArguments(IEnumerable<string> arguments)
	{
		foreach (var argument in arguments)
			if (ToUri(argument) is { } uri)
				PendingLaunch.Add(uri);
	}

	/// <summary>First Qt tick: registers the D-Bus adaptor and delivers the launch URLs.</summary>
	internal static void OnHostReady()
	{
		_ready = true;
		if (Enabled)
		{
			QtHostServices.Subscribe(ShellEvents.OpenUrl, e =>
			{
				if (!e.TryGetProperty("urls", out var urls) || urls.ValueKind != System.Text.Json.JsonValueKind.Array)
					return;
				foreach (var url in urls.EnumerateArray())
					if (ToUri(url.GetString()) is { } uri)
						Deliver(uri);
			});
			var qml = $$"""
				import QtQuick 2.6
				import Nemo.DBus 2.0
				DBusAdaptor {
				    service: "{{_service}}"
				    path: "{{_path}}"
				    iface: "{{_iface}}"
				    xml: '<interface name="{{_iface}}"><method name="openUrl"><arg name="urls" type="as" direction="in"/></method></interface>'
				    function openUrl(urls) {
				        window.mauiAppNotify("svc-open-url", JSON.stringify({ urls: urls }));
				        window.activate();
				    }
				}
				""";
			if (!QtHostServices.Ensure("open-url", qml))
				QtHostDiag.Warn(QtHostDiagChannel.QtHost, $"D-Bus service {_service} not registered — a running instance will not receive URLs");
		}
		foreach (var uri in PendingLaunch)
			Deliver(uri);
		PendingLaunch.Clear();
	}

	internal static void Deliver(Uri uri)
	{
		if (Intercept?.Invoke(uri) == true)
		{
			QtHostDiag.Trace(QtHostDiagChannel.QtHost, $"open url → WebAuthenticator callback ({uri.Scheme}://…)");
			Delivered?.Invoke(uri);
			return;
		}
		QtHostDiag.Trace(QtHostDiagChannel.QtHost, $"open url → OnAppLinkRequestReceived({uri})");
		try
		{
			(Microsoft.Maui.Controls.Application.Current)?.SendOnAppLinkRequestReceived(uri);
		}
		catch (Exception ex)
		{
			QtHostDiag.Error(QtHostDiagChannel.QtHost, $"OnAppLinkRequestReceived({uri}) threw: {ex.Message}");
		}
		Delivered?.Invoke(uri);
	}

	/// <summary>A URL with a scheme, or an existing file path as a file:// URI; null for anything else.</summary>
	internal static Uri? ToUri(string? value)
	{
		if (string.IsNullOrWhiteSpace(value) || value.StartsWith('-'))
			return null;
		if (value.Contains("://", StringComparison.Ordinal) && Uri.TryCreate(value, UriKind.Absolute, out var uri))
			return uri;
		return File.Exists(value) ? new Uri(Path.GetFullPath(value)) : null;
	}

	/// <summary>Diagnostics: runs the D-Bus path end to end by calling the app's own openUrl method.</summary>
	internal static string CallSelf(string url) =>
		!Enabled || !_ready
			? "disabled"
			: QtHostRuntime.Eval($$"""
				(function(){
				  var c = Qt.createQmlObject('import QtQuick 2.6; import Nemo.DBus 2.0; DBusInterface { service: "{{_service}}"; path: "{{_path}}"; iface: "{{_iface}}" }', window);
				  c.call("openUrl", [[{{BridgeValue.Quote(url)}}]]);
				  c.destroy(2000);
				  return "called";
				})()
				""");
}
