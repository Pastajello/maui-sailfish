using System.Globalization;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// Maps MAUI Image/ImageButton to the Image.qml adapter: QtQuick decodes images itself, so the managed
/// side only resolves the source to a URL Qt can load. Font glyphs render once to cached PNGs; stream
/// sources are copied to a cache file asynchronously and stay pending until then.
/// </summary>
internal static class QtHostImages
{
	/// <summary>Adapter URI (qml/adapters.json).</summary>
	public const string AdapterUri = Handlers.SailfishKeys.Adapter.Image;

	/// <summary>Sailfish theme and installed app icon directories, probed for icon-like names.</summary>
	private static readonly string[] ThemeIconDirs =
	{
		"/usr/share/themes/sailfish/icons/z1.0",
		"/usr/share/icons/hicolor/86x86/apps",
	};

	/// <summary>The image snapshot, or null when the source cannot be resolved. Takes IImage because
	/// ImageButton does not derive from Image.</summary>
	public static Dictionary<string, object?>? Props(IImage image)
	{
		var url = Resolve(image.Source as ImageSource);
		if (url is null)
			return null;
		var visual = image as VisualElement;
		var background = visual is null ? null : QtHostPaint.Background(visual);
		// Built only when tracing: this runs for every image snapshot.
		if (QtHostDiag.TraceEnabled)
		{
			var style = visual?.Style;
			QtHostDiag.Trace(QtHostDiagChannel.QmlProperty,
				$"image props: aspect={image.Aspect} background={background} " +
				$"style={(style is null ? "null" : string.Join("|", style.Setters.Select(s => $"{s.Property.PropertyName}={s.Value}")))} " +
				$"source={url}");
		}
		var props = new Dictionary<string, object?>
		{
			["mauiSource"] = url,
			// Map by member, not ordinal: the adapter's codes are fixed but MAUI 11 reordered the Aspect enum.
			["mauiAspect"] = image.Aspect switch
			{
				Aspect.Fill => 0,
				Aspect.AspectFit => 1,
				Aspect.AspectFill => 2,
				_ => 3,   // Center
			},
			["mauiBackground"] = background ?? Colors.Transparent,
			// ImageButton shares the adapter; this arms its MouseArea so taps reach SendClicked.
			["mauiTappable"] = image is IImageButton ? 1 : 0,
			// GIF sources play in an AnimatedImage.
			["mauiPlaying"] = image.IsAnimationPlaying,
		};
		// Decode at the arranged size (Qt units): a 700 px thumbnail in a 300 px tile decodes ~5x fewer pixels and
		// fits the small Qt 5.6 pixmap cache. The adapter freezes it once the load starts.
		if (visual is { Width: > 0, Height: > 0 })
		{
			props["mauiDecodeW"] = (int)Math.Ceiling(QtHostUnits.ToQtUnits(visual.Width));
			props["mauiDecodeH"] = (int)Math.Ceiling(QtHostUnits.ToQtUnits(visual.Height));
		}
		// Unsized (not arranged yet) and Center images: decode no larger than the screen's long side.
		props["mauiDecodeCap"] = DecodeCap;
		if (image is ImageButton button)
		{
			// ImageButton frame: stroke and corner radius; Padding insets the bitmap.
			var density = SailfishDisplay.Density;
			props["mauiCornerRadius"] = Math.Max(0, button.CornerRadius) * density;
			props["mauiStrokeColor"] = button.BorderColor ?? Colors.Transparent;
			props["mauiStrokeWidth"] = Math.Max(0, button.BorderWidth) * density;
			props["mauiPadL"] = button.Padding.Left * density;
			props["mauiPadT"] = button.Padding.Top * density;
			props["mauiPadR"] = button.Padding.Right * density;
			props["mauiPadB"] = button.Padding.Bottom * density;
		}
		return props;
	}

	/// <summary>The decode cap of unsized/Center images: the screen's long side in device pixels (0 before it is known).</summary>
	internal static int DecodeCap => Math.Max(SailfishDisplay.PixelWidth, SailfishDisplay.PixelHeight);

	private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (int Width, int Height)?> FileSizes = new();

	/// <summary>A local image's pixel size (a button's icon, which Silica's Icon paints at its native pixels; font glyphs
	/// render to PNG too), or null when the source is not a local image file.</summary>
	public static (int Width, int Height)? PixelSize(ImageSource? source)
	{
		if (Resolve(source) is not { } url || !url.StartsWith("file://", StringComparison.Ordinal))
			return null;
		return FilePixelSize(new Uri(url).LocalPath);
	}

