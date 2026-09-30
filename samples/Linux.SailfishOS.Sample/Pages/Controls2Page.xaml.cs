using Microsoft.Maui.Controls;

namespace Microsoft.Maui.SailfishOS.Sample.Pages;

/// <summary>
/// Gallery of the remaining basic controls: ActivityIndicator, RadioButton,
/// ImageButton, Border, Frame.
/// </summary>
public partial class Controls2Page : ContentPage
{
	private int _imageTaps;
	private string _radio = "Ambient (dark)";

	public Controls2Page()
	{
		InitializeComponent();
	}

	private void OnThemeRadioChanged(object? sender, CheckedChangedEventArgs e)
	{
		if (e.Value && sender is RadioButton { Content: string content })
		{
			_radio = content;
			StatusLabel.Text = $"radio: {_radio} | image taps: {_imageTaps}";
		}
	}

	private void OnImageClicked(object? sender, EventArgs e)
	{
		_imageTaps++;
		StatusLabel.Text = $"radio: {_radio} | image taps: {_imageTaps}";
	}
}
