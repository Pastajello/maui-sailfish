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
	public const string AdapterUri = "image";

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

	private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (int Width, int Height)?> PngSizes = new();

	/// <summary>A local PNG's pixel size (a button's icon, which Silica's Icon paints at its native pixels; font glyphs
	/// render to PNG too), or null when the source is not a local PNG.</summary>
	public static (int Width, int Height)? PixelSize(ImageSource? source)
	{
		if (Resolve(source) is not { } url || !url.StartsWith("file://", StringComparison.Ordinal))
			return null;
		return PngSizes.GetOrAdd(new Uri(url).LocalPath, static path =>
		{
			try
			{
				// PNG signature, then the IHDR chunk: width/height big-endian at bytes 16..23.
				using var fs = File.OpenRead(path);
				Span<byte> h = stackalloc byte[24];
				if (fs.Read(h) < 24 || h[0] != 0x89 || h[1] != (byte)'P' || h[12] != (byte)'I' || h[15] != (byte)'R')
					return null;
				return ((h[16] << 24) | (h[17] << 16) | (h[18] << 8) | h[19], (h[20] << 24) | (h[21] << 16) | (h[22] << 8) | h[23]);
			}
			catch (IOException)
			{
				return null;
			}
		});
	}

	/// <summary>ImageSource → a URL Qt can load; null when unsupported, missing or still pending.</summary>
	public static string? Resolve(ImageSource? source) => source switch
	{
		FileImageSource { File.Length: > 0 } file => FileUrl(file.File),
		UriImageSource { Uri: { IsFile: true } local } => FileUrl(local.LocalPath),
		UriImageSource { Uri: { } uri } remote when uri.Scheme is "http" or "https" => WithCachePolicy(remote, uri),
		FontImageSource glyph => GlyphUrl(glyph),
		StreamImageSource stream => StreamUrl(stream),
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

	/// <summary>A stream source still being read; the element should host nothing yet rather than the placeholder.</summary>
	public static bool IsPending(ImageSource? source) =>
		source is StreamImageSource stream && Streams.TryGetValue(stream, out var entry) && entry.Path is null && !entry.Failed;

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
		public string? Path;
		public bool Failed;
		public Dictionary<object, Action>? Ready;   // run once the stream is on disk, one per key
	}

	/// <summary>Runs <paramref name="action"/> once a pending stream source is readable (any thread), once per
	/// <paramref name="key"/> however often it is asked; nothing when the source is not pending.</summary>
	internal static void WhenReady(ImageSource? source, object key, Action action)
	{
		if (source is not StreamImageSource stream || !Streams.TryGetValue(stream, out var entry))
			return;
		lock (entry)
		{
			if (entry.Path is not null || entry.Failed)
				return;
			(entry.Ready ??= new Dictionary<object, Action>(ReferenceEqualityComparer.Instance))[key] = action;
		}
	}

	private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<StreamImageSource, StreamEntry> Streams = new();

	/// <summary>StreamImageSource → a cache file read asynchronously on first use (resumes on the Qt thread);
	/// the next reconcile picks it up.</summary>
	private static string? StreamUrl(StreamImageSource source)
	{
		if (Streams.TryGetValue(source, out var entry))
			return entry.Path is null ? null : new Uri(entry.Path).AbsoluteUri;
		entry = new StreamEntry();
		Streams.Add(source, entry);
		_ = ReadStream(source, entry);
		return null;
	}

	private static async Task ReadStream(StreamImageSource source, StreamEntry entry)
	{
		try
		{
			using var stream = await source.Stream(CancellationToken.None);
			if (stream is null)
			{
				entry.Failed = true;
				return;
			}
			var path = Path.Combine(CacheDir("streams"), Guid.NewGuid().ToString("N") + ".img");
			using (var file = File.Create(path))
				await stream.CopyToAsync(file);
			entry.Path = path;
			QtHostPageRenderer.Current?.InvalidateLayout();
			// The element hosted nothing while the stream was read: its container's subtree gains the image host.
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
		catch (Exception ex)
		{
			entry.Failed = true;
			QtHostDiag.Warn(QtHostDiagChannel.QmlProperty, $"stream image source could not be read: {ex.Message}");
		}
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
