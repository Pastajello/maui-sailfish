using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.SailfishOS.Handlers;
using Microsoft.Maui.SailfishOS.Platform;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>Handler-layer defects found in the architecture review (plan step B5).</summary>
[Collection("renderer")]
public class HandlerFixTests
{
	// Entry and Editor own Background in their snapshot keys, which replaced the generic view mapper's action; the
	// snapshot does not carry the shim-applied fill, so a runtime BackgroundColor reached native only with the next
	// full reconcile (the heartbeat).
	[Fact]
	public void A_runtime_entry_background_reaches_native_without_a_reconcile()
	{
		var entry = new Entry { Text = "x" };
		using var h = new RendererHarness(new ContentPage { Content = new VerticalStackLayout { entry } });
		var host = h.Shim.ByUri("entry").Single();

		entry.BackgroundColor = Colors.Red;   // no Poll: only the handler's mapper may carry it

		Assert.Equal("#ffff0000", host.Text("mauiBackgroundFill")?.ToLowerInvariant());
	}

	// W5.5: as LabelHandler(mapper, commandMapper) on the other platforms, an app's subclass can bring a mapper chained
	// from the built-in one; the snapshot keys come from that chain, so its own keys and the built-in ones both push.
	public sealed class ShoutingLabelHandler : SailfishLabelHandler
	{
		public static readonly PropertyMapper<ILabel, ShoutingLabelHandler> ShoutMapper = new(SailfishLabelHandler.Mapper)
		{
			["Shout"] = static (handler, _) => handler.Shouts++,
		};

		public int Shouts;

		public ShoutingLabelHandler() : base(ShoutMapper)
		{
		}
	}

	[Fact]
	public void A_subclass_mapper_chained_from_the_built_in_one_keeps_its_snapshot_keys()
	{
		var label = new Label { Text = "before" };
		var handler = new ShoutingLabelHandler();
		using var h = new RendererHarness(new ContentPage { Content = new VerticalStackLayout { label } });
		var context = label.Handler!.MauiContext!;
		label.Handler = null;
		handler.SetMauiContext(context);
		label.Handler = handler;
		h.Poll();
		h.Poll();

		Assert.True(handler.OwnsProperty(nameof(Label.Text)));
		Assert.True(handler.OwnsProperty(nameof(Label.TextColor)));
		Assert.False(handler.OwnsProperty("Shout"));
		var shouts = handler.Shouts;   // the connect pass mapped it once
		handler.UpdateValue("Shout");
		Assert.Equal(shouts + 1, handler.Shouts);

		label.Text = "after";   // no Poll: the chained mapper's snapshot key pushes
		Assert.Contains(h.Shim.ByUri("label"), o => o.Text("text") == "after");
	}
}

[Collection("renderer")]
public class WebViewHandlerFixTests
{
	// EvaluateJavaScriptAsync on a WebView whose handler disconnects (its page was popped) waited forever.
	[Fact]
	public async Task A_script_in_flight_completes_when_the_handler_disconnects()
	{
		var web = new WebView { Source = new HtmlWebViewSource { Html = "<b>x</b>" }, HeightRequest = 40 };
		using var h = new RendererHarness(new ContentPage { Content = new VerticalStackLayout { web } });

		var pending = web.EvaluateJavaScriptAsync("1 + 1");
		Assert.False(pending.IsCompleted);
		web.Handler!.DisconnectHandler();

		var completed = await Task.WhenAny(pending, Task.Delay(2000));
		Assert.Same(pending, completed);
		Assert.Null(await pending);
	}
}

[Collection("renderer")]
public class RootHandlerFactoryTests
{
	public sealed class AppPageHandler : SailfishPageHandler
	{
	}

	// MauiHandlersCollection is internal; the factory only enumerates descriptors.
	private sealed class TestHandlers : List<ServiceDescriptor>, IMauiHandlersCollection
	{
		public bool TryGetService(Type serviceType, out ServiceDescriptor? descriptor)
		{
			descriptor = FindLast(d => d.ServiceType == serviceType);
			return descriptor is not null;
		}
	}

