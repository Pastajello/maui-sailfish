using System.Text.Json.Serialization;

namespace Trailhead.Data;

public enum Activity
{
	Hike,
	Run,
	Bike,
	Ski,
}

/// <summary>One GPS fix of a track: degrees, metres above sea level, time of the fix.</summary>
public readonly record struct TrackPoint(double Lat, double Lon, double Ele, DateTimeOffset Time);

public sealed record Waypoint(double Lat, double Lon, string Name, DateTimeOffset Time);

/// <summary>A photo of a trip. <see cref="File"/> is relative to the trip's photo folder; the thumbnail is
/// <c>&lt;name&gt;_t.jpg</c> next to it.</summary>
public sealed record TripPhoto(string File, double Lat, double Lon, DateTimeOffset Time, string Caption)
{
	[JsonIgnore]
	public string ThumbnailFile => Path.GetFileNameWithoutExtension(File) + "_t" + Path.GetExtension(File);
}

public sealed class Trip
{
	public string Id { get; set; } = "";
	public string Name { get; set; } = "";
	public string Area { get; set; } = "";
	public Activity Activity { get; set; }
	public string Notes { get; set; } = "";
	public List<TrackPoint> Track { get; set; } = new();
	public List<Waypoint> Waypoints { get; set; } = new();
	public List<TripPhoto> Photos { get; set; } = new();

	[JsonIgnore]
	public DateTimeOffset Start => Track.Count > 0 ? Track[0].Time : default;

	[JsonIgnore]
	public DateTimeOffset End => Track.Count > 0 ? Track[^1].Time : default;
}

/// <summary>What the list shows of a trip, kept in the index so the list never loads a track.</summary>
public sealed record TripSummary(
	string Id,
	string Name,
	string Area,
	Activity Activity,
	DateTimeOffset Start,
	TimeSpan Duration,
	TimeSpan MovingTime,
	double DistanceM,
	double AscentM,
	double MaxEleM,
	int PhotoCount,
	string? CoverPhoto,
	double Lat,
	double Lon)
{
	public static TripSummary Of(Trip trip)
	{
		var stats = TripStats.Compute(trip.Track);
		var first = trip.Track.Count > 0 ? trip.Track[0] : default;
		return new TripSummary(trip.Id, trip.Name, trip.Area, trip.Activity, trip.Start, stats.Duration, stats.MovingTime,
			stats.DistanceM, stats.AscentM, stats.MaxEleM, trip.Photos.Count, trip.Photos.FirstOrDefault()?.File,
			first.Lat, first.Lon);
	}
}
