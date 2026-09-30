using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using ArcSegment = Microsoft.Maui.Controls.Shapes.ArcSegment;
using Path = Microsoft.Maui.Controls.Shapes.Path;
using PathFigure = Microsoft.Maui.Controls.Shapes.PathFigure;
using PathGeometry = Microsoft.Maui.Controls.Shapes.PathGeometry;

namespace SailfishKitchen.Controls;

/// <summary>
/// A rotating 270-degree arc spinner. <see cref="ActivityIndicator"/> maps to Silica's BusyIndicator, whose
/// graphic is missing in the SFOS 5.2 theme and paints as a static dot.
/// </summary>
public sealed class SpinnerView : Border
{
	public static readonly BindableProperty IsRunningProperty = BindableProperty.Create(
		nameof(IsRunning), typeof(bool), typeof(SpinnerView), false, propertyChanged: OnRunningChanged);

	public static readonly BindableProperty SpinnerColorProperty = BindableProperty.Create(
		nameof(SpinnerColor), typeof(Color), typeof(SpinnerView), Palette.Accent, propertyChanged: OnColorChanged);

	private readonly Path _arc;
	private CancellationTokenSource? _spin;

	public SpinnerView()
	{
		WidthRequest = 30;
		HeightRequest = 30;
		HorizontalOptions = LayoutOptions.Center;
		VerticalOptions = LayoutOptions.Center;
		Opacity = 0;
		IsEnabled = false;

		_arc = new Path
		{
			Stroke = Palette.Accent,
			StrokeThickness = 3.5,
			Data = BuildArc(),
		};

		Content = _arc;
	}

	public bool IsRunning
	{
		get => (bool)GetValue(IsRunningProperty);
		set => SetValue(IsRunningProperty, value);
	}

	public Color SpinnerColor
	{
		get => (Color)GetValue(SpinnerColorProperty);
		set => SetValue(SpinnerColorProperty, value);
	}

	private static void OnRunningChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is SpinnerView spinner)
			spinner.SyncRunning(newValue is true);
	}

	private static void OnColorChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is SpinnerView spinner && newValue is Color color)
			spinner._arc.Stroke = color;
	}

	/// <summary>A 270-degree open arc in a 32x32 box; the gap is what makes rotation read as spinning.</summary>
	private static PathGeometry BuildArc()
	{
		const double center = 16d;
		const double radius = 13d;

		var start = new Point(center, center - radius);
		var end = new Point(center - radius, center);

		var figure = new PathFigure
		{
			StartPoint = start,
			IsClosed = false,
			IsFilled = false,
		};

		figure.Segments.Add(new ArcSegment
		{
			Point = end,
			Size = new Size(radius, radius),
			SweepDirection = SweepDirection.Clockwise,
			IsLargeArc = true,
		});

		var geometry = new PathGeometry();
		geometry.Figures.Add(figure);
		return geometry;
	}

	private void SyncRunning(bool running)
	{
		// This backend pushes Opacity changes immediately but not IsVisible, so toggle by opacity.
		Opacity = running ? 1 : 0;
		IsEnabled = running;

		if (!running)
		{
			_spin?.Cancel();
			_spin?.Dispose();
			_spin = null;
			Rotation = 0;
			return;
		}

		if (_spin is not null)
			return;

		var cts = new CancellationTokenSource();
		_spin = cts;
		_ = SpinAsync(cts.Token);
	}

	/// <summary>One revolution per iteration, each a single MAUI animation.</summary>
	private async Task SpinAsync(CancellationToken ct)
	{
		try
		{
			while (!ct.IsCancellationRequested)
			{
				Rotation = 0;
				await this.RotateToAsync(360, 900, Easing.Linear).WaitAsync(ct).ConfigureAwait(true);
			}
		}
		catch (OperationCanceledException)
		{
			// Stopped; SyncRunning resets Rotation.
		}
		catch (Exception)
		{
			// A spinner that cannot animate must never take the page down.
		}
	}
}
