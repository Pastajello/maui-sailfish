# Connecting your phone

Everything the build needs to reach the phone: developer mode, the first-run questions, where the settings live,
and what the error messages mean. The commands that use the connection are in [tools.md](tools.md).

## Setup

**On the phone** (once): Settings › Developer tools › **Developer mode** on,
and set a password under **Remote connection**. The phone must be reachable
from the build machine — same Wi-Fi (the address is shown on that settings
page) or USB networking (the phone is `192.168.2.15`).

**First run.** `SailfishRun` (or the template's VS Code *Sailfish: F5 Deploy & Run*) asks
in the terminal, and keeps asking until the answers work — nothing is saved
before that:

1. *Phone IP address or host name* — must look like one (`192.168.1.20`,
   `jolla.local`); a typo like `192.` is rejected on the spot. SSH has to
   answer before the next question; if it does not, you get the reason and
   *Try again?* with your previous answers as defaults.
2. *SSH user* — `defaultuser` on current Sailfish OS.
3. *Developer-mode password* — the Remote connection password. It installs
   your SSH key on the phone (`~/.ssh/id_ed25519`, created if missing) and is
   checked against `devel-su`; a wrong one is asked again. It is stored
   because installing the RPM needs root (`devel-su rpm -U`). Leave it empty
   to type it into `ssh-copy-id` instead — installs then go through
   PackageKit, which the phone may refuse over SSH.

Then the settings are written to `~/.config/maui-sailfish/connect.info`
(mode 600, `known_hosts` next to it) and the deploy continues.

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
(`IP: …`, `user: …`, `password: …`, mode 0600, written by `tools/sf setup`)
or in the environment (`SF_HOST`, `SF_USER`, `SF_PASSWORD`). A `connect.info` in the
checkout or in an app's directory also works and is ignored by git
(`.gitignore`), but the per-user file keeps the password out of the tree.
Prefer key auth (`./tools/sf pair` / `ssh-copy-id`); the password is then
only needed for `devel-su`. The tools send remote commands over ssh's stdin,
so the password never appears in argv, `ps` or the deploy logs.

**Where the settings come from** (first hit wins): `$SF_HOST`/`$SF_USER`/
`$SF_PASSWORD`; the file named by `$SF_CONNECT_INFO`; `connect.info` in the
app's project directory (keep it out of git); `connect.info` at the root of a
maui-sailfish checkout; `~/.config/maui-sailfish/connect.info`. A file whose
`IP:` is still a `<placeholder>` does not count. There is no built-in default
address.

**Why `DOTNET_CLI_USE_MSBUILD_SERVER=0`:** the .NET 11 CLI runs build targets
in a background MSBuild server that has no terminal, so the setup could not
ask anything. The template's VS Code F5 configuration and tasks set the
variable; without it (or in CI) a missing or dead phone fails with the exact
`bash …/tools/sf setup` command to run instead.

## Error messages

| Message | Fix |
|---|---|
| `no Sailfish device configured, and this build has no terminal to ask in` | run the printed `bash …/sf-setup.sh`, or the `SailfishSetup` command above |
| `The configured phone … does not answer` | phone on, unlocked, same network? else change it (`SailfishSetupForce=true`) |
| `no SSH answer from … Operation timed out` (during setup) | wrong address, or the phone is on another network / Remote connection is off |
| `that password did not log in` / `devel-su did not accept that password` | it is the password from Developer tools › Remote connection |
| `pkcon install-local failed … Failed to obtain authentication` | no password stored — add it (`SailfishSetupForce=true`) |
| `Host key verification failed` after reflashing the phone | remove its line from `~/.config/maui-sailfish/known_hosts` (a checkout: `tools/sf forget-device`) |

