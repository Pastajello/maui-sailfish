# Screen recording on the phone: the compositor recorder (tools/screenrec) driven over ssh.
# Sourced after sf-lib.sh by cmd/record.sh and cmd/rec.sh:  . "$SCRIPT_DIR/../lib/sf-screenrec.sh"
#
# One recording at a time: the recorder writes /tmp/sfrec.mp4 (+ .frames.tsv, per-frame compositor
# timestamps), marks /tmp/sfrec.recording once frames flow, stops when /tmp/sfrec.stop appears or
# after --max-seconds, and touches /tmp/sfrec.done when the file is complete. It needs root (the
# encoder node, lipstick's privileged recorder interface), hence the developer-mode password.

SF_REC_BIN=/tmp/sf-screenrec
SF_REC_FILES="/tmp/sfrec.mp4 /tmp/sfrec.mp4.frames.tsv /tmp/sfrec.log /tmp/sfrec.recording /tmp/sfrec.stop /tmp/sfrec.done"

# sf_rec_state - RECORDING, FINISHED (a file waits to be pulled) or IDLE.
sf_rec_state() {
	sf_ssh_out "if [ -f /tmp/sfrec.done ]; then echo FINISHED; elif [ -f /tmp/sfrec.recording ]; then echo RECORDING; else echo IDLE; fi" 2>/dev/null | tail -1
}

# sf_rec_prepare - build the recorder when its source changed, upload it, clear the previous run.
sf_rec_prepare() {
	sf_have_root_pw || sf_die "recording the screen needs the developer-mode password (devel-su): $SF_SETUP_HINT"
	"$_SF_TOOLS_DIR/cmd/screenrec-build.sh" | sed 's/^/    /'
	sf_scp_to "$SF_REPO_ROOT/tools/.cache/sf-screenrec" "$SF_REC_BIN.new" >/dev/null || sf_die "could not upload the recorder"
	sf_root "rm -rf $SF_REC_FILES; mv -f $SF_REC_BIN.new $SF_REC_BIN; chmod 755 $SF_REC_BIN"
}

# sf_rec_start <max-seconds> <audio 0|1> - start the recorder detached; returns once frames flow.
sf_rec_start() {
	local args="-o /tmp/sfrec.mp4 --ready-file /tmp/sfrec.recording --stop-file /tmp/sfrec.stop --max-seconds $1"
	[ "$2" = 1 ] && args="$args --audio"
	sf_keep_awake
	sf_root "(setsid sh -c '$SF_REC_BIN $args >/tmp/sfrec.log 2>&1; touch /tmp/sfrec.done' </dev/null >/dev/null 2>&1 &)"
	local st
	for _ in $(seq 1 20); do
		st="$(sf_rec_state)"
		[ "$st" = RECORDING ] && return 0
		[ "$st" = FINISHED ] && break
		sleep 0.5
	done
	sf_ssh_out "cat /tmp/sfrec.log" 2>/dev/null | grep -v libandroidicu | sed 's/^/    /' || true
	sf_die "the recorder did not start"
}

# sf_rec_stop - ask the recorder to finish and wait until its file is complete; prints its summary.
sf_rec_stop() {
	sf_ssh "touch /tmp/sfrec.stop" >/dev/null 2>&1 || true
	for _ in $(seq 1 40); do
		[ "$(sf_rec_state)" = FINISHED ] && break
		sleep 0.5
	done
	sf_ssh_out "cat /tmp/sfrec.log" 2>/dev/null \
		| grep -E "^sf-screenrec: (compositor|encoder|hardware|software|[0-9]+ frames)|ERROR|refused" | sed 's/^/    /' || true
}

# sf_rec_pull <dir> - download the raw recording to <dir>/screen.mp4 (+ .frames.tsv), then clean up.
sf_rec_pull() {
	mkdir -p "$1"
	sf_scp_from /tmp/sfrec.mp4 "$1/screen.mp4" >/dev/null || sf_die "no video (recorder log: /tmp/sfrec.log on the phone)"
	sf_scp_from /tmp/sfrec.mp4.frames.tsv "$1/screen.mp4.frames.tsv" >/dev/null 2>&1 || true
	sf_ssh "rm -f $SF_REC_FILES" >/dev/null 2>&1 || true
}

# sf_rec_encode <raw.mp4> <out.mp4> <width> <audio 0|1> - upright, resampled to 60 fps, H.264.
# ffmpeg applies the recorder's "rotate 90°" display matrix, so the output is upright.
sf_rec_encode() {
	command -v ffmpeg >/dev/null || sf_die "ffmpeg not found (macOS: brew install ffmpeg)"
	local audio=(-an)
	[ "$4" = 1 ] && audio=(-c:a aac -b:a 128k)
	mkdir -p "$(dirname "$2")"
	ffmpeg -hide_banner -loglevel error -y -i "$1" -vf "scale=${3}:-2:flags=lanczos,setsar=1,fps=60" \
		-c:v libx264 -preset slow -crf 20 -pix_fmt yuv420p "${audio[@]}" -movflags +faststart "$2"
	local frames span
	frames="$(awk 'NR > 1 && $5 == 1' "$1.frames.tsv" 2>/dev/null | wc -l | tr -d ' ')"
	span="$(ffprobe -v error -show_entries format=duration -of csv=p=0 "$2" 2>/dev/null | cut -d. -f1)"
	sf_ok "$2 ($(du -h "$2" | cut -f1 | tr -d ' '), ${span:-?}s, $frames frames from the compositor)"
}

# sf_rec_gif <in.mp4> <out.gif> - README-sized GIF (360 px, 12 fps; gifsicle -O3 when installed).
sf_rec_gif() {
	ffmpeg -hide_banner -loglevel error -y -i "$1" \
		-vf "fps=12,scale=360:-2:flags=lanczos,split[a][b];[a]palettegen=max_colors=192:stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle" \
		"$2" || sf_die "GIF conversion failed"
	if command -v gifsicle >/dev/null 2>&1; then
		gifsicle -O3 --lossy=30 "$2" -o "$2.tmp" && mv -f "$2.tmp" "$2"
	fi
	sf_ok "$2 ($(du -h "$2" | cut -f1 | tr -d ' '))"
}
