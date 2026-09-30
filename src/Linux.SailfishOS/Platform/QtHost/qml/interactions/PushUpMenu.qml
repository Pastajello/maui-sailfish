import QtQuick 2.6
import Sailfish.Silica 1.0

// Adapter: MAUI Page.ToolbarItems (Order=Secondary) -> Silica PushUpMenu.
// Page-level (mauiDetached): attaches itself to the flickable the thumb actually drags.
// mauiItems is JSON [{text,enabled}]. Events: "toolbar-activated", "pulley-attached".
PushUpMenu {
    id: root

    property string mauiId: ""
    // Diag: which flickable carries the menu and whether the gesture can reach it.
    property string mauiProbe: (flickable
        ? (flickable.interactive ? "attached:interactive" : "attached:inert")
        : "detached") + " items=" + __items.length
    signal mauiEvent(string name, string payload)

    // Managed property pushes set this flag; events fired meanwhile are suppressed (no echo).
    property bool mauiApplying: false

    // Page-level host: MauiModelPage skips canvas parenting and order moves for it.
    property bool mauiDetached: true
    property var mauiPage: null

    property string mauiItems: "[]"
    property var __items: []
    property int __tries: 0
    property var __attachedTo: null
    // A clone attaches to an explicitly chosen flickable and never clones further.
    property var mauiForceFlick: null
    property var __clone: null
    property var __cloneComp: null

    onMauiPageChanged: __attach()
    // Init-time mauiItems (createObject) fires no change handler, so rebuild once complete.
    Component.onCompleted: { __attach(); __rebuild(); __suppressRestBar(); }
    onMauiItemsChanged: __rebuild()
    // The page flickable turns interactive only after setMauiScroll lands, so re-attach then.
    // A plain binding, not a Connections child: non-MenuItem children land in _content and break the load.
    property bool __pageScroll: mauiPage !== null && mauiPage.mauiScrollEnabled
    on__PageScrollChanged: __attach()
    // A popped-to page attaches while still invisible mid-transition, so the hosted list gets
    // no clone; re-attach once visible.
    property bool __pageVisible: mauiPage !== null && mauiPage.visible
    on__PageVisibleChanged: if (__pageVisible) __attach()

    // On Silica/Qt 5.6 the default property _content only accepts MenuItems (a Repeater child
    // breaks the load) and cannot be assigned a JS array. So one hidden MenuItem anchors the
    // content column and items are created parented into it (or into the menu root during init).
    MenuItem { id: __anchor; visible: false }
    // Diag: the content column the items must live in.
    readonly property Item mauiContentColumn: __anchor.parent

    // Silica's resting highlight bar peeks into the viewport; on a clone (mid-page list) or on a
    // primary whose page flickable is viewport-tall it paints as a stray line. Hide it there.
    // Page activation resets _inactiveOpacity to 1.0, so the color binding keeps the bar
    // transparent unless the menu is actually pulled.
    property bool __restBarHidden: false
    highlightColor: __restBarHidden && !active ? "transparent" : palette.highlightBackgroundColor
    function __suppressRestBar(evenPrimary) {
        if (!mauiForceFlick && !evenPrimary)
            return;
        __restBarHidden = true;
        for (var i = 0; i < children.length; ++i) {
            var c = children[i];
            if (c && c.hasOwnProperty("_inactiveOpacity"))
                c._inactiveOpacity = 0;
        }
    }

    // One compiled MenuItem component for every rebuild (a createQmlObject per item compiled QML each time). A property,
    // not a child: children land in the menu's _content. Rebuilding only when the items changed matters because
    // Component.onCompleted and onMauiItemsChanged both run it on a page's creation; a text/enabled-only change
    // updates the items in place (the pickers keep their index).
    property Component __itemComponent: Component { MenuItem {} }
    property string __builtItems: ""
    property bool __built: false
    function __rebuild() {
        if (__built && mauiItems === __builtItems)
            return;
        var items = [];
        try { items = JSON.parse(mauiItems); } catch (e) { items = []; }
        __builtItems = mauiItems;
        __built = true;
        if (items.length === __items.length && items.length > 0) {
            for (var u = 0; u < items.length; ++u) {
                __items[u].text = items[u].text || "";
                __items[u].enabled = items[u].enabled !== false;
            }
            root.mauiEvent("pulley-items", JSON.stringify({ menu: "push", count: __items.length }));
            return;
        }
        for (var i = 0; i < __items.length; ++i)
            __items[i].destroy();
        var column = __anchor.parent ? __anchor.parent : root;
        var fresh = [];
        for (var j = 0; j < items.length; ++j) {
            var mi = __itemComponent.createObject(column,
                                                 { text: items[j].text || "", enabled: items[j].enabled !== false });
            mi.clicked.connect(__picker(j));
            fresh.push(mi);
        }
        __items = fresh;
        root.mauiEvent("pulley-items", JSON.stringify({ menu: "push", count: fresh.length }));
    }

    // One closure per index; a shared loop closure would report the last index for every item.
    function __picker(index) {
        return function() {
            root.mauiEvent("toolbar-activated",
                           JSON.stringify({ menu: "push", index: index }));
        };
    }

    // The gesture fires on the flickable being dragged. With content in a hosted Silica list
    // the page flickable stays non-interactive, so fall back to the hosted scroller
    // (the canonical shape is SilicaListView { PushUpMenu {} }).
    function __scroller() {
        var pageFlick = (mauiPage && mauiPage.mauiFlickable) ? mauiPage.mauiFlickable() : null;
        if (pageFlick && pageFlick.interactive)
            return pageFlick;
        var list = __hostedScroller();
        if (list)
            return list;
        return pageFlick;
    }

    // The hosted Silica flickable the content scrolls in (list-view or scroll-view).
    function __hostedScroller() {
        var hosts = (mauiPage && mauiPage.__hosts) ? mauiPage.__hosts : ({});
        for (var id in hosts) {
            var uri = hosts[id].uri;
            if (uri !== "list-view" && uri !== "scroll-view")
                continue;
            var it = hosts[id].item;
            // A pulley needs a vertical scroller.
            if (uri === "scroll-view" && it && (it.mauiOrientation === "horizontal" || it.mauiOrientation === "neither"))
                continue;
            if (it && it !== root && it.visible && it.interactive === true)
                return it;
        }
        return null;
    }

    function __attach() {
        if (!mauiPage)
            return;
        var f = mauiForceFlick ? mauiForceFlick : __scroller();
        if (!f)
            return;
        // The scroller may not exist yet (hosts come in reconcile order): retry a bounded number of
        // times, then settle for the page flickable. mauiDefer, not a Timer child (children land in
        // _content) and not Qt.callLater (missing on Qt 5.6).
        if (!f.interactive && !mauiForceFlick && __tries < 8) {
            __tries++;
            mauiPage.mauiDefer(__attach);
            return;
        }
        __tries = 0;
        // Parent into the flickable's contentItem so the pulley moves with the drag. Parenting to the
        // flickable itself breaks re-attaches: Silica only moves it to contentItem on the first
        // `flickable` assignment, leaving the menu above the screen.
        var host = f.contentItem ? f.contentItem : f;
        if (parent !== host)
            parent = host;
        flickable = f;
        f.pushUpMenu = root;
        // The page flickable and a hosted list can both be drag surfaces; one menu rides one
        // flickable, so the other gets a clone with the same items and events.
        if (!mauiForceFlick) {
            var pageFlick = mauiPage.mauiFlickable ? mauiPage.mauiFlickable() : null;
            var other = (f === pageFlick) ? __hostedScroller() : pageFlick;
            if (other && other !== f && other.interactive) {
                __ensureClone(other);
                if (f === pageFlick)
                    __suppressRestBar(true);
            }
        }
        if (__attachedTo !== f) {
            __attachedTo = f;
            var pf = mauiPage.mauiFlickable ? mauiPage.mauiFlickable() : null;
            var n = 0;
            try { n = JSON.parse(root.mauiItems).length; } catch (e) { n = -1; }
            root.mauiEvent("pulley-attached", JSON.stringify({
                id: root.mauiId, menu: "push",
                pageFlick: f === pf, interactive: !!f.interactive,
                items: root.__items.length, contentH: f.contentHeight, h: f.height,
                kind: (f.model !== undefined) ? "list" : "flick" }));
        }
    }

    function __ensureClone(other) {
        if (root.__clone && root.__clone.mauiForceFlick === other)
            return;
        if (root.__clone) {
            root.__clone.destroy();
            root.__clone = null;
        }
        if (!root.__cloneComp)
            root.__cloneComp = Qt.createComponent("PushUpMenu.qml");
        if (!root.__cloneComp || root.__cloneComp.status !== Component.Ready)
            return;
        var c = root.__cloneComp.createObject(root, {
            mauiPage: root.mauiPage,
            mauiForceFlick: other,
            mauiDetached: true
        });
        if (!c)
            return;
        c.mauiItems = Qt.binding(function() { return root.mauiItems; });
        c.mauiEvent.connect(root.mauiPage.mauiNotify);
        root.__clone = c;
    }
}
