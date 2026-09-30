using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
using SailfishKitchen.Models;
using SailfishKitchen.Services;

namespace SailfishKitchen.Helpers;

/// <summary>
/// KITCHEN_TOUR=beef: a scripted Beef-category run (swipes with load-more, two recipe details) so recordings on
/// different platforms show the same flow. Each step is logged as "TOUR +ms step" for lining videos up.
/// KITCHEN_TOUR=home: swipes the home page's categories and logs the scroll offset (any orientation).
/// KITCHEN_TOUR=layout: opens Beef and flips the Grid / list pulley command twice, an SF-SHOT per state
/// (tools/sf shots) with the resulting column count logged.
/// </summary>
internal static class DemoTour
{
	private static readonly System.Diagnostics.Stopwatch Clock = new();
	private static long _lastTick;
	private static long _maxGapMs;

	public static async Task RunAsync(INavigationService navigation, Window window)
	{
		Clock.Restart();
		// UI-thread stall probe: a 16 ms timer; each step logs the longest gap between ticks since the last step.
		_lastTick = 0;
		var probe = Application.Current!.Dispatcher.CreateTimer();
		probe.Interval = TimeSpan.FromMilliseconds(16);
		probe.Tick += (_, _) =>
		{
			var now = Clock.ElapsedMilliseconds;
			if (_lastTick != 0)
				_maxGapMs = Math.Max(_maxGapMs, now - _lastTick);
			_lastTick = now;
		};
		probe.Start();
		Log("start");
		// KITCHEN_TOUR_DELAY_MS lets a slow-starting screen recorder catch the whole run.
		await Task.Delay(int.TryParse(Environment.GetEnvironmentVariable("KITCHEN_TOUR_DELAY_MS"), out var delay) ? delay : 2500);

		await navigation.OpenCatalogAsync(MealQuery.ByCategory("Beef"));
		Log("catalog opened");
		await Task.Delay(3000);

		var catalog = TopPage(window);
		var list = catalog?.GetVisualTreeDescendants().OfType<CollectionView>().FirstOrDefault();
		if (list is null)
		{
			Log("no CollectionView — stopping");
			return;
		}
		for (var i = 0; i < 8; i++)
		{
			await SwipeUpAsync(list);
			Log($"swipe {i + 1} (items={Count(list)})");
			await Task.Delay(1200);
		}

		await OpenDetailAsync(window, list, index: DetailIndex(0, 6), scroll: true);
		await navigation.GoBackAsync();
		Log("back");
		await Task.Delay(2000);
		await OpenDetailAsync(window, list, index: DetailIndex(1, 12), scroll: false);
		await navigation.GoBackAsync();
		Log("back");
		await Task.Delay(2000);
		Log("done");
		probe.Stop();
#if SAILFISH
		// Ends the device recording (tools/sf record stops when the app exits).
		Application.Current?.Quit();
#endif
	}

	public static async Task RunHomeAsync(Window window)
	{
		Clock.Restart();
		await Task.Delay(int.TryParse(Environment.GetEnvironmentVariable("KITCHEN_TOUR_DELAY_MS"), out var delay) ? delay : 4000);
		var list = TopPage(window)?.GetVisualTreeDescendants().OfType<CollectionView>().FirstOrDefault();
		if (list is null)
		{
			Log("no CollectionView — stopping");
			return;
		}
		var offset = 0.0;
		list.Scrolled += (_, e) => offset = e.VerticalOffset;
		var info = DeviceDisplay.MainDisplayInfo;
		Log($"home {info.Width}x{info.Height} {info.Orientation} items={Count(list)}");
		for (var i = 0; i < 3; i++)
		{
			await SwipeUpAsync(list);
			await Task.Delay(1500);
			Log($"swipe {i + 1} offset={offset:F0}");
		}
		Log("done");
	}

	public static async Task RunLayoutAsync(INavigationService navigation, Window window)
	{
		Clock.Restart();
		await Task.Delay(int.TryParse(Environment.GetEnvironmentVariable("KITCHEN_TOUR_DELAY_MS"), out var delay) ? delay : 2500);
		await navigation.OpenCatalogAsync(MealQuery.ByCategory("Beef"));
		await Task.Delay(3000);
		var catalog = TopPage(window);
		var list = catalog?.GetVisualTreeDescendants().OfType<CollectionView>().FirstOrDefault();
		if (catalog?.BindingContext is not ViewModels.CatalogViewModel vm || list is null)
		{
			Log("no catalog — stopping");
			return;
		}
		await ShotAsync($"layout-0-span{Span(list)}");
		for (var i = 1; i <= 2; i++)
		{
			// The command the pulley item runs.
			await vm.ToggleLayoutCommand.ExecuteAsync(null);
			await Task.Delay(1500);
			await ShotAsync($"layout-{i}-span{Span(list)}");
		}
		Log("done");
#if SAILFISH
		Application.Current?.Quit();
#endif
	}

	private static int Span(CollectionView list) => (list.ItemsLayout as GridItemsLayout)?.Span ?? 1;

