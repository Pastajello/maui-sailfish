.pragma library
// The pulley adapters (interactions/PullDownMenu.qml, PushUpMenu.qml): attach to the flickable the thumb drags, clone
// onto the page's second drag surface, leave a surface the shim destroys, build the MenuItems. Stateless; the menu
// keeps the state and names its kind in __menu ("pull" or "push").

function slot(menu) {
    return menu.__menu === "pull" ? "pullDownMenu" : "pushUpMenu";
}

// The shim unparents a destroyed host at once and deletes it on the next loop pass. A menu on that surface (a clone is
// owned by its primary, not by the surface) would outlive its flickable and Silica's bindings would throw on it, so it
// moves to the page's sink flickable as soon as the surface is unparented.
function leaveSurface(menu) {
    var page = menu.mauiPage;
    var sink = page && page.mauiPulleySink ? page.mauiPulleySink() : null;
    if (!sink)
        return;
    menu.__leaving = true;   // __attachedTo is cleared next turn: clearing it here re-enters __surfaceParent
    var f = menu.__attachedTo;
    if (f[slot(menu)] === menu)
        f[slot(menu)] = null;
    menu.visible = false;
    menu.parent = sink.contentItem;
    menu.flickable = sink;
    if (menu.mauiForceFlick) {
        menu.destroy();   // a clone exists only for its surface
        return;
    }
    // A primary stays with its page and picks the remaining surface next turn.
    page.mauiDefer(function() {
        menu.__attachedTo = null;
        menu.__leaving = false;
        menu.visible = true;
        attach(menu);
    });
}

// Silica's resting highlight bar peeks into the viewport; on a clone (mid-page list), or on a push-up primary whose
// page flickable is viewport-tall, it paints as a stray line. Page activation resets _inactiveOpacity to 1.0, so the
// menu's highlightColor binding on __restBarHidden keeps the bar transparent unless the menu is actually pulled.
function suppressRestBar(menu, evenPrimary) {
    if (!menu.mauiForceFlick && !evenPrimary)
        return;
    menu.__restBarHidden = true;
    for (var i = 0; i < menu.children.length; ++i) {
        var c = menu.children[i];
        if (c && c.hasOwnProperty("_inactiveOpacity"))
            c._inactiveOpacity = 0;
    }
}

// Rebuilds only when the items changed: Component.onCompleted and onMauiItemsChanged both run it on a page's creation.
// A text/enabled-only change updates the items in place (the pickers keep their index).
function rebuild(menu) {
    if (menu.__built && menu.mauiItems === menu.__builtItems)
        return;
    var items = [];
    try { items = JSON.parse(menu.mauiItems); } catch (e) { items = []; }
    menu.__builtItems = menu.mauiItems;
    menu.__built = true;
    var current = menu.__items;
    if (items.length === current.length && items.length > 0) {
        for (var u = 0; u < items.length; ++u) {
            current[u].text = items[u].text || "";
            current[u].enabled = items[u].enabled !== false;
        }
        menu.mauiEvent("pulley-items", JSON.stringify({ menu: menu.__menu, count: current.length }));
        return;
    }
    for (var i = 0; i < current.length; ++i)
        current[i].destroy();
    var column = menu.mauiContentColumn ? menu.mauiContentColumn : menu;
    var fresh = [];
    for (var j = 0; j < items.length; ++j) {
        var mi = menu.__itemComponent.createObject(column,
                                                   { text: items[j].text || "", enabled: items[j].enabled !== false });
        mi.clicked.connect(picker(menu, j));
        fresh.push(mi);
    }
    menu.__items = fresh;
    menu.mauiEvent("pulley-items", JSON.stringify({ menu: menu.__menu, count: fresh.length }));
}

// One closure per index; a shared loop variable would report the last index for every item.
function picker(menu, index) {
    return function() {
        menu.mauiEvent("toolbar-activated", JSON.stringify({ menu: menu.__menu, index: index }));
    };
}

