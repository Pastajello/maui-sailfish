#!/bin/bash
# End-to-end acceptance test for the app's harbour RPM.
#
# Usage: ./tools/sf package-test [--release-a N] [--release-b M]
#                                   [--jit|--trim|--trimr2r]
#
#   --release-a/-b N   RPM releases to install and update to (default: timestamp, +1)
#
# [P] payload audit of both RPMs: host shim, QML, .NET payload, desktop file, icon
# [Z] install, update, uninstall and reinstall via pkcon (the libzypp front-end;
#     Sailfish ships no zypper CLI), with sf verify after each install
# [C] desktop-file-validate, file modes, launcher symlink
# [L] launch via direct exec (graded) plus lipstick WindowModel and sailjail probes
#
# Needs the device unlocked with the display on, and libsailfishhost.so built.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=../lib/sf-lib.sh
. "$SCRIPT_DIR/../lib/sf-lib.sh"

# ------------------------------------------------------------------ args ----
REL_A=""
REL_B=""
while [ $# -gt 0 ]; do
	case "$1" in
		--release-a) shift; REL_A="${1:-}" ;;
		--release-b) shift; REL_B="${1:-}" ;;
		--jit) sf_set_profile jit ;;
		--trim) sf_set_profile trim ;;
		--trimr2r) sf_set_profile trimr2r ;;
		-h|--help) sed -n '2,15p' "$0"; exit 0 ;;
		*) sf_die "unknown argument: $1 (see --help)" ;;
	esac
	shift
done
[ -n "$REL_A" ] || REL_A="$(date +%Y%m%d%H%M%S)"
[ -n "$REL_B" ] || REL_B="$((REL_A + 1))"

command -v dotnet >/dev/null 2>&1 || sf_die "dotnet not found in PATH"
command -v cpio   >/dev/null 2>&1 || sf_die "cpio not found in PATH (payload audit)"
sf_use_rpm_shims || sf_die "rpmbuild missing and no shim available"
_sf_require_transport package-test

# ------------------------------------------------------- result tracking ----
RESULTS=""
record() { RESULTS="${RESULTS}$1|$2"$'\n'; }

# ------------------------------------------------------------- publish ------
publish_release() { # <release> -> sets RPM_FILE (stable copy; the next publish wipes the dir)
	sf_note "publishing release $1 (payload profile: $SF_PROFILE)"
	sf_publish_rpm "$1"
	RPM_FILE="/tmp/sf-package-test-$SF_PKG-$1.$SF_RPM_ARCH.rpm"
	cp "$SF_BUILT_RPM" "$RPM_FILE" || sf_die "cannot stage $SF_BUILT_RPM"
}

rpm_nvr() { # <rpm-file> -> VERSION-RELEASE (from the RPM header, like sf verify)
	rpm -qp --qf '%{VERSION}-%{RELEASE}' "$1" | tail -1
}

