.pragma library

/* Stateless vector-painting helpers shared by shapes/Shape.qml and shapes/GraphicsView.qml,
 * which paint through a Canvas Context2D in the Qt scene graph. Every function takes the
 * context it draws into. All coordinates are already device px (managed applies Density).
 *
 * Path op encoding (managed QtHostShapes.PathOps):
 *   ["m", x, y]                              moveTo
 *   ["l", x, y]                              lineTo
 *   ["q", cx, cy, x, y]                      quadraticCurveTo
 *   ["c", c1x, c1y, c2x, c2y, x, y]          bezierCurveTo
 *   ["a", x1, y1, x2, y2, startDeg, endDeg, clockwise, connect]  arc in a box
 *                                          (connect=1 → lineTo the arc start, as PathF.AddArc)
 *   ["z"]                                    closePath
 */

/* Context2D state persists between paints, so each paint starts by resetting it; the W3C
 * properties are restored by hand if reset() is missing or throws. */
function resetContext(ctx) {
    if (typeof ctx.reset === "function") {
        try {
            ctx.reset();
            ctx.beginPath();
            return;
        } catch (e) {
            /* fall through to the manual normalization */
        }
    }
    if (typeof ctx.setTransform === "function")
        ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.globalAlpha = 1;
    ctx.globalCompositeOperation = "source-over";
    ctx.fillStyle = "#000000";
    ctx.strokeStyle = "#000000";
    ctx.lineWidth = 1;
    ctx.lineCap = "butt";
    ctx.lineJoin = "miter";
    ctx.miterLimit = 10;
    ctx.shadowOffsetX = 0;
    ctx.shadowOffsetY = 0;
    ctx.shadowBlur = 0;
    ctx.shadowColor = "transparent";
    ctx.font = "10px sans-serif";
    ctx.textAlign = "start";
    ctx.textBaseline = "alphabetic";
    if (typeof ctx.lineDashOffset !== "undefined")
        ctx.lineDashOffset = 0;
    ctx.beginPath();
}

/* Builds the current path from an op list (see the encoding above). */
function buildPath(ctx, ops) {
    if (!ops)
        return 0;
    ctx.beginPath();
    for (var i = 0; i < ops.length; ++i) {
        var o = ops[i];
        switch (o[0]) {
        case "m": ctx.moveTo(o[1], o[2]); break;
        case "l": ctx.lineTo(o[1], o[2]); break;
        case "q": ctx.quadraticCurveTo(o[1], o[2], o[3], o[4]); break;
        case "c": ctx.bezierCurveTo(o[1], o[2], o[3], o[4], o[5], o[6]); break;
        case "a": arcInBox(ctx, o[1], o[2], o[3], o[4], o[5], o[6], o[7], o[8]); break;
        case "z": ctx.closePath(); break;
        }
    }
    return ops.length;
}

/* PathF arc: bounding box + start/end angle in degrees + direction, added to the current path. */
function arcInBox(ctx, x1, y1, x2, y2, startDeg, endDeg, clockwise, connect) {
    ellipseArc(ctx, (x1 + x2) / 2, (y1 + y2) / 2,
               Math.abs(x2 - x1) / 2, Math.abs(y2 - y1) / 2,
               startDeg, endDeg, clockwise, connect);
}

/* Elliptical arc as cubic Béziers emitted into the path. Qt 5.6 has no ellipse(), and the
 * scale()+arc() idiom also distorts the stroke because Qt replays the path with the matrix;
 * explicit Béziers are exact in path space. Uses the standard per-segment approximation
 *   alpha = sin(d) * (sqrt(4 + 3*tan(d/2)^2) - 1) / 3   (d = segment sweep). */
