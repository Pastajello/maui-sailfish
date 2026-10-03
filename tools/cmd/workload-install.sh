#!/bin/bash
# Installs the Sailfish workload manifest into the local SDK so net11.0-sailfish is
# recognized before restore (the SDK's TFM check runs before any NuGet import).
# `dotnet workload install sailfish` works only once this manifest is there (no
# workload set is shipped); copying into sdk-manifests/<band>/ is what the installer does.
# Remove that folder to uninstall. Without a checkout the sailfish-workload tool does
# the same: dnx Microsoft.Maui.SailfishOS.Workload install (tools/sf pack-local packs it).
#
# Usage: ./tools/sf workload-install

set -euo pipefail
case "${1:-}" in -h|--help) awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"; exit 0 ;; esac
[ $# -eq 0 ] || { echo "ERROR: unexpected argument: $1 (see --help)" >&2; exit 2; }

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

# The feature band: the patch rounded down to its hundred plus the first two prerelease labels
# (11.0.105 -> 11.0.100, 11.0.100-rc.1.26425.128 -> 11.0.100-rc.1).
BAND="$(dotnet --version | sed -E 's/^([0-9]+\.[0-9]+)\.([0-9]*)[0-9]{2}(-[0-9A-Za-z]+\.[0-9A-Za-z]+)?.*/\1.\200\3/')"
SDK_ROOT="$(dotnet --list-sdks | grep -F "$(dotnet --version)" | sed -E 's/.*\[(.*)\].*/\1/' | head -1)"
[ -n "$SDK_ROOT" ] || { echo "ERROR: cannot locate the active SDK root"; exit 1; }
# --list-sdks brackets the sdk/ dir itself; sdk-manifests/ is its sibling.
DEST="$(dirname "$SDK_ROOT")/sdk-manifests/$BAND/microsoft.maui.sailfishos"

PROJECT="$REPO_ROOT/src/Linux.SailfishOS.WorkloadManifest/Linux.SailfishOS.WorkloadManifest.csproj"
if [ -f "$PROJECT" ]; then
	FEED="${SF_WORKLOAD_FEED:-/tmp/sf-workload-feed}"
	MANIFEST_NUPKG="$FEED/microsoft.maui.sailfishos.Manifest-$BAND."*.nupkg
	# Repacked every run: a stale nupkg in the feed would install an old manifest.
	echo "==> packing the workload manifest into $FEED"
	mkdir -p "$FEED"
	rm -f $MANIFEST_NUPKG
	dotnet pack "$PROJECT" -c Release -o "$FEED" >/dev/null
else
	# Without the checkout: the manifest tools/sf pack-local put into the local feed.
	FEED="${SF_WORKLOAD_FEED:-${SF_LOCAL_FEED:-$HOME/.local/share/maui-sailfish/feed}}"
	MANIFEST_NUPKG="$FEED/microsoft.maui.sailfishos.Manifest-$BAND."*.nupkg
	ls $MANIFEST_NUPKG >/dev/null 2>&1 ||
		{ echo "ERROR: no microsoft.maui.sailfishos.Manifest-$BAND nupkg in $FEED (run tools/sf pack-local, or set SF_WORKLOAD_FEED)"; exit 1; }
	echo "==> using the workload manifest from $FEED"
fi

echo "==> installing manifest for band $BAND into $DEST"
mkdir -p "$DEST"
TMP="$(mktemp -d)"
unzip -q -o "$(ls $MANIFEST_NUPKG | head -1)" 'data/*' -d "$TMP"
cp "$TMP/data/WorkloadManifest.json" "$TMP/data/WorkloadManifest.targets" "$DEST/"
rm -rf "$TMP"

echo "    OK   sdk-manifests/$BAND/microsoft.maui.sailfishos installed"
echo "    (remove that directory to uninstall; apps then need the plain net11.0 TFM)"
