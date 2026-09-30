#!/bin/bash
# Shared helpers for the Sailfish OS device tooling (tools/sf-*.sh).
# Sourced, never executed:  . "$(dirname "$0")/sf-lib.sh"
# Remote commands travel on ssh's stdin (no quoting mangling, no secrets in argv)
# and the remote exit status is propagated.

# ---------------------------------------------------------------- device ----
SF_REPO_ROOT="${SF_REPO_ROOT:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}"
# The tools/ root (checkout or package): cmd/, remote/, py/ and the `sf` dispatcher live under it.
_SF_TOOLS_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# Device settings come from a connect.info ("IP:", "user:", "password:"); first hit wins:
#   $SF_CONNECT_INFO, $SF_SAMPLE_DIR/connect.info, <repo>/connect.info,
#   ${XDG_CONFIG_HOME:-~/.config}/maui-sailfish/connect.info (written by sf setup).
# A file whose IP is still a template placeholder does not count.
SF_CONFIG_DIR="${SF_CONFIG_DIR:-${XDG_CONFIG_HOME:-$HOME/.config}/maui-sailfish}"
_sf_file_has_host() { # <file> - true when it names a real device
	[ -f "$1" ] || return 1
	local ip
	ip="$(sed -n -E 's/^[[:space:]]*(IP|host):[[:space:]]*//p' "$1" | tr -d '\r' | tail -n 1)"
	case "$ip" in ''|'<'*'>') return 1 ;; esac
	return 0
}
if [ -z "${SF_CONNECT_INFO:-}" ]; then
	for _sf_candidate in "${SF_SAMPLE_DIR:+$SF_SAMPLE_DIR/connect.info}" "$SF_REPO_ROOT/connect.info" "$SF_CONFIG_DIR/connect.info"; do
		if [ -n "$_sf_candidate" ] && _sf_file_has_host "$_sf_candidate"; then
			SF_CONNECT_INFO="$_sf_candidate"
			break
		fi
	done
	unset _sf_candidate
	SF_CONNECT_INFO="${SF_CONNECT_INFO:-$SF_CONFIG_DIR/connect.info}"
fi

# _sf_connect_info <key> - value of a "<key>: <value>" line, empty if absent.
# Template placeholders ("<sshPassword>") count as absent, so a fresh clone warns
# instead of logging in with a literal placeholder.
_sf_connect_info() {
	[ -f "$SF_CONNECT_INFO" ] || return 0
	local value
	value="$(sed -n "s/^[[:space:]]*$1:[[:space:]]*//p" "$SF_CONNECT_INFO" | tr -d '\r' | tail -n 1)"
	case "$value" in
		''|'<'*'>') return 0 ;;
	esac
	printf '%s' "$value"
}

# The environment always wins over connect.info; "host:" is an alias of "IP:".
SF_HOST="${SF_HOST:-$(_sf_connect_info IP)}"
SF_HOST="${SF_HOST:-$(_sf_connect_info host)}"
SF_USER="${SF_USER:-$(_sf_connect_info user)}"
SF_USER="${SF_USER:-defaultuser}"
SF_PASSWORD="${SF_PASSWORD:-$(_sf_connect_info password)}"
SF_TIMEOUT="${SF_TIMEOUT:-300}"

# No built-in device address: remote calls refuse to run until one is configured.
SF_SETUP_HINT="run 'dotnet build -f net11.0-sailfish -t:SailfishSetup' in your app (or tools/sf setup from a checkout), or set SF_HOST"
sf_require_device() {
	[ -n "$SF_HOST" ] && return 0
	printf 'ERROR: no Sailfish device configured - %s.\n' "$SF_SETUP_HINT" >&2
	printf '       (looked for a connect.info: %s)\n' "$SF_CONNECT_INFO" >&2
	exit 1
}

# A visible placeholder instead of an empty password, so a missing one fails loudly.
if [ -z "$SF_PASSWORD" ]; then
	SF_PASSWORD='<sshPassword>'
	[ -n "$SF_HOST" ] && printf 'sf-lib: WARNING: no device password - installs need the developer-mode password (devel-su); %s, or add "password: ..." to %s\n' \
		"$SF_SETUP_HINT" "$SF_CONNECT_INFO" >&2
fi

