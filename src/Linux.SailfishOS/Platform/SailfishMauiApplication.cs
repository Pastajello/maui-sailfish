using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Base class for Sailfish OS MAUI applications: boots MAUI, creates the window and runs the Qt/Silica host loop.
/// The app's Platforms/SailfishOS/SailfishApplication.cs derives from it, like AppDelegate on iOS or MainActivity
/// on Android, to receive the native Sailfish/Qt events; each also raises the matching <see cref="SailfishLifecycle"/> event.
/// </summary>
public abstract class SailfishMauiApplication : IPlatformApplication
{
	private const string DefaultApplicationId = "com.maui.sailfish";
	private const string MauiApplicationIdMetadataKey = "MauiApplicationId";

	private IApplication _mauiApp = null!;
	private SailfishMauiContext _applicationContext = null!;
	private SailfishWindowScope? _windowScope;          // the one window's service scope (disposed when Run returns)
	private SailfishMauiContext _windowContext = null!;
	private IWindow? _mauiWindow;
	private string[] _arguments = Array.Empty<string>();

	/// <summary>Diagnostics set by SailfishDiagnostics.Register() (MAUI_SAILFISH_QT_HOST_DIAG=1); null when not referenced.</summary>
	internal static IQtHostDiagnostics? Diagnostics { get; set; }

	/// <summary>Gets the current SailfishMauiApplication instance.</summary>
	public static SailfishMauiApplication Current =>
		(SailfishMauiApplication)(IPlatformApplication.Current ?? throw new InvalidOperationException("No platform application."));

	public IServiceProvider Services { get; protected set; } = null!;
	public IApplication Application => _mauiApp;

	protected virtual string ApplicationId => ResolveApplicationId() ?? DefaultApplicationId;

	protected abstract MauiApp CreateMauiApp();

