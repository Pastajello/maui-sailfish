using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.SailfishOS.Sample.Models;

/// <summary>One task in the sample to-do store. Fully observable so XAML bindings update live.</summary>
public class TaskItem : INotifyPropertyChanged
{
	private string _title;
	private string _notes;
	private bool _isDone;
	private double _priority;

	public TaskItem(string title, string category, double priority, string notes, bool isDone = false)
	{
		_title = title;
		Category = category;
		_priority = priority;
		_notes = notes;
		_isDone = isDone;
	}

	public string Title
	{
		get => _title;
		set => Set(ref _title, value);
	}

	public string Notes
	{
		get => _notes;
		set => Set(ref _notes, value);
	}

	public string Category { get; }

	public bool IsDone
	{
		get => _isDone;
		set
		{
			if (Set(ref _isDone, value))
			{
				OnPropertyChanged(nameof(Subtitle));
				OnPropertyChanged(nameof(AccentColor));
			}
		}
	}

	public double Priority
	{
		get => _priority;
		set
		{
			if (Set(ref _priority, value))
				OnPropertyChanged(nameof(Subtitle));
		}
	}

	/// <summary>Second line in the task list row.</summary>
	public string Subtitle => $"{Category}  •  {(IsDone ? "done" : $"priority {Priority:P0}")}";

	/// <summary>Accent bar color: green-ish once done, category color otherwise.</summary>
	public Color AccentColor => IsDone
		? Color.FromArgb("#81C784")
		: Category switch
		{
			"Harbour" => Color.FromArgb("#4FC3F7"),
			"Lipstick" => Color.FromArgb("#BA68C8"),
			"Kernel" => Color.FromArgb("#FFB74D"),
			_ => Color.FromArgb("#90A4AE"),
		};

	public event PropertyChangedEventHandler? PropertyChanged;

	private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
	{
		if (EqualityComparer<T>.Default.Equals(field, value))
			return false;
		field = value;
		OnPropertyChanged(name ?? string.Empty);
		return true;
	}

	private void OnPropertyChanged(string name) =>
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
