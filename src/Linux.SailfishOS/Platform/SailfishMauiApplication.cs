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
public abstract partial class SailfishMauiApplication : IPlatformApplication
{
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

	protected abstract MauiApp CreateMauiApp();

	/// <summary>
	/// Makes the Sailfish provider MAUI's current one before the app's container exists. A plain UseMauiApp app
	/// registers IDispatcherProvider as DispatcherProvider.Current, whose plain-net default has no dispatcher, so its
	/// singletons were constructed with a null IDispatcher (GitTrends' ThemeService).
	/// </summary>
	internal static void InstallDispatcherProvider()
	{
		if (Microsoft.Maui.Dispatching.DispatcherProvider.Current is not SailfishDispatcherProvider)
			Microsoft.Maui.Dispatching.DispatcherProvider.SetCurrent(new SailfishDispatcherProvider());
	}

	/// <summary>Process exit code when the Qt host failed to start (a QtHostException: missing shim, QML load error).</summary>
	public const int ExitHostFailed = 2;

	/// <summary>Process exit code when managed startup failed (an exception building the app or its window).</summary>
	public const int ExitStartupFailed = 1;

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

	/// <summary>The display turned off, dimmed or on (MCE).</summary>
	protected virtual void OnDisplayStateChanged(SailfishDisplayState state) { }

	/// <summary>The lock screen was shown or dismissed (MCE touch-screen lock).</summary>
	protected virtual void OnScreenLockChanged(bool locked) { }

	/// <summary>MCE memory pressure changed: free caches on Warning/Critical (OnTrimMemory / DidReceiveMemoryWarning).</summary>
	protected virtual void OnMemoryLevelChanged(SailfishMemoryLevel level) { }

	private readonly SailfishSystemService _system = new();

	/// <summary>The last reported MCE states (Unknown/null until MCE answered).</summary>
	public SailfishDisplayState? DisplayState => _system.DisplayState;
	public bool? ScreenLocked => _system.ScreenLocked;
	public SailfishMemoryLevel MemoryLevel => _system.MemoryLevel;

	/// <summary>MCE answered the memory level query (it says "unknown" where memory tracking is off).</summary>
	internal bool MemoryLevelAnswered => _system.MemoryLevelAnswered;

	/// <summary>First Qt tick (SailfishEssentials.OnHostReady): the MCE service needs the running QML host. Without
	/// MCE bindings (a desktop Qt, a broken image) the events just never come.</summary>
	internal void StartSystemService() => _system.Start();

	/// <summary>The app is about to quit (closed from the home screen, or Application.Quit): save state here.</summary>
	protected virtual void OnQuitting() { }

	internal void RaiseLaunched()
	{
		SailfishOpenUrl.QueueLaunchArguments(_arguments);
		OnLaunched(_arguments);
		Invoke<SailfishLifecycle.OnLaunched>(d => d(this, _arguments));
	}

	/// <summary>The window's renderer while the loop runs (null for a window that is not a Controls Window).</summary>
	private QtHostPageRenderer? _renderer;
	private bool _windowDestroyed;

	internal void RaiseQuitting()
	{
		// The window lifecycle first, as Android ends with onPause/onStop/onDestroy: OnSleep and Window.Destroying run
		// before the app's own last hook (M1, tracker S04).
		try
		{
			if (_renderer is { } renderer)
				renderer.RaiseQuitLifecycle();
			else if (_mauiWindow is { } window && !_windowDestroyed)
			{
				_windowDestroyed = true;   // Window.Destroying throws when sent twice
				window.Destroying();
			}
		}
		catch (Exception ex) { Console.Error.WriteLine($"[Sailfish] quit lifecycle failed: {ex}"); }
		// Runs from the QML event callback while the loop ends: the app's override failing must not skip the lifecycle
		// delegates (W1.9).
		try { OnQuitting(); }
		catch (Exception ex) { Console.Error.WriteLine($"[Sailfish] OnQuitting failed: {ex}"); }
		try { Invoke<SailfishLifecycle.OnQuitting>(d => d(this)); }
		catch (Exception ex) { Console.Error.WriteLine($"[Sailfish] SailfishLifecycle.OnQuitting failed: {ex}"); }
	}

	private void ApplyOrientation(SailfishOrientation orientation)
	{
		if (orientation == SailfishDisplay.Orientation)
			return;
		SailfishDisplay.SetOrientation(orientation);   // DeviceDisplay reports the rotated size first
		OnOrientationChanged(orientation);
		Invoke<SailfishLifecycle.OnOrientationChanged>(d => d(this, orientation));
	}

