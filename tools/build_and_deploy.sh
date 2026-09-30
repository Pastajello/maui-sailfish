#!/bin/bash
# One-shot cycle: sf-deploy.sh --run (build, upload, install, verify, launch), then a
# compositor screenshot into tools/screenshots/ to confirm the result.
# Usage: ./tools/build_and_deploy.sh [sf-deploy.sh args...]
#
#   ./tools/build_and_deploy.sh              # incremental rebuild + deploy + run
#   ./tools/build_and_deploy.sh --clean      # full rebuild (wipe publish output)
#   ./tools/build_and_deploy.sh --release 42 # fixed RPM release number
#   ./tools/build_and_deploy.sh --no-verify  # skip payload verification

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=sf-lib.sh
. "$SCRIPT_DIR/sf-lib.sh"

sf_info "=== build_and_deploy: rebuild -> upload -> install -> verify -> run ==="

"$SCRIPT_DIR/sf-deploy.sh" --run "$@"

# Give lipstick a moment to finish the raise before capturing the screen.
sleep 3

SHOT="$SCRIPT_DIR/screenshots/build-and-deploy-$(date +%Y%m%d-%H%M%S).png"
sf_info "capturing confirmation screenshot"
if "$SCRIPT_DIR/sf-screenshot.sh" "$SHOT"; then
	sf_ok "cycle complete - screenshot: $SHOT"
else
	sf_error "app was deployed and started, but the screenshot failed (see above)"
	exit 1
fi
