using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.SailfishOS.Platform;
using Xunit;

namespace Linux.SailfishOS.Tests;

/// <summary>A dispatcher belongs to its thread: code called back from the native Qt loop (a fresh execution context
/// on the loop thread) must reach the loop's dispatcher, not a new one no loop drains.</summary>
public class DispatcherTests
{
	// An async method's AsyncLocal writes do not flow back to its caller, as a native callback's context does not.
	private static async Task<IDispatcher?> ResolveInOwnContext(IDispatcherProvider provider)
	{
		await Task.CompletedTask;
		return provider.GetForCurrentThread();
	}

	[Fact]
	public void The_same_thread_gets_the_same_dispatcher_in_any_execution_context()
	{
		var provider = new SailfishDispatcherProvider();
		Exception? failure = null;
		var thread = new Thread(() =>
		{
			try
			{
				var inCallback = ResolveInOwnContext(provider).GetAwaiter().GetResult();
				Assert.Same(inCallback, provider.GetForCurrentThread());
			}
			catch (Exception ex)
			{
				failure = ex;
			}
		});
		thread.Start();
		thread.Join();
		Assert.Null(failure);
	}

	[Fact]
	public void Other_threads_get_their_own_dispatcher()
	{
		var provider = new SailfishDispatcherProvider();
		IDispatcher? other = null;
		var thread = new Thread(() => other = provider.GetForCurrentThread());
		thread.Start();
		thread.Join();
		Assert.NotNull(other);
		Assert.NotSame(other, provider.GetForCurrentThread());
	}
}

/// <summary>Under plain UseMauiApp the registered IDispatcherProvider is MAUI's own; resolving IDispatcher through
/// MAUI's factory re-installs it as DispatcherProvider.Current, which left the Qt thread without a dispatcher (WeatherTwentyOne:
/// every renderer kick dropped, a forecast list filling in item by item on the heartbeat poll).</summary>
[Collection("renderer")]
public class DispatcherProviderOverlayTests
{
	private sealed class TestApp : Microsoft.Maui.Controls.Application
	{
	}

	[Fact]
	public void Resolving_a_dispatcher_keeps_the_sailfish_provider_current()
	{
		var builder = Microsoft.Maui.Hosting.MauiApp.CreateBuilder();
		builder.UseMauiApp<TestApp>();
		using var app = builder.Build();
		var overlay = new SailfishServiceOverlay(app.Services);
		try
		{
			DispatcherProvider.SetCurrent((IDispatcherProvider)overlay.GetService(typeof(IDispatcherProvider))!);

			Assert.IsType<SailfishDispatcher>(overlay.GetService(typeof(IDispatcher)));
			Assert.IsType<SailfishDispatcherProvider>(DispatcherProvider.Current);
			Assert.IsType<SailfishDispatcher>(Dispatcher.GetForCurrentThread());
		}
		finally
		{
			DispatcherProvider.SetCurrent(null);
		}
	}
}
