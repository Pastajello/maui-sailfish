#!/bin/bash
# Takes a compositor screenshot on the device (sf-screenshot-remote.sh) and copies it here.
# Usage: ./tools/sf screenshot [local-output.png]

set -euo pipefail
case "${1:-}" in -h|--help) awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"; exit 0 ;; esac

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=../lib/sf-lib.sh
. "$SCRIPT_DIR/../lib/sf-lib.sh"

[ $# -le 1 ] || sf_die "usage: $0 [local-output.png]"
case "${1:-}" in -*) sf_die "unknown argument: $1 (see --help)" ;; esac
OUT="${1:-$SF_REPO_ROOT/artifacts/screenshots/sf-screen-$(date +%Y%m%d-%H%M%S).png}"
mkdir -p "$(dirname "$OUT")"

sf_screenshot "$OUT" || sf_die "could not download the screenshot from the device"

if [ -s "$OUT" ]; then
	sf_ok "Screenshot saved: $OUT ($(wc -c < "$OUT" | tr -d ' ') bytes)"
else
	rm -f "$OUT"
	sf_die "screenshot is empty (is the app in the foreground?)"
fi
