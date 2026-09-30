using Microsoft.Maui.Controls;

namespace Microsoft.Maui.SailfishOS.Sample.Pages;

/// <summary>
/// Gallery of text-input controls: Entry, Editor, SearchBar, focus and
/// keyboard behaviour.
/// </summary>
public partial class TextInputPage : ContentPage
{
	private int _changes;

	public TextInputPage()
	{
		InitializeComponent();
	}

	private void OnEntryTextChanged(object? sender, TextChangedEventArgs e) =>
		UpdateStatus($"changes: {++_changes} | completed: {NameEntry.Text}");

	private void OnEntryCompleted(object? sender, EventArgs e) =>
		UpdateStatus($"changes: {_changes} | completed: {NameEntry.Text}");

	private void OnSearchPressed(object? sender, EventArgs e) =>
		UpdateStatus($"changes: {_changes} | search: {QuerySearch.Text}");

	private void OnFocusClicked(object? sender, EventArgs e) =>
		NameEntry.Focus();

	private void OnUnfocusClicked(object? sender, EventArgs e) =>
		NameEntry.Unfocus();

	private void UpdateStatus(string detail) =>
		StatusLabel.Text = detail;
}
