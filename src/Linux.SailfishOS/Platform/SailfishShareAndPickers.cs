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
internal sealed class SailfishShare : IShare
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
		var json = BridgeJson.Write(w =>
		{
			w.WriteString("title", title);
			w.WriteString("mimeType", mimeType);
			w.WriteStartArray("resources");
			foreach (var resource in resources)
			{
				switch (resource)
				{
					case string path:
						w.WriteStringValue(path);
						break;
					case (string name, string data, string type):
						w.WriteStartObject();
						w.WriteString("name", name);
						w.WriteString("data", data);
						w.WriteString("type", type);
						w.WriteEndObject();
						break;
					default:
						w.WriteNullValue();
						break;
				}
			}
			w.WriteEndArray();
		});
		return QtHostServices.Eval(Service, $"s.prepare({QtHostServices.Js(json)})");
	}

	internal static (string Title, string Mime, List<object> Resources) Describe(ShareTextRequest request)
	{
		var resources = new List<object>();
		var mime = "text/plain";
		if (!string.IsNullOrEmpty(request.Uri) && !string.IsNullOrEmpty(request.Text))
		{
			// Both: one text, the link after the message, as Android puts them into EXTRA_TEXT (the Uri was dropped
			// the Text before, tracker S10).
			resources.Add((request.Subject ?? request.Title ?? "text", request.Text + "\n" + request.Uri, "text/plain"));
		}
		else if (!string.IsNullOrEmpty(request.Uri))
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
internal sealed class SailfishPickers : IMediaPicker, IFilePicker
{
	private const string Service = "pickers";
	// One native picker service for both MediaPicker and FilePicker instances, so the open request is shared. Touched
	// only on the Qt thread: Pick hops there (W1.8), and the result event arrives there.
	private static TaskCompletionSource<List<string>>? _pending;

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
		    function pick(kind, filters, title) {
		        var comp = this[kind];
		        var props = {};
		        if (filters.length > 0 && (kind === "file" || kind === "files"))
		            props.nameFilters = filters;
		        if (title && title.length > 0)
		            props.title = title;
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

	private static Task<List<string>> Pick(string kind, IEnumerable<string>? filters = null, string? title = null) =>
		QtThread.Run(() => PickOnQt(kind, filters?.ToList(), title));

	private static Task<List<string>> PickOnQt(string kind, IEnumerable<string>? filters, string? title)
	{
		WarnIfUndeclared(kind);
		if (!QtHostServices.Ensure(Service, Qml, (ShellEvents.PickersResult, OnResult)))
			throw new FeatureNotSupportedException("Sailfish.Pickers is not available.");
		_pending?.TrySetResult(new List<string>());   // a new request supersedes an open one
		_pending = new TaskCompletionSource<List<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
		var filterJs = "[" + string.Join(",", (filters ?? Array.Empty<string>()).Select(QtHostServices.Js)) + "]";
		var result = QtHostServices.Eval(Service, $"s.pick({QtHostServices.Js(kind)},{filterJs},{QtHostServices.Js(title ?? string.Empty)})");
		if (result != "ok")
		{
			_pending.TrySetResult(new List<string>());
			QtHostDiag.Warn(QtHostDiagChannel.QtHost, $"picker '{kind}' failed to open: {result}");
		}
		return _pending.Task;
	}

	private static readonly HashSet<string> _warnedKinds = new();

	/// <summary>Sailfish.Pickers run inside the app's sandbox: without the folders' permissions the picker opens empty.
	/// MAUI's pickers need no permission on Android (the photo picker) or iOS, so this is a warning, not a
	/// PermissionException (tracker S12).</summary>
	private static void WarnIfUndeclared(string kind)
	{
		var needs = PickerPermissions(kind);
		if (needs.Length == 0 || needs.All(SailfishPermissions.Declares) || !_warnedKinds.Add(kind))
			return;
		QtHostDiag.Warn(QtHostDiagChannel.QtHost,
			$"picker '{kind}': the Sailjail sandbox shows only declared folders; add {string.Join(", ", needs.Where(n => !SailfishPermissions.Declares(n)))} " +
			"to <SailfishPermissions> in the csproj, or the picker opens empty");
	}

	internal static string[] PickerPermissions(string kind) => kind switch
	{
		"image" or "images" => new[] { "Pictures", "MediaIndexing" },
		"video" or "videos" => new[] { "Videos", "MediaIndexing" },
		"file" or "files" => new[] { "UserDirs", "Documents", "Downloads" },
		_ => Array.Empty<string>(),
	};

	private static void OnResult(JsonElement e)
	{
		var paths = new List<string>();
		if (e.TryGetProperty("paths", out var array) && array.ValueKind == JsonValueKind.Array)
			foreach (var p in array.EnumerateArray())
				if (p.GetString() is { Length: > 0 } path)
					paths.Add(path.StartsWith("file://", StringComparison.Ordinal) ? new Uri(path).LocalPath : path);
		var pending = _pending;
		_pending = null;
		pending?.TrySetResult(paths);
	}

	private static FileResult? One(List<string> paths) => paths.Count > 0 ? ToFileResult(paths[0]) : null;

	private static List<FileResult> Many(List<string> paths) => paths.Select(ToFileResult).ToList();

	/// <summary>A picked file with its MIME type: the plain-net FileBase cannot resolve one (its platform lookup
	/// throws), so ContentType threw for every picked file. OpenReadAsync stays MAUI's and throws on plain .NET; read
	/// <see cref="FileBase.FullPath"/> instead (porting-existing-apps.md).</summary>
	internal static FileResult ToFileResult(string path) => new(path, MimeTypes.For(path));

	public bool IsCaptureSupported => false;

	public async Task<FileResult?> PickPhotoAsync(MediaPickerOptions? options = null) =>
		One(await Pick("image", title: options?.Title).ConfigureAwait(true));

	public async Task<List<FileResult>> PickPhotosAsync(MediaPickerOptions? options = null) =>
		Limit(Many(await Pick("images", title: options?.Title).ConfigureAwait(true)), options);

	public async Task<FileResult?> PickVideoAsync(MediaPickerOptions? options = null) =>
		One(await Pick("video", title: options?.Title).ConfigureAwait(true));

	public async Task<List<FileResult>> PickVideosAsync(MediaPickerOptions? options = null) =>
		Limit(Many(await Pick("videos", title: options?.Title).ConfigureAwait(true)), options);

	/// <summary>MediaPickerOptions.SelectionLimit (0 = no limit): Silica's multi-pickers cannot cap the selection, so
	/// the first ones picked are kept, as Android does where its picker does not enforce the limit.</summary>
	internal static List<FileResult> Limit(List<FileResult> picked, MediaPickerOptions? options) =>
		options is { SelectionLimit: > 0 } && picked.Count > options.SelectionLimit
			? picked.Take(options.SelectionLimit).ToList()
			: picked;

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

	public async Task<FileResult?> PickAsync(PickOptions? options = null) =>
		One(await Pick("file", Filters(options), options?.PickerTitle).ConfigureAwait(true));

	public async Task<IEnumerable<FileResult?>> PickMultipleAsync(PickOptions? options = null) =>
		Many(await Pick("files", Filters(options), options?.PickerTitle).ConfigureAwait(true));

	/// <summary>The open picker page (diagnostics).</summary>
	internal static string CurrentPickerJs => "window.mauiServices['pickers'].current";
}

/// <summary>
/// Permissions under Sailjail, which asks at launch only, so Request equals Check: Granted when unsandboxed or
/// the needed Sailjail permission is declared. Permissions without a Sailjail counterpart are always Granted.
/// </summary>
internal sealed class SailfishPermissions : IPermissions
{
	private static readonly Lazy<(bool Sandboxed, HashSet<string> Declared)> Policy = new(ReadPolicy);

	/// <summary>The app runs in Sailjail (its .desktop file lists Permissions).</summary>
	internal static bool IsSandboxed => Policy.Value.Sandboxed;

	private static (bool, HashSet<string>) ReadPolicy()
	{
		var desktop = Path.Combine("/usr/share/applications", SailfishAppPaths.PackageName + ".desktop");
		return File.Exists(desktop)
			? ParsePolicy(File.ReadAllLines(desktop))
			: (false, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
	}

	/// <summary>The [X-Sailjail] section of a .desktop file: an empty Permissions= line is still a sandbox.</summary>
	internal static (bool Sandboxed, HashSet<string> Declared) ParsePolicy(IEnumerable<string> desktopLines)
	{
		var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var inSailjail = false;
		var sandboxed = false;
		foreach (var raw in desktopLines)
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
		"LaunchApp" => new[] { "AppLaunch" },   // a Sailjail permission (checked on SFOS 5.2, tracker S12)
		"Sensors" => new[] { "Sensors" },       // sensorfw inside the sandbox
		_ => Array.Empty<string>(),
	};

	/// <summary>Permissions no Sailjail permission grants inside a sandbox: the flashlight service is not reachable
	/// from Sailjail at all, so its permission reads Denied there rather than Granted (Flashlight then throws).</summary>
	internal static bool UnavailableInSandbox(Type permission) => permission.Name == "Flashlight";

	internal static PermissionStatus StatusFor(Type permission) => StatusFor(permission, Policy.Value);

	internal static PermissionStatus StatusFor(Type permission, (bool Sandboxed, HashSet<string> Declared) policy)
	{
		if (policy.Sandboxed && UnavailableInSandbox(permission))
			return PermissionStatus.Denied;
		var needs = SailjailFor(permission);
		if (!policy.Sandboxed || needs.Length == 0)
			return PermissionStatus.Granted;
		return needs.Any(policy.Declared.Contains) ? PermissionStatus.Granted : PermissionStatus.Denied;
	}

	/// <summary>Unsandboxed, or the Sailjail permission is declared (WebView, Secrets… have no MAUI permission type).</summary>
	internal static bool Declares(string sailjailPermission) =>
		!Policy.Value.Sandboxed || Policy.Value.Declared.Contains(sailjailPermission);

	/// <summary>Throws the PermissionException MAUI throws on Android/iOS when <paramref name="feature"/> runs undeclared.</summary>
	internal static void Demand(Type permission, string feature)
	{
		if (Missing(permission, feature) is { } missing)
			throw missing;
	}

	/// <summary><see cref="Demand"/> for a non-async Task method, which returns the exception as a faulted task.</summary>
	internal static PermissionException? Missing(Type permission, string feature) =>
		StatusFor(permission) == PermissionStatus.Granted ? null : new PermissionException(MissingMessage(feature, SailjailFor(permission)));

	internal static string MissingMessage(string feature, string[] sailjail) =>
		$"{feature} needs the {string.Join(" or ", sailjail)} Sailjail permission: add {sailjail[0]} to <SailfishPermissions> in the project file.";

	public Task<PermissionStatus> CheckStatusAsync<TPermission>() where TPermission : Permissions.BasePermission, new() =>
		Task.FromResult(StatusFor(typeof(TPermission)));

	public Task<PermissionStatus> RequestAsync<TPermission>() where TPermission : Permissions.BasePermission, new() =>
		CheckStatusAsync<TPermission>();

	public bool ShouldShowRationale<TPermission>() where TPermission : Permissions.BasePermission, new() => false;
}

/// <summary>Dialer, e-mail, SMS and maps via URI schemes, which lipstick routes to the Sailfish apps.</summary>
internal sealed class SailfishCommunication : IPhoneDialer, IEmail, ISms, IMap
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

	/// <summary>A mailto: link to the mail app. A mailto: link carries no attachments and no HTML body, so a message
	/// with attachments is refused (FeatureNotSupportedException, as MAUI's Windows head does for what it cannot send)
	/// instead of going out without them; an HTML body goes as its text.</summary>
	public Task ComposeAsync(EmailMessage? message)
	{
		if (message?.Attachments is { Count: > 0 })
			throw new FeatureNotSupportedException("Email attachments: Sailfish OS opens the mail app through a mailto: link, which cannot carry attachments. Share the file with Share.RequestAsync instead.");
		return SailfishBrowser.OpenUrl(MailUri(message));
	}

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
			? SailfishBrowser.OpenUrl(PlacemarkUri(placemark))
			: OpenAsync(location.Latitude, location.Longitude, options);
	}

	internal static string PlacemarkUri(Placemark placemark) =>
		"geo:0,0?q=" + Uri.EscapeDataString(string.Join(", ",
			new[] { placemark.Thoroughfare, placemark.Locality, placemark.CountryName }.Where(s => !string.IsNullOrEmpty(s))));

	/// <summary>Whether a map app took the geo: link (it was always true before, tracker S10).</summary>
	public Task<bool> TryOpenAsync(double latitude, double longitude, MapLaunchOptions options) =>
		SailfishBrowser.OpenUrl(GeoUri(latitude, longitude, options?.Name));

	public Task<bool> TryOpenAsync(Placemark placemark, MapLaunchOptions options) =>
		placemark.Location is { } location
			? TryOpenAsync(location.Latitude, location.Longitude, options)
			: SailfishBrowser.OpenUrl(PlacemarkUri(placemark));

	internal static string GeoUri(double latitude, double longitude, string? name)
	{
		var at = latitude.ToString("R", CultureInfo.InvariantCulture) + "," + longitude.ToString("R", CultureInfo.InvariantCulture);
		return "geo:" + at + (string.IsNullOrEmpty(name) ? string.Empty : "?q=" + at + "(" + Uri.EscapeDataString(name) + ")");
	}
}

/// <summary>Screenshot of the app window via QQuickWindow::grabWindow.</summary>
/// <summary>
/// Screenshots of the app window (IScreenshot) and, new in MAUI 11, of one view or window (IViewScreenshot, behind
/// <c>view.CaptureAsync()</c>): a hosted view is cut out of the window grab at its scene rect, so what is drawn over it
/// (a sibling above it) is in the picture too. The image is kept in memory; the grab's file is deleted at once.
/// </summary>
internal sealed class SailfishScreenshot : IScreenshot, IViewScreenshot
{
	public bool IsCaptureSupported => QtHostRuntime.IsRunning;

	public Task<IScreenshotResult> CaptureAsync() =>
		Task.FromResult<IScreenshotResult>(QtThread.Run(() => Grab(null))
			?? throw new InvalidOperationException($"Screenshot capture failed: {QtHostRuntime.LastErrorText}"));

	/// <summary>A hosted view (its <see cref="NativeElementHost"/>) → its part of the window; a window or anything else
	/// → the window. Null when the view is not on screen (no native object, or an empty rect).</summary>
	public Task<IScreenshotResult?> CaptureViewAsync(object platformView) => Task.FromResult(QtThread.Run(() =>
	{
		if (platformView is not NativeElementHost host)
			return Grab(null);
		if (!host.IsAttached || !QtHostRuntime.TryItemGeometry(host.NativeHandle, out var scene) || scene.Width < 1 || scene.Height < 1)
			return null;
		return Grab(scene);
	}));

	/// <summary>Qt thread.</summary>
	private static IScreenshotResult? Grab(NativeGeometry? sceneRect)
	{
		var path = Path.Combine(SailfishAppPaths.CacheDirectory, $"screenshot-{Guid.NewGuid():N}.png");
		try
		{
			if (QtHostRuntime.GrabImage(path, sceneRect) != 0 || !File.Exists(path))
				return null;
			return new ScreenshotImage(File.ReadAllBytes(path));
		}
		finally
		{
			TryDelete(path);
		}
	}

	private static void TryDelete(string path)
	{
		try
		{
			File.Delete(path);
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}

	/// <summary>A PNG in memory; JPEG is re-encoded by the shim on request (quality 0..100).</summary>
	internal sealed class ScreenshotImage : IScreenshotResult
	{
		private readonly byte[] _png;

		public ScreenshotImage(byte[] png)
		{
			_png = png;
			// PNG IHDR: width/height big-endian at bytes 16..23
			if (png.Length >= 24)
			{
				Width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
				Height = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
			}
		}

		public int Width { get; }

		public int Height { get; }

		/// <summary>The PNG as grabbed (SailfishScreenshotExtensions.ToImageAsync reads it without a copy).</summary>
		internal byte[] Png => _png;

		public Task<Stream> OpenReadAsync(ScreenshotFormat format = ScreenshotFormat.Png, int quality = 100) =>
			Task.FromResult<Stream>(new MemoryStream(format == ScreenshotFormat.Jpeg ? Jpeg(quality) : _png, writable: false));

		public async Task CopyToAsync(Stream destination, ScreenshotFormat format = ScreenshotFormat.Png, int quality = 100)
		{
			await using var source = await OpenReadAsync(format, quality).ConfigureAwait(true);
			await source.CopyToAsync(destination).ConfigureAwait(true);
		}

		private byte[] Jpeg(int quality)
		{
			var stem = Path.Combine(SailfishAppPaths.CacheDirectory, $"screenshot-{Guid.NewGuid():N}");
			var png = stem + ".png";
			var jpg = stem + ".jpg";
			try
			{
				File.WriteAllBytes(png, _png);
				if (QtHostRuntime.ConvertImage(png, jpg, Math.Clamp(quality, 0, 100)) != 0 || !File.Exists(jpg))
					throw new InvalidOperationException($"Screenshot JPEG encoding failed: {QtHostRuntime.LastErrorText}");
				return File.ReadAllBytes(jpg);
			}
			finally
			{
				TryDelete(png);
				TryDelete(jpg);
			}
		}
	}
}

/// <summary>MIME types by file extension for the files the pickers and Share hand out.</summary>
internal static class MimeTypes
{
	private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
	{
		[".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png", [".gif"] = "image/gif",
		[".webp"] = "image/webp", [".bmp"] = "image/bmp", [".heic"] = "image/heic", [".svg"] = "image/svg+xml",
		[".tif"] = "image/tiff", [".tiff"] = "image/tiff",
		[".mp4"] = "video/mp4", [".m4v"] = "video/mp4", [".mov"] = "video/quicktime", [".webm"] = "video/webm",
		[".mkv"] = "video/x-matroska", [".avi"] = "video/x-msvideo", [".3gp"] = "video/3gpp",
		[".mp3"] = "audio/mpeg", [".m4a"] = "audio/mp4", [".aac"] = "audio/aac", [".ogg"] = "audio/ogg",
		[".oga"] = "audio/ogg", [".opus"] = "audio/opus", [".wav"] = "audio/wav", [".flac"] = "audio/flac",
		[".pdf"] = "application/pdf", [".txt"] = "text/plain", [".csv"] = "text/csv", [".html"] = "text/html",
		[".htm"] = "text/html", [".xml"] = "application/xml", [".json"] = "application/json",
		[".zip"] = "application/zip", [".vcf"] = "text/vcard", [".ics"] = "text/calendar",
		[".odt"] = "application/vnd.oasis.opendocument.text",
		[".ods"] = "application/vnd.oasis.opendocument.spreadsheet",
		[".odp"] = "application/vnd.oasis.opendocument.presentation",
		[".doc"] = "application/msword",
		[".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
		[".xls"] = "application/vnd.ms-excel",
		[".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
		[".ppt"] = "application/vnd.ms-powerpoint",
		[".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
	};

	public static string For(string path) =>
		ByExtension.TryGetValue(Path.GetExtension(path), out var type) ? type : "application/octet-stream";
}
