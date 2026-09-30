#!/bin/bash
# Launches the installed app on the device via sf-run-remote.sh and prints its logs.
# Usage: ./tools/sf-run.sh [--env NAME=VALUE]... [--diagnostics]
#
#   --env          forward a variable into the app process (repeatable)
#   --diagnostics  open the .NET diagnostics port (vsdbg/dotnet-trace attach);
#                  closed by default so unattended runs leave no socket behind

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=sf-lib.sh
. "$SCRIPT_DIR/sf-lib.sh"

ENV_LINES=""
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
		--diagnostics)
			ENV_LINES="${ENV_LINES}export SF_DIAGNOSTICS='1'"$'\n'
			shift
			;;
		*)
			shift
			;;
	esac
done

ENV_B64=""
if [ -n "$ENV_LINES" ]; then
	ENV_B64="$(printf '%s' "$ENV_LINES" | base64 | tr -d '\n')"
fi

sf_push_helper "$SCRIPT_DIR/sf-kill-remote.sh"
sf_push_helper "$SCRIPT_DIR/sf-run-remote.sh"

sf_ssh "/tmp/sf-run-remote.sh $SF_PKG $SF_BIN - $ENV_B64" \
	|| sf_die "the app did not stay alive on the device (see the log output above)"