	// The window's root page used the Sailfish row even when the app registered its own handler for the page type.
	[Fact]
	public void An_app_registration_serves_the_window_root_page()
	{
		var collection = new TestHandlers();
		collection.AddHandler<ContentPage, AppPageHandler>();
		var services = new ServiceCollection()
			.AddSingleton<IMauiHandlersCollection>(collection)
			.AddSingleton<IMauiHandlersFactory>(sp => new SailfishHandlersFactory(sp))
			.BuildServiceProvider();
		var context = new SailfishMauiContext(services);

		var root = new ContentPage();
		SailfishHandlersFactory.AttachRootHandler(root, context);
		Assert.IsType<AppPageHandler>(root.Handler);

		var navigation = new NavigationPage(new ContentPage());
		SailfishHandlersFactory.AttachRootHandler(navigation, context);
		Assert.IsType<SailfishNavigationViewHandler>(navigation.Handler);   // unregistered types keep the Sailfish row
	}
}

[Collection("renderer")]
public class FocusBeforeAttachTests
{
	// Focus() on an entry whose QML object was not there (healed, re-created page) was answered from MAUI alone:
	// IsFocused said true, the re-created object never got the focus.
	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void Focus_before_the_object_exists_is_replayed_when_it_attaches(bool enabled)
	{
		var entry = new Entry { Text = "late", IsEnabled = enabled };
		using var h = new RendererHarness(new ContentPage { Content = new VerticalStackLayout { entry } });
		var host = h.Renderer.CurrentHosts.Single(x => ReferenceEquals(x.Element, entry));
		h.Shim.DestroyObject(host.NativeHandle);   // Silica rebuilt the page under it: the handler stays connected
		Assert.True(h.Renderer.HealIfDead(host));

		entry.Focus();
		h.Poll();

		var native = h.Shim.ByUri("entry").Single(o => !o.Destroyed);
		Assert.Equal(enabled, h.Shim.GetProperty(native.Handle, "activeFocus") == "true");
		Assert.Equal(enabled, entry.IsFocused);
	}
}

[Collection("renderer")]
public class CommandMapperTests
{
	// Focus/Unfocus/InvalidateMeasure lived in Invoke overrides, so AppendToMapping/ModifyMapping on a handler's
	// CommandMapper never saw them; every Sailfish handler now answers them through its mapper.
	[Fact]
	public void Every_sailfish_handler_answers_focus_through_its_command_mapper()
	{
		var missing = new List<string>();
		foreach (var row in SailfishHandlersFactory.ViewHandlers)
		{
			var handler = (IElementHandler)Activator.CreateInstance(row.Handler)!;
			var field = typeof(Microsoft.Maui.Handlers.ElementHandler).GetField("_commandMapper",
				System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
			var mapper = (CommandMapper?)field?.GetValue(handler);
			if (mapper?.GetCommand(nameof(IView.Focus)) is null ||
			    !ReferenceEquals(mapper.GetCommand(nameof(IView.Focus)), SailfishViewMapper.CommandMapper.GetCommand(nameof(IView.Focus))))
				missing.Add(row.Handler.Name);
		}
		Assert.Empty(missing);
	}
}

[Collection("renderer")]
public class AdapterRebindTests
{
	private static List<FakeShim.FakeObject> Live(RendererHarness h, string uri) =>
		h.Shim.ByUri(uri).Where(o => !o.Destroyed).ToList();

