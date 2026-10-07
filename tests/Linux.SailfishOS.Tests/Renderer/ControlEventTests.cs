using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.SailfishOS.Handlers;
using Microsoft.Maui.SailfishOS.Platform;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>Tracker S18 (plan M17 steps 8–9): Button/ImageButton Pressed/Released, Slider DragStarted/DragCompleted,
/// SwipeView SwipeStarted/SwipeChanging during a drag, Editor Completed on focus loss, ReturnType.Next and the
/// auto-uppercase hint of a custom keyboard.</summary>
[Collection("renderer")]
public sealed class ControlEventTests
{
	private static RendererHarness Start(View content)
	{
		var h = new RendererHarness(new ContentPage { Title = "Events", Content = content });
		for (var i = 0; i < 4; i++)
			h.Poll();
		return h;
	}

	private static string IdOf(RendererHarness h, Element element) =>
		h.Renderer.CurrentHosts.First(x => ReferenceEquals(x.Element, element)).Id;

	[Fact]
	public void A_button_press_raises_pressed_then_released()
	{
		var events = new List<string>();
		var button = new Button { Text = "Go" };
		button.Pressed += (_, _) => events.Add("pressed");
		button.Released += (_, _) => events.Add("released");
		var image = new ImageButton { Source = "logo.png", HeightRequest = 40, WidthRequest = 40 };
		image.Pressed += (_, _) => events.Add("image pressed");
		image.Released += (_, _) => events.Add("image released");
		using var h = Start(new VerticalStackLayout { Children = { button, image } });

		h.Renderer.HandleNativeEvent("pressed-changed", $"{{\"id\":\"{IdOf(h, button)}\",\"pressed\":true}}");
		h.Renderer.HandleNativeEvent("pressed-changed", $"{{\"id\":\"{IdOf(h, button)}\",\"pressed\":false}}");
		h.Renderer.HandleNativeEvent("pressed-changed", $"{{\"id\":\"{IdOf(h, image)}\",\"pressed\":true}}");
		h.Renderer.HandleNativeEvent("pressed-changed", $"{{\"id\":\"{IdOf(h, image)}\",\"pressed\":false}}");

		Assert.Equal(new[] { "pressed", "released", "image pressed", "image released" }, events);
	}

	[Fact]
	public void A_slider_drag_raises_drag_started_and_completed()
	{
		var events = new List<string>();
		var slider = new Slider();
		slider.DragStarted += (_, _) => events.Add("started");
		slider.DragCompleted += (_, _) => events.Add("completed");
		using var h = Start(slider);

		h.Renderer.HandleNativeEvent("drag-changed", $"{{\"id\":\"{IdOf(h, slider)}\",\"dragging\":true}}");
		h.Renderer.HandleNativeEvent("drag-changed", $"{{\"id\":\"{IdOf(h, slider)}\",\"dragging\":false}}");

		Assert.Equal(new[] { "started", "completed" }, events);
	}

	[Fact]
	public void A_swipe_drag_starts_once_reports_its_offset_and_ends()
	{
		var events = new List<string>();
		var swipe = new SwipeView
		{
			LeftItems = new SwipeItems { new SwipeItem { Text = "Flag" } },
			Content = new Label { Text = "row", HeightRequest = 60 },
		};
		swipe.SwipeStarted += (_, e) => events.Add($"started {e.SwipeDirection}");
		swipe.SwipeChanging += (_, e) => events.Add($"changing {e.SwipeDirection} {e.Offset:F0}");
		swipe.SwipeEnded += (_, e) => events.Add($"ended {e.SwipeDirection} {e.IsOpen}");
		using var h = Start(swipe);
		var id = IdOf(h, swipe);

		h.Renderer.HandleNativeEvent("swipe-changing", FormattableString.Invariant($"{{\"id\":\"{id}\",\"offset\":{QtHostUnits.ToQtUnits(20)}}}"));
		h.Renderer.HandleNativeEvent("swipe-changing", FormattableString.Invariant($"{{\"id\":\"{id}\",\"offset\":{QtHostUnits.ToQtUnits(40)}}}"));
		h.Renderer.HandleNativeEvent("swipe-state", $"{{\"id\":\"{id}\",\"side\":\"\",\"open\":false}}");

		Assert.Equal(new[] { "started Right", "changing Right 20", "changing Right 40", "ended Left False" }, events);
	}

	[Fact]
	public void An_editor_completes_when_it_loses_focus()
	{
		var completed = 0;
		var editor = new Editor { HeightRequest = 100 };
		editor.Completed += (_, _) => completed++;
		using var h = Start(editor);
		var id = IdOf(h, editor);

		h.Renderer.HandleNativeEvent("focus-changed", $"{{\"id\":\"{id}\",\"focused\":true}}");
		Assert.Equal(0, completed);
		h.Renderer.HandleNativeEvent("focus-changed", $"{{\"id\":\"{id}\",\"focused\":false}}");
		Assert.Equal(1, completed);

		// Unfocused from code: completes once too.
		editor.Focus();
		h.Poll();
		Assert.True(editor.IsFocused);
		editor.Unfocus();
		h.Poll();
		Assert.Equal(2, completed);
	}

	[Fact]
	public void Return_on_a_next_entry_focuses_the_following_input()
	{
		var first = new Entry { ReturnType = ReturnType.Next };
		var hidden = new Entry { IsVisible = false };
		var readOnly = new Entry { IsReadOnly = true };
		var second = new Entry();
		using var h = Start(new VerticalStackLayout { Children = { first, new Label { Text = "between" }, hidden, readOnly, second } });

		h.Renderer.HandleNativeEvent("completed", $"{{\"id\":\"{IdOf(h, first)}\"}}");
		h.Poll();

		var focused = h.Shim.ByUri("entry").Where(o => h.Shim.GetProperty(o.Handle, "activeFocus") == "true").Select(o => o.Handle).ToList();
		Assert.Equal(new[] { h.Renderer.CurrentHosts.First(x => ReferenceEquals(x.Element, second)).NativeHandle }, focused);
		Assert.True(second.IsFocused);
	}

	[Theory]
	[InlineData(KeyboardFlags.Suggestions, true)]
	[InlineData(KeyboardFlags.CapitalizeSentence | KeyboardFlags.Suggestions, false)]
	[InlineData(KeyboardFlags.CapitalizeWord, false)]
	[InlineData(KeyboardFlags.CapitalizeNone, true)]
	public void A_custom_keyboard_without_capitalize_turns_auto_uppercase_off(KeyboardFlags flags, bool noAutoUppercase)
	{
		const int ImhNoAutoUppercase = 0x4;
		var hints = AdapterSnapshots.MapInputMethodHints(Keyboard.Create(flags), true);
		Assert.Equal(noAutoUppercase, (hints & ImhNoAutoUppercase) != 0);
	}
}
