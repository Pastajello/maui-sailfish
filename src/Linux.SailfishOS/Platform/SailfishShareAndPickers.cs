using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.Communication;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Media;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Microsoft.Maui.Storage;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Share via the Sailfish.Share system sheet; text and URIs go as data resources, files as paths.
/// </summary>
public sealed class SailfishShare : IShare
{
	private const string Service = "share";

	private const string Qml = """
		import QtQuick 2.6
		import Sailfish.Share 1.0
		ShareAction {
		    function prepare(json) {
		        var o = JSON.parse(json);
		        title = o.title || "";
		        mimeType = o.mimeType;
		        resources = o.resources;
		        return JSON.stringify(toConfiguration());
		    }
		}
		""";

	/// <summary>Configures the share action without opening the sheet (diagnostics).</summary>
	internal static string Prepare(string title, string mimeType, IEnumerable<object> resources)
	{
		if (!QtHostServices.Ensure(Service, Qml))
			throw new FeatureNotSupportedException("Sailfish.Share is not available.");
		var sb = new StringBuilder("{\"title\":").Append(QtHostServices.Js(title))
			.Append(",\"mimeType\":").Append(QtHostServices.Js(mimeType)).Append(",\"resources\":[");
		var first = true;
		foreach (var resource in resources)
		{
			if (!first)
				sb.Append(',');
			first = false;
			sb.Append(resource switch
			{
				string path => QtHostServices.Js(path),
				(string name, string data, string type) =>
					"{\"name\":" + QtHostServices.Js(name) + ",\"data\":" + QtHostServices.Js(data) + ",\"type\":" + QtHostServices.Js(type) + "}",
				_ => "null",
			});
		}
		sb.Append("]}");
		return QtHostServices.Eval(Service, $"s.prepare({QtHostServices.Js(sb.ToString())})");
	}

	internal static (string Title, string Mime, List<object> Resources) Describe(ShareTextRequest request)
	{
		var resources = new List<object>();
		var mime = "text/plain";
		if (!string.IsNullOrEmpty(request.Uri))
		{
			mime = "text/x-url";
			resources.Add((request.Subject ?? request.Title ?? request.Uri, request.Uri, "text/x-url"));
		}
		else
		{
			resources.Add((request.Subject ?? request.Title ?? "text", request.Text ?? string.Empty, "text/plain"));
		}
		return (request.Title ?? string.Empty, mime, resources);
	}

	public Task RequestAsync(ShareTextRequest request)
	{
		var (title, mime, resources) = Describe(request);
		Prepare(title, mime, resources);
		QtHostServices.Eval(Service, "(function(){s.trigger();return 'ok';})()");
		return Task.CompletedTask;
	}

	public Task RequestAsync(ShareFileRequest request) =>
		RequestAsync(new ShareMultipleFilesRequest(request.Title ?? string.Empty, new[] { request.File! }));

	public Task RequestAsync(ShareMultipleFilesRequest request)
	{
		var files = request.Files?.Where(f => f is not null).ToList() ?? new List<ShareFile>();
		if (files.Count == 0)
			return Task.CompletedTask;
		var mime = files.Select(f => f.ContentType).Distinct().Count() == 1 ? files[0].ContentType : "application/octet-stream";
		Prepare(request.Title ?? string.Empty, string.IsNullOrEmpty(mime) ? "application/octet-stream" : mime, files.Select(f => (object)f.FullPath));
		QtHostServices.Eval(Service, "(function(){s.trigger();return 'ok';})()");
		return Task.CompletedTask;
	}
}

/// <summary>
/// MediaPicker and FilePicker on Sailfish.Pickers; closing the page without a selection is a cancel (null).
/// Capture is unsupported: Sailfish has no in-app camera API.
/// </summary>
public sealed class SailfishPickers : IMediaPicker, IFilePicker
{
	private const string Service = "pickers";
	private static TaskCompletionSource<List<string>>? _pending;
	private static bool _subscribed;