	// The first adapter a host was bound to won for good: an Image first shown with a missing file (an empty
	// placeholder) stayed a placeholder after its Source changed to a file that exists.
	[Fact]
	public void An_image_whose_source_resolves_later_gets_the_image_adapter()
	{
		var file = Path.Combine(Path.GetTempPath(), $"maui-sf-rebind-{Guid.NewGuid():N}.png");
		File.WriteAllBytes(file, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
		try
		{
			var image = new Image { Source = "/nonexistent/missing.png", HeightRequest = 40 };
			using var h = new RendererHarness(new ContentPage { Content = new VerticalStackLayout { image } });
			Assert.Empty(Live(h, "image"));

			image.Source = file;
			h.Poll();

			Assert.Single(Live(h, "image"));
			var host = h.Renderer.CurrentHosts.Single(x => ReferenceEquals(x.Element, image));
			Assert.Equal("image", host.QmlUri);
			Assert.Same(host, ((IElementHandler)image.Handler!).PlatformView);   // the handler pushes to the live host
			Assert.DoesNotContain(h.Renderer.CurrentHosts, x => ReferenceEquals(x.Element, image) && x.QmlUri != "image");
		}
		finally
		{
			File.Delete(file);
		}
	}

	[Fact]
	public void An_indicator_view_switches_between_dots_and_its_template()
	{
		var indicator = new IndicatorView { Count = 3, HeightRequest = 20 };
		using var h = new RendererHarness(new ContentPage { Content = new VerticalStackLayout { indicator } });
		Assert.Single(Live(h, "indicator-view"));

		indicator.IndicatorTemplate = new DataTemplate(() => new Label { Text = "o" });
		h.Poll();
		Assert.Empty(Live(h, "indicator-view"));

		indicator.IndicatorTemplate = null;
		h.Poll();
		Assert.Single(Live(h, "indicator-view"));
	}
}


[Collection("renderer")]
public class RowPoolRekeyTests
{
	private static (RendererHarness H, FakeShim.FakeObject Native) List(int count = 20)
	{
		var list = new CollectionView
		{
			ItemsSource = Enumerable.Range(0, count).Select(i => $"item {i}").ToArray(),
			ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label();
				label.SetBinding(Label.TextProperty, ".");
				return new Border { Padding = 8, Content = label };
			}),
			HeightRequest = 600,
		};
		var h = new RendererHarness(new ContentPage { Content = new VerticalStackLayout { list } });
		var native = h.Shim.ByUri("list-view").Single();
		for (var i = 0; i < 4; i++)
			h.Poll();
		return (h, native);
	}

	private static string Attach(RendererHarness h, FakeShim.FakeObject native, int row)
	{
		var dg = $"maui_{native.Id}__r{row}_{Guid.NewGuid():N}";
		h.Shim.AddNative(dg);
		h.Renderer.HandleNativeEvent("list-item-attached", $"{{\"id\":\"{native.Id}\",\"row\":{row},\"dg\":\"{dg}\"}}");
		return dg;
	}

	private static void Detach(RendererHarness h, FakeShim.FakeObject native, string dg) =>
		h.Renderer.HandleNativeEvent("list-item-detached", $"{{\"id\":\"{native.Id}\",\"dg\":\"{dg}\"}}");

	// Rows 1 and 2 scroll out (both pooled), row 1 scrolls back: the pool's top is row 2's subtree, whose rekey onto
	// row 1's ids MauiModelPage refuses (row 1's own pooled subtree still holds them); the hosts were bound anyway, to
	// objects still named after row 2.
	[Fact]
	public void A_row_that_scrolls_back_takes_its_own_pooled_subtree()
	{
		var (h, native) = List();
		using var _ = h;
		var dg1 = Attach(h, native, 1);
		var dg2 = Attach(h, native, 2);
		var label1 = h.Shim.ByUri("label").Single(o => !o.Destroyed && o.Text("text") == "item 1");
		Detach(h, native, dg1);
		Detach(h, native, dg2);

		Attach(h, native, 1);

		Assert.Equal(0, h.Shim.RekeysRefused);
		Assert.False(label1.Destroyed);
		var live = h.Shim.ByUri("label").Where(o => !o.Destroyed && o.Text("text") == "item 1").ToList();
		Assert.Same(label1, Assert.Single(live));   // its own QML label, no second one
		Assert.True(h.Renderer.TryGetHost(label1.Id, out var host));
		Assert.Equal(label1.Handle, host!.NativeHandle);   // the managed host drives that object
	}

