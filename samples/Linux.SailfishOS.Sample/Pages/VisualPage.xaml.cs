using Microsoft.Maui.Controls;

namespace Microsoft.Maui.SailfishOS.Sample.Pages;

/// <summary>
/// Gallery of visual capabilities: gradients, corner radius, shadows,
/// transforms, ZIndex, VisualStateManager and app theming.
/// </summary>
public partial class VisualPage : ContentPage
{
	private bool _disabled;

	public VisualPage()
	{
		InitializeComponent();
	}

	private void OnStateClicked(object? sender, EventArgs e)
	{
		_disabled = !_disabled;
		VisualStateManager.GoToState(StateButton, _disabled ? VisualStateManager.CommonStates.Disabled : VisualStateManager.CommonStates.Normal);
		StateButton.Text = _disabled ? "state: disabled" : "state: normal";
	}

	private void OnThemeClicked(object? sender, EventArgs e)
	{
		var app = Application.Current;
		if (app is null)
			return;

		app.UserAppTheme = app.UserAppTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
		ThemeLabel.Text = $"theme: {app.UserAppTheme}";
	}
}
