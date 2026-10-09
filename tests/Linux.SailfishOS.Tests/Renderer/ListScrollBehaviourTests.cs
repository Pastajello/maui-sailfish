using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.Maui.Controls;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>Tracker S26 (plan M8 steps 1, 2, 4): ItemsUpdatingScrollMode reaches the native list, a Span or spacing set
/// on the same ItemsLayout object rebuilds the rows, Scrolled carries deltas, ScrollTo(animate:) asks for an animation.</summary>
[Collection("renderer")]
public sealed class ListScrollBehaviourTests
{
	private static CollectionView List(System.Collections.IEnumerable items) => new()
	{
		ItemsSource = items,
		ItemTemplate = new DataTemplate(() =>
		{
			var label = new Label();
			label.SetBinding(Label.TextProperty, ".");
			return label;
		}),
		HeightRequest = 600,
	};

	private static RendererHarness Show(CollectionView list)
	{
		var h = new RendererHarness(new ContentPage { Title = "T", Content = new VerticalStackLayout { Children = { list } } });
		for (var i = 0; i < 4; i++)
			h.Poll();
		return h;
	}

	private static int RowCount(FakeShim.FakeObject native)
	{
		using var rows = JsonDocument.Parse(native.Text("mauiRowsJson")!);
		return rows.RootElement.GetArrayLength();
	}

	[Fact]
	public void ItemsUpdatingScrollMode_reaches_the_list_and_follows_a_change()
	{
		var list = List(new ObservableCollection<string> { "a", "b" });
		list.ItemsUpdatingScrollMode = ItemsUpdatingScrollMode.KeepLastItemInView;
		using var h = Show(list);
		var native = h.Shim.ByUri("list-view").Single();
		Assert.Equal("2", native.Text("mauiUpdatingMode"));

		list.ItemsUpdatingScrollMode = ItemsUpdatingScrollMode.KeepScrollOffset;
		h.Poll();
		Assert.Equal("1", native.Text("mauiUpdatingMode"));
	}

	[Fact]
	public void A_span_set_on_the_same_grid_layout_rebuilds_the_rows()
	{
		var layout = new GridItemsLayout(2, ItemsLayoutOrientation.Vertical);
		var list = List(Enumerable.Range(0, 6).Select(i => $"item {i}").ToList());
		list.ItemsLayout = layout;
		using var h = Show(list);
		var native = h.Shim.ByUri("list-view").Single();
		Assert.Equal(3, RowCount(native));

		layout.Span = 3;
		for (var i = 0; i < 4; i++)
			h.Poll();
		Assert.Equal(2, RowCount(native));
	}

	[Fact]
	public void Scrolled_carries_the_delta_since_the_last_report()
	{
		var list = List(Enumerable.Range(0, 40).Select(i => $"item {i}").ToList());
		var deltas = new List<double>();
		list.Scrolled += (_, e) => deltas.Add(e.VerticalDelta);
		using var h = Show(list);
		var native = h.Shim.ByUri("list-view").Single();
		var y1 = QtHostUnits.ToQtUnits(100);
		var y2 = QtHostUnits.ToQtUnits(60);

		h.Renderer.HandleNativeEvent("list-scroll", $"{{\"id\":\"{native.Id}\",\"y\":{y1},\"first\":2,\"last\":8,\"count\":40}}");
		h.Renderer.HandleNativeEvent("list-scroll", $"{{\"id\":\"{native.Id}\",\"y\":{y2},\"first\":1,\"last\":7,\"count\":40}}");

		Assert.Equal(2, deltas.Count);
		Assert.Equal(100, deltas[0], 1);
		Assert.Equal(-40, deltas[1], 1);
	}

	[Fact]
	public void An_animated_ScrollTo_asks_the_list_to_animate()
	{
		var list = List(Enumerable.Range(0, 40).Select(i => $"item {i}").ToList());
		using var h = Show(list);
		var native = h.Shim.ByUri("list-view").Single();

		list.ScrollTo(30, position: ScrollToPosition.End, animate: true);
		using (var animated = JsonDocument.Parse(h.Shim.Commands.Last(c => c.Id == native.Id).Json))
			Assert.True(animated.RootElement.GetProperty("animate").GetBoolean());

		list.ScrollTo(5, animate: false);
		using var jump = JsonDocument.Parse(h.Shim.Commands.Last(c => c.Id == native.Id).Json);
		Assert.False(jump.RootElement.GetProperty("animate").GetBoolean());
	}
}
