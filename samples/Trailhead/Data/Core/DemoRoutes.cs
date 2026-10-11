namespace Trailhead.Data;

/// <summary>A named point a demo route passes through. <see cref="StopMinutes"/> is a break there.</summary>
public sealed record RouteAnchor(string Name, double Lat, double Lon, double Ele, int StopMinutes = 0);

public sealed record DemoRoute(
	string Id,
	string Name,
	string Area,
	Activity Activity,
	DateTimeOffset Start,
	bool OutAndBack,
	string Notes,
	RouteAnchor[] Anchors);

/// <summary>
/// The demo set: real places with approximate coordinates and heights. The tracks between the anchors are generated
/// (<see cref="TripGenerator"/>), so they look like trails without copying any map data.
/// </summary>
public static class DemoRoutes
{
	private static readonly TimeSpan Cest = TimeSpan.FromHours(2), Cet = TimeSpan.FromHours(1);
	private static readonly TimeSpan Eest = TimeSpan.FromHours(3), Eet = TimeSpan.FromHours(2);

	public static readonly DemoRoute[] All =
	{
		new("demo-01", "Kasprowy Wierch from Kuźnice", "Tatra Mountains", Activity.Hike,
			new(2026, 7, 12, 7, 30, 0, Cest), true,
			"Early start to beat the crowds. Clouds came in on the ridge right after the summit.",
			new RouteAnchor[]
			{
				new("Kuźnice", 49.2697, 19.9800, 1010),
				new("Myślenickie Turnie", 49.2440, 19.9790, 1360, 10),
				new("Kasprowy Wierch", 49.2318, 19.9817, 1987, 30),
			}),
		new("demo-02", "Morskie Oko and Czarny Staw", "Tatra Mountains", Activity.Hike,
			new(2026, 8, 2, 8, 0, 0, Cest), true,
			"The asphalt part is long; the climb to Czarny Staw is the reward.",
			new RouteAnchor[]
			{
				new("Palenica Białczańska", 49.2546, 20.1036, 984),
				new("Włosienica", 49.2121, 20.0846, 1300),
				new("Morskie Oko", 49.2011, 20.0710, 1395, 25),
				new("Czarny Staw", 49.1899, 20.0758, 1583, 20),
			}),
		new("demo-03", "Śnieżka from Karpacz", "Karkonosze", Activity.Hike,
			new(2026, 6, 20, 9, 15, 0, Cest), true,
			"Windy at the top, as always.",
			new RouteAnchor[]
			{
				new("Karpacz, Biały Jar", 50.7690, 15.7290, 860),
				new("Strzecha Akademicka", 50.7505, 15.7230, 1258, 15),
				new("Dom Śląski", 50.7400, 15.7300, 1400),
				new("Śnieżka", 50.7360, 15.7398, 1603, 20),
			}),
		new("demo-04", "Połonina Wetlińska traverse", "Bieszczady", Activity.Hike,
			new(2026, 9, 14, 9, 0, 0, Cest), false,
			"Pancakes at Chatka Puchatka, then the long ridge to Smerek.",
			new RouteAnchor[]
			{
				new("Przełęcz Wyżna", 49.1196, 22.5440, 855),
				new("Chatka Puchatka", 49.1490, 22.5225, 1228, 35),
				new("Smerek", 49.1675, 22.4740, 1222, 10),
				new("Smerek village", 49.1960, 22.4650, 600),
			}),
		new("demo-05", "Tarnica at sunrise", "Bieszczady", Activity.Hike,
			new(2026, 9, 15, 4, 30, 0, Cest), true,
			"Headlamps up, sunrise from the cross.",
			new RouteAnchor[]
			{
				new("Wołosate", 49.0675, 22.6820, 750),
				new("Przełęcz pod Tarnicą", 49.0820, 22.7120, 1275),
				new("Tarnica", 49.0750, 22.7260, 1346, 40),
			}),
		new("demo-06", "Saana fell", "Lapland", Activity.Hike,
			new(2026, 8, 20, 10, 0, 0, Eest), true,
			"The stairs are brutal. Three countries visible from the top.",
			new RouteAnchor[]
			{
				new("Kilpisjärvi", 69.0430, 20.7950, 480),
				new("Saana stairs", 69.0480, 20.8050, 700, 5),
				new("Saana", 69.0430, 20.8250, 1029, 30),
			}),
		new("demo-07", "Pallas–Hetta, day one", "Lapland", Activity.Hike,
			new(2026, 8, 17, 8, 45, 0, Eest), false,
			"Open fells all day, reindeer at Hannukuru.",
			new RouteAnchor[]
			{
				new("Pallastunturi", 68.0580, 24.0630, 400),
				new("Taivaskero", 68.0750, 24.0120, 809, 20),
				new("Montellin maja", 68.1450, 23.9400, 420, 30),
				new("Hannukuru", 68.2000, 23.8800, 330),
			}),
		new("demo-08", "Nuuksio lakes run", "Uusimaa", Activity.Run,
			new(2026, 5, 30, 18, 0, 0, Eest), true,
			"Roots everywhere. Mosquitoes started at Mustalampi.",
			new RouteAnchor[]
			{
				new("Haukkalampi", 60.3040, 24.5060, 60),
				new("Mustalampi", 60.3150, 24.4950, 80),
				new("Kattila", 60.3200, 24.5000, 50, 3),
			}),
		new("demo-09", "Pyynikki ridge run", "Pirkanmaa", Activity.Run,
			new(2026, 6, 5, 7, 0, 0, Eest), true,
			"Tower doughnut afterwards.",
			new RouteAnchor[]
			{
				new("Pyynikki tower", 61.4930, 23.7350, 160),
				new("Pispala", 61.5010, 23.7000, 130, 2),
				new("Tahmela", 61.4990, 23.6800, 85),
			}),
		new("demo-10", "Vistula to Tyniec", "Kraków", Activity.Bike,
			new(2026, 5, 9, 10, 0, 0, Cest), true,
			"Flat river path, coffee at the abbey.",
			new RouteAnchor[]
			{
				new("Wawel", 50.0540, 19.9350, 210),
				new("Kolna", 50.0320, 19.8700, 205),
				new("Tyniec Abbey", 50.0220, 19.8230, 230, 25),
			}),
		new("demo-11", "Tri-City coast ride", "Pomerania", Activity.Bike,
			new(2026, 7, 25, 16, 30, 0, Cest), false,
			"Headwind the whole way, ice cream in Sopot.",
			new RouteAnchor[]
			{
				new("Gdańsk Brzeźno", 54.4110, 18.6300, 5),
				new("Sopot pier", 54.4470, 18.5700, 5, 15),
				new("Gdynia Orłowo", 54.4800, 18.5630, 10),
				new("Kamienna Góra", 54.5180, 18.5500, 50),
			}),
		new("demo-12", "Hala Gąsienicowa ski tour", "Tatra Mountains", Activity.Ski,
			new(2026, 2, 21, 8, 0, 0, Cet), false,
			"Skins up to Kasprowy, powder down Goryczkowa.",
			new RouteAnchor[]
			{
				new("Kuźnice", 49.2697, 19.9800, 1010),
				new("Hala Gąsienicowa", 49.2420, 20.0050, 1520, 20),
				new("Kasprowy Wierch", 49.2318, 19.9817, 1987, 25),
				new("Hala Goryczkowa", 49.2420, 19.9650, 1400),
				new("Kuźnice", 49.2697, 19.9800, 1010),
			}),
	};

	/// <summary>Named places for "near …" (plan D6: no geocoder on the phone).</summary>
	public static readonly RouteAnchor[] Gazetteer = All
		.SelectMany(r => r.Anchors)
		.Concat(new RouteAnchor[]
		{
			new("Zakopane", 49.2992, 19.9496, 840),
			new("Helsinki", 60.1699, 24.9384, 15),
			new("Tampere", 61.4978, 23.7610, 110),
			new("Kraków", 50.0647, 19.9450, 220),
			new("Gdańsk", 54.3520, 18.6466, 10),
		})
		.DistinctBy(a => a.Name)
		.ToArray();

	public static (RouteAnchor Place, double DistanceM) Nearest(double lat, double lon) =>
		Gazetteer.Select(a => (a, Geo.DistanceM(lat, lon, a.Lat, a.Lon))).MinBy(x => x.Item2);
}
