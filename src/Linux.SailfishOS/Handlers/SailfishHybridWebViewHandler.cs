using System.Text.Json;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Handlers;

/// <summary>
/// HybridWebView on the Gecko web view (tracker S48, decision D5 a). The page loads from a loopback server per view
/// (<see cref="HybridWebViewServer"/>): the app's <c>HybridRoot</c> files and MAUI's <c>hybridwebview.js</c>, whose
/// Android path talks HTTP to its own origin, so the script works unchanged. JS → .NET arrives as posts to that
/// origin (raw messages, InvokeJavaScript results, InvokeDotNet); .NET → JS is a <c>message</c> event dispatched into the
/// page (the script accepts events without a source window, as Android's postWebMessage delivers them).
/// </summary>
public class SailfishHybridWebViewHandler : SailfishSnapshotHandler<IHybridWebView>
{
	private static readonly string[] Keys = { nameof(IHybridWebView.DefaultFile), nameof(IHybridWebView.HybridRoot) };

	public static readonly PropertyMapper<IHybridWebView, SailfishHybridWebViewHandler> Mapper = SnapshotMapper<SailfishHybridWebViewHandler>(Keys);

	public static readonly CommandMapper<IHybridWebView, SailfishHybridWebViewHandler> CommandMapper = new(SailfishViewMapper.CommandMapper)
	{
		[nameof(IHybridWebView.EvaluateJavaScriptAsync)] = (h, _, args) =>
		{
			if (args is EvaluateJavaScriptAsyncRequest request)
				h.RunJs(request.Script, result => request.TrySetResult(result!));
		},
		[nameof(IHybridWebView.InvokeJavaScriptAsync)] = (h, _, args) =>
		{
			if (args is HybridWebViewInvokeJavaScriptRequest request)
				h.InvokeJavaScript(request);
		},
		[nameof(IHybridWebView.SendRawMessage)] = (h, _, args) =>
		{
			if (args is HybridWebViewRawMessage message)
				h.SendRawMessage(message.Message);
		},
	};

	private HybridWebViewServer? _server;
	private IDispatcher? _dispatcher;
	private int _jsSeq;
	private int _taskSeq;
	private readonly Dictionary<string, Action<string?>> _pendingJs = new(StringComparer.Ordinal);
	private readonly Dictionary<string, TaskCompletionSource<string?>> _pendingInvokes = new(StringComparer.Ordinal);

	public SailfishHybridWebViewHandler() : this(null)
	{
	}

	public SailfishHybridWebViewHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
		: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
	{
	}

	protected override string? AdapterUri => SailfishKeys.Adapter.WebView;

	protected override bool WalksChildren => false;

	/// <summary>The page's origin ("http://127.0.0.1:port"), once connected.</summary>
	internal string? Origin => _server?.Origin;

	private static int _sandboxChecked;

	protected override void ConnectHandler(NativeElementHost platformView)
	{
		// Gecko cannot start in Sailjail without the WebView permission (as for WebView).
		if (Interlocked.Exchange(ref _sandboxChecked, 1) == 0 && !Platform.SailfishPermissions.Declares("WebView"))
			QtHostDiag.Warn(QtHostDiagChannel.QmlLoad,
				"HybridWebView: " + Platform.SailfishPermissions.MissingMessage("WebView", new[] { "WebView" }));
		_dispatcher = Dispatcher.GetForCurrentThread();
		_server = new HybridWebViewServer(VirtualView?.HybridRoot, OnMessage, OnInvokeDotNet);
		base.ConnectHandler(platformView);
	}

	protected override Dictionary<string, object?>? Snapshot(IHybridWebView view) => new()
	{
		["mauiUrl"] = _server is null ? string.Empty : _server.Origin + "/" + (string.IsNullOrWhiteSpace(view.DefaultFile) ? "index.html" : view.DefaultFile.TrimStart('/')),
		["mauiHtml"] = string.Empty,
		["mauiBaseUrl"] = string.Empty,
		// A new DefaultFile or HybridRoot loads the page again.
		["mauiSourceId"] = HashCode.Combine(view.DefaultFile, view.HybridRoot, _server?.Origin),
		["mauiUserAgent"] = string.Empty,
	};

	/* --- .NET → JS --- */

