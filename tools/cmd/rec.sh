#!/bin/bash
# Records the phone's screen while you use it: start, do things on the phone, stop.
# Usage: tools/sf rec start [-f OUT.mp4] [--audio] [--width PX] [--max-seconds N]
#        tools/sf rec stop [-f OUT.mp4] [--gif] [--keep-raw]
#        tools/sf rec status
#
#   start          builds and uploads the recorder when needed, starts it on the phone, returns
#   -f OUT.mp4     where stop writes the video (default ./sailfish-rec-<timestamp>.mp4);
#                  given again at stop, the stop value wins
#   --audio        also record the phone's sound (PulseAudio)
#   --width PX     output width (default 720; the phone's 1032 is the full resolution)
#   --max-seconds  the recorder stops by itself after N s (default 1800)
#   stop           finishes the recording, pulls it and encodes the MP4 (upright, 60 fps)
#   --gif          also writes OUT.gif (360 px, 12 fps)
#   --keep-raw     also keeps the phone's raw H.264 file and frame timestamps (OUT.raw.mp4)
#   status         whether a recording is running, and for how long
#
# Full-screen compositor capture with the phone's hardware encoder (see tools/screenrec).
# Needs the developer-mode password (tools/sf setup) and ffmpeg on this machine.

set -euo pipefail
case "${1:-}" in ''|-h|--help) awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"; [ -n "${1:-}" ]; exit $? ;; esac

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=../lib/sf-lib.sh
. "$SCRIPT_DIR/../lib/sf-lib.sh"
# shellcheck source=../lib/sf-screenrec.sh
. "$SCRIPT_DIR/../lib/sf-screenrec.sh"

STATE_DIR="${XDG_STATE_HOME:-$HOME/.local/state}/maui-sailfish"
STATE="$STATE_DIR/rec.state"   # what `start` chose, for `stop`: OUT, WIDTH, AUDIO, STARTED

abs_path() { # <path> - absolute path of a file that may not exist yet
	local dir
	dir="$(dirname "$1")"
	mkdir -p "$dir"
	printf '%s/%s' "$(cd "$dir" && pwd)" "$(basename "$1")"
}

ACTION="$1"
shift
OUT=""
AUDIO=0
WIDTH=720
MAXS=1800
GIF=0
KEEP_RAW=0
while [ $# -gt 0 ]; do
	case "$ACTION:$1" in
		start:-f|stop:-f|start:--file|stop:--file)
			[ $# -ge 2 ] || sf_die "$1 needs a file name"
			OUT="$(abs_path "$2")"; shift ;;
		start:--audio) AUDIO=1 ;;
		start:--width)
			case "${2:-}" in ''|*[!0-9]*) sf_die "--width takes pixels" ;; esac
			WIDTH="$2"; shift ;;
		start:--max-seconds)
			case "${2:-}" in ''|*[!0-9]*) sf_die "--max-seconds takes whole seconds" ;; esac
			MAXS="$2"; shift ;;
		stop:--gif) GIF=1 ;;
		stop:--keep-raw) KEEP_RAW=1 ;;
		*) sf_die "unknown argument for '$ACTION': $1 (see --help)" ;;
	esac
	shift
done

case "$ACTION" in
start)
	st="$(sf_rec_state)"
	[ "$st" = RECORDING ] && sf_die "a recording is already running on the phone - tools/sf rec stop first"
	[ -n "$OUT" ] || OUT="$(abs_path "sailfish-rec-$(date +%Y%m%d-%H%M%S).mp4")"
	sf_info "preparing the recorder"
	sf_rec_prepare
	sf_rec_start "$MAXS" "$AUDIO"
	mkdir -p "$STATE_DIR"
	printf 'OUT=%s\nWIDTH=%s\nAUDIO=%s\nSTARTED=%s\n' "$OUT" "$WIDTH" "$AUDIO" "$(date +%s)" > "$STATE"
	sf_ok "recording (stops by itself after ${MAXS}s) - use the phone, then: tools/sf rec stop"
	sf_note "output: $OUT"
	;;
stop)
	STOP_OUT="$OUT"
	OUT=""; STARTED=""
	if [ -f "$STATE" ]; then
		OUT="$(sed -n 's/^OUT=//p' "$STATE")"
		WIDTH="$(sed -n 's/^WIDTH=//p' "$STATE")"
		AUDIO="$(sed -n 's/^AUDIO=//p' "$STATE")"
		STARTED="$(sed -n 's/^STARTED=//p' "$STATE")"
	fi
	[ -n "$STOP_OUT" ] && OUT="$STOP_OUT"
	[ -n "$OUT" ] || OUT="$(abs_path "sailfish-rec-$(date +%Y%m%d-%H%M%S).mp4")"
	st="$(sf_rec_state)"
	[ "$st" = IDLE ] && sf_die "no recording on the phone (tools/sf rec start)"
	[ -n "$STARTED" ] && sf_info "stopping after $(( $(date +%s) - STARTED ))s"
	sf_rec_stop
	WORK="$(mktemp -d "${TMPDIR:-/tmp}/sf-rec.XXXXXX")"
	trap 'rm -rf "$WORK"' EXIT
	sf_rec_pull "$WORK"
	rm -f "$STATE"
	sf_info "encoding → $OUT"
	sf_rec_encode "$WORK/screen.mp4" "$OUT" "${WIDTH:-720}" "${AUDIO:-0}"
	if [ "$KEEP_RAW" = 1 ]; then
		cp "$WORK/screen.mp4" "${OUT%.mp4}.raw.mp4"
		cp "$WORK/screen.mp4.frames.tsv" "${OUT%.mp4}.raw.mp4.frames.tsv" 2>/dev/null || true
		sf_note "raw recording: ${OUT%.mp4}.raw.mp4"
	fi
	if [ "$GIF" = 1 ]; then sf_rec_gif "$OUT" "${OUT%.mp4}.gif"; fi
	;;
status)
	st="$(sf_rec_state)"
	case "$st" in
		RECORDING)
			started="$(sed -n 's/^STARTED=//p' "$STATE" 2>/dev/null || true)"
			echo "recording${started:+ for $(( $(date +%s) - started ))s}$( [ -f "$STATE" ] && printf ' -> %s' "$(sed -n 's/^OUT=//p' "$STATE")")" ;;
		FINISHED) echo "finished (hit --max-seconds or stopped) - tools/sf rec stop pulls it" ;;
		*)        echo "idle" ;;
	esac
	;;
*)
	sf_die "unknown action: $ACTION (start, stop or status; see --help)" ;;
esac
