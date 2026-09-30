#!/bin/bash
# Assembles the Sailfish OS 5.2.0.17 aarch64 sysroot for cross-building the Qt shim
# (tools/sf-native-build.sh): Qt 5.6.3 headers, libsailfishapp-devel and the runtime
# .so.5.6.3 files the devel RPMs only symlink to.
#
# Usage: ./tools/sf-sysroot.sh [--no-runtime | --runtime-from-repo | --recorder]
#   --recorder           only add the screen recorder's GStreamer/GLib/Wayland
#                        headers (repo) and libraries (phone)
#   --no-runtime         headers only; the shim will not link
#   --runtime-from-repo  runtime .so from the public release repo (no phone / CI)
#   (default)            scp the runtime .so from the phone
#
# Output: tools/.sysroot/<arch> (gitignored); RPM cache: tools/.cache/rpm.
# Env: SF_ARCH=aarch64 (default) | armv7hl (runtime always from the repo), SF_SYSROOT_BASE.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ARCH="${SF_ARCH:-aarch64}"
case "$ARCH" in
	aarch64) LIBDIR=usr/lib64 ;;
	armv7hl) LIBDIR=usr/lib ;;
	*) echo "ERROR: unsupported SF_ARCH=$ARCH (aarch64|armv7hl)"; exit 2 ;;
esac
SYSROOT="$REPO_ROOT/tools/.sysroot/$ARCH"
CACHE="$REPO_ROOT/tools/.cache/rpm"

# Repo hrefs are "oss/<arch>/<nvr>.rpm" relative to the .../jolla/<arch> base, hence
# the doubled "<arch>/oss/<arch>". Directory listings answer 403; files are served.
BASE="${SF_SYSROOT_BASE:-https://releases.jolla.com/releases/5.2.0.17/jolla/$ARCH/oss/$ARCH}"
RPMS=(
	"qt5-qtcore-devel-5.6.3+git60-1.19.6.jolla.$ARCH.rpm"
	"qt5-qtgui-devel-5.6.3+git60-1.19.6.jolla.$ARCH.rpm"
	"qt5-qtdeclarative-devel-5.6.3+git28-1.9.3.jolla.$ARCH.rpm"
	"qt5-qtdeclarative-qtquick-devel-5.6.3+git28-1.9.3.jolla.$ARCH.rpm"
	"libsailfishapp-devel-1.2.17-1.8.4.jolla.$ARCH.rpm"
	# QNetworkDiskCache for the QML engine's http(s) image loads
	"qt5-qtnetwork-devel-5.6.3+git60-1.19.6.jolla.$ARCH.rpm"
	# SecureStorage bridge (libsailfishsecretsbridge.so): Secrets API + QtDBus
	"qt5-qtdbus-devel-5.6.3+git60-1.19.6.jolla.$ARCH.rpm"
	"libsailfishsecrets-devel-0.2.44-1.14.3.jolla.$ARCH.rpm"
)
# Sailfish Secrets is not preinstalled on devices, so its runtime always comes from the repo.
REPO_RUNTIME_RPMS=(
	"libsailfishsecrets-0.2.44-1.14.3.jolla.$ARCH.rpm"
)
# The targets of the -devel symlinks live in the non-devel packages of the same repo.
RUNTIME_RPMS=(
	"qt5-qtcore-5.6.3+git60-1.19.6.jolla.$ARCH.rpm"
	"qt5-qtgui-5.6.3+git60-1.19.6.jolla.$ARCH.rpm"
	"qt5-qtdeclarative-5.6.3+git28-1.9.3.jolla.$ARCH.rpm"
	# libQt5Quick.so.5.6.3 is in a separate package (the devel side splits the same way)
	"qt5-qtdeclarative-qtquick-5.6.3+git28-1.9.3.jolla.$ARCH.rpm"
	"libsailfishapp-1.2.17-1.8.4.jolla.$ARCH.rpm"
	"qt5-qtdbus-5.6.3+git60-1.19.6.jolla.$ARCH.rpm"
	"qt5-qtnetwork-5.6.3+git60-1.19.6.jolla.$ARCH.rpm"
)
RUNTIME_LIBS=(
	libQt5Core.so.5.6.3
	libQt5Gui.so.5.6.3
	libQt5Qml.so.5.6.3
	libQt5Quick.so.5.6.3
	libsailfishapp.so.1.2.17
	libQt5DBus.so.5.6.3
	libQt5Network.so.5.6.3
)

