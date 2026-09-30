#!/bin/bash
# Takes a compositor screenshot on the device (sf-screenshot-remote.sh) and copies it here.
# Usage: ./tools/sf-screenshot.sh [local-output.png]

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=sf-lib.sh
. "$SCRIPT_DIR/sf-lib.sh"

OUT="${1:-$SCRIPT_DIR/screenshots/sf-screen-$(date +%Y%m%d-%H%M%S).png}"
REMOTE_PNG="/tmp/sf-screen.png"

mkdir -p "$(dirname "$OUT")"

sf_push_helper "$SCRIPT_DIR/sf-screenshot-remote.sh"
sf_ssh "SF_SU_PASS='$SF_PASSWORD' /tmp/sf-screenshot-remote.sh" | grep -E 'SCREENSHOT_OK|SCREENSHOT_FAIL|BUS=|using |Error|error' || true
sf_scp_from "$REMOTE_PNG" "$OUT" || sf_die "could not download $REMOTE_PNG from the device"

if [ -s "$OUT" ]; then
	sf_ok "Screenshot saved: $OUT ($(wc -c < "$OUT" | tr -d ' ') bytes)"
else
	rm -f "$OUT"
	sf_die "screenshot is empty (is the app in the foreground?)"
fi
