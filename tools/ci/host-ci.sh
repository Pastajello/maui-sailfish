#!/usr/bin/env bash
# tools/ci/host-ci.sh [stage ...] — the host-only CI (tracker S57): everything that needs no phone and no native
# toolchain. .github/workflows/host-ci.yml runs it; locally it runs the same way from a checkout.
#
# Every stage but style first installs the net11.0-sailfish workload manifest under $CI_OUT/manifests (the
# repo's own workload tool, --manifest-root: no admin rights) and points DOTNETSDK_WORKLOAD_MANIFEST_ROOTS at it, so a
# runner whose SDK never saw the manifest knows the TFM (the samples and the template app target net11.0-sailfish).
#
#   style      tools/ci/style-check.cs (LF, final newline, no trailing whitespace, tab-indented C#)
#   build      the CI solution filter in Release (the template app's Android/iOS heads need MAUI workloads; the
#              template is checked by the template stage instead)
#   test       tests/Linux.SailfishOS.Tests
#   pack       every package into $CI_OUT/feed, the backend without the native shim (SailfishAllowMissingShim)
#   template   dotnet new maui-sailfish --sailfish-only from the packed template, against the packed backend and
#              workload manifest only (own NuGet cache, own template hive, manifest under $CI_OUT), then build it and
#              publish its RPM (SailfishRpmPack: no rpmbuild/python on the host)
#
# No stage given: all of them, in this order. CI_OUT (default artifacts/ci) holds everything the stages write.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CI_OUT="${CI_OUT:-$ROOT/artifacts/ci}"
STAGES=("$@")
[ ${#STAGES[@]} -gt 0 ] || STAGES=(style build test pack template)

step() { printf '\n==> %s\n' "$*"; }

# A path as the native dotnet wants it: on Windows (Git Bash) environment variables and file contents are not
# translated from /d/a/... the way arguments are.
native() { if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else printf '%s' "$1"; fi; }

MANIFESTS="$CI_OUT/manifests"
ensure_manifest() {
	[ -z "${_MANIFEST_READY:-}" ] || return 0
	step "workload manifest → $MANIFESTS"
	rm -rf "$MANIFESTS"
	dotnet run --project "$ROOT/src/Linux.SailfishOS.Workload/Linux.SailfishOS.Workload.csproj" -c Release -- \
		install --manifest-root "$(native "$MANIFESTS")"
	export DOTNETSDK_WORKLOAD_MANIFEST_ROOTS
	DOTNETSDK_WORKLOAD_MANIFEST_ROOTS="$(native "$MANIFESTS")"
	_MANIFEST_READY=1
}
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_CLI_USE_MSBUILD_SERVER=0

stage_style() {
	step "style"
	dotnet run "$ROOT/tools/ci/style-check.cs"
}

stage_build() {
	ensure_manifest
	step "build (Release)"
	dotnet build "$ROOT/tools/ci/Linux.Sailfish.ci.slnf" -c Release
}

stage_test() {
	ensure_manifest
	step "test"
	dotnet test "$ROOT/tests/Linux.SailfishOS.Tests/Linux.SailfishOS.Tests.csproj" -c Release --no-build
}

stage_pack() {
	ensure_manifest
	step "pack → $CI_OUT/feed"
	rm -rf "$CI_OUT/feed"
	mkdir -p "$CI_OUT/feed"
	local project
	for project in \
		src/Linux.SailfishOS/Linux.SailfishOS.csproj \
		src/Linux.SailfishOS.SkiaSharp/Linux.SailfishOS.SkiaSharp.csproj \
		src/Linux.SailfishOS.WorkloadManifest/Linux.SailfishOS.WorkloadManifest.csproj \
		src/Linux.SailfishOS.Workload/Linux.SailfishOS.Workload.csproj \
		src/Linux.SailfishOS.Tools/Linux.SailfishOS.Tools.csproj \
		templates/sailfishos/SailfishOS.Templates.csproj; do
		dotnet pack "$ROOT/$project" -c Release -o "$CI_OUT/feed" -p:SailfishAllowMissingShim=true
	done
	ls -1 "$CI_OUT/feed"
}

stage_template() {
	ensure_manifest
	step "template smoke test"
	local smoke="$CI_OUT/smoke" hive="$CI_OUT/template-hive"
	rm -rf "$smoke" "$hive" "$CI_OUT/packages"
	mkdir -p "$smoke"
	# Only what the packages carry: a NuGet cache of its own (no package of the same version from an earlier local
	# pack), the manifest (ensure_manifest), the template from the packed template package.
	export NUGET_PACKAGES
	NUGET_PACKAGES="$(native "$CI_OUT/packages")"
	dotnet new install "$(ls "$CI_OUT"/feed/Microsoft.Maui.Platforms.SailfishOS.Templates.*.nupkg | head -1)" --debug:custom-hive "$hive"
	dotnet new maui-sailfish --sailfish-only -n CiSmoke -o "$smoke/CiSmoke" --debug:custom-hive "$hive"
	cat > "$smoke/CiSmoke/nuget.config" <<-EOF
	<?xml version="1.0" encoding="utf-8"?>
	<configuration>
	  <packageSources>
	    <clear />
	    <add key="ci-feed" value="$(native "$CI_OUT/feed")" />
	    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
	  </packageSources>
	</configuration>
	EOF
	(
		cd "$smoke/CiSmoke"
		dotnet build -f net11.0-sailfish
		# The CI pack has no native shim, so the publish skips the shim check; the RPM itself is the point here.
		dotnet publish -f net11.0-sailfish -p:CreateSailfishRpm=true -p:SailfishSkipNativeCheck=true
	)
	local rpm
	rpm="$(ls "$smoke"/CiSmoke/bin/SailfishRpm/*.rpm 2>/dev/null | head -1)"
	[ -n "$rpm" ] || { echo "ERROR: the template app's publish wrote no RPM" >&2; exit 1; }
	echo "OK   $(basename "$rpm") ($(wc -c < "$rpm" | tr -d ' ') bytes)"
}

for stage in "${STAGES[@]}"; do
	case "$stage" in
		style|build|test|pack|template) "stage_$stage" ;;
		*) echo "unknown stage: $stage (style build test pack template)" >&2; exit 2 ;;
	esac
done
step "host CI passed: ${STAGES[*]}"
