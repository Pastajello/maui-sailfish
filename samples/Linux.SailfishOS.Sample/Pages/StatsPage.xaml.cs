using System.Collections.ObjectModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.SailfishOS.Sample.Services;

namespace Microsoft.Maui.SailfishOS.Sample.Pages;

/// <summary>
/// Live statistics over the shared store: totals in the header plus a second
/// (non-selectable) CollectionView listing per-category completion.
/// </summary>
public partial class StatsPage : ContentPage
{
	private readonly TaskStore _store;
	private readonly ObservableCollection<CategoryStat> _stats = new();

	public StatsPage(TaskStore store)
	{
		_store = store;
		InitializeComponent();

		StatsList.ItemsSource = _stats;
		_store.Changed += Refresh;

		// Mirror labels that change only when native adapter events reach the MAUI controls.
		AdapterEntry.TextChanged += (_, e) => EntryMirrorLabel.Text = $"Entry mirror: {e.NewTextValue}";
		AdapterSwitch.Toggled += (_, e) => SwitchStateLabel.Text = $"Switch adapter: {(e.Value ? "ON" : "OFF")}";
		AdapterSlider.ValueChanged += (_, e) => SliderValueLabel.Text = $"Slider: {e.NewValue:F1}";

		// Witness label driven only by the text-input events the Qt bridge must raise.
		AdapterEntry.Completed += (_, _) =>
			TextWitnessLabel.Text = $"text-input: completed '{AdapterEntry.Text}'";
		AdapterEntry.Focused += (_, _) =>
			TextWitnessLabel.Text = "text-input: entry focused";
		AdapterEntry.Unfocused += (_, _) =>
			TextWitnessLabel.Text = "text-input: entry unfocused";
		AdapterEditor.TextChanged += (_, e) =>
			TextWitnessLabel.Text = $"text-input: editor '{e.NewTextValue?.Replace("\n", "\\n")}'";

		Refresh();
	}

	private void Refresh()
	{
		HeadlineLabel.Text = $"{_store.Done} / {_store.Total} tasks completed";
		CompletionLabel.Text = $"Overall completion {_store.Completion:P0}";
		CompletionBar.Progress = _store.Completion;

		_stats.Clear();
		foreach (var stat in _store.GetCategoryStats())
			_stats.Add(stat);
	}
}