	private void RunJs(string? script, Action<string?> done)
	{
		if (string.IsNullOrEmpty(script))
		{
			done(null);
			return;
		}
		var id = "hjs" + (++_jsSeq).ToString(System.Globalization.CultureInfo.InvariantCulture);
		lock (_pendingJs)
			_pendingJs[id] = done;
		if (!SendCommand(SailfishKeys.Command.Js, new() { [SailfishKeys.Command.JsRequest] = id, [SailfishKeys.Command.JsScript] = script }))
			CompleteJs(id, ok: false, null);
	}

	/// <summary>The adapter's webview-js answer (null on a script error, as MAUI's EvaluateJavaScriptAsync).</summary>
	internal void CompleteJs(string requestId, bool ok, string? result)
	{
		Action<string?>? done;
		lock (_pendingJs)
			_pendingJs.Remove(requestId, out done);
		done?.Invoke(ok ? result : null);
	}

	/// <summary>HybridWebView.SendRawMessage: a <c>message</c> event in the page, which hybridwebview.js turns into
	/// <c>HybridWebViewMessageReceived</c>.</summary>
	internal void SendRawMessage(string? message) =>
		RunJs("window.dispatchEvent(new MessageEvent('message', { data: " + JsonSerializer.Serialize(message ?? string.Empty,
			SailfishHybridJsonContext.Default.String) + " }));", _ => { });

	/// <summary>InvokeJavaScriptAsync: MAUI's own sequence (HybridWebViewHandler.MapInvokeJavaScriptAsyncImpl, internal on
	/// this TFM): a task id, <c>HybridWebView.__InvokeJavaScript(id, method, [args])</c> in the page, and the result
	/// (or error) posted back as an __InvokeJavaScriptCompleted/Failed message.</summary>
	private void InvokeJavaScript(HybridWebViewInvokeJavaScriptRequest request)
	{
		var taskId = (++_taskSeq).ToString(System.Globalization.CultureInfo.InvariantCulture);
		var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
		lock (_pendingInvokes)
			_pendingInvokes[taskId] = completion;
		string args;
		try
		{
			args = request.ParamValues is null
				? string.Empty
				: string.Join(", ", request.ParamValues.Select((v, i) =>
					v is null || request.ParamJsonTypeInfos?[i] is not { } info ? "null" : JsonSerializer.Serialize(v, info)));
		}
		catch (Exception ex)
		{
			lock (_pendingInvokes)
				_pendingInvokes.Remove(taskId);
			request.TrySetException(ex);
			return;
		}
		RunJs($"window.HybridWebView.__InvokeJavaScript({taskId}, {request.MethodName}, [{args}])", _ => { });
		_ = Finish();

		async Task Finish()
		{
			try
			{
				var json = await completion.Task.ConfigureAwait(true);
				request.TrySetResult(json is null or "null" or "undefined" || request.ReturnTypeJsonTypeInfo is null
					? null
					: JsonSerializer.Deserialize(json, request.ReturnTypeJsonTypeInfo));
			}
			catch (Exception ex)
			{
				// MAUI throws by default (HybridWebView.InvokeJavaScriptThrowsExceptions); with the switch off it returns null.
				if (!AppContext.TryGetSwitch("HybridWebView.InvokeJavaScriptThrowsExceptions", out var throws) || throws)
					request.TrySetException(ex);
				else
					request.TrySetResult(null);
			}
		}
	}

	/* --- JS → .NET (server threads) --- */

	private Task OnMessage(string body) => OnMain(() =>
	{
		HandleMessage(body);
		return Task.FromResult(0);
	});

	/// <summary>"type|content", as MAUI's HybridWebViewHandler.MessageReceived reads it.</summary>
	internal void HandleMessage(string rawMessage)
	{
		var bar = rawMessage.IndexOf('|', StringComparison.Ordinal);
		if (bar < 0)
			return;
		var type = rawMessage[..bar];
		var content = rawMessage[(bar + 1)..];
		switch (type)
		{
			case "__RawMessage":
				VirtualView?.RawMessageReceived(Uri.UnescapeDataString(content));
				break;
			case "__InvokeJavaScriptCompleted":
			case "__InvokeJavaScriptFailed":
				var second = content.IndexOf('|', StringComparison.Ordinal);
				if (second < 0)
					return;
				var taskId = content[..second];
				var payload = content[(second + 1)..];
				TaskCompletionSource<string?>? completion;
				lock (_pendingInvokes)
					_pendingInvokes.Remove(taskId, out completion);
				if (completion is null)
					return;
				if (type == "__InvokeJavaScriptCompleted")
					completion.TrySetResult(payload);
				else
					completion.TrySetException(SailfishHybridWebViewJavaScriptException.FromJson(payload));
				break;
		}
	}

