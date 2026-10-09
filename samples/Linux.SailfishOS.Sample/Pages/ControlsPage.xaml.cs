using Microsoft.Maui.Controls;

namespace Microsoft.Maui.SailfishOS.Sample.Pages;

/// <summary>
/// Basic controls demo; each interaction updates a label through MAUI property changes.
/// </summary>
public partial class ControlsPage : ContentPage
{
	private int _counter;

	public ControlsPage()
	{
		InitializeComponent();
	}

	private void OnTapMeClicked(object? sender, EventArgs e)
	{
		_counter++;
		StatusLabel.Text = $"Tapped {_counter} time(s)";
	}

	private void OnCheckChanged(object? sender, CheckedChangedEventArgs e) =>
		CheckLabel.Text = $"CheckBox: {(e.Value ? "on" : "off")}";

	private void OnSwitchToggled(object? sender, ToggledEventArgs e) =>
		SwitchLabel.Text = $"Switch: {(e.Value ? "on" : "off")}";

	private void OnSliderChanged(object? sender, ValueChangedEventArgs e)
	{
		SliderLabel.Text = $"Slider: {e.NewValue:0.00}";
		Progress.Progress = e.NewValue;
	}
}
