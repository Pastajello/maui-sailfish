#!/bin/bash
# Build, upload, install and verify the Sailfish OS app on a device.
# Usage: ./tools/sf-deploy.sh [--clean] [--run] [--no-verify] [--release <n>]
#                             [--jit|--trim|--trimr2r]
#
#   --clean       also wipe the RID publish/intermediate dirs (full rebuild)
#   --run         launch the app on the device after a successful deploy
#   --no-verify   skip the post-install verification (not recommended)
#   --release N   RPM release number; default "auto" = build timestamp
#   --trimr2r     trimmed + ReadyToRun payload (default)
#   --trim        trimmed payload only
#   --jit         plain self-contained CoreCLR (escape hatch)
#
# Env: SF_HOST, SF_USER, SF_PASSWORD, SF_PKG, SF_BIN, SF_RID, SF_CONFIGURATION, SF_PROFILE,
# SF_PUBLISH_PROPS (extra -p: arguments for dotnet publish, e.g. "-p:SailfishMetrics=false" for A/B builds).
# SF_PKG names both the RPM and the on-device paths; a Debug build needs its own
# (e.g. SF_PKG=harbour-sample-debug) to coexist with Release.
#
# A unique release per build, wiped staging dirs, killing the app around the install
# and rpm -Uvh --force together guarantee the phone never keeps running an old build.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=sf-lib.sh
. "$SCRIPT_DIR/sf-lib.sh"

# ------------------------------------------------------------------ args ----
DO_CLEAN=0
DO_RUN=0
DO_VERIFY=1
RELEASE="auto"

while [ $# -gt 0 ]; do
	case "$1" in
		--clean) DO_CLEAN=1 ;;
		--run) DO_RUN=1 ;;
		--no-verify) DO_VERIFY=0 ;;
		--release) shift; RELEASE="${1:-}" ;;
		--jit) sf_set_profile jit ;;
		--trim) sf_set_profile trim ;;
		--trimr2r) sf_set_profile trimr2r ;;
		-h|--help) sed -n '2,19p' "$0"; exit 0 ;;
		*) sf_die "unknown argument: $1 (see --help)" ;;
	esac
	shift
done

if [ "$RELEASE" = "auto" ]; then
	RELEASE="$(date +%Y%m%d%H%M%S)"
fi
case "$RELEASE" in
	''|*[!0-9]*) sf_die "RPM release must be numeric (harbour requirement), got: '$RELEASE'" ;;
esac

for tool in dotnet base64; do
	command -v "$tool" >/dev/null 2>&1 || sf_die "$tool not found in PATH (see README.md, Prerequisites, for the build-host requirements)"
done
# Prefer real rpmbuild/rpm, fall back to the pure-Python shims in tools/.
sf_use_rpm_shims \
	|| sf_die "rpmbuild/rpm are missing and tools/sf-rpmbuild.py + tools/sf-rpmquery.py are not available"
sf_note "rpmbuild=$(command -v rpmbuild) rpm=$(command -v rpm) ssh-transport=$SF_TRANSPORT"
_sf_require_transport deploy

# ------------------------------------------------------- 1. clean staging ----
sf_info "[1/6] Cleaning previous Sailfish RPM artifacts"
rm -rf "$SF_SAMPLE_DIR/obj/SailfishRpm" "$SF_RPM_DIR"
if [ "$DO_CLEAN" -eq 1 ]; then
	sf_note "full rebuild: removing $SF_CONFIGURATION/$SF_TFM/$SF_RID publish + intermediate output"
	rm -rf "$SF_SAMPLE_DIR/bin/$SF_CONFIGURATION/$SF_TFM/$SF_RID" \
	       "$SF_SAMPLE_DIR/obj/$SF_CONFIGURATION/$SF_TFM/$SF_RID"
fi

# ------------------------------------------------------------ 2. publish ----
sf_info "[2/6] Publishing self-contained RPM ($SF_RID, release $RELEASE, package $SF_PKG)"
sf_note "payload profile: $SF_PROFILE${SF_PROFILE_PROPS:+  ($SF_PROFILE_PROPS)}${SF_PUBLISH_PROPS:+  extra: $SF_PUBLISH_PROPS}"
cd "$SF_SAMPLE_DIR"
# SC2086: SF_PROFILE_PROPS is a deliberate word-split list of -p: arguments.
# shellcheck disable=SC2086
dotnet publish \
	-c "$SF_CONFIGURATION" \
	-f "$SF_TFM" \
	-r "$SF_RID" \
	-p:SelfContained=true \
	-p:CreateSailfishRpm=true \
	-p:SailfishRelease="$RELEASE" \
	-p:SailfishPackageName="$SF_PKG" \
	$SF_PROFILE_PROPS \
	${SF_PUBLISH_PROPS:-} \
	--nologo -v minimal \
	|| sf_die "dotnet publish failed"

