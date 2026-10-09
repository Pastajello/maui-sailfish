#!/bin/sh
# Runs on the device: captures the screen to /tmp/sf-screen.png. lipstick's
# saveScreenshot is restricted to the privileged group, so it is called via devel-su.
# Usage on device: SF_SU_PASS=<devel-su-password> sf-screenshot-remote.sh

REMOTE=/tmp/sf-screen.png
# The password rides the environment, not argv, so it stays out of `ps`.
SU_PASS="${SF_SU_PASS:-${1:-}}"

. "$(dirname "$0")/sf-remote-env.sh"

echo "BUS=$DBUS_SESSION_BUS_ADDRESS"

rm -f "$REMOTE" 2>/dev/null

# lipstick validates the path against the session owner's home, not the caller's,
# so capture into /home/defaultuser and copy to /tmp.
CAPTURE_PNG=/home/defaultuser/sf-screen.png
DBUS_CMD="dbus-send --session --print-reply --dest=org.nemomobile.lipstick /org/nemomobile/lipstick/screenshot org.nemomobile.lipstick.saveScreenshot string:$CAPTURE_PNG"

if command -v screenshot >/dev/null 2>&1; then
	echo "--- using screenshot CLI ---"
	screenshot "$REMOTE" 2>&1
elif [ -n "$SU_PASS" ]; then
	echo "--- elevated saveScreenshot via devel-su ---"
	# devel-su -c does not split arguments, hence 'devel-su /bin/sh -c'.
	echo "$SU_PASS" | devel-su /bin/sh -c "export DBUS_SESSION_BUS_ADDRESS=$DBUS_SESSION_BUS_ADDRESS; $DBUS_CMD; cp $CAPTURE_PNG $REMOTE; chmod 644 $REMOTE; rm -f $CAPTURE_PNG" 2>&1 | head -8
else
	echo "--- unprivileged saveScreenshot (likely AccessDenied) ---"
	eval "$DBUS_CMD" 2>&1 | head -5
fi

sleep 1

if [ -f "$REMOTE" ]; then
	echo SCREENSHOT_OK
	ls -la "$REMOTE"
else
	echo SCREENSHOT_FAIL
fi
