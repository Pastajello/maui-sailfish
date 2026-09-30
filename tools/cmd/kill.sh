#!/bin/bash
# Kills every running instance of the app on the device (see sf-kill-remote.sh).
# Usage: ./tools/sf kill [--list]

set -euo pipefail
case "${1:-}" in -h|--help) awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"; exit 0 ;; esac

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=../lib/sf-lib.sh
. "$SCRIPT_DIR/../lib/sf-lib.sh"

case "${1:-}" in
	'')     MODE=kill ;;
	--list) MODE=--list ;;
	*)      sf_die "unknown argument: $1 (see --help)" ;;
esac
[ $# -le 1 ] || sf_die "unexpected argument: $2 (see --help)"

sf_kill_app "$MODE"