	/// <summary>
	/// Builds the MAUI app and runs the Qt/Silica loop on the current thread until the host shuts down.
	/// </summary>
	public void Run(string[] args)
	{
		_arguments = args ?? Array.Empty<string>();
		// The Qt host exists only on the device; on the build host fail with instructions instead of a dlopen crash.
		if (!OperatingSystem.IsLinux())
		{
			throw new PlatformNotSupportedException(
				"The Sailfish OS backend runs on the device, not on the build host. " +
				"In VS Code use the Run & Debug configuration " +
				"'Sailfish: F5 Deploy & Run (choose app...)' (or '... (device)' for the sample): " +
				"it deploys the app over SSH and streams the device log into the integrated terminal.");
		}
		Console.Error.WriteLine("[Sailfish] Run() entered");
		// Record unhandled exceptions so off-debugger crashes leave a trace on the device.
		AppDomain.CurrentDomain.UnhandledException += (_, e) => TraceCrash("AppDomain", e.ExceptionObject as Exception);
		TaskScheduler.UnobservedTaskException += (_, e) => TraceCrash("UnobservedTask", e.Exception);
		// ProcessExit fires on clean exit and SIGTERM; its absence in the trace means SIGKILL.
		AppDomain.CurrentDomain.ProcessExit += (_, _) => TraceLine("ProcessExit (clean exit or SIGTERM)");
		IPlatformApplication.Current = this;

		Console.Error.WriteLine("[Sailfish] Creating MAUI app...");
		var mauiApp = CreateMauiApp();
		Console.Error.WriteLine("[Sailfish] MAUI app created");

		Services = new SailfishServiceOverlay(mauiApp.Services);
		// Dispatcher.GetForCurrentThread() reads DispatcherProvider.Current, which plain UseMauiApp never sets.
		if (Services.GetService(typeof(Microsoft.Maui.Dispatching.IDispatcherProvider)) is Microsoft.Maui.Dispatching.IDispatcherProvider currentProvider)
			Microsoft.Maui.Dispatching.DispatcherProvider.SetCurrent(currentProvider);
		// Essentials statics must be installed before the app object and its pages exist.
		SailfishEssentials.Install(Services);
		var rootContext = new SailfishMauiContext(Services);
		_applicationContext = rootContext;

		_mauiApp = Services.GetRequiredService<IApplication>();
		Console.Error.WriteLine("[Sailfish] IApplication resolved");

		Console.Error.WriteLine("[Sailfish] Creating window...");
		// The window scope first, then the window, as MauiAppCompatActivity / MauiUISceneDelegate do.
		_windowScope = SailfishWindowScope.Create(Services);
		_windowContext = _windowScope.Context;
		var virtualWindow = _mauiApp.CreateWindow(new ActivationState(_windowContext));
		virtualWindow.Created();
		_mauiWindow = virtualWindow;
		Console.Error.WriteLine("[Sailfish] Window created");

		// The application and window handlers, as MauiApplication/MauiUIApplicationDelegate attach them on the other
		// platforms (the public ElementExtensions entry points; this object is the platform application). The
		// application handler answers Application.Quit(); MAUI_SAILFISH_APP_HANDLER=0 leaves it off.
		if (_mauiApp.Handler is null && SailfishEnv.Get("MAUI_SAILFISH_APP_HANDLER") != "0")
			Microsoft.Maui.Platform.ElementExtensions.SetApplicationHandler(this, _mauiApp, _applicationContext);

		// The window handler must exist before any page handler: AlertManager resolves its subscription
		// from window.Handler.MauiContext, otherwise DisplayAlertAsync and friends hang forever.
		if (virtualWindow.Handler is null)
		{
			Microsoft.Maui.Platform.ElementExtensions.SetWindowHandler(this, virtualWindow, _windowContext);
			Console.Error.WriteLine($"[Sailfish] Window handler attached ({virtualWindow.Handler?.GetType().Name})");
		}

		// The alert subscription fires when the root page handler attaches, and the renderer only
		// attaches handlers to the current content page, so attach the root handler here.
		if (virtualWindow is Microsoft.Maui.Controls.Window alertWindow &&
			alertWindow.Page is { Handler: null } rootPage)
		{
			// Use the registry's type so a NavigationPage root answers the navigation handshake.
			Handlers.SailfishHandlersFactory.AttachRootHandler(rootPage, _windowContext);
			Console.Error.WriteLine($"[Sailfish] Root page handler attached ({rootPage.GetType().Name}) — alert-manager subscription armed");
		}

		RaiseLaunched();

		var dispatcherProvider = Services.GetRequiredService<Microsoft.Maui.Dispatching.IDispatcherProvider>();
		var dispatcher = (SailfishDispatcher)dispatcherProvider.GetForCurrentThread()!;
		// This thread becomes the Qt loop thread; awaits started on it resume on it.
		SynchronizationContext.SetSynchronizationContext(new SailfishSynchronizationContext(dispatcher));

		// The native Qt loop is the MAUI main loop: its tick pumps the dispatcher and its timers.
		{
			var qml = SailfishEnv.Get("MAUI_SAILFISH_QT_HOST_QML");
			if (string.IsNullOrWhiteSpace(qml))
			{
				// Default: the shell shipped next to the app binary by the platform package.
				var bundled = Path.Combine(AppContext.BaseDirectory, "qml", "MauiShell.qml");
				if (File.Exists(bundled))
					qml = bundled;
			}

			if (string.IsNullOrWhiteSpace(qml))
				throw new InvalidOperationException("The Qt host requires MAUI_SAILFISH_QT_HOST_QML (absolute QML path) or the bundled qml/MauiShell.qml.");

			var qtDiag = QtHost.QtHostDiag.Enabled;
			Console.Error.WriteLine($"[Sailfish] Qt host QML: {qml}");

			// Measure text with QFontMetrics (what QML renders with); enable before the first layout pass.
			QtHost.QtHostTextMetrics.Enable();
			Console.Error.WriteLine("[Sailfish] Qt text-metrics: ENABLED (QFontMetrics via sailfish_host_measure_text — no SDL_ttf on the Qt-host path)");

			// The renderer reconciles the MAUI tree against persistent QML objects; QML events come back
			// as semantic events on the real MAUI controls.
			var renderer = virtualWindow as Microsoft.Maui.Controls.Window is { } controlsWindow
				? new QtHost.QtHostPageRenderer(controlsWindow, _windowContext)
				: null;
			if (renderer is null)
				Console.Error.WriteLine("[Sailfish] Qt render: window is not a Controls Window — page rendering disabled");

			ApplyAppMetaToShell();

			SubscribeNativeEvents();
			QtHost.QtHostRuntime.QmlEvent += (name, payload) =>
			{
				// aboutToQuit: synchronous, the loop ends right after and a dispatched call would never run.
				if (name == "svc-app-quit")
				{
					RaiseQuitting();
					return;
				}
				// Adapter events go to the renderer on the main thread; svc-* events go to platform services.
				if (QtHost.QtHostServices.IsServiceEvent(name))
				{
					dispatcher.Dispatch(() => QtHost.QtHostServices.Dispatch(name, payload));
					return;
				}
				if (renderer is null || name == "rendered")
					return;
				dispatcher.Dispatch(() => renderer.HandleNativeEvent(name, payload));
			};

			QtHost.QtHostRuntime.KeyInput += (kind, key, _, _) =>
			{
				// kind 0 = press. Back/Escape pop the MAUI navigation stack; the shim only observes keys,
				// so text keys still reach Qt's focus item.
				if (kind != 0 || (key != QtHost.QtHostRuntime.QtKeyBack && key != QtHost.QtHostRuntime.QtKeyEscape))
					return;
				dispatcher.Dispatch(() => renderer?.TryPop());
			};

			// Pointer events reach Silica first; the router forwards gestures to MAUI targets and leaves
			// native adapters' own events alone. Trace with MAUI_SAILFISH_QT_HOST_INPUT_TRACE=1.
						var inputRouter = renderer is null ? null : new QtHost.QtHostInputRouter(renderer, dispatcher, QtHost.QtHostPageRenderer.InputTrace);
			inputRouter?.Attach();

			if (renderer is not null)
			{
				// Platform services first, so the first page renders with the real ambience theme.
				dispatcher.Dispatch(SailfishEssentials.OnHostReady);
				dispatcher.Dispatch(renderer.Render);

				// Every change reaches the renderer as an event (handler mappers and commands, navigation requests, the
				// shell's stack/lifecycle events, the lists' own scheduling), which kicks a poll on the next loop turn.
				QtHost.QtHostPageRenderer.NavigationKick = () => dispatcher.Dispatch(renderer.KickedPoll);
				// A slow heartbeat stays as a verifier: work it finds is work no event announced (logged, counted as
				// timerWithWork; 0 on the device matrix). MAUI_SAILFISH_POLL_MS=250 is the former safety-net poll,
				// 0 turns it off.
				var pollMs = SailfishEnv.Int("MAUI_SAILFISH_POLL_MS") ?? QtHost.QtHostPageRenderer.DefaultHeartbeatMs;
				if (pollMs > 0)
				{
					Action? poll = null;
					poll = () =>
					{
						renderer.Poll();
						dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(pollMs), poll!);
					};
					dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(Math.Min(pollMs, 500)), poll);
				}
			}

