using System.Runtime.InteropServices;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// Managed wrapper over the native Qt/Silica host that also serves as the MAUI main loop.
/// The native tick drains the dispatcher and due timers on the Qt thread; <see cref="Post"/>
/// marshals work onto that thread from anywhere.
/// </summary>
public static class QtHostRuntime
{
	/// <summary>Qt::Key_Back (0x01000061) — hardware back on Sailfish.</summary>
	public const int QtKeyBack = 0x01000061;

	/// <summary>Qt::Key_Escape (0x01000000).</summary>
	public const int QtKeyEscape = 0x01000000;

	/// <summary>Qt::Key_Return (0x01000004).</summary>
	public const int QtKeyReturn = 0x01000004;

	/// <summary>Qt::Key_Backspace (0x01000003).</summary>
	public const int QtKeyBackspace = 0x01000003;

	/// <summary>Qt::Key_Left (0x01000012).</summary>
	public const int QtKeyLeft = 0x01000012;

	/// <summary>Qt::Key_Right (0x01000014).</summary>
	public const int QtKeyRight = 0x01000014;

	/// <summary>Qt::Key_A (0x41); letters continue sequentially to Qt::Key_Z (0x5A).</summary>
	public const int QtKeyA = 0x41;

	/// <summary>Qt::Key_0 (0x30); digits continue sequentially to Qt::Key_9 (0x39).</summary>
	public const int QtKey0 = 0x30;

	// Mirror of `enum sfhost_err` in sailfish_host.h; the values are stable ABI.

	/// <summary>sfhost_err: operation succeeded (0).</summary>
	public const int SfhostOk = 0;

	/// <summary>sfhost_err: invalid arguments, or host not ready / in teardown (-1).</summary>
	public const int SfhostEArgs = -1;

	/// <summary>sfhost_err: property error — unknown name or conversion failure (-2).</summary>
	public const int SfhostEProperty = -2;

	/// <summary>sfhost_err: dead or unknown object handle (-3).</summary>
	public const int SfhostEDeadHandle = -3;

	/// <summary>sfhost_err: the JS engine reported an error — eval, page push or pop (-4).</summary>
	public const int SfhostEJs = -4;

	/// <summary>sfhost_err: a QML file did not load; the QML errors are the last error (-5).</summary>
	public const int SfhostELoad = -5;

	// Delegates handed to native code must stay alive for the process lifetime.
	private static QtHostNative.TickFn? _tick;
	private static QtHostNative.PointerFn? _pointer;
	private static QtHostNative.KeyFn? _key;
	private static QtHostNative.EventFn? _event;
	// The shim stores this function pointer in each queued PostEvent: a method-group conversion is only kept alive
	// by the compiler's delegate cache, so hold the delegate explicitly.
	private static readonly QtHostNative.VoidFn _postThunk = PostThunk;

	// 1 once Shutdown() has run (makes teardown idempotent).
	private static int _shutdownRequested;

	// Post/GCHandle counters: Allocated == Freed must hold whenever the loop is quiescent.
	private static long _postsQueued;
	private static long _postsRun;
	private static long _postsRejected;
	private static long _handlesAllocated;
	private static long _handlesFreed;

	// Serializes Post's check-and-enqueue against Shutdown, so no trampoline reaches the shim
	// after teardown is requested (postEvent is FIFO, so earlier work still runs first).
	private static readonly object _postSync = new();

	/// <summary>Raised on the Qt thread for every pointer event (kind as in sailfish_host.h).</summary>
	public static event Action<int, double, double, double, int>? PointerInput;

	/// <summary>Raised on the Qt thread for every key event (kind, Qt::Key, modifiers, text).</summary>
	public static event Action<int, int, int, string>? KeyInput;

	/// <summary>Raised on the Qt thread for QML <c>mauiNotify(name, payload)</c> events; payload is usually JSON.</summary>
	public static event Action<string, string>? QmlEvent;

	/// <summary>Whether the native host loop is currently running.</summary>
	public static bool IsRunning { get => _running || TestShim is not null; private set => _running = value; }

	private static bool _running;

	/// <summary>In-memory shim for host-side tests; when set, every native call goes to it instead of Qt.</summary>
	internal static IQtHostShim? TestShim { get; set; }

	/// <summary>Property sets, property batches and geometry batches sent to the shim (Qt thread only).</summary>
	internal static long Mutations { get; private set; }

	/// <summary>The single-property share of <see cref="Mutations"/> (the collection bridge's row pushes).</summary>
	internal static long PropertySets { get; private set; }

