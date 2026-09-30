using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.SailfishOS.Sample.Pages;

/// <summary>
/// One row of the <see cref="FeaturesPage"/> gallery hub.
/// </summary>
public sealed class FeatureItem
{
	public required string Title { get; init; }

	public required string Subtitle { get; init; }

	public required Color Accent { get; init; }

	/// <summary>Creates the gallery page for this capability area.</summary>
	public required Func<Page> Create { get; init; }
}

/// <summary>
/// Gallery hub of the sample's capability areas; rows are fixed-height so device tests can
/// tap row N at a predictable coordinate.
/// </summary>
public partial class FeaturesPage : ContentPage
{
	public FeaturesPage()
	{
		InitializeComponent();

		FeatureList.ItemsSource = new List<FeatureItem>
		{
			new() { Title = "Text", Subtitle = "wrap, alignment, truncation, fonts, spans", Accent = Colors.MediumPurple, Create = () => new TextPage() },
			new() { Title = "Text input", Subtitle = "Entry, Editor, SearchBar, keyboard", Accent = Colors.Orange, Create = () => new TextInputPage() },
			new() { Title = "Pickers", Subtitle = "Picker, DatePicker, TimePicker, Stepper", Accent = Colors.Teal, Create = () => new PickersPage() },
			new() { Title = "Controls", Subtitle = "ActivityIndicator, RadioButton, Border, Frame", Accent = Colors.Green, Create = () => new Controls2Page() },
			new() { Title = "Dialogs", Subtitle = "alert, prompt, action sheet", Accent = Colors.Crimson, Create = () => new DialogsPage() },
			new() { Title = "Gestures", Subtitle = "tap, pan, swipe, pinch, refresh, swipe-views", Accent = Colors.DodgerBlue, Create = () => new GesturesPage() },
			new() { Title = "Visual", Subtitle = "gradients, corners, shadows, transforms, themes", Accent = Colors.HotPink, Create = () => new VisualPage() },
			new() { Title = "Collections", Subtitle = "grid layout, groups, header/footer, scroll-to", Accent = Colors.Goldenrod, Create = () => new CollectionAdvancedPage() },
			new() { Title = "Navigation", Subtitle = "modals, tabs, toolbar, lifecycle", Accent = Colors.SkyBlue, Create = () => new NavigationDemoPage() },
			new() { Title = "Shapes & images", Subtitle = "GraphicsView, shapes, aspect modes", Accent = Colors.LimeGreen, Create = () => new ShapesImagesPage() },
			new() { Title = "Essentials", Subtitle = "device info, prefs, clipboard, sensors", Accent = Colors.SlateGray, Create = () => new EssentialsPage() },
		};
	}

	private async void OnFeatureSelected(object? sender, SelectionChangedEventArgs e)
	{
		if (e.CurrentSelection.FirstOrDefault() is FeatureItem item)
			await Navigation.PushAsync(item.Create());

		// Allow re-selecting the same row after popping back.
		FeatureList.SelectedItem = null;
	}
}
