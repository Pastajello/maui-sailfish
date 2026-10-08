using System.Text.Json;
using Microsoft.Maui.Controls;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>Tracker S21 (plan M16 step 4, D14 a): Shell.SearchHandler as a Silica SearchField under the header — Query
/// both ways without echoing the field's own keystrokes, Command on the enter key, the visibility rules; tracker S22: the
/// results list (ShowsResults + ItemsSource) on the list adapter, a picked item.</summary>
[Collection("renderer")]
public sealed class SearchHandlerTests
{
	public SearchHandlerTests()
	{
		// SearchHandler.ItemsSource builds a ListProxy on the element's dispatcher.
		if (Microsoft.Maui.Dispatching.DispatcherProvider.Current is not Microsoft.Maui.SailfishOS.Platform.SailfishDispatcherProvider)
			Microsoft.Maui.Dispatching.DispatcherProvider.SetCurrent(new Microsoft.Maui.SailfishOS.Platform.SailfishDispatcherProvider());
		Microsoft.Maui.SailfishOS.Platform.SailfishDispatcherProvider.BindLoopThread();
	}

	private static void Settle(RendererHarness h, int polls = 6)
	{
		for (var i = 0; i < polls; i++)
			h.Poll();
	}

	private static JsonElement? LastSearch(RendererHarness h) =>
		h.Shim.Ops.LastOrDefault(op => op.GetProperty("op").GetString() == "search") is { ValueKind: JsonValueKind.Object } op ? op : null;

	private static bool SearchShown(RendererHarness h) => LastSearch(h)?.GetProperty("on").GetBoolean() ?? false;

	private static (RendererHarness Harness, SearchHandler Handler, ContentPage Page) ShellWithSearch(SearchHandler? handler = null)
	{
		handler ??= new SearchHandler { Placeholder = "Find a fruit" };
		var page = new ContentPage { Title = "Fruit", Content = new Label { Text = "fruit" } };
		Shell.SetSearchHandler(page, handler);
		var shell = new Shell();
		shell.Items.Add(new ShellContent { Content = page });
		var h = new RendererHarness(shell);
		Settle(h);
		return (h, handler, page);
	}

	[Fact]
	public void A_shell_page_with_a_search_handler_shows_the_field_with_its_placeholder()
	{
		var (h, _, _) = ShellWithSearch();
		using var _h = h;

		var op = LastSearch(h)!.Value;
		Assert.True(op.GetProperty("on").GetBoolean());
		Assert.Equal("Find a fruit", op.GetProperty("placeholder").GetString());
		Assert.True(op.GetProperty("enabled").GetBoolean());
	}

	[Fact]
	public void Typing_writes_the_query_back_and_is_not_pushed_back_into_the_field()
	{
		var (h, handler, _) = ShellWithSearch();
		using var _h = h;
		var before = h.Shim.Ops.Count;

		h.Renderer.HandleNativeEvent("search-changed", "{\"page\":\"p1\",\"text\":\"ap\"}");
		Settle(h);

		Assert.Equal("ap", handler.Query);
		// The field already shows "ap" (and maybe a later keystroke): no text in the ops that followed.
		Assert.DoesNotContain(h.Shim.Ops.Skip(before), op =>
			op.GetProperty("op").GetString() == "search" && op.GetProperty("text").ValueKind == JsonValueKind.String);
	}

	[Fact]
	public void The_apps_own_query_change_reaches_the_field()
	{
		var (h, handler, _) = ShellWithSearch();
		using var _h = h;

		handler.Query = "kiwi";
		Settle(h);

		Assert.Equal("kiwi", LastSearch(h)!.Value.GetProperty("text").GetString());
	}

	[Fact]
	public void The_enter_key_runs_the_command_with_its_parameter()
	{
		object? ran = null;
		var handler = new SearchHandler { Command = new Command(p => ran = p), CommandParameter = "go" };
		var (h, _, _) = ShellWithSearch(handler);
		using var _h = h;

		h.Renderer.HandleNativeEvent("search-submit", "{\"page\":\"p1\",\"text\":\"pear\"}");

		Assert.Equal("pear", handler.Query);
		Assert.Equal("go", ran);
	}

	[Fact]
	public void Hidden_visibility_and_IsSearchEnabled_follow_at_runtime()
	{
		var (h, handler, _) = ShellWithSearch();
		using var _h = h;

		handler.IsSearchEnabled = false;
		Settle(h);
		Assert.False(LastSearch(h)!.Value.GetProperty("enabled").GetBoolean());

		handler.SearchBoxVisibility = SearchBoxVisibility.Hidden;
		Settle(h);
		Assert.False(SearchShown(h));
	}

