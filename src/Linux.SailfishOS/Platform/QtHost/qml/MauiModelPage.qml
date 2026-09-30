import QtQuick 2.6
import Sailfish.Silica 1.0

// Persistent MAUI element host. Managed code reconciles the MAUI tree into op batches
// (applyMauiOps); each element gets a persistent QML object named "maui_<id>" that the
// shim resolves to a native handle and updates directly, so native state survives updates.
// MAUI is the only layout engine: hosts sit on a flat canvas positioned by the managed
// geometry pass (Qt scene units, already converted from dp); QML never lays them out.
Page {
    id: page

    // Managed-assigned page id ("mp1" for the shell root); the renderer addresses batches through it.
    property string mauiPageId: ""
    property var __shell: null

    property string pageTitle: "MAUI"

    // MAUI page background, painted behind the flickable: the colour, then Page.BackgroundImageSource
    // cropped to fill.
    property color mauiBackground: "transparent"
    property string mauiBackgroundImage: ""

    // Diagnostics: records every title/background this page took, so a one-tick flash of
    // another page's chrome is detectable. Off in production.
    property bool mauiRecordChrome: false
    property var mauiTitleHistory: []
    property var mauiBackgroundHistory: []
    onPageTitleChanged: if (mauiRecordChrome) mauiTitleHistory.push(pageTitle)
    onMauiBackgroundChanged: if (mauiRecordChrome) mauiBackgroundHistory.push(String(mauiBackground))

    // MAUI owns the scroll state (ScrollView.ScrollY); the SilicaFlickable only drives the
    // gesture and reports contentY back ("scroll-changed"). Managed pushes the authoritative
    // state via setMauiScroll(). contentH is in Qt scene units and includes the top inset.
    property bool mauiScrollEnabled: false
    property double mauiContentHeight: 0
    // Diag (MAUI_SAILFISH_OPEN_PULLEY): freeze managed ScrollY writes so an opened pulley stays open.
    property bool mauiHoldScrollY: false
    property bool __applyingScroll: false
    property double __lastReportedY: 0

    // Top safe-area inset (status area + PageHeader + tab bar) in Qt scene units, as reported to managed.
    property double topInset: ((page.statusHeight !== undefined) ? page.statusHeight : 0) + pageHeader.height + tabBar.height

    // Tab bar (Shell tabs / TabbedPage), hidden with fewer than two tabs. Its height counts
    // as header, so the MAUI content area shrinks with it.
    property var mauiTabs: []
    property int mauiTabIndex: 0
    function setMauiTabs(json) {
        var o = JSON.parse(json);
        mauiTabs = o.titles || [];
        mauiTabIndex = o.index !== undefined ? o.index : 0;
    }

    function setMauiScroll(json) {
        var o = JSON.parse(json);
        mauiScrollEnabled = !!o.enabled;
        mauiContentHeight = o.contentH || 0;
        // An opened pulley is an overscrolled contentY; the managed push would snap it shut.
        if (mauiHoldScrollY)
            return;
        __applyingScroll = true;
        flick.contentY = o.scrollY || 0;
        __applyingScroll = false;
        __lastReportedY = flick.contentY;
    }

    // RefreshView pull-to-refresh on the page flickable, for content without its own scroll
    // surface. Managed arms it only when the page has no pulley (which owns the top overscroll).
    property string mauiRefreshId: ""
    property bool mauiRefreshing: false
    property color mauiRefreshColor: "transparent"

    function setMauiRefresh(json) {
        var o = JSON.parse(json);
        mauiRefreshId = o.id || "";
        mauiRefreshing = !!o.refreshing;
        mauiRefreshColor = o.color ? o.color : "transparent";
    }

    // Deepest overscroll of the current pull: the release bounce resets contentY before
    // movementEnded, so the depth is remembered, and __pullDecided allows one decision per
    // overscroll so the bounce tail cannot fire a second refresh (see ListView.qml).
    property double __pullDepth: 0
    property bool __pullDecided: false

    function __maybeRequestRefresh() {
        if (mauiRefreshId.length === 0 || __pullDecided)
            return;
        if (__pullDepth >= -Theme.itemSizeMedium)
            return;
        __pullDecided = true;
        if (!mauiRefreshing)
            mauiNotify("refresh-requested", JSON.stringify({ id: mauiRefreshId }));
    }

    // Persistent hosts: id -> { item, uri }, plus creation order.
    property var __hosts: ({})
    property var __order: []

    // Cumulative lifecycle counters; property updates must not inflate them.
    property int createdTotal: 0
    property int destroyedTotal: 0

    property alias flickY: flick.contentY

    // QML → managed event queue (drained by the shim on every tick).
    property var __mauiQueue: []
    function mauiNotify(name, payload) {
        __mauiQueue.push({ name: name, payload: payload });
        if (page.__shell)
            page.__shell.mauiEventSeq++;   // wakes the shim's drain
    }
    function __mauiDrain() { var q = __mauiQueue; __mauiQueue = []; return q; }

    // Breadth-first objectName lookup under the page. Collection delegates/slots ("maui_<listId>__…")
    // are not in __hosts, so create ops use this to parent item hosts inside them.
    function __mauiFindByName(name) {
        var queue = [page];
        while (queue.length > 0) {
            var node = queue.shift();
            if (node.objectName === name)
                return node;
            var kids = node.children;
            if (kids) {
                for (var i = 0; i < kids.length; ++i)
                    queue.push(kids[i]);
            }
        }
        return null;
    }

    // Adapter components are loaded by "src" (from managed adapters.json) and cached per src;
    // the page knows no adapter type names.
    property var __comps: ({})
    // Hosts created before their parentObj placeholder existed, re-parented later by
    // __resolveReparents. Declared because undeclared properties cannot be assigned.
    property var __pendingReparents: []

    // Qt.callLater needs Qt 5.8 and throws on 5.6, so a one-shot Timer stands in. Used by
    // adapters that cannot own a Timer child (pulley menus put children into _content).
    function mauiDefer(fn) {
        var t = Qt.createQmlObject("import QtQuick 2.6; Timer { interval: 16; repeat: false }", page);
        t.triggered.connect(function() { t.destroy(); fn(); });
        t.start();
    }

    // Used when a src fails to load, so a broken adapter degrades to a plain label.
    Component {
        id: fallbackComp
        Label {
            property string mauiId: ""
            property string mauiProbe: ""
            signal mauiEvent(string name, string payload)
            property bool mauiApplying: false
            property string mauiEmphasis: "normal"
            wrapMode: Text.Wrap
            color: Theme.secondaryColor
        }
    }

    function __componentFor(src) {
        if (!src)
            return null;
        var comp = __comps[src];
        if (comp === undefined) {
            comp = Qt.createComponent(Qt.resolvedUrl(src));
            if (comp.status === Component.Error) {
                console.error("ADAPTER load failed src=" + src + ": " + comp.errorString());
                comp = null;
            } else {
                console.log("ADAPTER loaded src=" + src);
            }
            __comps[src] = comp;
        }
        return comp;
    }

    // Managed entry point (Qt thread): applies a reconciled batch of title/background/create/
    // destroy/order/reparent ops. "order" re-parents existing objects without recreating them.
    // Returns "created:destroyed".
    function applyMauiOps(opsJson) {
        var ops = JSON.parse(opsJson);
        // Settle pending re-parents whose parents an earlier batch created.
        __resolveReparents();
        if (page.__shell && page.__shell.mauiNoteOps)
            page.__shell.mauiNoteOps(ops.length);
        var created = 0, destroyed = 0, unknown = 0, unknownIds = [], createdIds = [], createdUris = [], destroyedIds = [];
        for (var i = 0; i < ops.length; ++i) {
            var o = ops[i];
            if (o.op === "title") { pageTitle = o.text; continue; }
            if (o.op === "background") {
                mauiBackground = o.color || "transparent";
                mauiBackgroundImage = o.image || "";
                continue;
            }
            if (o.op === "create") {
                if (__hosts[o.id] !== undefined)
                    __destroyHost(o.id, true);
                if (__createHost(o.id, o.uri, o.src || "", o.props || {}, o.parentObj || "", o.parent || ""))
                    { created++; createdIds.push(o.id); createdUris.push(o.uri); }
                else
                    { unknown++; unknownIds.push(o.id); }
                continue;
            }
            if (o.op === "destroy") {
                if (__hosts[o.id] !== undefined) {
                    // Isolate throwing destroys (e.g. a Silica list mid-teardown): skipped objects would
                    // outlive their managed mirror and reappear as stray paint or stale pulleys after a pop.
                    try {
                        __destroyHost(o.id, true);
                    } catch (e) {
                        console.error("HOST destroy failed id=" + o.id + ": " + e);
                    }
                    destroyed++;
                    destroyedIds.push(o.id);
                } else {
                    unknown++; unknownIds.push(o.id);
                }
                continue;
            }
            if (o.op === "order") {
                __applyOrder(o.ids || [], o.parent || "");
                continue;
            }
            if (o.op === "reparent") {
                __reparentHost(o.id, o.parent || "");
                continue;
            }
            unknown++;
        }
        createdTotal += created;
        destroyedTotal += destroyed;
        // unknown=N in a destroy batch means it hit the wrong page instance (how hosts leak across a pop).
        console.log("OPS page=" + mauiPageId + " created=" + created + " destroyed=" + destroyed +
                    " unknown=" + unknown +
                    (createdIds.length ? " createdIds=" + createdIds.slice(0, 8).join(",") +
                        (createdIds.length > 8 ? ",+" + (createdIds.length - 8) + " more" : "") +
                        " createdUris=" + createdUris.slice(0, 8).join(",") : "") +
                    (destroyedIds.length ? " destroyedIds=" + destroyedIds.slice(0, 8).join(",") +
                        (destroyedIds.length > 8 ? ",+" + (destroyedIds.length - 8) + " more" : "") : "") +
                    (unknownIds.length ? " unknownIds=" + unknownIds.join(",") : "") +
                    " hosts=" + __order.length +
                    " totals=" + createdTotal + "/" + destroyedTotal);
        reportTimer.restart();
        return created + ":" + destroyed;
    }

    // Native back-navigation can leave hosts whose managed ids were lost; they paint as strays
    // and stale pulleys re-attach. Wipes every host so the reconcile rebuilds the page.
    function __destroyAllHosts() {
        for (var id in __hosts) {
            try { __destroyHost(id, true); } catch (e) { /* isolate */ }
        }
        __hosts = {};
        __order = [];
        page.__pendingReparents = [];
    }

    // Destroys only hosts the managed mirror no longer knows (strays after a native pop), so
    // back-cached hosts survive. knownIds arrives as a JSON string. Strays have no managed owner,
    // so the QML object is destroyed here.
    function __destroyHostsNotIn(knownIds) {
        if (typeof knownIds === "string")
            knownIds = JSON.parse(knownIds);
        var keep = {};
        for (var i = 0; i < knownIds.length; ++i)
            keep[knownIds[i]] = true;
        var orphans = [];
        for (var id in __hosts)
            if (keep[id] === undefined)
                orphans.push(id);
        for (var j = 0; j < orphans.length; ++j) {
            try { __destroyHost(orphans[j], false); } catch (e) { /* isolate */ }
        }
        if (orphans.length)
            console.log("STRAYS page=" + mauiPageId + " destroyed " + orphans.length + " ids=" + orphans.slice(0, 8).join(",") +
                        (orphans.length > 8 ? ",+" + (orphans.length - 8) + " more" : "") + " known=" + knownIds.length);
        return orphans.length;
    }

    function __resolveReparents() {
        var pending = page.__pendingReparents;
        if (!pending || pending.length === 0)
            return;
        var left = [];
        for (var i = 0; i < pending.length; ++i) {
            var entry = pending[i];
            var host = __hosts[entry.id];
            // Managed may already have pinned the host to the exact placeholder by handle; a name
            // lookup could pick a same-named parked delegate instead.
            if (host && host.item && host.item.parent && host.item.parent.objectName === entry.parentObj)
                continue;
            var resolved = __mauiFindByName(entry.parentObj);
            if (host && resolved) {
                host.item.parent = resolved;
                // The shim maps coords through the parent chain, so the geometry pushed while on the
                // canvas is now wrong; ask managed to re-push it.
                if (host.item.mauiEvent !== undefined)
                    host.item.mauiEvent("reparented", JSON.stringify({ id: entry.id }));
            } else {
                left.push(entry);
            }
        }
        page.__pendingReparents = left;
    }

    // The QML item a host's children go into ("" or a failed parent = the canvas).
    function __hostItem(parentId) {
        if (!parentId || parentId.length === 0)
            return canvas;
        var parentHost = __hosts[parentId];
        if (!parentHost || !parentHost.item)
            return canvas;
        // A host may route children elsewhere (a scroll view uses its contentItem).
        return parentHost.item.mauiChildHost ? parentHost.item.mauiChildHost : parentHost.item;
    }

    // Moves a surviving element to another container; a following "order" op fixes its position.
    function __reparentHost(id, parentId) {
        var host = __hosts[id];
        if (!host || !host.item || host.item.mauiDetached === true)
            return;
        host.item.parent = __hostItem(parentId);
        host.parentId = parentId;
    }

    // Creates a persistent host inside its nearest hosted ancestor (or a named placeholder via
    // parentObj, e.g. a ListView delegate). mauiEvent is wired to the managed queue. Returns
    // false when even the fallback fails.
    function __createHost(id, uri, src, props, parentObj, parentId) {
        var comp = __componentFor(src) || fallbackComp;
        var init = {};
        // Generic view props are applied natively by the shim after create; adapters don't declare them.
        for (var k in props)
            if (k !== "mauiBackgroundFill" && k !== "mauiAccessibleName" &&
                    k !== "mauiAccessibleDescription" && k !== "mauiAutomationId" &&
                    k !== "mauiLayerShadow" && k !== "mauiLayerClip" &&
                    k !== "mauiAccessibleRole" && k !== "mauiAccessibleIgnored" && k !== "mauiMirrored")
                init[k] = props[k];
        init.objectName = "maui_" + id;
        init.mauiId = id;
        // Geometry arrives parent-relative; pre-order batches create parents first.
        var hostParent = __hostItem(parentId);
        if (parentObj && parentObj.length > 0) {
            var resolved = __mauiFindByName(parentObj);
            if (resolved)
                hostParent = resolved;
            else {
                // The parent can arrive in a later batch; parking the child on the canvas would paint it
                // at the origin above everything, so re-parent once the parent exists.
                hostParent = canvas;
                if (!page.__pendingReparents)
                    page.__pendingReparents = [];
                page.__pendingReparents.push({ id: id, parentObj: parentObj });
            }
        }
        var item = comp.createObject(hostParent, init);
        if (!item) {
            console.error("HOST create failed id=" + id + " uri=" + uri + " src=" + src);
            return false;
        }
        // Hidden until the first geometry push (which carries "vis"), otherwise it paints briefly
        // at implicit size with the Silica default font. Detached adapters get no geometry.
        if (item.mauiDetached !== true)
            item.visible = false;
        if (item.mauiEvent !== undefined)
            item.mauiEvent.connect(page.mauiNotify);
        // Page-level adapters (menus, panels, drawers) get the page so they can leave the canvas
        // and dock against the Silica chrome.
        if (item.mauiPage !== undefined)
            item.mauiPage = page;
        __hosts[id] = { item: item, uri: uri, parentId: parentId || "" };
        __order.push(id);
        return true;
    }

    // Re-attaches the same objects inside their parent in the desired order (child order =
    // stacking order); QObjects, state and native handles survive.
    function __applyOrder(ids, parentId) {
        var parentItem = __hostItem(parentId);
        for (var i = 0; i < ids.length; ++i) {
            var host = __hosts[ids[i]];
            // Detached page-level adapters live in the flickable/page; Silica owns their stacking.
            if (host && host.item && host.item.mauiDetached !== true) {
                host.item.parent = null;
                host.item.parent = parentItem;
            }
        }
    }

    // nativeDestroy: managed destroys the object via the shim, so QML only unregisters it.
    // The signal is disconnected before the object dies.
    function __destroyHost(id, nativeDestroy) {
        var host = __hosts[id];
        delete __hosts[id];
        var idx = __order.indexOf(id);
        if (idx >= 0) __order.splice(idx, 1);
        if (host && host.item) {
            if (host.item.mauiEvent !== undefined)
                host.item.mauiEvent.disconnect(page.mauiNotify);
            if (!nativeDestroy)
                host.item.destroy();
        }
    }

    Timer {
        id: reportTimer
        interval: 50
        onTriggered: page.reportRendered()
    }

    // Reports page size and Silica insets (Qt units) to managed, which defines the root
    // coordinate space and relayouts. The only dp conversion happens in managed (QtHostUnits).
    function reportWindowGeometry() {
        var status = (page.statusHeight !== undefined) ? page.statusHeight : 0;
        mauiNotify("window-geometry", JSON.stringify({
            pageWidth: page.width,
            pageHeight: page.height,
            headerHeight: pageHeader.height + tabBar.height,
            statusHeight: status
        }));
    }

    onWidthChanged: reportWindowGeometry()
    onHeightChanged: reportWindowGeometry()

    // Render evidence for managed: button/toggle geometry (for synthetic taps), counters, scroll.
    property int __emptyReports: 0

    function reportRendered() {
        // Silica hides the page until activation finishes; geometry read before that is meaningless.
        if (!page.visible) {
            reportTimer.restart();
            return;
        }
        // Also wait out push/pop animations: geometry is mid-slide and Silica refuses
        // navigation during the transition.
        if (pageStack.busy) {
            reportTimer.restart();
            return;
        }
        // Host creation waits for the stack to settle, so early reports carry the default title
        // and no hosts. Wait (bounded) for the first ops.
        if (__order.length === 0 && __emptyReports < 40) {
            __emptyReports++;
            reportTimer.restart();
            return;
        }
        reportWindowGeometry();   // managed dedups unchanged reports
        var buttons = [];
        var toggles = [];
        for (var i = 0; i < __order.length; ++i) {
            var id = __order[i];
            var host = __hosts[id];
            // Only buttons and toggles report geometry; mapToItem for every other host was most of the cost.
            if (host && host.item && host.item.visible &&
                    (host.uri === "button" || host.uri === "switch" || host.uri === "slider")) {
                var p = host.item.mapToItem(page, 0, 0);
                if (host.uri === "button")
                    buttons.push({ id: id, x: p.x, y: p.y,
                                   w: host.item.width, h: host.item.height });
                else if (host.uri === "switch" || host.uri === "slider")
                    toggles.push({ id: id, uri: host.uri, x: p.x, y: p.y,
                                   w: host.item.width, h: host.item.height });
            }
        }
        mauiNotify("rendered", JSON.stringify({ title: pageTitle,
                                                hosts: __order.length,
                                                createdTotal: createdTotal,
                                                destroyedTotal: destroyedTotal,
                                                flickY: flick.contentY,
                                                pv: page.visible, fv: flick.visible,
                                                buttons: buttons,
                                                toggles: toggles }));
    }

    // Diag: fires a host's mauiEvent as the native control would. Picker dialogs live in a
    // separate Silica window and cannot be pointer-injected, so tests go through the same queue.
    function __diagFireEvent(id, name, payload) {
        var host = __hosts[id];
        if (!host || !host.item || host.item.mauiEvent === undefined)
            return false;
        host.item.mauiEvent(name, payload);
        return true;
    }

    // Page-level adapters use this to register as the flickable's pullDownMenu/pushUpMenu.
    function mauiFlickable() { return flick; }

    // Diag (MAUI_SAILFISH_OPEN_PULLEY): the scroller that actually carries the pulley
    // (a hosted list on collection pages, the page flickable otherwise).
    function mauiPulleySurface(which) {
        // Prefer a surface with real scroll extent: on short content PulleyMenuBase's
        // _maxDragPosition guard clamps a parked contentY back to the origin.
        var pf = mauiFlickable();
        var fallback = null;
        for (var id in __hosts) {
            var it = __hosts[id].item;
            if (!it || !it[which])
                continue;
            if (it.contentHeight > it.height + 1)
                return it;
            fallback = fallback || it;
        }
        if (pf && pf[which] && pf.contentHeight > pf.height + 1)
            return pf;
        return fallback || pf;
    }

    // Diag (MAUI_SAILFISH_OPEN_PULLEY): opens a pulley without a gesture. Silica has no
    // activate(); "open" means menu active + flickable parked at _finalPosition, which needs
    // DragOverBounds, a cancelled bounce-back and frozen managed ScrollY pushes.
    function mauiOpenPulley(which, tries) {
        try {
            var f = mauiPulleySurface(which);
            if (!f || !f[which]) {
                // The menu attaches a few ticks after the create batch; retry, bounded.
                if ((tries || 0) < 20) {
                    mauiDefer(function() { mauiOpenPulley(which, (tries || 0) + 1); });
                    return "retry";
                }
                mauiNotify("pulley-open-try", JSON.stringify({
                    which: which, hasF: !!f, hasM: false, gaveUp: true }));
                return "gaveup";
            }
            var m = f[which];
            mauiHoldScrollY = true;
            f.boundsBehavior = Flickable.DragOverBounds;
            m.active = true;
            // Park synchronously: Qt.callLater from a C++-driven eval never runs here.
            var before = f.contentY;
            f.contentY = m._finalPosition;
            if (m.cancelBounceBack)
                m.cancelBounceBack();
            // Silica starts its own return animation once no drag is active, so keep re-parking.
            __pulleyHold = { f: f, m: m, ticks: 0 };
            pulleyHoldTimer.restart();
            mauiNotify("pulley-opened", JSON.stringify({
                which: which, pageFlick: f === flick, active: m.active,
                before: before, after: f.contentY, fin: m._finalPosition,
                origin: f.originY, menuH: m.height, bb: f.boundsBehavior }));
            return "ok";
        } catch (e) {
            return "err:" + e + " line=" + (e.lineNumber !== undefined ? e.lineNumber : -1);
        }
    }

    // Diag (MAUI_SAILFISH_OPEN_PULLEY): re-asserts the parked overscroll during the screenshot.
    property var __pulleyHold: null
    Timer {
        id: pulleyHoldTimer
        interval: 100
        repeat: true
        onTriggered: {
            if (!page.__pulleyHold) {
                stop();
                return;
            }
            var h = page.__pulleyHold;
            h.f.contentY = h.m._finalPosition;
            h.m.active = true;
            h.ticks++;
            if (h.ticks > 80) {
                page.__pulleyHold = null;
                stop();
            }
        }
    }

    // Opens the page's context-menu adapter around the long-pressed host. itemsJson is
    // [{text,enabled}]. Returns false if the target or menu host is missing.
    function __openContextMenu(hostId, itemsJson) {
        var target = __hosts[hostId];
        if (!target || !target.item)
            return false;
        var menu = null;
        for (var id in __hosts) {
            if (__hosts[id].uri === "context-menu") {
                menu = __hosts[id].item;
                break;
            }
        }
        if (!menu || menu.openFor === undefined)
            return false;
        menu.openFor(target.item, itemsJson);
        return true;
    }

    // Pushes a dialog adapter on the pageStack (same window, so it is pointer-injectable).
    // The dialog emits accept/reject through mauiNotify and Silica pops it itself.
    function __pushDialog(src, propsJson) {
        var comp = __componentFor(src);
        if (!comp)
            return "no-comp";
        if (pageStack.busy) {
            // Silica refuses pushes mid-transition (e.g. DisplayAlert from OnAppearing); wait for it to settle.
            var later = function() {
                if (pageStack.busy)
                    return;
                pageStack.busyChanged.disconnect(later);
                if (__pushDialogNow(comp, propsJson) !== "ok")
                    page.mauiNotify("dialog-failed", "{}");
            };
            pageStack.busyChanged.connect(later);
            return "ok";
        }
        return __pushDialogNow(comp, propsJson);
    }

    function __pushDialogNow(comp, propsJson) {
        var dlg = pageStack.push(comp, JSON.parse(propsJson), PageStackAction.Immediate);
        if (!dlg)
            return "no-push";
        // Silica pops a Dialog itself on accept/reject; a manual pop would warn mid-transition.
        dlg.mauiEvent.connect(function(name, payload) {
            page.mauiNotify(name, payload);
        });
        return "ok";
    }

    function __diagDump() {
        var out = { pageVis: visible, pageOp: opacity, pageStatus: status,
                    depth: pageStack.depth,
                    isCurrent: pageStack.currentPage === page,
                    bg: mauiBackground.toString(),
                    bgImage: bgImage.visible ? bgImage.status : -1,
                    flickVis: flick.visible, flickOp: flick.opacity,
                    canvasVis: canvas.visible, canvasOp: canvas.opacity,
                    canvasChildren: canvas.children.length,
                    flickY: flick.contentY, originY: flick.originY,
                    topMargin: flick.topMargin, contentItemY: flick.contentItem.y,
                    canvasSceneY: canvas.mapToItem(null, 0, 0).y,
                    pageSceneY: page.mapToItem(null, 0, 0).y, hosts: [] };
        for (var i = 0; i < __order.length; ++i) {
            var h = __hosts[__order[i]];
            if (!h || !h.item)
                continue;
            out.hosts.push({ id: __order[i], vis: h.item.visible, op: h.item.opacity,
                             x: h.item.x, y: h.item.y, w: h.item.width, h: h.item.height,
                             parent: h.item.parent === canvas ? "canvas"
                                   : (h.parentId && __hosts[h.parentId] && h.item.parent === __hosts[h.parentId].item
                                      ? h.parentId : (h.item.parent ? "other" : "null")) });
        }
        return JSON.stringify(out);
    }

    Component.onCompleted: {
        console.log("PAGE completed id=" + mauiPageId + " w=" + width + " h=" + height +
                    " vis=" + visible + " depth=" + pageStack.depth);
        // Register with the shell's page registry; managed polls it to detect native pops.
        for (var o = parent; o; o = o.parent) {
            if (o.registerMauiPage !== undefined) {
                page.__shell = o;
                o.registerMauiPage(page);
                break;
            }
        }
        reportWindowGeometry();
    }

    Component.onDestruction: {
        // Leave the registry; managed compares it with the MAUI stack to sync native pops back.
        if (page.__shell)
            page.__shell.unregisterMauiPage(page);
    }

    // Re-report once the page actually becomes visible (Silica activation).
    onVisibleChanged: {
        console.log("PAGE vis=" + visible);
        if (visible) {
            reportWindowGeometry();
            reportTimer.restart();
        }
    }

    // In landscape Silica narrows the page by the camera cutout; the background still fills the screen edge to
    // edge, as MAUI page backgrounds do under a notch on iOS (content keeps avoiding the cutout).
    readonly property real __cutoutBleed: page.isLandscape && (page.cutoutMode & CutoutMode.AvoidLandscapeCutout)
                                         ? Screen.topCutout.height : 0
    Rectangle {
        anchors.fill: parent
        anchors.leftMargin: -page.__cutoutBleed
        anchors.rightMargin: -page.__cutoutBleed
        color: page.mauiBackground
        visible: color.a > 0
    }
    Image {
        id: bgImage
        anchors.fill: parent
        anchors.leftMargin: -page.__cutoutBleed
        anchors.rightMargin: -page.__cutoutBleed
        source: page.mauiBackgroundImage
        visible: source != ""
        fillMode: Image.PreserveAspectCrop
        asynchronous: true
        clip: true
    }

    // Page-level scroll driver. The canvas lives inside its contentItem because Qt mouse
    // propagation walks ancestors, not siblings: hosts that accept mouse consume presses,
    // everything else propagates up to the flickable, which scrolls natively.
    SilicaFlickable {
        id: flick
        anchors.fill: parent
        interactive: page.mauiScrollEnabled
        contentHeight: Math.max(page.height, page.mauiContentHeight)
        onContentYChanged: {
            if (contentY < page.__pullDepth)
                page.__pullDepth = contentY;
            else if (contentY >= 0) {
                page.__pullDepth = 0;
                page.__pullDecided = false;
            }
            if (page.__applyingScroll || contentY === page.__lastReportedY)
                return;
            page.__lastReportedY = contentY;
            page.mauiNotify("scroll-changed", JSON.stringify({ y: contentY }));
        }
        // Releasing a top overscroll past itemSizeMedium is the pull-to-refresh gesture.
        onDragEnded: page.__maybeRequestRefresh()
        onMovementEnded: page.__maybeRequestRefresh()

        // Flat geometry canvas: hosts are positioned by the managed geometry batch; child order is
        // stacking order. Scrolling is the contentItem's own translation, not a canvas offset.
        Item {
            id: canvas
            // The shim recognizes this parent: hosts carry canvas coords, which must not include
            // the live contentY (mapFromScene would bake it in).
            objectName: "mauiCanvas"
            width: page.width
            height: Math.max(page.height, page.mauiContentHeight)
        }

        // RefreshView spinner, pinned to the viewport so it rides the top overscroll.
        BusyIndicator {
            objectName: "mauiRefreshSpinner"
            size: BusyIndicatorSize.Medium
            running: page.mauiRefreshing
            visible: page.mauiRefreshing
            color: page.mauiRefreshColor.a > 0 ? page.mauiRefreshColor : Theme.highlightColor
            width: Theme.itemSizeMedium
            height: Theme.itemSizeMedium
            x: (flick.width - width) / 2
            y: flick.contentY + Theme.paddingMedium
        }
    }

    // Silica chrome declared after the flickable so scrolled content paints under the header.
    Item {
        id: chrome
        x: 0
        y: 0
        width: parent.width
        height: page.topInset

        PageHeader { id: pageHeader; title: page.pageTitle }

        // Sailfish-style tab row; tapping one tells managed to switch the MAUI tab.
        Item {
            id: tabBar
            anchors.top: pageHeader.bottom
            width: parent.width
            visible: page.mauiTabs.length > 1
            height: visible ? Theme.itemSizeSmall : 0
            onHeightChanged: page.reportWindowGeometry()

            Row {
                id: tabRow
                anchors.fill: parent
                Repeater {
                    model: page.mauiTabs
                    BackgroundItem {
                        width: tabRow.width / Math.max(1, page.mauiTabs.length)
                        height: tabRow.height
                        objectName: "mauiTab_" + index
                        Label {
                            anchors.centerIn: parent
                            width: parent.width - 2 * Theme.paddingSmall
                            horizontalAlignment: Text.AlignHCenter
                            truncationMode: TruncationMode.Fade
                            text: modelData
                            color: index === page.mauiTabIndex ? Theme.highlightColor : Theme.secondaryColor
                            font.pixelSize: Theme.fontSizeMedium
                        }
                        Rectangle {
                            anchors.bottom: parent.bottom
                            width: parent.width
                            height: Theme.paddingSmall / 2
                            color: Theme.highlightColor
                            visible: index === page.mauiTabIndex
                        }
                        onClicked: page.mauiNotify("tab-selected", JSON.stringify({ index: index }))
                    }
                }
            }
        }
    }
}
