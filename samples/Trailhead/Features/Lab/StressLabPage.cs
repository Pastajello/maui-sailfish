using System.Diagnostics;
using System.Globalization;
using Trailhead.Data;
using Trailhead.Features.Shared;

namespace Trailhead.Features.Lab;

/// <summary>
/// Plan T18: the same controls at sizes a real app reaches. Each button runs one load and writes what it took into the
/// result lines (and the device log, "[Trailhead] STRESS …"): a CollectionView of 10,000 trips (build and first frame,
/// then a scroll to the end), a 50,000-point track as a Skia profile, and a GC/memory snapshot.
/// </summary>
public sealed class StressLabPage : ContentPage
{
	public const string Route = "lab/stress";

	private readonly VerticalStackLayout _results = new() { Spacing = 4 };
	private readonly ContentView _stage = new() { HeightRequest = 360 };

	public StressLabPage()
	{
		Title = "Stress";
		// No ScrollView around the stage: a list inside one is laid out at its full height (every row a delegate), which is
		// a different test (S29) from a long virtualized list.
		var buttons = new HorizontalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.Center };
		buttons.Add(AnimationsLabPage.Button("10,000 trips", async (_, _) => await ListAsync(10_000)));
		buttons.Add(AnimationsLabPage.Button("50,000 points", async (_, _) => await ProfileAsync(50_000)));
		buttons.Add(AnimationsLabPage.Button("Memory", (_, _) => Memory()));
		_stage.HeightRequest = -1;
		var grid = new Grid
		{
			Padding = new Thickness(16, 8),
			RowSpacing = 10,
			RowDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) },
		};
		grid.Add(buttons);
		grid.Add(_stage, 0, 1);
		grid.Add(_results, 0, 2);
		Content = grid;
	}

	public IReadOnlyList<string> Results => _results.Children.OfType<Label>().Select(l => l.Text ?? "").ToList();

	public CollectionView? List => _stage.Content as CollectionView;

	public Task ListAsync(int count) => ListAsync(count, ItemSizingStrategy.MeasureAllItems);

	public async Task ListAsync(int count, ItemSizingStrategy sizing)
	{
		Console.Error.WriteLine($"[Trailhead] STRESS list of {count} ({sizing}) starting");
		var watch = Stopwatch.StartNew();
		var items = await Task.Run(() => Enumerable.Range(0, count).Select(i =>
		{
			var t = TripGenerator.GenerateSynthetic(i, points: 8);
			return $"{t.Name} · {Format.Distance(TripStats.Compute(t.Track).DistanceM)}";
		}).ToList());
		var generated = watch.ElapsedMilliseconds;
		var list = new CollectionView
		{
			ItemSizingStrategy = sizing,
			ItemsSource = items,
			ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label { Padding = new Thickness(8, 10) };
				label.SetBinding(Label.TextProperty, static (string s) => s);
				return label;
			}),
		};
		watch.Restart();
		_stage.Content = list;
		await Task.Delay(50);
		await Dispatcher.DispatchAsync(() => { });
		var shown = watch.ElapsedMilliseconds;
		watch.Restart();
		list.ScrollTo(count - 1, position: ScrollToPosition.End, animate: false);
		await Task.Delay(300);
		Add($"list of {count:N0} ({sizing}): generated {generated} ms, on screen {shown} ms, scrolled to the end {watch.ElapsedMilliseconds} ms");
	}

	public async Task ProfileAsync(int points)
	{
		var watch = Stopwatch.StartNew();
		var trip = await Task.Run(() => TripGenerator.GenerateSynthetic(4242, points));
		var generated = watch.ElapsedMilliseconds;
		var chart = new Charts.ElevationChart { HeightRequest = 300 };
		chart.SetDynamicResource(Charts.ElevationChart.ColorProperty, ThemeResources.Highlight);
		watch.Restart();
		chart.Track = trip.Track;
		_stage.Content = chart;
		await Task.Delay(500);
		Add($"profile of {points:N0} points: generated {generated} ms, drawn {watch.ElapsedMilliseconds} ms (incl. 500 ms wait)");
	}

	public void Memory()
	{
		var info = GC.GetGCMemoryInfo();
		Add(string.Format(CultureInfo.InvariantCulture,
			"managed {0:0.0} MB, heap {1:0.0} MB, GCs {2}/{3}/{4}, pause {5:0.0} ms",
			GC.GetTotalMemory(false) / 1048576.0, info.HeapSizeBytes / 1048576.0,
			GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), info.PauseDurations.ToArray().Sum(p => p.TotalMilliseconds)));
	}

	private void Add(string line)
	{
		Console.Error.WriteLine($"[Trailhead] STRESS {line}");
		var label = new Label { Text = line, FontSize = 13 };
		label.SetDynamicResource(Label.TextColorProperty, ThemeResources.Secondary);
		_results.Add(label);
	}
}
