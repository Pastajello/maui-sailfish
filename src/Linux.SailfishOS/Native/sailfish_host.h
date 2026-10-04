/* sailfish_host.h — C ABI of the native Qt Quick/Silica host for MAUI.Sailfish.
 *
 * The shim runs inside the .NET process and owns QGuiApplication (via libsailfishapp)
 * and one Qt Quick window, either a plain QQuickView ("view" mode) or a Silica
 * ApplicationWindow with PageStack and optional cover ("window" mode).
 *
 * Threading: init, load, show, exec and everything else run on the thread that pumps
 * the Qt loop (tick and input callbacks run there too); only quit, post and wake
 * are safe from any thread.
 *
 * Ownership: QGuiApplication lives until process exit and is never deleted (QPA/Wayland
 * statics). Callbacks and their user data are borrowed from managed code and cleared
 * before QML teardown, so nothing calls into .NET afterwards. sailfish_host_quit() is an
 * idempotent teardown; after it the other APIs are no-ops.
 */
#ifndef SAILFISH_HOST_H
#define SAILFISH_HOST_H

#ifdef __cplusplus
extern "C" {
#endif

/* level follows QtMsgType (0=debug .. 3=fatal). Level 0, including QML console.log, is
 * printed only with MAUI_SAILFISH_QT_HOST_DIAG=1; warnings and errors always print. */
typedef void (*sfhost_log_fn)(int level, const char *message, void *user_data);

/* Stable return codes of all sailfish_host_* APIs (ABI contract: never change values).
 * Batch APIs also return a positive count of entries not applied. Details go to
 * sailfish_host_last_error(). */
enum sfhost_err {
    SFHOST_OK            =  0,  /* success */
    SFHOST_E_ARGS        = -1,  /* bad argument / null / bad JSON / host not ready */
    SFHOST_E_PROPERTY    = -2,  /* unknown property / object is not a QQuickItem */
    SFHOST_E_DEAD_HANDLE = -3,  /* handle not in registry or object already destroyed */
    SFHOST_E_JS          = -4,  /* the JS engine reported an error (eval, page push/pop) */
    SFHOST_E_LOAD        = -5   /* a QML file did not load (the QML errors are the last error) */
};

/* Bumped whenever an export's signature, a payload format or an error code changes. The managed side
 * (QtHostNative.AbiVersion) refuses to start against another version instead of losing features silently. */
#define SFHOST_ABI_VERSION 4


/* Called on the Qt loop every tick_ms milliseconds. */
typedef void (*sfhost_tick_fn)(void *user_data);

/* kind: 0 press, 1 release, 2 move, 3 wheel, 4 touch begin, 5 touch update, 6 touch end,
 * 7 second touch point (follows a 4/5/6 event that has two or more points; extra = fingers still down).
 * x/y in window pixels; delta = angleDelta.y(); extra = Qt button (mouse) or touch point count. */
typedef void (*sfhost_pointer_fn)(int kind, double x, double y, double delta,
                                  int extra, void *user_data);

/* kind: 0 press, 1 release; key = Qt::Key; modifiers = Qt::KeyboardModifiers; text UTF-8. */
typedef void (*sfhost_key_fn)(int kind, int key, int modifiers, const char *text,
                              void *user_data);

/* QML-to-C events: QML calls mauiNotify(name, payload) and the shim drains the page's
 * __mauiDrain() queue each tick, since there is no moc to let QML call C directly. */
typedef void (*sfhost_event_fn)(const char *name, const char *payload,
                                void *user_data);

/* Generic callback run on the Qt thread (bridge for MAUI dispatchers). */
typedef void (*sfhost_void_fn)(void *user_data);

/* Calls a QML function on the object behind handle: method(arg) when arg is non-null, else method(). The result
 * (its string form; "" for undefined) goes to out like eval's. Returns its UTF-8 length, SFHOST_E_ARGS,
 * SFHOST_E_PROPERTY (no such method with that arity) or SFHOST_E_DEAD_HANDLE. Unlike eval nothing is compiled per
 * call. A JS exception inside the function is logged by the QML engine; the call then answers "". Qt thread only. */
int sailfish_host_invoke(long long handle, const char *method, const char *arg, char *out, int cap);

/* SFHOST_ABI_VERSION of this build; callable before init. */
int sailfish_host_abi_version(void);

/* Creates QGuiApplication via SailfishApp::application. Idempotent. */
int sailfish_host_init(const char *app_name, sfhost_log_fn log, void *log_user);

/* View mode: QQuickView (SizeRootObjectToView) loading a QML file with an Item root. */
int sailfish_host_load(const char *qml_path);

/* Window mode: Silica ApplicationWindow via QQmlComponent. props_json (object or NULL) is
 * set as root context properties before create(), so Component.onCompleted can see them. */
int sailfish_host_load_window(const char *qml_path, const char *props_json);

/* Shows the window fullscreen and re-activates/raises it after 500/1500 ms. */
int sailfish_host_show(void);

/* Runs the Qt event loop; blocks. tick may be NULL. */
int sailfish_host_exec(sfhost_tick_fn tick, void *tick_user, int tick_ms);

/* Any thread. Idempotent teardown; exec returns 0 without running once it has happened. */
void sailfish_host_quit(void);

/* Any thread: queues fn(user_data) onto the Qt thread. Returns -1 when not initialised or
 * shutting down; the caller must then free its trampoline (GCHandle) itself. */
int sailfish_host_post(sfhost_void_fn fn, void *user_data);

/* Any thread: arms the next tick in delay_ms (the earlier deadline wins). */
int sailfish_host_wake(int delay_ms);

/* Root context properties of the QML engine. Qt thread. */
int sailfish_host_set_context_int(const char *name, long long value);
int sailfish_host_set_context_string(const char *name, const char *value);

/* Window mode only. qml_path must be absolute: PageStack._getPage() resolves relative paths
 * against a JS stack frame that does not exist when called from C++. */
int sailfish_host_push_page(const char *qml_path, const char *props_json, int immediate);
int sailfish_host_pop_page(int immediate);

/* Evaluates JS with the QML root as scope; result as text (use JSON.stringify for objects).
 * Returns the UTF-8 length of the result. */
int sailfish_host_eval(const char *expression, char *out, int cap);

/* The whole result of the last sailfish_host_eval or sailfish_host_invoke whose result did not fit its buffer: the
 * caller sees a length >= cap and fetches it here with a buffer of that length + 1, instead of evaluating again.
 * Kept until the next eval or invoke. Returns its UTF-8 length; 0 when the last call fit. Qt thread. (ABI 4) */
int sailfish_host_last_result(char *out, int cap);

/* Diagnostic JSON: mode, root/window classes, top-level windows, cover window presence. */
int sailfish_host_window_info(char *buf, int cap);

/* The input filter observes window events without consuming them. */
void sailfish_host_set_input_callbacks(sfhost_pointer_fn pointer, sfhost_key_fn key,
                                       void *user_data);

/* Callback for mauiNotify events; runs on the Qt thread inside the tick. */
void sailfish_host_set_event_callback(sfhost_event_fn fn, void *user_data);

/* Posts a synthetic mouse event (kind 0/1/2 as in sfhost_pointer_fn) through the normal
 * window delivery path. */
void sailfish_host_inject_pointer(int kind, double x, double y);

/* Posts a synthetic key event to the window, so the focused Silica TextField receives it
 * like a hardware key. Qt thread only. */
void sailfish_host_inject_key(int kind, int key, int modifiers, const char *text);


/* Writes QQuickWindow::grabWindow() as PNG; proves rendering independent of the compositor.
 * Qt thread. */
int sailfish_host_grab_png(const char *path);
/* Showcase recorder: PNG frames (with alpha) of the app window into dir (<ms>.png), at most
 * fps per second, scaled to scale_pct percent; stop returns the frame count. */
int sailfish_host_record_start(const char *dir, int fps, int scale_pct);
int sailfish_host_record_stop(void);

/* register_font writes the first family name to out and returns its length.
 * render_glyph draws text in family ("" = default) at px size, color "#AARRGGBB", into a
 * tightly cropped transparent PNG. Qt thread. */
int sailfish_host_register_font(const char *path, char *out, int cap);
int sailfish_host_render_glyph(const char *family, const char *text, double px,
                               const char *color, const char *out_path);

/* --- Persistent native objects (handles) -------------------------------------
 *
 * A handle is a QML QObject* kept in a QPointer registry, so an object QML destroys on
 * its own becomes a dead handle (-3) instead of a use-after-free. QML keeps ownership;
 * the shim only borrows. All handle functions run on the Qt thread.
 */

/* Finds objectName under the QML root (recursive findChild) and registers it; 0 if absent. */
long long sailfish_host_find_object(const char *object_name);

/* Like find_object but walks the visual tree (childItems, BFS) from parent (0 = root).
 * Needed for ListView delegates: Qt 5.6 reparents them visually only, so findChild misses them. */
long long sailfish_host_find_visual(long long parent, const char *object_name);

/* Sets a property from JSON, converted by the target property's type (bool/number/string,
 * enum name or number, color, rect/point/size objects, null = type default,
 * {"$handle":N} = object). Never recreates the object. */
int sailfish_host_set_property(long long handle, const char *name, const char *value_json);

/* props_json: ordered [{"name":..,"value":..}]; order is kept because the mauiApplying
 * envelope must be applied in sequence. Returns the number of properties not applied. */
int sailfish_host_apply_props(long long handle, const char *props_json);

/* Reads a property as text (bool "true"/"false", numbers in C format). Returns the length. */
int sailfish_host_get_property(long long handle, const char *name, char *out, int cap);

/* QQuickItem geometry: x/y in scene (window) coordinates, w/h in pixels. */
int sailfish_host_item_geometry(long long handle, double *x, double *y, double *w, double *h);

/* setParentItem by handle, not objectName: a ListView can briefly hold two delegates with
 * the same name. */
int sailfish_host_set_parent_item(long long handle, long long parent);

/* One call per MAUI layout pass: [{"handle":N|"N","x","y","w","h","vis":0|1}]. Values are
 * Qt scene units already converted by managed code (never scaled again); x/y are scene
 * coordinates mapped into each parent via mapFromScene. Returns entries not applied. Qt thread. */
int sailfish_host_apply_geometry(const char *geo_json);

/* QFontMetrics text measurement, so layout and QML rendering share metrics.
 * json: {"text","family","px","bold","italic","ls","lh","maxLines","wrap","maxW"}; px, ls and
 * maxW are device pixels, lh is a line-height multiplier, wrap 0=none 1=word 2=anywhere,
 * maxW 0 = unbounded. Output is device pixels; paragraphs split on \n. Qt thread. */
int sailfish_host_measure_text(const char *json, double *out_w, double *out_h);


/* JSON {"window":{x,y,width,height,dpr},"screen":{name,x,y,width,height,dpr,orientation,
 * nativeOrientation}} in device pixels. Returns the JSON length. */
int sailfish_host_screen_info(char *buf, int cap);

/* Unregisters the handle and deleteLaters the object; no-op for unknown/dead handles. */
void sailfish_host_destroy_object(long long handle);

long long sailfish_host_tick_count(void);

/* --- Lifecycle / threading diagnostics --- */

/* Injects QPA window activation (activate 1/0, -1 = unchanged) and application state
 * (Qt::ApplicationState 0..4, -1 = unchanged) to simulate background/resume without the
 * compositor. Delivered on the next loop pass. Qt thread. */
int sailfish_host_diag_app_state(int state, int activate);

/* JSON counters: registry (live handles), posts queued/run/rejected, lateCallbacks (calls
 * attempted after teardown, must stay 0), ticks, pointerEvents, shutdown. Qt thread. */
int sailfish_host_diag_stats(char *buf, int cap);

/* --- Performance diagnostics --- */

/* JSON perf report: frame/sync timings, CPU and RSS (/proc/self), QML object/item counts,
 * render loop class and boundary-crossing counters. Diagnostics only; Qt thread. */
int sailfish_host_perf_stats(char *buf, int cap);

/* --- Essentials --- */

/* Sets the clipboard text; an in-app mirror keeps the round trip working when the compositor clipboard
 * is unavailable. Qt thread. */
int sailfish_host_clipboard_set(const char *text);

/* Copies the clipboard text into buf; returns the copied UTF-8 length. Qt thread. */
int sailfish_host_clipboard_get(char *buf, int cap);

/* Opens a URL with the system handler (QDesktopServices). Qt thread. */
int sailfish_host_open_url(const char *url);

/* --- Drawing surfaces ---
 *
 * A surface shows pixels drawn by managed code (SkiaSharp's raster canvas) inside a host item, which it fills.
 * It is created on the first commit and dies with its host. Qt thread. */

/* Copies width x height RGBA8888 premultiplied pixels (stride bytes per row, >= width*4) and shows them in the next
 * frame. width or height 0 (or pixels NULL) frees the surface's pixels. -3 for a dead handle. */
int sailfish_host_surface_commit(long long handle, const void *pixels, int width, int height, int stride);

/* Called on the Qt thread in the next frame after sailfish_host_request_frame (QQuickWindow::afterAnimating, before
 * the scene graph syncs), once however many requests came. */
typedef void (*sfhost_frame_fn)(void *user_data);
void sailfish_host_set_frame_callback(sfhost_frame_fn fn, void *user_data);
int sailfish_host_request_frame(void);

/* Touch on a surface, after SkiaSharp's Android SKTouchHandler. action: 0 pressed, 1 moved, 2 released, 3 cancelled;
 * pointer = Qt touch point id (0 for the mouse); x/y in item pixels; device 0 touch, 1 mouse; button 0 left, 1 middle,
 * 2 right. Called synchronously during Qt's event delivery; the return value of the first press decides whether the
 * surface keeps the gesture (nonzero) or Qt passes it on. handle is the host's handle. Qt thread. */
typedef int (*sfhost_surface_touch_fn)(long long handle, int action, int pointer, double x, double y,
                                       double pressure, int device, int button, void *user_data);
void sailfish_host_set_surface_touch_callback(sfhost_surface_touch_fn fn, void *user_data);

/* Turns touch delivery for a host's surface on or off (creates the surface when turning it on). */
int sailfish_host_surface_set_touch(long long handle, int enabled);

/* Diagnostics: one multi-touch event through the QPA window-system interface, like real input. x/y pairs in window
 * pixels; states are Qt::TouchPointState values (1 pressed, 2 moved, 4 stationary, 8 released). Qt thread. */
void sailfish_host_inject_touch(int count, const int *ids, const double *xy, const int *states);

/* Copies the last error into buf; returns the full message length. */
int sailfish_host_last_error(char *buf, int cap);

#ifdef __cplusplus
} /* extern "C" */
#endif

#endif /* SAILFISH_HOST_H */

