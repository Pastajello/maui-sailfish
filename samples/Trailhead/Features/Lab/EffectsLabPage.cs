using Microsoft.Maui.Controls.Platform;
using Microsoft.Maui.SailfishOS.Handlers;
using Trailhead.Features.Shared;

namespace Trailhead.Features.Lab;

/// <summary>
/// The same glow two ways. An Effect (<see cref="GlowEffect"/>, a RoutingEffect served by <see cref="SailfishGlowEffect"/>,
/// the Xamarin.Forms way) and a handler mapper (<see cref="Glow.IsOnProperty"/>, appended to Sailfish's Label mapper,
/// the MAUI way). Both end up setting MAUI's Shadow: on Sailfish a mapper cannot draw on the native item itself
/// (docs/custom-controls.md, snapshot mappers), and neither can an effect.
/// </summary>
public sealed class EffectsLabPage : ContentPage
{
	public const string Route = "lab/effects";

	private readonly Label _byEffect = Sample("Glow by an Effect");
	private readonly Label _byMapper = Sample("Glow by a mapper");
	private readonly Label _state = new() { FontSize = 13 };

	public EffectsLabPage()
	{
		Title = "Effects and mappers";
		_state.SetDynamicResource(Label.TextColorProperty, ThemeResources.Secondary);
		var toggle = AnimationsLabPage.Button("Toggle both", (_, _) => Toggle());
		Content = new VerticalStackLayout
		{
			Padding = new Thickness(16, 24),
			Spacing = 28,
			Children = { _byEffect, _byMapper, toggle, _state },
		};
		Toggle();
	}

	public bool EffectAttached => _byEffect.Effects.OfType<GlowEffect>().Any() && SailfishGlowEffect.Attached > 0;

	public bool MapperRan => Glow.MapperRuns > 0;

	public Shadow? EffectShadow => _byEffect.Shadow;

	public Shadow? MapperShadow => _byMapper.Shadow;

	private void Toggle()
	{
		var on = !_byEffect.Effects.OfType<GlowEffect>().Any();
		if (on)
			_byEffect.Effects.Add(new GlowEffect());
		else
			_byEffect.Effects.Clear();
		Glow.SetIsOn(_byMapper, on);
		Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(300), () =>
			_state.Text = $"Effect attached {SailfishGlowEffect.Attached}×, detached {SailfishGlowEffect.Detached}×; mapper ran {Glow.MapperRuns}×");
	}

	private static Label Sample(string text) => new() { Text = text, FontSize = 26, HorizontalOptions = LayoutOptions.Center };
}

/// <summary>The cross-platform side of the effect: resolved to a platform effect by its name.</summary>
public sealed class GlowEffect() : RoutingEffect
{
}

/// <summary>The Sailfish side: attached and detached with the element, as on every platform.</summary>
public sealed class SailfishGlowEffect : PlatformEffect
{
	public static int Attached { get; private set; }

	public static int Detached { get; private set; }

	/// <summary>What OnAttached saw (tour diagnostics): the element's type and whether the shadow stuck.</summary>
	public static string LastAttach { get; private set; } = "";

	protected override void OnAttached()
	{
		Attached++;
		if (Element is VisualElement view)
			view.Shadow = Glow.Make();
		LastAttach = $"{Element?.GetType().Name ?? "null"} shadow={(Element as VisualElement)?.Shadow is not null}";
	}

	protected override void OnDetached()
	{
		Detached++;
		if (Element is VisualElement view)
			view.Shadow = null!;
	}
}

/// <summary>The mapper way: an attached property every Label can carry, applied by a key appended to the Label
/// mapper (MauiProgram registers it once for the app).</summary>
public static class Glow
{
	public static readonly BindableProperty IsOnProperty = BindableProperty.CreateAttached("IsOn", typeof(bool), typeof(Glow), false,
		propertyChanged: (b, _, _) => (b as VisualElement)?.Handler?.UpdateValue("Glow"));

	public static int MapperRuns { get; private set; }

	public static bool GetIsOn(BindableObject view) => (bool)view.GetValue(IsOnProperty);

	public static void SetIsOn(BindableObject view, bool value) => view.SetValue(IsOnProperty, value);

	public static Shadow Make() => new() { Brush = Colors.Orange, Radius = 18, Opacity = 0.9f, Offset = new Point(0, 0) };

	public static void Register() =>
		SailfishLabelHandler.Mapper.AppendToMapping("Glow", (_, label) =>
		{
			// Only the labels that ask for a glow: the key runs for every Label in the app, and clearing the Shadow of
			// the others took the effect's glow away too.
			if (label is not Label view || !view.IsSet(IsOnProperty))
				return;
			MapperRuns++;
			var on = GetIsOn(view);
			if (on && view.Shadow is null)
				view.Shadow = Make();
			else if (!on && view.Shadow is not null)
				view.Shadow = null!;
		});
}
