.pragma library
// The adapter event contract in one place: every adapter event is a JSON object that names its host ("id"), and every
// page event names its page ("page"). Managed resolves hosts by "id" (QtHostPageRenderer.TryResolveHost) and never
// parses a bare id. Qt 5.6's V4 is ES5: no Object.assign, no spread.

function _merge(target, extra) {
    if (extra)
        for (var k in extra)
            if (extra.hasOwnProperty(k))
                target[k] = extra[k];
    return target;
}

// An adapter event: {"id": item.mauiId, ...extra}.
function emit(item, name, extra) {
    item.mauiEvent(name, JSON.stringify(_merge({ id: item.mauiId }, extra)));
}

// An adapter event that is an echo of a managed push while the shim applies a batch (mauiApplying): counted in
// mauiSuppressedCount when the adapter declares it, never sent.
function guarded(item, name, extra) {
    if (item.mauiApplying) {
        if (item.mauiSuppressedCount !== undefined)
            item.mauiSuppressedCount++;
        return;
    }
    emit(item, name, extra);
}

// A page event (MauiModelPage.mauiNotify): {"page": page.mauiPageId, ...extra}.
function pageEmit(page, name, extra) {
    page.mauiNotify(name, JSON.stringify(_merge({ page: page.mauiPageId }, extra)));
}
