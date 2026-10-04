#!/bin/bash
# Cross-compiles the Qt Quick/Silica shim (src/Linux.SailfishOS/Native) into
# libsailfishhost.so for Sailfish OS 5.2.0.17 with zig; no SDK or VM needed.
# The sysroot comes from tools/sf sysroot.
#
# Usage: ./tools/sf native-build      (SF_ARCH=aarch64 default | armv7hl)
# Output: artifacts/native/<arch>/libsailfishhost.so (plus the secrets bridge and launcher)

set -euo pipefail
case "${1:-}" in -h|--help) awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"; exit 0 ;; esac
[ $# -eq 0 ] || { echo "ERROR: unexpected argument: $1 (see --help)" >&2; exit 2; }

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
ARCH="${SF_ARCH:-aarch64}"
case "$ARCH" in
	aarch64) ZIG_TARGET=aarch64-linux-gnu.2.34; LIBDIR=usr/lib64 ;;
	armv7hl) ZIG_TARGET=arm-linux-gnueabihf.2.34; LIBDIR=usr/lib ;;
	*) echo "ERROR: unsupported SF_ARCH=$ARCH (aarch64|armv7hl)"; exit 2 ;;
esac
SYSROOT="$REPO_ROOT/tools/.sysroot/$ARCH"
SRC="$REPO_ROOT/src/Linux.SailfishOS/Native"
OUT_DIR="$REPO_ROOT/artifacts/native/$ARCH"
OUT="$OUT_DIR/libsailfishhost.so"

[ -d "$SYSROOT/usr/include/qt5/QtCore" ] \
	|| { echo "ERROR: sysroot missing - run ./tools/sf sysroot first"; exit 1; }
command -v zig >/dev/null 2>&1 || { echo "ERROR: zig not found (macOS: brew install zig)"; exit 1; }

mkdir -p "$OUT_DIR"

INC=()
for d in QtCore QtGui QtNetwork QtQml QtQuick; do
	INC+=("-I$SYSROOT/usr/include/qt5/$d")
done
# Private QPA/QtQuick headers (QWindowSystemInterface, QQuickWindowPrivate) are safe
# because the sysroot is pinned to the device's Qt 5.6.3.
INC+=("-I$SYSROOT/usr/include/qt5/QtGui/5.6.3/QtGui" "-I$SYSROOT/usr/include/qt5/QtGui/5.6.3")
INC+=("-I$SYSROOT/usr/include/qt5/QtCore/5.6.3/QtCore" "-I$SYSROOT/usr/include/qt5/QtCore/5.6.3")
INC+=("-I$SYSROOT/usr/include/qt5/QtQml/5.6.3/QtQml" "-I$SYSROOT/usr/include/qt5/QtQml/5.6.3")
INC+=("-I$SYSROOT/usr/include/qt5/QtQuick/5.6.3/QtQuick" "-I$SYSROOT/usr/include/qt5/QtQuick/5.6.3")
INC+=("-I$SYSROOT/usr/include/qt5" "-I$SYSROOT/usr/include/sailfishapp" "-I$SYSROOT/usr/include" "-I$SRC")

# Stripped by default (Harbour flags unstripped libraries; exports stay in .dynsym).
# SF_NATIVE_KEEP_SYMBOLS=1 keeps the symbol table for gdb.
STRIP_FLAG="-Wl,-s"
[ "${SF_NATIVE_KEEP_SYMBOLS:-0}" = 1 ] && STRIP_FLAG=""

echo "==> zig c++ -> $OUT"
zig c++ \
	-target "$ZIG_TARGET" \
	-O2 -fPIC -shared -std=c++14 $STRIP_FLAG \
	"${INC[@]}" \
	"$SRC/sailfish_host.cpp" "$SRC/host_core.cpp" "$SRC/host_handles.cpp" "$SRC/host_text.cpp" \
	"$SRC/host_surface.cpp" "$SRC/host_diag.cpp" \
	-o "$OUT" \
	-L"$SYSROOT/$LIBDIR" \
	-lsailfishapp -lQt5Quick -lQt5Qml -lQt5Network -lQt5Gui -lQt5Core

echo "==> verify"
file "$OUT" | sed 's/^/    /'
ls -la "$OUT" | sed 's/^/    /'
# The C ABI exports must be visible to .NET P/Invoke.
NM_BIN="$(command -v llvm-nm || { ls /opt/homebrew/opt/llvm@21/bin/llvm-nm /opt/homebrew/opt/llvm/bin/llvm-nm 2>/dev/null | head -1 || true; })"
if [ -n "$NM_BIN" ]; then
	"$NM_BIN" -D --defined-only "$OUT" | grep 'sailfish_host_' | sed 's/^/    /' \
		|| { echo "ERROR: no sailfish_host_* exports"; exit 1; }
else
	echo "    (llvm-nm unavailable; skipped symbol check)"
fi
echo "    OK   libsailfishhost.so built"

# The secrets bridge is a separate library so the shim never depends on the
# non-preinstalled libsailfishsecrets.so.0; managed code dlopen()s it or falls back.
BRIDGE="$OUT_DIR/libsailfishsecretsbridge.so"
echo "==> zig c++ -> $BRIDGE"
zig c++ \
	-target "$ZIG_TARGET" \
	-O2 -fPIC -shared -std=c++14 -fvisibility=hidden $STRIP_FLAG \
	"-I$SYSROOT/usr/include/qt5/QtCore" "-I$SYSROOT/usr/include/qt5/QtDBus" "-I$SYSROOT/usr/include/qt5" \
	"-I$SYSROOT/usr/include/Sailfish" "-I$SYSROOT/usr/include" \
	"$SRC/sailfish_secrets.cpp" \
	-o "$BRIDGE" \
	-L"$SYSROOT/$LIBDIR" \
	-lsailfishsecrets -lQt5DBus -lQt5Core
if [ -n "$NM_BIN" ]; then
	"$NM_BIN" -D --defined-only "$BRIDGE" | grep ' T sfsec_' | sed 's/^/    /' \
		|| { echo "ERROR: no sfsec_* exports"; exit 1; }
fi
echo "    OK   libsailfishsecretsbridge.so built"

# Harbour launcher (/usr/bin/<package>): a PIE exporting main() for mapplauncherd's
# booster, hosting .NET from /usr/share/<package>/lib via hostfxr.
LAUNCHER="$OUT_DIR/sailfish-launcher"
echo "==> zig cc -> $LAUNCHER"
# DT_RPATH placeholder, patched per app by the packaging (Harbour requires
# $ORIGIN/../share/PKG/lib); sized for any package name.
RPATH_PLACEHOLDER='/__SAILFISH_LAUNCHER_RPATH__0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef__'
zig cc -target "$ZIG_TARGET" -O2 -fPIE -pie -Wl,-E -Wl,-s \
	-Wl,--disable-new-dtags -Wl,-rpath,"$RPATH_PLACEHOLDER" \
	"$SRC/sailfish_launcher.c" -o "$LAUNCHER" -ldl
python3 "$SCRIPT_DIR/../py/sf-elf-rpath.py" "$LAUNCHER"
if [ -n "$NM_BIN" ]; then
	"$NM_BIN" -D "$LAUNCHER" | grep -E ' (T main|U __libc_start_main)' | sed 's/^/    /'
	"$NM_BIN" -D --defined-only "$LAUNCHER" | grep -q ' T main$' \
		|| { echo "ERROR: launcher does not export main() (booster requirement)"; exit 1; }
fi
echo "    OK   sailfish-launcher built"
