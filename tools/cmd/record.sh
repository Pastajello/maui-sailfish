#!/bin/bash
# Records an MP4 from the phone while running the app (by default the showcase tour).
#
# Usage: tools/sf record <out.mp4> [--app] [--audio] [--no-ambience] [--keep DIR]
#                           [--max-seconds N] [--width PX] [--env NAME=VALUE ...]
#        tools/sf record <out.mp4> --manual [--seconds N] [--audio]
#
#   default   full-screen compositor capture (tools/screenrec) with the hardware H.264
#             encoder, real timestamps, resampled to 60 fps; needs the devel-su password
#   --app     the app records its own window (<=15 fps PNG) over the ambience
#             background (--no-ambience: black); no password needed
#   --manual  record the screen for --seconds N (default 30); no app is started
#
# Other --env values replace the showcase env; recording then stops when the app exits
# or after --max-seconds (150). --width defaults to 720. --keep DIR keeps the raw
# recording and app log for tools/sf page-load. Needs ffmpeg.

set -euo pipefail
case "${1:-}" in -h|--help) awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"; exit 0 ;; esac
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=../lib/sf-lib.sh
. "$SCRIPT_DIR/../lib/sf-lib.sh"
. "$SCRIPT_DIR/../lib/sf-screenrec.sh"

usage="usage: $0 <out.mp4> [--app|--manual [--seconds N]] [--audio] [--no-ambience] [--keep DIR] [--max-seconds N] [--width PX] [--env NAME=VALUE ...]"
OUT="${1:-}"
[ -n "$OUT" ] && [ "${OUT#-}" = "$OUT" ] || sf_die "$usage"
shift
MODE=hw
AMBIENCE=1
AUDIO=0
KEEP=""
MAX=150
SECONDS_MANUAL=30
WIDTH=720
ENVS=()
while [ $# -gt 0 ]; do
	case "$1" in
		--app) MODE=app; shift ;;
		--manual) MODE=manual; shift ;;
		--seconds) SECONDS_MANUAL="$2"; shift 2 ;;
		--audio) AUDIO=1; shift ;;
		--no-ambience) AMBIENCE=0; shift ;;
		--keep) KEEP="$2"; shift 2 ;;
		--max-seconds) MAX="$2"; shift 2 ;;
		--width) WIDTH="$2"; shift 2 ;;
		--env) ENVS+=(--env "$2"); shift 2 ;;
		*) sf_die "$usage" ;;
	esac
done
command -v ffmpeg >/dev/null || sf_die "ffmpeg not found (macOS: brew install ffmpeg)"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/sf-record.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

