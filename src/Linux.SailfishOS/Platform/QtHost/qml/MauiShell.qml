import QtQuick 2.6
import Sailfish.Silica 1.0

// MAUI-owned Silica shell: the root window the Qt host loads when
// MAUI_SAILFISH_QT_HOST=1 and no explicit QML path is given.
//
// cover stays null unless enabled: ApplicationWindow creates a second window
// (and EGL surface) for a truthy cover, which crashed qmlscene runs.
// initialPage is not set because its animatorPush never progresses outside the
// launcher flow; pages are pushed with PageStackAction.Immediate instead.
// Each MAUI page (navigation and modal) maps to one MauiModelPage, registered
// here bottom → top; transient dialogs are not registered.
ApplicationWindow {
    id: window

    visible: true
    // Set by the platform from qml/maui-appmeta.json (baked by MSBuild); All matches SailfishOrientation=Any.
    property int mauiOrientations: Orientation.All
    property bool mauiCoverEnabled: false
    property string mauiCoverTitle: ""
    // SailfishCover API data {title|null, lines[], actions[icon…]}; any call enables
    // the cover. mauiCoverUrl is the author's SailfishCoverQml item, fed mauiCoverData.
    property bool mauiCoverApi: false
    property var mauiCoverData: ({ title: null, lines: [], actions: [] })
    property string mauiCoverUrl: ""
    cover: mauiCoverEnabled || mauiCoverApi || mauiCoverUrl !== "" ? mauiCover : null
    allowedOrientations: mauiOrientations
    // Pages default to _defaultPageOrientations (Portrait in Silica) and the current page decides, so the app's
    // mask must reach them too or the window never turns.
    _defaultPageOrientations: mauiOrientations

    property var __mauiCoverItem: null   // the live cover (diagnostics)

    function mauiSetCover(data) {
        mauiCoverData = data;
        mauiCoverApi = true;
    }

    Component {
        id: mauiCover
        CoverBackground {
            id: coverRoot
            readonly property string coverTitle: window.mauiCoverData.title !== null && window.mauiCoverData.title !== undefined
                                                 ? window.mauiCoverData.title : window.mauiCoverTitle
            readonly property var coverLines: window.mauiCoverData.lines || []
            readonly property var coverActions: window.mauiCoverData.actions || []

            onStatusChanged: window.mauiAppNotify("svc-cover-status",
                JSON.stringify({ active: status === Cover.Active,
                                 status: status === Cover.Active ? "active" : status === Cover.Activating ? "activating"
                                       : status === Cover.Deactivating ? "deactivating" : "inactive" }))
            Component.onCompleted: window.__mauiCoverItem = coverRoot
            Component.onDestruction: if (window.__mauiCoverItem === coverRoot) window.__mauiCoverItem = null
            // the path a CoverAction tap takes (diagnostics trigger it directly)
            function trigger(i) { window.mauiAppNotify("svc-cover-action", JSON.stringify({ index: i })); }

            // no lines, no author item: the generic placeholder (title only)
            CoverPlaceholder {
                visible: coverRoot.coverLines.length === 0 && window.mauiCoverUrl === ""
                text: coverRoot.coverTitle
            }

            Column {
                visible: coverRoot.coverLines.length > 0 && window.mauiCoverUrl === ""
                x: Theme.paddingLarge
                y: Theme.paddingLarge
                width: parent.width - 2 * Theme.paddingLarge
                spacing: Theme.paddingSmall
                Label {
                    width: parent.width
                    text: coverRoot.coverTitle
                    color: Theme.highlightColor
                    font.pixelSize: Theme.fontSizeMedium
                    truncationMode: TruncationMode.Fade
                }
                Repeater {
                    model: coverRoot.coverLines
                    Label {
                        width: parent.width
                        text: modelData
                        color: Theme.primaryColor
                        font.pixelSize: Theme.fontSizeSmall
                        wrapMode: Text.Wrap
                        maximumLineCount: 3
                        truncationMode: TruncationMode.Fade
                    }
                }
            }

            Loader {
                id: authorCover
                anchors.fill: parent
                active: window.mauiCoverUrl !== ""
                source: window.mauiCoverUrl
                onStatusChanged: if (status === Loader.Error) console.error("SailfishCoverQml failed to load: " + source)
            }
            Binding {
                target: authorCover.item
                property: "mauiCoverData"
                value: window.mauiCoverData
                when: authorCover.item !== null && authorCover.item.hasOwnProperty("mauiCoverData")
            }

            // Silica takes the first enabled list: one action or two
            CoverActionList {
                enabled: coverRoot.coverActions.length === 1
                CoverAction {
                    iconSource: coverRoot.coverActions.length > 0 ? coverRoot.coverActions[0] : ""
                    onTriggered: coverRoot.trigger(0)
                }
            }
            CoverActionList {
                enabled: coverRoot.coverActions.length >= 2
                CoverAction {
                    iconSource: coverRoot.coverActions.length > 0 ? coverRoot.coverActions[0] : ""
                    onTriggered: coverRoot.trigger(0)
                }
                CoverAction {
                    iconSource: coverRoot.coverActions.length > 1 ? coverRoot.coverActions[1] : ""
                    onTriggered: coverRoot.trigger(1)
                }
            }
        }
    }

    // Live model pages, bottom → top. mauiModelPage is the top model page even
    // while a Silica Dialog is pageStack.currentPage.
    property var mauiPages: []
    property var mauiModelPage: mauiPages.length > 0 ? mauiPages[mauiPages.length - 1] : null
    property url mauiPageUrl: Qt.resolvedUrl("MauiModelPage.qml")

    // App-level platform services (theme, battery, sensors, …) report through their
    // own queue, since page queues change with navigation and dialogs would swallow events.
    property var mauiServices: ({})
    property var __mauiAppQueue: []
    // Bumped by every queued event so the shim drains on NOTIFY instead of polling.
    property int mauiEventSeq: 0
    function mauiAppNotify(name, payload) { __mauiAppQueue.push({ name: name, payload: payload }); mauiEventSeq++; }

    // Native platform events for SailfishMauiApplication (Platforms/SailfishOS/SailfishApplication.cs).
    onOrientationChanged: mauiAppNotify("svc-app-orientation", JSON.stringify({ orientation: orientation }))
    function __mauiReportInputMethod() {
        var r = Qt.inputMethod.keyboardRectangle;
        mauiAppNotify("svc-input-method", JSON.stringify({ visible: Qt.inputMethod.visible,
                                                            x: r.x, y: r.y, width: r.width, height: r.height }));
    }
    Connections {
        target: Qt.inputMethod
        onVisibleChanged: window.__mauiReportInputMethod()
        onKeyboardRectangleChanged: window.__mauiReportInputMethod()
    }
    function __mauiAppDrain() { var q = __mauiAppQueue; __mauiAppQueue = []; return q; }
    // The shim's drain, once per tick: every model page's queue and the app queue. A page under a Silica dialog or a
    // parked page reports too (the drain used to read pageStack.currentPage only, so their events waited until the
    // page was on top again).
    function __mauiDrainAll() {
        var out = null;
        for (var i = 0; i < mauiPages.length; ++i) {
            var p = mauiPages[i];
            if (p && p.__mauiQueue && p.__mauiQueue.length)
                out = (out || []).concat(p.__mauiDrain());
        }
        if (__mauiAppQueue.length)
            out = (out || []).concat(__mauiAppDrain());
        return out ? JSON.stringify(out) : "[]";
    }
    // The navigation snapshot the renderer reads every poll (QtHostPageRenderer.TryReadNavState), called through
    // sailfish_host_invoke on the "mauiShell" object below, so no JS is compiled per poll.
    function mauiNavState() {
        if (!window.mauiPages)
            return "{}";
        // The pageStack itself, not the page registry: a popped page stays registered until its deferred destruction.
        var ids = [];
        pageStack.find(function(p) { if (p && p.mauiPageId !== undefined) ids.unshift(String(p.mauiPageId)); return false; });
        return JSON.stringify({ ids: ids, ver: window.mauiStackVersion || 0, busy: !!pageStack.busy,
                                topModel: !!(pageStack.currentPage && pageStack.currentPage.mauiPageId !== undefined),
                                active: !!window.active,
                                appState: Qt.application ? Qt.application.state : -1 });
    }
    // What C# calls on the shell itself (sailfish_host_invoke): findChild finds this object, not the window.
    QtObject {
        objectName: "mauiShell"
        function navState() { return window.mauiNavState(); }
    }
    function mauiService(name, qml) {
        if (!mauiServices[name]) {
            try {
                mauiServices[name] = Qt.createQmlObject(qml, window, "maui-service-" + name);
            } catch (e) {
                console.error("maui service '" + name + "' failed: " + e);
                return "error: " + e;
            }
        }
        return "ok";
    }

    // Shadow/Clip for any adapter (apply_layer_effect). The effect Component must be
    // created from QML because the layer instantiates it in its creation context.
    function mauiApplyLayerEffect(it, text, url) {
        if (!text) {
            it.layer.enabled = false;
            it.layer.effect = null;
            return null;
        }
        var c = Qt.createQmlObject(text, it, url);
        it.layer.effect = c;
        it.layer.enabled = true;
        return c;
    }

    // Perf diagnostics: op batches and ops applied across all pages.
    // The renderer syncs the moment a transition ends or the stack changes natively (a back gesture),
    // not at its next poll; t is the QML clock for the transition-end → render measurement.
    Connections {
        target: pageStack
        onBusyChanged: if (!pageStack.busy) window.mauiAppNotify("svc-nav-idle", JSON.stringify({ t: Date.now() }))
        onDepthChanged: {
            window.mauiStackVersion++;
            window.mauiAppNotify("svc-nav-depth", JSON.stringify({ t: Date.now(), depth: pageStack.depth }));
        }
        // Silica can swap the top page instance without a depth change (the activation rebuild): the renderer
        // re-arms the page-instance state (title, background, geometry) when it sees the new instance.
        onCurrentPageChanged: {
            window.mauiStackVersion++;
            window.mauiAppNotify("svc-nav-depth", JSON.stringify({ t: Date.now(), depth: pageStack.depth }));
        }
    }
    // Foreground/background and window focus reach the lifecycle bridge when they change, not at a poll.
    Connections {
        target: Qt.application
        // One payload shape for both signals: the C# side reads `state` (SailfishApplicationState) and treated a
        // missing `state` as Active, so an `{ active: false }` payload used to report deactivation as activation.
        // The only source of svc-app-state (the shim no longer sends its own): both signals report the same payload,
        // and SailfishMauiApplication raises a transition once.
        onStateChanged: window.mauiReportAppState()
        onActiveChanged: window.mauiReportAppState()
    }
    function mauiReportAppState() {
        mauiAppNotify("svc-app-state", JSON.stringify({ state: Qt.application.state, active: Qt.application.active }));
    }
    // Bumped on every pageStack depth change: the coordinator logs which native state an operation saw.
    property int mauiStackVersion: 0

    // MAUI_SAILFISH_IMAGE_TRACE=1: every image adapter logs "MAUI-IMG" per load (controls/Image.qml).
    property bool mauiImageTrace: false
    // MAUI_SAILFISH_OPS_TIMING=1: each page logs where its host-creating op batches spend the time (OPS-TIMING).
    property bool mauiOpsTiming: false
    // MAUI_SAILFISH_LIST_PREFETCH=N: list rows are built N viewports ahead once a list settled (default 2).
    property real mauiListPrefetch: 2
    property int mauiOpsBatches: 0
    property int mauiOpsApplied: 0

    function mauiNoteOps(n) {
        mauiOpsBatches += 1;
        mauiOpsApplied += n;
    }

    // Adapter components, shared by every page (each page used to load its own) and preloaded in the background
    // once the first page shows (mauiPreloadAdapters): a page's first host of a kind no longer compiles its adapter.
    property var __adapterComps: ({})
    property var __preloadQueue: []
    property int mauiAdaptersPreloaded: 0
    function mauiComponentFor(src) {
        var comp = __adapterComps[src];
        if (comp && comp.status === Component.Error) {
            console.error("ADAPTER load failed src=" + src + ": " + comp.errorString());
            comp = null;
            __adapterComps[src] = null;
        }
        // Not loaded yet, or still loading in the background: a synchronous load finishes it now.
        if (comp === undefined || (comp && comp.status === Component.Loading)) {
            comp = Qt.createComponent(Qt.resolvedUrl(src));
            if (comp.status === Component.Error) {
                console.error("ADAPTER load failed src=" + src + ": " + comp.errorString());
                comp = null;
            } else {
                console.log("ADAPTER loaded src=" + src);
            }
            __adapterComps[src] = comp;
        }
        return comp;
    }
    function mauiPreloadAdapters(json) {
        __preloadQueue = JSON.parse(json);
        adapterPreload.start();
    }
    // One adapter per tick: loaded asynchronously (Qt's type loader compiles off the GUI thread), then one throwaway
    // instance off screen, since the first instance of a kind is what costs (Silica's own types load, bindings warm
    // up): a page's first Statistics-like push spent ~110 ms in QML ops against ~22 ms on the second visit.
    Item { id: __warmHolder; visible: false; width: 0; height: 0 }
    Timer {
        id: adapterPreload
        interval: 40
        repeat: true
        onTriggered: {
            while (window.__preloadQueue.length > 0) {
                var src = window.__preloadQueue[0];
                var comp = window.__adapterComps[src];
                if (comp === undefined) {
                    window.__adapterComps[src] = Qt.createComponent(Qt.resolvedUrl(src), Component.Asynchronous);
                    return;   // instantiated on a later tick, once loaded
                }
                if (comp && comp.status === Component.Loading)
                    return;
                window.__preloadQueue.shift();
                if (comp && comp.status === Component.Ready && window.__warmed[src] !== true) {
                    window.__warmed[src] = true;
                    var o = comp.createObject(__warmHolder, { mauiId: "warm" });
                    if (o)
                        o.destroy();
                    window.mauiAdaptersPreloaded++;
                    return;
                }
            }
            stop();
        }
    }
    property var __warmed: ({})

    function registerMauiPage(p) {
        if (mauiPages.indexOf(p) < 0)
            mauiPages.push(p);
        mauiPagesChanged();
    }

    function unregisterMauiPage(p) {
        var i = mauiPages.indexOf(p);
        if (i >= 0)
            mauiPages.splice(i, 1);
        mauiPagesChanged();
    }

    function mauiPageById(id) {
        for (var i = 0; i < mauiPages.length; ++i)
            if (mauiPages[i].mauiPageId === id)
                return mauiPages[i];
        return null;
    }

    // mauiFirstTitle: the first page's header, a context property of the window load.
    Component.onCompleted: {
        pageStack.push(mauiPageUrl,
                       { mauiPageId: "mp1",
                         pageTitle: typeof mauiFirstTitle !== "undefined" ? mauiFirstTitle : "" },
                       PageStackAction.Immediate);
        // The state settles while the window loads and shows: report where it is now (queued until managed drains).
        mauiReportAppState();
    }
}
