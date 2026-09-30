#!/bin/sh
# Remote helper: launch the installed app the way the Sailfish launcher does and
# check that lipstick treats it like a normal app.
#
# Usage: sf-package-launch-remote.sh <package> <binary> <mode>
#   windowmodel  lipstick WindowModel.launchProcess (closest to an app-grid tap)
#   sailjail     /usr/bin/sailjail -p <package>.desktop /usr/bin/<package>
#   direct       plain /usr/bin/<package> exec (sf-run parity)
#   diag         direct with the native diag channel on
#
# PASS = process alive and it owns the compositor's topmost window.
# privateTopmostWindowPolicyApplicationId is informational only: apps with
# Sandboxing=Disabled get a hex window-id there instead of their identity.
# No DOTNET_* env is injected, just like a grid tap.

set -u

PKG="${1:?package name}"
BIN="${2:?binary name}"
MODE="${3:?windowmodel|sailjail|direct}"

LIPSTICK=com.jolla.lipstick
LOG=/tmp/sf_pkgtest_launch.log

# ------------------------------------------------------------- environment --
. "$(dirname "$0")/sf-remote-env.sh"

top_pid() {
	dbus-send --session --print-reply --dest="$LIPSTICK" / \
		org.nemomobile.compositor.privateTopmostWindowProcessId 2>/dev/null \
		| awk '/int32/{print $2; exit}'
}
top_id() {
	dbus-send --session --print-reply --dest="$LIPSTICK" / \
		org.nemomobile.compositor.privateTopmostWindowPolicyApplicationId 2>/dev/null \
		| awk -F'"' '/string/{print $2; exit}'
}
app_pid() {
	# The cmdline is the launcher entry /usr/bin/<pkg>, which this helper's own cmdline
	# never contains, so no bracket trick is needed.
	pgrep -f "/usr/bin/$PKG" 2>/dev/null | head -1
}

# ------------------------------------------------------------ clean slate --
if [ -x /tmp/sf-kill-remote.sh ]; then
	/tmp/sf-kill-remote.sh "$PKG" "$BIN" >/dev/null 2>&1 || true
	sleep 1
fi
rm -f "$LOG"

echo "MODE=$MODE PKG=$PKG BIN=$BIN"

# ------------------------------------------------------------------ launch --
case "$MODE" in
	windowmodel)
		dbus-send --session --type=method_call --dest="$LIPSTICK" /WindowModel \
			local.Lipstick.WindowModel.launchProcess string:"/usr/bin/$PKG" \
			|| { echo "launchProcess call failed"; exit 1; }
		;;
	sailjail)
		cd "/usr/share/$PKG" || exit 1
		/usr/bin/sailjail -p "$PKG.desktop" "/usr/bin/$PKG" >"$LOG" 2>&1 &
		;;
	direct)
		cd "/usr/share/$PKG" || exit 1
		"/usr/bin/$PKG" >"$LOG" 2>&1 &
		;;
	diag)
		# The boot log then carries the resolved app id ("init ok ... app=<id>").
		cd "/usr/share/$PKG" || exit 1
		MAUI_SAILFISH_QT_HOST_DIAG=1 "/usr/bin/$PKG" >"$LOG" 2>&1 &
		;;
	*)
		echo "unknown mode: $MODE"
		exit 2
		;;
esac

# ------------------------------------------------- wait + association proof --
i=0
PID=""
TP=""
TID=""
while [ "$i" -lt 20 ]; do
	i=$((i + 1))
	sleep 1
	PID="$(app_pid)"
	[ -n "$PID" ] || continue
	TP="$(top_pid)"
	TID="$(top_id)"
	if [ "$TP" = "$PID" ] && [ "$TID" = "$PKG" ]; then
		break
	fi
done

ALIVE=no
if [ -n "$PID" ] && kill -0 "$PID" 2>/dev/null; then
	ALIVE=yes
fi

echo "PID=${PID:-none} ALIVE=$ALIVE TOPMOST_PID=${TP:-none} TOPMOST_APPID=${TID:-none} WAITED=${i}s"
if [ -f "$LOG" ]; then
	echo "--- launch log (tail) ---"
	tail -5 "$LOG"
fi
if [ "$MODE" = diag ]; then
	echo "--- init line (resolved app id) ---"
	grep -m1 'init ok' "$LOG" || echo "(no init line)"
fi

if [ "$ALIVE" = yes ] && [ "$TP" = "$PID" ]; then
	echo "RESULT=PASS"
	exit 0
fi
echo "RESULT=FAIL"
exit 1