	private Task<byte[]> OnInvokeDotNet(string body) => OnMain(async () =>
	{
		try
		{
			using var doc = JsonDocument.Parse(body);
			var root = doc.RootElement;
			var method = root.TryGetProperty("MethodName", out var m) ? m.GetString() : null;
			if (method is null || VirtualView is not { } view)
				throw new InvalidOperationException("The invoke data did not provide a method name.");
			string[]? parameters = null;
			if (root.TryGetProperty("ParamValues", out var p) && p.ValueKind == JsonValueKind.Array)
				parameters = p.EnumerateArray().Select(e => e.GetString() ?? "null").ToArray();
			return InvokeResult(await view.Invoker.InvokeMethodAsync(method, parameters).ConfigureAwait(true));
		}
		catch (Exception ex)
		{
			return ErrorResult(ex);
		}
	});

	/// <summary>The InvokeDotNet answer hybridwebview.js parses (MAUI's CreateInvokeResultBytes).</summary>
	internal static byte[] InvokeResult(string? json) => Write(w =>
	{
		w.WriteString("Result", json);
		w.WriteBoolean("IsJson", json is not null);
		w.WriteBoolean("IsError", false);
		w.WriteNull("ErrorMessage");
		w.WriteNull("ErrorType");
		w.WriteNull("ErrorStackTrace");
	});

	internal static byte[] ErrorResult(Exception ex) => Write(w =>
	{
		w.WriteNull("Result");
		w.WriteBoolean("IsJson", false);
		w.WriteBoolean("IsError", true);
		w.WriteString("ErrorMessage", ex.Message);
		w.WriteString("ErrorType", ex.GetType().Name);
		w.WriteString("ErrorStackTrace", ex.StackTrace);
	});

	private static byte[] Write(Action<Utf8JsonWriter> body)
	{
		using var stream = new MemoryStream();
		using (var writer = new Utf8JsonWriter(stream))
		{
			writer.WriteStartObject();
			body(writer);
			writer.WriteEndObject();
		}
		return stream.ToArray();
	}

	private async Task<T> OnMain<T>(Func<Task<T>> work) =>
		_dispatcher is { } dispatcher ? await dispatcher.DispatchAsync(work).ConfigureAwait(false) : await work().ConfigureAwait(false);

	protected override void DisconnectHandler(NativeElementHost platformView)
	{
		_server?.Dispose();
		_server = null;
		Action<string?>[] js;
		lock (_pendingJs)
		{
			js = _pendingJs.Values.ToArray();
			_pendingJs.Clear();
		}
		foreach (var done in js)
			done(null);
		TaskCompletionSource<string?>[] invokes;
		lock (_pendingInvokes)
		{
			invokes = _pendingInvokes.Values.ToArray();
			_pendingInvokes.Clear();
		}
		foreach (var invoke in invokes)
			invoke.TrySetCanceled();
		base.DisconnectHandler(platformView);
	}
}

/// <summary>A JavaScript error InvokeJavaScriptAsync reports (MAUI's own exception type is internal on this TFM).</summary>
public sealed class SailfishHybridWebViewJavaScriptException : Exception
{
	private readonly string? _stackTrace;

	public SailfishHybridWebViewJavaScriptException(string? message, string? name = null, string? stackTrace = null)
		: base("InvokeJavaScriptAsync threw an exception: " + (message ?? "unknown JavaScript error"))
	{
		Name = name;
		_stackTrace = stackTrace;
	}

	/// <summary>The JavaScript error's name (TypeError, …).</summary>
	public string? Name { get; }

	/// <summary>The JavaScript stack.</summary>
	public override string? StackTrace => _stackTrace ?? base.StackTrace;

	internal static SailfishHybridWebViewJavaScriptException FromJson(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
			return new SailfishHybridWebViewJavaScriptException(null);
		try
		{
			using var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;
			string? Str(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
			return new SailfishHybridWebViewJavaScriptException(Str("Message"), Str("Name"), Str("StackTrace"));
		}
		catch (JsonException)
		{
			return new SailfishHybridWebViewJavaScriptException(json);
		}
	}
}

[System.Text.Json.Serialization.JsonSerializable(typeof(string))]
internal sealed partial class SailfishHybridJsonContext : System.Text.Json.Serialization.JsonSerializerContext
{
}
