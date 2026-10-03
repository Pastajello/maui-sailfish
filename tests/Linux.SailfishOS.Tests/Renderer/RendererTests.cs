using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.SailfishOS.Platform;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

[CollectionDefinition("renderer", DisableParallelization = true)]
public sealed class RendererCollection
{
}

/// <summary>The real renderer against <see cref="FakeShim"/>, with no Qt or device.</summary>
internal sealed class RendererHarness : IDisposable
{
	public FakeShim Shim { get; } = new();
	public QtHostPageRenderer Renderer { get; }
	public Window Window { get; }

	/// <param name="appServices">An app's services (handler registrations); none = an empty container.</param>
	public RendererHarness(Page page, IServiceProvider? appServices = null)
	{
		_statics = new TestStatics();
		QtHostRuntime.TestShim = Shim;
		_loop = SailfishDispatcherProvider.BindLoopThread();   // this thread plays the Qt loop
		QtHostTextMetrics.Enable();
		QtHostPageRenderer.ActivationSettleMs = 0;   // the polls below run back to back
		var services = new SailfishServiceOverlay(appServices ?? new ServiceCollection().BuildServiceProvider());
		Window = new Window(page);
		var context = new SailfishMauiContext(services);
		Renderer = new QtHostPageRenderer(Window, context);
		// As SailfishMauiApplication.Run: the root page's handler attaches before the first render, so a root
		// NavigationPage navigates through its handler (RequestNavigation) instead of MAUI's handler-less path.
		if (page.Handler is null)
		{
			var rootHandler = (Microsoft.Maui.IViewHandler)Activator.CreateInstance(
				Microsoft.Maui.SailfishOS.Handlers.SailfishHandlersFactory.ResolveViewHandlerType(page.GetType()))!;
			rootHandler.SetMauiContext(context);
			rootHandler.SetVirtualView(page);
		}
		Renderer.HandleNativeEvent("window-geometry",
			"{\"pageWidth\":1080,\"pageHeight\":2160,\"headerHeight\":110,\"statusHeight\":40}");
		Renderer.Render();
		// Host creation is deferred until the window reports Active.
		for (var i = 0; i < 4 && (Renderer.CreationDeferred || Shim.Objects.Count() == 0); i++)
			Renderer.Poll();
		Renderer.Poll();
	}

	/// <summary>One 250 ms poll (native sync + reconcile + pending rows), after the work dispatched to this thread,
	/// as the Qt loop's tick drains the dispatcher.</summary>
	public void Poll()
	{
		_loop.DrainQueue();
		Renderer.Poll();
	}

	private readonly SailfishDispatcher _loop;
	private readonly TestStatics _statics;

	/// <summary>Restores the process-wide state and fails the test when the renderer sent an eval the fake does not
	/// model (<see cref="FakeShim.Strict"/>).</summary>
	public void Dispose()
	{
		_statics.Dispose();
		if (Shim.Strict && Shim.UnhandledEvals.Count > 0)
			throw new Xunit.Sdk.XunitException(
				$"FakeShim: {Shim.UnhandledEvals.Count} unmodelled eval(s); model them in FakeShim or list them in " +
				$"FakeShim.AllowedUnanswered. First: {Shim.UnhandledEvals[0][..Math.Min(200, Shim.UnhandledEvals[0].Length)]}");
	}
}

[Collection("renderer")]
public class RendererTests
{
	private static ContentPage Page(params View[] children)
	{
		var stack = new VerticalStackLayout();
		foreach (var child in children)
			stack.Children.Add(child);
		return new ContentPage { Title = "Test", Content = stack };
	}

