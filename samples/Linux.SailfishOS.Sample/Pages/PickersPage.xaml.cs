using Microsoft.Maui.Controls;

namespace Microsoft.Maui.SailfishOS.Sample.Pages;

/// <summary>
/// Gallery of value-picking controls: Picker, DatePicker, TimePicker, Stepper.
/// </summary>
public partial class PickersPage : ContentPage
{
	public PickersPage()
	{
		InitializeComponent();
		OnChanged(this, EventArgs.Empty);
	}

	private void OnChanged(object? sender, EventArgs e) =>
		StatusLabel.Text =
			$"picker: {DevicePicker.SelectedItem ?? "-"} | " +
			$"date: {ReleaseDate.Date:yyyy-MM-dd} | " +
			$"time: {StandupTime.Time:hh\\:mm} | " +
			$"step: {CountStepper.Value:0}";
}
