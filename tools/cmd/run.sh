#!/bin/bash
# Launches the installed app on the device via sf-run-remote.sh and prints its logs.
# Usage: ./tools/sf run [--env NAME=VALUE]... [--diagnostics] [--wait SECONDS]
#
#   --env          forward a variable into the app process (repeatable)
#   --diagnostics  open the .NET diagnostics port (vsdbg/dotnet-trace attach);
#                  closed by default so unattended runs leave no socket behind
#   --wait S       after the launch, wait up to S seconds for the app to exit on its own
#                  (auto-shutdown legs, scripted tours); exit 0 when it did, 124 when it still runs
#   --follow       after the launch, stream the app's log until it exits and return its exit code;
#                  Ctrl+C stops the app on the device (what `dotnet run -f net11.0-sailfish` uses)

set -euo pipefail
case "${1:-}" in -h|--help) awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"; exit 0 ;; esac

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=../lib/sf-lib.sh
. "$SCRIPT_DIR/../lib/sf-lib.sh"

ENV_LINES=""
WAIT=""
FOLLOW=""
while [ $# -gt 0 ]; do
	case "$1" in
		--env)
			[ $# -ge 2 ] || sf_die "--env requires a NAME=VALUE argument"
			# Quote the value: the env file is sourced by /bin/sh on the device.
			name="${2%%=*}"
			value="${2#*=}"
			escaped="$(printf '%s' "$value" | sed "s/'/'\\\\''/g")"
			ENV_LINES="${ENV_LINES}export ${name}='${escaped}'"$'\n'
			shift 2
			;;
		--wait)
			[ $# -ge 2 ] || sf_die "--wait requires a number of seconds"
			case "$2" in ''|*[!0-9]*) sf_die "--wait takes whole seconds, got: '$2'" ;; esac
			WAIT="$2"
			shift 2
			;;
		--follow)
			FOLLOW=1
			shift
			;;
		--diagnostics)
			ENV_LINES="${ENV_LINES}export SF_DIAGNOSTICS='1'"$'\n'
			shift
			;;
		*)
			sf_die "unknown argument: $1 (see --help)"
			;;
	esac
done

ENV_B64=""
if [ -n "$ENV_LINES" ]; then
	ENV_B64="$(printf '%s' "$ENV_LINES" | base64 | tr -d '\n')"
fi

sf_push_helper "$SCRIPT_DIR/../remote/sf-kill-remote.sh"
sf_push_helper "$SCRIPT_DIR/../remote/sf-run-remote.sh"

[ -z "$FOLLOW" ] || [ -z "$WAIT" ] || sf_die "--follow and --wait exclude each other"

if [ -n "$FOLLOW" ]; then
	_sf_run_stop() {
		trap - INT TERM
		echo
		sf_info "stopping $SF_PKG on the device"
		sf_kill_app >/dev/null 2>&1 || true
		exit 130
	}
	trap _sf_run_stop INT TERM
	sf_ssh "/tmp/sf-run-remote.sh $SF_PKG $SF_BIN - $ENV_B64" \
		|| sf_die "the app did not stay alive on the device (see the log output above)"
	sf_info "streaming the app log (Ctrl+C stops $SF_PKG)"
	# The log from its first line (the launch window above showed part of it), until the process is gone. The bracketed
	# first letter keeps pgrep from matching this shell. No expect timeout: a quiet app is not a dead ssh.
	PKG_PATTERN="[${SF_PKG:0:1}]${SF_PKG:1}"
	SF_TIMEOUT=-1 sf_ssh "tail -n +1 -f /tmp/sf_run.log & t=\$!; while pgrep -f '$PKG_PATTERN' >/dev/null; do sleep 1; done; sleep 1; kill \$t 2>/dev/null; true" || true
	trap - INT TERM
	# The stream only ends by itself once the app is gone. Still running means the stream was cut (Ctrl+C reached expect
	# or ssh, but not always this shell's trap: bash skips it when the foreground child exits on its own): stop the app.
	if sf_ssh_out "pgrep -f '$PKG_PATTERN' >/dev/null && echo ALIVE" 2>/dev/null | grep -q ALIVE; then
		_sf_run_stop
	fi
	code="$(sf_ssh_out "grep -oE 'exit code -?[0-9]+' /tmp/sf_run.log | tail -1" 2>/dev/null | grep -oE -- '-?[0-9]+$')" || code=""
	[ -n "$code" ] || { sf_note "$SF_PKG ended without logging an exit code (killed or crashed?)"; exit 1; }
	[ "$code" = 0 ] && sf_ok "$SF_PKG exited with code 0" || sf_error "$SF_PKG exited with code $code"
	exit "$code"
fi

if [ -z "$WAIT" ]; then
	sf_ssh "/tmp/sf-run-remote.sh $SF_PKG $SF_BIN - $ENV_B64" \
		|| sf_die "the app did not stay alive on the device (see the log output above)"
	exit 0
fi

# --wait: an app that ends itself within the helper's 10 s window is fine here.
sf_ssh "/tmp/sf-run-remote.sh $SF_PKG $SF_BIN - $ENV_B64" || true
# The app's process pattern with its first letter bracketed, so pgrep never matches the ssh shell itself.
PKG_PATTERN="[${SF_PKG:0:1}]${SF_PKG:1}"
sf_info "waiting up to ${WAIT}s for $SF_PKG to exit"
# Done when the process is gone (an EventPipe trace is complete only then); after the renderer's
# "event loop exited" line the process gets 15 s to leave, so a slow teardown cannot stall a run.
waited=0
ending=0
while [ "$waited" -lt "$WAIT" ]; do
	# A failed ssh round trip says nothing about the app: poll again instead of calling it exited.
	st="$(sf_ssh_out "if pgrep -f '$PKG_PATTERN' >/dev/null; then grep -q 'event loop exited' /tmp/sf_run.log && echo ENDING || echo ALIVE; else echo EXITED; fi" 2>/dev/null)" || st=UNKNOWN
	case "$st" in
		*EXITED*)
			# The app logs its exit code last ("[Sailfish] exit code N"); a failed start (2: the shim or the QML did
			# not load, 1: startup threw) is a failed run, not just "exited".
			code="$(sf_ssh_out "grep -oE 'exit code -?[0-9]+' /tmp/sf_run.log | tail -1" 2>/dev/null | grep -oE -- '-?[0-9]+$')" || code=""
			if [ -z "$code" ]; then
				sf_note "$SF_PKG exited (~${waited}s after the launch window) without logging an exit code (killed or crashed?)"
				exit 0
			fi
			[ "$code" = 0 ] || { sf_error "$SF_PKG exited with code $code (~${waited}s after the launch window)"; exit "$code"; }
			sf_ok "$SF_PKG exited with code 0 (~${waited}s after the launch window)"; exit 0 ;;
		*ENDING*)
			ending=$((ending + 3))
			[ "$ending" -gt 15 ] && { sf_ok "$SF_PKG ended its event loop (~${waited}s after the launch window)"; exit 0; } ;;
	esac
	[ $((waited % 30)) -lt 3 ] && sf_keep_awake
	sleep 3
	waited=$((waited + 3))
done
sf_note "$SF_PKG still runs after ${WAIT}s"
exit 124