	/// <summary>The geometry share of <see cref="Mutations"/>.</summary>
	internal static long GeometryBatches { get; private set; }

	/// <summary>
	/// True on the thread pumping the Qt loop, the only one allowed to touch QML.
	/// Also true before <see cref="Run"/>, since startup runs on the future loop thread.
	/// </summary>
	public static bool IsQtThread =>
		TestShim is not null || _loopThreadId < 0 || _loopThreadId == Environment.CurrentManagedThreadId;

	private static int _loopThreadId = -1;
	private static int _offThreadCalls;

	/// <summary>Shim calls made off the Qt thread; must stay 0, since they silently corrupt the QV4 heap.</summary>
	public static int OffThreadCalls => Volatile.Read(ref _offThreadCalls);

	/// <summary>MAUI_SAILFISH_STRICT_THREAD=0 only logs an off-thread shim call instead of throwing (kept for one
	/// release while apps move their calls onto the loop).</summary>
	private static readonly bool StrictThread = Environment.GetEnvironmentVariable("MAUI_SAILFISH_STRICT_THREAD") != "0";

	/// <summary>A shim call off the Qt thread corrupts the QV4 heap at some later point, so it fails here, at the call,
	/// as Android's CalledFromWrongThreadException does. Callers hop first (<see cref="RunOnQtThread"/>,
	/// MainThread.BeginInvokeOnMainThread); the Essentials hop for the app (QtThread).</summary>
	private static void CheckThread(string op)
	{
		if (IsQtThread)
			return;
		var n = Interlocked.Increment(ref _offThreadCalls);
		if (n <= 20)
			QtHostDiag.Error(QtHostDiagChannel.QtHost,
				$"OFF-THREAD shim call '{op}' #{n} on managed thread {Environment.CurrentManagedThreadId} (Qt thread {_loopThreadId}):\n{Environment.StackTrace}");
		if (StrictThread)
			throw new InvalidOperationException(
				$"Qt shim call '{op}' from managed thread {Environment.CurrentManagedThreadId}, not the Qt thread: hop with " +
				"QtHostRuntime.RunOnQtThread or MainThread.BeginInvokeOnMainThread (MAUI_SAILFISH_STRICT_THREAD=0 only logs it).");
	}

	// Longest the event-driven loop sleeps; a safety net for a missed wake.
	private const int HeartbeatMs = 1000;

