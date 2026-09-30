#!/bin/bash
# Kills every running instance of the app on the device (see sf-kill-remote.sh).
# Usage: ./tools/sf-kill.sh [--list]

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=sf-lib.sh
. "$SCRIPT_DIR/sf-lib.sh"

MODE="${1:-kill}"

sf_kill_app "$MODE"
