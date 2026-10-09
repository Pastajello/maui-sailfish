namespace Microsoft.Maui.SailfishOS.Tools;

/// <summary>
/// The phone to talk to, in tools/sf's <c>connect.info</c> format (<c>IP:</c>/<c>host:</c>, <c>user:</c>, <c>password:</c>
/// lines), so a phone set up with either tool works with the other. The first file that names a host wins:
/// <c>--connect-info</c>, <c>$SF_CONNECT_INFO</c>, the project's <c>connect.info</c>, then the user's
/// (<c>~/.config/maui-sailfish/connect.info</c>; <c>%APPDATA%\maui-sailfish</c> on Windows). <c>SF_HOST</c>,
/// <c>SF_USER</c> and <c>SF_PASSWORD</c> override the file.
/// </summary>
internal sealed class DeviceConfig
{
	public string Host { get; init; } = string.Empty;
	public string User { get; init; } = "defaultuser";

	/// <summary>The Remote connection password (developer mode): devel-su takes it for installs and screenshots.</summary>
	public string Password { get; init; } = string.Empty;

	/// <summary>The file the settings came from (where setup writes), or the user's file when none had them.</summary>
	public string File { get; init; } = UserFile;

	public bool HasHost => Host.Length > 0;

	public string Target => User + "@" + Host;

	/// <summary>The user's config directory for the tools (known_hosts, connect.info).</summary>
	public static string UserDirectory =>
		OperatingSystem.IsWindows()
			? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "maui-sailfish")
			: Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg
				? xdg
				: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "maui-sailfish");

	public static string UserFile => Path.Combine(UserDirectory, "connect.info");

	public static DeviceConfig Load(string? explicitFile, string? projectDirectory)
	{
		var candidates = new List<string?>
		{
			explicitFile,
			Environment.GetEnvironmentVariable("SF_CONNECT_INFO"),
			projectDirectory is null ? null : Path.Combine(projectDirectory, "connect.info"),
			UserFile,
		};
		var (host, user, password, file) = (string.Empty, string.Empty, string.Empty, explicitFile ?? UserFile);
		foreach (var candidate in candidates)
		{
			if (string.IsNullOrEmpty(candidate) || !System.IO.File.Exists(candidate))
				continue;
			var parsed = Parse(System.IO.File.ReadAllLines(candidate));
			if (parsed.Host.Length == 0)
				continue;
			(host, user, password, file) = (parsed.Host, parsed.User, parsed.Password, candidate);
			break;
		}
		static string Env(string name) => Environment.GetEnvironmentVariable(name) ?? string.Empty;
		return new DeviceConfig
		{
			Host = Env("SF_HOST") is { Length: > 0 } h ? h : host,
			User = Env("SF_USER") is { Length: > 0 } u ? u : user is { Length: > 0 } ? user : "defaultuser",
			Password = Env("SF_PASSWORD") is { Length: > 0 } p ? p : password,
			File = file,
		};
	}

	/// <summary>The lines' host, user and password; "&lt;…&gt;" placeholders (the template's) count as absent.</summary>
	internal static (string Host, string User, string Password) Parse(IEnumerable<string> lines)
	{
		string host = string.Empty, user = string.Empty, password = string.Empty;
		foreach (var raw in lines)
		{
			var line = raw.Trim();
			if (line.Length == 0 || line.StartsWith('#'))
				continue;
			var colon = line.IndexOf(':');
			if (colon <= 0)
				continue;
			var key = line[..colon].Trim().ToLowerInvariant();
			var value = line[(colon + 1)..].Trim();
			if (value.StartsWith('<') && value.EndsWith('>'))
				value = string.Empty;
			switch (key)
			{
				case "ip" or "host":
					host = value;
					break;
				case "user":
					user = value;
					break;
				case "password":
					password = value;
					break;
			}
		}
		return (host, user, password);
	}

	/// <summary>Writes the file as tools/sf setup does (0600 on Unix: it holds the password).</summary>
	public static void Save(string file, string host, string user, string password)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
		var text = "# Sailfish OS device for the maui-sailfish tools (written by sailfish setup)\n" +
		           $"IP: {host}\nuser: {user}\n" + (password.Length > 0 ? $"password: {password}\n" : string.Empty);
		var temp = file + ".tmp";
		System.IO.File.WriteAllText(temp, text);
		if (!OperatingSystem.IsWindows())
			System.IO.File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
		System.IO.File.Move(temp, file, overwrite: true);
	}
}
