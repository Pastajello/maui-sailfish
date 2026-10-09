import QtQuick 2.6

// A gradient Background under any host (tracker S41, D10 a), created by the shim's generic
// mauiBackgroundGradient property (host_handles.cpp). spec is QtHostPaint.GradientSpec's JSON:
// {"t":"linear","x0","y0","x1","y1","stops":[[offset,"#AARRGGBB"],...]} or {"t":"radial","x0","y0","r",...} with points
// and radius relative to the host (MAUI's units for these brushes). The stops are drawn once into a ramp texture (a
// vertical Rectangle gradient); a fragment shader maps each pixel onto it, so the GPU draws the gradient and nothing
// repaints per frame.
Item {
    id: root
    objectName: "mauiBackgroundGradient"
    anchors.fill: parent
    z: -999

    property string spec: ""
    property var __s: null
    visible: __s !== null && width > 0 && height > 0

    onSpecChanged: {
        var s = null;
        try { s = spec.length > 0 ? JSON.parse(spec) : null; } catch (e) { s = null; }
        if (s && (!s.stops || s.stops.length === 0))
            s = null;
        var old = ramp.gradient.stops;
        var stops = [];
        for (var i = 0; s && i < s.stops.length; ++i) {
            var stop = Qt.createQmlObject("import QtQuick 2.6; GradientStop {}", ramp.gradient);
            stop.position = s.stops[i][0];
            stop.color = s.stops[i][1];
            stops.push(stop);
        }
        ramp.gradient.stops = stops;
        for (var j = 0; j < old.length; ++j)
            old[j].destroy();
        __s = s;
    }

    Rectangle {
        id: ramp
        width: 2
        height: 256
        gradient: Gradient {}
    }

    ShaderEffect {
        anchors.fill: parent
        property variant source: ShaderEffectSource { sourceItem: ramp; hideSource: true; smooth: true }
        property real mode: root.__s && root.__s.t === "radial" ? 1 : 0
        property point p0: root.__s ? Qt.point(root.__s.x0, root.__s.y0) : Qt.point(0, 0)
        property point p1: root.__s && root.__s.t !== "radial" ? Qt.point(root.__s.x1, root.__s.y1) : Qt.point(1, 1)
        property real radius: root.__s && root.__s.r !== undefined ? root.__s.r : 0.5
        property size area: Qt.size(Math.max(1, width), Math.max(1, height))
        fragmentShader: "
            varying highp vec2 qt_TexCoord0;
            uniform sampler2D source;
            uniform lowp float qt_Opacity;
            uniform highp float mode;
            uniform highp vec2 p0;
            uniform highp vec2 p1;
            uniform highp float radius;
            uniform highp vec2 area;
            void main() {
                highp vec2 uv = qt_TexCoord0;
                highp float t;
                if (mode < 0.5) {
                    highp vec2 d = p1 - p0;
                    t = dot(uv - p0, d) / max(dot(d, d), 0.000001);
                } else {
                    // MAUI's radius is relative to the larger side (as Android draws it).
                    t = length((uv - p0) * area) / max(radius * max(area.x, area.y), 0.000001);
                }
                gl_FragColor = texture2D(source, vec2(0.5, clamp(t, 0.0, 1.0))) * qt_Opacity;
            }"
    }
}