function ellipseArc(ctx, cx, cy, rx, ry, startDeg, endDeg, clockwise, connect) {
    if (!(rx > 0) || !(ry > 0) || startDeg === endDeg)
        return;
    var HALF = Math.PI;
    var DEG = HALF / 180;
    var a0 = startDeg * DEG;
    var sweep = (endDeg - startDeg) * DEG;
    /* MAUI and Canvas2D both measure clockwise from 3 o'clock in y-down space;
     * only the sweep direction needs normalizing. */
    if (clockwise) {
        while (sweep <= 0)
            sweep += HALF * 2;
    } else {
        while (sweep >= 0)
            sweep -= HALF * 2;
    }
    var segments = Math.min(16, Math.ceil(Math.abs(sweep) / (HALF / 2)));
    var step = sweep / segments;
    var tan = Math.tan(step / 2);
    var alpha = Math.sin(step) * (Math.sqrt(4 + 3 * tan * tan) - 1) / 3;
    var x = cx + rx * Math.cos(a0);
    var y = cy + ry * Math.sin(a0);
    if (connect)
        ctx.lineTo(x, y);
    else
        ctx.moveTo(x, y);
    for (var i = 0; i < segments; ++i) {
        var s0 = a0 + step * i;
        var s1 = s0 + step;
        var c0 = Math.cos(s0), n0 = Math.sin(s0);
        var c1 = Math.cos(s1), n1 = Math.sin(s1);
        ctx.bezierCurveTo(
            cx + rx * (c0 - alpha * n0), cy + ry * (n0 + alpha * c0),
            cx + rx * (c1 + alpha * n1), cy + ry * (n1 - alpha * c1),
            cx + rx * c1,                cy + ry * n1);
    }
}

/* Full ellipse inside the box [x, y, w, h], added to the current path. */
function ellipseBox(ctx, x, y, w, h) {
    /* Qt's Context2D has a non-standard exact ellipse(); Béziers are the fallback. */
    if (typeof ctx.ellipse === "function" && w > 0 && h > 0) {
        ctx.ellipse(x, y, w, h);
        ctx.closePath();
        return;
    }
    ellipseArc(ctx, x + w / 2, y + h / 2, Math.abs(w) / 2, Math.abs(h) / 2,
               0, 360, true, false);
    ctx.closePath();
}

/* ICanvas.DrawArc/FillArc: a new path with the arc; `closed` closes it with a chord (SKPath.Close). */
function arcBox(ctx, x, y, w, h, startDeg, endDeg, clockwise, closed) {
    ctx.beginPath();
    ellipseArc(ctx, x + w / 2, y + h / 2, Math.abs(w) / 2, Math.abs(h) / 2,
               startDeg, endDeg, clockwise, false);
    if (closed)
        ctx.closePath();
}

/* ICanvas.FillArc: MAUI's Skia backend fills the pie wedge, not the chord, so the contour
 * runs center → arc → center. */
function pieBox(ctx, x, y, w, h, startDeg, endDeg, clockwise) {
    var cx = x + w / 2;
    var cy = y + h / 2;
    ctx.beginPath();
    ctx.moveTo(cx, cy);
    ellipseArc(ctx, cx, cy, Math.abs(w) / 2, Math.abs(h) / 2,
               startDeg, endDeg, clockwise, true);
    ctx.closePath();
}

/* MAUI Stretch (0 None, 1 Fill, 2 Uniform, 3 UniformToFill) → {sx, sy, tx, ty} mapping the
 * path's natural bounds [x, y, w, h] into the item rect. Uniform modes center the content. */
function stretchTransform(natural, w, h, aspect) {
    var nx = natural && natural.length > 0 ? natural[0] : 0;
    var ny = natural && natural.length > 1 ? natural[1] : 0;
    var nw = natural && natural.length > 2 ? natural[2] : 0;
    var nh = natural && natural.length > 3 ? natural[3] : 0;
    /* A degenerate axis (horizontal/vertical Line) keeps scale 1 instead of dividing by zero. */
    var fx = nw > 0 ? w / nw : 1;
    var fy = nh > 0 ? h / nh : 1;
    var sx = 1, sy = 1;
    if (aspect === 1) {                          /* Fill */
        sx = fx;
        sy = fy;
    } else if (aspect === 2 || aspect === 3) {   /* Uniform / UniformToFill */
        var s = aspect === 2 ? Math.min(fx, fy) : Math.max(fx, fy);
        sx = s;
        sy = s;
    }
    var cw = nw > 0 ? nw * sx : w;
    var ch = nh > 0 ? nh * sy : h;
    return {
        sx: sx,
        sy: sy,
        /* None is the identity in MAUI (Stretch.GetTransform): the path is not re-based to the origin. */
        tx: aspect === 0 ? 0 : (w - cw) / 2 - nx * sx,
        ty: aspect === 0 ? 0 : (h - ch) / 2 - ny * sy
    };
}

