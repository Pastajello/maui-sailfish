using Microsoft.Maui.Controls;
using Microsoft.Maui.SailfishOS.Sample.Models;
using Microsoft.Maui.SailfishOS.Sample.Services;

namespace Microsoft.Maui.SailfishOS.Sample.Pages;

/// <summary>
/// Detail page for a <see cref="TaskItem"/>, bound two-way; Delete edits the store before popping.
/// </summary>
public partial class TaskDetailPage : ContentPage
{
	private readonly TaskItem _task;
	private readonly TaskStore _store;

	public TaskDetailPage(TaskItem task, TaskStore store)
	{
		_task = task;
		_store = store;
		InitializeComponent();
		BindingContext = task;
	}

	private async void OnDeleteClicked(object? sender, EventArgs e)
	{
		_store.Remove(_task);
		await Navigation.PopAsync();
	}

	private async void OnBackClicked(object? sender, EventArgs e) =>
		await Navigation.PopAsync();
}