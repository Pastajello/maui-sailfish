#!/bin/bash
# One-time SSH key pairing with the Sailfish device; afterwards the tooling never
# asks for a password.
# Usage: ./tools/sf pair [--key PATH] [--force]
#
#   --key PATH   key to use (default: $SF_SSH_KEY, else ~/.ssh/id_ed25519)
#   --force      run ssh-copy-id even if key auth already works
#
# ssh-copy-id asks for the developer-mode password once in your terminal; this
# script never sees it. Needs Developer mode on with a "Remote connection" password.
# sf-lib.sh runs ssh without -i, so the key must be one ssh offers by default.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=../lib/sf-lib.sh
. "$SCRIPT_DIR/../lib/sf-lib.sh"

KEY="${SF_SSH_KEY:-$HOME/.ssh/id_ed25519}"
FORCE=0
while [ $# -gt 0 ]; do
	case "$1" in
		--key)   shift; KEY="${1:-}" ;;
		--force) FORCE=1 ;;
		-h|--help) sed -n '2,11p' "$0"; exit 0 ;;
		*)       sf_die "unknown argument: $1 (see --help)" ;;
	esac
	shift
done
[ -n "$KEY" ] || sf_die "--key requires a path"

# ------------------------------------------------------- 1. local key material --
sf_info "[1/4] local key"
if [ -f "$KEY" ] && [ -f "$KEY.pub" ]; then
	sf_ok "reusing $KEY (never overwriting an existing key)"
elif [ -f "$KEY" ]; then
	sf_die "$KEY exists but $KEY.pub does not - cannot pair with a private key alone"
elif [ -f "$KEY.pub" ]; then
	sf_die "$KEY.pub exists but the private key does not - refusing to guess; pass --key"
else
	sf_note "generating a dedicated ed25519 keypair"
	ssh-keygen -t ed25519 -f "$KEY" -N '' -C 'maui-sailfish' >/dev/null \
		|| sf_die "ssh-keygen failed for $KEY"
	sf_ok "created $KEY"
fi

# -------------------------------------------------------- 2. endpoint answers ----
sf_info "[2/4] endpoint"
probe_out="$(ssh -o BatchMode=yes $SF_SSH_OPTS -o ConnectTimeout=8 "$SF_USER@$SF_HOST" true 2>&1 || true)"
ALREADY=0
case "$probe_out" in
	'')
		ALREADY=1
		sf_ok "$SF_HOST answers and key auth already works"
		;;
	*"Permission denied"*|*"publickey"*)
		sf_ok "$SF_HOST answers SSH; auth pending, which is expected before pairing"
		;;
	*)
		sf_error "cannot reach $SF_USER@$SF_HOST over SSH"
		printf '%s\n' "$probe_out" | head -3 | sed 's/^/    /' >&2
		sf_die "check the cable/Wi-Fi, and that Developer tools > Remote connection is on"
		;;
esac

# ------------------------------------------------------ 3. install the pubkey ----
sf_info "[3/4] install the public key"
if [ "$ALREADY" = 1 ] && [ "$FORCE" != 1 ]; then
	sf_ok "nothing to do (pass --force to re-install anyway)"
else
	# ssh-copy-id reads the password from the controlling terminal, so without a tty
	# (VS Code task, CI) it would block forever; refuse early instead.
	if [ ! -t 0 ] || [ ! -t 1 ]; then
		sf_die "ssh-copy-id needs an interactive terminal to type the device password into; run ./tools/sf pair from a real terminal, not from a task or CI"
	fi
	sf_note "ssh-copy-id will ask for the device password ONCE, in this terminal"
	# shellcheck disable=SC2086  # SF_SSH_OPTS is intentionally word-split
	ssh-copy-id -i "$KEY.pub" $SF_SSH_OPTS "$SF_USER@$SF_HOST" \
		|| sf_die "ssh-copy-id failed (wrong password, or developer mode off?)"
fi

# --------------------------------------------------- 4. prove it is passwordless --
sf_info "[4/4] verify key-only auth"
if sf_ssh_key_ok; then
	sf_ok "passwordless SSH works for $SF_USER@$SF_HOST"
	sf_note "every tools/sf-*.sh now runs without prompting"
	sf_note "next: ./tools/sf detect   (confirms it really is a Sailfish device)"
else
	sf_die "the key was installed but BatchMode auth still fails - check PubkeyAuthentication in the device sshd_config, and that $KEY is a name ssh offers by default"
fi
