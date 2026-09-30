#!/bin/bash
# Blocking pre-flight check for the build/deploy loop: catches the conditions that
# otherwise give a green deploy and a broken app.
# Usage: ./tools/sf doctor [--local] [--fix] [--for-debug]
#
#   --local      skip the device checks (auth + endpoint identity)
#   --fix        build the sysroot and native shim when missing (needs network)
#   --for-debug  require SF_CONFIGURATION=Debug (Hot Reload, device diagnostics)
#
# The main catch: the .csproj ships libsailfishhost.so only if it exists, so a missing
# shim deploys cleanly and then dies at startup on DllImport("sailfishhost").
#
# Exit: 0 = all checks passed, 1 = at least one hard failure.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=../lib/sf-lib.sh
. "$SCRIPT_DIR/../lib/sf-lib.sh"

LOCAL_ONLY=0
DO_FIX=0
WANT_DEBUG=0
while [ $# -gt 0 ]; do
	case "$1" in
		--local)     LOCAL_ONLY=1 ;;
		--fix)       DO_FIX=1 ;;
		--for-debug) WANT_DEBUG=1 ;;
		-h|--help)   sed -n '2,13p' "$0"; exit 0 ;;
		*)           sf_die "unknown argument: $1 (see --help)" ;;
	esac
	shift
done

FAILS=0
pass() { sf_ok "$*"; }
fail() { sf_fail "$*"; FAILS=$((FAILS + 1)); }

# A checkout builds the shim from its own sysroot; the NuGet package ships a built one
# (tools sit in buildTransitive/net11.0/tools, the shim in runtimes/linux-arm64/native).
if [ -d "$SF_REPO_ROOT/.git" ]; then
	IN_CHECKOUT=1
	SYSROOT="$SF_REPO_ROOT/tools/.sysroot/aarch64"
	SHIM="$SF_REPO_ROOT/artifacts/native/aarch64/libsailfishhost.so"
else
	IN_CHECKOUT=0
	SYSROOT=""
	SHIM="$SF_REPO_ROOT/../../runtimes/linux-arm64/native/libsailfishhost.so"
fi

sf_info "preflight: configuration=$SF_CONFIGURATION rid=$SF_RID tfm=$SF_TFM pkg=$SF_PKG"

# --------------------------------------------- 1. aarch64 sysroot for the shim --
sf_info "[1/8] Qt sysroot"
if [ "$IN_CHECKOUT" = 0 ]; then
	pass "not needed: the package ships the built shim"
elif [ -d "$SYSROOT/usr/include/qt5/QtCore" ]; then
	pass "sysroot present: $SYSROOT"
elif [ "$DO_FIX" = 1 ]; then
	sf_note "missing - assembling from the public release repo"
	if "$SCRIPT_DIR/sysroot.sh" --runtime-from-repo >/dev/null; then
		pass "sysroot assembled"
	else
		fail "sf sysroot failed (network? see its output without the redirect)"
	fi
else
	fail "sysroot missing: $SYSROOT"
	sf_note "fix: ./tools/sf sysroot --runtime-from-repo   (or --fix here)"
fi

# ------------------------------------------------- 2. native shim, right arch --
sf_info "[2/8] native Qt/Silica shim"
if [ ! -f "$SHIM" ]; then
	if [ "$IN_CHECKOUT" = 0 ]; then
		fail "the package has no native shim: $SHIM (a broken Microsoft.Maui.SailfishOS package; restore it again)"
	elif [ "$DO_FIX" = 1 ] && [ -d "$SYSROOT/usr/include/qt5/QtCore" ]; then
		sf_note "missing - cross-building with zig"
		if "$SCRIPT_DIR/native-build.sh" >/dev/null; then
			pass "shim built: $SHIM"
		else
			fail "sf native-build failed"
		fi
	else
		fail "shim missing: $SHIM"
		sf_note "fix: ./tools/sf native-build   (needs the sysroot from step 1)"
		sf_note "     without it the app dies at startup on DllImport(\"sailfishhost\")"
	fi
fi
if [ -f "$SHIM" ]; then
	if command -v file >/dev/null 2>&1; then
		desc="$(file -b "$SHIM" 2>/dev/null || true)"
		case "$desc" in
			*ELF*aarch64*)
				pass "shim is ELF aarch64 ($(du -h "$SHIM" | cut -f1 | tr -d ' '))" ;;
			*Mach-O*)
				fail "shim is a macOS Mach-O binary, not linux/aarch64 - it cannot load on the device"
				sf_note "fix: ./tools/sf native-build" ;;
			*ELF*)
				fail "shim is ELF but not aarch64: $desc"
				sf_note "fix: ./tools/sf native-build (targets aarch64-linux-gnu)" ;;
			*)
				fail "shim is not an ELF shared object: ${desc:-unreadable}" ;;
		esac
	else
		pass "shim present (no 'file' on this host - architecture not verified)"
	fi
fi

