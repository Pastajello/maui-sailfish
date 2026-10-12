using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trailhead.Data;
using Trailhead.Features.Shared;

namespace Trailhead.Features.TripEdit;

/// <summary>Edits a trip's name, activity, notes and start (moving the start shifts every fix, waypoint and photo).
/// Query: <c>id</c>.</summary>
public partial class TripEditViewModel(TripStore store) : ObservableObject, IQueryAttributable
{
	private string _id = "";
	private Trip? _trip;

	/// <summary>The trip being edited (tour checks).</summary>
	public string Id => _id;

	public IReadOnlyList<string> Activities { get; } = Enum.GetValues<Activity>().Select(Format.ActivityName).ToList();

	public DateTime Today => DateTime.Today;

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(SaveCommand))]
	public partial string Name { get; set; } = "";

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(SaveCommand))]
	public partial bool IsNameValid { get; set; } = true;

	[ObservableProperty]
	public partial int ActivityIndex { get; set; }

	[ObservableProperty]
	public partial string Notes { get; set; } = "";

	[ObservableProperty]
	public partial DateTime Date { get; set; } = DateTime.Today;

	[ObservableProperty]
	public partial TimeSpan StartTime { get; set; }

	public void ApplyQueryAttributes(IDictionary<string, object> query) =>
		_id = query.TryGetValue("id", out var id) ? id.ToString() ?? "" : "";

	public async Task LoadAsync()
	{
		if (_trip is not null || await store.LoadAsync(_id) is not { } trip)
			return;
		_trip = trip;
		Name = trip.Name;
		ActivityIndex = (int)trip.Activity;
		Notes = trip.Notes;
		Date = trip.Start.Date;
		StartTime = trip.Start.TimeOfDay;
	}

	private bool CanSave() => _trip is not null && IsNameValid && !string.IsNullOrWhiteSpace(Name);

	[RelayCommand(CanExecute = nameof(CanSave))]
	private async Task SaveAsync()
	{
		if (_trip is not { } trip)
			return;
		trip.Name = Name.Trim();
		trip.Activity = (Activity)Math.Max(0, ActivityIndex);
		trip.Notes = Notes.Trim();

		var start = new DateTimeOffset(Date.Date + StartTime, trip.Start.Offset);
		var shift = start - trip.Start;
		if (shift != TimeSpan.Zero)
		{
			trip.Track = trip.Track.Select(p => p with { Time = p.Time + shift }).ToList();
			trip.Waypoints = trip.Waypoints.Select(w => w with { Time = w.Time + shift }).ToList();
			trip.Photos = trip.Photos.Select(p => p with { Time = p.Time + shift }).ToList();
		}
		await store.SaveAsync(trip);
		await Shell.Current.GoToAsync("..");
	}

	[RelayCommand]
	private Task CancelAsync() => Shell.Current.GoToAsync("..");
}
