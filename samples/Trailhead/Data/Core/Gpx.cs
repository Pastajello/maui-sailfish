using System.Globalization;
using System.Xml.Linq;

namespace Trailhead.Data;

/// <summary>GPX 1.1: one track (all segments joined) plus waypoints. Photos and notes stay in the app.</summary>
public static class Gpx
{
	private static readonly XNamespace Ns = "http://www.topografix.com/GPX/1/1";

	public static string Write(Trip trip)
	{
		var trk = new XElement(Ns + "trk",
			new XElement(Ns + "name", trip.Name),
			new XElement(Ns + "type", trip.Activity.ToString().ToLowerInvariant()),
			new XElement(Ns + "trkseg", trip.Track.Select(p => new XElement(Ns + "trkpt",
				new XAttribute("lat", F(p.Lat)), new XAttribute("lon", F(p.Lon)),
				new XElement(Ns + "ele", p.Ele.ToString("0.0", CultureInfo.InvariantCulture)),
				new XElement(Ns + "time", p.Time.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))))));

		var gpx = new XElement(Ns + "gpx",
			new XAttribute("version", "1.1"),
			new XAttribute("creator", "Trailhead"),
			new XElement(Ns + "metadata",
				new XElement(Ns + "name", trip.Name),
				new XElement(Ns + "desc", trip.Area)),
			trip.Waypoints.Select(w => new XElement(Ns + "wpt",
				new XAttribute("lat", F(w.Lat)), new XAttribute("lon", F(w.Lon)),
				new XElement(Ns + "time", w.Time.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
				new XElement(Ns + "name", w.Name))),
			trk);

		return new XDocument(new XDeclaration("1.0", "UTF-8", null), gpx).ToString();
	}

	/// <summary>Reads GPX 1.0 or 1.1. Points without a time get one second apart from the previous one, so the
	/// statistics stay defined. Throws <see cref="FormatException"/> when there is no track at all.</summary>
	public static Trip Read(string xml, string id)
	{
		var doc = XDocument.Parse(xml);
		var root = doc.Root ?? throw new FormatException("Empty GPX document");
		var ns = root.Name.Namespace;

		var trip = new Trip
		{
			Id = id,
			Name = (string?)root.Element(ns + "metadata")?.Element(ns + "name")
				?? (string?)root.Element(ns + "trk")?.Element(ns + "name")
				?? "Imported trip",
			Area = (string?)root.Element(ns + "metadata")?.Element(ns + "desc") ?? "Imported",
			Activity = ParseActivity((string?)root.Element(ns + "trk")?.Element(ns + "type")),
		};

		DateTimeOffset last = DateTimeOffset.UnixEpoch;
		foreach (var pt in root.Descendants(ns + "trkpt").Concat(root.Descendants(ns + "rtept")))
		{
			var time = Time(pt.Element(ns + "time")) ?? last.AddSeconds(1);
			last = time;
			trip.Track.Add(new TrackPoint(D(pt.Attribute("lat")), D(pt.Attribute("lon")),
				(double?)Number(pt.Element(ns + "ele")) ?? 0, time));
		}
		if (trip.Track.Count == 0)
			throw new FormatException("The GPX file has no track or route points");

		foreach (var w in root.Elements(ns + "wpt"))
			trip.Waypoints.Add(new Waypoint(D(w.Attribute("lat")), D(w.Attribute("lon")),
				(string?)w.Element(ns + "name") ?? "Waypoint", Time(w.Element(ns + "time")) ?? trip.Start));

		return trip;
	}

	private static Activity ParseActivity(string? type) => type?.ToLowerInvariant() switch
	{
		"run" or "running" => Activity.Run,
		"bike" or "cycling" or "biking" => Activity.Bike,
		"ski" or "skiing" or "ski touring" => Activity.Ski,
		_ => Activity.Hike,
	};

	private static string F(double v) => v.ToString("0.0000000", CultureInfo.InvariantCulture);

	private static double D(XAttribute? a) =>
		double.Parse(a?.Value ?? throw new FormatException("A point without lat/lon"), CultureInfo.InvariantCulture);

	private static double? Number(XElement? e) =>
		e is not null && double.TryParse(e.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

	private static DateTimeOffset? Time(XElement? e) =>
		e is not null && DateTimeOffset.TryParse(e.Value, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t) ? t : null;
}
