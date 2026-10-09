using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.Maui.SailfishOS.Tools;

/// <summary>The device commands, step for step what tools/cmd/{setup,deploy,run,kill,screenshot}.sh do.</summary>
internal sealed class Commands
{
	private readonly Options _options;

	public Commands(Options options) => _options = options;

	private static void Info(string text) => Console.WriteLine("==> " + text);
	private static void Ok(string text) => Console.WriteLine("    OK   " + text);
	private static void Note(string text) => Console.WriteLine("    " + text);

	private DeviceConfig Device(string? projectDirectory = null)
	{
		var device = DeviceConfig.Load(_options.ConnectInfo, projectDirectory);
		if (!device.HasHost)
			throw new ToolException("no phone is set up: run `sailfish setup` (or write connect.info with IP:, user: and password: lines)");
		return device;
	}

	private string? ProjectDirectoryOrNull()
	{
		try
		{
			return Path.GetDirectoryName(AppProject.Find(_options.Project));
		}
		catch (ToolException)
		{
			return null;
		}
	}

	/* --- setup --- */

	public int Setup()
	{
		var existing = DeviceConfig.Load(_options.ConnectInfo, ProjectDirectoryOrNull());
		if (existing.HasHost && !_options.Force)
		{
			var state = new Ssh(existing).Probe();
			if (state is "ok" or "auth")
			{
				if (_options.IfNeeded)
					return 0;
				Info($"a phone is already set up and answers: {existing.Target} ({existing.File})");
				Note("pass --force to change it");
				return 0;
			}
			Note($"The configured phone {existing.Target} ({existing.File}) does not answer: {state["down: ".Length..]}");
		}
		using var terminal = Terminal.Open();
		if (terminal is null)
		{
			Console.Error.WriteLine($"Setting up the phone needs a terminal: run `{SelfCommand()} setup` in one, once (it asks for the phone's");
			Console.Error.WriteLine("address and its Remote connection password from Settings › Developer tools).");
			return 1;
		}
		terminal.WriteLine("==> Set up the Sailfish OS phone (Settings › Developer tools: Developer mode on, a Remote connection password set)");
		var (host, user) = (existing.Host, existing.HasHost ? existing.User : "defaultuser");
		// Asked until the answers work, with the last ones as defaults; nothing is saved before that.
		while (true)
		{
			host = terminal.Ask("Phone address (USB: 192.168.2.15, Wi-Fi: the address under Developer tools)", host);
			if (!LooksLikeHost(host))
			{
				terminal.WriteLine($"  '{host}' is not an IP address or host name.");
				host = existing.Host;
				continue;
			}
			user = terminal.Ask("User", user);
			var password = terminal.AskSecret("Remote connection password (empty: ssh asks for it once, installs then go through PackageKit)");
			try
			{
				SetUp(new DeviceConfig { Host = host, User = user, Password = password, File = existing.File });
				return 0;
			}
			catch (ToolException ex)
			{
				terminal.WriteLine("  " + ex.Message);
				if (!terminal.Ask("Try again? [Y/n]", "y").StartsWith("y", StringComparison.OrdinalIgnoreCase))
				{
					terminal.WriteLine("Nothing changed.");
					return 1;
				}
			}
		}
	}

	private static void SetUp(DeviceConfig device)
	{
		var ssh = new Ssh(device);
		var probe = ssh.Probe();
		if (probe.StartsWith("down", StringComparison.Ordinal))
			throw new ToolException($"{device.Target} does not answer over SSH: {probe[6..]}");
		if (probe != "ok")
		{
			var key = KeyFile();
			if (!File.Exists(key))
			{
				Directory.CreateDirectory(Path.GetDirectoryName(key)!);
				RunProgram("ssh-keygen", ["-q", "-t", "ed25519", "-f", key, "-N", "", "-C", "maui-sailfish"]);
				Note("created " + key);
			}
			var pub = File.ReadAllText(key + ".pub").Trim();
			// With the password ssh gets it from this tool (askpass); without one ssh asks on the terminal.
			ssh.UsePassword = true;
			var rc = ssh.Run($"umask 077; mkdir -p ~/.ssh; touch ~/.ssh/authorized_keys; grep -qxF {Quote(pub)} ~/.ssh/authorized_keys || printf '%s\\n' {Quote(pub)} >> ~/.ssh/authorized_keys",
				_ => { }, _ => { });
			ssh.UsePassword = false;
			if (rc != 0)
				throw new ToolException($"that password did not log in to {device.Target}");
			if (ssh.Probe() != "ok")
				throw new ToolException("the key was installed but key login still fails — nothing saved");
		}
		Ok($"key login to {device.Target} works");
		if (device.Password.Length > 0)
		{
			var (_, id) = ssh.RootCapture("id -u");
			if (id.Trim() != "0")
				throw new ToolException("devel-su did not accept that password (it is the Remote connection password)");
			Ok("devel-su works — installs use rpm as root");
		}
		DeviceConfig.Save(device.File, device.Host, device.User, device.Password);
		Ok("saved " + device.File);
		var (_, release) = ssh.Capture("sed -n 's/^PRETTY_NAME=//p' /etc/os-release");
		Ok("phone: " + (release.Trim().Trim('"') is { Length: > 0 } r ? r : "unknown OS"));
	}