	[Fact]
	public void A_refused_rekey_creates_the_row_instead_of_binding_the_old_objects()
	{
		var (h, native) = List();
		using var _ = h;
		var dg1 = Attach(h, native, 1);
		var label1 = h.Shim.ByUri("label").Single(o => !o.Destroyed && o.Text("text") == "item 1");
		Detach(h, native, dg1);
		h.Shim.RefuseRekeys = true;

		Attach(h, native, 15);

		Assert.True(label1.Destroyed);   // the pooled objects went
		var label15 = h.Shim.ByUri("label").Single(o => !o.Destroyed && o.Text("text") == "item 15");
		Assert.True(h.Renderer.TryGetHost(label15.Id, out var host));
		Assert.Equal(label15.Handle, host!.NativeHandle);
		Assert.Equal(1L, h.Renderer.Collection.RowRekeysRefused);
		Assert.Equal(0L, h.Renderer.Collection.RowsAdopted);
	}

	[Fact]
	public void A_dead_pooled_row_is_not_adopted()
	{
		var (h, native) = List();
		using var _ = h;
		var dg1 = Attach(h, native, 1);
		var border1 = h.Shim.ByUri("border").Single(o => !o.Destroyed);
		Detach(h, native, dg1);
		h.Shim.DestroyObject(border1.Handle);   // Silica rebuilt the page under the pool

		Attach(h, native, 15);

		Assert.Equal(0L, h.Renderer.Collection.RowsAdopted);
		Assert.Single(h.Shim.ByUri("label"), o => !o.Destroyed && o.Text("text") == "item 15");
	}

	[Fact]
	public void Without_the_row_pool_a_detached_row_is_destroyed()
	{
		using var statics = new TestStatics();
		Microsoft.Maui.SailfishOS.Platform.QtHost.QtHostListAdapter.RowPoolEnabled = false;
		var (h, native) = List();
		using var _ = h;
		var dg1 = Attach(h, native, 1);
		var label1 = h.Shim.ByUri("label").Single(o => !o.Destroyed && o.Text("text") == "item 1");

		Detach(h, native, dg1);

		Assert.True(label1.Destroyed);
		Assert.Equal(0L, h.Renderer.Collection.RowsPooled);
	}

	[Fact]
	public void Removing_the_list_destroys_its_pooled_rows()
	{
		var (h, native) = List();
		using var _ = h;
		var dg1 = Attach(h, native, 1);
		var label1 = h.Shim.ByUri("label").Single(o => !o.Destroyed && o.Text("text") == "item 1");
		Detach(h, native, dg1);
		var page = (ContentPage)h.Window.Page!;

		((VerticalStackLayout)page.Content).Clear();
		for (var i = 0; i < 3; i++)
			h.Poll();

		Assert.True(label1.Destroyed);
	}
}

[Collection("renderer")]
public class RowRemeasureTests
{
	private sealed class Item : System.ComponentModel.INotifyPropertyChanged
	{
		private string _text = string.Empty;
		public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

		public string Text
		{
			get => _text;
			set
			{
				_text = value;
				PropertyChanged?.Invoke(this, new(nameof(Text)));
			}
		}
	}

	// A bound text that grew inside a row kept the row at its first height until the next rebuild (clipped or
	// overlapping the row below); the row now re-measures alone.
	[Fact]
	public void A_row_that_grows_pushes_a_new_height_without_a_rebuild()
	{
		var items = Enumerable.Range(0, 5).Select(i => new Item { Text = $"item {i}" }).ToArray();
		var list = new CollectionView
		{
			ItemsSource = items,
			ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label { LineBreakMode = LineBreakMode.WordWrap };
				label.SetBinding(Label.TextProperty, nameof(Item.Text));
				return label;
			}),
			HeightRequest = 600,
		};
		using var h = new RendererHarness(new ContentPage { Content = new VerticalStackLayout { list } });
		for (var i = 0; i < 4; i++)
			h.Poll();
		var native = h.Shim.ByUri("list-view").Single();
		var rows = native.Text("mauiRowsJson");
		var built = h.Renderer.Collection.RowsBuilt;

		items[1].Text = string.Join(" ", Enumerable.Repeat("a much longer text", 40));
		for (var i = 0; i < 3; i++)
			h.Poll();

		Assert.NotEqual(rows, native.Text("mauiRowsJson"));
		Assert.Equal(built, h.Renderer.Collection.RowsBuilt);
	}
}