	private static (int Width, int Height)? FilePixelSize(string path) =>
		FileSizes.GetOrAdd(path, static p =>
		{
			try
			{
				using var fs = File.OpenRead(p);
				return ImageHeader.PixelSize(fs);
			}
			catch (IOException)
			{
				return null;
			}
			catch (UnauthorizedAccessException)
			{
				return null;
			}
		});

	/// <summary>
	/// The size (dp) an Image with this source takes when nothing sizes it, as Android's ImageView sizes to its drawable:
	/// a resizetizer image at its BaseSize (here one 4× raster), an image MAUI copied unresized (a bitmap without
	/// BaseSize, a GIF) at one dp per pixel (Android's <c>drawable/</c>, mdpi), and any other bitmap (a file, a stream,
	/// a downloaded or library image, a font glyph) at its pixels ÷ density. Null while unknown: no resolvable source, a
	/// stream still being read, a remote image not loaded yet (the adapter reports it, <see cref="ReportNaturalSize"/>).
	/// </summary>
	public static Size? IntrinsicSize(ImageSource? source)
	{
		if (Resolve(source) is not { } url)
			return null;
		var density = SailfishDisplay.Density > 0 ? SailfishDisplay.Density : 1;
		if (!url.StartsWith("file://", StringComparison.Ordinal))
			return RemoteSizes.TryGetValue(url, out var remote) ? new Size(remote.Width / density, remote.Height / density) : null;
		var path = new Uri(url).LocalPath;
		if (FilePixelSize(path) is not { } px)
			return null;
		var scale = PackagedImageScale(path) ?? density;
		return new Size(px.Width / scale, px.Height / scale);
	}

	/// <summary>Natural pixel sizes of remote images, as the adapter reported them once loaded.</summary>
	private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (int Width, int Height)> RemoteSizes = new(StringComparer.Ordinal);

	/// <summary>
	/// The view's own size changed (its remote image loaded, its stream was read): re-measured the way Controls does when
	/// a property changes the measure, so MeasureInvalidated reaches a list row's watcher too. IView.InvalidateMeasure
	/// only asks the handler for a page pass, and a list row is measured by its list, never by the page.
	/// </summary>
	internal static void InvalidateIntrinsicSize(IView view)
	{
		if (view is VisualElement element)
#pragma warning disable CS0618 // the public entry to InvalidateMeasureInternal; InvalidateMeasure() raises no MeasureInvalidated
			element.InvalidateMeasureNonVirtual(Microsoft.Maui.Controls.Internals.InvalidationTrigger.MeasureChanged);
#pragma warning restore CS0618
		else
			view.InvalidateMeasure();
	}

	/// <summary>Records a loaded remote image's pixel size; true when it was not known yet (its views re-measure).</summary>
	internal static bool ReportNaturalSize(string url, int width, int height)
	{
		if (width <= 0 || height <= 0 || url.StartsWith("file://", StringComparison.Ordinal))
			return false;
		var known = RemoteSizes.TryGetValue(url, out var old);
		RemoteSizes[url] = (width, height);
		return !known || old != (width, height);
	}

	/// <summary>Manifest of the images the resizetizer rasterized (one name per line, no extension), written by
	/// <c>_SailfishProcessMauiImages</c>; the other files in images/ were copied as they are.</summary>
	internal const string ResizedManifest = "maui-resized.txt";

	private static HashSet<string>? _resized;

	/// <summary>4 for a resizetizer raster in the app's images/, 1 for an image copied there unresized; null elsewhere.</summary>
	private static double? PackagedImageScale(string path)
	{
		var imagesDir = Path.Combine(AppContext.BaseDirectory, "images");
		if (!string.Equals(Path.GetDirectoryName(path), imagesDir.TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal))
			return null;
		var resized = _resized ??= ReadResizedManifest(Path.Combine(imagesDir, ResizedManifest));
		return resized.Contains(Path.GetFileNameWithoutExtension(path)) ? 4 : 1;
	}

	private static HashSet<string> ReadResizedManifest(string path)
	{
		var names = new HashSet<string>(StringComparer.Ordinal);
		try
		{
			if (File.Exists(path))
				foreach (var line in File.ReadAllLines(path))
					if (line.Trim() is { Length: > 0 } name)
						names.Add(name);
		}
		catch (IOException)
		{
		}
		return names;
	}