# Installed layout: /usr/share/$SF_PKG/$SF_BIN, launcher symlink /usr/bin/$SF_PKG.
# Explicit SF_PKG/SF_BIN win; otherwise both derive from the project directory name.
SF_PKG_EXPLICIT="${SF_PKG:-}"
SF_BIN_EXPLICIT="${SF_BIN:-}"

SF_SAMPLE_DIR="${SF_SAMPLE_DIR:-$SF_REPO_ROOT/samples/Linux.SailfishOS.Sample}"
# Absolute-ize: sf deploy cds into SF_SAMPLE_DIR and derived paths are relative
# to it, so a relative value would resolve twice.
case "$SF_SAMPLE_DIR" in
	/*) ;;
	*)
		SF_SAMPLE_DIR="$(cd "$SF_SAMPLE_DIR" 2>/dev/null && pwd)" || {
			echo "ERROR: SF_SAMPLE_DIR does not exist: $SF_SAMPLE_DIR" >&2
			exit 1
		}
		;;
esac
if [ -n "$SF_PKG_EXPLICIT" ]; then
	SF_PKG="$SF_PKG_EXPLICIT"
elif [ "$SF_SAMPLE_DIR" != "$SF_REPO_ROOT/samples/Linux.SailfishOS.Sample" ]; then
	SF_PKG="harbour-$(basename "$SF_SAMPLE_DIR" | tr '[:upper:]' '[:lower:]')"
else
	SF_PKG="harbour-sample"
fi
# Debug gets its own package id so it can coexist with the Release install.
if [ -z "$SF_PKG_EXPLICIT" ] && [ "${SF_CONFIGURATION:-Release}" = "Debug" ]; then
	SF_PKG="$SF_PKG-debug"
fi
if [ -n "$SF_BIN_EXPLICIT" ]; then
	SF_BIN="$SF_BIN_EXPLICIT"
elif [ "$SF_SAMPLE_DIR" != "$SF_REPO_ROOT/samples/Linux.SailfishOS.Sample" ]; then
	SF_BIN="$(basename "$SF_SAMPLE_DIR")"
else
	SF_BIN="Linux.SailfishOS.Sample"
fi
SF_RID="${SF_RID:-linux-arm64}"
SF_CONFIGURATION="${SF_CONFIGURATION:-Release}"
# net11.0-sailfish when the project targets it, else the bare net11.0 of the old sample.
if [ -z "${SF_TFM:-}" ]; then
	SF_TFM=net11.0
	grep -qs "net11.0-sailfish" "$SF_SAMPLE_DIR"/*.csproj && SF_TFM=net11.0-sailfish
fi
SF_PUBLISH_DIR="${SF_PUBLISH_DIR:-$SF_SAMPLE_DIR/bin/$SF_CONFIGURATION/$SF_TFM/$SF_RID/publish}"
SF_RPM_DIR="${SF_RPM_DIR:-$SF_SAMPLE_DIR/bin/SailfishRpm}"
SF_RPM_ARCH="${SF_RPM_ARCH:-aarch64}"

# Managed payload profile (docs/aot-and-trimming.md): jit, trim or trimr2r (default).
# SF_PROFILE_PROPS is expanded unquoted into the dotnet publish command line.
SF_PROFILE="${SF_PROFILE:-trimr2r}"
SF_PROFILE_PROPS=""

# Host keys: trust-on-first-use with pinning in a private known_hosts, so a changed
# key fails loudly (un-pin with tools/sf forget-device). accept-new needs OpenSSH >= 7.6.
# A checkout uses tools/.known_hosts; the NuGet package (read-only cache) uses a per-user file.
if [ -z "${SF_KNOWN_HOSTS:-}" ]; then
	if [ -d "$SF_REPO_ROOT/.git" ]; then
		SF_KNOWN_HOSTS="$SF_REPO_ROOT/tools/.known_hosts"
	else
		mkdir -p "$SF_CONFIG_DIR" 2>/dev/null || true
		SF_KNOWN_HOSTS="$SF_CONFIG_DIR/known_hosts"
	fi
fi
case "$SF_KNOWN_HOSTS" in
	*[[:space:]]*)
		# $SF_SSH_OPTS is expanded unquoted (also inside expect), so whitespace would word-split.
		printf 'sf-lib: WARNING: SF_KNOWN_HOSTS contains whitespace ("%s"); falling back to $HOME/.sailfish_known_hosts\n' \
			"$SF_KNOWN_HOSTS" >&2
		SF_KNOWN_HOSTS="$HOME/.sailfish_known_hosts"
		;;
esac

# ServerAlive drops a session whose phone went away (Wi-Fi sleep, reboot) instead of waiting forever.
SF_SSH_OPTS="-o StrictHostKeyChecking=accept-new -o UserKnownHostsFile=$SF_KNOWN_HOSTS -o LogLevel=ERROR -o ConnectTimeout=15 -o ServerAliveInterval=5 -o ServerAliveCountMax=6"

export SF_HOST SF_USER SF_PASSWORD SF_KNOWN_HOSTS

# ----------------------------------------------------------------- output ----
sf_info()  { printf '==> %s\n' "$*"; }
sf_note()  { printf '    %s\n' "$*"; }
sf_ok()    { printf '    OK   %s\n' "$*"; }
sf_fail()  { printf '    FAIL %s\n' "$*" >&2; }
sf_error() { printf 'ERROR: %s\n' "$*" >&2; }

# Abort with a message if a step fails: sf_check "what failed" || exit 1
sf_die() { sf_error "$*"; exit 1; }

# sf_set_profile <jit|trim|trimr2r> - select the payload profile and recompute
# SF_PROFILE_PROPS; rejects typos so they cannot ship the untrimmed payload.
sf_set_profile() {
	case "${1:-}" in
		jit)     SF_PROFILE=jit;     SF_PROFILE_PROPS="-p:SailfishTrim=false -p:SailfishReadyToRun=false" ;;
		trim)    SF_PROFILE=trim;    SF_PROFILE_PROPS="-p:SailfishTrim=true -p:SailfishReadyToRun=false" ;;
		trimr2r) SF_PROFILE=trimr2r; SF_PROFILE_PROPS="-p:SailfishTrim=true -p:SailfishReadyToRun=true" ;;
		*)       sf_die "unknown payload profile: '${1:-}' (expected jit, trim or trimr2r; see docs/aot-and-trimming.md)" ;;
	esac
}
sf_set_profile "$SF_PROFILE"

# ------------------------------------------------------------- connection ----
# base64 keeps $ ; & | quotes and newlines intact through local shell, Tcl and remote shell.
_sf_b64() { printf '%s\n' "$*" | base64 | tr -d '\n'; }

# _sf_mtime <path> - mtime in epoch seconds (GNU or BSD stat), 0 when unavailable.
_sf_mtime() {
	stat -c %Y "$1" 2>/dev/null || stat -f %m "$1" 2>/dev/null || echo 0
}

# ------------------------------------------------------- host tooling --------
# _sf_sha256 <file> - sha256 hex via sha256sum, shasum or python3.
_sf_sha256() {
	if command -v sha256sum >/dev/null 2>&1; then
		sha256sum "$1" | awk '{print $1}'
	elif command -v shasum >/dev/null 2>&1; then
		shasum -a 256 "$1" | awk '{print $1}'
	elif command -v python3 >/dev/null 2>&1; then
		python3 -c 'import hashlib,sys;print(hashlib.file_digest(sys.stdin.buffer,"sha256").hexdigest())' < "$1"
	else
		return 1
	fi
}

# sf_use_rpm_shims - put pure-Python rpmbuild/rpm shims (sf-rpmbuild.py, sf-rpmquery.py)
# on PATH for whichever tool is missing; real binaries always win.
# A checkout keeps them in tools/.cache; the NuGet package (a read-only cache) in the per-user cache.
sf_use_rpm_shims() {
	local bin_dir="${XDG_CACHE_HOME:-$HOME/.cache}/maui-sailfish/bin" tool script
	[ -d "$SF_REPO_ROOT/.git" ] && bin_dir="$SF_REPO_ROOT/tools/.cache/bin"
	for tool in rpmbuild rpm; do
		command -v "$tool" >/dev/null 2>&1 && continue
		case "$tool" in
			rpmbuild) script="$_SF_TOOLS_DIR/py/sf-rpmbuild.py" ;;
			rpm)      script="$_SF_TOOLS_DIR/py/sf-rpmquery.py" ;;
		esac
		[ -f "$script" ] || return 1
		mkdir -p "$bin_dir" || return 1
		printf '#!/bin/sh\n# generated by tools/lib/sf-lib.sh - %s fallback\nexec python3 "%s" "$@"\n' \
			"$tool" "$script" > "$bin_dir/$tool" || return 1
		chmod +x "$bin_dir/$tool" || return 1
		PATH="$bin_dir:$PATH"
		export PATH
		sf_note "$tool not installed - using $(basename "$script") via $bin_dir/$tool"
	done
	return 0
}

# sf_publish_rpm <release> - dotnet publish of the app ($SF_SAMPLE_DIR) into a harbour RPM with the payload
# profile and SF_PUBLISH_PROPS; wipes the previous RPM staging first. Sets SF_BUILT_RPM.
sf_publish_rpm() {
	rm -rf "$SF_SAMPLE_DIR/obj/SailfishRpm" "$SF_RPM_DIR"
	# SC2086: SF_PROFILE_PROPS / SF_PUBLISH_PROPS are deliberate word-split lists of -p: arguments.
	# shellcheck disable=SC2086
	(cd "$SF_SAMPLE_DIR" && dotnet publish \
		-c "$SF_CONFIGURATION" -f "$SF_TFM" -r "$SF_RID" \
		-p:SelfContained=true -p:CreateSailfishRpm=true \
		-p:SailfishRelease="$1" -p:SailfishPackageName="$SF_PKG" \
		$SF_PROFILE_PROPS ${SF_PUBLISH_PROPS:-} \
		--nologo -v minimal) \
		|| sf_die "dotnet publish failed (release $1)"
	SF_BUILT_RPM="$(ls -1 "$SF_RPM_DIR"/*-"$1"."$SF_RPM_ARCH".rpm 2>/dev/null | head -1)"
	[ -n "$SF_BUILT_RPM" ] || sf_die "publish produced no RPM for release $1 in $SF_RPM_DIR"
}

# sf_upload_rpm <rpm> - copy it to the phone's home and check its sha256 there. Sets SF_REMOTE_RPM.
sf_upload_rpm() {
	local sha dev_sha
	SF_REMOTE_RPM="/home/$SF_USER/$(basename "$1")"
	sf_scp_to "$1" "$SF_REMOTE_RPM" || sf_die "scp of $(basename "$1") failed"
	sha="$(_sf_sha256 "$1")"
	dev_sha="$(sf_ssh_out "sha256sum '$SF_REMOTE_RPM' 2>/dev/null" | awk '{print $1}' | tail -1)" || true
	[ "$dev_sha" = "$sha" ] || sf_die "uploaded RPM is corrupt: device ${dev_sha:-<missing>} != local $sha"
	sf_ok "upload verified (sha256 matches)"
}

# Password transport, picked once at source time:
#   expect   answers the tty prompt
#   askpass  OpenSSH >= 8.4 SSH_ASKPASS_REQUIRE=force; no extra package or sudo needed
#   key      no password configured: plain ssh/scp in BatchMode
SF_HAVE_EXPECT=0
command -v expect >/dev/null 2>&1 && SF_HAVE_EXPECT=1

_SF_ASKPASS_DIR=""
_sf_askpass_cleanup() {
	if [ -n "$_SF_ASKPASS_DIR" ]; then
		rm -rf "$_SF_ASKPASS_DIR"
		_SF_ASKPASS_DIR=""
	fi
}

# _sf_install_traps - chain our cleanup onto an existing EXIT trap instead of replacing it.
_sf_install_traps() {
	local prev
	prev="$(trap -p EXIT)"
	prev="${prev#trap -- }"
	prev="${prev%\'}"
	prev="${prev#\'}"
	if [ -n "$prev" ]; then
		trap "_sf_askpass_cleanup; $prev" EXIT
	else
		trap '_sf_askpass_cleanup' EXIT
	fi
	trap '_sf_askpass_cleanup; trap - INT; kill -INT $$' INT
	trap '_sf_askpass_cleanup; trap - TERM; kill -TERM $$' TERM
}

# _sf_askpass - path to a 0700 helper printing $SF_PASSWORD, removed on EXIT/INT/TERM.
# The password reaches it only via the child's environment, never argv or disk.
_sf_askpass() {
	case "$SF_PASSWORD" in ''|'<sshPassword>') return 1 ;; esac
	if [ -z "$_SF_ASKPASS_DIR" ]; then
		# drop helpers left by killed runs (60 min keeps any live run safe)
		find "${TMPDIR:-/tmp}" -maxdepth 1 -name 'sf-askpass.*' -type d \
			-mmin +60 -exec rm -rf {} + 2>/dev/null || true
		_SF_ASKPASS_DIR="$(mktemp -d "${TMPDIR:-/tmp}/sf-askpass.XXXXXX")" || return 1
		chmod 700 "$_SF_ASKPASS_DIR" 2>/dev/null || true
		printf '#!/bin/sh\n# generated by tools/lib/sf-lib.sh - answers the ssh password prompt\nexec printf %%s "$SF_ASKPASS_PW"\n' \
			> "$_SF_ASKPASS_DIR/askpass.sh" \
			|| { rm -rf "$_SF_ASKPASS_DIR"; _SF_ASKPASS_DIR=""; return 1; }
		chmod 700 "$_SF_ASKPASS_DIR/askpass.sh" 2>/dev/null || true
		_sf_install_traps
	fi
	printf '%s' "$_SF_ASKPASS_DIR/askpass.sh"
}

# _sf_askpass_run <ssh|scp> <args...> - run ssh/scp with the askpass helper.
_sf_askpass_run() {
	local tool="$1"; shift
	local ap rc=0
	ap="$(_sf_askpass)" || return 127
	if command -v setsid >/dev/null 2>&1; then
		# no controlling tty -> ssh must use SSH_ASKPASS (works on any OpenSSH)
		SSH_ASKPASS="$ap" SSH_ASKPASS_REQUIRE=force SF_ASKPASS_PW="$SF_PASSWORD" \
			setsid -w "$tool" $SF_SSH_OPTS "$@" || rc=$?
	else
		SSH_ASKPASS="$ap" SSH_ASKPASS_REQUIRE=force SF_ASKPASS_PW="$SF_PASSWORD" \
			DISPLAY="${DISPLAY:-:0}" "$tool" $SF_SSH_OPTS "$@" || rc=$?
	fi
	return "$rc"
}

SF_TRANSPORT="expect"
if [ "$SF_HAVE_EXPECT" != 1 ]; then
	case "$SF_PASSWORD" in
		''|'<sshPassword>') SF_TRANSPORT="key" ;;
		*) _sf_askpass >/dev/null 2>&1 && SF_TRANSPORT="askpass" || SF_TRANSPORT="none" ;;
	esac
fi

# _sf_require_transport <what> - fail when a password is set but nothing can answer the prompt.
_sf_require_transport() {
	[ "$SF_TRANSPORT" != none ] && return 0
	sf_die "$1: a device password is configured but no transport can answer the ssh prompt (no 'expect', no working SSH_ASKPASS helper, OpenSSH < 8.4?). Install expect (macOS: brew install expect; Arch: pacman -S expect; Debian/Ubuntu: apt install expect; Fedora: dnf install expect), OR set up key auth (ssh-copy-id $SF_USER@$SF_HOST) and blank the password in $SF_CONNECT_INFO"
}

# ---------------------------------------------------------- multiplexing ----
# Opt-in (SF_SSH_MUX=1): one background master connection per phone (key auth only). Every later ssh/scp
# of this run, and of the scripts it starts, rides it: a call costs ~0.18 s instead of ~0.5 s. Off by
# default because on the Jolla phone about one long session in 15 died with rc 255 over the master
# (2026-09-30: 3 of ~42 `sf run`s, 0 of 12 without it); the cause is not found yet. The master is
# started with its stdio on /dev/null, because a ControlPersist master forked from a client inherits
# that client's stdout and keeps a $(...) capture open forever.
_SF_MUX_TRIED=0
_SF_MUX_UP=0
_SF_MUX_SOCK=""

# _sf_mux_dropped <rc> - report an ssh/scp that failed over the shared connection. rc 255 is a connection
# error or a remote command killed by a signal. The master is left alone: other sessions of this run may
# still use it, and a dead link ends the master by itself (ServerAlive, 30 s).
_sf_mux_dropped() {
	[ "$1" = 255 ] && [ "$_SF_MUX_UP" = 1 ] || return 0
	sf_error "ssh to $SF_USER@$SF_HOST over the shared connection failed (rc 255: connection error, or the remote command was killed; master log: $_SF_MUX_SOCK.log)"
}
_sf_mux() {
	[ "$_SF_MUX_TRIED" = 1 ] && return 0
	_SF_MUX_TRIED=1
	[ "${SF_SSH_MUX:-0}" = 1 ] || return 0
	local dir="${SF_MUX_DIR:-$HOME/.ssh}" sock
	sock="$dir/sf-mux-$SF_USER@$SF_HOST"
	case "$sock" in *[[:space:]]*) return 0 ;; esac   # SF_SSH_OPTS is word-split
	[ -d "$dir" ] || { mkdir -p "$dir" && chmod 700 "$dir"; } || return 0
	if ! ssh -o ControlPath="$sock" -O check "$SF_USER@$SF_HOST" >/dev/null 2>&1; then
		rm -f "$sock"
		ssh -o BatchMode=yes $SF_SSH_OPTS -o ControlMaster=yes -o ControlPath="$sock" -o ControlPersist=300 \
			-E "$sock.log" -f -N "$SF_USER@$SF_HOST" </dev/null >/dev/null 2>&1 || return 0
	fi
	_SF_MUX_SOCK="$sock"
	SF_SSH_OPTS="$SF_SSH_OPTS -o ControlMaster=no -o ControlPath=$sock"
	_SF_MUX_UP=1   # key auth is proven: plain ssh/scp, no expect start-up per call
}

# The remote command travels on ssh's stdin, so only this fixed runner appears in
# argv/ps/logs (the devel-su password stays hidden). ssh reads the login password from
# the tty, so expect still works. The script is slurped first and eval'd with stdin
# from /dev/null so no inner command can swallow the rest.
_SF_REMOTE_RUNNER='__sf_script=$(cat); eval "$__sf_script" </dev/null'

_sf_spawn_ssh() { # <remote-command-string>
	sf_require_device
	_sf_mux
	_sf_require_transport ssh
	local script rc=0
	script="$(mktemp "${TMPDIR:-/tmp}/sf-cmd.XXXXXX")" || sf_die "mktemp failed"
	chmod 600 "$script"
	printf '%s\n' "$1" > "$script"
	local transport="$SF_TRANSPORT"
	[ "$_SF_MUX_UP" = 1 ] && transport=key
	if [ "$transport" = askpass ]; then
		_sf_askpass_run ssh "$SF_USER@$SF_HOST" "$_SF_REMOTE_RUNNER" < "$script" || rc=$?
	elif [ "$transport" = key ]; then
		ssh -o BatchMode=yes $SF_SSH_OPTS "$SF_USER@$SF_HOST" "$_SF_REMOTE_RUNNER" < "$script" || rc=$?
		if [ "$rc" = 255 ] && [ "$_SF_MUX_UP" = 1 ]; then
			_sf_mux_dropped "$rc"
		elif [ "$rc" = 255 ]; then
			sf_error "ssh to $SF_USER@$SF_HOST failed with no password prompt (BatchMode) - set up key auth: ssh-copy-id $SF_USER@$SF_HOST - or install expect"
		fi
	else
		# The command rides the environment so Tcl never has to quote it.
		SF_EXPECT_CMD="exec ssh $SF_SSH_OPTS $SF_USER@$SF_HOST '$_SF_REMOTE_RUNNER' < '$script'" \
		expect <<EOF || rc=$?
set timeout $SF_TIMEOUT
log_user 1
spawn sh -c \$env(SF_EXPECT_CMD)
expect {
	-re {@[^\r\n]*[Pp]assword:} { send -- "\$env(SF_PASSWORD)\r"; exp_continue }
	timeout { puts stderr "sf-lib: TIMEOUT after $SF_TIMEOUT s waiting for ssh"; exit 124 }
	eof
}
catch wait result
exit [lindex \$result 3]
EOF
	fi
	rm -f "$script"
	return "$rc"
}

_sf_spawn_scp() { # <source-spec> <dest-spec>
	sf_require_device
	_sf_mux
	_sf_require_transport scp
	local rc=0 transport="$SF_TRANSPORT"
	[ "$_SF_MUX_UP" = 1 ] && transport=key
	if [ "$transport" = askpass ]; then
		_sf_askpass_run scp "$1" "$2" || rc=$?
		return "$rc"
	elif [ "$transport" = key ]; then
		scp -o BatchMode=yes $SF_SSH_OPTS "$1" "$2" || rc=$?
		if [ "$rc" = 255 ] && [ "$_SF_MUX_UP" = 1 ]; then
			_sf_mux_dropped "$rc"
		elif [ "$rc" = 255 ]; then
			sf_error "scp $1 -> $2 failed with no password prompt (BatchMode) - set up key auth or install expect"
		fi
		return "$rc"
	fi
	expect <<EOF
set timeout $SF_TIMEOUT
log_user 1
set src {$1}
set dst {$2}
spawn scp $SF_SSH_OPTS \$src \$dst
expect {
	-re {@[^\r\n]*[Pp]assword:} { send -- "\$env(SF_PASSWORD)\r"; exp_continue }
	timeout { puts stderr "sf-lib: TIMEOUT after $SF_TIMEOUT s waiting for scp"; exit 124 }
	eof
}
catch wait result
exit [lindex \$result 3]
EOF
}

# sf_ssh <command...> - run on the device as $SF_USER, return the remote exit status.
sf_ssh() { _sf_spawn_ssh "$*"; }

# sf_ssh_out <command...> - like sf_ssh but prints only the command's stdout (no
# expect banner/prompt), so it can be captured. Runs in a subshell so "exit N" reports N.
sf_ssh_out() {
	local raw rc
	raw="$(_sf_spawn_ssh "printf '%s\n' __SF_OUT_START__
(
$*
)
printf '\n__SF_OUT_RC=%s\n' \$?
printf '%s\n' __SF_OUT_END__")" || true

	# keep the marker-delimited region, minus markers and trailing blank lines
	printf '%s\n' "$raw" \
		| tr -d '\r' \
		| sed -n '/__SF_OUT_START__/,/__SF_OUT_END__/p' \
		| sed '1d;$d' \
		| sed '/^__SF_OUT_RC=[0-9]*$/d' \
		| awk '{ l[NR] = $0 } END { n = NR; while (n > 0 && l[n] == "") n--; for (i = 1; i <= n; i++) print l[i] }'

	rc="$(printf '%s\n' "$raw" | tr -d '\r' | sed -n 's/^__SF_OUT_RC=\([0-9]*\)$/\1/p' | tail -1)"
	return "${rc:-1}"
}

# sf_root / sf_root_out <command...> - run as root. devel-su -c does not split
# arguments, hence 'devel-su /bin/sh -c'; it reads the password from stdin.
# devel-su's own "Password:" prompt goes to /dev/null; the command's stderr rides fd 3 past it. fd 3 is
# closed for the command itself, or a process it leaves in the background would hold the ssh session open.
_sf_root_cmd() { sf_ssh "echo $(_sf_b64 "$SF_PASSWORD") | base64 -d | devel-su /bin/sh -c 'echo $(_sf_b64 "$*") | base64 -d | /bin/sh -s 2>&3 3>&-' 3>&2 2>/dev/null"; }
sf_root()     { _sf_root_cmd "$*"; }
sf_root_out() { sf_ssh_out "echo $(_sf_b64 "$SF_PASSWORD") | base64 -d | devel-su /bin/sh -c 'echo $(_sf_b64 "$*") | base64 -d | /bin/sh -s 2>&3 3>&-' 3>&2 2>/dev/null"; }

# sf_have_root_pw - true when a real device password is configured (devel-su can work).
sf_have_root_pw() {
	case "$SF_PASSWORD" in
		''|'<sshPassword>') return 1 ;;
		*)                  return 0 ;;
	esac
}

# sf_priv <command...> - via devel-su when a password is configured, else as the
# session user. Root-free works on Sailfish OS 5.2 because MCE answers dbus-send as
# defaultuser and PackageKit's polkit allows install-local/remove for an active session.
# Plain rpm write transactions (rpm -e, rpm -Uvh) have no root-free path; branch at the call site.
sf_priv() {
	if sf_have_root_pw; then sf_root "$*"; else sf_ssh "$*"; fi
}

# sf_scp_to <local-path> <remote-path> / sf_scp_from <remote-path> <local-path>
sf_scp_to()   { _sf_spawn_scp "$1" "$SF_USER@$SF_HOST:$2"; }
sf_scp_from() { _sf_spawn_scp "$SF_USER@$SF_HOST:$1" "$2"; }

# sf_ping - fail fast with a useful message when the device is unreachable.
sf_ping() {
	sf_ssh 'echo SF_DEVICE_OK' >/dev/null 2>&1 \
		|| sf_die "cannot reach $SF_USER@$SF_HOST over ssh (set SF_HOST/SF_USER/SF_PASSWORD via env or in $SF_CONNECT_INFO)"
}

# sf_ssh_key_ok [host] [user] - true when key login works; BatchMode fails instead of prompting.
sf_ssh_key_ok() {
	ssh -o BatchMode=yes $SF_SSH_OPTS "${2:-$SF_USER}@${1:-$SF_HOST}" true 2>/dev/null
}

# sf_push_helper <local-script> - upload a *-remote.sh helper to /tmp and chmod +x it.
# A helper that sources sf-remote-env.sh gets it next to it, in the same ssh round trip.
sf_push_helper() {
	local script="$1"
	local name env="" env_src="$_SF_TOOLS_DIR/remote/sf-remote-env.sh"
	name="$(basename "$script")"

	[ -f "$script" ] || sf_die "helper script not found: $script"
	if grep -q '^\. .*/sf-remote-env\.sh' "$script"; then
		[ -f "$env_src" ] || sf_die "helper script not found: $env_src"
		env="echo $(_sf_b64 "$(cat "$env_src")") | base64 -d > /tmp/sf-remote-env.sh && "
	fi
	sf_scp_to "$script" "/tmp/$name" || sf_die "could not upload $name to the device"
	sf_ssh "${env}chmod +x /tmp/$name" >/dev/null || sf_die "could not chmod /tmp/$name"
}

