using Microsoft.Maui.Controls;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>Tracker S28 (plan M8 steps 5, 7): ItemSizingStrategy.MeasureFirstItem templates only the first item at
/// build and the rest when their delegates materialize, at the first item's height; ItemsLayout snap points reach the
/// native list.</summary>
[Collection("renderer")]
public sealed class ListSizingTests
{
	private static (RendererHarness H, FakeShim.FakeObject Native, CollectionView List) Show(ItemSizingStrategy sizing, int count = 50)
	{
		var list = new CollectionView
		{
			ItemsSource = Enumerable.Range(0, count).Select(i => $"item {i}").ToList(),
			ItemSizingStrategy = sizing,
			ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label { Padding = 12 };
				label.SetBinding(Label.TextProperty, ".");
				var tap = new TapGestureRecognizer();
				label.GestureRecognizers.Add(tap);
				return label;
			}),
			HeightRequest = 600,
		};
		var h = new RendererHarness(new ContentPage { Content = new VerticalStackLayout { list } });
		for (var i = 0; i < 4; i++)
			h.Poll();
		return (h, h.Shim.ByUri("list-view").Single(), list);
	}

	private static void Attach(RendererHarness h, FakeShim.FakeObject native, int row)
	{
		var dg = $"maui_{native.Id}__r{row}_{Guid.NewGuid():N}";
		h.Shim.AddNative(dg);
		h.Renderer.HandleNativeEvent("list-item-attached", $"{{\"id\":\"{native.Id}\",\"row\":{row},\"dg\":\"{dg}\"}}");
	}

	[Fact]
	public void MeasureFirstItem_templates_the_first_item_only_and_the_rest_when_they_show()
	{
		var (h, native, list) = Show(ItemSizingStrategy.MeasureFirstItem);
		using var _ = h;
		var adapter = h.Renderer.Collection.AdapterOf(list)!;

		Assert.NotNull(adapter.Rows[0].CellViews[0]);
		Assert.All(adapter.Rows.Skip(1), r => Assert.Null(r.CellViews[0]));
		Assert.All(adapter.Rows, r => Assert.Equal(adapter.Rows[0].HeightDp, r.HeightDp));
		// The tap flag of rows not templated yet comes from the first item's template.
		Assert.Contains("\"t\":2", native.Text("mauiRowsJson")!.Split("},{")[30]);

		Attach(h, native, 5);   // on screen: materialized at once (off-screen rows wait for the pending pass)
		h.Poll();

		var view = Assert.IsType<Label>(adapter.Rows[5].CellViews[0]);
		Assert.Equal("item 5", view.Text);
		Assert.Equal(adapter.Rows[0].HeightDp, view.Bounds.Height, 1);
		Assert.Equal(1, adapter.LazyTemplated);
	}

	[Fact]
	public void MeasureAllItems_templates_every_item_at_build()
	{
		var (h, _, list) = Show(ItemSizingStrategy.MeasureAllItems);
		using var _h = h;
		var adapter = h.Renderer.Collection.AdapterOf(list)!;

		Assert.All(adapter.Rows, r => Assert.NotNull(r.CellViews[0]));
		Assert.Equal(0, adapter.LazyTemplated);
	}

	[Fact]
	public void Switching_the_strategy_at_runtime_rebuilds_the_rows()
	{
		var (h, _, list) = Show(ItemSizingStrategy.MeasureFirstItem);
		using var _h = h;

		list.ItemSizingStrategy = ItemSizingStrategy.MeasureAllItems;
		for (var i = 0; i < 4; i++)
			h.Poll();

		Assert.All(h.Renderer.Collection.AdapterOf(list)!.Rows, r => Assert.NotNull(r.CellViews[0]));
	}

	[Fact]
	public void Snap_points_reach_the_list_and_follow_the_layout_object()
	{
		var layout = new LinearItemsLayout(ItemsLayoutOrientation.Vertical)
		{
			SnapPointsType = SnapPointsType.Mandatory,
			SnapPointsAlignment = SnapPointsAlignment.Center,
		};
		var (h, native, list) = Show(ItemSizingStrategy.MeasureAllItems, 10);
		using var _ = h;
		list.ItemsLayout = layout;
		for (var i = 0; i < 4; i++)
			h.Poll();
		Assert.Equal("1", native.Text("mauiSnapType"));
		Assert.Equal("1", native.Text("mauiSnapAlign"));

		layout.SnapPointsType = SnapPointsType.MandatorySingle;
		layout.SnapPointsAlignment = SnapPointsAlignment.End;
		h.Poll();
		Assert.Equal("2", native.Text("mauiSnapType"));
		Assert.Equal("2", native.Text("mauiSnapAlign"));
	}

	[Fact]
	public void A_list_inside_a_ScrollView_holds_every_row_and_leaves_the_scrolling_to_it()
	{
		var list = new CollectionView
		{
			ItemsSource = Enumerable.Range(0, 300).Select(i => $"item {i}").ToList(),
			ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label { Padding = 12 };
				label.SetBinding(Label.TextProperty, ".");
				return label;
			}),
		};
		using var h = new RendererHarness(new ContentPage { Content = new ScrollView { Content = new VerticalStackLayout { list } } });
		for (var i = 0; i < 4; i++)
			h.Poll();
		var native = h.Shim.ByUri("list-view").Single();
		var adapter = h.Renderer.Collection.AdapterOf(list)!;

		Assert.True(adapter.Unbounded);
		Assert.Equal("true", native.Text("mauiUnbounded"), ignoreCase: true);
		Assert.True(adapter.DelegateCap >= 300, $"cap {adapter.DelegateCap}");
		// As tall as its rows: the ScrollView scrolls it.
		Assert.Equal(adapter.ContentExtentDp, list.Height, 1);
	}

	[Fact]
	public void A_bounded_list_keeps_the_delegate_cap()
	{
		var (h, native, list) = Show(ItemSizingStrategy.MeasureAllItems);
		using var _ = h;
		var adapter = h.Renderer.Collection.AdapterOf(list)!;

		Assert.False(adapter.Unbounded);
		Assert.Equal(Microsoft.Maui.SailfishOS.Platform.QtHost.QtHostCollectionBridge.MaxDelegates, adapter.DelegateCap);
	}
}
