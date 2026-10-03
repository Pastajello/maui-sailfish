using Microsoft.Maui.Animations;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// The animation ticker, on Qt's frame clock as Android's ticker rides the Choreographer and iOS's CADisplayLink: each
/// tick runs on the Qt thread right after Qt animated a frame (<see cref="QtHostSurface.RequestFrame"/>), and only
/// while an animation runs. MAUI's plain-net ticker fired from a thread-pool timer every 16 ms, unrelated to the frames,
/// and every tick had to hop to the Qt thread.
/// </summary>
internal sealed class SailfishFrameTicker : Ticker
{
	private readonly Action _onFrame;
	private bool _running;

	public SailfishFrameTicker() => _onFrame = OnFrame;

	public override bool IsRunning => _running;

	/// <summary>Ticks fired (diagnostics).</summary>
	public static long Ticks { get; private set; }

	public override void Start()
	{
		if (_running)
			return;
		_running = true;
		QtThread.Post(() => QtHostSurface.RequestFrame(_onFrame));
	}

	public override void Stop() => _running = false;

	private void OnFrame()
	{
		if (!_running)
			return;
		Ticks++;
		Fire?.Invoke();
		// The manager stops the ticker when its last animation ended (inside Fire); otherwise the next frame ticks.
		if (_running)
			QtHostSurface.RequestFrame(_onFrame);
	}
}
