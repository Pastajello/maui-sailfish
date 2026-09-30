import QtQuick 2.6
import Sailfish.Silica 1.0

// Adapter: MAUI View.ContextFlyout (MenuFlyout) -> Silica ContextMenu. Page-level (mauiDetached):
// Silica expands it around the long-press target. Managed detects the long press and calls
// openFor() via page.__openContextMenu; items are rebuilt on every open. Event: "context-activated".
ContextMenu {
    id: root

    property string mauiId: ""
    property string mauiProbe: ""
    signal mauiEvent(string name, string payload)

    // Managed property pushes set this flag; events fired meanwhile are suppressed (no echo).
    property bool mauiApplying: false

    // Page-level host: MauiModelPage skips canvas parenting and order moves for it.
    property bool mauiDetached: true
    property var mauiPage: null

    property var __items: []

    // MenuItems are created dynamically (a declared Component would land in contentColumn.data)
    // and parented to _contentColumn: parenting to the menu root bypasses the default-property
    // alias, leaving hasContent false so open() bails.
    function __makeItem(text, enabled, index) {
        var mi = Qt.createQmlObject("import QtQuick 2.6; import Sailfish.Silica 1.0; MenuItem {}", root._contentColumn);
        mi.text = text;
        mi.enabled = enabled;
        mi.clicked.connect(function() {
            root.mauiEvent("context-activated", JSON.stringify({ index: index }));
        });
        return mi;
    }

    // Rebuilds items from [{text,enabled}] and opens the menu around the target.
    function openFor(target, itemsJson) {
        var items = JSON.parse(itemsJson);
        for (var i = 0; i < __items.length; ++i)
            __items[i].destroy();
        __items = [];
        for (var j = 0; j < items.length; ++j)
            __items.push(__makeItem(items[j].text || "",
                                    items[j].enabled !== false, j));
        open(target);
    }
}