# ------------------------------------------------------- payload audit ------
payload_audit() { # <rpm> <release>
	local rpm="$1" names n_qml n_dll min_dll c staged
	# rpm -qpl is authoritative; the cpio fallback cannot read zstd payloads on macOS.
	if ! { names="$(rpm -qpl "$rpm" 2>/dev/null)" && [ -n "$names" ]; }; then
		names="$(python3 "$SCRIPT_DIR/../py/sf-rpm2cpio.py" "$rpm" | cpio -it 2>/dev/null)" \
			|| sf_die "cannot list payload of $rpm"
	fi
	# Normalize "/usr/…" (rpm) and "./usr/…" (cpio) to "usr/…".
	names="$(printf '%s\n' "$names" | sed 's|^\./||; s|^/||')"
	local required=(
		"usr/bin/$SF_PKG"
		"usr/share/applications/$SF_PKG.desktop"
		"usr/share/icons/hicolor/108x108/apps/$SF_PKG.png"
		"usr/share/$SF_PKG/libsailfishhost.so"
		"usr/share/$SF_PKG/$SF_BIN"
		"usr/share/$SF_PKG/$SF_BIN.dll"
		"usr/share/$SF_PKG/qml/MauiShell.qml"
	)
	for c in "${required[@]}"; do
		if printf '%s\n' "$names" | grep -qx "$c"; then
			sf_ok   "payload: $c"
		else
			sf_error "payload MISSING: $c"
			record "P:payload($c)" "FAIL"; return 1
		fi
	done
	n_qml="$(printf '%s\n' "$names" | grep -cE "/qml/.+\.(qml|js|json)$" || true)"
	n_dll="$(printf '%s\n' "$names" | grep -c '\.dll$' || true)"
	# Trimming drops unused framework assemblies, so the dll floor depends on the
	# profile; QML assets are Content and the same in every profile.
	case "$SF_PROFILE" in
		jit) min_dll=100 ;;
		*)   min_dll=60  ;;
	esac
	sf_note "payload: $n_qml QML assets, $n_dll managed assemblies (profile $SF_PROFILE, floor $min_dll)"
	[ "$n_qml" -ge 30 ]         || { sf_error "too few QML assets ($n_qml)"; record "P:qml-count" "FAIL"; return 1; }
	[ "$n_dll" -ge "$min_dll" ] || { sf_error "too few assemblies ($n_dll < $min_dll for profile $SF_PROFILE)"; record "P:dll-count" "FAIL"; return 1; }
	staged="$(find "$SF_SAMPLE_DIR/obj/SailfishRpm" -name "$SF_PKG.desktop" 2>/dev/null | head -1)"
	[ -n "$staged" ] || { sf_error "staged desktop file not found"; record "P:desktop" "FAIL"; return 1; }
	# Either the opt-out (SailfishSandboxing=false) or a sandbox: Permissions= plus the data-dir identity.
	if grep -q '^Sandboxing=Disabled$' "$staged"; then
		grep -q '^\[X-Sailjail\]$' "$staged" && ! grep -q '^Permissions=' "$staged" \
			|| { sf_error "desktop has a malformed unsandboxed [X-Sailjail] section:"; cat "$staged"; record "P:desktop" "FAIL"; return 1; }
		sf_ok "payload: desktop file has [X-Sailjail] Sandboxing=Disabled (SailfishSandboxing=false)"
	else
		grep -q '^\[X-Sailjail\]$' "$staged" && grep -q '^Permissions=' "$staged" \
			&& grep -q '^OrganizationName=.' "$staged" && grep -q '^ApplicationName=.' "$staged" \
			|| { sf_error "desktop lacks a Sailjail sandbox (Permissions=, OrganizationName, ApplicationName):"; cat "$staged"; record "P:desktop" "FAIL"; return 1; }
		sf_ok "payload: desktop file declares a Sailjail sandbox ($(grep '^Permissions=' "$staged"))"
	fi
	record "P:payload(release $2)" "PASS"
}

# -------------------------------------------------------------- upload ------
upload_rpm() { # <rpm> -> sets REMOTE_RPM
	sf_upload_rpm "$1"
	REMOTE_RPM="$SF_REMOTE_RPM"
}

# ------------------------------------------------------------- install ------
expect_nvr() { # <expected VERSION-RELEASE> <what>
	local got
	got="$(sf_ssh_out "rpm -q --qf '%{VERSION}-%{RELEASE}' $SF_PKG 2>/dev/null" | tail -1)" || true
	[ "$got" = "$1" ] || { sf_error "$2: installed NVR '$got' != expected '$1'"; return 1; }
	sf_ok "$2: rpm -q => $SF_PKG-$got"
}
# ------------------------------------------------------------------ main ----
sf_info "=== package test: releases A=$REL_A B=$REL_B ==="
sf_ping
sf_device_ready || sf_die "device is not awake+unlocked - unlock it, then re-run"

# -- [P] build both releases and audit their payloads -------------------------
sf_info "[P] Building release A ($REL_A)"
publish_release "$REL_A"; RPM_A="$RPM_FILE"
payload_audit "$RPM_A" "$REL_A" || sf_error "payload audit A FAILED"

sf_info "[P] Building release B ($REL_B)"
publish_release "$REL_B"; RPM_B="$RPM_FILE"
payload_audit "$RPM_B" "$REL_B" || sf_error "payload audit B FAILED"

