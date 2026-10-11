using System.Text.Json;
using Trailhead.Data;
using Xunit;
using Xunit.Abstractions;

namespace Trailhead.Tests;

public class CoreTests(ITestOutputHelper output)
{
	[Fact]
	public void Demo_trips_are_plausible()
	{
		foreach (var route in DemoRoutes.All)
		{
			var trip = TripGenerator.Generate(route);
			var stats = TripStats.Compute(trip.Track);
			output.WriteLine($"{route.Id} {route.Activity,-4} {stats.DistanceM / 1000,6:0.0} km  +{stats.AscentM,5:0} m  " +
				$"{stats.Duration:hh\\:mm} total {stats.MovingTime:hh\\:mm} moving  {trip.Track.Count,5} pts  {trip.Photos.Count,2} photos");

			Assert.InRange(stats.DistanceM / 1000, 2, 60);
			Assert.InRange(stats.Duration.TotalHours, 0.3, 14);
			Assert.True(stats.MovingTime <= stats.Duration);
			Assert.InRange(trip.Photos.Count, 1, 16);
			Assert.Equal(route.Anchors.Length, trip.Waypoints.Count);
			Assert.True(trip.Track.Zip(trip.Track.Skip(1)).All(p => p.Second.Time > p.First.Time), "times increase");
			// The track starts at the first anchor.
			Assert.True(Geo.DistanceM(trip.Track[0].Lat, trip.Track[0].Lon, route.Anchors[0].Lat, route.Anchors[0].Lon) < 1);
		}
	}

	[Fact]
	public void Generation_is_deterministic()
	{
		var a = TripGenerator.Generate(DemoRoutes.All[0]);
		var b = TripGenerator.Generate(DemoRoutes.All[0]);
		Assert.Equal(a.Track, b.Track);
		Assert.Equal(a.Photos, b.Photos);
	}

	[Fact]
	public void Gpx_round_trips()
	{
		var trip = TripGenerator.Generate(DemoRoutes.All[3]);
		var back = Gpx.Read(Gpx.Write(trip), "imported");

		Assert.Equal(trip.Name, back.Name);
		Assert.Equal(trip.Activity, back.Activity);
		Assert.Equal(trip.Track.Count, back.Track.Count);
		Assert.Equal(trip.Waypoints.Count, back.Waypoints.Count);
		var s1 = TripStats.Compute(trip.Track);
		var s2 = TripStats.Compute(back.Track);
		Assert.InRange(Math.Abs(s1.DistanceM - s2.DistanceM), 0, 1);
		Assert.Equal(s1.Duration, s2.Duration);
	}

	[Fact]
	public void Gpx_without_times_or_tracks()
	{
		const string routeOnly = """
			<gpx version="1.0"><rte><rtept lat="49.1" lon="20.0"><ele>1000</ele></rtept><rtept lat="49.2" lon="20.1"/></rte></gpx>
			""";
		var trip = Gpx.Read(routeOnly, "r");
		Assert.Equal(2, trip.Track.Count);
		Assert.Equal(TimeSpan.FromSeconds(1), trip.End - trip.Start);
		Assert.Throws<FormatException>(() => Gpx.Read("<gpx/>", "x"));
	}

	[Fact]
	public void Json_round_trips_with_the_source_generated_context()
	{
		var trip = TripGenerator.Generate(DemoRoutes.All[1]);
		var json = JsonSerializer.Serialize(trip, TrailheadJson.Default.Trip);
		var back = JsonSerializer.Deserialize(json, TrailheadJson.Default.Trip)!;
		Assert.Equal(trip.Track, back.Track);
		Assert.Contains("\"Hike\"", json);
	}

	[Fact]
	public void Stats_ignore_altitude_jitter()
	{
		var t0 = DateTimeOffset.UnixEpoch;
		var flat = Enumerable.Range(0, 100)
			.Select(i => new TrackPoint(50, 20 + i * 0.0001, 100 + (i % 2 == 0 ? 1 : -1), t0.AddSeconds(i * 5)))
			.ToList();
		Assert.Equal(0, TripStats.Compute(flat).AscentM);
	}

	[Fact]
	public void Stress_trips_are_valid()
	{
		var trip = TripGenerator.GenerateSynthetic(42, points: 50_000);
		Assert.Equal(50_000, trip.Track.Count);
		Assert.True(TripStats.Compute(trip.Track).DistanceM > 100_000);
	}

	[Fact]
	public void Nearest_place()
	{
		var (place, distance) = DemoRoutes.Nearest(49.2320, 19.9815);
		Assert.Equal("Kasprowy Wierch", place.Name);
		Assert.True(distance < 50);
	}
}