	[Fact]
	public void First_render_creates_one_host_per_control_with_its_state()
	{
		var label = new Label { Text = "Hello" };
		var button = new Button { Text = "Go" };
		using var h = new RendererHarness(Page(label, button));

		var labels = h.Shim.ByUri("label").ToList();
		var buttons = h.Shim.ByUri("button").ToList();
		Assert.Single(labels);
		Assert.Single(buttons);
		Assert.Equal("Hello", labels[0].Text("text"));
		Assert.Equal("Go", buttons[0].Text("text"));
		Assert.Contains(h.Shim.Ops, op => op.TryGetProperty("op", out var k) && k.GetString() == "title");
	}

	[Fact]
	public void A_property_change_updates_the_same_native_object_in_place()
	{
		var label = new Label { Text = "Hello" };
		using var h = new RendererHarness(Page(label));
		var host = h.Shim.ByUri("label").Single();
		var creates = h.Shim.Ops.Count(op => op.GetProperty("op").GetString() == "create");

		label.Text = "World";
		h.Poll();

		var after = h.Shim.ByUri("label").Single();
		Assert.Equal(host.Handle, after.Handle);
		Assert.Equal("World", after.Text("text"));
		Assert.Equal(creates, h.Shim.Ops.Count(op => op.GetProperty("op").GetString() == "create"));
	}

	[Fact]
	public void Removing_and_adding_children_destroys_and_creates_exactly_those_hosts()
	{
		var a = new Label { Text = "A" };
		var b = new Label { Text = "B" };
		var page = Page(a, b);
		using var h = new RendererHarness(page);
		var stack = (VerticalStackLayout)page.Content;
		var hostA = h.Shim.ByUri("label").Single(o => o.Text("text") == "A");

		stack.Children.Remove(b);
		h.Poll();
		Assert.Equal(new[] { "A" }, h.Shim.ByUri("label").Select(o => o.Text("text")));
		Assert.Equal(hostA.Handle, h.Shim.ByUri("label").Single().Handle);

		stack.Children.Add(new Label { Text = "C" });
		h.Poll();
		Assert.Equal(new[] { "A", "C" }, h.Shim.ByUri("label").Select(o => o.Text("text")).OrderBy(t => t));
		Assert.Equal(hostA.Handle, h.Shim.ByUri("label").Single(o => o.Text("text") == "A").Handle);
	}

	[Fact]
	public void Layout_pushes_geometry_that_stacks_children_top_to_bottom()
	{
		var first = new Label { Text = "First" };
		var second = new Label { Text = "Second" };
		using var h = new RendererHarness(Page(first, second));

		var g1 = h.Shim.ByUri("label").Single(o => o.Text("text") == "First").Geometry;
		var g2 = h.Shim.ByUri("label").Single(o => o.Text("text") == "Second").Geometry;
		Assert.True(g1.Width > 0 && g1.Height > 0, $"first label geometry {g1}");
		Assert.True(g2.Height > 0, $"second label geometry {g2}");
		// parent-relative in the stack: the second label sits below the first
		Assert.True(g2.Y >= g1.Y + g1.Height - 0.5, $"{g1} then {g2}");
	}

	[Fact]
	public void Native_text_change_writes_back_without_echoing_to_qml()
	{
		var entry = new Entry { Text = "a" };
		using var h = new RendererHarness(Page(entry));
		var id = h.Shim.ByUri("entry").Single().Id;
		var batches = h.Shim.PropertyBatches;
		var delivered = h.Renderer.NativeEventsDelivered;

		h.Renderer.HandleNativeEvent("text-changed", $"{{\"id\":\"{id}\",\"text\":\"typed\"}}");
		h.Poll();

		Assert.Equal("typed", entry.Text);
		Assert.Equal(delivered + 1, h.Renderer.NativeEventsDelivered);
		Assert.Equal(batches, h.Shim.PropertyBatches);   // the native side already holds "typed"

		h.Renderer.HandleNativeEvent("text-changed", $"{{\"id\":\"{id}\",\"text\":\"typed\"}}");
		Assert.Equal(delivered + 1, h.Renderer.NativeEventsDelivered);   // same value again = echo
	}