[Collection("renderer")]
public class IncrementalRowsTests
{
	private sealed record Item(string Text);

	private static (RendererHarness H, CollectionView List, System.Collections.ObjectModel.ObservableCollection<Item> Items) Create()
	{
		var items = new System.Collections.ObjectModel.ObservableCollection<Item>(
			Enumerable.Range(0, 30).Select(i => new Item($"item {i}")));
		var list = new CollectionView
		{
			ItemsSource = items,
			ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label();
				label.SetBinding(Label.TextProperty, nameof(Item.Text));
				return label;
			}),
			HeightRequest = 600,
		};
		var h = new RendererHarness(new ContentPage { Content = new VerticalStackLayout { list } });
		for (var i = 0; i < 4; i++)
			h.Poll();
		return (h, list, items);
	}

	private static Microsoft.Maui.SailfishOS.Platform.QtHost.QtHostListAdapter Adapter(CollectionView list) =>
		((Microsoft.Maui.SailfishOS.Handlers.SailfishListViewHandler)list.Handler!).Adapter!;

	// One added item: the other rows (their views, keys and heights) are the same objects; only the new row is built.
	[Fact]
	public void Adding_one_item_builds_one_row_and_keeps_the_others()
	{
		var (h, list, items) = Create();
		using var _ = h;
		var before = Adapter(list).Rows.ToList();

		items.Insert(10, new Item("inserted"));
		for (var i = 0; i < 3; i++)
			h.Poll();

		var after = Adapter(list).Rows;
		Assert.Equal(before.Count + 1, after.Count);
		var fresh = after.Where(r => !before.Contains(r)).ToList();
		Assert.Equal("inserted", ((Item)Assert.Single(fresh).CellItems[0]!).Text);
		Assert.Equal(before.Select(r => r.Key), after.Where(r => before.Contains(r)).Select(r => r.Key));
	}

	[Fact]
	public void Removing_an_item_releases_its_view()
	{
		var (h, list, items) = Create();
		using var _ = h;
		var removed = Adapter(list).Rows[5];
		var view = removed.CellViews[0]!;
		Assert.Same(list, view.Parent);

		items.RemoveAt(5);
		for (var i = 0; i < 3; i++)
			h.Poll();

		Assert.DoesNotContain(removed, Adapter(list).Rows);
		Assert.Null(view.Parent);   // no longer a logical child of the list
	}
}

[Collection("renderer")]
public class CollectionConsistencyTests
{
	private sealed record Item(string Text);

	private static Microsoft.Maui.SailfishOS.Platform.QtHost.QtHostListAdapter Adapter(ItemsView list) =>
		((Microsoft.Maui.SailfishOS.Handlers.SailfishListViewHandler)list.Handler!).Adapter!;

	private static RendererHarness Show(View list)
	{
		var h = new RendererHarness(new ContentPage { Content = new VerticalStackLayout { list } });
		for (var i = 0; i < 4; i++)
			h.Poll();
		return h;
	}

	private static DataTemplate LabelTemplate(string prefix) => new(() =>
	{
		var label = new Label();
		label.SetBinding(Label.TextProperty, new Binding(nameof(Item.Text), stringFormat: prefix + "{0}"));
		return label;
	});

	// The rebuild reused rows by item, so a new ItemTemplate kept every row's old views.
	[Fact]
	public void Changing_the_item_template_rebuilds_every_row()
	{
		var list = new CollectionView
		{
			ItemsSource = Enumerable.Range(0, 5).Select(i => new Item($"item {i}")).ToList(),
			ItemTemplate = LabelTemplate("A:"),
			HeightRequest = 600,
		};
		using var h = Show(list);

		list.ItemTemplate = LabelTemplate("B:");
		for (var i = 0; i < 3; i++)
			h.Poll();

		Assert.All(Adapter(list).Rows, r => Assert.StartsWith("B:", ((Label)r.CellViews[0]!).Text));
	}

