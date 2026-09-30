using System.Runtime.InteropServices;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// P/Invoke surface of <c>libsailfishhost.so</c> (Native/sailfish_host.h). Everything runs on the
/// Qt loop thread except quit, post and wake, which are safe from any thread.
/// </summary>
internal static class QtHostNative
{
	private const string Lib = "sailfishhost";

	/* Log levels match QtMsgType: 0=debug 1=warning 2=critical 3=fatal. */
	public delegate void LogFn(int level, IntPtr message, IntPtr userData);

	/* Invoked from the Qt event loop when the host is woken. */
	public delegate void TickFn(IntPtr userData);

	/* kind: 0=mouse press, 1=mouse release, 2=mouse move, 3=wheel,
	 * 4=touch begin, 5=touch update, 6=touch end. */
	public delegate void PointerFn(int kind, double x, double y, double delta, int extra, IntPtr userData);

	/* kind: 0=press, 1=release; key = Qt::Key; modifiers = Qt::KeyboardModifiers;
	 * text = UTF-8 (may be empty). */
	public delegate void KeyFn(int kind, int key, int modifiers, IntPtr text, IntPtr userData);

	/* QML mauiNotify event: name + payload as UTF-8, raised on the Qt thread. */
	public delegate void EventFn(IntPtr name, IntPtr payload, IntPtr userData);

	/* Callback queued onto the Qt thread via sailfish_host_post. */
	public delegate void VoidFn(IntPtr userData);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int sailfish_host_init(string appName, LogFn? log, IntPtr logUser);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int sailfish_host_load(string qmlPath);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int sailfish_host_load_window(string qmlPath, string? propsJson);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int sailfish_host_show();

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int sailfish_host_exec(TickFn? tick, IntPtr tickUser, int tickMs);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern void sailfish_host_quit();

	/* 0 = queued, -1 = rejected (teardown); on rejection the caller must free its GCHandle. */
	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int sailfish_host_post(VoidFn fn, IntPtr userData);

	/* Arms the next single-shot tick in delay_ms; any thread. */
	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int sailfish_host_wake(int delayMs);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern void sailfish_host_set_input_callbacks(PointerFn? pointer, KeyFn? key, IntPtr userData);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern void sailfish_host_set_event_callback(EventFn? fn, IntPtr userData);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int sailfish_host_eval(string expression, IntPtr outBuf, int cap);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern void sailfish_host_inject_pointer(int kind, double x, double y);

	/* Synthetic key event posted to the window, so it reaches Qt's focusObject. */
	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern void sailfish_host_inject_key(int kind, int key, int modifiers, string? text);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int sailfish_host_grab_png(string path);

	/* Records the app window's frames as JPEGs (used by tools/sf-record.sh). */
	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int sailfish_host_record_start(string dir, int fps, int scalePct);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int sailfish_host_record_stop();

	/* App fonts (QFontDatabase) and FontImageSource glyphs rendered to PNG. */
	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int sailfish_host_register_font(string path, IntPtr buf, int cap);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int sailfish_host_render_glyph(string family, string text, double px, string color, string outPath);

	/* --- Persistent native objects (see sailfish_host.h) --- */

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern long sailfish_host_find_object(string objectName);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern long sailfish_host_find_visual(long parent, string objectName);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int sailfish_host_set_property(long handle, string name, string? valueJson);

	/* Ordered [{"name":...,"value":...}] batch; returns the count not applied, -1 bad JSON, -3 dead handle. */
	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int sailfish_host_apply_props(long handle, string propsJson);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int sailfish_host_get_property(long handle, string name, IntPtr outBuf, int cap);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int sailfish_host_item_geometry(long handle, out double x, out double y, out double w, out double h);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int sailfish_host_set_parent_item(long handle, long parent);

	/* [{"handle":"N",x,y,w,h,vis}] in Qt scene units; returns the count not applied, -1 bad JSON. */
	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int sailfish_host_apply_geometry(string geoJson);

	/* Text measurement with QFontMetrics; input and output in device pixels. */
	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int sailfish_host_measure_text(string json, out double w, out double h);

	/* Window/screen geometry as JSON; returns its length or -1. */
	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int sailfish_host_screen_info(IntPtr outBuf, int cap);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern void sailfish_host_destroy_object(long handle);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int sailfish_host_push_page(string qmlPath, string? propsJson, int immediate);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int sailfish_host_pop_page(int immediate);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern long sailfish_host_tick_count();

	/* --- Diagnostics --- */

	/* Simulates background/resume via QPA events; state = Qt::ApplicationState, activate 1/0, -1 = unchanged. */
	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int sailfish_host_diag_app_state(int state, int activate);

	/* Shim counters as JSON; lateCallbacks must stay 0. Returns the JSON length or -1. */
	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int sailfish_host_diag_stats(IntPtr outBuf, int cap);

	/* Performance report as JSON; returns its length or -1. */
	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int sailfish_host_perf_stats(IntPtr outBuf, int cap);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int sailfish_host_last_error(IntPtr buf, int cap);

	/* Clipboard and URL opening for Essentials; clipboard_get returns the UTF-8 length. */
	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int sailfish_host_clipboard_set(IntPtr text);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int sailfish_host_clipboard_get(IntPtr buf, int cap);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int sailfish_host_open_url(IntPtr url);
}
