// The legacy ListView and its cells are obsolete in MAUI 11; supporting apps that still use them is the point here.
#pragma warning disable CS0618

using Microsoft.Maui.Controls;
using Microsoft.Maui.SailfishOS.Handlers;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>Tracker S32 (plan M4 steps 1–2, D2 b): the legacy ListView renders on the list adapter through its mirror
/// CollectionView — TextCell, ImageCell and ViewCell rows, ItemTapped/ItemSelected/SelectedItem from a row tap.</summary>
[Collection("renderer")]
public sealed class LegacyListViewTests
{
	public LegacyListViewTests()
	{
		// ListView (ItemsView<Cell>) reads the dispatcher at construction.
		if (Microsoft.Maui.Dispatching.DispatcherProvider.Current is not Microsoft.Maui.SailfishOS.Platform.SailfishDispatcherProvider)
			Microsoft.Maui.Dispatching.DispatcherProvider.SetCurrent(new Microsoft.Maui.SailfishOS.Platform.SailfishDispatcherProvider());
		Microsoft.Maui.SailfishOS.Platform.SailfishDispatcherProvider.BindLoopThread();
	}

	private sealed record Fruit(string Name, string Note);

	private static readonly Fruit[] Items = { new("apple", "red"), new("kiwi", "green"), new("plum", "blue") };

	private static RendererHarness Show(ListView list)
	{
		list.HeightRequest = 600;
		var h = new RendererHarness(new ContentPage { Title = "T", Content = new VerticalStackLayout { Children = { list } } });
		for (var i = 0; i < 4; i++)
			h.Poll();
		return h;
	}

	private static List<string?> RowTexts(RendererHarness h, ListView list) =>
		h.Renderer.Collection.AdapterOf(LegacyListMirror.Of(list).View)!.Rows
			.Select(r => r.CellViews.FirstOrDefault() is { } v ? FirstLabel(v)?.Text : null).ToList();

	private static IEnumerable<T> Descendants<T>(Element e)
	{
		foreach (var child in ((IVisualTreeElement)e).GetVisualChildren().OfType<Element>())
		{
			if (child is T match)
				yield return match;
			foreach (var deeper in Descendants<T>(child))
				yield return deeper;
		}
	}

	private static Label? FirstLabel(Element e) =>
		e as Label ?? ((IVisualTreeElement)e).GetVisualChildren().OfType<Element>().Select(FirstLabel).FirstOrDefault(l => l is not null);

	[Fact]
	public void TextCell_rows_show_their_text_and_detail()
	{
		var list = new ListView
		{
			ItemsSource = Items,
			ItemTemplate = new DataTemplate(() =>
			{
				var cell = new TextCell();
				cell.SetBinding(TextCell.TextProperty, nameof(Fruit.Name));
				cell.SetBinding(TextCell.DetailProperty, nameof(Fruit.Note));
				return cell;
			}),
		};
		using var h = Show(list);

		Assert.IsType<SailfishLegacyListViewHandler>(list.Handler);
		Assert.Single(h.Shim.ByUri("list-view"), o => !o.Destroyed);
		Assert.Equal(new[] { "apple", "kiwi", "plum" }, RowTexts(h, list));
		var detail = Descendants<Label>(h.Renderer.Collection.AdapterOf(LegacyListMirror.Of(list).View)!.Rows[1].CellViews[0]!).Last();
		Assert.Equal("green", detail.Text);
	}

	[Fact]
	public void A_list_without_a_template_shows_the_items_text()
	{
		using var h = Show(new ListView { ItemsSource = new[] { "one", "two" } });
		var list = (ListView)((VerticalStackLayout)((ContentPage)h.Window.Page!).Content).Children[0];
		Assert.Equal(new[] { "one", "two" }, RowTexts(h, list));
	}

