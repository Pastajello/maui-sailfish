using Microsoft.Maui.Controls;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;

namespace Linux.SailfishOS.Tests;

/// <summary>A pull-to-refresh surface follows exactly one RefreshView and only its refresh-surface properties.</summary>
// Constructing controls triggers MAUI's static mapper remap, which must not race HandlerParityTests.
[Collection("renderer")]
public class RefreshBindingTests
{
	[Fact]
	public void Forwards_refresh_changes_until_disarmed()
	{
		var refresh = new RefreshView();
		var binding = new QtHostRefreshBinding();
		var pushes = 0;
		Assert.True(binding.Arm(refresh, _ => pushes++));
		Assert.False(binding.Arm(refresh, _ => pushes++));   // same view: no re-subscribe

		refresh.IsRefreshing = true;
		refresh.Margin = new Microsoft.Maui.Thickness(3);     // not a refresh-surface property
		Assert.Equal(1, pushes);

		binding.Disarm();
		refresh.IsRefreshing = false;
		Assert.Equal(1, pushes);
		Assert.Null(binding.View);
	}

	[Fact]
	public void Suppressed_changes_are_not_pushed_back()
	{
		var refresh = new RefreshView();
		var binding = new QtHostRefreshBinding();
		var pushes = 0;
		var suppressed = true;
		binding.Arm(refresh, _ => pushes++, () => suppressed);

		refresh.IsRefreshing = true;
		suppressed = false;
		refresh.IsRefreshing = false;
		Assert.Equal(1, pushes);
	}

	[Fact]
	public void Rearming_moves_the_subscription_to_the_new_view()
	{
		var first = new RefreshView();
		var second = new RefreshView();
		var binding = new QtHostRefreshBinding();
		RefreshView? pushed = null;
		binding.Arm(first, r => pushed = r);
		binding.Arm(second, r => pushed = r);

		first.IsRefreshing = true;
		Assert.Null(pushed);
		second.IsRefreshing = true;
		Assert.Same(second, pushed);
	}
}

/// <summary>Adapters paint plain colors: a solid Background brush wins, the unset Brush.Default does not.</summary>
[Collection("renderer")]
public class PaintTests
{
	[Fact]
	public void Solid_brush_wins_over_background_color()
	{
		var view = new BoxView { BackgroundColor = Microsoft.Maui.Graphics.Colors.Red, Background = new SolidColorBrush(Microsoft.Maui.Graphics.Colors.Blue) };
		Assert.Equal(Microsoft.Maui.Graphics.Colors.Blue, QtHostPaint.Background(view));
	}

	[Fact]
	public void Unset_or_gradient_brush_falls_back_to_background_color()
	{
		var plain = new BoxView { BackgroundColor = Microsoft.Maui.Graphics.Colors.Red };
		Assert.Equal(Microsoft.Maui.Graphics.Colors.Red, QtHostPaint.Background(plain));
		var gradient = new BoxView { Background = new LinearGradientBrush() };
		Assert.Null(QtHostPaint.Background(gradient));
		Assert.Null(QtHostPaint.Solid(Brush.Default));
	}
}
