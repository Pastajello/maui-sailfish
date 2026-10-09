// The legacy ListView and its cells are obsolete in MAUI 11; supporting apps that still use them is the point here.
#pragma warning disable CS0618

using Microsoft.Maui.Controls;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Legacy list leg (MAUI_SAILFISH_QT_HOST_LEGACYLIST_DIAG=1, tracker S32–S36, D2 b): TextCell, ImageCell and ViewCell
/// rows render on the native list through the mirror CollectionView and a real tap selects (S32); a grouped list with
/// header, footer and separators scrolls to an item (S33); a real pull refreshes it and rows raise ItemAppearing (S34).
/// </summary>
internal sealed partial class QtHostDiagnosticsRunner
{
	private bool _qtLegacyListDiag;
	private readonly DiagChecks _qtLegacyChecks = new("Qt legacylist diag");

	private void RunQtLegacyListDiagnostics(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher) =>
		RunColLegacyListCheck(renderer, dispatcher, () =>
		{
			_qtLegacyChecks.CheckNoOffThreadCalls();
			_qtLegacyChecks.Accept("OK — the legacy ListView renders text, image and view cells, selects, groups, scrolls and refreshes on the native list");
			QtHost.QtHostRuntime.Shutdown();
		});

	private sealed record LegacyRow(string Kind, string Name, string Note);

