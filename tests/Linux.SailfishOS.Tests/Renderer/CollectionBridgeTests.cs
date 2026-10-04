using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.Maui.Controls;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>The collection bridge ↔ adapter contract (docs/architecture-handoff.md W4): parked attaches, row keys across
/// collection changes, and a list that leaves the page.</summary>
[Collection("renderer")]
public sealed class CollectionBridgeTests
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

	private static ContentPage Page(View content) => new() { Title = "T", Content = new VerticalStackLayout { Children = { content } } };

	// Qt 5.6 can report a delegate before it resolves by name: the attach is parked and retried by the pending pass.
	[Fact]
	public void An_attach_of_a_delegate_not_resolvable_yet_is_retried_until_it_is()
	{
		var list = List(new[] { "alpha", "beta" });
		using var h = new RendererHarness(Page(list));
		var native = h.Shim.ByUri("list-view").Single();
		var dg = $"maui_{native.Id}__r0";
		h.Renderer.HandleNativeEvent("list-item-attached", $"{{\"id\":\"{native.Id}\",\"row\":0,\"dg\":\"{dg}\"}}");
		h.Poll();
		Assert.DoesNotContain(h.Shim.ByUri("label"), l => l.Text("text") == "alpha" && !l.Destroyed);

		h.Shim.AddNative(dg);   // the delegate resolves now
		for (var i = 0; i < 3; i++)
			h.Poll();
		Assert.Contains(h.Shim.ByUri("label"), l => l.Text("text") == "alpha" && !l.Destroyed);
	}

	// An Add keeps the rows that were there: same keys, so QML keeps their delegates.
	[Fact]
	public void Adding_an_item_keeps_the_keys_of_the_rows_already_there()
	{
		var items = new ObservableCollection<string> { "a", "b", "c" };
		using var h = new RendererHarness(Page(List(items)));
		static List<long> Keys(RendererHarness h)
		{
			using var doc = JsonDocument.Parse(h.Shim.ByUri("list-view").Single().Text("mauiRowsJson")!);
			return doc.RootElement.EnumerateArray().Select(r => r.GetProperty("k").GetInt64()).ToList();
		}
		var before = Keys(h);

		items.Add("d");
		for (var i = 0; i < 3; i++)
			h.Poll();
		var after = Keys(h);
		Assert.Equal(4, after.Count);
		Assert.Equal(before, after.Take(3));
	}

	// A list removed from the page leaves the bridge: its rows stop counting and nothing keeps it alive.
	[Fact]
	public void A_list_removed_from_the_page_is_unregistered()
	{
		var list = List(new[] { "one", "two" });
		var stack = new VerticalStackLayout { Children = { list } };
		using var h = new RendererHarness(new ContentPage { Title = "T", Content = stack });
		Assert.Equal(2, h.Renderer.Collection.TotalRows);

		stack.Children.Remove(list);
		for (var i = 0; i < 3; i++)
			h.Poll();
		Assert.Equal(0, h.Renderer.Collection.TotalRows);
		Assert.Null(h.Renderer.Collection.FirstListObjectName);
	}

	// C9: the row keys C# writes into mauiRowsJson are the roles ListView.qml reads (one renamed side would leave
	// every row at height 0 or untappable without an error).
	[Fact]
	public void The_row_json_keys_are_the_roles_the_list_adapter_reads()
	{
		var qml = File.ReadAllText(Path.Combine(Repo.Root, "src/Linux.SailfishOS/Platform/QtHost/qml/containers/ListView.qml"));
		foreach (var key in new[] { QtHostListAdapter.RowJson.Key, QtHostListAdapter.RowJson.Row, QtHostListAdapter.RowJson.Height,
			         QtHostListAdapter.RowJson.Tap, QtHostListAdapter.RowJson.Cells })
			Assert.Matches($@"\.{key}\b", qml);
		Assert.Contains($"\"{QtHostListAdapter.RowJson.Selected}\"", qml);
	}

	// C9: the carousel's position is pushed only when MAUI's differs from what the native carousel shows. A swipe sets
	// the adapter's mauiPosition itself and reports it, so MAUI following it sends nothing back, and MAUI moving away
	// again is a real change that is sent (before, every layout push re-sent the position: the only way the adapter's
	// stale mauiPosition caught up).
	[Fact]
	public void A_swiped_carousel_is_not_echoed_and_a_later_move_is_pushed()
	{
		var carousel = new CarouselView
		{
			ItemsSource = new[] { "a", "b", "c", "d" },
			ItemTemplate = new DataTemplate(() => new Label { Text = "page" }),
			HeightRequest = 120,
		};
		using var h = new RendererHarness(Page(carousel));
		h.Poll();
		var native = h.Shim.ByUri("carousel-view").Single();
		var writes = h.Shim.PropertyWrites.Count;

		h.Renderer.HandleNativeEvent("carousel-position", $"{{\"id\":\"{native.Id}\",\"index\":2}}");   // a swipe
		h.Poll();
		Assert.Equal(2, carousel.Position);
		Assert.DoesNotContain(h.Shim.PropertyWrites.Skip(writes), w => w.Id == native.Id && w.Name == "mauiPosition");

		carousel.Position = 0;
		h.Poll();
		Assert.Contains(h.Shim.PropertyWrites.Skip(writes), w => w.Id == native.Id && w.Name == "mauiPosition");
		Assert.Equal("0", native.Text("mauiPosition"));
	}

	// C9: an item that starts two grid rows has two reusable rows; a rebuild takes the one with the same cells, not
	// only the first one queued, so the unchanged row keeps its key (and its QML delegate).
	[Fact]
	public void A_grid_row_whose_first_item_repeats_is_reused_by_its_cells()
	{
		var a = new object();
		var b = new object();
		var c = new object();
		var items = new ObservableCollection<object> { a, b, a, c };
		var list = List(items);
		list.ItemsLayout = new GridItemsLayout(2, ItemsLayoutOrientation.Vertical);
		list.ItemTemplate = new DataTemplate(() => new Label { Text = "cell" });
		using var h = new RendererHarness(Page(list));
		var adapter = ((Microsoft.Maui.SailfishOS.Handlers.SailfishListViewHandler)list.Handler!).Adapter!;
		var acKey = adapter.Rows.Single(r => r.CellItems.Count == 2 && r.CellItems[1] == c).Key;

		items[1] = c;   // [a, c, a, c]: the first row is now (a, c), which the old second row already is
		for (var i = 0; i < 3; i++)
			h.Poll();

		Assert.Equal(acKey, adapter.Rows[0].Key);
	}

	private static CarouselView Carousel(bool loop) => new()
	{
		ItemsSource = Enumerable.Range(0, 6).Select(i => $"page {i}").ToList(),
		ItemTemplate = new DataTemplate(() => new Label { Text = "page" }),
		HeightRequest = 120,
		Loop = loop,
	};

	// C9: Loop picks the adapter (a looping carousel is a PathView, a ListView cannot wrap). Changing it at run time
	// used to keep the first one; now the list gets the other adapter, with its rows.
	[Fact]
	public void Changing_Loop_swaps_the_carousel_adapter()
	{
		var carousel = Carousel(loop: false);
		using var h = new RendererHarness(Page(carousel));
		h.Poll();
		var before = h.Shim.ByUri("list-view").Single(o => !o.Destroyed);

		carousel.Loop = true;
		for (var i = 0; i < 3; i++)
			h.Poll();

		Assert.True(before.Destroyed);
		var after = h.Shim.ByUri("carousel-view").Single(o => !o.Destroyed);
		Assert.NotNull(after.Text("mauiRowsJson"));
		Assert.Equal(6, h.Renderer.Collection.TotalRows);
	}

	// C9: the looping carousel reports its page as a scroll, so Scrolled and RemainingItemsThresholdReached work with
	// Loop=true as without it.
	[Fact]
	public void A_looping_carousel_raises_Scrolled_and_the_remaining_items_threshold()
	{
		var carousel = Carousel(loop: true);
		carousel.RemainingItemsThreshold = 1;
		var scrolled = 0;
		var reached = 0;
		carousel.Scrolled += (_, _) => scrolled++;
		carousel.RemainingItemsThresholdReached += (_, _) => reached++;
		using var h = new RendererHarness(Page(carousel));
		h.Poll();
		var native = h.Shim.ByUri("carousel-view").Single();

		h.Renderer.HandleNativeEvent("list-scroll", $"{{\"id\":\"{native.Id}\",\"y\":1200,\"first\":4,\"last\":4,\"count\":6}}");

		Assert.Equal(1, scrolled);
		Assert.Equal(1, reached);
	}

	// A RefreshView around a list hands it the pull-to-refresh: the list host carries the armed id, and the release
	// gesture on it sets IsRefreshing (the page does not get a scroll surface of its own for it).
	[Fact]
	public void A_RefreshView_around_a_list_is_consumed_by_the_list()
	{
		var list = List(new[] { "a", "b" });
		var refresh = new RefreshView { Content = list };
		using var h = new RendererHarness(new ContentPage { Title = "T", Content = refresh });
		h.Poll();
		var native = h.Shim.ByUri("list-view").Single();
		Assert.Equal(QtHostPageRenderer.RefreshId, native.Text("mauiRefreshId"));

		h.Renderer.HandleNativeEvent("refresh-requested", $"{{\"id\":\"{QtHostPageRenderer.RefreshId}\"}}");

		Assert.True(refresh.IsRefreshing);
	}

	// SelectionMode.Multiple: each tap toggles its item in SelectedItems, and the highlight follows.
	[Fact]
	public void Taps_under_multiple_selection_toggle_their_items()
	{
		var list = List(new[] { "a", "b", "c" });
		list.SelectionMode = SelectionMode.Multiple;
		using var h = new RendererHarness(Page(list));
		h.Poll();
		var native = h.Shim.ByUri("list-view").Single();
		void Tap(int row) => h.Renderer.HandleNativeEvent("list-item-tapped", $"{{\"id\":\"{native.Id}\",\"row\":{row},\"cell\":0}}");

		Tap(0);
		Tap(2);
		h.Poll();
		Assert.Equal(new object[] { "a", "c" }, list.SelectedItems.ToArray());
		Assert.Equal("0,2", native.Text("mauiSelectedRows"));

		Tap(0);
		h.Poll();
		Assert.Equal(new object[] { "c" }, list.SelectedItems.ToArray());
		Assert.Equal("2", native.Text("mauiSelectedRows"));
	}

	// Header, footer and empty view are slots: each gets its MAUI view materialized into the adapter's slot item and
	// its height pushed; a slot whose view changes size is measured again.
	[Fact]
	public void Header_footer_and_empty_slots_are_built_and_remeasured()
	{
		var header = new Label { Text = "header", HeightRequest = 40 };
		var list = List(new List<string>());
		list.Header = header;
		list.Footer = new Label { Text = "footer", HeightRequest = 30 };
		list.EmptyView = new Label { Text = "nothing here", HeightRequest = 50 };
		using var h = new RendererHarness(Page(list));
		h.Poll();
		var native = h.Shim.ByUri("list-view").Single();
		foreach (var slot in new[] { "header", "footer", "empty" })
			h.Shim.AddNative($"maui_{native.Id}__{slot}");   // the adapter's slot items exist now
		for (var i = 0; i < 4; i++)
			h.Poll();

		Assert.Contains(h.Shim.ByUri("label"), l => l.Text("text") == "header" && !l.Destroyed);
		Assert.Contains(h.Shim.ByUri("label"), l => l.Text("text") == "footer" && !l.Destroyed);
		Assert.Contains(h.Shim.ByUri("label"), l => l.Text("text") == "nothing here" && !l.Destroyed);
		var headerH = native.Text("mauiHeaderH");
		Assert.NotNull(headerH);
		Assert.NotEqual("0", headerH);

		header.HeightRequest = 80;
		for (var i = 0; i < 4; i++)
			h.Poll();
		Assert.NotEqual(headerH, native.Text("mauiHeaderH"));
	}
}