	/// <summary>
	/// Boots the native host, loads the QML shell and runs the Qt event loop on the calling thread.
	/// </summary>
	/// <param name="dispatcher">Dispatcher created on the calling (main) thread.</param>
	/// <param name="qmlPath">Absolute path of the root QML.</param>
	/// <param name="windowMode">true = Silica ApplicationWindow + PageStack; false = plain QQuickView.</param>
	/// <param name="propsJson">Optional JSON object with root context properties (window mode only).</param>
	/// <param name="tickMs">Tick interval of the pump timer.</param>
	/// <returns>exec return code (0 = clean loop exit).</returns>
	internal static int Run(SailfishDispatcher dispatcher, string qmlPath, bool windowMode = true, string? propsJson = null, int tickMs = 16)
	{
		ArgumentNullException.ThrowIfNull(dispatcher);
		ArgumentException.ThrowIfNullOrEmpty(qmlPath);

		_loopThreadId = Environment.CurrentManagedThreadId;

		CheckAbi();
		var rc = QtHostNative.sailfish_host_init(ResolveAppId(), null, IntPtr.Zero);
		if (rc != 0)
			throw BootFailed(rc, "sailfish_host_init");
		Action[] early;
		lock (_postSync)
		{
			_hostInitialized = true;
			early = _prestartPosts.ToArray();
			_prestartPosts.Clear();
		}
		foreach (var action in early)
			Post(action);   // they run once the loop starts, after the shell has loaded

		QtHostDiag.Trace(QtHostDiagChannel.QtHost, $"init ok — loading '{qmlPath}' (windowMode={windowMode})");
		rc = windowMode
			? QtHostNative.sailfish_host_load_window(qmlPath, propsJson)
			: QtHostNative.sailfish_host_load(qmlPath);
		if (rc != 0)
			throw BootFailed(rc, windowMode ? "sailfish_host_load_window" : "sailfish_host_load", qmlPath);

		rc = QtHostNative.sailfish_host_show();
		if (rc != 0)
			throw BootFailed(rc, "sailfish_host_show");

		// Input callbacks run inside Qt's event delivery: an exception must not unwind into native code, so it goes to
		// SailfishExceptions (which ends the app from a thread of its own, unless the app handles it).
		_pointer = (kind, x, y, delta, extra, _) =>
		{
			try { PointerInput?.Invoke(kind, x, y, delta, extra); }
			catch (Exception ex) { SailfishExceptions.Report(ex, "pointer input"); }
		};
		_key = (kind, key, mods, text, _) =>
		{
			try
			{
				KeyInput?.Invoke(kind, key, mods, text == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(text) ?? string.Empty);
			}
			catch (Exception ex) { SailfishExceptions.Report(ex, "key input"); }
		};
		QtHostNative.sailfish_host_set_input_callbacks(_pointer, _key, IntPtr.Zero);

		// Runs inside the shim's drain timer: an exception from a subscriber (RaiseQuitting, an app's own handler) would
		// unwind into native code and fail fast, so it is logged like the pointer/key/post callbacks.
		_event = (name, payload, _) =>
		{
			try
			{
				QmlEvent?.Invoke(name == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(name) ?? string.Empty,
					payload == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(payload) ?? string.Empty);
			}
			catch (Exception ex) { SailfishExceptions.Report(ex, "a QML event handler"); }
		};
		QtHostNative.sailfish_host_set_event_callback(_event, IntPtr.Zero);

		// Runs on the dispatcher's own thread; the shim ticks only when woken, not at a fixed rate.
		_tick = _ =>
		{
			dispatcher.DrainQueue();
			var now = DateTime.UtcNow;
			try { SailfishRuntime.TickDueTimers(now); }
			catch (Exception ex) { SailfishExceptions.Report(ex, "a timer callback"); }
			var next = dispatcher.HasPendingWork ? 0 : SailfishRuntime.NextTimerDelayMs(DateTime.UtcNow);
			QtHostNative.sailfish_host_wake(next >= 0 ? Math.Min(next, HeartbeatMs) : HeartbeatMs);
		};

		SailfishRuntime.WakeHook = static ms => QtHostNative.sailfish_host_wake(ms);
		IsRunning = true;
		try
		{
			return QtHostNative.sailfish_host_exec(_tick, IntPtr.Zero, tickMs);
		}
		finally
		{
			IsRunning = false;
			SailfishRuntime.WakeHook = null;
			dispatcher.Close();   // nothing drains its queue any more
		}
	}

	/// <summary>Runs <paramref name="action"/> now when on the Qt thread, otherwise queues it there.</summary>
	public static void RunOnQtThread(Action action)
	{
		if (IsQtThread)
			action();
		else
			Post(action);
	}

	private static bool _hostInitialized;
	private static readonly List<Action> _prestartPosts = new();

	/// <summary>Queues <paramref name="action"/> onto the Qt thread; safe from any thread. Work posted before the
	/// host exists (app startup, OnLaunched) is held and sent once it does, instead of being refused.</summary>
	public static void Post(Action action)
	{
		ArgumentNullException.ThrowIfNull(action);
		if (TestShim is { } shim)
		{
			shim.Post(action);
			return;
		}
		lock (_postSync)
		{
			if (Volatile.Read(ref _shutdownRequested) != 0)
				return; // host is shutting down
			if (!_hostInitialized)
			{
				_prestartPosts.Add(action);
				return;
			}
			var handle = GCHandle.Alloc(action);
			Interlocked.Increment(ref _handlesAllocated);
			if (QtHostNative.sailfish_host_post(_postThunk, GCHandle.ToIntPtr(handle)) != 0)
			{
				// Native refused (teardown started); free the handle or it leaks.
				handle.Free();
				Interlocked.Increment(ref _handlesFreed);
				Interlocked.Increment(ref _postsRejected);
				return;
			}
			Interlocked.Increment(ref _postsQueued);
		}
	}

	/// <summary>
	/// Idempotent shutdown: tears down the QML layer and exits the loop so <see cref="Run"/> returns 0.
	/// After the first call <see cref="Post"/> drops new work.
	/// </summary>
	public static void Shutdown()
	{
		if (Interlocked.Exchange(ref _shutdownRequested, 1) != 0)
			return;
		// Barrier: wait for any in-flight Post to finish enqueueing before requesting teardown.
		lock (_postSync)
		{
		}
		QtHostNative.sailfish_host_quit();
	}

	/// <summary>Alias of <see cref="Shutdown"/> — asks the native loop to exit (safe from any thread).</summary>
	public static void Quit() => Shutdown();

