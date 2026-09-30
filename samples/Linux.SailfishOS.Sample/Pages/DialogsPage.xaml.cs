using Microsoft.Maui.Controls;

namespace Microsoft.Maui.SailfishOS.Sample.Pages;

/// <summary>
/// Gallery of modal dialogs: alert, confirm, prompt and action sheet.
/// </summary>
public partial class DialogsPage : ContentPage
{
	public DialogsPage()
	{
		InitializeComponent();
	}

	private async void OnAlertClicked(object? sender, EventArgs e)
	{
		await DisplayAlertAsync("Sailfish", "Hello from a MAUI alert dialog.", "OK");
		ResultLabel.Text = "result: alert dismissed";
	}

	private async void OnConfirmClicked(object? sender, EventArgs e)
	{
		var answer = await DisplayAlertAsync("Sailfish", "Do you like ambient theming?", "Yes", "No");
		ResultLabel.Text = $"result: {(answer ? "yes" : "no")}";
	}

	private async void OnPromptClicked(object? sender, EventArgs e)
	{
		var name = await DisplayPromptAsync("Sailfish", "Your name?", accept: "Save", cancel: "Cancel", placeholder: "name");
		ResultLabel.Text = $"result: {name ?? "<cancelled>"}";
	}

	private async void OnSheetClicked(object? sender, EventArgs e)
	{
		var action = await DisplayActionSheetAsync("Pick an action", "Cancel", "Delete", "Share", "Archive");
		ResultLabel.Text = $"result: {action}";
	}
}
