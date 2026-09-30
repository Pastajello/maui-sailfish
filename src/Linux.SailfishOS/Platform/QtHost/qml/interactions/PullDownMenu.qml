import QtQuick 2.6
import Sailfish.Silica 1.0
import "../lib/pulley.js" as Pulley

// Adapter: MAUI Page.ToolbarItems (Primary/Default) -> Silica PullDownMenu.
// Contract: mauiId / mauiProbe / mauiEvent — see controls/Label.qml.
//
// Page-level adapter (mauiDetached): it reparents itself into the flickable the thumb actually drags and registers as
// its pullDownMenu. mauiItems is JSON [{text,enabled}]. Events: "toolbar-activated", "pulley-attached",
// "pulley-items". The logic is shared with PushUpMenu.qml in lib/pulley.js; keep the two files in step.
PullDownMenu {
    id: root

    readonly property string __menu: "pull"

    property string mauiId: ""
    // Diag: which flickable carries the menu and whether the gesture can reach it.
    property string mauiProbe: (flickable
        ? (flickable.interactive ? "attached:interactive" : "attached:inert")
        : "detached") + " items=" + __items.length
    signal mauiEvent(string name, string payload)

    // Adapter events are suppressed while managed pushes are applied (no echo loop).
    property bool mauiApplying: false

    // Detached: MauiModelPage skips canvas parenting and order moves; the menu positions itself.
    property bool mauiDetached: true
    property var mauiPage: null

    // Page.IsBusy pulses the bar natively; a clone keeps its bar hidden (the busy timer would re-show it).
    busy: !mauiForceFlick && mauiPage !== null && mauiPage.mauiBusy && mauiPage.mauiBusyOnPulley

    property string mauiItems: "[]"
    property var __items: []
    property int __tries: 0
    property var __attachedTo: null
    // A clone rides the page's other scroll surface, mirrors mauiSource's items and never clones further.
    property var mauiForceFlick: null
    property var mauiSource: null
    property string __sourceItems: mauiSource ? mauiSource.mauiItems : ""
    on__SourceItemsChanged: if (mauiSource) mauiItems = __sourceItems
    property var __clone: null
    property var __cloneComp: null
    function __newClone(props) {
        if (!__cloneComp)
            __cloneComp = Qt.createComponent("PullDownMenu.qml");
        return __cloneComp.status === Component.Ready ? __cloneComp.createObject(root, props) : null;
    }

    function __attach() { Pulley.attach(root); }
    onMauiPageChanged: __attach()
    // Diagnostics: a menu left without its flickable throws in Silica's bindings; say which one and where.
    onFlickableChanged: if (!flickable && __attachedTo) console.warn("PULLEY pull lost its flickable: id=" + mauiId +
        " clone=" + (mauiForceFlick ? "yes" : "no") + " page=" + (mauiPage ? mauiPage.mauiPageId : "-"))

    // The surface's parent goes null the moment the shim destroys it (Pulley.leaveSurface).
    property Item __surfaceParent: __attachedTo ? __attachedTo.parent : null
    on__SurfaceParentChanged: if (__attachedTo && !__surfaceParent && !__leaving) Pulley.leaveSurface(root)
    property bool __leaving: false

    // mauiItems usually arrives in the createObject init, which does not fire the change handler, so the first
    // rebuild happens here.
    Component.onCompleted: { __attach(); Pulley.rebuild(root); Pulley.suppressRestBar(root, false); }
    onMauiItemsChanged: Pulley.rebuild(root)
    // Re-attach when the page flickable turns interactive (after setMauiScroll lands) or the page becomes visible
    // after a pop (a popped-to page attaches while still invisible mid-transition, so the hosted list got no clone).
    // Plain bindings, not Connections: any non-MenuItem child lands in the _content default property and breaks the
    // load.
    property bool __pageScroll: mauiPage !== null && mauiPage.mauiScrollEnabled
    on__PageScrollChanged: __attach()
    property bool __pageVisible: mauiPage !== null && mauiPage.visible
    on__PageVisibleChanged: if (__pageVisible) __attach()

    // On Silica/Qt 5.6 the default property _content only accepts MenuItems (a Repeater child breaks the load) and
    // cannot be assigned a JS array. So one hidden MenuItem anchors the content column and items are created parented
    // into it (or into the menu root during init).
    MenuItem { id: __anchor; visible: false }
    readonly property Item mauiContentColumn: __anchor.parent

    property bool __restBarHidden: false
    highlightColor: __restBarHidden && !active ? "transparent" : palette.highlightBackgroundColor

    // One compiled MenuItem component for every rebuild. A property, not a child: children land in _content.
    property Component __itemComponent: Component { MenuItem {} }
    property string __builtItems: ""
    property bool __built: false
}
