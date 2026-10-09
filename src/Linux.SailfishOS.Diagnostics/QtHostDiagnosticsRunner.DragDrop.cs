using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Input leg, drag &amp; drop (tracker S39): a real press held on a Label with a DragGestureRecognizer starts a drag, the
/// finger carries it over a Label with a DropGestureRecognizer and the release drops it: MAUI's default transfer copies
/// the text, and the events arrive in MAUI's order (DragStarting, DragOver, Drop, DropCompleted).
/// </summary>
internal sealed partial class QtHostDiagnosticsRunner
{
	private void RunQtDragDropCheck(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, Action done)
	{
		if (renderer.CurrentPage?.Navigation is not { } navigation)
		{
			_qtInputChecks.Check("drag and drop: a page to push from", false);
			done();
			return;
		}
		var events = new List<string>();
		var source = new Label { Text = "Drag me", FontSize = 28, Padding = 24, BackgroundColor = Colors.SteelBlue, HorizontalOptions = LayoutOptions.Start };
		var target = new Label { Text = "Drop here", FontSize = 28, Padding = 24, HeightRequest = 220, BackgroundColor = Colors.DarkSlateGray };
		var drag = new DragGestureRecognizer();
		drag.DragStarting += (_, _) => events.Add("DragStarting");
		drag.DropCompleted += (_, _) => events.Add("DropCompleted");
		source.GestureRecognizers.Add(drag);
		var drop = new DropGestureRecognizer();
		drop.DragOver += (_, _) => { if (events.LastOrDefault() != "DragOver") events.Add("DragOver"); };
		drop.Drop += (_, _) => events.Add("Drop");
		target.GestureRecognizers.Add(drop);
		var page = new ContentPage
		{
			Title = "Drag and drop",
			Content = new VerticalStackLayout { Padding = 24, Spacing = 160, Children = { source, target } },
		};
		Console.Error.WriteLine("[Sailfish] Qt input diag: drag and drop — pushing the 'Drag and drop' page");
		_ = navigation.PushAsync(page, false);

		bool Center(VisualElement element, out double x, out double y)
		{
			x = y = double.NaN;
			if (renderer.CurrentHosts.FirstOrDefault(h => h.IsAttached && ReferenceEquals(h.Element, element)) is not { } host ||
			    !QtHost.QtHostRuntime.TryItemGeometry(host.NativeHandle, out var scene))
				return false;
			x = scene.X + scene.Width / 2;
			y = scene.Y + scene.Height / 2;
			return true;
		}

		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1800), () =>
		{
			if (!Center(source, out var x0, out var y0) || !Center(target, out var x1, out var y1))
			{
				_qtInputChecks.Check("drag and drop S39: source and target hosts found", false);
				_ = navigation.PopAsync(false);
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200), done);
				return;
			}
			QtHost.QtHostRuntime.InjectPointer(0, x0, y0);
			// Held still past the drag start (Android's long press), then carried to the target in 16 ms steps.
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(QtHost.QtHostInputRouter.DragStartMs + 250), () =>
			{
				const int steps = 20;
				var i = 0;
				void Step()
				{
					i++;
					QtHost.QtHostRuntime.InjectPointer(2, x0 + (x1 - x0) * i / steps, y0 + (y1 - y0) * i / steps);
					if (i < steps)
					{
						dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(16), Step);
						return;
					}
					// The ghost over the accepting target, finger still down.
					var ghost = DiagQml.EvalNum("(function(){var p=pageStack.currentPage;for(var i=0;i<p.children.length;i++){var c=p.children[i];" +
						"if(c.sourceItem!==undefined&&c.grabX!==undefined)return c.visible?c.opacity:-1;}return -2;})()");
					Shot(dispatcher, "input-dnd-dragging", () =>
					{
						QtHost.QtHostRuntime.InjectPointer(1, x1, y1);
						dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900), () =>
						{
							var order = string.Join(",", events);
							_qtInputChecks.Check($"drag and drop S39: a held Label dragged onto a drop target gives it its text " +
								$"('{target.Text}'=='Drag me'; events {order}; ghost opacity mid-drag {ghost:F2}; " +
								$"router drags {QtHost.QtHostInputRouter.Active?.DragsStarted} drops {QtHost.QtHostInputRouter.Active?.Drops})",
								target.Text == "Drag me" && order == "DragStarting,DragOver,Drop,DropCompleted" && ghost > 0.6);
							Shot(dispatcher, "input-dnd-dropped", () =>
							{
								var popped = navigation.PopAsync(false);
								WaitFor(dispatcher, () => popped.IsCompleted, 4000, () => RunQtRowDragCheck(renderer, dispatcher, navigation, done));
							});
						});
					});
				}
				Step();
			});
		});
	}

	/// <summary>Tracker S40: a CollectionView row whose template has a DragGestureRecognizer, held and carried onto a
	/// target below the list: the target gets the row's text and the row is not selected by the release.</summary>
	private void RunQtRowDragCheck(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, INavigation navigation, Action done)
	{
		var target = new Label { Text = "Drop a row here", FontSize = 28, Padding = 24, HeightRequest = 220, BackgroundColor = Colors.DarkSlateGray };
		target.GestureRecognizers.Add(new DropGestureRecognizer());
		var list = new CollectionView
		{
			HeightRequest = 420,
			SelectionMode = SelectionMode.Single,
			ItemsSource = new[] { "Row one", "Row two", "Row three", "Row four" },
			ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label { FontSize = 28, Padding = new Microsoft.Maui.Thickness(24, 20) };
				label.SetBinding(Label.TextProperty, ".");
				label.GestureRecognizers.Add(new DragGestureRecognizer());
				return label;
			}),
		};
		var page = new ContentPage { Title = "Row drag", Content = new VerticalStackLayout { Padding = 24, Spacing = 80, Children = { list, target } } };
		Console.Error.WriteLine("[Sailfish] Qt input diag: drag and drop — pushing the 'Row drag' page");
		_ = navigation.PushAsync(page, false);
		dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(2200), () =>
		{
			var listHost = renderer.CurrentHosts.FirstOrDefault(h => h.IsAttached && ReferenceEquals(h.Element, list));
			var targetHost = renderer.CurrentHosts.FirstOrDefault(h => h.IsAttached && ReferenceEquals(h.Element, target));
			var row = listHost is null ? "" : QtHost.QtHostRuntime.Eval(
				$"(function(){{var p=pageStack.currentPage;var d=p.__findByName(p,'maui_{listHost.Id}__r1',0);if(!d)return '';" +
				"var c=d.mapToItem(null,d.width/2,d.height/2);return c.x+','+c.y;})()");
			if (!DiagQml.TryPoint(row, out var x0, out var y0) || targetHost is null ||
			    !QtHost.QtHostRuntime.TryItemGeometry(targetHost.NativeHandle, out var t))
			{
				_qtInputChecks.Check($"row drag S40: row and target found (row '{row}')", false);
				_ = navigation.PopAsync(false);
				dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200), done);
				return;
			}
			var (x1, y1) = (t.X + t.Width / 2, t.Y + t.Height / 2);
			QtHost.QtHostRuntime.InjectPointer(0, x0, y0);
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(QtHost.QtHostInputRouter.DragStartMs + 250), () =>
			{
				const int steps = 20;
				var i = 0;
				void Step()
				{
					i++;
					QtHost.QtHostRuntime.InjectPointer(2, x0 + (x1 - x0) * i / steps, y0 + (y1 - y0) * i / steps);
					if (i < steps)
					{
						dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(16), Step);
						return;
					}
					var ghost = DiagQml.EvalNum("(function(){var p=pageStack.currentPage;for(var i=0;i<p.children.length;i++){var c=p.children[i];" +
						"if(c.sourceItem!==undefined&&c.grabX!==undefined)return c.visible?c.opacity:-1;}return -2;})()");
					Shot(dispatcher, "input-dnd-row-dragging", () =>
					{
						QtHost.QtHostRuntime.InjectPointer(1, x1, y1);
						dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900), () =>
						{
							_qtInputChecks.Check($"row drag S40: a CollectionView row dragged onto a target gives it its text ('{target.Text}'=='Row two'; " +
								$"selected '{list.SelectedItem}' (none); ghost opacity mid-drag {ghost:F2})",
								target.Text == "Row two" && list.SelectedItem is null && ghost > 0.6);
							Shot(dispatcher, "input-dnd-row-dropped", () =>
							{
								_ = navigation.PopAsync(false);
								dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1200), done);
							});
						});
					});
				}
				Step();
			});
		});
	}
}