	private const string Qml = """
		import QtQuick 2.6
		import Sailfish.Silica 1.0
		import Sailfish.Pickers 1.0
		QtObject {
		    property Component image: Component { ImagePickerPage {} }
		    property Component video: Component { VideoPickerPage {} }
		    property Component file: Component { FilePickerPage {} }
		    property Component images: Component { MultiImagePickerDialog {} }
		    property Component videos: Component { MultiVideoPickerDialog {} }
		    property Component files: Component { MultiFilePickerDialog {} }
		    property var current: null
		    function report(paths, cancelled) {
		        window.mauiAppNotify("svc-pickers-result", JSON.stringify({ paths: paths, cancelled: cancelled }));
		    }
		    function pick(kind, filters) {
		        var comp = this[kind];
		        var props = {};
		        if (filters.length > 0 && (kind === "file" || kind === "files"))
		            props.nameFilters = filters;
		        var page = pageStack.push(comp, props);
		        current = page;
		        var done = false;
		        var seenActive = false;
		        if (kind === "image" || kind === "video" || kind === "file") {
		            page.selectedContentPropertiesChanged.connect(function() {
		                if (done || !page.selectedContentProperties) return;
		                var p = page.selectedContentProperties.filePath;
		                if (!p) return;
		                done = true;
		                report([String(p)], false);
		            });
		        } else {
		            page.accepted.connect(function() {
		                if (done) return;
		                done = true;
		                var out = [];
		                for (var i = 0; i < page.selectedContent.count; ++i)
		                    out.push(String(page.selectedContent.get(i).filePath));
		                report(out, false);
		            });
		        }
		        page.statusChanged.connect(function() {
		            if (page.status === PageStatus.Active) seenActive = true;
		            else if (page.status === PageStatus.Inactive && seenActive && !done) { done = true; report([], true); }
		        });
		        return "ok";
		    }
		}
		""";

	private static Task<List<string>> Pick(string kind, IEnumerable<string>? filters = null)
	{
		if (!QtHostServices.Ensure(Service, Qml))
			throw new FeatureNotSupportedException("Sailfish.Pickers is not available.");
		if (!_subscribed)
		{
			_subscribed = true;
			QtHostServices.Subscribe("svc-pickers-result", e =>
			{
				var paths = new List<string>();
				if (e.TryGetProperty("paths", out var array) && array.ValueKind == JsonValueKind.Array)
					foreach (var p in array.EnumerateArray())
						if (p.GetString() is { Length: > 0 } path)
							paths.Add(path.StartsWith("file://", StringComparison.Ordinal) ? new Uri(path).LocalPath : path);
				var pending = _pending;
				_pending = null;
				pending?.TrySetResult(paths);
			});
		}
		_pending?.TrySetResult(new List<string>());   // a new request supersedes an open one
		_pending = new TaskCompletionSource<List<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
		var filterJs = "[" + string.Join(",", (filters ?? Array.Empty<string>()).Select(QtHostServices.Js)) + "]";
		var result = QtHostServices.Eval(Service, $"s.pick({QtHostServices.Js(kind)},{filterJs})");
		if (result != "ok")
		{
			_pending.TrySetResult(new List<string>());
			QtHostDiag.Warn(QtHostDiagChannel.QtHost, $"picker '{kind}' failed to open: {result}");
		}
		return _pending.Task;
	}

	private static FileResult? One(List<string> paths) => paths.Count > 0 ? new FileResult(paths[0]) : null;

	private static List<FileResult> Many(List<string> paths) => paths.Select(p => new FileResult(p)).ToList();

	public bool IsCaptureSupported => false;

	public async Task<FileResult?> PickPhotoAsync(MediaPickerOptions? options = null) => One(await Pick("image").ConfigureAwait(true));

	public async Task<List<FileResult>> PickPhotosAsync(MediaPickerOptions? options = null) => Many(await Pick("images").ConfigureAwait(true));

	public async Task<FileResult?> PickVideoAsync(MediaPickerOptions? options = null) => One(await Pick("video").ConfigureAwait(true));

	public async Task<List<FileResult>> PickVideosAsync(MediaPickerOptions? options = null) => Many(await Pick("videos").ConfigureAwait(true));

	public Task<FileResult?> CapturePhotoAsync(MediaPickerOptions? options = null) =>
		throw new FeatureNotSupportedException("Sailfish OS has no in-app camera capture API; use PickPhotoAsync.");

	public Task<FileResult?> CaptureVideoAsync(MediaPickerOptions? options = null) =>
		throw new FeatureNotSupportedException("Sailfish OS has no in-app camera capture API; use PickVideoAsync.");

	/// <summary>Name filters from PickOptions.FileTypes; MIME types cannot be filtered.</summary>
	private static IEnumerable<string> Filters(PickOptions? options)
	{
		var types = options?.FileTypes?.Value;
		if (types is null)
			return Array.Empty<string>();
		return types.Where(t => t.StartsWith('.') || !t.Contains('/'))
			.Select(t => "*." + t.TrimStart('.', '*'));
	}