	/// <summary>An IPv4 address (four octets) or a host name; "192." and "my phone" are not.</summary>
	internal static bool LooksLikeHost(string text)
	{
		if (text.Length == 0 || text.Length > 253)
			return false;
		if (System.Text.RegularExpressions.Regex.IsMatch(text, @"^[0-9.]+$"))
		{
			var octets = text.Split('.');
			return octets.Length == 4 && octets.All(o => o.Length is > 0 and <= 3 && int.Parse(o, CultureInfo.InvariantCulture) <= 255);
		}
		return System.Text.RegularExpressions.Regex.IsMatch(text, @"^[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*$");
	}

	/// <summary>The key setup creates and ssh offers by default.</summary>
	private static string KeyFile() =>
		Environment.GetEnvironmentVariable("SF_SSH_KEY") is { Length: > 0 } key
			? key
			: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "id_ed25519");

	/// <summary>How to run this tool again: <c>sailfish</c> when installed, else <c>dotnet "…/sailfish.dll"</c> (the
	/// copy in the backend package that the build targets run).</summary>
	internal static string SelfCommand()
	{
		var host = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? string.Empty);
		return string.Equals(host, "dotnet", StringComparison.OrdinalIgnoreCase)
			? $"dotnet \"{typeof(Commands).Assembly.Location}\""
			: "sailfish";
	}

	/* --- deploy --- */

	public int Deploy()
	{
		var projectFile = AppProject.Find(_options.Project);
		var device = Device(Path.GetDirectoryName(projectFile));
		var ssh = new Ssh(device);
		var rid = _options.Rid ?? DeviceRid(ssh);
		var project = AppProject.Load(projectFile, _options.Configuration, rid, _options.Framework, _options.Package);
		var release = DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
		Info($"building {project.PackageName} ({project.TargetFramework}, {rid}, {_options.Configuration}, release {release})");
		foreach (var dir in new[] { Path.Combine(project.Directory, "obj", "SailfishRpm"), project.RpmDirectory })
			if (Directory.Exists(dir))
				Directory.Delete(dir, recursive: true);
		if (Dotnet.Run(project.PublishArguments(_options.Configuration, rid, release, _options.Properties.Select(p => "-p:" + p)), project.Directory) != 0)
			throw new ToolException("dotnet publish failed (see above)");
		var rpms = Directory.Exists(project.RpmDirectory)
			? Directory.GetFiles(project.RpmDirectory, $"*-{release}.{AppProject.RpmArch(rid)}.rpm")
			: [];
		if (rpms.Length != 1)
			throw new ToolException($"expected one RPM for release {release} in {project.RpmDirectory}, found {rpms.Length}");
		var rpm = rpms[0];
		Ok($"built {Path.GetFileName(rpm)} ({new FileInfo(rpm).Length / 1024 / 1024.0:F1} MB)");

		PushHelpers(ssh);
		Kill(ssh, project.PackageName, project.AssemblyName);
		var remote = $"/home/{device.User}/{Path.GetFileName(rpm)}";
		Info($"uploading to {device.Target}");
		if (ssh.CopyTo(rpm, remote) != 0)
			throw new ToolException("scp of the RPM failed");
		var local = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(rpm))).ToLowerInvariant();
		var (_, remoteSum) = ssh.Capture($"sha256sum {Quote(remote)}");
		if (!remoteSum.StartsWith(local, StringComparison.Ordinal))
			throw new ToolException($"the uploaded RPM differs from the local one (sha256 {local[..12]}… vs {remoteSum.Split(' ')[0]})");
		Info("installing");
		var install = device.Password.Length > 0
			? ssh.Root($"rpm -Uvh --force --nodeps {Quote(remote)}")
			: ssh.Run($"pkcon install-local -y {Quote(remote)}");
		ssh.Run($"rm -f {Quote(remote)}", _ => { });
		if (install != 0)
			throw new ToolException($"installing the RPM failed (rc {install})" + (device.Password.Length == 0 ? "; without a password pkcon installs, which may ask on the phone — run `sailfish setup` with the password" : string.Empty));
		var (_, installed) = ssh.Capture($"rpm -q {Quote(project.PackageName)}");
		if (!installed.Contains(release, StringComparison.Ordinal))
			throw new ToolException($"the phone reports {installed.Trim()} after the install, not release {release}");
		Kill(ssh, project.PackageName, project.AssemblyName);
		Ok($"deployed {installed.Trim()} to {device.Target}");
		return _options.Run ? Run(project, ssh) : 0;
	}

	private static string DeviceRid(Ssh ssh)
	{
		var (rc, machine) = ssh.Capture("uname -m");
		if (rc != 0)
			throw new ToolException($"{ssh.Device.Target} does not answer over SSH ({ssh.LastError}); run `sailfish setup`");
		return AppProject.RidFor(machine);
	}

	/* --- run, kill, logs --- */

	public int Run()
	{
		var projectFile = AppProject.Find(_options.Project);
		var device = Device(Path.GetDirectoryName(projectFile));
		var ssh = new Ssh(device);
		var project = AppProject.Load(projectFile, _options.Configuration, _options.Rid ?? "linux-arm64", _options.Framework, _options.Package);
		return Run(project, ssh);
	}

	private int Run(AppProject project, Ssh ssh)
	{
		PushHelpers(ssh);
		var env = EnvBlob(_options.Environment);
		Info($"starting {project.PackageName} on {ssh.Device.Target}");
		var started = ssh.Run($"/tmp/sf-run-remote.sh {Quote(project.PackageName)} {Quote(project.AssemblyName)} - {env}") == 0;
		// --follow reports the exit code the app logs: an app that is done within the helper's 10 s launch window
		// (a short run, a test leg) is not a failed start.
		if (!_options.Follow)
			return started ? 0 : throw new ToolException("the app did not stay alive on the phone (see the log above)");
		Info($"streaming the app log (Ctrl+C stops {project.PackageName})");
		var pattern = ProcessPattern(project.PackageName);
		using var cancel = new CancellationTokenSource();
		using (OnStop(cancel))
			ssh.Run($"tail -n +1 -f /tmp/sf_run.log & t=$!; while pgrep -f '{pattern}' >/dev/null; do sleep 1; done; sleep 1; kill $t 2>/dev/null; true",
				connectTimeout: 15, cancel: cancel.Token);
		var stopped = cancel.IsCancellationRequested;
		// The stream ends by itself once the app is gone; an app still running means the stream was cut.
		var (_, alive) = ssh.Capture($"pgrep -f '{pattern}' >/dev/null && echo ALIVE");
		if (stopped || alive.Contains("ALIVE", StringComparison.Ordinal))
		{
			Console.WriteLine();
			Info($"stopping {project.PackageName} on the phone");
			Kill(ssh, project.PackageName, project.AssemblyName);
			return 130;
		}
		var (_, codeText) = ssh.Capture("grep -oE 'exit code -?[0-9]+' /tmp/sf_run.log | tail -1");
		var code = codeText.Trim().Split(' ').LastOrDefault();
		if (!int.TryParse(code, out var exit))
		{
			Note($"{project.PackageName} ended without logging an exit code (killed or crashed?)");
			return 1;
		}
		Console.WriteLine(exit == 0 ? $"    OK   {project.PackageName} exited with code 0" : $"ERROR: {project.PackageName} exited with code {exit}");
		return exit;
	}

	/// <summary>Ctrl+C (SIGINT; Ctrl+C or Ctrl+Break on Windows) and SIGTERM (an IDE or <c>dotnet run</c> stopping the tool)
	/// cancel <paramref name="cancel"/> instead of ending the process, so the caller can stop the app on the phone.</summary>
	private static IDisposable OnStop(CancellationTokenSource cancel)
	{
		void Handle(System.Runtime.InteropServices.PosixSignalContext context)
		{
			context.Cancel = true;
			cancel.Cancel();
		}
		var registrations = new List<System.Runtime.InteropServices.PosixSignalRegistration>
		{
			System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGINT, Handle),
			System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGTERM, Handle),
		};
		if (OperatingSystem.IsWindows())
			registrations.Add(System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGQUIT, Handle));
		return new Registrations(registrations);
	}

	private sealed class Registrations(List<System.Runtime.InteropServices.PosixSignalRegistration> list) : IDisposable
	{
		public void Dispose() => list.ForEach(r => r.Dispose());
	}

	/// <summary>The package name with its first letter bracketed, so pgrep never matches the shell running it.</summary>
	internal static string ProcessPattern(string package) => package.Length == 0 ? package : $"[{package[0]}]{package[1..]}";

	/// <summary><c>--env NAME=VALUE</c> as the run helper reads it: base64 of <c>export NAME='VALUE'</c> lines.</summary>
	internal static string EnvBlob(IEnumerable<string> assignments)
	{
		var lines = new StringBuilder();
		foreach (var assignment in assignments)
		{
			var at = assignment.IndexOf('=');
			if (at <= 0)
				throw new ToolException($"--env takes NAME=VALUE, got '{assignment}'");
			var name = assignment[..at];
			if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$"))
				throw new ToolException($"'{name}' is not an environment variable name");
			lines.Append("export ").Append(name).Append('=').Append(Quote(assignment[(at + 1)..])).Append('\n');
		}
		return lines.Length == 0 ? string.Empty : Convert.ToBase64String(Encoding.UTF8.GetBytes(lines.ToString()));
	}

	public int KillCommand()
	{
		var projectFile = AppProject.Find(_options.Project);
		var ssh = new Ssh(Device(Path.GetDirectoryName(projectFile)));
		var project = AppProject.Load(projectFile, _options.Configuration, _options.Rid ?? "linux-arm64", _options.Framework, _options.Package);
		PushHelpers(ssh);
		return Kill(ssh, project.PackageName, project.AssemblyName) ? 0 : 1;
	}

	private static bool Kill(Ssh ssh, string package, string assembly)
	{
		var rc = ssh.Run($"/tmp/sf-kill-remote.sh {Quote(package)} {Quote(assembly)}", _ => { });
		if (rc != 0)
			Note($"{package}: an instance survived the kill (rc {rc})");
		return rc == 0;
	}

	public int Logs()
	{
		var ssh = new Ssh(Device(ProjectDirectoryOrNull()));
		if (!_options.Follow)
			return ssh.Run("cat /tmp/sf_run.log");
		using var cancel = new CancellationTokenSource();
		using (OnStop(cancel))
			ssh.Run("tail -n +1 -f /tmp/sf_run.log", cancel: cancel.Token);
		return 0;
	}

	/* --- screenshot --- */

	public int Screenshot()
	{
		var device = Device(ProjectDirectoryOrNull());
		var ssh = new Ssh(device);
		PushHelpers(ssh);
		var output = _options.Output ?? $"sailfish-screen-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.png";
		var lines = new List<string>();
		ssh.Run($"SF_SU_PASS=\"$(echo {Convert.ToBase64String(Encoding.UTF8.GetBytes(device.Password))} | base64 -d)\" /tmp/sf-screenshot-remote.sh", lines.Add);
		if (!lines.Any(l => l.Contains("SCREENSHOT_OK", StringComparison.Ordinal)))
			throw new ToolException("the phone did not take a screenshot: " + string.Join(" | ", lines.TakeLast(3)));
		if (ssh.CopyFrom("/tmp/sf-screen.png", output) != 0 || !File.Exists(output) || new FileInfo(output).Length == 0)
			throw new ToolException("copying the screenshot from the phone failed");
		Ok("saved " + Path.GetFullPath(output));
		return 0;
	}

	/* --- helpers --- */

	/// <summary>Writes the embedded phone-side scripts to /tmp in one round trip.</summary>
	private static void PushHelpers(Ssh ssh)
	{
		var script = new StringBuilder("set -e\n");
		var assembly = typeof(Commands).Assembly;
		foreach (var name in new[] { "sf-remote-env.sh", "sf-kill-remote.sh", "sf-run-remote.sh", "sf-screenshot-remote.sh" })
		{
			using var stream = assembly.GetManifestResourceStream("remote/" + name) ?? throw new InvalidOperationException("missing resource " + name);
			using var reader = new StreamReader(stream);
			var body = reader.ReadToEnd().Replace("\r\n", "\n");
			script.Append($"echo {Convert.ToBase64String(Encoding.UTF8.GetBytes(body))} | base64 -d > /tmp/{name}; chmod +x /tmp/{name}\n");
		}
		var rc = ssh.Run(script.ToString(), _ => { });
		if (rc != 0)
			throw new ToolException($"{ssh.Device.Target} does not take the helper scripts (rc {rc}); is the phone reachable? `sailfish setup` checks it");
	}

	/// <summary>A POSIX shell single-quoted word.</summary>
	internal static string Quote(string value) => "'" + value.Replace("'", "'\\''") + "'";

	private static void RunProgram(string program, string[] args)
	{
		var info = new System.Diagnostics.ProcessStartInfo(program) { UseShellExecute = false };
		foreach (var arg in args)
			info.ArgumentList.Add(arg);
		using var process = System.Diagnostics.Process.Start(info)!;
		process.WaitForExit();
		if (process.ExitCode != 0)
			throw new ToolException($"{program} failed (rc {process.ExitCode})");
	}
}

