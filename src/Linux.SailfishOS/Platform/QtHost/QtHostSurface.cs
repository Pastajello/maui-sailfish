namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// A natively hosted drawing surface: managed code draws pixels and shows them inside a host's QML item, which the
/// surface fills. It knows nothing about the drawing library; SkiaSharp's <c>SKCanvasView</c> handler is one user,
/// and any control that renders its own pixels can be another. Qt thread only.
/// </summary>
/// <remarks>
/// <para>Pixels are RGBA8888 with premultiplied alpha (SkiaSharp's <c>SKColorType.Rgba8888</c> +
/// <c>SKAlphaType.Premul</c>). A commit copies them once, so the caller may reuse its buffer at once; the shim
/// uploads the copy into a persistent texture on the render thread.</para>
/// <para>The surface is created by the first commit and dies with the host's QML object. When the host is
/// re-created (navigation, a recycled collection row) the new object shows nothing until the next commit.</para>
/// </remarks>
public static class QtHostSurface
{
	/// <summary>The generic adapter a drawing surface's handler names (qml/adapters.json): an empty item the surface
	/// fills.</summary>
	public const string AdapterUri = "surface";

	private static readonly Dictionary<NativeElementHost, TouchTarget> TouchByHost = new();
	private static readonly Dictionary<long, TouchTarget> TouchByHandle = new();
	private static QtHostNative.SurfaceTouchFn? _touch;   // kept alive for the process lifetime

	private sealed class TouchTarget(NativeElementHost host, Func<SurfaceTouch, bool> handler)
	{
		public NativeElementHost Host { get; } = host;
		public Func<SurfaceTouch, bool> Handler { get; set; } = handler;
		public long Handle { get; set; }
		public Dictionary<int, int> Ids { get; } = new();   // Qt touch point id → pointer id
	}

	private static readonly List<Action> Pending = new();
	private static readonly HashSet<Action> PendingSet = new();
	private static readonly List<Action> _running = new();
	private static QtHostNative.FrameFn? _frame;   // kept alive for the process lifetime

	/// <summary>Frames that ran at least one callback.</summary>
	internal static long Frames { get; private set; }

	/// <summary>Commits that reached the shim.</summary>
	internal static long Commits { get; private set; }

	/// <summary>Shows <paramref name="width"/> × <paramref name="height"/> pixels in <paramref name="host"/> from the
	/// next frame on. Returns false when the host has no native object (not created yet, or released).</summary>
	/// <param name="rowBytes">Bytes per row in <paramref name="pixels"/>, at least <c>width * 4</c>.</param>
	public static bool Commit(NativeElementHost host, IntPtr pixels, int width, int height, int rowBytes)
	{
		ArgumentNullException.ThrowIfNull(host);
		if (!host.IsAttached || pixels == IntPtr.Zero || width <= 0 || height <= 0)
			return false;
		if (rowBytes < width * 4)
			throw new ArgumentOutOfRangeException(nameof(rowBytes), rowBytes, "less than width * 4");
		Commits++;
		return QtHostRuntime.SurfaceCommit(host.NativeHandle, pixels, width, height, rowBytes) == QtHostRuntime.SfhostOk;
	}

	/// <summary>Frees the pixels the surface of <paramref name="host"/> holds (a hidden or empty canvas).</summary>
	public static void Release(NativeElementHost host)
	{
		ArgumentNullException.ThrowIfNull(host);
		if (host.IsAttached)
			QtHostRuntime.SurfaceCommit(host.NativeHandle, IntPtr.Zero, 0, 0, 0);
	}

	/// <summary>Delivers touch on the surface of <paramref name="host"/> to <paramref name="handler"/> (null turns it
	/// off). The handler runs synchronously during Qt's event delivery; what it returns for the first
	/// <see cref="SurfaceTouchAction.Pressed"/> of a gesture decides whether the surface keeps the gesture (true) or it
	/// goes on to whatever is below and to the parents, as an unhandled <c>ACTION_DOWN</c> on Android. Call it again
	/// after <see cref="NativeElementHost.Attached"/>: a re-created QML object starts with touch off.</summary>
	public static void SetTouch(NativeElementHost host, Func<SurfaceTouch, bool>? handler)
	{
		ArgumentNullException.ThrowIfNull(host);
		if (TouchByHost.TryGetValue(host, out var target))
		{
			TouchByHandle.Remove(target.Handle);
			if (handler is null)
				TouchByHost.Remove(host);
		}
		if (handler is not null)
		{
			target ??= new TouchTarget(host, handler);
			target.Handler = handler;
			target.Handle = host.NativeHandle;
			target.Ids.Clear();
			TouchByHost[host] = target;
			if (host.IsAttached)
				TouchByHandle[host.NativeHandle] = target;
		}
		if (host.IsAttached)
			QtHostRuntime.SurfaceSetTouch(host.NativeHandle, handler is not null, _touch ??= OnNativeTouch);
	}

