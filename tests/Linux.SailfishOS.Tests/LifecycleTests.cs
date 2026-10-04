using Microsoft.Maui.Graphics;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.Maui.SailfishOS.Platform;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;

namespace Linux.SailfishOS.Tests;

/// <summary>Native Sailfish events reach both the SailfishApplication overrides (Platforms/SailfishOS) and the
/// ConfigureLifecycleEvents(AddSailfish) handlers, override first, with the platform's values.</summary>
[Collection("renderer")]   // QtHostServices subscriptions are process-wide
public class LifecycleTests
{
	private sealed class TestApplication : SailfishMauiApplication
	{
		public readonly List<string> Calls = new();

		public TestApplication(IServiceProvider services) => Services = services;

		protected override MauiApp CreateMauiApp() => throw new NotSupportedException();
		protected override void OnApplicationStateChanged(SailfishApplicationState state) => Calls.Add($"override:state:{state}");
		protected override void OnInputMethodChanged(bool visible, Rect keyboard) => Calls.Add($"override:vkb:{visible}:{keyboard.Height}");
		protected override void OnQuitting() => Calls.Add("override:quit");
		protected override void OnDisplayStateChanged(SailfishDisplayState state) => Calls.Add($"override:display:{state}");
		protected override void OnScreenLockChanged(bool locked) => Calls.Add($"override:lock:{locked}");
		protected override void OnMemoryLevelChanged(SailfishMemoryLevel level) => Calls.Add($"override:memory:{level}");
		protected override void OnColorSchemeChanged(SailfishColorScheme scheme) =>
			Calls.Add($"override:scheme:{scheme}:theme={SailfishTheme.Current}");
	}

	[Fact]
	public void Native_events_reach_the_override_then_the_handlers()
	{
		var log = new List<string>();
		var builder = MauiApp.CreateBuilder(useDefaults: false);
		builder.ConfigureLifecycleEvents(events => events.AddSailfish(sf => sf
			.OnApplicationStateChanged((_, state) => log.Add($"event:state:{state}"))
			.OnInputMethodChanged((_, visible, keyboard) => log.Add($"event:vkb:{visible}:{keyboard.Height}"))
			.OnQuitting(_ => log.Add("event:quit"))));
		var app = new TestApplication(builder.Build().Services);
		app.SubscribeNativeEvents();

		QtHostServices.Dispatch("svc-app-state", "{\"state\":2}");
		QtHostServices.Dispatch("svc-input-method", "{\"visible\":true,\"x\":0,\"y\":1400,\"width\":1080,\"height\":760}");
		app.RaiseQuitting();

		Assert.Equal(new[] { "override:state:Inactive", "override:vkb:True:760", "override:quit" }, app.Calls);
		Assert.Equal(new[] { "event:state:Inactive", "event:vkb:True:760", "event:quit" }, log);
	}
	// MauiShell.qml reports both Qt.application signals (state and active) with the same payload, so one transition
	// arrives twice: the override and the handlers see it once.
	[Fact]
	public void An_application_state_transition_is_raised_once()
	{
		var app = new TestApplication(MauiApp.CreateBuilder(useDefaults: false).Build().Services);
		app.SubscribeNativeEvents();

		QtHostServices.Dispatch(ShellEvents.AppState, "{\"state\":2,\"active\":false}");
		QtHostServices.Dispatch(ShellEvents.AppState, "{\"state\":2,\"active\":false}");
		QtHostServices.Dispatch(ShellEvents.AppState, "{\"state\":4,\"active\":true}");
		QtHostServices.Dispatch(ShellEvents.AppState, "{\"state\":4,\"active\":true}");

		Assert.Equal(new[] { "override:state:Inactive", "override:state:Active" }, app.Calls);
	}

	// The MCE service (SailfishSystemService) keeps the last display/lock/memory state and raises each change once;
	// MCE re-reports on valid/changed, so repeats are common.
	[Fact]
	public void Mce_states_are_kept_and_each_change_is_raised_once()
	{
		var app = new TestApplication(MauiApp.CreateBuilder(useDefaults: false).Build().Services);
		app.SubscribeNativeEvents();
		Assert.Null(app.DisplayState);
		Assert.False(app.MemoryLevelAnswered);

		QtHostServices.Dispatch(ShellEvents.Display, "{\"state\":0}");
		QtHostServices.Dispatch(ShellEvents.Display, "{\"state\":0}");
		QtHostServices.Dispatch(ShellEvents.ScreenLock, "{\"locked\":true}");
		QtHostServices.Dispatch(ShellEvents.MemoryLevel, "{\"level\":\"unknown\"}");
		QtHostServices.Dispatch(ShellEvents.MemoryLevel, "{\"level\":\"warning\"}");
		QtHostServices.Dispatch(ShellEvents.MemoryLevel, "{\"level\":\"warning\"}");

		Assert.Equal(SailfishDisplayState.Off, app.DisplayState);
		Assert.True(app.ScreenLocked);
		Assert.Equal(SailfishMemoryLevel.Warning, app.MemoryLevel);
		Assert.True(app.MemoryLevelAnswered);
		Assert.Equal(new[] { "override:display:Off", "override:lock:True", "override:memory:Warning" }, app.Calls);
	}

	// W1.9: the application subscribed to the raw ambience event before the theme service (which subscribes on the first
	// Qt tick), so OnColorSchemeChanged ran while AppInfo.RequestedTheme still had the old value.
	[Fact]
	public void The_color_scheme_override_sees_the_new_theme()
	{
		var previous = SailfishTheme.Current;
		var app = new TestApplication(MauiApp.CreateBuilder(useDefaults: false).Build().Services);
		app.SubscribeNativeEvents();
		try
		{
			SailfishTheme.Apply(previous == Microsoft.Maui.ApplicationModel.AppTheme.Light
				? Microsoft.Maui.ApplicationModel.AppTheme.Dark : Microsoft.Maui.ApplicationModel.AppTheme.Light);
			var expected = SailfishTheme.Current == Microsoft.Maui.ApplicationModel.AppTheme.Light
				? $"override:scheme:{SailfishColorScheme.DarkOnLight}:theme=Light"
				: $"override:scheme:{SailfishColorScheme.LightOnDark}:theme=Dark";
			Assert.Equal(new[] { expected }, app.Calls);
		}
		finally
		{
			SailfishTheme.Apply(previous);
		}
	}
}
