using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Trailhead.Data;
using Trailhead.Features.TripDetail;

namespace Trailhead.Features.Photos;

/// <summary>A trip's photos, full size, one per carousel position. Query: <c>id</c>, <c>index</c>.</summary>
public partial class PhotoViewerViewModel(TripStore store) : ObservableObject, IQueryAttributable
{
	private string _id = "";
	private int _start;

	public ObservableCollection<PhotoItem> Photos { get; } = new();

	[ObservableProperty]
	public partial int Position { get; set; }

	[ObservableProperty]
	public partial string Title { get; set; } = "";

	[ObservableProperty]
	public partial string Caption { get; set; } = "";

	public void ApplyQueryAttributes(IDictionary<string, object> query)
	{
		_id = query.TryGetValue("id", out var id) ? id.ToString() ?? "" : "";
		_start = query.TryGetValue("index", out var index) && int.TryParse(index.ToString(), out var i) ? i : 0;
	}

	public async Task LoadAsync()
	{
		if (Photos.Count > 0 || await store.LoadAsync(_id) is not { } trip)
			return;
		for (var i = 0; i < trip.Photos.Count; i++)
		{
			var p = trip.Photos[i];
			Photos.Add(new PhotoItem(p, store.PhotoPath(trip.Id, p.File), store.PhotoPath(trip.Id, p.ThumbnailFile), i));
		}
		Position = Math.Clamp(_start, 0, Math.Max(0, Photos.Count - 1));
		OnPositionChanged(Position);
	}

	partial void OnPositionChanged(int value)
	{
		if (value < 0 || value >= Photos.Count)
			return;
		Title = $"{value + 1} / {Photos.Count}";
		Caption = $"{Photos[value].Caption}\n{Photos[value].When}";
	}

	public PhotoItem? Current => Position >= 0 && Position < Photos.Count ? Photos[Position] : null;
}
