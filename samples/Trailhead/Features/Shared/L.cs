using Microsoft.Maui.Storage;

namespace Trailhead.Features.Shared;

/// <summary>
/// Polish and English. The app runs with invariant globalization (no ICU in the payload), so .NET has no UI culture
/// to go by: the language comes from Settings, or the session's LANG ("pl_PL.utf8" on a Polish phone). Strings are
/// keyed by their English text, so an untranslated one shows in English. The Lab stays English (it is the developer's
/// corner).
/// </summary>
public static class L
{
	private const string LanguageKey = "language";

	private static readonly Dictionary<string, string> Polish = new()
	{
		// Trips
		["Trips"] = "Wyprawy",
		["Pull down for the menu"] = "Pociągnij w dół, aby otworzyć menu",
		["Diagnostics"] = "Diagnostyka",
		["Settings"] = "Ustawienia",
		["Lab"] = "Laboratorium",
		["Import GPX"] = "Importuj GPX",
		["New trip"] = "Nowa wyprawa",
		["Statistics"] = "Statystyki",
		["Scroll to top"] = "Na górę",
		["Share"] = "Udostępnij",
		["Edit"] = "Edytuj",
		["Delete"] = "Usuń",
		["Deleting"] = "Usuwanie",
		["No trips yet. Pull down to start one."] = "Brak wypraw. Pociągnij w dół, aby zacząć.",
		["Preparing the demo trips…"] = "Przygotowuję przykładowe wyprawy…",
		["trips"] = "wypraw",
		["1 trip"] = "1 wyprawa",
		["{0} trips"] = "Wypraw: {0}",
		["{0} this year"] = "{0} w tym roku",
		["Unfinished recording"] = "Nieukończone nagrywanie",
		["Trailhead was closed while recording. Continue it?"] = "Trailhead zamknięto w trakcie nagrywania. Kontynuować?",
		["Continue"] = "Kontynuuj",
		["Discard"] = "Odrzuć",
		["Cancel"] = "Anuluj",
		["Not a GPX track"] = "To nie jest ślad GPX",
		["Import a GPX track"] = "Importuj ślad GPX",
		// Trip
		["Map"] = "Mapa",
		["Share GPX"] = "Udostępnij GPX",
		["Import photos"] = "Importuj zdjęcia",
		["Deleting trip"] = "Usuwanie wyprawy",
		["distance"] = "dystans",
		["moving"] = "w ruchu",
		["pace"] = "tempo",
		["average"] = "średnio",
		["climb"] = "podejście",
		["highest"] = "najwyżej",
		["total"] = "łącznie",
		["No photos"] = "Brak zdjęć",
		["1 photo"] = "1 zdjęcie",
		["{0} photos"] = "Zdjęcia: {0}",
		["Touch the profile to read it"] = "Dotknij profilu, aby go odczytać",
		["Edit trip"] = "Edycja wyprawy",
		["Save"] = "Zapisz",
		["Name"] = "Nazwa",
		["Notes"] = "Notatki",
		["Activity"] = "Aktywność",
		["3 to 60 characters"] = "Od 3 do 60 znaków",
		["Hike"] = "Wędrówka",
		["Run"] = "Bieg",
		["Ride"] = "Rower",
		["Ski tour"] = "Skitury",
		// Map
		["Open in a map app"] = "Otwórz w aplikacji map",
		["Street map"] = "Mapa ulic",
		["Topographic map"] = "Mapa topograficzna",
		["Start"] = "Start",
		["Finish"] = "Meta",
		// Recording
		["Recording"] = "Nagrywanie",
		["Recording paused"] = "Nagrywanie wstrzymane",
		["Paused"] = "Wstrzymano",
		["Standing still"] = "Postój",
		["Recording · standing still"] = "Nagrywanie · postój",
		["Pause"] = "Wstrzymaj",
		["Resume"] = "Wznów",
		["Waypoint"] = "Punkt",
		["Stop following"] = "Nie śledź",
		["Follow me"] = "Śledź mnie",
		["Finishing the trip"] = "Kończę wyprawę",
		["speed"] = "prędkość",
		["altitude"] = "wysokość",
		["You"] = "Ty",
		["Photo"] = "Zdjęcie",
		["Camera"] = "Aparat",
		["Elevation profile"] = "Profil wysokości",
		["Drag a finger along it to read the height"] = "Przesuń po nim palcem, aby odczytać wysokość",
		["Map of the trip with its photos"] = "Mapa wyprawy ze zdjęciami",
		["Live map"] = "Mapa na żywo",
		["Compass, with the way back to the start"] = "Kompas z kierunkiem powrotu do startu",
		["Camera preview; pinch to zoom"] = "Podgląd aparatu; uszczypnij, aby przybliżyć",
		["Last photo"] = "Ostatnie zdjęcie",
		["Distance per month"] = "Dystans w miesiącach",
		["Switch camera"] = "Przełącz aparat",
		["Flash: auto"] = "Lampa: auto",
		["Flash: on"] = "Lampa: wł.",
		["Flash: off"] = "Lampa: wył.",
		["Take photo"] = "Zrób zdjęcie",
		["Taking the photo…"] = "Robię zdjęcie…",
		["Added to the recording"] = "Dodano do nagrania",
		["Saved to Pictures"] = "Zapisano w Obrazach",
		["Near {0}"] = "Okolice: {0}",
		["{0} from {1}"] = "{0}: {1}",
		// Statistics
		["climbed"] = "podejść",
		["on the move"] = "w ruchu",
		["Last twelve months (km)"] = "Ostatnie 12 miesięcy (km)",
		["Trips per activity"] = "Wyprawy według aktywności",
		["Records"] = "Rekordy",
		["Longest"] = "Najdłuższa",
		["Most climb"] = "Najwięcej podejść",
		["Highest"] = "Najwyżej",
		["Longest day out"] = "Najdłuższy dzień",
		// Settings
		["Units"] = "Jednostki",
		["Kilometres and metres"] = "Kilometry i metry",
		["Miles and feet"] = "Mile i stopy",
		["Language"] = "Język",
		["As the phone"] = "Jak w telefonie",
		["Replay a demo track instead of the GPS"] = "Odtwarzaj przykładową trasę zamiast GPS",
		["For trying recording at a desk; the demo tour turns it on."] = "Do wypróbowania nagrywania przy biurku.",
		["Topographic maps"] = "Mapy topograficzne",
		["OpenTopoMap's contour lines instead of OpenStreetMap's streets."] = "Poziomice OpenTopoMap zamiast ulic OpenStreetMap.",
		["Generate the demo trips again"] = "Wygeneruj przykładowe wyprawy ponownie",
		["Replacing the demo trips"] = "Wymieniam przykładowe wyprawy",
		["The language changes when Trailhead starts again."] = "Język zmieni się po ponownym uruchomieniu.",
	};

