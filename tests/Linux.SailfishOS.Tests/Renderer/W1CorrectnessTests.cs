using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>Correctness defects from the second architecture review (docs/architecture-handoff.md W1), one test each.</summary>
[Collection("renderer")]
public sealed class W1CorrectnessTests
{
	// W1.1: content reconciled before its first arrange retries once in the same pass; the retry was a nested
	// Reconcile(), so its time and count went in twice (the outer stopwatch covered it already).
	[Fact]
	public void A_reconcile_with_an_unarranged_shape_retry_counts_once()
	{
		var nav = new NavigationPage(new ContentPage { Title = "Home", Content = new Label { Text = "home" } });
		using var h = new RendererHarness(nav);
		for (var i = 0; i < 4; i++)
			h.Poll();
		var count = h.Renderer.ReconcileCount;
		var retries = h.Renderer.UnarrangedRetries;

		// A shape sized only by its arrange: the push's walk skips it, the retry after the layout pass creates it.
		var ellipse = new Ellipse { Fill = Colors.Red, HeightRequest = 40 };
		_ = nav.PushAsync(new ContentPage { Title = "Shape", Content = new VerticalStackLayout { Children = { ellipse } } });
		h.Poll();

		Assert.Equal(retries + 1, h.Renderer.UnarrangedRetries);
		Assert.Contains(h.Renderer.CurrentHosts, x => ReferenceEquals(x.Element, ellipse));
		Assert.Equal(count + 1, h.Renderer.ReconcileCount);
	}

	// W1.2: a native pop while a dialog is open waits in SyncNativeNavigation; the reconcile must not render MAUI's
	// still-pushed page onto the revealed native page meanwhile. (The other order, a dialog opened while MAUI follows
	// a pop, needs MAUI's pop to stay in flight across polls, which it does on the device but not in this harness;
	// QtHostPageRenderer.MauiFollowPending covers it.)
	[Fact]
	public void A_native_pop_under_an_open_dialog_waits_for_MAUI_to_follow()
	{
		var nav = new NavigationPage(new ContentPage { Title = "Home", Content = new Label { Text = "home" } });
		using var h = new RendererHarness(nav);
		var detailLabel = new Label { Text = "detail" };
		_ = nav.PushAsync(new ContentPage { Title = "Detail", Content = detailLabel });
		for (var i = 0; i < 6; i++)
			h.Poll();
		var detail = nav.CurrentPage;
		_ = h.Renderer.PushAlertAsync("Q", "open while the stack pops", null, "OK");
		Assert.Equal(1, h.Shim.DialogsOpened);

		h.Shim.Pages.RemoveAt(h.Shim.Pages.Count - 1);   // the native stack pops under the dialog
		var reconciles = h.Renderer.ReconcileCount;
		detailLabel.Text = "detail changed";              // MAUI still shows Detail: a change asks for a pass
		for (var i = 0; i < 3; i++)
			h.Renderer.KickedPoll();
		Assert.Equal(reconciles, h.Renderer.ReconcileCount);
		Assert.Same(detail, nav.CurrentPage);
	}
}