	private void RunColLegacyListCheck(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, Action done)
	{
		if (renderer.CurrentPage?.Navigation is not { } navigation)
		{
			_qtLegacyChecks.Check("legacy: a page to push from", false);
			done();
			return;
		}
		var rows = new[]
		{
			new LegacyRow("text", "legacy text row", "its detail"),
			new LegacyRow("image", "legacy image row", "with an icon"),
			new LegacyRow("view", "legacy view row", ""),
			new LegacyRow("text", "legacy second text", "tap me"),
		};
		var selector = new LegacySelector();
		var list = new ListView { ItemsSource = rows, ItemTemplate = selector, HasUnevenRows = true };
		var tapped = new List<object>();
		var selected = new List<object?>();
		list.ItemTapped += (_, e) => tapped.Add(e.Item);
		list.ItemSelected += (_, e) => selected.Add(e.SelectedItem);
		_ = navigation.PushAsync(new ContentPage { Title = "Legacy ListView", Content = list }, false);
		Console.Error.WriteLine("[Sailfish] Qt legacylist diag: legacy ListView — text, image and view cells");
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(2000), () =>
		{
			var mirror = QtHost.LegacyListMirror.Of(list).View;
			var adapter = renderer.Collection.AdapterOf(mirror);
			var built = adapter?.Rows.Count(r => r.CellViews.FirstOrDefault() is not null) ?? -1;
			_qtLegacyChecks.Check($"legacy S32: a ListView of text, image and view cells renders its {built}==4 rows on the native list (handler {list.Handler?.GetType().Name})",
				built == 4 && list.Handler is Handlers.SailfishLegacyListViewHandler);
			Shot(dispatcher, "collection-legacy-list", () =>
			{
				if (adapter is not null && adapter.TryGetRowPoint(3, out var rx, out var ry))
					DiagQml.Tap(rx, ry);
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900), () =>
				{
					_qtLegacyChecks.Check($"legacy S32: a tapped row is the SelectedItem ('{(list.SelectedItem as LegacyRow)?.Name}'), ItemTapped x{tapped.Count}==1, ItemSelected x{selected.Count}==1",
						ReferenceEquals(list.SelectedItem, rows[3]) && tapped.Count == 1 && selected.Count == 1);
					_ = navigation.PopAsync(false);
					dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1500), () => RunColLegacyGroupedCheck(renderer, dispatcher, navigation, done));
				});
			});
		});
	}

	private sealed class LegacySelector : DataTemplateSelector
	{
		private readonly DataTemplate _text = new(() =>
		{
			var cell = new TextCell();
			cell.SetBinding(TextCell.TextProperty, nameof(LegacyRow.Name));
			cell.SetBinding(TextCell.DetailProperty, nameof(LegacyRow.Note));
			return cell;
		});

		private readonly DataTemplate _image = new(() =>
		{
			var cell = new ImageCell { ImageSource = "sailfish_logo.png" };
			cell.SetBinding(TextCell.TextProperty, nameof(LegacyRow.Name));
			cell.SetBinding(TextCell.DetailProperty, nameof(LegacyRow.Note));
			return cell;
		});

		private readonly DataTemplate _view = new(() =>
		{
			var label = new Label { FontAttributes = FontAttributes.Bold, Padding = new Microsoft.Maui.Thickness(16, 12) };
			label.SetBinding(Label.TextProperty, nameof(LegacyRow.Name));
			return new ViewCell { View = new Border { Margin = 8, Padding = 4, Content = label } };
		});

		protected override DataTemplate OnSelectTemplate(object item, BindableObject container) =>
			((LegacyRow)item).Kind switch { "image" => _image, "view" => _view, _ => _text };
	}

	private sealed class LegacyGroup(string title, IEnumerable<string> items) : List<string>(items)
	{
		public string Title { get; } = title;
	}

	/// <summary>Tracker S33: a grouped ListView with GroupDisplayBinding headers, a Header and Footer and red
	/// separators; ScrollTo into the last group brings its last row into view.</summary>
	private void RunColLegacyGroupedCheck(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, INavigation navigation, Action done)
	{
		var groups = new List<LegacyGroup>
		{
			new("Fruit", new[] { "apple", "kiwi", "plum" }),
			new("Vegetables", new[] { "carrot", "leek" }),
			new("Grains", Enumerable.Range(1, 20).Select(i => $"grain {i}")),
		};
		var list = new ListView
		{
			ItemsSource = groups,
			IsGroupingEnabled = true,
			GroupDisplayBinding = new Binding(nameof(LegacyGroup.Title)),
			Header = "grouped list header",
			Footer = "grouped list footer",
			SeparatorColor = Microsoft.Maui.Graphics.Colors.IndianRed,
		};
		var lastVisible = -1;
		_ = navigation.PushAsync(new ContentPage { Title = "Grouped ListView", Content = list }, false);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(2000), () =>
		{
			var mirror = QtHost.LegacyListMirror.Of(list).View;
			mirror.Scrolled += (_, e) => lastVisible = e.LastVisibleItemIndex;
			var adapter = renderer.Collection.AdapterOf(mirror);
			var headers = adapter?.Rows.Count(r => r.Kind == QtHost.QtHostCollectionBridge.KindGroupHeader) ?? -1;
			var items = adapter?.Rows.Count(r => r.Kind == QtHost.QtHostCollectionBridge.KindItem) ?? -1;
			_qtLegacyChecks.Check($"legacy S33: a grouped ListView has {headers}==3 group header rows and {items}==25 item rows, header and footer slots set " +
				$"('{mirror.Header}', '{mirror.Footer}')",
				headers == 3 && items == 25 && Equals(mirror.Header, "grouped list header") && Equals(mirror.Footer, "grouped list footer"));
			Shot(dispatcher, "collection-legacy-grouped", () =>
			{
				list.ScrollTo("grain 20", groups[2], ScrollToPosition.End, false);
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200), () =>
				{
					_qtLegacyChecks.Check($"legacy S33: ScrollTo(last item, last group) brings it into view (last visible item {lastVisible}==24)",
						lastVisible == 24);
					Shot(dispatcher, "collection-legacy-scrolled", () =>
					{
						_ = navigation.PopAsync(false);
						dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1500), () => RunColLegacyRefreshCheck(renderer, dispatcher, navigation, done));
					});
				});
			});
		});
	}

	/// <summary>Tracker S34: a ListView with IsPullToRefreshEnabled and a RefreshCommand; a real pull from its header
	/// runs the command (IsRefreshing true, the spinner), the app's EndRefresh stops it; the rows in view raised
	/// ItemAppearing.</summary>
	private void RunColLegacyRefreshCheck(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, INavigation navigation, Action done)
	{
		var refreshed = 0;
		var appeared = 0;
		ListView? list = null;
		list = new ListView
		{
			ItemsSource = Enumerable.Range(1, 40).Select(i => $"refreshable row {i}").ToList(),
			Header = "pull me down",
			IsPullToRefreshEnabled = true,
			RefreshCommand = new Command(() => refreshed++),   // the refresh ends after the screenshot (EndRefresh below)
		};
		list.ItemAppearing += (_, _) => appeared++;
		_ = navigation.PushAsync(new ContentPage { Title = "Refresh ListView", Content = list }, false);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(2000), () =>
		{
			var host = renderer.CurrentHosts.FirstOrDefault(h => h.IsAttached && ReferenceEquals(h.Element, QtHost.LegacyListMirror.Of(list).View));
			if (host is null || !QtHost.QtHostRuntime.TryItemGeometry(host.NativeHandle, out var scene))
			{
				_qtLegacyChecks.Check("legacy S34: the refreshable list is hosted", false);
				Finish();
				return;
			}
			var cx = scene.X + scene.Width / 2;
			var cy = scene.Y + 30;   // the header slot: a row would take the press
			QtHost.QtHostRuntime.InjectPointer(0, cx, cy);
			for (var step = 1; step <= 8; step++)
			{
				var dy = step * 60.0;
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(40 * step), () => QtHost.QtHostRuntime.InjectPointer(2, cx, cy + dy));
			}
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(400), () => QtHost.QtHostRuntime.InjectPointer(1, cx, cy + 480.0));
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1000), () =>
			{
				var during = list.IsRefreshing;
				var spinner = QtHost.QtHostRuntime.GetProperty(host.NativeHandle, "mauiRefreshing");
				Console.Error.WriteLine($"[Sailfish] Qt legacylist diag: legacy S34 during the refresh: IsRefreshing {during}, native mauiRefreshing {spinner}");
				Shot(dispatcher, "collection-legacy-refreshing", () =>
				{
					list.EndRefresh();
					dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(800), () =>
				{
					_qtLegacyChecks.Check($"legacy S34: a real pull runs RefreshCommand (x{refreshed}==1, IsRefreshing during it {during}, the list's spinner {spinner}), EndRefresh stops it " +
						$"(now {list.IsRefreshing}); the rows in view raised ItemAppearing (x{appeared}>0)",
						refreshed == 1 && during && spinner == "true" && !list.IsRefreshing && appeared > 0);
					Finish();
				});
				});
			});
		});

		void Finish()
		{
			_ = navigation.PopAsync(false);
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1500), done);
		}
	}
}