DO_RUNTIME=1
RUNTIME_SOURCE="device"
[ "$ARCH" = aarch64 ] || RUNTIME_SOURCE="repo"   # no device of that arch to copy from
case "${1:-}" in
	"") ;;
	--no-runtime) DO_RUNTIME=0 ;;
	--runtime-from-repo) RUNTIME_SOURCE="repo" ;;
	--recorder) ;;
	-h|--help) sed -n '2,14p' "$0"; exit 0 ;;
	*)
		echo "ERROR: unknown option: $1"
		echo "Usage: $0 [--no-runtime | --runtime-from-repo]"
		exit 2
		;;
esac

mkdir -p "$CACHE" "$SYSROOT/$LIBDIR"

# Prefer the real rpm2cpio, else the stdlib-only Python fallback (no package manager needed).
if command -v rpm2cpio >/dev/null 2>&1; then
	RPM2CPIO=(rpm2cpio)
elif command -v python3 >/dev/null 2>&1; then
	RPM2CPIO=(python3 "$SCRIPT_DIR/sf-rpm2cpio.py")
	echo "    note: rpm2cpio not found - using the stdlib fallback $SCRIPT_DIR/sf-rpm2cpio.py"
else
	echo "ERROR: neither rpm2cpio nor python3 found (macOS: brew install rpm)"
	exit 1
fi
command -v cpio >/dev/null 2>&1 || { echo "ERROR: cpio not found"; exit 1; }
command -v curl >/dev/null 2>&1 || { echo "ERROR: curl not found (needed for the RPM + Khronos header downloads)"; exit 1; }

# fetch_rpm <rpm-file> [label] - download into the cache unless present, then unpack into the sysroot.
fetch_rpm() {
	local file="$CACHE/$1" label="${2:-$1}"
	if [ ! -f "$file" ]; then
		echo "    downloading $label"
		curl -fsSL -o "$file" "$BASE/$1" || { echo "ERROR: download failed: $BASE/$1"; exit 1; }
	fi
	(cd "$SYSROOT" && "${RPM2CPIO[@]}" "$file" | cpio -idmu --quiet) || { echo "ERROR: extract failed: $label"; exit 1; }
}

if [ "${1:-}" = --recorder ]; then
	[ "$ARCH" = aarch64 ] || { echo "ERROR: the recorder is built for aarch64 only"; exit 2; }
	echo "==> recorder headers (GStreamer, GLib, Wayland) into $SYSROOT"
	for name in gstreamer1.0-devel-1.26.11+git1-1.15.2.jolla gstreamer1.0-plugins-base-devel-1.26.11+git1-1.14.3.jolla \
	            glib2-devel-2.86.4+git1-1.10.3.jolla wayland-devel-1.24.0+git1-1.9.3.jolla; do
		fetch_rpm "$name.$ARCH.rpm" "$name"
	done
	echo "==> recorder libraries from the device (scp)"
	# shellcheck source=sf-lib.sh
	. "$SCRIPT_DIR/sf-lib.sh"
	for lib in libglib-2.0.so.0 libgobject-2.0.so.0 libgstreamer-1.0.so.0 libgstapp-1.0.so.0 libgstbase-1.0.so.0 \
	           libgstvideo-1.0.so.0 libwayland-client.so.0; do
		[ -f "$SYSROOT/$LIBDIR/$lib" ] && [ ! -L "$SYSROOT/$LIBDIR/$lib" ] && continue
		real="$(sf_ssh_out "readlink -f /$LIBDIR/$lib" | tr -d '\r' | tail -1)"
		rm -f "$SYSROOT/$LIBDIR/$lib"
		sf_scp_from "$real" "$SYSROOT/$LIBDIR/$lib" >/dev/null || { echo "ERROR: could not fetch $real"; exit 1; }
		echo "    scp $lib"
	done
	echo "    recorder sysroot ready"
	exit 0
fi

echo "==> [1/3] RPM headers into $SYSROOT"
for name in "${RPMS[@]}"; do
	# reuse RPMs from an earlier manual download
	if [ ! -f "$CACHE/$name" ] && [ -f "/tmp/sfroot/$name" ]; then
		cp "/tmp/sfroot/$name" "$CACHE/$name"
		echo "    seeded from /tmp/sfroot: $name"
	fi
	fetch_rpm "$name"
done

for name in "${REPO_RUNTIME_RPMS[@]}"; do
	fetch_rpm "$name"
done

