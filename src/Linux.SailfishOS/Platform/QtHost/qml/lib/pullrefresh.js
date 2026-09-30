.pragma library
// Pull-to-refresh on a MAUI scroll surface (ListView and ScrollView adapters). The flickable keeps the state in
// __pullDepth/__pullDecided; this library is stateless.

// On contentY change: remember the deepest top overscroll. The release bounce resets contentY before
// movementEnded, so the depth must be kept from the drag.
function track(flick) {
    if (flick.mauiRefreshId.length > 0 && flick.contentY < flick.__pullDepth)
        flick.__pullDepth = flick.contentY;
    else if (flick.contentY >= 0) {
        flick.__pullDepth = 0;
        flick.__pullDecided = false;
    }
}

// On drag/movement end: one "refresh-requested" per overscroll deeper than threshold (the latch stops the bounce
// tail from firing a second refresh after a fast one completed).
function release(flick, threshold) {
    if (flick.mauiRefreshId.length === 0 || flick.__pullDecided)
        return;
    // Still shallow: momentum may deepen it before movementEnded.
    if (flick.__pullDepth >= -threshold)
        return;
    flick.__pullDecided = true;
    if (!flick.mauiRefreshing)
        flick.mauiEvent("refresh-requested", JSON.stringify({ id: flick.mauiRefreshId }));
}
