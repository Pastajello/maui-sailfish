using System.Diagnostics;
using System.Text;

namespace Microsoft.Maui.SailfishOS.Tools;

/// <summary>
/// The phone over the system <c>ssh</c>/<c>scp</c>, as tools/lib/sf-lib.sh does it: the remote script goes in on stdin
/// (never in argv), the host key is pinned on first use in the tools' own known_hosts, and a password (before the key
/// is installed) is answered by this tool itself acting as <c>SSH_ASKPASS</c> (<see cref="AskpassVariable"/>). devel-su
/// reads the password from stdin, base64-wrapped, so it never shows in a process list.
/// </summary>
internal sealed class Ssh
{
	/// <summary>Set for the askpass child: the tool prints <see cref="AskpassPasswordVariable"/> and exits.</summary>
	internal const string AskpassVariable = "SAILFISH_TOOL_ASKPASS";
	internal const string AskpassPasswordVariable = "SAILFISH_TOOL_ASKPASS_PASSWORD";

	private readonly DeviceConfig _device;

	public Ssh(DeviceConfig device) => _device = device;

	public DeviceConfig Device => _device;

	/// <summary>Passwords go to ssh only when asked (setup installs the key with it).</summary>
	public bool UsePassword { get; set; }

	public static string KnownHosts => Path.Combine(DeviceConfig.UserDirectory, "known_hosts");

	internal IEnumerable<string> Options(int connectTimeout = 15)
	{
		Directory.CreateDirectory(DeviceConfig.UserDirectory);
		yield return "-o"; yield return "StrictHostKeyChecking=accept-new";
		yield return "-o"; yield return "UserKnownHostsFile=" + KnownHosts;
		yield return "-o"; yield return "LogLevel=ERROR";
		yield return "-o"; yield return "ConnectTimeout=" + connectTimeout;
		yield return "-o"; yield return "ServerAliveInterval=5";
		yield return "-o"; yield return "ServerAliveCountMax=6";
		if (!UsePassword)
		{
			yield return "-o"; yield return "BatchMode=yes";
		}
	}

	/// <summary>Runs <paramref name="script"/> (sh) on the phone. Output goes to <paramref name="stdout"/> (null: the
	/// console) line by line; returns the remote exit code (255: ssh itself failed).</summary>
	public int Run(string script, Action<string>? stdout = null, Action<string>? stderr = null, int connectTimeout = 15,
		CancellationToken cancel = default)
	{
		var args = Options(connectTimeout).ToList();
		args.Add(_device.Target);
		args.Add("__sf_script=$(cat); eval \"$__sf_script\" </dev/null");
		return Start("ssh", args, script.Replace("\r\n", "\n"), stdout, stderr, cancel);
	}

	/// <summary>Runs and collects stdout; rc and the text.</summary>
	public (int Rc, string Output) Capture(string script, int connectTimeout = 15)
	{
		var output = new StringBuilder();
		var errors = new StringBuilder();
		var rc = Run(script, line => output.Append(line).Append('\n'), line => errors.Append(line).Append('\n'), connectTimeout);
		if (rc == 255 && errors.Length > 0)
			LastError = errors.ToString().Trim();
		return (rc, output.ToString());
	}

	/// <summary>ssh's own error of the last failed <see cref="Capture"/>.</summary>
	public string LastError { get; private set; } = string.Empty;

	/// <summary>Runs <paramref name="script"/> as root through devel-su (the Remote connection password).</summary>
	public int Root(string script, Action<string>? stdout = null) => Run(RootScript(script, _device.Password), stdout);

	public (int Rc, string Output) RootCapture(string script) => Capture(RootScript(script, _device.Password));

	/// <summary>devel-su reads the password on stdin; its prompt goes to /dev/null and the command's stderr to fd 3.</summary>
	internal static string RootScript(string script, string password)
	{
		static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
		return $"echo {B64(password)} | base64 -d | devel-su /bin/sh -c 'echo {B64(script)} | base64 -d | /bin/sh -s 2>&3 3>&-' 3>&2 2>/dev/null";
	}

	/// <summary>Copies a local file to the phone.</summary>
	public int CopyTo(string localFile, string remotePath)
	{
		var args = Options().ToList();
		args.Add(localFile);
		args.Add(_device.Target + ":" + remotePath);
		return Start("scp", args, null, _ => { }, Console.Error.WriteLine, default);
	}

