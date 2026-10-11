namespace Trailhead.Data;

public static class Geo
{
	public const double EarthRadiusM = 6_371_000;

	public static double DistanceM(double lat1, double lon1, double lat2, double lon2)
	{
		var dLat = ToRad(lat2 - lat1);
		var dLon = ToRad(lon2 - lon1);
		var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
			Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
		return 2 * EarthRadiusM * Math.Asin(Math.Min(1, Math.Sqrt(a)));
	}

	public static double DistanceM(in TrackPoint a, in TrackPoint b) => DistanceM(a.Lat, a.Lon, b.Lat, b.Lon);

	/// <summary>Initial bearing from the first point to the second, degrees clockwise from north.</summary>
	public static double BearingDeg(double lat1, double lon1, double lat2, double lon2)
	{
		var y = Math.Sin(ToRad(lon2 - lon1)) * Math.Cos(ToRad(lat2));
		var x = Math.Cos(ToRad(lat1)) * Math.Sin(ToRad(lat2)) -
			Math.Sin(ToRad(lat1)) * Math.Cos(ToRad(lat2)) * Math.Cos(ToRad(lon2 - lon1));
		return (ToDeg(Math.Atan2(y, x)) + 360) % 360;
	}

	/// <summary>The point <paramref name="distanceM"/> from (lat, lon) along <paramref name="bearingDeg"/>.</summary>
	public static (double Lat, double Lon) Offset(double lat, double lon, double bearingDeg, double distanceM)
	{
		var d = distanceM / EarthRadiusM;
		var b = ToRad(bearingDeg);
		var la1 = ToRad(lat);
		var lo1 = ToRad(lon);
		var la2 = Math.Asin(Math.Sin(la1) * Math.Cos(d) + Math.Cos(la1) * Math.Sin(d) * Math.Cos(b));
		var lo2 = lo1 + Math.Atan2(Math.Sin(b) * Math.Sin(d) * Math.Cos(la1), Math.Cos(d) - Math.Sin(la1) * Math.Sin(la2));
		return (ToDeg(la2), ToDeg(lo2));
	}

	public static double ToRad(double deg) => deg * Math.PI / 180;

	public static double ToDeg(double rad) => rad * 180 / Math.PI;
}

public readonly record struct TripStats(
	double DistanceM,
	double AscentM,
	double DescentM,
	double MinEleM,
	double MaxEleM,
	TimeSpan Duration,
	TimeSpan MovingTime)
{
	/// <summary>Climb is counted once it exceeds this, so GPS altitude jitter does not add up.</summary>
	private const double ClimbHysteresisM = 3;

	/// <summary>Slower than this between two fixes counts as standing still.</summary>
	private const double MovingSpeedMps = 0.15;

	public double AverageSpeedMps => MovingTime.TotalSeconds > 0 ? DistanceM / MovingTime.TotalSeconds : 0;

	public static TripStats Compute(IReadOnlyList<TrackPoint> track)
	{
		if (track.Count == 0)
			return default;

		double distance = 0, ascent = 0, descent = 0, moving = 0;
		double min = track[0].Ele, max = track[0].Ele, anchor = track[0].Ele;
		for (var i = 1; i < track.Count; i++)
		{
			var step = Geo.DistanceM(track[i - 1], track[i]);
			var seconds = (track[i].Time - track[i - 1].Time).TotalSeconds;
			distance += step;
			if (seconds > 0 && step / seconds >= MovingSpeedMps)
				moving += seconds;

			var ele = track[i].Ele;
			min = Math.Min(min, ele);
			max = Math.Max(max, ele);
			if (ele - anchor >= ClimbHysteresisM)
			{
				ascent += ele - anchor;
				anchor = ele;
			}
			else if (anchor - ele >= ClimbHysteresisM)
			{
				descent += anchor - ele;
				anchor = ele;
			}
		}

		return new TripStats(distance, ascent, descent, min, max, track[^1].Time - track[0].Time, TimeSpan.FromSeconds(moving));
	}
}
