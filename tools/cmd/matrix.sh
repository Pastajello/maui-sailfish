#!/bin/bash
# Native regression matrix: one device launch per diag leg (each prints its own
# ACCEPTANCE verdict and auto-shuts down), aggregated into one QT MATRIX report.
#
# Usage: ./tools/sf matrix [leg ...]        default: all legs
#   SF_MATRIX_EXTRA_ENV="NAME=VALUE ..." adds env to every leg (A/B runs)
#   legs: see ALL_LEGS below
#
# Runs against the installed RPM (deploy first). The device must be unlocked with
# the display on, or the window never foregrounds and every leg reports NO-VERDICT.
# sf run exits non-zero for auto-shutdown legs, so verdicts come only from the device log.

set -euo pipefail
case "${1:-}" in -h|--help) awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"; exit 0 ;; esac

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=../lib/sf-lib.sh
. "$SCRIPT_DIR/../lib/sf-lib.sh"

ALL_LEGS="page controls nav popup collection collection10 collection100 collection500 shapes visual text input geometry reconcile bridge stress perf error tree shell containers pulley navback features f3 f4 adapterbench"
LEGS="${*:-$ALL_LEGS}"
# Checked up front: leg_env/leg_marker run in $(...), where sf_die only leaves the subshell.
for leg in $LEGS; do
	case " $ALL_LEGS " in *" $leg "*) ;; *) sf_die "unknown leg: $leg (legs: $ALL_LEGS)" ;; esac
done

leg_env() {
	case "$1" in
		page)       echo "MAUI_SAILFISH_QT_HOST_PAGE_DIAG=1" ;;
		controls)   echo "MAUI_SAILFISH_QT_HOST_CONTROLS_DIAG=1" ;;
		nav)        echo "MAUI_SAILFISH_QT_HOST_NAV_DIAG=1" ;;
		popup)      echo "MAUI_SAILFISH_QT_HOST_POPUP_DIAG=1" ;;
		collection) echo "MAUI_SAILFISH_QT_HOST_COLLECTION_DIAG=1" ;;
		collection10)  echo "MAUI_SAILFISH_QT_HOST_COLLECTION_DIAG=1 MAUI_SAILFISH_QT_HOST_COLLECTION_ROWS=10" ;;
		collection100) echo "MAUI_SAILFISH_QT_HOST_COLLECTION_DIAG=1 MAUI_SAILFISH_QT_HOST_COLLECTION_ROWS=100" ;;
		collection500) echo "MAUI_SAILFISH_QT_HOST_COLLECTION_DIAG=1 MAUI_SAILFISH_QT_HOST_COLLECTION_ROWS=500" ;;
		shapes)     echo "MAUI_SAILFISH_QT_HOST_SHAPES_DIAG=1" ;;
		visual)     echo "MAUI_SAILFISH_QT_HOST_VISUAL_DIAG=1" ;;
		reconcile)  echo "MAUI_SAILFISH_QT_HOST_RECONCILE_DIAG=1" ;;
		bridge)     echo "MAUI_SAILFISH_QT_HOST_RECONCILE_DIAG=1 MAUI_SAILFISH_QT_HOST_BRIDGE_DIAG=1" ;;
		text)       echo "MAUI_SAILFISH_QT_HOST_RECONCILE_DIAG=1 MAUI_SAILFISH_QT_HOST_TEXT_DIAG=1" ;;
		input)      echo "MAUI_SAILFISH_QT_HOST_RECONCILE_DIAG=1 MAUI_SAILFISH_QT_HOST_INPUT_DIAG=1" ;;
		geometry)   echo "MAUI_SAILFISH_QT_HOST_RECONCILE_DIAG=1 MAUI_SAILFISH_QT_HOST_GEOMETRY_DIAG=1" ;;
		stress)     echo "MAUI_SAILFISH_QT_HOST_STRESS_DIAG=1" ;;
		perf)       echo "MAUI_SAILFISH_QT_HOST_PERF_DIAG=1" ;;
		error)      echo "MAUI_SAILFISH_QT_HOST_ERROR_DIAG=1" ;;
		tree)       echo "MAUI_SAILFISH_QT_HOST_TREE_DIAG=1" ;;
		shell)      echo "MAUI_SAILFISH_QT_HOST_SHELL_DIAG=1" ;;
		containers) echo "MAUI_SAILFISH_QT_HOST_CONTAINERS_DIAG=1" ;;
		pulley)     echo "MAUI_SAILFISH_QT_HOST_PULLEY_DIAG=1" ;;
		navback)    echo "MAUI_SAILFISH_QT_HOST_NAVBACK_DIAG=1" ;;
		features)   echo "MAUI_SAILFISH_QT_HOST_FEATURES_DIAG=1" ;;
		adapterbench) echo "MAUI_SAILFISH_QT_HOST_ADAPTERBENCH_DIAG=1" ;;
		f3)         echo "MAUI_SAILFISH_QT_HOST_F3_DIAG=1" ;;
		f4)         echo "MAUI_SAILFISH_QT_HOST_F4_DIAG=1" ;;
		*) sf_die "unknown leg: $1" ;;
	esac
}

