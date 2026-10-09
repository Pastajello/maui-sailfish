using Microsoft.Maui.Controls;
using Microsoft.Maui.SailfishOS.Platform;
using Linux.SailfishOS.Tests.Renderer;
using Xunit;

namespace Linux.SailfishOS.Tests;

/// <summary>Tracker S59 (D11): an exception from app code on the UI thread ends the app unless a
/// SailfishExceptions.Unhandled handler marks it handled.</summary>
[Collection("renderer")]
public sealed class UnhandledExceptionTests
{
	// The recorder is process-wide and other test classes run in parallel: only this class's exceptions ("s59 …") count.
	private static List<(Exception Exception, string Source)> CrashesDuring(Action action)
	{
		var before = TestCrashes.Seen.Count;
		action();
		return TestCrashes.Seen.Skip(before).Where(c => c.Exception.Message.StartsWith("s59 ", StringComparison.Ordinal)).ToList();
	}

	[Fact]
	public void Dispatched_work_that_throws_ends_the_app()
	{
		var loop = SailfishDispatcherProvider.BindLoopThread();
		var crashes = CrashesDuring(() =>
		{
			loop.Dispatch(() => throw new InvalidOperationException("s59 app bug"));
			loop.DrainQueue();
		});
		var crash = Assert.Single(crashes);
		Assert.Equal("dispatched work", crash.Source);
		Assert.Equal("s59 app bug", crash.Exception.Message);
	}

	[Fact]
	public void A_handler_that_handles_it_keeps_the_app_running()
	{
		var loop = SailfishDispatcherProvider.BindLoopThread();
		var seen = new List<string>();
		void Handle(object? sender, SailfishUnhandledExceptionEventArgs e)
		{
			seen.Add($"{e.Source}: {e.Exception.Message}");
			e.Handled = true;
		}
		SailfishExceptions.Unhandled += Handle;
		try
		{
			var ranAfter = false;
			var crashes = CrashesDuring(() =>
			{
				loop.Dispatch(() => throw new InvalidOperationException("s59 app bug"));
				loop.Dispatch(() => ranAfter = true);
				loop.DrainQueue();
			});
			Assert.Empty(crashes);
			Assert.True(ranAfter);   // the loop went on
			Assert.Equal(new[] { "dispatched work: s59 app bug" }, seen);
		}
		finally
		{
			SailfishExceptions.Unhandled -= Handle;
		}
	}

	[Fact]
	public void A_handler_that_throws_itself_does_not_keep_the_app()
	{
		var loop = SailfishDispatcherProvider.BindLoopThread();
		void Broken(object? sender, SailfishUnhandledExceptionEventArgs e)
		{
			e.Handled = true;
			throw new InvalidOperationException("s59 the handler is broken too");
		}
		SailfishExceptions.Unhandled += Broken;
		try
		{
			var crashes = CrashesDuring(() =>
			{
				loop.Dispatch(() => throw new InvalidOperationException("s59 app bug"));
				loop.DrainQueue();
			});
			Assert.Equal("s59 app bug", Assert.Single(crashes).Exception.Message);
		}
		finally
		{
			SailfishExceptions.Unhandled -= Broken;
		}
	}

	[Fact]
	public void A_clicked_handler_that_throws_ends_the_app()
	{
		var button = new Button { Text = "Go" };
		button.Clicked += (_, _) => throw new InvalidOperationException("s59 clicked bug");
		using var h = new RendererHarness(new ContentPage { Title = "T", Content = new VerticalStackLayout { Children = { button } } });
		for (var i = 0; i < 4; i++)
			h.Poll();
		var host = h.Renderer.CurrentHosts.First(x => ReferenceEquals(x.Element, button));

		var crashes = CrashesDuring(() => h.Renderer.HandleNativeEvent("tap", $"{{\"id\":\"{host.Id}\"}}"));

		var crash = Assert.Single(crashes);
		Assert.Equal("event 'tap'", crash.Source);
		Assert.Equal("s59 clicked bug", crash.Exception.Message);
	}
}
