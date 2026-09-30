using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.Maui.SailfishOS.Sample.Models;

namespace Microsoft.Maui.SailfishOS.Sample.Services;

/// <summary>
/// In-memory task store shared by all sample pages; raises <see cref="Changed"/> on any edit.
/// </summary>
public class TaskStore
{
	public ObservableCollection<TaskItem> Tasks { get; } = new();

	/// <summary>Raised on any change to the task collection or its items.</summary>
	public event Action? Changed;

	public TaskStore()
	{
		Seed();
		Tasks.CollectionChanged += OnTasksCollectionChanged;
		foreach (var task in Tasks)
			task.PropertyChanged += OnTaskChanged;
	}

	public int Total => Tasks.Count;

	public int Done => Tasks.Count(t => t.IsDone);

	public double Completion => Total == 0 ? 0 : Done / (double)Total;

	public void Add(string title, string category, double priority, string notes) =>
		Tasks.Insert(0, new TaskItem(title, category, priority, notes));

	public void Remove(TaskItem item) => Tasks.Remove(item);

	public IReadOnlyList<CategoryStat> GetCategoryStats() =>
		Tasks
			.GroupBy(t => t.Category)
			.Select(g => new CategoryStat(g.Key, g.Count(), g.Count(t => t.IsDone)))
			.OrderByDescending(s => s.Count)
			.ToList();

	private void OnTasksCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.NewItems is not null)
		{
			foreach (TaskItem task in e.NewItems)
				task.PropertyChanged += OnTaskChanged;
		}

		if (e.OldItems is not null)
		{
			foreach (TaskItem task in e.OldItems)
				task.PropertyChanged -= OnTaskChanged;
		}

		Raise();
	}

	private void OnTaskChanged(object? sender, PropertyChangedEventArgs e) => Raise();

	private void Raise() => Changed?.Invoke();

	private void Seed()
	{
		Tasks.Add(new TaskItem("Port SDL2 renderer to lipstick", "Harbour", 0.9,
			"Fullscreen Wayland surface through SDL2 with the software renderer; verify vsync and resize events.", true));
		Tasks.Add(new TaskItem("Package app as harbour RPM", "Harbour", 0.8,
			"rpmbuild staging with AutoReqProv: no, launcher symlink and .desktop file.", true));
		Tasks.Add(new TaskItem("Implement CollectionView rows", "Lipstick", 0.75,
			"Realize DataTemplate items into a clipped, drag-scrollable panel with row selection."));
		Tasks.Add(new TaskItem("Wire NavigationPage stack", "Lipstick", 0.7,
			"Push/pop pages from the platform title bar back chevron and item taps."));
		Tasks.Add(new TaskItem("Decode PNG without SDL_image", "Kernel", 0.6,
			"Managed zlib inflate + unfilter pipeline feeding an RGBA32 SDL texture."));
		Tasks.Add(new TaskItem("musl libc smoke tests", "Kernel", 0.55,
			"Confirm the self-contained linux-arm64 runtime boots under sailjail."));
		Tasks.Add(new TaskItem("Ambient theming pass", "Harbour", 0.4,
			"Follow highlight color and dark/light ambience in the platform views."));
		Tasks.Add(new TaskItem("Gesture back navigation", "Lipstick", 0.35,
			"Right-edge swipe pops the navigation stack like the native UI."));
		Tasks.Add(new TaskItem("Battery-friendly frame loop", "Kernel", 0.3,
			"Only redraw on demand; coalesce wakeups to ~60fps max."));
		Tasks.Add(new TaskItem("Publish to Jolla Store", "Harbour", 0.2,
			"Final harbour validation, screenshots and store listing."));
	}
}

/// <summary>Per-category completion stats shown by the StatsPage list.</summary>
public class CategoryStat
{
	public CategoryStat(string category, int count, int done)
	{
		Category = category;
		Count = count;
		Done = done;
	}

	public string Category { get; }

	public int Count { get; }

	public int Done { get; }

	public double Ratio => Count == 0 ? 0 : Done / (double)Count;

	public string Summary => $"{Done} of {Count} done";
}