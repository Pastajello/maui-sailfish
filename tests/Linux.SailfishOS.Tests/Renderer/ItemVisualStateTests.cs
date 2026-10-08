using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>Tracker S27 (plan M8 steps 3, 6, D4 a): the Selected/Normal states on selected cell roots, the carousel's
/// CurrentItem/PreviousItem/NextItem/DefaultItem states and VisibleViews, IsDragging/IsScrolling from the native carousel,
/// IsScrollAnimated pushed.</summary>
[Collection("renderer")]
public sealed class ItemVisualStateTests
{
	private static RendererHarness Show(View content)
	{
		var h = new RendererHarness(new ContentPage { Title = "T", Content = new VerticalStackLayout { Children = { content } } });
		for (var i = 0; i < 4; i++)
			h.Poll();
		return h;
	}

	private static Grid StatefulRoot(params string[] states)
	{
		var root = new Grid { HeightRequest = 60, Children = { new Label { Text = "x" } } };
		var group = new VisualStateGroup { Name = "States" };
		foreach (var name in states)
			group.States.Add(new VisualState
			{
				Name = name,
				Setters = { new Setter { Property = VisualElement.BackgroundColorProperty, Value = name == "Normal" ? Colors.Black : Colors.Red } },
			});
		VisualStateManager.SetVisualStateGroups(root, new VisualStateGroupList { group });
		return root;
	}

	private static string? StateOf(View? view) =>
		view is null ? null : VisualStateManager.GetVisualStateGroups(view)[0].CurrentState?.Name;

	[Fact]
	public void A_tapped_row_goes_to_Selected_and_the_previous_one_back_to_Normal()
	{
		var list = new CollectionView
		{
			ItemsSource = new[] { "a", "b", "c" },
			SelectionMode = SelectionMode.Single,
			ItemTemplate = new DataTemplate(() => StatefulRoot("Normal", "Selected")),
			HeightRequest = 400,
		};
		using var h = Show(list);
		var native = h.Shim.ByUri("list-view").Single();
		var adapter = h.Renderer.Collection.AdapterOf(list)!;
		View? Cell(int row) => adapter.Rows[row].CellViews[0];

		h.Renderer.HandleNativeEvent("list-item-tapped", $"{{\"id\":\"{native.Id}\",\"row\":1,\"cell\":0}}");
		h.Poll();
		Assert.Equal("Selected", StateOf(Cell(1)));
		Assert.Equal(Colors.Red, Cell(1)!.BackgroundColor);

		h.Renderer.HandleNativeEvent("list-item-tapped", $"{{\"id\":\"{native.Id}\",\"row\":2,\"cell\":0}}");
		h.Poll();
		Assert.Equal("Normal", StateOf(Cell(1)));
		Assert.Equal("Selected", StateOf(Cell(2)));
	}

	[Fact]
	public void The_carousel_items_take_their_position_states_and_VisibleViews_follows()
	{
		var carousel = new CarouselView
		{
			ItemsSource = new[] { "one", "two", "three", "four" },
			ItemTemplate = new DataTemplate(() => StatefulRoot(CarouselView.CurrentItemVisualState, CarouselView.PreviousItemVisualState,
				CarouselView.NextItemVisualState, CarouselView.DefaultItemVisualState)),
			HeightRequest = 300,
		};
		using var h = Show(carousel);
		var native = h.Shim.Objects.Single(o => !o.Destroyed && o.Uri is "list-view" or "carousel-view");
		var adapter = h.Renderer.Collection.AdapterOf(carousel)!;
		View? Page(int i) => adapter.Rows.Where(r => r.Kind == QtHostCollectionBridge.KindItem).ElementAt(i).CellViews[0];

		h.Renderer.HandleNativeEvent("carousel-position", $"{{\"id\":\"{native.Id}\",\"index\":1}}");

		Assert.Equal(1, carousel.Position);
		Assert.Equal(new[] { "PreviousItem", "CurrentItem", "NextItem", "DefaultItem" }, Enumerable.Range(0, 4).Select(i => StateOf(Page(i))));
		Assert.Equal(new[] { Page(1) }, carousel.VisibleViews);
	}

	[Fact]
	public void The_native_carousel_motion_drives_IsDragging_and_IsScrolling()
	{
		var carousel = new CarouselView { ItemsSource = new[] { "one", "two" }, HeightRequest = 300, IsScrollAnimated = false };
		using var h = Show(carousel);
		var native = h.Shim.Objects.Single(o => !o.Destroyed && o.Uri is "list-view" or "carousel-view");
		Assert.Equal("false", native.Text("mauiScrollAnimated"), ignoreCase: true);

		h.Renderer.HandleNativeEvent("carousel-motion", $"{{\"id\":\"{native.Id}\",\"dragging\":true,\"moving\":true}}");
		Assert.True(carousel.IsDragging);
		Assert.True(carousel.IsScrolling);

		h.Renderer.HandleNativeEvent("carousel-motion", $"{{\"id\":\"{native.Id}\",\"dragging\":false,\"moving\":false}}");
		Assert.False(carousel.IsDragging);
		Assert.False(carousel.IsScrolling);
	}
}