	public async Task<FileResult?> PickAsync(PickOptions? options = null) => One(await Pick("file", Filters(options)).ConfigureAwait(true));

	public async Task<IEnumerable<FileResult?>> PickMultipleAsync(PickOptions? options = null) =>
		Many(await Pick("files", Filters(options)).ConfigureAwait(true));

	/// <summary>The open picker page (diagnostics).</summary>
	internal static string CurrentPickerJs => "window.mauiServices['pickers'].current";
}

/// <summary>
/// Permissions under Sailjail, which asks at launch only, so Request equals Check: Granted when unsandboxed or
/// the needed Sailjail permission is declared. Permissions without a Sailjail counterpart are always Granted.
/// </summary>
public sealed class SailfishPermissions : IPermissions
{
	private static readonly Lazy<(bool Sandboxed, HashSet<string> Declared)> Policy = new(ReadPolicy);

	private static (bool, HashSet<string>) ReadPolicy()
	{
		var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var desktop = Path.Combine("/usr/share/applications", SailfishAppPaths.PackageName + ".desktop");
		if (!File.Exists(desktop))
			return (false, declared);
		var inSailjail = false;
		var sandboxed = false;
		foreach (var raw in File.ReadAllLines(desktop))
		{
			var line = raw.Trim();
			if (line.StartsWith('['))
			{
				inSailjail = line == "[X-Sailjail]";
				continue;
			}
			if (!inSailjail)
				continue;
			if (line.StartsWith("Permissions=", StringComparison.Ordinal))
			{
				sandboxed = true;
				foreach (var p in line["Permissions=".Length..].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
					declared.Add(p);
			}
			else if (line.StartsWith("Sandboxing=", StringComparison.Ordinal))
			{
				sandboxed = !string.Equals(line["Sandboxing=".Length..].Trim(), "Disabled", StringComparison.OrdinalIgnoreCase);
			}
		}
		return (sandboxed, declared);
	}

	/// <summary>Sailjail permissions that satisfy a MAUI permission (any one); empty when none is needed.</summary>
	internal static string[] SailjailFor(Type permission) => permission.Name switch
	{
		"CalendarRead" or "CalendarWrite" or "Reminders" => new[] { "Calendar" },
		"Camera" => new[] { "Camera" },
		"ContactsRead" or "ContactsWrite" => new[] { "Contacts" },
		"LocationWhenInUse" or "LocationAlways" or "Maps" => new[] { "Location" },
		"Microphone" or "Speech" => new[] { "Microphone" },
		"Phone" => new[] { "Phone" },
		"Sms" => new[] { "Messages" },
		"Photos" or "PhotosAddOnly" => new[] { "Pictures" },
		"Media" => new[] { "Music", "Videos" },
		"StorageRead" or "StorageWrite" => new[] { "UserDirs", "Documents", "Pictures" },
		"Bluetooth" or "NearbyWifiDevices" => new[] { "Bluetooth" },
		"LaunchApp" => new[] { "AppLaunch" },
		_ => Array.Empty<string>(),
	};

	internal static PermissionStatus StatusFor(Type permission)
	{
		var (sandboxed, declared) = Policy.Value;
		var needs = SailjailFor(permission);
		if (!sandboxed || needs.Length == 0)
			return PermissionStatus.Granted;
		return needs.Any(declared.Contains) ? PermissionStatus.Granted : PermissionStatus.Denied;
	}

	public Task<PermissionStatus> CheckStatusAsync<TPermission>() where TPermission : Permissions.BasePermission, new() =>
		Task.FromResult(StatusFor(typeof(TPermission)));

	public Task<PermissionStatus> RequestAsync<TPermission>() where TPermission : Permissions.BasePermission, new() =>
		CheckStatusAsync<TPermission>();

	public bool ShouldShowRationale<TPermission>() where TPermission : Permissions.BasePermission, new() => false;
}

/// <summary>Dialer, e-mail, SMS and maps via URI schemes, which lipstick routes to the Sailfish apps.</summary>
public sealed class SailfishCommunication : IPhoneDialer, IEmail, ISms, IMap
{
	bool IPhoneDialer.IsSupported => true;

	public void Open(string number)
	{
		if (string.IsNullOrWhiteSpace(number))
			throw new ArgumentNullException(nameof(number));
		_ = SailfishBrowser.OpenUrl(DialUri(number));
	}

	internal static string DialUri(string number) => "tel:" + Uri.EscapeDataString(number.Trim());

	public bool IsComposeSupported => true;

	public Task ComposeAsync(EmailMessage? message) => SailfishBrowser.OpenUrl(MailUri(message));