/* Dash pattern [offset, d0, d1, ...] in device px (managed pre-multiplies by stroke width).
 * Returns false when setLineDash is missing so the adapter can report the skipped op. */
function applyDash(ctx, dash) {
    if (!dash || dash.length === 0)
        return true;                       /* nothing requested — not a skip */
    if (typeof ctx.setLineDash !== "function")
        return false;
    var pattern = [];
    for (var i = 1; i < dash.length; ++i)
        pattern.push(dash[i]);
    ctx.setLineDash(pattern);
    if (dash[0])
        ctx.lineDashOffset = dash[0];
    return true;
}

/* Dashing without setLineDash (Qt 5.6 has none): flatten the path into view-space polylines
 * (dash lengths are view px), cut them by the pattern, and emit dashes mapped back into path
 * space so the caller strokes them under its own transform. */
function flatten(ops, t) {
    return flattenWith(function(rec) { buildPath(rec, ops); }, t);
}

/* `draw(rec)` builds the path into a recorder that speaks the Context2D path API. */
function flattenWith(draw, t) {
    var lines = [];
    var cur = null;
    var sx = t ? t.sx : 1, sy = t ? t.sy : 1, tx = t ? t.tx : 0, ty = t ? t.ty : 0;
    var px = 0, py = 0, startX = 0, startY = 0;
    function map(x, y) { return [tx + x * sx, ty + y * sy]; }
    function begin(x, y) {
        cur = [map(x, y)];
        lines.push(cur);
        px = x; py = y; startX = x; startY = y;
    }
    function add(x, y) {
        if (!cur)
            begin(px, py);
        cur.push(map(x, y));
        px = x; py = y;
    }
    function steps(len) { return Math.max(4, Math.min(64, Math.ceil(len / 6))); }
    var rec = {
        moveTo: function(x, y) { begin(x, y); },
        lineTo: function(x, y) { add(x, y); },
        quadraticCurveTo: function(cx, cy, x, y) {
            var x0 = px, y0 = py;
            var n = steps((Math.abs(cx - x0) + Math.abs(cy - y0) + Math.abs(x - cx) + Math.abs(y - cy)) * Math.max(Math.abs(sx), Math.abs(sy)));
            for (var i = 1; i <= n; ++i) {
                var u = i / n, v = 1 - u;
                add(v * v * x0 + 2 * v * u * cx + u * u * x, v * v * y0 + 2 * v * u * cy + u * u * y);
            }
        },
        bezierCurveTo: function(c1x, c1y, c2x, c2y, x, y) {
            var x0 = px, y0 = py;
            var n = steps((Math.abs(c1x - x0) + Math.abs(c1y - y0) + Math.abs(c2x - c1x) + Math.abs(c2y - c1y) +
                           Math.abs(x - c2x) + Math.abs(y - c2y)) * Math.max(Math.abs(sx), Math.abs(sy)));
            for (var i = 1; i <= n; ++i) {
                var u = i / n, v = 1 - u;
                add(v * v * v * x0 + 3 * v * v * u * c1x + 3 * v * u * u * c2x + u * u * u * x,
                    v * v * v * y0 + 3 * v * v * u * c1y + 3 * v * u * u * c2y + u * u * u * y);
            }
        },
        closePath: function() {
            if (cur && cur.length > 0) {
                add(startX, startY);
                cur = null;
            }
        },
        rect: function(x, y, w, h) {
            begin(x, y);
            add(x + w, y);
            add(x + w, y + h);
            add(x, y + h);
            add(x, y);
            cur = null;
        },
        beginPath: function() {}
    };
    draw(rec);
    return lines;
}

function dashPath(ctx, ops, dash, t) {
    return dashWith(ctx, function(rec) { buildPath(rec, ops); }, dash, t);
}