	/// <summary>ImageSource → a URL Qt can load; null when unsupported, missing or still pending. A source whose
	/// type has an <see cref="ISailfishImageSourceService"/> in the app's <see cref="IImageSourceServiceProvider"/> goes
	/// through it; the stock sources resolve here otherwise.</summary>
	public static string? Resolve(ImageSource? source) =>
		source is not null && ServiceFor(source.GetType()) is { } service ? ServiceUrl(service, source) : ResolveStock(source);

	private static string? ResolveStock(ImageSource? source) => source switch
	{
		FileImageSource { File.Length: > 0 } file => FileUrl(file.File),
		UriImageSource { Uri: { IsFile: true } local } => FileUrl(local.LocalPath),
		UriImageSource { Uri: { } uri } remote when uri.Scheme is "http" or "https" => WithCachePolicy(remote, uri),
		FontImageSource glyph => GlyphUrl(glyph),
		StreamImageSource stream => StreamUrl(stream),
		{ } other => QtHostImageSources.Resolve(other),
		_ => null,
	};

	/// <summary>
	/// UriImageSource.CachingEnabled/CacheValidity, as Android (Glide) and iOS honour them: the shim's HTTP cache reads
	/// the policy from a URL fragment (never sent to the server). "maui-cache=0" loads from the network and stores
	/// nothing; "maui-cache=N" serves a copy younger than N seconds and refreshes an older one.
	/// </summary>
	private static string WithCachePolicy(UriImageSource source, Uri uri)
	{
		if (uri.Fragment.Length > 0)
			return uri.ToString();   // the app's own fragment: default policy
		var seconds = source.CachingEnabled ? Math.Max(1, (long)source.CacheValidity.TotalSeconds) : 0;
		return uri.ToString() + "#maui-cache=" + seconds.ToString(CultureInfo.InvariantCulture);
	}

	/// <summary>A stream source still being read, or a service still answering; the element should host nothing yet
	/// rather than the placeholder.</summary>
	public static bool IsPending(ImageSource? source) =>
		source is not null && Streams.TryGetValue(source, out var entry) && entry.Path is null && !entry.Failed;

	// ImageSource type → its Sailfish service (null: none, use the stock handling). The provider and the registrations
	// are fixed for the app's lifetime, so the answer is cached per type.
	private static readonly Dictionary<Type, ISailfishImageSourceService?> Services = new();
	private static IServiceProvider? _servicesFrom;

	/// <summary>Tests: forget the cached service lookups.</summary>
	internal static void ResetServicesForTests()
	{
		lock (Services)
		{
			Services.Clear();
			_servicesFrom = null;
		}
	}

	private static ISailfishImageSourceService? ServiceFor(Type type)
	{
		var services = IPlatformApplication.Current?.Services;
		if (services is null)
			return null;
		lock (Services)
		{
			if (!ReferenceEquals(services, _servicesFrom))
			{
				Services.Clear();
				_servicesFrom = services;
			}
			if (Services.TryGetValue(type, out var known))
				return known;
			ISailfishImageSourceService? found = null;
			try
			{
				found = (services.GetService(typeof(IImageSourceServiceProvider)) as IImageSourceServiceProvider)
					?.GetImageSourceService(type) as ISailfishImageSourceService;
			}
			catch (InvalidOperationException)
			{
				// No service registered for the type: the stock handling or a resolver takes it.
			}
			return Services[type] = found;
		}
	}

	/// <summary>A source a service answers: asked once per source object, asynchronously, through the same pending
	/// machinery as a stream (the element hosts it once the URL is there).</summary>
	private static string? ServiceUrl(ISailfishImageSourceService service, ImageSource source)
	{
		if (Streams.TryGetValue(source, out var entry))
			return entry.Path;
		entry = new StreamEntry();
		Streams.Add(source, entry);
		_ = AskService(service, source, entry);
		return null;
	}

	private static async Task AskService(ISailfishImageSourceService service, ImageSource source, StreamEntry entry)
	{
		try
		{
			// Resumes on the Qt loop, as a stream read does: the hosts are created there.
			var result = await service.GetUrlAsync(source, entry.Cancel.Token).ConfigureAwait(true);
			if (entry.Cancel.IsCancellationRequested)
			{
				result?.Dispose();
				return;
			}
			if (result is null || string.IsNullOrEmpty(result.Value))
			{
				Fail(entry);
				return;
			}
			entry.Result = result;
			Ready(entry, result.Value);
		}
		catch (OperationCanceledException) when (entry.Cancel.IsCancellationRequested)
		{
		}
		catch (Exception ex)
		{
			Fail(entry);
			QtHostDiag.Warn(QtHostDiagChannel.QmlProperty, $"image source service {service.GetType().Name} failed for {source.GetType().Name}: {ex.Message}");
		}
	}

