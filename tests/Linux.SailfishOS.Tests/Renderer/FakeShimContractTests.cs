using Microsoft.Maui.Controls;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>The fake shim's own contract: it must notice what it does not model, or renderer tests pass on silence.</summary>
[Collection("renderer")]
public class FakeShimContractTests
{
	[Fact]
	public void An_unmodelled_eval_is_recorded_and_fails_the_harness()
	{
		var h = new RendererHarness(new ContentPage { Content = new Label { Text = "x" } });
		QtHostRuntime.Eval("window.somethingTheFakeDoesNotKnow(1)");
		Assert.Contains(h.Shim.UnhandledEvals, e => e.Contains("somethingTheFakeDoesNotKnow", StringComparison.Ordinal));
		Assert.Throws<Xunit.Sdk.XunitException>(h.Dispose);
	}

	[Fact]
	public void Page_entry_points_are_recorded_with_their_page()
	{
		using var h = new RendererHarness(new ContentPage { Content = new Label { Text = "x" } });
		QtHostRuntime.Eval(QmlPage.Call(QmlPage.ByIdOr("mp1", QmlPage.Model), "setMauiTabs", "\"{}\""));
		QtHostRuntime.Eval($"{QmlPage.Model}.setMauiScroll(\"{{}}\")");
		Assert.Contains(new FakeShim.PageCall("mp1", "setMauiTabs", "\"{}\""), h.Shim.PageCalls);
		Assert.Contains(h.Shim.PageCalls, c => c.Method == "setMauiScroll");
		Assert.Empty(h.Shim.UnhandledEvals);
	}
}
