#!/bin/bash
# Interactive one-time device setup: asks for the phone's address, SSH user and
# developer-mode password, verifies them (SSH, key install, devel-su) and only then
# saves them to connect.info (mode 0600). A wrong answer is asked again, never saved.
#
# Usage: tools/sf-setup.sh [--if-needed] [--force]
#   --if-needed   do nothing when the configured phone answers (SailfishRun pre-flight)
#   --force       ask again even when a device is configured and answers
#
# Writes $SF_CONNECT_INFO, else ${XDG_CONFIG_HOME:-~/.config}/maui-sailfish/connect.info.
# From an app: DOTNET_CLI_USE_MSBUILD_SERVER=0 dotnet build -f net11.0-sailfish -t:SailfishSetup
# (the MSBuild server has no terminal; without one this only prints what to do).
# The password is stored because devel-su rpm needs it; leave it empty to install via
# PackageKit instead (which the phone may refuse over SSH).

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
IF_NEEDED=0
FORCE=0
while [ $# -gt 0 ]; do
	case "$1" in
		--if-needed) IF_NEEDED=1 ;;
		--force)     FORCE=1 ;;
		-h|--help)   sed -n '2,14p' "$0"; exit 0 ;;
		*)           echo "sf-setup: unknown argument: $1" >&2; exit 2 ;;
	esac
	shift
done

# shellcheck source=sf-lib.sh
. "$SCRIPT_DIR/sf-lib.sh" 2>/dev/null
TARGET="$SF_CONNECT_INFO"

# probe <host> <user> -> "ok" (key login works) | "auth" (SSH answers, key not
# accepted yet) | "down: <reason>". BatchMode never prompts or hangs.
probe() {
	local out
	# shellcheck disable=SC2086  # SF_SSH_OPTS is intentionally word-split
	out="$(ssh -o BatchMode=yes -o ConnectTimeout=6 $SF_SSH_OPTS "$2@$1" true 2>&1)" && { echo ok; return; }
	case "$out" in
		*"Permission denied"*|*publickey*|*"Too many authentication"*) echo auth ;;
		*) echo "down: $(printf '%s' "$out" | tr '\n' ' ' | sed 's/  */ /g' | cut -c1-140)" ;;
	esac
}

# ------------------------------------------------ already configured? ------
REASON=""
if [ -n "$SF_HOST" ] && [ "$FORCE" != 1 ]; then
	STATE="$(probe "$SF_HOST" "$SF_USER")"
	case "$STATE" in
		ok|auth)
			if [ "$IF_NEEDED" = 1 ]; then exit 0; fi
			sf_info "a device is already configured and answers: $SF_USER@$SF_HOST ($SF_CONNECT_INFO)"
			sf_note "pass --force (SailfishSetupForce=true) to change it"
			exit 0
			;;
	esac
	REASON="The configured phone $SF_USER@$SF_HOST ($SF_CONNECT_INFO) does not answer: ${STATE#down: }"
fi

if ! { exec 3</dev/tty 4>/dev/tty; } 2>/dev/null; then
	# Typical under `dotnet build`: the MSBuild server has no terminal.
	{
		if [ -n "$REASON" ]; then
			printf 'ERROR: %s\n' "$REASON"
			printf 'Check that the phone is on and on this network, or change the settings, from a terminal:\n'
			printf '    bash "%s/sf-setup.sh" --force\n' "$SCRIPT_DIR"
		else
			printf 'ERROR: no Sailfish device configured, and this build has no terminal to ask in\n'
			printf '(dotnet build runs targets in the background MSBuild server). Set the phone up\n'
			printf 'once, from a terminal:\n'
			printf '    bash "%s/sf-setup.sh"\n' "$SCRIPT_DIR"
		fi
		printf '(or: DOTNET_CLI_USE_MSBUILD_SERVER=0 dotnet build -f net11.0-sailfish -t:SailfishSetup%s)\n' \
			"$([ -n "$REASON" ] && printf ' -p:SailfishSetupForce=true')"
		printf 'or write %s (mode 600):\n' "$TARGET"
		printf '    IP: <the phone'"'"'s address — Settings > Developer tools shows it>\n'
		printf '    user: defaultuser\n'
		printf '    password: <Settings > Developer tools > Remote connection password>\n'
	} >&2
	exit 1
fi

say() { printf '%s\n' "$*" >&4; }
ask() { # <prompt> <default> -> REPLY
	local prompt="$1" def="${2:-}"
	if [ -n "$def" ]; then printf '%s [%s]: ' "$prompt" "$def" >&4; else printf '%s: ' "$prompt" >&4; fi
	IFS= read -r REPLY <&3 || { say ""; say "(input closed - nothing changed)"; exit 1; }
	REPLY="$(printf '%s' "$REPLY" | tr -d '[:space:]')"
	[ -n "$REPLY" ] || REPLY="$def"
}
ask_secret() { # <prompt> -> REPLY
	printf '%s: ' "$1" >&4
	stty -echo <&3 2>/dev/null || true
	IFS= read -r REPLY <&3 || REPLY=""
	stty echo <&3 2>/dev/null || true
	say ""
}
yes_no() { # <prompt> (default yes) -> 0 yes / 1 no
	ask "$1 [Y/n]" ""
	case "$REPLY" in [nN]*) return 1 ;; *) return 0 ;; esac
}
# A dotted IPv4 must have four 0-255 parts; otherwise a host name.
valid_host() {
	local h="$1"
	case "$h" in
		''|*[!A-Za-z0-9.-]*|.*|*.|*..*) return 1 ;;
	esac
	if printf '%s' "$h" | grep -Eq '^[0-9.]+$'; then
		printf '%s' "$h" | grep -Eq '^([0-9]{1,3}\.){3}[0-9]{1,3}$' || return 1
		local IFS=.
		# shellcheck disable=SC2086
		set -- $h
		for part in "$@"; do [ "$part" -le 255 ] || return 1; done
	fi
	return 0
}

