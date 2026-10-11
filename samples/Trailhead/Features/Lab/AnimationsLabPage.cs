using Trailhead.Features.Shared;

namespace Trailhead.Features.Lab;

/// <summary>
/// Easings side by side (a dot per easing races across), a sequence (move, turn, fade back), a parallel group, and a
/// long animation that a second tap cancels mid-way (AbortAnimation): the dot stops where it is.
/// </summary>
public sealed class AnimationsLabPage : ContentPage
{
	public const string Route = "lab/animations";

	private static readonly (string Name, Easing Easing)[] Easings =
	{
		("Linear", Easing.Linear), ("SinInOut", Easing.SinInOut), ("CubicOut", Easing.CubicOut),
		("BounceOut", Easing.BounceOut), ("SpringOut", Easing.SpringOut), ("CubicIn", Easing.CubicIn),
	};

	private readonly List<BoxView> _racers = new();
	private readonly BoxView _sequenceBox = Dot();
	private readonly BoxView _slowBox = Dot();
	private readonly Label _slowState = new() { FontSize = 13 };
	private bool _slowRunning;

	public AnimationsLabPage()
	{
		Title = "Animations";
		var stack = new VerticalStackLayout { Padding = new Thickness(16, 8), Spacing = 10 };

		stack.Add(Section("Easings (tap to race)"));
		foreach (var (name, _) in Easings)
		{
			var dot = Dot();
			_racers.Add(dot);
			var label = new Label { Text = name, FontSize = 12, VerticalOptions = LayoutOptions.Center };
			label.SetDynamicResource(Label.TextColorProperty, ThemeResources.Secondary);
			stack.Add(new Grid { ColumnDefinitions = { new(90), new(GridLength.Star) }, Children = { label, Place(dot, 1) } });
		}
		stack.Add(Button("Race", (_, _) => _ = RaceAsync()));

		stack.Add(Section("Sequence and parallel"));
		stack.Add(_sequenceBox);
		stack.Add(Button("Run", (_, _) => _ = SequenceAsync()));

		stack.Add(Section("Cancel mid-way"));
		stack.Add(_slowBox);
		stack.Add(_slowState);
		stack.Add(Button("Start / stop", (_, _) => ToggleSlow()));

		Content = new ScrollView { Content = stack };
	}

	/// <summary>The last race's positions, for the tour: every dot at the end.</summary>
	public IReadOnlyList<double> RacerPositions => _racers.Select(r => r.TranslationX).ToList();

	public Task RaceAsync()
	{
		var tasks = new List<Task>();
		for (var i = 0; i < _racers.Count; i++)
		{
			var dot = _racers[i];
			dot.TranslationX = 0;
			var distance = Math.Max(40, (Width > 0 ? Width : 400) - 90 - 32 - dot.WidthRequest);
			tasks.Add(dot.TranslateToAsync(distance, 0, 1500, Easings[i].Easing));
		}
		return Task.WhenAll(tasks);
	}

	public async Task SequenceAsync()
	{
		var box = _sequenceBox;
		await box.TranslateToAsync(120, 0, 400, Easing.CubicOut);
		await box.RotateToAsync(180, 400, Easing.CubicInOut);
		await Task.WhenAll(
			box.ScaleToAsync(1.6, 500, Easing.SpringOut),
			box.FadeToAsync(0.3, 500),
			box.TranslateToAsync(0, 40, 500));
		await Task.WhenAll(box.ScaleToAsync(1, 300), box.FadeToAsync(1, 300), box.RotateToAsync(0, 300), box.TranslateToAsync(0, 0, 300));
	}

	/// <summary>A five-second slide; a second call aborts it where it is.</summary>
	public void ToggleSlow()
	{
		if (_slowRunning)
		{
			_slowBox.AbortAnimation("slow");
			_slowRunning = false;
			_slowState.Text = $"Stopped at {_slowBox.TranslationX:0} dp";
			return;
		}
		_slowRunning = true;
		_slowBox.TranslationX = 0;
		_slowState.Text = "Sliding…";
		new Animation(v => _slowBox.TranslationX = v, 0, 260).Commit(_slowBox, "slow", length: 5000,
			finished: (_, cancelled) =>
			{
				_slowRunning = false;
				if (!cancelled)
					_slowState.Text = "Arrived";
			});
	}

	public double SlowPosition => _slowBox.TranslationX;

	private static BoxView Dot()
	{
		var dot = new BoxView { WidthRequest = 24, HeightRequest = 24, CornerRadius = 12, HorizontalOptions = LayoutOptions.Start };
		dot.SetDynamicResource(BoxView.ColorProperty, ThemeResources.Highlight);
		return dot;
	}

	private static View Place(View view, int column)
	{
		Grid.SetColumn(view, column);
		return view;
	}

	internal static Label Section(string text)
	{
		var label = new Label { Text = text, FontSize = 15, HorizontalTextAlignment = TextAlignment.End, Margin = new Thickness(0, 12, 0, 0) };
		label.SetDynamicResource(Label.TextColorProperty, ThemeResources.Highlight);
		return label;
	}

	internal static Button Button(string text, EventHandler clicked)
	{
		var button = new Button { Text = text, HorizontalOptions = LayoutOptions.Center };
		button.Clicked += clicked;
		return button;
	}
}
