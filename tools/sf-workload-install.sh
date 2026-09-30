#!/bin/bash
# Installs the Sailfish workload manifest into the local SDK so net11.0-sailfish is
# recognized before restore (the SDK's TFM check runs before any NuGet import).
# `dotnet workload install` would also need the aggregate workloads package, which
# is not shipped yet; copying into sdk-manifests/<band>/ is what the installer does.
# Remove that folder to uninstall.
#
# Usage: ./tools/sf-workload-install.sh

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

BAND="$(dotnet --version | sed -E 's/^([0-9]+\.[0-9]+\.[0-9]+)(-[a-z]+\.[0-9]+)?.*/\1\2/')"
SDK_ROOT="$(dotnet --list-sdks | grep -F "$(dotnet --version)" | sed -E 's/.*\[(.*)\].*/\1/' | head -1)"
[ -n "$SDK_ROOT" ] || { echo "ERROR: cannot locate the active SDK root"; exit 1; }
# --list-sdks brackets the sdk/ dir itself; sdk-manifests/ is its sibling.
DEST="$(dirname "$SDK_ROOT")/sdk-manifests/$BAND/microsoft.maui.sailfishos"

FEED="${SF_WORKLOAD_FEED:-/tmp/sf-workload-feed}"
MANIFEST_NUPKG="$FEED/microsoft.maui.sailfishos.Manifest-$BAND."*.nupkg

# Repacked every run: a stale nupkg in the feed would install an old manifest.
echo "==> packing the workload manifest into $FEED"
mkdir -p "$FEED"
rm -f $MANIFEST_NUPKG
dotnet pack "$REPO_ROOT/src/Linux.SailfishOS.WorkloadManifest/Linux.SailfishOS.WorkloadManifest.csproj" \
        -c Release -o "$FEED" >/dev/null

echo "==> installing manifest for band $BAND into $DEST"
mkdir -p "$DEST"
TMP="$(mktemp -d)"
unzip -q -o "$(ls $MANIFEST_NUPKG | head -1)" 'data/*' -d "$TMP"
cp "$TMP/data/WorkloadManifest.json" "$TMP/data/WorkloadManifest.targets" "$DEST/"
rm -rf "$TMP"

echo "    OK   sdk-manifests/$BAND/microsoft.maui.sailfishos installed"
echo "    (remove that directory to uninstall; apps then need the plain net11.0 TFM)"