	/// <summary>Copies a file from the phone.</summary>
	public int CopyFrom(string remotePath, string localFile)
	{
		var args = Options().ToList();
		args.Add(_device.Target + ":" + remotePath);
		args.Add(localFile);
		return Start("scp", args, null, _ => { }, Console.Error.WriteLine, default);
	}

	/// <summary>"ok" (key login works), "auth" (the phone answers, the key is not accepted yet) or "down: …".</summary>
	public string Probe()
	{
		var saved = UsePassword;
		UsePassword = false;
		try
		{
			var errors = new StringBuilder();
			var rc = Start("ssh", Options(6).Concat([_device.Target, "true"]).ToList(), null, _ => { }, l => errors.Append(l).Append(' '), default);
			if (rc == 0)
				return "ok";
			var text = errors.ToString();
			if (text.Contains("Permission denied", StringComparison.Ordinal) || text.Contains("publickey", StringComparison.Ordinal) ||
			    text.Contains("Too many authentication", StringComparison.Ordinal))
				return "auth";
			return "down: " + (text.Length > 160 ? text[..160] : text).Trim();
		}
		finally
		{
			UsePassword = saved;
		}
	}

	private int Start(string program, List<string> args, string? stdin, Action<string>? stdout, Action<string>? stderr, CancellationToken cancel)
	{
		var info = new ProcessStartInfo(program)
		{
			RedirectStandardInput = true,
			RedirectStandardOutput = stdout is not null,
			RedirectStandardError = stderr is not null,
			UseShellExecute = false,
			StandardInputEncoding = new UTF8Encoding(false),
		};
		foreach (var arg in args)
			info.ArgumentList.Add(arg);
		string? askpassScript = null;
		if (UsePassword && _device.Password.Length > 0)
			askpassScript = Askpass(info, _device.Password);
		Process process;
		try
		{
			process = Process.Start(info) ?? throw new InvalidOperationException(program + " did not start");
		}
		catch (System.ComponentModel.Win32Exception ex)
		{
			throw new ToolException($"{program} is not installed or not on PATH ({ex.Message}). Install OpenSSH: on Windows it is the optional feature \"OpenSSH Client\".");
		}
		using (process)
		using (cancel.Register(() => { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } }))
		{
			if (stdout is not null)
				process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout(e.Data); };
			if (stderr is not null)
				process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr(e.Data); };
			if (stdout is not null)
				process.BeginOutputReadLine();
			if (stderr is not null)
				process.BeginErrorReadLine();
			if (stdin is not null)
				process.StandardInput.Write(stdin);
			process.StandardInput.Close();
			process.WaitForExit();
			if (askpassScript is not null)
				try { File.Delete(askpassScript); } catch (IOException) { }
			return process.ExitCode;
		}
	}

	/// <summary>Points ssh's SSH_ASKPASS at this tool (its apphost) with the password in the child's environment;
	/// under <c>dotnet sailfish.dll</c> (no apphost) a 0700 temp script prints it instead (Unix). Returns that script.</summary>
	private static string? Askpass(ProcessStartInfo info, string password)
	{
		info.Environment[AskpassVariable] = "1";
		info.Environment[AskpassPasswordVariable] = password;
		info.Environment["SSH_ASKPASS_REQUIRE"] = "force";
		if (!info.Environment.TryGetValue("DISPLAY", out var display) || string.IsNullOrEmpty(display))
			info.Environment["DISPLAY"] = ":0";   // older OpenSSH only asks askpass with a DISPLAY
		var self = Environment.ProcessPath ?? string.Empty;
		var host = Path.GetFileNameWithoutExtension(self);
		if (!string.Equals(host, "dotnet", StringComparison.OrdinalIgnoreCase) && File.Exists(self))
		{
			info.Environment["SSH_ASKPASS"] = self;
			return null;
		}
		if (OperatingSystem.IsWindows())
			throw new ToolException("password login needs the installed tool (dotnet tool install -g Microsoft.Maui.Platforms.SailfishOS.Tools), not dotnet sailfish.dll");
		var script = Path.Combine(Path.GetTempPath(), "sailfish-askpass-" + Guid.NewGuid().ToString("N") + ".sh");
		File.WriteAllText(script, "#!/bin/sh\nprintf '%s\\n' \"$" + AskpassPasswordVariable + "\"\n");
		File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		info.Environment["SSH_ASKPASS"] = script;
		return script;
	}
}

/// <summary>A failure to report as one line (no stack trace).</summary>
internal sealed class ToolException(string message) : Exception(message);