	/// <summary>Injects a synthetic pointer event (0=press, 1=release, 2=move) into the host window.</summary>
	public static void InjectPointer(int kind, double x, double y)
	{
		CheckThread("inject_pointer");
		QtHostNative.sailfish_host_inject_pointer(kind, x, y);
	}

	/// <summary>
	/// Injects a synthetic key event (kind 0=press, 1=release) as a real QPA event, so it reaches
	/// the focused item. Qt thread only.
	/// </summary>
	public static void InjectKey(int kind, int key, int modifiers = 0, string? text = null)
	{
		CheckThread("inject_key");
		QtHostNative.sailfish_host_inject_key(kind, key, modifiers, text);
	}

	/// <summary>Pushes a QML page onto the Silica PageStack. The path must be absolute, since PageStack resolves relative URLs from the JS call frame.</summary>
	public static int PushPage(string qmlPath, string? propsJson = null, bool immediate = true)
	{
		if (TestShim is { } shim)
			return shim.PushPage(qmlPath, propsJson);
		CheckThread("push_page");
		return QtHostNative.sailfish_host_push_page(qmlPath, propsJson, immediate ? 1 : 0);
	}

	/// <summary>Pops the top page from the Silica PageStack (Qt thread only).</summary>
	public static int PopPage(bool immediate = true)
	{
		if (TestShim is { } shim)
			return shim.PopPage();
		CheckThread("pop_page");
		return QtHostNative.sailfish_host_pop_page(immediate ? 1 : 0);
	}

	/// <summary>Evaluates a JS expression in the QML root context and returns the result as text.</summary>
	public static string Eval(string expression)
	{
		if (TestShim is { } shim)
			return shim.Eval(expression);
		const int cap = 8192;
		var buf = Marshal.AllocHGlobal(cap);
		try
		{
			CheckThread("eval");
			var len = QtHostNative.sailfish_host_eval(expression, buf, cap);
			return len < 0 ? string.Empty : len >= cap ? WholeResult(len) : Marshal.PtrToStringUTF8(buf) ?? string.Empty;
		}
		finally
		{
			Marshal.FreeHGlobal(buf);
		}
	}

	/// <summary>A result longer than the call's buffer (a contacts dump, a file list): the shim kept it whole, fetched
	/// once with a buffer of its length instead of evaluating again (the call may have side effects).</summary>
	private static string WholeResult(int length)
	{
		var buf = Marshal.AllocHGlobal(length + 1);
		try
		{
			var kept = QtHostNative.sailfish_host_last_result(buf, length + 1);
			if (kept != length)
				QtHostDiag.Warn(QtHostDiagChannel.QtHost, $"result of {length} bytes: the shim kept {kept}");
			return Marshal.PtrToStringUTF8(buf) ?? string.Empty;
		}
		finally
		{
			Marshal.FreeHGlobal(buf);
		}
	}

	/// <summary>
	/// Calls a QML function on the object behind <paramref name="handle"/> without compiling JS (sailfish_host_invoke):
	/// <paramref name="method"/>(<paramref name="arg"/>), or method() when arg is null. Returns the result's string
	/// form, or null with <paramref name="rc"/> negative (no such method, dead handle). Qt thread.
	/// </summary>
	internal static string? Invoke(long handle, string method, string? arg, out int rc)
	{
		if (TestShim is { } shim)
			return shim.Invoke(handle, method, arg, out rc);
		CheckThread("invoke");
		const int cap = 8192;
		var buf = Marshal.AllocHGlobal(cap);
		try
		{
			rc = QtHostNative.sailfish_host_invoke(handle, method, arg, buf, cap);
			if (rc < 0)
				return null;
			return rc >= cap ? WholeResult(rc) : Marshal.PtrToStringUTF8(buf) ?? string.Empty;
		}
		finally
		{
			Marshal.FreeHGlobal(buf);
		}
	}

	/// <summary>Registers an app font file with Qt and returns its family name (null if rejected).</summary>
	public static string? RegisterFont(string path)
	{
		if (TestShim is not null)
			return null;
		CheckThread("register_font");
		const int cap = 512;
		var buf = Marshal.AllocHGlobal(cap);
		try
		{
			var len = QtHostNative.sailfish_host_register_font(path, buf, cap);
			return len <= 0 ? null : Marshal.PtrToStringUTF8(buf);
		}
		finally
		{
			Marshal.FreeHGlobal(buf);
		}
	}

	/// <summary>Renders a glyph (FontImageSource) to a PNG file.</summary>
	public static bool RenderGlyph(string family, string text, double px, string color, string outPath)
	{
		if (TestShim is not null)
			return false;
		CheckThread("render_glyph");
		return QtHostNative.sailfish_host_render_glyph(family, text, px, color, outPath) == 0;
	}

