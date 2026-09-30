# .NET MAUI App (with Sailfish OS) template

`dotnet new maui-sailfish` creates the standard .NET MAUI app — XAML App/AppShell/
MainPage, `Resources/` (app icon, splash, fonts, images, styles) and
`Platforms/` for Android, iOS, Mac Catalyst and Windows — plus a Sailfish OS head
(`net11.0-sailfish`, `Platforms/SailfishOS/`) backed by the
`Microsoft.Maui.SailfishOS` package: the same project builds for every platform.

```bash
dotnet new install Microsoft.Maui.Platforms.SailfishOS.Templates
dotnet new maui-sailfish -n MyApp            # all heads (needs the MAUI workloads)
dotnet new maui-sailfish -n MyApp --sailfish-only   # just Sailfish OS
cd MyApp
dotnet publish -f net11.0-sailfish           # device payload + harbour RPM
dotnet build -f net11.0-sailfish -t:SailfishRun     # deploy and launch on a phone
```

One-time machine setup: the workload manifest that teaches the SDK the
`net11.0-sailfish` TFM (`tools/sf workload-install` in the repository).

F5 in VS Code: the [MAUI Sailfish Tools](https://github.com/Pastajello-Organization/sailfishos_maui_tools)
extension deploys, runs and debugs the project on the phone with no further
setup. The generated `.vscode/` also has terminal-based configurations; their
debug-attach entries need `SF_TOOLS_DIR` pointing at a checkout's `tools/`.

## Your phone

On the phone: Settings > Developer tools > Developer mode on, and a password
under "Remote connection". The first `SailfishRun` (or
`dotnet build -f net11.0-sailfish -t:SailfishSetup`) asks for the phone's
address, the SSH user (`defaultuser`) and that password (VS Code F5 asks in its
terminal; from a shell run
`DOTNET_CLI_USE_MSBUILD_SERVER=0 dotnet build -f net11.0-sailfish -t:SailfishSetup`), stores them in
`~/.config/maui-sailfish/connect.info` (mode 600) and installs your SSH key on
the phone. The password is needed to install the RPM (`devel-su`). A
`connect.info` next to the project (keep it out of git) or `$SF_CONNECT_INFO`
overrides the per-user file:

```
IP: 192.168.1.20
user: defaultuser
password: <Remote connection password>
```
