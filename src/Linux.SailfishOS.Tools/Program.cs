namespace Microsoft.Maui.SailfishOS.Tools;

internal sealed class Options
{
	public string Command = string.Empty;
	public string? Project;
	public string? ConnectInfo;
	public string Configuration = "Release";
	public string? Rid;
	public string? Framework;
	public string? Package;
	public string? Output;
	public bool Run;
	public bool Follow;
	public bool Force;
	public bool IfNeeded;
	public bool Help;
	public readonly List<string> Environment = new();
	public readonly List<string> Properties = new();

	internal static Options Parse(string[] args)
	{
		var options = new Options();
		for (var i = 0; i < args.Length; i++)
		{
			string Value() => i + 1 < args.Length ? args[++i] : throw new ToolException($"{args[i]} needs a value");
			switch (args[i])
			{
				case "-h" or "--help" or "help":
					options.Help = true;
					break;
				case "--project" or "-p":
					options.Project = Value();
					break;
				case "--connect-info":
					options.ConnectInfo = Value();
					break;
				case "-c" or "--configuration":
					options.Configuration = Value();
					break;
				case "--rid" or "-r":
					options.Rid = Value();
					break;
				case "--framework" or "-f":
					options.Framework = Value();
					break;
				case "--package":
					options.Package = Value();
					break;
				case "-o" or "--output":
					options.Output = Value();
					break;
				case "--env":
					options.Environment.Add(Value());
					break;
				case "--property":
					options.Properties.Add(Value());
					break;
				case "--run":
					options.Run = true;
					break;
				case "--follow":
					options.Follow = true;
					break;
				case "--force":
					options.Force = true;
					break;
				case "--if-needed":
					options.IfNeeded = true;
					break;
				case var a when a.StartsWith("-", StringComparison.Ordinal):
					throw new ToolException($"unknown option {a} (sailfish --help)");
				case var a when options.Command.Length == 0:
					options.Command = a;
					break;
				case var a:
					throw new ToolException($"unexpected argument {a} (sailfish --help)");
			}
		}
		if (options.Command == "deploy" && options.Follow)
			options.Run = true;
		return options;
	}
}

/// <summary><c>sailfish</c>: the device loop for apps built with the Sailfish OS backend (tracker S56b).</summary>
public static class Program
{
	private const string Usage = """
		Usage: sailfish <command> [options]

		  setup        set up the phone once: address, Remote connection password (Settings › Developer tools), an SSH key
		               [--force: change it] [--if-needed: nothing when the phone already answers]
		  deploy       build the app's RPM (dotnet publish) and install it on the phone [--run] [--follow]
		  run          start the installed app [--follow: stream its log until it exits; Ctrl+C stops it] [--env NAME=VALUE]
		  kill         stop the app on the phone
		  logs         print the app's last log [--follow]
		  screenshot   save the phone's screen as PNG [-o <file>]

		  --project <csproj|dir>   the app (default: the .csproj in the current directory)
		  -c <configuration>       Release (default) or Debug
		  -f <tfm>                 net11.0-sailfish or net11.0 (default: net11.0-sailfish when the project lists it)
		  --rid <rid>              linux-arm64 or linux-arm (default: from the phone)
		  --package <name>         the RPM package name (default: the project's SailfishPackageName)
		  --property <Name=Value>  passed to dotnet publish as -p:Name=Value
		  --connect-info <file>    the phone's settings (default: the project's connect.info, then the user's)

		Needs the .NET SDK and OpenSSH (ssh, scp, ssh-keygen; on Windows the "OpenSSH Client" optional feature).
		""";

	public static int Main(string[] args)
	{
		// ssh runs this tool as its SSH_ASKPASS program while setup installs the key.
		if (Environment.GetEnvironmentVariable(Ssh.AskpassVariable) == "1")
		{
			Console.WriteLine(Environment.GetEnvironmentVariable(Ssh.AskpassPasswordVariable) ?? string.Empty);
			return 0;
		}
		try
		{
			var options = Options.Parse(args);
			if (options.Help || options.Command.Length == 0)
			{
				Console.WriteLine(Usage);
				return options.Help ? 0 : 1;
			}
			var commands = new Commands(options);
			return options.Command switch
			{
				"setup" => commands.Setup(),
				"deploy" => commands.Deploy(),
				"run" => commands.Run(),
				"kill" => commands.KillCommand(),
				"logs" => commands.Logs(),
				"screenshot" => commands.Screenshot(),
				var other => throw new ToolException($"unknown command '{other}' (sailfish --help)"),
			};
		}
		catch (ToolException ex)
		{
			Console.Error.WriteLine("ERROR: " + ex.Message);
			return 1;
		}
	}
}
