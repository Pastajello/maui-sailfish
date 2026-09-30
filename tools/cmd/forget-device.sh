#!/bin/bash
# Removes the pinned host key of the current device from the tooling's known_hosts,
# so the next connection is first contact (TOFU) again.
# Usage: ./tools/sf forget-device [--all] [--list]
#
#   --all    drop every pinned device, not just $SF_HOST
#   --list   show what is currently pinned, change nothing
#
# Use after a reflash or key rotation. An unexplained "REMOTE HOST IDENTIFICATION HAS
# CHANGED" means a different endpoint, which the next deploy would run rpm as root on.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=../lib/sf-lib.sh
. "$SCRIPT_DIR/../lib/sf-lib.sh"

MODE="one"
case "${1:-}" in
	--all)  MODE="all" ;;
	--list) MODE="list" ;;
	'')     ;;
	-h|--help) sed -n '2,10p' "$0"; exit 0 ;;
	*)      sf_die "unknown argument: $1 (see --help)" ;;
esac

if [ ! -f "$SF_KNOWN_HOSTS" ]; then
	sf_note "no pinned devices yet ($SF_KNOWN_HOSTS does not exist)"
	exit 0
fi

if [ "$MODE" = list ]; then
	sf_info "pinned device host keys in $SF_KNOWN_HOSTS"
	# Host + key type only; the key material is just noise in logs.
	awk 'NF && $1 !~ /^#/ { printf "    %-28s %s\n", $1, $2 }' "$SF_KNOWN_HOSTS"
	exit 0
fi

if [ "$MODE" = all ]; then
	sf_info "forgetting every pinned device"
	rm -f "$SF_KNOWN_HOSTS" "$SF_KNOWN_HOSTS.old"
	sf_ok "removed $SF_KNOWN_HOSTS"
	exit 0
fi

sf_info "forgetting $SF_HOST"
# ssh-keygen rewrites the file and leaves a <file>.old backup next to it.
ssh-keygen -R "$SF_HOST" -f "$SF_KNOWN_HOSTS" >/dev/null 2>&1 \
	|| sf_die "could not remove the entry for $SF_HOST from $SF_KNOWN_HOSTS"
rm -f "$SF_KNOWN_HOSTS.old"

if [ -s "$SF_KNOWN_HOSTS" ]; then
	sf_ok "un-pinned $SF_HOST; other devices kept"
else
	rm -f "$SF_KNOWN_HOSTS"
	sf_ok "un-pinned $SF_HOST (no devices left)"
fi
sf_note "next connection to $SF_HOST will be treated as first contact and re-pinned"
