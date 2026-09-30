#!/bin/bash
# Cross-compiles the on-phone screen recorder (tools/screenrec/sf_screenrec.c) with zig;
# the GStreamer/GLib/Wayland sysroot parts come from tools/sf sysroot --recorder.
# Usage: tools/sf screenrec-build        → tools/.cache/sf-screenrec (aarch64)
set -euo pipefail
case "${1:-}" in -h|--help) awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"; exit 0 ;; esac
[ $# -eq 0 ] || { echo "ERROR: unexpected argument: $1 (see --help)" >&2; exit 2; }
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
SYSROOT="$REPO_ROOT/tools/.sysroot/aarch64"
OUT="${SF_SCREENREC_OUT:-$REPO_ROOT/tools/.cache/sf-screenrec}"
SRC="$SCRIPT_DIR/../screenrec/sf_screenrec.c"

command -v zig >/dev/null 2>&1 || { echo "ERROR: zig not found (macOS: brew install zig)"; exit 1; }
if [ ! -f "$SYSROOT/usr/include/gstreamer-1.0/gst/app/gstappsrc.h" ] || [ ! -f "$SYSROOT/usr/include/wayland-client.h" ]; then
	"$SCRIPT_DIR/sysroot.sh" --recorder
fi
mkdir -p "$(dirname "$OUT")"
if [ -f "$OUT" ] && [ "$OUT" -nt "$SRC" ] && [ "$OUT" -nt "$0" ]; then
	echo "    sf-screenrec up to date ($OUT)"
	exit 0
fi
echo "==> zig cc -> $OUT"
zig cc -target aarch64-linux-gnu.2.34 -O2 -std=gnu11 -Wall -Wno-unused-parameter -Wl,-s \
	-I"$SYSROOT/usr/include/gstreamer-1.0" \
	-I"$SYSROOT/usr/include/glib-2.0" -I"$SYSROOT/usr/lib64/glib-2.0/include" \
	-I"$SYSROOT/usr/include" \
	"$SRC" -o "$OUT" \
	-L"$SYSROOT/usr/lib64" -Wl,--allow-shlib-undefined \
	-lgstapp-1.0 -lgstbase-1.0 -lgstreamer-1.0 -lgobject-2.0 -lglib-2.0 -lwayland-client
file "$OUT" | sed 's/^/    /'
