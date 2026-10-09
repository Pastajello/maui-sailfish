using System.Text;
using Microsoft.Maui.SailfishOS.Tools;
using Xunit;

namespace Linux.SailfishOS.Tests;

/// <summary>Tracker S56b: the parts of the <c>sailfish</c> dotnet tool that need no phone. The device runs (deploy,
/// run --follow, Ctrl+C/SIGTERM, screenshot, kill) are in the tracker notes.</summary>
public sealed class SailfishToolTests
{
	[Fact]
	public void Connect_info_is_read_as_tools_sf_writes_it()
	{
		var (host, user, password) = DeviceConfig.Parse(["# comment", "IP: 192.168.2.15", "user: defaultuser", "password: s3cr:t"]);
		Assert.Equal(("192.168.2.15", "defaultuser", "s3cr:t"), (host, user, password));

		// The template's placeholders count as absent; host: is the other spelling.
		Assert.Equal((string.Empty, string.Empty, string.Empty), DeviceConfig.Parse(["IP: <phone ip>", "user: <user>", "password: <sshPassword>"]));
		Assert.Equal("phone.local", DeviceConfig.Parse(["host: phone.local"]).Host);
	}

	[Fact]
	public void Setup_writes_a_file_the_reader_and_tools_sf_agree_on()
	{
		var file = Path.Combine(Path.GetTempPath(), "sf-tool-" + Guid.NewGuid().ToString("N"), "connect.info");
		try
		{
			DeviceConfig.Save(file, "10.0.0.7", "defaultuser", "pw");
			var config = DeviceConfig.Load(file, null);
			Assert.Equal(("10.0.0.7", "defaultuser", file), (config.Host, config.User, config.File));
			Assert.Contains("IP: 10.0.0.7", File.ReadAllText(file));
			if (!OperatingSystem.IsWindows())
				Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
		}
		finally
		{
			Directory.Delete(Path.GetDirectoryName(file)!, recursive: true);
		}
	}

	[Fact]
	public void Options_parse_the_commands_and_deploy_follow_implies_run()
	{
		var options = Options.Parse(["deploy", "--follow", "--project", "app.csproj", "--env", "A=1", "--property", "SailfishTrim=false", "-c", "Debug"]);

		Assert.Equal(("deploy", true, true, "app.csproj", "Debug"), (options.Command, options.Run, options.Follow, options.Project, options.Configuration));
		Assert.Equal(["A=1"], options.Environment);
		Assert.Equal(["SailfishTrim=false"], options.Properties);
		Assert.Throws<ToolException>(() => Options.Parse(["run", "--nope"]));
		Assert.Throws<ToolException>(() => Options.Parse(["run", "--project"]));
	}

	[Fact]
	public void The_environment_reaches_the_run_helper_as_quoted_exports()
	{
		var blob = Commands.EnvBlob(["MAUI_X=1", "QUOTE=it's \"fine\" $HOME"]);

		Assert.Equal("export MAUI_X='1'\nexport QUOTE='it'\\''s \"fine\" $HOME'\n", Encoding.UTF8.GetString(Convert.FromBase64String(blob)));
		Assert.Equal(string.Empty, Commands.EnvBlob([]));
		Assert.Throws<ToolException>(() => Commands.EnvBlob(["no-equals"]));
		Assert.Throws<ToolException>(() => Commands.EnvBlob(["1BAD=x"]));
	}

	[Fact]
	public void Root_commands_keep_the_password_and_the_script_out_of_argv()
	{
		var script = Ssh.RootScript("rpm -Uvh 'x.rpm'", "p@ss");

		Assert.DoesNotContain("p@ss", script);
		Assert.DoesNotContain("rpm -Uvh", script);
		Assert.Contains("devel-su /bin/sh -c", script);
		Assert.Contains(Convert.ToBase64String("p@ss"u8.ToArray()), script);
	}

	[Theory]
	[InlineData("aarch64", "linux-arm64", "aarch64")]
	[InlineData("armv7l", "linux-arm", "armv7hl")]
	[InlineData("x86_64", "linux-x64", "x86_64")]
	public void The_phones_machine_picks_the_rid_and_the_rpm_arch(string machine, string rid, string arch)
	{
		Assert.Equal(rid, AppProject.RidFor(machine + "\n"));
		Assert.Equal(arch, AppProject.RpmArch(rid));
	}

	[Fact]
	public void Small_shell_helpers()
	{
		Assert.Equal("'a'\\''b'", Commands.Quote("a'b"));
		Assert.Equal("[h]arbour-x", Commands.ProcessPattern("harbour-x"));
	}

	[Fact]
	public void The_phone_side_scripts_are_embedded()
	{
		var names = typeof(Commands).Assembly.GetManifestResourceNames();

		Assert.Equal(["remote/sf-kill-remote.sh", "remote/sf-remote-env.sh", "remote/sf-run-remote.sh", "remote/sf-screenshot-remote.sh"], names.Order());
	}
}
