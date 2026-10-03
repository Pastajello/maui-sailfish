using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Platform;

// Startup, in the order the other platforms' application/activity/scene delegates do it: the app and its services,
// the application handler, the window scope and window, the root handlers, then the Qt loop with the renderer.
public abstract partial class SailfishMauiApplication
{
	/// <summary>
	/// Builds the MAUI app and runs the Qt/Silica loop on the current thread until the host shuts down. Returns the
	/// process exit code: the loop's own, <see cref="ExitHostFailed"/> or <see cref="ExitStartupFailed"/>, so a launcher
	/// (tools/sf run, the booster) sees a failed start.
	/// </summary>
	public int Run(string[] args)
	{
		_arguments = args ?? Array.Empty<string>();
		EnsureOnDevice();
		Console.Error.WriteLine("[Sailfish] Run() entered");
		// Unhandled exceptions, unobserved tasks and the exit leave a trace on the device (stderr + trace file).
		SailfishCrashTrace.Install();
		IPlatformApplication.Current = this;

		BuildApplication();
		var window = CreateWindow();
		RaiseLaunched();

		var dispatcherProvider = Services.GetRequiredService<Microsoft.Maui.Dispatching.IDispatcherProvider>();
		var dispatcher = (SailfishDispatcher)dispatcherProvider.GetForCurrentThread()!;

		// The native Qt loop is the MAUI main loop: its tick pumps the dispatcher and its timers.
		var qml = ResolveShellQml();
		Console.Error.WriteLine($"[Sailfish] Qt host QML: {qml}");
		// Measure text with QFontMetrics (what QML renders with); enable before the first layout pass.
		QtHostTextMetrics.Enable();
		Console.Error.WriteLine("[Sailfish] Qt text-metrics: ENABLED (QFontMetrics via sailfish_host_measure_text — no SDL_ttf on the Qt-host path)");

		// The renderer reconciles the MAUI tree against persistent QML objects; QML events come back as semantic events
		// on the real MAUI controls.
		var renderer = window as Microsoft.Maui.Controls.Window is { } controlsWindow
			? new QtHostPageRenderer(controlsWindow, _windowContext)
			: null;
		if (renderer is null)
			Console.Error.WriteLine("[Sailfish] Qt render: window is not a Controls Window — page rendering disabled");

		ApplyAppMetaToShell();
		SubscribeNativeEvents();
		var inputRouter = RouteHostEvents(renderer, dispatcher);
		StartRendering(renderer, dispatcher);
		AttachDiagnostics(window, renderer, dispatcher, inputRouter);
		return RunLoop(qml, renderer, dispatcher, inputRouter);
	}

	/// <summary>The Qt host exists only on the device; on the build host fail with instructions instead of a dlopen crash.</summary>
	private static void EnsureOnDevice()
	{
		if (!OperatingSystem.IsLinux())
		{
			throw new PlatformNotSupportedException(
				"The Sailfish OS backend runs on the device, not on the build host. " +
				"In VS Code use the Run & Debug configuration " +
				"'Sailfish: F5 Deploy & Run (choose app...)' (or '... (device)' for the sample): " +
				"it deploys the app over SSH and streams the device log into the integrated terminal.");
		}
	}

	/// <summary>CreateMauiApp with the platform in place (Essentials statics, the loop-thread dispatcher and its
	/// synchronization context), then the services overlay, the Essentials statics from the container and the
	/// IApplication with its handler.</summary>
	private void BuildApplication()
	{
		Console.Error.WriteLine("[Sailfish] Creating MAUI app...");
		SailfishEssentials.InstallEarly();
		InstallDispatcherProvider();
		// This thread becomes the Qt loop thread. Its context goes in before the app exists, as Android's UI thread has
		// one from the start: async work the app starts in its constructor, CreateWindow or OnStart resumes here, not on
		// a pool thread (Profitocracy built its AppShell and set Window.Page from such a continuation).
		SynchronizationContext.SetSynchronizationContext(new SailfishSynchronizationContext(SailfishDispatcherProvider.BindLoopThread()));
		SailfishExtensions.Load(GetType().Assembly);
		var mauiApp = CreateMauiApp();
		Console.Error.WriteLine("[Sailfish] MAUI app created");

		Services = new SailfishServiceOverlay(mauiApp.Services);
		// Dispatcher.GetForCurrentThread() reads DispatcherProvider.Current, which plain UseMauiApp never sets.
		if (Services.GetService(typeof(Microsoft.Maui.Dispatching.IDispatcherProvider)) is Microsoft.Maui.Dispatching.IDispatcherProvider currentProvider)
			Microsoft.Maui.Dispatching.DispatcherProvider.SetCurrent(currentProvider);
		// Essentials' MainThread runs on this thread, the Qt loop's, through its dispatcher.
		if (Microsoft.Maui.Dispatching.Dispatcher.GetForCurrentThread() is { } loopDispatcher)
		{
			SailfishMainThread.Install(action => loopDispatcher.Dispatch(action));
			SailfishDevTaps.Schedule(loopDispatcher);   // MAUI_SAILFISH_TAPS only
		}
		// Essentials statics must be installed before the app object and its pages exist.
		SailfishEssentials.Install(Services);
		// Device.GetNamedSize (FontSize="Large") asks DependencyService, which has no Sailfish entry otherwise.
#pragma warning disable CS0612 // IFontNamedSizeService is obsolete, NamedSize XAML still needs it
		Microsoft.Maui.Controls.DependencyService.RegisterSingleton<Microsoft.Maui.Controls.Internals.IFontNamedSizeService>(new SailfishFontNamedSizeService());
#pragma warning restore CS0612
		_applicationContext = new SailfishMauiContext(Services);

		_mauiApp = Services.GetRequiredService<IApplication>();
		Console.Error.WriteLine("[Sailfish] IApplication resolved");

		// The application handler before the window, as MauiApplication.OnCreate / FinishedLaunching attach it: MAUI
		// sends the first page's Appearing inside CreateWindow, and anything it starts there (an entrance animation's
		// IAnimationManager, FindMauiContext) finds its context through the application. It also answers
		// Application.Quit(); MAUI_SAILFISH_APP_HANDLER=0 leaves it off.
		if (_mauiApp.Handler is null && SailfishEnv.Get("MAUI_SAILFISH_APP_HANDLER") != "0")
			Microsoft.Maui.Platform.ElementExtensions.SetApplicationHandler(this, _mauiApp, _applicationContext);
	}

