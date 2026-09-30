#!/bin/bash
# Launches the installed app on the device via sf-run-remote.sh and prints its logs.
# Usage: ./tools/sf run [--env NAME=VALUE]... [--diagnostics] [--wait SECONDS]
#
#   --env          forward a variable into the app process (repeatable)
#   --diagnostics  open the .NET diagnostics port (vsdbg/dotnet-trace attach);
#                  closed by default so unattended runs leave no socket behind
#   --wait S       after the launch, wait up to S seconds for the app to exit on its own
#                  (auto-shutdown legs, scripted tours); exit 0 when it did, 124 when it still runs

set -euo pipefail
case "${1:-}" in -h|--help) awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"; exit 0 ;; esac

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=../lib/sf-lib.sh
. "$SCRIPT_DIR/../lib/sf-lib.sh"

ENV_LINES=""
WAIT=""
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
		*EXITED*) sf_ok "$SF_PKG exited (~${waited}s after the launch window)"; exit 0 ;;
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
