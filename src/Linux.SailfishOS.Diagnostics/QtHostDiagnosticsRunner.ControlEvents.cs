using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.SailfishOS.Platform;

/// <summary>
/// Input leg, control events (tracker S18): on the "Router" page, injected touches and keys check Button
/// Pressed/Released, Slider DragStarted/DragCompleted, SwipeView SwipeStarted/SwipeChanging/SwipeEnded during a finger
/// drag, Editor Completed on focus loss, and Return on a ReturnType.Next Entry focusing the next Entry.
/// </summary>
internal sealed partial class QtHostDiagnosticsRunner
{
	private const int QtKeyReturn = 0x01000004;

	private void RunQtControlEventChecks(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, ContentPage page, Action done)
	{
		var events = new List<string>();
		var button = new Button { Text = "Events button" };
		button.Pressed += (_, _) => events.Add("pressed");
		button.Released += (_, _) => events.Add("released");
		button.Clicked += (_, _) => events.Add("clicked");
		var slider = new Slider { Minimum = 0, Maximum = 1 };
		slider.DragStarted += (_, _) => events.Add("drag-started");
		slider.DragCompleted += (_, _) => events.Add("drag-completed");
		var swipeChanging = 0;
		var swipe = new SwipeView
		{
			LeftItems = new SwipeItems { new SwipeItem { Text = "Flag", BackgroundColor = Colors.DarkOrange } },
			Content = new Label { Text = "swipe me right", HeightRequest = 70, Padding = new Thickness(16, 0), VerticalTextAlignment = TextAlignment.Center },
		};
		swipe.SwipeStarted += (_, _) => events.Add("swipe-started");
		swipe.SwipeChanging += (_, _) => swipeChanging++;
		swipe.SwipeEnded += (_, _) => events.Add("swipe-ended");
		var editor = new Editor { HeightRequest = 90, Placeholder = "editor" };
		editor.Completed += (_, _) => events.Add("editor-completed");
		var first = new Entry { Placeholder = "first (Next)", ReturnType = ReturnType.Next };
		var second = new Entry { Placeholder = "second" };
		page.Content = new VerticalStackLayout
		{
			Padding = new Thickness(16),
			Spacing = 16,
			Children = { button, slider, swipe, editor, first, second },
		};
		Console.Error.WriteLine("[Sailfish] Qt input diag: control events — Button, Slider, SwipeView, Editor, Entry (Next) on the 'Router' page");

		QtHost.NativeElementHost? HostOf(VisualElement element) =>
			renderer.CurrentHosts.FirstOrDefault(h => ReferenceEquals(h.Element, element) && h.IsAttached);
		static double Px(double dp) => QtHost.QtHostUnits.ToQtUnits(dp);
		void After(int ms, Action next) => dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(ms), next);