	/// <summary>The window scope, the window and its handler, then the root page's handler.</summary>
	private IWindow CreateWindow()
	{
		Console.Error.WriteLine("[Sailfish] Creating window...");
		// The window scope first, then the window, as MauiAppCompatActivity / MauiUISceneDelegate do.
		_windowScope = SailfishWindowScope.Create(Services);
		_windowContext = _windowScope.Context;
		var window = _mauiApp.CreateWindow(new ActivationState(_windowContext));
		window.Created();
		_mauiWindow = window;
		Console.Error.WriteLine("[Sailfish] Window created");

		// The window handler, as MauiAppCompatActivity/MauiUISceneDelegate attach it on the other platforms (the public
		// ElementExtensions entry points; this object is the platform application). It must exist before any page
		// handler: AlertManager resolves its subscription from window.Handler.MauiContext, otherwise DisplayAlertAsync
		// and friends hang forever.
		if (window.Handler is null)
		{
			Microsoft.Maui.Platform.ElementExtensions.SetWindowHandler(this, window, _windowContext);
			Console.Error.WriteLine($"[Sailfish] Window handler attached ({window.Handler?.GetType().Name})");
		}

		// The alert subscription fires when the root page handler attaches, and the renderer only attaches handlers to
		// the current content page, so attach the root handler here.
		if (window is Microsoft.Maui.Controls.Window alertWindow && alertWindow.Page is { Handler: null } rootPage)
		{
			// Use the registry's type so a NavigationPage root answers the navigation handshake.
			Handlers.SailfishHandlersFactory.AttachRootHandler(rootPage, _windowContext);
			Console.Error.WriteLine($"[Sailfish] Root page handler attached ({rootPage.GetType().Name}) — alert-manager subscription armed");
		}
		return window;
	}

	/// <summary>The shell QML: MAUI_SAILFISH_QT_HOST_QML, else the one shipped next to the app by the platform package.</summary>
	private static string ResolveShellQml()
	{
		var qml = SailfishEnv.Get("MAUI_SAILFISH_QT_HOST_QML");
		if (string.IsNullOrWhiteSpace(qml))
		{
			var bundled = Path.Combine(AppContext.BaseDirectory, "qml", "MauiShell.qml");
			if (File.Exists(bundled))
				qml = bundled;
		}
		if (string.IsNullOrWhiteSpace(qml))
			throw new InvalidOperationException("The Qt host requires MAUI_SAILFISH_QT_HOST_QML (absolute QML path) or the bundled qml/MauiShell.qml.");
		return qml;
	}

	/// <summary>QML events, keys and pointer input from the shim to MAUI: svc-* events to the platform services,
	/// adapter events and back keys to the renderer, pointer gestures through the input router (returned).</summary>
	private QtHostInputRouter? RouteHostEvents(QtHostPageRenderer? renderer, SailfishDispatcher dispatcher)
	{
		QtHostRuntime.QmlEvent += (name, payload) =>
		{
			// aboutToQuit: synchronous, the loop ends right after and a dispatched call would never run.
			if (name == ShellEvents.AppQuit)
			{
				RaiseQuitting();
				return;
			}
			// Adapter events go to the renderer on the main thread; svc-* events go to platform services.
			if (QtHostServices.IsServiceEvent(name))
			{
				dispatcher.Dispatch(() => QtHostServices.Dispatch(name, payload));
				return;
			}
			if (renderer is null || name == "rendered")
				return;
			dispatcher.Dispatch(() => renderer.HandleNativeEvent(name, payload));
		};

		QtHostRuntime.KeyInput += (kind, key, _, _) =>
		{
			// kind 0 = press. Back/Escape pop the MAUI navigation stack; the shim only observes keys, so text keys
			// still reach Qt's focus item.
			if (kind != 0 || (key != QtHostRuntime.QtKeyBack && key != QtHostRuntime.QtKeyEscape))
				return;
			dispatcher.Dispatch(() => renderer?.TryPop());
		};

		// Pointer events reach Silica first; the router forwards gestures to MAUI targets and leaves native adapters'
		// own events alone. Trace with MAUI_SAILFISH_QT_HOST_INPUT_TRACE=1.
		var inputRouter = renderer is null ? null : new QtHostInputRouter(renderer, dispatcher, QtHostPageRenderer.InputTrace);
		inputRouter?.Attach();
		return inputRouter;
	}