NVR_A="$(rpm_nvr "$RPM_A")"; NVR_B="$(rpm_nvr "$RPM_B")"

# -- [Z] zypp (pkcon) install / update / remove cycle -------------------------
sf_info "[Z] Uploading both RPMs"
upload_rpm "$RPM_A"; REMOTE_A="$REMOTE_RPM"
upload_rpm "$RPM_B"; REMOTE_B="$REMOTE_RPM"

sf_push_kill_helper
sf_kill_app >/dev/null 2>&1 || true

# rpm -e needs root; root-free mode reaches the same baseline via PackageKit.
if sf_have_root_pw; then
	sf_info "[Z] Baseline removal (rpm -e via devel-su)"
	sf_root "rpm -e $SF_PKG 2>/dev/null; rpm -q $SF_PKG >/dev/null 2>&1 && { echo STILL_INSTALLED; exit 1; }; echo BASELINE_CLEAN" | grep -q BASELINE_CLEAN \
		&& sf_ok "baseline: package removed" || sf_die "could not reach a clean baseline"
else
	sf_info "[Z] Baseline removal (pkcon remove, root-free mode)"
	sf_ssh "pkcon remove -y $SF_PKG >/dev/null 2>&1; rpm -q $SF_PKG >/dev/null 2>&1 && { echo STILL_INSTALLED; exit 1; }; echo BASELINE_CLEAN" | grep -q BASELINE_CLEAN \
		&& sf_ok "baseline: package removed (root-free)" || sf_die "could not reach a clean baseline"
fi

sf_info "[Z] Fresh install via pkcon (zypp backend): release A"
if sf_priv "pkcon install-local -y '$REMOTE_A' 2>&1"; then
	expect_nvr "$NVR_A" "Z:fresh-install" && "$SCRIPT_DIR/verify.sh" "$RPM_A" \
		&& record "Z:fresh-install(pkcon A)" "PASS" \
		|| record "Z:fresh-install(pkcon A)" "FAIL"
else
	sf_error "pkcon install-local A failed"
	record "Z:fresh-install(pkcon A)" "FAIL"
fi

sf_info "[Z] Update via pkcon (zypp backend): release A -> B"
if sf_priv "pkcon install-local -y '$REMOTE_B' 2>&1"; then
	expect_nvr "$NVR_B" "Z:update" && "$SCRIPT_DIR/verify.sh" "$RPM_B" \
		&& record "Z:update(pkcon A->B)" "PASS" \
		|| record "Z:update(pkcon A->B)" "FAIL"
else
	sf_error "pkcon install-local B (update) failed"
	record "Z:update(pkcon A->B)" "FAIL"
fi

sf_info "[Z] Clean uninstall via pkcon"
if sf_priv "pkcon remove -y $SF_PKG 2>&1"; then
	leftover="$(sf_ssh_out "ls -d /usr/bin/$SF_PKG /usr/share/$SF_PKG /usr/share/applications/$SF_PKG.desktop /usr/share/icons/hicolor/108x108/apps/$SF_PKG.png 2>/dev/null | wc -l" | tail -1)"
	if [ "${leftover:-1}" = "0" ]; then
		sf_ok "uninstall: no payload leftovers"
		record "Z:uninstall(pkcon)" "PASS"
	else
		sf_error "uninstall left $leftover path(s) behind"
		record "Z:uninstall(pkcon)" "FAIL"
	fi
else
	sf_error "pkcon remove failed"
	record "Z:uninstall(pkcon)" "FAIL"
fi

sf_info "[Z] Restore: reinstall release B via pkcon"
if sf_priv "pkcon install-local -y '$REMOTE_B' 2>&1"; then
	expect_nvr "$NVR_B" "Z:restore" && record "Z:restore(pkcon B)" "PASS" || record "Z:restore(pkcon B)" "FAIL"
else
	record "Z:restore(pkcon B)" "FAIL"
fi
sf_ssh "rm -f '$REMOTE_A' '$REMOTE_B'" >/dev/null 2>&1 || true

