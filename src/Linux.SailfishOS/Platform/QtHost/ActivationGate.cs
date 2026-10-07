namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// The activation half of the renderer (C4, W3.7a): window focus and the application state become MAUI's window
/// lifecycle (Activated/Deactivated/Resumed/Stopped), and host creation waits until the application is stably active,
/// because Silica rebuilds model-page visuals around activation and objects created meanwhile die. The wait is bounded
/// (<see cref="DeferralLimitMs"/>) so a stuck stack sync cannot leave the app blank.
/// </summary>
internal sealed class ActivationGate
{
	/// <summary>Longest wait for a stable activation before hosts are created anyway.</summary>
	internal const int DeferralLimitMs = 10_000;

	// Kinds: 1=Activated 2=Deactivated 3=Resumed 4=Stopped.
	private readonly Action<Action<Microsoft.Maui.IWindow>, string, int> _raise;
	private bool? _lastWindowActive;
	private int _lastAppState = -1;
	private bool _everActive;         // the application was Active once (Stopped needs a started app, as onStop does)
	private bool _stopped;            // Stopped was raised and no Resumed followed yet
	private long _activeSinceMs;      // since when appState == Active (0 = not active)
	private long _deferredSinceMs;    // when the creation wait began (0 = not waiting)
	private int _deferredTicks;       // reconciles spent waiting

	/// <param name="raise">Sends one window lifecycle call to MAUI: (call, description, kind).</param>
	public ActivationGate(Action<Action<Microsoft.Maui.IWindow>, string, int> raise) => _raise = raise;

	/// <summary>Qt.application.state at the last snapshot (-1 before the first).</summary>
	public int LastAppState => _lastAppState;

	/// <summary>Window focus at the last snapshot (null before the first).</summary>
	public bool? LastWindowActive => _lastWindowActive;

	/// <summary>The application has been Active for <see cref="QtHostPageRenderer.ActivationSettleMs"/>.</summary>
	public bool Settled =>
		_activeSinceMs != 0 && Environment.TickCount64 - _activeSinceMs >= QtHostPageRenderer.ActivationSettleMs;

	/// <summary>The creation wait ran out: create the page anyway.</summary>
	public bool DeferralExpired =>
		_deferredSinceMs != 0 && Environment.TickCount64 - _deferredSinceMs >= DeferralLimitMs;

	/// <summary>Creation may go ahead as far as activation is concerned.</summary>
	public bool AllowsCreation => Settled || DeferralExpired;

	/// <summary>
	/// One snapshot of window focus and application state (the navigation state read). Raised as Android raises them:
	/// leaving Active (to the cover, Inactive; Hidden; Suspended) is Deactivated then Stopped (onPause, onStop), coming
	/// back is Resumed then Activated (onRestart, onResume). Resumed only follows a Stopped, and startup raises no
	/// Resumed (Android: Created → Activated). MAUI sent Created itself. An unknown state (-1: Qt.application not
	/// readable in the snapshot) changes nothing.
	/// </summary>
	public void Observe(bool active, int appState)
	{
		// Qt.ApplicationState: Suspended=0, Hidden=1, Inactive=2, Active=4.
		var known = appState != -1;
		if (known && appState == 4 && _stopped)
		{
			_stopped = false;
			_raise(w => w.Resumed(), $"Resumed (appState {_lastAppState}→{appState})", 3);
		}

		if (_lastWindowActive is null)
		{
			if (active)
				_raise(w => w.Activated(), "Activated (window active at startup)", 1);
		}
		else if (active != _lastWindowActive.Value)
		{
			if (active) _raise(w => w.Activated(), "Activated", 1);
			else _raise(w => w.Deactivated(), "Deactivated", 2);
		}
		_lastWindowActive = active;

		if (known && appState != 4 && _everActive && !_stopped)
		{
			_stopped = true;
			_raise(w => w.Stopped(), $"Stopped (appState {_lastAppState}→{appState})", 4);
		}
		if (known && appState == 4)
			_everActive = true;
		if (known)
			_lastAppState = appState;
		// Host creation waits for a stable Active state: objects created in the activation rebuild die.
		if (appState != 4)
			_activeSinceMs = 0;
		else if (_activeSinceMs == 0)
			_activeSinceMs = Environment.TickCount64;
	}

	/// <summary>The app quits (home-screen close, Application.Quit): what Android sends before onDestroy, once each and
	/// only what is still owed: Deactivated while the window is active, Stopped unless already stopped.</summary>
	public void Quit()
	{
		if (_lastWindowActive == true)
		{
			_lastWindowActive = false;
			_raise(w => w.Deactivated(), "Deactivated (quit)", 2);
		}
		if (_everActive && !_stopped)
		{
			_stopped = true;
			_raise(w => w.Stopped(), "Stopped (quit)", 4);
		}
	}

	/// <summary>A reconcile that has to wait: counts it, starts the clock on the first one (true then, to log the
	/// start), and gives the delay after which the gate may have opened (<paramref name="recheckMs"/>).</summary>
	public bool Wait(out long recheckMs)
	{
		_deferredTicks++;
		var started = _deferredSinceMs == 0;
		if (started)
			_deferredSinceMs = Environment.TickCount64;
		// Come back when the gate opens (the app-state event covers the not-yet-active case).
		recheckMs = _activeSinceMs != 0
			? QtHostPageRenderer.ActivationSettleMs - (Environment.TickCount64 - _activeSinceMs)
			: DeferralLimitMs - (Environment.TickCount64 - _deferredSinceMs);
		return started;
	}

	/// <summary>The gate is open: ends a wait. True when there was one, with how long it took.</summary>
	public bool EndWait(out int ticks, out long waitedMs)
	{
		ticks = _deferredTicks;
		waitedMs = _deferredSinceMs == 0 ? 0 : Environment.TickCount64 - _deferredSinceMs;
		_deferredTicks = 0;
		_deferredSinceMs = 0;
		return ticks > 0;
	}
}