	[Fact]
	public void ViewCell_and_ImageCell_rows_carry_their_views()
	{
		var viewList = new ListView
		{
			ItemsSource = Items,
			ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label();
				label.SetBinding(Label.TextProperty, nameof(Fruit.Name));
				return new ViewCell { View = new Grid { Padding = 8, Children = { label } } };
			}),
		};
		using (var h = Show(viewList))
			Assert.Equal(new[] { "apple", "kiwi", "plum" }, RowTexts(h, viewList));

		var imageList = new ListView
		{
			ItemsSource = Items,
			ItemTemplate = new DataTemplate(() =>
			{
				var cell = new ImageCell { ImageSource = "dotnet_bot.png" };
				cell.SetBinding(TextCell.TextProperty, nameof(Fruit.Name));
				return cell;
			}),
		};
		using (var h = Show(imageList))
		{
			Assert.Equal(new[] { "apple", "kiwi", "plum" }, RowTexts(h, imageList));
			var row = h.Renderer.Collection.AdapterOf(LegacyListMirror.Of(imageList).View)!.Rows[0].CellViews[0]!;
			Assert.NotNull(Descendants<Image>(row).Single().Source);
		}
	}

	[Fact]
	public void A_row_tap_selects_and_raises_ItemTapped_and_ItemSelected()
	{
		var tapped = new List<object>();
		var selected = new List<object?>();
		var list = new ListView { ItemsSource = Items };
		list.ItemTapped += (_, e) => tapped.Add(e.Item);
		list.ItemSelected += (_, e) => selected.Add(e.SelectedItem);
		using var h = Show(list);
		var native = h.Shim.ByUri("list-view").Single(o => !o.Destroyed);

		h.Renderer.HandleNativeEvent("list-item-tapped", $"{{\"id\":\"{native.Id}\",\"row\":1,\"cell\":0}}");
		h.Poll();

		Assert.Same(Items[1], list.SelectedItem);
		Assert.Equal(new object[] { Items[1] }, tapped);
		Assert.Equal(new object?[] { Items[1] }, selected);

		// Selected from code: the mirror follows.
		list.SelectedItem = Items[2];
		Assert.Same(Items[2], LegacyListMirror.Of(list).View.SelectedItem);
	}

	[Fact]
	public void A_list_without_selection_still_reports_taps()
	{
		var tapped = 0;
		var list = new ListView { ItemsSource = Items, SelectionMode = ListViewSelectionMode.None };
		list.ItemTapped += (_, _) => tapped++;
		using var h = Show(list);
		var native = h.Shim.ByUri("list-view").Single(o => !o.Destroyed);

		h.Renderer.HandleNativeEvent("list-item-tapped", $"{{\"id\":\"{native.Id}\",\"row\":0,\"cell\":0}}");
		h.Poll();

		Assert.Equal(1, tapped);
		Assert.Null(list.SelectedItem);
		Assert.Null(LegacyListMirror.Of(list).View.SelectedItem);
	}

	private sealed class FruitGroup : List<Fruit>
	{
		public FruitGroup(string title, IEnumerable<Fruit> items) : base(items) => Title = title;
		public string Title { get; }
	}

	[Fact]
	public void A_grouped_list_shows_its_group_headers_and_taps_report_the_group()
	{
		var groups = new List<FruitGroup>
		{
			new("Red", new[] { Items[0] }),
			new("Other", new[] { Items[1], Items[2] }),
		};
		var tapped = new List<(object? Group, object Item)>();
		var list = new ListView
		{
			ItemsSource = groups,
			IsGroupingEnabled = true,
			GroupDisplayBinding = new Binding(nameof(FruitGroup.Title)),
		};
		list.ItemTapped += (_, e) => tapped.Add((e.Group, e.Item));
		using var h = Show(list);
		var adapter = h.Renderer.Collection.AdapterOf(LegacyListMirror.Of(list).View)!;

		var headers = adapter.Rows.Where(r => r.Kind == QtHostCollectionBridge.KindGroupHeader)
			.Select(r => FirstLabel(r.CellViews[0]!)?.Text).ToList();
		Assert.Equal(new[] { "Red", "Other" }, headers);

		var native = h.Shim.ByUri("list-view").Single(o => !o.Destroyed);
		var plumRow = adapter.Rows.FindIndex(r => r.Kind == QtHostCollectionBridge.KindItem && ReferenceEquals(r.CellItems[0], Items[2]));
		h.Renderer.HandleNativeEvent("list-item-tapped", $"{{\"id\":\"{native.Id}\",\"row\":{plumRow},\"cell\":0}}");
		h.Poll();

		Assert.Equal(new[] { ((object?)groups[1], (object)Items[2]) }, tapped);
		Assert.Same(Items[2], list.SelectedItem);
	}

	[Fact]
	public void Header_footer_and_separators_reach_the_rows()
	{
		var list = new ListView { ItemsSource = Items, Header = "list header", Footer = "list footer", SeparatorColor = Microsoft.Maui.Graphics.Colors.Red };
		using var h = Show(list);
		var mirror = LegacyListMirror.Of(list).View;
		Assert.Equal("list header", mirror.Header);
		Assert.Equal("list footer", mirror.Footer);
		var row = h.Renderer.Collection.AdapterOf(mirror)!.Rows.First(r => r.Kind == QtHostCollectionBridge.KindItem).CellViews[0]!;
		var line = ((IVisualTreeElement)row).GetVisualChildren().OfType<Grid>().Single().Children.OfType<BoxView>().Single();
		Assert.Equal(Microsoft.Maui.Graphics.Colors.Red, line.Color);

		list.SeparatorVisibility = SeparatorVisibility.None;
		for (var i = 0; i < 4; i++)
			h.Poll();
		var plain = h.Renderer.Collection.AdapterOf(mirror)!.Rows.First(r => r.Kind == QtHostCollectionBridge.KindItem).CellViews[0]!;
		Assert.Empty(((IVisualTreeElement)plain).GetVisualChildren().OfType<Grid>());
	}

	[Fact]
	public void ScrollTo_reaches_the_native_list()
	{
		var many = Enumerable.Range(0, 60).Select(i => new Fruit($"fruit {i}", "")).ToArray();
		var list = new ListView { ItemsSource = many };
		using var h = Show(list);
		var native = h.Shim.ByUri("list-view").Single(o => !o.Destroyed);

		list.ScrollTo(many[40], ScrollToPosition.Start, animated: false);

		using var command = System.Text.Json.JsonDocument.Parse(h.Shim.Commands.Last(c => c.Id == native.Id).Json);
		Assert.Equal("scrollTo", command.RootElement.GetProperty("name").GetString());
		Assert.Equal(40, command.RootElement.GetProperty("row").GetInt32());
	}

	[Fact]
	public void A_pull_refreshes_the_list_through_its_command_and_the_app_ends_it()
	{
		var refreshed = 0;
		var list = new ListView
		{
			ItemsSource = Items,
			IsPullToRefreshEnabled = true,
			RefreshCommand = new Command(() => refreshed++),
		};
		using var h = Show(list);
		var native = h.Shim.ByUri("list-view").Single(o => !o.Destroyed);
		Assert.Equal(QtHostPageRenderer.RefreshId, native.Text("mauiRefreshId"));

		h.Renderer.HandleNativeEvent("refresh-requested", $"{{\"id\":\"{QtHostPageRenderer.RefreshId}\"}}");
		h.Poll();
		Assert.Equal(1, refreshed);
		Assert.True(list.IsRefreshing);

		list.EndRefresh();
		h.Poll();
		Assert.False(((RefreshView)LegacyListMirror.Of(list).Root).IsRefreshing);
	}

	[Fact]
	public void Scrolling_raises_ItemAppearing_and_ItemDisappearing_for_the_rows_in_view()
	{
		var many = Enumerable.Range(0, 30).Select(i => new Fruit($"fruit {i}", "")).ToArray();
		var appeared = new List<object>();
		var disappeared = new List<object>();
		var list = new ListView { ItemsSource = many };
		list.ItemAppearing += (_, e) => appeared.Add(e.Item);
		list.ItemDisappearing += (_, e) => disappeared.Add(e.Item);
		using var h = Show(list);
		var native = h.Shim.ByUri("list-view").Single(o => !o.Destroyed);

		h.Renderer.HandleNativeEvent("list-scroll", $"{{\"id\":\"{native.Id}\",\"y\":1,\"first\":0,\"last\":4,\"count\":30}}");
		Assert.Equal(many.Take(5), appeared.Cast<Fruit>().OrderBy(f => Array.IndexOf(many, f)));

		appeared.Clear();
		h.Renderer.HandleNativeEvent("list-scroll", $"{{\"id\":\"{native.Id}\",\"y\":400,\"first\":3,\"last\":7,\"count\":30}}");
		Assert.Equal(new[] { many[5], many[6], many[7] }, appeared.Cast<Fruit>().OrderBy(f => Array.IndexOf(many, f)));
		Assert.Equal(new[] { many[0], many[1], many[2] }, disappeared.Cast<Fruit>().OrderBy(f => Array.IndexOf(many, f)));
	}
}
