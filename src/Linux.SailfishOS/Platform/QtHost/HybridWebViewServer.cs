using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// The origin a <c>HybridWebView</c> page loads from (tracker S48): a loopback HTTP server per view that serves the app's
/// <c>HybridRoot</c> files (MauiAssets under the app root, Resources/Raw/wwwroot by default) and MAUI's own
/// <c>_framework/hybridwebview.js</c>, and answers the two endpoints that script posts to on Android, where the bridge is
/// HTTP as well: <c>__hwvSendMessage</c> (raw messages and InvokeJavaScript results) and <c>__hwvInvokeDotNet</c>. Both must
/// carry MAUI's token header and come from this origin, as the Android handler checks.
/// </summary>
internal sealed class HybridWebViewServer : IDisposable
{
	internal const string InvokeDotNetPath = "__hwvInvokeDotNet";
	internal const string SendMessagePath = "__hwvSendMessage";
	internal const string ScriptPath = "_framework/hybridwebview.js";

	private readonly HttpListener _listener = new();
	private readonly string _root;
	private readonly Func<string, Task> _onMessage;
	private readonly Func<string, Task<byte[]>> _onInvokeDotNet;

	/// <summary>"http://127.0.0.1:port" (no trailing slash), what <c>window.location.origin</c> is in the page.</summary>
	public string Origin { get; }

	/// <param name="hybridRoot">The folder under the app root the page's files come from.</param>
	/// <param name="onMessage">A "type|content" message from the page (main thread is the callee's concern).</param>
	/// <param name="onInvokeDotNet">The InvokeDotNet request body → the JSON result MAUI's script expects.</param>
	public HybridWebViewServer(string? hybridRoot, Func<string, Task> onMessage, Func<string, Task<byte[]>> onInvokeDotNet)
	{
		_root = string.IsNullOrWhiteSpace(hybridRoot) ? "wwwroot" : hybridRoot.Trim('/', '\\');
		_onMessage = onMessage;
		_onInvokeDotNet = onInvokeDotNet;
		var port = FreePort();
		Origin = "http://127.0.0.1:" + port.ToString(System.Globalization.CultureInfo.InvariantCulture);
		_listener.Prefixes.Add(Origin + "/");
		_listener.Start();
		_ = AcceptLoop();
	}

	/// <summary>Requests answered (diagnostics).</summary>
	public int Requests { get; private set; }

	private static int FreePort()
	{
		var probe = new TcpListener(IPAddress.Loopback, 0);
		probe.Start();
		var port = ((IPEndPoint)probe.LocalEndpoint).Port;
		probe.Stop();
		return port;
	}

	private async Task AcceptLoop()
	{
		while (_listener.IsListening)
		{
			HttpListenerContext context;
			try
			{
				context = await _listener.GetContextAsync().ConfigureAwait(false);
			}
			catch (Exception) when (!_listener.IsListening)
			{
				return;   // disposed
			}
			catch (Exception ex)
			{
				QtHostDiag.Warn(QtHostDiagChannel.QmlLoad, $"HybridWebView server: {ex.Message}");
				continue;
			}
			_ = Task.Run(() => Serve(context));
		}
	}

	private async Task Serve(HttpListenerContext context)
	{
		var response = context.Response;
		try
		{
			Requests++;
			var path = Uri.UnescapeDataString(context.Request.Url?.AbsolutePath ?? "/").TrimStart('/');
			if (context.Request.HttpMethod == "POST" && (path == SendMessagePath || path == InvokeDotNetPath))
			{
				if (!FromThisPage(context.Request))
				{
					response.StatusCode = 403;
					return;
				}
				var body = await ReadBody(context.Request).ConfigureAwait(false);
				if (path == SendMessagePath)
				{
					await _onMessage(body).ConfigureAwait(false);
					response.StatusCode = 200;
					return;
				}
				var result = await _onInvokeDotNet(body).ConfigureAwait(false);
				response.ContentType = "application/json";
				await response.OutputStream.WriteAsync(result).ConfigureAwait(false);
				return;
			}
			await using var file = await OpenAsync(path.Length == 0 ? "index.html" : path).ConfigureAwait(false);
			if (file is null)
			{
				response.StatusCode = 404;
				return;
			}
			response.ContentType = ContentType(path);
			// The page's own files never go stale while the app runs; no cache keeps a rebuilt RPM's files visible.
			response.Headers["Cache-Control"] = "no-cache";
			await file.CopyToAsync(response.OutputStream).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			QtHostDiag.Warn(QtHostDiagChannel.QmlLoad, $"HybridWebView server: {context.Request.Url?.AbsolutePath}: {ex.Message}");
			try { response.StatusCode = 500; } catch (InvalidOperationException) { }
		}
		finally
		{
			try { response.Close(); } catch (Exception) { }
		}
	}

	/// <summary>MAUI's own check (HybridWebViewHandler.HasExpectedHeaders): the token header plus an Origin or Referer of
	/// this page, so another page or app on the loopback cannot call into .NET.</summary>
	private bool FromThisPage(HttpListenerRequest request)
	{
		if (!string.Equals(request.Headers["X-Maui-Invoke-Token"], "HybridWebView", StringComparison.OrdinalIgnoreCase))
			return false;
		static string Trim(string? value) => (value ?? string.Empty).TrimEnd('/');
		return string.Equals(Trim(request.Headers["Origin"]), Origin, StringComparison.OrdinalIgnoreCase) ||
		       Trim(request.Headers["Referer"]).StartsWith(Origin, StringComparison.OrdinalIgnoreCase);
	}

	private static async Task<string> ReadBody(HttpListenerRequest request)
	{
		using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8);
		var body = await reader.ReadToEndAsync().ConfigureAwait(false);
		// The script sends the body twice (Android cannot read POST bodies); the body wins when there is one.
		return body.Length > 0 ? body : request.Headers["X-Maui-Request-Body"] ?? string.Empty;
	}

	private async Task<Stream?> OpenAsync(string path)
	{
		if (path.Contains("..", StringComparison.Ordinal))
			return null;
		if (string.Equals(path, ScriptPath, StringComparison.OrdinalIgnoreCase))
			return typeof(Microsoft.Maui.Handlers.HybridWebViewHandler).Assembly.GetManifestResourceStream(ScriptPath);
		// MauiAssets land at the app root under their LogicalName (SailfishFileSystem.OpenAppPackageFileAsync reads there).
		var file = Path.Combine(AppContext.BaseDirectory, _root, path);
		return File.Exists(file) ? await Task.FromResult<Stream>(File.OpenRead(file)).ConfigureAwait(false) : null;
	}

	internal static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
	{
		".html" or ".htm" => "text/html; charset=utf-8",
		".js" or ".mjs" => "text/javascript; charset=utf-8",
		".css" => "text/css; charset=utf-8",
		".json" or ".map" => "application/json",
		".svg" => "image/svg+xml",
		".png" => "image/png",
		".jpg" or ".jpeg" => "image/jpeg",
		".gif" => "image/gif",
		".webp" => "image/webp",
		".ico" => "image/x-icon",
		".wasm" => "application/wasm",
		".woff" => "font/woff",
		".woff2" => "font/woff2",
		".ttf" => "font/ttf",
		".txt" => "text/plain; charset=utf-8",
		_ => "application/octet-stream",
	};

	public void Dispose()
	{
		try
		{
			_listener.Stop();
			_listener.Close();
		}
		catch (Exception)
		{
		}
	}
}