	// --- Drawing surfaces (Qt thread only; see QtHostSurface) ---

	private static QtHostNative.FrameFn? _frameFn;

	internal static int SurfaceCommit(long handle, IntPtr pixels, int width, int height, int stride)
	{
		if (TestShim is { } shim)
			return shim.SurfaceCommit(handle, pixels, width, height, stride);
		CheckThread("surface_commit");
		return QtHostNative.sailfish_host_surface_commit(handle, pixels, width, height, stride);
	}

	private static QtHostNative.SurfaceTouchFn? _surfaceTouchFn;

	internal static int SurfaceSetTouch(long handle, bool enabled, QtHostNative.SurfaceTouchFn onTouch)
	{
		if (TestShim is { } shim)
			return shim.SurfaceSetTouch(handle, enabled);
		CheckThread("surface_set_touch");
		if (_surfaceTouchFn is null)
		{
			_surfaceTouchFn = onTouch;
			QtHostNative.sailfish_host_set_surface_touch_callback(_surfaceTouchFn, IntPtr.Zero);
		}
		return QtHostNative.sailfish_host_surface_set_touch(handle, enabled ? 1 : 0);
	}

	/// <summary>Diagnostics: one multi-touch event through Qt's real input path (window pixels; states are
	/// Qt::TouchPointState: 1 pressed, 2 moved, 4 stationary, 8 released). Qt thread.</summary>
	public static void InjectTouch(int[] ids, double[] xy, int[] states)
	{
		if (TestShim is not null)
			return;
		CheckThread("inject_touch");
		QtHostNative.sailfish_host_inject_touch(ids.Length, ids, xy, states);
	}

	/// <summary>Asks the shim for one frame callback; <paramref name="onFrame"/> is installed once and kept alive.</summary>
	internal static void RequestFrame(QtHostNative.FrameFn onFrame)
	{
		if (TestShim is { } shim)
		{
			shim.RequestFrame();
			return;
		}
		CheckThread("request_frame");
		if (_frameFn is null)
		{
			_frameFn = onFrame;
			QtHostNative.sailfish_host_set_frame_callback(_frameFn, IntPtr.Zero);
		}
		QtHostNative.sailfish_host_request_frame();
	}

	/// <summary>Writes the current scene to a PNG on the device (Qt thread only).</summary>
	public static int GrabPng(string path)
	{
		if (TestShim is not null)
			return -1;
		CheckThread("grab_png");
		return QtHostNative.sailfish_host_grab_png(path);
	}

	/// <summary>Writes the window, or the part at <paramref name="sceneRect"/> (scene units), to <paramref name="path"/>:
	/// JPEG for a .jpg path (<paramref name="quality"/> 0..100), else PNG. 0 on success (Qt thread only).</summary>
	public static int GrabImage(string path, NativeGeometry? sceneRect = null, int quality = -1)
	{
		if (TestShim is not null)
			return -1;
		CheckThread("grab_image");
		var r = sceneRect ?? default;
		return QtHostNative.sailfish_host_grab_image(path, r.X, r.Y, r.Width, r.Height, quality);
	}

	/// <summary>Re-encodes an image file (format by <paramref name="dst"/>'s extension). 0 on success.</summary>
	public static int ConvertImage(string src, string dst, int quality = -1)
	{
		if (TestShim is not null)
			return -1;
		return QtHostNative.sailfish_host_convert_image(src, dst, quality);
	}

	/// <summary>Starts recording the window's frames as <c>&lt;ms&gt;.jpg</c> files in <paramref name="dir"/>.</summary>
	public static int RecordStart(string dir, int fps = 15, int scalePct = 70)
	{
		if (TestShim is not null)
			return -1;
		CheckThread("record_start");
		return QtHostNative.sailfish_host_record_start(dir, fps, scalePct);
	}

	/// <summary>Stops the recorder; returns the number of frames written.</summary>
	public static int RecordStop()
	{
		if (TestShim is not null)
			return 0;
		CheckThread("record_stop");
		return QtHostNative.sailfish_host_record_stop();
	}

	// --- Persistent native objects (Qt thread only) ---

	/// <summary>
	/// Resolves an object under the QML root by objectName and returns a native handle (0 = not found).
	/// If QML destroys the object first, the handle turns dead (error -3) instead of dangling.
	/// </summary>
	public static long FindObject(string objectName)
	{
		if (TestShim is { } shim)
			return shim.FindObject(objectName);
		CheckThread("find_object");
		return QtHostNative.sailfish_host_find_object(objectName);
	}

