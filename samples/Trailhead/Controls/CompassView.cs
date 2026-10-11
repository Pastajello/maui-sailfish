namespace Trailhead.Controls;

/// <summary>
/// A compass dial: the rose turns with <see cref="Heading"/> (degrees from north the phone points to), and an arrow
/// shows <see cref="TargetBearing"/> (the way back to the start), when set. On Sailfish it is a native QML item with its
/// own adapter (Platforms/Sailfish: the custom-control path of docs/custom-controls.md, as a library would ship it);
/// the rotation animates in QML along the shortest way, so 359° → 1° turns two degrees, not the full circle.
/// </summary>
public class CompassView : View
{
	public static readonly BindableProperty HeadingProperty =
		BindableProperty.Create(nameof(Heading), typeof(double), typeof(CompassView), 0.0);

	public static readonly BindableProperty TargetBearingProperty =
		BindableProperty.Create(nameof(TargetBearing), typeof(double?), typeof(CompassView));

	public static readonly BindableProperty IsCalibratedProperty =
		BindableProperty.Create(nameof(IsCalibrated), typeof(bool), typeof(CompassView), true);

	public double Heading
	{
		get => (double)GetValue(HeadingProperty);
		set
		{
			SetValue(HeadingProperty, value);
			SemanticProperties.SetDescription(this, $"Compass, heading {Math.Round(((value % 360) + 360) % 360)} degrees");
		}
	}

	public double? TargetBearing
	{
		get => (double?)GetValue(TargetBearingProperty);
		set => SetValue(TargetBearingProperty, value);
	}

	/// <summary>False shows "calibrate" (figure-eight the phone) instead of trusting the needle.</summary>
	public bool IsCalibrated
	{
		get => (bool)GetValue(IsCalibratedProperty);
		set => SetValue(IsCalibratedProperty, value);
	}

	/// <summary>Raised when the user taps the dial (the recording page toggles north-up/heading-up there).</summary>
	public event EventHandler? Tapped;

	internal void SendTapped() => Tapped?.Invoke(this, EventArgs.Empty);
}