	[Theory]
	[InlineData("{\"id\":42,\"text\":\"x\"}")]
	[InlineData("{\"text\":\"x\"}")]
	[InlineData("{\"id\":\"nope\",\"text\":\"x\"}")]
	public void Events_for_unknown_or_malformed_ids_are_dropped(string payload)
	{
		var entry = new Entry { Text = "a" };
		using var h = new RendererHarness(Page(entry));
		h.Renderer.HandleNativeEvent("text-changed", payload);
		h.Renderer.HandleNativeEvent("swipe-state", payload);
		Assert.Equal("a", entry.Text);
	}

	[Fact]
	public void A_row_template_root_keeps_its_margin_inside_the_delegate()
	{
		var list = new CollectionView
		{
			ItemsSource = new[] { "a", "b" },
			ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label { Margin = new Thickness(16, 10) };
				label.SetBinding(Label.TextProperty, ".");
				return label;
			}),
			HeightRequest = 600,
		};
		using var h = new RendererHarness(Page(list));
		var native = h.Shim.ByUri("list-view").Single();
		for (var i = 0; i < 4; i++)
			h.Poll();
		var dg = $"maui_{native.Id}__r1";
		h.Shim.AddNative(dg);

		h.Renderer.HandleNativeEvent("list-item-attached", $"{{\"id\":\"{native.Id}\",\"row\":1,\"dg\":\"{dg}\"}}");

		var row = h.Shim.ByUri("label").Single(o => o.Text("text") == "b");
		// Delegate-relative, as MAUI arranged it: at the margin (16,10 dp in scene px), not at 0,0.
		Assert.True(row.Geometry.Y > 0, $"row root at y={row.Geometry.Y}");
		Assert.Equal(1.6, row.Geometry.X / row.Geometry.Y, 3);
	}

	// Qt 5.6 destroys a delegate that scrolls out; the row's subtree waits in the pool and the next row of the same
	// template takes it over (new ids, only the differences pushed) instead of creating every host again.
	[Fact]
	public void A_row_that_scrolls_in_takes_over_a_detached_rows_hosts()
	{
		var items = Enumerable.Range(0, 20).Select(i => $"item {i}").ToArray();
		// Kitchen's cards: a Style shares one RoundRectangle as every Border's StrokeShape, so each row also lists that
		// shape's host; it belongs to no row and must not travel with one.
		var card = new Style(typeof(Border))
		{
			Setters = { new Setter { Property = Border.StrokeShapeProperty, Value = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 } } },
		};
		var list = new CollectionView
		{
			ItemsSource = items,
			ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label();
				label.SetBinding(Label.TextProperty, ".");
				return new Border { Padding = 8, Style = card, Content = label };
			}),
			HeightRequest = 600,
		};
		using var h = new RendererHarness(Page(list));
		var native = h.Shim.ByUri("list-view").Single();
		for (var i = 0; i < 4; i++)
			h.Poll();
		void Attach(int row, string dg)
		{
			h.Shim.AddNative(dg);
			h.Renderer.HandleNativeEvent("list-item-attached", $"{{\"id\":\"{native.Id}\",\"row\":{row},\"dg\":\"{dg}\"}}");
		}

		var dg1 = $"maui_{native.Id}__r1";
		Attach(1, dg1);
		var label1 = h.Shim.ByUri("label").Single(o => !o.Destroyed && o.Text("text") == "item 1");
		var creates = h.Shim.Ops.Count(op => op.GetProperty("op").GetString() == "create");

		h.Renderer.HandleNativeEvent("list-item-detached", $"{{\"id\":\"{native.Id}\",\"dg\":\"{dg1}\"}}");
		Assert.False(label1.Destroyed);   // pooled, not destroyed
		Attach(15, $"maui_{native.Id}__r15");

		Assert.Equal(creates, h.Shim.Ops.Count(op => op.GetProperty("op").GetString() == "create"));
		Assert.Equal(2, h.Shim.Rekeys);   // the border and its label
		Assert.False(label1.Destroyed);
		Assert.Equal("item 15", label1.Text("text"));   // the same QML label, now row 15's
		var bridge = h.Renderer.Collection;
		Assert.Equal((1L, 1L), (bridge.RowsPooled, bridge.RowsAdopted));
	}

	// A grid row holds a subtree per cell: every cell root joins the new delegate (only the first did, and the second
	// column of every pooled row stayed off screen).
	[Fact]
	public void A_pooled_grid_row_puts_every_cell_into_the_new_delegate()
	{
		var list = new CollectionView
		{
			ItemsSource = Enumerable.Range(0, 20).Select(i => $"item {i}").ToArray(),
			ItemsLayout = new GridItemsLayout(2, ItemsLayoutOrientation.Vertical),
			ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label();
				label.SetBinding(Label.TextProperty, ".");
				return new Border { Padding = 8, Content = label };
			}),
			HeightRequest = 600,
		};
		using var h = new RendererHarness(Page(list));
		var native = h.Shim.ByUri("list-view").Single();
		for (var i = 0; i < 4; i++)
			h.Poll();
		var dg0 = $"maui_{native.Id}__r0";
		h.Shim.AddNative(dg0);
		h.Renderer.HandleNativeEvent("list-item-attached", $"{{\"id\":\"{native.Id}\",\"row\":0,\"dg\":\"{dg0}\"}}");
		h.Renderer.HandleNativeEvent("list-item-detached", $"{{\"id\":\"{native.Id}\",\"dg\":\"{dg0}\"}}");
		var dg5 = h.Shim.AddNative($"maui_{native.Id}__r5");
		h.Renderer.HandleNativeEvent("list-item-attached", $"{{\"id\":\"{native.Id}\",\"row\":5,\"dg\":\"{dg5.Id}\"}}");

		Assert.Equal(1L, h.Renderer.Collection.RowsAdopted);
		var borders = h.Shim.ByUri("border").Where(o => !o.Destroyed).ToList();
		Assert.Equal(2, borders.Count);   // the two cells, reused
		Assert.All(borders, b => Assert.Equal(dg5.Handle, b.ParentHandle));
		Assert.Equal(new[] { "item 10", "item 11" }, h.Shim.ByUri("label").Where(o => !o.Destroyed).Select(o => o.Text("text")).OrderBy(t => t));
	}

	[Fact]
	public void Grid_tap_selects_the_touched_cell_not_the_row()
	{
		var items = new[] { "a", "b", "c", "d", "e" };
		var grid = new CollectionView
		{
			ItemsSource = items,
			SelectionMode = SelectionMode.Single,
			ItemsLayout = new GridItemsLayout(3, ItemsLayoutOrientation.Vertical),
			ItemTemplate = new DataTemplate(() => new Label()),
			HeightRequest = 600,
		};
		using var h = new RendererHarness(Page(grid));
		var list = h.Shim.ByUri("list-view").Single();
		for (var i = 0; i < 4; i++)
			h.Poll();

		h.Renderer.HandleNativeEvent("list-item-tapped", $"{{\"id\":\"{list.Id}\",\"row\":0,\"cell\":2}}");
		Assert.Equal("c", grid.SelectedItem);
		Assert.Equal("0:2", list.Text("mauiSelectedRows"));

		h.Renderer.HandleNativeEvent("list-item-tapped", $"{{\"id\":\"{list.Id}\",\"row\":1,\"cell\":2}}");
		Assert.Equal("c", grid.SelectedItem);   // row 1 has only d, e: an empty cell selects nothing

		h.Renderer.HandleNativeEvent("list-item-tapped", $"{{\"id\":\"{list.Id}\",\"row\":1,\"cell\":1}}");
		Assert.Equal("e", grid.SelectedItem);
		Assert.Equal("1:1", list.Text("mauiSelectedRows"));
	}

	[Fact]
	public void Appending_to_a_grid_keeps_the_full_rows()
	{
		var items = new System.Collections.ObjectModel.ObservableCollection<string> { "a", "b", "c", "d", "e" };
		var grid = new CollectionView
		{
			ItemsSource = items,
			ItemsLayout = new GridItemsLayout(2, ItemsLayoutOrientation.Vertical),
			ItemTemplate = new DataTemplate(() => new Label()),
			HeightRequest = 600,
		};
		using var h = new RendererHarness(Page(grid));
		var list = h.Shim.ByUri("list-view").Single();
		for (var i = 0; i < 4; i++)
			h.Poll();
		static long[] Keys(string? json) =>
			System.Text.Json.JsonDocument.Parse(json!).RootElement.EnumerateArray().Select(r => r.GetProperty("k").GetInt64()).ToArray();
		var before = Keys(list.Text("mauiRowsJson"));   // [a,b] [c,d] [e]

		items.Add("f");
		items.Add("g");
		h.Poll();
		var after = Keys(list.Text("mauiRowsJson"));    // [a,b] [c,d] [e,f] [g]

		Assert.Equal(3, before.Length);
		Assert.Equal(4, after.Length);
		Assert.Equal(before[..2], after[..2]);           // unchanged full rows keep their key (and QML delegate)
		Assert.NotEqual(before[2], after[2]);            // the grown partial row is rebuilt
	}

	[Fact]
	public void An_idle_poll_pushes_nothing()
	{
		using var h = new RendererHarness(Page(new Label { Text = "Still" }, new Button { Text = "Quiet" }));
		h.Poll();
		var batches = h.Shim.PropertyBatches;
		var geometry = h.Shim.GeometryBatches;
		var ops = h.Shim.Ops.Count;

		h.Poll();
		h.Poll();

		Assert.Equal(batches, h.Shim.PropertyBatches);
		Assert.Equal(geometry, h.Shim.GeometryBatches);
		Assert.Equal(ops, h.Shim.Ops.Count);
	}
}

