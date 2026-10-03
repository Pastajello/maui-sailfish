#!/bin/bash
# Verifies that the RPM built locally is byte-for-byte what is installed on the
# device, and that no stale app instance is masking the new binary.
# Usage: ./tools/sf verify [path/to.rpm]
#
# Checks (all must pass):
#   L1 the RPM contains the freshly published app assembly (payload digest ==
#      sha256 of bin/.../publish/<BIN>.dll) -> catches a stale publish
#   D1 installed NVR == RPM NVR             -> catches a no-op install
#   D2 rpmdb payload digest list == RPM payload digest list (all files)
#   D3 `rpm -V <pkg>` clean                 -> files on disk match the rpmdb
#   D4 sha256 of the installed <BIN>.dll == local published one
#   D5 no process runs a "(deleted)" binary -> catches the stale-instance bug
#   D6 sha256 of the installed libsailfishhost.so == local published one
#      -> catches a shim built before the last native change (tools/sf native-build)

set -uo pipefail
case "${1:-}" in -h|--help) awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"; exit 0 ;; esac

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=../lib/sf-lib.sh
. "$SCRIPT_DIR/../lib/sf-lib.sh"

FAILURES=0
pass() { sf_ok "$*"; }
fail() { sf_fail "$*"; FAILURES=$((FAILURES + 1)); }

# --------------------------------------------------------------- inputs ----
[ $# -le 1 ] || sf_die "usage: $0 [path/to.rpm]"
case "${1:-}" in -*) sf_die "unknown argument: $1 (see --help)" ;; esac
RPM_FILE="${1:-}"
if [ -z "$RPM_FILE" ]; then
	RPM_FILE="$(ls -1t "$SF_RPM_DIR"/*.rpm 2>/dev/null | head -1)"
fi
[ -n "$RPM_FILE" ] && [ -f "$RPM_FILE" ] || sf_die "no RPM found (looked in $SF_RPM_DIR); run ./tools/sf deploy first"

# `rpm -qp --qf` reads only the header, so hosts without the rpm toolchain fall
# back to tools/py/sf-rpmquery.py (installed as an `rpm` shim by sf_use_rpm_shims).
sf_use_rpm_shims || sf_die "rpm is required on the build host (macOS: brew install rpm; otherwise keep tools/py/sf-rpmquery.py in place)"

PKG_NAME="$(rpm -qp --qf '%{NAME}' "$RPM_FILE")"
LOCAL_NVR="$(rpm -qp --qf '%{NAME}-%{VERSION}-%{RELEASE}' "$RPM_FILE")"
LOCAL_DLL="$SF_PUBLISH_DIR/$SF_BIN.dll"
REMOTE_DIR="/usr/share/$PKG_NAME"

sf_info "Verifying $PKG_NAME"
sf_note "local RPM   : $(basename "$RPM_FILE") ($LOCAL_NVR)"
sf_note "publish dir : $SF_PUBLISH_DIR"

TMP_DIR="$(mktemp -d)"
# keep the sf-lib.sh askpass cleanup: a later trap would otherwise replace it
trap 'rm -rf "$TMP_DIR"; _sf_askpass_cleanup' EXIT

# ------------------------------------------------------- local: L1 ----------
if [ ! -f "$LOCAL_DLL" ]; then
	fail "L1 published assembly missing: $LOCAL_DLL"
else
	local_dll_sha="$(_sf_sha256 "$LOCAL_DLL")"
	rpm_dll_sha="$(rpm -qp --qf "[ %{FILENAMES} %{FILEDIGESTS}\n]" "$RPM_FILE" \
		| awk -v f="$REMOTE_DIR/$SF_BIN.dll" '$1 == f {print $2}')"

	if [ -z "$rpm_dll_sha" ]; then
		fail "L1 RPM payload has no $REMOTE_DIR/$SF_BIN.dll"
	elif [ "$rpm_dll_sha" = "$local_dll_sha" ]; then
		pass "L1 RPM contains the current publish output (${local_dll_sha:0:16}...)"
	else
		fail "L1 RPM is STALE: payload $rpm_dll_sha != published $local_dll_sha"
	fi

	# rpm prints array-tag output with a leading space and no trailing newline
	# on the last element -> normalise exactly like the device side does.
	rpm -qp --qf "[ %{FILENAMES} %{FILEDIGESTS}\n]" "$RPM_FILE" \
		| sed 's/^[[:space:]]*//; s/[[:space:]]*$//' | grep -v '^$' | LC_ALL=C sort > "$TMP_DIR/local.txt"
fi

# ------------------------------------------------------------ device --------
sf_ping