# -------------------------------------- 3. shim actually landed in the publish --
sf_info "[3/8] publish output"
if [ -d "$SF_PUBLISH_DIR" ]; then
	if [ -f "$SF_PUBLISH_DIR/libsailfishhost.so" ]; then
		pass "shim is in the publish output"
	else
		fail "publish output exists but has no libsailfishhost.so: $SF_PUBLISH_DIR"
		sf_note "the .csproj includes the shim only when it exists - re-publish after step 2"
	fi
else
	sf_note "no publish output yet ($SF_PUBLISH_DIR) - re-run preflight after the build"
fi

# ------------------------------------------------------- 4. host tooling present --
sf_info "[4/8] host tooling"
PREFLIGHT_TOOLS="dotnet python3 ssh scp base64"
[ "$IN_CHECKOUT" = 1 ] && PREFLIGHT_TOOLS="dotnet zig python3 ssh scp base64"
for tool in $PREFLIGHT_TOOLS; do
	if command -v "$tool" >/dev/null 2>&1; then
		pass "$tool"
	else
		# zig is only needed to (re)build the shim, so it does not block when the shim exists.
		if [ "$tool" = zig ] && [ -f "$SHIM" ]; then
			sf_note "zig missing, but the shim is already built - not blocking"
		else
			fail "$tool not found in PATH"
		fi
	fi
done
# dotnet must honour the global.json pin; a mismatch makes dotnet itself error out.
if command -v dotnet >/dev/null 2>&1; then
	if sdk_ver="$(cd "$SF_REPO_ROOT" && dotnet --version 2>&1)"; then
		pass "dotnet SDK $sdk_ver (global.json satisfied)"
	else
		fail "dotnet --version failed: $sdk_ver"
	fi
fi

if [ "$LOCAL_ONLY" = 1 ]; then
	sf_note "[5/8] auth      - skipped (--local)"
	sf_note "[6/8] endpoint  - skipped (--local)"
else
	# ------------------------------------------------------- 5. non-interactive auth --
	# Tests key auth, not the transport: the expect transport runs ssh without BatchMode,
	# so an unpaired key would otherwise surface as a password prompt mid-deploy.
	sf_info "[5/8] SSH key auth (non-interactive)"
	if sf_ssh_key_ok; then
		pass "key auth works for $SF_USER@$SF_HOST"
	else
		fail "cannot authenticate to $SF_USER@$SF_HOST without a password prompt"
		sf_note "fix: ssh-keygen -t ed25519 -f ~/.ssh/id_ed25519 -N '' -C 'maui-sailfish'"
		sf_note "     ssh-copy-id -i ~/.ssh/id_ed25519.pub \"$SF_USER@$SF_HOST\""
	fi

	# --------------------------------------------------- 6. endpoint is a device --
	sf_info "[6/8] endpoint identity"
	set +e
	detect_out="$("$SCRIPT_DIR/detect.sh" --quiet 2>&1)"
	detect_rc=$?
	set -e
	case "$detect_rc" in
		0) pass "$detect_out" ;;
		2) fail "$detect_out"
		   sf_note "the endpoint answers but is not authenticated - see step 5" ;;
		*) fail "$detect_out" ;;
	esac
fi

# ------------------------------------------------------ 7. effective build config --
sf_info "[7/8] build configuration"
if [ "$WANT_DEBUG" = 1 ]; then
	if [ "$SF_CONFIGURATION" = Debug ]; then
		pass "SF_CONFIGURATION=Debug, as required for Hot Reload / device diagnostics"
	else
		fail "--for-debug given but SF_CONFIGURATION=$SF_CONFIGURATION"
		sf_note "Hot Reload and usable stepping need an unoptimized build: SF_CONFIGURATION=Debug"
	fi
else
	sf_note "SF_CONFIGURATION=$SF_CONFIGURATION (pass --for-debug to require Debug)"
fi

# --------------------------------------- 8. configuration/package-name consistency --
# SailfishRpmFileName has no $(Configuration), so Debug and Release of one version
# share an NVR; the -debug package suffix keeps them apart.
sf_info "[8/8] configuration / package identity"
case "$SF_CONFIGURATION" in
	Debug)
		if [ "${SF_PKG##*-}" = debug ]; then
			pass "Debug build -> distinct package '$SF_PKG'"
		else
			fail "Debug build would ship as '$SF_PKG', colliding with the Release package"
			sf_note "fix: SF_CONFIGURATION=Debug SF_PKG=$SF_PKG-debug ./tools/sf deploy --run"
		fi
		;;
	*)
		if [ "${SF_PKG##*-}" = debug ]; then
			fail "$SF_CONFIGURATION build is using the debug package name '$SF_PKG'"
			sf_note "fix: unset SF_PKG (defaults to the release identity)"
		else
			pass "$SF_CONFIGURATION build -> package '$SF_PKG'"
		fi
		;;
esac

# --------------------------------------------------------------------- verdict --
echo
if [ "$FAILS" -eq 0 ]; then
	sf_ok "preflight PASSED"
	exit 0
fi
sf_error "preflight FAILED with $FAILS problem(s) - do not deploy"
exit 1