[Collection("renderer")]
public class RendererFeatureTests
{
	private static ContentPage Page(params View[] children)
	{
		var stack = new VerticalStackLayout();
		foreach (var child in children)
			stack.Children.Add(child);
		return new ContentPage { Title = "Test", Content = stack };
	}

	[Fact]
	public void Navigation_push_renders_the_new_page_on_a_new_model_page()
	{
		var nav = new NavigationPage(Page(new Label { Text = "Root" }));
		using var h = new RendererHarness(nav);
		Assert.Equal("mp1", h.Shim.ByUri("label").Single().Page);

		_ = nav.PushAsync(Page(new Label { Text = "Second" }));
		for (var i = 0; i < 4; i++)
			h.Poll();

		Assert.Equal(new[] { "mp1", "mp2" }, h.Shim.Pages);
		var second = h.Shim.ByUri("label").Single(o => o.Text("text") == "Second");
		Assert.Equal("mp2", second.Page);
	}

	[Fact]
	public void Pop_destroys_the_popped_page_and_restores_the_parked_root()
	{
		var nav = new NavigationPage(Page(new Label { Text = "Root" }));
		using var h = new RendererHarness(nav);
		var root = h.Shim.ByUri("label").Single();

		_ = nav.PushAsync(Page(new Label { Text = "Second" }));
		for (var i = 0; i < 4; i++)
			h.Poll();
		var second = h.Shim.ByUri("label").Single(o => o.Text("text") == "Second");
		Assert.False(root.Destroyed);   // parked in the back cache, not torn down

		_ = nav.PopAsync();
		for (var i = 0; i < 4; i++)
			h.Poll();

		Assert.Equal(new[] { "mp1" }, h.Shim.Pages);
		Assert.True(second.Destroyed);
		Assert.Same(root, h.Shim.ByUri("label").Single());   // same QML object, restored
	}

