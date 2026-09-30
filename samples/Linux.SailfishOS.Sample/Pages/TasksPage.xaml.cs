using Microsoft.Maui.Controls;
using Microsoft.Maui.SailfishOS.Platform;
using Microsoft.Maui.SailfishOS.Sample.Models;
using Microsoft.Maui.SailfishOS.Sample.Services;

namespace Microsoft.Maui.SailfishOS.Sample.Pages;

/// <summary>
/// Home page: header with live progress, buttons that push secondary pages, and a
/// <see cref="CollectionView"/> of tasks that open the detail page.
/// </summary>
public partial class TasksPage : ContentPage
{
	private static readonly string[] SampleTitles =
	{
		"Review harbour manifest",
		"Retest sailjail sandboxing",
		"Tune frame pacing",
		"Refresh store screenshots",
		"Rebase on latest MAUI",
	};

	private static readonly string[] SampleCategories = { "Harbour", "Lipstick", "Kernel" };

	private readonly TaskStore _store;
	private int _addCounter;

	// Fed by the title label's gesture recognizers (real Qt pointer events).
	private int _titleTaps;
	private double _panX, _panY;

	public TasksPage(TaskStore store)
	{
		_store = store;
		InitializeComponent();

		TasksList.ItemsSource = _store.Tasks;
		_store.Changed += UpdateSummary;
		// The home-screen cover: progress, next open task and an "add task" action.
		SailfishCover.SetActions(new SailfishCoverAction("image://theme/icon-cover-new", () => OnAddTaskClicked(null, EventArgs.Empty)));
		UpdateSummary();
	}

	private void UpdateSummary()
	{
		SummaryLabel.Text = $"{_store.Done} of {_store.Total} tasks done";
		OverallProgress.Progress = _store.Completion;
		var next = _store.Tasks.FirstOrDefault(t => !t.IsDone);
		SailfishCover.SetContent("Sailfish Tasks", $"{_store.Done} of {_store.Total} done",
			next is null ? "All done!" : "Next: " + next.Title);
	}

	private void OnTitleTapped(object? sender, TappedEventArgs e)
	{
		_titleTaps++;
		UpdateInputStatus();
	}

	private void OnTitlePanned(object? sender, PanUpdatedEventArgs e)
	{
		if (e.StatusType == GestureStatus.Running)
		{
			_panX = e.TotalX;
			_panY = e.TotalY;
			UpdateInputStatus();
		}
	}

	private void UpdateInputStatus() =>
		InputStatusLabel.Text = $"input: taps={_titleTaps} pan={_panX:0},{_panY:0}";

	private async void OnTaskSelected(object? sender, SelectionChangedEventArgs e)
	{
		if (e.CurrentSelection.FirstOrDefault() is not TaskItem task)
			return;

		// Reset so tapping the same row again navigates a second time.
		TasksList.SelectedItem = null;
		await Navigation.PushAsync(new TaskDetailPage(task, _store));
	}

	private void OnAddTaskClicked(object? sender, EventArgs e)
	{
		var title = SampleTitles[_addCounter % SampleTitles.Length];
		var category = SampleCategories[_addCounter % SampleCategories.Length];
		var priority = ((_addCounter * 37) % 90 + 10) / 100.0;
		_addCounter++;

		_store.Add(title, category, priority, "Added from the device sample at runtime.");
	}

	private async void OnStatsClicked(object? sender, EventArgs e) =>
		await Navigation.PushAsync(new StatsPage(_store));

	private async void OnControlsClicked(object? sender, EventArgs e) =>
		await Navigation.PushAsync(new ControlsPage());

	private async void OnAboutClicked(object? sender, EventArgs e) =>
		await Navigation.PushAsync(new AboutPage());

	private async void OnFeaturesClicked(object? sender, EventArgs e) =>
		await Navigation.PushAsync(new FeaturesPage());
}