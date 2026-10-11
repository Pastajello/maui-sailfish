using Trailhead.Features.Shared;

namespace Trailhead.Features.Lab;

/// <summary>
/// SwipeView rows: they work, with their items left and right, but a sideways swipe on a Sailfish page is also the
/// back gesture, which is why Trailhead's own list uses press and hold (a Silica context menu) instead.
/// </summary>
public sealed class SwipeLabPage : ContentPage
{
	public const string Route = "lab/swipe";

	private readonly Label _last = new() { FontSize = 13, HorizontalTextAlignment = TextAlignment.Center };

	public SwipeLabPage()
	{
		Title = "SwipeView";
		_last.SetDynamicResource(Label.TextColorProperty, ThemeResources.Secondary);
		var stack = new VerticalStackLayout { Padding = new Thickness(0, 8), Spacing = 6 };
		foreach (var name in new[] { "Kasprowy Wierch", "Śnieżka", "Saana" })
			stack.Add(Row(name));
		stack.Add(_last);
		Content = stack;
	}

	public string LastAction => _last.Text ?? "";

	private View Row(string name)
	{
		var content = new Label { Text = name, Padding = new Thickness(16, 18), BackgroundColor = Color.FromRgba(255, 255, 255, 20) };
		var favourite = new SwipeItem { Text = "Favourite", BackgroundColor = Color.FromArgb("#2E7D32") };
		favourite.Invoked += (_, _) => _last.Text = $"{name}: favourite";
		var delete = new SwipeItem { Text = "Delete", BackgroundColor = Color.FromArgb("#C62828") };
		delete.Invoked += (_, _) => _last.Text = $"{name}: delete";
		return new SwipeView
		{
			LeftItems = new SwipeItems { favourite },
			RightItems = new SwipeItems { delete },
			Content = content,
		};
	}
}
