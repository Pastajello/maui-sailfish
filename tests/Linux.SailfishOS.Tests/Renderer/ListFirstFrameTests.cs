using Microsoft.Maui.Controls;
using Microsoft.Maui.SailfishOS.Platform.QtHost;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>A page with a list taller than its viewport opens in two turns: the page first, the rows after its first
/// frame (QtHostListAdapter.FirstFrame.cs).</summary>
[Collection("renderer")]
public sealed class ListFirstFrameTests
{
	private static ContentPage Page(int items) => new()
	{
		Title = "T",
		Content = new CollectionView
		{
			ItemsSource = Enumerable.Range(0, items).Select(i => $"item {i}").ToList(),
			ItemTemplate = new DataTemplate(() =>
			{
				var label = new Label();
				label.SetBinding(Label.TextProperty, ".");
				return label;
			}),
			HeightRequest = 600,
		},
	};

	private static string Rows(RendererHarness h) => h.Shim.ByUri("list-view").Single().Text("mauiRowsJson") ?? string.Empty;

	[Fact]
	public void A_long_list_builds_its_rows_after_the_pages_first_frame()
	{
		var held = QtHostListAdapter.FirstBuildsHeld;
		using var h = new RendererHarness(Page(100), firstBuildWaitsFrame: true);
		for (var i = 0; i < 3; i++)
			h.Poll();
		Assert.Equal(held + 1, QtHostListAdapter.FirstBuildsHeld);
		Assert.Equal(string.Empty, Rows(h));

		QtHostSurface.RunFrame();
		h.Poll();
		Assert.StartsWith("[{", Rows(h));
	}

	// Kitchen's home is one CollectionView whose Header holds the page: held with the rows, the app opened on an empty
	// page for the wait. The header paints with the page; only the rows wait.
	[Fact]
	public void A_long_lists_header_paints_with_the_page()
	{
		var page = Page(100);
		((CollectionView)page.Content).Header = new Label { Text = "Sailfish Kitchen header" };
		using var h = new RendererHarness(page, firstBuildWaitsFrame: true);
		h.Shim.AddNative($"maui_{h.Shim.ByUri("list-view").Single().Id}__header");   // the ListView's header placeholder
		for (var i = 0; i < 3; i++)
			h.Renderer.KickedPoll();   // list work runs on kicked polls
		Assert.Equal(string.Empty, Rows(h));
		Assert.Contains(h.Shim.ByUri("label"), l => l.Text("text") == "Sailfish Kitchen header" && !l.Destroyed);

		QtHostSurface.RunFrame();
		h.Renderer.KickedPoll();   // list work runs on kicked polls
		Assert.StartsWith("[{", Rows(h));
		Assert.Single(h.Shim.ByUri("label"), l => l.Text("text") == "Sailfish Kitchen header" && !l.Destroyed);
	}

	[Fact]
	public void A_list_that_fits_builds_its_rows_with_the_page()
	{
		var held = QtHostListAdapter.FirstBuildsHeld;
		using var h = new RendererHarness(Page(3), firstBuildWaitsFrame: true);
		h.Poll();
		Assert.Equal(held, QtHostListAdapter.FirstBuildsHeld);
		Assert.StartsWith("[{", Rows(h));
	}
}