	// MAUI matches the selection by Equals (Android's adapter, iOS's source): a record equal to an item selects it.
	[Fact]
	public void Selection_matches_items_by_value_equality()
	{
		var list = new CollectionView
		{
			ItemsSource = Enumerable.Range(0, 5).Select(i => new Item($"item {i}")).ToList(),
			ItemTemplate = LabelTemplate(""),
			SelectionMode = SelectionMode.Single,
			HeightRequest = 600,
		};
		using var h = Show(list);

		var source = (List<Item>)list.ItemsSource;
		list.SelectedItem = source[1];
		for (var i = 0; i < 3; i++)
			h.Poll();
		Console.WriteLine($"PROBE same-instance cells=[{string.Join(",", Adapter(list).SelectedCells)}]");
		list.SelectedItem = new Item("item 2");   // equal, not the same instance
		for (var i = 0; i < 3; i++)
			h.Poll();
		Console.WriteLine($"PROBE equal cells=[{string.Join(",", Adapter(list).SelectedCells)}] selItems={list.SelectedItems?.Count}");

		Assert.Contains((2, 0), Adapter(list).SelectedCells);
	}

	// ScrollTo(index, groupIndex) on a grouped list: the index is within the group, not a flat ordinal.
	[Fact]
	public void ScrollTo_with_a_group_index_lands_in_that_group()
	{
		var groups = Enumerable.Range(0, 3).Select(g =>
			new Group($"g{g}", Enumerable.Range(0, 4).Select(i => new Item($"g{g} item {i}")))).ToList();
		var list = new CollectionView
		{
			ItemsSource = groups,
			IsGrouped = true,
			GroupHeaderTemplate = new DataTemplate(() => new Label { Text = "header" }),
			ItemTemplate = LabelTemplate(""),
			HeightRequest = 600,
		};
		using var h = Show(list);
		var native = h.Shim.ByUri("list-view").Single();

		list.ScrollTo(1, groupIndex: 2, position: ScrollToPosition.Start, animate: false);

		using var command = System.Text.Json.JsonDocument.Parse(h.Shim.Commands.Last(c => c.Id == native.Id).Json);
		Assert.Equal("scrollTo", command.RootElement.GetProperty("name").GetString());
		var row = command.RootElement.GetProperty("row").GetInt32();
		var target = Adapter(list).Rows[row];
		Assert.Equal("g2 item 1", ((Item)target.CellItems[0]!).Text);
	}

	private sealed class Group(string name, IEnumerable<Item> items) : List<Item>(items)
	{
		public string Name { get; } = name;
	}

	// A replaced Header view kept the old view's hosts (the slot was only re-measured).
	[Fact]
	public void Replacing_the_header_materializes_the_new_view()
	{
		var list = new CollectionView
		{
			ItemsSource = Enumerable.Range(0, 5).Select(i => new Item($"item {i}")).ToList(),
			ItemTemplate = LabelTemplate(""),
			Header = new Label { Text = "first header" },
			HeightRequest = 600,
		};
		using var h = Show(list);
		var native = h.Shim.ByUri("list-view").Single();
		h.Shim.AddNative($"maui_{native.Id}__header");   // the ListView's header placeholder
		for (var i = 0; i < 3; i++)
			h.Poll();   // the list's own scheduled pass runs on the harness clock (W2.1)
		Assert.Contains(h.Shim.ByUri("label"), o => !o.Destroyed && o.Text("text") == "first header");

		list.Header = new Label { Text = "second header" };
		for (var i = 0; i < 4; i++)
			h.Poll();   // the list's own scheduled pass runs on the harness clock (W2.1)

		Assert.Contains(h.Shim.ByUri("label"), o => !o.Destroyed && o.Text("text") == "second header");
		Assert.DoesNotContain(h.Shim.ByUri("label"), o => !o.Destroyed && o.Text("text") == "first header");
	}

