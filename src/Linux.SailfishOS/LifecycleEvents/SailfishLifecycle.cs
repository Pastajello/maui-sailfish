using Microsoft.Maui.Graphics;
using Microsoft.Maui.SailfishOS.Platform;

namespace Microsoft.Maui.LifecycleEvents;

/// <summary>
/// Native Sailfish OS / Qt application events, the counterpart of iOSLifecycle (AppDelegate) and AndroidLifecycle
/// (MainActivity). MAUI's own Window.Activated/Resumed/Stopped still fire; these carry the platform's view.
/// Each has a matching virtual on <see cref="SailfishMauiApplication"/> (Platforms/SailfishOS/SailfishApplication.cs).
/// </summary>
public static class SailfishLifecycle
{
	/// <summary>The app and its window exist and the Qt loop starts next; <paramref name="arguments"/> are the
	/// process arguments (FinishedLaunching / OnCreate).</summary>
	public delegate void OnLaunched(SailfishMauiApplication application, string[] arguments);

	/// <summary>Qt's application state changed (QGuiApplication::applicationStateChanged).</summary>
	public delegate void OnApplicationStateChanged(SailfishMauiApplication application, SailfishApplicationState state);

	/// <summary>The Silica window turned (OnConfigurationChanged / viewWillTransitionToSize).</summary>
	public delegate void OnOrientationChanged(SailfishMauiApplication application, SailfishOrientation orientation);

	/// <summary>The app's cover on the home screen changed state (Silica CoverBackground.status).</summary>
	public delegate void OnCoverStatusChanged(SailfishMauiApplication application, SailfishCoverStatus status);

	/// <summary>A cover action (SailfishCover.SetActions) was tapped on the home screen.</summary>
	public delegate void OnCoverActionTriggered(SailfishMauiApplication application, int index);

	/// <summary>The ambience switched between light and dark (Silica Theme.colorScheme).</summary>
	public delegate void OnColorSchemeChanged(SailfishMauiApplication application, SailfishColorScheme scheme);

	/// <summary>The virtual keyboard opened, closed or resized; <paramref name="keyboard"/> is in window pixels.</summary>
	public delegate void OnInputMethodChanged(SailfishMauiApplication application, bool visible, Rect keyboard);

	/// <summary>The display turned off, dimmed or on (MCE).</summary>
	public delegate void OnDisplayStateChanged(SailfishMauiApplication application, SailfishDisplayState state);

	/// <summary>The lock screen was shown or dismissed (MCE touch-screen lock).</summary>
	public delegate void OnScreenLockChanged(SailfishMauiApplication application, bool locked);

	/// <summary>MCE memory pressure changed; free caches on Warning and Critical (OnTrimMemory / DidReceiveMemoryWarning).</summary>
	public delegate void OnMemoryLevelChanged(SailfishMauiApplication application, SailfishMemoryLevel level);

	/// <summary>The app is about to quit (QGuiApplication::aboutToQuit: window closed from the home screen, or
	/// Application.Quit); the last point to save state (WillTerminate / OnDestroy).</summary>
	public delegate void OnQuitting(SailfishMauiApplication application);
}

/// <summary>Registers <see cref="SailfishLifecycle"/> handlers inside ConfigureLifecycleEvents.</summary>
public interface ISailfishLifecycleBuilder : ILifecycleBuilder
{
}

public static class SailfishLifecycleExtensions
{
	/// <summary>Sailfish OS handlers, like AddiOS/AddAndroid: <c>events.AddSailfish(sf => sf.OnQuitting(app => …))</c>.</summary>
	public static ILifecycleBuilder AddSailfish(this ILifecycleBuilder builder, Action<ISailfishLifecycleBuilder> configure)
	{
		configure?.Invoke(new Builder(builder));
		return builder;
	}

	public static ISailfishLifecycleBuilder OnLaunched(this ISailfishLifecycleBuilder builder, SailfishLifecycle.OnLaunched handler) =>
		builder.On(handler);

	public static ISailfishLifecycleBuilder OnApplicationStateChanged(this ISailfishLifecycleBuilder builder, SailfishLifecycle.OnApplicationStateChanged handler) =>
		builder.On(handler);

	public static ISailfishLifecycleBuilder OnOrientationChanged(this ISailfishLifecycleBuilder builder, SailfishLifecycle.OnOrientationChanged handler) =>
		builder.On(handler);

	public static ISailfishLifecycleBuilder OnCoverStatusChanged(this ISailfishLifecycleBuilder builder, SailfishLifecycle.OnCoverStatusChanged handler) =>
		builder.On(handler);

	public static ISailfishLifecycleBuilder OnCoverActionTriggered(this ISailfishLifecycleBuilder builder, SailfishLifecycle.OnCoverActionTriggered handler) =>
		builder.On(handler);

	public static ISailfishLifecycleBuilder OnColorSchemeChanged(this ISailfishLifecycleBuilder builder, SailfishLifecycle.OnColorSchemeChanged handler) =>
		builder.On(handler);

	public static ISailfishLifecycleBuilder OnInputMethodChanged(this ISailfishLifecycleBuilder builder, SailfishLifecycle.OnInputMethodChanged handler) =>
		builder.On(handler);

	public static ISailfishLifecycleBuilder OnDisplayStateChanged(this ISailfishLifecycleBuilder builder, SailfishLifecycle.OnDisplayStateChanged handler) =>
		builder.On(handler);

	public static ISailfishLifecycleBuilder OnScreenLockChanged(this ISailfishLifecycleBuilder builder, SailfishLifecycle.OnScreenLockChanged handler) =>
		builder.On(handler);

	public static ISailfishLifecycleBuilder OnMemoryLevelChanged(this ISailfishLifecycleBuilder builder, SailfishLifecycle.OnMemoryLevelChanged handler) =>
		builder.On(handler);

	public static ISailfishLifecycleBuilder OnQuitting(this ISailfishLifecycleBuilder builder, SailfishLifecycle.OnQuitting handler) =>
		builder.On(handler);

	// The event name is the delegate's name, which is also what SailfishMauiApplication invokes.
	private static ISailfishLifecycleBuilder On<TDelegate>(this ISailfishLifecycleBuilder builder, TDelegate handler)
		where TDelegate : Delegate
	{
		builder.AddEvent(typeof(TDelegate).Name, handler);
		return builder;
	}

	private sealed class Builder(ILifecycleBuilder inner) : ISailfishLifecycleBuilder
	{
		public void AddEvent<TDelegate>(string eventName, TDelegate action) where TDelegate : Delegate =>
			inner.AddEvent(eventName, action);
	}
}
