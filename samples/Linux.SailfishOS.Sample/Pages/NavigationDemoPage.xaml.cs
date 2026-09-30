using Microsoft.Maui.Controls;

namespace Microsoft.Maui.SailfishOS.Sample.Pages;

/// <summary>
/// Gallery of navigation features: modal pages, toolbar items, TabbedPage,
/// FlyoutPage and the page lifecycle callbacks.
/// </summary>
public partial class NavigationDemoPage : ContentPage
{
	private readonly List<string> _log = new();

	public NavigationDemoPage()
	{
		InitializeComponent();
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		Log("appearing");
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();
		Log("disappearing");
	}

	private void Log(string what)
	{
		_log.Add($"{what}@{DateTime.Now:HH:mm:ss}");
		LifecycleLabel.Text = string.Join(" → ", _log);
	}

	private async void OnModalClicked(object? sender, EventArgs e)
	{
		var modal = new ContentPage
		{
			Title = "Modal",
			BackgroundColor = Color.FromArgb("#181020"),
			Content = new VerticalStackLayout
			{
				Padding = new Thickness(20),
				Spacing = 12,
				Children =
				{
					new Label { Text = "This page was pushed modally.", TextColor = Colors.White, FontSize = 22 },
					new Button { Text = "Close modal", BackgroundColor = Colors.Crimson, TextColor = Colors.White },
				},
			},
		};

		((Button)((VerticalStackLayout)modal.Content).Children[1]).Clicked += async (_, _) =>
			await Navigation.PopModalAsync();

		await Navigation.PushModalAsync(new NavigationPage(modal));
		Log("push-modal");
	}

	private async void OnTabsClicked(object? sender, EventArgs e)
	{
		var tabs = new TabbedPage { Title = "Tabs" };
		tabs.Children.Add(new ContentPage { Title = "One", BackgroundColor = Color.FromArgb("#101018"), Content = new Label { Text = "tab one", TextColor = Colors.White, FontSize = 24 } });
		tabs.Children.Add(new ContentPage { Title = "Two", BackgroundColor = Color.FromArgb("#101018"), Content = new Label { Text = "tab two", TextColor = Colors.White, FontSize = 24 } });
		tabs.Children.Add(new ContentPage { Title = "Three", BackgroundColor = Color.FromArgb("#101018"), Content = new Label { Text = "tab three", TextColor = Colors.White, FontSize = 24 } });

		await Navigation.PushAsync(tabs);
		Log("push-tabs");
	}

	private async void OnFlyoutClicked(object? sender, EventArgs e)
	{
		var flyout = new FlyoutPage { Title = "Flyout" };
		flyout.Flyout = new ContentPage
		{
			Title = "Menu",
			BackgroundColor = Color.FromArgb("#101820"),
			Content = new VerticalStackLayout
			{
				Padding = new Thickness(20),
				Children =
				{
					new Label { Text = "menu item A", TextColor = Colors.White, FontSize = 22 },
					new Label { Text = "menu item B", TextColor = Colors.White, FontSize = 22 },
				},
			},
		};
		flyout.Detail = new NavigationPage(new ContentPage
		{
			Title = "Detail",
			BackgroundColor = Color.FromArgb("#101018"),
			Content = new Label { Text = "flyout detail", TextColor = Colors.White, FontSize = 24 },
		});

		await Navigation.PushModalAsync(flyout);
		Log("push-flyout");
	}

	private void OnToolbarInfoClicked(object? sender, EventArgs e) =>
		Log("toolbar-info");
}
