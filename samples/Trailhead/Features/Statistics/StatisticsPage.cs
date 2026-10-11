using System.Globalization;
using Trailhead.Data;
using Trailhead.Features.Charts;
using Trailhead.Features.Shared;

namespace Trailhead.Features.Statistics;

/// <summary>
/// Totals that count up, the last twelve months as growing bars, trips per activity, and the records (longest,
/// most climb, highest, longest day out); a record opens its trip.
/// </summary>
public sealed class StatisticsPage : ContentPage
{
	private readonly TripStore _store;
	private readonly AnimatedCounter _trips = Counter(v => v.ToString("0", CultureInfo.InvariantCulture));
	private readonly AnimatedCounter _distance = Counter(Format.Distance);
	private readonly AnimatedCounter _climb = Counter(Format.Height);
	private readonly AnimatedCounter _hours = Counter(v => v.ToString("0", CultureInfo.InvariantCulture) + " h");
	private readonly BarChart _months = new() { ValueFormat = v => (v / 1000).ToString("0", CultureInfo.InvariantCulture) };
	private readonly BarChart _activities = new() { HeightRequest = 140 };
	private readonly VerticalStackLayout _records = new() { Spacing = 4 };

	public StatisticsPage(TripStore store)
	{
		_store = store;
		Title = L.T("Statistics");
		SemanticProperties.SetDescription(_months, L.T("Distance per month"));
		SemanticProperties.SetDescription(_activities, L.T("Trips per activity"));
		foreach (var chart in new[] { _months, _activities })
		{
			chart.SetDynamicResource(BarChart.ColorProperty, ThemeResources.Highlight);
			chart.SetDynamicResource(BarChart.TextColorProperty, ThemeResources.Primary);
		}

		var totals = new Grid
		{
			ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) },
			RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto) },
			RowSpacing = 2,
		};
		totals.Add(_trips, 0, 0);
		totals.Add(Caption(L.T("trips")), 0, 1);
		totals.Add(_distance, 1, 0);
		totals.Add(Caption(L.T("distance")), 1, 1);
		totals.Add(_climb, 0, 2);
		totals.Add(Caption(L.T("climbed")), 0, 3);
		totals.Add(_hours, 1, 2);
		totals.Add(Caption(L.T("on the move")), 1, 3);

		Content = new ScrollView
		{
			Content = new VerticalStackLayout
			{
				Padding = new Thickness(16, 8, 16, 24),
				Spacing = 12,
				Children =
				{
					totals,
					Section(L.T("Last twelve months (km)")),
					_months,
					Section(L.T("Trips per activity")),
					_activities,
					Section(L.T("Records")),
					_records,
				},
			},
		};
	}

	/// <summary>The month bars, for tour checks.</summary>
	public BarChart Months => _months;

	/// <summary>The record rows (tour checks).</summary>
	public IReadOnlyList<View> RecordRows => _records.Children.OfType<View>().ToList();

	/// <summary>How many records were celebrated on the last visit (tour checks).</summary>
	public int Celebrated { get; private set; }

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await _store.Ready;
		var all = _store.Summaries;
		_trips.Value = all.Count;
		_distance.Value = all.Sum(s => s.DistanceM);
		_climb.Value = all.Sum(s => s.AscentM);
		_hours.Value = all.Sum(s => s.MovingTime.TotalHours);

		// The twelve months ending with the newest trip's month, so the demo set (2026) shows its season.
		var newest = all.Count > 0 ? all.Max(s => s.Start) : DateTimeOffset.Now;
		var months = Enumerable.Range(0, 12).Select(i => new DateTime(newest.Year, newest.Month, 1).AddMonths(i - 11)).ToList();
		_months.Bars = months.Select(m => new Bar(L.MonthShort(m.Month)[..1].ToUpperInvariant(),
			all.Where(s => s.Start.Year == m.Year && s.Start.Month == m.Month).Sum(s => s.DistanceM))).ToList();
		_activities.ValueFormat = v => v.ToString("0", CultureInfo.InvariantCulture);
		_activities.Bars = Enum.GetValues<Activity>()
			.Select(a => new Bar(Format.ActivityName(a), all.Count(s => s.Activity == a))).ToList();

		_records.Children.Clear();
		Celebrated = 0;
		if (all.Count == 0)
			return;
		AddRecord("longest", L.T("Longest"), all.MaxBy(s => s.DistanceM)!, s => Format.Distance(s.DistanceM));
		AddRecord("climb", L.T("Most climb"), all.MaxBy(s => s.AscentM)!, s => Format.Climb(s.AscentM));
		AddRecord("highest", L.T("Highest"), all.MaxBy(s => s.MaxEleM)!, s => Format.Height(s.MaxEleM));
		AddRecord("day", L.T("Longest day out"), all.MaxBy(s => s.Duration)!, s => Format.Duration(s.Duration));
	}

	/// <summary>
	/// One record row. It slides in after the ones above it; a record another trip held at the last visit (the holder is
	/// kept in Preferences) is new: a star, and its value pops a few times. The first visit only remembers the holders.
	/// </summary>
	private void AddRecord(string key, string what, TripSummary trip, Func<TripSummary, string> value)
	{
		var seen = Preferences.Default.Get("record-" + key, "");
		Preferences.Default.Set("record-" + key, trip.Id);
		var isNew = seen.Length > 0 && seen != trip.Id;
		if (isNew)
			Celebrated++;

		var row = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, Padding = new Thickness(0, 6) };
		var name = new Label { Text = $"{(isNew ? "★ " : "")}{what}: {trip.Name}", LineBreakMode = LineBreakMode.TailTruncation };
		var amount = new Label { Text = value(trip) };
		amount.SetDynamicResource(Label.TextColorProperty, ThemeResources.Highlight);
		row.Add(name);
		row.Add(amount, 1, 0);
		var tap = new TapGestureRecognizer();
		tap.Tapped += async (_, _) => await Shell.Current.GoToAsync($"{AppShell.TripRoute}?id={trip.Id}");
		row.GestureRecognizers.Add(tap);
		row.Opacity = 0;
		row.TranslationX = 48;
		_records.Children.Add(row);
		_ = EnterAsync(row, amount, _records.Children.Count - 1, isNew);
	}

	private static async Task EnterAsync(View row, View amount, int index, bool celebrate)
	{
		await Task.Delay(400 + index * 140);
		await Task.WhenAll(row.FadeToAsync(1, 350), row.TranslateToAsync(0, 0, 350, Easing.CubicOut));
		if (!celebrate)
			return;
		for (var i = 0; i < 3; i++)
		{
			await amount.ScaleToAsync(1.35, 180, Easing.CubicOut);
			await amount.ScaleToAsync(1, 260, Easing.SpringOut);
		}
	}

	private static AnimatedCounter Counter(Func<double, string> format)
	{
		var counter = new AnimatedCounter { Formatter = format, FontSize = 28, HorizontalTextAlignment = TextAlignment.Center };
		counter.SetDynamicResource(Label.TextColorProperty, ThemeResources.Highlight);
		return counter;
	}

	private static Label Caption(string text)
	{
		var label = new Label { Text = text, FontSize = 13, HorizontalTextAlignment = TextAlignment.Center };
		label.SetDynamicResource(Label.TextColorProperty, ThemeResources.Secondary);
		return label;
	}

	private static Label Section(string text)
	{
		var label = new Label { Text = text, FontSize = 15, HorizontalTextAlignment = TextAlignment.End, Margin = new Thickness(0, 12, 0, 0) };
		SemanticProperties.SetHeadingLevel(label, SemanticHeadingLevel.Level2);
		label.SetDynamicResource(Label.TextColorProperty, ThemeResources.Highlight);
		return label;
	}
}
