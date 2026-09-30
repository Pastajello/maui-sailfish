using Microsoft.Maui.Controls;
using SailfishKitchen.Models;

namespace SailfishKitchen.Controls;

/// <summary>
/// Shows one of loading, empty or error by swapping its content, never by toggling IsVisible: this backend
/// honours IsVisible only at host creation (Button hosts ignore it entirely), while create/destroy is reconciled.
/// In the Content state it holds no child and takes no space.
/// </summary>
public sealed class StateSlot : Border
{
	public static readonly BindableProperty StateProperty = BindableProperty.Create(
		nameof(State), typeof(LoadState), typeof(StateSlot), LoadState.Content,
		propertyChanged: (bindable, _, _) => ((StateSlot)bindable).Sync());

	public static readonly BindableProperty LoadingTemplateProperty = BindableProperty.Create(
		nameof(LoadingTemplate), typeof(DataTemplate), typeof(StateSlot), null,
		propertyChanged: (bindable, _, _) => ((StateSlot)bindable).Sync());

	public static readonly BindableProperty EmptyTemplateProperty = BindableProperty.Create(
		nameof(EmptyTemplate), typeof(DataTemplate), typeof(StateSlot), null,
		propertyChanged: (bindable, _, _) => ((StateSlot)bindable).Sync());

	public static readonly BindableProperty ErrorTemplateProperty = BindableProperty.Create(
		nameof(ErrorTemplate), typeof(DataTemplate), typeof(StateSlot), null,
		propertyChanged: (bindable, _, _) => ((StateSlot)bindable).Sync());

	public StateSlot()
	{
		// Chrome-less: the templates bring their own card styling.
		Padding = new Thickness(0);
		Stroke = Colors.Transparent;
		StrokeThickness = 0;
		BackgroundColor = Colors.Transparent;
	}

	public LoadState State
	{
		get => (LoadState)GetValue(StateProperty);
		set => SetValue(StateProperty, value);
	}

	public DataTemplate? LoadingTemplate
	{
		get => (DataTemplate?)GetValue(LoadingTemplateProperty);
		set => SetValue(LoadingTemplateProperty, value);
	}

	public DataTemplate? EmptyTemplate
	{
		get => (DataTemplate?)GetValue(EmptyTemplateProperty);
		set => SetValue(EmptyTemplateProperty, value);
	}

	public DataTemplate? ErrorTemplate
	{
		get => (DataTemplate?)GetValue(ErrorTemplateProperty);
		set => SetValue(ErrorTemplateProperty, value);
	}

	private void Sync()
	{
		var template = State switch
		{
			LoadState.Loading => LoadingTemplate,
			LoadState.Empty => EmptyTemplate,
			LoadState.Error => ErrorTemplate,
			_ => null,
		};

		// CreateContent builds a fresh element, so flipping states never resurrects a stale host.
		Content = template?.CreateContent() as View;

		// The backend does not reliably destroy a cleared child host but does reconcile geometry, so collapse
		// to zero height and disable to hide any leftover.
		HeightRequest = template is null ? 0 : -1;
		IsEnabled = template is not null;
	}
}
