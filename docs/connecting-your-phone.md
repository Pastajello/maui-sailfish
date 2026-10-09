# Connecting your phone

Everything the build needs to reach the phone: developer mode, the first-run questions, where the settings live,
and what the error messages mean. The commands that use the connection are in [tools.md](tools.md).

## Setup

**On the phone** (once): Settings › Developer tools › **Developer mode** on,
and set a password under **Remote connection**. The phone must be reachable
from the build machine — same Wi-Fi (the address is shown on that settings
page) or USB networking (the phone is `192.168.2.15`).

**First run.** `dotnet run -f net11.0-sailfish`, `SailfishRun` (or the template's VS Code *Sailfish: F5 Deploy & Run*)
runs the `sailfish` tool's `setup`, which asks in the terminal and keeps asking until the answers work — nothing is
saved before that. The same questions come from `sailfish setup` (`dotnet tool install -g
Microsoft.Maui.Platforms.SailfishOS.Tools`) or `./tools/sf setup` in a checkout:

1. *Phone address* — an IP address or host name (`192.168.1.20`, `jolla.local`); a typo like `192.` is rejected on
   the spot. If SSH does not answer, you get the reason and *Try again?* with your previous answers as defaults.
2. *User* — `defaultuser` on current Sailfish OS.
3. *Remote connection password* — the developer-mode password. It installs your SSH key on the phone
   (`~/.ssh/id_ed25519`, created with `ssh-keygen` if missing) and is checked against `devel-su`; a wrong one is
   asked again. It is stored because installing the RPM needs root (`devel-su rpm -U`). Leave it empty and ssh asks
   for it once instead — installs then go through PackageKit, which the phone may refuse over SSH.

The build machine needs OpenSSH (`ssh`, `scp`, `ssh-keygen`): built into macOS and most Linux distributions, and on
Windows 10/11 the optional feature *OpenSSH Client* (Settings › System › Optional features). Nothing else: no bash,
`expect` or python3.

Then the settings are written to `~/.config/maui-sailfish/connect.info` (Windows: `%APPDATA%\maui-sailfish\connect.info`;
mode 600, `known_hosts` next to it) and the deploy continues.

**Before every deploy** the configured phone is probed (at most 6 s). If it
does not answer — phone off, other network, a changed address — a terminal
gets the setup again with the reason; a build without a terminal stops right
away and prints the commands to fix it.

**Change the phone:**

```bash
DOTNET_CLI_USE_MSBUILD_SERVER=0 dotnet build -f net11.0-sailfish -t:SailfishSetup -p:SailfishSetupForce=true
```

or edit/delete `~/.config/maui-sailfish/connect.info`:

```
IP: 192.168.1.20
user: defaultuser
password: <Remote connection password>
```

## Credentials

Device credentials live in `~/.config/maui-sailfish/connect.info`
(`IP: …`, `user: …`, `password: …`, mode 0600, written by `sailfish setup` or `tools/sf setup`; both read it)
or in the environment (`SF_HOST`, `SF_USER`, `SF_PASSWORD`). A `connect.info` in the
checkout or in an app's directory also works and is ignored by git
(`.gitignore`), but the per-user file keeps the password out of the tree.
The password is only used to install the SSH key once and for `devel-su`; every other login uses the key. The tools
send remote commands over ssh's stdin, and the password reaches ssh through the tool itself acting as `SSH_ASKPASS`
and `devel-su` on stdin, so it never appears in argv, `ps` or the deploy logs.

**Where the settings come from** (first hit wins): `$SF_HOST`/`$SF_USER`/
`$SF_PASSWORD`; the file named by `--connect-info` or `$SF_CONNECT_INFO`; `connect.info` in the
app's project directory (keep it out of git); `connect.info` at the root of a
maui-sailfish checkout (`tools/sf` only); `~/.config/maui-sailfish/connect.info`. A file whose
`IP:` is still a `<placeholder>` does not count. There is no built-in default
address.

**Why `DOTNET_CLI_USE_MSBUILD_SERVER=0`:** the .NET 11 CLI runs build targets
in a background MSBuild server that has no terminal, so the setup could not
ask anything (it asks on `/dev/tty`; on Windows a build never has a terminal to ask in, so run `sailfish setup` once).
The template's VS Code F5 configuration and tasks set the
variable; without it (or in CI) a missing or dead phone fails with the exact
`dotnet "…/sailfish.dll" setup` command to run instead.

## Error messages

| Message | Fix |
|---|---|
| `Setting up the phone needs a terminal: run … setup` | run the printed command (or `sailfish setup`) in a terminal, or the `SailfishSetup` command above |
| `The configured phone … does not answer` | phone on, unlocked, same network? else change it (`SailfishSetupForce=true`) |
| `no SSH answer from … Operation timed out` (during setup) | wrong address, or the phone is on another network / Remote connection is off |
| `that password did not log in` / `devel-su did not accept that password` | it is the password from Developer tools › Remote connection |
| `pkcon install-local failed … Failed to obtain authentication` | no password stored — add it (`SailfishSetupForce=true`) |
| `Host key verification failed` after reflashing the phone | remove its line from `~/.config/maui-sailfish/known_hosts` (a checkout: `tools/sf forget-device`) |
| `ssh is not installed or not on PATH` | install OpenSSH (Windows: the *OpenSSH Client* optional feature) |

