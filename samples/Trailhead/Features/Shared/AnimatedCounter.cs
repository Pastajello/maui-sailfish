namespace Trailhead.Features.Shared;

/// <summary>
/// A number that counts to its new value instead of jumping (350 ms, cubic ease-out) and pulses when it crosses a
/// whole unit of <see cref="PulseEvery"/> (each kilometre). An animation that is still running is replaced, not queued.
/// </summary>
public sealed class AnimatedCounter : Label
{
	public static readonly BindableProperty ValueProperty = BindableProperty.Create(nameof(Value), typeof(double),
		typeof(AnimatedCounter), 0.0, propertyChanged: (b, o, n) => ((AnimatedCounter)b).AnimateTo((double)o, (double)n));

	private const string AnimationName = "count";
	private double _shown;

	public AnimatedCounter()
	{
		FontSize = 30;
		Text = "–";
	}

	public double Value
	{
		get => (double)GetValue(ValueProperty);
		set => SetValue(ValueProperty, value);
	}

	/// <summary>How the value reads; the default is a plain number.</summary>
	public Func<double, string> Formatter { get; set; } = v => v.ToString("0", System.Globalization.CultureInfo.InvariantCulture);

	/// <summary>Pulse when the value passes a multiple of this (0: never).</summary>
	public double PulseEvery { get; set; }

	private void AnimateTo(double from, double to)
	{
		this.AbortAnimation(AnimationName);
		var start = _shown;
		new Animation(v =>
		{
			_shown = v;
			Text = Formatter(v);
		}, start, to, Easing.CubicOut).Commit(this, AnimationName, length: 350);

		if (PulseEvery > 0 && Math.Floor(to / PulseEvery) > Math.Floor(from / PulseEvery))
			_ = PulseAsync();
	}

	private async Task PulseAsync()
	{
		await this.ScaleToAsync(1.25, 150, Easing.CubicOut);
		await this.ScaleToAsync(1, 250, Easing.SpringOut);
	}
}
