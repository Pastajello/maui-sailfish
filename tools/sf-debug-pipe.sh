#!/bin/bash
# pipeTransport shim for VS Code's `coreclr` remote attach:
#   sf-debug-pipe.sh <debuggerPath> --interpreter=vscode   (DAP on stdin/stdout)
#
# A shim keeps the device address (connect.info) out of launch.json and reuses the
# pinned known_hosts. The stream goes through sf-debug-dap-filter.py, which strips the
# SHA384/SHA512 breakpoint checksums vsdbg rejects (else every breakpoint stays unbound).
# Requires a paired key (tools/sf-pair.sh).

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=lib/sf-lib.sh
. "$SCRIPT_DIR/lib/sf-lib.sh"

[ $# -gt 0 ] || sf_die "usage: sf-debug-pipe.sh <remote-vsdbg-path> [args...]"
command -v python3 >/dev/null 2>&1 ||
	sf_die "python3 is required on the build host: it runs tools/sf-debug-dap-filter.py, the DAP stream relay"

# The filter owns the ssh child; SF_SSH_OPTS is a flat list of -o options.
SF_SSH_OPTS="$SF_SSH_OPTS" SF_SSH_TARGET="$SF_USER@$SF_HOST" \
	exec python3 "$SCRIPT_DIR/py/sf-debug-dap-filter.py" "$@"