function pageFlickable(menu) {
    var page = menu.mauiPage;
    return (page && page.mauiFlickable) ? page.mauiFlickable() : null;
}

// The gesture fires on the flickable being dragged. With content in a hosted Silica list the page flickable stays
// non-interactive and a menu there is unreachable, so prefer the page flickable only while it scrolls.
function scroller(menu) {
    var pageFlick = pageFlickable(menu);
    if (pageFlick && pageFlick.interactive)
        return pageFlick;
    var hosted = hostedScroller(menu);
    return hosted ? hosted : pageFlick;
}

// The hosted Silica flickable the content scrolls in (list-view, or a vertical scroll-view).
function hostedScroller(menu) {
    var hosts = (menu.mauiPage && menu.mauiPage.__hosts) ? menu.mauiPage.__hosts : ({});
    for (var id in hosts) {
        var uri = hosts[id].uri;
        if (uri !== "list-view" && uri !== "scroll-view")
            continue;
        var it = hosts[id].item;
        if (uri === "scroll-view" && it && (it.mauiOrientation === "horizontal" || it.mauiOrientation === "neither"))
            continue;
        if (it && it !== menu && it.visible && it.interactive === true)
            return it;
    }
    return null;
}

function attach(menu) {
    var page = menu.mauiPage;
    if (!page)
        return;
    var f = menu.mauiForceFlick ? menu.mauiForceFlick : scroller(menu);
    if (!f)
        return;
    // The scroller may not exist yet (hosts come in reconcile order): retry a bounded number of times, then settle for
    // the page flickable. mauiDefer, not a Timer child (children land in _content) and not Qt.callLater (missing on
    // Qt 5.6).
    if (!f.interactive && !menu.mauiForceFlick && menu.__tries < 8) {
        menu.__tries++;
        page.mauiDefer(function() { attach(menu); });
        return;
    }
    menu.__tries = 0;
    // Parent into the flickable's contentItem so the pulley moves with the drag. Parenting to the flickable itself
    // breaks re-attaches: Silica only moves the menu to contentItem on the first `flickable` assignment, leaving it
    // off screen.
    var host = f.contentItem ? f.contentItem : f;
    if (menu.parent !== host)
        menu.parent = host;
    menu.flickable = f;
    f[slot(menu)] = menu;
    var pageFlick = pageFlickable(menu);
    // The page flickable and a hosted list can both be drag surfaces; one menu rides one flickable, so the other gets
    // a clone with the same items and events.
    if (!menu.mauiForceFlick) {
        var other = (f === pageFlick) ? hostedScroller(menu) : pageFlick;
        if (other && other !== f && other.interactive) {
            ensureClone(menu, other);
            if (f === pageFlick && menu.__menu === "push")
                suppressRestBar(menu, true);
        }
    }
    if (menu.__attachedTo !== f) {
        menu.__attachedTo = f;
        menu.mauiEvent("pulley-attached", JSON.stringify({
            id: menu.mauiId, menu: menu.__menu,
            pageFlick: f === pageFlick, interactive: !!f.interactive,
            items: menu.__items.length, contentH: f.contentHeight, h: f.height,
            kind: (f.model !== undefined) ? "list" : "flick",
            contentLen: menu._content ? menu._content.length : -1,
            kids: menu.children.length,
            anchor: menu.mauiContentColumn ? "col" : "null" }));
    }
}

function ensureClone(menu, other) {
    if (menu.__clone && menu.__clone.mauiForceFlick === other)
        return;
    if (menu.__clone) {
        menu.__clone.destroy();
        menu.__clone = null;
    }
    // The menu creates it from its own file, so the clone gets the menu's QML context and imports.
    var c = menu.__newClone({
        mauiPage: menu.mauiPage,
        mauiForceFlick: other,
        mauiSource: menu,
        mauiItems: menu.mauiItems,
        mauiDetached: true
    });
    if (!c)
        return;
    c.mauiEvent.connect(menu.mauiPage.mauiNotify);
    menu.__clone = c;
}