	/// <summary>The entry has its URL: a layout pass, and the elements waiting for it gain their image host.</summary>
	private static void Ready(StreamEntry entry, string url)
	{
		entry.Path = url;
		SailfishRenderSession.OfApp?.Renderer?.InvalidateLayout();
		Settle(entry);
	}

	/// <summary>The entry has no image (a null stream, a service answering nothing, an error): the elements waiting
	/// for it stop waiting (Image.IsLoading goes false).</summary>
	private static void Fail(StreamEntry entry)
	{
		entry.Failed = true;
		Settle(entry);
	}

	private static void Settle(StreamEntry entry)
	{
		Dictionary<object, Action>? ready;
		lock (entry)
		{
			ready = entry.Ready;
			entry.Ready = null;
		}
		if (ready is not null)
			foreach (var action in ready.Values)
				action();
	}

	/// <summary>An element moved off <paramref name="source"/> (MAUI cancels the old source's load on a source change):
	/// a stream still being read, or a service still answering, is cancelled and forgotten, so a later use starts
	/// again. A finished entry stays (other elements may show it).</summary>
	internal static void CancelPending(ImageSource? source)
	{
		if (source is null || !Streams.TryGetValue(source, out var entry) || entry.Path is not null || entry.Failed)
			return;
		Streams.Remove(source);
		entry.Cancel.Cancel();
		Cancelled++;
	}

	/// <summary>Pending loads cancelled by a source change (diagnostics).</summary>
	internal static long Cancelled;

	// Sailjail ORG/APP cache when sandboxed; the process name is the booster's under silica-qt5.
	private static string CacheDir(string kind) => SailfishAppPaths.Cache(kind);