say ""
[ -n "$REASON" ] && { say "$REASON"; say "Let's fix it (Ctrl+C to give up)."; say ""; }
say "Sailfish OS device setup"
say "  On the phone: Settings > Developer tools > Developer mode ON, set a password"
say "  under 'Remote connection', and have the phone on this computer's network"
say "  (same Wi-Fi, or USB: the phone is 192.168.2.15 then)."
say ""

HOST_DEF="${SF_HOST:-}"
USER_DEF="${SF_USER:-defaultuser}"
KEY="${SF_SSH_KEY:-$HOME/.ssh/id_ed25519}"

# ---------------------------------------------- 1. an address that answers ---
while :; do
	ask "Phone IP address or host name" "$HOST_DEF"
	HOST="$REPLY"
	if ! valid_host "$HOST"; then
		say "  '$HOST' is not an IP address or host name (e.g. 192.168.1.20) - try again."
		continue
	fi
	ask "SSH user" "$USER_DEF"
	USER_NAME="$REPLY"
	say "  checking $USER_NAME@$HOST ..."
	STATE="$(probe "$HOST" "$USER_NAME")"
	case "$STATE" in
		ok|auth) break ;;
	esac
	say "  no SSH answer from $HOST: ${STATE#down: }"
	say "  (phone on and unlocked? same network? Developer mode + Remote connection on?)"
	HOST_DEF="$HOST"
	USER_DEF="$USER_NAME"
	yes_no "  Try again?" || { say "Nothing changed."; exit 1; }
done

# ---------------------------------- 2. a password that logs in (or none) ----
while :; do
	ask_secret "Developer-mode password (empty = do not store it)"
	PASS="$REPLY"
	export SF_HOST="$HOST" SF_USER="$USER_NAME" SF_PASSWORD="$PASS"
	# re-source so the transport matches this password (env wins in sf-lib)
	# shellcheck source=sf-lib.sh
	. "$SCRIPT_DIR/sf-lib.sh" 2>/dev/null
	if [ "$STATE" = ok ]; then
		say "  OK   key login to $USER_NAME@$HOST already works"
	else
		[ -f "$KEY" ] || { ssh-keygen -t ed25519 -f "$KEY" -N '' -C 'maui-sailfish' >/dev/null; say "  created $KEY"; }
		if [ -n "$PASS" ]; then
			# The password is known: install the public key with it (no second prompt).
			PUB="$(cat "$KEY.pub")"
			if ! sf_ssh "umask 077; mkdir -p ~/.ssh; touch ~/.ssh/authorized_keys; grep -qxF '$PUB' ~/.ssh/authorized_keys || printf '%s\n' '$PUB' >> ~/.ssh/authorized_keys" >/dev/null 2>&1; then
				say "  that password did not log in to $USER_NAME@$HOST - try again."
				continue
			fi
		else
			say "  ssh-copy-id will now ask for the developer-mode password once:"
			if ! SF_CONNECT_INFO=/dev/null bash "$SCRIPT_DIR/sf-pair.sh" <&3 >&4 2>&4; then
				yes_no "  Pairing failed. Try again?" && continue
				say "Nothing changed."
				exit 1
			fi
		fi
		[ "$(probe "$HOST" "$USER_NAME")" = ok ] || { say "  ERROR: the key was installed but key login still fails - nothing saved."; exit 1; }
		say "  OK   key login to $USER_NAME@$HOST works"
	fi
	if [ -n "$PASS" ]; then
		if [ "$(sf_root_out 'id -u' 2>/dev/null | tr -d '[:space:]' | tail -c 1)" = "0" ]; then
			say "  OK   devel-su works - installs use rpm as root"
		else
			say "  devel-su did not accept that password (it is the Remote connection password) - try again."
			continue
		fi
	fi
	break
done

# ------------------------------------------------------- 3. save ------------
umask 077
mkdir -p "$(dirname "$TARGET")"
{
	printf '# Sailfish OS device for the maui-sailfish tools (written by sf-setup.sh)\n'
	printf 'IP: %s\n' "$HOST"
	printf 'user: %s\n' "$USER_NAME"
	[ -n "$PASS" ] && printf 'password: %s\n' "$PASS"
} > "$TARGET.tmp"
chmod 600 "$TARGET.tmp"
mv -f "$TARGET.tmp" "$TARGET"
say "  saved $TARGET"

RELEASE="$(ssh -o BatchMode=yes $SF_SSH_OPTS "$USER_NAME@$HOST" "sed -n 's/^PRETTY_NAME=//p' /etc/os-release" 2>/dev/null | tr -d '"')"
say "  OK   device: ${RELEASE:-unknown OS}"
if [ -z "$PASS" ]; then
	say "  NOTE no password stored: installs go through PackageKit, which the phone"
	say "       may refuse over SSH ('Failed to obtain authentication'); run with --force to add it"
fi
say ""
say "Done. Deploy with: dotnet build -f net11.0-sailfish -t:SailfishRun"
