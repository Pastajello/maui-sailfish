using Microsoft.Maui.SailfishOS.Platform.QtHost;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// MCE (display, touch-screen lock, memory level) as one app-level service on the Nemo QML bindings. It keeps the
/// last state of each and raises a change once; <see cref="SailfishMauiApplication"/> forwards the changes to its
/// overrides and the lifecycle handlers. Without MCE bindings (a desktop Qt, a broken image) the events never come.
/// </summary>
internal sealed class SailfishSystemService
{
	private const string Qml = """
		import QtQuick 2.6
		import Nemo.Mce 1.0
		import Nemo.DBus 2.0
		Item {
		    // valid turns true once MCE answered, after creation: report then and on every change.
		    MceDisplay {
		        id: display
		        function report() { if (valid) window.mauiAppNotify("svc-display", JSON.stringify({ state: display.state })); }
		        onValidChanged: report()
		        onStateChanged: report()
		    }
		    MceTkLock {
		        id: tklock
		        function report() { if (valid) window.mauiAppNotify("svc-screen-lock", JSON.stringify({ locked: tklock.locked })); }
		        onValidChanged: report()
		        onLockedChanged: report()
		    }
		    DBusInterface {
		        id: mce
		        bus: DBus.SystemBus
		        service: "com.nokia.mce"
		        path: "/com/nokia/mce/signal"
		        iface: "com.nokia.mce.signal"
		        signalsEnabled: true
		        function sig_memory_level_ind(level) { window.mauiAppNotify("svc-memory-level", JSON.stringify({ level: level })) }
		    }
		    DBusInterface {
		        id: mceRequest
		        bus: DBus.SystemBus
		        service: "com.nokia.mce"
		        path: "/com/nokia/mce/request"
		        iface: "com.nokia.mce.request"
		    }
		    Component.onCompleted: {
		        display.report();
		        tklock.report();
		        mceRequest.typedCall("get_memory_level", [], function(level) {
		            window.mauiAppNotify("svc-memory-level", JSON.stringify({ level: level }));
		        }, function() {});
		    }
		}
		""";

	/// <summary>The last reported states (null/Unknown until MCE answered).</summary>
	public SailfishDisplayState? DisplayState { get; private set; }
	public bool? ScreenLocked { get; private set; }
	public SailfishMemoryLevel MemoryLevel { get; private set; } = SailfishMemoryLevel.Unknown;

	/// <summary>MCE answered the memory level query (it says "unknown" where memory tracking is off).</summary>
	public bool MemoryLevelAnswered { get; private set; }

	public event Action<SailfishDisplayState>? DisplayStateChanged;
	public event Action<bool>? ScreenLockChanged;
	public event Action<SailfishMemoryLevel>? MemoryLevelChanged;

	/// <summary>First Qt tick (SailfishEssentials.OnHostReady): the QML needs the running host.</summary>
	public void Start()
	{
		if (!QtHostServices.Ensure("system", Qml))
			QtHostDiag.Warn(QtHostDiagChannel.QtHost, "MCE system service unavailable — display/lock/memory events off");
	}

	/// <summary>Routes the service's shell events into the state above.</summary>
	public void Subscribe()
	{
		QtHostServices.Subscribe(ShellEvents.Display, e => OnDisplay(DisplayPayload.Parse(e).State));
		QtHostServices.Subscribe(ShellEvents.ScreenLock, e => OnScreenLock(ScreenLockPayload.Parse(e).Locked));
		QtHostServices.Subscribe(ShellEvents.MemoryLevel, e => OnMemoryLevel(MemoryLevelPayload.Parse(e).Level));
	}

	private void OnDisplay(SailfishDisplayState state)
	{
		if (DisplayState == state)
			return;
		DisplayState = state;
		DisplayStateChanged?.Invoke(state);
	}

	private void OnScreenLock(bool locked)
	{
		if (ScreenLocked == locked)
			return;
		ScreenLocked = locked;
		ScreenLockChanged?.Invoke(locked);
	}

	private void OnMemoryLevel(SailfishMemoryLevel level)
	{
		MemoryLevelAnswered = true;
		if (MemoryLevel == level)
			return;
		MemoryLevel = level;
		MemoryLevelChanged?.Invoke(level);
	}
}
