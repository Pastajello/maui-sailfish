using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Collection leg, item states (tracker S27, D4 a): rows with an opaque template whose VisualStateManager Selected state
/// recolours them; a real tap selects a row and the Selected colour shows (no Silica highlight). Then a carousel whose
/// pages carry CurrentItem/DefaultItem states: a real swipe drags it (IsDragging) and moves CurrentItem to the next page.
/// </summary>
internal sealed partial class QtHostDiagnosticsRunner
{
	private static readonly Color StateNormal = Color.FromArgb("#202428");
	private static readonly Color StateSelected = Colors.SteelBlue;

	private static View StateRoot(string text, params (string State, Color Color)[] states)
	{
		var label = new Label { Padding = new Microsoft.Maui.Thickness(16, 14), TextColor = Colors.White };
		label.SetBinding(Label.TextProperty, ".");
		var root = new Grid { BackgroundColor = StateNormal, Children = { label } };
		var group = new VisualStateGroup { Name = "CommonStates" };
		foreach (var (state, color) in states)
			group.States.Add(new VisualState { Name = state, Setters = { new Setter { Property = VisualElement.BackgroundColorProperty, Value = color } } });
		VisualStateManager.SetVisualStateGroups(root, new VisualStateGroupList { group });
		return root;
	}

	private void RunColItemStateChecks(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, Action done)
	{
		if (renderer.CurrentPage?.Navigation is not { } navigation)
		{
			done();
			return;
		}
		var list = new CollectionView
		{
			ItemsSource = new[] { "state row 1", "state row 2", "state row 3" },
			SelectionMode = SelectionMode.Single,
			HeightRequest = 520,
			ItemTemplate = new DataTemplate(() => StateRoot("", ("Normal", StateNormal), ("Selected", StateSelected))),
		};
		var dragged = false;
		var carousel = new CarouselView
		{
			ItemsSource = new[] { "page A", "page B", "page C" },
			HeightRequest = 300,
			ItemTemplate = new DataTemplate(() => StateRoot("", (CarouselView.CurrentItemVisualState, Colors.DarkGreen),
				(CarouselView.DefaultItemVisualState, StateNormal), (CarouselView.NextItemVisualState, StateNormal),
				(CarouselView.PreviousItemVisualState, StateNormal))),
		};
		carousel.PropertyChanged += (_, e) =>
		{
			if (e.PropertyName == nameof(CarouselView.IsDragging) && carousel.IsDragging)
				dragged = true;
		};
		var page = new ContentPage { Title = "Item states", Content = new VerticalStackLayout { Spacing = 24, Children = { list, carousel } } };
		Console.Error.WriteLine("[Sailfish] Qt collection diag: item states — pushing a selectable list with a VSM Selected colour and a carousel");
		_ = navigation.PushAsync(page, false);

		View? CellRoot(ItemsView view, int row) =>
			renderer.Collection.AdapterOf(view) is { } adapter && row < adapter.Rows.Count ? adapter.Rows[row].CellViews.FirstOrDefault() : null;

		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1800), () =>
		{
			if (renderer.Collection.AdapterOf(list) is { } listAdapter && listAdapter.TryGetRowPoint(1, out var rx, out var ry))
				DiagQml.Tap(rx, ry);
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900), () =>
			{
				var root = CellRoot(list, 1);
				_qtColChecks.Check($"item states S27: a tapped row is selected ('{list.SelectedItem}'=='state row 2') and its root takes the VSM Selected colour " +
					$"({root?.BackgroundColor?.ToArgbHex()}=={StateSelected.ToArgbHex()}), the other rows stay Normal ({CellRoot(list, 0)?.BackgroundColor?.ToArgbHex()})",
					Equals(list.SelectedItem, "state row 2") && Equals(root?.BackgroundColor, StateSelected) && Equals(CellRoot(list, 0)?.BackgroundColor, StateNormal));
				var first = CellRoot(carousel, 0);
				_qtColChecks.Check($"item states S27: the carousel's first page is CurrentItem ({first?.BackgroundColor?.ToArgbHex()}=={Colors.DarkGreen.ToArgbHex()})",
					Equals(first?.BackgroundColor, Colors.DarkGreen));
				Shot(dispatcher, "collection-item-states", () =>
				{
					var host = renderer.CurrentHosts.FirstOrDefault(h => h.IsAttached && ReferenceEquals(h.Element, carousel));
					if (host is null || !QtHost.QtHostRuntime.TryItemGeometry(host.NativeHandle, out var scene))
					{
						_qtColChecks.Check("item states S27: the carousel is hosted", false);
						Finish();
						return;
					}
					// A real swipe to the left across the carousel: the next page becomes current.
					var y = scene.Y + scene.Height / 2;
					var x0 = scene.X + scene.Width * 0.8;
					var x1 = scene.X + scene.Width * 0.15;
					QtHost.QtHostRuntime.InjectPointer(0, x0, y);
					var i = 0;
					void Step()
					{
						i++;
						var x = x0 + (x1 - x0) * i / 10;
						if (i < 10)
						{
							QtHost.QtHostRuntime.InjectPointer(2, x, y);
							dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(16), Step);
							return;
						}
						QtHost.QtHostRuntime.InjectPointer(1, x, y);
						dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200), () =>
						{
							_qtColChecks.Check($"item states S27: a swipe drags the carousel (IsDragging seen {dragged}, now {carousel.IsDragging}) to page {carousel.Position}==1, " +
								$"which turns CurrentItem ({CellRoot(carousel, 1)?.BackgroundColor?.ToArgbHex()}) while page 0 leaves it ({CellRoot(carousel, 0)?.BackgroundColor?.ToArgbHex()})",
								dragged && !carousel.IsDragging && carousel.Position == 1 &&
								Equals(CellRoot(carousel, 1)?.BackgroundColor, Colors.DarkGreen) && Equals(CellRoot(carousel, 0)?.BackgroundColor, StateNormal));
							Finish();
						});
					}
					dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(16), Step);
				});
			});
		});

		void Finish()
		{
			_ = navigation.PopAsync(false);
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200), done);
		}
	}
}
