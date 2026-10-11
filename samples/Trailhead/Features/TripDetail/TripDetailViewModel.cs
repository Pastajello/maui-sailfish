using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Maui.SailfishOS.Platform;
using Trailhead.Data;
using Trailhead.Features.Shared;

namespace Trailhead.Features.TripDetail;

public sealed record StatCell(string Value, string Label);

public sealed class PhotoItem(TripPhoto photo, string fullPath, string thumbPath, int index)
{
	public TripPhoto Photo { get; } = photo;
	public int Index { get; } = index;
	public string FullPath { get; } = fullPath;
	public ImageSource Thumbnail { get; } = ImageSource.FromFile(thumbPath);
	public ImageSource Full { get; } = ImageSource.FromFile(fullPath);
	public string Caption => Photo.Caption;
	public string When => Format.DayAndTime(Photo.Time);
}

/// <summary>One trip: header facts, statistics, notes and the photo grid. Query: <c>id</c>.</summary>
public partial class TripDetailViewModel(TripStore store) : ObservableObject, IQueryAttributable
{
	public string Id { get; private set; } = "";

	public Trip? Trip { get; private set; }

	public ObservableCollection<StatCell> Stats { get; } = new();

	public ObservableCollection<PhotoItem> Photos { get; } = new();

	[ObservableProperty]
	public partial string Name { get; set; } = "";

	[ObservableProperty]
	public partial string Subtitle { get; set; } = "";

	[ObservableProperty]
	public partial string Notes { get; set; } = "";

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasHero))]
	public partial ImageSource? Hero { get; set; }

	/// <summary>A trip without photos has no header picture (and no empty space for one).</summary>
	public bool HasHero => Hero is not null;

	[ObservableProperty]
	public partial string PhotosHeader { get; set; } = "";

	[ObservableProperty]
	public partial IReadOnlyList<TrackPoint>? Track { get; set; }

	/// <summary>Distance and height under the finger on the elevation profile.</summary>
	[ObservableProperty]
	public partial string ScrubText { get; set; } = L.T("Touch the profile to read it");

	public void ApplyQueryAttributes(IDictionary<string, object> query)
	{
		if (query.TryGetValue("id", out var id))
			Id = Uri.UnescapeDataString(id.ToString() ?? "");
	}

	public async Task LoadAsync()
	{
		if (await store.LoadAsync(Id) is not { } trip)
			return;
		Trip = trip;
		var stats = TripStats.Compute(trip.Track);
		Name = trip.Name;
		Subtitle = $"{Format.ActivityName(trip.Activity)} · {trip.Area} · {Format.Day(trip.Start)}";
		Notes = trip.Notes;
		Track = trip.Track;

		Stats.Clear();
		Stats.Add(new(Format.Distance(stats.DistanceM), L.T("distance")));
		Stats.Add(new(Format.Duration(stats.MovingTime), L.T("moving")));
		Stats.Add(new(Format.Pace(trip.Activity, stats.AverageSpeedMps), L.T(trip.Activity is Activity.Bike or Activity.Ski ? "average" : "pace")));
		Stats.Add(new(Format.Climb(stats.AscentM), L.T("climb")));
		Stats.Add(new(Format.Height(stats.MaxEleM), L.T("highest")));
		Stats.Add(new(Format.Duration(stats.Duration), L.T("total")));

		Photos.Clear();
		for (var i = 0; i < trip.Photos.Count; i++)
		{
			var p = trip.Photos[i];
			Photos.Add(new PhotoItem(p, store.PhotoPath(trip.Id, p.File), store.PhotoPath(trip.Id, p.ThumbnailFile), i));
		}
		Hero = Photos.FirstOrDefault()?.Full;
		PhotosHeader = Photos.Count switch { 0 => L.T("No photos"), 1 => L.T("1 photo"), var n => L.F("{0} photos", n) };

		SailfishCover.SetContent(trip.Name, Format.Distance(stats.DistanceM), Format.Duration(stats.MovingTime));
	}

	public Task DeleteAsync() => store.DeleteAsync(Id);

	/// <summary>
	/// Copies picked photos into the trip (plan T13). Each goes on the track where the trip was at the file's time, or,
	/// for a file from outside the trip (a copy, a download), spread evenly along the track; Skia reads no EXIF GPS, so
	/// the time is all there is. Returns how many were added.
	/// </summary>
	public async Task<int> ImportPhotosAsync(IReadOnlyList<string> paths)
	{
		if (Trip is not { Track.Count: > 0 } trip || paths.Count == 0)
			return 0;
		var added = await Task.Run(() =>
		{
			var count = 0;
			foreach (var (source, i) in paths.Select((p, i) => (p, i)))
			{
				if (!File.Exists(source))
					continue;
				var taken = new DateTimeOffset(File.GetLastWriteTime(source));
				var at = taken >= trip.Start && taken <= trip.End
					? trip.Track.MinBy(p => Math.Abs((p.Time - taken).Ticks))!
					: trip.Track[(int)((i + 1L) * (trip.Track.Count - 1) / (paths.Count + 1))];
				var file = $"import-{Guid.NewGuid():N}"[..15] + Path.GetExtension(source).ToLowerInvariant();
				var (place, _) = DemoRoutes.Nearest(at.Lat, at.Lon);
				var photo = new TripPhoto(file, at.Lat, at.Lon, at.Time, L.F("Near {0}", place.Name));
				var target = store.PhotoPath(trip.Id, file);
				Directory.CreateDirectory(Path.GetDirectoryName(target)!);
				File.Copy(source, target);
				PhotoThumbnails.Make(target, store.PhotoPath(trip.Id, photo.ThumbnailFile));
				trip.Photos.Add(photo);
				count++;
			}
			trip.Photos.Sort((a, b) => a.Time.CompareTo(b.Time));
			return count;
		});
		if (added > 0)
		{
			await store.SaveAsync(trip);
			await LoadAsync();
		}
		return added;
	}
}