function dashWith(ctx, draw, dash, t) {
    var pattern = [];
    var total = 0;
    for (var i = 1; i < dash.length; ++i) {
        var d = Math.max(0, dash[i]);
        pattern.push(d);
        total += d;
    }
    if (pattern.length % 2 === 1)          /* SVG/WPF: an odd list repeats once */
        pattern = pattern.concat(pattern), total *= 2;
    ctx.beginPath();
    if (!(total > 0)) {
        draw(ctx);
        return 1;
    }
    var sx = t ? t.sx : 1, sy = t ? t.sy : 1, tx = t ? t.tx : 0, ty = t ? t.ty : 0;
    function back(p) { return [(p[0] - tx) / (sx || 1), (p[1] - ty) / (sy || 1)]; }
    var lines = flattenWith(draw, t);
    var dashes = 0;
    for (var l = 0; l < lines.length; ++l) {
        var pts = lines[l];
        /* The pattern restarts on every contour, shifted by the offset. */
        var offset = ((dash[0] || 0) % total + total) % total;
        var idx = 0;
        while (offset >= pattern[idx]) {
            offset -= pattern[idx];
            idx = (idx + 1) % pattern.length;
        }
        var left = pattern[idx] - offset;      /* remaining length of the current entry */
        var on = idx % 2 === 0;
        if (on) {
            var s0 = back(pts[0]);
            ctx.moveTo(s0[0], s0[1]);
        }
        for (var k = 1; k < pts.length; ++k) {
            var ax = pts[k - 1][0], ay = pts[k - 1][1];
            var bx = pts[k][0], by = pts[k][1];
            var seg = Math.sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
            var pos = 0;
            while (seg - pos > left) {
                pos += left;
                var f = pos / seg;
                var q = back([ax + (bx - ax) * f, ay + (by - ay) * f]);
                if (on) {
                    ctx.lineTo(q[0], q[1]);
                    dashes++;
                } else {
                    ctx.moveTo(q[0], q[1]);
                }
                on = !on;
                idx = (idx + 1) % pattern.length;
                left = pattern[idx];
            }
            left -= seg - pos;
            if (on) {
                var e = back(pts[k]);
                ctx.lineTo(e[0], e[1]);
            }
        }
        if (on)
            dashes++;
    }
    return dashes;
}

/* Paint spec (QtHostShapes.PaintSpec) → Context2D style:
 *   ["solid",  "#AARRGGBB"]
 *   ["linear", x0, y0, x1, y1, [[offset, "#AARRGGBB"], ...]]
 *   ["radial", cx, cy, r,      [[offset, "#AARRGGBB"], ...]]
 * Returns null for "no paint". `color` is a fallback for callers passing a plain QML color. */
function paintStyle(ctx, spec, color) {
    if (spec && spec.length > 0) {
        if (spec[0] === "solid")
            return spec[1];                  /* "#AARRGGBB" — Qt parses CSS colors */
        var linear = spec[0] === "linear";
        var g = linear ? ctx.createLinearGradient(spec[1], spec[2], spec[3], spec[4])
                       : ctx.createRadialGradient(spec[1], spec[2], 0, spec[1], spec[2], spec[3]);
        var stops = linear ? spec[5] : spec[4];
        if (g && stops) {
            for (var i = 0; i < stops.length; ++i)
                g.addColorStop(stops[i][0], stops[i][1]);
            return g;
        }
    }
    if (color !== undefined && color !== null && String(color) !== "transparent" && color.a > 0)
        return Qt.rgba(color.r, color.g, color.b, color.a);
    return null;
}

/* Diag: FNV-1a over the alpha channel plus the painted-pixel count. On demand only
 * (mauiWantHash), since the readback forces the Image render target for that frame. */
function pixelHash(ctx, w, h) {
    var iw = Math.max(1, Math.ceil(w));
    var ih = Math.max(1, Math.ceil(h));
    var data;
    try {
        data = ctx.getImageData(0, 0, iw, ih).data;
    } catch (e) {
        return "err:" + e;
    }
    var hash = 2166136261;
    var painted = 0;
    for (var i = 3; i < data.length; i += 4) {
        var a = data[i];
        if (a > 0)
            painted++;
        hash = ((hash ^ a) * 16777619) >>> 0;
    }
    return (hash >>> 0) + ":" + painted + "/" + (iw * ih);
}

// "PAINT-SLOW …" text when a Canvas paint started at t0 (0 = not timed) took over 15 ms; "" otherwise.
function slowPaintMessage(tag, name, width, height, t0) {
    var ms = Date.now() - t0;
    if (t0 <= 0 || ms <= 15)
        return "";
    return "PAINT-SLOW " + tag + " " + name + " " + Math.round(width) + "x" + Math.round(height) + " " + ms + " ms";
}
