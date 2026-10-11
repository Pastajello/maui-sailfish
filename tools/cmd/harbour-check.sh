#!/bin/bash
# Checks an app RPM against the Jolla Store's (Harbour's) own rule lists, from any build host: the package name and
# file locations, Requires, QML imports, the libraries every ELF file links, the Sailjail permissions and keys, the
# icons. The rules are sailfishos/sdk-harbour-rpmvalidator (the lists the Store's validator uses), cloned once into
# ~/.cache/maui-sailfish/harbour-rules and updated with --update. Exits non-zero when the Store would refuse the RPM.
# Usage: ./tools/sf harbour-check [--update] [app.rpm]
#   default RPM: the newest one in $SF_SAMPLE_DIR/bin/SailfishRpm (build it with -p:SailfishHarbour=true)

set -euo pipefail
case "${1:-}" in -h|--help) awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"; exit 0 ;; esac

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=../lib/sf-lib.sh
. "$SCRIPT_DIR/../lib/sf-lib.sh"

UPDATE=0
RPM=""
for arg in "$@"; do
	case "$arg" in
		--update) UPDATE=1 ;;
		-*) sf_die "unknown argument: $arg (see --help)" ;;
		*) RPM="$arg" ;;
	esac
done
if [ -z "$RPM" ]; then
	RPM="$(ls -t "$SF_SAMPLE_DIR"/bin/SailfishRpm/*.rpm 2>/dev/null | head -1 || true)"
	[ -n "$RPM" ] || sf_die "no RPM given and none in $SF_SAMPLE_DIR/bin/SailfishRpm"
fi
[ -f "$RPM" ] || sf_die "no such RPM: $RPM"

RULES="${XDG_CACHE_HOME:-$HOME/.cache}/maui-sailfish/harbour-rules"
if [ ! -d "$RULES/.git" ]; then
	sf_info "Fetching the Harbour rules (sailfishos/sdk-harbour-rpmvalidator)"
	mkdir -p "$(dirname "$RULES")"
	git clone --quiet --depth 1 https://github.com/sailfishos/sdk-harbour-rpmvalidator "$RULES" \
		|| sf_die "could not clone the Harbour rules"
elif [ "$UPDATE" -eq 1 ]; then
	git -C "$RULES" pull --quiet --ff-only || sf_note "could not update the Harbour rules; using the cached ones"
fi
sf_note "rules: $RULES ($(git -C "$RULES" log -1 --format='%h %cs' 2>/dev/null || echo unknown))"

sf_info "Checking $(basename "$RPM")"
python3 -I "$SCRIPT_DIR/../py/sf-harbour-check.py" "$RPM" "$RULES"