echo "==> [1.5/3] patch headers for modern clang (zig c++)"
python3 "$SCRIPT_DIR/sf-patch-sysroot.py" "$SYSROOT" \
	|| { echo "ERROR: sysroot patch failed"; exit 1; }

echo "==> [1.6/3] Khronos GLES headers (the device Qt has QT_OPENGL_ES_3_1)"
# qopengl.h includes GLES3/gl31.h, which no Sailfish package provides; these are pure
# Khronos declarations (no ABI impact), so vendoring them from the registry is safe.
GL_INC="$SYSROOT/usr/include"
mkdir -p "$GL_INC/GLES2" "$GL_INC/GLES3" "$GL_INC/KHR"
fetch_gl() { # <remote-subpath> <local-relpath>
	local url="$1" dest="$GL_INC/$2"
	[ -s "$dest" ] && return 0
	curl -fsSL -o "$dest" "$url" || { echo "ERROR: download failed: $url"; return 1; }
	echo "    fetched $2"
}
fetch_gl "https://registry.khronos.org/OpenGL/api/GLES2/gl2.h"          GLES2/gl2.h
fetch_gl "https://registry.khronos.org/OpenGL/api/GLES2/gl2ext.h"       GLES2/gl2ext.h
fetch_gl "https://registry.khronos.org/OpenGL/api/GLES2/gl2platform.h"  GLES2/gl2platform.h
fetch_gl "https://registry.khronos.org/OpenGL/api/GLES3/gl3.h"          GLES3/gl3.h
fetch_gl "https://registry.khronos.org/OpenGL/api/GLES3/gl31.h"         GLES3/gl31.h
fetch_gl "https://registry.khronos.org/OpenGL/api/GLES3/gl3platform.h"  GLES3/gl3platform.h
fetch_gl "https://registry.khronos.org/EGL/api/KHR/khrplatform.h"       KHR/khrplatform.h

echo "==> [2/3] runtime libraries"
if [ "$DO_RUNTIME" = 1 ] && [ "$RUNTIME_SOURCE" = "repo" ]; then
	echo "    from the release repo (--runtime-from-repo)"
	for name in "${RUNTIME_RPMS[@]}"; do
		fetch_rpm "$name"
	done
elif [ "$DO_RUNTIME" = 1 ]; then
	echo "    from the device (scp)"
	# shellcheck source=sf-lib.sh
	. "$SCRIPT_DIR/sf-lib.sh"
	# sf_ping exits via sf_die; run it in a subshell so the hint can be printed
	( sf_ping ) || {
		echo "ERROR: device unreachable"
		echo "HINT: run '$0 --runtime-from-repo' to take the same .so files from the"
		echo "      public release repo (no device needed), then deploy to a phone later."
		exit 1
	}
	for lib in "${RUNTIME_LIBS[@]}"; do
		if [ -f "$SYSROOT/$LIBDIR/$lib" ]; then
			echo "    cached: $lib"
			continue
		fi
		echo "    scp $lib"
		sf_scp_from "/$LIBDIR/$lib" "$SYSROOT/$LIBDIR/$lib" \
			|| { echo "ERROR: could not fetch /$LIBDIR/$lib"; exit 1; }
	done
else
	echo "    skipped (--no-runtime)"
fi

echo "==> [3/3] verify"
missing=0
for link in libQt5Core.so libQt5Gui.so libQt5Qml.so libQt5Quick.so libsailfishapp.so libQt5DBus.so libsailfishsecrets.so; do
	target="$SYSROOT/$LIBDIR/$link"
	if [ -L "$target" ] && [ -e "$target" ]; then
		echo "    OK   $link -> $(readlink "$target")"
	else
		echo "    FAIL $link (dangling or missing; rerun without --no-runtime, or with --runtime-from-repo on a device-less host)"
		missing=1
	fi
done
for hdr in QtCore/qconfig.h QtGui/qwindow.h QtQuick/qquickview.h sailfishapp/sailfishapp.h QtDBus/qdbusconnection.h Sailfish/Secrets/secretmanager.h; do
	[ -f "$SYSROOT/usr/include/qt5/$hdr" ] || [ -f "$SYSROOT/usr/include/$hdr" ] \
		&& echo "    OK   $hdr" || { echo "    FAIL $hdr"; missing=1; }
done
[ "$missing" = 0 ] || { echo "ERROR: sysroot incomplete"; exit 1; }

echo "    sysroot ready: $SYSROOT"