		After(1800, () =>
		{
			renderer.Render();
			if (HostOf(button) is not { } bh || HostOf(slider) is not { } sh || HostOf(swipe) is not { } wh || HostOf(editor) is not { } eh)
			{
				_qtInputChecks.Check("control events: hosts attached on the 'Router' page", false);
				done();
				return;
			}
			// Button: press, hold 200 ms, release.
			var bc = bh.MauiLogicalBounds.Center;
			QtHost.QtHostRuntime.InjectPointer(0, Px(bc.X), Px(bc.Y));
			After(200, () =>
			{
				QtHost.QtHostRuntime.InjectPointer(1, Px(bc.X), Px(bc.Y));
				After(500, () =>
				{
					_qtInputChecks.Check($"S18 Button: [{string.Join(",", events)}] == [pressed,released,clicked]",
						events.SequenceEqual(new[] { "pressed", "released", "clicked" }));
					events.Clear();
					// Slider: drag the handle from 30 % to 70 % of the groove, a move every 40 ms.
					var sb = sh.MauiLogicalBounds;
					var y = Px(sb.Center.Y);
					var from = sb.X + sb.Width * 0.3;
					var to = sb.X + sb.Width * 0.7;
					QtHost.QtHostRuntime.InjectPointer(0, Px(from), y);
					for (var i = 1; i <= 8; i++)
					{
						var step = i;
						After(40 * step, () => QtHost.QtHostRuntime.InjectPointer(2, Px(from + (to - from) * step / 8), y));
					}
					After(40 * 9, () => QtHost.QtHostRuntime.InjectPointer(1, Px(to), y));
					After(40 * 9 + 500, () =>
					{
						_qtInputChecks.Check($"S18 Slider: [{string.Join(",", events)}] == [drag-started,drag-completed] (value {slider.Value:F2})",
							events.SequenceEqual(new[] { "drag-started", "drag-completed" }));
						events.Clear();
						SwipeAndEdit(renderer, dispatcher, events, () => swipeChanging, wh, eh, editor, first, second, done);
					});
				});
			});
		});
	}

	private void SwipeAndEdit(QtHost.QtHostPageRenderer renderer, SailfishDispatcher dispatcher, List<string> events, Func<int> swipeChanging,
	                          QtHost.NativeElementHost swipeHost, QtHost.NativeElementHost editorHost, Editor editor, Entry first, Entry second,
	                          Action done)
	{
		static double Px(double dp) => QtHost.QtHostUnits.ToQtUnits(dp);
		void After(int ms, Action next) => dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(ms), next);

		// SwipeView: drag the content 160 dp to the right (reveals the left item), a move every 40 ms.
		var wb = swipeHost.MauiLogicalBounds;
		var y = Px(wb.Center.Y);
		var from = wb.X + 60;
		QtHost.QtHostRuntime.InjectPointer(0, Px(from), y);
		for (var i = 1; i <= 8; i++)
		{
			var step = i;
			After(40 * step, () => QtHost.QtHostRuntime.InjectPointer(2, Px(from + 20 * step), y));
		}
		After(40 * 9, () => QtHost.QtHostRuntime.InjectPointer(1, Px(from + 160), y));
		After(40 * 9 + 800, () =>
		{
			_qtInputChecks.Check($"S18 SwipeView drag: [{string.Join(",", events)}] starts with swipe-started and ends with swipe-ended, SwipeChanging x{swipeChanging()} >= 2",
				events.FirstOrDefault() == "swipe-started" && events.LastOrDefault() == "swipe-ended" && swipeChanging() >= 2);
			events.Clear();
			// Editor: a tap focuses it natively; Unfocus from code must complete it.
			var ec = editorHost.MauiLogicalBounds.Center;
			QtHost.QtHostRuntime.InjectPointer(0, Px(ec.X), Px(ec.Y));
			QtHost.QtHostRuntime.InjectPointer(1, Px(ec.X), Px(ec.Y));
			After(800, () =>
			{
				var focusedFirst = editor.IsFocused;
				editor.Unfocus();
				After(400, () =>
				{
					_qtInputChecks.Check($"S18 Editor: focused by a tap ({focusedFirst}), Completed on Unfocus [{string.Join(",", events)}] == [editor-completed]",
						focusedFirst && events.SequenceEqual(new[] { "editor-completed" }));
					// Entry ReturnType.Next: focus the first entry, press Return (a real key event to the focused item).
					first.Focus();
					After(600, () =>
					{
						QtHost.QtHostRuntime.InjectKey(0, QtKeyReturn, 0, "\r");
						QtHost.QtHostRuntime.InjectKey(1, QtKeyReturn, 0, "\r");
						After(800, () =>
						{
							var secondHost = renderer.CurrentHosts.FirstOrDefault(h => ReferenceEquals(h.Element, second) && h.IsAttached);
							var nativeFocus = secondHost is null ? "?" : QtHost.QtHostRuntime.GetProperty(secondHost.NativeHandle, "activeFocus");
							_qtInputChecks.Check($"S18 Entry Next: Return on the first entry focused the second (IsFocused {second.IsFocused}, native activeFocus {nativeFocus})",
								second.IsFocused && nativeFocus == "true");
							second.Unfocus();
							After(300, done);
						});
					});
				});
			});
		});
	}
}
