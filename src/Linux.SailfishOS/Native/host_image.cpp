// host_image.cpp — MAUI IImage on QImage (tracker S49, decision D6 a): decode, resize and encode encoded image bytes.
// Stateless: encoded bytes in, encoded bytes out, so no native handle outlives a call. QImage is reentrant; these run
// on any thread.

#include "host_internal.h"

#include <QBuffer>
#include <QImage>
#include <QImageReader>
#include <QPainter>

using namespace sfhost;

static bool load_image(const unsigned char *data, int len, QImage *out)
{
    if (!data || len <= 0)
        return false;
    QByteArray bytes = QByteArray::fromRawData(reinterpret_cast<const char *>(data), len);
    QBuffer buffer(&bytes);
    buffer.open(QIODevice::ReadOnly);
    QImageReader reader(&buffer);
    reader.setAutoTransform(true);   // EXIF orientation, as the platforms' decoders apply it
    *out = reader.read();
    return !out->isNull();
}

extern "C" {

int sailfish_host_image_info(const unsigned char *data, int len, int *out_w, int *out_h)
{
    if (!out_w || !out_h)
        return fail_args("sailfish_host_image_info");
    *out_w = 0;
    *out_h = 0;
    QByteArray bytes = QByteArray::fromRawData(reinterpret_cast<const char *>(data), len);
    QBuffer buffer(&bytes);
    buffer.open(QIODevice::ReadOnly);
    QImageReader reader(&buffer);
    reader.setAutoTransform(true);
    QSize size = reader.size();
    if (!size.isValid()) {
        // Some formats only know their size once decoded.
        QImage image;
        if (!load_image(data, len, &image)) {
            set_error("image_info: not a decodable image");
            return -1;
        }
        size = image.size();
    } else if (reader.transformation() & QImageIOHandler::TransformationRotate90) {
        size.transpose();
    }
    *out_w = size.width();
    *out_h = size.height();
    return 0;
}

// op: {"w","h","mode":"stretch|fit|fill|keep","format":"png|jpg|gif|bmp|tiff","quality":0..100}. "fit" letterboxes the
// image into w×h (transparent bars), "fill" covers w×h and crops the centre, "stretch" scales to w×h, "keep" re-encodes
// at the decoded size. Returns the encoded size; a result larger than cap is not written (call again with a buffer of
// that size); negative on error.
int sailfish_host_image_transform(const unsigned char *data, int len, const char *op, unsigned char *out, int cap)
{
    if (!op)
        return fail_args("sailfish_host_image_transform");
    QImage image;
    if (!load_image(data, len, &image)) {
        set_error("image_transform: not a decodable image");
        return -1;
    }
    QJsonParseError perr;
    const QJsonObject o = QJsonDocument::fromJson(QByteArray(op), &perr).object();
    if (perr.error != QJsonParseError::NoError) {
        set_error("image_transform: bad op JSON");
        return -1;
    }
    const QString mode = o.value(QStringLiteral("mode")).toString(QStringLiteral("keep"));
    const int w = qMax(1, o.value(QStringLiteral("w")).toInt(image.width()));
    const int h = qMax(1, o.value(QStringLiteral("h")).toInt(image.height()));
    QImage result;
    if (mode == QLatin1String("stretch")) {
        result = image.scaled(w, h, Qt::IgnoreAspectRatio, Qt::SmoothTransformation);
    } else if (mode == QLatin1String("fit")) {
        const QImage scaled = image.scaled(w, h, Qt::KeepAspectRatio, Qt::SmoothTransformation);
        result = QImage(w, h, QImage::Format_ARGB32_Premultiplied);
        result.fill(Qt::transparent);
        QPainter painter(&result);
        painter.drawImage((w - scaled.width()) / 2, (h - scaled.height()) / 2, scaled);
    } else if (mode == QLatin1String("fill")) {
        const QImage scaled = image.scaled(w, h, Qt::KeepAspectRatioByExpanding, Qt::SmoothTransformation);
        result = scaled.copy((scaled.width() - w) / 2, (scaled.height() - h) / 2, w, h);
    } else {
        result = image;
    }
    const QByteArray format = o.value(QStringLiteral("format")).toString(QStringLiteral("png")).toLatin1();
    if (format == "jpg" || format == "jpeg" || format == "bmp")
        result = result.convertToFormat(QImage::Format_RGB32);   // no alpha: transparent areas turn black, not garbage
    QByteArray encoded;
    QBuffer buffer(&encoded);
    buffer.open(QIODevice::WriteOnly);
    if (!result.save(&buffer, format.constData(), qBound(-1, o.value(QStringLiteral("quality")).toInt(-1), 100))) {
        set_error(std::string("image_transform: Qt cannot write ") + format.constData());
        return -1;
    }
    if (out && encoded.size() <= cap)
        memcpy(out, encoded.constData(), static_cast<size_t>(encoded.size()));
    return encoded.size();
}

} // extern "C"
