using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.SailfishOS.Handlers;
using Microsoft.Maui.SailfishOS.Hosting;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>
/// Guards of the architecture alignment with MAUI's handler-driven model (stages A0–A7). The baseline facts hold today; each skipped fact is the target
/// of the stage named in its Skip reason, fails on the current architecture, and is un-skipped by that stage.
/// </summary>
[Collection("renderer")]
public class ArchitectureAlignmentTests
{

	private static ContentPage Page(params View[] children)
	{
		var stack = new VerticalStackLayout();
		foreach (var child in children)
			stack.Children.Add(child);
		return new ContentPage { Title = "Test", Content = stack };
	}

	private static NativeElementHost HostOf(RendererHarness h, Element element) =>
		h.Renderer.CurrentHosts.Single(host => ReferenceEquals(host.Element, element));

	// --- Baseline: the counters attribute work to the right channel ---

	[Fact]
	public void A_mapper_owned_change_travels_as_a_handler_snapshot()
	{
		var label = new Label { Text = "Hello" };
		using var h = new RendererHarness(Page(label));
		var before = h.Renderer.ArchitectureCounters;

		label.Text = "World";

		var after = h.Renderer.ArchitectureCounters;
		Assert.True(after.HandlerSnapshots > before.HandlerSnapshots, $"{before} → {after}");
		Assert.True(after.HandlerPropertyPushes > before.HandlerPropertyPushes, $"{before} → {after}");
		Assert.Equal("World", h.Shim.ByUri("label").Single().Text("text"));
	}

	[Fact]
	public void Idle_timer_polls_do_no_native_work()
	{
		using var h = new RendererHarness(Page(new Label { Text = "Still" }, new Entry { Text = "Quiet" }));
		h.Poll();
		var before = h.Renderer.ArchitectureCounters;

		h.Poll();
		h.Poll();

		var after = h.Renderer.ArchitectureCounters;
		Assert.Equal(before.TimerPolls + 2, after.TimerPolls);
		Assert.Equal(before.TimerPollsWithWork, after.TimerPollsWithWork);
	}

	// --- A1: an app or library can register and extend handlers as on the other platforms ---

	private sealed class TestApp : Application
	{
	}

	/// <summary>A library control with its own Sailfish handler.</summary>
	public sealed class CustomView : View
	{
	}

	public sealed class CustomViewHandler : NullViewHandler
	{
	}

	public sealed class AppLabelHandler : SailfishLabelHandler
	{
	}

	[Fact]
	public void App_handler_registrations_win_over_the_backend_table()
	{
		var builder = MauiApp.CreateBuilder(useDefaults: false);
		builder.UseMauiAppSailfish<TestApp>();
		builder.ConfigureMauiHandlers(handlers =>
		{
			handlers.AddHandler<CustomView, CustomViewHandler>();
			handlers.AddHandler<Label, AppLabelHandler>();
		});
		using var app = builder.Build();
		var factory = app.Services.GetRequiredService<IMauiHandlersFactory>();

		Assert.IsType<CustomViewHandler>(factory.GetHandler(typeof(CustomView)));
		Assert.IsType<AppLabelHandler>(factory.GetHandler(typeof(Label)));
		Assert.IsType<SailfishButtonHandler>(factory.GetHandler(typeof(Button)));   // the rest stays Sailfish
	}

	[Fact]
	public void Stock_registrations_give_way_to_the_sailfish_table_on_plain_UseMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder.UseMauiApp<TestApp>();
		using var app = builder.Build();
		var factory = new Microsoft.Maui.SailfishOS.Platform.SailfishServiceOverlay(app.Services)
			.GetService(typeof(IMauiHandlersFactory)) as IMauiHandlersFactory;
		Assert.IsType<SailfishHandlersFactory>(factory);

