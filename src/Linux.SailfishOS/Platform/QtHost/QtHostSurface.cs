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

	/// <summary>Runs <paramref name="callback"/> once in the next frame, before the scene graph syncs, so a commit
	/// made there shows in that frame. A callback already waiting is not queued twice; one requested while frame
	/// callbacks run waits for the following frame. Nothing runs while the window renders no frames (hidden).</summary>
	public static void RequestFrame(Action callback)
	{
		ArgumentNullException.ThrowIfNull(callback);
		if (!PendingSet.Add(callback))
			return;
		Pending.Add(callback);
		if (Pending.Count == 1)
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