	/// <summary>The first tick starts the platform services and the first render; every later change reaches the
	/// renderer as an event, and a slow heartbeat only verifies.</summary>
	private static void StartRendering(QtHostPageRenderer? renderer, SailfishDispatcher dispatcher)
	{
		// Platform services first, so the first page renders with the real ambience theme. Also without a renderer
		// (a window that is not a Controls Window): HostReady gates SecureStorage, the theme, the cover and OpenUrl.
		dispatcher.Dispatch(SailfishEssentials.OnHostReady);
		if (renderer is null)
			return;
		dispatcher.Dispatch(renderer.Render);

		// Every change reaches the renderer as an event (handler mappers and commands, navigation requests, the shell's
		// stack/lifecycle events, the lists' own scheduling), which kicks a poll on the next loop turn.
		renderer.PollKick = () => dispatcher.Dispatch(renderer.KickedPoll);
		// A slow heartbeat stays as a verifier: work it finds is work no event announced (logged, counted as
		// timerWithWork; 0 on the device matrix). MAUI_SAILFISH_POLL_MS=250 is the former safety-net poll, 0 turns it off.
		RenderScheduler.StartHeartbeat(dispatcher,
			SailfishEnv.Int("MAUI_SAILFISH_POLL_MS") ?? QtHostPageRenderer.DefaultHeartbeatMs, renderer.Poll);
	}

	/// <summary>Diagnostics live in a separate assembly that apps opt into with SailfishDiagnostics.Register().</summary>
	private void AttachDiagnostics(IWindow window, QtHostPageRenderer? renderer, SailfishDispatcher dispatcher, QtHostInputRouter? inputRouter)
	{
		if (!QtHostDiag.Enabled)
			return;
		if (Diagnostics is { } diagnostics)
			diagnostics.Attach(new QtHostDiagnosticsContext(this, window, _windowContext, renderer, dispatcher, inputRouter));
		else
			Console.Error.WriteLine("[Sailfish] MAUI_SAILFISH_QT_HOST_DIAG=1 but no diagnostics are registered — reference Microsoft.Maui.SailfishOS.Diagnostics and call SailfishDiagnostics.Register() in CreateMauiApp");
	}

	/// <summary>Runs the Qt loop until the host shuts down and tears down; returns the exit code.</summary>
	private int RunLoop(string qml, QtHostPageRenderer? renderer, SailfishDispatcher dispatcher, QtHostInputRouter? inputRouter)
	{
		QtHostDiag.Trace(QtHostDiagChannel.QtHost, "entering QtHostRuntime.Run (Qt/Silica host loop)");
		int exitCode;
		try
		{
			var firstTitle = renderer?.CurrentTitle ?? string.Empty;
			exitCode = QtHostRuntime.Run(dispatcher, qml,
				propsJson: "{\"mauiFirstTitle\":" + BridgeValue.Quote(firstTitle) + "}");
			inputRouter?.Detach();
			// Shutdown already ran; the repeat checks that teardown is idempotent.
			QtHostRuntime.Shutdown();
			QtHostDiag.Trace(QtHostDiagChannel.Lifecycle, $"QtHostRuntime.Run returned rc={exitCode}; idempotent Shutdown re-call OK");
		}
		catch (QtHostException hx)
		{
			// The boot failure is already logged on the QT_HOST channel; only trace and tear down here.
			QtHostDiag.Trace(QtHostDiagChannel.Lifecycle, $"host failed ({hx.Operation}, native code {hx.Code}) — best-effort teardown");
			inputRouter?.Detach();
			QtHostRuntime.Shutdown(); // best-effort teardown of the partially-initialized host
			exitCode = ExitHostFailed;
		}
		catch (Exception ex)
		{
			// The whole exception: a boot failure must be diagnosable from the device log alone.
			QtHostDiag.Error(QtHostDiagChannel.QtHost, $"host failed: {ex}");
			inputRouter?.Detach();
			QtHostRuntime.Shutdown(); // best-effort teardown of the partially-initialized host
			exitCode = ExitStartupFailed;
		}
		// The window is gone with the loop: its scoped services go with it.
		_windowScope?.Dispose();
		return exitCode;
	}
}
