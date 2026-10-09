#!/bin/sh
# Device-side helper: sf-run-remote.sh <package-name> <binary-name> [- <env-b64>]
# Launches via /usr/bin/<package> (the desktop entry's Exec=), not the private binary,
# to match an app-grid launch including the Sailjail configuration.

set -u

PKG="${1:-harbour-sample}"
BIN="${2:-Linux.SailfishOS.Sample}"

DIR="/usr/share/$PKG"
BIN_PATH="$DIR/$BIN"
LAUNCHER="/usr/bin/$PKG"
DESKTOP="/usr/share/applications/$PKG.desktop"

LOG="/tmp/sf_run.log"

echo "============================================================"
echo " Sailfish application launcher"
echo "============================================================"
echo "PKG=$PKG"
echo "BIN=$BIN"
echo "DIR=$DIR"
echo "BIN_PATH=$BIN_PATH"
echo "LAUNCHER=$LAUNCHER"
echo "DESKTOP=$DESKTOP"
echo

# --- Environment ---

. "$(dirname "$0")/sf-remote-env.sh"

echo "--- environment ---"
echo "USER=$(id -un)"
echo "UID=$(id -u)"
echo "XDG_RUNTIME_DIR=$XDG_RUNTIME_DIR"
echo "WAYLAND_DISPLAY=$WAYLAND_DISPLAY"
echo "DBUS_SESSION_BUS_ADDRESS=${DBUS_SESSION_BUS_ADDRESS:-<unset>}"
echo

# --- Kill previous instance ---

echo "--- stopping previous instance ---"

if [ -x /tmp/sf-kill-remote.sh ]; then
    /tmp/sf-kill-remote.sh "$PKG" "$BIN" || {
        echo "ERROR: could not stop previous application instance"
        exit 1
    }
else
    echo "WARN: /tmp/sf-kill-remote.sh missing"
fi

sleep 1

# --- Verify installed package ---

echo "--- installed package ---"

rpm -q --qf \
    '%{NAME}-%{VERSION}-%{RELEASE} installed=%{INSTALLTIME}\n' \
    "$PKG" 2>/dev/null || {
        echo "ERROR: package $PKG is not installed"
        exit 1
    }

echo

echo "--- installed files ---"

ls -la \
    "$BIN_PATH" \
    "$DIR/$BIN.dll" \
    "$LAUNCHER" \
    "$DESKTOP" \
    2>/dev/null || true

echo

# The Harbour layout has no apphost: /usr/bin/$PKG is a native launcher hosting $DIR/lib/$BIN.dll.
if [ ! -x "$BIN_PATH" ] && [ ! -f "$DIR/lib/$BIN.dll" ]; then
    echo "ERROR: application binary is missing or not executable:"
    echo "  $BIN_PATH (nor the Harbour payload $DIR/lib/$BIN.dll)"
    exit 1
fi

if [ ! -x "$LAUNCHER" ]; then
    echo "ERROR: Sailfish launcher is missing or not executable:"
    echo "  $LAUNCHER"
    exit 1
fi

if [ ! -f "$DESKTOP" ]; then
    echo "ERROR: desktop file is missing:"
    echo "  $DESKTOP"
    exit 1
fi

echo "--- desktop entry ---"
cat "$DESKTOP"
echo

echo "--- application binary ---"
file "$BIN_PATH" 2>/dev/null || true
sha256sum "$BIN_PATH" 2>/dev/null || true
sha256sum "$DIR/$BIN.dll" 2>/dev/null || true
echo

# --- Launcher ---

echo "--- launcher ---"
ls -l "$LAUNCHER"
readlink -f "$LAUNCHER" 2>/dev/null || true
echo

echo "--- launcher type ---"
file "$LAUNCHER" 2>/dev/null || true
echo

# --- Sailjail ---

echo "--- sailjail ---"

if command -v sailjail >/dev/null 2>&1; then
    echo "sailjail: $(command -v sailjail)"
else
    echo "sailjail: NOT FOUND"
fi

echo

# --- Wayland ---

echo "--- Wayland socket ---"

if [ -S "$XDG_RUNTIME_DIR/$WAYLAND_DISPLAY" ]; then
    echo "WAYLAND SOCKET OK:"
    ls -l "$XDG_RUNTIME_DIR/$WAYLAND_DISPLAY"
else
    echo "WARNING: Wayland socket not found:"
    echo "  $XDG_RUNTIME_DIR/$WAYLAND_DISPLAY"
