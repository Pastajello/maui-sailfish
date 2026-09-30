#!/bin/bash
# Profiles the installed app from its start with an EventPipe file session (nothing to install on the
# phone), waits until the app exits, then fetches the trace and the device log. See docs/profiling.md.
# Usage: ./tools/sf trace OUT_DIR [--timeout S] [--config EVENTPIPE_CONFIG] [sf run args...]
#
#   OUT_DIR    gets trace.nettrace, trace.speedscope.json, device.log, sf-run.out and analysis.txt
#   --timeout  seconds to wait for the app to exit on its own (default 180); then sf-kill (SIGTERM)
#   --config   DOTNET_EventPipeConfig (default: sampled thread time + the dotnet-common runtime events)
#   the rest goes to sf run, e.g. --env KITCHEN_TOUR=beef --env KITCHEN_OFFLINE=1
#
# The app should end itself (a scripted tour calling Application.Quit): a clean exit writes the rundown
# that names the methods. --diagnostics is always on (DOTNET_EnableDiagnostics=0 would also disable
# EventPipe). SF_SAMPLE_DIR picks the app as for sf run. Local tools: dotnet-trace (convert) and
# dotnet (tools/analyze/sf-trace-analyze.cs); either is skipped when missing.

set -uo pipefail
case "${1:-}" in -h|--help) awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"; exit 0 ;; esac

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=../lib/sf-lib.sh
. "$SCRIPT_DIR/../lib/sf-lib.sh"

[ $# -ge 1 ] || sf_die "usage: $0 OUT_DIR [--timeout S] [--config EVENTPIPE_CONFIG] [sf run args...]"
OUT="$1"
case "$OUT" in -*) sf_die "usage: $0 OUT_DIR [--timeout S] [--config EVENTPIPE_CONFIG] [sf run args...]" ;; esac
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

sf_info "Launching $SF_PKG with an EventPipe file session, waiting up to ${TIMEOUT}s for it to exit"
rc=0
"$SCRIPT_DIR/run.sh" --diagnostics --wait "$TIMEOUT" \
	--env DOTNET_EnableEventPipe=1 \
	--env DOTNET_EventPipeOutputPath=$REMOTE_TRACE \
	--env DOTNET_EventPipeOutputStreaming=1 \
	--env "DOTNET_EventPipeConfig=$CONFIG" \
	${RUN_ARGS[@]+"${RUN_ARGS[@]}"} > "$OUT/sf-run.out" 2>&1 || rc=$?
grep -q '^LAUNCHED_PID=' "$OUT/sf-run.out" || sf_die "the app did not launch (see $OUT/sf-run.out)"
if [ "$rc" = 124 ]; then
	sf_note "still running after ${TIMEOUT}s - stopping it (SIGTERM first, so the rundown gets written)"
	sf_kill_app >/dev/null
fi

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
	dotnet run "$SCRIPT_DIR/../analyze/sf-trace-analyze.cs" -- "$OUT/trace.nettrace" > "$OUT/analysis.txt" 2>&1 \
		&& sf_ok "analysis: $OUT/analysis.txt (more views: tools/analyze/sf-trace-analyze.cs header)"
fi