	/// <summary>
	/// A host's QML object by name, in the one documented order (W3.4: four lookups used three orders): inside
	/// <paramref name="scopeHandle"/>'s visual tree first, since a delegate Qt released keeps the global name until it is
	/// deleted (its content would die with it); then the QObject tree by name (a parked page's items hang outside the
	/// visual tree); then the whole visual tree (ListView delegate content is visually parented only). 0 = not found.
	/// </summary>
	public static long FindScoped(string objectName, long scopeHandle)
	{
		var handle = scopeHandle != 0 ? FindVisual(scopeHandle, objectName) : 0;
		if (handle == 0)
			handle = FindObject(objectName);
		if (handle == 0)
			handle = FindVisual(0, objectName);
		return handle;
	}

	/// <summary>
	/// Visual-tree lookup from a parent handle (0 = QML root). Needed because Qt 5.6 ListView delegates
	/// are only visually parented, so <see cref="FindObject"/> never finds them.
	/// </summary>
	public static long FindVisual(long parentHandle, string objectName)
	{
		if (TestShim is { } shim)
			return shim.FindObject(objectName);
		CheckThread("find_visual");
		return QtHostNative.sailfish_host_find_visual(parentHandle, objectName);
	}

	/// <summary>
	/// Sets a property in place from a JSON value, converted to the property's type by the shim.
	/// Returns 0 ok, -1 args/conversion, -2 unknown property, -3 dead handle.
	/// </summary>
	public static int SetProperty(long handle, string name, string? valueJson)
	{
		Mutations++;
		PropertySets++;
		if (TestShim is { } shim)
			return shim.SetProperty(handle, name, valueJson);
		CheckThread("set_property");
		return QtHostNative.sailfish_host_set_property(handle, name, valueJson);
	}

	/// <summary>
	/// Applies an ordered batch <c>[{"name":…,"value":…}, …]</c>; order matters because the
	/// mauiApplying envelope rides in it. Returns the count not applied, -1 bad JSON, -3 dead handle.
	/// </summary>
	public static int ApplyProperties(long handle, string propsJson)
	{
		Mutations++;
		if (TestShim is { } shim)
			return shim.ApplyProperties(handle, propsJson);
		CheckThread("apply_props");
		return QtHostNative.sailfish_host_apply_props(handle, propsJson);
	}

	/// <summary>The shim's last error text.</summary>
	public static string LastErrorText => LastError();

	/// <summary>Reads a property of the native object as text (empty on error).</summary>
	public static string GetProperty(long handle, string name)
	{
		if (TestShim is { } shim)
			return shim.GetProperty(handle, name);
		const int cap = 4096;
		var buf = Marshal.AllocHGlobal(cap);
		try
		{
			CheckThread("get_property");
			var len = QtHostNative.sailfish_host_get_property(handle, name, buf, cap);
			return len < 0 ? string.Empty : Marshal.PtrToStringUTF8(buf) ?? string.Empty;
		}
		finally
		{
			Marshal.FreeHGlobal(buf);
		}
	}

	/// <summary>Reads the scene geometry of a native QQuickItem (false on dead/invalid handle).</summary>
	public static bool TryItemGeometry(long handle, out NativeGeometry geometry)
	{
		if (TestShim is { } shim)
			return shim.TryItemGeometry(handle, out geometry);
		CheckThread("item_geometry");
		var rc = QtHostNative.sailfish_host_item_geometry(handle, out var x, out var y, out var w, out var h);
		geometry = rc == 0 ? new NativeGeometry(x, y, w, h) : default;
		return rc == 0;
	}

	/// <summary>Visually re-parents a QQuickItem onto <paramref name="parent"/> (false on dead handles).</summary>
	public static bool SetParentItem(long handle, long parent)
	{
		if (TestShim is { } shim)
			return shim.SetParentItem(handle, parent);
		CheckThread("set_parent_item");
		return QtHostNative.sailfish_host_set_parent_item(handle, parent) == 0;
	}

	/// <summary>
	/// Applies a geometry batch already converted to Qt scene units by <see cref="QtHostUnits"/>;
	/// the shim never scales again. Returns the count not applied, or -1 for bad JSON.
	/// </summary>
	public static int ApplyGeometry(string geoJson)
	{
		Mutations++;
		GeometryBatches++;
		if (TestShim is { } shim)
			return shim.ApplyGeometry(geoJson);
		CheckThread("apply_geometry");
		return QtHostNative.sailfish_host_apply_geometry(geoJson);
	}