	/// <summary>FontImageSource → a cached PNG of the glyph; colour defaults to white for the dark Silica theme.</summary>
	private static string? GlyphUrl(FontImageSource glyph)
	{
		if (string.IsNullOrEmpty(glyph.Glyph) || !QtHostRuntime.IsRunning || !QtHostRuntime.IsQtThread)
			return null;
		var family = QtHostFonts.Resolve(glyph.FontFamily);
		var px = (glyph.Size > 0 ? glyph.Size : 30) * SailfishDisplay.Density;
		var color = glyph.Color is { } c ? BridgeValue.ColorString(c) : "#ffffffff";
		var key = $"{family}|{glyph.Glyph}|{px:F1}|{color}";
		if (GlyphUrls.TryGetValue(key, out var known))
			return known;
		var name = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(key))) + ".png";
		var path = Path.Combine(CacheDir("glyphs"), name);
		if (!File.Exists(path) && !QtHostRuntime.RenderGlyph(family, glyph.Glyph, px, color, path))
		{
			QtHostDiag.Warn(QtHostDiagChannel.QmlProperty, $"glyph '{glyph.Glyph}' ({family}) could not be rendered: {QtHostRuntime.LastErrorText}");
			return null;
		}
		return GlyphUrls[key] = new Uri(path).AbsoluteUri;
	}

	/// <summary>Rendered glyph URLs, so each reconcile skips the hash and disk probe.</summary>
	private static readonly Dictionary<string, string> GlyphUrls = new(StringComparer.Ordinal);

	private sealed class StreamEntry
	{
		public string? Path;                       // the URL: a stream's cache file, or a service's answer
		public string? File;                       // a stream's cache file on disk, deleted with the entry
		public IImageSourceServiceResult<string>? Result;   // a service's result, disposed with the entry
		public readonly CancellationTokenSource Cancel = new();

		// The entry lives as long as its source object (ConditionalWeakTable): when the app drops the source, its
		// cache file goes and the service's result is disposed (the "dispose" of MAUI's image source results).
		~StreamEntry()
		{
			try
			{
				if (File is { } file)
					System.IO.File.Delete(file);
				Result?.Dispose();
			}
			catch (Exception)
			{
				// best effort: the startup sweep removes what is left
			}
		}
		public bool Failed;
		public Dictionary<object, Action>? Ready;   // run once the stream is on disk, one per key
	}

	/// <summary>Runs <paramref name="action"/> once a pending stream source is readable (any thread), once per
	/// <paramref name="key"/> however often it is asked; nothing when the source is not pending.</summary>
	internal static void WhenReady(ImageSource? source, object key, Action action) => WhenSettled(source, key, action);

	/// <summary>As <see cref="WhenReady"/>, also when the load fails.</summary>
	internal static void WhenSettled(ImageSource? source, object key, Action action)
	{
		if (source is null || !Streams.TryGetValue(source, out var entry))
			return;
		lock (entry)
		{
			if (entry.Path is not null || entry.Failed)
				return;
			(entry.Ready ??= new Dictionary<object, Action>(ReferenceEqualityComparer.Instance))[key] = action;
		}
	}

	private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ImageSource, StreamEntry> Streams = new();

	/// <summary>StreamImageSource → a cache file read asynchronously on first use (resumes on the Qt thread);
	/// the next reconcile picks it up.</summary>
	private static string? StreamUrl(StreamImageSource source)
	{
		if (Streams.TryGetValue(source, out var entry))
			return entry.Path;
		entry = new StreamEntry();
		Streams.Add(source, entry);
		_ = ReadStream(source, entry);
		return null;
	}

	private static async Task ReadStream(StreamImageSource source, StreamEntry entry)
	{
		string? path = null;
		try
		{
			var token = entry.Cancel.Token;
			using var stream = await source.Stream(token);
			if (stream is null)
			{
				Fail(entry);
				return;
			}
			path = Path.Combine(StreamsDir(), Guid.NewGuid().ToString("N") + ".img");
			using (var file = File.Create(path))
				await stream.CopyToAsync(file, token);
			// A GIF plays only from a .gif URL (Image.qml picks AnimatedImage by the extension).
			if (IsGif(path))
			{
				var gif = Path.ChangeExtension(path, ".gif");
				File.Move(path, gif);
				path = gif;
			}
			entry.File = path;
			if (token.IsCancellationRequested)
				return;
			// The element hosted nothing while the stream was read: its container's subtree gains the image host.
			Ready(entry, new Uri(path).AbsoluteUri);
		}
		catch (OperationCanceledException) when (entry.Cancel.IsCancellationRequested)
		{
			TryDelete(path);
		}
		catch (Exception ex)
		{
			TryDelete(path);
			Fail(entry);
			QtHostDiag.Warn(QtHostDiagChannel.QmlProperty, $"stream image source could not be read: {ex.Message}");
		}
	}

	internal static bool IsGif(string path)
	{
		try
		{
			using var fs = File.OpenRead(path);
			Span<byte> head = stackalloc byte[4];
			return fs.Read(head) == 4 && head[0] == (byte)'G' && head[1] == (byte)'I' && head[2] == (byte)'F' && head[3] == (byte)'8';
		}
		catch (IOException)
		{
			return false;
		}
	}

	private static void TryDelete(string? path)
	{
		if (path is null)
			return;
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

	private static int _streamsSwept;

	/// <summary>The stream cache, emptied once per process: files of an earlier run belong to sources that are gone
	/// (a crash or a kill skips the per-entry cleanup).</summary>
	private static string StreamsDir()
	{
		var dir = CacheDir("streams");
		if (Interlocked.Exchange(ref _streamsSwept, 1) == 0)
		{
			try
			{
				foreach (var file in Directory.EnumerateFiles(dir))
					TryDelete(file);
			}
			catch (IOException)
			{
			}
		}
		return dir;
	}

	private static string? FileUrl(string file)
	{
		var path = ResolveFilePath(file);
		return path is null ? null : new Uri(path).AbsoluteUri;
	}

	/// <summary>Probes the given path, the output directory, images/ (where the RPM ships Resources/Images),
	/// Resources/Images/, and the theme directories for icon-like names.</summary>
	private static string? ResolveFilePath(string file)
	{
		if (File.Exists(file))
			return Path.GetFullPath(file);

		var baseDir = AppContext.BaseDirectory;
		var name = Path.GetFileName(file);
		var candidates = new List<string>
		{
			Path.Combine(baseDir, file),
			Path.Combine(baseDir, "images", name),
			Path.Combine(baseDir, "Resources", "Images", name),
		};
		if (name.StartsWith("icon-", StringComparison.OrdinalIgnoreCase) ||
		    name.StartsWith("image-", StringComparison.OrdinalIgnoreCase))
		{
			foreach (var dir in ThemeIconDirs)
			{
				candidates.Add(Path.Combine(dir, name));
				if (!name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
					candidates.Add(Path.Combine(dir, name + ".png"));
			}
		}

		foreach (var candidate in candidates)
			if (File.Exists(candidate))
				return candidate;
		return null;
	}
}
