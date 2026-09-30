.pragma library
// Walks Silica control internals the public API does not expose. Stateless, so one shared copy serves every adapter.

// First descendant (or item itself) with this objectName, depth-first.
function findByObjectName(item, name) {
    if (!item)
        return null;
    if (item.objectName === name)
        return item;
    for (var i = 0; i < item.children.length; ++i) {
        var hit = findByObjectName(item.children[i], name);
        if (hit)
            return hit;
    }
    return null;
}

// The Silica "glass" items (they carry falloffRadius) under item, depth-first.
function collectGlass(item, out) {
    out = out || [];
    for (var i = 0; i < item.children.length; ++i) {
        var c = item.children[i];
        if (c.falloffRadius !== undefined)
            out.push(c);
        collectGlass(c, out);
    }
    return out;
}

function findValueFlow(item) {
    if (!item)
        return null;
    if (item.children.length >= 2 && item.children[0].font !== undefined && item.children[1].font !== undefined
            && item.children[0].text !== undefined)
        return item;
    for (var i = 0; i < item.children.length; ++i) {
        var hit = findValueFlow(item.children[i]);
        if (hit)
            return hit;
    }
    return null;
}

// A ValueButton's [title, value] labels (a Flow under contentItem, where BackgroundItem keeps its items); null if not found.
function valueLabels(button) {
    var flow = findValueFlow(button.contentItem ? button.contentItem : button);
    return flow ? [flow.children[0], flow.children[1]] : null;
}
