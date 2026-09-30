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
}
