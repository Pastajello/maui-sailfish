// host_text.cpp — fonts and text: register_font, render_glyph, measure_text.

#include "host_internal.h"

using namespace sfhost;

extern "C" {

int sailfish_host_register_font(const char *path, char *out, int cap)
{
    if (out && cap > 0)
        out[0] = '\0';
    if (!path || !path[0]) {
        set_error("empty font path");
        return -1;
    }
    const int id = QFontDatabase::addApplicationFont(QString::fromUtf8(path));
    if (id < 0) {
        set_error(std::string("QFontDatabase rejected font ") + path);
        return -1;
    }
    const QStringList families = QFontDatabase::applicationFontFamilies(id);
    const QByteArray utf = families.isEmpty() ? QByteArray() : families.first().toUtf8();
    copy_out(utf, out, cap);
    log_line(0, QStringLiteral("font registered %1 -> %2").arg(QString::fromUtf8(path), QString::fromUtf8(utf)));
    return utf.size();
}

int sailfish_host_render_glyph(const char *family, const char *text, double px,
                               const char *color, const char *out_path)
{
    if (!text || !text[0] || !out_path || !out_path[0] || px <= 0) {
        set_error("render_glyph: empty text/path or non-positive size");
        return -1;
    }
    QFont font = (family && family[0]) ? QFont(QString::fromUtf8(family)) : QFont();
    font.setPixelSize(qMax(1, qRound(px)));
    const QString glyph = QString::fromUtf8(text);
    const QFontMetricsF fm(font);
        // Square-ish box at least px (icons are square), wide enough for the glyph advance.
    const int w = qMax(qRound(px), qCeil(fm.width(glyph)));
    const int h = qMax(qRound(px), qCeil(fm.height()));
    QImage img(w, h, QImage::Format_ARGB32_Premultiplied);
    img.fill(Qt::transparent);
    QPainter painter(&img);
    painter.setRenderHint(QPainter::Antialiasing);
    painter.setRenderHint(QPainter::TextAntialiasing);
    painter.setFont(font);
    painter.setPen(parse_color(QString::fromUtf8(color && color[0] ? color : "#ffffffff")));
    painter.drawText(QRectF(0, 0, w, h), Qt::AlignCenter, glyph);
    painter.end();
    if (!img.save(QString::fromUtf8(out_path), "PNG")) {
        set_error(std::string("render_glyph: could not save ") + out_path);
        return -1;
    }
    return 0;
}

// One line's natural width as QQuickText lays it out: QTextLayout with design metrics (Text.QtRendering, the
// default). QFontMetricsF sums hinted advances, a few px short on a long bold title, so a label sized to its own
// text (a centered card, an Auto column) wrapped its last word onto a line the layout never reserved.
static double text_line_width(const QFont &font, const QString &s)
{
    QTextLayout layout(s, font);
    QTextOption option;
    option.setUseDesignMetrics(true);
    option.setWrapMode(QTextOption::NoWrap);
    layout.setTextOption(option);
    layout.beginLayout();
    double width = 0;
    QTextLine line = layout.createLine();
    if (line.isValid()) {
        line.setLineWidth(1e7);
        width = line.naturalTextWidth();
    }
    layout.endLayout();
    return width;
}

// A run's font: the base font with the run's family, pixel size, weight, slant and spacing (absent keys keep the base).
static QFont run_font(const QFont &base, const QJsonObject &r)
{
    QFont f = base;
    const QString family = r.value(QStringLiteral("family")).toString();
    if (!family.isEmpty())
        f.setFamily(family);
    if (r.contains(QStringLiteral("px")))
        f.setPixelSize(qMax(1, qRound(r.value(QStringLiteral("px")).toDouble(0))));
    if (r.contains(QStringLiteral("bold")))
        f.setBold(r.value(QStringLiteral("bold")).toInt(0) != 0);
    if (r.contains(QStringLiteral("italic")))
        f.setItalic(r.value(QStringLiteral("italic")).toInt(0) != 0);
    const double ls = r.value(QStringLiteral("ls")).toDouble(0);
    if (ls != 0.0)
        f.setLetterSpacing(QFont::AbsoluteSpacing, ls);
    return f;
}

// Mixed-font text (FormattedText spans, tracker S43): one QTextLayout per paragraph with a format range per run, as
// the rich-text label lays it out, so a line is as tall as its tallest run (ascent + descent + leading of the line)
// and wraps where the wider runs push it. "runs": [{"s": start, "n": length, family/px/bold/italic/ls}], offsets in
// UTF-16 code units of "text".
static void measure_runs(const QString &text, const QFont &base, const QJsonArray &runs, int wrap, double maxW,
                         double lh, int maxLines, double *out_w, double *out_h)
{
    double widest = 0;
    double height = 0;
    int totalLines = 0;
    int paraStart = 0;
    const QStringList paragraphs = text.split(QLatin1Char('\n'));
    for (int p = 0; p < paragraphs.size(); ++p) {
        const QString &para = paragraphs.at(p);
        const int paraEnd = paraStart + para.size();
        QVector<QTextLayout::FormatRange> formats;
        QFont first = base;
        bool haveFirst = false;
        for (const QJsonValue &v : runs) {
            const QJsonObject r = v.toObject();
            const int s = r.value(QStringLiteral("s")).toInt(0);
            const int e = s + r.value(QStringLiteral("n")).toInt(0);
            const int from = qMax(s, paraStart), to = qMin(e, paraEnd);
            if (to <= from && !(para.isEmpty() && s <= paraStart && paraStart <= e))
                continue;
            const QFont f = run_font(base, r);
            if (!haveFirst) {
                first = f;   // an empty paragraph is as tall as the run it sits in
                haveFirst = true;
            }
            if (to > from) {
                QTextLayout::FormatRange range;
                range.start = from - paraStart;
                range.length = to - from;
                range.format.setFont(f);
                formats.append(range);
            }
        }
        QTextLayout layout(para.isEmpty() ? QStringLiteral(" ") : para, first);
        layout.setFormats(formats);
        QTextOption option;
        option.setUseDesignMetrics(true);
        option.setWrapMode(wrap == 0 || maxW <= 0 ? QTextOption::NoWrap
                           : wrap == 2 ? QTextOption::WrapAnywhere : QTextOption::WordWrap);
        layout.setTextOption(option);
        layout.beginLayout();
        bool capped = false;
        for (;;) {
            QTextLine line = layout.createLine();
            if (!line.isValid())
                break;
            line.setLeadingIncluded(true);
            line.setLineWidth(wrap == 0 || maxW <= 0 ? 1e7 : maxW);
            if (!para.isEmpty())
                widest = qMax(widest, line.naturalTextWidth());
            height += std::ceil(line.height() - 0.001) * lh;
            ++totalLines;
            if (maxLines > 0 && totalLines >= maxLines) {
                capped = true;
                break;
            }
        }
        layout.endLayout();
        if (capped)
            break;
        paraStart = paraEnd + 1;   // the '\n'
    }
    *out_w = std::ceil(widest - 0.001);
    *out_h = height;
}

// Layout measurement and QML Text share Qt metrics and its line breaker (QTextLayout, Text.WordWrap /
// Text.WrapAnywhere); lh multiplies the line height like Text.ProportionalHeight. Qt thread.
int sailfish_host_measure_text(const char *json, double *out_w, double *out_h)
{
    if (!json || !out_w || !out_h || g.shutdown || !g.app)
        return fail_args("sailfish_host_measure_text");
    ++g.text_measures;
    QJsonParseError perr;
    const QJsonDocument doc = QJsonDocument::fromJson(QByteArray(json), &perr);
    if (perr.error != QJsonParseError::NoError || !doc.isObject()) {
        set_error("measure_text: expected a JSON object");
        return -1;
    }
    const QJsonObject o = doc.object();
    const QString text = o.value(QStringLiteral("text")).toString();
    *out_w = 0;
    *out_h = 0;
    if (text.isEmpty())
        return 0;

    QFont font;
    const QString family = o.value(QStringLiteral("family")).toString();
    if (!family.isEmpty())
        font.setFamily(family);
    font.setPixelSize(qMax(1, qRound(o.value(QStringLiteral("px")).toDouble(0))));
    font.setBold(o.value(QStringLiteral("bold")).toInt(0) != 0);
    font.setItalic(o.value(QStringLiteral("italic")).toInt(0) != 0);
    const double ls = o.value(QStringLiteral("ls")).toDouble(0);
    if (ls != 0.0)
        font.setLetterSpacing(QFont::AbsoluteSpacing, ls);
    const double lh = qMax(0.1, o.value(QStringLiteral("lh")).toDouble(1.0));
    const int maxLines = o.value(QStringLiteral("maxLines")).toInt(0);
    const int wrap = o.value(QStringLiteral("wrap")).toInt(1);
    const double maxW = o.value(QStringLiteral("maxW")).toDouble(0);
    const QJsonArray runs = o.value(QStringLiteral("runs")).toArray();
    if (!runs.isEmpty()) {
        measure_runs(text, font, runs, wrap, maxW, lh, maxLines, out_w, out_h);
        return 0;
    }

    // Widths are whole-line design-metric widths, rounded up: QQuickText wraps when the
    // fractional natural width exceeds the item, so summed integer word widths broke lines
    // one pixel early. Line height stays integral (fm.height()).
    const QFontMetrics fm(font);
    const double lineH = fm.height() * lh;

    double widest = 0;
    int totalLines = 0;
    bool capped = false;
    const QStringList paragraphs = text.split(QLatin1Char('\n'));
    for (int p = 0; p < paragraphs.size() && !capped; ++p) {
        const QString &para = paragraphs.at(p);
        if (para.isEmpty() || wrap == 0 || maxW <= 0) {
            // Empty paragraph, NoWrap or unbounded width: one line.
            if (!para.isEmpty())
                widest = qMax(widest, text_line_width(font, para));
            ++totalLines;
            if (maxLines > 0 && totalLines >= maxLines)
                capped = true;
            continue;
        }
        // One QTextLayout per paragraph, as QQuickText lays it out: the same line breaker (Unicode break
        // opportunities, so a hyphen or a slash breaks as the label will) and one pass instead of re-measuring every
        // growing candidate line (W8.2: the greedy loop was quadratic in the words of a paragraph).
        QTextLayout layout(para, font);
        QTextOption option;
        option.setUseDesignMetrics(true);
        option.setWrapMode(wrap == 2 ? QTextOption::WrapAnywhere : QTextOption::WordWrap);
        layout.setTextOption(option);
        layout.beginLayout();
        for (;;) {
            QTextLine line = layout.createLine();
            if (!line.isValid())
                break;
            line.setLineWidth(maxW);
            widest = qMax(widest, line.naturalTextWidth());
            ++totalLines;
            if (maxLines > 0 && totalLines >= maxLines) {
                capped = true;
                break;
            }
        }
        layout.endLayout();
    }
    widest = std::ceil(widest - 0.001);
    *out_w = widest;
    *out_h = totalLines * lineH;
    return 0;
}

} // extern "C"