	private static readonly string[] PolishMonths =
	{
		"styczeń", "luty", "marzec", "kwiecień", "maj", "czerwiec", "lipiec", "sierpień", "wrzesień", "październik",
		"listopad", "grudzień",
	};

	private static readonly string[] PolishMonthsShort = { "sty", "lut", "mar", "kwi", "maj", "cze", "lip", "sie", "wrz", "paź", "lis", "gru" };

	private static readonly string[] PolishDays = { "nd.", "pon.", "wt.", "śr.", "czw.", "pt.", "sob." };

	private static bool? _polish;

	/// <summary>"system", "en" or "pl" (Settings; read once at start).</summary>
	public static string Choice
	{
		get => Preferences.Default.Get(LanguageKey, "system");
		set => Preferences.Default.Set(LanguageKey, value);
	}

	/// <summary>TRAILHEAD_LANG=pl|en wins over Settings (the tour's runs in either language).</summary>
	public static bool IsPolish => _polish ??= (Environment.GetEnvironmentVariable("TRAILHEAD_LANG") is { Length: > 0 } forced ? forced : Choice) switch
	{
		"pl" => true,
		"en" => false,
		_ => (Environment.GetEnvironmentVariable("LANG") ?? "").StartsWith("pl", StringComparison.OrdinalIgnoreCase),
	};

	public static string T(string english) => IsPolish && Polish.TryGetValue(english, out var pl) ? pl : english;

	public static string F(string english, params object[] args) => string.Format(T(english), args);

	public static string Month(int month, int year) =>
		IsPolish ? $"{PolishMonths[month - 1]} {year}" : new DateTime(year, month, 1).ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture);

	public static string MonthShort(int month) =>
		IsPolish ? PolishMonthsShort[month - 1] : new DateTime(2000, month, 1).ToString("MMM", System.Globalization.CultureInfo.InvariantCulture);

	public static string Day(DateTimeOffset when) =>
		IsPolish ? $"{PolishDays[(int)when.DayOfWeek]} {when.Day} {PolishMonthsShort[when.Month - 1]} {when.Year}"
			: when.ToString("ddd d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);

	public static string DayAndTime(DateTimeOffset when) =>
		IsPolish ? $"{PolishDays[(int)when.DayOfWeek]} {when.Day} {PolishMonthsShort[when.Month - 1]}, {when:HH:mm}"
			: when.ToString("ddd d MMM, HH:mm", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>XAML: <c>Text="{l:T Trips}"</c>.</summary>
[ContentProperty(nameof(Text))]
public sealed class TExtension : IMarkupExtension<string>
{
	public string Text { get; set; } = "";

	public string ProvideValue(IServiceProvider serviceProvider) => L.T(Text);

	object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) => ProvideValue(serviceProvider);
}
