using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.SailfishOS.Platform;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>Tracker S17 (plan M17 steps 4, 5, 7): a QML-consumed control's own Tap/LongPress recognizers, LongPress
/// AllowableMovement and Canceled, and Pan/LongPress recognizers inside CollectionView rows.</summary>
[Collection("renderer")]
public sealed class RouterRowTests
{
	private static ContentPage Page(View content) => new() { Title = "T", Content = content };

	private static (RendererHarness H, QtHostInputRouter Router, SailfishDispatcher Loop) Start(ContentPage page)
	{
		var h = new RendererHarness(page);
		for (var i = 0; i < 4; i++)
			h.Poll();
		var loop = SailfishDispatcherProvider.BindLoopThread();
		var router = new QtHostInputRouter(h.Renderer, loop);
		router.Attach();   // QtHostInputRouter.Active: the list adapter hands row presses to it
		h.Disposing += router.Detach;
		return (h, router, loop);
	}

	private static double Px(double dp) => QtHostUnits.ToQtUnits(dp);

	private static Rect BoundsOf(RendererHarness h, Element element) =>
		h.Renderer.CurrentHosts.First(x => ReferenceEquals(x.Element, element)).MauiLogicalBounds;

	[Fact]
	public void A_buttons_own_tap_recognizer_fires_and_an_ancestors_does_not()
	{
		var own = 0;
		var ancestor = 0;
		var button = new Button { Text = "Go", HeightRequest = 60 };
		var ownTap = new TapGestureRecognizer();
		ownTap.Tapped += (_, _) => own++;
		button.GestureRecognizers.Add(ownTap);
		var row = new VerticalStackLayout { Children = { button } };
		var rowTap = new TapGestureRecognizer();
		rowTap.Tapped += (_, _) => ancestor++;
		row.GestureRecognizers.Add(rowTap);
		var (h, router, loop) = Start(Page(row));
		using var _ = h;
		var at = BoundsOf(h, button).Center;

		router.OnPointer(0, Px(at.X), Px(at.Y), 0, 0);
		router.OnPointer(1, Px(at.X), Px(at.Y), 0, 0);
		loop.DrainQueue();

		Assert.Equal((1, 0), (own, ancestor));
		Assert.Equal(1, router.OwnGestureCaptures);
	}

	[Fact]
	public void An_early_release_cancels_a_long_press_and_allowable_movement_is_honoured()
	{
		var states = new List<GestureStatus>();
		var fired = 0;
		var box = new BoxView { HeightRequest = 200, WidthRequest = 200, Color = Colors.Blue };
		var longPress = new LongPressGestureRecognizer { AllowableMovement = 30 };
		longPress.LongPressing += (_, e) => states.Add(e.Status);
		longPress.LongPressed += (_, _) => fired++;
		box.GestureRecognizers.Add(longPress);
		var (h, router, loop) = Start(Page(box));
		using var _ = h;
		var at = BoundsOf(h, box).Center;

		// Released at once: Canceled, no LongPressed.
		router.OnPointer(0, Px(at.X), Px(at.Y), 0, 0);
		router.OnPointer(1, Px(at.X), Px(at.Y), 0, 0);
		loop.DrainQueue();
		Assert.Equal(new[] { GestureStatus.Canceled }, states);
		Assert.Equal(0, fired);

		// Moved 20 dp (inside AllowableMovement 30) and held: it fires.
		states.Clear();
		router.OnPointer(0, Px(at.X), Px(at.Y), 0, 0);
		router.OnPointer(2, Px(at.X + 20), Px(at.Y), 0, 0);
		for (var i = 0; i < 3; i++)
			h.Poll();   // 750 ms of test clock
		router.OnPointer(1, Px(at.X + 20), Px(at.Y), 0, 0);
		loop.DrainQueue();
		Assert.Equal(1, fired);
		Assert.Equal(new[] { GestureStatus.Started, GestureStatus.Completed }, states);

		// Moved 40 dp (past it): Canceled at the move.
		states.Clear();
		router.OnPointer(0, Px(at.X), Px(at.Y), 0, 0);
		router.OnPointer(2, Px(at.X + 40), Px(at.Y), 0, 0);
		for (var i = 0; i < 3; i++)
			h.Poll();
		router.OnPointer(1, Px(at.X + 40), Px(at.Y), 0, 0);
		loop.DrainQueue();
		Assert.Equal(1, fired);
		Assert.Equal(new[] { GestureStatus.Canceled }, states);
	}

	private static CollectionView RowList(Action<Label> addRecognizers, SelectionMode selection = SelectionMode.None) => new()
	{
		HeightRequest = 400,
		SelectionMode = selection,
		ItemsSource = new[] { "one", "two", "three" },
		ItemTemplate = new DataTemplate(() =>
		{
			var label = new Label { HeightRequest = 80 };
			label.SetBinding(Label.TextProperty, ".");
			addRecognizers(label);
			return label;
		}),
	};

