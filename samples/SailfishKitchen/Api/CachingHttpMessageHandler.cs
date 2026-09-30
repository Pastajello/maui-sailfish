using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SailfishKitchen.Services;

namespace SailfishKitchen.Api;

/// <summary>
/// Cache-first handler for TheMealDB GETs. Offline mode serves the cache/seed or a parseable 503; prefer-offline
/// serves any cached body; otherwise fresh entries are served, misses forwarded and stored, and a stale entry
/// beats an error when the network fails.
/// </summary>
public sealed class CachingHttpMessageHandler : DelegatingHandler
{
	private const string JsonMediaType = "application/json";

	private readonly HttpResponseCache _cache;
	private readonly ISettingsService _settings;
	private readonly ILogger<CachingHttpMessageHandler> _logger;
	private readonly string _apiHost;

	public CachingHttpMessageHandler(
		HttpResponseCache cache,
		ISettingsService settings,
		IOptions<MealDbOptions> options,
		ILogger<CachingHttpMessageHandler> logger)
	{
		_cache = cache;
		_settings = settings;
		_logger = logger;
		_apiHost = new Uri(options.Value.BaseAddress).Host;
	}

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		if (!IsCacheable(request))
			return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

		var settings = _settings.Current;
		var key = HttpResponseCache.MakeKey(request.RequestUri!);
		var url = request.RequestUri!;

		if (settings.OfflineMode)
		{
			return _cache.TryGet(key, TimeSpan.Zero, out var offlineBody)
				? BuildResponse(HttpStatusCode.OK, offlineBody, url, fromCache: true, stale: false)
				: BuildResponse(HttpStatusCode.ServiceUnavailable, OfflineMiss(url), url, fromCache: false, stale: false);
		}

		// Any stored body is good enough regardless of age.
		if (settings.PreferOffline && _cache.TryGet(key, TimeSpan.Zero, out var preferredBody))
		{
			_logger.LogDebug("http-cache: HIT (prefer-offline) {Key}", key);
			return BuildResponse(HttpStatusCode.OK, preferredBody, url, fromCache: true, stale: false);
		}

		if (_cache.TryGet(key, settings.CacheTtl, out var freshBody))
		{
			_logger.LogDebug("http-cache: HIT {Key}", key);
			return BuildResponse(HttpStatusCode.OK, freshBody, url, fromCache: true, stale: false);
		}

		try
		{
			var live = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
			if (!live.IsSuccessStatusCode)
				return live;

			// Thumbs share the API host; decoding binary bodies as UTF-8 would corrupt them, so non-JSON passes through uncached.
			var contentType = live.Content.Headers.ContentType?.MediaType;
			if (!string.Equals(contentType, JsonMediaType, StringComparison.OrdinalIgnoreCase))
				return live;

			// Buffer once: both the caller and the cache need the body.
			var body = await live.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			if (body.StartsWith('{'))
				_cache.Put(key, url, body);

			var cached = BuildResponse(live.StatusCode, body, url, fromCache: false, stale: false);
			live.Dispose();
			return cached;
		}
		catch (HttpRequestException ex) when (!cancellationToken.IsCancellationRequested)
		{
			// Network is gone but a stale answer exists; serve it and flag it.
			if (_cache.TryGet(key, TimeSpan.Zero, out var staleBody))
			{
				_logger.LogWarning("http-cache: STALE fallback for {Key} ({Reason})", key, ex.Message);
				return BuildResponse(HttpStatusCode.OK, staleBody, url, fromCache: true, stale: true);
			}

			throw;
		}
	}

	private bool IsCacheable(HttpRequestMessage request) =>
		request.Method == HttpMethod.Get
		&& request.RequestUri is not null
		&& string.Equals(request.RequestUri.Host, _apiHost, StringComparison.OrdinalIgnoreCase);

	private static HttpResponseMessage BuildResponse(HttpStatusCode status, string body, Uri url, bool fromCache, bool stale)
	{
		var response = new HttpResponseMessage(status)
		{
			Content = new StringContent(body, Encoding.UTF8, JsonMediaType),
		};

		if (fromCache)
			response.Headers.TryAddWithoutValidation("X-Kitchen-Cache", stale ? "stale" : "hit");

		return response;
	}

	private static string OfflineMiss(Uri url) =>
		// Shaped like a real "no results" body so the parser yields an empty list.
		$"{{\"meals\":null,\"categories\":null,\"kitchenOffline\":true,\"requested\":\"{url}\"}}";
}