# D1 installed NVR
DEVICE_NVR="$(sf_ssh_out "rpm -q --qf '%{NAME}-%{VERSION}-%{RELEASE}' $PKG_NAME 2>/dev/null; echo" | tail -1)" || true
if [ "$DEVICE_NVR" = "$LOCAL_NVR" ]; then
	pass "D1 installed NVR $DEVICE_NVR"
else
	fail "D1 installed NVR '${DEVICE_NVR:-<not installed>}' != RPM '$LOCAL_NVR'"
fi

# D2 payload digest lists (busybox awk prefixes array output with a space,
# and the last file has no trailing newline -> normalise before sorting)
sf_ssh_out "rpm -q --qf '[%{FILENAMES} %{FILEDIGESTS}\n]' $PKG_NAME 2>/dev/null; echo" \
	| sed 's/^[[:space:]]*//; s/[[:space:]]*$//' | grep -v '^$' | LC_ALL=C sort > "$TMP_DIR/device.txt" || true
if [ -s "$TMP_DIR/local.txt" ] && [ -s "$TMP_DIR/device.txt" ]; then
	if diff -q "$TMP_DIR/local.txt" "$TMP_DIR/device.txt" >/dev/null; then
		pass "D2 all $(wc -l < "$TMP_DIR/device.txt" | tr -d ' ') payload files match the RPM"
	else
		fail "D2 payload mismatch:"
		diff "$TMP_DIR/local.txt" "$TMP_DIR/device.txt" | head -20 | sed 's/^/         /'
	fi
else
	fail "D2 could not read the payload digest list from the device"
fi

# D3 rpm -V (files on disk vs rpmdb)
V_OUT="$(sf_ssh_out "rpm -V $PKG_NAME 2>&1" || true)"
if [ -z "$V_OUT" ]; then
	pass "D3 rpm -V clean (files on disk match the package)"
else
	fail "D3 rpm -V reported differences:"
	printf '%s\n' "$V_OUT" | head -10 | sed 's/^/         /'
fi

# D4 installed dll sha256
if [ -n "${local_dll_sha:-}" ]; then
	DEVICE_DLL_SHA="$(sf_ssh_out "sha256sum $REMOTE_DIR/$SF_BIN.dll 2>/dev/null" | awk '{print $1}' | tail -1)" || true
	if [ "$DEVICE_DLL_SHA" = "$local_dll_sha" ]; then
		pass "D4 installed $SF_BIN.dll matches the local build"
	else
		fail "D4 installed $SF_BIN.dll ${DEVICE_DLL_SHA:-<missing>} != local $local_dll_sha"
	fi
fi

# D6 installed native shim sha256 (the managed side also refuses another SFHOST_ABI_VERSION at start)
LOCAL_SO="$SF_PUBLISH_DIR/libsailfishhost.so"
if [ -f "$LOCAL_SO" ]; then
	local_so_sha="$(_sf_sha256 "$LOCAL_SO")"
	DEVICE_SO_SHA="$(sf_ssh_out "sha256sum $REMOTE_DIR/libsailfishhost.so 2>/dev/null" | awk '{print $1}' | tail -1)" || true
	if [ "$DEVICE_SO_SHA" = "$local_so_sha" ]; then
		pass "D6 installed libsailfishhost.so matches the local build"
	else
		fail "D6 installed libsailfishhost.so ${DEVICE_SO_SHA:-<missing>} != local $local_so_sha"
	fi
fi

# D5 stale instances
sf_push_helper "$SCRIPT_DIR/../remote/sf-kill-remote.sh" >/dev/null
PROC_OUT="$(sf_ssh_out "/tmp/sf-kill-remote.sh $PKG_NAME $SF_BIN --list" || true)"
if printf '%s\n' "$PROC_OUT" | grep -q '(deleted)'; then
	fail "D5 a process is still running a REPLACED binary (stale instance):"
	printf '%s\n' "$PROC_OUT" | grep '(deleted)' | sed 's/^/         /'
	sf_note "fix: ./tools/sf kill"
elif printf '%s\n' "$PROC_OUT" | grep -q '^RUNNING'; then
	pass "D5 running instance is the installed build"
	printf '%s\n' "$PROC_OUT" | grep '^RUNNING' | sed 's/^/         /'
else
	pass "D5 no app instance running (next launch uses the new binary)"
fi

# ------------------------------------------------------------- summary ------
INSTALL_TIME="$(sf_ssh_out "rpm -q --last $PKG_NAME 2>/dev/null" | head -1)" || true
[ -n "$INSTALL_TIME" ] && sf_note "installed: $INSTALL_TIME"

echo
if [ "$FAILURES" -eq 0 ]; then
	sf_ok "VERIFY PASSED - the device runs exactly this build"
	exit 0
fi
sf_fail "VERIFY FAILED - $FAILURES check(s) did not pass"
exit 1
