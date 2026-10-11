namespace Trailhead.Data;

/// <summary>
/// Turns a <see cref="DemoRoute"/> into a plausible recorded trip: a winding track through the anchors, an elevation
/// profile, fix times from a slope-dependent pace (Tobler's hiking function and per-activity variants), breaks,
/// waypoints and photo positions. Deterministic: the same route and seed give the same trip.
/// </summary>
public static class TripGenerator
{
	private const double SpacingM = 15;

	public static Trip Generate(DemoRoute route, int seed = 2026)
	{
		var rng = new Random(HashCode.Combine(seed, StableHash(route.Id)));
		var trip = new Trip
		{
			Id = route.Id,
			Name = route.Name,
			Area = route.Area,
			Activity = route.Activity,
			Notes = route.Notes,
		};

		// Geometry: legs between anchors, then the way back for out-and-back routes.
		var path = new List<(double Lat, double Lon, double Ele, int Anchor)>();
		for (var i = 0; i + 1 < route.Anchors.Length; i++)
			AppendLeg(path, route.Anchors[i], route.Anchors[i + 1], i, rng, route.Activity, first: i == 0);
		if (route.OutAndBack)
		{
			for (var i = path.Count - 2; i >= 0; i--)
			{
				var p = path[i];
				var (lat, lon) = Geo.Offset(p.Lat, p.Lon, rng.NextDouble() * 360, rng.NextDouble() * 4);
				path.Add((lat, lon, p.Ele + (rng.NextDouble() - 0.5) * 2, p.Anchor >= 0 ? p.Anchor + 1000 : -1));
			}
		}

		// Time: walk the path at the activity's pace, stopping at anchors with a break.
		// Fix times are whole seconds, as a GPS logs them (and GPX keeps them).
		var pace = 0.9 + rng.NextDouble() * 0.2;
		var elapsed = 0.0;
		var time = route.Start;
		for (var i = 0; i < path.Count; i++)
		{
			var p = path[i];
			if (i > 0)
			{
				var prev = path[i - 1];
				var dist = Math.Max(0.5, Geo.DistanceM(prev.Lat, prev.Lon, p.Lat, p.Lon));
				var speedKmh = SpeedKmh(route.Activity, (p.Ele - prev.Ele) / dist) * pace;
				elapsed += Math.Max(1, dist / (speedKmh / 3.6));
				time = route.Start.AddSeconds(Math.Round(elapsed));
			}
			trip.Track.Add(new TrackPoint(Math.Round(p.Lat, 7), Math.Round(p.Lon, 7), Math.Round(p.Ele, 1), time));

			if (p.Anchor >= 0 && p.Anchor < 1000)
			{
				var anchor = route.Anchors[p.Anchor];
				trip.Waypoints.Add(new Waypoint(anchor.Lat, anchor.Lon, anchor.Name, time));
				if (anchor.StopMinutes > 0)
				{
					// Standing still: a few fixes wandering a couple of metres.
					var stop = time.AddMinutes(anchor.StopMinutes);
					elapsed += anchor.StopMinutes * 60;
					while ((time = time.AddSeconds(60)) < stop)
					{
						var (lat, lon) = Geo.Offset(p.Lat, p.Lon, rng.NextDouble() * 360, rng.NextDouble() * 3);
						trip.Track.Add(new TrackPoint(Math.Round(lat, 7), Math.Round(lon, 7), Math.Round(p.Ele, 1), time));
					}
				}
			}
		}

		AddPhotos(trip, route, rng);
		return trip;
	}

	/// <summary>A trip for the stress set: a random loop of <paramref name="points"/> fixes near one of the demo areas.</summary>
	public static Trip GenerateSynthetic(int index, int points = 600)
	{
		var rng = new Random(index * 7919 + 17);
		var home = DemoRoutes.Gazetteer[rng.Next(DemoRoutes.Gazetteer.Length)];
		var activity = (Activity)rng.Next(4);
		var start = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero)
			.AddDays(-rng.Next(0, 3650)).AddMinutes(rng.Next(0, 600));
		var trip = new Trip
		{
			Id = $"stress-{index:00000}",
			Name = $"{activity} #{index} near {home.Name}",
			Area = home.Name,
			Activity = activity,
			Notes = "Generated for the stress set.",
		};

