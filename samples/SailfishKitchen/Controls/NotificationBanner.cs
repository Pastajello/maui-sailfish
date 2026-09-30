using System.ComponentModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using SailfishKitchen.ViewModels;

namespace SailfishKitchen.Controls;

/// <summary>
/// The app-wide transient-message strip at the bottom of each page. It is the guaranteed notification path,
/// since the toolkit toast may have no native counterpart on this host.
/// </summary>
public sealed class NotificationBanner : Border
{
	private readonly Label _text;
	private readonly BoxView _accentBar;
	private readonly Button _dismiss;
	private BannerViewModel? _model;
	private bool _animating;

	public NotificationBanner()
	{
		BackgroundColor = Palette.SurfaceRaised;
		Stroke = Palette.Border;
		StrokeThickness = 1;
		StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) };
		Padding = new Thickness(14, 10);
		Margin = new Thickness(16, 0, 16, 16);
		VerticalOptions = LayoutOptions.End;
		// Hidden = opacity 0 + disabled: this backend drops IsVisible updates once the host exists.
		IsEnabled = false;
		Opacity = 0;
		TranslationY = 24;
		// Above the list, so a scrolling row never paints over it.
		ZIndex = 100;

		_accentBar = new BoxView
		{
			Color = Palette.Accent,
			WidthRequest = 4,
			CornerRadius = 2,
			VerticalOptions = LayoutOptions.Fill,
		};

		_text = new Label
		{
			FontSize = 15,
			TextColor = Palette.TextPrimary,
			VerticalOptions = LayoutOptions.Center,
			LineBreakMode = LineBreakMode.WordWrap,
		};

		_dismiss = new Button
		{
			Text = "✕",
			FontSize = 14,
			BackgroundColor = Colors.Transparent,
			BorderColor = Colors.Transparent,
			TextColor = Palette.TextSecondary,
			Padding = new Thickness(6),
			MinimumWidthRequest = 34,
			MinimumHeightRequest = 34,
			VerticalOptions = LayoutOptions.Center,
		};
		_dismiss.Clicked += (_, _) => _model?.Dismiss();

		var layout = new Grid { ColumnSpacing = 10 };
		layout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
		layout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
		layout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
		layout.Add(_accentBar, 0);
		layout.Add(_text, 1);
		layout.Add(_dismiss, 2);

		Content = layout;
		BindingContextChanged += OnBindingContextChanged;
	}

	private void OnBindingContextChanged(object? sender, EventArgs e)
	{
		if (_model is not null)
			_model.PropertyChanged -= OnModelPropertyChanged;

		_model = BindingContext as BannerViewModel;
		if (_model is null)
			return;

		_model.PropertyChanged += OnModelPropertyChanged;
		_text.SetBinding(Label.TextProperty, nameof(BannerViewModel.Text));
		ApplyKind();
		SyncVisibility(animate: false);
	}

	private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		switch (e.PropertyName)
		{
			case nameof(BannerViewModel.IsVisible):
				SyncVisibility(animate: true);
				break;

			case nameof(BannerViewModel.Text):
			case nameof(BannerViewModel.Accent):
				ApplyKind();
				break;
		}
	}

	/// <summary>The strip and border carry the notification kind's colour.</summary>
	private void ApplyKind()
	{
		if (_model is null)
			return;

		_accentBar.Color = _model.Accent;
		Stroke = _model.Accent;
	}

	/// <summary>
	/// Slides the strip in or out via Opacity and TranslationY, which the renderer pushes to the Qt scene graph.
	/// </summary>
	private async void SyncVisibility(bool animate)
	{
		if (_model is null || _animating)
			return;

		var visible = _model.IsVisible;
		_animating = true;
		try
		{
			if (visible)
			{
				IsEnabled = true;
				if (animate)
				{
					await Task.WhenAll(
						this.FadeToAsync(1, 180, Easing.CubicOut),
						this.TranslateToAsync(0, 0, 220, Easing.CubicOut));
				}
				else
				{
					Opacity = 1;
					TranslationY = 0;
				}
			}
			else
			{
				if (animate)
				{
					await Task.WhenAll(
						this.FadeToAsync(0, 160, Easing.CubicIn),
						this.TranslateToAsync(0, 24, 200, Easing.CubicIn));
				}
				else
				{
					Opacity = 0;
					TranslationY = 24;
				}

				IsEnabled = false;
			}
		}
		finally
		{
			_animating = false;
		}
	}
}