	/// <summary>
	/// Measures text with the same QFontMetrics QML renders with. Input and output are in device pixels.
	/// </summary>
	public static bool TryMeasureText(string json, out double widthPx, out double heightPx)
	{
		if (TestShim is { } shim)
			return shim.TryMeasureText(json, out widthPx, out heightPx);
		CheckThread("measure_text");
		var rc = QtHostNative.sailfish_host_measure_text(json, out widthPx, out heightPx);
		return rc == 0;
	}

	/// <summary>The decoded size of encoded image bytes (QImageReader, EXIF orientation applied). Any thread: QImage
	/// needs no Qt loop.</summary>
	public static bool TryImageInfo(byte[] data, out int width, out int height)
	{
		if (TestShim is { } shim)
			return shim.TryImageInfo(data, out width, out height);
		return QtHostNative.sailfish_host_image_info(data, data.Length, out width, out height) == 0;
	}

	/// <summary>Decodes, transforms and re-encodes image bytes with QImage (op JSON {w,h,mode,format,quality}); null on
	/// error (<see cref="LastErrorText"/> says why). Any thread.</summary>
	public static byte[]? ImageTransform(byte[] data, string opJson, int widthHint, int heightHint)
	{
		if (TestShim is { } shim)
			return shim.ImageTransform(data, opJson);
		// Uncompressed pixels plus headers fit any format QImage writes; PNG and JPEG come out far smaller.
		var cap = (int)Math.Min(int.MaxValue - 64, (long)Math.Max(1, widthHint) * Math.Max(1, heightHint) * 4 + 65536);
		var buffer = new byte[cap];
		var len = QtHostNative.sailfish_host_image_transform(data, data.Length, opJson, buffer, cap);
		if (len > cap)
		{
			buffer = new byte[len];
			len = QtHostNative.sailfish_host_image_transform(data, data.Length, opJson, buffer, len);
		}
		if (len < 0 || len > buffer.Length)
			return null;
		Array.Resize(ref buffer, len);
		return buffer;
	}

	/// <summary>Window and screen geometry as JSON in device pixels (empty on error).</summary>
	public static string ScreenInfo()
	{
		if (TestShim is { } shim)
			return shim.ScreenInfo();
		const int cap = 2048;
		var buf = Marshal.AllocHGlobal(cap);
		try
		{
			var len = QtHostNative.sailfish_host_screen_info(buf, cap);
			return len <= 0 ? string.Empty : Marshal.PtrToStringUTF8(buf) ?? string.Empty;
		}
		finally
		{
			Marshal.FreeHGlobal(buf);
		}
	}

	/// <summary>Destroys a native object that left the MAUI tree; no-op for dead handles. Drop the handle afterwards.</summary>
	public static void DestroyObject(long handle)
	{
		if (TestShim is { } shim)
		{
			shim.DestroyObject(handle);
			return;
		}
		CheckThread("destroy_object");
		QtHostNative.sailfish_host_destroy_object(handle);
	}

	/// <summary>Number of ticks delivered by the native loop so far.</summary>
	public static long TickCount => QtHostNative.sailfish_host_tick_count();

	// --- Post / GCHandle counters ---

	/// <summary>Posts accepted by the native shim.</summary>
	public static long PostsQueued => Interlocked.Read(ref _postsQueued);

	/// <summary>Posts whose callback ran on the Qt thread.</summary>
	public static long PostsRun => Interlocked.Read(ref _postsRun);

	/// <summary>Posts refused by the shim (teardown already began; trampoline freed).</summary>
	public static long PostsRejected => Interlocked.Read(ref _postsRejected);

	/// <summary>GCHandle trampolines allocated by <see cref="Post"/>.</summary>
	public static long HandlesAllocated => Interlocked.Read(ref _handlesAllocated);

	/// <summary>GCHandle trampolines freed; equals <see cref="HandlesAllocated"/> whenever quiescent.</summary>
	public static long HandlesFreed => Interlocked.Read(ref _handlesFreed);

	/// <summary>
	/// Simulates background/resume by injecting QPA app-state events (0=Suspended, 1=Hidden,
	/// 2=Inactive, 4=Active; -1 = unchanged) without the compositor.
	/// </summary>
	public static bool DiagSetAppState(int state, int activate) =>
		QtHostNative.sailfish_host_diag_app_state(state, activate) == 0;

