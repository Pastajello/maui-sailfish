using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace SailfishKitchen.Controls;

/// <summary>
/// The grey placeholder tile shown while the first page loads. Static on purpose: each animated frame
/// costs a Qt reconcile pass, and a grid of shimmer loops would drive layout continuously.
/// </summary>
public sealed class SkeletonTile : Border
{
	public SkeletonTile()
	{
		BackgroundColor = Palette.Surface;
		Stroke = Palette.Border;
		StrokeThickness = 1;
		StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) };
		Padding = new Thickness(14);
		IsEnabled = false;

		Content = new VerticalStackLayout
		{
			Spacing = 10,
			Children =
			{
				Block(height: 96, widthFraction: double.NaN),
				Block(height: 16, widthFraction: 0.85),
				Block(height: 12, widthFraction: 0.55),
			},
		};
	}

	/// <summary>One grey bar; a <paramref name="widthFraction"/> below 1 makes it read as text.</summary>
	private static BoxView Block(double height, double widthFraction)
	{
		var block = new BoxView
		{
			Color = Palette.Skeleton,
			HeightRequest = height,
			CornerRadius = (float)(height / 2),
			HorizontalOptions = double.IsNaN(widthFraction) ? LayoutOptions.Fill : LayoutOptions.Start,
		};

		if (!double.IsNaN(widthFraction))
			block.WidthRequest = 220 * widthFraction;

		return block;
	}
}

/// <summary>
/// A grid of skeleton tiles for a page's initial load. Derives from <c>Border</c> rather than a <c>Layout</c>:
/// the XAML source generator fails with MAUIG1001 on a self-closing element whose type derives from a multi-child Layout.
/// </summary>
public sealed class SkeletonPanel : Border
{
	public static readonly BindableProperty RowsProperty = BindableProperty.Create(
		nameof(Rows), typeof(int), typeof(SkeletonPanel), 3, propertyChanged: OnShapeChanged);

	public static readonly BindableProperty ColumnsProperty = BindableProperty.Create(
		nameof(Columns), typeof(int), typeof(SkeletonPanel), 2, propertyChanged: OnShapeChanged);

	public SkeletonPanel() => Content = Build(Rows, Columns);

	public int Rows
	{
		get => (int)GetValue(RowsProperty);
		set => SetValue(RowsProperty, value);
	}

	public int Columns
	{
		get => (int)GetValue(ColumnsProperty);
		set => SetValue(ColumnsProperty, value);
	}

	private static void OnShapeChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is SkeletonPanel panel)
			panel.Content = Build(panel.Rows, panel.Columns);
	}

	private static VerticalStackLayout Build(int rows, int columns)
	{
		rows = Math.Clamp(rows, 1, 12);
		columns = Math.Clamp(columns, 1, 4);

		var stack = new VerticalStackLayout { Spacing = 12 };
		for (var row = 0; row < rows; row++)
		{
			var grid = new Grid { ColumnSpacing = 12 };
			for (var column = 0; column < columns; column++)
				grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

			for (var column = 0; column < columns; column++)
				grid.Add(new SkeletonTile(), column, 0);

			stack.Children.Add(grid);
		}

		return stack;
	}
}
