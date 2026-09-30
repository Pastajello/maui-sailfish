#!/bin/sh
# Runs on the Sailfish OS device: kills every running instance of the app.
# Usage: sf-kill-remote.sh [package-name] [binary-name] [--list]
#
# Not a plain pkill: grid launches run as /usr/bin/<pkg> under invoker + firejail while
# sf run runs ./<bin>, and invoker --single-instance would re-activate any survivor.
# pkill -f would also match this ssh session, so pids are matched on /proc/PID/exe and
# the first cmdline token, skipping our own ancestors.
#
# Exits 0 when no app instance is left running, 1 otherwise.

PKG="${1:-harbour-sample}"
BIN="${2:-Linux.SailfishOS.Sample}"
MODE="${3:-kill}"
PREFIX="/usr/share/$PKG"
LAUNCHER="/usr/bin/$PKG"

# --- own ancestor chain (never kill ourselves or the ssh session) -----------
ANCESTORS=" "
_p=$$
_i=0
while [ -n "$_p" ] && [ "$_p" != "0" ] && [ "$_p" != "1" ] && [ "$_i" -lt 16 ]; do
	ANCESTORS="$ANCESTORS$_p "
	_p=$(sed 's/.*) //' "/proc/$_p/stat" 2>/dev/null | awk '{print $1}')
	_i=$((_i + 1))
done

is_ancestor() {
	case "$ANCESTORS" in
		*" $1 "*) return 0 ;;
		*) return 1 ;;
	esac
}

# --- classify one pid -------------------------------------------------------
# Prints a reason when the pid belongs to the app, nothing otherwise.
match_reason() {
	_pid="$1"
	_exe=$(readlink "/proc/$_pid/exe" 2>/dev/null)
	_cmd=$(tr '\0' ' ' < "/proc/$_pid/cmdline" 2>/dev/null)

	# the ELF itself, including the "(deleted)" case of a replaced binary
	case "$_exe" in
		"$LAUNCHER"*) echo "exe=$_exe"; return 0 ;;
		"$PREFIX/"*) echo "exe=$_exe"; return 0 ;;
	esac

	# first cmdline token == app binary / launcher
	set -- $_cmd
	case "$1" in
		"$LAUNCHER"|"$BIN"|"./$BIN"|"$PREFIX/$BIN") echo "cmd=$1"; return 0 ;;
	esac

	# invoker (single-instance supervisor) and sailjail/firejail sandbox
	case "$_cmd" in
		*invoker*"--id=$PKG"*) echo "invoker"; return 0 ;;
		*firejail*"$PKG"*) echo "sandbox(firejail)"; return 0 ;;
		*sailjail*"$PKG"*) echo "sandbox(sailjail)"; return 0 ;;
	esac

	return 1
}

collect_pids() {
	for _d in /proc/[0-9]*; do
		_pid=${_d#/proc/}
		is_ancestor "$_pid" && continue
		match_reason "$_pid" >/dev/null 2>&1 && printf '%s\n' "$_pid"
	done
}

# --- list mode (no killing, used by sf verify) ---------------------------
if [ "$MODE" = "--list" ] || [ "$MODE" = "list" ]; then
	LIST=$(collect_pids)
	if [ -z "$LIST" ]; then
		echo "no running instance of $PKG"
	else
		for _pid in $LIST; do
			echo "RUNNING pid=$_pid $(match_reason "$_pid")"
		done
	fi
	exit 0
fi

# --- TERM, then KILL --------------------------------------------------------
PIDS=$(collect_pids)

if [ -z "$PIDS" ]; then
	echo "no running instance of $PKG"
	exit 0
fi

# Snapshot reasons first: killing invoker/sandbox can make the app vanish before we log it.
TARGETS=""
for _pid in $PIDS; do
	TARGETS="$TARGETS$_pid|$(match_reason "$_pid")
"
done

printf '%s' "$TARGETS" | while IFS='|' read -r _pid _reason; do
	[ -n "$_pid" ] || continue
	echo "killing pid=$_pid ($_reason)"
	kill -TERM "$_pid" 2>/dev/null
done
sleep 2

for _pid in $PIDS; do
	if [ -d "/proc/$_pid" ]; then
		echo "pid=$_pid survived SIGTERM, sending SIGKILL"
		kill -KILL "$_pid" 2>/dev/null
	fi
done

sleep 1

LEFT=$(collect_pids)
if [ -n "$LEFT" ]; then
	echo "STILL_RUNNING:"
	for _pid in $LEFT; do
		echo "  pid=$_pid $(match_reason "$_pid")"
	done
	exit 1
fi

echo "all instances of $PKG stopped"
exit 0
