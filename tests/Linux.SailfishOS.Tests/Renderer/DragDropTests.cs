using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.SailfishOS.Platform;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>Tracker S39 (plan M28): a press held still on a DragGestureRecognizer view starts a drag (Android's long
/// press), the drop target under the finger gets DragOver/DragLeave, and the release awaits Drop (MAUI's default copies
/// the text) before DropCompleted; moving before the hold, a cancel and a release elsewhere drop nothing.</summary>
[Collection("renderer")]
public sealed class DragDropTests
{
	private sealed class Scene
	{
		public required RendererHarness H { get; init; }
		public required QtHostInputRouter Router { get; init; }
		public required SailfishDispatcher Loop { get; init; }
		public required Label Source { get; init; }
		public required Label Target { get; init; }
		public required Label Elsewhere { get; init; }
		public List<string> Events { get; } = new();

		public Point Center(View view) => H.Renderer.CurrentHosts.First(x => ReferenceEquals(x.Element, view)).MauiLogicalBounds.Center;

		public void Press(Point at) => Router.OnPointer(0, Px(at.X), Px(at.Y), 0, 0);

		public void Hold() => Wait(QtHostInputRouter.DragStartMs + 50);

		public void Wait(long ms)
		{
			SailfishRuntime.TickDueTimers(DateTime.UtcNow + TimeSpan.FromMilliseconds(ms));
			Loop.DrainQueue();
		}

		public void MoveTo(Point from, Point to)
		{
			for (var i = 1; i <= 6; i++)
				Router.OnPointer(2, Px(from.X + (to.X - from.X) * i / 6), Px(from.Y + (to.Y - from.Y) * i / 6), 0, 0);
		}

		public void Release(Point at)
		{
			Router.OnPointer(1, Px(at.X), Px(at.Y), 0, 0);
			Loop.DrainQueue();
		}

		/// <summary>The ghost calls' JSON (the page call carries it as a quoted JS string).</summary>
		public List<string> Ghosts() => H.Shim.PageCalls.Where(c => c.Method == "mauiDragGhost")
			.Select(c => c.Arg.Trim('"').Replace("\\\"", "\"")).ToList();
	}

	private static double Px(double dp) => QtHostUnits.ToQtUnits(dp);

	private static Scene Start(Action<DragGestureRecognizer>? drag = null, Action<DropGestureRecognizer>? drop = null)
	{
		var source = new Label { Text = "hello", HeightRequest = 120, Padding = 20 };
		var target = new Label { Text = "", HeightRequest = 120, BackgroundColor = Colors.DarkSlateGray };
		var elsewhere = new Label { Text = "elsewhere", HeightRequest = 120 };
		var dragRecognizer = new DragGestureRecognizer();
		var dropRecognizer = new DropGestureRecognizer();
		source.GestureRecognizers.Add(dragRecognizer);
		target.GestureRecognizers.Add(dropRecognizer);
		var h = new RendererHarness(new ContentPage
		{
			Title = "DnD",
			Content = new VerticalStackLayout { Spacing = 40, Children = { source, target, elsewhere } },
		});
		for (var i = 0; i < 4; i++)
			h.Poll();
		var loop = SailfishDispatcherProvider.BindLoopThread();
		var scene = new Scene
		{
			H = h, Router = new QtHostInputRouter(h.Renderer, loop), Loop = loop,
			Source = source, Target = target, Elsewhere = elsewhere,
		};
		dragRecognizer.DragStarting += (_, e) => scene.Events.Add("DragStarting");
		dragRecognizer.DropCompleted += (_, _) => scene.Events.Add("DropCompleted");
		dropRecognizer.DragOver += (_, e) => { if (scene.Events.LastOrDefault() != "DragOver") scene.Events.Add("DragOver"); };
		dropRecognizer.DragLeave += (_, _) => scene.Events.Add("DragLeave");
		dropRecognizer.Drop += (_, e) => scene.Events.Add($"Drop@{e.GetPosition(target)?.Y > 0}");
		drag?.Invoke(dragRecognizer);
		drop?.Invoke(dropRecognizer);
		return scene;
	}

	[Fact]
	public void Dragging_a_label_onto_a_target_transfers_its_text_in_maui_order()
	{
		var s = Start();
		using var _ = s.H;
		var from = s.Center(s.Source);
		var to = s.Center(s.Target);

		s.Press(from);
		s.Hold();
		s.MoveTo(from, to);
		s.Release(to);
		s.Wait(10);   // the awaited drop finishes

		Assert.Equal(new[] { "DragStarting", "DragOver", "Drop@True", "DropCompleted" }, s.Events);
		Assert.Equal("hello", s.Target.Text);
		Assert.Equal(1, s.Router.DragsStarted);
		Assert.Equal(1, s.Router.Drops);
		var ghosts = s.Ghosts();
		Assert.Contains("\"id\":", ghosts[0]);             // the first show names the source host
		Assert.Contains("\"allowed\":true", ghosts[^2]);   // over the accepting target
		Assert.Equal("{\"show\":false}", ghosts[^1]);
		Assert.Equal(new[] { "true", "false" },
			s.H.Shim.PageCalls.Where(c => c.Method == "mauiHoldDrag").Select(c => c.Arg.Trim('"')));
		Assert.False(s.Router.DragActive);
	}

	[Fact]
	public void Moving_before_the_hold_is_no_drag()
	{
		var s = Start();
		using var _ = s.H;
		var from = s.Center(s.Source);
		var to = s.Center(s.Target);

		s.Press(from);
		s.MoveTo(from, to);   // a scroll or a pan
		s.Hold();
		s.Release(to);

		Assert.Empty(s.Events);
		Assert.Equal(0, s.Router.DragsStarted);
		Assert.Empty(s.Ghosts());
	}

	[Fact]
	public void Leaving_the_target_and_releasing_elsewhere_completes_without_a_drop()
	{
		var s = Start();
		using var _ = s.H;
		var from = s.Center(s.Source);
		var over = s.Center(s.Target);
		var away = s.Center(s.Elsewhere);

		s.Press(from);
		s.Hold();
		s.MoveTo(from, over);
		s.MoveTo(over, away);
		s.Release(away);
		s.Wait(10);

		Assert.Equal(new[] { "DragStarting", "DragOver", "DragLeave", "DropCompleted" }, s.Events);
		Assert.Equal("", s.Target.Text);
		Assert.Equal(0, s.Router.Drops);
	}

	[Fact]
	public void A_target_that_accepts_nothing_gets_no_drop()
	{
		var s = Start(drop: d => d.DragOver += (_, e) => e.AcceptedOperation = DataPackageOperation.None);
		using var _ = s.H;
		var from = s.Center(s.Source);
		var to = s.Center(s.Target);

		s.Press(from);
		s.Hold();
		s.MoveTo(from, to);
		s.Release(to);
		s.Wait(10);

		Assert.DoesNotContain(s.Events, e => e.StartsWith("Drop@", StringComparison.Ordinal));
		Assert.Equal("DropCompleted", s.Events[^1]);
		Assert.Equal("", s.Target.Text);
		Assert.Contains(s.Ghosts(), g => g.Contains("\"allowed\":false"));
	}

	[Fact]
	public void A_canceled_drag_starting_leaves_the_press_alone()
	{
		var s = Start(drag: d => d.DragStarting += (_, e) => e.Cancel = true);
		using var _ = s.H;
		var from = s.Center(s.Source);
		var to = s.Center(s.Target);

		s.Press(from);
		s.Hold();
		s.MoveTo(from, to);
		s.Release(to);
		s.Wait(10);

		Assert.Equal(new[] { "DragStarting" }, s.Events);   // no DragOver, Drop or DropCompleted
		Assert.Empty(s.Ghosts());
		Assert.Equal(0, s.Router.DragsStarted);
	}

	[Fact]
	public void A_second_finger_ends_the_drag_without_a_drop()
	{
		var s = Start();
		using var _ = s.H;
		var from = s.Center(s.Source);
		var to = s.Center(s.Target);

		s.Press(from);
		s.Hold();
		s.MoveTo(from, to);
		s.Router.OnPointer(7, Px(to.X + 50), Px(to.Y), 0, 2);   // a second finger
		s.Loop.DrainQueue();
		s.Release(to);
		s.Wait(10);

		Assert.Equal(new[] { "DragStarting", "DragOver", "DragLeave", "DropCompleted" }, s.Events);
		Assert.Equal("", s.Target.Text);
	}

	// Tracker S40: a DragGestureRecognizer in a CollectionView row's template. The ListView consumes the press and the
	// delegate reports it (list-item-pressed); the drag holds the row (no flick) and the release is no row tap.
	[Fact]
	public void A_row_of_a_collection_view_can_be_dragged_onto_a_target()
	{
		var target = new Label { Text = "", HeightRequest = 120, BackgroundColor = Colors.DarkSlateGray };
		target.GestureRecognizers.Add(new DropGestureRecognizer());
		var list = new CollectionView
		{
			HeightRequest = 300,
			SelectionMode = SelectionMode.Single,
			ItemsSource = new[] { "one", "two", "three" },
			ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label { HeightRequest = 80 };
				label.SetBinding(Label.TextProperty, ".");
				label.GestureRecognizers.Add(new DragGestureRecognizer());
				return label;
			}),
		};
		var h = new RendererHarness(new ContentPage { Title = "Rows", Content = new VerticalStackLayout { Spacing = 40, Children = { list, target } } });
		using var _h = h;
		for (var i = 0; i < 4; i++)
			h.Poll();
		var loop = SailfishDispatcherProvider.BindLoopThread();
		var router = new QtHostInputRouter(h.Renderer, loop);
		router.Attach();
		h.Disposing += router.Detach;
		var native = h.Shim.ByUri("list-view").Single();
		var area = h.Renderer.CurrentHosts.First(x => ReferenceEquals(x.Element, list)).MauiLogicalBounds;
		var press = new Point(area.X + 100, area.Y + 120);   // row 1 (80 dp rows)
		var to = h.Renderer.CurrentHosts.First(x => ReferenceEquals(x.Element, target)).MauiLogicalBounds.Center;
		var payload = FormattableString.Invariant($"{{\"id\":\"{native.Id}\",\"row\":1,\"cell\":0,\"x\":{Px(100)},\"y\":{Px(40)}}}");

		router.OnPointer(0, Px(press.X), Px(press.Y), 0, 0);
		h.Renderer.HandleNativeEvent("list-item-pressed", payload);
		SailfishRuntime.TickDueTimers(DateTime.UtcNow + TimeSpan.FromMilliseconds(QtHostInputRouter.DragStartMs + 50));
		loop.DrainQueue();
		Assert.True(router.DragActive);
		Assert.Equal("1", native.Text("mauiHoldRow"));   // the ListView does not flick under the drag
		Assert.Equal("1", native.Text("mauiDragRow"));   // and the dragged row drops its press highlight
		for (var i = 1; i <= 6; i++)
			router.OnPointer(2, Px(press.X + (to.X - press.X) * i / 6), Px(press.Y + (to.Y - press.Y) * i / 6), 0, 0);
		router.OnPointer(1, Px(to.X), Px(to.Y), 0, 0);
		h.Renderer.HandleNativeEvent("list-item-tapped", payload);   // the delegate's click on release
		SailfishRuntime.TickDueTimers(DateTime.UtcNow + TimeSpan.FromMilliseconds(10));
		loop.DrainQueue();

		Assert.Equal("two", target.Text);
		Assert.Null(list.SelectedItem);                     // the drag's release is no row tap
		Assert.Equal("-1", native.Text("mauiHoldRow"));
		Assert.Equal("-1", native.Text("mauiDragRow"));
		var first = h.Shim.PageCalls.First(c => c.Method == "mauiDragGhost").Arg.Trim('"').Replace("\\\"", "\"");
		Assert.Contains($"\"name\":\"maui_{native.Id}__r1\"", first);
	}
}