			// Diagnostics live in a separate assembly that apps opt into with SailfishDiagnostics.Register().
			if (qtDiag)
			{
				if (Diagnostics is { } diagnostics)
					diagnostics.Attach(new QtHostDiagnosticsContext(this, virtualWindow, _windowContext, renderer, dispatcher, inputRouter));
				else
					Console.Error.WriteLine("[Sailfish] MAUI_SAILFISH_QT_HOST_DIAG=1 but no diagnostics are registered — reference Microsoft.Maui.SailfishOS.Diagnostics and call SailfishDiagnostics.Register() in CreateMauiApp");
			}

			QtHost.QtHostDiag.Trace(QtHost.QtHostDiagChannel.QtHost, "entering QtHostRuntime.Run (Qt/Silica host loop)");
			try
			{
				var rc = QtHost.QtHostRuntime.Run(dispatcher, qml);
				inputRouter?.Detach();
				// Shutdown already ran; the repeat checks that teardown is idempotent.
				QtHost.QtHostRuntime.Shutdown();
				QtHost.QtHostDiag.Trace(QtHost.QtHostDiagChannel.Lifecycle,
					$"QtHostRuntime.Run returned rc={rc}; idempotent Shutdown re-call OK");
			}
			catch (QtHost.QtHostException hx)
			{
				// The boot failure is already logged on the QT_HOST channel; only trace and tear down here.
				QtHost.QtHostDiag.Trace(QtHost.QtHostDiagChannel.Lifecycle,
					$"host failed ({hx.Operation}, native code {hx.Code}) — best-effort teardown");
				inputRouter?.Detach();
				QtHost.QtHostRuntime.Shutdown(); // best-effort teardown of the partially-initialized host
			}
			catch (Exception ex)
			{
				QtHost.QtHostDiag.Error(QtHost.QtHostDiagChannel.QtHost, $"host failed: {ex.Message}");
				inputRouter?.Detach();
				QtHost.QtHostRuntime.Shutdown(); // best-effort teardown of the partially-initialized host
			}
			// The window is gone with the loop: its scoped services go with it.
			_windowScope?.Dispose();
			return;
		}
	}

	// --- Native platform events: the counterpart of AppDelegate (iOS) / MainActivity (Android) ---
	// Each override runs first, then the ConfigureLifecycleEvents(events => events.AddSailfish(...)) handlers.

	/// <summary>The app and its window exist and the Qt loop starts next (FinishedLaunching / OnCreate).</summary>
	protected virtual void OnLaunched(string[] arguments) { }

	/// <summary>Qt's application state changed: Active in front, Inactive when minimized to a cover or behind a
	/// system dialog.</summary>
	protected virtual void OnApplicationStateChanged(SailfishApplicationState state) { }

	/// <summary>The Silica window turned (see SailfishOrientation in the csproj for the allowed ones).</summary>
	protected virtual void OnOrientationChanged(SailfishOrientation orientation) { }

	/// <summary>The app's cover on the home screen changed state.</summary>
	protected virtual void OnCoverStatusChanged(SailfishCoverStatus status) { }

	/// <summary>A cover action (SailfishCover.SetActions) was tapped; the action's own callback runs too.</summary>
	protected virtual void OnCoverActionTriggered(int index) { }

	/// <summary>The ambience switched between light and dark text.</summary>
	protected virtual void OnColorSchemeChanged(SailfishColorScheme scheme) { }

	/// <summary>The virtual keyboard opened, closed or resized; <paramref name="keyboard"/> is in window pixels.</summary>
	protected virtual void OnInputMethodChanged(bool visible, Rect keyboard) { }

	/// <summary>The app is about to quit (closed from the home screen, or Application.Quit): save state here.</summary>
	protected virtual void OnQuitting() { }

	internal void RaiseLaunched()
	{
		OnLaunched(_arguments);
		Invoke<SailfishLifecycle.OnLaunched>(d => d(this, _arguments));
	}

	internal void RaiseQuitting()
	{
		OnQuitting();
		Invoke<SailfishLifecycle.OnQuitting>(d => d(this));
	}

	private void ApplyOrientation(SailfishOrientation orientation)
	{
		if (orientation == SailfishDisplay.Orientation)
			return;
		SailfishDisplay.SetOrientation(orientation);   // DeviceDisplay reports the rotated size first
		OnOrientationChanged(orientation);
		Invoke<SailfishLifecycle.OnOrientationChanged>(d => d(this, orientation));
	}

	/// <summary>Routes the shell's native events (MauiShell.qml, the shim) to the overrides and handlers.</summary>
	internal void SubscribeNativeEvents()
	{
		QtHost.QtHostServices.Subscribe("svc-app-state", e =>
		{
			var state = (SailfishApplicationState)BridgeJson.Int(e, "state", (int)SailfishApplicationState.Active);
			OnApplicationStateChanged(state);
			Invoke<SailfishLifecycle.OnApplicationStateChanged>(d => d(this, state));
		});
		QtHost.QtHostServices.Subscribe("svc-app-orientation", e =>
			ApplyOrientation((SailfishOrientation)BridgeJson.Int(e, "orientation", (int)SailfishOrientation.Portrait)));
		// The shell turns while it starts, before this subscription exists, so the first orientation is read here.
		QtHost.QtHostRuntime.Post(() =>
		{
			if (int.TryParse(QtHost.QtHostRuntime.Eval("typeof window!=='undefined'&&window?window.orientation:0").Trim('"'),
			        out var orientation) && orientation > 0)
				ApplyOrientation((SailfishOrientation)orientation);
		});
		QtHost.QtHostServices.Subscribe("svc-cover-status", e =>
		{
			var status = (e.TryGetProperty("status", out var s) ? s.GetString() : null) switch
			{
				"active" => SailfishCoverStatus.Active,
				"activating" => SailfishCoverStatus.Activating,
				"deactivating" => SailfishCoverStatus.Deactivating,
				_ => SailfishCoverStatus.Inactive,
			};
			OnCoverStatusChanged(status);
			Invoke<SailfishLifecycle.OnCoverStatusChanged>(d => d(this, status));
		});
		QtHost.QtHostServices.Subscribe("svc-cover-action", e =>
		{
			var index = BridgeJson.Int(e, "index");
			OnCoverActionTriggered(index);
			Invoke<SailfishLifecycle.OnCoverActionTriggered>(d => d(this, index));
		});
		QtHost.QtHostServices.Subscribe("svc-theme-changed", e =>
		{
			var scheme = e.TryGetProperty("light", out var light) && light.ValueKind == System.Text.Json.JsonValueKind.True
				? SailfishColorScheme.DarkOnLight
				: SailfishColorScheme.LightOnDark;
			OnColorSchemeChanged(scheme);
			Invoke<SailfishLifecycle.OnColorSchemeChanged>(d => d(this, scheme));
		});
		QtHost.QtHostServices.Subscribe("svc-input-method", e =>
		{
			var visible = e.TryGetProperty("visible", out var v) && v.ValueKind == System.Text.Json.JsonValueKind.True;
			var keyboard = new Rect(BridgeJson.Num(e, "x"), BridgeJson.Num(e, "y"), BridgeJson.Num(e, "width"), BridgeJson.Num(e, "height"));
			OnInputMethodChanged(visible, keyboard);
			Invoke<SailfishLifecycle.OnInputMethodChanged>(d => d(this, visible, keyboard));
		});
	}

	private void Invoke<TDelegate>(Action<TDelegate> call) where TDelegate : Delegate =>
		Services?.GetService<ILifecycleEventService>()?.InvokeEvents(typeof(TDelegate).Name, call);
	/// <summary>Appends an unhandled exception to stderr and /tmp/maui_trace.log.</summary>
	private static void TraceCrash(string source, Exception? ex)
	{
		var text = $"[Sailfish] CRASH {source}: {ex}";
		Console.Error.WriteLine(text);
		try
		{
			File.AppendAllText("/tmp/maui_trace.log", text + Environment.NewLine);
		}
		catch
		{
			// best effort — the stderr line above already carries the stack
		}
	}

	/// <summary>Appends an exit line to stderr and /tmp/maui_trace.log.</summary>
	private static void TraceLine(string text)
	{
		var line = $"[Sailfish] EXIT {text}";
		Console.Error.WriteLine(line);
		try
		{
			File.AppendAllText("/tmp/maui_trace.log", line + Environment.NewLine);
		}
		catch
		{
			// best effort — the stderr line above already carries the text
		}
	}

	/// <summary>
	/// Applies qml/maui-appmeta.json (orientation, cover, title from MSBuild) to the shell window.
	/// A build without the file (a plain net11.0 head) still gets the MAUI_SAILFISH_ORIENTATION override.
	/// </summary>
	private void ApplyAppMetaToShell()
	{
		try
		{
			var path = Path.Combine(AppContext.BaseDirectory, "qml", "maui-appmeta.json");
			using var doc = System.Text.Json.JsonDocument.Parse(File.Exists(path) ? File.ReadAllText(path) : "{}");
			var root = doc.RootElement;
			// MAUI_SAILFISH_ORIENTATION overrides the baked value (tools/sf matrix pins Portrait: its injected
			// gestures use portrait window coordinates).
			var orientation = SailfishEnv.Get("MAUI_SAILFISH_ORIENTATION") is { Length: > 0 } forced
				? forced
				: root.TryGetProperty("orientation", out var o) ? o.GetString() : null;
			var cover = root.TryGetProperty("cover", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.True;
			var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty;
			var coverQml = root.TryGetProperty("coverQml", out var cq) ? cq.GetString() ?? string.Empty : string.Empty;
			var coverUrl = coverQml.Length > 0 && File.Exists(Path.Combine(AppContext.BaseDirectory, "qml", coverQml))
				? new Uri(Path.Combine(AppContext.BaseDirectory, "qml", coverQml)).AbsoluteUri
				: string.Empty;
			var coverUrlJs = System.Text.Json.JsonSerializer.Serialize(coverUrl, SailfishJsonContext.Default.String);
			var mask = orientation switch
			{
				"Portrait" => 1,
				"Landscape" => 2,
				_ => 15,   // Any, and an app meta from before the default changed
			};
			// Source-generated: reflection-based System.Text.Json is off in the trimmed app.
			var titleJs = System.Text.Json.JsonSerializer.Serialize(title, SailfishJsonContext.Default.String);
			var coverJs = cover ? "true" : "false";
			Console.Error.WriteLine($"[Sailfish] app meta: orientation={orientation} mask={mask} cover={cover}{(coverUrl.Length > 0 ? " coverQml=" + coverQml : "")} title={title}");
			// `window` is MauiShell's ApplicationWindow; pageStack.parent is an inner item, where these would land as
			// unused dynamic properties (the app stayed portrait and the cover off).
			QtHost.QtHostRuntime.Post(() => QtHost.QtHostRuntime.Eval(
				"(function(){if(typeof window==='undefined'||!window)return 'no window';"
				+ "window.mauiOrientations=" + mask + ";window.mauiCoverEnabled=" + coverJs + ";window.mauiCoverTitle=" + titleJs
				+ ";window.mauiCoverUrl=" + coverUrlJs + ";return 'ok';})()"));
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[Sailfish] app meta: failed to apply ({ex.Message})");
		}
	}

	private string? ResolveApplicationId()
	{
		if (TryGetApplicationId(GetType().Assembly, out var applicationId))
			return applicationId;

		var entryAssembly = Assembly.GetEntryAssembly();
		if (entryAssembly != null
			&& !ReferenceEquals(entryAssembly, GetType().Assembly)
			&& TryGetApplicationId(entryAssembly, out applicationId))
		{
			return applicationId;
		}

		return null;
	}

	private static bool TryGetApplicationId(Assembly assembly, out string applicationId)
	{
		foreach (var metadata in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
		{
			if (!string.Equals(metadata.Key, MauiApplicationIdMetadataKey, StringComparison.Ordinal))
				continue;

			if (string.IsNullOrWhiteSpace(metadata.Value))
				continue;

			applicationId = metadata.Value;
			return true;
		}

		applicationId = string.Empty;
		return false;
	}

}