		double lat = home.Lat, lon = home.Lon, ele = home.Ele, heading = rng.NextDouble() * 360;
		var time = start;
		for (var i = 0; i < points; i++)
		{
			heading += (rng.NextDouble() - 0.5) * 30;
			var step = SpacingM * (0.6 + rng.NextDouble() * 0.8);
			(lat, lon) = Geo.Offset(lat, lon, heading, step);
			var dEle = (rng.NextDouble() - 0.5) * 4;
			ele = Math.Max(0, ele + dEle);
			time = time.AddSeconds(Math.Max(1, Math.Round(step / (SpeedKmh(activity, dEle / step) / 3.6))));
			trip.Track.Add(new TrackPoint(lat, lon, ele, time));
		}
		return trip;
	}

	/// <summary>Speed for a slope (rise over run). Hike: Tobler; run: about twice that; bike: a road bike's
	/// sensitivity to climbs; ski: skinning up slowly, fast down.</summary>
	public static double SpeedKmh(Activity activity, double slope)
	{
		var tobler = 6 * Math.Exp(-3.5 * Math.Abs(slope + 0.05));
		return activity switch
		{
			Activity.Run => Math.Min(14, tobler * 2.1),
			Activity.Bike => Math.Clamp(20 * (1 - 6 * slope), 7, 40),
			Activity.Ski => slope > 0.01 ? Math.Max(1.5, 3 * Math.Exp(-2 * slope)) : Math.Clamp(-slope * 120, 6, 30),
			_ => Math.Max(0.8, tobler),
		};
	}

	private static void AppendLeg(List<(double, double, double, int)> path, RouteAnchor from, RouteAnchor to, int fromIndex,
		Random rng, Activity activity, bool first)
	{
		var straight = Geo.DistanceM(from.Lat, from.Lon, to.Lat, to.Lon);
		var bearing = Geo.BearingDeg(from.Lat, from.Lon, to.Lat, to.Lon);
		var steep = Math.Abs(to.Ele - from.Ele) / Math.Max(1, straight);
		// Trails wind more where it is steep (switchbacks); bikes and flat paths less.
		// The bend amplitude is a share of the bend wavelength, so a sine path is ~(1 + (π·share)²) times the straight line.
		var winding = activity == Activity.Bike ? 0.05 : 0.08 + Math.Min(0.1, steep * 0.6);
		var bends = Math.Max(1, (int)(straight / 450));
		var amplitude = straight / bends * winding;
		var count = Math.Max(4, (int)(straight * (1 + Math.Pow(Math.PI * winding, 2)) / SpacingM));
		var noise = SmoothNoise(rng, count + 1, knotEvery: 12);
		var eleNoise = SmoothNoise(rng, count + 1, knotEvery: 8);
		var phase = rng.NextDouble() * Math.PI;

		for (var k = first ? 0 : 1; k <= count; k++)
		{
			var t = (double)k / count;
			var envelope = Math.Sin(Math.PI * t);
			var lateral = amplitude * envelope * (Math.Sin(2 * Math.PI * bends * t + phase) + 0.6 * noise[k]);
			var (lat, lon) = Geo.Offset(from.Lat, from.Lon, bearing, straight * t);
			(lat, lon) = Geo.Offset(lat, lon, bearing + 90, lateral);
			var smooth = t * t * (3 - 2 * t);
			var ele = from.Ele + (to.Ele - from.Ele) * smooth + eleNoise[k] * 6 * envelope;
			var anchor = k == 0 ? fromIndex : k == count ? fromIndex + 1 : -1;
			path.Add((lat, lon, ele, anchor));
		}
	}

	private static void AddPhotos(Trip trip, DemoRoute route, Random rng)
	{
		var count = route.Activity switch
		{
			Activity.Hike => rng.Next(6, 16),
			Activity.Ski => rng.Next(5, 10),
			_ => rng.Next(3, 7),
		};
		var indices = Enumerable.Range(0, count)
			.Select(_ => rng.Next(1, trip.Track.Count - 1))
			.Distinct()
			.Order()
			.ToList();
		var n = 1;
		foreach (var i in indices)
		{
			var p = trip.Track[i];
			var (place, distance) = DemoRoutes.Nearest(p.Lat, p.Lon);
			var caption = distance < 150 ? $"At {place.Name}" : rng.Next(3) switch
			{
				0 => $"On the way, {distance / 1000:0.0} km from {place.Name}",
				1 => $"View towards {place.Name}",
				_ => $"Trail near {place.Name}",
			};
			trip.Photos.Add(new TripPhoto($"p{n++:00}.jpg", p.Lat, p.Lon, p.Time, caption));
		}
	}

	/// <summary>Values in [-1, 1] that change smoothly: random knots, cosine-interpolated.</summary>
	private static double[] SmoothNoise(Random rng, int length, int knotEvery)
	{
		var knots = new double[length / knotEvery + 2];
		for (var i = 0; i < knots.Length; i++)
			knots[i] = rng.NextDouble() * 2 - 1;
		var values = new double[length];
		for (var i = 0; i < length; i++)
		{
			var k = i / knotEvery;
			var f = (1 - Math.Cos(Math.PI * (i % knotEvery) / knotEvery)) / 2;
			values[i] = knots[k] * (1 - f) + knots[k + 1] * f;
		}
		return values;
	}

	private static int StableHash(string s)
	{
		var h = 17;
		foreach (var c in s)
			h = h * 31 + c;
		return h;
	}
}
