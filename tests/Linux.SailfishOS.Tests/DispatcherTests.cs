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
				SailfishDispatcherProvider.BindLoopThread();
				var inCallback = ResolveInOwnContext(provider).GetAwaiter().GetResult();
				Assert.NotNull(inCallback);
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

	// Profitocracy: its AppShell was built on a pool thread (after an await) and kept that thread's dispatcher, a queue
	// nothing drained, so Shell's own DispatchAsync never ran and every back gesture's pop hung.
	[Fact]
	public void Only_a_loop_thread_has_a_dispatcher()
	{
		var provider = new SailfishDispatcherProvider();
		IDispatcher? pool = new SailfishDispatcher(), loop = null;
		var thread = new Thread(() =>
		{
			pool = provider.GetForCurrentThread();
			loop = SailfishDispatcherProvider.BindLoopThread();
		});
		thread.Start();
		thread.Join();
		Assert.Null(pool);
		Assert.NotNull(loop);
	}

	[Fact]
	public void An_element_built_off_the_loop_thread_dispatches_to_the_loop()
	{
		var loop = SailfishDispatcherProvider.BindLoopThread();
		DispatcherProvider.SetCurrent(new SailfishDispatcherProvider());
		try
		{
			var app = new Microsoft.Maui.Controls.Application();
			Microsoft.Maui.Controls.Application.Current = app;
			Microsoft.Maui.Controls.ContentPage? page = null;
			var thread = new Thread(() => page = new Microsoft.Maui.Controls.ContentPage());
			thread.Start();
			thread.Join();

			Assert.Same(loop, page!.Dispatcher);
		}
		finally
		{
			Microsoft.Maui.Controls.Application.Current = null;
			DispatcherProvider.SetCurrent(null);
		}
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
		SailfishDispatcherProvider.BindLoopThread();
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

	private sealed class ThemeService(IDispatcher dispatcher)
	{
		public IDispatcher? Dispatcher { get; } = dispatcher;
	}

	// GitTrends: singletons built by the app's own container (plain UseMauiApp) took IDispatcher in their constructor
	// and got null, since MAUI's root registration reads DispatcherProvider.Current; ThemeService then threw on start.
	[Fact]
	public void A_plain_maui_app_container_injects_the_sailfish_dispatcher()
	{
		SailfishMauiApplication.InstallDispatcherProvider();
		SailfishDispatcherProvider.BindLoopThread();   // as Run does on the Qt thread
		try
		{
			var builder = Microsoft.Maui.Hosting.MauiApp.CreateBuilder();
			builder.UseMauiApp<TestApp>();
			builder.Services.AddSingleton<ThemeService>();
			using var app = builder.Build();

			Assert.IsType<SailfishDispatcher>(app.Services.GetRequiredService<ThemeService>().Dispatcher);
		}
		finally
		{
			DispatcherProvider.SetCurrent(null);
		}
	}
}

/// <summary>EmployeeDirectory: MainThread.InvokeOnMainThreadAsync threw NotImplementedInReferenceAssemblyException from
/// the plain-net Essentials, and the async startup left the loading page up for good.</summary>
[Collection("renderer")]
public class MainThreadTests
{
	[Fact]
	public async Task MainThread_runs_on_the_installing_thread_through_its_dispatcher()
	{
		var queued = new List<Action>();
		Assert.True(SailfishMainThread.Install(queued.Add));
		try
		{
			Assert.True(Microsoft.Maui.ApplicationModel.MainThread.IsMainThread);
			var otherThreadSaysMain = await Task.Run(() => Microsoft.Maui.ApplicationModel.MainThread.IsMainThread);
			Assert.False(otherThreadSaysMain);

			var ran = false;
			await Task.Run(() => Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() => ran = true));
			Assert.Single(queued);
			queued[0]();
			Assert.True(ran);
		}
		finally
		{
			SailfishMainThread.Clear();
		}
	}
}
