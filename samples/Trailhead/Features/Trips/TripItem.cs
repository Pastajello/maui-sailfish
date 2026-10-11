using System.Collections.ObjectModel;
using Trailhead.Data;
using Trailhead.Features.Shared;

namespace Trailhead.Features.Trips;

/// <summary>A row of the trips list: the summary, already formatted.</summary>
public sealed class TripItem
{
	public TripItem(TripSummary summary, string? thumbnailPath)
	{
		Summary = summary;
		Thumbnail = thumbnailPath is null ? null : ImageSource.FromFile(thumbnailPath);
	}

	public TripSummary Summary { get; }

	public string Id => Summary.Id;

	public string Name => Summary.Name;

	public string Activity => Format.ActivityName(Summary.Activity).ToUpperInvariant();

	public string Facts => $"{Format.Distance(Summary.DistanceM)} · {Format.Climb(Summary.AscentM)} · {Format.Duration(Summary.MovingTime)}";

	public string Where => $"{Summary.Area} · {Format.DayAndTime(Summary.Start)}";

	public ImageSource? Thumbnail { get; }

	public bool HasPhotos => Summary.PhotoCount > 0;

	/// <summary>What a screen reader says for the row: the name and the facts in one sentence.</summary>
	public string Spoken => $"{Activity}: {Name}. {Facts}. {Where}";
}

/// <summary>A month of trips: the list's group, with its header text.</summary>
public sealed class TripGroup : ObservableCollection<TripItem>
{
	public TripGroup(DateTimeOffset month)
	{
		Month = new DateTimeOffset(month.Year, month.Month, 1, 0, 0, 0, month.Offset);
		Title = Format.Month(month);
	}

	public DateTimeOffset Month { get; }

	public string Title { get; }

	public string CountText => Count == 1 ? L.T("1 trip") : L.F("{0} trips", Count);
}

/// <summary>Rows with photos show the cover thumbnail; the others a compact text row.</summary>
public sealed class TripTemplateSelector : DataTemplateSelector
{
	public DataTemplate? WithPhoto { get; set; }

	public DataTemplate? Plain { get; set; }

	protected override DataTemplate? OnSelectTemplate(object item, BindableObject container) =>
		item is TripItem { HasPhotos: true } ? WithPhoto : Plain;
}