	internal static string MailUri(EmailMessage? message)
	{
		var to = string.Join(",", message?.To ?? new List<string>());
		var query = new List<string>();
		if (message?.Cc is { Count: > 0 } cc)
			query.Add("cc=" + Uri.EscapeDataString(string.Join(",", cc)));
		if (message?.Bcc is { Count: > 0 } bcc)
			query.Add("bcc=" + Uri.EscapeDataString(string.Join(",", bcc)));
		if (!string.IsNullOrEmpty(message?.Subject))
			query.Add("subject=" + Uri.EscapeDataString(message.Subject));
		if (!string.IsNullOrEmpty(message?.Body))
			query.Add("body=" + Uri.EscapeDataString(message.Body));
		return "mailto:" + to + (query.Count > 0 ? "?" + string.Join("&", query) : string.Empty);
	}

	public Task ComposeAsync(SmsMessage? message) => SailfishBrowser.OpenUrl(SmsUri(message));

	internal static string SmsUri(SmsMessage? message)
	{
		var to = string.Join(",", message?.Recipients ?? new List<string>());
		return "sms:" + to + (string.IsNullOrEmpty(message?.Body) ? string.Empty : "?body=" + Uri.EscapeDataString(message.Body));
	}

	public Task OpenAsync(double latitude, double longitude, MapLaunchOptions options) =>
		SailfishBrowser.OpenUrl(GeoUri(latitude, longitude, options?.Name));

	public Task OpenAsync(Placemark placemark, MapLaunchOptions options)
	{
		var location = placemark.Location;
		return location is null
			? SailfishBrowser.OpenUrl("geo:0,0?q=" + Uri.EscapeDataString(string.Join(", ",
				new[] { placemark.Thoroughfare, placemark.Locality, placemark.CountryName }.Where(s => !string.IsNullOrEmpty(s)))))
			: OpenAsync(location.Latitude, location.Longitude, options);
	}

	public async Task<bool> TryOpenAsync(double latitude, double longitude, MapLaunchOptions options)
	{
		await OpenAsync(latitude, longitude, options).ConfigureAwait(true);
		return true;
	}

	public async Task<bool> TryOpenAsync(Placemark placemark, MapLaunchOptions options)
	{
		await OpenAsync(placemark, options).ConfigureAwait(true);
		return true;
	}

	internal static string GeoUri(double latitude, double longitude, string? name)
	{
		var at = latitude.ToString("R", CultureInfo.InvariantCulture) + "," + longitude.ToString("R", CultureInfo.InvariantCulture);
		return "geo:" + at + (string.IsNullOrEmpty(name) ? string.Empty : "?q=" + at + "(" + Uri.EscapeDataString(name) + ")");
	}
}

/// <summary>Screenshot of the app window via QQuickWindow::grabWindow.</summary>
public sealed class SailfishScreenshot : IScreenshot
{
	public bool IsCaptureSupported => QtHostRuntime.IsRunning;

	public Task<IScreenshotResult> CaptureAsync()
	{
		var path = Path.Combine(SailfishAppPaths.CacheDirectory, $"screenshot-{Guid.NewGuid():N}.png");
		if (QtHostRuntime.GrabPng(path) != 0 || !File.Exists(path))
			throw new InvalidOperationException($"Screenshot capture failed: {QtHostRuntime.LastErrorText}");
		return Task.FromResult<IScreenshotResult>(new ScreenshotFile(path));
	}

	private sealed class ScreenshotFile : IScreenshotResult
	{
		private readonly string _path;

		public ScreenshotFile(string path)
		{
			_path = path;
			// PNG IHDR: width/height big-endian at bytes 16..23
			using var fs = File.OpenRead(path);
			var header = new byte[24];
			fs.ReadExactly(header);
			Width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
			Height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
		}

		public int Width { get; }

		public int Height { get; }

		public Task<Stream> OpenReadAsync(ScreenshotFormat format = ScreenshotFormat.Png, int quality = 100)
		{
			if (format != ScreenshotFormat.Png)
				throw new NotSupportedException("Sailfish screenshots are PNG.");
			return Task.FromResult<Stream>(File.OpenRead(_path));
		}

		public async Task CopyToAsync(Stream destination, ScreenshotFormat format = ScreenshotFormat.Png, int quality = 100)
		{
			await using var source = await OpenReadAsync(format, quality).ConfigureAwait(true);
			await source.CopyToAsync(destination).ConfigureAwait(true);
		}
	}
}