# sf_push_kill_helper - upload sf-kill-remote.sh, once per run.
_SF_KILL_PUSHED=0
sf_push_kill_helper() {
	[ "$_SF_KILL_PUSHED" = 1 ] && return 0
	sf_push_helper "$_SF_TOOLS_DIR/remote/sf-kill-remote.sh"
	_SF_KILL_PUSHED=1
}

# sf_kill_app [mode] - stop every running instance of the app ("--list" only lists them).
# In a pipe (a subshell) call sf_push_kill_helper first, so a failed upload still aborts.
sf_kill_app() {
	sf_push_kill_helper
	sf_ssh "/tmp/sf-kill-remote.sh $SF_PKG $SF_BIN${1:+ $1}"
}

# sf_screenshot <local-png> - compositor screenshot of the phone into <local-png>; the remote helper is
# uploaded once per run, so a series of shots (sf shots) costs one ssh + one scp each.
_SF_SHOT_PUSHED=0
sf_screenshot() {
	if [ "$_SF_SHOT_PUSHED" != 1 ]; then
		sf_push_helper "$_SF_TOOLS_DIR/remote/sf-screenshot-remote.sh"
		_SF_SHOT_PUSHED=1
	fi
	sf_ssh "SF_SU_PASS=\"\$(echo $(_sf_b64 "$SF_PASSWORD") | base64 -d)\" /tmp/sf-screenshot-remote.sh" \
		| grep -E 'SCREENSHOT_OK|SCREENSHOT_FAIL|BUS=|using |Error|error' || true
	sf_scp_from /tmp/sf-screen.png "$1"
}

# ---------------------------------------------------------------- display ----
_SF_MCE="dbus-send --system --dest=com.nokia.mce /com/nokia/mce/request"

# sf_keep_awake - pause display blanking (a pause lasts 60 s); never fails.
sf_keep_awake() {
	sf_priv "$_SF_MCE com.nokia.mce.request.req_display_blanking_pause" >/dev/null 2>&1 || true
}

# sf_device_ready - wake the display; true only when it is on and unlocked.
# Waking is allowed programmatically; unlocking the tklock is not.
sf_device_ready() {
	local lock disp
	sf_priv "$_SF_MCE com.nokia.mce.request.req_display_state_on" >/dev/null 2>&1 || true
	sf_keep_awake
	lock="$(sf_priv "$_SF_MCE --print-reply com.nokia.mce.request.get_tklock_mode 2>/dev/null | tail -1" 2>/dev/null || true)"
	disp="$(sf_priv "$_SF_MCE --print-reply com.nokia.mce.request.get_display_status 2>/dev/null | tail -1" 2>/dev/null || true)"
	case "$lock" in *unlocked*) ;; *) return 1 ;; esac
	case "$disp" in *'"on"'*) ;; *) return 1 ;; esac
	return 0
}