	[Fact]
	public void A_pan_inside_a_row_is_captured_and_holds_the_list()
	{
		var panned = new List<(GestureStatus, string?)>();
		var list = RowList(label =>
		{
			var pan = new PanGestureRecognizer();
			pan.PanUpdated += (s, e) => panned.Add((e.StatusType, (string?)((BindableObject)s!).BindingContext));
			label.GestureRecognizers.Add(pan);
		});
		var (h, router, loop) = Start(Page(list));
		using var _ = h;
		var native = h.Shim.ByUri("list-view").Single();
		Assert.Contains("\"g\":1", native.Text("mauiRowsJson"));
		var area = BoundsOf(h, list);
		var press = new Point(area.X + 100, area.Y + 120);   // row 1 (80 dp rows)

		router.OnPointer(0, Px(press.X), Px(press.Y), 0, 0);   // the list consumes; the delegate reports the press
		h.Renderer.HandleNativeEvent("list-item-pressed", FormattableString.Invariant(
			$"{{\"id\":\"{native.Id}\",\"row\":1,\"cell\":0,\"x\":{Px(100)},\"y\":{Px(40)}}}"));
		Assert.Equal(1, router.RowCaptures);
		Assert.Equal("1", native.Text("mauiHoldRow"));

		for (var i = 1; i <= 5; i++)
			router.OnPointer(2, Px(press.X + 20 * i), Px(press.Y), 0, 0);
		router.OnPointer(1, Px(press.X + 100), Px(press.Y), 0, 0);
		loop.DrainQueue();

		Assert.Contains((GestureStatus.Started, "two"), panned);
		Assert.Contains((GestureStatus.Completed, "two"), panned);
		Assert.Equal("-1", native.Text("mauiHoldRow"));
	}

	[Fact]
	public void A_long_press_inside_a_row_fires_and_its_release_does_not_select()
	{
		var pressed = new List<string?>();
		var list = RowList(label =>
		{
			var longPress = new LongPressGestureRecognizer();
			longPress.LongPressed += (s, _) => pressed.Add((string?)((BindableObject)s!).BindingContext);
			label.GestureRecognizers.Add(longPress);
		}, SelectionMode.Single);
		var (h, router, loop) = Start(Page(list));
		using var _ = h;
		var native = h.Shim.ByUri("list-view").Single();
		var area = BoundsOf(h, list);
		var press = new Point(area.X + 100, area.Y + 40);   // row 0
		var payload = FormattableString.Invariant($"{{\"id\":\"{native.Id}\",\"row\":0,\"cell\":0,\"x\":{Px(100)},\"y\":{Px(40)}}}");

		router.OnPointer(0, Px(press.X), Px(press.Y), 0, 0);
		h.Renderer.HandleNativeEvent("list-item-pressed", payload);
		for (var i = 0; i < 3; i++)
			h.Poll();
		router.OnPointer(1, Px(press.X), Px(press.Y), 0, 0);
		h.Renderer.HandleNativeEvent("list-item-tapped", payload);   // the delegate's click on release
		loop.DrainQueue();

		Assert.Equal(new[] { "one" }, pressed);
		Assert.Null(list.SelectedItem);
		Assert.NotEqual("0", native.Text("mauiHoldRow"));   // no pan: the row was never held

		// A short tap on the same row still selects.
		router.OnPointer(0, Px(press.X), Px(press.Y), 0, 0);
		h.Renderer.HandleNativeEvent("list-item-pressed", payload);
		router.OnPointer(1, Px(press.X), Px(press.Y), 0, 0);
		h.Renderer.HandleNativeEvent("list-item-tapped", payload);
		loop.DrainQueue();
		Assert.Equal("one", list.SelectedItem);
	}

	// On the phone the delegate's press report can reach managed after the whole fast drag: a capture then armed a
	// long press with no finger down, which fired half a second later.
	[Fact]
	public void A_row_press_reported_after_the_release_captures_nothing()
	{
		var held = 0;
		var list = RowList(label =>
		{
			var longPress = new LongPressGestureRecognizer();
			longPress.LongPressed += (_, _) => held++;
			label.GestureRecognizers.Add(longPress);
		});
		var (h, router, loop) = Start(Page(list));
		using var _ = h;
		var native = h.Shim.ByUri("list-view").Single();
		var at = BoundsOf(h, list).Center;

		router.OnPointer(0, Px(at.X), Px(at.Y), 0, 0);
		router.OnPointer(1, Px(at.X), Px(at.Y), 0, 0);
		h.Renderer.HandleNativeEvent("list-item-pressed", FormattableString.Invariant(
			$"{{\"id\":\"{native.Id}\",\"row\":0,\"cell\":0,\"x\":{Px(100)},\"y\":{Px(40)}}}"));
		for (var i = 0; i < 3; i++)
			h.Poll();
		loop.DrainQueue();

		Assert.Equal(0, router.RowCaptures);
		Assert.Equal(0, held);
	}

	[Fact]
	public void A_pan_inside_a_row_does_not_select_it()
	{
		var list = RowList(label => label.GestureRecognizers.Add(new PanGestureRecognizer()), SelectionMode.Single);
		var (h, router, loop) = Start(Page(list));
		using var _ = h;
		var native = h.Shim.ByUri("list-view").Single();
		var area = BoundsOf(h, list);
		var press = new Point(area.X + 100, area.Y + 40);
		var payload = FormattableString.Invariant($"{{\"id\":\"{native.Id}\",\"row\":0,\"cell\":0,\"x\":{Px(100)},\"y\":{Px(40)}}}");

		router.OnPointer(0, Px(press.X), Px(press.Y), 0, 0);
		h.Renderer.HandleNativeEvent("list-item-pressed", payload);
		for (var i = 1; i <= 5; i++)
			router.OnPointer(2, Px(press.X + 20 * i), Px(press.Y), 0, 0);
		router.OnPointer(1, Px(press.X + 100), Px(press.Y), 0, 0);
		h.Renderer.HandleNativeEvent("list-item-tapped", payload);   // MouseArea clicks on release after a sideways drag
		loop.DrainQueue();

		Assert.Null(list.SelectedItem);
	}
}
