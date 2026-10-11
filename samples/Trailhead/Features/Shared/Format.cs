using System.Globalization;
using Microsoft.Maui.Storage;
using Trailhead.Data;

namespace Trailhead.Features.Shared;

public enum Units
{
	Metric,
	Imperial,
}

/// <summary>Distances, heights, durations and dates in the units the user picked (Settings).</summary>
public static class Format
{
	private const string UnitsKey = "units";
	private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

	public static Units Units
	{
		get => Preferences.Default.Get(UnitsKey, nameof(Units.Metric)) == nameof(Units.Imperial) ? Units.Imperial : Units.Metric;
		set => Preferences.Default.Set(UnitsKey, value.ToString());
	}

	public static string Distance(double metres) => Units == Units.Imperial
		? (metres / 1609.344).ToString(metres < 16_000 ? "0.0" : "0", Culture) + " mi"
		: (metres / 1000).ToString(metres < 10_000 ? "0.0" : "0", Culture) + " km";

	public static string Height(double metres) => Units == Units.Imperial
		? (metres * 3.28084).ToString("#,0", Culture) + " ft"
		: metres.ToString("#,0", Culture) + " m";

	public static string Climb(double metres) => "+" + Height(metres);

	public static string Duration(TimeSpan span) =>
		span.TotalHours >= 1 ? $"{(int)span.TotalHours}:{span.Minutes:00} h" : $"{span.Minutes}:{span.Seconds:00} min";

	public static string Clock(TimeSpan span) => $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}";

	/// <summary>Pace for walking and running (min/km), speed for bikes and skis.</summary>
	public static string Pace(Activity activity, double metresPerSecond)
	{
		if (metresPerSecond <= 0.05)
			return "–";
		if (activity is Activity.Bike or Activity.Ski)
			return Units == Units.Imperial
				? (metresPerSecond * 2.236936).ToString("0.0", Culture) + " mph"
				: (metresPerSecond * 3.6).ToString("0.0", Culture) + " km/h";
		var secondsPerUnit = (Units == Units.Imperial ? 1609.344 : 1000) / metresPerSecond;
		var pace = TimeSpan.FromSeconds(secondsPerUnit);
		return $"{(int)pace.TotalMinutes}:{pace.Seconds:00} /{(Units == Units.Imperial ? "mi" : "km")}";
	}

	public static string Day(DateTimeOffset when) => L.Day(when);

	public static string DayAndTime(DateTimeOffset when) => L.DayAndTime(when);

	public static string Month(DateTimeOffset when) => L.Month(when.Month, when.Year);

	public static string ActivityName(Activity activity) => activity switch
	{
		Activity.Hike => L.T("Hike"),
		Activity.Run => L.T("Run"),
		Activity.Bike => L.T("Ride"),
		Activity.Ski => L.T("Ski tour"),
		_ => activity.ToString(),
	};
}