	[Fact]
	public void A_hidden_navigation_bar_hides_the_field_too()
	{
		var (h, _, page) = ShellWithSearch();
		using var _h = h;

		Shell.SetNavBarIsVisible(page, false);
		Settle(h);
		Assert.False(SearchShown(h));
	}

	[Fact]
	public void A_search_handler_outside_a_shell_is_ignored()
	{
		var page = new ContentPage { Title = "Plain", Content = new Label { Text = "plain" } };
		Shell.SetSearchHandler(page, new SearchHandler { Placeholder = "x" });
		using var h = new RendererHarness(new NavigationPage(page));
		Settle(h);

		Assert.False(SearchShown(h));
	}

	private sealed record Fruit(string Name);

	private sealed class PickingHandler : SearchHandler
	{
		public List<object> Picked { get; } = new();
		protected override void OnItemSelected(object item) => Picked.Add(item);
	}

	private static Microsoft.Maui.SailfishOS.Platform.QtHost.NativeElementHost? ResultsList(RendererHarness h) =>
		h.Renderer.CurrentHosts.FirstOrDefault(x => x.IsAttached && x.Element is CollectionView);

	[Fact]
	public void Results_show_as_a_list_over_the_content_and_a_pick_selects_and_closes_them()
	{
#pragma warning disable CS0618   // DisplayMemberName is obsolete in MAUI 11, still honoured for apps that set it
		var handler = new PickingHandler { ShowsResults = true, DisplayMemberName = nameof(Fruit.Name) };
#pragma warning restore CS0618
		var (h, _, _) = ShellWithSearch(handler);
		using var _h = h;
		h.Renderer.HandleNativeEvent("window-geometry",
			"{\"pageWidth\":1080,\"pageHeight\":2160,\"headerHeight\":220,\"titleHeight\":110,\"statusHeight\":40}");
		Assert.Null(ResultsList(h));

		var fruit = new[] { new Fruit("apple"), new Fruit("apricot") };
		h.Renderer.HandleNativeEvent("search-changed", "{\"text\":\"ap\"}");
		handler.ItemsSource = fruit;
		Settle(h);

		var list = ResultsList(h);
		Assert.NotNull(list);
		var content = h.Renderer.CurrentHosts.First(x => x.Element is Label { Text: "fruit" });
		// Over the content: the same top, after it in the walk (painted above).
		Assert.Equal(content.MauiLogicalBounds.Top, list!.MauiLogicalBounds.Top, 1);
		Assert.True(h.Renderer.CurrentHosts.ToList().IndexOf(list) > h.Renderer.CurrentHosts.ToList().IndexOf(content));
		var native = h.Shim.ById(list.Id)!;
		using (var rows = JsonDocument.Parse(native.Text("mauiRowsJson")!))
			Assert.Equal(2, rows.RootElement.GetArrayLength());

		h.Renderer.HandleNativeEvent("list-item-tapped", $"{{\"id\":\"{list.Id}\",\"row\":1,\"cell\":0}}");
		Settle(h);

		Assert.Equal(new object[] { fruit[1] }, handler.Picked);
		Assert.Same(fruit[1], handler.SelectedItem);
		Assert.Null(ResultsList(h));
		Assert.True(LastSearch(h)!.Value.GetProperty("blur").GetBoolean());

		// Typing on brings them back.
		h.Renderer.HandleNativeEvent("search-changed", "{\"text\":\"apr\"}");
		Settle(h);
		Assert.NotNull(ResultsList(h));
	}

	[Fact]
	public void Results_follow_an_observable_source_and_ShowsResults()
	{
		var source = new System.Collections.ObjectModel.ObservableCollection<string>();
		var handler = new SearchHandler { ShowsResults = true, ItemsSource = source };
		var (h, _, _) = ShellWithSearch(handler);
		using var _h = h;
		Assert.Null(ResultsList(h));

		source.Add("kiwi");
		Settle(h);
		Assert.NotNull(ResultsList(h));

		handler.ShowsResults = false;
		Settle(h);
		Assert.Null(ResultsList(h));
	}

	[Fact]
	public void The_enter_key_closes_the_results()
	{
		var handler = new SearchHandler { ShowsResults = true, ItemsSource = new[] { "kiwi" } };
		var (h, _, _) = ShellWithSearch(handler);
		using var _h = h;
		Assert.NotNull(ResultsList(h));

		h.Renderer.HandleNativeEvent("search-submit", "{\"text\":\"ki\"}");
		Settle(h);
		Assert.Null(ResultsList(h));
	}
}