fi

echo

# --- Launch ---

rm -f "$LOG"

echo "--- launching ---"
echo "Command:"
echo "  $LAUNCHER"
echo

# Launch via /usr/bin/$PKG, never the private binary (see header).

cd "$DIR" || {
    echo "ERROR: cannot cd to $DIR"
    exit 1
}

# Optional app environment from sf run --env: base64 NAME=VALUE lines, exported via set -a.
ENV_B64="${4:-}"
if [ -n "$ENV_B64" ]; then
    if printf '%s' "$ENV_B64" | base64 -d > /tmp/sf-app-env 2>/dev/null && [ -s /tmp/sf-app-env ]; then
        echo "--- forwarded app environment ---"
        cat /tmp/sf-app-env
        set -a
        . /tmp/sf-app-env
        set +a
    else
        echo "WARN: could not decode the forwarded environment blob"
    fi
fi

# The diagnostics port stays closed unless SF_DIAGNOSTICS=1 (vsdbg/dotnet-trace attach),
# so unattended runs never leave a diagnostic socket behind.
if [ "${SF_DIAGNOSTICS:-0}" = "1" ]; then
    export DOTNET_EnableDiagnostics=1
    echo "--- diagnostics port OPEN (DOTNET_EnableDiagnostics=1) ---"
else
    export DOTNET_EnableDiagnostics=0
fi

# SF_SAILJAIL=1 (in the forwarded environment) starts the app inside its Sailjail sandbox, with the desktop
# entry's permissions, as an app-grid launch of a sandboxed package does; otherwise it runs unsandboxed.
set --
if [ "${SF_SAILJAIL:-0}" = "1" ]; then
    echo "--- launching in the Sailjail sandbox (sailjail -p $PKG) ---"
    set -- sailjail -p "$PKG" --
fi

# Its own session and no inherited stdin: nothing ties the app to this ssh session, so ssh can
# close as soon as this helper ends. setsid execs in place (a background job is no group leader),
# so $! stays the app's PID (sailjail's under SF_SAILJAIL; it waits for the sandboxed app).
if command -v setsid >/dev/null 2>&1; then
    setsid "$@" "$LAUNCHER" >"$LOG" 2>&1 </dev/null &
else
    "$@" "$LAUNCHER" >"$LOG" 2>&1 </dev/null &
fi

APP_PID=$!

echo "LAUNCHED_PID=$APP_PID"

# --- Wait for startup ---

echo
echo "--- waiting for application ---"

i=0

while [ "$i" -lt 10 ]; do
    i=$((i + 1))

    sleep 1

    if kill -0 "$APP_PID" 2>/dev/null; then
        echo "t=${i}s PROCESS_ALIVE=yes"
    else
        echo "t=${i}s PROCESS_ALIVE=no"
        break
    fi
done

# --- Process information ---

echo
echo "--- process ---"

if [ -d "/proc/$APP_PID" ]; then
    echo "PID=$APP_PID"
    echo "EXE=$(readlink "/proc/$APP_PID/exe" 2>/dev/null || true)"
    echo "CMDLINE=$(tr '\0' ' ' < "/proc/$APP_PID/cmdline" 2>/dev/null || true)"
else
    echo "PID=$APP_PID is no longer running"
fi

echo

echo "--- matching processes ---"

if [ -x /tmp/sf-kill-remote.sh ]; then
    /tmp/sf-kill-remote.sh "$PKG" "$BIN" --list 2>/dev/null || true
fi

# --- Logs ---

echo
echo "--- application stdout/stderr ---"

if [ -f "$LOG" ]; then
    cat "$LOG"
else
    echo "(no log file)"
fi

echo

echo "--- journal ---"

journalctl --user \
    --since "30 seconds ago" \
    2>/dev/null \
    | grep -i -E \
        "$PKG|$BIN|invoker|sailjail|wayland|lipstick|dotnet" \
    | tail -100 || true

echo

# --- Final status ---

if kill -0 "$APP_PID" 2>/dev/null; then
    echo "============================================================"
    echo " RESULT: application is running"
    echo " PID:    $APP_PID"
    echo " EXE:    $(readlink "/proc/$APP_PID/exe" 2>/dev/null || true)"
    echo "============================================================"
    exit 0
fi

echo "============================================================"
echo " RESULT: application died during startup"
echo "============================================================"

exit 1
