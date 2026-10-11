using Trailhead.Features.Shared;

namespace Trailhead.Features.Lab;

/// <summary>
/// One box per recognizer, each saying what it last saw: pan (the box follows the finger), pinch (it scales), swipe
/// (the direction), long press, double tap, and a drag from one zone to the other (DragGestureRecognizer and
/// DropGestureRecognizer: a long press lifts the token).
/// </summary>
public sealed class GesturesLabPage : ContentPage
{
	public const string Route = "lab/gestures";

	public GesturesLabPage()
	{
		Title = "Gestures";
		var stack = new VerticalStackLayout { Padding = new Thickness(16, 8), Spacing = 12 };

		var panBox = Box("Pan me");
		var pan = new PanGestureRecognizer();
		double startX = 0, startY = 0;
		pan.PanUpdated += (_, e) =>
		{
			if (e.StatusType == GestureStatus.Started)
				(startX, startY) = (panBox.TranslationX, panBox.TranslationY);
			else if (e.StatusType == GestureStatus.Running)
				(panBox.TranslationX, panBox.TranslationY) = (startX + e.TotalX, startY + e.TotalY);
			else if (e.StatusType == GestureStatus.Completed)
				_ = panBox.TranslateToAsync(0, 0, 400, Easing.SpringOut);
		};
		panBox.GestureRecognizers.Add(pan);
		stack.Add(AnimationsLabPage.Section("Pan (springs back)"));
		stack.Add(panBox);

		var pinchBox = Box("Pinch me");
		var pinch = new PinchGestureRecognizer();
		pinch.PinchUpdated += (_, e) =>
		{
			if (e.Status == GestureStatus.Running)
				pinchBox.Scale = Math.Clamp(pinchBox.Scale * e.Scale, 0.5, 3);
		};
		pinchBox.GestureRecognizers.Add(pinch);
		stack.Add(AnimationsLabPage.Section("Pinch"));
		stack.Add(pinchBox);

		var swipeBox = Box("Swipe up or down here");
		foreach (var direction in new[] { SwipeDirection.Up, SwipeDirection.Down })
		{
			var swipe = new SwipeGestureRecognizer { Direction = direction };
			swipe.Swiped += (_, e) => swipeBox.Text = $"Swiped {e.Direction}";
			swipeBox.GestureRecognizers.Add(swipe);
		}
		stack.Add(AnimationsLabPage.Section("Swipe (sideways belongs to Silica's back gesture)"));
		stack.Add(swipeBox);

		var holdBox = Box("Hold or double tap");
		var hold = new LongPressGestureRecognizer();
		hold.LongPressed += (_, _) => holdBox.Text = "Long press";
		holdBox.GestureRecognizers.Add(hold);
		var doubleTap = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
		doubleTap.Tapped += (_, _) => holdBox.Text = "Double tap";
		holdBox.GestureRecognizers.Add(doubleTap);
		stack.Add(AnimationsLabPage.Section("Long press, double tap"));
		stack.Add(holdBox);

		var token = Box("Drag me");
		token.WidthRequest = 120;
		token.GestureRecognizers.Add(new DragGestureRecognizer());
		var target = Box("…to here");
		var drop = new DropGestureRecognizer();
		drop.Drop += (_, _) => target.Text = "Dropped!";
		drop.DragOver += (_, _) => target.Text = "Let go";
		drop.DragLeave += (_, _) => target.Text = "…to here";
		target.GestureRecognizers.Add(drop);
		stack.Add(AnimationsLabPage.Section("Drag and drop"));
		stack.Add(new HorizontalStackLayout { Spacing = 16, Children = { token, target } });

		Content = new ScrollView { Content = stack };
	}

	private static Label Box(string text)
	{
		var label = new Label
		{
			Text = text,
			HeightRequest = 80,
			HorizontalTextAlignment = TextAlignment.Center,
			VerticalTextAlignment = TextAlignment.Center,
			BackgroundColor = Color.FromRgba(255, 255, 255, 30),
		};
		return label;
	}
}