		Assert.IsType<SailfishLabelHandler>(factory!.GetHandler(typeof(Label)));
		Assert.IsType<SailfishStackHandler>(factory.GetHandler(typeof(VerticalStackLayout)));
		Assert.IsType<SailfishBorderHandler>(factory.GetHandler(typeof(Frame)));
		Assert.IsType<SailfishPageHandler>(factory.GetHandler(typeof(ContentPage)));
		Assert.IsType<SailfishLayoutHandler>(factory.GetHandler(typeof(FlexLayout)));
		Assert.IsType<SailfishContainerHandler>(factory.GetHandler(typeof(CustomView)));
		Assert.IsType<SailfishApplicationHandler>(factory.GetHandler(typeof(Application)));
		Assert.IsType<SailfishWindowHandler>(factory.GetHandler(typeof(Window)));
		Assert.IsType<NullElementHandler>(factory.GetHandler(typeof(MenuFlyoutItem)));
	}

	/// <summary>The handler's public static mapper, as on every MAUI handler (LabelHandler.Mapper).</summary>
	private static IPropertyMapper<ILabel, SailfishLabelHandler>? LabelMapper()
	{
		const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
		var type = typeof(SailfishLabelHandler);
		var value = type.GetField("Mapper", flags)?.GetValue(null) ?? type.GetProperty("Mapper", flags)?.GetValue(null);
		return value as IPropertyMapper<ILabel, SailfishLabelHandler>;
	}

	[Fact]
	public void Sailfish_handlers_are_typed_on_the_native_host_with_a_chained_public_mapper()
	{
		Assert.IsAssignableFrom<IViewHandler<ILabel, NativeElementHost>>(new SailfishLabelHandler());
		var mapper = LabelMapper();
		Assert.NotNull(mapper);
		Assert.Contains(nameof(ILabel.Text), mapper!.GetKeys());
		Assert.Contains(nameof(IView.Opacity), mapper.GetKeys());   // chained from ViewHandler.ViewMapper
	}

	[Fact]
	public void An_app_mapper_customization_runs_with_the_native_host()
	{
		var mapper = LabelMapper();
		Assert.NotNull(mapper);
		object? seen = null;
		mapper!.AppendToMapping(nameof(ILabel.Text), (handler, view) =>
		{
			if (view is Label { AutomationId: "a1-probe" })
				seen = handler.PlatformView;
		});

		using var h = new RendererHarness(Page(new Label { Text = "Probe", AutomationId = "a1-probe" }));

		Assert.IsType<NativeElementHost>(seen);
	}

	/// <summary>A library control with a registered adapter of its own.</summary>
	public sealed class RatingView : View
	{
		public static readonly BindableProperty ValueProperty =
			BindableProperty.Create(nameof(Value), typeof(int), typeof(RatingView), 0);

		public int Value
		{
			get => (int)GetValue(ValueProperty);
			set => SetValue(ValueProperty, value);
		}
	}

	public sealed class RatingViewHandler : SailfishSnapshotHandler
	{
		public RatingViewHandler() : base(new[] { nameof(RatingView.Value) })
		{
		}

		protected override string? AdapterUri => "test-rating";

		protected override Dictionary<string, object?>? Snapshot(IView view) =>
			view is RatingView rating ? new Dictionary<string, object?> { ["value"] = rating.Value } : null;

		protected override void OnAdapterEvent(string name, System.Text.Json.JsonElement payload)
		{
			if (name == "rating-changed" && VirtualView is RatingView rating)
				rating.Value = payload.GetProperty("value").GetInt32();
		}
	}

	[Fact]
	public void A_library_control_renders_on_its_registered_adapter_and_hears_its_events()
	{
		QtHostAdapters.Register("test-rating", "file:///opt/lib/RatingView.qml");
		var builder = MauiApp.CreateBuilder(useDefaults: false);
		builder.UseMauiAppSailfish<TestApp>();
		builder.ConfigureMauiHandlers(handlers => handlers.AddHandler<RatingView, RatingViewHandler>());
		using var app = builder.Build();
		var rating = new RatingView { Value = 3 };
		using var h = new RendererHarness(Page(rating), app.Services);

		var host = h.Shim.ByUri("test-rating").Single();
		Assert.Equal("3", host.Text("value"));
		Assert.IsType<RatingViewHandler>(rating.Handler);
		Assert.Same(((IElementHandler)rating.Handler!).PlatformView, HostOf(h, rating));

		rating.Value = 4;
		Assert.Equal("4", h.Shim.ByUri("test-rating").Single().Text("value"));

		h.Renderer.HandleNativeEvent("rating-changed", $"{{\"id\":\"{host.Id}\",\"value\":5}}");
		Assert.Equal(5, rating.Value);
	}

	// --- A2: an existing host changes only through its handler, one snapshot per mapper pass ---

	[Fact]
	public void Connecting_a_handler_sends_one_snapshot_per_host()
	{
		var label = new Label { Text = "Once", TextColor = Colors.Red, FontSize = 20 };
		using var h = new RendererHarness(Page(label));

		Assert.Equal(1, HostOf(h, label).HandlerSnapshots);
	}

	[Fact]
	public void Generic_view_properties_change_through_the_mapper_only()
	{
		var label = new Label { Text = "Fade" };
		using var h = new RendererHarness(Page(label));
		var before = h.Renderer.ArchitectureCounters;

		label.Opacity = 0.5;
		label.IsEnabled = false;
		h.Poll();

		var after = h.Renderer.ArchitectureCounters;
		Assert.Equal(before.ReconcileDiffPropertyPushes, after.ReconcileDiffPropertyPushes);
		Assert.Equal("0.5", h.Shim.ByUri("label").Single().Text("opacity"));
	}

	// --- A4: only a measure invalidation relayouts, and it does not wait for the timer ---

	[Fact]
	public void A_color_change_does_not_relayout()
	{
		var label = new Label { Text = "Paint" };
		using var h = new RendererHarness(Page(label));
		var passes = h.Renderer.ArchitectureCounters.LayoutPasses;

		label.TextColor = Colors.Red;
		h.Poll();

		Assert.Equal(passes, h.Renderer.ArchitectureCounters.LayoutPasses);
	}

	[Fact]
	public void A_text_change_is_laid_out_before_the_safety_net_poll()
	{
		var label = new Label { Text = "Short", HorizontalOptions = LayoutOptions.Start };   // sized by its text
		using var h = new RendererHarness(Page(label));
		var width = h.Shim.ByUri("label").Single().Geometry.Width;
		var withWork = h.Renderer.ArchitectureCounters.TimerPollsWithWork;

		label.Text = "A considerably longer text";

		Assert.True(h.Shim.ByUri("label").Single().Geometry.Width > width);   // laid out without any poll
		h.Poll();
		Assert.Equal(withWork, h.Renderer.ArchitectureCounters.TimerPollsWithWork);
	}

	[Fact]
	public void A_relayout_of_unchanged_text_measures_nothing_natively()
	{
		var labels = Enumerable.Range(0, 20).Select(i => new Label { Text = $"row {i} of the measured page" }).ToArray();
		using var h = new RendererHarness(Page(labels));
		var measures = h.Shim.TextMeasures;

		h.Renderer.RequestLayout();   // a layout pass over the same texts at the same widths
		h.Poll();
		Assert.Equal(measures, h.Shim.TextMeasures);

		labels[3].Text = "a new text";   // only the changed text crosses the shim
		Assert.InRange(h.Shim.TextMeasures - measures, 1, 3);
	}

	// --- A3: tree changes arrive through the handlers, not through a window-wide scan ---

	[Fact]
	public void Layout_children_reconcile_their_subtree_and_page_changes_request_the_sync_at_once()
	{
		var stack = new VerticalStackLayout { Children = { new Label { Text = "A" } } };
		var page = new ContentPage { Title = "Tree", Content = stack };
		using var h = new RendererHarness(page);
		var kicks = 0;
		var previous = QtHostPageRenderer.NavigationKick;
		QtHostPageRenderer.NavigationKick = () => { kicks++; h.Renderer.KickedPoll(); };
		try
		{
			Assert.IsAssignableFrom<ILayoutHandler<NativeElementHost>>(stack.Handler);
			var subtrees = h.Renderer.SubtreeReconciles;
			var reconciles = h.Renderer.ReconcileCount;
			stack.Children.Add(new Label { Text = "B" });   // SailfishStackHandler.Add → the stack's subtree only
			Assert.Equal(subtrees + 1, h.Renderer.SubtreeReconciles);
			Assert.Equal(reconciles, h.Renderer.ReconcileCount);   // no page reconcile
			Assert.Equal(0, kicks);
			Assert.Equal(new[] { "A", "B" }, h.Shim.ByUri("label").Select(o => o.Text("text")).OrderBy(t => t));

			page.Title = "Renamed";                             // SailfishPageHandler
			Assert.True(kicks > 0, "the page title did not request a sync");
			Assert.Equal("Renamed", h.Shim.Ops.Last(op => op.GetProperty("op").GetString() == "title").GetProperty("text").GetString());
		}
		finally
		{
			QtHostPageRenderer.NavigationKick = previous;
		}
	}

	[Fact]
	public void A_subtree_pass_removes_reorders_and_nests_like_the_page_reconcile()
	{
		var a = new Label { Text = "A" };
		var b = new Label { Text = "B" };
		var inner = new VerticalStackLayout { Children = { new Label { Text = "C" } } };
		var stack = new VerticalStackLayout { Children = { a, b } };
		using var h = new RendererHarness(Page(stack));
		var fixups = h.Renderer.TreeFixups;

		stack.Children.Remove(a);
		stack.Children.Insert(0, inner);   // a nested container with its own child
		stack.Children.Add(a);             // back, now last
		h.Poll();                          // the verifier: nothing left to fix

		Assert.Equal(fixups, h.Renderer.TreeFixups);
		Assert.Equal(new[] { "B", "C", "A" }.OrderBy(t => t), h.Shim.ByUri("label").Select(o => o.Text("text")).OrderBy(t => t));
		var stackHost = h.Renderer.CurrentHosts.Single(x => ReferenceEquals(x.Element, stack));
		var innerHost = h.Renderer.CurrentHosts.Single(x => ReferenceEquals(x.Element, inner));
		Assert.Equal(new object[] { inner, b, a },
			h.Renderer.CurrentHosts.Where(x => ReferenceEquals(x.Parent, stackHost)).Select(x => x.Element).ToArray());
		Assert.Same(innerHost, h.Renderer.CurrentHosts.Single(x => x.Element is Label { Text: "C" }).Parent);
		// The native child order follows.
		Assert.Equal(new[] { innerHost.Id, HostOf(h, b).Id, HostOf(h, a).Id }, NativeChildren(h, stackHost.Id));

		stack.Children.Clear();
		Assert.Empty(h.Shim.ByUri("label"));
		Assert.DoesNotContain(h.Renderer.CurrentHosts, x => ReferenceEquals(x.Parent, stackHost));
	}

	/// <summary>The QML child order of <paramref name="parentId"/>, replayed from the op batches.</summary>
	private static List<string> NativeChildren(RendererHarness h, string parentId)
	{
		var parents = new Dictionary<string, string>();
		var order = new List<string>();
		static string? Str(System.Text.Json.JsonElement op, string name) =>
			op.TryGetProperty(name, out var v) ? v.GetString() : null;
		foreach (var op in h.Shim.Ops)
		{
			var id = Str(op, "id");
			switch (Str(op, "op"))
			{
				case "create" or "reparent":
					parents[id!] = Str(op, "parent") ?? string.Empty;
					order.Remove(id!);
					order.Add(id!);
					break;
				case "destroy":
					parents.Remove(id!);
					order.Remove(id!);
					break;
				case "order":
					foreach (var child in op.GetProperty("ids").EnumerateArray().Select(e => e.GetString()!))
					{
						order.Remove(child);
						order.Add(child);
					}
					break;
			}
		}
		return order.Where(id => parents.GetValueOrDefault(id) == parentId).ToList();
	}

	[Fact]
	public void A_subtree_holding_a_list_or_scroll_falls_back_to_the_page_reconcile()
	{
		var stack = new VerticalStackLayout { Children = { new Label { Text = "A" } } };
		using var h = new RendererHarness(Page(stack));
		var fallbacks = h.Renderer.SubtreeFallbacks;

		stack.Children.Add(new ScrollView { Content = new Label { Text = "In scroll" } });
		h.Poll();

		Assert.True(h.Renderer.SubtreeFallbacks > fallbacks);
		Assert.Single(h.Shim.ByUri("scroll-view"));
		Assert.Contains(h.Shim.ByUri("label"), o => o.Text("text") == "In scroll");
	}

	[Fact]
	public void With_the_handler_tree_off_the_page_reconcile_applies_tree_changes()
	{
		var stack = new VerticalStackLayout { Children = { new Label { Text = "A" } } };
		using var h = new RendererHarness(Page(stack));
		var previous = QtHostPageRenderer.HandlerTree;
		QtHostPageRenderer.HandlerTree = false;
		try
		{
			var subtrees = h.Renderer.SubtreeReconciles;
			stack.Children.Add(new Label { Text = "B" });
			h.Poll();
			Assert.Equal(subtrees, h.Renderer.SubtreeReconciles);
			Assert.Equal(2, h.Shim.ByUri("label").Count());
		}
		finally
		{
			QtHostPageRenderer.HandlerTree = previous;
		}
	}

	[Fact]
	public void A_disconnected_handler_takes_the_host_of_a_removed_element_with_it()
	{
		var gone = new Label { Text = "Gone" };
		var stack = new VerticalStackLayout { Children = { new Label { Text = "Stays" }, gone } };
		using var h = new RendererHarness(Page(stack));
		var previous = QtHostPageRenderer.HandlerTree;
		QtHostPageRenderer.HandlerTree = false;   // no reconcile sees the removal first
		try
		{
			stack.Children.Remove(gone);
			Assert.Equal(2, h.Shim.ByUri("label").Count());

			gone.Handler!.DisconnectHandler();

			Assert.Equal(1, h.Renderer.HandlerReleases);
			Assert.Equal("Stays", h.Shim.ByUri("label").Single().Text("text"));
			Assert.DoesNotContain(h.Renderer.CurrentHosts, x => ReferenceEquals(x.Element, gone));
		}
		finally
		{
			QtHostPageRenderer.HandlerTree = previous;
		}
	}

	[Fact]
	public void Disconnecting_the_handler_of_a_visible_element_keeps_its_host()
	{
		var label = new Label { Text = "Here" };
		using var h = new RendererHarness(Page(label));

		label.Handler!.DisconnectHandler();

		Assert.Equal(0, h.Renderer.HandlerReleases);
		Assert.Single(h.Shim.ByUri("label"));
	}

	// --- A7: a list follows its ItemsView through the handler's mapper ---

	[Fact]
	public void A_new_ItemsSource_reaches_the_native_list_through_the_list_handler()
	{
		var list = new CollectionView
		{
			ItemsSource = new[] { "a", "b" },
			ItemTemplate = new DataTemplate(() => new Label()),
			HeightRequest = 600,
		};
		using var h = new RendererHarness(Page(list));
		Assert.IsType<SailfishListViewHandler>(list.Handler);
		var native = h.Shim.ByUri("list-view").Single();
		static int Rows(string? json) => System.Text.Json.JsonDocument.Parse(json!).RootElement.GetArrayLength();
		Assert.Equal(2, Rows(native.Text("mauiRowsJson")));

		list.ItemsSource = new[] { "a", "b", "c", "d" };   // SailfishListViewHandler.MapItemsProperty
		h.Poll();

		Assert.Equal(4, Rows(h.Shim.ByUri("list-view").Single().Text("mauiRowsJson")));
	}

	[Fact]
	public void The_list_handler_owns_its_adapter_while_the_list_is_on_the_page()
	{
		var list = new CollectionView { ItemsSource = new[] { "a" }, ItemTemplate = new DataTemplate(() => new Label()), HeightRequest = 300 };
		var page = Page(list);
		using var h = new RendererHarness(page);
		var handler = Assert.IsType<SailfishListViewHandler>(list.Handler);
		Assert.NotNull(handler.Adapter);
		Assert.Same(list, handler.Adapter!.View);

		page.Content = new Label { Text = "no list" };   // the list leaves the page: its adapter retires
		h.Poll();

		Assert.Null(handler.Adapter);
		Assert.Empty(h.Shim.ByUri("list-view"));
	}

	[Fact]
	public void A_new_root_page_gets_its_handler_and_a_tab_switch_wakes_the_renderer()
	{
		using var h = new RendererHarness(Page(new Label { Text = "first root" }));
		var windowHandler = new SailfishWindowHandler();
		windowHandler.SetMauiContext(h.Renderer.MauiContext);
		windowHandler.SetVirtualView(h.Window);
		var kicks = 0;
		var previous = QtHostPageRenderer.NavigationKick;
		QtHostPageRenderer.NavigationKick = () => { kicks++; h.Renderer.KickedPoll(); };
		try
		{
			var tabbed = new TabbedPage { Children = { Page(new Label { Text = "tab A" }), Page(new Label { Text = "tab B" }) } };
			h.Window.Page = tabbed;   // WindowHandler.MapContent: the root gets its handler
			Assert.IsType<SailfishTabbedPageHandler>(tabbed.Handler);
			Assert.Contains(h.Shim.ByUri("label"), o => o.Text("text") == "tab A");

			kicks = 0;
			tabbed.CurrentPage = tabbed.Children[1];   // CurrentPageChanged → the page handler asks for the sync
			Assert.True(kicks > 0, "the tab switch did not wake the renderer");
			Assert.Contains(h.Shim.ByUri("label"), o => o.Text("text") == "tab B");
		}
		finally
		{
			QtHostPageRenderer.NavigationKick = previous;
		}
	}

	// --- A5: the page cache keeps each container's pages (tabs, sections, detail) ---

	[Fact]
	public async Task PopToRoot_over_several_levels_brings_the_root_back_with_live_objects()
	{
		var root = Tab("root body");
		var nav = new NavigationPage(root);
		using var h = new RendererHarness(nav);
		for (var i = 1; i <= 3; i++)
		{
			_ = nav.PushAsync(Tab($"level {i}"));
			for (var k = 0; k < 4; k++)
				h.Poll();
		}
		Assert.Equal(4, h.Renderer.NativePageIds.Count);

		_ = nav.PopToRootAsync(false);
		for (var k = 0; k < 6; k++)
			h.Poll();
		await Task.Yield();

		Assert.Single(h.Renderer.NativePageIds);
		var content = h.Renderer.CurrentHosts.Where(x => !x.Id.StartsWith("synth-", StringComparison.Ordinal)).ToList();
		Assert.NotEmpty(content);
		// Every host's QML object is alive: none points at an object destroyed with an intermediate model page.
		Assert.All(content, x => Assert.True(x.IsAttached && h.Shim.ById(x.Id) is { Destroyed: false }, $"{x} has no live object"));
		Assert.Contains(h.Shim.ByUri("label"), o => o.Text("text") == "root body");
	}

	private static ContentPage Tab(string text) =>
		new() { Title = text, Content = new VerticalStackLayout { Children = { new Label { Text = text } } } };

	private static int Creates(RendererHarness h, string text) =>
		h.Shim.Ops.Count(op => op.GetProperty("op").GetString() == "create" &&
		                       op.TryGetProperty("props", out var p) && p.TryGetProperty("text", out var t) && t.GetString() == text);

	[Fact]
	public void A_tab_switched_away_from_is_kept_hidden_and_comes_back_without_a_rebuild()
	{
		var tabbed = new TabbedPage { Children = { Tab("tab A"), Tab("tab B") } };
		using var h = new RendererHarness(tabbed);
		var a = h.Shim.ByUri("label").Single(o => o.Text("text") == "tab A");
		var aRoot = h.Shim.ByUri("stack-layout").Single();          // tab A's content root

		tabbed.CurrentPage = tabbed.Children[1];
		h.Poll();
		Assert.False(a.Destroyed);                                   // parked, not destroyed
		Assert.Equal("false", aRoot.Text("visible"), ignoreCase: true);   // hidden under tab B (its root, so all of it)
		Assert.Contains(h.Shim.ByUri("label"), o => o.Text("text") == "tab B");
		Assert.Equal(1, h.Renderer.ParkedPages);

		tabbed.CurrentPage = tabbed.Children[0];
		h.Poll();
		Assert.Equal(1, Creates(h, "tab A"));                        // the same QML object came back
		Assert.Same(a, h.Shim.ByUri("label").Single(o => o.Text("text") == "tab A"));
		Assert.True(a.Visible);
		Assert.True(h.Renderer.PageCacheRestores >= 1);
		Assert.Contains(h.Renderer.CurrentHosts, x => x.Element is Label { Text: "tab A" });
	}

	[Fact]
	public void Pages_of_nested_containers_are_kept_per_page()
	{
		// Flyout → Tabbed → a NavigationPage per tab: each tab's page is remembered on its own.
		var tabA = new NavigationPage(Tab("nav A"));
		var tabB = new NavigationPage(Tab("nav B")) { Title = "B" };
		tabA.Title = "A";
		var tabbed = new TabbedPage { Children = { tabA, tabB } };
		var flyout = new FlyoutPage { Flyout = new ContentPage { Title = "Menu" }, Detail = tabbed };
		using var h = new RendererHarness(flyout);
		Assert.IsType<SailfishTabbedPageHandler>(tabbed.Handler);   // a nested container gets its handler on demand
		Assert.Contains(h.Shim.ByUri("label"), o => o.Text("text") == "nav A");

		tabbed.CurrentPage = tabB;
		h.Poll();
		tabbed.CurrentPage = tabA;
		h.Poll();
		tabbed.CurrentPage = tabB;
		h.Poll();

		Assert.Equal(1, Creates(h, "nav A"));
		Assert.Equal(1, Creates(h, "nav B"));
		Assert.Contains(h.Shim.ByUri("label"), o => o.Text("text") == "nav B" && o.Visible);
	}

	[Fact]
	public void Pages_the_app_no_longer_holds_leave_the_cache()
	{
		var tabbed = new TabbedPage { Children = { Tab("old A"), Tab("old B") } };
		var flyout = new FlyoutPage { Flyout = new ContentPage { Title = "Menu" }, Detail = tabbed };
		using var h = new RendererHarness(flyout);
		tabbed.CurrentPage = tabbed.Children[1];   // parks "old A"
		h.Poll();
		Assert.Equal(1, h.Renderer.ParkedPages);

		flyout.Detail = Tab("new detail");          // the old tabs are gone from the app
		h.Poll();

		Assert.Equal(0, h.Renderer.ParkedPages);
		Assert.DoesNotContain(h.Shim.ByUri("label"), o => o.Text("text") is "old A" or "old B");
		Assert.Contains(h.Shim.ByUri("label"), o => o.Text("text") == "new detail");
	}

	[Fact]
	public void The_least_recently_used_page_beyond_the_limit_is_rebuilt()
	{
		var previous = QtHostPageRenderer.PageCacheLimit;
		QtHostPageRenderer.PageCacheLimit = 2;
		try
		{
			var tabbed = new TabbedPage { Children = { Tab("one"), Tab("two"), Tab("three") } };
			using var h = new RendererHarness(tabbed);
			tabbed.CurrentPage = tabbed.Children[1];   // parks "one"
			h.Poll();
			tabbed.CurrentPage = tabbed.Children[2];   // parks "two": one, two
			h.Poll();
			Assert.Equal(2, h.Renderer.ParkedPages);

			tabbed.CurrentPage = tabbed.Children[0];   // parks "three", drops the oldest other ("two"), restores "one"
			h.Poll();
			Assert.True(h.Renderer.ParkedPages <= 2);
			Assert.Equal(1, Creates(h, "one"));          // the page switched to is never the one evicted
			Assert.DoesNotContain(h.Shim.ByUri("label"), o => o.Text("text") == "two");

			tabbed.CurrentPage = tabbed.Children[1];
			h.Poll();
			Assert.Equal(2, Creates(h, "two"));          // rebuilt
			Assert.Equal(1, Creates(h, "three"));        // still cached
		}
		finally
		{
			QtHostPageRenderer.PageCacheLimit = previous;
		}
	}

	// --- A5: PushAsync completes when the page is on screen, as on Android/iOS ---

	[Fact]
	public void PushAsync_completes_only_after_the_native_transition()
	{
		var nav = new NavigationPage(Page(new Label { Text = "Root" }));
		using var h = new RendererHarness(nav);

		h.Shim.StackBusy = true;   // the Silica push animation is running
		var push = nav.PushAsync(Page(new Label { Text = "Second" }));
		for (var i = 0; i < 4; i++)
			h.Poll();
		Assert.False(push.IsCompleted);

		h.Shim.StackBusy = false;
		for (var i = 0; i < 4; i++)
			h.Poll();
		Assert.True(push.IsCompletedSuccessfully);
		Assert.Equal(new[] { "mp1", "mp2" }, h.Shim.Pages);
	}

	// --- A6: focus is decided by the native side, as on Android (a disabled view refuses it) ---

	[Fact]
	public void Focus_on_a_disabled_entry_returns_false()
	{
		var entry = new Entry { Text = "locked", IsEnabled = false };
		using var h = new RendererHarness(Page(entry));

		Assert.False(entry.Focus());
		Assert.False(entry.IsFocused);
	}
}