RPM_FILE="$(ls -1 "$SF_RPM_DIR"/*-"$RELEASE"."$SF_RPM_ARCH".rpm 2>/dev/null | head -1)"
[ -n "$RPM_FILE" ] || sf_die "publish produced no RPM for release $RELEASE in $SF_RPM_DIR"

RPM_COUNT="$(ls -1 "$SF_RPM_DIR"/*.rpm 2>/dev/null | wc -l | tr -d ' ')"
[ "$RPM_COUNT" = "1" ] || sf_die "expected exactly 1 RPM in $SF_RPM_DIR, found $RPM_COUNT"

RPM_NAME="$(basename "$RPM_FILE")"
LOCAL_RPM_SHA="$(_sf_sha256 "$RPM_FILE")"
sf_note "RPM: $RPM_NAME ($(du -h "$RPM_FILE" | cut -f1)) sha256=${LOCAL_RPM_SHA:0:16}..."

[ -f "$SF_PUBLISH_DIR/$SF_BIN.dll" ] || sf_die "publish output not found at $SF_PUBLISH_DIR"

# -------------------------------------------------- 3. stop the running app --
# invoker runs the app --single-instance: a surviving old process would just be
# re-activated on the next tap and the new binary would never run.
sf_info "[3/6] Stopping running instances on $SF_USER@$SF_HOST"
sf_ping
sf_push_kill_helper
sf_kill_app | sed 's/^/    /' || true

# ------------------------------------------------------------ 4. upload -----
sf_info "[4/6] Uploading $RPM_NAME"
REMOTE_RPM="/home/$SF_USER/$RPM_NAME"
sf_scp_to "$RPM_FILE" "$REMOTE_RPM" || sf_die "scp of $RPM_NAME failed"

DEVICE_RPM_SHA="$(sf_ssh_out "sha256sum '$REMOTE_RPM' 2>/dev/null" | awk '{print $1}' | tail -1)" || true
[ "$DEVICE_RPM_SHA" = "$LOCAL_RPM_SHA" ] \
	|| sf_die "uploaded RPM is corrupt: device ${DEVICE_RPM_SHA:-<missing>} != local $LOCAL_RPM_SHA"
sf_ok "upload verified (sha256 matches)"

# ------------------------------------------------------------ 5. install ----
if sf_have_root_pw; then
	sf_info "[5/6] Installing on the device (devel-su + rpm -Uvh --force)"
	# --force reinstalls even an identical NVR; --nodeps is safe because harbour
	# packages are self-contained (AutoReqProv: no).
	sf_root "rpm -Uvh --force --nodeps '$REMOTE_RPM'" || sf_die "rpm install failed on the device"
else
	# Root-free (no device password): install through PackageKit, whose polkit policy
	# allows install-local for the active session. zypp refuses to reinstall an identical
	# NVR, so the unique release matters even more here.
	sf_info "[5/6] Installing on the device (pkcon install-local, root-free mode)"
	sf_ssh "pkcon install-local -y '$REMOTE_RPM' 2>&1" || sf_die "pkcon install-local failed on the device"
fi
sf_note "installed: $(sf_ssh_out "rpm -q --last $SF_PKG 2>/dev/null" | head -1)"
sf_kill_app >/dev/null 2>&1 || true

# ------------------------------------------------------------- 6. verify ----
if [ "$DO_VERIFY" -eq 1 ]; then
	sf_info "[6/6] Verifying the installed build"
	"$SCRIPT_DIR/sf-verify.sh" "$RPM_FILE" || {
		sf_error "deploy verification FAILED - the device does not run this build"
		exit 1
	}
else
	sf_info "[6/6] Verification skipped (--no-verify)"
fi

# Do not leave one ~27 MB RPM per build in $HOME.
sf_ssh "rm -f '$REMOTE_RPM'" >/dev/null 2>&1 || true

echo
sf_ok "Deployed $RPM_NAME to $SF_USER@$SF_HOST"
sf_note "start it from the app grid, or run: ./tools/sf-run.sh"

if [ "$DO_RUN" -eq 1 ]; then
	echo
	# sf-run.sh's ssh session only forwards the --env blob, so SF_DIAGNOSTICS must ride
	# it or the CLR debug port stays closed and vsdbg breakpoints stay unbound.
	if [ "${SF_DIAGNOSTICS:-0}" = "1" ]; then
		exec "$SCRIPT_DIR/sf-run.sh" --env SF_DIAGNOSTICS=1
	fi
	exec "$SCRIPT_DIR/sf-run.sh"
fi

