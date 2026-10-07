using System.Runtime.InteropServices;

namespace Microsoft.Maui.SailfishOS.Platform.QtHost;

/// <summary>
/// P/Invoke surface of <c>libsailfishhost.so</c> (Native/sailfish_host.h). Everything runs on the
/// Qt loop thread except quit, post and wake, which are safe from any thread.
/// </summary>
internal static class QtHostNative
{
	private const string Lib = "sailfishhost";

	/// <summary>SFHOST_ABI_VERSION of the shim this code was written against (sailfish_host.h).</summary>
	internal const int AbiVersion = 5;

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

	/* Frame callback of the drawing surfaces: the next frame after sailfish_host_request_frame. */
	public delegate void FrameFn(IntPtr userData);

	/* Surface touch: action 0 pressed, 1 moved, 2 released, 3 cancelled; device 0 touch, 1 mouse; returns handled. */
	public delegate int SurfaceTouchFn(long handle, int action, int pointer, double x, double y, double pressure,
		int device, int button, IntPtr userData);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int sailfish_host_abi_version();

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int sailfish_host_invoke(long handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string method,
		[MarshalAs(UnmanagedType.LPUTF8Str)] string? arg, IntPtr output, int capacity);

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
	public static extern int sailfish_host_last_result(IntPtr outBuf, int cap);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern void sailfish_host_inject_pointer(int kind, double x, double y);

	/* Synthetic key event posted to the window, so it reaches Qt's focusObject. */
	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern void sailfish_host_inject_key(int kind, int key, int modifiers, string? text);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int sailfish_host_grab_png(string path);

	/* Screenshot: the window or a part of it (scene units) as PNG, or JPEG for a .jpg path. */
	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int sailfish_host_grab_image(string path, double x, double y, double w, double h, int quality);

	/* Re-encodes an image file (format by the destination's extension). */
	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int sailfish_host_convert_image(string src, string dst, int quality);

	/* Records the app window's frames as JPEGs (used by tools/sf record). */
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

	/* --- Drawing surfaces --- */

	/* Shows width x height RGBA8888-premultiplied pixels in the host item; width/height 0 frees them. */
	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int sailfish_host_surface_commit(long handle, IntPtr pixels, int width, int height, int stride);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern void sailfish_host_set_frame_callback(FrameFn? fn, IntPtr userData);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int sailfish_host_request_frame();

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern void sailfish_host_set_surface_touch_callback(SurfaceTouchFn? fn, IntPtr userData);

	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern int sailfish_host_surface_set_touch(long handle, int enabled);

	/* Diagnostics: one multi-touch event; states are Qt::TouchPointState values. */
	[DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
	public static extern void sailfish_host_inject_touch(int count, int[] ids, double[] xy, int[] states);
}
