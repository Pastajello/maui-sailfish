using SailfishSpecific = Microsoft.Maui.Controls.PlatformConfiguration.SailfishOSSpecific;

namespace Trailhead.Features.Photos;

/// <summary>
/// An image that pinches to zoom (1×–4×), pans while zoomed and toggles 2× on a double tap. The pan recognizer exists
/// only while zoomed, and only then does the view keep its drags (KeepsDrag: Silica's preventStealing), so at 1× a
/// horizontal swipe still belongs to the carousel around it.
/// </summary>
public sealed class ZoomableImage : ContentView
{
	public static readonly BindableProperty SourceProperty =
		BindableProperty.Create(nameof(Source), typeof(ImageSource), typeof(ZoomableImage),
			propertyChanged: (b, _, n) => ((ZoomableImage)b)._image.Source = (ImageSource?)n);

	private const double MaxScale = 4;

	private readonly Image _image = new() { Aspect = Aspect.AspectFit };
	private readonly PanGestureRecognizer _pan = new();
	private double _startScale = 1, _panStartX, _panStartY;

	public ZoomableImage()
	{
		Content = _image;
		IsClippedToBounds = true;

		var pinch = new PinchGestureRecognizer();
		pinch.PinchUpdated += OnPinch;
		GestureRecognizers.Add(pinch);

		var doubleTap = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
		doubleTap.Tapped += (_, _) => _ = ZoomToAsync(_image.Scale > 1 ? 1 : 2);
		GestureRecognizers.Add(doubleTap);

		_pan.PanUpdated += OnPan;
		UpdatePan();
	}

	public ImageSource? Source
	{
		get => (ImageSource?)GetValue(SourceProperty);
		set => SetValue(SourceProperty, value);
	}

	public bool IsZoomed => _image.Scale > 1.01;

	public double ImageScale => _image.Scale;

	/// <summary>Back to 1× without animation (the carousel moved on).</summary>
	public void Reset()
	{
		_image.Scale = 1;
		_image.TranslationX = _image.TranslationY = 0;
		UpdatePan();
	}

	private async Task ZoomToAsync(double scale)
	{
		if (scale <= 1)
			await Task.WhenAll(_image.ScaleToAsync(1, 200, Easing.CubicOut), _image.TranslateToAsync(0, 0, 200, Easing.CubicOut));
		else
			await _image.ScaleToAsync(scale, 200, Easing.CubicOut);
		UpdatePan();
	}

	private void OnPinch(object? sender, PinchGestureUpdatedEventArgs e)
	{
		switch (e.Status)
		{
			case GestureStatus.Started:
				_startScale = _image.Scale;
				_image.AnchorX = e.ScaleOrigin.X;
				_image.AnchorY = e.ScaleOrigin.Y;
				break;
			case GestureStatus.Running:
				_image.Scale = Math.Clamp(_image.Scale * e.Scale, 0.8, MaxScale);
				break;
			case GestureStatus.Completed:
				if (_image.Scale < 1)
					_ = ZoomToAsync(1);
				else
					UpdatePan();
				break;
		}
	}

	private void OnPan(object? sender, PanUpdatedEventArgs e)
	{
		switch (e.StatusType)
		{
			case GestureStatus.Started:
				_panStartX = _image.TranslationX;
				_panStartY = _image.TranslationY;
				break;
			case GestureStatus.Running:
				// Keep the image edge inside the frame: at scale s the image overhangs (s-1)/2 of its size on each side.
				var maxX = Width * (_image.Scale - 1) / 2;
				var maxY = Height * (_image.Scale - 1) / 2;
				_image.TranslationX = Math.Clamp(_panStartX + e.TotalX, -maxX, maxX);
				_image.TranslationY = Math.Clamp(_panStartY + e.TotalY, -maxY, maxY);
				break;
		}
	}

	private void UpdatePan()
	{
		SailfishSpecific.VisualElement.SetKeepsDrag(this, IsZoomed);
		var has = GestureRecognizers.Contains(_pan);
		if (IsZoomed && !has)
			GestureRecognizers.Add(_pan);
		else if (!IsZoomed && has)
			GestureRecognizers.Remove(_pan);
	}
}
