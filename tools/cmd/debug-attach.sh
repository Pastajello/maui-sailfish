#!/bin/bash
# Prepares managed debugging for VS Code's "F5 DEBUG" / "DEBUGGER attach" configs:
# pushes the linux-arm64 vsdbg to /tmp/vsdbg (never in the RPM), checks it runs,
# relaxes yama ptrace_scope when needed and prints DEVICE_PID of the running app.
# Debug only through VS Code: vsdbg's license handshake rejects hand-rolled DAP
# clients (every managed request fails with 0x89720009).
#
# Usage: ./tools/sf debug-attach [--push-only | --if-needed]
#   --push-only   push + verify vsdbg, do not look for a running app
#   --if-needed   same, but only when /tmp/vsdbg or ptrace_scope=0 is gone (reboot)
#
# VSDBG_DIR overrides the local vsdbg directory (default ~/.vsdbg-linux-arm64):
#   curl -sSL https://aka.ms/getvsdbgsh | bash -s -- -v latest -r linux-arm64 -l ~/.vsdbg-linux-arm64

set -euo pipefail
case "${1:-}" in -h|--help) awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"; exit 0 ;; esac

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=../lib/sf-lib.sh
. "$SCRIPT_DIR/../lib/sf-lib.sh"

case "${1:-}" in ''|--push-only|--if-needed) ;; *) sf_die "unknown argument: $1 (see --help)" ;; esac
[ $# -le 1 ] || sf_die "unexpected argument: $2 (see --help)"
PUSH_ONLY=0
[ "${1:-}" = "--push-only" ] && PUSH_ONLY=1
# A reboot wipes /tmp and resets ptrace_scope, so --if-needed re-checks both.
if [ "${1:-}" = "--if-needed" ]; then
	PUSH_ONLY=1
	if [ "$(sf_ssh_out "[ -x /tmp/vsdbg/vsdbg ] && echo HAVE; cat /proc/sys/kernel/yama/ptrace_scope 2>/dev/null || echo 0" 2>/dev/null | tr '\n' ' ' | tr -s ' ')" = "HAVE 0 " ]; then
		sf_ok "vsdbg present on the device, ptrace_scope=0 - nothing to do"
		exit 0
	fi
	sf_note "vsdbg missing on the device (rebooted?) or ptrace_scope reset - setting the debugger up again"
fi

VSDBG_DIR="${VSDBG_DIR:-$HOME/.vsdbg-linux-arm64}"
[ -x "$VSDBG_DIR/vsdbg" ] || sf_die "no vsdbg at $VSDBG_DIR/vsdbg - install it with: curl -sSL https://aka.ms/getvsdbgsh | bash -s -- -v latest -r linux-arm64 -l $VSDBG_DIR"

sf_ping

sf_info "[1/3] pushing vsdbg to /tmp/vsdbg on $SF_USER@$SF_HOST"
# vsdbg dlopens libvsdbg.so and locale dirs from its own directory: push the whole tree.
tar -C "$VSDBG_DIR" -cf /tmp/vsdbg-push.tar .
sf_ssh "mkdir -p /tmp/vsdbg" >/dev/null
sf_scp_to /tmp/vsdbg-push.tar /tmp/vsdbg-push.tar
sf_ssh "tar -C /tmp/vsdbg -xf /tmp/vsdbg-push.tar && chmod +x /tmp/vsdbg/vsdbg && rm -f /tmp/vsdbg-push.tar"
rm -f /tmp/vsdbg-push.tar

sf_info "[2/3] verifying vsdbg runs on the device"
# This vsdbg speaks only DAP (--interpreter=vscode); starting it with stdin closed
# proves the binary loads on the device.
sf_ssh_out "printf '' | /tmp/vsdbg/vsdbg --interpreter=vscode 2>&1 | head -2; echo VSDBG_EXIT=\$?" | sed 's/^/    /'

sf_info "[3/3] ptrace scope"
scope="$(sf_ssh_out "cat /proc/sys/kernel/yama/ptrace_scope 2>/dev/null || echo missing" | tail -1)"
case "$scope" in
	0) sf_ok "yama ptrace_scope=0 (attach allowed)" ;;
	missing) sf_note "no yama ptrace_scope on this kernel - attach unrestricted" ;;
	*)
		sf_note "yama ptrace_scope=$scope - relaxing to 0 through devel-su (attach to a non-child process)"
		sf_root "echo 0 > /proc/sys/kernel/yama/ptrace_scope"
		;;
esac

if [ "$PUSH_ONLY" = 1 ]; then
	sf_ok "push-only done"
	exit 0
fi

PID="$(sf_ssh_out "pgrep -f '/usr/bin/$SF_PKG' | head -1" | tail -1)"
if [ -z "$PID" ]; then
	sf_die "no running $SF_PKG process - deploy+run the Debug build first (SF_CONFIGURATION=Debug SF_SAMPLE_DIR=<app> ./tools/sf deploy --run)"
fi
echo
echo "DEVICE_PID=$PID"
sf_note "Attach from VS Code: 'Sailfish: Debug attach (pick remote process)', or"
sf_note "'Sailfish: Debug attach (pid...)' with DEVICE_PID=$PID."
sf_note "Do NOT test vsdbg with a hand-rolled DAP client - vsdbg's license handshake"
sf_note "refuses it and reports 0x89720009 on every managed request (see the header)."
