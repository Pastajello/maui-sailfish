using Microsoft.Maui.Controls;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>
/// Tracker S44 (plan M12 step 2): Silica shrinks the page by the keyboard. A field inside a ScrollView gets a shorter
/// page (adjustResize: the reported height drops and the page lays out again); a field in plain content keeps the full
/// height (MauiModelPage reports page.height + the keyboard inset while it pans), so nothing moves under it.
/// </summary>
[Collection("renderer")]
public sealed class KeyboardInsetTests
{
	private static void Geometry(RendererHarness h, double pageHeightPx)
	{
		h.Renderer.HandleNativeEvent("window-geometry",
			$"{{\"pageWidth\":1080,\"pageHeight\":{pageHeightPx},\"headerHeight\":110,\"titleHeight\":110,\"statusHeight\":40}}");
		for (var i = 0; i < 6; i++)
			h.Poll();
	}

	private static (RendererHarness H, ContentPage Page, Entry Bottom) Show()
	{
		var bottom = new Entry { Placeholder = "last", VerticalOptions = Microsoft.Maui.Controls.LayoutOptions.End };
		var page = new ContentPage { Title = "Form", Content = new Grid { Children = { bottom } } };
		var h = new RendererHarness(new NavigationPage(page));
		Geometry(h, 2160);
		return (h, page, bottom);
	}

	[Fact]
	public void A_shorter_page_lays_out_again_and_the_bottom_field_moves_up()
	{
		var (h, page, bottom) = Show();
		using var _ = h;
		var fullHeight = page.Height;
		var fullBottom = bottom.Y + bottom.Height;

		Geometry(h, 2160 - 836);   // the keyboard panel taken off (resize: the field sits in a scrolling container)

		Assert.True(page.Height < fullHeight - 1, $"page {page.Height} after the keyboard, {fullHeight} before");
		Assert.True(bottom.Y + bottom.Height < fullBottom - 1, $"field bottom {bottom.Y + bottom.Height}, before {fullBottom}");
	}

	[Fact]
	public void The_full_height_while_panning_leaves_the_layout_alone()
	{
		var (h, page, bottom) = Show();
		using var _ = h;
		var (height, y) = (page.Height, bottom.Y);

		Geometry(h, 2160);   // page.height − panel + the inset: what MauiModelPage reports while it pans

		Assert.Equal(height, page.Height, 3);
		Assert.Equal(y, bottom.Y, 3);
	}

	[Fact]
	public void The_page_reports_the_inset_while_it_pans_and_keeps_silicas_screen()
	{
		var qml = File.ReadAllText(Path.Combine(Repo.Root, "src/Linux.SailfishOS/Platform/QtHost/qml/MauiModelPage.qml"));

		Assert.Contains("pageHeight: page.height + (__keyboardPan ? __keyboardInset : 0)", qml);
		// The pan targets the focused field's own adapter, not the item on the canvas (a layout that can hold the whole
		// form: the F3 leg's Entry went off screen under the keyboard).
		Assert.Contains("while (item && item !== canvas && item.mauiId === undefined)", qml);
		// Unqualified, QtQuick.Window's Screen shadows Silica's (Screen.topCutout broke on the phone).
		Assert.Contains("import QtQuick.Window 2.2 as QtWindow", qml);
		Assert.DoesNotMatch(@"(?m)^import QtQuick\.Window [0-9.]+\s*$", qml);
		// Qt 5.6 has no Qt.callLater.
		Assert.DoesNotContain("Qt.callLater(", qml.Replace("Qt.callLater needs", "").Replace("no Qt.callLater", "").Replace("Qt.callLater from", ""));
	}
}
