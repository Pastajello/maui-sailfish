using Microsoft.Maui.SailfishOS.Platform;
using Microsoft.Maui.Storage;
using Trailhead.Data;
using Trailhead.Features.Recording;
using Trailhead.Features.Shared;

namespace Trailhead.Features.Settings;

/// <summary>
/// Units, the GPS source for recordings (the phone's or a replayed demo track), the map layer new maps start with,
/// and the demo data. Every value is a Preference and takes effect at once.
/// </summary>
public sealed class SettingsPage : ContentPage
{
	public const string Route = "settings";
	public const string TopoKey = "map-topo";

	private readonly TripStore _store;
	private readonly Picker _units = new() { Title = L.T("Units"), ItemsSource = new[] { L.T("Kilometres and metres"), L.T("Miles and feet") } };
	private readonly Picker _language = new() { Title = L.T("Language"), ItemsSource = new[] { L.T("As the phone"), "English", "Polski" } };
	private readonly Switch _simulate = new();
	private readonly Switch _topo = new();

	public SettingsPage(TripStore store)
	{
		_store = store;
		Title = L.T("Settings");
		_units.SelectedIndex = Format.Units == Units.Imperial ? 1 : 0;
		_units.SelectedIndexChanged += (_, _) => Format.Units = _units.SelectedIndex == 1 ? Units.Imperial : Units.Metric;
		_simulate.IsToggled = RecordingService.Simulated;
		_simulate.Toggled += (_, e) => RecordingService.Simulated = e.Value;
		_topo.IsToggled = TopographicMaps;
		_topo.Toggled += (_, e) => TopographicMaps = e.Value;

		_language.SelectedIndex = L.Choice switch { "en" => 1, "pl" => 2, _ => 0 };
		_language.SelectedIndexChanged += async (_, _) =>
		{
			L.Choice = _language.SelectedIndex switch { 1 => "en", 2 => "pl", _ => "system" };
			await DisplayAlertAsync(L.T("Language"), L.T("The language changes when Trailhead starts again."), "OK");
		};
		var reset = new Button { Text = L.T("Generate the demo trips again") };
		reset.Clicked += async (_, _) =>
		{
			if (await SailfishRemorse.ExecuteAsync(L.T("Replacing the demo trips"), () => { }))
				await _store.RegenerateDemoAsync();
		};

		Content = new ScrollView
		{
			Content = new VerticalStackLayout
			{
				Padding = new Thickness(16, 8),
				Spacing = 18,
				Children =
				{
					_units,
					_language,
					Row(L.T("Replay a demo track instead of the GPS"), _simulate, L.T("For trying recording at a desk; the demo tour turns it on.")),
					Row(L.T("Topographic maps"), _topo, L.T("OpenTopoMap's contour lines instead of OpenStreetMap's streets.")),
					reset,
					About(),
				},
			},
		};
	}

	/// <summary>New maps start on OpenTopoMap (true) or OpenStreetMap.</summary>
	public static bool TopographicMaps
	{
		get => Preferences.Default.Get(TopoKey, true);
		set => Preferences.Default.Set(TopoKey, value);
	}

	private static View Row(string title, Switch toggle, string detail)
	{
		var label = new Label { Text = title, VerticalOptions = LayoutOptions.Center };
		var note = new Label { Text = detail, FontSize = 13 };
		note.SetDynamicResource(Label.TextColorProperty, ThemeResources.Secondary);
		var grid = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
		grid.Add(label);
		grid.Add(toggle, 1, 0);
		return new VerticalStackLayout { Spacing = 2, Children = { grid, note } };
	}

	private static View About()
	{
		var text = new Label
		{
			FontSize = 13,
			Text = $"Trailhead {AppInfo.Current.VersionString} ({AppFlavor.Name} build) · a .NET MAUI app on Sailfish OS.\n" +
				"Map data © OpenStreetMap contributors; topographic tiles OpenTopoMap (CC-BY-SA). The demo trips are generated, " +
				"along real places.",
		};
		text.SetDynamicResource(Label.TextColorProperty, ThemeResources.Secondary);
		return text;
	}
}
