using Microsoft.Maui.Controls;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Collection leg, sizing and snapping (tracker S28): the same 500-item list opened with MeasureAllItems and with
/// MeasureFirstItem — the second templates only what shows and builds its rows faster; then a list with Mandatory snap
/// points, flicked, comes to rest on a row boundary.
/// </summary>
internal sealed partial class QtHostDiagnosticsRunner
{
	private static CollectionView SizingList(ItemSizingStrategy sizing, int count) => new()
	{
		ItemsSource = Enumerable.Range(1, count).Select(i => $"sized item {i}").ToList(),
		ItemSizingStrategy = sizing,
		ItemTemplate = new DataTemplate(() =>
		{
			var label = new Label { Padding = new Microsoft.Maui.Thickness(16, 12) };
			label.SetBinding(Label.TextProperty, ".");
			return new Grid { HeightRequest = 72, Children = { label } };
		}),
	};

	private void RunColSizingChecks(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, Action done)
	{
		if (renderer.CurrentPage?.Navigation is not { } navigation)
		{
			done();
			return;
		}

		// Opens a 500-item list with the strategy; reports the push→rows-built time and the views templated.
		void Open(ItemSizingStrategy sizing, Action<double, int, int> then)
		{
			var list = SizingList(sizing, 500);
			var page = new ContentPage { Title = $"Sizing {sizing}", Content = list };
			var sw = System.Diagnostics.Stopwatch.StartNew();
			_ = navigation.PushAsync(page, false);
			double builtMs = double.NaN;
			WaitFor(dispatcher, () =>
			{
				if (renderer.Collection.AdapterOf(list) is { Rows.Count: 500 })
				{
					if (double.IsNaN(builtMs))
						builtMs = sw.Elapsed.TotalMilliseconds;
					return true;
				}
				return false;
			}, 5000, () =>
			{
				var adapter = renderer.Collection.AdapterOf(list);
				var templated = adapter?.Rows.Count(r => r.CellViews.FirstOrDefault() is not null) ?? -1;
				var lazy = adapter?.LazyTemplated ?? -1;
				Console.Error.WriteLine($"[Sailfish] Qt collection diag: sizing {sizing}: 500 rows built {builtMs:F0} ms after the push, {templated} views templated ({lazy} on materialize)");
				_ = navigation.PopAsync(false);
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200), () => then(builtMs, templated, lazy));
			});
		}

		Open(ItemSizingStrategy.MeasureAllItems, (allMs, allViews, _) =>
			Open(ItemSizingStrategy.MeasureFirstItem, (firstMs, firstViews, lazy) =>
			{
				_qtColChecks.Check($"sizing S28: MeasureFirstItem templates only what shows ({firstViews} of 500 views, {lazy} on materialize; " +
					$"MeasureAllItems {allViews}) and builds faster ({firstMs:F0} ms < {allMs:F0} ms)",
					allViews == 500 && firstViews < 100 && lazy > 0 && firstMs < allMs);
				RunColSnapCheck(renderer, dispatcher, navigation, done);
			}));
	}

	private void RunColSnapCheck(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, INavigation navigation, Action done)
	{
		var list = SizingList(ItemSizingStrategy.MeasureAllItems, 60);
		list.ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Vertical) { SnapPointsType = SnapPointsType.Mandatory };
		var page = new ContentPage { Title = "Snap", Content = list };
		_ = navigation.PushAsync(page, false);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1800), () =>
		{
			var host = renderer.CurrentHosts.FirstOrDefault(h => h.IsAttached && ReferenceEquals(h.Element, list));
			if (host is null || !QtHost.QtHostRuntime.TryItemGeometry(host.NativeHandle, out var scene))
			{
				_qtColChecks.Check("snap S28: the list is hosted", false);
				Finish();
				return;
			}
			double Num(string name) => DiagQml.Num(QtHost.QtHostRuntime.GetProperty(host.NativeHandle, name));
			// A short upward drag released mid-row: without snapping the list would rest between rows.
			var cx = scene.X + scene.Width / 2;
			var cy = scene.Y + scene.Height * 0.7;
			QtHost.QtHostRuntime.InjectPointer(0, cx, cy);
			for (var step = 1; step <= 6; step++)
			{
				var dy = -step * 37.0;
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(60 * step), () => QtHost.QtHostRuntime.InjectPointer(2, cx, cy + dy));
			}
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(420), () => QtHost.QtHostRuntime.InjectPointer(1, cx, cy - 222));
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(2200), () =>
			{
				var adapter = renderer.Collection.AdapterOf(list);
				var stride = QtHost.QtHostUnits.ToQtUnits((adapter?.Rows.FirstOrDefault()?.HeightDp ?? 0) + (adapter?.SpacingDp ?? 0));
				var offset = Num("contentY") - Num("originY");
				var rest = stride > 0 ? offset % stride : double.NaN;
				var aligned = Math.Min(rest, stride - rest);
				_qtColChecks.Check($"snap S28: Mandatory snap points leave the flicked list on a row boundary (offset {offset:F0} px, row stride {stride:F0} px, off by {aligned:F1} px)",
					offset > stride / 2 && aligned <= 2);
				Shot(dispatcher, "collection-snap", Finish);
			});
		});

		void Finish()
		{
			_ = navigation.PopAsync(false);
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200), done);
		}
	}
}
