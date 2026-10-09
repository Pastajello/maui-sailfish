using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.SailfishOS.Platform;
using Xunit;

namespace Linux.SailfishOS.Tests.Renderer;

/// <summary>Tracker S46 (plan M13): the ambience's light/dark is known before the Qt side runs (a dconf read seeds it),
/// its colours are readable and follow ambience changes, and the layout direction comes from Qt's locale.</summary>
[Collection("renderer")]
public sealed class ThemeSeedTests
{
	[Theory]
	[InlineData("0\n", AppTheme.Dark)]
	[InlineData("1", AppTheme.Light)]
	[InlineData("", AppTheme.Dark)]        // unset: Silica's default, light text on dark
	[InlineData("'x'", AppTheme.Unspecified)]
	public void The_dconf_colour_scheme_maps_to_the_maui_theme(string dconf, AppTheme expected) =>
		Assert.Equal(expected, SailfishTheme.ParseColorScheme(dconf));

	[Fact]
	public void Code_that_reads_the_theme_before_the_host_runs_gets_the_ambiences()
	{
		using var statics = new TestStatics();
		SailfishTheme.SeedForTests(AppTheme.Light);

		Assert.Equal(AppTheme.Light, SailfishTheme.Current);
		Assert.Equal(AppTheme.Light, new SailfishAppInfo().RequestedTheme);
	}

	[Fact]
	public void The_ambience_colours_are_readable_and_a_change_is_reported_once()
	{
		using var statics = new TestStatics();
		var changes = 0;
		void OnChange() => changes++;
		SailfishTheme.ColorsChanged += OnChange;
		try
		{
			SailfishTheme.ApplyPalette("#ffbffe7f|#ffffffff|#ffbababa|#ff88cb66");
			SailfishTheme.ApplyPalette("#ffbffe7f|#ffffffff|#ffbababa|#ff88cb66");   // same ambience again

			Assert.Equal(Color.FromArgb("#ffbffe7f"), SailfishTheme.HighlightColor);
			Assert.Equal(Colors.White, SailfishTheme.PrimaryColor);
			Assert.Equal(Color.FromArgb("#ffbababa"), SailfishTheme.SecondaryColor);
			Assert.Equal(Color.FromArgb("#ff88cb66"), SailfishTheme.SecondaryHighlightColor);
			Assert.Equal(1, changes);

			SailfishTheme.ApplyPalette("#ff00aaff|#ffffffff|#ffbababa|#ff88cb66");   // another ambience, still dark
			Assert.Equal(2, changes);
		}
		finally
		{
			SailfishTheme.ColorsChanged -= OnChange;
		}
	}

	[Fact]
	public void A_right_to_left_locale_reaches_requested_layout_direction()
	{
		using var h = new RendererHarness(new ContentPage { Title = "T", Content = new Label { Text = "x" } });
		h.Shim.EvalHook = e => e == "Qt.application.layoutDirection" ? "1" : null;

		SailfishLayoutDirection.OnHostReady();

		Assert.Equal(Microsoft.Maui.ApplicationModel.LayoutDirection.RightToLeft, new SailfishAppInfo().RequestedLayoutDirection);
		Assert.Null(SailfishLayoutDirection.Parse(""));
	}
}
