#!/bin/bash
# Finds a reachable Sailfish OS device and proves it is one before any deploy
# (which runs rpm as root) may target it.
# Usage: ./tools/sf detect [--candidates] [--quiet]
#
#   --candidates  print the candidate list and exit (no probing)
#   --quiet       only print the verdict line; exit code carries the result
#
# Exit: 0 = Sailfish device, 1 = none / not Sailfish, 2 = reachable but not authenticated.
# Probing uses BatchMode and a throwaway known_hosts, so it never prompts and never
# pins a candidate; only the selected endpoint is pinned by the identity check.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=../lib/sf-lib.sh
. "$SCRIPT_DIR/../lib/sf-lib.sh"

# Sailfish OS USB networking default; only a candidate when this host has an
# interface on that subnet.
SF_USB_DEFAULT_HOST="${SF_USB_DEFAULT_HOST:-192.168.2.15}"

_SF_PROBE_OPTS="-o BatchMode=yes -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null -o LogLevel=ERROR -o ConnectTimeout=5"

QUIET=0
MODE="detect"
while [ $# -gt 0 ]; do
	case "$1" in
		--candidates) MODE="candidates" ;;
		--quiet)      QUIET=1 ;;
		-h|--help)    sed -n '2,11p' "$0"; exit 0 ;;
		*)            sf_die "unknown argument: $1 (see --help)" ;;
	esac
	shift
done

_say() { [ "$QUIET" = 1 ] || sf_info "$*"; }

# ---------------------------------------------------------- candidate list ----
_usb_net_present() {
	# An address on the SFOS USB subnet means the phone exposed a network gadget.
	ifconfig 2>/dev/null | grep -qE 'inet 192\.168\.2\.[0-9]+'
}

_candidates() {
	printf '%s\n' "$SF_HOST"
	_usb_net_present && printf '%s\n' "$SF_USB_DEFAULT_HOST"
	# Already-pinned endpoints, in file order, minus the one we listed first.
	if [ -f "$SF_KNOWN_HOSTS" ]; then
		awk 'NF && $1 !~ /^#/ { print $1 }' "$SF_KNOWN_HOSTS"
	fi | while read -r h; do
		[ "$h" = "$SF_HOST" ] && continue
		[ "$h" = "$SF_USB_DEFAULT_HOST" ] && continue
		printf '%s\n' "$h"
	done
}

CANDIDATES="$(_candidates | awk 'NF && !seen[$0]++')"

if [ "$MODE" = candidates ]; then
	printf '%s\n' "$CANDIDATES"
	exit 0
fi

# ------------------------------------------------------------------ probing ----
# _sf_probe <host> -> AUTHED | REACHABLE | UNREACHABLE
_sf_probe() {
	local out rc=0
	out="$(ssh $_SF_PROBE_OPTS "$SF_USER@$1" true 2>&1)" || rc=$?
	[ "$rc" = 0 ] && { printf 'AUTHED'; return; }
	case "$out" in
		*"Permission denied"*|*"publickey"*|*"Too many authentication"*)
			printf 'REACHABLE' ;;
		*)
			printf 'UNREACHABLE' ;;
	esac
}

_say "probing for a reachable SSH endpoint (user: $SF_USER)"
FOUND=""
FOUND_STATE=""
while read -r host; do
	[ -n "$host" ] || continue
	state="$(_sf_probe "$host")"
	case "$state" in
		AUTHED)      _say "  $host  ->  reachable, key auth OK" ;;
		REACHABLE)   _say "  $host  ->  reachable, auth NOT set up" ;;
		UNREACHABLE) _say "  $host  ->  no SSH"; continue ;;
	esac
	if [ -z "$FOUND" ]; then FOUND="$host"; FOUND_STATE="$state"; fi
	# The configured endpoint wins; others are only suggested, never silently retargeted.
	if [ "$host" = "$SF_HOST" ]; then break; fi
done <<< "$CANDIDATES"

if [ -z "$FOUND" ]; then
	sf_error "no reachable SSH endpoint among the candidates:"
	printf '%s\n' "$CANDIDATES" | sed 's/^/    /' >&2
	sf_error "check the cable/Wi-Fi, and that Developer tools > Remote connection is on"
	exit 1
fi

if [ "$FOUND" != "$SF_HOST" ]; then
	sf_note "NOTE: $FOUND answered but the tooling is configured for $SF_HOST."
	sf_note "      it will keep using $SF_HOST. To switch: SF_HOST=$FOUND, or edit"
	sf_note "      the IP: line in $SF_CONNECT_INFO"
fi

# ------------------------------------------------- identity of the endpoint ----
# Uses the real SF_SSH_OPTS, so this pins the endpoint (TOFU) and verifies it later.
_say "verifying that $FOUND is a Sailfish OS device"
IDENTITY=""
if [ "$FOUND_STATE" = AUTHED ]; then
	IDENTITY="$(ssh -o BatchMode=yes $SF_SSH_OPTS "$SF_USER@$FOUND" \
		'cat /etc/os-release 2>/dev/null; echo "---"; uname -m' 2>/dev/null)" || IDENTITY=""
fi

if [ -z "$IDENTITY" ]; then
	# Unauthenticated means unproven; sf doctor treats this as a hard stop.
	printf 'UNVERIFIED %s (reachable, but not authenticated - run the pairing step)\n' "$FOUND"
	[ "$QUIET" = 1 ] || sf_note "pair with: ssh-copy-id -i ~/.ssh/id_ed25519.pub \"$SF_USER@$FOUND\""
	exit 2
fi

if printf '%s' "$IDENTITY" | grep -qi 'sailfish'; then
	arch="$(printf '%s\n' "$IDENTITY" | tail -1 | tr -d '[:space:]')"
	name="$(printf '%s\n' "$IDENTITY" | grep -i '^PRETTY_NAME=' | head -1 | cut -d= -f2- | tr -d '"')"
	printf 'OK %s %s %s\n' "$FOUND" "${name:-Sailfish OS}" "${arch:-unknown-arch}"

	# A RID/arch mismatch would only fail at exec time on the device; mapping matches
	# Microsoft.Maui.Platforms.SailfishOS.targets.
	case "$SF_RID" in
		linux-arm64) expect_arch="aarch64" ;;
		linux-x64)   expect_arch="x86_64" ;;
		linux-arm)   expect_arch="armv7hl" ;;
		*)           expect_arch="" ;;
	esac
	if [ -n "$expect_arch" ] && [ "$arch" != "$expect_arch" ]; then
		sf_note "WARNING: device arch is '$arch' but SF_RID=$SF_RID builds for '$expect_arch'"
	fi
	exit 0
fi

# SSH answers but it is not Sailfish: deploying would run rpm as root on the wrong machine.
sf_error "$FOUND answers SSH but /etc/os-release does not say Sailfish OS."
printf '%s\n' "$IDENTITY" | head -5 | sed 's/^/    /' >&2
sf_error "refusing to treat it as a deploy target. Fix IP: in $SF_CONNECT_INFO (or SF_HOST)."
exit 1