# -- [C] desktop / icon / permissions compliance ------------------------------
sf_info "[C] Desktop-file and permissions compliance"
if sf_ssh_out "desktop-file-validate /usr/share/applications/$SF_PKG.desktop && echo DTV_OK" | grep -q DTV_OK; then
	sf_ok "desktop-file-validate: clean"
	record "C:desktop-file-validate" "PASS"
else
	sf_error "desktop-file-validate reported problems"
	record "C:desktop-file-validate" "FAIL"
fi
sf_ssh_out "cat /usr/share/applications/$SF_PKG.desktop" | sed 's/^/    /' || true

MODES_OK=1
check_mode() { # <path> <expected-octal-prefix> <label>
	local m
	m="$(sf_ssh_out "stat -c '%a' '$1' 2>/dev/null" | tail -1)" || true
	case "$m" in
		$2*) sf_ok "mode $1 = $m" ;;
		*)   sf_error "mode $1 = '${m:-missing}' (expected $2*)"; MODES_OK=0 ;;
	esac
}
check_mode "/usr/bin/$SF_PKG" 777 "launcher symlink"     # lrwxrwxrwx
check_mode "/usr/share/$SF_PKG/$SF_BIN" 755 "app ELF"
check_mode "/usr/share/$SF_PKG/libsailfishhost.so" 755 "qt host shim"
check_mode "/usr/share/applications/$SF_PKG.desktop" 644 "desktop"
check_mode "/usr/share/icons/hicolor/108x108/apps/$SF_PKG.png" 644 "icon"
ELF_OK="$(sf_ssh_out "readlink -f /usr/bin/$SF_PKG; file -L /usr/bin/$SF_PKG | grep -c ELF" | tr '\n' ' ')" || true
sf_note "launcher resolves to ELF: $ELF_OK"
if [ "$MODES_OK" = 1 ]; then record "C:modes" "PASS"; else record "C:modes" "FAIL"; fi

# -- [L] launcher-style launches ----------------------------------------------
# Direct exec of /usr/bin/<pkg> is graded: the binary is the same one lipstick starts, but a sandboxed
# entry goes through sailjail, which refuses an SSH session, so windowmodel and sailjail stay informational.
sf_info "[L] Launcher-path launches (direct graded; windowmodel/sailjail probes)"
sf_push_helper "$SCRIPT_DIR/../remote/sf-package-launch-remote.sh"
for mode in direct windowmodel sailjail; do
	sf_keep_awake
	sf_info "[L] mode=$mode"
	out="$(sf_ssh "/tmp/sf-package-launch-remote.sh $SF_PKG $SF_BIN $mode" 2>&1)" || true
	printf '%s\n' "$out" | sed 's/^/    /'
	if printf '%s\n' "$out" | grep -q 'RESULT=PASS'; then
		if [ "$mode" = direct ]; then
			record "L:launch(direct-exec,topmost)" "PASS"
		else
			record "L:probe($mode)" "PASS"
		fi
	else
		if [ "$mode" = direct ]; then
			record "L:launch(direct-exec,topmost)" "FAIL"
		else
			record "L:probe($mode)" "INFO"
		fi
	fi
done

sf_keep_awake
sf_info "[L] resolved app-id evidence (diag launch)"
out="$(sf_ssh "/tmp/sf-package-launch-remote.sh $SF_PKG $SF_BIN diag" 2>&1)" || true
printf '%s\n' "$out" | sed 's/^/    /'
if printf '%s\n' "$out" | grep -q "app=$SF_PKG "; then
	record "L:appid($SF_PKG)" "PASS"
else
	record "L:appid($SF_PKG)" "FAIL"
fi
sf_kill_app >/dev/null 2>&1 || true

# ------------------------------------------------------------- summary ------
echo
sf_info "=== PACKAGE TEST SUMMARY ==="
FAILS=0
while IFS='|' read -r name verdict; do
	[ -n "$name" ] || continue
	printf '  %-34s %s\n' "$name" "$verdict"
	if [ "$verdict" = FAIL ]; then
		FAILS=$((FAILS + 1))
	fi
done <<< "$RESULTS"
if [ "$FAILS" -eq 0 ]; then
	sf_ok "package test: ALL PASS"
	exit 0
fi
sf_error "package test: $FAILS FAILURE(S)"
exit 1