	/// <summary>
	/// Shim counters as JSON (empty on error). The registry count is timing-dependent,
	/// so it is not a reliable leak signal on its own.
	/// </summary>
	public static string DiagStats()
	{
		const int cap = 512;
		var buf = Marshal.AllocHGlobal(cap);
		try
		{
			var len = QtHostNative.sailfish_host_diag_stats(buf, cap);
			return len <= 0 ? string.Empty : Marshal.PtrToStringUTF8(buf) ?? string.Empty;
		}
		finally
		{
			Marshal.FreeHGlobal(buf);
		}
	}

	/// <summary>Shim performance report as JSON (see sailfish_host_perf_stats); empty on error.</summary>
	public static string PerfStats()
	{
		const int cap = 2048;
		var buf = Marshal.AllocHGlobal(cap);
		try
		{
			var len = QtHostNative.sailfish_host_perf_stats(buf, cap);
			return len <= 0 ? string.Empty : Marshal.PtrToStringUTF8(buf) ?? string.Empty;
		}
		finally
		{
			Marshal.FreeHGlobal(buf);
		}
	}

	private static void PostThunk(IntPtr userData)
	{
		Interlocked.Increment(ref _postsRun);
		var handle = GCHandle.FromIntPtr(userData);
		try
		{
			((Action)handle.Target!).Invoke();
		}
		catch (Exception ex)
		{
			// Same policy as SailfishDispatcher.DrainQueue: an exception must not unwind into PostReceiver::event,
			// where the runtime fails fast; it goes to the device log instead.
			QtHostDiag.Error(QtHostDiagChannel.QtHost, $"unhandled exception in posted work: {ex}");
		}
		finally
		{
			handle.Free();
			Interlocked.Increment(ref _handlesFreed);
		}
	}

	private static string LastError()
	{
		if (TestShim is not null)
			return "(test shim)";
		var buf = Marshal.AllocHGlobal(512);
		try
		{
			var len = QtHostNative.sailfish_host_last_error(buf, 512);
			return len <= 0 ? "(no error)" : Marshal.PtrToStringUTF8(buf, Math.Min(len, 511)) ?? "(no error)";
		}
		finally
		{
			Marshal.FreeHGlobal(buf);
		}
	}

	/// <summary>
	/// App id for the Qt applicationName, which becomes the Wayland app_id; lipstick matches it
	/// to the .desktop entry for the launcher icon. Order: <c>MAUI_SAILFISH_APP_ID</c>, the
	/// installed package directory name, then "maui-sailfish".
	/// </summary>
	internal static string ResolveAppId()
	{
		var fromEnv = SailfishEnv.Get("MAUI_SAILFISH_APP_ID");
		if (!string.IsNullOrWhiteSpace(fromEnv))
			return fromEnv.Trim();

		try
		{
			var dir = Environment.ProcessPath is { } exe ? Path.GetDirectoryName(exe) : null;
			if (!string.IsNullOrEmpty(dir))
			{
				var pkg = Path.GetFileName(dir);
				if (File.Exists(Path.Combine("/usr/share/applications", pkg + ".desktop")))
					return pkg;
			}
		}
		catch
		{
			// Best effort — a probing failure must never block boot.
		}

		return "maui-sailfish";
	}

	// Logs before returning, so the channel counters record the failure even if the caller swallows it.
	/// <summary>The installed libsailfishhost.so must be the one this build was made with: an older or newer shim
	/// fails here, at start, instead of losing features silently later.</summary>
	private static void CheckAbi()
	{
		int abi;
		try
		{
			abi = QtHostNative.sailfish_host_abi_version();
		}
		catch (EntryPointNotFoundException)
		{
			abi = -1;   // a shim older than the version symbol
		}
		if (abi == QtHostNative.AbiVersion)
			return;
		var message = $"libsailfishhost.so ABI {(abi < 0 ? "unversioned (older than 2)" : abi.ToString(System.Globalization.CultureInfo.InvariantCulture))}, " +
		              $"this build needs {QtHostNative.AbiVersion}: the installed shim does not match the managed code (redeploy the app)";
		QtHostDiag.Error(QtHostDiagChannel.QtHost, $"sailfish_host_abi_version failed: {message}");
		throw new QtHostException(abi, "sailfish_host_abi_version", message, null);
	}

	private static QtHostException BootFailed(int rc, string operation, string? qmlPath = null)
	{
		var nativeMessage = LastError();
		QtHostDiag.Error(QtHostDiagChannel.QtHost,
			$"{operation} failed: native code {rc}" +
			$"{(qmlPath is null ? string.Empty : $", qml '{qmlPath}'")}: {nativeMessage}");
		return new QtHostException(rc, operation, nativeMessage, qmlPath);
	}
}
