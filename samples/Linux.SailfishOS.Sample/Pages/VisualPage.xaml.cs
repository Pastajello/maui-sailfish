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

	/// <summary>The pinch log the visual leg reads ("Started,Running,…,Completed").</summary>
	public List<GestureStatus> PinchLog { get; } = new();

	// Two fingers resize the box, as a photo viewer zooms.
	private void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
	{
		PinchLog.Add(e.Status);
		if (e.Status == GestureStatus.Running)
			Pinchable.Scale = Math.Clamp(Pinchable.Scale * e.Scale, 0.5, 3);
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