	// A template that changes a list property while the rows are built (here the footer) asked for a rebuild inside
	// the rebuild; it now runs after it, and the rows and the footer both end up current.
	[Fact]
	public void A_rebuild_triggered_from_inside_a_rebuild_runs_after_it()
	{
		CollectionView? list = null;
		var touched = false;
		list = new CollectionView
		{
			ItemsSource = Enumerable.Range(0, 5).Select(i => new Item($"item {i}")).ToList(),
			ItemTemplate = new DataTemplate(() =>
			{
				if (!touched)
				{
					touched = true;
					list!.Footer = new Label { Text = "footer" };
				}
				var label = new Label();
				label.SetBinding(Label.TextProperty, nameof(Item.Text));
				return label;
			}),
			HeightRequest = 600,
		};
		using var h = Show(list);
		for (var i = 0; i < 3; i++)
			h.Renderer.KickedPoll();

		var adapter = Adapter(list);
		Assert.Equal(5, adapter.Rows.Count);
		Assert.Equal(5, adapter.Rows.Select(r => r.Key).Distinct().Count());
		Assert.False(adapter.RowsDirty);
	}
}

[Collection("renderer")]
public class PageInvokeTests
{
	// Op batches and page calls reach the model page through sailfish_host_invoke: nothing is compiled per call.
	[Fact]
	public void Op_batches_and_page_calls_go_through_invoke_after_a_push()
	{
		var nav = new NavigationPage(new ContentPage { Content = new Label { Text = "root" } });
		using var h = new RendererHarness(nav);
		_ = nav.PushAsync(new ContentPage { Content = new VerticalStackLayout { new Label { Text = "pushed" }, new Button { Text = "b" } } }, false);
		for (var i = 0; i < 6; i++)
			h.Poll();

		Assert.Contains(h.Shim.Invokes, c => c.Method == "applyMauiOps" && c.Page == h.Shim.Pages[^1]);
		Assert.DoesNotContain(h.Shim.Evals, e => e.Contains("applyMauiOps(", StringComparison.Ordinal));
		Assert.Equal(0, h.Renderer.PageCallFallbacks);
		Assert.Contains(h.Shim.ByUri("label"), o => !o.Destroyed && o.Text("text") == "pushed" && o.Page == h.Shim.Pages[^1]);
	}
}

[Collection("renderer")]
public class AdapterCommandTests
{
	// Actions cross as one mauiCommand call each (no property + counter pair re-firing equal values).
	[Fact]
	public async Task A_web_view_script_is_a_command_and_its_result_completes_the_task()
	{
		var web = new WebView { Source = new HtmlWebViewSource { Html = "<b>x</b>" }, HeightRequest = 40 };
		using var h = new RendererHarness(new ContentPage { Content = new VerticalStackLayout { web } });
		var native = h.Shim.ByUri("web-view").Single();

		var pending = web.EvaluateJavaScriptAsync("1 + 1");
		var (_, json) = h.Shim.Commands.Last(c => c.Id == native.Id);
		using var command = System.Text.Json.JsonDocument.Parse(json);
		Assert.Equal("js", command.RootElement.GetProperty("name").GetString());
		Assert.Contains("1 + 1", command.RootElement.GetProperty("script").GetString());   // MAUI wraps it
		var req = command.RootElement.GetProperty("req").GetString();

		h.Renderer.HandleNativeEvent("webview-js", $"{{\"id\":\"{native.Id}\",\"req\":\"{req}\",\"ok\":true,\"result\":\"2\"}}");
		Assert.Equal("2", await pending);
		Assert.DoesNotContain("mauiJsTick", native.Props.Keys);
	}

	[Fact]
	public void Opening_a_swipe_view_is_a_command()
	{
		var swipe = new SwipeView
		{
			LeftItems = new SwipeItems { new SwipeItem { Text = "left" } },
			Content = new Label { Text = "row" },
			HeightRequest = 60,
		};
		using var h = new RendererHarness(new ContentPage { Content = new VerticalStackLayout { swipe } });
		var native = h.Shim.ByUri("swipe-view").Single();

		swipe.Open(OpenSwipeItem.LeftItems, false);

		var (_, json) = h.Shim.Commands.Last(c => c.Id == native.Id);
		Assert.Equal("{\"side\":\"left\",\"name\":\"open\"}", json);
	}
}