	private static int OnNativeTouch(long handle, int action, int pointer, double x, double y, double pressure,
		int device, int button, IntPtr userData) =>
		DeliverTouch(handle, (SurfaceTouchAction)action, pointer, x, y, pressure, device == 1, button) ? 1 : 0;

	/// <summary>Routes one native touch to its handler (the shim's callback; tests call it directly).</summary>
	internal static bool DeliverTouch(long handle, SurfaceTouchAction action, int nativeId, double x, double y,
		double pressure, bool mouse, int button)
	{
		if (!TouchByHandle.TryGetValue(handle, out var target))
			return false;
		int id;
		if (mouse)
			id = 0;
		else if (action == SurfaceTouchAction.Pressed)
		{
			// Android hands out the lowest pointer id not in use, so a second finger is 1 and a finger that comes
			// back after a lift gets the free slot again; Qt ids just count up.
			id = 0;
			while (target.Ids.ContainsValue(id))
				id++;
			target.Ids[nativeId] = id;
		}
		else if (!target.Ids.TryGetValue(nativeId, out id))
			return false;
		if (!mouse && action is SurfaceTouchAction.Released or SurfaceTouchAction.Cancelled)
			target.Ids.Remove(nativeId);
		try
		{
			return target.Handler(new SurfaceTouch(action, id, x, y, pressure, mouse, button));
		}
		catch (Exception ex)
		{
			QtHostDiag.Error(QtHostDiagChannel.Input, $"unhandled exception in a surface touch handler: {ex}");
			return false;
		}
	}

	/// <summary>Runs <paramref name="callback"/> once in the next frame, before the scene graph syncs, so a commit
	/// made there shows in that frame. A callback already waiting is not queued twice; one requested while frame
	/// callbacks run waits for the following frame. Nothing runs while the window renders no frames (hidden).</summary>
	public static void RequestFrame(Action callback)
	{
		ArgumentNullException.ThrowIfNull(callback);
		if (PendingSet.Add(callback))
			Pending.Add(callback);
		// Asked every time (the shim coalesces), so one frame that never came cannot stall the queue.
		QtHostRuntime.RequestFrame(_frame ??= _ => RunFrame());
	}

	/// <summary>Whether <paramref name="callback"/> waits for the next frame.</summary>
	public static bool IsFrameRequested(Action callback) => PendingSet.Contains(callback);

	/// <summary>Runs the callbacks queued for this frame (the shim's frame callback; tests call it directly).</summary>
	internal static void RunFrame()
	{
		if (Pending.Count == 0)
			return;
		Frames++;
		// Swapped out first, so callbacks that request the next frame queue for it instead of this one.
		_running.Clear();
		_running.AddRange(Pending);
		Pending.Clear();
		PendingSet.Clear();
		foreach (var callback in _running)
		{
			try
			{
				callback();
			}
			catch (Exception ex)
			{
				// The backend's policy for app code run from the loop (see SailfishDispatcher.DrainQueue): logged, and
				// the loop keeps running.
				QtHostDiag.Error(QtHostDiagChannel.QtHost, $"unhandled exception in a frame callback: {ex}");
			}
		}
	}
}

/// <summary>The phase of a <see cref="SurfaceTouch"/>.</summary>
public enum SurfaceTouchAction
{
	Pressed = 0,
	Moved = 1,
	Released = 2,
	Cancelled = 3,
}

/// <summary>One touch on a drawing surface (<see cref="QtHostSurface.SetTouch"/>).</summary>
/// <param name="PointerId">Android-style pointer id: the lowest free one for touch (0, 1, …), 0 for the mouse.</param>
/// <param name="X">Surface-local, in device pixels.</param>
/// <param name="Y">Surface-local, in device pixels.</param>
/// <param name="IsMouse">A real mouse (Qt's mouse synthesized from touch is never reported).</param>
/// <param name="MouseButton">0 left, 1 middle, 2 right.</param>
public readonly record struct SurfaceTouch(SurfaceTouchAction Action, int PointerId, double X, double Y,
	double Pressure, bool IsMouse, int MouseButton);
