using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.Maui.Controls;
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
}