# The app's process pattern with its first letter bracketed, so pgrep never matches the ssh shell itself.
PKG_PATTERN="[${SF_PKG:0:1}]${SF_PKG:1}"
SHOWCASE=0
if [ "$MODE" != manual ] && [ ${#ENVS[@]} -eq 0 ]; then
	SHOWCASE=1
	ENVS=(--env MAUI_SAILFISH_QT_HOST=1 --env MAUI_SAILFISH_QT_HOST_DIAG=1 --env MAUI_SAILFISH_QT_HOST_AUTO_SHUTDOWN=1
	      --env MAUI_SAILFISH_QT_HOST_SHOWCASE=1 --env MAUI_SAILFISH_ORIENTATION=Portrait)   # the tour's gestures are portrait
fi

# wait_for_app - until the showcase is done or the app's event loop exits (at most
# $MAX s), keeping the display awake; then print the app's last showcase/crash lines.
wait_for_app() {
	local waited=0 st
	while [ "$waited" -lt "$MAX" ]; do
		if [ "$SHOWCASE" = 1 ]; then
			st="$(sf_ssh_out "grep -q 'SHOWCASE done' /tmp/sf_run.log && echo DONE; grep -q 'event loop exited' /tmp/sf_run.log && echo DONE; echo RUNNING" 2>/dev/null | head -1)"
		else
			# An app that quits without the renderer's exit line (e.g. Application.Quit in Kitchen) is done once its process is gone.
			st="$(sf_ssh_out "grep -q 'event loop exited' /tmp/sf_run.log && echo DONE || { pgrep -f '$PKG_PATTERN' >/dev/null && echo RUNNING || echo DONE; }" 2>/dev/null | tail -1)"
		fi
		[ "$st" = DONE ] && break
		[ $((waited % 30)) -eq 0 ] && sf_keep_awake
		sleep 3; waited=$((waited + 3))
	done
	sf_ssh_out "grep -E 'SHOWCASE (recorded|done)|CRASH' /tmp/sf_run.log | tail -3" 2>/dev/null | sed 's/^/    /' || true
}

# ============================================================ compositor ======
if [ "$MODE" = hw ] || [ "$MODE" = manual ]; then
	sf_info "[1/4] recorder (tools/screenrec → $SF_REC_BIN)"
	sf_rec_prepare
	if [ "$MODE" = manual ]; then
		sf_info "[2/4] recording the screen for ${SECONDS_MANUAL}s - use the phone now"
		sf_rec_start "$SECONDS_MANUAL" "$AUDIO"
		sleep "$SECONDS_MANUAL"
	else
		ENVS+=(--env MAUI_SAILFISH_QT_HOST_SHOWCASE_SYNC=1)
		sf_info "[2/4] running $SF_PKG (${ENVS[*]}) + recording the screen"
		sf_ssh "rm -rf /tmp/sf-record-frames" >/dev/null 2>&1 || true
		# Recorder first: sf run returns only ~10 s after the launch, and an app without the
		# showcase's recorder sync would already be past its first screens by then.
		sf_rec_start "$((MAX + 30))" "$AUDIO"
		"$SCRIPT_DIR/run.sh" "${ENVS[@]}" >"$WORK/run.log" 2>&1 || true
		wait_for_app
	fi
	sf_info "[3/4] stopping the recorder, pulling the video"
	sf_rec_stop
	if [ -n "$KEEP" ] && [ "$MODE" != manual ]; then
		# the app log first: pulling the video clears the recorder's files, not the app's
		mkdir -p "$KEEP"
		sf_ssh "cat /tmp/sf_run.log" > "$KEEP/app.log" 2>/dev/null || true
	fi
	sf_rec_pull "$WORK"
	if [ -n "$KEEP" ]; then
		mkdir -p "$KEEP"
		rm -rf "$KEEP/frames"
		cp "$WORK/screen.mp4" "$WORK/screen.mp4.frames.tsv" "$KEEP/" 2>/dev/null || true
		sf_note "kept the recording and the app log in $KEEP"
	fi
	sf_info "[4/4] encoding → $OUT"
	sf_rec_encode "$WORK/screen.mp4" "$OUT" "$WIDTH" "$AUDIO"
	exit 0
fi

# ============================================================ app (--app) =====
REMOTE_DIR=/tmp/sf-record-frames   # written by the app (its own user)
ENVS+=(--env "MAUI_SAILFISH_QT_HOST_SHOWCASE_RECORD=$REMOTE_DIR")
sf_keep_awake

sf_info "[1/4] running $SF_PKG (${ENVS[*]})"
sf_ssh "rm -rf $REMOTE_DIR" >/dev/null 2>&1 || true
"$SCRIPT_DIR/run.sh" "${ENVS[@]}" >"$WORK/run.log" 2>&1 || true

sf_info "[2/4] the app records its own frames (until the tour ends or ${MAX}s)"
wait_for_app

sf_info "[3/4] pulling the frames"
sf_ssh "cd /tmp && rm -rf sfrec.tar sfrec && mv sf-record-frames sfrec && tar cf /tmp/sfrec.tar sfrec" >/dev/null
sf_scp_from /tmp/sfrec.tar "$WORK/frames.tar" >/dev/null
sf_ssh "rm -rf /tmp/sfrec /tmp/sfrec.tar" >/dev/null 2>&1 || true
tar xf "$WORK/frames.tar" -C "$WORK"
if [ "$AMBIENCE" = 1 ]; then
	BG="$(sf_ssh_out "dconf read /desktop/jolla/background/portrait/app_picture_filename 2>/dev/null" 2>/dev/null | tr -d "'\r" | tail -1)"
	if [ -n "$BG" ] && sf_scp_from "$BG" "$WORK/ambience.jpg" >/dev/null 2>&1; then
		sf_note "ambience background: $BG"
	else
		AMBIENCE=0
		sf_note "no ambience background found - black"
	fi
fi
COUNT="$(ls "$WORK/sfrec" 2>/dev/null | wc -l | tr -d ' ')"
[ "$COUNT" -gt 1 ] || sf_die "no frames captured (app log: /tmp/sf_run.log on the device)"

if [ -n "$KEEP" ]; then
	mkdir -p "$KEEP"
	rm -rf "$KEEP/frames"
	cp -R "$WORK/sfrec" "$KEEP/frames"
	sf_ssh "cat /tmp/sf_run.log" > "$KEEP/app.log" 2>/dev/null || true
	sf_note "kept the frames and the app log in $KEEP"
fi

sf_info "[4/4] encoding $COUNT frames → $OUT"
( cd "$WORK/sfrec" && ls *.png | sort -n ) | awk -v dir="$WORK/sfrec" '
	{ t[NR] = $1; sub(/\.[a-z]+$/, "", t[NR]); f[NR] = $1 }
	END {
		for (i = 1; i <= NR; i++) {
			d = (i < NR) ? (t[i+1] - t[i]) / 1000 : 1.0
			if (d <= 0) d = 0.01
			printf "file '\''%s/%s'\''\nduration %.3f\n", dir, f[i], d
		}
		printf "file '\''%s/%s'\''\n", dir, f[NR]
	}' > "$WORK/list.txt"
SPAN="$(awk '/^duration/ { s += $2 } END { printf "%.1f", s }' "$WORK/list.txt")"
mkdir -p "$(dirname "$OUT")"
POST="scale=${WIDTH}:-2:flags=lanczos,setsar=1,fps=30"
if [ "$AMBIENCE" = 1 ]; then
	ffmpeg -hide_banner -loglevel error -y -f concat -safe 0 -i "$WORK/list.txt" -loop 1 -i "$WORK/ambience.jpg" \
		-filter_complex "[1:v][0:v]scale2ref=flags=lanczos[bg][fg];[bg][fg]overlay=format=auto:shortest=1,${POST}" \
		-c:v libx264 -preset slow -crf 22 -pix_fmt yuv420p -movflags +faststart "$OUT"
else
	ffmpeg -hide_banner -loglevel error -y -f concat -safe 0 -i "$WORK/list.txt" -vf "$POST" \
		-c:v libx264 -preset slow -crf 22 -pix_fmt yuv420p -movflags +faststart "$OUT"
fi
sf_ok "$OUT ($(du -h "$OUT" | cut -f1), ${SPAN}s, $COUNT frames)"
