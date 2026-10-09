using System.Reflection;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.SailfishOS.Sample.Pages;

/// <summary>About page: large image, platform blurb and version stamp.</summary>
public partial class AboutPage : ContentPage
{
	public AboutPage()
	{
		InitializeComponent();

		var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
		VersionLabel.Text = $"Sample version {version}  •  {DateTime.Now:yyyy-MM-dd}";
	}

	private async void OnBackClicked(object? sender, EventArgs e) =>
		await Navigation.PopAsync();
}
