#!/bin/bash
# Runs the installed app with the given env and takes a compositor screenshot for
# every "SF-SHOT <name>" marker it logs, into <out>/<name>.png. The app holds each
# marked state for MAUI_SAILFISH_SHOT_HOLD_MS (--hold).
#
# Usage: ./tools/sf-shots.sh [--out DIR] [--hold MS] --env NAME=VALUE [--env ...]
#   e.g. ./tools/sf-shots.sh --env MAUI_SAILFISH_QT_HOST_PULLEY_DIAG=1
#
# Stops when the app exits or after 300 s. Default out: tools/screenshots/shots-<timestamp>;
# in-app filmstrip frames (/tmp/sf-film) land in <out>/sf-film.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=sf-lib.sh
. "$SCRIPT_DIR/sf-lib.sh"

OUT="$SCRIPT_DIR/screenshots/shots-$(date +%Y%m%d-%H%M%S)"
HOLD_MS=6000
RUN_ARGS=(--env MAUI_SAILFISH_QT_HOST=1 --env MAUI_SAILFISH_QT_HOST_DIAG=1 --env MAUI_SAILFISH_QT_HOST_AUTO_SHUTDOWN=1)
while [ $# -gt 0 ]; do
	case "$1" in
		--out)  OUT="$2"; shift ;;
		--hold) HOLD_MS="$2"; shift ;;
		--env)  RUN_ARGS+=(--env "$2"); shift ;;
		-h|--help) sed -n '2,10p' "$0"; exit 0 ;;
		*) sf_die "unknown argument: $1 (see --help)" ;;
	esac
	shift
done
RUN_ARGS+=(--env "MAUI_SAILFISH_SHOT_HOLD_MS=$HOLD_MS")
mkdir -p "$OUT"

# A fresh log, so markers from an earlier run cannot fire a shot.
sf_ssh "rm -f /tmp/sf_run.log" >/dev/null 2>&1 || true
"$SCRIPT_DIR/sf-run.sh" "${RUN_ARGS[@]}" >"$OUT/run.log" 2>&1 &

TAKEN=" "
DEADLINE=$(( $(date +%s) + 300 ))
sleep 5
while [ "$(date +%s)" -lt "$DEADLINE" ]; do
	state="$(sf_ssh_out "grep -o 'SF-SHOT [A-Za-z0-9_-]*' /tmp/sf_run.log 2>/dev/null | cut -d' ' -f2 | tr '\n' ' '; grep -q 'event loop exited' /tmp/sf_run.log 2>/dev/null && echo EXITED" 2>/dev/null || true)"
	for name in ${state//EXITED/}; do
		case "$TAKEN" in *" $name "*) continue ;; esac
		TAKEN="$TAKEN$name "
		"$SCRIPT_DIR/sf-screenshot.sh" "$OUT/$name.png" >/dev/null 2>&1 \
			&& sf_ok "shot $name -> $OUT/$name.png" \
			|| sf_fail "shot $name failed"
	done
	case "$state" in *EXITED*) break ;; esac
	sleep 1
done
sf_ssh_out "grep -E 'ACCEPTANCE|CHECK FAIL' /tmp/sf_run.log" 2>/dev/null | tail -20 || true
# In-app filmstrip frames (diag Film()), if the leg wrote any.
if sf_ssh_out "ls /tmp/sf-film/*.png >/dev/null 2>&1 && tar cf /tmp/sf-film.tar -C /tmp sf-film && echo FILM" 2>/dev/null | grep -q FILM; then
	sf_scp_from /tmp/sf-film.tar "$OUT/film.tar" >/dev/null 2>&1 \
		&& tar xf "$OUT/film.tar" -C "$OUT" && rm -f "$OUT/film.tar" \
		&& sf_ok "filmstrip -> $OUT/sf-film ($(ls "$OUT/sf-film" | wc -l | tr -d ' ') frames)" \
		|| sf_fail "filmstrip download failed"
fi
# Consecutive identical shots are either the same state or a frozen compositor
# surface (seen after a crashed run); flag them.
prev_sum=""; prev_name=""
for name in $TAKEN; do
	sum="$(shasum "$OUT/$name.png" 2>/dev/null | cut -d' ' -f1)"
	if [ -n "$sum" ] && [ "$sum" = "$prev_sum" ]; then
		sf_info "NOTE shots '$prev_name' and '$name' are byte-identical — fine if they show the same state; if not, the compositor surface was frozen (re-run)"
	fi
	prev_sum="$sum"; prev_name="$name"
done
sf_info "screenshots in $OUT"
