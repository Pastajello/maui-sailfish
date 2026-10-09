using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Linux.SailfishOS.Tests;

/// <summary>Tracker S58 (B8, D9 b): the maui-sailfish template carries the official template's symbols, and its styles
/// colour only the in-box platforms, so a Sailfish app shows the phone's ambience. Generation and the phone run are in
/// the tracker notes; tools/ci/host-ci.sh builds a generated app.</summary>
public class TemplateTests
{
	private static readonly string Template = Path.Combine(Repo.Root, "templates", "maui-sailfish-app");

	[Fact]
	public void The_official_symbols_replace_what_the_project_contains()
	{
		using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Template, ".template.config", "template.json")));
		var symbols = json.RootElement.GetProperty("symbols");
		var csproj = File.ReadAllText(Path.Combine(Template, "MauiSailfishApp.csproj"));
		var manifest = File.ReadAllText(Path.Combine(Template, "Platforms", "Windows", "Package.appxmanifest"));

		Assert.True(symbols.TryGetProperty("applicationId", out _));
		Assert.Equal("com.companyname.mauisailfishapp", symbols.GetProperty("finalAppId").GetProperty("replaces").GetString());
		Assert.Contains("<ApplicationId>com.companyname.mauisailfishapp</ApplicationId>", csproj);
		Assert.Equal("net11.0", symbols.GetProperty("Framework").GetProperty("replaces").GetString());
		var guid = symbols.GetProperty("PhoneProductId").GetProperty("replaces").GetString()!;
		Assert.Equal("guid", symbols.GetProperty("PhoneProductId").GetProperty("generator").GetString());
		Assert.Contains($"PhoneProductId=\"{guid}\"", manifest);
	}

	[Fact]
	public void Every_style_colour_is_for_the_in_box_platforms_only()
	{
		var styles = File.ReadAllText(Path.Combine(Template, "Resources", "Styles", "Styles.xaml"));

		// No colour set straight on a Setter: each sits in an OnPlatform without a SailfishOS branch.
		Assert.DoesNotMatch(new Regex(@"<Setter Property=""[A-Za-z.]*Color"" Value=""\{"), styles);
		var wrapped = Regex.Matches(styles, @"<On Platform=""([^""]+)"" Value=""\{").Select(m => m.Groups[1].Value).ToList();
		Assert.True(wrapped.Count > 50, $"{wrapped.Count} wrapped colours");
		Assert.All(wrapped, platforms => Assert.Equal("Android, iOS, MacCatalyst, WinUI", platforms));
	}

	[Fact]
	public void UseMaui_is_on_for_the_multi_head_app_and_off_for_sailfish_only()
	{
		var csproj = File.ReadAllText(Path.Combine(Template, "MauiSailfishApp.csproj"));

		Assert.Matches(new Regex(@"<!--#if \(!SailfishOnly\) -->\s*<!--[^>]*-->\s*<UseMaui>true</UseMaui>\s*<!--#else -->", RegexOptions.Singleline), csproj);
	}
}
