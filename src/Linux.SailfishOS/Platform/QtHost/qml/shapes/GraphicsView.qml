import QtQuick 2.6
import Sailfish.Silica 1.0
import "pathops.js" as PathOps

// Adapter: MAUI GraphicsView + IDrawable -> QtQuick Canvas.
// Contract: mauiId / mauiProbe / mauiEvent — see controls/Label.qml.
//
// QtHostCanvasRecorder records IDrawable.Draw calls into mauiCommands and this
// adapter replays them with Context2D, so drawing happens in Qt rather than a
// managed rasterizer. Commands are in MAUI dp, scaled once by mauiScale; the op
// encoding must match QtHostCanvasRecorder.
Canvas {
    id: root

    property string mauiId: ""
    property string mauiProbe: ""
    signal mauiEvent(string name, string payload)

    // Adapter events are suppressed while managed pushes are applied (no echo loop).
    property bool mauiApplying: false

    property var mauiCommands: []
    property real mauiScale: 1
    property color mauiBackground: "transparent"
    property string mauiFontFamily: ""

    // --- Diagnostics, read back through the native handle ---
    property int mauiPaints: 0
    property int mauiExecutedOps: 0
    property int mauiSkippedOps: 0
    property int mauiGradOps: 0
    property int mauiClipOps: 0
    property string mauiPixelHash: "0"
    property bool mauiWantHash: false
    property int mauiDashes: 0

    // Qt 5.6 cannot change strategy/target once the context exists, and pixel
    // readback (mauiWantHash) is unsupported in Cooperative mode, hence Immediate + FBO.
    renderStrategy: Canvas.Immediate
    renderTarget: Canvas.FramebufferObject

    onMauiWantHashChanged: requestPaint()
    onMauiCommandsChanged: requestPaint()
    onMauiScaleChanged: requestPaint()
    onMauiBackgroundChanged: requestPaint()
    onMauiFontFamilyChanged: requestPaint()
    onWidthChanged: requestPaint()
    onHeightChanged: requestPaint()

    property double __paintT0: 0   // logs PAINT-SLOW for paints over 15 ms
    onPainted: { var slow = PathOps.slowPaintMessage("graphics", objectName, width, height, __paintT0); if (slow) console.log(slow); }
    onPaint: {
        __paintT0 = Date.now();
        var ctx = getContext("2d");
        PathOps.resetContext(ctx);
        ctx.clearRect(0, 0, width, height);
        mauiPaints++;
        mauiDashes = 0;
        var st = {
            fill: null, stroke: "#000000", lineWidth: 1, fontColor: "#000000",
            fontSize: 12, fontName: mauiFontFamily, fontWeight: 400, fontItalic: false,
            alpha: 1, executed: 0, skipped: 0
        };
        if (mauiBackground.a > 0) {
            ctx.fillStyle = PathOps.paintStyle(ctx, [], mauiBackground);
            ctx.fillRect(0, 0, width, height);
        }
        var s = mauiScale > 0 ? mauiScale : 1;
        ctx.save();
        ctx.scale(s, s);
        exec(ctx, mauiCommands, st);
        ctx.restore();
        mauiExecutedOps = st.executed;
        mauiSkippedOps = st.skipped;
        if (mauiWantHash)
            mauiPixelHash = PathOps.pixelHash(ctx, width, height);
        if (mauiWantHash)
            mauiEvent("canvas-painted", JSON.stringify({
                id: mauiId, ops: st.executed, skipped: st.skipped,
                paints: mauiPaints, hash: mauiPixelHash
            }));
    }

    /* Replays the recorded stream. st carries MAUI paint state; a gradient fill
     * spec can only be applied at fill time, so fillSetup resolves it per op. */
    function exec(ctx, cmds, st) {
        if (!cmds)
            return;
        for (var i = 0; i < cmds.length; ++i) {
            var c = cmds[i];
            switch (c[0]) {
            /* --- state --- */
            case "sv": stateSave(st); ctx.save(); st.executed++; break;
            case "rs": ctx.restore(); stateRestore(st); st.executed++; break;
            case "reset": PathOps.resetContext(ctx); st.executed++; break;   // ICanvas.ResetState()
            case "aa": st.antialias = c[1] !== 0; st.executed++; break;   /* Qt antialiases in the scene graph */
            case "al": ctx.globalAlpha = c[1]; st.executed++; break;
            case "bm": ctx.globalCompositeOperation = blendName(c[1]); st.executed++; break;
            /* --- stroke / fill / font state --- */
            case "sc": st.stroke = c[1]; st.executed++; break;
            case "ss": st.lineWidth = c[1]; st.executed++; break;
            case "cap": ctx.lineCap = ["butt", "round", "square"][c[1]] || "butt"; st.executed++; break;
            case "join": ctx.lineJoin = ["miter", "round", "bevel"][c[1]] || "miter"; st.executed++; break;
            case "ml": ctx.miterLimit = c[1]; st.executed++; break;
            /* Dash pattern/offset are multiples of the stroke size. Qt 5.6 has no
             * setLineDash, so strokeShape cuts dashes in JS. */
            case "dashp": st.dash = c[1]; st.executed++; break;
            case "dasho": st.dashOffset = c[1]; st.executed++; break;
            case "fc": st.fill = c[1]; st.fillSpec = null; st.executed++; break;
            case "fpaint": st.fillSpec = c[1]; st.executed++; mauiGradOps++; break;
            case "foc": st.fontColor = c[1]; st.executed++; break;
            case "fos": st.fontSize = c[1]; st.executed++; break;
            case "fon":
                /* An empty name means the platform font (see applyFont). */
                st.fontName = c[1];
                st.fontWeight = c.length > 2 ? c[2] : 400;
                st.fontItalic = c.length > 3 && c[3] !== 0;
                st.executed++;
                break;
            case "shadow":
                ctx.shadowOffsetX = c[1]; ctx.shadowOffsetY = c[2];
                ctx.shadowBlur = c[3]; ctx.shadowColor = c[4];
                st.executed++;
                break;
            /* --- transforms --- */
            case "tr": ctx.translate(c[1], c[2]); st.executed++; break;
            case "ro": ctx.rotate(c[1] * Math.PI / 180); st.executed++; break;
            case "ro2":
                ctx.translate(c[2], c[3]);
                ctx.rotate(c[1] * Math.PI / 180);
                ctx.translate(-c[2], -c[3]);
                st.executed++;
                break;
            case "sc2": ctx.scale(c[1], c[2]); st.executed++; break;
            case "ct": ctx.transform(c[1], c[2], c[3], c[4], c[5], c[6]); st.executed++; break;
            /* --- clip --- */
            case "clipr":
                ctx.beginPath(); ctx.rect(c[1], c[2], c[3], c[4]); ctx.clip();
                st.executed++;
                mauiClipOps++;
                break;
            case "clipp":
                PathOps.buildPath(ctx, c[1]);
                try { ctx.clip(c[2] === 1 ? "evenodd" : "nonzero"); } catch (e) { ctx.clip(); }
                st.executed++;
                break;
            case "subclipr":
                /* SubtractFromClip: no clip subtraction or path booleans in Qt 5.6; counted as skipped. */
                st.skipped++;
                break;
            /* --- geometry --- */
            case "line":
                strokeShape(ctx, st, function(p) { p.beginPath(); p.moveTo(c[1], c[2]); p.lineTo(c[3], c[4]); });
                st.executed++;
                break;
            case "rect":
                strokeShape(ctx, st, function(p) { p.beginPath(); p.rect(c[1], c[2], c[3], c[4]); });
                st.executed++;
                break;
            case "frect":
                fillSetup(ctx, st);
                if (st.hasFill) { ctx.beginPath(); ctx.rect(c[1], c[2], c[3], c[4]); ctx.fill(); }
                st.executed++;
                break;
            case "rrect":
                strokeShape(ctx, st, function(p) { roundRectPath(p, c[1], c[2], c[3], c[4], c[5]); });
                st.executed++;
                break;
            case "frrect":
                fillSetup(ctx, st);
                if (st.hasFill) { roundRectPath(ctx, c[1], c[2], c[3], c[4], c[5]); ctx.fill(); }
                st.executed++;
                break;
            /* Per-corner rounded rects are MAUI extension methods and arrive as "path"/"fpath". */
            case "ell":
                strokeShape(ctx, st, function(p) { ellipsePath(p, c[1], c[2], c[3], c[4]); });
                st.executed++;
                break;
            case "fell":
                fillSetup(ctx, st);
                if (st.hasFill) { ellipsePath(ctx, c[1], c[2], c[3], c[4]); ctx.fill(); }
                st.executed++;
                break;
            case "arc":
                strokeShape(ctx, st, function(p) { arcPath(p, c[1], c[2], c[3], c[4], c[5], c[6], c[7], c[8] !== 0); });
                st.executed++;
                break;
            case "farc":
                fillSetup(ctx, st);
                /* FillArc paints a pie wedge from the box center, like MAUI's Skia backend. */
                if (st.hasFill) { piePath(ctx, c[1], c[2], c[3], c[4], c[5], c[6], c[7] !== 0); ctx.fill(); }
                st.executed++;
                break;
            case "path":
                strokeShape(ctx, st, function(p) { PathOps.buildPath(p, c[1]); });
                st.executed++;
                break;
            case "fpath":
                fillSetup(ctx, st);
                if (st.hasFill) {
                    PathOps.buildPath(ctx, c[1]);
                    try { ctx.fill(c[2] === 1 ? "evenodd" : "nonzero"); } catch (e) { ctx.fill(); }
                }
                st.executed++;
                break;
            case "img":
                /* IImage has no decoded pixels on this platform yet; counted as skipped. */
                st.skipped++;
                break;
            /* --- text --- */
            case "strp":
                drawTextPoint(ctx, st, c[1], c[2], c[3], c[4]);
                st.executed++;
                break;
            case "str":
                drawTextRect(ctx, st, c[1], c[2], c[3], c[4], c[5], c[6], c[7], c[8]);
                st.executed++;
                break;
            case "txt":
                /* IAttributedText, flattened to plain text by the recorder. */
                drawTextRect(ctx, st, c[1], c[2], c[3], c[4], c[5], 0, 0, 0);
                st.executed++;
                break;
            default:
                st.skipped++;
                if (!st.warned)
                    st.warned = {};
                if (!st.warned[c[0]]) {
                    st.warned[c[0]] = true;
                    console.warn("CANVAS unknown op '" + c[0] + "' id=" + mauiId);
                }
                break;
            }
        }
    }

    /* --- MAUI-level state (Canvas2D save/restore only covers Context2D) --- */
    function stateSave(st) {
        if (!st.stack)
            st.stack = [];
        st.stack.push({ stroke: st.stroke, fill: st.fill, fillSpec: st.fillSpec,
                        lineWidth: st.lineWidth, dash: st.dash, dashOffset: st.dashOffset, fontColor: st.fontColor,
                        fontSize: st.fontSize, fontName: st.fontName,
                        fontWeight: st.fontWeight, fontItalic: st.fontItalic });
    }
    function stateRestore(st) {
        if (!st.stack || st.stack.length === 0)
            return;
        var s = st.stack.pop();
        for (var k in s)
            st[k] = s[k];
    }

    /* --- paint helpers --- */
    function strokeSetup(ctx, st) {
        ctx.lineWidth = st.lineWidth;
        ctx.strokeStyle = st.stroke || "#000000";
    }

    /* Strokes the path `draw(ctx)` builds, dashed when a dash pattern is set. */
    function strokeShape(ctx, st, draw) {
        strokeSetup(ctx, st);
        if (st.dash && st.dash.length > 0) {
            var lw = st.lineWidth > 0 ? st.lineWidth : 1;
            var d = [(st.dashOffset || 0) * lw];
            for (var i = 0; i < st.dash.length; ++i)
                d.push(st.dash[i] * lw);
            mauiDashes += PathOps.dashWith(ctx, draw, d, null);
        } else {
            draw(ctx);
        }
        ctx.stroke();
    }

    /* Resolves the fill into ctx.fillStyle; st.hasFill is false when nothing would paint. */
    function fillSetup(ctx, st) {
        st.hasFill = false;
        var spec = st.fillSpec;
        if (spec && spec.length > 0) {
            if (spec[0] === "solid") {
                ctx.fillStyle = spec[1];
                st.hasFill = true;
            } else {
                var g = PathOps.paintStyle(ctx, spec, null);
                if (g) {
                    ctx.fillStyle = g;
                    st.hasFill = true;
                }
            }
        } else if (st.fill) {
            ctx.fillStyle = st.fill;
            st.hasFill = true;
        }
    }

    function blendName(mode) {
        return ["source-over", "multiply", "screen", "overlay", "darken", "lighten",
                "color-dodge", "color-burn", "soft-light", "hard-light", "difference",
                "exclusion", "hue", "saturation", "color", "luminosity"][mode] || "source-over";
    }

    /* --- geometry helpers --- */
    function corner(ctx, cx, cy, x2, y2, r) {
        if (typeof ctx.arcTo === "function")
            ctx.arcTo(cx, cy, x2, y2, r);
        else
            ctx.quadraticCurveTo(cx, cy, x2, y2);
    }

    function roundRectPath(ctx, x, y, w, h, r) {
        var rr = Math.min(Math.abs(r), Math.abs(w) / 2, Math.abs(h) / 2);
        ctx.beginPath();
        if (!(rr > 0)) {
            ctx.rect(x, y, w, h);
            return;
        }
        ctx.moveTo(x + rr, y);
        ctx.lineTo(x + w - rr, y);
        corner(ctx, x + w, y, x + w, y + rr, rr);
        ctx.lineTo(x + w, y + h - rr);
        corner(ctx, x + w, y + h, x + w - rr, y + h, rr);
        ctx.lineTo(x + rr, y + h);
        corner(ctx, x, y + h, x, y + h - rr, rr);
        ctx.lineTo(x, y + rr);
        corner(ctx, x, y, x + rr, y, rr);
        ctx.closePath();
    }

    function ellipsePath(ctx, x, y, w, h) {
        ctx.beginPath();
        PathOps.ellipseBox(ctx, x, y, w, h);
    }

    function arcPath(ctx, x, y, w, h, startDeg, endDeg, clockwise, closed) {
        PathOps.arcBox(ctx, x, y, w, h, startDeg, endDeg, clockwise, closed);
    }

    function piePath(ctx, x, y, w, h, startDeg, endDeg, clockwise) {
        PathOps.pieBox(ctx, x, y, w, h, startDeg, endDeg, clockwise);
    }

    /* --- text helpers --- */
    function applyFont(ctx, st) {
        /* Empty name = the Silica theme family, so canvas text matches Labels. */
        var fam = st.fontName || mauiFontFamily || Theme.fontFamily;
        if (fam.indexOf(" ") >= 0)
            fam = "'" + fam + "'";
        /* Qt 5.6's CSS font parser handles weight keywords more reliably than numbers. */
        var css = (st.fontItalic ? "italic " : "") + (st.fontWeight >= 600 ? "bold " : "");
        ctx.font = css + (st.fontSize || 12) + "px " + fam;
        ctx.fillStyle = st.fontColor || "#000000";
    }

    function measure(ctx, text, st) {
        try {
            return ctx.measureText(text).width;
        } catch (e) {
            return text.length * (st.fontSize || 12) * 0.6;
        }
    }

    /* DrawString(value, x, y, ha): the point is the top-left of the text box, as in
     * MAUI's Skia backend. ha: 0 Left, 1 Center, 2 Right, 3 Justify. */
    function drawTextPoint(ctx, st, text, x, y, ha) {
        applyFont(ctx, st);
        var w = measure(ctx, text, st);
        var dx = ha === 1 ? x - w / 2 : (ha === 2 ? x - w : x);
        ctx.textBaseline = "top";
        ctx.textAlign = "left";
        ctx.fillText(text, dx, y);
    }

    /* DrawString in a rect: flow 0 (ClipBounds) wraps and clips, 1 (OverflowBounds)
     * keeps one line per "\n". va: 0 Top, 1 Center, 2 Bottom. */
    function drawTextRect(ctx, st, text, x, y, w, h, ha, va, flow, lsa) {
        applyFont(ctx, st);
        var lines = [];
        var src = String(text).split("\n");
        if (flow === 0 && w > 0) {
            for (var s = 0; s < src.length; ++s)
                wrapLine(ctx, st, src[s], w, lines);
        } else {
            lines = src;
        }
        var lh = (st.fontSize || 12) * 1.2 + (lsa || 0);
        var total = lh * lines.length;
        var top = va === 1 ? y + (h - total) / 2 : (va === 2 ? y + h - total : y);
        ctx.save();
        if (flow === 0 && w > 0 && h > 0) {
            ctx.beginPath();
            ctx.rect(x, y, w, h);
            ctx.clip();
        }
        ctx.textBaseline = "top";
        ctx.textAlign = "left";
        for (var i = 0; i < lines.length; ++i) {
            var tw = measure(ctx, lines[i], st);
            var dx = ha === 1 ? x + (w - tw) / 2 : (ha === 2 ? x + w - tw : x);
            ctx.fillText(lines[i], dx, top + lh * i);
        }
        ctx.restore();
    }

    /* Greedy word wrap on the measured width (Context2D has no text layout). */
    function wrapLine(ctx, st, text, w, out) {
        var words = String(text).split(" ");
        var line = "";
        for (var i = 0; i < words.length; ++i) {
            var cand = line.length === 0 ? words[i] : line + " " + words[i];
            if (line.length > 0 && measure(ctx, cand, st) > w) {
                out.push(line);
                line = words[i];
            } else {
                line = cand;
            }
        }
        out.push(line);
    }
}
