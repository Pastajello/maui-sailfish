using Microsoft.Maui;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// The Sailfish form of MAUI's <see cref="IImageSourceService"/>: turns an image source into a URL Qt can load
/// (<c>file://</c> or <c>http(s)://</c>). Implement <see cref="ISailfishImageSourceService{T}"/> and register it for
/// your own image source type as on the other platforms,
/// <c>builder.ConfigureImageSources(s =&gt; s.AddService&lt;MyImageSource, MyImageSourceService&gt;())</c>; the backend
/// asks <see cref="IImageSourceServiceProvider"/> for every source it shows and uses a service that implements this
/// interface before its own handling of the stock sources, so a service registered for <c>IUriImageSource</c> (or
/// any stock interface) replaces the built-in loading too. The URL is asked for once per source object, off the Qt
/// thread; the element shows nothing until it arrives.
/// </summary>
public interface ISailfishImageSourceService : IImageSourceService
{
	/// <summary>A URL for <paramref name="source"/>, or null when it has no image. Disposing the result may delete a
	/// file it wrote.</summary>
	Task<IImageSourceServiceResult<string>?> GetUrlAsync(IImageSource source, CancellationToken cancellationToken = default);
}

/// <summary><see cref="ISailfishImageSourceService"/> for the source type <typeparamref name="T"/>, the form MAUI's
/// <c>AddService&lt;TImageSource, TImageSourceService&gt;()</c> takes.</summary>
public interface ISailfishImageSourceService<in T> : ISailfishImageSourceService, IImageSourceService<T>
	where T : IImageSource
{
}

/// <summary>A URL result for <see cref="ISailfishImageSourceService"/>; <c>dispose</c> runs once (e.g. deletes a
/// temporary file).</summary>
public sealed class SailfishImageSourceServiceResult : IImageSourceServiceResult<string>
{
	private Action? _dispose;

	public SailfishImageSourceServiceResult(string url, Action? dispose = null)
	{
		Value = url;
		_dispose = dispose;
	}

	public string Value { get; }

	public bool IsResolutionDependent => false;

	public bool IsDisposed { get; private set; }

	public void Dispose()
	{
		if (IsDisposed)
			return;
		IsDisposed = true;
		_dispose?.Invoke();
		_dispose = null;
	}
}