	[Fact]
	public void Hardware_back_with_a_native_pop_on_the_same_key_pops_MAUI_once()
	{
		var nav = new NavigationPage(Page(new Label { Text = "Root" }));
		using var h = new RendererHarness(nav);
		_ = nav.PushAsync(Page(new Label { Text = "Second" }));
		for (var i = 0; i < 4; i++)
			h.Poll();
		_ = nav.PushAsync(Page(new Label { Text = "Third" }));
		for (var i = 0; i < 4; i++)
			h.Poll();
		Assert.Equal(3, nav.Navigation.NavigationStack.Count);
		Assert.Equal(3, h.Shim.Pages.Count);

		Assert.True(h.Renderer.TryPop());      // hardware Back → MAUI PopAsync
		h.Shim.Pages.RemoveAt(h.Shim.Pages.Count - 1);   // Silica pops on the same key
		for (var i = 0; i < 6; i++)
			h.Poll();

		Assert.Equal(2, nav.Navigation.NavigationStack.Count);   // not 1: the native pop was MAUI's own
		Assert.Equal(2, h.Shim.Pages.Count);
	}

	[Fact]
	public void IsVisible_false_hides_the_host_through_geometry_not_destruction()
	{
		var label = new Label { Text = "Peekaboo" };
		using var h = new RendererHarness(Page(label));
		var host = h.Shim.ByUri("label").Single();
		Assert.True(host.Visible);

		label.IsVisible = false;
		h.Poll();

		Assert.False(h.Shim.ByUri("label").Single().Visible);
		Assert.Equal(host.Handle, h.Shim.ByUri("label").Single().Handle);
	}

