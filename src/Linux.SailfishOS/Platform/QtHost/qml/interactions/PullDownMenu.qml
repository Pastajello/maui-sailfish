import QtQuick 2.6
import Sailfish.Silica 1.0

// Adapter: MAUI Page.ToolbarItems (Primary/Default) -> Silica PullDownMenu.
// Contract: mauiId / mauiProbe / mauiEvent — see controls/Label.qml.
//
// Page-level adapter: it reparents itself into the flickable the thumb actually
// drags and registers as its pullDownMenu. mauiItems is JSON [{text,enabled}].
PullDownMenu {
    id: root

    property string mauiId: ""
    property string mauiProbe: (flickable
        ? (flickable.interactive ? "attached:interactive" : "attached:inert")
        : "detached") + " items=" + __items.length
    signal mauiEvent(string name, string payload)

    // Adapter events are suppressed while managed pushes are applied (no echo loop).
    property bool mauiApplying: false

    // Detached: MauiModelPage skips canvas parenting; the menu positions itself.
    property bool mauiDetached: true
    property var mauiPage: null

    property string mauiItems: "[]"
    property var __items: []
    property int __tries: 0
    property var __attachedTo: null
    // A clone rides the page's other scroll surface and never clones further.
    property var mauiForceFlick: null
    property var __clone: null
    property var __cloneComp: null

    onMauiPageChanged: __attach()
    // mauiItems usually arrives in the createObject init, which does not fire the
    // change handler, so the first rebuild happens here.
    Component.onCompleted: { __attach(); __rebuild(); __suppressRestBar(); }
    onMauiItemsChanged: __rebuild()
    // Re-attach when the page flickable turns interactive or the page becomes
    // visible after a pop. Plain bindings, not Connections: any non-MenuItem child
    // lands in the _content default property and breaks the load.
    property bool __pageScroll: mauiPage !== null && mauiPage.mauiScrollEnabled
    on__PageScrollChanged: __attach()
    property bool __pageVisible: mauiPage !== null && mauiPage.visible
    on__PageVisibleChanged: if (__pageVisible) __attach()

    // _content is the default property and accepts only MenuItems, and it cannot be
    // assigned a JS array. So this hidden anchor keeps the content column reachable
    // and items are created directly in its parent (or root before it is parented).
    MenuItem { id: __anchor; visible: false }
    readonly property Item mauiContentColumn: __anchor.parent

    // On a clone the resting pulley bar would paint as a stray line mid-page, so
    // its inactive opacity is zeroed; an actual pull still highlights.
    function __suppressRestBar() {
        if (!mauiForceFlick)
            return;
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
            root.mauiEvent("pulley-items", JSON.stringify({ menu: "pull", count: __items.length }));
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
        root.mauiEvent("pulley-items", JSON.stringify({ menu: "pull", count: fresh.length }));
    }

    // One closure per index; a shared loop variable would report the last index.
    function __picker(index) {
        return function() {
            root.mauiEvent("toolbar-activated",
                           JSON.stringify({ menu: "pull", index: index }));
        };
    }

    // When content scrolls in a hosted list the page flickable is inert and a menu
    // there is unreachable, so prefer the page flickable only while it scrolls.
    function __scroller() {
        var pageFlick = (mauiPage && mauiPage.mauiFlickable) ? mauiPage.mauiFlickable() : null;
        if (pageFlick && pageFlick.interactive)
            return pageFlick;
        var list = __hostedScroller();
        if (list)
            return list;
        return pageFlick;
    }

    // The hosted list-view / scroll-view the content scrolls in.
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
        // The scroller may not exist yet on first attach; retry a few times, then
        // settle for the page flickable. mauiDefer, because a Timer child would break
        // _content and Qt 5.6 has no Qt.callLater.
        if (!f.interactive && !mauiForceFlick && __tries < 8) {
            __tries++;
            mauiPage.mauiDefer(__attach);
            return;
        }
        __tries = 0;
        // Parent to contentItem, not the flickable: Silica only moves the menu there
        // on the first `flickable` assignment, so a re-attach would leave it off screen.
        var host = f.contentItem ? f.contentItem : f;
        if (parent !== host)
            parent = host;
        flickable = f;
        f.pullDownMenu = root;
        // A menu rides one flickable; if the page has a second drag surface it gets a clone.
        if (!mauiForceFlick) {
            var pageFlick = mauiPage.mauiFlickable ? mauiPage.mauiFlickable() : null;
            var other = (f === pageFlick) ? __hostedScroller() : pageFlick;
            if (other && other !== f && other.interactive)
                __ensureClone(other);
        }
        if (__attachedTo !== f) {
            __attachedTo = f;
            var pf = mauiPage.mauiFlickable ? mauiPage.mauiFlickable() : null;
            var n = 0;
            try { n = JSON.parse(root.mauiItems).length; } catch (e) { n = -1; }
            root.mauiEvent("pulley-attached", JSON.stringify({
                id: root.mauiId, menu: "pull",
                pageFlick: f === pf, interactive: !!f.interactive,
                items: root.__items.length, contentH: f.contentHeight, h: f.height,
                kind: (f.model !== undefined) ? "list" : "flick",
                contentLen: root._content ? root._content.length : -1,
                kids: root.children.length,
                anchor: __anchor.parent ? "col" : "null" }));
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
            root.__cloneComp = Qt.createComponent("PullDownMenu.qml");
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
