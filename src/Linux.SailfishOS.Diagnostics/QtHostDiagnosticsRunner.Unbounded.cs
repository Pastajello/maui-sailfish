using Microsoft.Maui.Controls;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Collection leg, a list inside a ScrollView (tracker S29): 300 items, as tall as its rows; the ScrollView scrolls it
/// to the end and the last row shows its item (the 192-delegate cap held rows past it empty before).
/// </summary>
internal sealed partial class QtHostDiagnosticsRunner
{
	private void RunColUnboundedCheck(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, Action done)
	{
		if (renderer.CurrentPage?.Navigation is not { } navigation)
		{
			done();
			return;
		}
		var list = new CollectionView
		{
			ItemsSource = Enumerable.Range(1, 300).Select(i => $"nested row {i}").ToList(),
			ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label { Padding = new Microsoft.Maui.Thickness(16, 10) };
				label.SetBinding(Label.TextProperty, ".");
				return label;
			}),
		};
		var scroll = new ScrollView
		{
			Content = new VerticalStackLayout { Children = { new Label { Text = "above the list", Padding = 16 }, list } },
		};
		_ = navigation.PushAsync(new ContentPage { Title = "Nested list", Content = scroll }, false);
		Console.Error.WriteLine("[Sailfish] Qt collection diag: unbounded — a 300-item CollectionView inside a ScrollView");
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(2500), () =>
		{
			_ = scroll.ScrollToAsync(list, ScrollToPosition.End, false);
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(2000), () =>
			{
				var adapter = renderer.Collection.AdapterOf(list);
				var last = adapter?.Rows.LastOrDefault();
				// Item hosts belong to their row's delegate, not to the page's host list.
				var lastHost = last?.CellViews.FirstOrDefault() is { } view && last.DgObj is { } dgObj &&
				               adapter!.Delegates.TryGetValue(dgObj, out var dg)
					? dg.Children.FirstOrDefault(h => h.IsAttached && ReferenceEquals(h.Element, view))
					: null;
				var lastText = lastHost is null ? null : QtHost.QtHostRuntime.GetProperty(lastHost.NativeHandle, "text");
				var withContent = adapter?.Rows.Count(r => r.DgObj is not null) ?? -1;
				_qtColChecks.Check($"unbounded S29: a list in a ScrollView is as tall as its rows (unbounded {adapter?.Unbounded}), every row has its delegate " +
					$"({withContent}==300) and the last one shows its item after the ScrollView scrolled there ('{lastText}'=='nested row 300')",
					adapter?.Unbounded == true && withContent == 300 && lastText == "nested row 300");
				Shot(dispatcher, "collection-nested-end", () =>
				{
					_ = navigation.PopAsync(false);
					dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1500), done);
				});
			});
		});
	}
}
