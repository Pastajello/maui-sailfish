#!/bin/bash
# Profiles the installed app from its start with an EventPipe file session (nothing to install on the
# phone), waits until the app exits, then fetches the trace and the device log. See docs/profiling.md.
# Usage: ./tools/sf-trace.sh OUT_DIR [--timeout S] [--config EVENTPIPE_CONFIG] [sf-run.sh args...]
#
#   OUT_DIR    gets trace.nettrace, trace.speedscope.json, device.log, sf-run.out and analysis.txt
#   --timeout  seconds to wait for the app to exit on its own (default 180); then sf-kill (SIGTERM)
#   --config   DOTNET_EventPipeConfig (default: sampled thread time + the dotnet-common runtime events)
#   the rest goes to sf-run.sh, e.g. --env KITCHEN_TOUR=beef --env KITCHEN_OFFLINE=1
#
# The app should end itself (a scripted tour calling Application.Quit): a clean exit writes the rundown
# that names the methods. --diagnostics is always on (DOTNET_EnableDiagnostics=0 would also disable
# EventPipe). SF_SAMPLE_DIR picks the app as for sf-run.sh. Local tools: dotnet-trace (convert) and
# dotnet (tools/sf-trace-analyze.cs); either is skipped when missing.

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=sf-lib.sh
. "$SCRIPT_DIR/sf-lib.sh"

[ $# -ge 1 ] || sf_die "usage: $0 OUT_DIR [--timeout S] [--config EVENTPIPE_CONFIG] [sf-run.sh args...]"
OUT="$1"
shift
TIMEOUT=180
CONFIG="Microsoft-DotNETCore-SampleProfiler:0:5,Microsoft-Windows-DotNETRuntime:0x100003801D:4"
RUN_ARGS=()
while [ $# -gt 0 ]; do
	case "$1" in
		--timeout) TIMEOUT="$2"; shift 2 ;;
		--config)  CONFIG="$2"; shift 2 ;;
		*)         RUN_ARGS+=("$1"); shift ;;
	esac
done
REMOTE_TRACE=/tmp/sf-trace.nettrace
mkdir -p "$OUT"

sf_device_ready || sf_note "display off or locked: the app may start behind the lock screen"
# A stale device log from the previous run must not look like this run's output.
sf_ssh_out "rm -f $REMOTE_TRACE /tmp/sf_run.log" >/dev/null

sf_info "Launching $SF_PKG with an EventPipe file session"
"$SCRIPT_DIR/sf-run.sh" --diagnostics \
	--env DOTNET_EnableEventPipe=1 \
	--env DOTNET_EventPipeOutputPath=$REMOTE_TRACE \
	--env DOTNET_EventPipeOutputStreaming=1 \
	--env "DOTNET_EventPipeConfig=$CONFIG" \
	"${RUN_ARGS[@]}" > "$OUT/sf-run.out" 2>&1 &
RUN_PID=$!
# sf-run.sh returns ~10 s after launch but can hang on ssh after the app exits.
( sleep 90; kill "$RUN_PID" 2>/dev/null ) &
WATCHDOG=$!
disown "$WATCHDOG"   # no "Terminated" job notice when it is stopped below

# sf-run.sh spends ~10 s on helpers before the launch; "no instance" before that means "not started yet".
for _ in $(seq 1 60); do
	grep -q '^LAUNCHED_PID=' "$OUT/sf-run.out" 2>/dev/null && break
	sleep 1
done
grep -q '^LAUNCHED_PID=' "$OUT/sf-run.out" || sf_die "the app did not launch (see $OUT/sf-run.out)"

sf_info "Waiting up to ${TIMEOUT}s for the app to exit"
waited=0
while [ "$waited" -lt "$TIMEOUT" ]; do
	sf_keep_awake
	running="$(sf_ssh_out "/tmp/sf-kill-remote.sh $SF_PKG $SF_BIN --list 2>/dev/null | grep -c RUNNING || true")"
	[ "${running//[^0-9]/}" = "0" ] && break
	sleep 3
	waited=$((waited + 3))
done
if [ "$waited" -ge "$TIMEOUT" ]; then
	sf_note "still running after ${TIMEOUT}s - stopping it (SIGTERM first, so the rundown gets written)"
	sf_kill_app >/dev/null
fi
kill "$WATCHDOG" 2>/dev/null
wait "$RUN_PID" 2>/dev/null

sf_scp_from /tmp/sf_run.log "$OUT/device.log" >/dev/null || sf_note "no device log"
sf_scp_from "$REMOTE_TRACE" "$OUT/trace.nettrace" >/dev/null || sf_die "no trace at $REMOTE_TRACE (see $OUT/sf-run.out)"
sf_ok "trace: $OUT/trace.nettrace ($(wc -c < "$OUT/trace.nettrace" | tr -d ' ') bytes)"

TRACE_TOOL="$(command -v dotnet-trace || echo "$HOME/.dotnet/tools/dotnet-trace")"
if [ -x "$TRACE_TOOL" ]; then
	"$TRACE_TOOL" convert "$OUT/trace.nettrace" --format Speedscope -o "$OUT/trace" >/dev/null \
		&& sf_ok "speedscope: $OUT/trace.speedscope.json (open in https://www.speedscope.app)"
else
	sf_note "dotnet-trace not found (dotnet tool install -g dotnet-trace): no speedscope file"
fi
if command -v dotnet >/dev/null 2>&1; then
	dotnet run "$SCRIPT_DIR/sf-trace-analyze.cs" -- "$OUT/trace.nettrace" > "$OUT/analysis.txt" 2>&1 \
		&& sf_ok "analysis: $OUT/analysis.txt (more views: tools/sf-trace-analyze.cs header)"
fi
