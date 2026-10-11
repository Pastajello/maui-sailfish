import QtQuick 2.6
import QtLocation 5.0
import QtPositioning 5.2
import Sailfish.Silica 1.0

// Adapter: Microsoft.Maui.Controls.Maps.Map -> QtLocation Map with OpenStreetMap tiles (SailfishMapHandler).
// Contract: mauiId / mauiProbe / mauiEvent / mauiApplying / mauiCommand (docs/custom-controls.md).
// QtLocation 5.4 (Jolla's build, with its OSM plugin): the street map is tile.openstreetmap.org, the second map type
// OpenTopoMap; the plugin takes no tile host, only a User-Agent ("useragent").
// Snapshot: mapType, showUser, scrollEnabled, zoomEnabled, pins, clusters, elements (JSON strings, see the handler). Commands: move-to {lat,lon,latDeg,lonDeg,animated}, show-info {pin},
// hide-info. Events: map-clicked, map-long-clicked {lat,lon}; pin-clicked, info-clicked {pin}; cluster-clicked
// {cluster,lat,lon}; element-clicked {element}; region-changed {lat,lon,latDeg,lonDeg}; user-location {lat,lon};
// adapter-ready.
Item {
    id: root

    property string mauiId: ""
    property string mauiProbe: "zoom=" + map.zoomLevel.toFixed(2) + " pins=" + pinItems.length + " clusters=" + clusterItems.length +
                               " elements=" + elementItems.filter(function (e) { return e.index >= 0; }).length + " points=" + pathPoints()
    function pathPoints() {
        var n = 0;
        for (var i = 0; i < elementItems.length; i++)
            if (elementItems[i].path !== undefined && elementItems[i].index >= 0)
                n += elementItems[i].path.length;
        return n;
    }
    signal mauiEvent(string name, string payload)
    property bool mauiApplying: false

    property string mapType: "Street"
    property bool showUser: false
    property bool scrollEnabled: true
    property bool zoomEnabled: true
    property string pins: "[]"
    property string clusters: "[]"
    property string elements: "[]"

    property var pinItems: []
    property var clusterItems: []
    property var elementItems: []
    property int infoPin: -1

    clip: true

    Plugin {
        id: osm
        name: "osm"
        // OSM's tile policy asks every app to identify itself.
        PluginParameter { name: "useragent"; value: "MauiSailfishMaps/0.1 (Sailfish OS; .NET MAUI)" }
    }

    Map {
        id: map
        anchors.fill: parent
        plugin: osm
        zoomLevel: 13
        gesture.enabled: root.scrollEnabled || root.zoomEnabled
        gesture.panEnabled: root.scrollEnabled
        gesture.pinchEnabled: root.zoomEnabled
        // Street → the first map type (OSM), anything else (Satellite, Hybrid) → the second (OpenTopoMap).
        activeMapType: supportedMapTypes.length > 1 && root.mapType !== "Street" ? supportedMapTypes[1] : supportedMapTypes[0]

        onCenterChanged: regionTimer.restart()
        onZoomLevelChanged: regionTimer.restart()
        onWidthChanged: regionTimer.restart()

        MouseArea {
            anchors.fill: parent
            propagateComposedEvents: true
            onClicked: {
                var c = map.toCoordinate(Qt.point(mouse.x, mouse.y));
                root.infoPin = -1;
                root.mauiEvent("map-clicked", JSON.stringify({ id: root.mauiId, lat: c.latitude, lon: c.longitude }));
                mouse.accepted = false;
            }
            onPressAndHold: {
                var c = map.toCoordinate(Qt.point(mouse.x, mouse.y));
                root.mauiEvent("map-long-clicked", JSON.stringify({ id: root.mauiId, lat: c.latitude, lon: c.longitude }));
            }
        }
    }

    // The visible region, a moment after the map settles (every pan frame would flood managed).
    Timer {
        id: regionTimer
        interval: 200
        onTriggered: {
            if (map.width <= 0 || map.height <= 0)
                return;
            var tl = map.toCoordinate(Qt.point(0, 0));
            var br = map.toCoordinate(Qt.point(map.width, map.height));
            if (!tl.isValid || !br.isValid || !map.center.isValid || isNaN(tl.latitude) || isNaN(br.longitude))
                return;
            root.mauiEvent("region-changed", JSON.stringify({
                id: root.mauiId, lat: map.center.latitude, lon: map.center.longitude,
                latDeg: Math.abs(tl.latitude - br.latitude), lonDeg: Math.abs(br.longitude - tl.longitude) }));
        }
    }

    Component {
        id: pinComponent
        MapQuickItem {
            property int index: -1
            property string label: ""
            anchorPoint.x: Theme.iconSizeSmall / 2
            anchorPoint.y: Theme.iconSizeSmall * 1.2
            // Drawn here, not a theme icon: it must read on every tile set, light or dark.
            sourceItem: Item {
                width: marker.width
                height: marker.height + tip.height / 2
                Rectangle {
                    id: tip
                    width: marker.width / 2.4
                    height: width
                    rotation: 45
                    anchors.horizontalCenter: marker.horizontalCenter
                    y: marker.height - height / 1.6
                    color: "white"
                }
                Rectangle {
                    id: marker
                    width: Theme.iconSizeSmall
                    height: width
                    radius: width / 2
                    color: Theme.highlightBackgroundColor
                    border.color: "white"
                    border.width: Math.max(2, Theme.paddingSmall / 2)
                }
                MouseArea {
                    anchors.fill: parent
                    anchors.margins: -Theme.paddingMedium
                    onClicked: root.mauiEvent("pin-clicked", JSON.stringify({ id: root.mauiId, pin: index }))
                }
            }
        }
    }

    Component {
        id: clusterComponent
        MapQuickItem {
            property int index: -1
            property int count: 0
            anchorPoint.x: bubble.width / 2
            anchorPoint.y: bubble.height / 2
            sourceItem: Rectangle {
                id: bubble
                width: Theme.itemSizeExtraSmall
                height: width
                radius: width / 2
                color: Theme.rgba(Theme.highlightBackgroundColor, 0.85)
                border.color: Theme.highlightColor
                border.width: 2
                Label {
                    anchors.centerIn: parent
                    text: count
                    font.pixelSize: Theme.fontSizeSmall
                    color: Theme.primaryColor
                }
                MouseArea {
                    anchors.fill: parent
                    onClicked: root.mauiEvent("cluster-clicked", JSON.stringify({
                        id: root.mauiId, cluster: index, lat: coordinate.latitude, lon: coordinate.longitude }))
                }
            }
        }
    }

    Component { id: polylineComponent; MapPolyline { property int index: -1 } }
    Component { id: polygonComponent; MapPolygon { property int index: -1 } }
    Component { id: circleComponent; MapCircle { property int index: -1 } }

    // The user's position: a dot in the highlight colour.
    PositionSource {
        id: position
        active: root.showUser && Qt.application.active
        updateInterval: 2000
        onPositionChanged: {
            var c = position.position.coordinate;
            if (!c.isValid)
                return;
            root.mauiEvent("user-location", JSON.stringify({ id: root.mauiId, lat: c.latitude, lon: c.longitude }));
        }
    }
    MapQuickItem {
        id: userDot
        visible: root.showUser && position.position.coordinate.isValid
        coordinate: position.position.coordinate
        anchorPoint.x: dot.width / 2
        anchorPoint.y: dot.height / 2
        sourceItem: Rectangle {
            id: dot
            width: Theme.paddingLarge * 1.5
            height: width
            radius: width / 2
            color: Theme.highlightColor
            border.color: "white"
            border.width: 3
        }
    }

    // The info window of the tapped pin: label and address, a tap on it is InfoWindowClicked.
    Rectangle {
        id: info
        visible: root.infoPin >= 0 && root.infoPin < root.pinItems.length
        property var pinItem: visible ? root.pinItems[root.infoPin] : null
        property point at: pinItem ? map.toScreenPosition(pinItem.coordinate) : Qt.point(0, 0)
        x: Math.max(Theme.paddingSmall, Math.min(root.width - width - Theme.paddingSmall, at.x - width / 2))
        y: at.y - height - Theme.itemSizeExtraSmall
        width: Math.min(root.width - 2 * Theme.paddingLarge, infoText.implicitWidth + 2 * Theme.paddingMedium)
        height: infoText.implicitHeight + 2 * Theme.paddingSmall
        radius: Theme.paddingSmall
        color: Theme.rgba(Theme.highlightDimmerColor, 0.92)
        Label {
            id: infoText
            anchors.centerIn: parent
            width: parent.width - 2 * Theme.paddingMedium
            wrapMode: Text.Wrap
            font.pixelSize: Theme.fontSizeSmall
            text: info.pinItem ? info.pinItem.label : ""
        }
        MouseArea {
            anchors.fill: parent
            onClicked: root.mauiEvent("info-clicked", JSON.stringify({ id: root.mauiId, pin: root.infoPin }))
        }
    }

    // OpenStreetMap's licence asks for this on every map.
    Label {
        anchors { right: parent.right; bottom: parent.bottom; margins: Theme.paddingSmall }
        text: "© OpenStreetMap contributors"
        font.pixelSize: Theme.fontSizeTiny
        color: "#303030"
        Rectangle { z: -1; anchors.fill: parent; anchors.margins: -4; color: "#c0ffffff"; radius: 3 }
    }

    onPinsChanged: rebuildPins()
    onClustersChanged: rebuildClusters()
    onElementsChanged: rebuildElements()

    function clearItems(list) {
        for (var i = 0; i < list.length; i++) {
            map.removeMapItem(list[i]);
            list[i].destroy();
        }
    }

    function rebuildPins() {
        clearItems(pinItems);
        var made = [];
        var rows = JSON.parse(pins);
        for (var i = 0; i < rows.length; i++) {
            var r = rows[i];   // [index, lat, lon, label, address, visible]
            var item = pinComponent.createObject(map, {
                index: r[0], coordinate: QtPositioning.coordinate(r[1], r[2]),
                label: r[4].length > 0 ? r[3] + "\n" + r[4] : r[3], visible: r[5] === 1 });
            map.addMapItem(item);
            made.push(item);
        }
        pinItems = made;
        if (infoPin >= made.length)
            infoPin = -1;
    }

    function rebuildClusters() {
        clearItems(clusterItems);
        var made = [];
        var rows = JSON.parse(clusters);
        for (var i = 0; i < rows.length; i++) {
            var r = rows[i];   // [index, lat, lon, count]
            var item = clusterComponent.createObject(map, { index: r[0], coordinate: QtPositioning.coordinate(r[1], r[2]), count: r[3] });
            map.addMapItem(item);
            made.push(item);
        }
        clusterItems = made;
    }

    function coords(path) {
        var out = [];
        for (var i = 0; i < path.length; i++)
            out.push(QtPositioning.coordinate(path[i][0], path[i][1]));
        return out;
    }

    function color(argb, fallback) {
        return argb && argb.length > 0 ? argb : fallback;
    }

    function rebuildElements() {
        clearItems(elementItems);
        var made = [];
        var rows = JSON.parse(elements);
        for (var i = 0; i < rows.length; i++) {
            var e = rows[i];
            var item = null;
            if (e.kind === "polyline") {
                // A darker casing under the line keeps it readable on any tiles (the way map apps draw routes).
                var casing = polylineComponent.createObject(map, { index: -1 });
                casing.line.width = (e.width + 3) * Theme.pixelRatio;
                casing.line.color = Qt.rgba(0, 0, 0, 0.45);
                casing.path = coords(e.path);
                casing.visible = e.visible;
                casing.z = e.z - 1;
                map.addMapItem(casing);
                made.push(casing);
                item = polylineComponent.createObject(map, { index: e.i });
                item.line.width = e.width * Theme.pixelRatio;
                item.line.color = color(e.stroke, Theme.highlightColor);
                item.path = coords(e.path);
            } else if (e.kind === "polygon") {
                item = polygonComponent.createObject(map, { index: e.i });
                item.border.width = e.width * Theme.pixelRatio;
                item.border.color = color(e.stroke, Theme.highlightColor);
                item.color = color(e.fill, "transparent");
                item.path = coords(e.path);
            } else if (e.kind === "circle") {
                item = circleComponent.createObject(map, { index: e.i });
                item.border.width = e.width * Theme.pixelRatio;
                item.border.color = color(e.stroke, Theme.highlightColor);
                item.color = color(e.fill, "transparent");
                item.center = QtPositioning.coordinate(e.center[0], e.center[1]);
                item.radius = e.radius;
            }
            if (!item)
                continue;
            item.visible = e.visible;
            item.z = e.z;
            map.addMapItem(item);
            made.push(item);
        }
        elementItems = made;
    }

    // A span → the zoom level that shows it: 256-pixel tiles, 360° of longitude at zoom 0.
    function zoomFor(latDeg, lonDeg) {
        var w = Math.max(1, map.width), h = Math.max(1, map.height);
        var byLon = Math.log(360 * w / 256 / Math.max(lonDeg, 1e-6)) / Math.LN2;
        var latRad = map.center.latitude * Math.PI / 180;
        var byLat = Math.log(360 * h / 256 / Math.max(latDeg / Math.cos(latRad), 1e-6)) / Math.LN2;
        return Math.max(map.minimumZoomLevel, Math.min(map.maximumZoomLevel, Math.min(byLon, byLat)));
    }

    function mauiCommand(json) {
        var c = JSON.parse(json);
        if (c.name === "move-to") {
            applyMove(c);
        } else if (c.name === "show-info") {
            infoPin = c.pin;
        } else if (c.name === "hide-info") {
            infoPin = -1;
        }
    }

    // The id arrives after creation, and an event in that same turn reaches managed before it knows the id: say
    // "ready" on the next turn, so a region requested before the adapter existed is sent again.
    Timer {
        id: readyTimer
        interval: 0
        onTriggered: root.mauiEvent("adapter-ready", JSON.stringify({ id: root.mauiId }))
    }
    onMauiIdChanged: if (mauiId.length > 0) readyTimer.restart()

    // A move before the map has a size cannot compute its zoom: it waits for the size.
    property var pendingMove: null
    onWidthChanged: if (pendingMove && width > 0 && height > 0) applyMove(pendingMove)
    onHeightChanged: if (pendingMove && width > 0 && height > 0) applyMove(pendingMove)

    function applyMove(c) {
        if (map.width <= 0 || map.height <= 0) {
            pendingMove = c;
            return;
        }
        pendingMove = null;
        map.center = QtPositioning.coordinate(c.lat, c.lon);
        map.zoomLevel = zoomFor(c.latDeg, c.lonDeg);
        regionTimer.restart();
    }
}
