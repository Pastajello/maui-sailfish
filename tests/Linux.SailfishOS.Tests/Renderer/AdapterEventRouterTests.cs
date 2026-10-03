using Microsoft.Maui.Controls;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>Adapter events decoded by AdapterEventRouter and written back into MAUI under push suppression.</summary>
[Collection("renderer")]
public class AdapterEventRouterTests
{
	[Fact]
	public void A_malformed_cursor_report_is_dropped_and_the_cursor_stays()
	{
		var entry = new Entry { Text = "hello", CursorPosition = 2 };
		using var h = new RendererHarness(new ContentPage { Content = entry });
		var host = h.Shim.ByUri("entry").Single();

		h.Renderer.HandleNativeEvent("cursor-changed", $"{{\"id\":\"{host.Id}\",\"cursor\":-1,\"selStart\":0,\"selEnd\":0}}");
		Assert.Equal(2, entry.CursorPosition);
		Assert.Equal(0, h.Renderer.CursorWriteBacks);

		h.Renderer.HandleNativeEvent("cursor-changed", $"{{\"id\":\"{host.Id}\",\"cursor\":4,\"selStart\":4,\"selEnd\":4}}");
		Assert.Equal(4, entry.CursorPosition);
		Assert.Equal(1, h.Renderer.CursorWriteBacks);
	}

	[Fact]
	public void A_native_picker_open_reaches_IsOpen_without_echoing_back()
	{
		var picker = new Picker { ItemsSource = new List<string> { "a", "b" } };
		using var h = new RendererHarness(new ContentPage { Content = picker });
		var host = h.Shim.ByUri("picker").Single();
		var batches = h.Shim.PropertyBatches;
		var evals = h.Shim.Evals.Count;

		h.Renderer.HandleNativeEvent("picker-open", $"{{\"id\":\"{host.Id}\",\"open\":true}}");
		h.Poll();

		Assert.True(picker.IsOpen);
		// The applied state already says open, so the IsOpen change pushes nothing back to the adapter.
		Assert.DoesNotContain(h.Shim.Evals.Skip(evals), e => e.Contains("mauiOpen", StringComparison.Ordinal));
		Assert.Equal(batches, h.Shim.PropertyBatches);
	}
}