# Only the leg's own verdict line counts: reconcile-based legs also print the
# generic "QtHost diag: VERIFY … => OK" line first.
leg_marker() {
	case "$1" in
		page)        echo 'Qt page diag: ACCEPTANCE' ;;
		controls)    echo 'Qt controls diag: ACCEPTANCE' ;;
		nav)         echo 'Qt nav diag: ACCEPTANCE' ;;
		popup)       echo 'Qt popup diag: ACCEPTANCE' ;;
		collection*) echo 'Qt collection diag: ACCEPTANCE' ;;
		shapes)      echo 'QT SHAPES DIAG:' ;;
		visual)      echo 'QT VISUAL DIAG:' ;;
		reconcile)   echo 'QtHost diag: VERIFY' ;;
		bridge)      echo 'Qt bridge diag: ACCEPTANCE' ;;
		text)        echo 'Qt text diag: ACCEPTANCE' ;;
		input)       echo 'Qt input diag: ACCEPTANCE' ;;
		geometry)    echo 'Qt geometry diag: ACCEPTANCE' ;;
		stress)      echo 'QT STRESS DIAG:' ;;
		perf)        echo 'QT PERF DIAG:' ;;
		error)       echo 'QT ERROR DIAG: ACCEPTANCE' ;;
		tree)        echo 'Qt tree diag: ACCEPTANCE' ;;
		shell)       echo 'Qt shell diag: ACCEPTANCE' ;;
		containers)  echo 'Qt containers diag: ACCEPTANCE' ;;
		pulley)      echo 'Qt pulley diag: ACCEPTANCE' ;;
		navback)     echo 'Qt navback diag: ACCEPTANCE' ;;
		features)    echo 'Qt features diag: ACCEPTANCE' ;;
		adapterbench) echo 'Qt adapterbench diag: ACCEPTANCE' ;;
		f3)          echo 'Qt f3 diag: ACCEPTANCE' ;;
		f4)          echo 'Qt f4 diag: ACCEPTANCE' ;;
		*) sf_die "no verdict marker for leg: $1" ;;
	esac
}

run_leg() { # <leg> -> prints "leg|verdict|evidence"
	local leg="$1" log="/tmp/sf-matrix-$1.log" verdict evidence
	sf_keep_awake
	# Portrait pinned: the legs inject gestures at portrait window coordinates, however the phone is held.
	local args=(--env MAUI_SAILFISH_QT_HOST=1 --env MAUI_SAILFISH_QT_HOST_DIAG=1
	            --env MAUI_SAILFISH_QT_HOST_AUTO_SHUTDOWN=1 --env MAUI_SAILFISH_ORIENTATION=Portrait)
	local e
	for e in $(leg_env "$leg"); do args+=(--env "$e"); done
	# A/B runs: extra env for every leg, e.g. SF_MATRIX_EXTRA_ENV="DOTNET_TieredPGO=0"
	for e in ${SF_MATRIX_EXTRA_ENV:-}; do args+=(--env "$e"); done
	# Waits for the leg's auto-shutdown (long legs finish well after the launch window); a leg still
	# running after 240 s gets its verdict from whatever the log holds by then.
	"$SCRIPT_DIR/run.sh" --wait 240 "${args[@]}" >"$log" 2>&1 || true
	# Most legs print their verdict after sf-run's log dump, so re-fetch the device log.
	sf_ssh "cat /tmp/sf_run.log" >"$log.dev" 2>/dev/null || true
	cat "$log" "$log.dev" >"$log.all" 2>/dev/null || cp "$log" "$log.all"
	evidence="$(grep -F "$(leg_marker "$leg")" "$log.all" | tail -1 || true)"
	if [ -z "$evidence" ]; then
		verdict="NO-VERDICT"
		evidence="(leg never printed '$(leg_marker "$leg")' — see $log.all)"
	elif printf '%s' "$evidence" | grep -qE 'FAIL|failed=[1-9]' \
	     || grep -q 'CHECK FAIL' "$log.all"; then
		verdict="FAIL"
	elif printf '%s' "$evidence" | grep -qE '=> (OK|PASS)|checks OK'; then
		verdict="PASS"
	else
		verdict="NO-VERDICT"
	fi
	printf '%s|%s|%s\n' "$leg" "$verdict" "$evidence"
}

sf_info "=== QT MATRIX: legs: $LEGS ==="
if ! sf_device_ready; then
	sf_die "device is not awake+unlocked — unlock the phone and wake the display, then re-run"
fi

RESULTS=""
PASS_N=0
TOTAL_N=0
for leg in $LEGS; do
	sf_info "--- leg: $leg ---"
	line="$(run_leg "$leg")"
	RESULTS="${RESULTS}${line}"$'\n'
	TOTAL_N=$((TOTAL_N + 1))
	case "$line" in *"|PASS|"*) PASS_N=$((PASS_N + 1)) ;; esac
done

SUMMARY="/tmp/sf-matrix-summary-$(date +%Y%m%d-%H%M%S).txt"
{
	echo "QT MATRIX $(date '+%Y-%m-%d %H:%M:%S')"
	printf '%s' "$RESULTS" | while IFS='|' read -r leg verdict evidence; do
		[ -n "$leg" ] && printf '  %-11s %-10s %s\n' "$leg" "$verdict" "$evidence"
	done
	echo "QT MATRIX: $PASS_N/$TOTAL_N legs PASS"
} | tee "$SUMMARY"

[ "$PASS_N" -eq "$TOTAL_N" ] || sf_die "matrix has failing/no-verdict legs (see $SUMMARY)"
sf_ok "QT MATRIX complete: $PASS_N/$TOTAL_N legs PASS ($SUMMARY)"