	/// <summary>Routes the shell's native events (MauiShell.qml, the inline services) to the overrides and handlers.
	/// Payloads are parsed by their <see cref="ShellEvents"/> records.</summary>
	internal void SubscribeNativeEvents()
	{
		QtHost.QtHostServices.Subscribe(ShellEvents.AppState, e =>
		{
			// The shell reports both Qt.application signals (state and active) with the same payload, so one transition
			// arrives twice: raise it once.
			var state = AppStatePayload.Parse(e).State;
			if (_appStateRaised == state)
				return;
			_appStateRaised = state;
			OnApplicationStateChanged(state);
			Invoke<SailfishLifecycle.OnApplicationStateChanged>(d => d(this, state));
		});
		QtHost.QtHostServices.Subscribe(ShellEvents.AppOrientation, e => ApplyOrientation(AppOrientationPayload.Parse(e).Orientation));
		// The shell turns while it starts, before this subscription exists, so the first orientation is read here.
		QtHost.QtThread.Later(() =>
		{
			if (int.TryParse(QtHost.QtHostRuntime.Eval("typeof window!=='undefined'&&window?window.orientation:0").Trim('"'),
			        out var orientation) && orientation > 0)
				ApplyOrientation((SailfishOrientation)orientation);
		});
		// The services update their state first (SailfishCover.IsActive, AppInfo.RequestedTheme), then the app hears it.
		SailfishCover.StatusChanged += status =>
		{
			OnCoverStatusChanged(status);
			Invoke<SailfishLifecycle.OnCoverStatusChanged>(d => d(this, status));
		};
		SailfishCover.ActionTriggered += index =>
		{
			OnCoverActionTriggered(index);
			Invoke<SailfishLifecycle.OnCoverActionTriggered>(d => d(this, index));
		};
		SailfishTheme.Changed += theme =>
		{
			var scheme = theme == AppTheme.Light ? SailfishColorScheme.DarkOnLight : SailfishColorScheme.LightOnDark;
			OnColorSchemeChanged(scheme);
			Invoke<SailfishLifecycle.OnColorSchemeChanged>(d => d(this, scheme));
		};
		_system.Subscribe();
		_system.DisplayStateChanged += state =>
		{
			OnDisplayStateChanged(state);
			Invoke<SailfishLifecycle.OnDisplayStateChanged>(d => d(this, state));
		};
		_system.ScreenLockChanged += locked =>
		{
			OnScreenLockChanged(locked);
			Invoke<SailfishLifecycle.OnScreenLockChanged>(d => d(this, locked));
		};
		_system.MemoryLevelChanged += level =>
		{
			OnMemoryLevelChanged(level);
			Invoke<SailfishLifecycle.OnMemoryLevelChanged>(d => d(this, level));
		};
		QtHost.QtHostServices.Subscribe(ShellEvents.InputMethod, e =>
		{
			var (visible, keyboard) = InputMethodPayload.Parse(e);
			OnInputMethodChanged(visible, keyboard);
			Invoke<SailfishLifecycle.OnInputMethodChanged>(d => d(this, visible, keyboard));
		});
	}

	// The last application state raised (svc-app-state de-duplication); null before the first report.
	private SailfishApplicationState? _appStateRaised;

	/// <summary>Diagnostics: how often each native lifecycle event (by SailfishLifecycle delegate name) was raised.</summary>
	internal static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> NativeEventCounts = new();

	private void Invoke<TDelegate>(Action<TDelegate> call) where TDelegate : Delegate
	{
		NativeEventCounts.AddOrUpdate(typeof(TDelegate).Name, 1, static (_, n) => n + 1);
		Services?.GetService<ILifecycleEventService>()?.InvokeEvents(typeof(TDelegate).Name, call);
	}
	/// <summary>
	/// Applies qml/maui-appmeta.json (orientation, cover, title from MSBuild) to the shell window.
	/// A build without the file (a plain net11.0 head) still gets the MAUI_SAILFISH_ORIENTATION override.
	/// </summary>
	private void ApplyAppMetaToShell()
	{
		try
		{
			var meta = SailfishAppMeta.Current;
			// MAUI_SAILFISH_ORIENTATION overrides the baked value (tools/sf matrix pins Portrait: its injected
			// gestures use portrait window coordinates).
			var orientation = SailfishEnv.Get("MAUI_SAILFISH_ORIENTATION") is { Length: > 0 } forced ? forced : meta.Orientation;
			var cover = meta.Cover;
			var title = meta.Title;
			var coverQml = meta.CoverQml;
			var coverUrl = coverQml.Length > 0 && File.Exists(Path.Combine(AppContext.BaseDirectory, "qml", coverQml))
				? new Uri(Path.Combine(AppContext.BaseDirectory, "qml", coverQml)).AbsoluteUri
				: string.Empty;
			var coverUrlJs = System.Text.Json.JsonSerializer.Serialize(coverUrl, SailfishJsonContext.Default.String);
			var mask = SailfishAppMeta.OrientationMask(orientation);
			// Source-generated: reflection-based System.Text.Json is off in the trimmed app.
			var titleJs = System.Text.Json.JsonSerializer.Serialize(title, SailfishJsonContext.Default.String);
			var coverJs = cover ? "true" : "false";
			Console.Error.WriteLine($"[Sailfish] app meta: orientation={orientation} mask={mask} cover={cover}{(coverUrl.Length > 0 ? " coverQml=" + coverQml : "")} title={title}");
			// `window` is MauiShell's ApplicationWindow; pageStack.parent is an inner item, where these would land as
			// unused dynamic properties (the app stayed portrait and the cover off).
			QtHost.QtThread.Later(() => QtHost.QtHostRuntime.Eval(
				"(function(){if(typeof window==='undefined'||!window)return 'no window';"
				+ "window.mauiOrientations=" + mask + ";window.mauiCoverEnabled=" + coverJs + ";window.mauiCoverTitle=" + titleJs
				+ ";window.mauiCoverUrl=" + coverUrlJs + ";return 'ok';})()"));
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[Sailfish] app meta: failed to apply ({ex.Message})");
		}
	}

}