/// <summary>Where setup asks its questions: the console, or the terminal behind it when stdio is redirected (MSBuild's
/// Exec under <c>dotnet build -t:SailfishSetup</c>), which on Unix is /dev/tty, as tools/sf setup reads it.</summary>
internal sealed class Terminal : IDisposable
{
	private readonly TextReader? _in;
	private readonly TextWriter? _out;

	private Terminal(TextReader? input, TextWriter? output)
	{
		_in = input;
		_out = output;
	}

	public static Terminal? Open()
	{
		if (!Console.IsInputRedirected)
			return new Terminal(null, null);
		if (OperatingSystem.IsWindows())
			return null;
		try
		{
			var stream = new FileStream("/dev/tty", FileMode.Open, FileAccess.ReadWrite);
			return new Terminal(new StreamReader(stream), new StreamWriter(stream) { AutoFlush = true });
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return null;   // no controlling terminal (CI, an IDE's build)
		}
	}

	public void WriteLine(string text)
	{
		if (_out is null)
			Console.WriteLine(text);
		else
			_out.WriteLine(text);
	}

	private void Write(string text)
	{
		if (_out is null)
			Console.Write(text);
		else
			_out.Write(text);
	}

	public string Ask(string prompt, string fallback)
	{
		Write(fallback.Length > 0 ? $"  {prompt} [{fallback}]: " : $"  {prompt}: ");
		var answer = (_in is null ? Console.ReadLine() : _in.ReadLine())?.Trim() ?? string.Empty;
		return answer.Length > 0 ? answer : fallback;
	}

	public string AskSecret(string prompt)
	{
		Write($"  {prompt}: ");
		if (_in is not null)
		{
			Stty("-echo");
			try
			{
				return _in.ReadLine() ?? string.Empty;
			}
			finally
			{
				Stty("echo");
				WriteLine(string.Empty);
			}
		}
		var text = new StringBuilder();
		while (true)
		{
			var key = Console.ReadKey(intercept: true);
			if (key.Key == ConsoleKey.Enter)
				break;
			if (key.Key == ConsoleKey.Backspace)
			{
				if (text.Length > 0)
					text.Length--;
				continue;
			}
			if (!char.IsControl(key.KeyChar))
				text.Append(key.KeyChar);
		}
		Console.WriteLine();
		return text.ToString();
	}

	private static void Stty(string mode)
	{
		var info = new System.Diagnostics.ProcessStartInfo("/bin/sh") { UseShellExecute = false };
		info.ArgumentList.Add("-c");
		info.ArgumentList.Add($"stty {mode} < /dev/tty");
		using var process = System.Diagnostics.Process.Start(info);
		process?.WaitForExit();
	}

	public void Dispose()
	{
		_in?.Dispose();
		_out?.Dispose();
	}
}
