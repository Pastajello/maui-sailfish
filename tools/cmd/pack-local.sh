#!/bin/bash
# Packs this checkout into the local NuGet feed: Microsoft.Maui.SailfishOS (+ .SkiaSharp), the workload
# manifest (net11.0-sailfish), the sailfish-workload tool that installs it and the
# maui-sailfish template, which SailfishKitchen and template projects restore from.
#
# Usage: tools/sf pack-local [--no-template]
#   feed: $SF_LOCAL_FEED (default ~/.local/share/maui-sailfish/feed), registered
#   as the user-level NuGet source "maui-sailfish-local" when missing.
#
# The version is fixed, so the cached package is dropped and build servers stopped
# to make the next restore take the fresh one.
set -euo pipefail
case "${1:-}" in -h|--help) awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"; exit 0 ;; esac
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
FEED="${SF_LOCAL_FEED:-$HOME/.local/share/maui-sailfish/feed}"
TEMPLATE=1
case "${1:-}" in
	'') ;;
	--no-template) TEMPLATE=0 ;;
	*) echo "ERROR: unknown argument: $1 (see --help)" >&2; exit 2 ;;
esac
[ $# -le 1 ] || { echo "ERROR: unexpected argument: $2 (see --help)" >&2; exit 2; }

VERSION="$(sed -n 's:.*<PlatformMauiSailfishVersion[^>]*>\(.*\)</PlatformMauiSailfishVersion>.*:\1:p' "$REPO_ROOT/Directory.Build.props" | head -1)"
[ -n "$VERSION" ] || { echo "ERROR: PlatformMauiSailfishVersion not found in Directory.Build.props"; exit 1; }
[ -f "$REPO_ROOT/artifacts/native/aarch64/libsailfishhost.so" ] || "$SCRIPT_DIR/native-build.sh"

mkdir -p "$FEED"
echo "==> Microsoft.Maui.SailfishOS $VERSION → $FEED"
dotnet pack "$REPO_ROOT/src/Linux.SailfishOS/Linux.SailfishOS.csproj" -c Release -o "$FEED" --nologo -v quiet
echo "==> Microsoft.Maui.SailfishOS.SkiaSharp $VERSION → $FEED"
dotnet pack "$REPO_ROOT/src/Linux.SailfishOS.SkiaSharp/Linux.SailfishOS.SkiaSharp.csproj" -c Release -o "$FEED" --nologo -v quiet
# The workload manifest too, so `tools/sf workload-install` (and a machine without this checkout, from the feed)
# can teach the SDK the net11.0-sailfish TFM.
echo "==> workload manifest → $FEED"
rm -f "$FEED"/microsoft.maui.sailfishos.Manifest-*."$VERSION".nupkg
dotnet pack "$REPO_ROOT/src/Linux.SailfishOS.WorkloadManifest/Linux.SailfishOS.WorkloadManifest.csproj" -c Release -o "$FEED" --nologo -v quiet
# and the sailfish-workload tool that installs it without a checkout (dnx Microsoft.Maui.SailfishOS.Workload install).
echo "==> sailfish-workload tool → $FEED"
dotnet pack "$REPO_ROOT/src/Linux.SailfishOS.Workload/Linux.SailfishOS.Workload.csproj" -c Release -o "$FEED" --nologo -v quiet
rm -rf "$HOME/.nuget/packages/microsoft.maui.sailfishos.workload/$VERSION"
if [ "$TEMPLATE" = 1 ]; then
	echo "==> template package → $FEED"
	dotnet pack "$REPO_ROOT/templates/sailfishos/SailfishOS.Templates.csproj" -c Release -o "$FEED" --nologo -v quiet
fi

if ! dotnet nuget list source 2>/dev/null | grep -q "maui-sailfish-local"; then
	echo "==> registering the NuGet source maui-sailfish-local"
	dotnet nuget add source "$FEED" --name maui-sailfish-local >/dev/null
fi

rm -rf "$HOME/.nuget/packages/microsoft.maui.sailfishos/$VERSION" "$HOME/.nuget/packages/microsoft.maui.sailfishos.skiasharp/$VERSION"
dotnet build-server shutdown >/dev/null 2>&1 || true

if [ "$TEMPLATE" = 1 ]; then
	echo "==> installing the maui-sailfish template"
	# install --force of the same nupkg adds another registration each time (dotnet new then fails with
	# "Sequence contains more than one matching element"), so uninstall first.
	dotnet new uninstall Microsoft.Maui.Platforms.SailfishOS.Templates >/dev/null 2>&1 || true
	dotnet new install "$FEED/Microsoft.Maui.Platforms.SailfishOS.Templates.$VERSION.nupkg" >/dev/null
fi
echo "    OK   local feed ready — restore picks up Microsoft.Maui.SailfishOS $VERSION from $FEED"
