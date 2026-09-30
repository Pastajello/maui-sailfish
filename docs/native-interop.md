# Native Sailfish from a MAUI app

How an app (or library) on `Microsoft.Maui.SailfishOS` reaches native Sailfish OS: a system library,
its own C/C++ code, a QML module (`Sailfish.*`, `Nemo.*`, `Amber.*`), a D-Bus service or a native UI element. The document
describes what works today without backend changes, what is missing and how to close the gaps.

Status: code review of 2026-09-29. The paths described below follow from the code. None was checked on the phone,
except control adapters (the `PublicApiGuard` guard and matrix legs).

## In short

It is harder than on iOS/Android, but not "almost impossible". The difference lies elsewhere than the
comparison suggests:

- **dotnet/android and dotnet/macios** must have a binding layer, because the native APIs are in Java and Objective-C. Without
  JNI / an ObjC bridge .NET cannot call a single platform method.
- **On Sailfish .NET runs natively on Linux (glibc).** `DllImport` to any C library works out of the box,
  D-Bus is handled by purely managed libraries, and the process is already a Qt process with a running QML engine.
  The missing layer concerns only **C++/Qt** (no stable ABI for P/Invoke) and **QML** (Sailfish's public API
  is largely QML modules, just as the Android API is in Java).

The backend today exposes a back door through which almost anything can be done: `QtHostRuntime.Eval`,
`QtHostRuntime.QmlEvent`, `QtHostRuntime.RunOnQtThread`, `QtHostAdapters.Register` adapters. It is, however,
undocumented, based on JS strings and has a few hard gaps: no access to `QQmlEngine`, no QML import
paths, no `Requires:` in the RPM, no toolchain for third-party native code. These gaps cannot be worked around
from the app level. The plan to close them is in section [4](#4-how-to-implement-it).

## 1. Layers of native Sailfish and the path to each

| What you want to use | Examples | Path | Today |
|---|---|---|---|
| C library (system) | `libglib-2.0`, `libdbus-1`, `libc`, `libsystemd` | `DllImport` / `LibraryImport` | works |
| Own C library | codec, parser, BT driver | `DllImport` + `.so` in publish | works, no packaging support |
| D-Bus service | MCE, ofono, connman, Nemo Notifications, own daemon | managed D-Bus client or `Nemo.DBus` in QML | works; in the sandbox depends on Sailjail permissions |
| QML module without UI | `Nemo.Configuration`, `Nemo.KeepAlive`, `Amber.Mpris`, `QtPositioning` | invisible QML object + events | works via `Eval`, internal API |
| UI element from QML | Silica `ComboBox`, `VideoOutput`, `Map`, `Camera` | handler + QML adapter | works and is documented ([custom-controls.md](custom-controls.md)) |
| Own C++/Qt code | `QObject`, `QQuickItem`, image provider, QML plugin | library with an `extern "C"` API | partially; no engine, import paths, toolchain |
| System C++ library | `libsailfishsecrets`, `libconnman-qt5`, `libqofono` | as above, via own C bridge | the pattern exists only inside the backend (Secrets) |

The backend itself uses these paths:

- Essentials are invisible QML objects: battery in `Nemo.Mce`, network in `MeeGo.Connman`, notifications
  in `Nemo.Notifications`, sharing in `Sailfish.Share` (see [SailfishDevices.cs](../src/Linux.SailfishOS/Platform/SailfishDevices.cs)).
- SecureStorage is a C++ bridge loaded via `dlopen` ([sailfish_secrets.cpp](../src/Linux.SailfishOS/Native/sailfish_secrets.cpp),
  [SailfishSecureStorage.cs](../src/Linux.SailfishOS/Platform/SailfishSecureStorage.cs)).

The same patterns should be available to apps.

## 2. What works today

### 2.0 Threading model (applies to all paths)

The MAUI UI thread **is** the Qt loop thread: `QtHostRuntime.Run` pumps the MAUI dispatcher in the Qt tick. Three
rules follow from this:

- Everything that touches QML or Qt objects (including from your own C++) must run on this thread. From another thread
  call `QtHostRuntime.RunOnQtThread(...)` or `MainThread.BeginInvokeOnMainThread(...)`.
  Shim calls from outside the Qt thread are counted by `QtHostRuntime.OffThreadCalls`, and this counter must stay 0: such calls
  silently corrupt the QV4 heap.
- `QtHostRuntime.QmlEvent` is invoked on the Qt thread, in the tick. In the handler do not block or synchronously wait
  on a `Task`.
- A callback from a native worker thread (e.g. a C library with its own thread) must hop to the Qt thread via
  `RunOnQtThread` before it touches MAUI.

### 2.1 C library: `DllImport`

Works as on any Linux:

```csharp
#if SAILFISH
using System.Runtime.InteropServices;

static partial class GLib
{
    [LibraryImport("libglib-2.0.so.0", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr g_get_user_name();
}
#endif
```

Your own library goes into publish like any file and into the RPM together with the rest (`_SailfishPublishFiles`
is `$(PublishDir)**/*`):

```xml
<ItemGroup Condition="'$(TargetPlatformIdentifier)' == 'sailfish'">
  <None Include="native/aarch64/libmycodec.so" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />
</ItemGroup>
```

Pitfalls:

- **Dependencies of your own `.so`.** CoreCLR will find `libmycodec.so` in the app directory
  (`/usr/share/<pkg>/` or `/usr/share/<pkg>/lib/` in the Harbour profile). The system loader will not find its
  dependencies lying next to it, though. Link with `-Wl,-rpath,'$ORIGIN'`.
- **Harbour** accepts only system libraries from the allowed list and requires stripped `.so` files. Before
  submitting to the store, check the RPM with the Harbour validator (rpmvalidation from the Sailfish SDK). Packaging gives `.so` files
  mode 0755, everything else 0644.
- **RID version.** Devices are `aarch64` (Jolla C2, Xperia 10 III+) or `armv7hl`. The `.so` must match `-r`.
- **AOT/trimming.** Use `LibraryImport` and `[UnmanagedCallersOnly]` callbacks via `delegate* unmanaged`,
  not marshaled delegates (see [aot-and-trimming.md](aot-and-trimming.md), B7/B9).

### 2.2 D-Bus from C#

The app process has the session and system bus. Two paths:

- **Managed client** (e.g. `Tmds.DBus.Protocol` with a proxy generator from introspection XML). Works without QML
  and off the Qt thread. The generator does not use reflection, so it is trimming-friendly.
- **`Nemo.DBus` in QML** (`DBusInterface`, `DBusAdaptor`) via the invisible service from 2.3. This is what most
  native Sailfish apps do, and such code is easy to port from existing QML projects.

In the sandbox (`SailfishPermissions` non-empty, required for Harbour) Sailjail filters D-Bus by permissions.
A call to a service the app has no permission for will not reach its target (behavior to be verified on the
phone). Add the needed permissions to `SailfishPermissions` ([sailfishos-packaging.md](sailfishos-packaging.md)).

### 2.3 QML module without UI: invisible service

The Essentials pattern, repeated with public API. A QML object created once on the app window reports events via
the app queue (`window.mauiAppNotify`), which the shim drains on every tick:

```csharp
#if SAILFISH
using System.Text.Json;
using Microsoft.Maui.SailfishOS.Platform.QtHost;

// Watches and writes a dconf key through Nemo.Configuration.
static class CounterSetting
{
    const string Name = "myapp-counter";
    const string Qml = """
        import QtQuick 2.6
        import Nemo.Configuration 1.0
        ConfigurationValue {
            key: "/apps/harbour-myapp/counter"
            defaultValue: 0
            onValueChanged: window.mauiAppNotify("svc-myapp-counter", JSON.stringify({ value: value }))
        }
        """;

    public static event Action<int>? Changed;

    // After the window starts, e.g. in SailfishApplication.OnLaunched.
    public static void Start() => QtHostRuntime.RunOnQtThread(() =>
    {
        QtHostRuntime.QmlEvent += (name, payload) =>
        {
            if (name != "svc-myapp-counter")
                return;
            using var doc = JsonDocument.Parse(payload);
            Changed?.Invoke(doc.RootElement.GetProperty("value").GetInt32());
        };
        var rc = QtHostRuntime.Eval($"window.mauiService({Js(Name)}, {Js(Qml)})");
        if (rc != "ok")
            Console.Error.WriteLine($"Nemo.Configuration unavailable: {rc}");
    });

    public static void Set(int value) => QtHostRuntime.RunOnQtThread(() =>
        QtHostRuntime.Eval($"window.mauiServices[{Js(Name)}].value = {value}"));

    static string Js(string s) => JsonSerializer.Serialize(s);
}
#endif
```

Why this works:

- `mauiService` ([MauiShell.qml:153](../src/Linux.SailfishOS/Platform/QtHost/qml/MauiShell.qml#L153)) does
  `Qt.createQmlObject` on the app window, so the object survives navigation.
- Events with the `svc-` prefix bypass the renderer
  ([SailfishMauiApplication.cs:154](../src/Linux.SailfishOS/Platform/SailfishMauiApplication.cs#L154)).
- `QmlEvent` is a public multicast, so the app's handler gets every event.

Limitations (list of gaps in section 3):

- `mauiService`, `mauiServices` and `mauiAppNotify` are internal shell JS, not a contract. They may change without
  warning.
- You share the `svc-` prefix with the backend. Use your own namespace (`svc-<app>-…`).
- `Eval` returns text and silently truncates results above 8 KB.
- Methods and signals are available only as JS strings. Types and meaningful errors are missing.

### 2.4 Native UI element

The `SailfishSnapshotHandler` handler together with your own QML adapter, registered via `QtHostAdapters.Register`,
embeds any QML `Item` in the MAUI tree. MAUI does the layout, events come back via `mauiEvent`. Full description
and example are in [custom-controls.md](custom-controls.md). This path is public, guarded by `PublicApiGuard`
and works in the NuGet package (`buildTransitive` copies the QML).

In the adapter you may import any QML module from the system: `QtMultimedia` (`VideoOutput`, `Camera`), `QtLocation`,
`Sailfish.Silica` and others. Limitations are described in the "Limits" section of that document: the adapter's children are not MAUI
views, and the adapter works only for controls deriving directly from `View`.

### 2.5 Own C++/Qt code

Part of it already works today. A C++ library linked against the same Qt 5.6.3 as the device runs in the same
`QGuiApplication` and exposes an `extern "C"` API:

```cpp
// libmyqt.so
#include <QtQml>
#include "myitem.h"   // class MyItem : public QQuickItem { Q_OBJECT ... }

extern "C" __attribute__((visibility("default"))) void myqt_register(void)
{
    qmlRegisterType<MyItem>("MyQt", 1, 0, "MyItem");   // process-wide
}
```

```csharp
[LibraryImport("libmyqt.so")] internal static partial void myqt_register();

// In CreateMauiApp or OnLaunched, before the first page that uses the adapter:
QtHostRuntime.RunOnQtThread(myqt_register);
```

From then on `import MyQt 1.0` works in every QML adapter and service. `qmlRegisterType` registers types for
the whole process, so neither an import path nor a `qmldir` file is needed. Signals from C++ to C# have two paths:
via QML (`onFoo: mauiEvent(...)` or `window.mauiAppNotify(...)`) or via an
`[UnmanagedCallersOnly]` function pointer passed to C++.

This cannot be done today:

- **No access to `QQmlEngine*`**, so `addImageProvider`, `setContextProperty`
  with a `QObject*`, `addImportPath` or `QQmlNetworkAccessManagerFactory` cannot be registered. The shim has `sailfish_host_set_context_int/string`,
  but C# does not call them ([QtHostNative.cs](../src/Linux.SailfishOS/Platform/QtHost/QtHostNative.cs)).
- **No access to the MAUI host's `QObject*` / `QQuickItem*`.** `NativeElementHost.NativeHandle` is an id
  in the shim's `QPointer` registry, not a pointer. So a native library cannot hook onto an element that
  MAUI rendered.
- **QML plugins (`qmldir` + `plugin`) from the app directory do not work.** The engine gets only system paths
  ([sailfish_host.cpp:1083](../src/Linux.SailfishOS/Native/sailfish_host.cpp#L1083)). Under the Harbour booster
  `applicationDirPath` points at the booster, not the package.
- **No toolchain.** [tools/sf native-build](../tools/sf native-build) (zig + sysroot from
  [tools/sf sysroot](../tools/sf sysroot)) builds only the shim. The sysroot has QtCore/Gui/Qml/Quick/Network headers
  and `sailfishapp`, and adding another `-devel` (e.g. `nemo-qml-plugin-dbus-qt5-devel`) requires editing the script.
  The alternative is the full Sailfish SDK (`sfdk`).

## 3. Gaps

| # | Gap | Effect on the developer | Where |
|---|---|---|---|
| L1 | `QtHostServices` is `internal`, and `mauiService`/`mauiAppNotify` are JS without a contract | every QML service is hand-glued JS that may break on update | [QtHostServices.cs:10](../src/Linux.SailfishOS/Platform/QtHost/QtHostServices.cs#L10), [MauiShell.qml:138](../src/Linux.SailfishOS/Platform/QtHost/qml/MauiShell.qml#L138) |
| L2 | No typed QML object: only `Eval`, no method call, signal connection or errors | stringly-typed code, errors surface only at runtime as `""` | [QtHostRuntime.cs:294](../src/Linux.SailfishOS/Platform/QtHost/QtHostRuntime.cs#L294) |
| L3 | `Eval` silently truncates the result to 8 KB (C returns the full length, C# does not check it) | corrupted JSON with larger data (contacts, file list) | [QtHostRuntime.cs:298](../src/Linux.SailfishOS/Platform/QtHost/QtHostRuntime.cs#L298) |
| L4 | No `QQmlEngine*` and no "before shell load" hook | no image providers, context objects or NAM factory | [sailfish_host.h](../src/Linux.SailfishOS/Native/sailfish_host.h) |
| L5 | No `QObject*` for a handle | native code cannot touch a MAUI element | [NativeElementHost.cs:44](../src/Linux.SailfishOS/Platform/QtHost/NativeElementHost.cs#L44) |
| L6 | No QML import paths from the package | QML modules with a C++ plugin and a library's `qmldir` modules do not work | [sailfish_host.cpp:1083](../src/Linux.SailfishOS/Native/sailfish_host.cpp#L1083) |
| L7 | RPM: `AutoReqProv: no`, `Requires:` hard-coded only for Secrets | the app cannot declare e.g. `nemo-qml-plugin-configuration-qt5`, so on a clean system the QML import fails | [Microsoft.Maui.SailfishOS.targets:761](../src/Linux.SailfishOS/buildTransitive/Microsoft.Maui.SailfishOS.targets#L761) |
| L8 | No MSBuild items for native `.so` files and QML modules (rpath, strip, Harbour validation) | everyone looks after location, permissions and dependencies themselves | targets, `_PrepareSailfishRpmStaging` |
| L9 | No toolchain and sysroot for the app's native code | you have to recreate `sf native-build` or set up `sfdk` | [tools/sf native-build](../tools/sf native-build) |
| L10 | No documentation or example beyond adapters | this section was until now knowledge from the code | — |

## 4. How to implement it

Order by value-to-cost ratio. Phases 1 and 2 close practically all real scenarios.
Phase 4 is the equivalent of Android's binding generator and is optional.

### Phase 1: public QML objects (L1–L3)

New public API in `Microsoft.Maui.SailfishOS.Platform`, built on what already exists:

```csharp
public static class SailfishQml
{
    // An object on the app window (survives navigation), from inline QML or a file in the app's qml/.
    public static SailfishQmlObject CreateService(string name, string qml);
    public static SailfishQmlObject CreateServiceFromFile(string name, string relativeQmlPath);
}

public sealed class SailfishQmlObject : IDisposable
{
    public string Name { get; }
    public T? Get<T>(string property);                       // sailfish_host_get_property
    public void Set(string property, object? value);         // sailfish_host_set_property (already in the shim)
    public JsonElement Invoke(string method, params object?[] args);
    public IDisposable On(string signal, Action<JsonElement> handler);   // signal arguments as a JSON array
    public void Dispose();                                   // destroy_object + signal disconnect
}
```

Implementation:

- **Object**: `mauiService` sets `objectName = "maui-ext-<name>"`, and `sailfish_host_find_object` gives a handle.
  Properties go through the existing `set_property`/`get_property`, and the `QPointer` in the registry handles a dead object
  (code -3, `ObjectDisposedException`).
- **Signals** need no new C++: in JS `obj[signal].connect(function(){ window.mauiAppNotify(evt,
  JSON.stringify(Array.prototype.slice.call(arguments))) })`. Events get their own namespace `ext-<name>-<signal>`,
  routed in [SailfishMauiApplication.cs:154](../src/Linux.SailfishOS/Platform/SailfishMauiApplication.cs#L154)
  to `SailfishQml` subscribers, next to `svc-`.
- **Methods**: the JS variant is enough (`Eval` with JSON arguments). For large data a new
  `sailfish_host_invoke(handle, method, args_json, out, cap)` on `QMetaObject::invokeMethod` with `QVariant` is better, without
  building JS strings.
- **L3**: `Eval` and `get_property` retry the call with a buffer of the returned length when `len >= cap`.
- **Thread**: every method checks `IsQtThread` and throws outside it, instead of only counting `OffThreadCalls`.
  A simpler rule is easier to describe.
- The shell JS contract (`mauiAppNotify`, `mauiService`) described in a `MauiShell.qml` comment as stable.
- Internal Essentials move to the same API. That is the best proof that the API is sufficient.

Tests: unit tests on `IQtHostShim` (`TestShim`), an entry in `PublicApiGuard`, a matrix leg with `Nemo.Configuration`
(write, read, signal) on the phone.

### Phase 2: native extensions and packaging (L4–L8)

**The shim** gets a small, stable ABI for extension authors, in a separate header `sailfish_host_ext.h`, packed into
NuGet (`build/native/include/`):

```c
/* Qt thread. Valid after engine creation until teardown; do not delete. */
void *sailfish_host_qml_engine(void);                 /* QQmlEngine* */
void *sailfish_host_object_ptr(long long handle);     /* QObject* or NULL for a dead handle */
int   sailfish_host_add_import_path(const char *path);
```

- **Initialization hook.** Today `sailfish_host_load_window` creates the engine and immediately loads the shell, so there is no
  moment for `addImageProvider`. This has to be split: either `props_json` carries `importPaths` and a list of libraries
  to `dlopen` with a `maui_sailfish_ext_init(void *engine)` symbol called between engine creation
  and `setSource`, or C# registers a `SailfishNative.OnEngineCreated(Action<IntPtr>)` callback called at the same
  place. The `dlopen` variant is simpler for NuGet libraries, because it requires no code in the app.
- `sailfish_host_object_ptr` lets a native library e.g. attach a `QQuickItem` under an adapter or read
  the element's `window()`. Contract: the pointer is borrowed, QML remains the owner, and it may be used only until
  the end of the current call on the Qt thread.

**MSBuild** (in `Microsoft.Maui.SailfishOS.targets`):

```xml
<ItemGroup Condition="'$(TargetPlatformIdentifier)' == 'sailfish'">
  <!-- .so into the package lib dir; checks strip, arch and RUNPATH, and in Harbour warns about disallowed NEEDED entries. -->
  <SailfishNativeLibrary Include="native/$(SailfishRpmArch)/libmyqt.so" />
  <!-- Directory with a qmldir: goes to qml/imports/ and onto the engine import paths (appmeta -> add_import_path). -->
  <SailfishQmlModule Include="qml-modules/MyQt" />
  <!-- Requires: lines; in Harbour only allow-listed packages (others are an error). -->
  <SailfishRequires Include="nemo-qml-plugin-configuration-qt5" />
</ItemGroup>
```

- `SailfishRequires` replaces today's `_SailfishRequiresLines` (Secrets becomes an ordinary entry).
- A NuGet library (e.g. `MyCompany.Maui.Sailfish.Mpris`) adds these items in its `buildTransitive`, just as the
  backend adds its `qml/`. The app gets RPM dependencies for free, because the NuGet dependency pulls in the RPM dependency.
- Import paths go into `maui-appmeta.json` (already generated in `_SailfishWriteAppMeta`), and the shim reads them before
  `setSource`. This also works under the booster, because the path is computed from `AppContext.BaseDirectory`.

### Phase 3: toolchain for native code (L9, L10)

- `tools/sf-native-cc.sh` (or MSBuild `SailfishNativeCompile`) as a generalization of `sf native-build`: zig
  with the same sysroot, flags `-fPIC -shared -std=c++14 -Wl,-s -Wl,-rpath,'$ORIGIN'` and linking against Qt 5.6.3.
- `sf sysroot --devel <rpm>...` pulls additional `-devel` packages from the release repository (the RPM download
  mechanism already exists).
- A `dotnet new sailfish-native-lib` template: C++ (`QQuickItem` + `extern "C"`), a C# project with `LibraryImport`,
  a QML adapter and `buildTransitive`.
- An example in `samples/`: one QML service (Phase 1) and one C++ element (Phase 2), with a matrix leg.
- This document reworked into a guide in English, next to [custom-controls.md](custom-controls.md).

### Phase 4 (optional): typed proxy generator

This is the equivalent of the Android and iOS binding generators, except that the source is the Qt metaobject, not Java or ObjC.

- QML modules in the system usually have `plugins.qmltypes` (type description from `qmlplugindump`). To be checked on the phone
  (`ls /usr/lib64/qt5/qml/Nemo/*/`).
- A source generator reads the `.qmltypes` pointed to by the item `<SailfishQmlBinding Include="Nemo.Configuration" Version="1.0" />`
  and generates C# classes (`ConfigurationValue` with typed properties, methods and events) on
  `SailfishQmlObject` from Phase 1.
- For D-Bus nothing needs to be built; it is enough to point to the proxy generator from introspection XML (section 2.2).

Phase 4 makes sense only once Phase 1 is in use and it is visible which modules people bind by hand.

## 5. Decisions to make

1. **Whether Phase 1 goes in as public API now.** It is a few days of work with tests. It closes L1–L3 and moves
   Essentials to the shared API.
2. **How to do the engine hook in Phase 2:** `dlopen` + a `maui_sailfish_ext_init` symbol (simpler for NuGet libraries)
   or a C# callback (more control, requires code in the app). Recommendation: `dlopen`, with the C# callback
   added later if needed.
3. **ABI stability of `sailfish_host_ext.h`.** Version it separately from the rest of the shim (which stays internal)
   and keep only the calls from this list.