	// tools/sf shots screenshots each "SF-SHOT <name>" line while the state is held, and acknowledges it with
	// /tmp/sf-shot-ack/<name> (MAUI_SAILFISH_SHOT_SYNC=1); without the handshake the state is held for a fixed time.
	private static async Task ShotAsync(string name)
	{
		Log(name);
		Console.Error.WriteLine($"SF-SHOT {name}");
		var hold = int.TryParse(Environment.GetEnvironmentVariable("MAUI_SAILFISH_SHOT_HOLD_MS"), out var ms) ? ms : 4000;
		if (Environment.GetEnvironmentVariable("MAUI_SAILFISH_SHOT_SYNC") != "1")
		{
			await Task.Delay(hold);
			return;
		}
		var ack = Path.Combine("/tmp/sf-shot-ack", name);
		for (var waited = 0; waited < 20000 && !File.Exists(ack); waited += 100)
			await Task.Delay(100);
		await Task.Delay(hold);
	}

	private static async Task OpenDetailAsync(Window window, CollectionView list, int index, bool scroll)
	{
		var items = (list.ItemsSource as System.Collections.IEnumerable)?.Cast<object>().ToList() ?? new List<object>();
		if (items.Count == 0)
			return;
		// Through SelectedItem, the same path a tap takes.
		list.SelectedItem = items[Math.Min(index, items.Count - 1)];
		Log($"detail {index} requested");
		var requested = Clock.ElapsedMilliseconds;
		while (Clock.ElapsedMilliseconds - requested < 4000)
		{
			if (TopPage(window)?.BindingContext is ViewModels.MealDetailViewModel { HasIngredients: true })
			{
				Log($"detail {index} data ready after {Clock.ElapsedMilliseconds - requested} ms");
				break;
			}
			await Task.Delay(16);
		}
		await Task.Delay((int)Math.Max(0, 4000 - (Clock.ElapsedMilliseconds - requested)));
		if (scroll && TopPage(window) is { } detail &&
		    detail.GetVisualTreeDescendants().OfType<ScrollView>().FirstOrDefault() is { } scrollView)
		{
			await SwipeUpAsync(scrollView);
			Log("detail swipe");
			await Task.Delay(1500);
		}
	}

	// KITCHEN_TOUR_DETAILS="20,44" picks the recipes (uncached ones show the loading path).
	private static int DetailIndex(int which, int fallback)
	{
		var parts = (Environment.GetEnvironmentVariable("KITCHEN_TOUR_DETAILS") ?? string.Empty).Split(',');
		return which < parts.Length && int.TryParse(parts[which], out var index) ? index : fallback;
	}

	private static Page? TopPage(Window window) =>
		(window.Page as NavigationPage)?.CurrentPage;

	private static int Count(CollectionView list) =>
		(list.ItemsSource as System.Collections.ICollection)?.Count ?? -1;

	private static void Log(string step)
	{
		Console.WriteLine($"TOUR +{Clock.ElapsedMilliseconds}ms {step} | ui-stall max {_maxGapMs} ms");
		_maxGapMs = 0;
	}

#if SAILFISH
	// A real finger flick: pointer events injected into the Qt window, so Silica's own flick physics run.
	private static async Task SwipeUpAsync(VisualElement _)
	{
		var info = DeviceDisplay.MainDisplayInfo;
		var x = info.Width / 2;
		var from = info.Height * 0.72;
		var to = info.Height * 0.32;
		const int steps = 10;
		Inject(0, x, from);
		for (var i = 1; i <= steps; i++)
		{
			await Task.Delay(16);
			Inject(2, x, from + (to - from) * i / steps);
		}
		Inject(1, x, to);
	}

	// The Qt surface stays portrait while Silica turns its content, so a point on screen becomes window pixels.
	private static void Inject(int type, double x, double y)
	{
		var w = Microsoft.Maui.SailfishOS.Platform.SailfishDisplay.PixelWidth;
		var h = Microsoft.Maui.SailfishOS.Platform.SailfishDisplay.PixelHeight;
		var (wx, wy) = Microsoft.Maui.SailfishOS.Platform.SailfishDisplay.Orientation switch
		{
			Microsoft.Maui.SailfishOS.Platform.SailfishOrientation.Landscape => (w - y, x),
			Microsoft.Maui.SailfishOS.Platform.SailfishOrientation.LandscapeInverted => (y, h - x),
			Microsoft.Maui.SailfishOS.Platform.SailfishOrientation.PortraitInverted => (w - x, h - y),
			_ => (x, y),
		};
		Microsoft.Maui.SailfishOS.Platform.QtHost.QtHostRuntime.InjectPointer(type, wx, wy);
	}
#elif IOS
	// No touch injection on the simulator: an animated content-offset scroll of the same distance.
	private static Task SwipeUpAsync(VisualElement view)
	{
		if (FindScrollView(view.Handler?.PlatformView as UIKit.UIView) is { } scroll)
		{
			var distance = scroll.Bounds.Height * 0.45;
			var max = Math.Max(0, scroll.ContentSize.Height - scroll.Bounds.Height);
			var y = Math.Min(max, scroll.ContentOffset.Y + distance);
			scroll.SetContentOffset(new CoreGraphics.CGPoint(scroll.ContentOffset.X, y), animated: true);
		}
		return Task.CompletedTask;
	}

	private static UIKit.UIScrollView? FindScrollView(UIKit.UIView? view)
	{
		if (view is null)
			return null;
		if (view is UIKit.UIScrollView scroll)
			return scroll;
		foreach (var child in view.Subviews)
			if (FindScrollView(child) is { } hit)
				return hit;
		return null;
	}
#else
	private static Task SwipeUpAsync(VisualElement _) => Task.CompletedTask;
#endif
}