	[Fact]
	public void Generic_background_and_semantics_ride_the_create_props()
	{
		var sw = new Switch { BackgroundColor = Microsoft.Maui.Graphics.Colors.Red };
		var save = new Button { Text = "Save", AutomationId = "save" };
		SemanticProperties.SetDescription(save, "Save file");
		using var h = new RendererHarness(Page(sw, save));

		Assert.Equal("#FFFF0000", h.Shim.ByUri("switch").Single().Text("mauiBackgroundFill"));
		var button = h.Shim.ByUri("button").Single();
		Assert.Equal("Save file", button.Text("mauiAccessibleName"));
		Assert.Equal("save", button.Text("mauiAutomationId"));
	}

	[Fact]
	public void Text_inputs_are_tall_enough_for_the_editor_line_plus_the_field_margins()
	{
		var entry = new Entry { Text = "typed" };
		using var h = new RendererHarness(Page(entry));
		// Headless estimate: 18 dp margins plus a 1.2 em line at the 25 dp Silica fallback size.
		Assert.True(entry.Height >= 25 * 1.2 + 18 - 0.5, $"Entry height {entry.Height}");
	}

	[Fact]
	public void An_untitled_shell_page_shows_its_shell_content_title()
	{
		// the MAUI template: <ShellContent Title="Home" ContentTemplate="{DataTemplate local:MainPage}" />
		var page = Page(new Label { Text = "Hello" });
		page.Title = string.Empty;
		var shell = new Shell();
		shell.Items.Add(new ShellContent { Title = "Home", Content = page });
		using var h = new RendererHarness(shell);
		var title = h.Shim.Ops.Last(op => op.GetProperty("op").GetString() == "title").GetProperty("text").GetString();
		Assert.Equal("Home", title);
	}

	[Fact]
	public void Label_measure_rounds_up_so_the_qt_text_never_wraps_early()
	{
		var label = new Label { Text = "Loop A", FontSize = 33 };
		using var h = new RendererHarness(Page(label));
		// 6 chars × 16.5 = 99 dp; a fractional advance must never truncate below the text width.
		Assert.True(label.Width >= 99 - 0.01, $"Label width {label.Width}");
	}
}
